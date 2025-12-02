# File Access OBO Refactor - Project Guide

**Project Status**: Ready for Implementation
**Priority**: High
**Estimated Time**: 3-4 hours
**Created**: 2025-01-25

---

## 🎯 Quick Start

This project refactors FileAccessEndpoints to use On-Behalf-Of (OBO) authentication instead of app-only authentication, fixing "Access denied" errors and enabling scalable permission management.

### Problem
FileAccessEndpoints use service principal (app-only) authentication, which requires manual PowerShell grants for each SharePoint Embedded container. Not scalable.

### Solution
Switch to OBO (On-Behalf-Of) authentication using user context. Graph API automatically enforces user permissions via Azure AD group memberships.

---

## 📋 Project Files

### Overview
- [**00-PROJECT-OVERVIEW.md**](./00-PROJECT-OVERVIEW.md) - Project goals, task breakdown, success criteria

### Technical Documentation
- [**FILE-ACCESS-OBO-REFACTOR-REVIEW.md**](./FILE-ACCESS-OBO-REFACTOR-REVIEW.md) - Comprehensive technical review (1,400+ lines)

### Task Files (Implementation Order)

#### Phase 1: Foundation (Tasks 01-02)
1. [**01-TASK-SdapProblemException.md**](./01-TASK-SdapProblemException.md) - Create custom exception class (15 min)
2. [**02-TASK-GlobalExceptionHandler.md**](./02-TASK-GlobalExceptionHandler.md) - Add RFC 7807 exception handler (20 min)

#### Phase 2: OBO Infrastructure (Tasks 03-04)
3. [**03-TASK-IGraphClientFactory.md**](./03-TASK-IGraphClientFactory.md) - Update interface with ForUserAsync/ForApp (10 min)
4. [**04-TASK-GraphClientFactory-Implementation.md**](./04-TASK-GraphClientFactory-Implementation.md) - Implement new methods (15 min)

#### Phase 3: Endpoint Refactoring (Task 05)
5. [**05-TASK-FileAccessEndpoints-Refactor.md**](./05-TASK-FileAccessEndpoints-Refactor.md) - Refactor all 4 endpoints with validation (45 min)

#### Phase 4: Optional Enhancements (Tasks 06-07)
6. [**06-TASK-SpeFileStore-Update.md**](./06-TASK-SpeFileStore-Update.md) - Update SpeFileStore for OBO (30 min) **[OPTIONAL]**
7. [**07-TASK-DocumentStorageResolver-Validation.md**](./07-TASK-DocumentStorageResolver-Validation.md) - Add validation (20 min) **[OPTIONAL]**

#### Phase 5: Integration (Task 08)
8. [**08-TASK-Program-DI-Update.md**](./08-TASK-Program-DI-Update.md) - Remove GraphServiceClient registration (10 min)

#### Phase 6: Testing and Deployment (Tasks 09-10)
9. [**09-TASK-Build-Test-Local.md**](./09-TASK-Build-Test-Local.md) - Build and test locally (30 min)
10. [**10-TASK-Deploy-Verify-Azure.md**](./10-TASK-Deploy-Verify-Azure.md) - Deploy to Azure and verify (30 min)

---

## ⚡ Fast Track (Minimum Viable Implementation)

To ship quickly, complete only the **required tasks**:

### Required Tasks (2.5 hours)
- ✅ Task 01: SdapProblemException (15 min)
- ✅ Task 02: Global Exception Handler (20 min)
- ✅ Task 03: IGraphClientFactory Interface (10 min)
- ✅ Task 04: GraphClientFactory Implementation (15 min)
- ✅ Task 05: FileAccessEndpoints Refactor (45 min)
- ✅ Task 08: Program.cs DI Update (10 min)
- ✅ Task 09: Build and Test Local (30 min)
- ✅ Task 10: Deploy and Verify (30 min)

### Optional Tasks (Skip for MVP)
- ⏭️ Task 06: SpeFileStore Update (not used by FileAccessEndpoints)
- ⏭️ Task 07: DocumentStorageResolver Validation (endpoint-level validation is sufficient)

---

## 📚 Key Concepts

### On-Behalf-Of (OBO) Flow
```
User → PCF → BFF API → Extract Token → OBO Exchange → Graph API (as user)
```

**Benefits**:
- User permissions automatically enforced
- No manual container grants needed
- Scalable (works with any number of containers)
- Tokens cached in Redis (55-min TTL, 97% overhead reduction)

### SPE Pointer Validation
Before calling Graph API, validate:
- **driveId**: Not null, starts with "b!"
- **itemId**: Not null, length >= 20 chars

### RFC 7807 Problem Details
Structured error responses:
```json
{
  "type": "https://spaarke.com/errors/invalid_id",
  "title": "Invalid Document ID",
  "detail": "Document ID 'abc' is not a valid GUID format",
  "status": 400,
  "extensions": {
    "code": "invalid_id",
    "correlationId": "0HN7GKQJ5K3QR:00000001"
  }
}
```

