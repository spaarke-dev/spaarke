using Spe.Bff.Api.Infrastructure.Graph;
using Spe.Bff.Api.Infrastructure.Errors;
using Spe.Bff.Api.Infrastructure.Resilience;
using Spe.Bff.Api.Infrastructure.Validation;
using Spe.Bff.Api.Models;
using System.Threading.RateLimiting;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Polly;
using Api; // for MapOBOEndpoints extension
using WebApp = Microsoft.AspNetCore.Builder.WebApplication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Metrics;
using OpenTelemetry;

var builder = WebApp.CreateBuilder(args);

// ---- Services Registration ----

// Memory cache for capabilities caching
builder.Services.AddMemoryCache();

// Authorization policies (replace RequireAssertion with your real mapping)
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("canmanagecontainers", p => p.RequireAssertion(_ => true)); // TODO
    options.AddPolicy("canwritefiles",      p => p.RequireAssertion(_ => true)); // TODO
});

// Graph Client Factory with MI + OBO support
builder.Services.AddSingleton<IGraphClientFactory, Spe.Bff.Api.Infrastructure.Graph.GraphClientFactory>();

// SPE Service (business logic)
builder.Services.AddScoped<ISpeService, SpeService>();

// OBO SPE service (testable abstraction)
builder.Services.AddScoped<Services.IOboSpeService, Services.OboSpeService>();

// CORS for SPA
var allowed = builder.Configuration.GetValue<string>("Cors:AllowedOrigins") ?? "";
builder.Services.AddCors(o =>
{
    o.AddPolicy("spa", p =>
    {
        if (!string.IsNullOrWhiteSpace(allowed))
            p.WithOrigins(allowed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        else
            p.AllowAnyOrigin(); // dev fallback
        p.AllowAnyHeader().AllowAnyMethod();
        p.WithExposedHeaders("request-id","client-request-id","traceparent");
    });
});

// OpenTelemetry (ASP.NET Core + HttpClient). Configure Azure Monitor in cloud via connection string.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName: "spe-bff-api"))
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation();
        t.AddHttpClientInstrumentation(o => o.RecordException = true);
    })
    .WithMetrics(m => { });

