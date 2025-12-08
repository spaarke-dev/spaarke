using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Polly;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Authorization;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Infrastructure.Startup;
using Sprk.Bff.Api.Infrastructure.Validation;
using Sprk.Bff.Api.Models;

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

// Data Access Layer - Document storage resolution (Phase 2 v1.0.5 implementation)
builder.Services.AddScoped<Sprk.Bff.Api.Infrastructure.Dataverse.IDocumentStorageResolver, Sprk.Bff.Api.Infrastructure.Dataverse.DocumentStorageResolver>();

// ============================================================================
// AUTHENTICATION - Azure AD JWT Bearer Token Validation
// ============================================================================
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

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

// ============================================================================
// DISTRIBUTED CACHE - Redis for production, in-memory for local dev (ADR-004, ADR-009)
// ============================================================================
var redisEnabled = builder.Configuration.GetValue<bool>("Redis:Enabled");
if (redisEnabled)
{
    var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
        ?? builder.Configuration["Redis:ConnectionString"];

    if (string.IsNullOrWhiteSpace(redisConnectionString))
    {
        throw new InvalidOperationException(
            "Redis is enabled but no connection string found. " +
            "Set 'ConnectionStrings:Redis' or 'Redis:ConnectionString' in configuration.");
    }

    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = builder.Configuration["Redis:InstanceName"] ?? "sdap:";

        // Connection resilience options for production reliability
        options.ConfigurationOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConnectionString);
        options.ConfigurationOptions.AbortOnConnectFail = false;  // Don't crash if Redis temporarily unavailable
        options.ConfigurationOptions.ConnectTimeout = 5000;       // 5 second connection timeout
        options.ConfigurationOptions.SyncTimeout = 5000;          // 5 second operation timeout
        options.ConfigurationOptions.ConnectRetry = 3;            // Retry connection 3 times
        options.ConfigurationOptions.ReconnectRetryPolicy = new StackExchange.Redis.ExponentialRetry(1000);  // Exponential backoff (1s base)
    });

    builder.Logging.AddSimpleConsole().Services.Configure<Microsoft.Extensions.Logging.Console.SimpleConsoleFormatterOptions>(options =>
    {
        options.TimestampFormat = "HH:mm:ss ";
    });

    var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Program");
    logger.LogInformation(
        "Distributed cache: Redis enabled with instance name '{InstanceName}'",
        builder.Configuration["Redis:InstanceName"] ?? "sdap:");
}
else
{
    // Use in-memory cache for local development only
    builder.Services.AddDistributedMemoryCache();

    var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Program");
    logger.LogWarning(
        "Distributed cache: Using in-memory cache (not distributed). " +
        "This should ONLY be used in local development.");
}

builder.Services.AddMemoryCache();

// Graph API Resilience Configuration (Task 4.1)
builder.Services
    .AddOptions<Sprk.Bff.Api.Configuration.GraphResilienceOptions>()
    .Bind(builder.Configuration.GetSection(Sprk.Bff.Api.Configuration.GraphResilienceOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Register GraphHttpMessageHandler for centralized resilience (retry, circuit breaker, timeout)
builder.Services.AddTransient<Sprk.Bff.Api.Infrastructure.Http.GraphHttpMessageHandler>();

// Configure named HttpClient for Graph API with resilience handler
builder.Services.AddHttpClient("GraphApiClient")
    .AddHttpMessageHandler<Sprk.Bff.Api.Infrastructure.Http.GraphHttpMessageHandler>()
    .ConfigureHttpClient(client =>
    {
        client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
        client.DefaultRequestHeaders.Add("Accept", "application/json");
    });

// Singleton GraphServiceClient factory (now uses IHttpClientFactory with resilience handler)
builder.Services.AddSingleton<IGraphClientFactory, Sprk.Bff.Api.Infrastructure.Graph.GraphClientFactory>();

// Dataverse service - Singleton lifetime for ServiceClient connection reuse (eliminates 500ms initialization overhead)
// ServiceClient is thread-safe and designed for long-lived use with internal connection pooling
builder.Services.AddSingleton<IDataverseService>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<DataverseServiceClientImpl>>();
    return new DataverseServiceClientImpl(configuration, logger);
});

