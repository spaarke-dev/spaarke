# Playbook Pre-Fill Integration Guide

> **Purpose**: Reference for creating and wiring "Create New Matter Pre-Fill" and "Create New Project Pre-Fill" playbooks into the wizard forms.
>
> **Audience**: Claude Code sessions working on playbook creation, BFF endpoint wiring, and frontend integration.

> **Created Date: March 5, 2026

---

## Overview

When a user uploads files in the Create New Matter (or Create New Project) wizard, the system:

1. Sends the raw files to the BFF API
2. BFF extracts text from the files
3. BFF invokes a **playbook** (configured by GUID) via Azure OpenAI
4. The AI returns a flat JSON with **display names only** (no Dataverse GUIDs)
5. BFF passes the JSON back to the frontend
6. Frontend fuzzy-matches each display name against Dataverse lookup tables to resolve GUIDs
7. Form fields are pre-filled with resolved values + "AI Pre-filled" badges

```
Browser                          BFF API (Azure)                    Azure OpenAI
───────                          ──────────────                     ────────────

1. User uploads files in Step 1
   (files held in browser memory as File blobs)

2. User advances to Step 2 → CreateRecordStep mounts → useEffect fires

3. POST /api/workspace/matters/pre-fill
   Content-Type: multipart/form-data
   Body: [file1.pdf, file2.docx]  ──────►  MatterPreFillService
   (authenticatedFetch + Bearer)            .AnalyzeFilesAsync()
                                             │
                                             ├─ Extract text (ITextExtractor)
                                             ├─ Resolve playbook GUID (config or default)
                                             │
                                    4. PlaybookService.ExecuteAsync()
                                             │
                                             ├─ Load playbook nodes from Dataverse
                                             ├─ Build prompt with extracted text
                                             │
                                    5. Azure OpenAI  ──────►  GPT-4o returns:
                                             │                 {
                                             │                   "matterTypeName": "Litigation",
                                             │                   "practiceAreaName": "Employment",
                                             │                   "matterName": "Smith v Acme",
                                             │                   "summary": "...",
                                             │                   "confidence": 0.85
                                             │                 }
                                             │
                                    6. ParseAiResponse()
                                             │  (normalize, build preFilledFields[])
   ◄────────────────────────────────────────┘

7. HTTP 200 OK → JSON response
   │
8. Frontend parses response
   │
9. Fuzzy-match lookup fields against Dataverse
   ├─ searchMatterTypes(webApi, "Litigation") → retrieveMultipleRecords
   │    → findBestLookupMatch("Litigation", results) → {id: "abc-123", name: "Litigation"}
   ├─ searchPracticeAreas(webApi, "Employment") → retrieveMultipleRecords
   │    → findBestLookupMatch("Employment", results) → {id: "def-456", name: "Employment Law"}
   └─ (all resolve in parallel via Promise.all)
   │
10. dispatch({ type: 'APPLY_AI_PREFILL', fields })
    → Form renders with pre-filled values + "AI Pre-filled" badges
```

---

## Part 1: AI Playbook Output Contract

### What the AI Playbook Must Return

The playbook returns a **flat JSON object with display names only**. The frontend resolves lookup GUIDs — the AI never needs to know Dataverse record IDs.

#### Create New Matter Pre-Fill

```json
{
  "matterTypeName": "Litigation",
  "practiceAreaName": "Employment Law",
  "matterName": "Smith v. Acme Corp - Wrongful Termination",
  "summary": "Employment dispute involving wrongful termination claim filed by John Smith against Acme Corporation. The complaint alleges violation of state employment protection statutes.",
  "assignedAttorneyName": "Jane Smith",
  "assignedParalegalName": "Bob Jones",
  "assignedOutsideCounselName": "Wilson & Partners LLP",
  "confidence": 0.85
}
```

#### Create New Project Pre-Fill

```json
{
  "projectTypeName": "Contract Review",
  "practiceAreaName": "Corporate Law",
  "projectName": "Acme Corp - Vendor Agreement Review",
  "description": "Review and analysis of vendor service agreements for Acme Corporation, focusing on liability provisions and termination clauses.",
  "assignedAttorneyName": "Jane Smith",
  "assignedParalegalName": "Bob Jones",
  "confidence": 0.82
}
```

### Field Rules for Playbook Prompts

