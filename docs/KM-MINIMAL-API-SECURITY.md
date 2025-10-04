# Minimal APIs & ASP.NET Core Security Best Practices for Spaarke

## Overview
This guide provides security best practices for implementing Minimal APIs in ASP.NET Core for the Spaarke platform, aligned with ADR-001 and ADR-008.

## Authentication & Authorization

### JWT Bearer Authentication Setup
```csharp
// Program.cs
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["AzureAd:Instance"] + builder.Configuration["AzureAd:TenantId"];
        options.Audience = builder.Configuration["AzureAd:ClientId"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(5)
        };
        
        // Add token to telemetry
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var correlationId = context.HttpContext.TraceIdentifier;
                context.HttpContext.Items["CorrelationId"] = correlationId;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
```

### Endpoint-Level Authorization (per ADR-008)
```csharp
// Use endpoint filters instead of global middleware
app.MapGet("/api/documents/{documentId}", 
    async (Guid documentId, AuthorizationService authService, SpeFileStore fileStore) =>
    {
        // Authorization happens here, not in middleware
        var authResult = await authService.AuthorizeAsync(documentId, Operation.Read);
        if (!authResult.IsAuthorized)
            return Results.Forbid();
            
        var document = await fileStore.GetDocumentAsync(documentId);
        return Results.Ok(document);
    })
    .RequireAuthorization()
    .AddEndpointFilter<DocumentAuthorizationFilter>();

// Endpoint filter implementation
public class DocumentAuthorizationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var authService = context.HttpContext.RequestServices.GetRequiredService<AuthorizationService>();
        var documentId = context.GetArgument<Guid>(0);
        
        var result = await authService.AuthorizeAsync(documentId, Operation.Read);
        if (!result.IsAuthorized)
        {
            return Results.Problem(
                title: "Access Denied",
                detail: result.DenialReason,
                statusCode: 403,
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = result.ReasonCode // e.g., "sdap.access.deny.team_mismatch"
                });
        }
        
        return await next(context);
    }
}
```

### Context Enrichment Middleware (per ADR-008)
```csharp
// Single middleware for context, not authorization
public class SpaarkeContextMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Extract user context
        if (context.User.Identity?.IsAuthenticated == true)
        {
            context.Items["UserId"] = context.User.FindFirst("oid")?.Value;
            context.Items["TenantId"] = context.User.FindFirst("tid")?.Value;
            context.Items["UserPrincipalName"] = context.User.FindFirst("upn")?.Value;
        }
        
        // Set correlation ID
        if (!context.Request.Headers.TryGetValue("X-Correlation-Id", out var correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
        }
        context.Items["CorrelationId"] = correlationId.ToString();
        context.Response.Headers.Add("X-Correlation-Id", correlationId.ToString());
        
        await next(context);
    }
}
```

## Input Validation & Sanitization

### Model Validation with FluentValidation
```csharp
// Use FluentValidation for complex validation
public class DocumentUploadRequestValidator : AbstractValidator<DocumentUploadRequest>
{
    public DocumentUploadRequestValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty()
            .MaximumLength(255)
            .Matches(@"^[a-zA-Z0-9\-_\.\s]+$")
            .WithMessage("Filename contains invalid characters");
            
        RuleFor(x => x.FileSize)
            .GreaterThan(0)
            .LessThanOrEqualTo(100 * 1024 * 1024) // 100MB max
            .WithMessage("File size must be between 1 byte and 100MB");
            
        RuleFor(x => x.MimeType)
            .NotEmpty()
            .Must(BeAllowedMimeType)
            .WithMessage("File type not allowed");
    }
    
    private bool BeAllowedMimeType(string mimeType)
    {
        var allowedTypes = new[] 
        { 
            "application/pdf", 
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };
        return allowedTypes.Contains(mimeType.ToLowerInvariant());
    }
}

// Wire up validation
app.MapPost("/api/documents/upload", 
    async (DocumentUploadRequest request, IValidator<DocumentUploadRequest> validator) =>
    {
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return Results.ValidationProblem(validationResult.ToDictionary());
        }
        // Process upload
    });
```

## CORS Configuration

```csharp
// Configure CORS for SPAs and Power Apps
builder.Services.AddCors(options =>
{
    options.AddPolicy("SpaarkeApps", policy =>
    {
        policy.WithOrigins(
                "https://make.powerapps.com",
                "https://apps.powerapps.com",
                builder.Configuration["Frontend:Url"])
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()
            .WithExposedHeaders("X-Correlation-Id", "X-Total-Count");
    });
});

app.UseCors("SpaarkeApps");
```

## Rate Limiting

```csharp
// Configure rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirst("oid")?.Value ?? "anonymous",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));
            
    // Specific endpoint limits
    options.AddPolicy("upload", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.User.FindFirst("oid")?.Value ?? "anonymous",
            factory: partition => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                SegmentsPerWindow = 5
            }));
});

app.UseRateLimiter();

// Apply to specific endpoint
app.MapPost("/api/documents/upload", UploadHandler)
    .RequireAuthorization()
    .RequireRateLimiting("upload");
```

