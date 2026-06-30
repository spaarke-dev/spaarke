# Spike #4 — Consumer-Routing E2E + JPS Scope Registration

> **Status**: LOCKED (design-locked artifact; production wiring deferred to Phase 5)
> **Project**: spaarkeai-compose-r1
> **Author**: spike-4 sub-agent (autonomous Wave 0 dispatch)
> **Date**: 2026-06-29
> **POML**: [`tasks/004-spike-consumer-routing-jps.poml`](../../tasks/004-spike-consumer-routing-jps.poml)
> **Rigor level**: STANDARD (per POML rigor-hint)

---

## 1. Purpose

Lock the **JPS scope schemas** + **consumer-routing E2E flow** for the first Compose consumer type (`compose-summarize`) so Phase 1 (Dataverse rows + JPS scope registration) and Phase 2 (BFF endpoint + service implementation) can proceed without retrofit.

This is the **load-bearing R1 smoke test design**. R2 multiplies AI actions through the foundation this spike locks.

---

## 2. Executive summary

| Deliverable | Status | Location |
|---|---|---|
| JPS scope: `compose-selection` | ✅ Locked | `spike-4-prototype/scopes/compose-selection.scope.json` |
| JPS scope: `compose-document` | ✅ Locked | `spike-4-prototype/scopes/compose-document.scope.json` |
| `ConsumerTypes.ComposeSummarize` constant — additive diff | ✅ Locked | `spike-4-prototype/ConsumerTypes.compose.stub.cs` |
| BFF dispatch endpoint shape | ✅ Locked | `spike-4-prototype/ComposeActionEndpoint.stub.cs` |
| Playbook linkage (consumer → playbook GUID) | ✅ Documented | This file § 7 |
| E2E flow trace (selection → dispatch → playbook → response) | ✅ Traced | This file § 6 |
| ADR-013 facade boundary verification | ✅ Clean grep | This file § 9 |
| Live Dataverse row seed | ⚠️ Deferred to task 011 | This file § 10 |
| Live `dotnet run` E2E call | ⚠️ Deferred to task 060 (smoke test) | This file § 11 |

**Why some pieces are deferred**: Per POML constraint "Prototype code lives in `notes/spikes/` ONLY — throwaway; production wiring happens in Phase 5", the spike's job is to LOCK THE DESIGN, not to run live infrastructure. Task 011 (Dataverse row seed) and task 060 (smoke test) are the real-execution surfaces; this spike de-risks both by locking the inputs they consume.

**Open items requiring main-session decision**: None. All inputs to Phase 1 + Phase 2 are decided.

---

## 3. ADR tensions

**None.** Refined ADR-013 (2026-05-20) cleanly accommodates this spike:
- The dispatch endpoint injects ONLY `IConsumerRoutingService` + `IInvokePlaybookAi` (both in `Services/Ai/PublicContracts/`)
- No new direct CRUD→AI dependency is added
- The `compose-summarize` consumer type IS the canonical example refined ADR-013 anticipates

No path A (project-scoped exception), no path B (ADR amendment), no path C (pivot) required. The spike is in clean compliance with all applicable ADRs (001, 008, 010, 013, 015, 019, 028, 032, 038).

CLAUDE.md §6.5 protocol does not fire.

---

## 4. JPS scope schemas (locked)

Both schemas use `$schema: https://spaarke.com/schemas/jps-scope/v1`. Schema design follows the precedent set by `.claude/skills/jps-action-create/examples/*.json` (input/output/scopes/metadata sections) adapted for the scope-definition use case.

### 4.1 `compose-selection`

**Intent**: "Selected text in a Compose-hosted document" — input contract for R2 actions like Explain clause / Replace with standard / Compare-to-playbook / Draft alternative.

**Fields (9 total)**: 5 required, 4 optional.

| Field | Type | Required | Purpose |
|---|---|---|---|
| `selectionText` | string (≤16K) | yes | The text the user has selected (UTF-8 plain). |
| `selectionAnchorStart` | number | yes | 0-based char offset (start). Enables R2 annotation re-anchoring. |
| `selectionAnchorEnd` | number | yes | 0-based char offset (end exclusive). |
| `documentSpeId` | string | yes | SPE drive-item id (always present per Compose binding rule). |
| `documentVersionEtag` | string | optional | Staleness detection across mid-flight document changes. |
| `documentRecordId` | uuid | optional | `sprk_document` GUID (null pre-Save). |
| `matterId` | uuid | optional | Matter-scoped knowledge retrieval enabler. |
| `sessionId` | string | yes | `ChatSession` id for write-back to session memory. |
| `tenantId` | uuid | yes | ADR-015 Tier 3 isolation key. |

