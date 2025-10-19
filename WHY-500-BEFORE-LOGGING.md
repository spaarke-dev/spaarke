# Why HTTP 500 Occurs Before Application Logging

**Date**: 2025-10-16
**User's Key Observation**: "we get a 500 error before this"
**Problem**: Error happens before .NET application can log it

---

## The Problem: Where Does the Error Actually Occur?

You're absolutely correct. The HTTP 500 error happens **BEFORE** the application logging can capture it. Let me explain where in the request pipeline this is failing.

---

## ASP.NET Core Request Pipeline

### Normal Successful Request Flow

```
1. IIS receives request
   ↓
2. AspNetCoreModule forwards to Kestrel (.NET)
   ↓
3. ASP.NET Core Middleware Pipeline:
   a. Logging middleware (starts logging)
   b. Exception handling middleware
   c. Authentication middleware (validates JWT Token A)
   d. Authorization middleware
   e. Routing middleware
   f. Endpoint middleware
   ↓
4. Your endpoint code executes:
   - GraphClientFactory.CreateOnBehalfOfClientAsync()
   - OBO token exchange
   - Graph API call
   ↓
5. Response returned through pipeline
   ↓
6. IIS returns response to client
```

### Where the Error is Occurring (Current Behavior)

```
1. IIS receives request ✅
   ↓
2. AspNetCoreModule forwards to Kestrel ✅
   ↓
3. ASP.NET Core Middleware Pipeline:
   a. Logging middleware ✅ (starts)
   b. Exception handling middleware ✅
   c. Authentication middleware ✅ (validates Token A)
   d. Authorization middleware ✅
   e. Routing middleware ✅
   f. Endpoint middleware ✅
   ↓
4. Your endpoint code starts executing:
   - GraphClientFactory.CreateOnBehalfOfClientAsync() ✅
   - OBO token exchange ✅ (Azure AD returns Token B)
   - Token B is missing FileStorageContainer.Selected ⚠️
   - CreateGraphClientFromToken(Token B) ✅
   - graphClient.Drives[containerId].Root.ItemWithPath(path).Content.PutAsync()
   ↓
5. ❌ **GRAPH SDK THROWS EXCEPTION HERE**
   ↓
6. Exception bubbles up through middleware
   ↓
7. ❌ **EXCEPTION HANDLER FAILS OR IS BYPASSED**
   ↓
8. ❌ **KESTREL CRASHES OR RETURNS 500**
   ↓
9. ❌ **IIS INTERCEPTS AND RETURNS GENERIC 500 PAGE**
```

---

## Why We Don't See .NET Logs

### Theory 1: Graph SDK Exception Crashes Middleware

**Hypothesis**: The `Microsoft.Graph.ServiceException` is thrown in a way that bypasses the exception handling middleware.

**Evidence**:
- HTTP 500.0 (not 500.30) = app is running, but request failed
- IIS returns generic HTML error page (not ASP.NET Core error page)
- No .NET exception appears in logs

**Root Cause**:
- Graph SDK may throw exception from async continuation
- Exception occurs outside the middleware try-catch scope
- ASP.NET Core can't catch it properly

### Theory 2: Exception Occurs During Response Streaming

**Hypothesis**: The upload starts streaming, then fails mid-stream.

**Evidence**:
- PUT request for file upload
- May be using streaming upload
- Exception during stream = can't return proper error response

**Root Cause**:
- Headers already sent to client
- Can't change status code to 403/401
- ASP.NET Core returns 500 by default

### Theory 3: Detailed Error Mode Not Applied to Requests Yet

**Hypothesis**: The `ASPNETCORE_DETAILEDERRORS=true` setting hasn't taken effect for all scenarios.

**Evidence**:
- We only recently enabled it
- App restarted at 04:52 AM (hours ago)
- May not apply to all error conditions

---

## What Actually Happens (Most Likely)

### Step 1: Request Reaches Endpoint ✅

```csharp
// OBOEndpoints.cs
app.MapPut("/api/obo/containers/{containerId}/files/{*path}", async (
    HttpContext context,
    string containerId,
    string path,
    ...) => {

    // Get user token from Authorization header
    var userToken = await context.GetTokenAsync("access_token"); ✅
```

