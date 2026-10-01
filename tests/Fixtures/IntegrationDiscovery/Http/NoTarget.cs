namespace Fixture.Http;

internal static class NoTarget
{
    public static void Configure(IServiceCollection services)
    {
        services.AddHttpClient();
    }
}
