using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Replica.Api.Infrastructure;

public enum IdempotencyTelemetryOutcome
{
    Hit = 1,
    Miss = 2,
    Mismatch = 3
}

public static class ReplicaApiObservability
{
    private static readonly DateTime StartedAtUtc = DateTime.UtcNow;
    private static readonly double[] LatencyBucketUpperBoundsMs = [5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000];
    private static readonly long[] LatencyBuckets = new long[LatencyBucketUpperBoundsMs.Length + 1];
    private static readonly ConcurrentDictionary<string, CommandCounters> CommandCountersByName = new(StringComparer.OrdinalIgnoreCase);

    private static long _httpRequestsTotal;
    private static long _httpRequests5xx;
    private static long _httpRequests4xx;
    private static long _httpElapsedMsScaledTotal;

    private static long _writeCommandsTotal;
    private static long _writeSuccessTotal;
    private static long _writeConflictTotal;
    private static long _writeNotFoundTotal;
    private static long _writeBadRequestTotal;

    private static long _idempotencyHitsTotal;
    private static long _idempotencyMissesTotal;
    private static long _idempotencyMismatchesTotal;

    private static long _pushPublishedTotal;
    private static long _pushPublishFailuresTotal;
    private static long _pushOrderUpdatedPublished;
    private static long _pushOrderDeletedPublished;
    private static long _pushForceRefreshPublished;
    private static long _pushOrderUpdatedFailures;
    private static long _pushOrderDeletedFailures;
    private static long _pushForceRefreshFailures;

    public static void RecordHttpRequest(string method, string path, int statusCode, double elapsedMs)
    {
        Interlocked.Increment(ref _httpRequestsTotal);
        if (statusCode >= 500)
            Interlocked.Increment(ref _httpRequests5xx);
        else if (statusCode >= 400)
            Interlocked.Increment(ref _httpRequests4xx);

        var scaledElapsed = elapsedMs <= 0 ? 0L : (long)Math.Round(elapsedMs * 1000.0, MidpointRounding.AwayFromZero);
        Interlocked.Add(ref _httpElapsedMsScaledTotal, scaledElapsed);

        var bucketIndex = ResolveLatencyBucketIndex(elapsedMs);
        Interlocked.Increment(ref LatencyBuckets[bucketIndex]);

        if (string.IsNullOrWhiteSpace(path))
            return;

        var normalizedPath = NormalizeRequestPath(path);
        var key = $"{method?.Trim().ToUpperInvariant() ?? "UNKNOWN"} {normalizedPath}";
        var counters = CommandCountersByName.GetOrAdd(key, _ => new CommandCounters());
        Interlocked.Increment(ref counters.HttpCount);
    }

    public static void RecordWriteCommand(string commandName, string resultKind)
    {
        var normalizedCommand = NormalizeCommandName(commandName);
        var normalizedResultKind = NormalizeResultKind(resultKind);

        Interlocked.Increment(ref _writeCommandsTotal);
        switch (normalizedResultKind)
        {
            case "success":
                Interlocked.Increment(ref _writeSuccessTotal);
                break;
            case "conflict":
                Interlocked.Increment(ref _writeConflictTotal);
                break;
            case "not_found":
                Interlocked.Increment(ref _writeNotFoundTotal);
                break;
            default:
                Interlocked.Increment(ref _writeBadRequestTotal);
                break;
        }

        var counters = CommandCountersByName.GetOrAdd(normalizedCommand, _ => new CommandCounters());
        Interlocked.Increment(ref counters.WriteTotal);
        switch (normalizedResultKind)
        {
            case "success":
                Interlocked.Increment(ref counters.WriteSuccess);
                break;
            case "conflict":
                Interlocked.Increment(ref counters.WriteConflict);
                break;
            case "not_found":
                Interlocked.Increment(ref counters.WriteNotFound);
                break;
            default:
                Interlocked.Increment(ref counters.WriteBadRequest);
                break;
        }
    }

    public static void RecordIdempotency(string commandName, IdempotencyTelemetryOutcome outcome)
    {
        var normalizedCommand = NormalizeCommandName(commandName);
        var counters = CommandCountersByName.GetOrAdd(normalizedCommand, _ => new CommandCounters());

        switch (outcome)
        {
            case IdempotencyTelemetryOutcome.Hit:
                Interlocked.Increment(ref _idempotencyHitsTotal);
                Interlocked.Increment(ref counters.IdempotencyHit);
                return;
            case IdempotencyTelemetryOutcome.Mismatch:
                Interlocked.Increment(ref _idempotencyMismatchesTotal);
                Interlocked.Increment(ref counters.IdempotencyMismatch);
                return;
            default:
                Interlocked.Increment(ref _idempotencyMissesTotal);
                Interlocked.Increment(ref counters.IdempotencyMiss);
                return;
        }
    }

