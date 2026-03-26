using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Replica.Shared;
using Replica.Shared.Models;

namespace Replica.Api.Infrastructure;

public sealed class ReplicaApiCurrentUser
{
    public static ReplicaApiCurrentUser Anonymous { get; } = new();

    public string Name { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public bool IsAuthenticated { get; init; }
    public bool IsValidated { get; init; }
    public int FailureStatusCode { get; init; }
    public string FailureMessage { get; init; } = string.Empty;

    public bool HasFailure => FailureStatusCode > 0;
}

public static class ReplicaApiRoles
{
    public const string Admin = "Admin";
    public const string Operator = "Operator";

    public static string Normalize(string? role)
    {
        return string.Equals(role?.Trim(), Admin, StringComparison.OrdinalIgnoreCase)
            ? Admin
            : Operator;
    }

    public static bool IsInRole(string actualRole, string requiredRole)
    {
        var normalizedActual = Normalize(actualRole);
        var normalizedRequired = Normalize(requiredRole);

        if (string.Equals(normalizedActual, Admin, StringComparison.Ordinal))
            return true;

        return string.Equals(normalizedActual, normalizedRequired, StringComparison.Ordinal);
    }
}

public static class ReplicaApiCurrentUserContext
{
    private const string LegacyBootstrapUserName = "\u0421\u0435\u0440\u0432\u0435\u0440 \"\u0422\u0430\u0443\u0434\u0435\u043c\u0438\"";
    private const string MissingHeaderMessage = "X-Current-User header is required";
    private static readonly object ItemKey = new();

    public static ReplicaApiCurrentUser Resolve(
        HttpRequest request,
        IReadOnlyList<SharedUser>? knownUsers,
        bool strictActorValidation,
        ILogger? logger = null)
    {
        if (!TryResolveActor(request, out var actor))
        {
            return new ReplicaApiCurrentUser
            {
                FailureStatusCode = StatusCodes.Status401Unauthorized,
                FailureMessage = MissingHeaderMessage
            };
        }

        var normalizedActor = actor.Trim();
        var candidates = (knownUsers ?? Array.Empty<SharedUser>())
            .Where(user => user != null && !string.IsNullOrWhiteSpace(user.Name))
            .ToList();

        if (candidates.Count == 0)
            return CreateAuthenticated(normalizedActor, ReplicaApiRoles.Operator, isValidated: false);

        if (candidates.All(user => string.Equals(user.Name.Trim(), LegacyBootstrapUserName, StringComparison.OrdinalIgnoreCase)))
            return CreateAuthenticated(normalizedActor, ReplicaApiRoles.Operator, isValidated: false);

        var matchedUser = candidates.FirstOrDefault(user =>
            string.Equals(user.Name.Trim(), normalizedActor, StringComparison.OrdinalIgnoreCase));
        if (matchedUser != null && matchedUser.IsActive)
        {
            return CreateAuthenticated(
                matchedUser.Name.Trim(),
                ReplicaApiRoles.Normalize(matchedUser.Role),
                isValidated: true);
        }

        if (!strictActorValidation)
        {
            logger?.LogWarning(
                "Request actor {Actor} is not present in active users list. Allowing because strict actor validation is disabled.",
                normalizedActor);
            return CreateAuthenticated(normalizedActor, ReplicaApiRoles.Operator, isValidated: false);
        }

        logger?.LogWarning("Request rejected for actor {Actor}: unknown or inactive user.", normalizedActor);
        return new ReplicaApiCurrentUser
        {
            Name = normalizedActor,
            FailureStatusCode = StatusCodes.Status403Forbidden,
            FailureMessage = "actor is not allowed"
        };
    }

    public static void Set(HttpContext context, ReplicaApiCurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Items[ItemKey] = user ?? ReplicaApiCurrentUser.Anonymous;
    }

    public static ReplicaApiCurrentUser Get(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Items.TryGetValue(ItemKey, out var value) && value is ReplicaApiCurrentUser currentUser
            ? currentUser
            : ReplicaApiCurrentUser.Anonymous;
    }

    private static ReplicaApiCurrentUser CreateAuthenticated(string actor, string role, bool isValidated)
    {
        return new ReplicaApiCurrentUser
        {
            Name = actor,
            Role = ReplicaApiRoles.Normalize(role),
            IsAuthenticated = true,
            IsValidated = isValidated
        };
    }

    private static bool TryResolveActor(HttpRequest request, out string actor)
    {
        actor = string.Empty;

        if (request.Headers.TryGetValue(CurrentUserHeaderCodec.EncodedHeaderName, out var encodedActorHeader)
            && CurrentUserHeaderCodec.TryDecode(encodedActorHeader.ToString(), out var decodedActor))
        {
            actor = decodedActor;
            return true;
        }

        if (request.Headers.TryGetValue(CurrentUserHeaderCodec.HeaderName, out var actorHeader))
        {
            actor = actorHeader.ToString().Trim();
            return !string.IsNullOrWhiteSpace(actor);
        }

        return false;
    }
}

