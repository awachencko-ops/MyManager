using System;

namespace Replica;

public sealed class OrderStatusTransitionService
{
    public StatusTransitionResult Apply(OrderData order, string status, string source, string reason)
    {
        if (order == null)
            return StatusTransitionResult.NotChanged();

        var normalizedSource = string.IsNullOrWhiteSpace(source)
            ? OrderStatusSourceNames.Ui
            : source.Trim();
        var normalizedReason = NormalizeFileSyncReason(normalizedSource, reason);
        var oldStatus = order.Status ?? string.Empty;
        var nextStatus = status ?? string.Empty;

        var unchanged = string.Equals(oldStatus, nextStatus, StringComparison.Ordinal)
            && string.Equals(order.LastStatusSource ?? string.Empty, normalizedSource, StringComparison.Ordinal)
            && string.Equals(order.LastStatusReason ?? string.Empty, normalizedReason, StringComparison.Ordinal);

        if (unchanged)
            return StatusTransitionResult.NotChanged();

        var statusAt = DateTime.Now;
        order.Status = nextStatus;
        order.LastStatusSource = normalizedSource;
        order.LastStatusReason = normalizedReason;
        order.LastStatusAt = statusAt;

        return new StatusTransitionResult(
            Changed: true,
            OldStatus: oldStatus,
            NewStatus: nextStatus,
            Source: normalizedSource,
            Reason: normalizedReason,
            StatusAt: statusAt);
    }

    public static string NormalizeFileSyncReason(string? source, string? reason)
    {
        if (!string.Equals(source, OrderStatusSourceNames.FileSync, StringComparison.OrdinalIgnoreCase))
            return reason ?? string.Empty;

        return (reason ?? string.Empty).Trim() switch
        {
            "stage-1" => "Найден исходный файл",
            "stage-2" => "Найден файл подготовки",
            "stage-3" => "Найден печатный файл",
            _ => reason ?? string.Empty
        };
    }
}

public sealed record StatusTransitionResult(
    bool Changed,
    string OldStatus,
    string NewStatus,
    string Source,
    string Reason,
    DateTime StatusAt)
{
    public static StatusTransitionResult NotChanged() => new(
        Changed: false,
        OldStatus: string.Empty,
        NewStatus: string.Empty,
        Source: string.Empty,
        Reason: string.Empty,
        StatusAt: DateTime.MinValue);
}
