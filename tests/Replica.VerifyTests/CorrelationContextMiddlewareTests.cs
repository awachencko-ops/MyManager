using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Replica.Api.Infrastructure;
using Xunit;

namespace Replica.VerifyTests;

public sealed class CorrelationContextMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_UsesIncomingCorrelationHeader()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationContextMiddleware.CorrelationHeaderName] = "corr-123";

        var middleware = new CorrelationContextMiddleware(
            _ => Task.CompletedTask,
            NullLogger<CorrelationContextMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal("corr-123", context.TraceIdentifier);
        Assert.Equal("corr-123", context.Response.Headers[CorrelationContextMiddleware.CorrelationHeaderName].ToString());
    }

    [Fact]
    public async Task InvokeAsync_WhenHeaderMissing_GeneratesCorrelationId()
    {
        var context = new DefaultHttpContext();

        var middleware = new CorrelationContextMiddleware(
            _ => Task.CompletedTask,
            NullLogger<CorrelationContextMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.StartsWith("replica-", context.TraceIdentifier, StringComparison.Ordinal);
        Assert.Equal(context.TraceIdentifier, context.Response.Headers[CorrelationContextMiddleware.CorrelationHeaderName].ToString());
    }
}
