namespace Fixture.CloudMessaging;

internal sealed class Negative
{
    public Task PublishAsync() => Task.CompletedTask;
    public Task SendMessageAsync() => Task.CompletedTask;
    public Task StartProcessingAsync() => Task.CompletedTask;
}
