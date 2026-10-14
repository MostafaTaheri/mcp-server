using Microsoft.Extensions.Primitives;
using Serilog.Context;

namespace MCPServer.Api.Middlewares;

public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationIdProvider correlationIdProvider)
    {
        context.Request.Headers.TryGetValue("X-Correlation-Id", out StringValues correlationIds);
        var id = correlationIds.FirstOrDefault() ?? Guid.NewGuid().ToString("N");
        correlationIdProvider.SetCurrentCorrelationId(id);
        
        using (LogContext.PushProperty("CorrelationId", id))
        {
            await _next(context);
        }
    }
}