---

## ✅ Success Criteria

### Build Success
- [ ] No compilation errors
- [ ] All unit tests pass
- [ ] Deployment succeeds

### Functional Success
- [ ] User with container access gets preview URL (200 OK)
- [ ] User without access gets 403 Forbidden (expected)
- [ ] Invalid document ID → 400 `"invalid_id"`
- [ ] Missing driveId → 409 `"mapping_missing_drive"`
- [ ] Missing itemId → 409 `"mapping_missing_item"`

### Performance Success
- [ ] Request latency < 300ms (with cache)
- [ ] OBO token cache hit rate > 90%

### Business Success
- [ ] PCF file preview works end-to-end
- [ ] Zero manual container grants needed
- [ ] Clear error messages with correlation IDs

---

## 🚨 Critical Requirements

### PCF Token Audience
PCF must send BFF token (NOT Graph token):
- **Correct**: `api://{BFF-AppId}/SDAP.Access`
- **Wrong**: `https://graph.microsoft.com/.default`

Verify token audience in MSAL.js config.

### Azure AD Permissions
BFF App Registration must have delegated permissions:
- `Files.ReadWrite.All` (delegated)
- `Sites.FullControl.All` (delegated)
- Admin consent granted

### Dataverse SPE Pointers
Documents must have both fields populated:
- `sprk_graphdriveid` (format: "b!...")
- `sprk_graphitemid` (20+ chars)

---

## 🔧 Implementation Tips

### Method-Group Pattern (CS1593 Fix)
Keep using static local functions for endpoint handlers:

```csharp
docs.MapGet("/{documentId}/preview-url", GetPreviewUrl);

static async Task<IResult> GetPreviewUrl(
    string documentId,
    IGraphClientFactory graphFactory,
    // ...
)
{
    // Implementation
}
```

### Error Handling
Don't use try-catch in endpoints. Let exceptions bubble to global handler:

```csharp
// ❌ DON'T DO THIS
try
{
    var document = await resolver.GetDocumentAsync(id);
}
catch (Exception ex)
{
    return Results.Problem("Error");
}

// ✅ DO THIS
var document = await resolver.GetDocumentAsync(id);
// Exception automatically handled by global exception handler
```

### Validation Pattern
Throw `SdapProblemException` for validation errors:

```csharp
if (!Guid.TryParse(documentId, out var guid))
{
    throw new SdapProblemException(
        "invalid_id",
        "Invalid Document ID",
        $"Document ID '{documentId}' is not a valid GUID format",
        400
    );
}
```

---

## 📊 Progress Tracking

Use this checklist to track progress:

- [ ] Task 01: SdapProblemException
- [ ] Task 02: Global Exception Handler
- [ ] Task 03: IGraphClientFactory Interface
- [ ] Task 04: GraphClientFactory Implementation
- [ ] Task 05: FileAccessEndpoints Refactor
- [ ] Task 06: SpeFileStore Update (optional)
- [ ] Task 07: DocumentStorageResolver Validation (optional)
- [ ] Task 08: Program.cs DI Update
- [ ] Task 09: Build and Test Local
- [ ] Task 10: Deploy and Verify

---

## 🔗 Related Documentation

### Architecture
- [SDAP Architecture Guide](../../../docs/architecture/SDAP-ARCHITECTURE-GUIDE-10-20-2025.md) - System architecture

### Working Examples
- [OBOEndpoints.cs](../../../src/api/Spe.Bff.Api/Api/OBOEndpoints.cs) - Working OBO implementation
- [TokenHelper.cs](../../../src/api/Spe.Bff.Api/Infrastructure/Auth/TokenHelper.cs) - Token extraction utility

### Current Implementation
- [FileAccessEndpoints.cs](../../../src/api/Spe.Bff.Api/Api/FileAccessEndpoints.cs) - Current (app-only)
- [IGraphClientFactory.cs](../../../src/api/Spe.Bff.Api/Infrastructure/Graph/IGraphClientFactory.cs) - Current interface
- [GraphClientFactory.cs](../../../src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs) - Current implementation

---

## 📝 Notes

- Complete tasks in order (dependencies matter)
- Test each component before proceeding
- Skip optional tasks (06-07) for MVP
- Document any deviations from the plan
- Update this README as you progress

---

## 📞 Support

- **Technical Questions**: Review [FILE-ACCESS-OBO-REFACTOR-REVIEW.md](./FILE-ACCESS-OBO-REFACTOR-REVIEW.md)
- **Architecture Questions**: See [SDAP Architecture Guide](../../../docs/architecture/SDAP-ARCHITECTURE-GUIDE-10-20-2025.md)
- **OBO Examples**: Refer to [OBOEndpoints.cs](../../../src/api/Spe.Bff.Api/Api/OBOEndpoints.cs)

---

**Ready to start?** Begin with [01-TASK-SdapProblemException.md](./01-TASK-SdapProblemException.md) 🚀
