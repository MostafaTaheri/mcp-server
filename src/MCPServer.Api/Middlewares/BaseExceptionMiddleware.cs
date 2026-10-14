namespace MCPServer.Api.Middlewares;

public abstract class BaseExceptionMiddleware : IMiddleware
{
    private readonly ILogger<BaseExceptionMiddleware> _logger;

    protected BaseExceptionMiddleware(ILogger<BaseExceptionMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext httpContext, RequestDelegate next)
    {
        try
        {
            await next(httpContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Something went wrong: {message}", ex.Message);
            await HandleExceptionAsync(httpContext, ex);
        }
    }

    protected abstract Task HandleExceptionAsync(HttpContext context, Exception exception);
}