using System.ComponentModel.DataAnnotations;

namespace OrderService.Domain
{
    public sealed class Order
    {
        [Key]
        public Guid Id { get; set; }

        [Required, MaxLength(320)]
        public string CustomerEmail { get; set; } = string.Empty;

        [Required, MaxLength(64)]
        public string ProductType { get; set; } = string.Empty;

        [Required, MaxLength(2)]
        public string Country { get; set; } = string.Empty;

        public OrderStatus Status { get; set; } = OrderStatus.Created;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
