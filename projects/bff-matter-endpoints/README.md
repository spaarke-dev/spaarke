# BFF Matter Endpoints — Mini-Project

> **Branch**: `work/bff-matter-endpoints`
> **PR**: #188 (draft)
> **Status**: Implementation

---

## Objective

Wire up the **Create New Matter** wizard to production BFF endpoints and the playbook-based AI system. The front-end wizard (LegalWorkspace) already has a complete UI — this project connects it to real backend services using existing infrastructure patterns.

### Design Principles

1. **Use existing upload endpoints** — Files upload via `PUT /api/obo/containers/{containerId}/files/{fileName}` (already built). No custom `/matters/files` endpoint.
2. **Front-end creates Dataverse records** — `sprk_matter` and `sprk_document` records created via `Xrm.WebApi.createRecord()` with `@odata.bind` lookups. No BFF involvement for CRUD.
3. **Playbook-based AI** — All AI processing uses `IPlaybookOrchestrationService` with configurable playbook nodes. No hardcoded prompts or direct `IOpenAiClient` calls.
4. **Generic entity creation** — Front-end `EntityCreationService` supports Matter, Project, and future entity types.
5. **MSAL authentication** — PCF controls acquire tokens via `MsalAuthProvider` for BFF calls.

### What Already Exists

| Layer | Component | Status |
|-------|-----------|--------|
| Front-end | `matterService.ts` — wizard orchestration | Complete (scaffold) |
| Front-end | `CreateRecordStep.tsx` — form with AI generate button | Complete |
| Front-end | `WizardDialog.tsx` — full wizard flow | Complete |
| BFF API | `WorkspaceMatterEndpoints.cs` — `/api/workspace/matters/pre-fill` | **Implemented (hardcoded AI)** |
| BFF API | `MatterPreFillService.cs` — direct IOpenAiClient calls | **Needs refactor to playbooks** |
| BFF API | `WorkspaceAiService.cs` — stub/mock implementations | **Needs real playbook integration** |
| BFF API | Playbook orchestration system | **Fully built** (nodes, executors, handlers) |
| BFF API | SPE upload endpoint `PUT /api/obo/containers/{id}/files/{name}` | **Fully built** |
| BFF API | Existing handlers: SummaryHandler, DocumentClassifierHandler, EntityExtractorHandler | **Fully built** |
| Shared | `@spaarke/sdap-client` — SPE upload/download library | **Available** |

---

## Architecture

### Flow Overview

```
┌──────────────────────────┐       ┌────────────────────────┐       ┌────────────────┐
│  LegalWorkspace UI       │       │  BFF API               │       │  External Svcs │
│  (Dataverse host)        │       │  (Azure App Service)   │       │                │
│                          │       │                        │       │                │
│  Step 1: Upload ─────────┼──────►│  PUT /api/obo/         │──────►│  SpeFileStore  │
│  (SdapApiClient + MSAL)  │       │  containers/{id}/      │       │  (Graph API)   │
│                          │       │  files/{name}          │       │                │
│                          │       │  (EXISTING endpoint)   │       │                │
│                          │       │                        │       │                │
│  Step 2: AI Pre-Fill ────┼──────►│  POST /api/workspace/  │──────►│  Playbook      │
│  ("Generate with AI")    │       │  matters/pre-fill      │       │  Orchestrator  │
│                          │       │  (refactored to use    │       │  → Summarize   │
│                          │       │   playbook system)     │       │  → Classify    │
│                          │       │                        │       │  → Extract     │
│                          │       │                        │       │                │
│  Create Matter ──────────┼──┐   │                        │       │                │
│  (Xrm.WebApi)            │  │   │                        │       │                │
│                          │  │   │                        │       │                │
│  Create sprk_document ───┼──┤   │                        │       │                │
│  (Xrm.WebApi)            │  │   │                        │       │                │
│                          │  │   │                        │       │                │
│  Draft Summary Email ────┼──┘   │                        │       │                │
│  (Xrm.WebApi email)      │     │                        │       │                │
└──────────────────────────┘     └────────────────────────┘       └────────────────┘
```

### Key Principle: Dataverse-First, BFF for Files + AI Only

- **BFF handles**: SPE file uploads (Graph API), AI processing (playbook orchestration)
- **Front-end handles**: All Dataverse CRUD (`sprk_matter`, `sprk_document`, `email` activity)
- **Playbooks handle**: AI analysis configuration — prompts, scopes, and handler orchestration are configurable in Dataverse without code deployment

---

## What's Changing

### BFF API Changes

| Component | Change | Description |
|-----------|--------|-------------|
| `MatterPreFillService.cs` | **Refactor** | Replace direct `IOpenAiClient.GetCompletionAsync()` with `IPlaybookOrchestrationService.ExecuteAsync()` |
| `WorkspaceAiService.cs` | **Implement** | Replace mock stubs with real playbook execution calls |
| `WorkspaceMatterEndpoints.cs` | **Modify** | Update pre-fill handler to use refactored service |
| `PreFillResponse.cs` | **Modify** | Align response fields with new form shape |
| `WorkspaceModule.cs` | **Modify** | Update DI registrations if needed |

### Front-End Changes

| Component | Change | Description |
|-----------|--------|-------------|
| `matterService.ts` | **Refactor** | Replace custom upload with `SdapApiClient`, add MSAL auth for BFF calls, remove `/files` and `/draft-summary` endpoints |
| `EntityCreationService.ts` | **New** | Generic entity creation service (shared across Matter/Project/future) |
| `bffConfig.ts` | **New** | BFF base URL discovery from PCF parameter `sdapApiBaseUrl` |

### Playbook Configuration (Dataverse)

