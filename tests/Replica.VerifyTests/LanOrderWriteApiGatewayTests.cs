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

public sealed class LanOrderWriteApiGatewayTests
{
    [Fact]
    public async Task CreateOrderAsync_SendsRequestAndParsesOrderPayload()
    {
        string requestBody = string.Empty;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    "{\"internalId\":\"order-1\",\"orderNumber\":\"1001\",\"status\":\"Waiting\",\"version\":5}",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var gateway = new LanOrderWriteApiGateway(new HttpClient(handler));
        LanOrderWriteApiResult result;
        using (Logger.BeginCorrelationScope("corr-write-123"))
        {
            result = await gateway.CreateOrderAsync(
                "http://localhost:5000/",
                new LanCreateOrderRequest
                {
                    OrderNumber = "1001",
                    UserName = "operator-1"
                },
                actor: "operator-1");
        }

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Order);
        Assert.Equal("order-1", result.Order!.InternalId);
        Assert.Equal(5, result.Order.Version);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/orders", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.True(handler.LastRequest.Headers.TryGetValues("X-Current-User", out var actors));
        Assert.Equal("operator-1", actors.Single());
        Assert.True(handler.LastRequest.Headers.TryGetValues("X-Correlation-Id", out var correlations));
        Assert.Equal("corr-write-123", correlations.Single());
        Assert.True(handler.LastRequest.Headers.TryGetValues("Idempotency-Key", out var idempotencyKeys));
        Assert.StartsWith("replica-write-", idempotencyKeys.Single(), StringComparison.Ordinal);
        Assert.Contains("\"orderNumber\":\"1001\"", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateOrderAsync_Conflict_ReturnsCurrentVersion()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(
                    "{\"error\":\"order version mismatch\",\"currentVersion\":11}",
                    Encoding.UTF8,
                    "application/json")
            }));

        var gateway = new LanOrderWriteApiGateway(new HttpClient(handler));
        var result = await gateway.UpdateOrderAsync(
            "http://localhost:5000/",
            "order-2",
            new LanUpdateOrderRequest
            {
                ExpectedVersion = 10,
                OrderNumber = "1002"
            },
            actor: "operator-2");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsConflict);
        Assert.Equal(11, result.CurrentVersion);
        Assert.Contains("version", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteOrderAsync_SendsDeleteToOrderEndpoint()
    {
        string requestBody = string.Empty;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"internalId\":\"order-2\",\"orderNumber\":\"1002\",\"status\":\"Waiting\",\"version\":12}",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var gateway = new LanOrderWriteApiGateway(new HttpClient(handler));
        var result = await gateway.DeleteOrderAsync(
            "http://localhost:5000/",
            "order-2",
            new LanDeleteOrderRequest
            {
                ExpectedVersion = 11
            },
            actor: "operator-2");

        Assert.True(result.IsSuccess);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
        Assert.Equal("/api/orders/order-2", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"expectedVersion\":11", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReorderOrderItemsAsync_SendsRequestToReorderEndpoint()
    {
        string requestBody = string.Empty;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"internalId\":\"order-3\",\"version\":9,\"items\":[{\"itemId\":\"i2\",\"sequenceNo\":0},{\"itemId\":\"i1\",\"sequenceNo\":1}]}",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var gateway = new LanOrderWriteApiGateway(new HttpClient(handler));
        var result = await gateway.ReorderOrderItemsAsync(
            "http://localhost:5000/",
            "order-3",
            new LanReorderOrderItemsRequest
            {
                ExpectedOrderVersion = 8,
                OrderedItemIds = { "i2", "i1" }
            },
            actor: "operator-3");

        Assert.True(result.IsSuccess);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/orders/order-3/items/reorder", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"expectedOrderVersion\":8", requestBody, StringComparison.Ordinal);
        Assert.Contains("\"orderedItemIds\":[\"i2\",\"i1\"]", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddOrderItemAsync_SendsRequestToItemsEndpoint()
    {
        string requestBody = string.Empty;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"internalId\":\"order-4\",\"version\":13,\"items\":[{\"itemId\":\"i-1\",\"sequenceNo\":0,\"version\":2}]}",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var gateway = new LanOrderWriteApiGateway(new HttpClient(handler));
        var result = await gateway.AddOrderItemAsync(
            "http://localhost:5000/",
            "order-4",
            new LanAddOrderItemRequest
            {
                ExpectedOrderVersion = 12,
                Item = new Replica.Shared.Models.SharedOrderItem
                {
                    ItemId = "i-1",
                    SequenceNo = 0
                }
            },
            actor: "operator-4");

        Assert.True(result.IsSuccess);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/orders/order-4/items", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"expectedOrderVersion\":12", requestBody, StringComparison.Ordinal);
        Assert.Contains("\"itemId\":\"i-1\"", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateOrderItemAsync_SendsPatchToItemEndpoint()
    {
        string requestBody = string.Empty;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"internalId\":\"order-5\",\"version\":18,\"items\":[{\"itemId\":\"i-5\",\"sequenceNo\":0,\"version\":7}]}",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var gateway = new LanOrderWriteApiGateway(new HttpClient(handler));
        var result = await gateway.UpdateOrderItemAsync(
            "http://localhost:5000/",
            "order-5",
            "i-5",
            new LanUpdateOrderItemRequest
            {
                ExpectedOrderVersion = 17,
                ExpectedItemVersion = 6,
                FileStatus = "Waiting"
            },
            actor: "operator-5");

        Assert.True(result.IsSuccess);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
        Assert.Equal("/api/orders/order-5/items/i-5", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"expectedOrderVersion\":17", requestBody, StringComparison.Ordinal);
        Assert.Contains("\"expectedItemVersion\":6", requestBody, StringComparison.Ordinal);
        Assert.Contains("\"fileStatus\":\"Waiting\"", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteOrderItemAsync_SendsDeleteToItemEndpoint()
    {
        string requestBody = string.Empty;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"internalId\":\"order-6\",\"version\":19,\"items\":[{\"itemId\":\"i-6b\",\"sequenceNo\":0,\"version\":2}]}",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var gateway = new LanOrderWriteApiGateway(new HttpClient(handler));
        var result = await gateway.DeleteOrderItemAsync(
            "http://localhost:5000/",
            "order-6",
            "i-6a",
            new LanDeleteOrderItemRequest
            {
                ExpectedOrderVersion = 18,
                ExpectedItemVersion = 4
            },
            actor: "operator-6");

        Assert.True(result.IsSuccess);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
        Assert.Equal("/api/orders/order-6/items/i-6a", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"expectedOrderVersion\":18", requestBody, StringComparison.Ordinal);
        Assert.Contains("\"expectedItemVersion\":4", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateOrderAsync_WithNonAsciiActor_SendsEncodedAndAsciiFallbackHeaders()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    "{\"internalId\":\"order-7\",\"orderNumber\":\"1007\",\"status\":\"Waiting\",\"version\":1}",
                    Encoding.UTF8,
                    "application/json")
            }));

        var gateway = new LanOrderWriteApiGateway(new HttpClient(handler));
        const string actorName = "\u0421\u0435\u0440\u0433\u0435\u0439";
        var result = await gateway.CreateOrderAsync(
            "http://localhost:5000/",
            new LanCreateOrderRequest
            {
                OrderNumber = "1007",
                UserName = actorName
            },
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
    public async Task CreateOrderAsync_WithInvalidBaseUrl_ReturnsUnavailable()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("request should not be executed"));

        var gateway = new LanOrderWriteApiGateway(new HttpClient(handler));
        var result = await gateway.CreateOrderAsync(
            "not-a-url",
            new LanCreateOrderRequest { OrderNumber = "1003" },
            actor: "operator-3");

        Assert.True(result.IsUnavailable);
        Assert.Contains("invalid LAN API base URL", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task CreateOrderAsync_WithStoredBearerToken_UsesAuthorizationHeader()
    {
        var authSessionStore = new InMemoryLanApiAuthSessionStore();
        authSessionStore.Save(new LanApiAuthSession
        {
            AccessToken = "rpl1.session-2.secret",
            SessionId = "session-2",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(20),
            UserName = "operator-7",
            Role = "Operator"
        });

        var handler = new StubHttpMessageHandler((request, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    "{\"internalId\":\"order-8\",\"orderNumber\":\"1008\",\"status\":\"Waiting\",\"version\":1}",
                    Encoding.UTF8,
                    "application/json")
            }));

        var gateway = new LanOrderWriteApiGateway(new HttpClient(handler), authSessionStore);
        var result = await gateway.CreateOrderAsync(
            "http://localhost:5000/",
            new LanCreateOrderRequest
            {
                OrderNumber = "1008",
                UserName = "operator-7"
            },
            actor: "operator-7");

        Assert.True(result.IsSuccess);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("rpl1.session-2.secret", handler.LastRequest.Headers.Authorization?.Parameter);
        Assert.False(handler.LastRequest.Headers.Contains(CurrentUserHeaderCodec.HeaderName));
    }

    [Fact]
    public async Task CreateOrderAsync_WithExpiringSession_RefreshesTokenBeforeWriteRequest()
    {
        var authSessionStore = new InMemoryLanApiAuthSessionStore();
        authSessionStore.Save(new LanApiAuthSession
        {
            AccessToken = "rpl1.session-old.secret",
            SessionId = "session-old",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(2),
            UserName = "operator-8",
            Role = "Operator"
        });

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/auth/refresh")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"accessToken\":\"rpl1.session-new.secret\",\"expiresAtUtc\":\"2030-01-01T00:00:00Z\",\"name\":\"operator-8\",\"role\":\"Operator\",\"sessionId\":\"session-old\"}",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            if (request.RequestUri.AbsolutePath == "/api/orders")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        "{\"internalId\":\"order-9\",\"orderNumber\":\"1009\",\"status\":\"Waiting\",\"version\":1}",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        });

        var gateway = new LanOrderWriteApiGateway(new HttpClient(handler), authSessionStore);
        var result = await gateway.CreateOrderAsync(
            "http://localhost:5000/",
            new LanCreateOrderRequest
            {
                OrderNumber = "1009",
                UserName = "operator-8"
            },
            actor: "operator-8");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/api/auth/refresh", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("/api/orders", handler.Requests[1].RequestUri!.AbsolutePath);
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