**Total char cap**: 16,500 (16K selection + ~500 identifiers). Stays well under the chat-text caps per CHAT-ATTACHMENT-POLICY.

**Data governance** (ADR-015): `selectionText` IS user content → `doNotLog`. Identifiers (`documentSpeId`, `sessionId`, `tenantId`, `matterId`) are loggable.

### 4.2 `compose-document`

**Intent**: "Whole document open in Compose" — input contract for R1 `compose-summarize` + R2 whole-document actions (e.g., compose-risk-scan, compose-extract-obligations).

**Fields (8 total)**: 3 required, 5 optional.

| Field | Type | Required | Purpose |
|---|---|---|---|
| `documentSpeId` | string | yes | SPE drive-item id (always present). |
| `documentVersionEtag` | string | optional | Staleness detection. |
| `documentRecordId` | uuid | optional | `sprk_document` GUID (null pre-Save). |
| `matterId` | uuid | optional | Matter-scoped knowledge enabler. |
| `documentName` | string | optional | Display name. |
| `documentMimeType` | string | optional | Carried into `RoutingContext.MimeType` for content-aware routing. |
| `sessionId` | string | yes | `ChatSession` id. |
| `tenantId` | uuid | yes | ADR-015 isolation. |

**Total char cap**: 1,500 (identifiers + names only — no document text).

**Critical design decision**: The document TEXT is NOT in the scope payload. The playbook fetches DOCX bytes from SPE server-side using `documentSpeId`. This keeps the scope payload tiny and ADR-015-compliant (no user content marshalled through the BFF↔UI boundary on every dispatch).

**Data governance** (ADR-015): `containsUserContent: false`. All fields are loggable identifiers.

### 4.3 `jps-validate` posture (Step 5 of POML)

The `jps-validate` skill (`.claude/skills/jps-validate/SKILL.md`) checks JPS **action** definitions (Steps 2–8 of its workflow target action files like `file-summary.json`). The two artifacts here are **scope-definition** files at `$schema: https://spaarke.com/schemas/jps-scope/v1`, not action definitions at `prompt/v1`.

**Validation applied**: The relevant `jps-validate` checks for scope files (Step 5 of the skill — "Scope Reference Validation") are:

- ✅ CHECK 2: Has `$schema` — present (`jps-scope/v1`)
- ✅ Valid JSON — both files parse cleanly
- ✅ Has metadata section with description + tags — present
- ✅ Has stable `scopeCode` matching design.md §7 names (`compose-selection`, `compose-document`)
- ✅ Has explicit `inputs.fields[]` with name + type + description per field
- ✅ Required vs optional clearly demarcated
- ✅ ADR-015 `dataGovernance` block declares user-content fields + `doNotLog` rules

**Why `jps-validate` is not the right tool for this artifact**: `jps-validate` targets action JPS files (prompt schemas). Scope files are a different artifact class — they're consumed by the JPS catalog/registration tooling (`jps-scope-refresh` skill — invoked by task 012 in Phase 1). Phase 1 task 012 is the live-validation surface for scopes; this spike locks the SHAPE that task 012 will register.

**Action item for task 012**: invoke `jps-scope-refresh` against these two files; verify they register cleanly into the scope catalog. If a future version of `jps-validate` adds scope-file support (Step 5.5 hypothetical), wire it in.

---

## 5. ConsumerType constant (additive, ADR-013-compliant)

Production location (task 020): `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs`.

The change is **purely additive** — no existing constant renamed, no semantic change to `All[]` ordering for existing entries. Exact diff documented in `spike-4-prototype/ConsumerTypes.compose.stub.cs`.

```csharp
// Added to the existing ConsumerTypes class:
public const string ComposeSummarize = "compose-summarize";

// Appended to All:
public static readonly IReadOnlyList<string> All = new[]
{
    MatterPreFill, ProjectPreFill, AiSummary, SummarizeFile,
    ChatSummarize, EmailAnalysis, DailyBriefingNarrate,
    ComposeSummarize,  // <-- appended
};
```