| Field | Type | Constraint | Notes |
|-------|------|------------|-------|
| `matterTypeName` / `projectTypeName` | string | Must match a value in the lookup table | Use proper case, full official name. AI should output the name as it would appear in the system. |
| `practiceAreaName` | string | Must match a value in the lookup table | Same lookup table (`sprk_practicearea_ref`) for both matters and projects. |
| `matterName` / `projectName` | string | Max 10 words | Descriptive, specific. Format: "Party v. Party - Topic" or "Client - Engagement Type". |
| `summary` / `description` | string | Max 500 words | Brief narrative of the matter/project scope and objectives. |
| `assignedAttorneyName` | string | Full name as it appears in contacts | "Jane Smith", not "J. Smith" or "jane smith". |
| `assignedParalegalName` | string | Full name as it appears in contacts | Same rule. |
| `assignedOutsideCounselName` | string | Organization name (matters only) | Full legal name: "Wilson & Partners LLP", not "Wilson". |
| `confidence` | number | 0.0 to 1.0 | Overall extraction confidence. |

### Critical Rules

1. **Return display names, never GUIDs** — the frontend resolves IDs via fuzzy matching
2. **Omit fields rather than guess** — if the AI can't extract a field with reasonable confidence, leave it out of the JSON entirely
3. **Use exact display names when possible** — the fuzzy matcher has thresholds:
   - `1.0` — Exact match (case-insensitive)
   - `0.8` — One string starts with the other ("Corporate" ↔ "Corporate Law")
   - `0.7` — One contains the other ("Trans" in "Transactional")
   - `0.5` — Single result from Dataverse search (implicit relevance)
   - `0.4` — Minimum acceptance threshold (below this = no match)
4. **Same playbook works across environments** — display names are portable, GUIDs are not

---

## Part 2: Dataverse Lookup Tables

The frontend resolves AI display names by searching these Dataverse tables:

| AI Field | Dataverse Table | Search Field | ID Field | Used By |
|----------|----------------|--------------|----------|---------|
| `matterTypeName` | `sprk_mattertype_ref` | `sprk_name` | `sprk_mattertype_refid` | Matter |
| `projectTypeName` | `sprk_projecttype_ref` | `sprk_name` | `sprk_projecttype_refid` | Project |
| `practiceAreaName` | `sprk_practicearea_ref` | `sprk_name` | `sprk_practicearea_refid` | Both |
| `assignedAttorneyName` | `contact` | `fullname` | `contactid` | Both |
| `assignedParalegalName` | `contact` | `fullname` | `contactid` | Both |
| `assignedOutsideCounselName` | (TBD — `sprk_organization` or `account`) | TBD | TBD | Matter |

### Fuzzy Match Implementation

Located in `CreateRecordStep.tsx` — the `findBestLookupMatch()` function:

```typescript
function findBestLookupMatch(aiValue: string, candidates: ILookupItem[]): ILookupItem | null {
  const aiLower = aiValue.toLowerCase().trim();
  let bestScore = 0;
  let bestItem: ILookupItem | null = null;

  for (const item of candidates) {
    const dbLower = item.name.toLowerCase().trim();
    let score = 0;

    if (dbLower === aiLower)                                         score = 1.0;  // Exact
    else if (dbLower.startsWith(aiLower) || aiLower.startsWith(dbLower)) score = 0.8;  // Prefix
    else if (dbLower.includes(aiLower) || aiLower.includes(dbLower))     score = 0.7;  // Contains

    if (score > bestScore) {
      bestScore = score;
      bestItem = item;
    }
  }

  // Trust single Dataverse result even if score < 0.4
  if (bestScore < 0.4 && candidates.length === 1) {
    bestScore = 0.5;
    bestItem = candidates[0];
  }

  return bestScore >= 0.4 ? bestItem : null;
}
```

**Implication for playbook prompts**: The closer the AI output matches the exact Dataverse record name, the higher the match score. Exact case doesn't matter (case-insensitive), but spelling and word order do.

---

## Part 3: BFF API — Existing Implementation (Matter)

### Current Configuration

| Item | Value |
|------|-------|
| **Endpoint** | `POST /api/workspace/matters/pre-fill` |
| **Service** | `MatterPreFillService` (scoped) |
| **Config Key** | `Workspace:PreFillPlaybookId` |
| **Default Playbook GUID** | `18cf3cc8-02ec-f011-8406-7c1e520aa4df` |
| **Timeout** | 45s (BFF), 60s (frontend) |
| **Rate Limit** | 10 req/min per user (`ai-stream` policy) |

### Key Files

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs` | Service that extracts text, invokes playbook, parses AI response |
| `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceMatterEndpoints.cs` | Endpoint registration (`/pre-fill` route) |
| `src/server/api/Sprk.Bff.Api/Api/Workspace/Models/PreFillResponse.cs` | Response DTO |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/WorkspaceModule.cs` | DI registration (`services.AddScoped<MatterPreFillService>()`) |

