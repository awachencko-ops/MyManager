using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Replica.Api.Infrastructure;
using Xunit;

namespace Replica.VerifyTests;

public sealed class ReplicaApiAuthConfigurationTests
{
    [Fact]
    public void ResolveMode_DefaultsToCompatibility()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var mode = ReplicaApiAuthConfiguration.ResolveMode(configuration);

        Assert.Equal(ReplicaApiAuthModes.Compatibility, mode);
    }

    [Fact]
    public void ResolveMode_UsesLegacyStrictFlagWhenAuthModeMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReplicaApi:StrictActorValidation"] = "true"
            })
            .Build();

        var mode = ReplicaApiAuthConfiguration.ResolveMode(configuration);

        Assert.Equal(ReplicaApiAuthModes.Strict, mode);
    }

    [Fact]
    public void ResolveMode_UsesExplicitAuthMode()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReplicaApi:Auth:Mode"] = "Strict",
                ["ReplicaApi:StrictActorValidation"] = "false"
            })
            .Build();

        var mode = ReplicaApiAuthConfiguration.ResolveMode(configuration);

        Assert.Equal(ReplicaApiAuthModes.Strict, mode);
    }

    [Fact]
    public void ResolveMode_ExplicitCompatibilityOverridesLegacyStrictFlag()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReplicaApi:Auth:Mode"] = "Compatibility",
                ["ReplicaApi:StrictActorValidation"] = "true"
            })
            .Build();

        var mode = ReplicaApiAuthConfiguration.ResolveMode(configuration);

        Assert.Equal(ReplicaApiAuthModes.Compatibility, mode);
    }
}