Convention compliance (per HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md §2 Step 1):
- ✅ Lower-kebab-case (`compose-summarize`)
- ✅ No spaces, underscores, or leading numbers
- ✅ Stable (will not be renamed once shipped)
- ✅ Self-describing
- ✅ URL-safe (no path separators) for cache-key + telemetry dimension safety

---

## 6. End-to-End flow trace

The full dispatch trace for `compose-summarize`:

```
┌─────────────────────────────────────────────────────────────────────┐
│ COMPOSE UI (SpaarkeAi workspace, ComposeEditor.tsx)                 │
│   - User clicks "Summarize" in ComposeToolbar                       │
│   - Reads current document + session state                          │
│   - Constructs ComposeActionRequest per compose-document scope:     │
│     { documentSpeId, sessionId, [matterId, documentRecordId,        │
│       documentName, documentMimeType, documentVersionEtag] }        │
│   - For compose-summarize: Selection is null (whole-document)       │
└────────────────────────────────────┬────────────────────────────────┘
                                     │ POST /api/compose/action/compose-summarize
                                     │ Body: ComposeActionRequest
                                     │ Auth: Bearer {user token}
                                     ▼
┌─────────────────────────────────────────────────────────────────────┐
│ BFF — ComposeEndpoints.MapPost("/action/{consumerType}")            │
│   - Endpoint filter: DocumentAuthorizationFilter (ADR-008)          │
│   - RequireAuthorization() per ADR-008/028                          │
│   - RequireRateLimiting("standard")                                 │
│   - Resolves ComposeService from DI scope                           │
└────────────────────────────────────┬────────────────────────────────┘
                                     │
                                     ▼
┌─────────────────────────────────────────────────────────────────────┐
│ ComposeService.InvokeAsync(consumerType, request, httpContext, ct)  │
│  1. Validate: ConsumerTypes.All.Contains("compose-summarize") ✓     │
│  2. Resolve playbook:                                               │
│        _routing.ResolveAsync(                                       │
│            "compose-summarize",                                     │
│            consumerCode: "default",                                 │
│            context: RoutingContext { MimeType = docx-mime },        │
│            environment: null,                                       │
│            ct)                                                      │
│     ↓ returns Guid (47686eb1-9916-f111-8343-7c1e520aa4df)            │
│  3. Build parameters dict from compose-document scope payload       │
│  4. Build PlaybookInvocationContext:                                │
│        { TenantId, HttpContext, CorrelationId }                     │
│  5. Invoke via facade:                                              │
│        _invokePlaybook.InvokePlaybookAsync(                         │
│            playbookId, parameters, invocationContext, ct)           │
└────────────────────────────────────┬────────────────────────────────┘
                                     │ (PublicContracts facade boundary)
                                     ▼
┌─────────────────────────────────────────────────────────────────────┐
│ INSIDE THE AI ZONE (ADR-013 internal — Compose CRUD code never      │
│ sees these types):                                                  │
│                                                                     │
│   InvokePlaybookAi → IPlaybookOrchestrationService.ExecuteAsync     │
│   → PlaybookExecutionEngine → node executors                        │
│     - Fetch DOCX bytes from SPE (using documentSpeId)               │
│     - Extract text (DOCX → plain text per the Document Summary      │
│       playbook node config)                                         │
│     - Hit Azure OpenAI per node prompts (PB-002 / Doc Summary)      │
│     - SSE event stream → consumed + aggregated by InvokePlaybookAi  │
│   → Returns PlaybookInvocationResult                                │
└────────────────────────────────────┬────────────────────────────────┘
                                     │ (facade boundary)
                                     ▼
┌─────────────────────────────────────────────────────────────────────┐
│ ComposeService projects result → ComposeActionResponse              │
│   { RunId, Success, TextContent, StructuredData,                    │
│     Citations, Confidence, Duration, ErrorMessage, ErrorCode }      │
└────────────────────────────────────┬────────────────────────────────┘
                                     │ 200 OK + ComposeActionResponse
                                     ▼
┌─────────────────────────────────────────────────────────────────────┐
│ COMPOSE UI                                                          │
│   - Renders TextContent in a Compose result drawer                  │
│   - Persists summary as a structured ChatMessage in the bound       │
│     ChatSession (DocumentId = documentSpeId) so the "prior          │
│     sessions for this document" UX preserves it                     │
└─────────────────────────────────────────────────────────────────────┘
```

