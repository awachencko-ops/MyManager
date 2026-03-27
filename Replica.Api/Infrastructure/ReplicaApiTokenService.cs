using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Replica.Api.Data;
using Replica.Api.Data.Entities;

namespace Replica.Api.Infrastructure;

public interface IReplicaApiTokenService
{
    ReplicaApiTokenIssueResult IssueToken(
        string userName,
        string role,
        string issuedBy,
        string ipAddress,
        string userAgent);

    ReplicaApiTokenValidationResult ValidateToken(string rawToken);

    ReplicaApiTokenIssueResult RefreshToken(
        string sessionId,
        string requestedBy,
        string ipAddress,
        string userAgent);

    ReplicaApiTokenRevokeResult RevokeToken(
        string sessionId,
        string requestedBy,
        string ipAddress,
        string userAgent);
}

public sealed class ReplicaApiTokenService : IReplicaApiTokenService
{
    private const string TokenPrefix = "rpl1";
    private readonly object _sync = new();
    private readonly Dictionary<string, AuthSessionRecord> _inMemorySessions = new(StringComparer.Ordinal);
    private readonly List<AuthAuditEventRecord> _inMemoryAuditEvents = new();
    private readonly IDbContextFactory<ReplicaDbContext>? _dbContextFactory;
    private readonly ILogger<ReplicaApiTokenService> _logger;
    private readonly TimeSpan _tokenLifetime;

    public ReplicaApiTokenService(
        IConfiguration configuration,
        ILogger<ReplicaApiTokenService> logger,
        IDbContextFactory<ReplicaDbContext>? dbContextFactory = null)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;

