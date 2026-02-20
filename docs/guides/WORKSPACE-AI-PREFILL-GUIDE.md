# Workspace AI Pre-Fill Guide

> **Version:** 1.0.0
> **Last Updated:** February 20, 2026
> **Applies To:** Corporate Workspace SPA — AI-powered form field extraction for entity creation wizards

---

## TL;DR

AI Pre-Fill analyzes uploaded files during entity creation (e.g., Create New Matter) to automatically populate form fields. The client sends files to a BFF endpoint, which extracts text and invokes an AI Playbook to return structured field suggestions. Fields are populated with visual "AI" tags and remain fully editable. The current implementation uses a dedicated pre-fill playbook with a 45-second execution timeout; r2 will integrate this into the full Playbook Node-Based architecture for configurable extraction scopes.

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────────┐
│  Create Entity Wizard (Vite SPA)                                  │
│  └─ CreateRecordStep.tsx                                          │
│     ├─ Uploads files as multipart/form-data                       │
│     ├─ Receives IAiPrefillResponse                                │
│     ├─ Fuzzy-matches names → Dataverse lookup IDs                 │
│     └─ Applies field values with "AI" sparkle tags                │
└──────────┬────────────────────────────────────────────────────────┘
           │
    POST /api/workspace/matters/pre-fill
    (multipart/form-data, OBO auth)
           │
    ┌──────▼──────────────────────────────────────────────┐
    │  BFF: WorkspaceMatterEndpoints.cs                    │
    │  └─ MatterPreFillService.cs                          │
    │     ├─ 1. Validate files (type, size)                │
    │     ├─ 2. Stage files → SpeFileStore                 │
    │     ├─ 3. Extract text → ITextExtractor               │
    │     ├─ 4. Invoke Playbook → IPlaybookOrchestrationService │
    │     └─ 5. Parse AI output → PreFillResponse          │
    └──────┬──────────────┬──────────────┬─────────────────┘
           │              │              │
    SpeFileStore     ITextExtractor   Playbook
    (staging)        (PDF/DOCX/XLSX)  (Azure OpenAI)
