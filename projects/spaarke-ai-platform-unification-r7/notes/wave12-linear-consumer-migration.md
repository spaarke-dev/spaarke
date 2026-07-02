# R7 Wave 12 — Linear AI Consumer Migration — Work Spec

> **Created**: 2026-07-02
> **Status**: Approved by operator (2026-07-02 chat)
> **Architecture doc**: [`docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md`](../../../docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md)
> **Task plan**: [`wave12-linear-consumer-tasks.md`](wave12-linear-consumer-tasks.md)

## Goal

Migrate six linear AI consumers off the Playbook Engine (`PlaybookOrchestrationService`) onto the new Linear AI Consumer library. Doc Upload is tonight's target; the other five are follow-on tasks completed this week.

Post-migration: linear consumers are simple typed C# service classes composed of four shared primitives. No playbook orchestration, no template engine, no dispatch registry involved in their execution path.

Playbook Engine consumers (Chat, Insight Engine) are unaffected.

## In-scope consumers (this migration)

1. **Document Upload / Profile Document** — `POST /api/ai/analysis/execute` (when playbookId = Document Profile). Tonight.
2. **File Summarize** — `POST /api/workspace/files/summarize` (SSE). Follow-on.
3. **Matter Prefill** — `POST /api/workspace/matters/pre-fill`. Follow-on.
4. **Project Prefill** — `POST /api/workspace/projects/pre-fill`. Follow-on.
5. **Work Assignment Prefill** — currently aliased to Matter Prefill endpoint. Follow-on.
6. **Document Create Profile** — endpoint TBD during migration. Follow-on.

## Out of scope

- Chat (`/api/ai/chat/*`) — stays on Playbook Engine.
- Insight Engine — stays on Playbook Engine.
- Daily Briefing narration — already Linear-shaped (Wave 11). Formal refactor to share primitives is deferred to a follow-on cleanup pass; it MUST NOT break during this work.
- The Playbook Engine itself (`PlaybookOrchestrationService`, `NodeExecutorRegistry`, `PlaybookTemplateContextBuilder`) — unchanged for its remaining consumers.
- The `sprk_analysisaction` schema and `sprk_playbookconsumer` schema — unchanged. Both paths depend on these.
- Playbook-to-code compilation (for the remaining engine consumers) — deferred to R7 Wave 12 Summarize Assistant UAT or later.

## Success criteria

- All six migrated consumers pass their existing operator UAT.
- Chat continues to work.
- Insight Engine continues to work.
- Daily Briefing continues to work.
- Playbook rows for migrated consumers are deactivated in Dataverse (audit-preserving; not deleted).
- All BFF tests pass.
- BFF publish size stays under NFR-01 ceiling.

## Reversion / preservation of tonight's patches

Before the migration begins, the following commits from tonight's Doc Upload debugging cycle must be handled:

**Revert (were Playbook-Engine bandaids)**:

- `4facf26ef` — `DataverseWebApiService.GetEntitySetNameAsync` metadata-accessor form (broken, superseded)
- `2021028da` — `$filter` form of metadata query (also broken)
- `1909b4432` — heuristic pluralization (workaround for broken metadata endpoint)
- `15511117b` — Layer 1 nested-JSON skip (patch for template-shape mismatch that Linear path eliminates)

**Keep (correct regardless of path)**:

- `17f432b13` — `PlaybookLookupService.GetByIdAsync` dual-path (still used by remaining Playbook consumers)
- `d75de048b` — Endpoint pre-loads `DocumentContext` (correct new-architecture endpoint contract; Linear path also benefits — same document-loading responsibility)
- `a4cf7560d` — Populate `DocumentContext.Metadata` with GraphDriveId / GraphItemId (still needed for indexing)
- `3eb0aacbb` — Expose `{{document.*}}` at Layer 1 (used by remaining Playbook consumers)
- `0a8d200ba` — PATCH payload diagnostic (harmless, still useful for engine consumers)

**Data-side patches on Dataverse rows that stay (not code)**:

- `sprk_documenttype` dropped from Update Record fieldMappings on the Doc Profile playbook — reversible; playbook rows will be retired anyway
- Project prefill playbook Start node + modelDeploymentId added — reversible; will be retired when Project prefill migrates

## Reference information — per consumer

### 1. Document Upload / Profile Document (tonight's target)

**Current wire**:
- Client hook: [`src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts`](../../../src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts)
- Endpoint: [`AnalysisEndpoints.ExecuteAnalysis`](../../../src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs) — `POST /api/ai/analysis/execute`
- Currently invokes `IPlaybookOrchestrationService.ExecuteAsync(playbookRequest, http, ct)`

**Target service**: `DocumentProfileService` (new). Composes four shared primitives + `IDocumentDataverseService` + `IJobEnqueueService`.

**Action row (KEEP)**: `bb356968-ebe9-f011-8406-7ced8d1dc988` (`sprk_analysisaction` "Document Profiler") — JPS prompt + output schema.

**Consumer routing (KEEP)**: `sprk_playbookconsumer` row for `document-profile` consumer type. Verify present; add if missing.

