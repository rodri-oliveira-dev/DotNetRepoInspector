namespace Fixture.Messaging;

internal sealed class Negative
{
    private const string Example = "channel.BasicPublish(exchange: \"false\")";

    public Task RunAsync(EmailSender sender, MetricsPublisher publisher)
    {
        sender.SendAsync("not-a-message");
        return publisher.PublishAsync("not-a-bus");
    }
}
