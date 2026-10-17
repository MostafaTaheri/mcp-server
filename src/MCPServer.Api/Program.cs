using MCPServer.Api;
using MCPServer.Api.Middlewares;
using MCPServer.Composition;
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    var configuration = builder.Configuration;

    builder.Host.UseSerilog((ctx, services, cfg) =>
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services), preserveStaticLogger: true);

    builder.Services.AddServices(configuration);
    builder.Services.AddCompositionServices(configuration);
    builder.Services.AddMcpServices();
    builder.Services.AddOpenApi();
    builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);

    var app = builder.Build();

    app.UseRequestLogging();
    app.UseGlobalExceptionHandler();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            options.Title = "MT MCP Server API";
            options.OpenApiRoutePattern = "/openapi/v1.json";
        });
    }

    if (app.Environment.IsProduction()) app.UseHsts();

    app.UseHttpsRedirection();
    app.UseRouting();

    app.MapControllers();
    app.MapHealthEndpoints();


    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