### PreFillResponse Model (Current)

```csharp
// File: Api/Workspace/Models/PreFillResponse.cs
public record PreFillResponse(
    string? MatterTypeName,
    string? PracticeAreaName,
    string? MatterName,
    string? Summary,
    double Confidence,
    string[] PreFilledFields);

// Factory method for empty/failed responses
public static PreFillResponse Empty() =>
    new(null, null, null, null, 0, []);
```

### How the Playbook GUID Is Resolved

```csharp
// In MatterPreFillService.ExtractFieldsViaPlaybookAsync():

// 1. Check appsettings configuration
var playbookIdStr = _configuration[PlaybookIdConfigKey];  // "Workspace:PreFillPlaybookId"

// 2. Parse or fall back to hardcoded default
var playbookId = !string.IsNullOrEmpty(playbookIdStr) && Guid.TryParse(playbookIdStr, out var parsed)
    ? parsed
    : DefaultPreFillPlaybookId;  // 18cf3cc8-02ec-f011-8406-7c1e520aa4df

// 3. Build PlaybookRunRequest
var request = new PlaybookRunRequest
{
    PlaybookId = playbookId,
    DocumentIds = [],                    // Empty — text is passed via UserContext
    UserContext = documentText,           // Extracted text from uploaded files
    Parameters = new Dictionary<string, string>
    {
        ["entity_type"] = "matter",      // Tells playbook what entity to extract for
        ["extraction_mode"] = "pre-fill"
    }
};

// 4. Execute and stream results
await foreach (var evt in _playbookService.ExecuteAsync(request, httpContext, ct))
{
    // Collect StructuredData or TextContent from NodeCompleted events
}
```

### PlaybookRunRequest Structure

```csharp
// File: Services/Ai/IPlaybookOrchestrationService.cs
public record PlaybookRunRequest
{
    public required Guid PlaybookId { get; init; }
    public required Guid[] DocumentIds { get; init; }
    public string? UserContext { get; init; }
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }
}
```

### MatterPreFillService Dependencies (Constructor)

```csharp
public MatterPreFillService(
    SpeFileStore speFileStore,                         // File storage facade
    ITextExtractor textExtractor,                      // PDF/DOCX → plain text
    IPlaybookOrchestrationService playbookService,     // AI playbook execution
    IConfiguration configuration,                      // App settings
    ILogger<MatterPreFillService> logger)
```

---

## Part 4: What Needs to Be Built for Project Pre-Fill

### Option A: Separate ProjectPreFillService (Recommended)

Follows the same pattern as `MatterPreFillService` but with project-specific config.

#### New Files

| File | Based On | Changes |
|------|----------|---------|
| `Services/Workspace/ProjectPreFillService.cs` | `MatterPreFillService.cs` | Different config key, different default GUID, different `entity_type` parameter, project field names in response |
| `Api/Workspace/WorkspaceProjectEndpoints.cs` | `WorkspaceMatterEndpoints.cs` | Route: `/workspace/projects/pre-fill` |
| `Api/Workspace/Models/ProjectPreFillResponse.cs` | `PreFillResponse.cs` | Project field names |

#### Modified Files

| File | Change |
|------|--------|
| `Infrastructure/DI/WorkspaceModule.cs` | Add `services.AddScoped<ProjectPreFillService>()` |
| `Program.cs` | Add `app.MapWorkspaceProjectEndpoints()` |

#### ProjectPreFillService Key Differences

```csharp
// Config key for project playbook
private const string PlaybookIdConfigKey = "Workspace:ProjectPreFillPlaybookId";

// Default playbook GUID (replace with actual GUID after creating playbook in Dataverse)
private static readonly Guid DefaultPreFillPlaybookId =
    Guid.Parse("XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX");  // ← SET AFTER PLAYBOOK CREATION

// Parameters tell the playbook what entity type to extract for
var request = new PlaybookRunRequest
{
    PlaybookId = playbookId,
    DocumentIds = [],
    UserContext = documentText,
    Parameters = new Dictionary<string, string>
    {
        ["entity_type"] = "project",          // ← Changed from "matter"
        ["extraction_mode"] = "pre-fill"
    }
};
```

#### ProjectPreFillResponse

```csharp
public record ProjectPreFillResponse(
    string? ProjectTypeName,          // → fuzzy-match to sprk_projecttype_ref
    string? PracticeAreaName,         // → fuzzy-match to sprk_practicearea_ref
    string? ProjectName,              // → direct text field
    string? Description,              // → direct text field
    string? AssignedAttorneyName,     // → fuzzy-match to contact
    string? AssignedParalegalName,    // → fuzzy-match to contact
    double Confidence,
    string[] PreFilledFields);
```

