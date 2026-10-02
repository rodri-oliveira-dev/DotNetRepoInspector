namespace Fixture.DataDependencies;

internal sealed class CacheNoSql
{
    public void Configure(IServiceCollection services, IConfiguration configuration)
    {
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration["Redis:Connection"];
            options.InstanceName = "LoanCache";
        });
    }

    public async Task RunAsync(
        IDistributedCache cache,
        IMongoClient mongo,
        CosmosClient cosmos,
        IAmazonDynamoDB dynamo,
        IConfiguration configuration)
    {
        await cache.GetAsync("customer-key");
        await cache.SetAsync("customer-key", "PRIVATE-REDIS-PAYLOAD");

        IMongoDatabase database = mongo.GetDatabase("loans");
        IMongoCollection<Loan> collection = database.GetCollection<Loan>("applications");
        await collection.FindAsync("PRIVATE-MONGO-QUERY");
        await collection.InsertOneAsync(new Loan("PRIVATE-MONGO-PAYLOAD"));

        Container container = cosmos.GetContainer(
            configuration["Cosmos:Database"],
            "loan-items");
        await container.ReadItemAsync<Loan>("id", new PartitionKey("PRIVATE-PARTITION"));
        await container.UpsertItemAsync(new Loan("PRIVATE-COSMOS-PAYLOAD"));

        await dynamo.GetItemAsync(new GetItemRequest
        {
            TableName = configuration["Dynamo:Table"],
            Key = PrivatePayload()
        });
        await dynamo.PutItemAsync(new PutItemRequest
        {
            TableName = "loan-events",
            Item = PrivatePayload()
        });
    }
}
