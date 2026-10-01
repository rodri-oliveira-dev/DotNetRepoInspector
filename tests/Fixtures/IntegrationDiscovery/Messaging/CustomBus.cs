namespace Fixture.Messaging;

internal sealed class CustomBus
{
    public async Task RunAsync(
        IEventBus eventBus,
        IMessageConsumer consumer,
        OrderCreated message)
    {
        await eventBus.PublishAsync<OrderCreated>("custom-orders", message);
        await eventBus.PublishAsync<OrderCreated>(message);
        await consumer.SubscribeAsync<OrderCreated>("custom-events", HandleAsync);
    }
}