```

### Key Components

| Component | Location | Purpose |
|-----------|----------|---------|
| **CreateRecordStep.tsx** | `src/solutions/LegalWorkspace/src/components/CreateMatter/` | Step 2 form with AI pre-fill trigger on mount |
| **AiFieldTag.tsx** | Same directory | Sparkle "AI" badge for pre-filled fields |
| **formTypes.ts** | Same directory | `IAiPrefillRequest`, `IAiPrefillResponse`, `IAiPrefillFields` |
| **WorkspaceMatterEndpoints.cs** | `src/server/api/Sprk.Bff.Api/Api/Workspace/` | `POST /api/workspace/matters/pre-fill` endpoint |
| **PreFillResponse.cs** | `src/server/api/Sprk.Bff.Api/Api/Workspace/Models/` | BFF response record |
| **MatterPreFillService.cs** | `src/server/api/Sprk.Bff.Api/Services/Workspace/` | Orchestrates file staging, text extraction, AI invocation |
| **IPlaybookOrchestrationService** | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Playbook execution engine |

---

## Pre-Fill Flow (End to End)

### 1. Client Triggers Pre-Fill

When the user advances to Step 2 (Create Record Form) with files uploaded in Step 1, the `CreateRecordStep` component automatically triggers AI pre-fill on mount:

```typescript
// CreateRecordStep.tsx — triggered on component mount when files exist
useEffect(() => {
  if (uploadedFileNames.length === 0) return;
  dispatch({ type: 'AI_PREFILL_LOADING' });

  const formData = new FormData();
  // Append actual file blobs from Step 1
  uploadedFiles.forEach(f => formData.append('files', f.blob, f.name));

  authenticatedFetch('/api/workspace/matters/pre-fill', {
    method: 'POST',
    body: formData,
    signal: AbortSignal.timeout(60_000), // 60s client timeout
  })
    .then(res => res.json())
    .then((data: IAiPrefillResponse) => {
      dispatch({ type: 'APPLY_AI_PREFILL', fields: data.fields });
      dispatch({ type: 'AI_PREFILL_SUCCESS' });
    })
    .catch(() => {
      dispatch({ type: 'AI_PREFILL_ERROR' });
      // Graceful: form remains empty, no error modal
    });
}, []);
```

### 2. BFF Receives Files

**Endpoint**: `POST /api/workspace/matters/pre-fill`

| Property | Value |
|----------|-------|
| Content-Type | `multipart/form-data` |
| Auth | OBO Bearer token (via `WorkspaceAuthorizationFilter`) |
| Rate Limit | 10 req/min per user (`ai-stream` policy) |
| Max File Size | 10 MB per file |
| Allowed Extensions | `.pdf`, `.docx`, `.xlsx` |

### 3. MatterPreFillService Orchestration

```
Receive multipart/form-data files
    │
    ├─ [Validate] Reject invalid files → 400 ProblemDetails
    │   • At least one file
    │   • Extension in [.pdf, .docx, .xlsx]
    │   • Content-Type matches (defence-in-depth)
    │   • File size ≤ 10 MB each
    │
    ├─ [Stage] Upload to SpeFileStore
    │   • Prefix: ai-prefill/{requestId}/{fileName}
    │   • Falls back to in-memory extraction if staging unavailable
    │
    ├─ [Extract] ITextExtractor.ExtractAsync()
    │   • Supports PDF, DOCX, XLSX
    │   • Combined text truncated to ~80KB (~20K tokens)
    │   • Skips unsupported types gracefully
    │
    ├─ [Invoke Playbook] IPlaybookOrchestrationService.ExecuteAsync()
    │   • Playbook ID: configurable (Workspace:PreFillPlaybookId)
    │   • Default: 18cf3cc8-02ec-f011-8406-7c1e520aa4df
    │   • UserContext: extracted text
    │   • Parameters: { entity_type: "matter", extraction_mode: "pre-fill" }
    │   • Timeout: 45 seconds
    │
    ├─ [Listen] Consume NodeCompleted stream events
    │   • Read NodeOutput.StructuredData (JSON)
    │   • Falls back to NodeOutput.TextContent
    │   • Extract confidence score
    │
    └─ [Parse & Return] PreFillResponse
        • Strip markdown code fences from AI output
        • Deserialize to AiPreFillResult DTO
        • Map to PreFillResponse record
        • Return preFilledFields array (only extracted fields)
```

### 4. Client Applies Pre-Fill Results

The `APPLY_AI_PREFILL` reducer action:

1. Sets scalar fields directly (e.g., `matterName`, `summary`)
2. For lookup fields (e.g., `matterTypeName`), **fuzzy-matches** the AI-suggested name against Dataverse ref table records to resolve the lookup GUID
3. Tracks which fields were pre-filled in `prefilledFields: Set<keyof ICreateMatterFormState>`
4. Renders `<AiFieldTag>` sparkle badge next to each pre-filled field label

---

## Data Contracts

### Client → BFF Request

```typescript
// formTypes.ts
interface IAiPrefillRequest {
  fileNames: string[];
}
// Actual transport: multipart/form-data with file blobs
```

### BFF → Client Response

```typescript
// formTypes.ts
interface IAiPrefillResponse {
  fields: IAiPrefillFields;
}

interface IAiPrefillFields {
  matterTypeId?: string;
  matterTypeName?: string;     // AI suggests display name
  practiceAreaId?: string;
  practiceAreaName?: string;   // AI suggests display name
  matterName?: string;
  summary?: string;
}
```

```csharp
// PreFillResponse.cs
record PreFillResponse(
    string? MatterTypeName,
    string? PracticeAreaName,
    string? MatterName,
    string? Summary,
    double Confidence,            // 0.0–1.0
    string[] PreFilledFields      // e.g., ["matterTypeName", "summary"]
);
```

### Form State Management

```typescript
// Reducer actions for AI pre-fill lifecycle
type FormAction =
  | { type: 'SET_FIELD'; field: ...; value: string }
  | { type: 'SET_LOOKUP'; idField: ...; nameField: ...; id: string; name: string }
  | { type: 'APPLY_AI_PREFILL'; fields: IAiPrefillFields }
  | { type: 'AI_PREFILL_LOADING' }
  | { type: 'AI_PREFILL_SUCCESS' }
  | { type: 'AI_PREFILL_ERROR' }
