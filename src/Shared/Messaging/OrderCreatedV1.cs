using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Messaging
{
    public sealed record OrderCreatedV1(
        Guid OrderId,
        string CustomerEmail,
        string ProductType,
        string Country,
        string? PaymentScenario
    );
}
