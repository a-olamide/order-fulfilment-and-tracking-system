using Confluent.Kafka;
using Npgsql;
using Shared.Events;
using Shared.Messaging;
using System.Text.Json;

namespace FulfillmentWorker
{
    public sealed class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _cfg;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public Worker(ILogger<Worker> logger, IConfiguration cfg)
        {
            _logger = logger;
            _cfg = cfg;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var bootstrap = _cfg["Kafka:BootstrapServers"] ?? "localhost:9092";
            var groupId = _cfg["Kafka:GroupId"] ?? "fulfillment-worker-v1";
            var connStr = _cfg.GetConnectionString("Db") ?? throw new InvalidOperationException("Missing ConnectionStrings:Db");

            await EnsureIdempotencyTable(connStr, stoppingToken);

            var consumerConfig = new ConsumerConfig
            {
                BootstrapServers = bootstrap,
                GroupId = groupId,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            };

            var producerConfig = new ProducerConfig
            {
                BootstrapServers = bootstrap,
                Acks = Acks.All,
                EnableIdempotence = true
            };

            using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
            using var producer = new ProducerBuilder<string, string>(producerConfig).Build();

            consumer.Subscribe(Topics.PaymentsEventsV1);
            _logger.LogInformation("FulfillmentWorker started. Consuming {Topic} as {GroupId}", Topics.PaymentsEventsV1, groupId);

            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? cr = null;

                try
                {
                    cr = consumer.Consume(stoppingToken);
                    if (cr?.Message?.Value is null) continue;

                    // We don't know event type at compile time here; parse minimal envelope first
                    using var doc = JsonDocument.Parse(cr.Message.Value);
                    var eventType = doc.RootElement.GetProperty("eventType").GetString();

                    if (!string.Equals(eventType, nameof(PaymentAuthorizedV1), StringComparison.Ordinal))
                    {
                        consumer.Commit(cr);
                        continue; // ignore failures
                    }

                    var envelope = JsonSerializer.Deserialize<EventEnvelope<PaymentAuthorizedV1>>(cr.Message.Value, JsonOptions);
                    if (envelope is null) { consumer.Commit(cr); continue; }

                    using var scope = _logger.BeginScope(new Dictionary<string, object?>
                    {
                        ["CorrelationId"] = envelope.CorrelationId,
                        ["EventId"] = envelope.EventId,
                        ["OrderId"] = envelope.Payload.OrderId
                    });

                    if (await IsProcessed(connStr, envelope.EventId, stoppingToken))
                    {
                        _logger.LogInformation("Skipping already processed event.");
                        consumer.Commit(cr);
                        continue;
                    }

                    var eta = DateTimeOffset.UtcNow.AddDays(3);

                    var outEnv = new EventEnvelope<FulfillmentScheduledV1>(
                        EventId: Guid.NewGuid(),
                        EventType: nameof(FulfillmentScheduledV1),
                        EventVersion: 1,
                        OccurredAt: DateTimeOffset.UtcNow,
                        CorrelationId: envelope.CorrelationId,
                        CausationId: envelope.EventId,
                        Payload: new FulfillmentScheduledV1(envelope.Payload.OrderId, eta)
                    );

                    await producer.ProduceAsync(
                        Topics.FulfillmentEventsV1,
                        new Message<string, string>
                        {
                            Key = envelope.Payload.OrderId.ToString(),
                            Value = JsonSerializer.Serialize(outEnv, JsonOptions)
                        },
                        stoppingToken
                    );

                    _logger.LogInformation("Published FulfillmentScheduled.");

                    await MarkProcessed(connStr, envelope.EventId, stoppingToken);
                    consumer.Commit(cr);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error in FulfillmentWorker.");
                    await Task.Delay(500, stoppingToken);
                }
            }
        }

        private static async Task EnsureIdempotencyTable(string connStr, CancellationToken ct)
        {
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync(ct);

            const string sql = """
        create table if not exists processed_events_fulfillment(
          event_id uuid primary key,
          processed_at timestamptz not null
        );
        """;

            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static async Task<bool> IsProcessed(string connStr, Guid eventId, CancellationToken ct)
        {
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync(ct);

            const string sql = "select 1 from processed_events_fulfillment where event_id = @id limit 1;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", eventId);

            return await cmd.ExecuteScalarAsync(ct) is not null;
        }

        private static async Task MarkProcessed(string connStr, Guid eventId, CancellationToken ct)
        {
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync(ct);

            const string sql = "insert into processed_events_fulfillment(event_id, processed_at) values (@id, now()) on conflict do nothing;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", eventId);

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
