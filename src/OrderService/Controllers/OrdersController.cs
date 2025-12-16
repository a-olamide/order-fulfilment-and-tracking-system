using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain;
using OrderService.Infrastructure;
using OrderService.Infrastructure.Messaging;
using Shared.Messaging;

namespace OrderService.Controllers
{
    [ApiController]
    [Route("orders")]
    public sealed class OrdersController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IEventProducer _producer;
        private readonly ILogger<OrdersController> _logger;

        public OrdersController(AppDbContext db, IEventProducer producer, ILogger<OrdersController> logger)
        {
            _db = db;
            _producer = producer;
            _logger = logger;
        }

        public sealed record CreateOrderRequest(string CustomerEmail, string ProductType, string Country, string? PaymentScenario);

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateOrderRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.CustomerEmail)) return BadRequest("CustomerEmail required");
            if (string.IsNullOrWhiteSpace(req.ProductType)) return BadRequest("ProductType required");
            if (string.IsNullOrWhiteSpace(req.Country) || req.Country.Length != 2) return BadRequest("Country must be 2-letter code");

            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerEmail = req.CustomerEmail.Trim(),
                ProductType = req.ProductType.Trim(),
                Country = req.Country.Trim().ToUpperInvariant(),
                Status = OrderStatus.Created,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await _db.Orders.AddAsync(order, ct);
            await _db.SaveChangesAsync(ct);

            var correlationId = Guid.NewGuid();

            var payload = new OrderCreatedV1(order.Id, order.CustomerEmail, order.ProductType, order.Country, req.PaymentScenario);
            var envelope = new EventEnvelope<OrderCreatedV1>(
                EventId: Guid.NewGuid(),
                EventType: nameof(OrderCreatedV1),
                EventVersion: 1,
                OccurredAt: DateTimeOffset.UtcNow,
                CorrelationId: correlationId,
                CausationId: null,
                Payload: payload
            );

            _logger.LogInformation("Order created {OrderId} correlation {CorrelationId}", order.Id, correlationId);

            await _producer.ProduceAsync(Topics.OrdersEventsV1, envelope, ct);

            return CreatedAtAction(nameof(GetById), new { id = order.Id }, new { order.Id, correlationId });
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
        {
            var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
            if (order is null) return NotFound();

            return Ok(new
            {
                order.Id,
                order.CustomerEmail,
                order.ProductType,
                order.Country,
                Status = order.Status.ToString(),
                order.CreatedAt,
                order.UpdatedAt
            });
        }
    }
}
