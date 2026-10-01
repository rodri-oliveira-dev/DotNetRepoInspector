namespace Fixture.CloudMessaging;

internal sealed class AwsMessaging
{
    public async Task RunAsync(
        IAmazonSQS sqs,
        IAmazonSimpleNotificationService sns,
        IAmazonEventBridge eventBridge,
        IAmazonKinesis kinesis,
        IConfiguration configuration)
    {
        await sqs.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/orders",
            MessageBody = "PRIVATE-SQS-PAYLOAD"
        });
        await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = configuration["Messaging:Aws:InputQueue"]
        });
        await sns.PublishAsync(new PublishRequest
        {
            TopicArn = "arn:aws:sns:us-east-1:123456789012:order-events",
            Message = "PRIVATE-SNS-PAYLOAD"
        });
        await eventBridge.PutEventsAsync(
            new PutEventsRequest
            {
                Entries =
                [
                    new PutEventsRequestEntry
                    {
                        EventBusName = configuration["Messaging:Aws:EventBus"],
                        Source = "fixture.orders",
                        DetailType = "OrderCreated",
                        Detail = "PRIVATE-EVENT-DETAIL"
                    }
                ]
            });
        await kinesis.PutRecordAsync(new PutRecordRequest
        {
            StreamName = "orders-stream",
            Data = "PRIVATE-KINESIS-DATA"
        });
        await kinesis.GetShardIteratorAsync(new GetShardIteratorRequest
        {
            StreamName = configuration["Messaging:Aws:InputStream"]
        });
    }
}
