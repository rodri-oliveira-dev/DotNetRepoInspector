using DotNetRepoInspector.Core.Contracts;

using Xunit;

namespace DotNetRepoInspector.IntegrationDiscovery.Tests;

public sealed class HttpIntegrationDetectorTests
{
    [Fact]
    public async Task DetectsNamedTypedDirectAndFactoryHttpClients()
    {
        IntegrationDiscoveryResult result = await DiscoverAsync("Http/Http.csproj");

        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Technology == "httpclient" &&
                finding.Target == "Antifraud" &&
                finding.Signals.Contains("http:named-client"));
        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Contract == "IWeatherClient" &&
                finding.Target == "Weather" &&
                finding.Confidence == IntegrationConfidence.Medium);
        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Contract == "IPaymentsApi" &&
                finding.Target == "Payments");
        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Target == "direct.example.test" &&
                finding.Signals.Contains("http:base-address"));
        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Target == "Direct" &&
                finding.ConfigurationKey == "Direct:BaseUrl");
        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Target == "Catalog" &&
                finding.Signals.Contains("http:create-client"));
        Assert.Contains(result.Findings, static finding => finding.Target == "put.example.test");
        Assert.Contains(result.Findings, static finding => finding.Target == "patch.example.test");
        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Target == "Send" &&
                finding.ConfigurationKey == "Send:RequestUri");
        Assert.Contains(result.Findings, static finding => finding.Target == "new-client.example.test");
        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Source.Path == "Http/NoTarget.cs" &&
                finding.Target is null &&
                finding.Confidence == IntegrationConfidence.Low);
        Assert.All(
            result.Findings,
            static finding =>
            {
                Assert.Equal(IntegrationKind.Http, finding.Kind);
                Assert.Equal(IntegrationDirection.Outbound, finding.Direction);
                Assert.Equal("Http/Http.csproj", finding.ProjectPath);
                Assert.StartsWith("Http/", finding.Source.Path, StringComparison.Ordinal);
                Assert.True(finding.Source.Line > 0);
            });
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task DetectsRefitAndCorrelatesFluentBaseAddressConfiguration()
    {
        IntegrationDiscoveryResult result = await DiscoverAsync("Http/Http.csproj");

        IntegrationFinding serasa = Assert.Single(
            result.Findings,
            static finding =>
                finding.Technology == "refit" &&
                finding.Contract == "ISerasaApi");
        Assert.Equal("Serasa", serasa.Target);
        Assert.Equal("Serasa:BaseUrl", serasa.ConfigurationKey);
        Assert.Equal(IntegrationConfidence.High, serasa.Confidence);
        Assert.Contains("refit:add-refit-client", serasa.Signals);
        Assert.Contains("http:base-address", serasa.Signals);

        IntegrationFinding inventory = Assert.Single(
            result.Findings,
            static finding =>
                finding.Technology == "refit" &&
                finding.Contract == "IInventoryApi");
        Assert.Equal("Inventory", inventory.Target);
        Assert.Null(inventory.ConfigurationKey);
        Assert.Equal(IntegrationConfidence.Medium, inventory.Confidence);

        IntegrationFinding shipping = Assert.Single(
            result.Findings,
            static finding =>
                finding.Technology == "refit" &&
                finding.Contract == "IShippingApi");
        Assert.Equal("Shipping", shipping.Target);
        Assert.Contains("http:literal-host", shipping.Signals);
    }

    [Fact]
    public async Task IgnoresCommentsStringsAndCustomGetAsyncReceivers()
    {
        IntegrationDiscoveryResult result = await DiscoverAsync("Http/Http.csproj");

        IntegrationFinding negative = Assert.Single(
            result.Findings,
            static finding => finding.Source.Path == "Http/Negative.cs");
        Assert.Null(negative.Target);
        Assert.DoesNotContain(
            result.Findings,
            static finding => finding.Target == "FalsePositive");
        Assert.DoesNotContain(
            result.Findings,
            static finding => finding.Target == "custom.example.test");
        Assert.DoesNotContain(
            result.Findings,
            static finding => finding.Target == "comment.example.test");
        Assert.DoesNotContain(
            result.Findings,
            static finding => finding.Target == "CustomRoute");
    }

    [Fact]
    public async Task DetectsMultipleClientsAndPreservesTestProjectOwnership()
    {
        IntegrationDiscoveryResult result = await DiscoverAsync(
            "HttpTests/HttpTests.csproj",
            "Http/Http.csproj");

        Assert.Contains(result.Findings, static finding => finding.Target == "FirstExternalApi");
        Assert.Contains(result.Findings, static finding => finding.Target == "SecondExternalApi");
        IntegrationFinding mock = Assert.Single(
            result.Findings,
            static finding => finding.Target == "MockPayments");
        Assert.Equal("HttpTests/HttpTests.csproj", mock.ProjectPath);
        Assert.Equal("HttpTests/MockRegistration.cs", mock.Source.Path);
    }

    [Fact]
    public async Task SanitizesLiteralUrisAndNeverSerializesSecretsOrSourceBodies()
    {
        IntegrationDiscoveryResult result = await DiscoverAsync("Http/Http.csproj");
        string json = InspectionJsonSerializer.Serialize(Report(result));

        Assert.Contains(
            result.Findings,
            static finding => finding.Target == "secure.example.test");
        Assert.DoesNotContain("fixture-password", json, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("user:", json, StringComparison.Ordinal);
        Assert.DoesNotContain("/private", json, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", json, StringComparison.Ordinal);
        Assert.DoesNotContain("services.AddHttpClient", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProducesDeterministicOutputAcrossProjectOrder()
    {
        IntegrationDiscoveryResult first = await DiscoverAsync(
            "Http/Http.csproj",
            "HttpTests/HttpTests.csproj");
        IntegrationDiscoveryResult second = await DiscoverAsync(
            "HttpTests/HttpTests.csproj",
            "Http/Http.csproj");

        Assert.Equal(
            InspectionJsonSerializer.Serialize(Report(first)),
            InspectionJsonSerializer.Serialize(Report(second)));
    }

    private static async Task<IntegrationDiscoveryResult> DiscoverAsync(params string[] projects)
    {
        var pipeline = new IntegrationDiscoveryPipeline([new HttpIntegrationDetector()]);
        return await pipeline.DiscoverAsync(
            new IntegrationDiscoveryRequest(
                FixtureRoot,
                projects.Select(static path => new IntegrationDiscoveryProject(path)).ToArray()),
            TestContext.Current.CancellationToken);
    }

    private static InspectionReport Report(IntegrationDiscoveryResult result) =>
        InspectionReport.Create(
            new RepositoryMetadata("fixture", null, null, null, false),
            new DotNetSdkMetadata(null, null, null),
            Array.Empty<ProjectInspection>(),
            result.Diagnostics) with
        {
            Integrations = result.Findings
        };

    private static string FixtureRoot =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "IntegrationDiscovery");
}