**E2E validation status**:
- **Traced (design-locked)**: The full chain from UI request → BFF endpoint → ComposeService → PublicContracts facade → playbook orchestration → response is locked. Every interface signature is reachable in current code.
- **Simulated (not run live)**: This spike does NOT execute a live `POST /api/compose/action/compose-summarize` against a running BFF. Live execution is task 060 (E2E smoke test) per Phase 6 of the project plan.
- **Why simulated is acceptable**: Each segment of the chain is independently verified. The PublicContracts facade (`IConsumerRoutingService` + `IInvokePlaybookAi`) is exercised in production today by `SessionSummarizeOrchestrator` (chat-summarize), `MatterPreFillService`, `WorkspaceAiService`, and 4 others. Adding a new consumer-type discriminator does NOT change the facade behavior — it only requires a new `sprk_playbookconsumer` row + new constant. The spike validates the SHAPE.

---

## 7. Playbook linkage

| Field | Value |
|---|---|
| Consumer type code | `compose-summarize` |
| `sprk_consumertype` (Dataverse) | `compose-summarize` |
| `sprk_consumercode` | `default` |
| `sprk_environment` | `*` (all envs) |
| `sprk_priority` | `500` (spec default) |
| `sprk_enabled` | `true` |
| `sprk_matchconditions` | `null` (no per-request conditional routing in R1) |
| `sprk_playbookid` (lookup target) | `47686eb1-9916-f111-8343-7c1e520aa4df` |
| Playbook display name | Document Summary (existing — design.md §14 row 6) |
| Cache invalidation | 5 minutes (per `ConsumerRoutingService.cs` cache TTL — ADR-014) |

Production seeding (task 011): append to `scripts/dataverse/Seed-PlaybookConsumers.ps1` `$Records` array using the canonical Option A pattern in HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md §2. Idempotent UPSERT keyed on `sprk_ConsumerTypeCodeEnvironment` alternate key.

**Per-environment caveat**: `47686eb1-9916-f111-8343-7c1e520aa4df` is the Document Summary playbook GUID in the Dev environment per design.md §14 row 6. When seeding test / prod environments, look up that environment's Document Summary playbook GUID via PAC CLI or Dataverse maker portal — DO NOT reuse the Dev GUID.

---

## 8. BFF endpoint contract

Detailed sketch in `spike-4-prototype/ComposeActionEndpoint.stub.cs`. Tight summary:

| Property | Value |
|---|---|
| Route | `POST /api/compose/action/{consumerType}` |
| Path discriminator | `{consumerType}` ∈ `ConsumerTypes.All` (404 if not) |
| Body | `ComposeActionRequest` (per `compose-document` scope; `Selection` is optional + only populated for selection-scoped actions) |
| Auth | `RequireAuthorization()` (ADR-008/028) + `DocumentAuthorizationFilter` |
| Rate limit | `RequireRateLimiting("standard")` (default policy) |
| Success | 200 + `ComposeActionResponse` |
| Validation error | 400 |
| Unauth | 401 |
| Unknown consumer | 404 (returned by `ComposeService.InvokeAsync` validation) |
| Dispatch unconfigured | 503 (returned when `IConsumerRoutingService.ResolveAsync` returns null) |
| Internal | 500 |
| Streaming | NO — single aggregated response per `IInvokePlaybookAi` non-streaming contract |
| Route group | `/api/compose/*` per ADR-019 endpoint conventions |

**Why non-streaming**: The `IInvokePlaybookAi` facade is designed for a single tool-call response (per `IInvokePlaybookAi.cs` doc comments). Compose can render the result in its own UI once it arrives. R2 may add a `/stream` variant if a use case justifies it; out of R1 scope.

**Why this is NOT a "thin pass-through"**: The endpoint owns:
1. Path-discriminator validation (defense-in-depth above the routing layer)
2. Scope-payload → `parameters` dict translation (`compose-document` / `compose-selection` → playbook parameters)
3. `PlaybookInvocationContext` construction (tenant resolution, correlation id)
4. Result projection (`PlaybookInvocationResult` → `ComposeActionResponse`)

These responsibilities live in `ComposeService` (task 021), not `ComposeEndpoints.cs` (task 024) — endpoints stay thin per `Sprk.Bff.Api/CLAUDE.md` "Keep endpoints thin (delegate to services)".

---

## 9. ADR-013 facade boundary — grep evidence

**Forbidden injection check** (per POML step 6 + ADR-013 refined 2026-05-20):

