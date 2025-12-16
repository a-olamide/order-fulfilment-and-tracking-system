using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Events
{
    public sealed record PaymentFailedV1(Guid OrderId, string Reason);

}
