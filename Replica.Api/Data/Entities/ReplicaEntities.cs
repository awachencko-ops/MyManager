using System;
using System.Collections.Generic;

namespace Replica.Api.Data.Entities;

public sealed class OrderRecord
{
    public string InternalId { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ArrivalDate { get; set; }
    public DateTime OrderDate { get; set; }
    public int StartMode { get; set; }
    public int TopologyMarker { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public long Version { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<OrderItemRecord> Items { get; set; } = new();
}

public sealed class OrderItemRecord
{
    public string ItemId { get; set; } = string.Empty;
    public string OrderInternalId { get; set; } = string.Empty;
    public long SequenceNo { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public long Version { get; set; }
    public DateTime UpdatedAt { get; set; }

    public OrderRecord? Order { get; set; }
}

public sealed class OrderEventRecord
{
    public long EventId { get; set; }
    public string OrderInternalId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string EventSource { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public sealed class UserRecord
{
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class StorageMetaRecord
{
    public string MetaKey { get; set; } = string.Empty;
    public string MetaValue { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public sealed class OrderRunLockRecord
{
    public string OrderInternalId { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string LeaseToken { get; set; } = string.Empty;
    public string LeaseOwner { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class OrderRunIdempotencyRecord
{
    public string OrderInternalId { get; set; } = string.Empty;
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string ResultKind { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public long CurrentVersion { get; set; }
    public string ResponseOrderJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class OrderWriteIdempotencyRecord
{
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string OrderInternalId { get; set; } = string.Empty;
    public string ResultKind { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public long CurrentVersion { get; set; }
    public string ResponseOrderJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
