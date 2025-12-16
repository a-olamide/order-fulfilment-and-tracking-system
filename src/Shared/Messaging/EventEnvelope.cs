using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Messaging
{

    public sealed record EventEnvelope<T>(
        Guid EventId,
        string EventType,
        int EventVersion,
        DateTimeOffset OccurredAt,
        Guid CorrelationId,
        Guid? CausationId,
        T Payload
    );
}
