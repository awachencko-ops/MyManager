using System;
using System.Net;
using System.Net.Http;
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
        CancellationToken cancellationToken = default);
}

public sealed class LanApiIdentityService : ILanApiIdentityService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public LanApiIdentityService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public async Task<LanApiIdentityResult> GetCurrentUserAsync(
        string apiBaseUrl,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (!TryBuildAuthMeUri(apiBaseUrl, out var requestUri))
            return LanApiIdentityResult.Unavailable("invalid LAN API base URL");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
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

    private static void TryAddActorHeader(HttpRequestMessage request, string actor)
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
}

public sealed class LanApiCurrentUser
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; }
    public bool IsValidated { get; set; }
    public bool CanManageUsers { get; set; }
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
