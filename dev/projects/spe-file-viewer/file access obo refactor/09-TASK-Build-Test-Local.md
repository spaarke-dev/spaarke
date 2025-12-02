# Task 09: Build and Test Locally

**Task ID**: `09-Build-Test-Local`
**Estimated Time**: 30 minutes
**Status**: Not Started
**Dependencies**: 01-08 (all previous tasks)

---

## 📋 Prompt

Build the BFF API solution and run local tests to verify the OBO refactor works correctly before deploying to Azure. This includes unit tests, integration tests, and manual testing with real requests.

---

## ✅ Todos

- [ ] Clean and restore NuGet packages
- [ ] Build the solution
- [ ] Run unit tests (if available)
- [ ] Start API locally
- [ ] Test health endpoint
- [ ] Test FileAccessEndpoints with valid/invalid requests
- [ ] Verify error responses match RFC 7807 format
- [ ] Check logs for correlation IDs
- [ ] Fix any issues found

---

## 📚 Required Knowledge

### Build Process
1. **Clean**: Remove old build artifacts
2. **Restore**: Download NuGet packages
3. **Build**: Compile C# code to DLL
4. **Test**: Run unit/integration tests
5. **Run**: Start API locally (Kestrel web server)

### Local Testing Environment
- **Base URL**: `https://localhost:7001` (or configured port)
- **Health Endpoint**: `GET /health`
- **FileAccess Endpoints**: `GET /api/documents/{id}/preview-url`