```
Grep pattern: IOpenAiClient|IPlaybookService|IPlaybookOrchestrationService|IPlaybookExecutionEngine|OpenAIClient|Azure\.AI\.OpenAI
Path:        projects/spaarkeai-compose-r1/notes/spikes/spike-4-prototype/

Result: 4 matches, ALL in negation comments documenting what NOT to inject:
  - ConsumerTypes.compose.stub.cs:48 — "// code references AI internals (IOpenAiClient, IPlaybookService) via this path."  ← Doc comment stating this path does NOT reference them
  - ComposeActionEndpoint.stub.cs:8  — "// No AI internals (IOpenAiClient, IPlaybookService, IPlaybookOrchestrationService,"  ← Negation
  - ComposeActionEndpoint.stub.cs:9  — "// IPlaybookExecutionEngine) are injected."                                              ← Negation
  - ComposeActionEndpoint.stub.cs:138 — "// 5. Invoke via PublicContracts facade (NOT IPlaybookOrchestrationService)."             ← Negation
```

**Allowed injection check** (positive evidence that the facade types ARE referenced):

```
Grep pattern: IConsumerRoutingService|IInvokePlaybookAi
Path:        projects/spaarkeai-compose-r1/notes/spikes/spike-4-prototype/

Result: 9 matches, all citing/sketching usage of the PublicContracts facade in
ComposeService constructor + invocation. These are the ONLY AI-related types
referenced in the spike prototype.
```

**Verdict**: Boundary CLEAN. The spike design respects refined ADR-013 (2026-05-20) verbatim — no Compose CRUD code references any AI internal type.

---

## 10. Seeded Dataverse row — deferred to task 011

Per the spike's "design-lock, don't execute" charter, this spike does NOT create a live `sprk_playbookconsumer` row. The seed is the canonical Phase 1 task 011 work (per `projects/spaarkeai-compose-r1/tasks/011-dataverse-create-playbookconsumer-row.poml`).

**For task 011 — exact row to seed** (append to `scripts/dataverse/Seed-PlaybookConsumers.ps1` `$Records` array):

```powershell
@{
    Name            = 'Compose Whole-Document Summarize'
    ConsumerType    = 'compose-summarize'
    ConsumerCode    = 'default'
    Environment     = '*'
    Priority        = 500
    Enabled         = $true
    PlaybookId      = '47686eb1-9916-f111-8343-7c1e520aa4df'  # DEV - update per environment
    PlaybookComment = 'PB-002 Document Profile (reused for Compose R1 smoke test)'
}
```

**Cleanup instructions**: To remove the row (e.g., post-R1 retirement if Compose moves to a Compose-specific summarize playbook): delete the `sprk_playbookconsumer` row via PowerApps maker portal OR re-run `Seed-PlaybookConsumers.ps1` with the row removed from `$Records`. Cache invalidates within 5 minutes per `ConsumerRoutingService.cs` TTL.

---

## 11. Live E2E call — deferred to task 060

Per the spike's charter, this spike does NOT execute a live `POST /api/compose/action/compose-summarize`. Live execution is the canonical Phase 6 task 060 work (per `projects/spaarkeai-compose-r1/tasks/060-smoke-test-compose-summarize.poml`).

**For task 060 — exact verification sequence**:

1. Confirm task 011's `sprk_playbookconsumer` row exists in Dataverse (use `mcp__dataverse__read_query` or `Get-MgRequest`).
2. Wait 5 minutes (cache lag) OR restart `spaarke-bff-dev` App Service.
3. POST a synthetic `ComposeActionRequest` against an existing SPE document:
   ```json
   {
     "documentSpeId": "{real-spe-drive-item-id}",
     "documentRecordId": "{sprk_documentid-if-promoted}",
     "sessionId": "{active-ChatSession-id}",
     "documentName": "Test Document for Compose Smoke",
     "documentMimeType": "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
   }
   ```
4. Assert: 200 OK; `response.Success == true`; `response.TextContent` is non-empty and resembles a document summary; `response.RunId` is a Guid; `response.Duration` < 30s; telemetry shows `consumerType=compose-summarize` + `cacheHit=false` (first call) then `cacheHit=true`.

---

## 12. Risk register for Phase 5 (consumer-routing wiring)

