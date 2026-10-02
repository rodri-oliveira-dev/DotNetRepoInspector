namespace Fixture.Http;

internal sealed class SecretUri
{
    public Task CallAsync(HttpClient client) =>
        client.GetAsync("https://user:fixture-password@secure.example.test/private?apiKey=fixture-secret");
}
