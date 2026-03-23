using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return _handler(request, cancellationToken);
        }
    }
}
