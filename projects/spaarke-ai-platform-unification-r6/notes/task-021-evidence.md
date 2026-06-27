# Task 021 Evidence — Build Generic `invoke_playbook` Chat Tool (Pillar 3, D-A-13)

> **Status**: Code + tests + seed row complete; deployment of seed row deferred to main session.
> **Wave**: Phase A wind-down (021 ✅).
> **Last Updated**: 2026-06-08

---

## Outcome Summary

Built the generic `invoke_playbook(playbookId, parameters)` chat-tool handler that
dispatches to ANY tenant-accessible playbook via the `IInvokePlaybookAi` facade
(task 020 landing). Closes Pillar 3 / Q11.

| Deliverable | Status | Path |
|---|---|---|
| Handler (chat-only) | ✅ NEW | `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/InvokePlaybookHandler.cs` |
| Seed row JSON | ✅ NEW | `infra/dataverse/sprk_analysistool-invoke-playbook-row.json` |
| Seed-script map entry | ✅ MODIFIED | `scripts/Seed-TypedHandlers.ps1` (+1 `$RowFiles` entry: `INVOKE-PLAYBOOK`) |
| Unit tests | ✅ NEW (29 tests) | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/InvokePlaybookHandlerTests.cs` |
| DI module edit | ⏭️ SKIPPED (auto-discovery sufficient — see §Design Decisions) | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` |
| Bookkeeping note | ✅ NEW (this file) | `projects/spaarke-ai-platform-unification-r6/notes/task-021-evidence.md` |

---

## Build + Test Verification

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | **0 errors**, 16 baseline warnings ✅ |
| InvokePlaybookHandlerTests | **29/29 PASS** in 265ms ✅ |
| `Services.Ai` sweep (3637 tests) | **3637 passed, 0 failed, 22 skipped** in 16s ✅ |
| Test count delta vs baseline | +29 (baseline pre-task: 3608) ✅ |
| Publish-size delta | Compressed publish **~44.6 MB** (vs ~45.65 MB prior baseline) — neutral / slightly under (Release-mode + BCL-only changes; well under the +5 MB R6 budget) ✅ |

---

## Design Decisions

### D1. Facade-only injection (ADR-013 binding)

The handler injects ONLY `IInvokePlaybookAi` (the public facade) for orchestration. NEVER
`IPlaybookOrchestrationService`, `IPlaybookExecutionEngine`, or any other AI-internal type.
This is the canonical Pillar 3 / Q11 contract: chat-tool dispatch is a CRUD-side caller of
the orchestration layer; the facade is the bridge.

Companion deps:
- `IPlaybookService` — tenant playbook-visibility lookup (Dataverse CRUD facade per ADR-013;
  registered in `AddPlaybookServices` as a typed HttpClient).
- `IHttpContextAccessor` — required by `PlaybookInvocationContext.HttpContext` (facade
  contract). Available via `AddHttpContextAccessor()` in `AddAnalysisOrchestrationServices`.
- `IMemoryCache` — per-tenant visibility cache per ADR-014.

### D2. Tenant playbook visibility — IPlaybookService.GetPlaybookAsync (NOT a new service)

**Surfaced** during initial design: the POML asked for "validate playbookId against
tenant-accessible `sprk_analysisplaybook` rows". No dedicated `ITenantPlaybookVisibilityService`
exists in the codebase.

**Decision**: use the existing `IPlaybookService.GetPlaybookAsync(playbookId)` — which
returns `null` for both "not found" AND "no access". The BFF authenticates per-tenant
against the Dataverse environment with a per-tenant credential; Dataverse rows are scoped
per environment + per-row security, so any playbook returned by the service IS
tenant-accessible. **Collapsing "not found" + "no access" into a uniform
`ValidationFailed` response is intentional** — prevents the LLM (and downstream
observability) from inferring cross-tenant existence.

This avoided introducing an undocumented service or invasive entry to the visibility
contract. Per project CLAUDE.md "ADRs Are Defaults" — choosing the simplest mechanism
that satisfies FR-22 + NFR-14 without a new abstraction.

### D3. Per-tenant cache (ADR-014 binding)

`IMemoryCache` key format: `invoke-playbook:visibility:{tenantId}:{playbookId:D}`. The
`tenantId` prefix is non-optional — cross-tenant leakage is impossible. TTL 5 minutes
(short enough for visibility revocations to propagate; long enough to amortize
Dataverse lookups across a chat turn and absorb LLM-retry storms).

Both positive (visible) and negative (not visible) results are cached. Negative caching
is safe because the LLM cannot influence tenant-admin visibility changes; if a tenant
admin grants access mid-window, the worst-case latency to discoverability is the
5-minute TTL.

