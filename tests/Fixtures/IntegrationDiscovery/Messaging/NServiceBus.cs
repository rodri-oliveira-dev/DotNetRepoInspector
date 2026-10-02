namespace Fixture.Messaging;

internal sealed class NServiceBusSender
{
    public async Task SendAsync(IMessageSession session)
    {
        await session.Send<OrderCommand>("sales", new OrderCommand());
        await session.Publish<OrderCreated>(new OrderCreated());
        routing.RouteToEndpoint(typeof(PaymentRequested), "payments");
    }
}

internal sealed class OrderCreatedHandler : IHandleMessages<OrderCreated>
{
    public Task Handle(OrderCreated message, IMessageHandlerContext context) =>
        Task.CompletedTask;
}
