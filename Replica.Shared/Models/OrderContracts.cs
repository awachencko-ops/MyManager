using System;
using System.Collections.Generic;

namespace Replica.Shared.Models
{
    public enum SharedOrderStartMode
    {
        Unknown = 0,
        Extended = 1,
        Simple = 2
    }

    public enum SharedOrderTopologyMarker
    {
        Unknown = 0,
        SingleOrder = 1,
        MultiOrder = 2
    }

    public sealed class SharedOrderItem
    {
        public string ItemId { get; set; } = Guid.NewGuid().ToString("N");
        public long Version { get; set; }
        public long SequenceNo { get; set; }
        public string ClientFileLabel { get; set; } = string.Empty;
        public string Variant { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public long? SourceFileSizeBytes { get; set; }
        public string SourceFileHash { get; set; } = string.Empty;
        public string PreparedPath { get; set; } = string.Empty;
        public long? PreparedFileSizeBytes { get; set; }
        public string PreparedFileHash { get; set; } = string.Empty;
        public string PrintPath { get; set; } = string.Empty;
        public long? PrintFileSizeBytes { get; set; }
        public string PrintFileHash { get; set; } = string.Empty;
        public string FileStatus { get; set; } = string.Empty;
        public string LastReason { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public string PitStopAction { get; set; } = "-";
        public string ImposingAction { get; set; } = "-";
    }

    public sealed class SharedOrder
    {
        public string InternalId { get; set; } = Guid.NewGuid().ToString("N");
        public string OrderNumber { get; set; } = string.Empty;
        public string Keyword { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string CreatedById { get; set; } = string.Empty;
        public string CreatedByUser { get; set; } = string.Empty;
        public DateTime ArrivalDate { get; set; } = DateTime.Now;
        public DateTime ManagerOrderDate { get; set; } = DateTime.Today;
        public string FolderName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public SharedOrderStartMode StartMode { get; set; } = SharedOrderStartMode.Unknown;
        public SharedOrderTopologyMarker TopologyMarker { get; set; } = SharedOrderTopologyMarker.Unknown;
        public long Version { get; set; }
        public string LastStatusReason { get; set; } = string.Empty;
        public string LastStatusSource { get; set; } = string.Empty;
        public DateTime LastStatusAt { get; set; } = DateTime.Now;
        public string PitStopAction { get; set; } = "-";
        public string ImposingAction { get; set; } = "-";
        public List<SharedOrderItem> Items { get; set; } = new();
    }

    public sealed class SharedOrderEvent
    {
        public long EventId { get; set; }
        public string OrderInternalId { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string EventSource { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = "{}";
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public sealed class SharedUser
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = "Operator";
        public bool IsActive { get; set; } = true;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
