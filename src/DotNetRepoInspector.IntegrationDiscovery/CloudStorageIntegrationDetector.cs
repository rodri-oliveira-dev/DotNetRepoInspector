using DotNetRepoInspector.Core.Contracts;

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetRepoInspector.IntegrationDiscovery;

public sealed class CloudStorageIntegrationDetector : IIntegrationDetector
{
    public string Id => "data-cloud-storage";

    public ValueTask DetectAsync(IntegrationDetectionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        VariableTypeMap types = CloudMessagingSyntax.FindVariableTypes(context.Root);
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

            string? type = CloudMessagingSyntax.ReceiverType(receiver, types);
            ResourceEvidence mapped = DataIntegrationSyntax.MappedResource(receiver, resources);
            if (DetectS3(context, invocation, method, type) ||
                DetectGoogleStorage(context, invocation, method, type) ||
                DetectAzureBlob(context, invocation, method, type, mapped, types, resources) ||
                DetectBigQuery(context, invocation, method, type, mapped, types, resources))
            {
                continue;
            }
        }

        return ValueTask.CompletedTask;
    }

    private static bool DetectS3(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        string method,
        string? type)
    {
        if (type is not ("IAmazonS3" or "AmazonS3Client"))
            return false;
        string? direction = method switch
        {
            "GetObjectAsync" or "ListObjectsAsync" or "ListObjectsV2Async" => IntegrationDirection.Read,
            "PutObjectAsync" or "DeleteObjectAsync" or "UploadObjectFromFilePathAsync" => IntegrationDirection.Write,
            _ => null
        };
        if (direction is null)
            return false;
        ResourceEvidence bucket = DataIntegrationSyntax.RequestEvidence(context, invocation, ["BucketName", "bucketName"]);
        DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Storage, direction, "aws-s3", bucket,
            "bucket", ["storage:aws-s3", $"s3:{direction}"]);
        return true;
    }

    private static bool DetectGoogleStorage(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        string method,
        string? type)
    {
        if (type != "StorageClient")
            return false;
        string? direction = method switch
        {
            "DownloadObject" or "DownloadObjectAsync" or "GetObject" or "ListObjects" => IntegrationDirection.Read,
            "UploadObject" or "UploadObjectAsync" or "DeleteObject" or "DeleteObjectAsync" => IntegrationDirection.Write,
            _ => null
        };
        if (direction is null)
            return false;
        ResourceEvidence bucket = DataIntegrationSyntax.RequestEvidence(context, invocation, ["bucket", "bucketName"]);
        DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Storage, direction, "google-cloud-storage", bucket,
            "bucket", ["storage:google-cloud-storage", $"gcs:{direction}"]);
        return true;
    }

    private static bool DetectAzureBlob(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        string method,
        string? type,
        ResourceEvidence mapped,
        VariableTypeMap types,
        Dictionary<string, ResourceEvidence> resources)
    {
        if (type == "BlobServiceClient" && method == "GetBlobContainerClient")
        {
            ResourceEvidence container = DataIntegrationSyntax.RequestEvidence(context, invocation, ["blobContainerName", "containerName"]);
            DataIntegrationSyntax.MapInvocationResult(invocation, types, resources, "BlobContainerClient", container);
            DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Storage, IntegrationDirection.Bidirectional,
                "azure-blob-storage", container, "container", ["storage:azure-blob-storage", "blob:get-container"]);
            return true;
        }

        if (type != "BlobContainerClient")
            return false;
        string? direction = method switch
        {
            "DownloadBlobTo" or "DownloadBlobToAsync" or "DownloadContent" or "DownloadContentAsync" => IntegrationDirection.Read,
            "UploadBlob" or "UploadBlobAsync" or "DeleteBlob" or "DeleteBlobAsync" => IntegrationDirection.Write,
            _ => null
        };
        if (direction is null)
            return false;
        DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Storage, direction, "azure-blob-storage", mapped,
            "container", ["storage:azure-blob-storage", $"blob:{direction}"]);
        return true;
    }

    private static bool DetectBigQuery(
        IntegrationDetectionContext context,
        InvocationExpressionSyntax invocation,
        string method,
        string? type,
        ResourceEvidence mapped,
        VariableTypeMap types,
        Dictionary<string, ResourceEvidence> resources)
    {
        if (type == "BigQueryClient" && method == "GetDataset")
        {
            ResourceEvidence dataset = DataIntegrationSyntax.RequestEvidence(context, invocation, ["datasetId"]);
            DataIntegrationSyntax.MapInvocationResult(invocation, types, resources, "BigQueryDataset", dataset);
            DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Database, IntegrationDirection.Read,
                "google-bigquery", dataset, "dataset", ["database:google-bigquery", "bigquery:get-dataset"]);
            return true;
        }

        if ((type == "BigQueryClient" || type == "BigQueryDataset") && method == "GetTable")
        {
            int position = type == "BigQueryClient" ? Math.Max(0, invocation.ArgumentList.Arguments.Count - 1) : 0;
            ResourceEvidence table = DataIntegrationSyntax.RequestEvidence(context, invocation, ["tableId"], position);
            DataIntegrationSyntax.MapInvocationResult(invocation, types, resources, "BigQueryTable", table);
            DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Database, IntegrationDirection.Read,
                "google-bigquery", table, "table", ["database:google-bigquery", "bigquery:get-table"]);
            return true;
        }

        if (type is "BigQueryClient" or "BigQueryTable" && method is "InsertRow" or "InsertRows" or "InsertRowsAsync")
        {
            DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Database, IntegrationDirection.Write,
                "google-bigquery", mapped, "table", ["database:google-bigquery", "bigquery:write"]);
            return true;
        }

        if (type == "BigQueryClient" && method is "ExecuteQuery" or "ExecuteQueryAsync")
        {
            // SQL is deliberately ignored and cannot safely establish whether the operation reads or writes.
            DataIntegrationSyntax.Add(context, invocation, IntegrationKind.Database, IntegrationDirection.Unknown,
                "google-bigquery", mapped, "project", ["database:google-bigquery", "bigquery:query"]);
            return true;
        }

        return false;
    }

    private static void MapConstructedClients(
        IntegrationDetectionContext context,
        VariableTypeMap types,
        Dictionary<string, ResourceEvidence> resources)
    {
        foreach (VariableDeclaratorSyntax variable in context.Root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (!types.TryGetType(variable, out string? type))
                continue;
            if (type is "BlobServiceClient" or "BlobContainerClient")
            {
                // Connection strings and SAS-bearing service URLs are never evidence.
                resources[variable.Identifier.ValueText] = ResourceEvidence.Empty;
            }
            else if (type == "BigQueryClient")
            {
                resources[variable.Identifier.ValueText] = CloudMessagingSyntax.ConstructionEvidence(
                    context, variable, ["projectId"], 0);
            }
        }
    }
}
