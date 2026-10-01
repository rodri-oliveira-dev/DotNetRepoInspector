namespace Fixture.Http;

internal static class MultipleClients
{
    public static void Configure(IServiceCollection services)
    {
        services.AddHttpClient("FirstExternalApi");
        services.AddHttpClient(
            "SecondExternalApi",
            client => client.BaseAddress = new Uri("https://second.example.test/path"));
    }
}