// Rate limiting policies for Graph API calls (used in PR-2)
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("graph-write", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User?.Identity?.Name ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromSeconds(10),
                PermitLimit = 10,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("graph-read", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User?.Identity?.Name ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromSeconds(10),
                PermitLimit = 100,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

var app = builder.Build();

// ---- Middleware Pipeline ----

app.UseRateLimiter();
app.UseCors("spa");
app.UseMiddleware<Api.SecurityHeadersMiddleware>();
app.UseAuthorization();

// ---- Health Endpoints ----

// Simple health probe (no allocations)
app.MapGet("/healthz", () => Results.Ok());

// Detailed ping endpoint
app.MapGet("/ping", (HttpContext context) =>
{
    return Results.Ok(new
    {
        ok = true,
        service = "SPE BFF API",
        traceId = context.TraceIdentifier,
        timestamp = DateTimeOffset.UtcNow
    });
});

// ---- User Identity & Entitlements Endpoints ----

// Map user identity endpoints (whoami + capabilities)
app.MapUserEndpoints();

// ---- OBO Endpoints ----

// Map OBO endpoints (user-enforced CRUD)
app.MapOBOEndpoints();

// ---- MI Endpoints ----

// POST /api/containers - Create container (MI)
app.MapPost("/api/containers", async (
    CreateContainerRequest request,
    ISpeService speService,
    ILogger<Program> logger,
    HttpContext context) =>
{
    var traceId = context.TraceIdentifier;

    try
    {
        // Validate request
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return ProblemDetailsHelper.ValidationError("DisplayName is required");

        if (request.ContainerTypeId == Guid.Empty)
            return ProblemDetailsHelper.ValidationError("ContainerTypeId must be a valid GUID");

        logger.LogInformation("Creating container {DisplayName} with type {ContainerTypeId}",
            request.DisplayName, request.ContainerTypeId);

        // Execute with retry policy
        var pipeline = RetryPolicies.GraphTransient<FileStorageContainer?>();
        var result = await pipeline.ExecuteAsync(async () =>
        {
            return await speService.CreateContainerAsync(
                request.ContainerTypeId,
                request.DisplayName,
                request.Description);
        });

        return Results.Created($"/api/containers/{result?.Id}", result);
    }
    catch (ServiceException ex)
    {
        logger.LogError(ex, "Failed to create container");
        return ProblemDetailsHelper.FromGraphException(ex);
    }
})
.RequireRateLimiting("graph-write")
.RequireAuthorization("canmanagecontainers");

// GET /api/containers?containerTypeId= - List containers (MI)
app.MapGet("/api/containers", async (
    Guid? containerTypeId,
    ISpeService speService,
    ILogger<Program> logger,
    HttpContext context) =>
{
    var traceId = context.TraceIdentifier;

    try
    {
        if (!containerTypeId.HasValue || containerTypeId.Value == Guid.Empty)
            return ProblemDetailsHelper.ValidationError("containerTypeId query parameter is required and must be a valid GUID");

        logger.LogInformation("Listing containers for type {ContainerTypeId}", containerTypeId.Value);

        var pipeline = RetryPolicies.GraphTransient<IList<FileStorageContainer>?>();
        var result = await pipeline.ExecuteAsync(async () =>
        {
            return await speService.ListContainersAsync(containerTypeId.Value);
        });

        return Results.Ok(result);
    }
    catch (ServiceException ex)
    {
        logger.LogError(ex, "Failed to list containers");
        return ProblemDetailsHelper.FromGraphException(ex);
    }
})
.RequireRateLimiting("graph-read");

// GET /api/containers/{id}/drive - Get container drive (MI)
app.MapGet("/api/containers/{containerId}/drive", async (
    string containerId,
    ISpeService speService,
    ILogger<Program> logger,
    HttpContext context) =>
{
    var traceId = context.TraceIdentifier;

    try
    {
        if (string.IsNullOrWhiteSpace(containerId))
            return ProblemDetailsHelper.ValidationError("containerId is required");

        logger.LogInformation("Getting drive for container {ContainerId}", containerId);

        var pipeline = RetryPolicies.GraphTransient<Drive?>();
        var result = await pipeline.ExecuteAsync(async () =>
        {
            return await speService.GetContainerDriveAsync(containerId);
        });

        return Results.Ok(result);
    }
    catch (ServiceException ex)
    {
        logger.LogError(ex, "Failed to get container drive");
        return ProblemDetailsHelper.FromGraphException(ex);
    }
})
.RequireRateLimiting("graph-read");

// PUT /api/containers/{id}/files/{*path} - Upload small file (MI)
app.MapPut("/api/containers/{containerId}/files/{*path}", async (
    string containerId, string path, HttpRequest req, ISpeService spe, CancellationToken ct) =>
{
    var (ok, err) = PathValidator.ValidatePath(path);
    if (!ok) return ProblemDetailsHelper.ValidationProblem(new() { ["path"] = new[] { err! } });

    if (req.ContentLength is long len && len > PathValidator.SmallUploadMaxBytes)
        return Results.Problem(statusCode: 413, title: "payload too large",
            detail: $"use upload session for files larger than {PathValidator.SmallUploadMaxBytes} bytes");

    try
    {
        using var ms = new MemoryStream();
        await req.Body.CopyToAsync(ms, ct);
        ms.Position = 0;

        var item = await RetryPolicies.GraphTransient().ExecuteAsync(() =>
            spe.UploadSmallAsync(containerId, path, ms, ct));

        return Results.Ok(item); // or choose 201 with existence preflight
    }
    catch (Microsoft.Graph.ServiceException ex)
    {
        return ProblemDetailsHelper.FromGraphException(ex);
    }
})
.RequireRateLimiting("graph-write")
.RequireAuthorization("canwritefiles");

// POST /api/containers/{containerId}/upload - Create upload session (MI)
app.MapPost("/api/containers/{containerId}/upload", async (string containerId, string path, ISpeService spe, CancellationToken ct) =>
{
    var (ok, err) = PathValidator.ValidatePath(path);
    if (!ok) return ProblemDetailsHelper.ValidationProblem(new() { ["path"] = new[] { err! } });

    try
    {
        var session = await RetryPolicies.GraphTransient().ExecuteAsync(() =>
            spe.CreateUploadSessionAsync(containerId, path, ct));
        return Results.Ok(session);
    }
    catch (Microsoft.Graph.ServiceException ex)
    {
        return ProblemDetailsHelper.FromGraphException(ex);
    }
})
.RequireRateLimiting("graph-write")
.RequireAuthorization("canwritefiles");

// GET /api/drives/{driveId}/children - List drive children (MI)
app.MapGet("/api/drives/{driveId}/children", async (string driveId, string? itemId, ISpeService spe, CancellationToken ct) =>
{
    try
    {
        var items = await RetryPolicies.GraphTransient().ExecuteAsync(() =>
            spe.ListChildrenAsync(driveId, itemId, ct));
        return Results.Ok(items);
    }
    catch (Microsoft.Graph.ServiceException ex)
    {
        return ProblemDetailsHelper.FromGraphException(ex);
    }
})
.RequireRateLimiting("graph-read");

app.Run();

// expose Program for WebApplicationFactory in tests
public partial class Program
{
}