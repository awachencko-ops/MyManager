using System.Linq;
using Replica.Api.Contracts;
using Replica.Api.Infrastructure;
using Replica.Api.Services;
using Xunit;

namespace Replica.VerifyTests;

public sealed class UsersAdminManagementTests
{
    [Fact]
    public void UpsertUser_CreatesAdminUser()
    {
        var store = new InMemoryLanOrderStore();

        var result = store.UpsertUser(new UpsertUserRequest
        {
            Name = "admin-2",
            Role = ReplicaApiRoles.Admin,
            IsActive = true
        }, actor: "Administrator");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.User);
        Assert.Equal("admin-2", result.User!.Name);
        Assert.Equal(ReplicaApiRoles.Admin, result.User.Role);
        Assert.True(result.User.IsActive);
    }

    [Fact]
    public void UpsertUser_RejectsInvalidRole()
    {
        var store = new InMemoryLanOrderStore();

        var result = store.UpsertUser(new UpsertUserRequest
        {
            Name = "qa-user",
            Role = "Supervisor"
        }, actor: "Administrator");

        Assert.True(result.IsBadRequest);
        Assert.Equal("role must be Admin or Operator", result.Error);
    }

    [Fact]
    public void UpsertUser_RejectsRemovingLastActiveAdmin()
    {
        var store = new InMemoryLanOrderStore();

        var result = store.UpsertUser(new UpsertUserRequest
        {
            Name = "Administrator",
            Role = ReplicaApiRoles.Operator,
            IsActive = false
        }, actor: "Administrator");

        Assert.True(result.IsBadRequest);
        Assert.Equal("at least one active admin is required", result.Error);
    }

    [Fact]
    public void GetUsers_IncludeInactive_ReturnsInactiveUsers()
    {
        var store = new InMemoryLanOrderStore();
        var upsert = store.UpsertUser(new UpsertUserRequest
        {
            Name = "disabled-user",
            Role = ReplicaApiRoles.Operator,
            IsActive = false
        }, actor: "Administrator");

        Assert.True(upsert.IsSuccess);
        Assert.DoesNotContain(store.GetUsers(), user => user.Name == "disabled-user");
        Assert.Contains(store.GetUsers(includeInactive: true), user => user.Name == "disabled-user" && !user.IsActive);
    }
}
