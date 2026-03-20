using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Replica.Api.Infrastructure;

public sealed class CorrelationContextMiddleware
{
    public const string CorrelationHeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationContextMiddleware> _logger;

    public CorrelationContextMiddleware(RequestDelegate next, ILogger<CorrelationContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context.Request.Headers);
        context.TraceIdentifier = correlationId;
        context.Response.Headers[CorrelationHeaderName] = correlationId;

        var startedAt = Stopwatch.GetTimestamp();
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["correlation_id"] = correlationId,
            ["request_method"] = context.Request.Method,
            ["request_path"] = context.Request.Path.ToString()
        }))
        {
            _logger.LogInformation("HTTP request started");

            try
            {
                await _next(context);
            }
            finally
            {
                var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                _logger.LogInformation(
                    "HTTP request completed with status {StatusCode} in {ElapsedMs:0.###} ms",
                    context.Response.StatusCode,
                    elapsedMs);
            }
        }
    }

    internal static string ResolveCorrelationId(IHeaderDictionary headers)
    {
        if (headers.TryGetValue(CorrelationHeaderName, out var rawHeader))
        {
            var value = rawHeader.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return value.Length <= 128 ? value : value[..128];
        }

        return $"replica-{Guid.NewGuid():N}";
    }
}

public static class CorrelationContextMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationContext(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationContextMiddleware>();
    }
}