### D4. Wave 7b citation envelope projection

The facade's `PlaybookInvocationResult.Citations` (already aggregated across the
playbook's terminal node outputs + RAG retrievals per task 020's impl) is forwarded into
`ToolResult.Metadata[ToolResultMetadataKeys.Citations]` so the
`ToolHandlerToAIFunctionAdapter` accumulates them into the per-chat-turn `CitationContext`.
Citation list is omitted from `Metadata` when empty (cleaner adapter behavior; tests
verify both paths).

**No widget envelope** is emitted from this handler. Sub-playbooks may emit their own
widget events via their tool nodes; this dispatcher's role is summary text + structured
data + citations only. Future cross-playbook widget composition is OUT OF SCOPE for
task 021.

### D5. Chat-only (not Both)

`SupportedInvocationContexts = InvocationContextKind.Chat` — playbook-context
invocation does not make sense for a generic playbook dispatcher (playbooks don't chain
to other playbooks via chat-tool indirection; they call actions/tools directly through
their node executors). `Validate` + `ExecuteAsync` return a clear `ValidationFailed`
when called from the playbook path. Future R7+ work MAY extend this to `Both` if
cross-playbook composition becomes a requirement.

### D6. Parameter shape validation only (per-playbook schema deferred)

**Surfaced during design**: the POML asked for "validate parameters against the
playbook's parameter schema". A general per-playbook parameter-schema validator would
need the playbook's `ConfigJson` to declare a JSON Schema for parameters, and a JSON
Schema validator NuGet — both significant additions (the NuGet costs ~1 MB per the
"ADRs Are Defaults" example pattern in CLAUDE.md).

**Decision (per task POML stop-and-surface guidance)**: ship shape validation only —
`parameters` must be a JSON object (not array / scalar) when present. The seed row's
`sprk_jsonschema` uses `parameters: { type: "object", additionalProperties: true }` so
the adapter doesn't pre-reject playbook-specific keys. Per-playbook schema validation is
correctly deferred to a future task (potentially Pillar 5 outputSchema work + a related
parameter-schema migration). Documented in the seed row's `_comment_parameters_schema`
block.

### D7. Auto-discovery — NO module edit required

Per `Services/Ai/Handlers/HandlerRegistrationConventions.md` §(2), handlers are
auto-discovered by `ToolFrameworkExtensions.AddToolHandlersFromAssembly`. All
dependencies of `InvokePlaybookHandler` are already registered:

| Dependency | Registration site |
|---|---|
| `IInvokePlaybookAi` | `AddPublicContractsFacade` (line 544, compound-AI-ON) + `AddNullObjectsForCompoundOff` (line 271, NullInvokePlaybookAi) — symmetric pair per F.1 anti-pattern guard |
| `IPlaybookService` | `AddPlaybookServices` line 445 (compound-AI-ON) + `AddNullObjectsForCompoundOff` line 287 (NullPlaybookService) |
| `IHttpContextAccessor` | `AddAnalysisOrchestrationServices` line 380 |
| `IMemoryCache` | Registered globally via `services.AddMemoryCache()` (Spaarke.Core) |
| `ILogger<>` | ASP.NET Core default |

Verified `dotnet build` succeeds with 0 errors. The `HandlerType_IsRegisteredInDi` test
passes — assembly scan picks up `InvokePlaybookHandler` automatically. NO new
top-level DI line; NO `AnalysisServicesModule.cs` edit. Per ADR-010 (DI minimalism).

### D8. ADR-015 telemetry hygiene

- Telemetry log statements emit: handler name, playbookId, tenantId, parameterCount,
  decision (success/visibility-denied/failed), runId, citationCount, durationMs —
  ALL deterministic identifiers or scalars.
- NEVER logged: parameter VALUES, raw tool-arguments JSON content, facade result
  `TextContent`, citation text, user-message content.
- Two dedicated telemetry tests (sentinel-string scan):
  - `Telemetry_RespectsAdr015_DoesNotLogParameterValues_OrFacadeTextContent` — happy
    path with secret parameter value + secret-client-name text content; asserts neither
    appears in any captured log message.
  - `Telemetry_RespectsAdr015_VisibilityDenial_DoesNotLogParameterValues` — denial
    path; same parameter sentinel scan.

### D9. Error code mapping

| Failure mode | `ToolErrorCodes` |
|---|---|
| Missing / non-GUID / unparseable args | `ValidationFailed` |
| Playbook not found / not active / not visible to tenant | `ValidationFailed` (uniform — no info leakage) |
| Visibility lookup transient throw (HttpRequestException, etc.) | `InternalError` (not cached) |
| `HttpContext` unavailable (background invocation) | `DependencyUnavailable` |
| Facade throws `FeatureDisabledException` | `DependencyUnavailable` |
| Facade returns `Success=false` with `ErrorCode` | preserved (e.g., `PLAYBOOK_INVOCATION_FAILED`) or `InternalError` fallback |
| `CancellationToken` triggered | `Cancelled` |
| Other unhandled exception | `InternalError` |

