# Phase 3: Feature Module Pattern & DI Minimalism

## Objective
Implement feature module pattern to simplify DI configuration per ADR-010.

## Duration
4-6 hours

## Prerequisites
- Phase 2 complete and validated
- All tests passing
- Branch: `refactor/adr-compliance`

---

## Overview

This phase consolidates 20+ scattered service registrations into 3 feature modules, reducing `Program.cs` DI section to ~15-20 lines.

**Target Structure:**
Extensions/
├── SpaarkeCore.Extensions.cs      (Authorization, caching, core services)
├── DocumentsModule.Extensions.cs  (SPE, Graph, Dataverse services)
└── WorkersModule.Extensions.cs    (Background services, job processors)

---

## Task 3.1: Create SpaarkeCore Extensions

### New File: `src/api/Spe.Bff.Api/Extensions/SpaarkeCore.Extensions.cs`
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Spaarke.Core.Authorization;
using Spaarke.Dataverse;
using StackExchange.Redis;

namespace Spe.Bff.Api.Extensions;

public static class SpaarkeCoreExtensions
{
    /// <summary>
    /// Registers core Spaarke services: authorization, caching, and shared infrastructure.
    /// </summary>
    public static IServiceCollection AddSpaarkeCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Authorization
        services.AddSingleton<AuthorizationService>();
        services.AddScoped<IAccessDataSource, DataverseAccessDataSource>();
        
        // Authorization Rules (order matters - processed in registration order)
        services.AddSingleton<IAuthorizationRule, ExplicitDenyRule>();
        services.AddSingleton<IAuthorizationRule, ExplicitGrantRule>();
        services.AddSingleton<IAuthorizationRule, TeamMembershipRule>();
        services.AddSingleton<IAuthorizationRule, RoleScopeRule>();
        services.AddSingleton<IAuthorizationRule, LinkTokenRule>();
        
        // Distributed Cache (Redis)
        var redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrEmpty(redisConnection))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnection;
                options.InstanceName = "sdap:";
            });
        }
        else
        {
            // Fallback to memory cache for local development without Redis
            services.AddDistributedMemoryCache();
        }
        
        // Per-request cache (for collapsing duplicate reads within one request)
        services.AddScoped<RequestCache>();
        
        return services;
    }
}
Validation for Task 3.1

 File created at correct location
 Namespace matches project structure
 All authorization rules registered
 Redis cache configured with fallback
 Build succeeds


Task 3.2: Create DocumentsModule Extensions
New File: src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs
csharpusing Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spe.Bff.Api.Configuration;
using Spe.Bff.Api.Infrastructure;
using Spe.Bff.Api.Storage;
using Spe.Bff.Api.Services;
using Spaarke.Dataverse;

namespace Spe.Bff.Api.Extensions;

public static class DocumentsModuleExtensions
{
    /// <summary>
    /// Registers document management services: SPE storage, Graph API, and Dataverse.
    /// </summary>
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        // SPE Storage (concrete class, no interface)
        services.AddScoped<SpeFileStore>();
        
        // Graph Client Factory (interface required - creates different client types)
        services.AddSingleton<IGraphClientFactory, GraphClientFactory>();
        
        // Graph Token Cache (for OBO token caching)
        services.AddSingleton<GraphTokenCache>();
        
        // Upload Session Manager (for chunked uploads)
        services.AddScoped<UploadSessionManager>();
        
        // Dataverse ServiceClient (Singleton for connection reuse)
        services.AddSingleton<DataverseServiceClientImpl>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DataverseOptions>>().Value;
            
            var connectionString = 
                $"AuthType=ClientSecret;" +
                $"Url={options.ServiceUrl};" +
                $"ClientId={options.ClientId};" +
                $"ClientSecret={options.ClientSecret};" +
                $"RequireNewInstance=false;"; // Enable connection pooling
            
