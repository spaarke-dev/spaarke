# Playbook Pre-Fill Integration Guide

> **Purpose**: Reference for creating and wiring "Create New Matter Pre-Fill" and "Create New Project Pre-Fill" playbooks into the wizard forms.
>
> **Audience**: Claude Code sessions working on playbook creation, BFF endpoint wiring, and frontend integration.
>
> **Created**: March 5, 2026
> **Updated**: March 6, 2026 — Added $choices constrained decoding, corrected Dataverse field names

---

## Overview

When a user uploads files in the Create New Matter (or Create New Project) wizard, the system:

1. Sends the raw files to the BFF API
2. BFF extracts text from the files
3. BFF invokes a **playbook** (configured by GUID) via Azure OpenAI
4. **$choices resolution**: `LookupChoicesResolver` pre-resolves Dataverse lookup/optionset values and injects them as `enum` constraints in the JSON Schema — the AI is forced to return exact Dataverse values
5. The AI returns a flat JSON with **display names** (constrained to exact Dataverse values for `$choices` fields)
6. BFF passes the JSON back to the frontend
7. Frontend resolves lookup GUIDs — exact match for `$choices`-constrained fields, fuzzy match for free-text fields (contacts, organizations)
8. Form fields are pre-filled with resolved values + "AI Pre-filled" badges

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
                                    4. PlaybookOrchestrationService.ExecuteAsync()
                                             │
                                             ├─ Load playbook nodes from Dataverse
                                             ├─ LookupChoicesResolver: pre-resolve $choices
                                             │    └─ Query sprk_mattertype_ref → ["Patent", "Trademark", ...]
                                             │    └─ Query sprk_practicearea_ref → ["Corporate", "IP", ...]
                                             ├─ PromptSchemaRenderer: JPS → prompt + JSON Schema
                                             │    └─ Inject $choices as "enum" constraints
                                             │
                                    5. Azure OpenAI (constrained decoding) ──────►
                                             │                 GPT-4o returns:
                                             │                 {
                                             │                   "matterTypeName": "Patent",
                                             │                   "practiceAreaName": "IP",
                                             │                   "matterName": "Smith v Acme",
                                             │                   "summary": "...",
                                             │                   "confidence": 0.85
                                             │                 }
                                             │                 (matterTypeName/practiceAreaName
                                             │                  guaranteed to be exact Dataverse values)
                                             │
                                    6. ParseAiResponse()
                                             │  (normalize, build preFilledFields[])
   ◄────────────────────────────────────────┘

7. HTTP 200 OK → JSON response
   │
8. Frontend parses response
   │
9. Resolve lookup GUIDs
   ├─ searchMatterTypes(webApi, "Patent") → exact match (guaranteed by $choices)
   │    → {id: "abc-123", name: "Patent"}
   ├─ searchPracticeAreas(webApi, "IP") → exact match (guaranteed by $choices)
   │    → {id: "def-456", name: "Intellectual Property"}
   └─ (all resolve in parallel via Promise.all)
   │
10. dispatch({ type: 'APPLY_AI_PREFILL', fields })
    → Form renders with pre-filled values + "AI Pre-filled" badges
