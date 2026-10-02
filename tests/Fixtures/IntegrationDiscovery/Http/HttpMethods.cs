namespace Fixture.Http;

internal sealed class HttpMethods
{
    public async Task CallAsync(HttpClient client, IConfiguration configuration)
    {
        await client.PutAsync("https://put.example.test/resource", content: null);
        await client.PatchAsync("https://patch.example.test/resource", content: null);
        await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Get,
            configuration["Send:RequestUri"]));
        await new HttpClient().GetAsync("https://new-client.example.test/resource");
    }
}