### D10. Coordination with task 025

Confirmed `AnalysisServicesModule.cs` was NOT modified by this task (auto-discovery
sufficient — see D7). No merge conflict surface with task 025's
`SessionSummarizeOrchestrator` refactor. Task 025's coordinator dispatched a message
mid-execution that referenced orchestrator/engine work — surfaced and noted as
out-of-scope for task 021; no action taken.

---

## Acceptance Criteria Walk

| Criterion | Status | Evidence |
|---|---|---|
| `InvokePlaybookHandler` implements `IToolHandler`; injects `IInvokePlaybookAi` facade only | ✅ | Handler class file; constructor takes only `IInvokePlaybookAi` + companion CRUD deps (`IPlaybookService`, `IHttpContextAccessor`, `IMemoryCache`, `ILogger`); no AI-internal type. |
| `sprk_analysistool` row seeded with valid JsonSchema | ✅ | `infra/dataverse/sprk_analysistool-invoke-playbook-row.json`; `sprk_toolcode = INVOKE-PLAYBOOK`, `sprk_availableincontexts = 100000001` (Chat), JSON Schema with `playbookId: uuid` required + `parameters: object additionalProperties: true`. |
| Handler validates `playbookId` against tenant-accessible rows | ✅ | `IsTenantVisibleAsync` via `IPlaybookService.GetPlaybookAsync` + per-tenant `IMemoryCache` prefix; null/inactive → uniform `ValidationFailed`; facade NOT invoked. Tests: `ExecuteChatAsync_ReturnsValidationFailed_WhenPlaybookNotFound_AndDoesNotDispatchFacade`, `ExecuteChatAsync_ReturnsValidationFailed_WhenPlaybookIsInactive`. |
| Handler validates `parameters` against playbook's parameter schema | ⚠️ Shape only (D6) | Shape validation in `ValidateChat` + `TryParseArgs`. Per-playbook schema validation deferred per D6. |
| Tenant isolation enforced; cross-tenant id rejected | ✅ | Cache key includes `tenantId` prefix; tenant A visible + tenant B denied test (`ExecuteChatAsync_TenantIsolation_DifferentTenantsDoNotShareVisibilityCache`). |
| Telemetry ADR-015-compliant | ✅ | Two sentinel-string scan tests (D8). |
| Handler registered inside existing module per ADR-010 | ✅ | Auto-discovered (D7); zero new DI line. |
| BFF publish-size delta reported; ≤+5 MB R6 budget | ✅ | ~44.6 MB compressed (neutral vs 45.65 MB baseline); well under +5 MB budget. |

---

## Stop-and-Surface Items (resolved during execution)

| # | Item | Resolution |
|---|---|---|
| S1 | Per-playbook parameter schema validation requires NuGet + ConfigJson schema declaration | Per POML guidance: shipped shape-only validation + documented in seed row `_comment_parameters_schema`. Per-playbook validation deferred. |
| S2 | No `ITenantPlaybookVisibilityService` exists | Reused `IPlaybookService.GetPlaybookAsync` — Dataverse per-tenant auth scope makes returned-non-null = tenant-accessible. Uniform `null → ValidationFailed` collapse prevents cross-tenant info leakage. |
| S3 | `PlaybookInvocationContext.HttpContext` required by facade; `ChatInvocationContext` doesn't carry it | Inject `IHttpContextAccessor` (established Spaarke pattern); handle absence cleanly with `DependencyUnavailable` rather than NRE. |
| S4 | Coordinator routing — mid-execution message addressed task 025 but landed in 021 inbox | Noted as out-of-scope; continued task 021 per original dispatch. Documented in D10. |

No remaining open stop-and-surface items.

---

## Files Touched (final)

```
NEW:
  src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/InvokePlaybookHandler.cs            (498 LOC)
  infra/dataverse/sprk_analysistool-invoke-playbook-row.json
  tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/InvokePlaybookHandlerTests.cs     (568 LOC, 29 tests)
  projects/spaarke-ai-platform-unification-r6/notes/task-021-evidence.md               (this file)

MODIFIED:
  scripts/Seed-TypedHandlers.ps1                                                       (+1 $RowFiles entry)
```

`AnalysisServicesModule.cs` and `current-task.md` / `TASK-INDEX.md` are unchanged (per
sub-agent write boundary + auto-discovery sufficiency).
