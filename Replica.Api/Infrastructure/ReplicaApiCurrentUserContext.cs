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
    public string AuthScheme { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
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
        IReplicaApiTokenService? tokenService,
        ILogger? logger = null)
    {
        var candidates = (knownUsers ?? Array.Empty<SharedUser>())
            .Where(user => user != null && !string.IsNullOrWhiteSpace(user.Name))
            .ToList();

        if (TryResolveAccessToken(request, out var rawToken))
        {
            if (tokenService == null)
            {
                return new ReplicaApiCurrentUser
                {
                    FailureStatusCode = StatusCodes.Status401Unauthorized,
                    FailureMessage = "token authentication is not configured"
                };
            }

            var tokenValidation = tokenService.ValidateToken(rawToken);
            if (!tokenValidation.IsSuccess)
            {
                return new ReplicaApiCurrentUser
                {
                    FailureStatusCode = StatusCodes.Status401Unauthorized,
                    FailureMessage = tokenValidation.Error
                };
            }

            if (candidates.Count == 0
                || candidates.All(user => string.Equals(user.Name.Trim(), LegacyBootstrapUserName, StringComparison.OrdinalIgnoreCase)))
            {
                return CreateAuthenticated(
                    tokenValidation.UserName.Trim(),
                    ReplicaApiRoles.Normalize(tokenValidation.Role),
                    isValidated: false,
                    authScheme: "Bearer",
                    sessionId: tokenValidation.SessionId);
            }

            var matchedTokenUser = candidates.FirstOrDefault(user =>
                user.IsActive
                && string.Equals(user.Name.Trim(), tokenValidation.UserName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (matchedTokenUser == null)
            {
                logger?.LogWarning("Request rejected for token session {SessionId}: user {Actor} is unknown or inactive.", tokenValidation.SessionId, tokenValidation.UserName);
                return new ReplicaApiCurrentUser
                {
                    Name = tokenValidation.UserName.Trim(),
                    FailureStatusCode = StatusCodes.Status403Forbidden,
                    FailureMessage = "actor is not allowed"
                };
            }

            return CreateAuthenticated(
                matchedTokenUser.Name.Trim(),
                ReplicaApiRoles.Normalize(matchedTokenUser.Role),
                isValidated: true,
                authScheme: "Bearer",
                sessionId: tokenValidation.SessionId);
        }

        if (!TryResolveActor(request, out var actor))
        {
            return new ReplicaApiCurrentUser
            {
                FailureStatusCode = StatusCodes.Status401Unauthorized,
                FailureMessage = MissingHeaderMessage
            };
        }

        var normalizedActor = actor.Trim();
        if (candidates.Count == 0)
            return CreateAuthenticated(normalizedActor, ReplicaApiRoles.Operator, isValidated: false, authScheme: "Header", sessionId: string.Empty);

        if (candidates.All(user => string.Equals(user.Name.Trim(), LegacyBootstrapUserName, StringComparison.OrdinalIgnoreCase)))
            return CreateAuthenticated(normalizedActor, ReplicaApiRoles.Operator, isValidated: false, authScheme: "Header", sessionId: string.Empty);

        var matchedUser = candidates.FirstOrDefault(user =>
            string.Equals(user.Name.Trim(), normalizedActor, StringComparison.OrdinalIgnoreCase));
        if (matchedUser != null && matchedUser.IsActive)
        {
            return CreateAuthenticated(
                matchedUser.Name.Trim(),
                ReplicaApiRoles.Normalize(matchedUser.Role),
                isValidated: true,
                authScheme: "Header",
                sessionId: string.Empty);
        }

        if (!strictActorValidation)
        {
            logger?.LogWarning(
                "Request actor {Actor} is not present in active users list. Allowing because strict actor validation is disabled.",
                normalizedActor);
            return CreateAuthenticated(normalizedActor, ReplicaApiRoles.Operator, isValidated: false, authScheme: "Header", sessionId: string.Empty);
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

    private static ReplicaApiCurrentUser CreateAuthenticated(string actor, string role, bool isValidated, string authScheme, string sessionId)
    {
        return new ReplicaApiCurrentUser
        {
            Name = actor,
            Role = ReplicaApiRoles.Normalize(role),
            IsAuthenticated = true,
            IsValidated = isValidated,
            AuthScheme = authScheme ?? string.Empty,
            SessionId = sessionId ?? string.Empty
        };
    }

    private static bool TryResolveAccessToken(HttpRequest request, out string rawToken)
    {
        rawToken = string.Empty;

        if (request.Headers.TryGetValue("Authorization", out var authorizationHeader))
        {
            var authorizationValue = authorizationHeader.ToString().Trim();
            if (authorizationValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                rawToken = authorizationValue["Bearer ".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(rawToken))
                    return true;
            }
        }

        if (request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader))
        {
            rawToken = apiKeyHeader.ToString().Trim();
            return !string.IsNullOrWhiteSpace(rawToken);
        }

        return false;
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