### Authentication Consideration
OBO requires a real user token. For local testing:
- **Option A**: Use Postman with MSAL.js token acquisition
- **Option B**: Temporarily add a diagnostic endpoint with hardcoded tokens (#if DEBUG)
- **Option C**: Test only validation logic (invalid IDs, missing pointers)

---

## 📂 Related Files

**Project to Build**:
- [src/api/Spe.Bff.Api/Spe.Bff.Api.csproj](../../../src/api/Spe.Bff.Api/Spe.Bff.Api.csproj)

**Test Files** (if available):
- `src/api/Spe.Bff.Api.Tests/*.cs`

---

## 🎯 Implementation

### 1. Clean and Restore

```bash
cd c:/code_files/spaarke

# Clean previous build artifacts
dotnet clean src/api/Spe.Bff.Api/Spe.Bff.Api.csproj

# Restore NuGet packages
dotnet restore src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
```

### 2. Build Solution

```bash
# Build in Release mode
dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj --configuration Release
```

**Expected Output**:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**If build fails**:
- Review error messages carefully
- Check Tasks 01-08 were completed correctly
- Verify all using statements are present
- Check for typos in method names

### 3. Run Unit Tests (if available)

```bash
# Run tests
dotnet test src/api/Spe.Bff.Api.Tests --configuration Release
```

**If no test project exists**: Skip this step.

### 4. Start API Locally

```bash
# Run API locally
cd src/api/Spe.Bff.Api
dotnet run --configuration Release
```

**Expected Output**:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:7001
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
```

**Keep terminal open** - API is now running locally.

### 5. Test Health Endpoint

Open a new terminal/browser:

```bash
# Test health endpoint
curl https://localhost:7001/health
```

**Expected Response**:
```
Healthy
```

### 6. Test Invalid Document ID (Validation)

```bash
# Test with invalid GUID format
curl https://localhost:7001/api/documents/invalid-id/preview-url
```

**Expected Response** (400 Bad Request):
```json
{
  "type": "https://spaarke.com/errors/invalid_id",
  "title": "Invalid Document ID",
  "detail": "Document ID 'invalid-id' is not a valid GUID format",
  "status": 400,
  "extensions": {
    "code": "invalid_id",
    "correlationId": "0HN7GKQJ5K3QR:00000001"
  }
}
```

**Verify**:
- [ ] Status code is 400
- [ ] Content-Type is `application/problem+json`
- [ ] Response includes `correlationId`
- [ ] Error code is `"invalid_id"`

### 7. Test Document Not Found

```bash
# Test with valid GUID but non-existent document
curl https://localhost:7001/api/documents/00000000-0000-0000-0000-000000000000/preview-url
```

**Expected Response** (404 Not Found):
```json
{
  "type": "https://spaarke.com/errors/document_not_found",
  "title": "Document Not Found",
  "detail": "Document with ID '00000000-0000-0000-0000-000000000000' does not exist",
  "status": 404,
  "extensions": {
    "code": "document_not_found",
    "correlationId": "0HN7GKQJ5K3QR:00000002"
  }
}
```

### 8. Test Missing Authorization Header

```bash
# Test without Authorization header (if endpoint requires auth)
curl https://localhost:7001/api/documents/{valid-guid}/preview-url
```

**Expected Response** (401 Unauthorized):
```json
{
  "type": "https://spaarke.com/errors/unauthorized",
  "title": "Unauthorized",
  "detail": "Missing Authorization header",
  "status": 401,
  "extensions": {
    "code": "unauthorized",
    "correlationId": "..."
  }
}
```

### 9. Test with Valid Document (Requires Real Token)

**Note**: This requires a valid user access token from MSAL.js.

**Option A - Postman**:
1. Configure MSAL authentication in Postman
2. Get token with audience `api://{BFF-AppId}/SDAP.Access`
3. Send request with `Authorization: Bearer {token}` header

**Option B - Add Diagnostic Endpoint** (only if needed):

Create a temporary endpoint for local testing:

```csharp
#if DEBUG
app.MapGet("/debug/test-obo", async (
    IGraphClientFactory graphFactory,
    HttpContext ctx,
    CancellationToken ct) =>
{
    var graphClient = await graphFactory.ForUserAsync(ctx, ct);
    var me = await graphClient.Me.GetAsync(ct);
    return Results.Ok(new { userPrincipalName = me?.UserPrincipalName });
}).RequireAuthorization();
#endif
```

**Option C - Test Validation Only**:
Skip full OBO testing until deployed to Azure (Task 10) where PCF provides real tokens.

---

## ✅ Acceptance Criteria

### Build Success
- [ ] `dotnet clean` succeeds
- [ ] `dotnet restore` succeeds
- [ ] `dotnet build` succeeds with 0 errors, 0 warnings
- [ ] `dotnet test` succeeds (if tests exist)

### API Startup
- [ ] API starts without errors
- [ ] Health endpoint returns "Healthy"
- [ ] No exceptions in console logs

### Validation Tests
- [ ] Invalid ID → 400 `"invalid_id"`
- [ ] Non-existent document → 404 `"document_not_found"`
- [ ] Missing auth header → 401 (if endpoint requires auth)

### Error Format
- [ ] All errors return `application/problem+json`
- [ ] All errors include `correlationId`
- [ ] All errors match RFC 7807 structure

### Logs
- [ ] Errors are logged with correlation IDs
- [ ] Log messages are clear and actionable
- [ ] No stack traces for expected errors (validation failures)

---

## 📝 Testing Checklist

| Test Case | Endpoint | Expected Status | Expected Error Code |
|-----------|----------|-----------------|---------------------|
| Health check | `GET /health` | 200 OK | N/A |
| Invalid GUID | `GET /api/documents/invalid/preview-url` | 400 | `invalid_id` |
| Non-existent doc | `GET /api/documents/{zero-guid}/preview-url` | 404 | `document_not_found` |
| Missing auth | `GET /api/documents/{id}/preview-url` | 401 | `unauthorized` |
| Valid request | `GET /api/documents/{valid-id}/preview-url` | 200 or 403 | N/A or `access_denied` |

---

## 🚨 Common Issues

### Issue 1: Build Errors
**Symptom**: CS0246 "Type or namespace not found"
**Solution**: Add missing using statements in affected files

### Issue 2: DI Errors at Startup
**Symptom**: "Unable to resolve service for type 'SdapProblemException'"
**Solution**: Exceptions are NOT registered in DI - they're thrown, not injected

### Issue 3: Global Exception Handler Not Working
**Symptom**: Exceptions return default ASP.NET error page instead of Problem Details
**Solution**: Verify `UseExceptionHandler` is called BEFORE `UseAuthorization` in Program.cs

### Issue 4: CORS Errors
**Symptom**: Browser blocks requests from localhost
**Solution**: Ensure CORS is configured in Program.cs for local development

---

## 🔗 Related Documentation

- [ASP.NET Core Testing Guide](https://learn.microsoft.com/en-us/aspnet/core/test/)
- [RFC 7807 Problem Details](https://www.rfc-editor.org/rfc/rfc7807)

---

**Previous Task**: [08-TASK-Program-DI-Update.md](./08-TASK-Program-DI-Update.md)
**Next Task**: [10-TASK-Deploy-Verify-Azure.md](./10-TASK-Deploy-Verify-Azure.md)
