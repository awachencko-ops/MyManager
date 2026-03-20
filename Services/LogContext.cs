using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Replica;

public static class LogContext
{
    private const string CorrelationKey = "correlation_id";
    private static readonly AsyncLocal<ScopeFrame?> CurrentFrame = new();
    private static readonly AsyncLocal<string?> CurrentCorrelationId = new();

    public static string? CorrelationId => CurrentCorrelationId.Value;

    public static string EnsureCorrelationId()
    {
        if (string.IsNullOrWhiteSpace(CurrentCorrelationId.Value))
            CurrentCorrelationId.Value = CreateCorrelationId();

        return CurrentCorrelationId.Value!;
    }

    public static IDisposable BeginCorrelationScope(string? correlationId = null)
    {
        var previous = CurrentCorrelationId.Value;
        var next = string.IsNullOrWhiteSpace(correlationId)
            ? (previous ?? CreateCorrelationId())
            : correlationId.Trim();
        CurrentCorrelationId.Value = next;
        return new RestoreScope(() => CurrentCorrelationId.Value = previous);
    }

    public static IDisposable BeginScope(params (string Key, string? Value)[] fields)
    {
        if (fields == null || fields.Length == 0)
            return NoopScope.Instance;

        var normalized = NormalizeFields(fields);
        if (normalized.Count == 0)
            return NoopScope.Instance;

        var previous = CurrentFrame.Value;
        CurrentFrame.Value = new ScopeFrame(previous, normalized);
        return new RestoreScope(() => CurrentFrame.Value = previous);
    }

    public static IReadOnlyDictionary<string, string> GetPropertiesSnapshot()
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var frame = CurrentFrame.Value; frame != null; frame = frame.Parent)
        {
            foreach (var field in frame.Fields)
            {
                if (!merged.ContainsKey(field.Key))
                    merged[field.Key] = field.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(CurrentCorrelationId.Value))
            merged[CorrelationKey] = CurrentCorrelationId.Value!;

        return merged;
    }

    private static Dictionary<string, string> NormalizeFields(IEnumerable<(string Key, string? Value)> fields)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawKey, rawValue) in fields)
        {
            var key = NormalizeKey(rawKey);
            var value = NormalizeValue(rawValue);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                continue;

            normalized[key] = value;
        }

        return normalized;
    }

    private static string NormalizeKey(string? rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
            return string.Empty;

        var trimmed = rawKey.Trim().ToLowerInvariant();
        var builder = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_' || ch == '-')
                builder.Append(ch);
            else
                builder.Append('_');
        }

        return builder.ToString().Trim('_');
    }

    private static string NormalizeValue(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return string.Empty;

        return rawValue.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static string CreateCorrelationId()
    {
        return $"replica-{Guid.NewGuid():N}";
    }

    private sealed class ScopeFrame
    {
        public ScopeFrame(ScopeFrame? parent, IReadOnlyDictionary<string, string> fields)
        {
            Parent = parent;
            Fields = fields;
        }

        public ScopeFrame? Parent { get; }
        public IReadOnlyDictionary<string, string> Fields { get; }
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly Action _restore;
        private bool _disposed;

        public RestoreScope(Action restore)
        {
            _restore = restore;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _restore();
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }
}