        var configuredMinutes = configuration.GetValue<int?>("ReplicaApi:Auth:AccessTokenLifetimeMinutes") ?? 480;
        configuredMinutes = Math.Clamp(configuredMinutes, 5, 30 * 24 * 60);
        _tokenLifetime = TimeSpan.FromMinutes(configuredMinutes);
    }

    public ReplicaApiTokenIssueResult IssueToken(
        string userName,
        string role,
        string issuedBy,
        string ipAddress,
        string userAgent)
    {
        var normalizedUserName = userName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedUserName))
            return ReplicaApiTokenIssueResult.Failed("user name is required");

        var normalizedRole = ReplicaApiRoles.Normalize(role);
        var normalizedIssuedBy = issuedBy?.Trim() ?? string.Empty;
        var nowUtc = DateTime.UtcNow;
        var sessionId = Guid.NewGuid().ToString("N");
        var token = BuildRawToken(sessionId);
        var session = new AuthSessionRecord
        {
            SessionId = sessionId,
            UserName = normalizedUserName,
            Role = normalizedRole,
            AccessTokenHash = ComputeTokenHash(token),
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc + _tokenLifetime,
            LastSeenAtUtc = nowUtc,
            RevokedAtUtc = null,
            IssuedBy = normalizedIssuedBy
        };

        if (!TryStoreSession(session, out var error))
            return ReplicaApiTokenIssueResult.Failed(error);

        RecordAudit(
            eventType: "login",
            userName: normalizedUserName,
            role: normalizedRole,
            sessionId: sessionId,
            outcome: "success",
            reason: string.Empty,
            ipAddress: ipAddress,
            userAgent: userAgent,
            metadata: new { issued_by = normalizedIssuedBy });

        return ReplicaApiTokenIssueResult.Success(
            token,
            sessionId,
            normalizedUserName,
            normalizedRole,
            session.ExpiresAtUtc);
    }

    public ReplicaApiTokenValidationResult ValidateToken(string rawToken)
    {
        if (!TryParseToken(rawToken, out var sessionId, out var normalizedToken))
            return ReplicaApiTokenValidationResult.Invalid("invalid bearer token format");

        if (!TryGetSession(sessionId, out var session))
            return ReplicaApiTokenValidationResult.Invalid("invalid or expired bearer token");

        if (session.RevokedAtUtc.HasValue)
            return ReplicaApiTokenValidationResult.Invalid("bearer token is revoked");

        if (session.ExpiresAtUtc <= DateTime.UtcNow)
            return ReplicaApiTokenValidationResult.Invalid("bearer token is expired");

        var incomingHash = ComputeTokenHash(normalizedToken);
        if (!HashesEqual(incomingHash, session.AccessTokenHash))
            return ReplicaApiTokenValidationResult.Invalid("invalid or expired bearer token");

        return ReplicaApiTokenValidationResult.Success(
            session.UserName,
            ReplicaApiRoles.Normalize(session.Role),
            session.SessionId);
    }

    public ReplicaApiTokenIssueResult RefreshToken(
        string sessionId,
        string requestedBy,
        string ipAddress,
        string userAgent)
    {
        var normalizedSessionId = sessionId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedSessionId))
            return ReplicaApiTokenIssueResult.Failed("session id is required");

        var nowUtc = DateTime.UtcNow;
        string token;
        AuthSessionRecord refreshedSession;

        if (_dbContextFactory != null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var session = db.AuthSessions.FirstOrDefault(x => x.SessionId == normalizedSessionId);
            if (session == null)
            {
                RecordAudit("refresh", string.Empty, string.Empty, normalizedSessionId, "failed", "session not found", ipAddress, userAgent, null);
                return ReplicaApiTokenIssueResult.NotFound("session not found");
            }

            if (session.RevokedAtUtc.HasValue || session.ExpiresAtUtc <= nowUtc)
            {
                RecordAudit("refresh", session.UserName, session.Role, session.SessionId, "failed", "session expired or revoked", ipAddress, userAgent, null);
                return ReplicaApiTokenIssueResult.Failed("session expired or revoked");
            }

            token = BuildRawToken(session.SessionId);
            session.AccessTokenHash = ComputeTokenHash(token);
            session.ExpiresAtUtc = nowUtc + _tokenLifetime;
            session.LastSeenAtUtc = nowUtc;
            db.SaveChanges();

            refreshedSession = CloneSession(session);
        }
        else
        {
            lock (_sync)
            {
                if (!_inMemorySessions.TryGetValue(normalizedSessionId, out var session))
                {
                    RecordAudit("refresh", string.Empty, string.Empty, normalizedSessionId, "failed", "session not found", ipAddress, userAgent, null);
                    return ReplicaApiTokenIssueResult.NotFound("session not found");
                }

                if (session.RevokedAtUtc.HasValue || session.ExpiresAtUtc <= nowUtc)
                {
                    RecordAudit("refresh", session.UserName, session.Role, session.SessionId, "failed", "session expired or revoked", ipAddress, userAgent, null);
                    return ReplicaApiTokenIssueResult.Failed("session expired or revoked");
                }

                token = BuildRawToken(session.SessionId);
                session.AccessTokenHash = ComputeTokenHash(token);
                session.ExpiresAtUtc = nowUtc + _tokenLifetime;
                session.LastSeenAtUtc = nowUtc;
                refreshedSession = CloneSession(session);
            }
        }

        RecordAudit(
            eventType: "refresh",
            userName: refreshedSession.UserName,
            role: refreshedSession.Role,
            sessionId: refreshedSession.SessionId,
            outcome: "success",
            reason: string.Empty,
            ipAddress: ipAddress,
            userAgent: userAgent,
            metadata: new { requested_by = requestedBy?.Trim() ?? string.Empty });

        return ReplicaApiTokenIssueResult.Success(
            token,
            refreshedSession.SessionId,
            refreshedSession.UserName,
            ReplicaApiRoles.Normalize(refreshedSession.Role),
            refreshedSession.ExpiresAtUtc);
    }

    public ReplicaApiTokenRevokeResult RevokeToken(
        string sessionId,
        string requestedBy,
        string ipAddress,
        string userAgent)
    {
        var normalizedSessionId = sessionId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedSessionId))
            return ReplicaApiTokenRevokeResult.Failed("session id is required");

        var nowUtc = DateTime.UtcNow;
        var normalizedRequestedBy = requestedBy?.Trim() ?? string.Empty;
        AuthSessionRecord? revokedSession = null;

        if (_dbContextFactory != null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var session = db.AuthSessions.FirstOrDefault(x => x.SessionId == normalizedSessionId);
            if (session == null)
            {
                RecordAudit("revoke", string.Empty, string.Empty, normalizedSessionId, "failed", "session not found", ipAddress, userAgent, null);
                return ReplicaApiTokenRevokeResult.NotFound("session not found");
            }

            if (!session.RevokedAtUtc.HasValue)
            {
                session.RevokedAtUtc = nowUtc;
                session.RevokedBy = normalizedRequestedBy;
                session.LastSeenAtUtc = nowUtc;
                db.SaveChanges();
            }

            revokedSession = CloneSession(session);
        }
        else
        {
            lock (_sync)
            {
                if (!_inMemorySessions.TryGetValue(normalizedSessionId, out var session))
                {
                    RecordAudit("revoke", string.Empty, string.Empty, normalizedSessionId, "failed", "session not found", ipAddress, userAgent, null);
                    return ReplicaApiTokenRevokeResult.NotFound("session not found");
                }

                if (!session.RevokedAtUtc.HasValue)
                {
                    session.RevokedAtUtc = nowUtc;
                    session.RevokedBy = normalizedRequestedBy;
                    session.LastSeenAtUtc = nowUtc;
                }

                revokedSession = CloneSession(session);
            }
        }

        RecordAudit(
            eventType: "revoke",
            userName: revokedSession.UserName,
            role: revokedSession.Role,
            sessionId: revokedSession.SessionId,
            outcome: "success",
            reason: string.Empty,
            ipAddress: ipAddress,
            userAgent: userAgent,
            metadata: new { requested_by = normalizedRequestedBy });

        return ReplicaApiTokenRevokeResult.Success();
    }

    private bool TryGetSession(string sessionId, out AuthSessionRecord session)
    {
        if (_dbContextFactory != null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var stored = db.AuthSessions
                .AsNoTracking()
                .FirstOrDefault(x => x.SessionId == sessionId);
            if (stored == null)
            {
                session = new AuthSessionRecord();
                return false;
            }

            session = stored;
            return true;
        }

        lock (_sync)
        {
            if (!_inMemorySessions.TryGetValue(sessionId, out var stored))
            {
                session = new AuthSessionRecord();
                return false;
            }

            session = CloneSession(stored);
            return true;
        }
    }

    private bool TryStoreSession(AuthSessionRecord session, out string error)
    {
        error = string.Empty;

        try
        {
            if (_dbContextFactory != null)
            {
                using var db = _dbContextFactory.CreateDbContext();
                db.AuthSessions.Add(session);
                db.SaveChanges();
                return true;
            }

            lock (_sync)
            {
                _inMemorySessions[session.SessionId] = CloneSession(session);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist auth session {SessionId}", session.SessionId);
            error = "failed to persist auth session";
            return false;
        }
    }

    private void RecordAudit(
        string eventType,
        string userName,
        string role,
        string sessionId,
        string outcome,
        string reason,
        string ipAddress,
        string userAgent,
        object? metadata)
    {
        var entry = new AuthAuditEventRecord
        {
            EventType = eventType ?? string.Empty,
            UserName = userName?.Trim() ?? string.Empty,
            Role = string.IsNullOrWhiteSpace(role) ? string.Empty : ReplicaApiRoles.Normalize(role),
            SessionId = sessionId?.Trim() ?? string.Empty,
            Outcome = outcome ?? string.Empty,
            Reason = reason ?? string.Empty,
            IpAddress = ipAddress?.Trim() ?? string.Empty,
            UserAgent = userAgent?.Trim() ?? string.Empty,
            MetadataJson = metadata == null ? "{}" : JsonSerializer.Serialize(metadata),
            CreatedAtUtc = DateTime.UtcNow
        };

        try
        {
            if (_dbContextFactory != null)
            {
                using var db = _dbContextFactory.CreateDbContext();
                db.AuthAuditEvents.Add(entry);
                db.SaveChanges();
                return;
            }

            lock (_sync)
            {
                _inMemoryAuditEvents.Add(entry);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist auth audit event {EventType}", eventType);
        }
    }

    private static AuthSessionRecord CloneSession(AuthSessionRecord source)
    {
        return new AuthSessionRecord
        {
            SessionId = source.SessionId,
            UserName = source.UserName,
            Role = source.Role,
            AccessTokenHash = source.AccessTokenHash,
            CreatedAtUtc = source.CreatedAtUtc,
            ExpiresAtUtc = source.ExpiresAtUtc,
            LastSeenAtUtc = source.LastSeenAtUtc,
            RevokedAtUtc = source.RevokedAtUtc,
            IssuedBy = source.IssuedBy,
            RevokedBy = source.RevokedBy
        };
    }

    private static string BuildRawToken(string sessionId)
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        return $"{TokenPrefix}.{sessionId}.{secret}";
    }

    private static string ComputeTokenHash(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool TryParseToken(string rawToken, out string sessionId, out string normalizedToken)
    {
        sessionId = string.Empty;
        normalizedToken = rawToken?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedToken))
            return false;

        var parts = normalizedToken.Split('.', StringSplitOptions.None);
        if (parts.Length != 3)
            return false;

        if (!string.Equals(parts[0], TokenPrefix, StringComparison.Ordinal))
            return false;

        sessionId = parts[1]?.Trim() ?? string.Empty;
        var secret = parts[2]?.Trim() ?? string.Empty;

        return !string.IsNullOrWhiteSpace(sessionId)
               && !string.IsNullOrWhiteSpace(secret);
    }

    private static bool HashesEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}

