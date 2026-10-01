namespace Fixture.EndToEnd.Worker;

internal sealed class Integrations
{
    public async Task RunAsync(
        IChannel rabbit,
        IProducer<string, OrderCreated> producer,
        IConsumer<string, OrderCreated> consumer,
        ServiceBusClient serviceBus,
        IConfiguration configuration)
    {
        await rabbit.BasicPublishAsync("orders.exchange", "orders.created", body: "E2E-PRIVATE-RABBIT-PAYLOAD");
        await rabbit.BasicConsumeAsync("orders.queue", autoAck: false, consumer: rabbitConsumer);
        await producer.ProduceAsync(configuration["Kafka:OrdersTopic"], "E2E-PRIVATE-KAFKA-PAYLOAD");
        consumer.Subscribe("orders.input");

        ServiceBusSender sender = serviceBus.CreateSender("orders-outbound");
        await sender.SendMessageAsync(new ServiceBusMessage("E2E-PRIVATE-SERVICEBUS-PAYLOAD"));
        serviceBus.CreateProcessor(configuration["ServiceBus:InputQueue"]);
    }
}

internal sealed class Lookalikes
{
    public void SendMessageAsync() { }
    public void PublishAsync() { }
    public void GetConnectionString() { }
    public void UploadObjectAsync() { }
}

internal sealed class SensitiveStrings
{
    private const string ConnectionString = "Host=private;Username=private-user;Password=E2E-PRIVATE-PASSWORD";
    private const string Authorization = "Bearer E2E-PRIVATE-TOKEN";
    private const string Sql = "SELECT * FROM private_table WHERE token = 'E2E-PRIVATE-SQL'";
    private const string SignedUrl = "https://private.example/file?sig=E2E-PRIVATE-SIGNATURE";
    private const string SourceSnippet = "class E2E_PRIVATE_SOURCE_BODY {}";
}
