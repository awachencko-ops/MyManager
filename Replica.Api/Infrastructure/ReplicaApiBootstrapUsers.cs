using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Replica.Api.Contracts;
using Replica.Api.Services;
using Replica.Shared.Models;

namespace Replica.Api.Infrastructure;

public sealed class ReplicaApiBootstrapUserConfiguration
{
    public string Name { get; init; } = string.Empty;
    public string Role { get; init; } = ReplicaApiRoles.Operator;
    public bool IsActive { get; init; } = true;
}

public static class ReplicaApiBootstrapUsers
{
    public const string BootstrapActor = "system/bootstrap";

    private static readonly IReadOnlyList<SharedUser> DefaultUsers =
    [
        new() { Id = "u-andrew", Name = "Andrew", Role = ReplicaApiRoles.Admin, IsActive = true },
        new() { Id = "u-admin", Name = "Administrator", Role = ReplicaApiRoles.Admin, IsActive = true },
        new() { Id = "u-operator-1", Name = "Operator 1", Role = ReplicaApiRoles.Operator, IsActive = true },
        new() { Id = "u-operator-2", Name = "Operator 2", Role = ReplicaApiRoles.Operator, IsActive = true }
    ];

    public static IReadOnlyList<SharedUser> GetDefaultUsers()
    {
        return DefaultUsers.Select(CloneUser).ToList();
    }

    public static IReadOnlyList<UpsertUserRequest> ResolveBootstrapRequests(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configured = configuration
            .GetSection("ReplicaApi:Auth:BootstrapUsers")
            .Get<List<ReplicaApiBootstrapUserConfiguration>>();
        if (configured == null || configured.Count == 0)
            return BuildRequests(DefaultUsers);

        var requests = configured
            .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Name))
            .Select(entry => new UpsertUserRequest
            {
                Name = entry.Name.Trim(),
                Role = ReplicaApiRoles.Normalize(entry.Role),
                IsActive = entry.IsActive
            })
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();

        return requests.Count == 0
            ? BuildRequests(DefaultUsers)
            : requests;
    }

    public static void EnsurePresent(ILanOrderStore store, IConfiguration configuration, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(configuration);

        foreach (var request in ResolveBootstrapRequests(configuration))
        {
            var result = store.UpsertUser(request, BootstrapActor);
            if (!result.IsSuccess)
            {
                logger?.LogWarning(
                    "Bootstrap user {UserName} could not be applied: {Error}",
                    request.Name,
                    result.Error ?? "unknown error");
                continue;
            }

            logger?.LogInformation(
                "Bootstrap user {UserName} ensured with role {Role} (active={IsActive}).",
                result.User?.Name ?? request.Name,
                result.User?.Role ?? request.Role ?? ReplicaApiRoles.Operator,
                result.User?.IsActive ?? request.IsActive ?? true);
        }
    }

    private static IReadOnlyList<UpsertUserRequest> BuildRequests(IEnumerable<SharedUser> users)
    {
        return users
            .Select(user => new UpsertUserRequest
            {
                Name = user.Name,
                Role = user.Role,
                IsActive = user.IsActive
            })
            .ToList();
    }

    private static SharedUser CloneUser(SharedUser source)
    {
        return new SharedUser
        {
            Id = source.Id,
            Name = source.Name,
            Role = source.Role,
            IsActive = source.IsActive,
            UpdatedAt = source.UpdatedAt
        };
    }
}
