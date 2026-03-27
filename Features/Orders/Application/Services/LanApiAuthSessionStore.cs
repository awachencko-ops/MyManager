using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Replica;

public interface ILanApiAuthSessionStore
{
    bool TryGetActiveSession(out LanApiAuthSession session);
    void Save(LanApiAuthSession session);
    void Clear();
}

public sealed class LanApiAuthSession
{
    public string AccessToken { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    public bool IsExpired => ExpiresAtUtc != default && ExpiresAtUtc <= DateTime.UtcNow;
}

public sealed class InMemoryLanApiAuthSessionStore : ILanApiAuthSessionStore
{
    private readonly object _sync = new();
    private LanApiAuthSession? _current;

    public bool TryGetActiveSession(out LanApiAuthSession session)
    {
        lock (_sync)
        {
            if (_current == null || string.IsNullOrWhiteSpace(_current.AccessToken) || _current.IsExpired)
            {
                session = new LanApiAuthSession();
                return false;
            }

            session = Clone(_current);
            return true;
        }
    }

    public void Save(LanApiAuthSession session)
    {
        if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
            return;

        lock (_sync)
        {
            _current = Clone(session);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _current = null;
        }
    }

    private static LanApiAuthSession Clone(LanApiAuthSession source)
    {
        return new LanApiAuthSession
        {
            AccessToken = source.AccessToken,
            SessionId = source.SessionId,
            ExpiresAtUtc = source.ExpiresAtUtc,
            UserName = source.UserName,
            Role = source.Role
        };
    }
}

public sealed class DpapiLanApiAuthSessionStore : ILanApiAuthSessionStore
{
    private readonly object _sync = new();
    private readonly string _filePath;

    public DpapiLanApiAuthSessionStore(string? filePath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Replica",
                "AppData",
                "lan_api_auth_session.dat")
            : filePath;
    }

    public bool TryGetActiveSession(out LanApiAuthSession session)
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    session = new LanApiAuthSession();
                    return false;
                }

                var encryptedBytes = File.ReadAllBytes(_filePath);
                if (encryptedBytes.Length == 0)
                {
                    session = new LanApiAuthSession();
                    return false;
                }

                var plainBytes = ProtectedData.Unprotect(encryptedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                var payload = Encoding.UTF8.GetString(plainBytes);
                session = JsonSerializer.Deserialize<LanApiAuthSession>(payload) ?? new LanApiAuthSession();
                if (string.IsNullOrWhiteSpace(session.AccessToken) || session.IsExpired)
                {
                    ClearCore();
                    session = new LanApiAuthSession();
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"LAN-API | auth-session-read-failed | {ex.Message}");
                session = new LanApiAuthSession();
                return false;
            }
        }
    }

    public void Save(LanApiAuthSession session)
    {
        if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
            return;

        lock (_sync)
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                var payload = JsonSerializer.Serialize(session);
                var plainBytes = Encoding.UTF8.GetBytes(payload);
                var encryptedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(_filePath, encryptedBytes);
            }
            catch (Exception ex)
            {
                Logger.Warn($"LAN-API | auth-session-save-failed | {ex.Message}");
            }
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            ClearCore();
        }
    }

    private void ClearCore()
    {
        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        catch (Exception ex)
        {
            Logger.Warn($"LAN-API | auth-session-clear-failed | {ex.Message}");
        }
    }
}