            return new DataverseServiceClientImpl(connectionString);
        });
        
        // If IDataverseService interface still exists and is used:
        // services.AddSingleton<IDataverseService>(sp => 
        //     sp.GetRequiredService<DataverseServiceClientImpl>());
        
        return services;
    }
}
Validation for Task 3.2

 File created at correct location
 SpeFileStore registered as concrete (no interface)
 DataverseServiceClientImpl registered as Singleton
 GraphTokenCache registered
 Build succeeds


Task 3.3: Create WorkersModule Extensions
New File: src/api/Spe.Bff.Api/Extensions/WorkersModule.Extensions.cs
csharpusing Microsoft.Extensions.DependencyInjection;
using Spe.Bff.Api.Services;
using Spe.Bff.Api.BackgroundServices;

namespace Spe.Bff.Api.Extensions;

public static class WorkersModuleExtensions
{
    /// <summary>
    /// Registers background services and job processors.
    /// </summary>
    public static IServiceCollection AddWorkersModule(this IServiceCollection services)
    {
        // Background Service Bus Processors
        services.AddHostedService<DocumentEventProcessor>();
        services.AddHostedService<ServiceBusJobProcessor>();
        
        // Idempotency Service (prevents duplicate job processing)
        services.AddSingleton<IdempotencyService>();
        
        // Job Handlers (registered as scoped - created per job)
        services.AddScoped<DocumentProcessingJobHandler>();
        
        return services;
    }
}
Validation for Task 3.3

 File created at correct location
 Background services registered
 IdempotencyService registered as Singleton
 Build succeeds


Task 3.4: Simplify Program.cs
File to Modify: src/api/Spe.Bff.Api/Program.cs
Current State (Before):
csharpvar builder = WebApplication.CreateBuilder(args);

// 80+ lines of scattered registrations
builder.Services.AddScoped<IResourceStore, SpeResourceStore>();
builder.Services.AddScoped<ISpeService, OboSpeService>();
builder.Services.AddScoped<IDataverseSecurityService, DataverseSecurityService>();
builder.Services.AddScoped<IUacService, UacService>();
builder.Services.AddSingleton<IAuthorizationService, AuthorizationService>();
builder.Services.AddSingleton<IAuthorizationRule, ExplicitDenyRule>();
builder.Services.AddSingleton<IAuthorizationRule, ExplicitGrantRule>();
builder.Services.AddSingleton<IAuthorizationRule, TeamMembershipRule>();
// ... 15+ more authorization rules
builder.Services.AddScoped<IAccessDataSource, DataverseAccessDataSource>();
builder.Services.AddScoped<IGraphClientFactory, GraphClientFactory>();
builder.Services.AddStackExchangeRedisCache(options => { ... });
builder.Services.AddHostedService<DocumentEventProcessor>();
builder.Services.AddHostedService<ServiceBusJobProcessor>();
// ... many more lines
Target State (After):
csharpusing Spe.Bff.Api.Extensions;
using Spe.Bff.Api.Configuration;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// Configuration
// ============================================================================
builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{builder.Configuration["KeyVaultName"]}.vault.azure.net/"),
    new DefaultAzureCredential());

// ============================================================================
// Feature Modules (Core Services)
// ============================================================================
builder.Services.AddSpaarkeCore(builder.Configuration);
builder.Services.AddDocumentsModule();
builder.Services.AddWorkersModule();

