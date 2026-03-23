using System;
using System.Collections.Generic;
using Replica.Shared.Models;

namespace Replica.Api.Contracts;

public sealed class CreateOrderRequest
{
    public string OrderNumber { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string CreatedById { get; set; } = string.Empty;
    public string CreatedByUser { get; set; } = string.Empty;
    public string Status { get; set; } = "Waiting";
    public string Keyword { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public SharedOrderStartMode StartMode { get; set; } = SharedOrderStartMode.Unknown;
    public SharedOrderTopologyMarker TopologyMarker { get; set; } = SharedOrderTopologyMarker.Unknown;
    public string PitStopAction { get; set; } = "-";
    public string ImposingAction { get; set; } = "-";
    public DateTime? ArrivalDate { get; set; }
    public DateTime? ManagerOrderDate { get; set; }
    public List<SharedOrderItem>? Items { get; set; }
}

public sealed class UpdateOrderRequest
{
    public long ExpectedVersion { get; set; }
    public string? OrderNumber { get; set; }
    public DateTime? ManagerOrderDate { get; set; }
    public string? UserName { get; set; }
    public string? Status { get; set; }
    public string? Keyword { get; set; }
    public string? FolderName { get; set; }
    public string? PitStopAction { get; set; }
    public string? ImposingAction { get; set; }
}

public sealed class AddOrderItemRequest
{
    public long ExpectedOrderVersion { get; set; }
    public SharedOrderItem Item { get; set; } = new();
}

public sealed class UpdateOrderItemRequest
{
    public long ExpectedOrderVersion { get; set; }
    public long ExpectedItemVersion { get; set; }
    public string? ClientFileLabel { get; set; }
    public string? Variant { get; set; }
    public string? FileStatus { get; set; }
    public string? LastReason { get; set; }
    public string? SourcePath { get; set; }
    public string? PreparedPath { get; set; }
    public string? PrintPath { get; set; }
    public string? SourceFileHash { get; set; }
    public string? PreparedFileHash { get; set; }
    public string? PrintFileHash { get; set; }
    public string? PitStopAction { get; set; }
    public string? ImposingAction { get; set; }
}

public sealed class DeleteOrderItemRequest
{
    public long ExpectedOrderVersion { get; set; }
    public long ExpectedItemVersion { get; set; }
}

public sealed class ReorderOrderItemsRequest
{
    public long ExpectedOrderVersion { get; set; }
    public List<string> OrderedItemIds { get; set; } = new();
}

public sealed class RunOrderRequest
{
    public long ExpectedOrderVersion { get; set; }
}

public sealed class StopOrderRequest
{
    public long ExpectedOrderVersion { get; set; }
}