```

---

## Part 1: AI Playbook Output Contract

### What the AI Playbook Must Return

The playbook returns a **flat JSON object with display names**. For fields constrained by `$choices`, the AI returns exact Dataverse values (constrained by JSON Schema `enum`). For free-text fields (contacts, names), fuzzy matching resolves on the frontend.

#### Create New Matter Pre-Fill

```json
{
  "matterTypeName": "Patent",
  "practiceAreaName": "Intellectual Property",
  "matterName": "Smith v. Acme Corp - Wrongful Termination",
  "summary": "Employment dispute involving wrongful termination claim filed by John Smith against Acme Corporation.",
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
  "description": "Review and analysis of vendor service agreements for Acme Corporation.",
  "assignedAttorneyName": "Jane Smith",
  "assignedParalegalName": "Bob Jones",
  "confidence": 0.82
}
```

### Field Rules for Playbook Prompts

| Field | Type | $choices | Resolution | Notes |
|-------|------|----------|------------|-------|
| `matterTypeName` / `projectTypeName` | string | `lookup:sprk_mattertype_ref.sprk_mattertypename` / `lookup:sprk_projecttype_ref.sprk_projecttypename` | Exact match (constrained) | AI forced to pick from Dataverse values |
| `practiceAreaName` | string | `lookup:sprk_practicearea_ref.sprk_practiceareaname` | Exact match (constrained) | Same lookup table for both matters and projects |
| `matterName` / `projectName` | string | — | Direct text (no lookup) | Max 10 words. Format: "Party v. Party - Topic" |
| `summary` / `description` | string | — | Direct text (no lookup) | Max 500 words |
| `assignedAttorneyName` | string | — | Fuzzy match to `contact.fullname` | "Jane Smith", not "J. Smith" |
| `assignedParalegalName` | string | — | Fuzzy match to `contact.fullname` | Same rule |
| `assignedOutsideCounselName` | string | — | Fuzzy match to `sprk_organization.sprk_organizationname` | Matters only |
| `confidence` | number | — | Direct value | 0.0 to 1.0 |

### Critical Rules

1. **$choices fields return exact Dataverse values** — `matterTypeName`, `practiceAreaName`, `projectTypeName` are constrained by JSON Schema `enum` at the AI level. The frontend does an exact search (case-insensitive) to resolve the GUID.
2. **Free-text fields use fuzzy matching** — `assignedAttorneyName`, `assignedParalegalName`, `assignedOutsideCounselName` are NOT constrained by `$choices` (contact tables are too large). The frontend fuzzy-matches these.
3. **Omit fields rather than guess** — if the AI can't extract a field with reasonable confidence, leave it out of the JSON entirely.
4. **Same playbook works across environments** — display names are portable, GUIDs are not.

### Fuzzy Match Thresholds (for non-$choices fields)

| Score | Condition |
|-------|-----------|
| `1.0` | Exact match (case-insensitive) |
| `0.8` | One string starts with the other ("Corporate" ↔ "Corporate Law") |
| `0.7` | One contains the other ("Trans" in "Transactional") |
| `0.5` | Single result from Dataverse search (implicit relevance) |
| `0.4` | Minimum acceptance threshold (below this = no match) |

---

## Part 2: $choices — Constrained Decoding for Lookup Fields

### How $choices Eliminates Fuzzy Matching

The JPS (JSON Prompt Schema) on the playbook's Action record defines `$choices` on lookup fields. At render time, `LookupChoicesResolver` queries Dataverse for all valid values and injects them as `"enum"` in the JSON Schema sent to Azure OpenAI.

**Before $choices**: AI returns "Intellectual Property" → fuzzy match fails → no matter type pre-filled
**After $choices**: AI forced to return "Patent" (exact Dataverse value) → exact match → matter type pre-filled

### JPS Output Fields Configuration

```json
{
  "output": {
    "structuredOutput": true,
    "fields": [
      {
        "name": "matterTypeName",
        "type": "string",
        "description": "The matter type that best matches this document",
        "$choices": "lookup:sprk_mattertype_ref.sprk_mattertypename"
      },
      {
        "name": "practiceAreaName",
        "type": "string",
        "description": "The practice area for this matter",
        "$choices": "lookup:sprk_practicearea_ref.sprk_practiceareaname"
      },
      {
        "name": "matterName",
        "type": "string",
        "description": "A concise matter name (max 10 words)",
        "maxLength": 100
      },
      {
        "name": "summary",
        "type": "string",
        "description": "Brief narrative of the matter scope and objectives",
        "maxLength": 2000
      },
      {
        "name": "confidence",
        "type": "number",
        "description": "Overall extraction confidence (0.0 to 1.0)"
      }
    ]
  }
}
```

### $choices Resolution Pipeline

```
1. LookupChoicesResolver.ResolveFromJpsAsync(Action.SystemPrompt)
   ├─ Scans JPS for $choices with Dataverse prefixes
   ├─ "lookup:sprk_mattertype_ref.sprk_mattertypename"
   │    → IScopeResolverService.QueryLookupValuesAsync()
   │    → OData: sprk_mattertype_refs?$select=sprk_mattertypename&$filter=statecode eq 0
   │    → ["Patent", "Trademark", "Copyright", "Litigation", ...]
   └─ Returns Dictionary<"lookup:sprk_mattertype_ref.sprk_mattertypename", string[]>

2. AiAnalysisNodeExecutor → ToolExecutionContext.PreResolvedLookupChoices

3. GenericAnalysisHandler → PromptSchemaRenderer.Render()

4. PromptSchemaRenderer.ResolveChoices()
   └─ Injects values as "enum" in JSON Schema:
      { "name": "matterTypeName", "type": "string", "enum": ["Patent", "Trademark", ...] }