## Security Headers

```csharp
// Add security headers middleware
app.Use(async (context, next) =>
{
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Add("X-Frame-Options", "DENY");
    context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Add("Referrer-Policy", "no-referrer");
    context.Response.Headers.Add("Content-Security-Policy", 
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://appsforoffice.microsoft.com; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https:; " +
        "connect-src 'self' https://graph.microsoft.com");
    
    // Remove server header
    context.Response.Headers.Remove("Server");
    
    await next();
});
```

## Secure File Upload Handling

```csharp
app.MapPost("/api/documents/upload-session", async (
    UploadSessionRequest request,
    AuthorizationService authService,
    SpeFileStore fileStore) =>
{
    // Validate file metadata before creating session
    if (!IsValidFileExtension(Path.GetExtension(request.FileName)))
    {
        return Results.BadRequest("Invalid file type");
    }
    
    // Scan first chunk for malware signatures
    if (request.FirstChunk != null)
    {
        var scanResult = await ScanForMalware(request.FirstChunk);
        if (!scanResult.IsClean)
        {
            // Log security event
            return Results.BadRequest("File failed security scan");
        }
    }
    
    // Create upload session with expiry
    var session = await fileStore.CreateUploadSessionAsync(new UploadSessionDto
    {
        FileName = SanitizeFileName(request.FileName),
        FileSize = request.FileSize,
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(24)
    });
    
    return Results.Ok(session);
})
.RequireAuthorization()
.DisableAntiforgery(); // Required for multipart uploads

private static string SanitizeFileName(string fileName)
{
    // Remove path traversal attempts
    fileName = Path.GetFileName(fileName);
    
    // Remove special characters
    var invalidChars = Path.GetInvalidFileNameChars();
    foreach (var c in invalidChars)
    {
        fileName = fileName.Replace(c, '_');
    }
    
    return fileName;
}
```

## Logging & Auditing

```csharp
// Security event logging
public class SecurityAuditMiddleware
{
    private readonly ILogger<SecurityAuditMiddleware> _logger;
    
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Log authentication events
        if (context.User.Identity?.IsAuthenticated == true)
        {
            _logger.LogInformation("Authenticated request: User={UserId}, IP={IP}, Path={Path}",
                context.User.FindFirst("oid")?.Value,
                context.Connection.RemoteIpAddress,
                context.Request.Path);
        }
        
        await next(context);
        
        // Log authorization failures
        if (context.Response.StatusCode == 403)
        {
            _logger.LogWarning("Authorization failed: User={UserId}, Path={Path}, Method={Method}",
                context.User.FindFirst("oid")?.Value ?? "anonymous",
                context.Request.Path,
                context.Request.Method);
        }
    }
}
```

## API Key Authentication (for External Systems)

```csharp
// For external integrations that can't use Azure AD
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader))
        {
            return AuthenticateResult.NoResult();
        }
        
        var apiKey = apiKeyHeader.ToString();
        
        // Validate against secure store (Key Vault)
        var validKey = await Options.ValidateKey(apiKey);
        if (!validKey.IsValid)
        {
            return AuthenticateResult.Fail("Invalid API key");
        }
        
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, validKey.ClientId),
            new Claim("client_type", "api_key"),
            new Claim("scope", string.Join(" ", validKey.Scopes))
        };
        
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        
        return AuthenticateResult.Success(ticket);
    }
}
```

## Testing Security

```csharp
[Fact]
public async Task Endpoint_RequiresAuthentication()
{
    // Arrange
    var client = _factory.CreateClient();
    
    // Act
    var response = await client.GetAsync("/api/documents/123");
    
    // Assert
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Fact]
public async Task Endpoint_EnforcesAuthorization()
{
    // Arrange
    var client = _factory.WithWebHostBuilder(builder =>
    {
        builder.ConfigureServices(services =>
        {
            services.AddAuthentication("Test")
                .AddScheme<TestAuthenticationSchemeOptions, TestAuthenticationHandler>("Test", options => { });
        });
    }).CreateClient();
    
    client.DefaultRequestHeaders.Authorization = 
        new AuthenticationHeaderValue("Test", "user-without-access");
    
    // Act
    var response = await client.GetAsync("/api/documents/restricted-doc");
    
    // Assert
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}
```

## Key Security Principles for Spaarke

1. **Never trust client input** - Always validate and sanitize
2. **Use endpoint-level authorization** - Per ADR-008, not global middleware
3. **Implement defense in depth** - Multiple security layers
4. **Log security events** - For audit and threat detection
5. **Use platform features** - Leverage ASP.NET Core's built-in security
6. **Fail securely** - Default to deny, provide minimal error details
7. **Keep secrets in Key Vault** - Never in code or config files
8. **Use managed identities** - For Azure service authentication