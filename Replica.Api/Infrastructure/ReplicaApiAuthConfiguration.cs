using Microsoft.Extensions.Configuration;

namespace Replica.Api.Infrastructure;

public static class ReplicaApiAuthModes
{
    public const string Compatibility = "Compatibility";
    public const string Strict = "Strict";

    public static string Normalize(string? mode)
    {
        return string.Equals(mode?.Trim(), Strict, StringComparison.OrdinalIgnoreCase)
            ? Strict
            : Compatibility;
    }
}

public static class ReplicaApiAuthConfiguration
{
    public static string ResolveMode(IConfiguration configuration)
    {
        var configuredMode = configuration["ReplicaApi:Auth:Mode"]?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredMode))
            return ReplicaApiAuthModes.Normalize(configuredMode);

        var legacyStrictActorValidation = configuration.GetValue<bool?>("ReplicaApi:StrictActorValidation") ?? false;
        return legacyStrictActorValidation
            ? ReplicaApiAuthModes.Strict
            : ReplicaApiAuthModes.Compatibility;
    }

    public static bool IsStrict(string mode)
    {
        return string.Equals(ReplicaApiAuthModes.Normalize(mode), ReplicaApiAuthModes.Strict, StringComparison.Ordinal);
    }
}
