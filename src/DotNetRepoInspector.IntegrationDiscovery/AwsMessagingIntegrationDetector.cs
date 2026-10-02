using DotNetRepoInspector.Core.Contracts;

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetRepoInspector.IntegrationDiscovery;

public sealed class AwsMessagingIntegrationDetector : IIntegrationDetector
{
    private static readonly HashSet<string> SqsTypes = new(StringComparer.Ordinal)
    {
        "AmazonSQSClient", "IAmazonSQS"
    };
    private static readonly HashSet<string> SnsTypes = new(StringComparer.Ordinal)
    {
        "AmazonSimpleNotificationServiceClient", "IAmazonSimpleNotificationService"
    };
    private static readonly HashSet<string> EventBridgeTypes = new(StringComparer.Ordinal)
    {
        "AmazonEventBridgeClient", "IAmazonEventBridge"
    };
    private static readonly HashSet<string> KinesisTypes = new(StringComparer.Ordinal)
    {
        "AmazonKinesisClient", "IAmazonKinesis"
    };

    public string Id => "messaging-aws";

    public ValueTask DetectAsync(IntegrationDetectionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        VariableTypeMap types = CloudMessagingSyntax.FindVariableTypes(context.Root);
        foreach (InvocationExpressionSyntax invocation in context.Root.DescendantNodes()
                     .OfType<InvocationExpressionSyntax>().OrderBy(static node => node.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CloudMessagingSyntax.TryGetInvocation(invocation, out string method, out ExpressionSyntax? receiver))
            {
                continue;
            }

            string? type = CloudMessagingSyntax.ReceiverType(receiver, types);
            if (type is not null)
            {
                DetectSqs(context, invocation, method, type);
                DetectSns(context, invocation, method, type);
                DetectEventBridge(context, invocation, method, type);
                DetectKinesis(context, invocation, method, type);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static void DetectSqs(IntegrationDetectionContext context, InvocationExpressionSyntax invocation, string method, string type)
    {
        if (!SqsTypes.Contains(type) || method is not ("SendMessageAsync" or "SendMessageBatchAsync" or "ReceiveMessageAsync"))
        {
            return;
        }

        ResourceEvidence resource = RequestEvidence(context, invocation, ["QueueUrl", "queueUrl"]);
        CloudMessagingSyntax.Add(
            context,
            invocation,
            method == "ReceiveMessageAsync" ? IntegrationDirection.Consume : IntegrationDirection.Publish,
            "aws-sqs",
            resource,
            "queue",
            ["aws:sqs", method == "ReceiveMessageAsync" ? "sqs:receive" : "sqs:send"]);
    }

    private static void DetectSns(IntegrationDetectionContext context, InvocationExpressionSyntax invocation, string method, string type)
    {
        if (!SnsTypes.Contains(type) || method != "PublishAsync")
        {
            return;
        }

        CloudMessagingSyntax.Add(
            context,
            invocation,
            IntegrationDirection.Publish,
            "aws-sns",
            RequestEvidence(context, invocation, ["TopicArn", "TargetArn", "topicArn"]),
            "topic",
            ["aws:sns", "sns:publish"]);
    }

    private static void DetectEventBridge(IntegrationDetectionContext context, InvocationExpressionSyntax invocation, string method, string type)
    {
        if (!EventBridgeTypes.Contains(type) || method != "PutEventsAsync")
        {
            return;
        }

        ExpressionSyntax? request = CloudMessagingSyntax.Argument(invocation, ["request"], 0);
        ResourceEvidence resource = CloudMessagingSyntax.Evidence(context, request, ["EventBusName"]);
        var signals = new List<string> { "aws:eventbridge", "eventbridge:put-events" };
        if (HasAssignedProperty(request, "Source"))
            signals.Add("eventbridge:source-observed");
        if (HasAssignedProperty(request, "DetailType"))
            signals.Add("eventbridge:detail-type-observed");
        CloudMessagingSyntax.Add(context, invocation, IntegrationDirection.Publish, "aws-eventbridge", resource, "event-bus", signals);
    }

    private static void DetectKinesis(IntegrationDetectionContext context, InvocationExpressionSyntax invocation, string method, string type)
    {
        if (!KinesisTypes.Contains(type))
        {
            return;
        }

        string? direction = method switch
        {
            "PutRecordAsync" or "PutRecordsAsync" => IntegrationDirection.Publish,
            "GetRecordsAsync" or "GetShardIteratorAsync" or "SubscribeToShardAsync" => IntegrationDirection.Consume,
            _ => null
        };
        if (direction is null)
        {
            return;
        }

        CloudMessagingSyntax.Add(
            context,
            invocation,
            direction,
            "aws-kinesis",
            RequestEvidence(context, invocation, ["StreamName", "streamName"]),
            "stream",
            ["aws:kinesis", direction == IntegrationDirection.Publish ? "kinesis:put-record" : "kinesis:read"]);
    }

    private static ResourceEvidence RequestEvidence(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        IReadOnlyCollection<string> properties)
    {
        ExpressionSyntax? request = CloudMessagingSyntax.Argument(invocation, ["request", .. properties], 0);
        return CloudMessagingSyntax.Evidence(context, request, properties);
    }

    private static bool HasAssignedProperty(ExpressionSyntax? expression, string property) =>
        expression?.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>().Any(assignment =>
            assignment.Left is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == property) == true;
}
