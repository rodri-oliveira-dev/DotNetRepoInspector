namespace Fixture.Messaging;

internal sealed class MassTransit
{
    public async Task PublishAsync(
        IPublishEndpoint publisher,
        ISendEndpoint sender,
        ISendEndpointProvider provider,
        IConfiguration configuration)
    {
        await publisher.Publish<OrderCreated>(new OrderCreated());
        await sender.Send<OrderCommand>(new OrderCommand());

        var paymentEndpoint = await provider.GetSendEndpoint(new Uri("queue:payments"));
        await paymentEndpoint.Send<PaymentRequested>(new PaymentRequested());

        ConfigureBus(cfg =>
        {
            cfg.ReceiveEndpoint(
                "payment-approved",
                endpoint => endpoint.Consumer<PaymentApprovedConsumer>());
            cfg.ReceiveEndpoint(
                configuration["Messaging:AuditQueue"],
                endpoint => endpoint.Consumer<AuditConsumer>());
        });
    }
}
