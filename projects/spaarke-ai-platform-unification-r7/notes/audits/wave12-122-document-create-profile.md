# Wave 12.1 Audit — Document Create Profile (Task 122)

> **Author**: Claude (R7 Wave 12.1 audit via task-execute STANDARD rigor)
> **Date**: 2026-06-30
> **Scope**: READ-ONLY. No code edits, no deploys, no Dataverse writes.
> **Purpose**: Locate document-create-profile code, reproduce failure, categorize root cause, recommend disposition for Wave 12.3.
> **Companion audit**: Wave 12.1 Task 123 (three Prefill wizards) — see §10 for shared-work assessment.

---

## 0. Executive summary

The "Document create profile" feature is the **AI Document Profile playbook (PB-002)** invoked from `DocumentUploadWizard` Step 2 (`SummaryStep`). The user uploads a file via the wizard → `useAiSummary` hook POSTs to `/api/ai/analysis/execute` with `playbookId = 18cf3cc8-02ec-f011-8406-7c1e520aa4df` → the NodeBased orchestrator runs 4 nodes (`Profile Document` → `Save Profile` + `Update Record` + `Index Document`) → an `UpdateRecord` node PATCHes `sprk_document` fields from the LLM structured output.

**The "profile form" is NOT a separate form.** It is an inline **profile-card UI** in the wizard ([`SummaryStep.tsx:264`](../../../../src/solutions/DocumentUploadWizard/src/components/SummaryStep.tsx#L264)) showing TLDR/keywords/entities/documentType as the SSE stream arrives, AND a server-side `UpdateRecord` node persisting the LLM output to fixed `sprk_document` columns. **Field binding is hardcoded** at TWO independent layers, not schema-driven.

**Primary failure: BROKEN UPDATE-RECORD NODE CONFIG** on node `Save Profile` (id `c9334fb7-a415-f111-8343-7c1e520aa4df`). Its `sprk_configjson` has executor type changed to `UpdateRecord (22)` but config body still has `DeliverOutput`-style fields (`{deliveryType: "markdown", maxOutputLength: 0, includeMetadata: true}`) — no `entityLogicalName`, no `recordId`, no `fieldMappings`. The node will fail `UpdateRecordNodeExecutor.Validate()` ([`UpdateRecordNodeExecutor.cs:141-146`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/UpdateRecordNodeExecutor.cs#L141-L146)) on every run with `"At least one field to update is required (use 'fields' or 'fieldMappings')"`.

**Disposition recommendation: CONFIGURATION DRIFT FIX (Dataverse-only)** — fix or delete the `Save Profile` node row. The properly-configured `Update Record` node (id `0fa4e8db-b216-f111-8343-7c1e520aa4df`) at execution order 3 carries the actual field-mapping config. Effort: **~30 minutes (data fix only)** vs **~2-4 hours if engine remediation is preferred** (see §9). No C# code change required for the primary fix.

**Critical preservation contract for Wave 12.3**: the Action `Document Profiler` (id `bb356968-ebe9-f011-8406-7ced8d1dc988`, code `ACT-011`)'s embedded JPS output schema (`output.fields[]`) defines 7 strict field names that drive the `UpdateRecord` field mappings. This is the **wizard-UI contract**. The maker can edit this schema in the Action row to add/remove fields and the `Update Record` node will continue to PATCH them — but the wizard inline UI ([`SummaryStep.tsx:314-330`](../../../../src/solutions/DocumentUploadWizard/src/components/SummaryStep.tsx#L314-L330)) and the hardcoded `DocumentProfileFieldMapper` ([`DocumentProfileFieldMapper.cs:24-40`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/DocumentProfileFieldMapper.cs#L24-L40)) reference fixed field names from older versions. See §4.

---

## 1. Code locations

### 1.1 UI entry point (wizard component)

| Layer | File | Lines |
|---|---|---|
| Wizard dialog | [`src/solutions/DocumentUploadWizard/src/DocumentUploadWizardDialog.tsx`](../../../../src/solutions/DocumentUploadWizard/src/DocumentUploadWizardDialog.tsx) | full file |
| Step 2 (Document Profile) | [`src/solutions/DocumentUploadWizard/src/components/SummaryStep.tsx`](../../../../src/solutions/DocumentUploadWizard/src/components/SummaryStep.tsx) | 490-682 |
| Profile-card render | [`src/solutions/DocumentUploadWizard/src/components/SummaryStep.tsx`](../../../../src/solutions/DocumentUploadWizard/src/components/SummaryStep.tsx) | 264-484 |
| Inline UI fields rendered | `doc.documentType` (line 365), `doc.tldr` (379), `doc.keywords` (394), `doc.entities` (411), `doc.summary` (432) | — |
| Upload orchestrator | [`src/solutions/DocumentUploadWizard/src/services/uploadOrchestrator.ts`](../../../../src/solutions/DocumentUploadWizard/src/services/uploadOrchestrator.ts) | 1-628 |
| **Legacy doc-profile endpoint REMOVED** | [`uploadOrchestrator.ts:436-441`](../../../../src/solutions/DocumentUploadWizard/src/services/uploadOrchestrator.ts#L436-L441) | comment notes: "The legacy `/api/ai/tools/document-profile/enqueue` endpoint no longer exists. Document Profile now runs through the AI playbook system and is triggered by the SummaryStep component (useAiSummary hook) during Step 2 rendering." |

### 1.2 Shared library hook (BFF caller)

| Layer | File | Lines |
|---|---|---|
| `useAiSummary` hook (sole consumer of doc-profile flow) | [`src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts`](../../../../src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts) | 1-648 |
| Hardcoded playbook GUID constant | [`useAiSummary.ts:34`](../../../../src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts#L34) | `'18cf3cc8-02ec-f011-8406-7c1e520aa4df'` |
| `/api/ai/playbooks/by-id/{id}` resolution | [`useAiSummary.ts:310-355`](../../../../src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts#L310-L355) | |
| `POST /api/ai/analysis/execute` (SSE) | [`useAiSummary.ts:358-377`](../../../../src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts#L358-L377) | request body: `{ documentIds: [docId], playbookId, actionId: null, additionalContext: null }` |
| SSE event handlers (`metadata` / `chunk` / `done` / `error`) | [`useAiSummary.ts:412-470`](../../../../src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts#L412-L470) | `done` event extracts `analysisId`, `partialStorage`, `storageMessage` |
| **Hardcoded field-shape mapping** | [`useAiSummary.ts:39-70`](../../../../src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts#L39-L70) | `ExtractedEntities`, `DocumentAnalysisResult` interfaces fix the shape the UI expects |

### 1.3 BFF endpoint

| Layer | File | Lines |
|---|---|---|
| Endpoint registration | [`src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs`](../../../../src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs) | 47-48 (`group.MapPost("/execute", ExecuteAnalysis)`) |
| Endpoint handler | [`AnalysisEndpoints.cs:240-360`](../../../../src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs#L240-L360) | `ExecuteAnalysis` — feature-gates on `AnalysisOptions.Enabled`, requires `request.PlaybookId.HasValue` (FR-11 binding, 400 if missing) |
| Orchestrator dispatch | [`AnalysisEndpoints.cs:300-316`](../../../../src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs#L300-L316) | calls `IPlaybookOrchestrationService.ExecuteAsync` (R7 FR-11 canonical path) |
| Event bridge | [`AnalysisEndpoints.cs:375-422`](../../../../src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs#L375-L422) | `BridgePlaybookEventToAnalysisChunk` maps `PlaybookStreamEvent` → `AnalysisStreamChunk` SSE wire shape |
| `done` chunk shape | [`AnalysisEndpoints.cs:793-802`](../../../../src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs#L793-L802) | `partialStorage` + `storageMessage` carried in `Completed` chunk |

### 1.4 Server-side orchestration

| Layer | File | Lines |
|---|---|---|
| Mode detection | [`src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs:247-266`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs#L247-L266) | `nodes.Length == 0` → Legacy; else → NodeBased. Document Profile has 4 nodes → **NodeBased mode** |
| Legacy-mode delegate | [`PlaybookOrchestrationService.cs:600-602`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs#L600-L602) | calls `AnalysisOrchestrationService.ExecutePlaybookAsync` (NOT invoked for Document Profile) |
| NodeBased executor dispatch | [`PlaybookOrchestrationService.cs`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs) | `ExecuteNodeBasedModeAsync` → per-node `INodeExecutor` resolution from `INodeExecutorRegistry` |
| AiAnalysis node executor | [`src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs) | 32-1342 |
| AiAnalysis result → NodeOutput | [`AiAnalysisNodeExecutor.cs:1316-1342`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs#L1316-L1342) | `StructuredData = toolResult.Data` (JsonElement) |
| UpdateRecord node executor | [`src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/UpdateRecordNodeExecutor.cs`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/UpdateRecordNodeExecutor.cs) | 1-405 |
| UpdateRecord config-parse (handles nested-JSON-string format) | [`UpdateRecordNodeExecutor.cs:299-323`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/UpdateRecordNodeExecutor.cs#L299-L323) | accepts both direct and nested `configJson` string |
| UpdateRecord validation | [`UpdateRecordNodeExecutor.cs:141-146`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/UpdateRecordNodeExecutor.cs#L141-L146) | requires `fields` OR `fieldMappings` non-empty |
| Template substitution | [`UpdateRecordNodeExecutor.cs:337-374`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/UpdateRecordNodeExecutor.cs#L337-L374) | binds `previousOutputs` as `{ output, text, success }` so `{{output_aiAnalysis.output.sprk_filesummary}}` resolves correctly |

### 1.5 Legacy code paths still present (DEAD for Document Profile)

These exist in the BFF but **are NOT exercised** for Document Profile because the playbook runs NodeBased mode:

| Layer | File | Lines |
|---|---|---|
| `DocumentProfileFieldMapper` (hardcoded field-name mapping) | [`src/server/api/Sprk.Bff.Api/Services/Ai/DocumentProfileFieldMapper.cs`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/DocumentProfileFieldMapper.cs) | 1-256 |
| `DocumentProfileResult` model | [`src/server/api/Sprk.Bff.Api/Models/Ai/DocumentProfileResult.cs`](../../../../src/server/api/Sprk.Bff.Api/Models/Ai/DocumentProfileResult.cs) | 1-104 |
| `AnalysisResultPersistence.StoreDocumentProfileOutputsAsync` | [`src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisResultPersistence.cs`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisResultPersistence.cs) | 109-225 |
| `AnalysisOrchestrationService.ExecutePlaybookAsync` (Legacy-mode-only invocation point) | [`src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs:1163-1182`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs#L1163-L1182) | calls `_resultPersistence.StoreDocumentProfileOutputsAsync(...)` |

**Note**: these legacy paths still run for any playbook with 0 nodes that has `playbook.Name == "Document Profile"`. The DEPLOYED Document Profile playbook has 4 nodes, so the legacy code is dead for it. The `DocumentProfileFieldMapper`'s hardcoded field-name → column mapping is therefore unused at runtime for this playbook. See §8 for implications.

---

## 2. Playbook + Action shape (Dataverse)

### 2.1 Playbook row

| Field | Value |
|---|---|
| `sprk_analysisplaybookid` | `18cf3cc8-02ec-f011-8406-7c1e520aa4df` |
| `sprk_name` | `Document Profile` |
| `sprk_playbookid` | `18cf3cc8-02ec-f011-8406-7c1e520aa4df` (mirror) |

**Note**: `sprk_consumertype` column **does not exist** on this entity in spaarkedev1. Consumer routing is therefore not used for this playbook — the UI directly references the playbook GUID via `by-id` lookup.

### 2.2 Nodes (ordered by `sprk_executionorder`)

| # | Node ID | `sprk_name` | `sprk_executortype` | `sprk_actionid` | `sprk_outputvariable` | `sprk_dependsonjson` | Status |
|---|---|---|---|---|---|---|---|
| 1 | `ca334fb7-a415-f111-8343-7c1e520aa4df` | Profile Document | 0 (AiAnalysis) | `bb356968-ebe9-f011-8406-7ced8d1dc988` (ACT-011 Document Profiler) | `output_aiAnalysis` | null (Start) | OK |
| 2 | `c9334fb7-a415-f111-8343-7c1e520aa4df` | **Save Profile** | 22 (UpdateRecord) | null | `result` | depends on node 1 | **BROKEN — see §3** |
| 3 | `0fa4e8db-b216-f111-8343-7c1e520aa4df` | Update Record | 22 (UpdateRecord) | null | `output_updateRecord` | depends on node 1 | OK (correctly configured) |
| 4 | `4ce880b6-e11e-f111-88b3-7ced8d1dc988` | Index Document | 41 (DeliverToIndex) | null | `index_result` | depends on node 1 | OK |

**ExecutorType enum reference**: [`INodeExecutor.cs:128-180`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs#L128-L180).

### 2.3 Action `Document Profiler` (ACT-011)

| Field | Value |
|---|---|
| `sprk_analysisactionid` | `bb356968-ebe9-f011-8406-7ced8d1dc988` |
| `sprk_name` | `Document Profiler` |
| `sprk_actioncode` | `ACT-011` |
| `sprk_outputschemajson` | null/empty — the schema lives **inside** `sprk_systemprompt` as JPS `output.fields[]` |
| `sprk_outputformat` | not set in summary view (canonical JSON-format presumed from JPS) |

### 2.4 Action JPS output schema (the wizard-UI contract — BINDING for Wave 12.3)

From `sprk_systemprompt` JPS `output.fields[]`:

| # | Field name | Type | Constraint | Description |
|---|---|---|---|---|
| 1 | `sprk_filesummary` | string | maxLength: 5000 | 2-4 paragraph comprehensive summary |
| 2 | `sprk_filetldr` | string | maxLength: 500 | 1-2 sentence ultra-concise summary |
| 3 | `sprk_extractorganization` | string | maxLength: 2000 | comma-separated org names |
| 4 | `sprk_extractpeople` | string | maxLength: 2000 | comma-separated person names |
| 5 | `sprk_filetype` | string | maxLength: 200 | document format description |
| 6 | `sprk_filekeywords` | string | maxLength: 500 | comma-separated 5-15 keywords |
| 7 | `sprk_documenttype` | string | enum: `["contract", "invoice", "proposal", "report", "letter", "memo", "email", "agreement", "statement", "patent", "trademark", "nda", "other"]` | classification |

JPS structural property `output.structuredOutput: true` enables OpenAI constrained decoding.

**This 7-field set is the contract** that:
- The LLM must emit (enforced via constrained decoding)
- The `Update Record` node configJson references via `{{output_aiAnalysis.output.<fieldName>}}` template variables
- The wizard inline UI ([`SummaryStep.tsx`](../../../../src/solutions/DocumentUploadWizard/src/components/SummaryStep.tsx)) consumes via `documentType` / `tldr` / `keywords` / etc.

---

## 3. Configuration drift — the actual broken node

### 3.1 Node 2 "Save Profile" (BROKEN)

Raw `sprk_configjson`:

```json
{
  "__canvasNodeId": "node_1772236478565_0h13rtirj",
  "__actionType": 40,
  "deliveryType": "markdown",
  "maxOutputLength": 0,
  "includeMetadata": true
}
```

Failure analysis:

- `__actionType: 40` is `DeliverOutput` (old name)
- `sprk_executortype = 22` (UpdateRecord) — drifted from `__actionType`
- The body has `deliveryType` / `maxOutputLength` / `includeMetadata` — these are `DeliverOutput` config fields, NOT `UpdateRecord` fields
- `entityLogicalName`, `recordId`, and `fieldMappings`/`fields` are all missing
- `UpdateRecordNodeExecutor.Validate()` ([file:line](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/UpdateRecordNodeExecutor.cs#L141-L146)) returns three validation errors:
  - `"Entity logical name is required"`
  - `"Record ID is required"`
  - `"At least one field to update is required (use 'fields' or 'fieldMappings')"`

The orchestrator treats node failure as terminal for the run path (per `PlaybookOrchestrationService` semantics) — this node failing causes the run to enter the `partialStorage: true` / `CompletedWithWarnings` branch ([`AnalysisOrchestrationService.cs:717-721`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs#L717-L721) on the legacy path; on the NodeBased path the equivalent surfaces as a `RunFailed` or `NodeFailed` event from `PlaybookOrchestrationService`).

**Hypothesis**: someone modified `sprk_executortype` in the Playbook Builder UI from `DeliverOutput (40)` to `UpdateRecord (22)` without re-populating `sprk_configjson`. The Builder either does not validate type-vs-config alignment on save, or this was a direct Dataverse data edit bypassing the Builder.

### 3.2 Node 3 "Update Record" (CORRECTLY configured)

Raw `sprk_configjson` (decoded; nested `configJson` string contains the real config):

```json
{
  "__canvasNodeId": "node_1772509260018_8qwhl3xe0",
  "__actionType": 22,
  "isConfigured": false,
  "validationErrors": [],
  "configJson": "<JSON string>"
}
```

Decoded nested `configJson`:

```json
{
  "entityLogicalName": "sprk_document",
  "recordId": "{{document.id}}",
  "fieldMappings": [
    { "field": "sprk_filesummary", "type": "string", "value": "{{output_aiAnalysis.output.sprk_filesummary}}" },
    { "field": "sprk_filetldr", "type": "string", "value": "{{output_aiAnalysis.output.sprk_filetldr}}" },
    { "field": "sprk_filekeywords", "type": "string", "value": "{{output_aiAnalysis.output.sprk_filekeywords}}" },
    { "field": "sprk_extractorganization", "type": "string", "value": "{{output_aiAnalysis.output.sprk_extractorganization}}" },
    { "field": "sprk_extractpeople", "type": "string", "value": "{{output_aiAnalysis.output.sprk_extractpeople}}" },
    { "field": "sprk_filesummarystatus", "type": "choice", "value": "{{output_aiAnalysis.output.sprk_filesummarystatus}}",
      "options": { "Completed": 100000002 } },
    { "field": "sprk_classification", "type": "choice", "value": "{{output_aiAnalysis.output.sprk_classification}}",
      "options": { "InvoiceCandidate": 100000000, "NotInvoice": 100000001, "Unknown": 100000002 } }
  ]
}
```

Validation notes on this node:
- 7 field mappings ✓
- Outer `isConfigured: false` is misleading — `UpdateRecordNodeExecutor.ParseConfig` ([`UpdateRecordNodeExecutor.cs:299-323`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/UpdateRecordNodeExecutor.cs#L299-L323)) reads the nested `configJson` string and ignores `isConfigured`
- **Field-name mismatches vs JPS schema (§2.4)**:
  - `sprk_filesummarystatus` is referenced but is NOT in the Action's JPS `output.fields[]` — the LLM will NOT emit this. Template renders to `""` → choice coercion to int will fail or write 0. (Likely silent.)
  - `sprk_classification` is referenced but is NOT in the Action's JPS `output.fields[]` — same problem.
  - `sprk_filetype` IS in the JPS but NOT in `fieldMappings` — that field is collected by the LLM but not persisted.
  - `sprk_documenttype` IS in the JPS but NOT in `fieldMappings` — that field is collected by the LLM but not persisted.

So even node 3 has drift between Action JPS and node config. Persists 5 fields correctly (summary, tldr, keywords, organization, people); silently drops 2 mapped fields (`sprk_filesummarystatus`, `sprk_classification`) because they're not in the JPS; silently drops 2 emitted-but-not-mapped fields (`sprk_filetype`, `sprk_documenttype`).

### 3.3 Inline wizard UI consumption mismatch ([`SummaryStep.tsx:314-330`](../../../../src/solutions/DocumentUploadWizard/src/components/SummaryStep.tsx#L314-L330))

The wizard inline UI consumes via `useAiSummary` `DocumentAnalysisResult` shape ([`useAiSummary.ts:39-70`](../../../../src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts#L39-L70)):

```typescript
interface ExtractedEntities {
  organizations: string[];
  people: string[];
  amounts: string[];      // NOT in JPS schema
  dates: string[];        // NOT in JPS schema
  documentType: string;
  references: string[];   // NOT in JPS schema
}

interface DocumentAnalysisResult {
  summary: string;        // maps to sprk_filesummary
  tldr: string[];         // expects ARRAY; JPS emits STRING
  keywords: string;
  entities: ExtractedEntities;
}
```

**Wizard UI expects an OLDER schema** that has `entities.amounts`, `entities.dates`, `entities.references`, and `tldr` as a string ARRAY. The current Action JPS schema emits `sprk_extractorganization`/`sprk_extractpeople` as strings (no amounts/dates/references) and `sprk_filetldr` as a 1-2 sentence string. The wizard inline UI will receive `undefined` for several fields and render an empty card body in those sections.

Further, the SSE stream the wizard parses ([`useAiSummary.ts:412-470`](../../../../src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts#L412-L470)) consumes only `chunk` (accumulating raw text) and `done` events. The `done` event carries `analysisId` + `partialStorage` + `storageMessage`, NOT the structured fields. The wizard's structured fields (`tldr`, `keywords`, `entities`, `documentType`) come from a path NOT visible in this code — likely the wizard expects a different SSE event type (possibly emitted by older BFF code) that no longer fires. The wizard inline UI is therefore likely showing **only the accumulated raw streaming text** ([`SummaryStep.tsx:357-362`](../../../../src/solutions/DocumentUploadWizard/src/components/SummaryStep.tsx#L357-L362)) without the structured field display.

This is a **secondary defect** independent of the `Save Profile` node failure. It does not block the server-side PATCH from happening (node 3 still runs and writes 5 fields), but it makes the wizard inline UI display look broken even when persistence succeeds.

---

## 4. Data-flow trace (UI → BFF → Dataverse PATCH)

```
1. User picks files in DocumentUploadWizard Step 1 (Upload + Create Records)
2. Phase 1 uploads to SPE; Phase 2 creates sprk_document records (uploadOrchestrator.ts:248-337)
3. Phase 3 (Document Profile) is NO LONGER kicked off here (uploadOrchestrator.ts:436-441 — removed)
4. User clicks Next → SummaryStep mounts (SummaryStep.tsx:611-633)
5. SummaryStep calls useAiSummary.addDocuments → kicks streamDocument per doc
6. streamDocument:
   a. GET /api/ai/playbooks/by-id/18cf3cc8-02ec-f011-8406-7c1e520aa4df
   b. POST /api/ai/analysis/execute (SSE) body={documentIds:[id], playbookId, actionId:null}
7. BFF AnalysisEndpoints.ExecuteAnalysis (FR-11 gate: PlaybookId required → 400 if missing)
8. Forwards to PlaybookOrchestrationService.ExecuteAsync
9. Loads 4 nodes for playbookId → nodes.Length > 0 → NodeBased mode
10. Topological order from sprk_executionorder + sprk_dependsonjson:
    Profile Document (1) → [Save Profile (2), Update Record (3), Index Document (4)] (parallel)
11. Profile Document (AiAnalysis):
    - Action ACT-011 loaded
    - JPS prompt + schema rendered → IOpenAiClient.GetStructuredCompletionRawAsync
    - Returns 7-field JSON → stored as NodeOutput { StructuredData = <json>, TextContent = <raw> }
    - Output bound to scope "output_aiAnalysis"
12. Batch parallel execution of {Save Profile, Update Record, Index Document}:
    a. Save Profile (UpdateRecord executor) → Validate FAILS:
       "Entity logical name is required; Record ID is required; At least one field..."
       Returns NodeOutput.Error → NodeFailed event
    b. Update Record (UpdateRecord executor) → Validate OK → reads nested configJson →
       BuildTemplateContext → renders 7 field mapping values from {{output_aiAnalysis.output.*}} →
       PATCHes sprk_document(id) with 5 fields populated (5 in JPS) + 2 empty (not in JPS)
    c. Index Document (DeliverToIndex executor) → enqueues RAG indexing job
13. PlaybookOrchestrationService either:
    - Emits NodeFailed for Save Profile → RunFailed → SSE "error" chunk → wizard shows error badge
    - OR emits partial-success completion (depends on whether NodeFailed aborts the run)
14. BridgePlaybookEventToAnalysisChunk maps to SSE wire format
15. Wizard receives "error" chunk → updateDocument(status: "error") → red error badge in UI
    OR receives "done" chunk with partialStorage → updateDocument(partialStorage, storageMessage)
```

**Predicted failure point**: step 12a. The `Save Profile` node fails validation. Depending on `PlaybookOrchestrationService` failure semantics (immediate-abort vs continue-on-error), either:
- Whole run fails → wizard shows error badge → user sees red `Error` badge in Step 2
- Run completes with warning → wizard shows yellow `partialStorage` message but document IS profiled

Need empirical reproduction to determine which path; either way the user-visible state is "broken-looking" even though the Update Record node at step 12b succeeds and persists fields.

---

## 5. Reproduction status

**Not reproduced directly** in this audit (no live wizard run performed — read-only constraint).

Evidence base:
- Dataverse query results showing the broken `Save Profile` node config (§3.1)
- Static read of `UpdateRecordNodeExecutor.Validate` showing validation will fail
- Sibling Wave 11 T116 systematic assessment ([`notes/handoffs/wave11-t116-narrate-systematic-assessment.md`](../handoffs/wave11-t116-narrate-systematic-assessment.md)) confirms aggregation behavior on the orchestrator/aggregator path
- Operator confirmation: feature was previously working, now broken

To reproduce empirically (operator action):
1. Open `DocumentUploadWizard` from a Matter or Project record
2. Upload any file in Step 1; advance to Step 2
3. Watch Step 2 SummaryStep: observe whether the profile card shows status `error` (red badge) or `partialStorage` (yellow message); inspect browser console for SSE event types
4. Query Dataverse for the just-uploaded `sprk_document` row: check whether `sprk_filesummary`, `sprk_filetldr`, `sprk_filekeywords`, `sprk_extractorganization`, `sprk_extractpeople` were PATCHed (Update Record node 3 succeeds even if Save Profile fails)

---

## 6. Root-cause categorization

**Primary: CONFIGURATION DRIFT** (Dataverse data state) — the `Save Profile` node has mismatched `sprk_executortype` (22) and `sprk_configjson` (DeliverOutput-shape). Almost certainly a data-edit bypass of the Playbook Builder.

**Secondary: ACTION JPS ↔ NODE CONFIG DRIFT** — node 3 `Update Record` references 2 JPS fields not in the schema (`sprk_filesummarystatus`, `sprk_classification`) and omits 2 fields that ARE in the schema (`sprk_filetype`, `sprk_documenttype`). Not a "blocker" — node 3 still runs and writes 5 fields — but reflects schema drift that lost some functionality.

**Tertiary: WIZARD-UI ↔ ACTION JPS DRIFT** — the wizard inline UI consumes an older schema shape (`entities.amounts/dates/references`, `tldr: string[]`) that the Action JPS no longer emits. The wizard's inline display will look incomplete even after the backend PATCH succeeds.

**NOT R7 regression**: enum rename (ActionType→ExecutorType, Wave 2 task 022) and dispatch reform (single-hop dispatch, Wave 3) do NOT depend on or affect this playbook. The `sprk_executortype` int values match the deployed enum (0, 22, 41); the dispatch reads `sprk_executortype` directly per FR-09.

**NOT inherent engine bug class** (compared to /narrate W11 T116):
- /narrate W11 failures (P1 aggregator drops ReturnResponse, P2 LoadKnowledge type mismatch, P3 EntityNameValidator allowList) are all in `IInvokePlaybookAi` and node executors used by `/narrate`. Document Profile uses different executors (AiAnalysis, UpdateRecord, DeliverToIndex) and goes through the SSE-based `/api/ai/analysis/execute` path (not `IInvokePlaybookAi.InvokePlaybookAsync`). None of P1/P2/P3 apply here.
- The orchestrator engine itself is structurally correct for this flow.

---

## 7. Disposition recommendation

### 7.1 PRIMARY: Configuration fix (recommended) — ~30 minutes

**Approach**: Fix the data, not the code. Edit the `Save Profile` node row in Dataverse.

Two options:

| Option | Action | Effort |
|---|---|---|
| **A. Delete node 2** | Delete `c9334fb7-a415-f111-8343-7c1e520aa4df` (Save Profile) — the redundant `Update Record` node (id `0fa4e8db-...`) at execution order 3 already does the PATCH. | 5 min |
| **B. Fix node 2 config** | Update `sprk_configjson` on node 2 to a proper `UpdateRecord` config (mirror node 3's structure) — useful if `Save Profile` is meant to be doing something distinct from `Update Record` (the names suggest both are doing the same thing). | 15-30 min |

**Recommendation**: Option A (delete node 2). Two `UpdateRecord` nodes writing to the same `sprk_document` row is redundant; consolidating to one node is simpler. Operator can confirm via Playbook Builder UI that they want to keep node 3 only.

**Also recommended in same data-fix cycle**:
1. Align node 3 `fieldMappings` with Action JPS `output.fields[]`:
   - Add `sprk_filetype` mapping
   - Add `sprk_documenttype` mapping
   - Remove `sprk_filesummarystatus` mapping (or add it to Action JPS if needed)
   - Remove `sprk_classification` mapping (or add it to Action JPS if needed)
2. Operator decision: should the Action JPS emit `sprk_filesummarystatus` and `sprk_classification` (then add to JPS), or are these obsolete fields to drop?

### 7.2 SECONDARY: Wizard UI fix — ~2-4 hours (separate task)

Update `useAiSummary.ts` `DocumentAnalysisResult` interface and `SummaryStep.tsx` rendering to match the current Action JPS schema:
- `entities` → just `{organizations, people, documentType}` (drop amounts/dates/references)
- `tldr` → `string` not `string[]` (or display string as a single bullet)
- Wire SSE to parse the actual structured output event(s) emitted by the BFF (need to trace which SSE events carry the per-field updates — likely the issue is that the BFF emits `chunk` only and the wizard's structured-field renders never fire)

This is a separate concern from the primary fix; the backend PATCH will work without this. Wizard inline UI cosmetic issue.

### 7.3 NOT recommended for Wave 12.3: code-defined narrator pattern

The Wave 11 POC's code-defined narrator pattern (per [`notes/spikes/poc-vs-playbook-engine-architecture.md`](../spikes/poc-vs-playbook-engine-architecture.md)) is NOT a good fit for this feature because:
- The Document Profile playbook uses simple `AiAnalysis` (single LLM call) + `UpdateRecord` (deterministic PATCH) — not the complex fan-out / multi-call composition that the narrator pattern improves.
- The Action's JPS is already the wizard-UI contract. Operator can edit JPS in Action row → emit new fields → update node 3 fieldMappings to PATCH them. This is the operator-tunable surface the operator values.
- A code-defined wrapper would either need its own field list (defeating the JPS contract) or read JPS dynamically (which IS what the engine already does).

For this feature, the engine is doing what it should. The bug is data, not code.

---

## 8. Estimated total effort

| Path | Effort | Risk |
|---|---|---|
| **Primary data fix only** (delete or repair node 2) | 30 min | LOW |
| **Primary + Action JPS / node 3 alignment** (add/remove fieldMappings to match JPS) | 1-2 hours | LOW |
| **Primary + secondary (wizard UI cosmetics fix)** | 2-4 hours total | MEDIUM (involves frontend deploy) |
| **Engine remediation (code-defined wrapper)** | 4-8 hours — NOT RECOMMENDED | HIGH (over-engineering, loses operator-tunable JPS) |

**Recommended Wave 12.3 scope**: Primary data fix (option A) + Action JPS / node 3 field alignment + wizard UI cosmetic fix. Total ~2-4 hours of focused work. Operator-verified post-deploy with a sample upload.

---

## 9. Wizard-UI contract — BINDING preservation for Wave 12.3

**The Action JPS `output.fields[]` IS the wizard-UI contract.** Wave 12.3 work MUST preserve:

1. Action row id `bb356968-ebe9-f011-8406-7ced8d1dc988` (ACT-011 Document Profiler)
2. The `sprk_systemprompt` JPS structure (instruction + input + output.fields + output.structuredOutput)
3. The 7-field schema names exactly as defined (operator may add/remove fields — both backend Update Record node config and wizard UI must follow)
4. The hardcoded `useAiSummary` playbook GUID constant pattern ([`useAiSummary.ts:34`](../../../../src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts#L34)) — by-id resolution is the canonical pattern per FR-03 Pattern B (task 021)

**Wave 12.3 MUST NOT**:
- Inline the Action prompt into C# (loses operator tunability)
- Hardcode a fixed field list in BFF code that bypasses the Action JPS (loses operator tunability — though `DocumentProfileFieldMapper` already does this for the legacy Legacy-mode path, that path is dead for this playbook)
- Add new abstractions per CLAUDE.md §11 (no 2nd consumer demonstrated)
- Switch the playbook to legacy mode (which would activate `DocumentProfileFieldMapper`'s hardcoded mapping)

---

## 10. Shared work assessment vs Task 123 (three Prefill wizards)

The three Prefill wizards (Create Matter, Create Project, Create Work Assignment) in task 123 share a similar **Action-output-schema-as-wizard-UI-contract pattern**. Specifically, both this audit (122) and task 123 likely will find:
- A playbook → AiAnalysis node → structured output schema → UI form binding
- Question of whether UI binding is dynamic-schema-driven OR hardcoded mapping

**Document Profile (this audit)** uses **hardcoded mapping** at THREE layers:
1. UI: `useAiSummary.ts` interfaces hardcode field names like `summary`, `tldr`, `keywords`, `entities`, `documentType`
2. Inline UI render: `SummaryStep.tsx` references those specific field names
3. Server PATCH: `UpdateRecord` node configJson hardcodes `sprk_document` field names + template paths

**Verdict on shared-work potential**: PARTIAL. The architectural pattern is similar (Action JPS as contract), but the implementations differ:
- Document Profile: persists via `UpdateRecord` node (PATCH existing doc record). No form-prefill — the wizard's "form" is the inline profile card, not a creatable form.
- Three Prefill wizards: persist by prefilling a CREATE-form's fields then user submits. Likely uses `WorkspacePrefillAi.ExecutePlaybookAsync` ([`WorkspacePrefillAi.cs`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/WorkspacePrefillAi.cs), [`MatterPreFillService.cs:403`](../../../../src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs#L403), [`ProjectPreFillService.cs:367`](../../../../src/server/api/Sprk.Bff.Api/Services/Workspace/ProjectPreFillService.cs#L367)), NOT the SSE-based `/api/ai/analysis/execute` path Document Profile uses.

Wave 12.3 implementation work likely splits cleanly:
- **122 (this)**: data fix on PB-002 nodes + minor wizard inline UI cleanup
- **123 (three Prefill wizards)**: separate audit will likely find different bug — possibly in `WorkspacePrefillAi` or the form-prefill UI pattern

Recommend Wave 12.3 generates **2 independent task POMLs** (one for 122, one for 123) rather than a combined task. The wizards share an architectural pattern but not the same bug nor the same code path.

---

## 11. Open questions for operator

1. **Save Profile vs Update Record naming**: Are these meant to be two distinct nodes (e.g., Save Profile updates a status field; Update Record persists content), or is one a stale duplicate? Recommend operator review the playbook in Playbook Builder UI and decide.
2. **JPS field set**: Should the Action JPS schema add `sprk_filesummarystatus` and `sprk_classification` (currently in node 3 fieldMappings but missing from JPS), or should node 3 drop those mappings? What is the use case for those fields?
3. **JPS missing-from-mappings fields**: Should node 3 add `sprk_filetype` and `sprk_documenttype` mappings (currently in JPS but not persisted)? Or are those fields not meant to be stored at the document record level?
4. **Wizard UI schema mismatch**: Is the wizard inline UI's `entities.amounts/dates/references` and `tldr: string[]` expectation important (extend the Action JPS to emit them), or should the wizard UI be simplified to match the current JPS (drop those sections)?
5. **TypeScript / wizard deploy gating**: Any change to `useAiSummary.ts` propagates to all consumers (DocumentUploadWizard and DocumentEmailWizard). Wave 12.3 task should verify DocumentEmailWizard impact before changing the shared hook.

---

## 12. References

- Wave 12 plan: [`notes/wave12-mvp-completion-plan.md`](../wave12-mvp-completion-plan.md)
- Wave 11 T116 systematic assessment (sibling failure modes): [`notes/handoffs/wave11-t116-narrate-systematic-assessment.md`](../handoffs/wave11-t116-narrate-systematic-assessment.md)
- POC vs Engine architecture: [`notes/spikes/poc-vs-playbook-engine-architecture.md`](../spikes/poc-vs-playbook-engine-architecture.md)
- BFF extensions constraint: [`.claude/constraints/bff-extensions.md`](../../../../.claude/constraints/bff-extensions.md)
- Companion audit (parallel): `notes/audits/wave12-123-three-prefill-wizards.md` (forthcoming)

---

*End of audit. Disposition: configuration data fix (Dataverse) primary; minor JPS+node alignment secondary; wizard UI cosmetic fix tertiary. No engine code change required. ~2-4 hours total Wave 12.3 effort.*
