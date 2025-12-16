using Confluent.Kafka;
using Npgsql;
using Shared.Events;
using Shared.Messaging;
using System.Text.Json;

namespace PaymentWorker
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
            var groupId = _cfg["Kafka:GroupId"] ?? "payment-worker-v1";
            var connStr = _cfg.GetConnectionString("Db") ?? throw new InvalidOperationException("Missing ConnectionStrings:Db");

            await EnsureIdempotencyTable(connStr, stoppingToken);

            var consumerConfig = new ConsumerConfig
            {
                BootstrapServers = bootstrap,
                GroupId = groupId,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false,
                EnablePartitionEof = false
            };

            var producerConfig = new ProducerConfig
            {
                BootstrapServers = bootstrap,
                Acks = Acks.All,
                EnableIdempotence = true
            };

            using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
            using var producer = new ProducerBuilder<string, string>(producerConfig).Build();

            consumer.Subscribe(Topics.OrdersEventsV1);
            _logger.LogInformation("PaymentWorker started. Consuming {Topic} as {GroupId}", Topics.OrdersEventsV1, groupId);

            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? cr = null;

                try
                {
                    cr = consumer.Consume(stoppingToken);
                    if (cr?.Message?.Value is null) continue;

                    // Parse event envelope
                    var envelope = JsonSerializer.Deserialize<EventEnvelope<OrderCreatedV1>>(cr.Message.Value, JsonOptions);
                    if (envelope is null) continue;

                    using var scope = _logger.BeginScope(new Dictionary<string, object?>
                    {
                        ["CorrelationId"] = envelope.CorrelationId,
                        ["EventId"] = envelope.EventId,
                        ["OrderId"] = envelope.Payload.OrderId
                    });

                    // Idempotency check
                    var already = await IsProcessed(connStr, envelope.EventId, stoppingToken);
                    if (already)
                    {
                        _logger.LogInformation("Skipping already processed event.");
                        consumer.Commit(cr);
                        continue;
                    }

                    var scenario = envelope.Payload.PaymentScenario?.Trim().ToLowerInvariant();
                    var ok = scenario == "success" || (scenario is null && envelope.Payload.OrderId.ToString("N")[^1] % 2 == 0);

                    if (ok)
                    {
                        var outEnv = new EventEnvelope<PaymentAuthorizedV1>(
                            EventId: Guid.NewGuid(),
                            EventType: nameof(PaymentAuthorizedV1),
                            EventVersion: 1,
                            OccurredAt: DateTimeOffset.UtcNow,
                            CorrelationId: envelope.CorrelationId,
                            CausationId: envelope.EventId,
                            Payload: new PaymentAuthorizedV1(envelope.Payload.OrderId)
                        );

                        await producer.ProduceAsync(
                            Topics.PaymentsEventsV1,
                            new Message<string, string>
                            {
                                Key = envelope.Payload.OrderId.ToString(),
                                Value = JsonSerializer.Serialize(outEnv, JsonOptions)
                            },
                            stoppingToken
                        );

                        _logger.LogInformation("Published PaymentAuthorized.");
                    }
                    else
                    {
                        var outEnv = new EventEnvelope<PaymentFailedV1>(
                            EventId: Guid.NewGuid(),
                            EventType: nameof(PaymentFailedV1),
                            EventVersion: 1,
                            OccurredAt: DateTimeOffset.UtcNow,
                            CorrelationId: envelope.CorrelationId,
                            CausationId: envelope.EventId,
                            Payload: new PaymentFailedV1(envelope.Payload.OrderId, "Card declined (demo)")
                        );

                        await producer.ProduceAsync(
                            Topics.PaymentsEventsV1,
                            new Message<string, string>
                            {
                                Key = envelope.Payload.OrderId.ToString(),
                                Value = JsonSerializer.Serialize(outEnv, JsonOptions)
                            },
                            stoppingToken
                        );

                        _logger.LogInformation("Published PaymentFailed.");
                    }

                    // Mark processed + commit offset
                    await MarkProcessed(connStr, envelope.EventId, stoppingToken);
                    consumer.Commit(cr);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka consume error: {Reason}", ex.Error.Reason);
                    if (cr is not null) consumer.Commit(cr); // avoid poison looping in demo
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error in PaymentWorker.");
                    await Task.Delay(500, stoppingToken);
                }
            }
        }

        private static async Task EnsureIdempotencyTable(string connStr, CancellationToken ct)
        {
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync(ct);

            const string sql = """
        create table if not exists processed_events(
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

            const string sql = "select 1 from processed_events where event_id = @id limit 1;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", eventId);

            var res = await cmd.ExecuteScalarAsync(ct);
            return res is not null;
        }

        private static async Task MarkProcessed(string connStr, Guid eventId, CancellationToken ct)
        {
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync(ct);

            const string sql = "insert into processed_events(event_id, processed_at) values (@id, now()) on conflict do nothing;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", eventId);

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
