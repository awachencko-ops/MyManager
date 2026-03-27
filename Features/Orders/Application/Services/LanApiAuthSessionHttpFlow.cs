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

internal static class LanApiAuthSessionHttpFlow
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly TimeSpan RefreshLeadTime = TimeSpan.FromMinutes(5);

    public static async Task<LanApiAuthSessionResolution> TryResolveSessionForRequestAsync(
        HttpClient httpClient,
        ILanApiAuthSessionStore authSessionStore,
        string apiBaseUrl,
        string actor,
        CancellationToken cancellationToken)
    {
        if (httpClient == null || authSessionStore == null)
            return LanApiAuthSessionResolution.None;

        if (!authSessionStore.TryGetActiveSession(out var session))
            return LanApiAuthSessionResolution.None;

        if (!ShouldRefresh(session))
            return LanApiAuthSessionResolution.From(session);

        if (!TryBuildAuthRefreshUri(apiBaseUrl, out var refreshUri))
            return LanApiAuthSessionResolution.From(session);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, refreshUri)
            {
                Content = JsonContent.Create(
                    new RefreshRequestPayload
                    {
                        SessionId = session.SessionId
                    },
                    options: JsonOptions)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken.Trim());
            TryAddActorHeader(request, actor);

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var refreshed = DeserializeTokenResponse(payload);
                if (refreshed == null || string.IsNullOrWhiteSpace(refreshed.AccessToken))
                    return LanApiAuthSessionResolution.From(session);

                var refreshedSession = new LanApiAuthSession
                {
                    AccessToken = refreshed.AccessToken.Trim(),
                    SessionId = string.IsNullOrWhiteSpace(refreshed.SessionId) ? session.SessionId : refreshed.SessionId.Trim(),
                    ExpiresAtUtc = refreshed.ExpiresAtUtc == default ? session.ExpiresAtUtc : refreshed.ExpiresAtUtc,
                    UserName = string.IsNullOrWhiteSpace(refreshed.Name) ? session.UserName : refreshed.Name.Trim(),
                    Role = string.IsNullOrWhiteSpace(refreshed.Role) ? session.Role : refreshed.Role.Trim()
                };
                authSessionStore.Save(refreshedSession);
                return LanApiAuthSessionResolution.From(refreshedSession);
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                authSessionStore.Clear();
                return LanApiAuthSessionResolution.None;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Keep the current token for this request if refresh timed out.
        }
        catch
        {
            // Keep the current token for this request on transient refresh failure.
        }

        return LanApiAuthSessionResolution.From(session);
    }

    private static bool ShouldRefresh(LanApiAuthSession session)
    {
        if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
            return false;

        if (session.ExpiresAtUtc == default)
            return false;

        return session.ExpiresAtUtc - DateTime.UtcNow <= RefreshLeadTime;
    }

    private static bool TryBuildAuthRefreshUri(string apiBaseUrl, out Uri requestUri)
    {
        requestUri = null!;
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            return false;

        if (!Uri.TryCreate(apiBaseUrl.Trim(), UriKind.Absolute, out var baseUri))
            return false;

        requestUri = new Uri(baseUri, "api/auth/refresh");
        return true;
    }

    private static TokenResponsePayload? DeserializeTokenResponse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            return JsonSerializer.Deserialize<TokenResponsePayload>(payload, JsonOptions);
        }
        catch
        {
            return null;
        }
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

    private sealed class RefreshRequestPayload
    {
        public string SessionId { get; set; } = string.Empty;
    }

    private sealed class TokenResponsePayload
    {
        public string AccessToken { get; set; } = string.Empty;
        public DateTime ExpiresAtUtc { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
    }
}

internal readonly struct LanApiAuthSessionResolution
{
    private LanApiAuthSessionResolution(bool hasSession, LanApiAuthSession session)
    {
        HasSession = hasSession;
        Session = session;
    }

    public bool HasSession { get; }
    public LanApiAuthSession Session { get; }

    public static LanApiAuthSessionResolution None => new(false, new LanApiAuthSession());

    public static LanApiAuthSessionResolution From(LanApiAuthSession session)
    {
        if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
            return None;

        return new LanApiAuthSessionResolution(true, session);
    }
}
