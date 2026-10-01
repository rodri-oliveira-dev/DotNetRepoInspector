using DotNetRepoInspector.Core.Contracts;

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetRepoInspector.IntegrationDiscovery;

public sealed class GooglePubSubIntegrationDetector : IIntegrationDetector
{
    public string Id => "messaging-google-pubsub";

    public ValueTask DetectAsync(IntegrationDetectionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        Dictionary<string, string> types = CloudMessagingSyntax.FindVariableTypes(context.Root);
        var resources = new Dictionary<string, ResourceEvidence>(StringComparer.Ordinal);
        MapCreatedClients(context, types, resources);

        foreach (InvocationExpressionSyntax invocation in context.Root.DescendantNodes()
                     .OfType<InvocationExpressionSyntax>().OrderBy(static node => node.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CloudMessagingSyntax.TryGetInvocation(invocation, out string method, out ExpressionSyntax? receiver))
                continue;
            string? type = CloudMessagingSyntax.ReceiverType(receiver, types);
            string? name = (receiver as IdentifierNameSyntax)?.Identifier.ValueText;
            ResourceEvidence resource = name is not null && resources.TryGetValue(name, out ResourceEvidence? mapped)
                ? mapped
                : ResourceEvidence.Empty;

            if (type == "PublisherClient" && method == "PublishAsync")
            {
                CloudMessagingSyntax.Add(context, invocation, IntegrationDirection.Publish, "google-pubsub", resource, "topic", ["google:pubsub", "pubsub:publish"]);
            }
            else if (type == "SubscriberClient" && method is "StartAsync" or "PullAsync" or "SubscribeAsync")
            {
                CloudMessagingSyntax.Add(context, invocation, IntegrationDirection.Consume, "google-pubsub", resource, "subscription", ["google:pubsub", "pubsub:consume"]);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static void MapCreatedClients(
        IntegrationDetectionContext context,
        Dictionary<string, string> types,
        Dictionary<string, ResourceEvidence> resources)
    {
        foreach (VariableDeclaratorSyntax variable in context.Root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            ExpressionSyntax? value = variable.Initializer?.Value;
            InvocationExpressionSyntax? creation = value?.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(invocation => CloudMessagingSyntax.TryGetInvocation(invocation, out string method, out ExpressionSyntax? receiver) &&
                    method is "Create" or "CreateAsync" && receiver is IdentifierNameSyntax { Identifier.ValueText: "PublisherClient" or "SubscriberClient" });
            if (creation is null || !CloudMessagingSyntax.TryGetInvocation(creation, out _, out ExpressionSyntax? factory))
                continue;
            string type = ((IdentifierNameSyntax)factory!).Identifier.ValueText;
            types[variable.Identifier.ValueText] = type;
            ExpressionSyntax? resource = CloudMessagingSyntax.Argument(creation, type == "PublisherClient" ? ["topicName"] : ["subscriptionName"], 0);
            resources[variable.Identifier.ValueText] = CloudMessagingSyntax.Evidence(context, resource,
                type == "PublisherClient" ? ["TopicName", "topicName"] : ["SubscriptionName", "subscriptionName"]);
        }
    }
}