```

---

## Pre-Fill vs. Document Profile

These are distinct AI operations with different purposes:

| Property | AI Pre-Fill | Document Profile |
|----------|-------------|------------------|
| **Purpose** | Suggest entity form fields from uploaded files | Enrich individual document records with AI metadata |
| **When** | During entity creation (Step 2 form load) | After document records exist (Step 4 finish) |
| **Target** | Parent entity fields (matter name, type, etc.) | Document fields (summary, keywords, entities, classification) |
| **Auth** | OBO (user's token, real-time) | App-only (background job, asynchronous) |
| **Transport** | HTTP POST → stream response | Service Bus → background handler |
| **Failure** | Graceful: empty form, no error shown | Non-fatal: document exists, profiling runs later |
| **Timeout** | 45s (BFF) / 60s (client) | No timeout (async job) |
| **Playbook** | Pre-fill playbook (configurable ID) | "Document Profile" playbook |

```
Create Entity Wizard Timeline:

Step 1: Upload Files ──→ Step 2: Form ──→ Step 3: Next Steps ──→ Step 4: Finish
                              │                                        │
                         AI Pre-Fill                            Document Profile
                         (real-time)                           (async, per-doc)
                              │                                        │
                    ┌─────────▼──────────┐              ┌──────────────▼─────────────┐
                    │ POST /workspace/   │              │ POST /documents/{id}/      │
                    │ matters/pre-fill   │              │ analyze                    │
                    │ (OBO, streaming)   │              │ (OBO → Service Bus → app)  │
                    └────────────────────┘              └────────────────────────────┘
```

---

## AI Playbook Integration

### Current Architecture (Playbook Service)

The pre-fill uses `IPlaybookOrchestrationService` — the same playbook engine used across all SDAP AI features.

```
IPlaybookOrchestrationService.ExecuteAsync()
    │
    ├─ Mode detection:
    │   • Legacy: No nodes → delegates to IAnalysisOrchestrationService
    │   • NodeBased: Has nodes → ExecutionGraph with parallel batches
    │
    ├─ Stream events via Channel<PlaybookStreamEvent>:
    │   • RunStarted → NodeStarted → NodeProgress → NodeCompleted → RunCompleted
    │
    ├─ Pre-fill consumes NodeCompleted event:
    │   • NodeOutput.StructuredData (JSON) — preferred
    │   • NodeOutput.TextContent — fallback (parsed as JSON)
    │   • Confidence score from node
    │
    └─ Run tracking:
        • ConcurrentDictionary<Guid, PlaybookRunContext>
        • Per-run context: RunId, PlaybookId, DocumentIds, UserContext, Parameters
        • 1-hour cleanup
