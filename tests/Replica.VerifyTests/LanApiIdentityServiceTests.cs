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
