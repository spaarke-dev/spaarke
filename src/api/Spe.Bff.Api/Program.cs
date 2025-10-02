using Spe.Bff.Api.Infrastructure.Graph;
using Spe.Bff.Api.Infrastructure.Errors;
using Spe.Bff.Api.Infrastructure.Resilience;
using Spe.Bff.Api.Infrastructure.Validation;
using Spe.Bff.Api.Infrastructure.DI;
using Spe.Bff.Api.Infrastructure.Authorization;
using Spe.Bff.Api.Infrastructure.Startup;
using Spe.Bff.Api.Configuration;
using Spe.Bff.Api.Models;
using Spe.Bff.Api.Api;
using System.Threading.RateLimiting;
using Microsoft.Graph;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Polly;
using Spaarke.Dataverse;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration Validation ----

// Register and validate configuration options with fail-fast behavior
builder.Services
    .AddOptions<GraphOptions>()
    .Bind(builder.Configuration.GetSection(GraphOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<DataverseOptions>()
    .Bind(builder.Configuration.GetSection(DataverseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<ServiceBusOptions>()
    .Bind(builder.Configuration.GetSection(ServiceBusOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<RedisOptions>()
    .Bind(builder.Configuration.GetSection(RedisOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Custom validation for conditional requirements
builder.Services.AddSingleton<IValidateOptions<GraphOptions>, GraphOptionsValidator>();

// Add startup health check to validate configuration
builder.Services.AddHostedService<StartupValidationService>();

// ---- Services Registration ----

// Cross-cutting concerns
// builder.Services.AddProblemDetails(); // Requires newer version


// Core module (AuthorizationService, RequestCache)
builder.Services.AddSpaarkeCore();

// Register authorization handler (Scoped to match AuthorizationService dependency)
builder.Services.AddScoped<IAuthorizationHandler, ResourceAccessHandler>();

// Authorization policies - granular operation-level policies matching SPE/Graph API operations
// Each policy maps to a specific OperationAccessPolicy operation with required Dataverse AccessRights
builder.Services.AddAuthorization(options =>
{
    // ====================================================================================
    // DRIVEITEM CONTENT OPERATIONS (most common user operations)
    // ====================================================================================
    options.AddPolicy("canpreviewfiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.preview")));

    options.AddPolicy("candownloadfiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.content.download")));

    options.AddPolicy("canuploadfiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.content.upload")));

    options.AddPolicy("canreplacefiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.content.replace")));

    // ====================================================================================
    // DRIVEITEM METADATA OPERATIONS
    // ====================================================================================
    options.AddPolicy("canreadmetadata", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.get")));

    options.AddPolicy("canupdatemetadata", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.update")));

    options.AddPolicy("canlistchildren", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.list.children")));

    // ====================================================================================
    // DRIVEITEM FILE MANAGEMENT
    // ====================================================================================
    options.AddPolicy("candeletefiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.delete")));

    options.AddPolicy("canmovefiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.move")));

    options.AddPolicy("cancopyfiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.copy")));

    options.AddPolicy("cancreatefolders", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.create.folder")));

    // ====================================================================================
    // DRIVEITEM SHARING & PERMISSIONS
    // ====================================================================================
    options.AddPolicy("cansharefiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.createlink")));

    options.AddPolicy("canmanagefilepermissions", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.permissions.add")));

    // ====================================================================================
    // DRIVEITEM VERSIONING
    // ====================================================================================
    options.AddPolicy("canviewversions", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.versions.list")));

    options.AddPolicy("canrestoreversions", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.versions.restore")));

    // ====================================================================================
    // CONTAINER OPERATIONS
    // ====================================================================================
    options.AddPolicy("canlistcontainers", p =>
        p.Requirements.Add(new ResourceAccessRequirement("container.list")));

    options.AddPolicy("cancreatecontainers", p =>
        p.Requirements.Add(new ResourceAccessRequirement("container.create")));

    options.AddPolicy("candeletecontainers", p =>
        p.Requirements.Add(new ResourceAccessRequirement("container.delete")));

    options.AddPolicy("canupdatecontainers", p =>
        p.Requirements.Add(new ResourceAccessRequirement("container.update")));

    options.AddPolicy("canmanagecontainerpermissions", p =>
        p.Requirements.Add(new ResourceAccessRequirement("container.permissions.add")));

    // ====================================================================================
    // ADVANCED OPERATIONS (less common, admin-level)
    // ====================================================================================
    options.AddPolicy("cansearchfiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.search")));

    options.AddPolicy("cantrackchanges", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.delta")));

    options.AddPolicy("canmanagecompliancelabels", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.sensitivitylabel.assign")));

    // ====================================================================================
    // LEGACY COMPATIBILITY (backward compatible with old operation names)
    // ====================================================================================
    options.AddPolicy("canreadfiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("preview_file")));

    options.AddPolicy("canwritefiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("upload_file")));

    options.AddPolicy("canmanagecontainers", p =>
        p.Requirements.Add(new ResourceAccessRequirement("create_container")));
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

// Background Job Processing (ADR-004) - Unified Strategy
var useServiceBus = builder.Configuration.GetValue<bool>("Jobs:UseServiceBus", true);

// Always register JobSubmissionService (unified entry point)
builder.Services.AddSingleton<Spe.Bff.Api.Services.Jobs.JobSubmissionService>();

// Register job handlers (used by both processors)
builder.Services.AddScoped<Spe.Bff.Api.Services.Jobs.IJobHandler, Spe.Bff.Api.Services.Jobs.Handlers.DocumentProcessingJobHandler>();
// TODO: Register additional IJobHandler implementations here

if (useServiceBus)
{
    // Production: Service Bus mode
    var serviceBusConnectionString = builder.Configuration.GetConnectionString("ServiceBus");
    if (!string.IsNullOrWhiteSpace(serviceBusConnectionString))
    {
        builder.Services.AddSingleton(sp => new Azure.Messaging.ServiceBus.ServiceBusClient(serviceBusConnectionString));
        builder.Services.AddHostedService<Spe.Bff.Api.Services.Jobs.ServiceBusJobProcessor>();
        builder.Logging.AddConsole();
        Console.WriteLine("✓ Job processing configured with Service Bus (queue: sdap-jobs)");
    }
    else
    {
        throw new InvalidOperationException(
            "Jobs:UseServiceBus is true but ServiceBus:ConnectionString is not configured. " +
            "Either configure Service Bus or set Jobs:UseServiceBus=false for development.");
    }
}
else
{
    // Development: In-memory mode
    builder.Services.AddSingleton<Spe.Bff.Api.Services.BackgroundServices.JobProcessor>();
    builder.Services.AddHostedService<Spe.Bff.Api.Services.BackgroundServices.JobProcessor>(sp =>
        sp.GetRequiredService<Spe.Bff.Api.Services.BackgroundServices.JobProcessor>());
    Console.WriteLine("⚠️ Job processing configured with In-Memory queue (DEVELOPMENT ONLY - not durable)");
}

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

// Permissions endpoints (for UI to query user capabilities)
app.MapPermissionsEndpoints();

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