```

### Playbook Configuration

| Setting | Config Key | Default Value |
|---------|-----------|---------------|
| Playbook ID | `Workspace:PreFillPlaybookId` | `18cf3cc8-02ec-f011-8406-7c1e520aa4df` |
| Staging Container | `SharePointEmbedded:StagingContainerId` | Environment-specific |
| Text Limit | Hardcoded | 80KB (~20K tokens) |
| Execution Timeout | Hardcoded | 45 seconds |
| Max Parallel Nodes | Hardcoded | 3 |

---

## Error Handling

| Failure | Severity | Behavior |
|---------|----------|----------|
| Invalid file type/size | Hard reject | 400 ProblemDetails to client |
| SPE staging fails | Fallback | In-memory extraction continues |
| Text extraction fails | Soft | Skip file, try remaining files |
| All text extraction fails | Soft | Empty PreFillResponse (confidence=0) |
| Playbook timeout (45s) | Soft | Return empty PreFillResponse |
| Playbook node error | Soft | Return partial results if available |
| Rate limit exceeded | Hard reject | 429 Too Many Requests |
| Network error (client) | Graceful | Form remains empty, no error modal |

**Design principle**: Pre-fill failures NEVER block entity creation. The user can always fill the form manually.

---

## UI Behavior

### Loading State

While pre-fill is in progress (`status: 'loading'`):
- Skeleton placeholders shown in form fields
- "Analyzing files..." indicator text
- Form fields disabled (prevents user edits during analysis)

### Success State

When pre-fill completes (`status: 'success'`):
- Fields populated with AI-suggested values
- `<AiFieldTag>` sparkle badge next to each pre-filled field label
- "AI Pre-filled" top-right badge when any fields populated
- All fields fully editable (user can override AI suggestions)

### Error State

On pre-fill failure (`status: 'error'`):
- Form renders empty (no error modal)
- User fills fields manually
- No indication of failure (by design — pre-fill is a convenience, not a requirement)

### AiFieldTag Component

```
┌────────────────────────┐
│ ✦ AI                    │   ← SparkleRegular icon + "AI" text
│ colorBrandBackground2   │   ← Semantic Fluent token (theme-aware)
│ aria-label="AI pre-filled"│
└────────────────────────┘
```

- Zero hardcoded colors
- Supports light, dark, high-contrast themes
- Accessible with screen reader label

---

## Extending to Other Entity Types

The pre-fill pattern is parameterized for reuse:

### Current: Matter Pre-Fill

- Endpoint: `POST /api/workspace/matters/pre-fill`
- Playbook parameters: `{ entity_type: "matter", extraction_mode: "pre-fill" }`
- Extracted fields: matterTypeName, practiceAreaName, matterName, summary

### Future: Project Pre-Fill

To add pre-fill for Project creation:

1. Create `POST /api/workspace/projects/pre-fill` endpoint (or parameterize existing endpoint)
2. Create `ProjectPreFillService` following `MatterPreFillService` pattern
3. Configure project-specific playbook ID (or add `entity_type: "project"` parameter to shared playbook)
4. Define `IProjectPrefillFields` with project-specific fields
5. Add fuzzy-matching for project ref tables

### Entity-Agnostic Pre-Fill (Future)

A unified endpoint `POST /api/workspace/{entityType}/pre-fill` could:
- Accept `entityType` as path parameter
- Route to entity-specific pre-fill services
- Share common infrastructure (file validation, text extraction, playbook invocation)
- Use entity-type-specific playbook configurations

---

## Pre-Fill R2 Roadmap

### Current Limitations (R1)

| Limitation | Impact |
|-----------|--------|
| Single playbook ID for all matter pre-fills | Cannot customize extraction scope per matter type |
| Hardcoded field set (5 fields) | Cannot add new extractable fields without code changes |
| No document-type-aware extraction | Same extraction regardless of PDF vs. contract vs. invoice |
| Confidence score not surfaced to user | User cannot gauge AI reliability |
| No feedback loop | AI cannot learn from user corrections |
| Text-only extraction | No image/table analysis from uploaded files |
| 80KB text limit | Large documents may lose important content |

### R2: Playbook Node-Based Integration

The current pre-fill uses the playbook platform but with a **single monolithic prompt**. R2 integrates pre-fill into the **Node-Based Playbook Architecture** for configurable, multi-step extraction.

#### R2 Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│  Pre-Fill Playbook (Node-Based)                                   │
│                                                                   │
│  ┌─────────┐   ┌──────────────┐   ┌─────────────┐   ┌─────────┐ │
│  │ Extract  │──▶│ Classify     │──▶│ Extract     │──▶│ Format  │ │
│  │ Text     │   │ Document Type│   │ Entity      │   │ Output  │ │
│  │ (Node 1) │   │ (Node 2)     │   │ Fields      │   │ (Node 4)│ │
│  └─────────┘   └──────────────┘   │ (Node 3)     │   └─────────┘ │
│                                    └─────────────┘               │
│                                         ▲                        │
│                                         │                        │
│                          ┌──────────────┴────────────┐           │
│                          │ Scope: configured per      │           │
│                          │ entity type + doc type     │           │
│                          └───────────────────────────┘           │
└──────────────────────────────────────────────────────────────────┘
```

#### R2 Node Definitions

| Node | Purpose | AI Resource | Configurable |
|------|---------|-------------|-------------|
| **Extract Text** | PDF/DOCX/XLSX → text | Azure Document Intelligence | OCR mode, page limits |
| **Classify Document** | Determine document type (contract, letter, invoice, etc.) | Azure OpenAI (fast model) | Classification taxonomy |
| **Extract Entity Fields** | Pull structured fields based on entity type + doc type | Azure OpenAI (reasoning model) | Field schema per entity type |
| **Format Output** | Normalize names, validate types, compute confidence | Code node (no AI) | Output schema |

#### R2 Configurable Scopes

