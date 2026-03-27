using Replica.Api.Infrastructure;
using Replica.Shared.Models;

namespace Replica.Api.Services;

internal static class StoreActorRoleGuard
{
    public static bool TryEnsureAdminAccess(
        IReadOnlyList<SharedUser> users,
        string actor,
        out string error)
    {
        var normalizedActor = actor?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedActor))
        {
            error = "actor role is not allowed";
            return false;
        }

        if (string.Equals(normalizedActor, ReplicaApiBootstrapUsers.BootstrapActor, StringComparison.OrdinalIgnoreCase))
        {
            error = string.Empty;
            return true;
        }

        var activeUsers = (users ?? Array.Empty<SharedUser>())
            .Where(user => user != null
                           && user.IsActive
                           && !string.IsNullOrWhiteSpace(user.Name))
            .ToList();
        if (activeUsers.Count == 0)
        {
            error = "actor role is not allowed";
            return false;
        }

        var matchedUser = activeUsers.FirstOrDefault(user =>
            string.Equals(user.Name.Trim(), normalizedActor, StringComparison.OrdinalIgnoreCase));
        if (matchedUser == null || !ReplicaApiRoles.IsInRole(matchedUser.Role, ReplicaApiRoles.Admin))
        {
            error = "actor role is not allowed";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