5. Azure OpenAI constrained decoding → AI MUST pick from enum values
```

### Supported $choices Prefixes

| Prefix | Use Case | Example |
|--------|----------|---------|
| `lookup:` | Reference entity record values | `"lookup:sprk_mattertype_ref.sprk_mattertypename"` |
| `optionset:` | Single-select choice/picklist labels | `"optionset:sprk_matter.sprk_matterstatus"` |
| `multiselect:` | Multi-select picklist labels | `"multiselect:sprk_matter.sprk_jurisdictions"` |
| `boolean:` | Two-option boolean field labels | `"boolean:sprk_matter.sprk_isconfidential"` |
| `downstream:` | Downstream node routing (playbook patterns) | `"downstream:update_doc.sprk_documenttype"` |

---

## Part 3: Dataverse Lookup Tables

The frontend resolves AI display names by searching these Dataverse tables:

| AI Field | Dataverse Table | Search Field | ID Field | $choices | Used By |
|----------|----------------|--------------|----------|----------|---------|
| `matterTypeName` | `sprk_mattertype_ref` | `sprk_mattertypename` | `sprk_mattertype_refid` | Yes — exact match | Matter |
| `projectTypeName` | `sprk_projecttype_ref` | `sprk_projecttypename` | `sprk_projecttype_refid` | Yes — exact match | Project |
| `practiceAreaName` | `sprk_practicearea_ref` | `sprk_practiceareaname` | `sprk_practicearea_refid` | Yes — exact match | Both |
| `assignedAttorneyName` | `contact` | `fullname` | `contactid` | No — fuzzy match | Both |
| `assignedParalegalName` | `contact` | `fullname` | `contactid` | No — fuzzy match | Both |
| `assignedOutsideCounselName` | `sprk_organization` | `sprk_organizationname` | `sprk_organizationid` | No — fuzzy match | Matter |

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

**With $choices**: For constrained fields (matterTypeName, practiceAreaName, projectTypeName), the AI always returns an exact Dataverse value, so `findBestLookupMatch` will always score `1.0` (exact match). The fuzzy matching logic is a safety net, not the primary resolution mechanism.

---

## Part 4: BFF API — Implementation

### Current Configuration

| Item | Value |
|------|-------|
| **Matter Endpoint** | `POST /api/workspace/matters/pre-fill` |
| **Project Endpoint** | `POST /api/workspace/projects/pre-fill` |
| **Matter Service** | `MatterPreFillService` (scoped) |
| **Project Service** | `ProjectPreFillService` (scoped) |
| **Matter Config Key** | `Workspace:PreFillPlaybookId` |
| **Project Config Key** | `Workspace:ProjectPreFillPlaybookId` |
| **Default Matter Playbook GUID** | `18cf3cc8-02ec-f011-8406-7c1e520aa4df` |
| **Timeout** | 45s (BFF), 60s (frontend) |
| **Rate Limit** | 10 req/min per user (`ai-stream` policy) |

### Key Files

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs` | Matter: extract text, invoke playbook, parse AI response |
| `src/server/api/Sprk.Bff.Api/Services/Workspace/ProjectPreFillService.cs` | Project: same pattern as matter |
| `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceMatterEndpoints.cs` | Matter endpoint registration |
| `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceProjectEndpoints.cs` | Project endpoint registration |
| `src/server/api/Sprk.Bff.Api/Api/Workspace/Models/PreFillResponse.cs` | Response DTO (shared) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/LookupChoicesResolver.cs` | $choices pre-resolution from Dataverse |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PromptSchemaRenderer.cs` | JPS → prompt text + JSON Schema |

### PreFillResponse Model

```csharp
// File: Api/Workspace/Models/PreFillResponse.cs
public record PreFillResponse(
    string? MatterTypeName,
    string? PracticeAreaName,
    string? MatterName,
    string? Summary,
    double Confidence,
    string[] PreFilledFields);

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

// 4. Execute — LookupChoicesResolver runs automatically in the pipeline
//    $choices on matterTypeName/practiceAreaName are resolved from Dataverse
//    and injected as enum constraints before the AI call
await foreach (var evt in _playbookService.ExecuteAsync(request, httpContext, ct))
{
    // Collect StructuredData or TextContent from NodeCompleted events
}
```

---

## Part 5: Frontend Integration

### CreateRecordStep (Matter — Implemented)

The matter wizard already has AI pre-fill in `CreateRecordStep.tsx`:
1. On mount with `uploadedFiles`, sends to `/api/workspace/matters/pre-fill`
2. Parses response and resolves lookups via `findBestLookupMatch`
3. Applies to form state with "AI Pre-filled" badges

### CreateProjectStep (Project — Planned)

Follows the same pattern:
1. Accept `uploadedFiles` prop from `ProjectWizardDialog`
2. On mount, send files to `POST /api/workspace/projects/pre-fill`
3. Parse response, resolve lookups, apply to form

---

## Part 6: Playbook Records in Dataverse

### Playbook Table: `sprk_analysisplaybook`

