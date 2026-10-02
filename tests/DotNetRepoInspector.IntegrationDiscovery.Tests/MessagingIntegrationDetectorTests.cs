using DotNetRepoInspector.Core.Contracts;

using Xunit;

namespace DotNetRepoInspector.IntegrationDiscovery.Tests;

public sealed class MessagingIntegrationDetectorTests
{
    [Fact]
    public async Task DetectsRabbitMqPublishConsumeAndResources()
    {
        IntegrationDiscoveryResult result = await DiscoverAsync();

        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Technology == "rabbitmq" &&
                finding.Direction == IntegrationDirection.Publish &&
                finding.Target == "orders.exchange" &&
                finding.ResourceType == "exchange");
        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Technology == "rabbitmq" &&
                finding.ConfigurationKey == "Messaging:RabbitExchange" &&
                finding.ResourceType == "exchange");
        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Technology == "rabbitmq" &&
                finding.Direction == IntegrationDirection.Consume &&
                finding.Target == "orders.queue" &&
                finding.ResourceType == "queue" &&
                finding.Contract == "AsyncEventingBasicConsumer");
    }

    [Fact]
    public async Task DetectsMassTransitPublishSendReceiveAndConsumerContracts()
    {
        IntegrationDiscoveryResult result = await DiscoverAsync();

        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Technology == "masstransit" &&
                finding.Direction == IntegrationDirection.Publish &&
                finding.Contract == "OrderCreated" &&
                finding.Signals.Contains("masstransit:publish"));
        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Technology == "masstransit" &&
                finding.Contract == "OrderCommand" &&
                finding.Signals.Contains("masstransit:send"));
        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Technology == "masstransit" &&
                finding.Target == "payments" &&
                finding.ResourceType == "queue" &&
                finding.Contract == "PaymentRequested");
        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Technology == "masstransit" &&
                finding.Direction == IntegrationDirection.Consume &&
                finding.Target == "payment-approved" &&
                finding.Contract == "PaymentApprovedConsumer");
        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Technology == "masstransit" &&
                finding.ConfigurationKey == "Messaging:AuditQueue" &&
                finding.Contract == "AuditConsumer");
    }

    [Fact]
    public async Task DetectsKafkaProduceSubscribeTopicsAndMessageTypes()
    {
        IntegrationDiscoveryResult result = await DiscoverAsync();
        IntegrationFinding[] kafka = result.Findings
            .Where(static finding => finding.Technology == "kafka")
            .ToArray();

        Assert.Equal(4, kafka.Length);
        Assert.Contains(
            kafka,
            static finding =>
                finding.Direction == IntegrationDirection.Publish &&
                finding.Target == "orders.created" &&
                finding.Contract == "OrderCreated");
        Assert.Contains(
            kafka,
            static finding =>
                finding.Direction == IntegrationDirection.Publish &&
                finding.ConfigurationKey == "Messaging:OrdersTopic");
        Assert.Contains(
            kafka,
            static finding =>
                finding.Direction == IntegrationDirection.Consume &&
                finding.Target == "orders.input");
        Assert.Contains(
            kafka,
            static finding =>
                finding.Direction == IntegrationDirection.Consume &&
                finding.ConfigurationKey == "Messaging:InputTopic");
        Assert.All(kafka, static finding => Assert.Equal("topic", finding.ResourceType));
    }

    [Fact]
    public async Task DetectsNServiceBusSendPublishRouteAndHandler()
    {
        IntegrationDiscoveryResult result = await DiscoverAsync();

        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Technology == "nservicebus" &&
                finding.Target == "sales" &&
                finding.Contract == "OrderCommand");
        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Technology == "nservicebus" &&
                finding.Direction == IntegrationDirection.Publish &&
                finding.Contract == "OrderCreated" &&
                finding.Signals.Contains("nservicebus:publish"));
        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Technology == "nservicebus" &&
                finding.Target == "payments" &&
                finding.Contract == "PaymentRequested" &&
                finding.Signals.Contains("nservicebus:route"));
        Assert.Contains(
            result.Findings,
            static finding =>
                finding.Technology == "nservicebus" &&
                finding.Direction == IntegrationDirection.Consume &&
                finding.Contract == "OrderCreated" &&
                finding.Signals.Contains("nservicebus:handler"));
    }

    [Fact]
    public async Task CustomFallbackRequiresStrongMessagingReceiverType()
    {
        IntegrationDiscoveryResult result = await DiscoverAsync();
        IntegrationFinding[] custom = result.Findings
            .Where(static finding => finding.Technology == IntegrationKind.Unknown)
            .ToArray();

        Assert.Equal(3, custom.Length);
        Assert.Contains(
            custom,
            static finding =>
                finding.Direction == IntegrationDirection.Publish &&
                finding.Target == "custom-orders" &&
                finding.Contract == "OrderCreated");
        Assert.Contains(
            custom,
            static finding =>
                finding.Direction == IntegrationDirection.Consume &&
                finding.Target == "custom-events" &&
                finding.Contract == "OrderCreated");
        Assert.Contains(
            custom,
            static finding =>
                finding.Direction == IntegrationDirection.Publish &&
                finding.Target is null &&
                finding.Contract == "OrderCreated" &&
                finding.Confidence == IntegrationConfidence.Medium);
        Assert.DoesNotContain(
            result.Findings,
            static finding => finding.Source.Path == "Messaging/Negative.cs");
    }

    [Fact]
    public async Task OutputIsDeterministicAndContainsNoCredentialsOrPayloads()
    {
        var pipeline = new IntegrationDiscoveryPipeline([new MessagingIntegrationDetector()]);
        var request = new IntegrationDiscoveryRequest(
            FixtureRoot,
            [new IntegrationDiscoveryProject("Messaging/Messaging.csproj")]);

        IntegrationDiscoveryResult first = await pipeline.DiscoverAsync(
            request,
            TestContext.Current.CancellationToken);
        IntegrationDiscoveryResult second = await pipeline.DiscoverAsync(
            request,
            TestContext.Current.CancellationToken);
        string firstJson = InspectionJsonSerializer.Serialize(Report(first));
        string secondJson = InspectionJsonSerializer.Serialize(Report(second));

        Assert.Equal(firstJson, secondJson);
        Assert.DoesNotContain("fixture-user", firstJson, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-password", firstJson, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-secret", firstJson, StringComparison.Ordinal);
        Assert.DoesNotContain("bootstrap.servers", firstJson, StringComparison.Ordinal);
        Assert.DoesNotContain("new OrderCreated", firstJson, StringComparison.Ordinal);
        Assert.All(
            first.Findings,
            static finding =>
            {
                Assert.Equal(IntegrationKind.Messaging, finding.Kind);
                Assert.Equal("Messaging/Messaging.csproj", finding.ProjectPath);
                Assert.StartsWith("Messaging/", finding.Source.Path, StringComparison.Ordinal);
                Assert.True(finding.Source.Line > 0);
            });
        Assert.Empty(first.Diagnostics);
    }

    private static async Task<IntegrationDiscoveryResult> DiscoverAsync()
    {
        var pipeline = new IntegrationDiscoveryPipeline([new MessagingIntegrationDetector()]);
        return await pipeline.DiscoverAsync(
            new IntegrationDiscoveryRequest(
                FixtureRoot,
                [new IntegrationDiscoveryProject("Messaging/Messaging.csproj")]),
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
