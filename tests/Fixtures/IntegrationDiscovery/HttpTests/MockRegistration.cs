namespace Fixture.HttpTests;

internal static class MockRegistration
{
    public static void Configure(IServiceCollection services)
    {
        services.AddHttpClient("MockPayments");
    }
}
