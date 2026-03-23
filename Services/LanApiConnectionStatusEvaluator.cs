namespace Replica;

internal static class LanApiConnectionStatusEvaluator
{
    public static bool IsDisconnected(bool apiReachable, int consecutiveFailures, int failureThreshold)
    {
        return !apiReachable && consecutiveFailures >= NormalizeFailureThreshold(failureThreshold);
    }

    public static bool IsTransientFailure(bool apiReachable, int consecutiveFailures, int failureThreshold)
    {
        var normalizedThreshold = NormalizeFailureThreshold(failureThreshold);
        return !apiReachable
            && consecutiveFailures > 0
            && consecutiveFailures < normalizedThreshold;
    }

    public static bool IsDegraded(
        bool apiReachable,
        bool isReady,
        bool isDegraded,
        bool sloHealthy,
        DependencyHealthLevel dependencyHealthLevel)
    {
        if (!apiReachable)
            return false;

        return !isReady
            || isDegraded
            || !sloHealthy
            || dependencyHealthLevel != DependencyHealthLevel.Healthy;
    }

    private static int NormalizeFailureThreshold(int failureThreshold)
    {
        return failureThreshold <= 0 ? 1 : failureThreshold;
    }
}