    public static void RecordPushPublished(string eventName)
    {
        Interlocked.Increment(ref _pushPublishedTotal);
        switch (NormalizePushEventName(eventName))
        {
            case "orderupdated":
                Interlocked.Increment(ref _pushOrderUpdatedPublished);
                return;
            case "orderdeleted":
                Interlocked.Increment(ref _pushOrderDeletedPublished);
                return;
            case "forcerefresh":
                Interlocked.Increment(ref _pushForceRefreshPublished);
                return;
        }
    }

    public static void RecordPushPublishFailure(string eventName)
    {
        Interlocked.Increment(ref _pushPublishFailuresTotal);
        switch (NormalizePushEventName(eventName))
        {
            case "orderupdated":
                Interlocked.Increment(ref _pushOrderUpdatedFailures);
                return;
            case "orderdeleted":
                Interlocked.Increment(ref _pushOrderDeletedFailures);
                return;
            case "forcerefresh":
                Interlocked.Increment(ref _pushForceRefreshFailures);
                return;
        }
    }

    public static ReplicaApiObservabilitySnapshot GetSnapshot()
    {
        var httpTotal = Interlocked.Read(ref _httpRequestsTotal);
        var http5xx = Interlocked.Read(ref _httpRequests5xx);
        var http4xx = Interlocked.Read(ref _httpRequests4xx);
        var elapsedScaled = Interlocked.Read(ref _httpElapsedMsScaledTotal);

        var writeTotal = Interlocked.Read(ref _writeCommandsTotal);
        var writeSuccess = Interlocked.Read(ref _writeSuccessTotal);
        var writeConflict = Interlocked.Read(ref _writeConflictTotal);
        var writeNotFound = Interlocked.Read(ref _writeNotFoundTotal);
        var writeBadRequest = Interlocked.Read(ref _writeBadRequestTotal);

        var idempotencyHits = Interlocked.Read(ref _idempotencyHitsTotal);
        var idempotencyMisses = Interlocked.Read(ref _idempotencyMissesTotal);
        var idempotencyMismatches = Interlocked.Read(ref _idempotencyMismatchesTotal);

        var pushPublishedTotal = Interlocked.Read(ref _pushPublishedTotal);
        var pushPublishFailuresTotal = Interlocked.Read(ref _pushPublishFailuresTotal);
        var pushOrderUpdatedPublished = Interlocked.Read(ref _pushOrderUpdatedPublished);
        var pushOrderDeletedPublished = Interlocked.Read(ref _pushOrderDeletedPublished);
        var pushForceRefreshPublished = Interlocked.Read(ref _pushForceRefreshPublished);
        var pushOrderUpdatedFailures = Interlocked.Read(ref _pushOrderUpdatedFailures);
        var pushOrderDeletedFailures = Interlocked.Read(ref _pushOrderDeletedFailures);
        var pushForceRefreshFailures = Interlocked.Read(ref _pushForceRefreshFailures);

        var latencyBuckets = new Dictionary<string, long>(StringComparer.Ordinal);
        for (var i = 0; i < LatencyBuckets.Length; i++)
        {
            var bucketName = i < LatencyBucketUpperBoundsMs.Length
                ? $"le_{LatencyBucketUpperBoundsMs[i]:0.###}ms"
                : "gt_5000ms";
            latencyBuckets[bucketName] = Interlocked.Read(ref LatencyBuckets[i]);
        }

        var p95 = CalculatePercentileMs(95);
        var p50 = CalculatePercentileMs(50);
        var avgMs = httpTotal > 0
            ? (elapsedScaled / 1000.0) / httpTotal
            : 0;

        var commandMetrics = CommandCountersByName
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                entry => entry.Key,
                entry => new ReplicaApiCommandMetricsSnapshot
                {
                    HttpCount = Interlocked.Read(ref entry.Value.HttpCount),
                    WriteTotal = Interlocked.Read(ref entry.Value.WriteTotal),
                    WriteSuccess = Interlocked.Read(ref entry.Value.WriteSuccess),
                    WriteConflict = Interlocked.Read(ref entry.Value.WriteConflict),
                    WriteNotFound = Interlocked.Read(ref entry.Value.WriteNotFound),
                    WriteBadRequest = Interlocked.Read(ref entry.Value.WriteBadRequest),
                    IdempotencyHit = Interlocked.Read(ref entry.Value.IdempotencyHit),
                    IdempotencyMiss = Interlocked.Read(ref entry.Value.IdempotencyMiss),
                    IdempotencyMismatch = Interlocked.Read(ref entry.Value.IdempotencyMismatch)
                },
                StringComparer.OrdinalIgnoreCase);

