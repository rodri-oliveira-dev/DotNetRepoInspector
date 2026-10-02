namespace Fixture.Http;

internal static class NamedClient
{
    public static void Configure(IServiceCollection services)
    {
        services.AddHttpClient("Antifraud");
    }
}