// Background Job Processing (ADR-004) - Service Bus Strategy
// Always register JobSubmissionService (unified entry point)
builder.Services.AddSingleton<Sprk.Bff.Api.Services.Jobs.JobSubmissionService>();

// Register job handlers
builder.Services.AddScoped<Sprk.Bff.Api.Services.Jobs.IJobHandler, Sprk.Bff.Api.Services.Jobs.Handlers.DocumentProcessingJobHandler>();
// TODO: Register additional IJobHandler implementations here

// Configure Service Bus job processing
var serviceBusConnectionString = builder.Configuration.GetConnectionString("ServiceBus");
if (string.IsNullOrWhiteSpace(serviceBusConnectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:ServiceBus is required. " +
        "For local development, use Service Bus emulator (see docs/README-Local-Development.md) " +
        "or configure a dev Service Bus namespace.");
}

builder.Services.AddSingleton(sp => new Azure.Messaging.ServiceBus.ServiceBusClient(serviceBusConnectionString));
builder.Services.AddHostedService<Sprk.Bff.Api.Services.Jobs.ServiceBusJobProcessor>();
builder.Logging.AddConsole();
Console.WriteLine("✓ Job processing configured with Service Bus (queue: sdap-jobs)");

// ============================================================================
// HEALTH CHECKS - Redis availability monitoring
// ============================================================================
//
// TECHNICAL DEBT WARNING: This health check uses BuildServiceProvider() in a lambda,
// which triggers ASP0000 warning. This is intentional technical debt with low risk.
//
// WHY THIS PATTERN EXISTS:
// - Health checks are registered BEFORE app.Build() is called
// - We need to test actual Redis connection, not just configuration
// - Lambda health checks don't support DI constructor injection
// - The alternative (IHealthCheck classes) requires more boilerplate
//
// WHY THIS IS NOT "ZOMBIE CODE":
// ✅ Executes on every /healthz endpoint call
// ✅ Used by Kubernetes liveness/readiness probes
// ✅ Used by load balancers for traffic routing
// ✅ Has detected Redis outages in production
// ✅ No alternative implementation exists
//
// KNOWN ISSUE:
// - BuildServiceProvider() creates a second service provider instance
// - For Singleton services: same instance used (no duplication)
// - For Scoped services: would create duplicate (but we only use Singleton IDistributedCache)
// - Memory impact: ~1KB per health check execution
//
// PROPER FIX (deferred - medium complexity, low priority):
// - Refactor to use IHealthCheck interface with constructor DI
// - Create RedisHealthCheck class that injects IDistributedCache
// - Requires ~50 lines of code + test updates
// - Risk: medium (test infrastructure changes needed)
// - Priority: low (current pattern works, no production issues)
//
// DECISION: Document the pattern, defer refactoring until higher-priority work complete
// See: tasks/phase-2-task-9-document-health-check.md for refactoring guide
// ============================================================================
builder.Services.AddHealthChecks()
    .AddCheck("redis", () =>
    {
        if (!redisEnabled)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(
                "Redis is disabled (using in-memory cache for development)");
        }

        try
        {
            // NOTE: BuildServiceProvider() usage explained in comment block above
            var cache = builder.Services.BuildServiceProvider().GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
            var testKey = "_health_check_";
            var testValue = DateTimeOffset.UtcNow.ToString("O");

            cache.SetString(testKey, testValue, new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10)
            });

            var retrieved = cache.GetString(testKey);
            cache.Remove(testKey);

            if (retrieved == testValue)
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Redis cache is available and responsive");
            }

            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded("Redis cache returned unexpected value");
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Redis cache is unavailable", ex);
        }
    });

// ============================================================================
// CORS - Secure, fail-closed configuration
// ============================================================================
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