public sealed class ReplicaApiTokenIssueResult
{
    private ReplicaApiTokenIssueResult(
        bool isSuccess,
        bool isNotFound,
        string error,
        string accessToken,
        string sessionId,
        string userName,
        string role,
        DateTime expiresAtUtc)
    {
        IsSuccess = isSuccess;
        IsNotFound = isNotFound;
        Error = error;
        AccessToken = accessToken;
        SessionId = sessionId;
        UserName = userName;
        Role = role;
        ExpiresAtUtc = expiresAtUtc;
    }

    public bool IsSuccess { get; }
    public bool IsNotFound { get; }
    public string Error { get; }
    public string AccessToken { get; }
    public string SessionId { get; }
    public string UserName { get; }
    public string Role { get; }
    public DateTime ExpiresAtUtc { get; }

    public static ReplicaApiTokenIssueResult Success(
        string accessToken,
        string sessionId,
        string userName,
        string role,
        DateTime expiresAtUtc)
        => new(
            isSuccess: true,
            isNotFound: false,
            error: string.Empty,
            accessToken: accessToken,
            sessionId: sessionId,
            userName: userName,
            role: role,
            expiresAtUtc: expiresAtUtc);

    public static ReplicaApiTokenIssueResult Failed(string error)
        => new(
            isSuccess: false,
            isNotFound: false,
            error: error ?? string.Empty,
            accessToken: string.Empty,
            sessionId: string.Empty,
            userName: string.Empty,
            role: string.Empty,
            expiresAtUtc: DateTime.MinValue);