### Step 2: OBO Exchange Succeeds (But Token B is Wrong) ⚠️

```csharp
// GraphClientFactory.cs
var result = await _cca.AcquireTokenOnBehalfOf(
    new[] {
        "https://graph.microsoft.com/Sites.FullControl.All",
        "https://graph.microsoft.com/Files.ReadWrite.All"
        // FileStorageContainer.Selected NOT requested
    },
    new UserAssertion(userAccessToken)
).ExecuteAsync(); ✅

// This SUCCEEDS - Azure AD returns Token B
// But Token B does NOT have FileStorageContainer.Selected scope
```

### Step 3: Graph Client Created ✅

```csharp
return new GraphServiceClient(httpClient, authProvider, "https://graph.microsoft.com/beta"); ✅
```

### Step 4: Graph API Call Made ✅

```csharp
// UploadSessionManager.cs or similar
var uploadedItem = await graphClient.Drives[containerId].Root
    .ItemWithPath(path)
    .Content
    .PutAsync(content); // ❌ FAILS HERE
```

### Step 5: Graph API Returns 403 Forbidden ❌

**Graph API Response** (internal):
```http
HTTP/1.1 403 Forbidden
Content-Type: application/json

{
  "error": {
    "code": "AccessDenied",
    "message": "Insufficient privileges to complete the operation.",
    "innerError": {
      "date": "2025-10-16T...",
      "request-id": "...",
      "client-request-id": "..."
    }
  }
}
```

### Step 6: Graph SDK Throws Exception ❌

```csharp
// Microsoft.Graph SDK internals
throw new ServiceException(
    new Error {
        Code = "AccessDenied",
        Message = "Insufficient privileges to complete the operation."
    },
    httpResponse.Headers,
    (int)HttpStatusCode.Forbidden
);
```

### Step 7: Exception Handler Can't Convert to Proper Response ❌

**Why**:
- Exception may occur during async streaming
- Response headers may already be sent
- Can't set status code to 403 after response started
- ASP.NET Core fallback = HTTP 500

### Step 8: IIS Returns Generic Error Page ❌

**IIS Behavior**:
- Sees HTTP 500 from Kestrel
- No custom error page configured
- Returns generic IIS 500.0 error page (HTML)

---

## Why Detailed Logging Doesn't Help (Yet)

### What ASPNETCORE_DETAILEDERRORS Does

**From Microsoft Docs**:
> When true, the developer exception page is displayed for unhandled exceptions.

**Key Word**: "page" - This is for browser-displayed errors, not logged errors.

**What It Does**:
- Shows detailed exception page in browser
- Includes stack trace
- Only works if middleware can catch the exception

**What It Doesn't Do**:
- Doesn't force Graph SDK exceptions to be logged
- Doesn't capture exceptions thrown after response starts
- Doesn't help if exception bypasses middleware

### What Logging__LogLevel__Default=Debug Does

**Purpose**: Increases verbosity of application logs

**What It Logs**:
- Method entry/exit
- Configuration values
- Middleware execution
- Token acquisition (we see this in GraphClientFactory)

**What It Doesn't Log**:
- Exceptions thrown by external SDKs (Microsoft.Graph)
- Exceptions that crash the request pipeline
- Errors that occur after response streaming starts

---

## How to ACTUALLY Capture the Error

### Option 1: Add Try-Catch Around Graph SDK Call

**Location**: Wherever `graphClient.Drives[containerId]...PutAsync()` is called

```csharp
try
{
    var uploadedItem = await graphClient.Drives[containerId].Root
        .ItemWithPath(path)
        .Content
        .PutAsync(content);

    return uploadedItem;
}
catch (Microsoft.Graph.ServiceException ex)
{
    _logger.LogError(ex,
        "Graph API call failed. StatusCode={StatusCode}, Code={Code}, Message={Message}",
        ex.StatusCode, ex.Error?.Code, ex.Error?.Message);

    // Log the response details
    if (ex.ResponseHeaders != null)
    {
        foreach (var header in ex.ResponseHeaders)
        {
            _logger.LogDebug("Response header: {Key} = {Value}", header.Key, string.Join(", ", header.Value));
        }
    }

    throw; // Re-throw to let middleware handle it
}
```