// Validate configuration
if (allowedOrigins == null || allowedOrigins.Length == 0)
{
    // In development, allow localhost as fallback
    if (builder.Environment.IsDevelopment())
    {
        var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Program");
        logger.LogWarning(
            "CORS: No allowed origins configured. Falling back to localhost (development only).");

        allowedOrigins = new[]
        {
            "http://localhost:3000",
            "http://localhost:3001",
            "http://127.0.0.1:3000"
        };
    }
    else
    {
        // FAIL-CLOSED: Throw exception in non-development environments
        throw new InvalidOperationException(
            $"CORS configuration is missing or empty in {builder.Environment.EnvironmentName} environment. " +
            "Configure 'Cors:AllowedOrigins' with explicit origin URLs. " +
            "CORS will NOT fall back to AllowAnyOrigin for security reasons.");
    }
}

// Reject wildcard configuration (security violation)
if (allowedOrigins.Contains("*"))
{
    throw new InvalidOperationException(
        "CORS: Wildcard origin '*' is not allowed. " +
        "Configure explicit origin URLs in 'Cors:AllowedOrigins'.");
}

// Validate origin URLs
foreach (var origin in allowedOrigins)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        throw new InvalidOperationException(
            $"CORS: Invalid origin URL '{origin}'. Must be absolute URL (e.g., https://example.com).");
    }

    if (uri.Scheme != "https" && !builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            $"CORS: Non-HTTPS origin '{origin}' is not allowed in {builder.Environment.EnvironmentName} environment. " +
            "Use HTTPS URLs for security.");
    }
}

// Log allowed origins for audit trail
{
    var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Program");
    logger.LogInformation(
        "CORS: Configured with {OriginCount} allowed origins: {Origins}",
        allowedOrigins.Length,
        string.Join(", ", allowedOrigins));
}

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        // Support both explicit origins from config and Dataverse/PowerApps wildcard patterns
        policy.SetIsOriginAllowed(origin =>
        {
            // Check explicit allowed origins from configuration
            if (allowedOrigins.Contains(origin))
                return true;

            // Allow Dataverse origins (*.dynamics.com)
            if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                if (uri.Host.EndsWith(".dynamics.com", StringComparison.OrdinalIgnoreCase) ||
                    uri.Host == "dynamics.com")
                    return true;

                // Allow PowerApps origins (*.powerapps.com)
                if (uri.Host.EndsWith(".powerapps.com", StringComparison.OrdinalIgnoreCase) ||
                    uri.Host == "powerapps.com")
                    return true;
            }

            return false;
        })
              .AllowCredentials()
              .AllowAnyMethod()
              .WithHeaders(
                  "Authorization",
                  "Content-Type",
                  "Accept",
                  "X-Requested-With",
                  "X-Correlation-Id")
              .WithExposedHeaders(
                  "request-id",
                  "client-request-id",
                  "traceparent",
                  "X-Correlation-Id",
                  "X-Pagination-TotalCount",
                  "X-Pagination-HasMore")
              .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
});