Instead of a hardcoded field set, R2 uses **Playbook Scopes** defined in Dataverse:

```
sprk_playbookscope (Dataverse entity)
├── sprk_name: "Matter Pre-Fill — Litigation"
├── sprk_entitytype: "sprk_matter"
├── sprk_documentclassification: "Contract"
├── sprk_extractionfields: [
│     { name: "matterTypeName", type: "lookup", refTable: "sprk_mattertype_ref" },
│     { name: "practiceAreaName", type: "lookup", refTable: "sprk_practicearea_ref" },
│     { name: "matterName", type: "text", maxLength: 200 },
│     { name: "summary", type: "multiline", maxLength: 4000 },
│     { name: "estimatedBudget", type: "currency" },
│     { name: "jurisdiction", type: "lookup", refTable: "sprk_jurisdiction_ref" }
│   ]
├── sprk_aimodel: "gpt-4o" (configurable)
└── sprk_prompttemplate: "Extract the following fields from this {docType}..."
```

**Benefits**:
- Add new extractable fields without code changes
- Different extraction prompts per document type
- Per-entity-type field schemas
- Configurable AI model (cost vs. quality tradeoff)
- Custom prompt templates for domain-specific extraction

#### R2 Enhanced AI Resources

| Resource | R1 (Current) | R2 (Planned) |
|----------|-------------|--------------|
| Text Extraction | `ITextExtractor` (basic) | Azure Document Intelligence (OCR, tables, layout) |
| Classification | None | Azure OpenAI with classification taxonomy |
| Field Extraction | Single prompt, all fields | Per-field-type prompts with validation |
| Confidence | Single score for entire response | Per-field confidence scores |
| Feedback | None | User corrections stored for prompt refinement |

#### R2 Implementation Checklist

- [ ] Create `sprk_playbookscope` entity in Dataverse
- [ ] Define scope records for Matter, Project, Invoice entity types
- [ ] Update playbook to use Node-Based architecture (4 nodes)
- [ ] Add document classification node (document type detection)
- [ ] Add per-field confidence scores to response
- [ ] Create `POST /api/workspace/{entityType}/pre-fill` unified endpoint
- [ ] Add Azure Document Intelligence integration for OCR/table extraction
- [ ] Add user correction tracking (for prompt refinement)
- [ ] Surface confidence indicators in wizard UI
- [ ] Add "Retry with different model" option for low-confidence results

---

## Configuration Reference

### BFF Configuration

```json
{
  "Workspace": {
    "PreFillPlaybookId": "18cf3cc8-02ec-f011-8406-7c1e520aa4df"
  },
  "SharePointEmbedded": {
    "StagingContainerId": "b!..."
  }
}
```

### Rate Limiting

```csharp
// ai-stream policy: 10 requests per minute per user
options.AddPolicy("ai-stream", partition =>
    RateLimitPartition.GetSlidingWindowLimiter(
        partition, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 2
        }));
```

### File Constraints

| Constraint | Value |
|-----------|-------|
| Allowed extensions | `.pdf`, `.docx`, `.xlsx` |
| Max file size | 10 MB per file |
| Text extraction limit | 80 KB (~20K tokens) |
| BFF timeout | 45 seconds |
| Client timeout | 60 seconds |
| Rate limit | 10 req/min per user |

---

## Key Files Reference

```
Client (Vite SPA):
src/solutions/LegalWorkspace/src/
├── components/CreateMatter/
│   ├── CreateRecordStep.tsx      # Step 2 form with pre-fill trigger
│   ├── AiFieldTag.tsx            # Sparkle "AI" badge
│   ├── formTypes.ts              # IAiPrefillRequest/Response types
│   └── LookupField.tsx           # Lookup with isAiPrefilled prop

BFF (ASP.NET Core):
src/server/api/Sprk.Bff.Api/
├── Api/Workspace/
│   ├── WorkspaceMatterEndpoints.cs   # POST /api/workspace/matters/pre-fill
│   └── Models/PreFillResponse.cs     # Response record
├── Services/Workspace/
│   └── MatterPreFillService.cs       # Orchestration service
└── Services/Ai/
    ├── IPlaybookOrchestrationService.cs  # Playbook interface
    └── PlaybookOrchestrationService.cs   # Playbook engine
```
