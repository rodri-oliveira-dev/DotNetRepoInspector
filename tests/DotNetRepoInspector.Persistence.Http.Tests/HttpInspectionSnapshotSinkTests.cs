using System.Net;
using System.Text.Json;

using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Persistence;

using Xunit;

namespace DotNetRepoInspector.Persistence.Http.Tests;

public sealed class HttpInspectionSnapshotSinkTests
{
    [Fact]
    public async Task WriteAsync_SendsCanonicalPayloadIdempotencyKeyAndBearerToken()
    {
        const string token = "test-bearer-token";
        string? capturedBody = null;
        string? capturedIdempotencyKey = null;
        string? capturedAuthorizationScheme = null;
        string? capturedAuthorizationParameter = null;
        using var handler = new StubHttpMessageHandler(async (request, _, cancellationToken) =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            capturedIdempotencyKey = request.Headers.GetValues("Idempotency-Key").Single();
            capturedAuthorizationScheme = request.Headers.Authorization?.Scheme;
            capturedAuthorizationParameter = request.Headers.Authorization?.Parameter;
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        using var client = new HttpClient(handler, disposeHandler: false);
        var snapshot = CreateSnapshot();
        var sink = new HttpInspectionSnapshotSink(
            client,
            new HttpInspectionSnapshotSinkOptions(new Uri("https://example.invalid/evidence"))
            {
                BearerToken = token,
                RetryBaseDelay = TimeSpan.Zero
            });

        InspectionSinkWriteResult result = await sink.WriteAsync(
            snapshot,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(snapshot.IdempotencyKey, capturedIdempotencyKey);
        Assert.Equal("Bearer", capturedAuthorizationScheme);
        Assert.Equal(token, capturedAuthorizationParameter);
        Assert.NotNull(capturedBody);
        Assert.DoesNotContain(token, capturedBody, StringComparison.Ordinal);

        using JsonDocument payload = JsonDocument.Parse(capturedBody);
        Assert.Equal(
            snapshot.IdempotencyKey,
            payload.RootElement.GetProperty("idempotencyKey").GetString());
        Assert.Equal(
            snapshot.ReportSha256,
            payload.RootElement.GetProperty("reportSha256").GetString());
    }

    [Fact]
    public async Task WriteAsync_RetriesTransientResponsesWithinConfiguredBound()
    {
        using var handler = new StubHttpMessageHandler((_, attempt, _) =>
            Task.FromResult(new HttpResponseMessage(
                attempt < 3
                    ? HttpStatusCode.ServiceUnavailable
                    : HttpStatusCode.NoContent)));
        using var client = new HttpClient(handler, disposeHandler: false);
        var sink = new HttpInspectionSnapshotSink(
            client,
            new HttpInspectionSnapshotSinkOptions(new Uri("https://example.invalid/evidence"))
            {
                MaxAttempts = 3,
                RetryBaseDelay = TimeSpan.Zero
            });

        InspectionSinkWriteResult result = await sink.WriteAsync(
            CreateSnapshot(),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task WriteAsync_DoesNotRetryAuthenticationFailureOrExposeResponseBody()
    {
        const string responseSecret = "remote-secret-must-not-leak";
        using var handler = new StubHttpMessageHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(responseSecret)
            }));
        using var client = new HttpClient(handler, disposeHandler: false);
        var sink = new HttpInspectionSnapshotSink(
            client,
            new HttpInspectionSnapshotSinkOptions(new Uri("https://example.invalid/evidence"))
            {
                MaxAttempts = 5,
                RetryBaseDelay = TimeSpan.Zero
            });

        InspectionSinkWriteResult result = await sink.WriteAsync(
            CreateSnapshot(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(1, handler.CallCount);
        Assert.NotNull(result.Failure);
        Assert.Equal(HttpInspectionSinkErrorCodes.AuthenticationFailed, result.Failure.Code);
        Assert.False(result.Failure.IsTransient);
        Assert.DoesNotContain(responseSecret, result.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_RetriesTransportFailureAndReturnsTransientFailure()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException()));
        using var client = new HttpClient(handler, disposeHandler: false);
        var sink = new HttpInspectionSnapshotSink(
            client,
            new HttpInspectionSnapshotSinkOptions(new Uri("https://example.invalid/evidence"))
            {
                MaxAttempts = 2,
                RetryBaseDelay = TimeSpan.Zero
            });

        InspectionSinkWriteResult result = await sink.WriteAsync(
            CreateSnapshot(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(2, handler.CallCount);
        Assert.NotNull(result.Failure);
        Assert.Equal(HttpInspectionSinkErrorCodes.TransportFailure, result.Failure.Code);
        Assert.True(result.Failure.IsTransient);
    }

    [Fact]
    public async Task WriteAsync_PropagatesCallerCancellation()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        using var client = new HttpClient(handler, disposeHandler: false);
        var sink = new HttpInspectionSnapshotSink(
            client,
            new HttpInspectionSnapshotSinkOptions(new Uri("https://example.invalid/evidence")));
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sink.WriteAsync(CreateSnapshot(), cancellationSource.Token));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void Constructor_RejectsNonHttpEndpoint()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        using var client = new HttpClient(handler, disposeHandler: false);

        Assert.Throws<ArgumentException>(() =>
            new HttpInspectionSnapshotSink(
                client,
                new HttpInspectionSnapshotSinkOptions(new Uri("ftp://example.invalid/evidence"))));
    }

    [Fact]
    public void Constructor_RejectsEndpointWithEmbeddedCredentials()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        using var client = new HttpClient(handler, disposeHandler: false);

        Assert.Throws<ArgumentException>(() =>
            new HttpInspectionSnapshotSink(
                client,
                new HttpInspectionSnapshotSinkOptions(
                    new Uri("https://user:password@example.invalid/evidence"))));
    }

    private static InspectionSnapshot CreateSnapshot()
    {
        InspectionReport report = InspectionReport.Create(
            new RepositoryMetadata(
                "sample",
                "0123456789012345678901234567890123456789",
                "main",
                "https://example.invalid/owner/sample.git",
                false),
            new DotNetSdkMetadata(null, null, "10.0.400"),
            [],
            []);

        return new InspectionSnapshotFactory(
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero)))
            .Create(report, "1.0.0-test");
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> _handler =
            handler ?? throw new ArgumentNullException(nameof(handler));

        public int CallCount
        {
            get;
            private set;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return _handler(request, CallCount, cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
