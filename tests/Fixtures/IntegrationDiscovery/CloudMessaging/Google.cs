namespace Fixture.CloudMessaging;

internal sealed class GoogleMessaging
{
    public async Task RunAsync(IConfiguration configuration)
    {
        PublisherClient publisher = await PublisherClient.CreateAsync(
            TopicName.FromProjectTopic("private-project-id", "orders-topic"));
        await publisher.PublishAsync("PRIVATE-PUBSUB-PAYLOAD");

        SubscriberClient subscriber = await SubscriberClient.CreateAsync(
            SubscriptionName.FromProjectSubscription(
                "private-project-id",
                configuration["Messaging:Gcp:Subscription"]));
        await subscriber.StartAsync((message, cancellationToken) => Task.FromResult(SubscriberClient.Reply.Ack));
    }
}
