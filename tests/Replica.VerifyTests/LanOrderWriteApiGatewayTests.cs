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
