using System;

namespace Replica.Api.Infrastructure;

public static class ReplicaApiWorkflowStatusNormalizer
{
    public const string Processed = "Обработано";
    public const string Archived = "В архиве";
    public const string Building = "Сборка";
    public const string Processing = "Обрабатывается";
    public const string Waiting = "Ожидание";
    public const string Cancelled = "Отменено";
    public const string Error = "Ошибка";
    public const string Completed = "Завершено";

    private static readonly string[] CanonicalStatuses =
    {
        Processed,
        Archived,
        Building,
        Processing,
        Waiting,
        Cancelled,
        Error,
        Completed
    };

    public static string NormalizeOrDefault(string? rawStatus)
    {
        return Normalize(rawStatus) ?? Waiting;
    }

    public static string? Normalize(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
            return null;

        var value = rawStatus.Trim();
        foreach (var status in CanonicalStatuses)
        {
            if (string.Equals(value, status, StringComparison.OrdinalIgnoreCase))
                return status;

            if (value.Contains(status, StringComparison.OrdinalIgnoreCase))
                return status;
        }

        if (ContainsAny(value, "архив", "archiv"))
            return Archived;

        if (ContainsAny(value, "отмен", "cancel"))
            return Cancelled;

        if (ContainsAny(value, "ошиб", "error", "fail"))
            return Error;

        if (ContainsAny(value, "сборк", "building", "imposing", "pitstop"))
            return Building;

        if (ContainsAny(value, "обрабатыва", "processing", "running", "in work", "run"))
            return Processing;

        if (ContainsAny(value, "ожид", "waiting"))
            return Waiting;

        if (ContainsAny(value, "групп", "group", "папк", "folder"))
            return Waiting;

        if (ContainsAny(value, "обработано", "processed"))
            return Processed;

        if (ContainsAny(value, "готово", "заверш", "напечат", "complete", "completed", "ready", "printed"))
            return Completed;

        return null;
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
