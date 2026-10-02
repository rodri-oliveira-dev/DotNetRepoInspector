using DotNetRepoInspector.Core.Contracts;

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetRepoInspector.IntegrationDiscovery;

public sealed class AzureMessagingIntegrationDetector : IIntegrationDetector
{
    public string Id => "messaging-azure";

    public ValueTask DetectAsync(IntegrationDetectionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        VariableTypeMap types = CloudMessagingSyntax.FindVariableTypes(context.Root);
        var resources = new Dictionary<string, ResourceEvidence>(StringComparer.Ordinal);
        MapConstructedClients(context, types, resources);

        foreach (InvocationExpressionSyntax invocation in context.Root.DescendantNodes()
                     .OfType<InvocationExpressionSyntax>().OrderBy(static node => node.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CloudMessagingSyntax.TryGetInvocation(invocation, out string method, out ExpressionSyntax? receiver))
            {
                continue;
            }

            string? receiverName = (receiver as IdentifierNameSyntax)?.Identifier.ValueText;
            string? type = CloudMessagingSyntax.ReceiverType(receiver, types);
            ResourceEvidence mapped = receiverName is not null && resources.TryGetValue(receiverName, out ResourceEvidence? evidence)
                ? evidence
                : ResourceEvidence.Empty;

            if (type == "ServiceBusClient" && method is "CreateSender" or "CreateProcessor")
            {
                ResourceEvidence resource = CloudMessagingSyntax.Evidence(
                    context,
                    CloudMessagingSyntax.Argument(invocation, method == "CreateSender" ? ["queueOrTopicName"] : ["queueName", "topicName", "subscriptionName"], method == "CreateProcessor" && invocation.ArgumentList.Arguments.Count > 1 ? 1 : 0),
                    ["queueOrTopicName", "queueName", "topicName", "subscriptionName"]);
                MapInvocationResult(invocation, types, resources, method == "CreateSender" ? "ServiceBusSender" : "ServiceBusProcessor", resource);
                if (method == "CreateProcessor")
                {
                    CloudMessagingSyntax.Add(context, invocation, IntegrationDirection.Consume, "azure-servicebus", resource,
                        invocation.ArgumentList.Arguments.Count > 1 ? "subscription" : "queue", ["azure:servicebus", "servicebus:create-processor"]);
                }
                continue;
            }

            if (type == "ServiceBusSender" && method is "SendMessageAsync" or "SendMessagesAsync")
            {
                CloudMessagingSyntax.Add(context, invocation, IntegrationDirection.Publish, "azure-servicebus", mapped, "queue", ["azure:servicebus", "servicebus:send"]);
            }
            else if (type == "EventHubProducerClient" && method is "SendAsync" or "SendBatchAsync")
            {
                CloudMessagingSyntax.Add(context, invocation, IntegrationDirection.Publish, "azure-eventhubs", mapped, "event-hub", ["azure:eventhubs", "eventhubs:send"]);
            }
            else if (type is "EventProcessorClient" or "EventHubConsumerClient" && method is "StartProcessingAsync" or "ReadEventsAsync" or "ReadEventsFromPartitionAsync")
            {
                CloudMessagingSyntax.Add(context, invocation, IntegrationDirection.Consume, "azure-eventhubs", mapped, "event-hub", ["azure:eventhubs", "eventhubs:consume"]);
            }
            else if (type == "EventGridPublisherClient" && method is "SendEvent" or "SendEvents" or "SendEventAsync" or "SendEventsAsync")
            {
                CloudMessagingSyntax.Add(context, invocation, IntegrationDirection.Publish, "azure-eventgrid", mapped, "topic", ["azure:eventgrid", "eventgrid:send"]);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static void MapConstructedClients(
        IntegrationDetectionContext context,
        VariableTypeMap types,
        Dictionary<string, ResourceEvidence> resources)
    {
        foreach (VariableDeclaratorSyntax variable in context.Root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (!types.TryGetType(variable, out string? type))
                continue;
            (string[] names, int position)? rule = type switch
            {
                "EventHubProducerClient" => (["eventHubName"], 1),
                "EventProcessorClient" => (["eventHubName"], 3),
                "EventHubConsumerClient" => (["eventHubName"], 2),
                "EventGridPublisherClient" => (["endpoint"], 0),
                _ => null
            };
            if (rule is not null)
            {
                resources[variable.Identifier.ValueText] = CloudMessagingSyntax.ConstructionEvidence(context, variable, rule.Value.names, rule.Value.position);
            }
        }
    }

    private static void MapInvocationResult(
        InvocationExpressionSyntax invocation,
        VariableTypeMap types,
        Dictionary<string, ResourceEvidence> resources,
        string type,
        ResourceEvidence resource)
    {
        if (invocation.Parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variable })
            return;
        types.Add(variable, type);
        resources[variable.Identifier.ValueText] = resource;
    }
}
