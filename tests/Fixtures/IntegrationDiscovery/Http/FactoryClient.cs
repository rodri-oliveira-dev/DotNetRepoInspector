namespace Fixture.Http;

internal sealed class FactoryClient(IHttpClientFactory factory)
{
    public Task CallAsync()
    {
        HttpClient client = factory.CreateClient("Catalog");
        return client.DeleteAsync("items/42");
    }
}
