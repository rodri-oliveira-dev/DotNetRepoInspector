using DotNetRepoInspector.Core.Contracts;

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetRepoInspector.IntegrationDiscovery;

public sealed class CacheAndNoSqlIntegrationDetector : IIntegrationDetector
{
    public string Id => "data-cache-nosql";

    public ValueTask DetectAsync(IntegrationDetectionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        Dictionary<string, string> types = CloudMessagingSyntax.FindVariableTypes(context.Root);
        var resources = new Dictionary<string, ResourceEvidence>(StringComparer.Ordinal);
        MapConstructedClients(context, types, resources);

        foreach (InvocationExpressionSyntax invocation in context.Root.DescendantNodes()
                     .OfType<InvocationExpressionSyntax>().OrderBy(static node => node.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CloudMessagingSyntax.TryGetInvocation(invocation, out string method, out ExpressionSyntax? receiver))
            {
                continue;
            }

            if (method == "AddStackExchangeRedisCache")
            {
                DetectRedisRegistration(context, invocation);
                continue;
            }

            string? type = CloudMessagingSyntax.ReceiverType(receiver, types);
            ResourceEvidence mapped = DataIntegrationSyntax.MappedResource(receiver, resources);
            if (DetectRedis(context, invocation, method, type, mapped) ||
                DetectMongo(context, invocation, method, type, mapped, types, resources) ||
                DetectCosmos(context, invocation, method, type, mapped, types, resources) ||
                DetectDynamoDb(context, invocation, method, type))
            {
                continue;
            }
        }

        return ValueTask.CompletedTask;
    }

