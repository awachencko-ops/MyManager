using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Replica.Shared;
using Xunit;

namespace Replica.VerifyTests;

public sealed class LanOrderRunApiGatewayTests
{
    [Fact]
    public async Task StartRunAsync_SendsRequestAndParsesOrderPayload()
    {
        string requestBody = string.Empty;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"internalId\":\"order-1\",\"status\":\"Processing\",\"version\":42}",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var gateway = new LanOrderRunApiGateway(new HttpClient(handler));
        LanOrderRunApiResult result;
        using (Logger.BeginCorrelationScope("corr-test-123"))
        {
            result = await gateway.StartRunAsync(
                "http://localhost:5000/",
                "order-1",
                expectedOrderVersion: 12,
                actor: "operator-1");
        }

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Order);
        Assert.Equal(42, result.Order!.Version);
        Assert.Equal("Processing", result.Order.Status);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/orders/order-1/run", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.True(handler.LastRequest.Headers.TryGetValues("X-Current-User", out var actors));
        Assert.Equal("operator-1", actors.Single());
        Assert.True(handler.LastRequest.Headers.TryGetValues("X-Correlation-Id", out var correlations));
        Assert.Equal("corr-test-123", correlations.Single());
        Assert.True(handler.LastRequest.Headers.TryGetValues("Idempotency-Key", out var idempotencyKeys));
        Assert.StartsWith("replica-run-", idempotencyKeys.Single(), StringComparison.Ordinal);
        Assert.Contains("\"expectedOrderVersion\":12", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopRunAsync_Conflict_ReturnsCurrentVersion()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(
                    "{\"error\":\"run already active\",\"currentVersion\":77}",
                    Encoding.UTF8,
                    "application/json")
            }));

        var gateway = new LanOrderRunApiGateway(new HttpClient(handler));
        var result = await gateway.StopRunAsync(
            "http://localhost:5000/",
            "order-2",
            expectedOrderVersion: 70,
            actor: "operator-2");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsConflict);
        Assert.Equal(77, result.CurrentVersion);
        Assert.Contains("run already active", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartRunAsync_WithNonAsciiActor_SendsEncodedAndAsciiFallbackHeaders()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"internalId\":\"order-ru\",\"status\":\"Processing\",\"version\":13}",
                    Encoding.UTF8,
                    "application/json")
            }));

        var gateway = new LanOrderRunApiGateway(new HttpClient(handler));
        const string actorName = "\u0421\u0435\u0440\u0433\u0435\u0439";
        var result = await gateway.StartRunAsync(
            "http://localhost:5000/",
            "order-ru",
            expectedOrderVersion: 12,
            actor: actorName);

        Assert.True(result.IsSuccess);
        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.TryGetValues(CurrentUserHeaderCodec.HeaderName, out var fallbackActors));
        Assert.Equal(CurrentUserHeaderCodec.BuildAsciiFallback(actorName), fallbackActors.Single());
        Assert.True(handler.LastRequest.Headers.TryGetValues(CurrentUserHeaderCodec.EncodedHeaderName, out var encodedActors));
        Assert.True(CurrentUserHeaderCodec.TryDecode(encodedActors.Single(), out var actor));
        Assert.Equal(actorName, actor);
    }

    [Fact]
    public async Task StartRunAsync_WithInvalidBaseUrl_ReturnsUnavailable()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("request should not be executed"));

        var gateway = new LanOrderRunApiGateway(new HttpClient(handler));
        var result = await gateway.StartRunAsync(
            "not-a-url",
            "order-3",
            expectedOrderVersion: 1,
            actor: "operator-3");

        Assert.True(result.IsUnavailable);
        Assert.Contains("invalid LAN API base URL", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task StartRunAsync_WithStoredBearerToken_UsesAuthorizationHeader()
    {
        var authSessionStore = new InMemoryLanApiAuthSessionStore();
        authSessionStore.Save(new LanApiAuthSession
        {
            AccessToken = "rpl1.session-3.secret",
            SessionId = "session-3",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
            UserName = "operator-3",
            Role = "Operator"
        });

        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"internalId\":\"order-3\",\"status\":\"Processing\",\"version\":2}",
                    Encoding.UTF8,
                    "application/json")
            }));

        var gateway = new LanOrderRunApiGateway(new HttpClient(handler), authSessionStore);
        var result = await gateway.StartRunAsync(
            "http://localhost:5000/",
            "order-3",
            expectedOrderVersion: 1,
            actor: "operator-3");

        Assert.True(result.IsSuccess);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("rpl1.session-3.secret", handler.LastRequest.Headers.Authorization?.Parameter);
        Assert.False(handler.LastRequest.Headers.Contains(CurrentUserHeaderCodec.HeaderName));
    }

    [Fact]
    public async Task StartRunAsync_WithExpiringSession_RefreshesTokenBeforeCommandRequest()
    {
        var authSessionStore = new InMemoryLanApiAuthSessionStore();
        authSessionStore.Save(new LanApiAuthSession
        {
            AccessToken = "rpl1.session-old.secret",
            SessionId = "session-old",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(1),
            UserName = "operator-9",
            Role = "Operator"
        });

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/auth/refresh")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"accessToken\":\"rpl1.session-new.secret\",\"expiresAtUtc\":\"2030-01-01T00:00:00Z\",\"name\":\"operator-9\",\"role\":\"Operator\",\"sessionId\":\"session-old\"}",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            if (request.RequestUri.AbsolutePath == "/api/orders/order-9/run")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"internalId\":\"order-9\",\"status\":\"Processing\",\"version\":4}",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        });

        var gateway = new LanOrderRunApiGateway(new HttpClient(handler), authSessionStore);
        var result = await gateway.StartRunAsync(
            "http://localhost:5000/",
            "order-9",
            expectedOrderVersion: 3,
            actor: "operator-9");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/api/auth/refresh", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("/api/orders/order-9/run", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", handler.Requests[1].Headers.Authorization?.Scheme);
        Assert.Equal("rpl1.session-new.secret", handler.Requests[1].Headers.Authorization?.Parameter);
        Assert.False(handler.Requests[1].Headers.Contains(CurrentUserHeaderCodec.HeaderName));
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
        public System.Collections.Generic.List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            Requests.Add(request);
            return _handler(request, cancellationToken);
        }
    }
}

