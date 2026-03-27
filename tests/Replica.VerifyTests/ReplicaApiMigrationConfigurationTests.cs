using Microsoft.Extensions.Configuration;
using Replica.Api.Infrastructure;
using Xunit;

namespace Replica.VerifyTests;

public sealed class ReplicaApiMigrationConfigurationTests
{
    [Fact]
    public void Resolve_WhenSectionMissing_ReturnsSafeDefaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        var options = ReplicaApiMigrationConfiguration.Resolve(configuration);

        Assert.False(options.DualWriteEnabled);
        Assert.Equal(ReplicaApiMigrationShadowWriteFailurePolicies.WarnOnly, options.ShadowWriteFailurePolicy);
        Assert.Equal(ReplicaApiMigrationOptions.DefaultShadowHistoryFilePath, options.ShadowHistoryFilePath);
    }

    [Fact]
    public void Resolve_WhenFailPolicyConfigured_NormalizesPolicyAndPath()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReplicaApi:Migration:DualWriteEnabled"] = "true",
                ["ReplicaApi:Migration:ShadowWriteFailurePolicy"] = "FailCommand",
                ["ReplicaApi:Migration:ShadowHistoryFilePath"] = "  AppData/custom-shadow.json  "
            })
            .Build();

        var options = ReplicaApiMigrationConfiguration.Resolve(configuration);

        Assert.True(options.DualWriteEnabled);
        Assert.Equal(ReplicaApiMigrationShadowWriteFailurePolicies.FailCommand, options.ShadowWriteFailurePolicy);
        Assert.Equal("AppData/custom-shadow.json", options.ShadowHistoryFilePath);
    }

    [Fact]
    public void ResolveShadowHistoryFilePath_WhenRelative_ReturnsAbsolutePathFromAppBase()
    {
        var options = new ReplicaApiMigrationOptions
        {
            ShadowHistoryFilePath = "AppData/history.shadow.json"
        };

        var resolvedPath = ReplicaApiMigrationConfiguration.ResolveShadowHistoryFilePath(options);

        Assert.True(Path.IsPathRooted(resolvedPath));
        Assert.EndsWith(Path.Combine("AppData", "history.shadow.json"), resolvedPath, StringComparison.OrdinalIgnoreCase);
    }
}