| Field | Type | Description |
|-------|------|-------------|
| `sprk_analysisplaybookid` | GUID (PK) | Primary key — this is the GUID referenced in BFF config |
| `sprk_name` | string | Display name (e.g., "Create New Matter Pre-Fill") |
| `sprk_description` | string | Human-readable description |
| `statecode` | int | 0 = Active, 1 = Inactive |

### Playbooks

#### 1. "Create New Matter Pre-Fill"

- **GUID**: `18cf3cc8-02ec-f011-8406-7c1e520aa4df`
- **Parameters**: `entity_type=matter`, `extraction_mode=pre-fill`
- **$choices fields**: `matterTypeName` → `lookup:sprk_mattertype_ref.sprk_mattertypename`, `practiceAreaName` → `lookup:sprk_practicearea_ref.sprk_practiceareaname`
- **Free-text fields**: `matterName`, `summary`, `assignedAttorneyName`, `assignedParalegalName`, `assignedOutsideCounselName`, `confidence`

#### 2. "Create New Project Pre-Fill" (NEW)

- **GUID**: To be created — then add to BFF config as `Workspace:ProjectPreFillPlaybookId`
- **Parameters**: `entity_type=project`, `extraction_mode=pre-fill`
- **$choices fields**: `projectTypeName` → `lookup:sprk_projecttype_ref.sprk_projecttypename`, `practiceAreaName` → `lookup:sprk_practicearea_ref.sprk_practiceareaname`
- **Free-text fields**: `projectName`, `description`, `assignedAttorneyName`, `assignedParalegalName`, `confidence`

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

4. **Ensure JPS has $choices** on the playbook's Action `sprk_systemprompt`:
   - `matterTypeName` / `projectTypeName` field: `"$choices": "lookup:sprk_mattertype_ref.sprk_mattertypename"` (or project equivalent)
   - `practiceAreaName` field: `"$choices": "lookup:sprk_practicearea_ref.sprk_practiceareaname"`
   - `structuredOutput: true` must be enabled

5. **Deploy BFF API** after code changes:
   ```powershell
   .\scripts\Deploy-BffApi.ps1
   ```

6. **Verify endpoint responds** (expect 401 = route registered, needs auth):
   ```bash
   curl -s -o /dev/null -w "%{http_code}" \
     https://spe-api-dev-67e2xz.azurewebsites.net/api/workspace/projects/pre-fill
   # Expected: 401 (NOT 404)
   ```

---

## Part 8: Existing Code to Reuse

| Component | Location | Reuse Strategy |
|-----------|----------|----------------|
| `MatterPreFillService` | `Services/Workspace/MatterPreFillService.cs` | Copy pattern for `ProjectPreFillService` |
| `LookupChoicesResolver` | `Services/Ai/LookupChoicesResolver.cs` | Automatic — runs in playbook pipeline for any JPS with `$choices` |
| `PromptSchemaRenderer` | `Services/Ai/PromptSchemaRenderer.cs` | Automatic — renders JPS with resolved `$choices` as enum |
| `findBestLookupMatch()` | `CreateMatter/CreateRecordStep.tsx` | Copy into `CreateProjectStep.tsx` |
| `authenticatedFetch` | `services/bffAuthProvider.ts` | Import directly |
| `getBffBaseUrl()` | `config/bffConfig.ts` | Import directly |
| `AiFieldTag` component | `CreateMatter/AiFieldTag.tsx` | Import directly for badges |

---

## Part 9: End-to-End Verification

### Test: Create New Matter Pre-Fill

1. Open Corporate Workspace → click "Create New Matter"
2. Step 1: Upload a legal document (PDF/DOCX)
3. Step 2: Observe "Analyzing documents..." spinner
4. Verify form fields populate with AI-extracted values
5. **Verify lookup fields show exact Dataverse values** — "Patent" not "Intellectual Property" (constrained by $choices)
6. Verify "AI Pre-filled" badges appear on populated fields
7. Check BFF logs for `$choices lookup resolved for field 'matterTypeName': N values`

### Test: $choices Constrained Decoding

1. Check BFF logs for `Found N $choices references to resolve`
2. Verify `matterTypeName` value is in the Dataverse `sprk_mattertype_ref` table (exact match)
3. Verify `practiceAreaName` value is in the Dataverse `sprk_practicearea_ref` table (exact match)
4. If JPS does NOT have `$choices`, AI returns best-guess names → fuzzy match still works as fallback

### Test: Graceful Degradation

1. Upload a file with no extractable entity information
2. Verify form loads empty (no crash, no error)
3. Verify user can manually fill all fields
4. Check that `confidence: 0` response produces empty form

### Test: Timeout Handling

1. If playbook takes > 45s, BFF returns empty response
2. Frontend shows no error — just loads empty form after 60s timeout
3. User can proceed normally with manual entry