| Component | Change | Description |
|-----------|--------|-------------|
| Entity Pre-Fill Skill | **New** | `sprk_analysisskill` record with extraction prompt template |
| Entity Pre-Fill Tool | **New or reuse** | `sprk_analysistool` record — either new `EntityPreFillHandler` or configure `GenericAnalysisHandler` |
| Document Profile playbook | **Modify** | Add pre-fill node to existing playbook (ID: `18cf3cc8-02ec-f011-8406-7c1e520aa4df`) |

---

## Playbook Integration Design

### Current State (Problem)

`MatterPreFillService.cs` bypasses the playbook system entirely:
- Line 17: "PlaybookService integration is not suitable for ad-hoc structured JSON extraction"
- Line 290: Direct `IOpenAiClient.GetCompletionAsync(prompt)` call
- Lines 322-349: Hardcoded extraction prompt with fixed JSON schema
- Lines 441-466: Internal `AiPreFillResult` class with hardcoded fields

### Target State (Playbook-Based)

```
POST /api/workspace/matters/pre-fill
    ↓
WorkspaceMatterEndpoints.HandlePreFill()
    ↓
WorkspaceAiService.GeneratePreFillAsync(documentIds, userContext)
    ↓
IPlaybookOrchestrationService.ExecuteAsync(
    PlaybookRunRequest {
        PlaybookId = documentProfilePlaybookId,
        DocumentIds = [...],
        UserContext = "Extract matter pre-fill fields"
    })
    ↓
Playbook Node Graph:
  ┌───────────────┐     ┌───────────────────┐
  │ Node 1:       │     │ Node 2:           │
  │ Summarize     │     │ Classify          │
  │ Tool: Summary │     │ Tool: DocClassify │
  │ Out: "summary"│     │ Out: "class"      │
  └───────┬───────┘     └────────┬──────────┘
          │                      │
          └──────────┬───────────┘
                     ▼
          ┌──────────────────────┐
          │ Node 3:              │
          │ Extract Pre-Fill     │
          │ Tool: Generic/Custom │
          │ Skill: "Matter       │
          │   Extraction Prompt" │
          │ Out: "prefill_data"  │
          └──────────────────────┘
    ↓
Extract "prefill_data" NodeOutput → PreFillResponse
    ↓
Return to front-end
```

### Node Data Flow

Nodes chain via `OutputVariable` names stored in `ConcurrentDictionary<string, NodeOutput>`:
- Node 1 completes → stores output as `"summary"`
- Node 2 completes → stores output as `"class"`
- Node 3 starts → receives `PreviousOutputs["summary"]` and `PreviousOutputs["class"]`
- Node 3's Skill prompt can reference upstream results for richer extraction

### Extraction Schema Placement

| Where | What Goes There |
|-------|-----------------|
| **Skill** (`sprk_analysisskill`) | Extraction prompt template — field names, types, valid values, JSON output format. Swappable per entity type (Matter vs Project). |
| **Tool Configuration** (`sprk_configuration`) | Code-level validation rules — required fields, lookup entity names, output format constraints. |
| **Output** (`sprk_analysisoutput`) | Optional — Dataverse field mappings for automated record updates (future). |

---

## Authentication Pattern

### Front-End → BFF (MSAL.js + OBO)

```typescript
// PCF control acquires token via MSAL
const token = await MsalAuthProvider.getInstance().getToken([
  'api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation'
]);

// BFF calls include Bearer token
const response = await fetch(`${bffBaseUrl}/api/workspace/matters/pre-fill`, {
  method: 'POST',
  headers: { 'Authorization': `Bearer ${token}` },
  body: formData
});
```

### BFF Base URL Discovery

```typescript
// From PCF parameter sdapApiBaseUrl (same pattern as UniversalDocumentUpload)
const bffBaseUrl = context.parameters.sdapApiBaseUrl?.raw
  ?? 'https://spe-api-dev-67e2xz.azurewebsites.net';
```

Reference: `docs/architecture/sdap-auth-patterns.md`

---

## ADR Compliance

| ADR | Constraint | How We Comply |
|-----|-----------|---------------|
| ADR-001 | Minimal API, no controllers | `MapPost()` with static handler delegates |
| ADR-007 | SpeFileStore facade, no Graph SDK leakage | File uploads use existing SPE endpoints via `SdapApiClient` |
| ADR-008 | Endpoint filters for auth | `AddEndpointFilter<WorkspaceAuthorizationFilter>()` |
| ADR-010 | DI minimalism, concrete types, ≤15 registrations | No new service registrations (refactoring existing) |
| ADR-013 | AI via playbook orchestration | Refactor from direct IOpenAiClient to IPlaybookOrchestrationService |
| ADR-019 | ProblemDetails for all errors | All error paths return `Results.Problem()` |
| ADR-021 | Fluent UI v9 | All UI already uses Fluent v9 tokens |

---

## Dependencies

| Dependency | Status | Notes |
|------------|--------|-------|
| `IPlaybookOrchestrationService` | Available | Full node-based execution built |
| `SpeFileStore` | Available | Registered in Program.cs |
| `SdapApiClient` | Available | `@spaarke/sdap-client` npm package |
| `MsalAuthProvider` | Available | Used by UniversalDocumentUpload |
| `WorkspaceAuthorizationFilter` | Available | Endpoint filter for user identity |
| `GenericAnalysisHandler` | Available | Default handler for config-driven tools |
| `SummaryHandler`, `DocumentClassifierHandler` | Available | Existing playbook tool handlers |
| Document Profile Playbook | Available | ID: `18cf3cc8-02ec-f011-8406-7c1e520aa4df` |
