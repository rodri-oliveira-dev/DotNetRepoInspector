namespace Fixture.EndToEnd.App;

internal sealed class Integrations
{
    public void Configure(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(
            "billing",
            client => client.BaseAddress = new Uri(configuration["Billing:BaseUrl"]));
        services.AddHttpClient<TypedBillingClient>(
            client => client.BaseAddress = new Uri("https://typed-billing.example.test"));
        services.AddRefitClient<IExternalCatalogApi>()
            .ConfigureHttpClient(client => client.BaseAddress = new Uri(configuration["Catalog:BaseUrl"]));
        services.AddNpgsqlDataSource(configuration.GetConnectionString("OrdersDb"));
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration["Redis:Connection"];
            options.InstanceName = "OrdersCache";
        });
    }

    public async Task PublishAsync(
        IAmazonSQS sqs,
        PublisherClient publisher,
        IAmazonS3 s3,
        IEventBus eventBus)
    {
        await sqs.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/orders",
            MessageBody = "E2E-PRIVATE-SQS-PAYLOAD"
        });
        await publisher.PublishAsync("E2E-PRIVATE-PUBSUB-PAYLOAD");
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "orders-documents",
            ContentBody = "E2E-PRIVATE-S3-PAYLOAD"
        });
        await eventBus.PublishAsync<OrderCreated>("custom-orders", new OrderCreated("E2E-PRIVATE-CUSTOM-PAYLOAD"));
    }
}