// ============================================================================
// Options (Strongly-Typed Configuration)
// ============================================================================
builder.Services.AddOptions<DataverseOptions>()
    .Bind(builder.Configuration.GetSection(DataverseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<GraphResilienceOptions>()
    .Bind(builder.Configuration.GetSection(GraphResilienceOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<ServiceBusOptions>()
    .Bind(builder.Configuration.GetSection(ServiceBusOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ============================================================================
// ASP.NET Core Framework Services
// ============================================================================
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("PowerPlatform", policy =>
    {
        policy.WithOrigins("https://*.crm.dynamics.com", "https://*.crm*.dynamics.com")
              .SetIsOriginAllowedToAllowWildcardSubdomains()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<DataverseHealthCheck>("dataverse")
    .AddCheck<ServiceBusHealthCheck>("servicebus")
    .AddCheck<RedisHealthCheck>("redis");

// ============================================================================
// Build Application
// ============================================================================
var app = builder.Build();

// ============================================================================
// Middleware Pipeline
// ============================================================================
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors("PowerPlatform");
app.UseAuthentication();
app.UseAuthorization();

// Custom middleware
app.UseMiddleware<SpaarkeContextMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

// ============================================================================
// Endpoint Mapping
// ============================================================================
app.MapHealthChecks("/healthz");
app.MapHealthChecks("/healthz/ready");
app.MapHealthChecks("/healthz/live");

app.MapGet("/ping", () => Results.Ok(new { 
    timestamp = DateTime.UtcNow,
    version = typeof(Program).Assembly.GetName().Version?.ToString()
}));

app.MapOBOEndpoints();
app.MapDocumentsEndpoints();
app.MapDataverseDocumentsEndpoints();
app.MapUploadEndpoints();
app.MapPermissionsEndpoints();
app.MapUserEndpoints();

app.Run();
Key Changes in Program.cs

Feature Module Calls (3 lines replace 50+):

csharp   builder.Services.AddSpaarkeCore(builder.Configuration);
   builder.Services.AddDocumentsModule();
   builder.Services.AddWorkersModule();

Options Pattern (strongly-typed config):

csharp   builder.Services.AddOptions<DataverseOptions>()
       .Bind(builder.Configuration.GetSection(DataverseOptions.SectionName))
       .ValidateDataAnnotations()
       .ValidateOnStart();

Clear Section Comments (improves readability):

Configuration
Feature Modules
Options
Framework Services
Middleware Pipeline
Endpoint Mapping


Remove All Scattered Registrations:

Delete all AddScoped, AddSingleton, AddTransient lines for custom services
Keep only framework services (AddAuthentication, AddAuthorization, etc.)



Validation for Task 3.4

 Program.cs reduced to ~100 lines (from 200+)
 DI section reduced to ~15-20 lines
 All scattered registrations removed
 Feature module calls added
 Options pattern used for configuration
 Build succeeds


Task 3.5: Verify No Circular Dependencies
Check DI Container Resolution
Add this temporary test to verify all services resolve:
Temporary File: tests/Spe.Bff.Api.Tests/DependencyInjectionTests.cs
csharpusing Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Xunit;
using Spe.Bff.Api.Extensions;

namespace Spe.Bff.Api.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AllServicesResolveSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
                ["Dataverse:ClientSecret"] = "test-secret",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:Domain"] = "test.onmicrosoft.com",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-client-id"
            })
            .Build();
        
        // Add feature modules
        services.AddSpaarkeCore(configuration);
        services.AddDocumentsModule();
        services.AddWorkersModule();
        
        // Act
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert - Verify key services resolve
        Assert.NotNull(serviceProvider.GetRequiredService<AuthorizationService>());
        Assert.NotNull(serviceProvider.GetRequiredService<SpeFileStore>());
        Assert.NotNull(serviceProvider.GetRequiredService<IGraphClientFactory>());
        Assert.NotNull(serviceProvider.GetRequiredService<DataverseServiceClientImpl>());
        Assert.NotNull(serviceProvider.GetRequiredService<GraphTokenCache>());
        Assert.NotNull(serviceProvider.GetRequiredService<IdempotencyService>());
        
        // Verify no circular dependencies (BuildServiceProvider would throw)
        // Verify service lifetimes are correct
        using var scope1 = serviceProvider.CreateScope();
        using var scope2 = serviceProvider.CreateScope();
        
        var fileStore1 = scope1.ServiceProvider.GetRequiredService<SpeFileStore>();
        var fileStore2 = scope2.ServiceProvider.GetRequiredService<SpeFileStore>();
        
        // Scoped services should be different instances per scope
        Assert.NotSame(fileStore1, fileStore2);
        
        var authzService1 = scope1.ServiceProvider.GetRequiredService<AuthorizationService>();
        var authzService2 = scope2.ServiceProvider.GetRequiredService<AuthorizationService>();
        
        // Singleton services should be same instance across scopes
        Assert.Same(authzService1, authzService2);
    }
}
Validation for Task 3.5

 DI test passes
 No circular dependency errors
 Scoped services create new instances per scope
 Singleton services reuse same instance


Task 3.6: Update Existing Tests
Files to Modify
All test files in tests/Spe.Bff.Api.Tests/ that mock services.
Pattern for Test Updates
Before (mocking interfaces):
csharpvar mockResourceStore = new Mock<IResourceStore>();
mockResourceStore.Setup(x => x.UploadAsync(...)).ReturnsAsync(...);

var mockSecurityService = new Mock<IDataverseSecurityService>();
mockSecurityService.Setup(x => x.AuthorizeAsync(...)).ReturnsAsync(true);
After (using concrete services with mocked dependencies):
csharp// Mock the infrastructure boundary (Graph factory)
var mockGraphFactory = new Mock<IGraphClientFactory>();
var mockGraphClient = CreateMockGraphClient(); // Helper method
mockGraphFactory.Setup(x => x.CreateOnBehalfOfClientAsync(It.IsAny<string>()))
    .ReturnsAsync(mockGraphClient);

// Use real service with mocked dependency
var fileStore = new SpeFileStore(
    mockGraphFactory.Object,
    Mock.Of<ILogger<SpeFileStore>>());

// Mock authorization service (or use real one with mocked IAccessDataSource)
var mockAuthzService = new Mock<AuthorizationService>(
    Mock.Of<IEnumerable<IAuthorizationRule>>(),
    Mock.Of<ILogger<AuthorizationService>>());
mockAuthzService.Setup(x => x.AuthorizeAsync(...))
    .ReturnsAsync(AuthorizationResult.Success());
Validation for Task 3.6

 All unit tests updated
 Tests use concrete classes where possible
 Mocks only at infrastructure boundaries
 All tests pass


Complete Validation Checklist
Build Validation
bashdotnet clean
dotnet build --configuration Release
# Expected: Success with 0 warnings
Test Validation
bashdotnet test --verbosity normal
# Expected: All tests pass, including new DI test
Runtime Validation
bash# Start application
dotnet run --project src/api/Spe.Bff.Api

# Verify application starts
# Expected: No startup errors, listens on configured ports

# Check logs for DI warnings
# Expected: No "Unable to resolve service" errors
Health Check Validation
bashcurl -X GET https://localhost:5001/healthz
# Expected: 200 OK

curl -X GET https://localhost:5001/healthz/ready
# Expected: 200 OK (all dependencies healthy)

curl -X GET https://localhost:5001/healthz/live
# Expected: 200 OK
Endpoint Validation
bash# Test OBO endpoint (requires valid token)
curl -X PUT https://localhost:5001/api/obo/containers/{containerId}/files/test.pdf \
  -H "Authorization: Bearer {valid-token}" \
  -H "Content-Type: application/pdf" \
  --data-binary @test.pdf

# Expected: 200 OK with FileHandleDto
Code Quality Validation

 Program.cs DI section ≤ 20 lines
 3 feature module files created
 All custom service registrations moved to modules
 No duplicate registrations
 Consistent namespace usage
 XML documentation on extension methods


Success Criteria
Quantitative Metrics

 Program.cs reduced from 200+ lines to ~100 lines
 DI registrations reduced from 50+ lines to ~15 lines
 Service registration count reduced from 20+ to 0 in Program.cs
 Feature modules: 3 created (Core, Documents, Workers)

Qualitative Criteria

 Code is more readable (clear sections, less noise)
 Registration logic is grouped by feature
 Easy to understand what services exist
 Easy to add new services (clear where to add them)
 No breaking changes (all functionality preserved)

ADR Compliance

 ADR-010: DI minimalism achieved

Concrete registrations (except IGraphClientFactory, IAccessDataSource)
Feature module pattern implemented
~15 lines of DI code in Program.cs




Commit Message
refactor(phase-3): implement feature module pattern per ADR-010

- Create SpaarkeCore.Extensions.cs (authorization, caching, core)
- Create DocumentsModule.Extensions.cs (SPE, Graph, Dataverse)
- Create WorkersModule.Extensions.cs (background services)
- Simplify Program.cs from 200+ lines to ~100 lines
- Reduce DI registrations from 50+ to ~15 lines
- Update tests to use concrete services with mocked dependencies

Benefits:
- 75% reduction in Program.cs DI complexity
- Clear feature boundaries
- Easy to understand service composition
- Maintainable and extensible architecture

ADR-010 compliance: Register concretes, feature modules, minimal DI

Rollback Plan
If validation fails:
bash# Revert Program.cs
git checkout HEAD~1 -- src/api/Spe.Bff.Api/Program.cs

# Remove extension files
rm src/api/Spe.Bff.Api/Extensions/SpaarkeCore.Extensions.cs
rm src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs
rm src/api/Spe.Bff.Api/Extensions/WorkersModule.Extensions.cs

# Rebuild and test
dotnet build
dotnet test

Common Issues & Solutions
Issue 1: "Unable to resolve service for type X"
Cause: Service not registered in any feature module
Solution: Verify service registration in appropriate module:

Core services → SpaarkeCore.Extensions.cs
SPE/Graph/Dataverse → DocumentsModule.Extensions.cs
Background services → WorkersModule.Extensions.cs

Issue 2: Circular dependency detected
Cause: Service A depends on Service B, which depends on Service A
Solution:

Use IServiceProvider and resolve on-demand
Refactor to break circular dependency
Use event-based communication instead

Issue 3: Tests fail after DI changes
Cause: Tests mocking interfaces that no longer exist
Solution: Update tests to use concrete classes with mocked dependencies (see Task 3.6)
Issue 4: Redis connection fails in tests
Cause: Tests trying to connect to real Redis
Solution: Use AddDistributedMemoryCache() in tests instead of Redis

Next Steps
After Phase 3 is complete and validated:

Commit changes with provided commit message
Push branch to remote
Proceed to Phase 4: Token Caching
Reference .claude/phase-4-instructions.md


Appendix: Before/After Comparison
Before (Program.cs DI Section)
csharp// 80+ lines, scattered, hard to understand
builder.Services.AddScoped<IResourceStore, SpeResourceStore>();
builder.Services.AddScoped<ISpeService, OboSpeService>();
builder.Services.AddScoped<IDataverseService, DataverseServiceClientImpl>();
builder.Services.AddScoped<IDataverseSecurityService, DataverseSecurityService>();
builder.Services.AddScoped<IUacService, UacService>();
builder.Services.AddSingleton<IAuthorizationService, AuthorizationService>();
builder.Services.AddSingleton<IAuthorizationRule, ExplicitDenyRule>();
builder.Services.AddSingleton<IAuthorizationRule, ExplicitGrantRule>();
builder.Services.AddSingleton<IAuthorizationRule, TeamMembershipRule>();
builder.Services.AddSingleton<IAuthorizationRule, RoleScopeRule>();
builder.Services.AddSingleton<IAuthorizationRule, LinkTokenRule>();
builder.Services.AddScoped<IAccessDataSource, DataverseAccessDataSource>();
builder.Services.AddScoped<IGraphClientFactory, GraphClientFactory>();
builder.Services.AddStackExchangeRedisCache(options => 
{
    options.Configuration = builder.Configuration["ConnectionStrings:Redis"];
    options.InstanceName = "sdap:";
});
builder.Services.AddHostedService<DocumentEventProcessor>();
builder.Services.AddHostedService<ServiceBusJobProcessor>();
builder.Services.AddSingleton<IdempotencyService>();
builder.Services.AddScoped<UploadSessionManager>();
// ... 60+ more lines
After (Program.cs DI Section)
csharp// 15 lines, clear, organized
builder.Services.AddSpaarkeCore(builder.Configuration);
builder.Services.AddDocumentsModule();
builder.Services.AddWorkersModule();

builder.Services.AddOptions<DataverseOptions>()
    .Bind(builder.Configuration.GetSection(DataverseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<GraphResilienceOptions>()
    .Bind(builder.Configuration.GetSection(GraphResilienceOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
Result: 83% reduction in lines, 100% improvement in clarity