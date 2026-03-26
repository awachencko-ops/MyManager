using Microsoft.Extensions.Configuration;
using Replica.Api.Infrastructure;
using Replica.Api.Services;

namespace Replica.VerifyTests;

public sealed class ReplicaApiBootstrapUsersTests
{
    [Fact]
    public void GetDefaultUsers_IncludesAndrewAsActiveAdmin()
    {
        var users = ReplicaApiBootstrapUsers.GetDefaultUsers();

        var andrew = Assert.Single(users, user =>
            string.Equals(user.Name, "Andrew", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ReplicaApiRoles.Admin, andrew.Role);
        Assert.True(andrew.IsActive);
    }

    [Fact]
    public void ResolveBootstrapRequests_UsesConfiguredUsers_WhenPresent()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReplicaApi:Auth:BootstrapUsers:0:Name"] = "qa-admin",
                ["ReplicaApi:Auth:BootstrapUsers:0:Role"] = "Admin",
                ["ReplicaApi:Auth:BootstrapUsers:0:IsActive"] = "true",
                ["ReplicaApi:Auth:BootstrapUsers:1:Name"] = "qa-operator",
                ["ReplicaApi:Auth:BootstrapUsers:1:Role"] = "Operator",
                ["ReplicaApi:Auth:BootstrapUsers:1:IsActive"] = "false"
            })
            .Build();

        var requests = ReplicaApiBootstrapUsers.ResolveBootstrapRequests(configuration);

        Assert.Collection(
            requests,
            request =>
            {
                Assert.Equal("qa-admin", request.Name);
                Assert.Equal(ReplicaApiRoles.Admin, request.Role);
                Assert.True(request.IsActive);
            },
            request =>
            {
                Assert.Equal("qa-operator", request.Name);
                Assert.Equal(ReplicaApiRoles.Operator, request.Role);
                Assert.False(request.IsActive);
            });
    }

    [Fact]
    public void EnsurePresent_UpsertsAndrewAsAdmin()
    {
        var store = new InMemoryLanOrderStore();
        var configuration = new ConfigurationBuilder().Build();

        ReplicaApiBootstrapUsers.EnsurePresent(store, configuration);

        var andrew = Assert.Single(store.GetUsers(includeInactive: true), user =>
            string.Equals(user.Name, "Andrew", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ReplicaApiRoles.Admin, andrew.Role);
        Assert.True(andrew.IsActive);
    }
}
