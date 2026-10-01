namespace Fixture.Http;

internal static class TypedClient
{
    public static void Configure(IServiceCollection services)
    {
        services.AddHttpClient<IWeatherClient>();
        services.AddHttpClient<IPaymentsApi, PaymentsApi>();
    }
}
