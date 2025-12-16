using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Messaging
{
    public static class Topics
    {
        public const string OrdersEventsV1 = "orders.events.v1";
        public const string PaymentsEventsV1 = "payments.events.v1";
        public const string FulfillmentEventsV1 = "fulfillment.events.v1";
    }
}
