using Shared.Messaging;

namespace OrderService.Infrastructure.Messaging
{
    public interface IEventProducer
    {
        Task ProduceAsync<T>(string topic, EventEnvelope<T> envelope, CancellationToken ct);
    }
}
