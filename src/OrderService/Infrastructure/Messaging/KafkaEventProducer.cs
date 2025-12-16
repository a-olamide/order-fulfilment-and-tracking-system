using Confluent.Kafka;
using Shared.Messaging;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace OrderService.Infrastructure.Messaging
{
    public sealed class KafkaEventProducer : IEventProducer, IDisposable
    {
        private readonly IProducer<string, string> _producer;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public KafkaEventProducer(IConfiguration cfg)
        {
            var bootstrapServers = cfg["Kafka:BootstrapServers"] ?? "localhost:9092";

            var config = new ProducerConfig
            {
                BootstrapServers = bootstrapServers,
                Acks = Acks.All,
                EnableIdempotence = true,
                MessageSendMaxRetries = 3
            };

            _producer = new ProducerBuilder<string, string>(config).Build();
        }

        public async Task ProduceAsync<T>(string topic, EventEnvelope<T> envelope, CancellationToken ct)
        {
            var key = GetKey(envelope);
            var json = JsonSerializer.Serialize(envelope, JsonOptions);

            var msg = new Message<string, string>
            {
                Key = key,
                Value = json,
                Headers = new Headers
            {
                { "eventType", System.Text.Encoding.UTF8.GetBytes(envelope.EventType) },
                { "correlationId", System.Text.Encoding.UTF8.GetBytes(envelope.CorrelationId.ToString()) }
            }
            };

            // ct isn't directly supported by ProduceAsync; we honor it by throwing early
            ct.ThrowIfCancellationRequested();
            await _producer.ProduceAsync(topic, msg);
        }

        private static string GetKey<T>(EventEnvelope<T> envelope)
        {
            // if payload has OrderId, use it. Otherwise use eventId.
            var prop = envelope.Payload?.GetType().GetProperty("OrderId");
            var val = prop?.GetValue(envelope.Payload);
            return (val as Guid?)?.ToString() ?? envelope.EventId.ToString();
        }

        public void Dispose() => _producer.Dispose();
    }
}
