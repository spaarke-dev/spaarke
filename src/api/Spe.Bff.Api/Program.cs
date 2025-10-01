using Spe.Bff.Api.Infrastructure.Graph;
using Spe.Bff.Api.Infrastructure.Errors;
using Spe.Bff.Api.Infrastructure.Resilience;
using Spe.Bff.Api.Infrastructure.Validation;
using Spe.Bff.Api.Infrastructure.DI;
using Spe.Bff.Api.Models;
using Spe.Bff.Api.Api;
using System.Threading.RateLimiting;
using Microsoft.Graph;
using Polly;
using Spaarke.Dataverse;

var builder = WebApplication.CreateBuilder(args);

// ---- Services Registration ----

// Cross-cutting concerns
// builder.Services.AddProblemDetails(); // Requires newer version


// Core module (AuthorizationService, RequestCache)
builder.Services.AddSpaarkeCore();

// Authorization policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("canmanagecontainers", p => p.RequireAssertion(_ => true)); // TODO
    options.AddPolicy("canwritefiles", p => p.RequireAssertion(_ => true)); // TODO
});

// Documents module (endpoints + filters)
builder.Services.AddDocumentsModule();

// Workers module (Service Bus + BackgroundService)
builder.Services.AddWorkersModule(builder.Configuration);

// Distributed cache for idempotency tracking (ADR-004)
// Production: Use Redis - requires Microsoft.Extensions.Caching.StackExchangeRedis package
// builder.Services.AddStackExchangeRedisCache(options =>
// {
//     options.Configuration = builder.Configuration.GetConnectionString("Redis");
// });
// Development: Use distributed memory cache
builder.Services.AddDistributedMemoryCache();
builder.Services.AddMemoryCache();

// Singleton GraphServiceClient factory
builder.Services.AddSingleton<IGraphClientFactory, Spe.Bff.Api.Infrastructure.Graph.GraphClientFactory>();

// Dataverse service - using Web API for .NET 8.0 compatibility with IHttpClientFactory
builder.Services.AddHttpClient<IDataverseService, DataverseWebApiService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Health checks
builder.Services.AddHealthChecks();

// CORS for SPA
var allowed = builder.Configuration.GetValue<string>("Cors:AllowedOrigins") ?? "";
builder.Services.AddCors(o =>
{
    o.AddPolicy("spa", p =>
    {
        if (!string.IsNullOrWhiteSpace(allowed))
        {
            p.WithOrigins(allowed.Split(',', StringSplitOptions.RemoveEmptyEntries))
             .AllowCredentials(); // Required for credentials: 'include' in JavaScript
        }
        else
        {
            p.AllowAnyOrigin(); // dev fallback (cannot use AllowCredentials with AllowAnyOrigin)
        }
        p.AllowAnyHeader().AllowAnyMethod();
        p.WithExposedHeaders("request-id", "client-request-id", "traceparent");
    });
});

// TODO: Rate limiting - API needs to be updated for .NET 8
// builder.Services.AddRateLimiter(options =>
// {
//     options.AddTokenBucketLimiter("graph-write", limiter =>
//     {
//         limiter.TokenLimit = 10;
//         limiter.TokensPerPeriod = 10;
//         limiter.ReplenishmentPeriod = TimeSpan.FromSeconds(10);
//         limiter.QueueLimit = 5;
//     });

//     options.AddTokenBucketLimiter("graph-read", limiter =>
//     {
//         limiter.TokenLimit = 100;
//         limiter.TokensPerPeriod = 100;
//         limiter.ReplenishmentPeriod = TimeSpan.FromSeconds(10);
//         limiter.QueueLimit = 10;
//     });

//     options.OnRejected = async (context, ct) =>
//     {
//         context.HttpContext.Response.StatusCode = 429;
//         await context.HttpContext.Response.WriteAsync("Rate limit exceeded", ct);
//     };
// });

// TODO: OpenTelemetry - API needs to be updated for .NET 8
// builder.Services.AddOpenTelemetry()
//     .WithTracing(tracing =>
//     {
//         tracing.AddAspNetCoreInstrumentation();
//         tracing.AddHttpClientInstrumentation();
//     });

var app = builder.Build();

// ---- Middleware Pipeline ----

// Cross-cutting: CORS
app.UseCors("spa");
// TODO: app.UseRateLimiter(); // Disabled until rate limiting API is fixed
app.UseMiddleware<Api.SecurityHeadersMiddleware>();
app.UseAuthorization();

// ---- Health Endpoints ----

// Health checks endpoints
app.MapHealthChecks("/healthz");

// Dataverse connection test endpoint
app.MapGet("/healthz/dataverse", async (IDataverseService dataverseService) =>
{
    try
    {
        var isConnected = await dataverseService.TestConnectionAsync();
        if (isConnected)
        {
            return Results.Ok(new { status = "healthy", message = "Dataverse connection successful" });
        }
        else
        {
            return Results.Problem(
                detail: "Dataverse connection test failed",
                statusCode: 503,
                title: "Service Unavailable"
            );
        }
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            statusCode: 503,
            title: "Dataverse Connection Error"
        );
    }
});

// Dataverse CRUD operations test endpoint
app.MapGet("/healthz/dataverse/crud", async (IDataverseService dataverseService) =>
{
    try
    {
        var testPassed = await dataverseService.TestDocumentOperationsAsync();
        if (testPassed)
        {
            return Results.Ok(new { status = "healthy", message = "Dataverse CRUD operations successful" });
        }
        else
        {
            return Results.Problem(
                detail: "Dataverse CRUD operations test failed",
                statusCode: 503,
                title: "Service Unavailable"
            );
        }
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            statusCode: 503,
            title: "Dataverse CRUD Test Error"
        );
    }
});

// Detailed ping endpoint
app.MapGet("/ping", (HttpContext context) =>
{
    return Results.Json(new
    {
        service = "Spe.Bff.Api",
        version = "1.0.0",
        environment = app.Environment.EnvironmentName,
        timestamp = DateTimeOffset.UtcNow
    });
});

// ---- Endpoint Groups ----

// User identity and capabilities endpoints
app.MapUserEndpoints();

// Dataverse document CRUD endpoints (Task 1.3)
app.MapDataverseDocumentsEndpoints();

// Document and container management endpoints (SharePoint Embedded)
app.MapDocumentsEndpoints();

// Upload endpoints for file operations
app.MapUploadEndpoints();

// OBO endpoints (user-enforced CRUD)
app.MapOBOEndpoints();

app.Run();

// expose Program for WebApplicationFactory in tests
public partial class Program
{
}