    public static ReplicaApiTokenIssueResult NotFound(string error)
        => new(
            isSuccess: false,
            isNotFound: true,
            error: error ?? string.Empty,
            accessToken: string.Empty,
            sessionId: string.Empty,
            userName: string.Empty,
            role: string.Empty,
            expiresAtUtc: DateTime.MinValue);
}

public sealed class ReplicaApiTokenValidationResult
{
    private ReplicaApiTokenValidationResult(bool isSuccess, string error, string userName, string role, string sessionId)
    {
        IsSuccess = isSuccess;
        Error = error;
        UserName = userName;
        Role = role;
        SessionId = sessionId;
    }

    public bool IsSuccess { get; }
    public string Error { get; }
    public string UserName { get; }
    public string Role { get; }
    public string SessionId { get; }

    public static ReplicaApiTokenValidationResult Success(string userName, string role, string sessionId)
        => new(
            isSuccess: true,
            error: string.Empty,
            userName: userName,
            role: role,
            sessionId: sessionId);

    public static ReplicaApiTokenValidationResult Invalid(string error)
        => new(
            isSuccess: false,
            error: error ?? string.Empty,
            userName: string.Empty,
            role: string.Empty,
            sessionId: string.Empty);
}

public sealed class ReplicaApiTokenRevokeResult
{
    private ReplicaApiTokenRevokeResult(bool isSuccess, bool isNotFound, string error)
    {
        IsSuccess = isSuccess;
        IsNotFound = isNotFound;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsNotFound { get; }
    public string Error { get; }

    public static ReplicaApiTokenRevokeResult Success() => new(
        isSuccess: true,
        isNotFound: false,
        error: string.Empty);

    public static ReplicaApiTokenRevokeResult NotFound(string error) => new(
        isSuccess: false,
        isNotFound: true,
        error: error ?? string.Empty);

    public static ReplicaApiTokenRevokeResult Failed(string error) => new(
        isSuccess: false,
        isNotFound: false,
        error: error ?? string.Empty);
}
