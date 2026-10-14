using MCPServer.Api;
using MCPServer.Api.Auth;
using MCPServer.Api.Mcp;
using MCPServer.Api.OAuth;
using MCPServer.Api.Middlewares;
using MCPServer.Composition;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;


Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();
    
try
{
    var builder = WebApplication.CreateBuilder(args);
    var configuration = builder.Configuration;

    builder.Host.UseSerilog((ctx, services, cfg) =>
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services));

    // Add services to the container.
    builder.Services.AddServices(configuration);
    builder.Services.AddCompositionServices(configuration);

    var oauthMode = string.Equals(configuration["Jira:AuthMode"], "OAuth", StringComparison.OrdinalIgnoreCase);
    builder.Services.AddMcpServer()
        .WithHttpTransport(o => o.Stateless = true)
        .WithTools<JiraMcpTools>();
    if (oauthMode)
    {
        // SessionBearer validates tokens; the SDK's MCP scheme owns the 401 challenge and the
        // RFC 9728 protected-resource metadata document.
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = SessionBearerDefaults.Scheme;
                options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
            })
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, SessionBearerHandler>(
                SessionBearerDefaults.Scheme, null)
            .AddMcp(options => options.Events.OnResourceMetadataRequest = context =>
            {
                var request = context.HttpContext.Request;
                var baseUrl = $"{request.Scheme}://{request.Host}";
                context.ResourceMetadata = new ProtectedResourceMetadata
                {
                    Resource = $"{baseUrl}/mcp",
                    AuthorizationServers = [baseUrl],
                    BearerMethodsSupported = ["header"]
                };
                return Task.CompletedTask;
            });
        builder.Services.AddAuthorization();
    }

    // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
    builder.Services.AddOpenApi();

    builder.Services.Configure<ApiBehaviorOptions>(options => { options.SuppressModelStateInvalidFilter = true; });

    var app = builder.Build();

    // Structured HTTP request/response logging
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.GetLevel = (ctx, _, ex) =>
            ex != null || ctx.Response.StatusCode >= 500
                ? LogEventLevel.Error
                : ctx.Response.StatusCode >= 400
                    ? LogEventLevel.Warning
                    : LogEventLevel.Information;
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
            diagnosticContext.Set("QueryString", httpContext.Request.QueryString.Value ?? string.Empty);
        };
    });

    app.UseGlobalExceptionHandler();

    // Configure the HTTP request pipeline.
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
    if (oauthMode)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapJiraOAuth();
        app.MapMcp("/mcp").RequireAuthorization();
    }
    else if (app.Environment.IsDevelopment())
    {
        // ApiToken mode: every caller shares one Jira identity, so /mcp is unauthenticated and local-only.
        app.MapMcp("/mcp");
    }

    app.MapControllers();

    // Map Health Check Endpoints
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false,
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

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