**Playbook rows (RETIRE after cutover)**:
- `sprk_analysisplaybook` `18cf3cc8-02ec-f011-8406-7c1e520aa4df` ("Document Profile")
- `sprk_playbooknode` `ca334fb7-a415-f111-8343-7c1e520aa4df` (Profile Document)
- `sprk_playbooknode` `0fa4e8db-b216-f111-8343-7c1e520aa4df` (Update Record)
- `sprk_playbooknode` `4ce880b6-e11e-f111-88b3-7ced8d1dc988` (Index Document)

**Endpoint routing decision**: `POST /api/ai/analysis/execute` receives a `playbookId`. To preserve the client contract during migration, the endpoint dispatches by playbookId — if playbookId matches Document Profile, route to `DocumentProfileService`; otherwise fall through to the existing playbook engine path. After the follow-on migrations complete, the endpoint can be simplified.

**AI output shape** (from Action's `sprk_outputschemajson` if present, otherwise inferred from the current playbook's field mappings):
```
{
  sprk_filesummary: string,
  sprk_filetldr: string,
  sprk_filekeywords: string,
  sprk_extractorganization: string,
  sprk_extractpeople: string,
  sprk_filetype: string,
  sprk_documenttype: string  // Choice option label — was dropped tonight; restore
}
```

**Typed field mapping**: `DocumentProfileFieldMap.FromAiOutput(JsonElement)` deserializes to a typed intermediate, then to a Dataverse-ready object. For `sprk_documenttype`: string label → Choice option value via a hardcoded label→int table (source of truth is the Dataverse metadata; hardcoding here is acceptable for the migration and can be replaced by a metadata-cache lookup later).

**Persistence**: `IDocumentDataverseService.UpdateProfileAsync(documentId, fields, ct)` — a typed method (may need to be added if not already present). Uses the existing SDK-based `DataverseServiceClientImpl` — no PATCH construction, no metadata resolution.

**Follow-on effect**: `IJobEnqueueService.EnqueueRagIndexingAsync(documentId, driveId, itemId, tenantId, ct)` — the RAG indexing job enqueue that Deliver To Index was doing.

### 2. File Summarize

**Current wire**:
- Endpoint: [`WorkspaceFileEndpoints.SummarizeFilesAsync`](../../../src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceFileEndpoints.cs) — `POST /api/workspace/files/summarize` (SSE)
- Internal helper `RunSummarizePlaybookAsSSEAsync` invokes the playbook engine

**Target service**: `FileSummarizeService` — resolves Summarize Action, extracts text from all uploaded files, calls LLM, emits SSE progress + final result.

**Action row (KEEP)**: resolved via `ConsumerTypes.SummarizeFile` routing; fallback env var `Workspace__SummarizePlaybookId`.

**Playbook rows (RETIRE)**: `sprk_analysisplaybook` `4a72f99c-a119-f111-8343-7ced8d1dc988` ("Summarize File") + its nodes.

**Output**: returned to client as SSE `result` chunk; no Dataverse writes; no downstream jobs.

### 3. Matter Prefill

**Current wire**:
- Client hook: `useAiPrefill` (in `Spaarke.UI.Components`)
- Endpoint: [`WorkspaceMatterEndpoints`](../../../src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceMatterEndpoints.cs) — `POST /api/workspace/matters/pre-fill`
- Service: `MatterPreFillService` (already exists — wraps a playbook call internally)

**Target**: Modify existing `MatterPreFillService` — replace the internal `ExtractFieldsViaPlaybookAsync` playbook call with `IActionRunner.RunAsync`. Return path unchanged (typed `AiPreFillResult` deserialized from AI JSON output).

**Action row (KEEP)**: `89cc641a-df18-f111-8343-7c1e520aa4df` (ACT-023 "New Matter Field Extraction").

**Playbook rows (RETIRE)**: `sprk_analysisplaybook` `2d660cad-d418-f111-8343-7ced8d1dc988` + its nodes.

**Output**: returned to client as JSON `ProjectPreFillResponse` DTO (mis-named historical — actually `MatterPreFillResponse`).

### 4. Project Prefill

**Current wire**:
- Endpoint: [`WorkspaceProjectEndpoints`](../../../src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceProjectEndpoints.cs) — `POST /api/workspace/projects/pre-fill`
- Service: `ProjectPreFillService` (already exists — wraps playbook call)

**Target**: Same treatment as Matter Prefill.

**Action row (KEEP)**: `1e838114-7919-f111-8343-7ced8d1dc988` (ACT-024 "New Project Field Extraction").

**Playbook rows (RETIRE)**: `sprk_analysisplaybook` `fc343e9c-3460-f111-ab0b-7c1e521b425f` + its nodes (the Start node + modelDeploymentId I added tonight will be retired with these).

### 5. Work Assignment Prefill

**Current wire**: Client-side `EnterInfoStep.tsx` in `CreateWorkAssignmentWizard` hard-codes `endpoint: '/api/workspace/matters/pre-fill'`. Reuses Matter's endpoint.

