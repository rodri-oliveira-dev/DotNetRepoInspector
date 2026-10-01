namespace Fixture.Messaging;

internal sealed class Secrets
{
    public void Configure()
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri("amqp://fixture-user:fixture-password@broker.example.test/vhost")
        };
        const string connectionString = "bootstrap.servers=broker;password=fixture-secret";
    }
}
