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
builder.Services.AddWorkersModule();

// Redis distributed cache (placeholder - requires Microsoft.Extensions.Caching.StackExchangeRedis package)
// builder.Services.AddStackExchangeRedisCache(options =>
// {
//     options.Configuration = builder.Configuration.GetConnectionString("Redis");
// });
builder.Services.AddMemoryCache(); // Temporary fallback

// Singleton GraphServiceClient factory
builder.Services.AddSingleton<IGraphClientFactory, Spe.Bff.Api.Infrastructure.Graph.GraphClientFactory>();

// Health checks
builder.Services.AddHealthChecks();

// CORS for SPA
var allowed = builder.Configuration.GetValue<string>("Cors:AllowedOrigins") ?? "";
builder.Services.AddCors(o =>
{
    o.AddPolicy("spa", p =>
    {
        if (!string.IsNullOrWhiteSpace(allowed))
            p.WithOrigins(allowed.Split(',', StringSplitOptions.RemoveEmptyEntries));
        else
            p.AllowAnyOrigin(); // dev fallback
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

// Document and container management endpoints
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