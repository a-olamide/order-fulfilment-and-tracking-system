using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Events
{
    public sealed record FulfillmentScheduledV1(Guid OrderId, DateTimeOffset EtaUtc);

}