### Option B: Reuse MatterPreFillService with entity_type Parameter

If the playbook can handle both entity types, a single service could route to different playbooks based on `entity_type`. This is simpler but couples the two workflows.

**Recommendation**: Use Option A (separate service) for cleaner separation.

---

## Part 5: Frontend Integration (CreateProjectStep)

### Current State

`CreateProjectStep.tsx` has no AI pre-fill. It needs the same pattern as `CreateRecordStep.tsx`.

### What to Add

1. Accept `uploadedFiles` prop (passed from `ProjectWizardDialog`)
2. On mount, send files to `POST /api/workspace/projects/pre-fill`
3. Parse response into `IAiProjectPrefillFields`
4. Fuzzy-match lookup fields against Dataverse
5. Dispatch to form state + show "AI Pre-filled" badges

### IAiProjectPrefillFields Interface (New)

```typescript
export interface IAiProjectPrefillFields {
  projectTypeId?: string;
  projectTypeName?: string;
  practiceAreaId?: string;
  practiceAreaName?: string;
  projectName?: string;
  description?: string;
  assignedAttorneyId?: string;
  assignedAttorneyName?: string;
  assignedParalegalId?: string;
  assignedParalegalName?: string;
}
```

### Frontend Pre-Fill Flow (Pseudocode)

```typescript
// In CreateProjectStep useEffect (on mount when uploadedFiles.length > 0):

const bffBaseUrl = getBffBaseUrl();
const formData = new FormData();
for (const f of uploadedFiles) {
  formData.append('files', f.file, f.name);
}

const response = await authenticatedFetch(`${bffBaseUrl}/workspace/projects/pre-fill`, {
  method: 'POST',
  body: formData,
});

const data = await response.json();
const fields: IAiProjectPrefillFields = {};

if (data.projectTypeName) fields.projectTypeName = data.projectTypeName;
if (data.practiceAreaName) fields.practiceAreaName = data.practiceAreaName;
if (data.projectName) fields.projectName = data.projectName;
if (data.description) fields.description = data.description;

// Resolve lookups in parallel
const resolvePromises: Promise<void>[] = [];

if (data.projectTypeName) {
  resolvePromises.push(
    serviceRef.current.searchProjectTypes(data.projectTypeName).then((results) => {
      const best = findBestLookupMatch(data.projectTypeName, results);
      if (best) {
        fields.projectTypeId = best.id;
        fields.projectTypeName = best.name;
      }
    }).catch(() => {})
  );
}

if (data.practiceAreaName) {
  resolvePromises.push(
    serviceRef.current.searchPracticeAreas(data.practiceAreaName).then((results) => {
      const best = findBestLookupMatch(data.practiceAreaName, results);
      if (best) {
        fields.practiceAreaId = best.id;
        fields.practiceAreaName = best.name;
      }
    }).catch(() => {})
  );
}

// ... same for assignedAttorneyName, assignedParalegalName

await Promise.all(resolvePromises);

// Apply to form
setFormState((prev) => ({ ...prev, ...fields }));
```

---

## Part 6: Playbook Records in Dataverse

### Playbook Table: `sprk_analysisplaybook`

Each playbook is a record in this table with N:N relationships to scopes.

| Field | Type | Description |
|-------|------|-------------|
| `sprk_analysisplaybookid` | GUID (PK) | Primary key — this is the GUID referenced in BFF config |
| `sprk_name` | string | Display name (e.g., "Create New Matter Pre-Fill") |
| `sprk_description` | string | Human-readable description |
| `statecode` | int | 0 = Active, 1 = Inactive |

### N:N Relationships (Scopes)

| Relationship | Links To | Purpose |
|-------------|----------|---------|
| `sprk_analysisplaybook_analysisskill` | `sprk_analysisskill` | Extraction skills/prompts |
| `sprk_analysisplaybook_analysisknowledge` | `sprk_analysisknowledge` | Reference context documents |
| `sprk_analysisplaybook_analysistool` | `sprk_analysistool` | Executable tool handlers |
| `sprk_analysisplaybook_analysisaction` | `sprk_analysisaction` | Action type classification |

### Playbooks to Create

#### 1. "Create New Matter Pre-Fill"

- **Purpose**: Extract matter fields from uploaded documents
- **GUID**: Already exists: `18cf3cc8-02ec-f011-8406-7c1e520aa4df`
- **Parameters received**: `entity_type=matter`, `extraction_mode=pre-fill`
- **Expected output fields**: `matterTypeName`, `practiceAreaName`, `matterName`, `summary`, `assignedAttorneyName`, `assignedParalegalName`, `assignedOutsideCounselName`, `confidence`

