internal sealed class ScopedReceivers
{
    public void Store()
    {
        AmazonS3Client client = new();
        client.PutObjectAsync(new PutObjectRequest { BucketName = "scoped-real" });
    }

    public void UseUnrelatedClient()
    {
        HttpClient client = new();
        client.PutObjectAsync(new { BucketName = "not-a-bucket" });
    }

    public void Lookalike()
    {
        HttpClient service = new();
        service.PutObjectAsync(new { BucketName = "scoped-lookalike" });
    }

    public void CreateStorageClient()
    {
        AmazonS3Client service = new();
    }
}