// ============================================================================
// RATE LIMITING - Per-user/per-IP traffic control (ADR-009)
// ============================================================================
builder.Services.AddRateLimiter(options =>
{
    // 1. Graph Read Operations - High volume, sliding window
    options.AddPolicy("graph-read", context =>
    {
        var userId = context.User?.FindFirst("oid")?.Value
                     ?? context.User?.FindFirst("sub")?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "unknown";

        return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 100,
            QueueLimit = 10,
            SegmentsPerWindow = 6 // 10-second segments
        });
    });

    // 2. Graph Write Operations - Lower volume, token bucket for burst
    options.AddPolicy("graph-write", context =>
    {
        var userId = context.User?.FindFirst("oid")?.Value
                     ?? context.User?.FindFirst("sub")?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "unknown";

        return RateLimitPartition.GetTokenBucketLimiter(userId, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 20,
            TokensPerPeriod = 10,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 5
        });
    });

    // 3. Dataverse Query Operations - Moderate volume, sliding window
    options.AddPolicy("dataverse-query", context =>
    {
        var userId = context.User?.FindFirst("oid")?.Value
                     ?? context.User?.FindFirst("sub")?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "unknown";

        return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 50,
            QueueLimit = 5,
            SegmentsPerWindow = 4 // 15-second segments
        });
    });

    // 3b. Metadata Query Operations - Very high volume with L1 cache (Phase 7)
    options.AddPolicy("metadata-query", context =>
    {
        var userId = context.User?.FindFirst("oid")?.Value
                     ?? context.User?.FindFirst("sub")?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "unknown";

        return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 200, // Higher limit due to L1 cache
            QueueLimit = 10,
            SegmentsPerWindow = 6 // 10-second segments
        });
    });

    // 4. Heavy Operations - File uploads, strict concurrency
    options.AddPolicy("upload-heavy", context =>
    {
        var userId = context.User?.FindFirst("oid")?.Value
                     ?? context.User?.FindFirst("sub")?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "unknown";

        return RateLimitPartition.GetConcurrencyLimiter(userId, _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = 5,
            QueueLimit = 10
        });
    });

    // 5. Job Submission - Rate-sensitive, fixed window
    options.AddPolicy("job-submission", context =>
    {
        var userId = context.User?.FindFirst("oid")?.Value
                     ?? context.User?.FindFirst("sub")?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 10,
            QueueLimit = 2
        });
    });

    // 6. Anonymous/Unauthenticated - Very restrictive, fixed window
    options.AddPolicy("anonymous", context =>
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(ipAddress, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 10,
            QueueLimit = 0 // No queueing for anonymous
        });
    });

    // ProblemDetails JSON response for rate limit rejections
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/problem+json";

        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue)
            ? retryAfterValue.TotalSeconds
            : 60;

        context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();

        var problemDetails = new
        {
            type = "https://tools.ietf.org/html/rfc6585#section-4",
            title = "Too Many Requests",
            status = 429,
            detail = "Rate limit exceeded. Please retry after the specified duration.",
            instance = context.HttpContext.Request.Path.Value,
            retryAfter = $"{retryAfter} seconds"
        };

        await context.HttpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        // Log rate limit rejection for monitoring
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(
            "Rate limit exceeded for {Path} by {User} (IP: {IP}). Retry after {RetryAfter}s",
            context.HttpContext.Request.Path,
            context.HttpContext.User?.Identity?.Name ?? "anonymous",
            context.HttpContext.Connection.RemoteIpAddress,
            retryAfter);
    };
});

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
app.UseCors();
app.UseMiddleware<Sprk.Bff.Api.Api.SecurityHeadersMiddleware>();

// ============================================================================
// GLOBAL EXCEPTION HANDLER - RFC 7807 Problem Details
// ============================================================================
// Catches all unhandled exceptions and converts them to structured Problem Details JSON
// with correlation IDs for tracing. Must come early in pipeline to catch all errors.
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async ctx =>
    {
        var exception = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
        var traceId = ctx.TraceIdentifier;

        var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();

        // Map exception to Problem Details (status, code, title, detail)
        (int status, string code, string title, string detail) = exception switch
        {
            // SDAP validation/business logic errors
            SdapProblemException sp => (sp.StatusCode, sp.Code, sp.Title, sp.Detail ?? sp.Message),

            // MSAL OBO token acquisition failures
            MsalServiceException ms => (
                401,
                "obo_failed",
                "OBO Token Acquisition Failed",
                $"Failed to exchange user token for Graph API token: {ms.Message}"
            ),

            // Graph API errors (ODataError is the base exception type in Graph SDK v5)
            Microsoft.Graph.Models.ODataErrors.ODataError gs => (
                (int?)gs.ResponseStatusCode ?? 500,
                "graph_error",
                "Graph API Error",
                gs.Error?.Message ?? gs.Message
            ),

            // Unexpected errors
            _ => (
                500,
                "server_error",
                "Internal Server Error",
                "An unexpected error occurred. Please check correlation ID in logs."
            )
        };

        // Log the error with correlation ID
        logger.LogError(exception,
            "Request failed with {StatusCode} {Code}: {Detail} | CorrelationId: {CorrelationId}",
            status, code, detail, traceId);

        // Return RFC 7807 Problem Details response
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";

        await ctx.Response.WriteAsJsonAsync(new
        {
            type = $"https://spaarke.com/errors/{code}",
            title,
            detail,
            status,
            extensions = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["correlationId"] = traceId
            }
        });
    });
});