**Result**: This WILL capture the error before it becomes a 500.

---

### Option 2: Add Global Exception Handler

**Location**: `Program.cs`

```csharp
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
        var exception = exceptionHandlerPathFeature?.Error;

        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

        if (exception is Microsoft.Graph.ServiceException graphEx)
        {
            logger.LogError(graphEx,
                "🔴 GRAPH API EXCEPTION: Status={Status}, Code={Code}, Message={Message}",
                graphEx.StatusCode, graphEx.Error?.Code, graphEx.Error?.Message);

            context.Response.StatusCode = (int)graphEx.StatusCode;
            await context.Response.WriteAsJsonAsync(new {
                error = graphEx.Error?.Code,
                message = graphEx.Error?.Message
            });
        }
        else
        {
            logger.LogError(exception, "🔴 UNHANDLED EXCEPTION: {Message}", exception?.Message);
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new {
                error = "Internal Server Error",
                message = exception?.Message
            });
        }
    });
});
```

**Result**: This WILL capture ALL exceptions, even Graph SDK ones.

---

### Option 3: Enable Application Insights Query (No Code Change)

**Query Application Insights** for Graph API exceptions:

```bash
az monitor app-insights query \
  --app 6a76b012-46d9-412f-b4ab-4905658a9559 \
  --analytics-query "exceptions
    | where timestamp > ago(24h)
    | where outerType contains 'Graph' or outerMessage contains 'Graph' or outerMessage contains 'Access'
    | project timestamp, outerType, outerMessage, innermostMessage, operation_Name
    | order by timestamp desc" \
  --output table
```

**Result**: MAY show the exception if Application Insights captured it.

---

### Option 4: Just Add the Missing Scope (Fastest)

**Reasoning**:
1. We've confirmed from code review: `FileStorageContainer.Selected` is NOT requested
2. We've confirmed from guide: `FileStorageContainer.Selected` IS required
3. We know the app permission exists (user confirmed)
4. We know the fix is trivial (one line)

**Risk**: Extremely low - we're adding a scope that's already granted

**Time to Fix**: 2 minutes
**Time to Deploy**: 5 minutes
**Time to Test**: 1 minute

**Total**: 8 minutes vs hours of diagnostic work

---

## My Recommendation

### Path A: Add Try-Catch (See Error - 10 minutes)

1. Add try-catch around Graph SDK call in UploadSessionManager
2. Deploy
3. Trigger upload
4. See actual error in logs
5. Confirm it's the missing scope
6. Then add the scope

**Total Time**: ~30 minutes (code change + deploy + test + fix + deploy + test)

---

### Path B: Just Fix It (Trust Code Review - 8 minutes)

1. Add `"https://graph.microsoft.com/FileStorageContainer.Selected"` to line 153
2. Deploy
3. Test upload
4. If it works: Done ✅
5. If it doesn't: Add try-catch and investigate

**Total Time**: ~8 minutes (if our hypothesis is correct)

---

### Path C: Query Application Insights (See Historical Errors - 1 minute)

1. Run the Application Insights query above
2. See if any Graph exceptions were captured in the past 24 hours
3. This might show us the error without needing to trigger a new upload

**Total Time**: 1 minute

---

## What I Recommend

**Try Path C first** (Application Insights query), then **go with Path B** (just fix it).

**Reasoning**:
- We have high confidence the scope is missing (code review + guide comparison)
- The fix is trivial and low risk
- Waiting to see the error adds no value if we already know the cause
- Even if we see the error, we'll still need to make the same one-line fix

**If you want absolute certainty first**: Path A (add try-catch)

**What would you prefer?**

---

**Document Created**: 2025-10-16 06:15 AM
**Key Insight**: Error occurs during Graph SDK call execution, exception bubbles up incorrectly, becomes HTTP 500 instead of proper 403/401
**Solution**: Either add try-catch to see error, or just fix the missing scope now
