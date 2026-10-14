namespace MCPServer.Api.Middlewares;

public static class MiddlwareRegistration
{
    public static void UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<ExceptionsMiddleware>();
    }  
}