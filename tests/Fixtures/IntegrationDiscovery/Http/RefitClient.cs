namespace Fixture.Http;

internal static class RefitClient
{
    public static void Configure(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddRefitClient<ISerasaApi>()
            .ConfigureHttpClient(client =>
                client.BaseAddress = new Uri(
                    configuration["Serasa:BaseUrl"]));

        services.AddRefitClient<IInventoryApi>();
        RestService.For<IShippingApi>("https://shipping.example.test/v1");
    }
}
