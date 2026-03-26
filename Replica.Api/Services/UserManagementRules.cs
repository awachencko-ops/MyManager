using Replica.Api.Contracts;
using Replica.Api.Infrastructure;

namespace Replica.Api.Services;

public static class UserManagementRules
{
    public static bool TryNormalizeUpsertRequest(
        UpsertUserRequest? request,
        out string normalizedName,
        out string normalizedRole,
        out bool? normalizedIsActive,
        out string error)
    {
        normalizedName = string.Empty;
        normalizedRole = ReplicaApiRoles.Operator;
        normalizedIsActive = null;
        error = string.Empty;

        if (request == null)
        {
            error = "request body is required";
            return false;
        }

        normalizedName = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            error = "user name is required";
            return false;
        }

        var requestedRole = request.Role?.Trim();
        if (string.IsNullOrWhiteSpace(requestedRole))
        {
            normalizedRole = ReplicaApiRoles.Operator;
        }
        else if (string.Equals(requestedRole, ReplicaApiRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            normalizedRole = ReplicaApiRoles.Admin;
        }
        else if (string.Equals(requestedRole, ReplicaApiRoles.Operator, StringComparison.OrdinalIgnoreCase))
        {
            normalizedRole = ReplicaApiRoles.Operator;
        }
        else
        {
            error = "role must be Admin or Operator";
            return false;
        }

        normalizedIsActive = request.IsActive;
        return true;
    }

    public static bool WouldRemoveLastActiveAdmin(
        string currentRole,
        bool currentIsActive,
        string nextRole,
        bool nextIsActive,
        int otherActiveAdminsCount)
    {
        var isCurrentActiveAdmin = currentIsActive
            && string.Equals(ReplicaApiRoles.Normalize(currentRole), ReplicaApiRoles.Admin, StringComparison.Ordinal);
        var isNextActiveAdmin = nextIsActive
            && string.Equals(ReplicaApiRoles.Normalize(nextRole), ReplicaApiRoles.Admin, StringComparison.Ordinal);

        return isCurrentActiveAdmin && !isNextActiveAdmin && otherActiveAdminsCount <= 0;
    }
}
