namespace Fixture.Http;

internal sealed class DirectClient
{
    public async Task CallAsync(IConfiguration configuration)
    {
        HttpClient client = new HttpClient();
        client.BaseAddress = new Uri("https://direct.example.test/v1?ignored=true");
        await client.GetAsync("relative/path");

        var configured = new HttpClient
        {
            BaseAddress = new Uri(configuration["Direct:BaseUrl"])
        };
        await configured.PostAsync("relative/path", content: null);
    }
}