    private static void DetectRedisRegistration(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation)
    {
        ResourceEvidence target = CloudMessagingSyntax.Evidence(context, invocation, ["InstanceName"]);
        ResourceEvidence configuration = CloudMessagingSyntax.Evidence(context, invocation, ["Configuration"]);
        var resource = new ResourceEvidence(target.Target, configuration.ConfigurationKey);
        DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Cache, IntegrationDirection.Bidirectional,
            "redis", resource, "cache", ["cache:redis", "redis:registration"]);
    }

    private static bool DetectRedis(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        string method,
        string? type,
        ResourceEvidence resource)
    {
        if (type is not ("IDistributedCache" or "IDatabase" or "ConnectionMultiplexer"))
            return false;
        string? direction = method switch
        {
            "Get" or "GetAsync" or "StringGet" or "StringGetAsync" => IntegrationDirection.Read,
            "Set" or "SetAsync" or "Remove" or "RemoveAsync" or "StringSet" or "StringSetAsync" or "KeyDelete" or "KeyDeleteAsync" => IntegrationDirection.Write,
            "Connect" or "ConnectAsync" => IntegrationDirection.Bidirectional,
            _ => null
        };
        if (direction is null)
            return false;
        DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Cache, direction, "redis", resource,
            "cache", ["cache:redis", $"redis:{direction}"]);
        return true;
    }

    private static bool DetectMongo(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        string method,
        string? type,
        ResourceEvidence mapped,
        Dictionary<string, string> types,
        Dictionary<string, ResourceEvidence> resources)
    {
        if (type is "MongoClient" or "IMongoClient" && method == "GetDatabase")
        {
            ResourceEvidence database = DataIntegrationSyntax.RequestEvidence(context, invocation, ["name", "databaseName"]);
            DataIntegrationSyntax.MapInvocationResult(invocation, types, resources, "IMongoDatabase", database);
            DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Database, IntegrationDirection.Bidirectional,
                "mongodb", database, "database", ["database:mongodb", "mongodb:get-database"]);
            return true;
        }

        if (type == "IMongoDatabase" && method == "GetCollection")
        {
            ResourceEvidence collection = DataIntegrationSyntax.RequestEvidence(context, invocation, ["name", "collectionName"]);
            DataIntegrationSyntax.MapInvocationResult(invocation, types, resources, "IMongoCollection", collection);
            DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Database, IntegrationDirection.Bidirectional,
                "mongodb", collection, "collection", ["database:mongodb", "mongodb:get-collection"]);
            return true;
        }

        if (type != "IMongoCollection")
            return false;
        string? direction = method switch
        {
            "Find" or "FindAsync" or "Aggregate" or "AggregateAsync" => IntegrationDirection.Read,
            "InsertOne" or "InsertOneAsync" or "InsertMany" or "InsertManyAsync" or "ReplaceOne" or "ReplaceOneAsync" or "UpdateOne" or "UpdateOneAsync" or "DeleteOne" or "DeleteOneAsync" => IntegrationDirection.Write,
            _ => null
        };
        if (direction is null)
            return false;
        DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Database, direction, "mongodb", mapped,
            "collection", ["database:mongodb", $"mongodb:{direction}"]);
        return true;
    }

    private static bool DetectCosmos(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        string method,
        string? type,
        ResourceEvidence mapped,
        Dictionary<string, string> types,
        Dictionary<string, ResourceEvidence> resources)
    {
        if (type == "CosmosClient" && method == "GetDatabase")
        {
            ResourceEvidence database = DataIntegrationSyntax.RequestEvidence(context, invocation, ["id", "databaseId"]);
            DataIntegrationSyntax.MapInvocationResult(invocation, types, resources, "Database", database);
            DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Database, IntegrationDirection.Bidirectional,
                "azure-cosmosdb", database, "database", ["database:azure-cosmosdb", "cosmos:get-database"]);
            return true;
        }

        if ((type == "CosmosClient" || type == "Database") && method == "GetContainer")
        {
            int position = type == "CosmosClient" ? 1 : 0;
            ResourceEvidence container = DataIntegrationSyntax.RequestEvidence(context, invocation, ["containerId", "id"], position);
            DataIntegrationSyntax.MapInvocationResult(invocation, types, resources, "Container", container);
            DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Database, IntegrationDirection.Bidirectional,
                "azure-cosmosdb", container, "container", ["database:azure-cosmosdb", "cosmos:get-container"]);
            return true;
        }

        if (type != "Container")
            return false;
        string? direction = method switch
        {
            "ReadItemAsync" or "GetItemQueryIterator" => IntegrationDirection.Read,
            "CreateItemAsync" or "UpsertItemAsync" or "ReplaceItemAsync" or "DeleteItemAsync" => IntegrationDirection.Write,
            _ => null
        };
        if (direction is null)
            return false;
        DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Database, direction, "azure-cosmosdb", mapped,
            "container", ["database:azure-cosmosdb", $"cosmos:{direction}"]);
        return true;
    }

    private static bool DetectDynamoDb(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        string method,
        string? type)
    {
        if (type is not ("IAmazonDynamoDB" or "AmazonDynamoDBClient"))
            return false;
        string? direction = method switch
        {
            "GetItemAsync" or "QueryAsync" or "ScanAsync" or "BatchGetItemAsync" => IntegrationDirection.Read,
            "PutItemAsync" or "UpdateItemAsync" or "DeleteItemAsync" or "BatchWriteItemAsync" => IntegrationDirection.Write,
            _ => null
        };
        if (direction is null)
            return false;
        ResourceEvidence table = DataIntegrationSyntax.RequestEvidence(context, invocation, ["TableName", "tableName"]);
        DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Database, direction, "aws-dynamodb", table,
            "table", ["database:aws-dynamodb", $"dynamodb:{direction}"]);
        return true;
    }

    private static void MapConstructedClients(
        IntegrationDetectionContext context,
        Dictionary<string, string> types,
        Dictionary<string, ResourceEvidence> resources)
    {
        foreach (VariableDeclaratorSyntax variable in context.Root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (!types.TryGetValue(variable.Identifier.ValueText, out string? type))
                continue;
            if (type == "ConnectionMultiplexer")
            {
                resources[variable.Identifier.ValueText] = CloudMessagingSyntax.Evidence(
                    context, variable.Initializer?.Value, ["configuration"]);
            }
            else if (type == "MongoClient")
            {
                resources[variable.Identifier.ValueText] = ResourceEvidence.Empty;
            }
            else if (type == "CosmosClient")
            {
                // Constructor input is a credential-bearing endpoint/connection string and is deliberately ignored.
                resources[variable.Identifier.ValueText] = ResourceEvidence.Empty;
            }
        }
    }
}
