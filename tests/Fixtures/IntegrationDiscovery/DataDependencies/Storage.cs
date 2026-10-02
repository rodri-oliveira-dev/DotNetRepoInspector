namespace Fixture.DataDependencies;

internal sealed class Storage
{
    public async Task RunAsync(
        IAmazonS3 s3,
        StorageClient gcs,
        BlobServiceClient blobs,
        IConfiguration configuration)
    {
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "loan-documents",
            ContentBody = "PRIVATE-S3-PAYLOAD"
        });
        await s3.GetObjectAsync(new GetObjectRequest
        {
            BucketName = configuration["Storage:S3:Bucket"],
            Key = "private-object-key"
        });

        await gcs.UploadObjectAsync(
            configuration["Storage:Gcs:Bucket"],
            "private-object-name",
            "application/json",
            PrivatePayload());
        await gcs.DownloadObjectAsync("loan-archive", "private-object-name", PrivateStream());

        BlobContainerClient container = blobs.GetBlobContainerClient(
            configuration["Storage:Azure:Container"]);
        await container.UploadBlobAsync("private-blob-name", PrivateStream());

        BigQueryClient bigQuery = BigQueryClient.Create(configuration["BigQuery:Project"]);
        BigQueryDataset dataset = bigQuery.GetDataset("lending");
        BigQueryTable table = dataset.GetTable("applications");
        await bigQuery.ExecuteQueryAsync("PRIVATE BIGQUERY SQL", parameters: null);
        await table.InsertRowsAsync(new BigQueryInsertRow { ["payload"] = "PRIVATE-BIGQUERY-PAYLOAD" });
    }
}