**Target**: Either (a) leave the aliasing in place and Matter service serves both (simpler); or (b) extract `WorkAssignmentPreFillService` with a WA-specific Action and endpoint (cleaner, longer). Decide during Matter conversion.

### 6. Document Create Profile

**Current wire**: Endpoint currently unclear from tonight's session — need to trace from the wizard flow.

**Target**: New `DocumentCreateProfileService`. Determine the entry endpoint during the Matter/Project prefill migration; likely a similar shape.

## Existing components — what to reuse

- `IOpenAiClient.GetStructuredCompletionRawAsync` — LLM call, returns raw `JsonElement`. Used by `IActionRunner`.
- `IConsumerRoutingService` — consumer-type routing. Used by `IActionResolver`.
- `IScopeResolverService.GetActionAsync(actionId, ct)` — Action lookup. Used by `IActionResolver`.
- `AnalysisDocumentLoader` — SPE document + text extraction. Used by `IDocumentTextSource`.
- `ITextExtractor` — file text extraction from `IFormFile`. Used by `IDocumentTextSource`.
- `IDocumentDataverseService` — typed document reads/writes. Used by `DocumentProfileService`.
- `IJobEnqueueService` — Service Bus job enqueue. Used by `DocumentProfileService`.
- `IServiceClient` (via `DataverseServiceClientImpl`) — for any typed Dataverse operations without metadata calls.

## New components to build

- Namespace: `Sprk.Bff.Api.Services.Ai.LinearConsumers`
- Folder: `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/`

Files:

1. `IActionResolver.cs` + `ActionResolver.cs` — Singleton, wraps `IConsumerRoutingService` + `IScopeResolverService`.
2. `IDocumentTextSource.cs` + `DocumentTextSource.cs` — Scoped (uses HttpContext), wraps `AnalysisDocumentLoader` + `ITextExtractor`.
3. `IActionRunner.cs` + `ActionRunner.cs` — Singleton, wraps `IOpenAiClient`. Handles JPS input binding + structured-output parsing.
4. `LinearRunContext.cs` — record type.
5. `DocumentText.cs` — record type.
6. `LinearConsumersModule.cs` — DI registration.

Consumer services:

7. `DocumentProfileService.cs` — new (tonight).
8. `FileSummarizeService.cs` — follow-on.
9. Consumer-specific DTOs (`DocumentProfileResult`, `DocumentProfileFieldMap`) — per consumer.

Modified files:

10. `AnalysisEndpoints.cs` — endpoint dispatches by playbookId or by an explicit route.
11. `MatterPreFillService.cs` — replace playbook call with `IActionRunner`.
12. `ProjectPreFillService.cs` — same.
13. `IDocumentDataverseService.cs` — add `UpdateProfileAsync(documentId, DocumentProfileFields, ct)` if not present.

## Data changes — Dataverse (post-cutover only)

Per-consumer, after code migration verified in UAT:

1. Deactivate the `sprk_analysisplaybook` row (set `statecode = 1 Inactive`).
2. Deactivate the associated `sprk_playbooknode` rows.
3. Leave `sprk_analysisaction` rows active — still load-bearing.
4. Leave `sprk_playbookconsumer` rows active — still load-bearing.

Perform via MCP `update_record`. Reversible.

## Consistency guardrails — MUST NOT break

- Chat sessions execute via `PlaybookOrchestrationService`. Do not touch chat dispatch code.
- Insight Engine executes via `PlaybookOrchestrationService`. Do not touch its dispatch.
- Daily Briefing narration runs its own code-defined path via `DailyBriefingNarrator`. Do not refactor it during this migration.
- The Playbook Engine's four Playbook-consumer smoke tests (chat, insights, etc.) MUST pass after all migrations.
- `sprk_analysisaction` schema and semantics must not change.
- `sprk_playbookconsumer` schema and semantics must not change.

## Verification checklist

For each migrated consumer, verify:

- Endpoint responds successfully (2xx) with expected DTO shape
- AI output is deserialized correctly
- Any typed persistence writes happen with correct values
- Any downstream jobs are enqueued
- No orchestrator / template / dispatch code appears in the call stack
- Unit tests for the service class cover happy path + Action-not-configured + LLM-fails
- Playbook row (for that consumer) is deactivated

For coexistence, verify:

- Chat still works (send a message, verify reply)
- Daily Briefing still renders (widget UAT)
- No regressions in Insights (if applicable)
- BFF publish size stays under NFR-01 ceiling
- `dotnet test` all pass

## Related documents

- Architecture: [`docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md`](../../../docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md)
- Task plan: [`wave12-linear-consumer-tasks.md`](wave12-linear-consumer-tasks.md)
- Historical Doc Upload path: [`docs/architecture/sdap-document-processing-architecture.md`](../../../docs/architecture/sdap-document-processing-architecture.md)
- Daily Briefing pattern (companion): [`docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md`](../../../docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md)
- Wizard integration: [`docs/guides/DOCUMENT-UPLOAD-WIZARD-INTEGRATION-GUIDE.md`](../../../docs/guides/DOCUMENT-UPLOAD-WIZARD-INTEGRATION-GUIDE.md)
