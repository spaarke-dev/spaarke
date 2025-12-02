# Task 08: Update Program.cs DI Registration

**Task ID**: `08-Program-DI-Update`
**Estimated Time**: 10 minutes
**Status**: Not Started
**Dependencies**: 02-TASK-GlobalExceptionHandler, 05-TASK-FileAccessEndpoints-Refactor

---

## 📋 Prompt

Remove the `GraphServiceClient` DI registration from `Program.cs` since FileAccessEndpoints now create per-request clients using OBO. The global exception handler (Task 02) should already be in place.

---

## ✅ Todos

- [ ] Open `src/api/Spe.Bff.Api/Program.cs`
- [ ] Locate `builder.Services.AddScoped<GraphServiceClient>` registration (around line 264)
- [ ] Remove the GraphServiceClient registration
- [ ] Verify global exception handler is present (added in Task 02)
- [ ] Build and verify no compilation errors
- [ ] Run locally and test endpoints

---

## 📚 Required Knowledge

### Why Remove GraphServiceClient Registration?

**Current Problem** (from previous session):
```csharp
// Program.cs (lines 264-270)
builder.Services.AddScoped<Microsoft.Graph.GraphServiceClient>(sp =>
{
    var factory = sp.GetRequiredService<IGraphClientFactory>();
    return factory.CreateAppOnlyClient();  // Always uses app-only!
});
```

**Issues**:
1. Uses app-only authentication (requires manual container grants)
2. FileAccessEndpoints need per-request OBO clients (not scoped app-only)
3. Misleading - looks like it supports OBO but doesn't

**After Refactor** (Task 05):
```csharp
// FileAccessEndpoints.cs
var graphClient = await graphFactory.ForUserAsync(context, ct);  // Per-request OBO
```

FileAccessEndpoints create clients dynamically using `IGraphClientFactory.ForUserAsync`, so the DI registration is unused and incorrect.

---

## 📂 Related Files

**File to Modify**:
- [src/api/Spe.Bff.Api/Program.cs](../../../src/api/Spe.Bff.Api/Program.cs)

---

## 🎯 Implementation

### 1. Remove GraphServiceClient Registration

**Find and DELETE** these lines (around line 264):

```csharp
// ❌ DELETE THIS BLOCK
// Register GraphServiceClient for minimal API endpoint injection (app-only authentication)
builder.Services.AddScoped<Microsoft.Graph.GraphServiceClient>(sp =>
{
    var factory = sp.GetRequiredService<IGraphClientFactory>();
    return factory.CreateAppOnlyClient();  // Synchronous method, not async
});
```

### 2. Verify Global Exception Handler Is Present

The global exception handler should be **after** `app.UseHttpsRedirection()` and **before** `app.UseAuthorization()`:

```csharp
app.UseHttpsRedirection();

// ✅ VERIFY THIS EXISTS (added in Task 02)
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async ctx =>
    {
        var exception = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
        var traceId = ctx.TraceIdentifier;

        var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();

        // Map exception to Problem Details
        var (status, code, title, detail) = exception switch
        {
            SdapProblemException sp => (sp.StatusCode, sp.Code, sp.Title, sp.Detail),
            MsalServiceException ms => (401, "obo_failed", "OBO Token Acquisition Failed", $"Failed to exchange user token: {ms.Message}"),
            Microsoft.Graph.Models.ODataErrors.ServiceException gs when gs.ResponseStatusCode > 0
                => (gs.ResponseStatusCode, "graph_error", "Graph API Error", gs.Message),
            _ => (500, "server_error", "Internal Server Error", "An unexpected error occurred")
        };

        logger.LogError(exception, "Request failed with {StatusCode} {Code}: {Detail} | CorrelationId: {CorrelationId}",
            status, code, detail, traceId);

        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";

        await ctx.Response.WriteAsJsonAsync(new
        {
            type = $"https://spaarke.com/errors/{code}",
            title,
            detail,
            status,
            extensions = new Dictionary<string, object?> { ["code"] = code, ["correlationId"] = traceId }
        });
    });
});

app.UseAuthorization();
```

**If the exception handler is NOT present**: Stop and complete Task 02 first.

---

## ✅ Acceptance Criteria

### Build Success
- [ ] Project builds without errors
- [ ] No missing dependency errors

### Code Quality
- [ ] GraphServiceClient DI registration removed
- [ ] Global exception handler present (from Task 02)
- [ ] No dead code remaining

### Verification Steps
1. Open [Program.cs](../../../src/api/Spe.Bff.Api/Program.cs)
2. Search for `AddScoped<GraphServiceClient>` - should return NO results
3. Search for `UseExceptionHandler` - should return ONE result (the global handler)
4. Build: `dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj`
5. Verify no errors

---

## 📝 Notes

### Why Was GraphServiceClient Registered in the First Place?

During the previous session (fixing CS1593 errors), `GraphServiceClient` was registered to fix an "unresolved parameter" error when FileAccessEndpoints tried to inject it:

```csharp
// Old FileAccessEndpoints (before OBO refactor)
static async Task<IResult> GetPreviewUrl(
    string documentId,
    IDocumentStorageResolver documentStorageResolver,
    SpeFileStore speFileStore,
    GraphServiceClient graphClient,  // ❌ Required DI registration
    ILogger<Program> logger,
    HttpContext context,
    CancellationToken ct)
{
    // Used graphClient directly (app-only authentication)
}
```

After the OBO refactor (Task 05), endpoints no longer inject `GraphServiceClient`. Instead, they inject `IGraphClientFactory` and create per-request clients:

```csharp
// New FileAccessEndpoints (after OBO refactor)
static async Task<IResult> GetPreviewUrl(
    string documentId,
    IDocumentStorageResolver documentStorageResolver,
    IGraphClientFactory graphFactory,  // ✅ Already registered
    ILogger<Program> logger,
    HttpContext context,
    CancellationToken ct)
{
    var graphClient = await graphFactory.ForUserAsync(context, ct);  // Per-request OBO
    // ...
}
```

**Result**: The DI registration is no longer needed and should be removed to avoid confusion.

---

## 🔗 Related Tasks

- **Task 02**: Added global exception handler
- **Task 05**: Refactored FileAccessEndpoints to use IGraphClientFactory

---

**Previous Task**: [07-TASK-DocumentStorageResolver-Validation.md](./07-TASK-DocumentStorageResolver-Validation.md)
**Next Task**: [09-TASK-Build-Test-Local.md](./09-TASK-Build-Test-Local.md)
