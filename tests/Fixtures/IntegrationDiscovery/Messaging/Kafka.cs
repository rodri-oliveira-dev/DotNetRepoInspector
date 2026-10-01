namespace Fixture.Messaging;

internal sealed class Kafka
{
    public async Task RunAsync(
        IProducer<string, OrderCreated> producer,
        IConsumer<string, OrderCreated> consumer,
        IConfiguration configuration)
    {
        producer.Produce("orders.created", new Message<string, OrderCreated>());
        await producer.ProduceAsync(
            configuration["Messaging:OrdersTopic"],
            new Message<string, OrderCreated>());
        consumer.Subscribe("orders.input");
        consumer.Subscribe(configuration["Messaging:InputTopic"]);
    }
}
