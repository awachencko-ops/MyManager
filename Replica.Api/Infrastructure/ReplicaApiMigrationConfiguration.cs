using Microsoft.Extensions.Configuration;

namespace Replica.Api.Infrastructure;

public static class ReplicaApiMigrationShadowWriteFailurePolicies
{
    public const string WarnOnly = "WarnOnly";
    public const string FailCommand = "FailCommand";

    public static string Normalize(string? value)
    {
        return string.Equals(value?.Trim(), FailCommand, StringComparison.OrdinalIgnoreCase)
            ? FailCommand
            : WarnOnly;
    }

    public static bool IsFailCommand(string? value)
    {
        return string.Equals(Normalize(value), FailCommand, StringComparison.Ordinal);
    }
}

public sealed class ReplicaApiMigrationOptions
{
    public const string DefaultShadowHistoryFilePath = "AppData/history.shadow.json";

    public bool DualWriteEnabled { get; set; }
    public string ShadowWriteFailurePolicy { get; set; } = ReplicaApiMigrationShadowWriteFailurePolicies.WarnOnly;
    public string ShadowHistoryFilePath { get; set; } = DefaultShadowHistoryFilePath;
}

public static class ReplicaApiMigrationConfiguration
{
    public static ReplicaApiMigrationOptions Resolve(IConfiguration configuration)
    {
        var options = new ReplicaApiMigrationOptions();
        configuration
            .GetSection("ReplicaApi:Migration")
            .Bind(options);
        return Normalize(options);
    }

    public static ReplicaApiMigrationOptions Normalize(ReplicaApiMigrationOptions? options)
    {
        options ??= new ReplicaApiMigrationOptions();

        return new ReplicaApiMigrationOptions
        {
            DualWriteEnabled = options.DualWriteEnabled,
            ShadowWriteFailurePolicy = ReplicaApiMigrationShadowWriteFailurePolicies.Normalize(options.ShadowWriteFailurePolicy),
            ShadowHistoryFilePath = string.IsNullOrWhiteSpace(options.ShadowHistoryFilePath)
                ? ReplicaApiMigrationOptions.DefaultShadowHistoryFilePath
                : options.ShadowHistoryFilePath.Trim()
        };
    }

    public static string ResolveShadowHistoryFilePath(ReplicaApiMigrationOptions options)
    {
        var normalized = Normalize(options);
        var configuredPath = normalized.ShadowHistoryFilePath;
        if (Path.IsPathRooted(configuredPath))
            return configuredPath;

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
    }
}