#### 2. "Create New Project Pre-Fill" (NEW)

- **Purpose**: Extract project fields from uploaded documents
- **GUID**: To be created — then add to BFF config as `Workspace:ProjectPreFillPlaybookId`
- **Parameters received**: `entity_type=project`, `extraction_mode=pre-fill`
- **Expected output fields**: `projectTypeName`, `practiceAreaName`, `projectName`, `description`, `assignedAttorneyName`, `assignedParalegalName`, `confidence`

---

## Part 7: Configuration Wiring Checklist

### After Creating Playbook Records in Dataverse

1. **Note the GUIDs** of the created playbook records

2. **Update BFF Configuration** (Azure App Settings or appsettings.json):
   ```json
   {
     "Workspace": {
       "PreFillPlaybookId": "18cf3cc8-02ec-f011-8406-7c1e520aa4df",
       "ProjectPreFillPlaybookId": "<NEW-GUID-HERE>"
     }
   }
   ```

3. **Or set via Azure CLI** (for App Service):
   ```bash
   az webapp config appsettings set \
     -g spe-infrastructure-westus2 \
     -n spe-api-dev-67e2xz \
     --settings Workspace__ProjectPreFillPlaybookId=<NEW-GUID-HERE>
   ```
   Note: Use `__` (double underscore) for nested config keys in environment variables.

4. **Deploy BFF API** after code changes:
   ```powershell
   .\scripts\Deploy-BffApi.ps1
   ```

5. **Verify endpoint responds** (expect 401 = route registered, needs auth):
   ```bash
   curl -s -o /dev/null -w "%{http_code}" \
     https://spe-api-dev-67e2xz.azurewebsites.net/api/workspace/projects/pre-fill
   # Expected: 401 (NOT 404)
   ```

---

## Part 8: Existing Code to Reuse (Do Not Re-Implement)

| Component | Location | Reuse Strategy |
|-----------|----------|----------------|
| `MatterPreFillService` | `src/server/api/.../Services/Workspace/MatterPreFillService.cs` | Copy pattern for `ProjectPreFillService` |
| `WorkspaceMatterEndpoints` | `src/server/api/.../Api/Workspace/WorkspaceMatterEndpoints.cs` | Copy pattern for `WorkspaceProjectEndpoints` |
| `PreFillResponse` | `src/server/api/.../Api/Workspace/Models/PreFillResponse.cs` | Create `ProjectPreFillResponse` with project field names |
| `findBestLookupMatch()` | `src/solutions/LegalWorkspace/.../CreateMatter/CreateRecordStep.tsx` | Extract to shared utility or copy into `CreateProjectStep.tsx` |
| `authenticatedFetch` | `src/solutions/LegalWorkspace/src/services/bffAuthProvider.ts` | Import directly (already shared) |
| `getBffBaseUrl()` | `src/solutions/LegalWorkspace/src/config/bffConfig.ts` | Import directly (already shared) |
| `AiFieldTag` component | `src/solutions/LegalWorkspace/.../CreateMatter/AiFieldTag.tsx` | Import directly for "AI Pre-filled" badges |

---

## Part 9: End-to-End Verification

### Test: Create New Matter Pre-Fill

1. Open Corporate Workspace → click "Create New Matter"
2. Step 1: Upload a legal document (PDF/DOCX)
3. Step 2: Observe "Analyzing documents..." spinner
4. Verify form fields populate with AI-extracted values
5. Verify lookup fields show resolved Dataverse values (not just text)
6. Verify "AI Pre-filled" badges appear on populated fields
7. Check browser console for `[CreateMatter] Pre-fill response:` log

### Test: Create New Project Pre-Fill

1. Open Corporate Workspace → click "Create New Project"
2. Step 1: Upload a project document
3. Step 2: Observe AI pre-fill in progress
4. Verify `projectTypeName`, `practiceAreaName`, `projectName`, `description` populate
5. Check BFF logs for playbook execution: `Invoking playbook for project field extraction`

### Test: Graceful Degradation

1. Upload a file that contains no extractable entity information
2. Verify form loads empty (no crash, no error)
3. Verify user can manually fill all fields
4. Check that `confidence: 0` response produces empty form (not partial garbage)

### Test: Timeout Handling

1. If playbook takes > 45s, BFF returns empty response
2. Frontend shows no error — just loads empty form after 60s timeout
3. User can proceed normally with manual entry