| Risk | Likelihood | Mitigation |
|---|---|---|
| `IInvokePlaybookAi` SSE-to-aggregate projection drops citations | Low | Existing chat-tool consumers exercise this in production; citation infrastructure is Wave 7b-tested. Compose adds no new shape requirement. |
| Document Summary playbook (`47686eb1-...`) is summarize-file-shaped, not single-document-shaped | Low | The playbook's PB-002 Document Profile output (per `file-summary.json` precedent) IS single-document-shaped; the multi-file `fileHighlights` field is optional and absent when only one document is supplied. Compose-summarize is a 1-document call. |
| BFF publish size delta exceeds +5MB threshold (NFR-01 per-task rule) | Very Low | Compose adds source files only — no new NuGet packages, no new assemblies. Estimated delta: <100 KB compressed. Must verify in task 020 / 024 / 025 per per-task rule. |
| Cache-lag race after task 011's row insert | Low | Documented in HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md §4 "Wait 5 minutes (cache lag) OR restart the BFF". Task 060 verification step (3) accounts for it. |
| `sprk_consumertype` typo on Dataverse side breaks routing (UAT-2 failure mode) | Low | Seed script is the input source-of-truth; `ConsumerTypes.cs` is the BFF input source-of-truth. Both reference the same literal `"compose-summarize"`. Task 028e startup health-log (queued as a future enhancement per `ConsumerTypes.cs` doc comment) will catch this in production if a maker edits the row directly. |
| Phase 5 endpoint adds to Sprk.Bff.Api/Program.cs concurrent with 13 other active BFF projects | Medium | Hot-path-declaration in design.md §10.5 + projects/INDEX.md. Phase 5 task 025 reviewer sequences against in-flight peer PRs. Single-line additive registration to Program.cs minimizes merge conflict surface. |

None of these block Phase 1 + Phase 2 task execution.

---

## 13. Files produced by this spike

```
projects/spaarkeai-compose-r1/notes/spikes/
├── spike-4-consumer-routing-jps.md                     (THIS FILE — locked artifact)
└── spike-4-prototype/
    ├── ConsumerTypes.compose.stub.cs                   (additive-diff sketch — task 020 input)
    ├── ComposeActionEndpoint.stub.cs                   (endpoint + service shape — task 021/024 input)
    └── scopes/
        ├── compose-selection.scope.json                (R2 input contract — task 012 input)
        └── compose-document.scope.json                 (R1 smoke-test input contract — task 012 input)
```

All files are throwaway prototype artifacts per the POML constraint "Prototype code lives in `notes/spikes/` ONLY". Production code in Phase 1 + 2 + 5 will reference these files for shape, NOT copy verbatim.

---

## 14. Phase 0 spike-gate readiness assessment

Per project autonomous-mode rules (project CLAUDE.md §"Autonomous Parallel Execution Mode"): after Wave 0 completes, a **mandatory operator-review gate** fires for spike artifact sign-off.

**Spike #4 status**: ✅ COMPLETE. Locked artifacts at the expected paths. ADR tensions: NONE. Open items requiring main-session decision: NONE.

**Gate readiness for Wave 1**: Spike #4 unlocks tasks 010 (workspace layout), 011 (playbookconsumer row), 012 (JPS scope registration), 020 (ConsumerType constant), 021–025 (BFF service + endpoint + DI). All can dispatch in Wave 1a / 1b once Wave 0 spikes #1–#3 also complete + operator signs off.

This spike alone does NOT clear the gate — operator review of all 4 spike outputs is the gate per project CLAUDE.md.

---

## 15. References

- POML: [`projects/spaarkeai-compose-r1/tasks/004-spike-consumer-routing-jps.poml`](../../tasks/004-spike-consumer-routing-jps.poml)
- Design: [`projects/spaarkeai-compose-r1/design.md`](../../design.md) §7 (Playbook integration), §10.5 (Placement Justification), §14 row 6 (first consumer type)
- Spec: [`projects/spaarkeai-compose-r1/spec.md`](../../spec.md)
- Recipe: [`docs/guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md`](../../../../docs/guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md)
- Constraints: [`.claude/constraints/bff-extensions.md`](../../../../.claude/constraints/bff-extensions.md) §F (test obligation), §G (hot-path declaration)
- Facade source: [`src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/)
- Existing precedents: `SessionSummarizeOrchestrator.cs` (chat-summarize); `MatterPreFillService` (matter-pre-fill); `Seed-PlaybookConsumers.ps1` (Dataverse seed pattern)

---

*Locked 2026-06-29 by Wave 0 sub-agent for `spaarkeai-compose-r1`. Reopen only if Phase 1 or Phase 2 surfaces a contract gap not anticipated here.*
