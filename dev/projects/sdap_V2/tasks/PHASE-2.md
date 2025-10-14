# Phase 2: Service Layer Simplification

## Objective
Consolidate SPE storage layer and authorization services per ADR-007 and ADR-010.

## Duration
12-16 hours (split into two sub-phases)

## Prerequisites
- Phase 1 complete and validated
- All tests passing
- Branch: `refactor/adr-compliance`

---

## Phase 2.1: Consolidate SPE Storage Layer

### Task 2.1.1: Create SpeFileStore
**New file:** `src/api/Spe.Bff.Api/Storage/SpeFileStore.cs`

**Requirements:**
- Concrete class (no interface)
- Constructor injection: `IGraphClientFactory` only
- All methods return SDAP DTOs (FileHandleDto, ContainerDto, etc.)
- Never return Graph SDK types (DriveItem, Drive, etc.)

**Method signatures to implement:**
```csharp
public class SpeFileStore
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly ILogger<SpeFileStore> _logger;
    
    public SpeFileStore(
        IGraphClientFactory graphClientFactory,
        ILogger<SpeFileStore> logger)
    {
        _graphClientFactory = graphClientFactory;
        _logger = logger;
    }
    
    public async Task<FileHandleDto> UploadFileAsync(
        string driveId, 
        string fileName, 
        Stream content, 
        string userToken,
        CancellationToken cancellationToken = default)
    
    public async Task<Stream> DownloadFileAsync(
        string driveId, 
        string itemId, 
        string userToken,
        CancellationToken cancellationToken = default)
    
    public async Task<UploadSessionDto> CreateUploadSessionAsync(
        string driveId, 
        string fileName, 
        long fileSize,
        string userToken,
        CancellationToken cancellationToken = default)
    
    public async Task<IEnumerable<ContainerDto>> ListContainersAsync(
        string userToken,
        CancellationToken cancellationToken = default)
    
    public async Task<ContainerDto> CreateContainerAsync(
        string containerName, 
        string description,
        string userToken,
        CancellationToken cancellationToken = default)
    
    public async Task<FileMetadataDto> GetFileMetadataAsync(
        string driveId,
        string itemId,
        string userToken,
        CancellationToken cancellationToken = default)
}
Logic to consolidate from:

Services/SpeResourceStore.cs
Services/OboSpeService.cs
Infrastructure/ContainerOperations.cs
Infrastructure/DriveItemOperations.cs

Task 2.1.2: Update Endpoints
Files to modify:

Endpoints/OBOEndpoints.cs
Endpoints/DocumentsEndpoints.cs
Endpoints/UploadEndpoints.cs

Pattern:
csharp// Before:
private static async Task<IResult> UploadFile(
    IResourceStore resourceStore,  // Remove
    ISpeService speService,         // Remove
    HttpRequest request)

// After:
private static async Task<IResult> UploadFile(
    SpeFileStore fileStore,        // Concrete class
    HttpRequest request)
Task 2.1.3: Update DI Registration
File to modify: src/api/Spe.Bff.Api/Program.cs
Remove:
csharpbuilder.Services.AddScoped<IResourceStore, SpeResourceStore>();
builder.Services.AddScoped<ISpeService, OboSpeService>();
builder.Services.AddScoped<IOboSpeService, OboSpeService>();
Add:
csharpbuilder.Services.AddScoped<SpeFileStore>();
Task 2.1.4: Update Tests
Files to modify:

All test files in tests/Spe.Bff.Api.Tests/ that reference old interfaces

Pattern:
csharp// Before:
var mockStore = new Mock<IResourceStore>();

// After:
var mockGraphFactory = new Mock<IGraphClientFactory>();
var fileStore = new SpeFileStore(mockGraphFactory.Object, mockLogger.Object);
Validation (Phase 2.1)

 Build succeeds
 All tests pass
 Manual test: Upload file via OBO endpoint
 Manual test: Download file via OBO endpoint
 Manual test: List containers
 Verify no Graph SDK types in endpoint signatures

Cleanup (After Validation)
Delete obsolete files:

 Services/IResourceStore.cs
 Services/SpeResourceStore.cs
 Services/ISpeService.cs
 Services/IOboSpeService.cs (if exists)
 Services/OboSpeService.cs


Phase 2.2: Simplify Authorization Layer
Task 2.2.1: Verify AuthorizationService Exists
Location: src/shared/Spaarke.Core/Authorization/AuthorizationService.cs
Should already exist per ADR-003. If missing, this is a blocker.
Task 2.2.2: Remove Redundant Authorization Services
Files to delete (after consolidating logic):

Services/IDataverseSecurityService.cs
Services/DataverseSecurityService.cs
Services/IUacService.cs
Services/UacService.cs

Before deleting: Review each service for unique logic. If found, move to:

AuthorizationService (if general authorization logic)
New IAuthorizationRule implementation (if specific rule)

Task 2.2.3: Keep Required Seam
Keep these (per ADR-003):

IAccessDataSource (interface)
DataverseAccessDataSource (implementation)

Task 2.2.4: Update Endpoint Authorization
Files to modify: Any endpoints using authorization
Pattern:
csharp// Before:
private static async Task<IResult> SomeEndpoint(
    IDataverseSecurityService securityService,
    IUacService uacService,
    string documentId)

// After:
private static async Task<IResult> SomeEndpoint(
    AuthorizationService authzService,
    ClaimsPrincipal user,
    string documentId)
{
    var authzResult = await authzService.AuthorizeAsync(
        user: user,
        resource: documentId,
        operation: Operation.Read);
    
    if (!authzResult.Succeeded)
    {
        return Results.StatusCode(403);
    }
    
    // Proceed with operation
}
Task 2.2.5: Update DI Registration
File to modify: src/api/Spe.Bff.Api/Program.cs
Remove:
csharpbuilder.Services.AddScoped<IDataverseSecurityService, DataverseSecurityService>();
builder.Services.AddScoped<IUacService, UacService>();
Keep (from Spaarke.Core):
csharpbuilder.Services.AddSpaarkeCore(); // Registers AuthorizationService + rules
Validation (Phase 2.2)

 Build succeeds
 All tests pass
 Manual test: Authenticated request succeeds
 Manual test: Unauthenticated request returns 401
 Manual test: Unauthorized operation returns 403


Overall Phase 2 Success Criteria

 SpeFileStore created and working
 All obsolete storage interfaces removed
 Authorization simplified to use AuthorizationService
 Service count reduced by 6+
 Interface count reduced by 4+
 All tests pass
 All endpoints functional

Commit Messages
refactor(phase-2.1): consolidate SPE storage to SpeFileStore per ADR-007

- Create SpeFileStore as single concrete storage facade
- Remove IResourceStore, ISpeService, IOboSpeService interfaces
- Update all endpoints to inject SpeFileStore directly
- Consolidate logic from SpeResourceStore and OboSpeService

ADR-007 compliance: Single focused facade, no generic interfaces

---

refactor(phase-2.2): simplify authorization per ADR-010

- Remove redundant IDataverseSecurityService and IUacService
- Use AuthorizationService from Spaarke.Core directly
- Keep only IAccessDataSource as required seam
- Update endpoints to use streamlined authorization

ADR-010 compliance: Register concretes, minimize interfaces

---

## Step 4: How to Use These Files with Claude Code

### Option A: Reference Files in Chat
I need to refactor the SDAP BFF API. Please review:

.claude/project-context.md
.claude/architectural-decisions.md
.claude/phase-1-instructions.md

Then execute Phase 1: Configuration & Critical Fixes

### Option B: Add to Claude Code Project Knowledge
1. Open Claude Code
2. Go to Project Settings
3. Add `.claude/` directory to "Knowledge Sources"
4. Claude Code will automatically index these files

### Option C: Explicitly Paste Key Sections
When starting a phase, paste the specific instruction file:
Execute this refactoring phase:
[Paste entire content of .claude/phase-1-instructions.md]
Context files you should reference:

.claude/project-context.md (for constraints)
.claude/architectural-decisions.md (for ADR rules)
.claude/anti-patterns.md (for what to avoid)


---

## Step 5: Recommended Workflow

### First Time Setup

Create .claude/ directory
Add all 8 context files
Create phase instruction files (phase-1, phase-2, etc.)
Commit to git (these are documentation)


### Starting Phase 1
Hey Claude Code,
I'm refactoring the SDAP BFF API to achieve ADR compliance.
Please read:

.claude/project-context.md (project constraints)
.claude/architectural-decisions.md (ADR rules)
.claude/phase-1-instructions.md (what to do)

Then execute Phase 1: Configuration & Critical Fixes
Follow the validation checklist in .claude/refactoring-checklist.md after each task.
Stop and report results before proceeding to the next phase.

### After Phase 1 Succeeds
Claude Code,
Phase 1 is complete and validated. Now execute:

.claude/phase-2-instructions.md

Continue following:

.claude/anti-patterns.md (avoid these mistakes)
.claude/code-patterns.md (use these patterns)

Report results after Phase 2.1 before proceeding to Phase 2.2.