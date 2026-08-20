using System.Net;
using System.Net.Http.Headers;
using System.Text;

using DotNetRepoInspector.Persistence;

namespace DotNetRepoInspector.Persistence.Http;

public sealed class HttpInspectionSnapshotSink : IInspectionSnapshotSink
{
    private const double MaximumRetryDelayMilliseconds = 2_000;

    private readonly HttpClient _httpClient;
    private readonly HttpInspectionSnapshotSinkOptions _options;

    public HttpInspectionSnapshotSink(
        HttpClient httpClient,
        HttpInspectionSnapshotSinkOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();
        _httpClient = httpClient;
        _options = options;
    }

    public string Name => "http";

    public async Task<InspectionSinkWriteResult> WriteAsync(
        InspectionSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        string payload = InspectionSnapshotJsonSerializer.Serialize(snapshot);

        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            InspectionSinkWriteResult result;

            try
            {
                using HttpRequestMessage request = CreateRequest(snapshot, payload);
                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return InspectionSinkWriteResult.Success();
                }

                result = CreateResponseFailure(response.StatusCode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                result = InspectionSinkWriteResult.Failed(
                    HttpInspectionSinkErrorCodes.RequestTimeout,
                    "The HTTP persistence request timed out.",
                    isTransient: true);
            }
            catch (HttpRequestException)
            {
                result = InspectionSinkWriteResult.Failed(
                    HttpInspectionSinkErrorCodes.TransportFailure,
                    "The HTTP persistence endpoint could not be reached.",
                    isTransient: true);
            }

            if (result.Failure?.IsTransient != true || attempt == _options.MaxAttempts)
            {
                return result;
            }

            TimeSpan retryDelay = GetRetryDelay(attempt);
            if (retryDelay > TimeSpan.Zero)
            {
                await Task.Delay(retryDelay, cancellationToken);
            }
        }

        return InspectionSinkWriteResult.Failed(
            HttpInspectionSinkErrorCodes.TransportFailure,
            "The HTTP persistence request did not complete.",
            isTransient: true);
    }

    private HttpRequestMessage CreateRequest(
        InspectionSnapshot snapshot,
        string payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        _ = request.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            snapshot.IdempotencyKey);

        if (!string.IsNullOrWhiteSpace(_options.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                _options.BearerToken);
        }

        return request;
    }

    private TimeSpan GetRetryDelay(int completedAttempt)
    {
        double multiplier = Math.Pow(2, completedAttempt - 1);
        double delayMilliseconds = Math.Min(
            _options.RetryBaseDelay.TotalMilliseconds * multiplier,
            MaximumRetryDelayMilliseconds);

        return TimeSpan.FromMilliseconds(delayMilliseconds);
    }

    private static InspectionSinkWriteResult CreateResponseFailure(HttpStatusCode statusCode)
    {
        if (IsTransientStatus(statusCode))
        {
            return InspectionSinkWriteResult.Failed(
                HttpInspectionSinkErrorCodes.TransientResponse,
                "The HTTP persistence endpoint returned a transient failure response.",
                isTransient: true);
        }

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return InspectionSinkWriteResult.Failed(
                HttpInspectionSinkErrorCodes.AuthenticationFailed,
                "The HTTP persistence endpoint rejected the configured credentials.");
        }

        if (statusCode == HttpStatusCode.NotFound)
        {
            return InspectionSinkWriteResult.Failed(
                HttpInspectionSinkErrorCodes.EndpointNotFound,
                "The HTTP persistence endpoint was not found.");
        }

        if ((int)statusCode is >= 400 and <= 499)
        {
            return InspectionSinkWriteResult.Failed(
                HttpInspectionSinkErrorCodes.RequestRejected,
                "The HTTP persistence endpoint rejected the request.");
        }

        return InspectionSinkWriteResult.Failed(
            HttpInspectionSinkErrorCodes.UnexpectedResponse,
            "The HTTP persistence endpoint returned an unexpected response.");
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode) =>
        statusCode is
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;
}
