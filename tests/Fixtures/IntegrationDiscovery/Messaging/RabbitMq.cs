namespace Fixture.Messaging;

internal sealed class RabbitMq
{
    public void Configure(IConfiguration configuration, string payload)
    {
        IChannel channel = GetChannel();
        channel.BasicPublish(
            exchange: "orders.exchange",
            routingKey: "orders.created",
            body: payload);
        channel.BasicPublish(
            exchange: configuration["Messaging:RabbitExchange"],
            routingKey: "orders.updated",
            body: payload);

        AsyncEventingBasicConsumer consumer = CreateConsumer(channel);
        channel.BasicConsume(
            queue: "orders.queue",
            autoAck: false,
            consumer: consumer);
    }
}
