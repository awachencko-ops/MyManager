using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Replica.Shared;
using Xunit;

namespace Replica.VerifyTests;

public sealed class LanApiIdentityServiceTests
{
    [Fact]
    public async Task GetCurrentUserAsync_SendsActorHeaderAndParsesPayload()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"name\":\"Andrew\",\"role\":\"Admin\",\"isAuthenticated\":true,\"isValidated\":true,\"canManageUsers\":true}",
                    Encoding.UTF8,
                    "application/json")
            }));

        var service = new LanApiIdentityService(new HttpClient(handler));
        var result = await service.GetCurrentUserAsync("http://localhost:5000/", "Andrew");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.User);
        Assert.Equal("Andrew", result.User!.Name);
        Assert.Equal("Admin", result.User.Role);
        Assert.True(result.User.CanManageUsers);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/api/auth/me", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.True(handler.LastRequest.Headers.TryGetValues(CurrentUserHeaderCodec.HeaderName, out var actors));
        Assert.Equal("Andrew", actors.Single());
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithNonAsciiActor_SendsEncodedAndAsciiFallbackHeaders()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"name\":\"Сергей\",\"role\":\"Operator\",\"isAuthenticated\":true,\"isValidated\":true,\"canManageUsers\":false}",
                    Encoding.UTF8,
                    "application/json")
            }));

        var service = new LanApiIdentityService(new HttpClient(handler));
        const string actor = "Сергей";

        var result = await service.GetCurrentUserAsync("http://localhost:5000/", actor);

        Assert.True(result.IsSuccess);
        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.TryGetValues(CurrentUserHeaderCodec.HeaderName, out var fallbackActors));
        Assert.Equal(CurrentUserHeaderCodec.BuildAsciiFallback(actor), fallbackActors.Single());
        Assert.True(handler.LastRequest.Headers.TryGetValues(CurrentUserHeaderCodec.EncodedHeaderName, out var encodedActors));
        Assert.True(CurrentUserHeaderCodec.TryDecode(encodedActors.Single(), out var decoded));
        Assert.Equal(actor, decoded);
    }

    [Fact]
    public async Task GetCurrentUserAsync_Unauthorized_ReturnsUnauthorized()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(
                    "{\"error\":\"X-Current-User header is required\"}",
                    Encoding.UTF8,
                    "application/json")
            }));

        var service = new LanApiIdentityService(new HttpClient(handler));
        var result = await service.GetCurrentUserAsync("http://localhost:5000/", actor: string.Empty);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsUnauthorized);
        Assert.Contains("header is required", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithBootstrap_LogsInAndUsesBearerSession()
    {
        var authSessionStore = new InMemoryLanApiAuthSessionStore();
        var requestCount = 0;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            requestCount++;
            if (request.RequestUri!.AbsolutePath == "/api/auth/me" && requestCount == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("{\"error\":\"unauthorized\"}", Encoding.UTF8, "application/json")
                });
            }

            if (request.RequestUri.AbsolutePath == "/api/auth/login")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"accessToken\":\"rpl1.session-1.secret\",\"expiresAtUtc\":\"2030-01-01T00:00:00Z\",\"name\":\"Andrew\",\"role\":\"Admin\",\"sessionId\":\"session-1\"}",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            if (request.RequestUri.AbsolutePath == "/api/auth/me" && requestCount == 3)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"name\":\"Andrew\",\"role\":\"Admin\",\"isAuthenticated\":true,\"isValidated\":true,\"canManageUsers\":true,\"authScheme\":\"Bearer\",\"sessionId\":\"session-1\"}",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        });

        var service = new LanApiIdentityService(new HttpClient(handler), authSessionStore);
        var result = await service.GetCurrentUserAsync("http://localhost:5000/", "Andrew", allowSessionBootstrap: true);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.User);
        Assert.Equal("Andrew", result.User!.Name);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("/api/auth/login", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", handler.Requests[2].Headers.Authorization?.Scheme);
        Assert.Equal("rpl1.session-1.secret", handler.Requests[2].Headers.Authorization?.Parameter);
        Assert.True(authSessionStore.TryGetActiveSession(out var session));
        Assert.Equal("session-1", session.SessionId);
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithExpiringSession_RefreshesTokenBeforeMeRequest()
    {
        var authSessionStore = new InMemoryLanApiAuthSessionStore();
        authSessionStore.Save(new LanApiAuthSession
        {
            AccessToken = "rpl1.session-old.secret",
            SessionId = "session-old",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(2),
            UserName = "Andrew",
            Role = "Admin"
        });

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/auth/refresh")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"accessToken\":\"rpl1.session-new.secret\",\"expiresAtUtc\":\"2030-01-01T00:00:00Z\",\"name\":\"Andrew\",\"role\":\"Admin\",\"sessionId\":\"session-old\"}",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            if (request.RequestUri.AbsolutePath == "/api/auth/me")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"name\":\"Andrew\",\"role\":\"Admin\",\"isAuthenticated\":true,\"isValidated\":true,\"canManageUsers\":true,\"authScheme\":\"Bearer\",\"sessionId\":\"session-old\"}",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        });

        var service = new LanApiIdentityService(new HttpClient(handler), authSessionStore);
        var result = await service.GetCurrentUserAsync("http://localhost:5000/", "Andrew", allowSessionBootstrap: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/api/auth/refresh", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", handler.Requests[1].Headers.Authorization?.Scheme);
        Assert.Equal("rpl1.session-new.secret", handler.Requests[1].Headers.Authorization?.Parameter);
        Assert.True(authSessionStore.TryGetActiveSession(out var refreshedSession));
        Assert.Equal("rpl1.session-new.secret", refreshedSession.AccessToken);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public HttpRequestMessage? LastRequest { get; private set; }
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            Requests.Add(request);
            return _handler(request, cancellationToken);
        }
    }
}
