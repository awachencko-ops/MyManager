using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Replica.Shared;

namespace Replica;

public interface ILanApiIdentityService
{
    Task<LanApiIdentityResult> GetCurrentUserAsync(
        string apiBaseUrl,
        string actor,
        bool allowSessionBootstrap = false,
        CancellationToken cancellationToken = default);
}

public sealed class LanApiIdentityService : ILanApiIdentityService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILanApiAuthSessionStore _authSessionStore;

    public LanApiIdentityService(HttpClient? httpClient = null, ILanApiAuthSessionStore? authSessionStore = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _authSessionStore = authSessionStore ?? new InMemoryLanApiAuthSessionStore();
    }

    public async Task<LanApiIdentityResult> GetCurrentUserAsync(
        string apiBaseUrl,
        string actor,
        bool allowSessionBootstrap = false,
        CancellationToken cancellationToken = default)
    {
        if (!TryBuildAuthMeUri(apiBaseUrl, out var requestUri))
            return LanApiIdentityResult.Unavailable("invalid LAN API base URL");

        if (!allowSessionBootstrap)
            return await SendMeRequestAsync(
                    requestUri,
                    apiBaseUrl,
                    actor,
                    useStoredSession: false,
                    cancellationToken)
                .ConfigureAwait(false);

        var firstAttempt = await SendMeRequestAsync(
                requestUri,
                apiBaseUrl,
                actor,
                useStoredSession: true,
                cancellationToken)
            .ConfigureAwait(false);
        if (firstAttempt.IsSuccess || firstAttempt.IsForbidden)
            return firstAttempt;

        if (firstAttempt.IsUnauthorized && _authSessionStore.TryGetActiveSession(out _))
            _authSessionStore.Clear();

        if (string.IsNullOrWhiteSpace(actor))
            return firstAttempt;

        if (!await TryBootstrapSessionAsync(apiBaseUrl, actor, cancellationToken).ConfigureAwait(false))
            return firstAttempt;

        var secondAttempt = await SendMeRequestAsync(
                requestUri,
                apiBaseUrl,
                actor,
                useStoredSession: true,
                cancellationToken)
            .ConfigureAwait(false);
        return secondAttempt.IsSuccess ? secondAttempt : firstAttempt;
    }

    private async Task<LanApiIdentityResult> SendMeRequestAsync(
        Uri requestUri,
        string apiBaseUrl,
        string actor,
        bool useStoredSession,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            var usedStoredSession = false;
            if (useStoredSession)
            {
                var sessionResolution = await LanApiAuthSessionHttpFlow
                    .TryResolveSessionForRequestAsync(_httpClient, _authSessionStore, apiBaseUrl, actor, cancellationToken)
                    .ConfigureAwait(false);
                if (sessionResolution.HasSession)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionResolution.Session.AccessToken.Trim());
                    usedStoredSession = true;
                }
            }

            if (!usedStoredSession)
                TryAddActorHeader(request, actor);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var user = Deserialize(payload);
                if (user != null)
                    return LanApiIdentityResult.Success(user);

                return LanApiIdentityResult.Failed("LAN API returned malformed auth payload");
            }

            var errorMessage = TryReadError(payload)
                ?? $"LAN API returned {(int)response.StatusCode} ({response.StatusCode})";

            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => LanApiIdentityResult.Unauthorized(errorMessage),
                HttpStatusCode.Forbidden => LanApiIdentityResult.Forbidden(errorMessage),
                _ => LanApiIdentityResult.Failed(errorMessage)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LanApiIdentityResult.Unavailable("LAN API request timed out");
        }
        catch (HttpRequestException ex)
        {
            return LanApiIdentityResult.Unavailable(ex.Message);
        }
        catch (Exception ex)
        {
            return LanApiIdentityResult.Failed(ex.Message);
        }
    }

    private async Task<bool> TryBootstrapSessionAsync(
        string apiBaseUrl,
        string actor,
        CancellationToken cancellationToken)
    {
        if (!TryBuildLoginUri(apiBaseUrl, out var loginUri))
            return false;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, loginUri)
            {
                Content = JsonContent.Create(
                    new LoginRequestPayload { UserName = actor?.Trim() ?? string.Empty },
                    options: JsonOptions)
            };
            TryAddActorHeader(request, actor);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return false;

            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var loginResponse = DeserializeLogin(payload);
            if (loginResponse == null || string.IsNullOrWhiteSpace(loginResponse.AccessToken))
                return false;

            _authSessionStore.Save(new LanApiAuthSession
            {
                AccessToken = loginResponse.AccessToken.Trim(),
                SessionId = loginResponse.SessionId?.Trim() ?? string.Empty,
                ExpiresAtUtc = loginResponse.ExpiresAtUtc,
                UserName = loginResponse.Name?.Trim() ?? string.Empty,
                Role = loginResponse.Role?.Trim() ?? string.Empty
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryBuildAuthMeUri(string apiBaseUrl, out Uri requestUri)
    {
        requestUri = null!;
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            return false;

        if (!Uri.TryCreate(apiBaseUrl.Trim(), UriKind.Absolute, out var baseUri))
            return false;

        requestUri = new Uri(baseUri, "api/auth/me");
        return true;
    }

    private static bool TryBuildLoginUri(string apiBaseUrl, out Uri requestUri)
    {
        requestUri = null!;
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            return false;

        if (!Uri.TryCreate(apiBaseUrl.Trim(), UriKind.Absolute, out var baseUri))
            return false;

        requestUri = new Uri(baseUri, "api/auth/login");
        return true;
    }

    private static void TryAddActorHeader(HttpRequestMessage request, string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            return;

        var normalizedActor = actor.Trim();
        if (CurrentUserHeaderCodec.RequiresEncoding(normalizedActor))
        {
            request.Headers.TryAddWithoutValidation(
                CurrentUserHeaderCodec.HeaderName,
                CurrentUserHeaderCodec.BuildAsciiFallback(normalizedActor));
            request.Headers.TryAddWithoutValidation(
                CurrentUserHeaderCodec.EncodedHeaderName,
                CurrentUserHeaderCodec.Encode(normalizedActor));
            return;
        }

        request.Headers.TryAddWithoutValidation(CurrentUserHeaderCodec.HeaderName, normalizedActor);
    }

    private static LanApiCurrentUser? Deserialize(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            return JsonSerializer.Deserialize<LanApiCurrentUser>(payload, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static LoginResponsePayload? DeserializeLogin(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            return JsonSerializer.Deserialize<LoginResponsePayload>(payload, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ApiErrorResponse>(payload, JsonOptions)?.Error;
        }
        catch
        {
            return null;
        }
    }

    private sealed class ApiErrorResponse
    {
        public string? Error { get; set; }
    }

    private sealed class LoginRequestPayload
    {
        public string UserName { get; set; } = string.Empty;
    }

    private sealed class LoginResponsePayload
    {
        public string AccessToken { get; set; } = string.Empty;
        public DateTime ExpiresAtUtc { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
    }
}

public sealed class LanApiCurrentUser
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; }
    public bool IsValidated { get; set; }
    public bool CanManageUsers { get; set; }
    public string AuthScheme { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
}

public sealed class LanApiIdentityResult
{
    private LanApiIdentityResult(
        bool isSuccess,
        bool isUnauthorized,
        bool isForbidden,
        bool isUnavailable,
        string error,
        LanApiCurrentUser? user)
    {
        IsSuccess = isSuccess;
        IsUnauthorized = isUnauthorized;
        IsForbidden = isForbidden;
        IsUnavailable = isUnavailable;
        Error = error;
        User = user;
    }

    public bool IsSuccess { get; }
    public bool IsUnauthorized { get; }
    public bool IsForbidden { get; }
    public bool IsUnavailable { get; }
    public string Error { get; }
    public LanApiCurrentUser? User { get; }

    public static LanApiIdentityResult Success(LanApiCurrentUser user) => new(
        isSuccess: true,
        isUnauthorized: false,
        isForbidden: false,
        isUnavailable: false,
        error: string.Empty,
        user: user);

    public static LanApiIdentityResult Unauthorized(string error) => new(
        isSuccess: false,
        isUnauthorized: true,
        isForbidden: false,
        isUnavailable: false,
        error: error ?? string.Empty,
        user: null);

    public static LanApiIdentityResult Forbidden(string error) => new(
        isSuccess: false,
        isUnauthorized: false,
        isForbidden: true,
        isUnavailable: false,
        error: error ?? string.Empty,
        user: null);

    public static LanApiIdentityResult Unavailable(string error) => new(
        isSuccess: false,
        isUnauthorized: false,
        isForbidden: false,
        isUnavailable: true,
        error: error ?? string.Empty,
        user: null);

    public static LanApiIdentityResult Failed(string error) => new(
        isSuccess: false,
        isUnauthorized: false,
        isForbidden: false,
        isUnavailable: false,
        error: error ?? string.Empty,
        user: null);
}
