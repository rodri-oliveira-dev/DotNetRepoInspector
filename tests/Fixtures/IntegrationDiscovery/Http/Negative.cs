namespace Fixture.Http;

internal sealed class Negative
{
    private const string CommentLikeText = "services.AddHttpClient(\"FalsePositive\")";

    public Task CallAsync(CustomClient custom)
    {
        // custom.GetAsync("https://comment.example.test");
        return custom.GetAsync("https://custom.example.test");
    }

    public Task CallHttpAsync(HttpClient client, Dictionary<string, string> routes) =>
        client.GetAsync(routes["CustomRoute"]);
}