        return new ReplicaApiObservabilitySnapshot
        {
            StartedAtUtc = StartedAtUtc,
            UptimeSeconds = Math.Max(0, (long)(DateTime.UtcNow - StartedAtUtc).TotalSeconds),
            HttpRequestsTotal = httpTotal,
            HttpRequests4xx = http4xx,
            HttpRequests5xx = http5xx,
            HttpAvailabilityRatio = BuildRatio(httpTotal - http5xx, httpTotal),
            HttpLatencyAvgMs = avgMs,
            HttpLatencyP50Ms = p50,
            HttpLatencyP95Ms = p95,
            HttpLatencyBuckets = latencyBuckets,
            WriteCommandsTotal = writeTotal,
            WriteSuccess = writeSuccess,
            WriteConflict = writeConflict,
            WriteNotFound = writeNotFound,
            WriteBadRequest = writeBadRequest,
            WriteSuccessRatio = BuildRatio(writeSuccess, writeTotal),
            IdempotencyHits = idempotencyHits,
            IdempotencyMisses = idempotencyMisses,
            IdempotencyMismatches = idempotencyMismatches,
            IdempotencyHitRatio = BuildRatio(idempotencyHits, idempotencyHits + idempotencyMisses),
            PushPublishedTotal = pushPublishedTotal,
            PushPublishFailuresTotal = pushPublishFailuresTotal,
            PushOrderUpdatedPublished = pushOrderUpdatedPublished,
            PushOrderDeletedPublished = pushOrderDeletedPublished,
            PushForceRefreshPublished = pushForceRefreshPublished,
            PushOrderUpdatedFailures = pushOrderUpdatedFailures,
            PushOrderDeletedFailures = pushOrderDeletedFailures,
            PushForceRefreshFailures = pushForceRefreshFailures,
            PushPublishSuccessRatio = BuildRatio(pushPublishedTotal, pushPublishedTotal + pushPublishFailuresTotal),
            Commands = commandMetrics
        };
    }

    public static string NormalizeRequestPath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return "/";

        var segments = rawPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Trim())
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();
        if (segments.Length == 0)
            return "/";

        if (segments.Length >= 2
            && string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[1], "orders", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length == 2)
                return "/api/orders";
            if (segments.Length == 3)
                return "/api/orders/{id}";
            if (segments.Length == 4 && IsRunCommandSegment(segments[3]))
                return $"/api/orders/{{id}}/{segments[3].ToLowerInvariant()}";
            if (segments.Length == 4 && string.Equals(segments[3], "items", StringComparison.OrdinalIgnoreCase))
                return "/api/orders/{id}/items";
            if (segments.Length == 5 && string.Equals(segments[3], "items", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(segments[4], "reorder", StringComparison.OrdinalIgnoreCase))
                    return "/api/orders/{id}/items/reorder";
                return "/api/orders/{id}/items/{itemId}";
            }
        }

        if (segments.Length >= 2
            && string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[1], "users", StringComparison.OrdinalIgnoreCase))
        {
            return "/api/users";
        }

        var normalizedSegments = segments
            .Select(segment => LooksLikeDynamicSegment(segment) ? "{id}" : segment.ToLowerInvariant())
            .ToArray();
        return "/" + string.Join("/", normalizedSegments);
    }

    private static bool IsRunCommandSegment(string segment)
    {
        return string.Equals(segment, "run", StringComparison.OrdinalIgnoreCase)
               || string.Equals(segment, "stop", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeDynamicSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return false;

        if (Guid.TryParse(segment, out _))
            return true;

        if (segment.Length == 32 && segment.All(IsHexDigit))
            return true;

        if (segment.Length >= 3 && segment.All(char.IsDigit))
            return true;

        return false;
    }

    private static bool IsHexDigit(char value)
    {
        return (value >= '0' && value <= '9')
               || (value >= 'a' && value <= 'f')
               || (value >= 'A' && value <= 'F');
    }

    private static int ResolveLatencyBucketIndex(double elapsedMs)
    {
        for (var i = 0; i < LatencyBucketUpperBoundsMs.Length; i++)
        {
            if (elapsedMs <= LatencyBucketUpperBoundsMs[i])
                return i;
        }

        return LatencyBucketUpperBoundsMs.Length;
    }

    private static double CalculatePercentileMs(int percentile)
    {
        if (percentile <= 0)
            return 0;

        var total = Interlocked.Read(ref _httpRequestsTotal);
        if (total <= 0)
            return 0;

        var threshold = Math.Ceiling(total * (percentile / 100.0));
        long cumulative = 0;
        for (var i = 0; i < LatencyBuckets.Length; i++)
        {
            cumulative += Interlocked.Read(ref LatencyBuckets[i]);
            if (cumulative < threshold)
                continue;

            if (i < LatencyBucketUpperBoundsMs.Length)
                return LatencyBucketUpperBoundsMs[i];

            return 10000;
        }

        return LatencyBucketUpperBoundsMs[^1];
    }

    private static double BuildRatio(long numerator, long denominator)
    {
        if (denominator <= 0)
            return 1.0;

        var ratio = numerator / (double)denominator;
        if (ratio < 0)
            return 0;
        if (ratio > 1)
            return 1;
        return ratio;
    }

    private static string NormalizeCommandName(string commandName)
    {
        return string.IsNullOrWhiteSpace(commandName)
            ? "unknown"
            : commandName.Trim().ToLowerInvariant();
    }

    private static string NormalizeResultKind(string resultKind)
    {
        return string.IsNullOrWhiteSpace(resultKind)
            ? "bad_request"
            : resultKind.Trim().ToLowerInvariant();
    }

    private static string NormalizePushEventName(string eventName)
    {
        return string.IsNullOrWhiteSpace(eventName)
            ? string.Empty
            : eventName.Trim().ToLowerInvariant();
    }

    private sealed class CommandCounters
    {
        public long HttpCount;
        public long WriteTotal;
        public long WriteSuccess;
        public long WriteConflict;
        public long WriteNotFound;
        public long WriteBadRequest;
        public long IdempotencyHit;
        public long IdempotencyMiss;
        public long IdempotencyMismatch;
    }
}