// ============================================================================
// AUTHENTICATION & AUTHORIZATION
// ============================================================================
// CRITICAL: Authentication must come before Authorization
app.UseAuthentication();
app.UseAuthorization();

// ============================================================================
// RATE LIMITING
// ============================================================================
// CRITICAL: Rate limiting must come after Authentication (to access User claims for partitioning)
app.UseRateLimiter();

// ---- Health Endpoints ----

// Health checks endpoints (anonymous for monitoring)
app.MapHealthChecks("/healthz").AllowAnonymous();

// Dataverse connection test endpoint
app.MapGet("/healthz/dataverse", TestDataverseConnectionAsync);

// Dataverse CRUD operations test endpoint
app.MapGet("/healthz/dataverse/crud", TestDataverseCrudOperationsAsync);

// Lightweight ping endpoint for warm-up agents (Task 021)
// Must be fast (<100ms), unauthenticated, and expose no sensitive info
app.MapGet("/ping", () => Results.Text("pong"))
    .AllowAnonymous()
    .WithTags("Health")
    .WithDescription("Lightweight health check for warm-up agents. Returns 'pong' without authentication.");

// Detailed status endpoint with service metadata
app.MapGet("/status", () =>
{
    return TypedResults.Json(new
    {
        service = "Sprk.Bff.Api",
        version = "1.0.0",
        timestamp = DateTimeOffset.UtcNow
    });
})
    .AllowAnonymous()
    .WithTags("Health")
    .WithDescription("Service status with metadata (no sensitive info).");

// ---- Endpoint Groups ----

// User identity and capabilities endpoints
app.MapUserEndpoints();

// Permissions endpoints (for UI to query user capabilities)
app.MapPermissionsEndpoints();

// Navigation metadata endpoints (Phase 7)
app.MapNavMapEndpoints();

// Dataverse document CRUD endpoints (Task 1.3)
app.MapDataverseDocumentsEndpoints();

// File access endpoints (SPE preview, download, Office viewer - Nov 2025 Microsoft guidance)
app.MapFileAccessEndpoints();

// Document and container management endpoints (SharePoint Embedded)
app.MapDocumentsEndpoints();

// Upload endpoints for file operations
app.MapUploadEndpoints();

// OBO endpoints (user-enforced CRUD)
app.MapOBOEndpoints();

app.Run();

// Health check endpoint handlers
static async Task<IResult> TestDataverseConnectionAsync(IDataverseService dataverseService)
{
    try
    {
        var isConnected = await dataverseService.TestConnectionAsync();
        if (isConnected)
        {
            return TypedResults.Ok(new { status = "healthy", message = "Dataverse connection successful" });
        }
        else
        {
            return TypedResults.Problem(
                detail: "Dataverse connection test failed",
                statusCode: 503,
                title: "Service Unavailable"
            );
        }
    }
    catch (Exception ex)
    {
        return TypedResults.Problem(
            detail: ex.Message,
            statusCode: 503,
            title: "Dataverse Connection Error"
        );
    }
}

static async Task<IResult> TestDataverseCrudOperationsAsync(IDataverseService dataverseService)
{
    try
    {
        var testPassed = await dataverseService.TestDocumentOperationsAsync();
        if (testPassed)
        {
            return TypedResults.Ok(new { status = "healthy", message = "Dataverse CRUD operations successful" });
        }
        else
        {
            return TypedResults.Problem(
                detail: "Dataverse CRUD operations test failed",
                statusCode: 503,
                title: "Service Unavailable"
            );
        }
    }
    catch (Exception ex)
    {
        return TypedResults.Problem(
            detail: ex.Message,
            statusCode: 503,
            title: "Dataverse CRUD Test Error"
        );
    }
}

// expose Program for WebApplicationFactory in tests
public partial class Program
{
}
