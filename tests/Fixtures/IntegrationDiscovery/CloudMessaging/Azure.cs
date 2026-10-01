namespace Fixture.CloudMessaging;

internal sealed class AzureMessaging
{
    public async Task RunAsync(ServiceBusClient bus, IConfiguration configuration)
    {
        ServiceBusSender sender = bus.CreateSender("orders");
        await sender.SendMessageAsync(new ServiceBusMessage("PRIVATE-SERVICEBUS-PAYLOAD"));

        ServiceBusProcessor processor = bus.CreateProcessor(
            configuration["Messaging:Azure:InputQueue"]);
        await processor.StartProcessingAsync();

        var producer = new EventHubProducerClient(
            "Endpoint=sb://secret.servicebus.windows.net/;SharedAccessKey=PRIVATE-SAS",
            configuration["Messaging:Azure:EventHub"]);
        await producer.SendAsync(new[] { new EventData("PRIVATE-EVENTHUB-DATA") });

        var eventProcessor = new EventProcessorClient(
            checkpointStore,
            "consumer-group",
            "Endpoint=sb://secret.servicebus.windows.net/;SharedAccessKey=PRIVATE-SAS",
            "incoming-hub");
        await eventProcessor.StartProcessingAsync();

        var grid = new EventGridPublisherClient(
            new Uri("https://orders-topic.eastus-1.eventgrid.azure.net/api/events"),
            new AzureKeyCredential("PRIVATE-EVENTGRID-KEY"));
        await grid.SendEventsAsync(new[] { new EventGridEvent("subject", "type", "1", "PRIVATE-GRID-DATA") });
    }
}