public sealed class ReplicaApiObservabilitySnapshot
{
    public DateTime StartedAtUtc { get; set; }
    public long UptimeSeconds { get; set; }

    public long HttpRequestsTotal { get; set; }
    public long HttpRequests4xx { get; set; }
    public long HttpRequests5xx { get; set; }
    public double HttpAvailabilityRatio { get; set; }
    public double HttpLatencyAvgMs { get; set; }
    public double HttpLatencyP50Ms { get; set; }
    public double HttpLatencyP95Ms { get; set; }
    public Dictionary<string, long> HttpLatencyBuckets { get; set; } = new(StringComparer.Ordinal);

    public long WriteCommandsTotal { get; set; }
    public long WriteSuccess { get; set; }
    public long WriteConflict { get; set; }
    public long WriteNotFound { get; set; }
    public long WriteBadRequest { get; set; }
    public double WriteSuccessRatio { get; set; }

    public long IdempotencyHits { get; set; }
    public long IdempotencyMisses { get; set; }
    public long IdempotencyMismatches { get; set; }
    public double IdempotencyHitRatio { get; set; }

    public long PushPublishedTotal { get; set; }
    public long PushPublishFailuresTotal { get; set; }
    public long PushOrderUpdatedPublished { get; set; }
    public long PushOrderDeletedPublished { get; set; }
    public long PushForceRefreshPublished { get; set; }
    public long PushOrderUpdatedFailures { get; set; }
    public long PushOrderDeletedFailures { get; set; }
    public long PushForceRefreshFailures { get; set; }
    public double PushPublishSuccessRatio { get; set; }

    public Dictionary<string, ReplicaApiCommandMetricsSnapshot> Commands { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ReplicaApiCommandMetricsSnapshot
{
    public long HttpCount { get; set; }
    public long WriteTotal { get; set; }
    public long WriteSuccess { get; set; }
    public long WriteConflict { get; set; }
    public long WriteNotFound { get; set; }
    public long WriteBadRequest { get; set; }
    public long IdempotencyHit { get; set; }
    public long IdempotencyMiss { get; set; }
    public long IdempotencyMismatch { get; set; }
}
