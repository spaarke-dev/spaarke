# Task 022 Evidence — Dynamic `invoke_playbook` Tool Description at Chat-Agent Build Time

> **Status**: Code + tests complete; ready for main-session test run after task 025 lands.
> **Wave**: Phase A wind-down (022 ✅).
> **Last Updated**: 2026-06-08

---

## Outcome Summary

Made the generic `invoke_playbook` chat-tool's description dynamic at chat-agent build time
so the LLM sees the menu of tenant-accessible playbooks (id + name + short description)
rather than the static seed-row placeholder. This unblocks task 023 (specialized-bridge
removal) — the LLM no longer has to "know" playbook IDs; they're in the tool description.

| Deliverable | Status | Path |
|---|---|---|
| Factory edit (override + helper) | ✅ MODIFIED | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` |
| Unit tests | ✅ NEW (14 tests) | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/InvokePlaybookDescriptionTests.cs` |
| Bookkeeping note | ✅ NEW (this file) | `projects/spaarke-ai-platform-unification-r6/notes/task-022-evidence.md` |
| Seed row | ⏭️ UNCHANGED (per spec — static description is the fallback; dynamic LIVES IN FACTORY) | `infra/dataverse/sprk_analysistool-invoke-playbook-row.json` |
| Seed script | ⏭️ UNCHANGED (no new deploy) | `scripts/Seed-TypedHandlers.ps1` |
| DI registrations | ⏭️ UNCHANGED (ADR-010 — no new top-level lines) | `Infrastructure/DI/*.cs` |

---

## Build + Test Verification

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | **0 errors**, 16 baseline warnings ✅ |
| `dotnet build tests/unit/Sprk.Bff.Api.Tests/` (with my changes only) | New test file compiles ✅; **9 pre-existing errors are in task 025's in-flight files** (`SessionSummarizeOrchestratorTests.cs`, `InvokeSummarizePlaybookToolTests.cs`, `PlaybookExecutionEngineTests.cs`, `SummarizeSessionEndpointTests.cs`) — not caused by task 022. Tests will run after task 025 lands its test updates. |
| Pre-existing warnings | 16 (unchanged from baseline) |
| Tests added | 14 (in `InvokePlaybookDescriptionTests.cs`) |
| Publish-size delta | Compressed publish **44.62 MB** (vs ~44.6 MB prior baseline from task 021 end state) — effectively zero delta. 15 MB below the 60 MB ceiling per ADR-029 / NFR-02. ✅ |

### Why the test project won't build standalone

Task 025 (running in parallel per task 022 prompt §"Coordination with task 025") refactored
`SessionSummarizeOrchestrator` + `PlaybookExecutionEngine` constructor signatures + removed
two public constants (`SummarizeActionCode`, `CombinedSummaryInterjection`) that older tests
referenced. Those tests are getting updated as part of task 025's deliverables. The 9 errors
are localized to 4 task-025 files and DO NOT touch my new test file. When task 025 commits,
both task 022's and task 025's tests build together cleanly.

---

## Design Decisions

### D1. Override mechanism: `row with { Description = dynamicDescription }`

`AnalysisTool` is a C# record with `init`-only properties (`src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs:713`).
The standard record-with pattern produces a new instance with the description overridden;
the original row's other fields (Id, Name, JsonSchema, HandlerClass, …) carry through
unchanged. The adapter constructor receives this overridden copy.

**Alternatives rejected**:

- Adding a `descriptionOverride` parameter to `ToolHandlerToAIFunctionAdapter`: more invasive
  (changes a public ctor with 8+ existing call sites already wired in factory + tests).
  The record-with approach keeps the adapter contract simple — the row's `Description` is
  authoritative.
- Mutating the cached row in-place: rows come from `AnalysisToolService.ListToolsAsync` which
  may share / cache instances; mutation would corrupt cross-tenant state.

### D2. Tenant-accessible playbook list: mirror `ChatEndpoints.ListPlaybooksAsync`

The `GET /api/ai/chat/playbooks` endpoint already defines what "tenant-accessible" means for
chat: user-owned playbooks (via `ListUserPlaybooksAsync` when the `oid` claim is present) +
public playbooks (via `ListPublicPlaybooksAsync`), deduplicated by ID. Task 022 reuses the
exact same surface — no new query API surface was added.

Why this is right: the LLM should see the same playbook list a user can manually pick from
the playbook selector UI — single mental model.

### D3. Cache: `IMemoryCache` (in-process), 5-min TTL, key `r6:chat-tools:invoke-playbook-description:{tenantId}`

ADR-014 says cache keys MUST include `tenantId` — implemented via the per-tenant suffix.
The `r6:` prefix matches the project-memory convention. 5-min TTL matches the
`InvokePlaybookHandler.VisibilityCacheTtl` (also 5 min) — tool description and handler
visibility check stay coherent within a chat turn.

Used `IMemoryCache` not `IDistributedCache` because:

- Chat-agent build runs per chat-session-start (not per-message), so the cache hit ratio
  inside a single process is high already.
- `IDistributedCache` (Redis) is wired but `IMemoryCache` is also DI-registered and is the
  same surface task 021's `InvokePlaybookHandler` uses for its tenant-visibility cache —
  consistency.
- ADR-014's intent (per-tenant cross-pollination prevention) is satisfied by the per-tenant
  key prefix regardless of backing store.

### D4. NFR-10 budget: 1500-char soft cap with alphabetical truncation

- **Per-entry cap**: 120 chars on the description segment (after newline collapse). Keeps
  each menu line legible; truncated entries show `…`.
- **Total cap**: 1500 chars (~375 tokens) — about 5% of the 8K system-prompt budget shared
  across persona + memory + retrieval + tool descriptions. Conservative.
- **Truncation strategy**: alphabetical sort + first-N-fit; surplus collapses to
  `- ...and N more (request by name to discover their IDs).` Stable across builds because
  sort is deterministic.
- **Empty list**: dedicated copy "No playbooks currently available for this tenant. Use
  natural language to request analysis." — steers LLM to conversational mode rather than
  inventing GUIDs.

**Budget math sanity check**: a tenant with 50 playbooks averaging 80 chars each = 4000
chars rendered = far over 1500. The truncation triggers after ~15-18 entries depending on
playbook-name length, with a clear "...and 32 more" suffix.

### D5. Detection: `row.HandlerClass == nameof(InvokePlaybookHandler)`

The dynamic override is gated by the row's `sprk_handlerclass` matching the canonical
handler-class name. This is the same discriminator the data-driven block uses elsewhere
(handler-registry lookup). Using `nameof()` keeps the binding compile-checked rather than
relying on the runtime string `"InvokePlaybookHandler"`.

### D6. Failure mode: fallback to static seed-row description

If the playbook-list query throws (Dataverse outage, transient auth failure, etc.), the
catch block logs and proceeds with the original `row.Description` (the static seed-row
text). The tool still registers — it's just less context-aware for that one chat session.
NFR-01 conversational primacy preserved.

### D7. ADR-015 telemetry

Single `LogInformation` line at the end of `BuildInvokePlaybookDescriptionAsync` emits:

- `tenantId` (deterministic ID — ADR-015 allowed)
- `playbookCount` (deterministic count)
- `descriptionLengthChars` (deterministic int — for budget telemetry)

Cache-hit path logs at `Debug` level (length only, no content). Cache miss + render path
also logs at `Information` (count + length, no content). **No playbook names ever above
Debug level.** The pure renderer (`RenderInvokePlaybookDescription`) takes NO `ILogger` —
makes the ADR-015 contract structural.

---

## NFR / ADR Compliance Walk

| Rule | Status | Evidence |
|---|---|---|
| ADR-010 (no new top-level DI) | ✅ | Zero new `services.AddXxx()` lines. `IMemoryCache` + `IPlaybookService` already registered. |
| ADR-013 (facade boundary) | ✅ | Uses `IPlaybookService` (CRUD facade) only; no AI-internal types injected. |
| ADR-014 (per-tenant cache) | ✅ | Cache key includes `{tenantId}`; cache key prefix asserted by `InvokePlaybookDescriptionCacheKeyPrefix_HasR6TenantScopedShape` test. |
| ADR-015 (telemetry hygiene) | ✅ | Logs only count + tenantId + lengthChars at Information; pure renderer has no logger. |
| ADR-018 (no new feature flags) | ✅ | Override applied unconditionally for the InvokePlaybook row; no flag added. |
| ADR-029 (publish-size budget) | ✅ | +0 MB compressed delta. |
| NFR-10 (8K system prompt budget) | ✅ | 1500-char soft cap with truncation + reproducible alphabetical ordering. |
| NFR-14 (tenant isolation) | ✅ | Per-tenant cache key + per-tenant playbook query; assertion test: `Cache_DifferentTenantKeys_AreIsolated`. |
| FR-23 (name + description in tool desc) | ✅ | Renderer includes id + name + description for each playbook. |

---

## Stop-and-Surface Items

None — all design questions resolved within scope:

- `AnalysisTool` IS a mutable record (via `with` expression) — no need for adapter ctor change.
- `IPlaybookService.ListUserPlaybooksAsync` + `ListPublicPlaybooksAsync` already exist and
  carry the tenant-accessible definition used by the chat endpoint — no new query surface.
- 1500-char budget is workable; alphabetical truncation handles the 50-playbook case
  gracefully.
- The factory's data-driven block exposed a clean override point (insert the override right
  before adapter construction, inside the per-row loop).
- `IMemoryCache` is fine for chat-agent-build-time caching (per ADR-014 intent — tenant
  isolation is via key prefix, not backing store).
- ADR-015 logging hygiene preserved structurally by making the pure renderer logger-free.

---

## Test Coverage Summary (14 tests)

| Category | Tests | What it Asserts |
|---|---|---|
| Empty-list path | 2 | Description states "No playbooks currently available"; renderer + helper agree |
| Small-list rendering | 2 | All entries present with id + name + description; null description handled |
| NFR-10 budget | 3 | Long list truncates within budget with "...and N more" suffix; per-entry trim; newline collapse |
| ADR-014 cache constants | 3 | Key prefix starts with `r6:`, contains `invoke-playbook`, ends with `:`; TTL = 5 min; budget = 1500 |
| Tenant isolation | 1 | Different lists → different outputs (NFR-14) |
| ADR-015 telemetry contract | 1 | Pure renderer takes no logger (structural ADR-015 contract) |
| Cache wiring | 2 | Same key returns cached value; different tenant keys isolated |

---

## Files Modified

1. `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs`
   - Added 2 using directives: `Sprk.Bff.Api.Models.Ai` + `Microsoft.Extensions.Caching.Memory`
   - In `ResolveTools()` data-driven block: inserted `rowForAdapter` override gate before
     `ToolHandlerToAIFunctionAdapter` construction. When `row.HandlerClass ==
     nameof(InvokePlaybookHandler)`, calls `BuildInvokePlaybookDescriptionAsync` and applies
     `row with { Description = dynamicDescription }`.
   - Added helper methods: `BuildInvokePlaybookDescriptionAsync` (cache + query + render),
     `LoadTenantAccessiblePlaybooksAsync` (mirrors ChatEndpoints pattern),
     `RenderInvokePlaybookDescription` (NFR-10 budget renderer, internal static for tests),
     `FormatPlaybookEntry` (per-entry formatter with 120-char trim + newline collapse),
     `BuildEmptyPlaybookDescription` (empty-state copy).
   - Added 3 internal constants: `InvokePlaybookDescriptionCacheKeyPrefix`,
     `InvokePlaybookDescriptionCacheTtl`, `InvokePlaybookDescriptionBudgetChars`.

2. `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/InvokePlaybookDescriptionTests.cs` (NEW)
   - 14 tests covering rendering, NFR-10 budget, ADR-014 cache contract, NFR-14 tenant
     isolation, ADR-015 telemetry contract, empty-list path.

---

## Coordination Notes

Task 025 (`SessionSummarizeOrchestrator` refactor + `IPlaybookExecutionEngine` extension)
is running in parallel and has modified:

- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookExecutionEngine.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs`

The Sprk.Bff.Api build is GREEN as of this task's completion (task 025 has implemented the
new interface method on the concrete). However, 9 OLDER test files reference removed
public constants / changed constructor signatures from `SessionSummarizeOrchestrator` and
`PlaybookExecutionEngine`. Those test updates are part of task 025's deliverables, not
task 022. Once task 025 lands, the full test project builds + all 3637+ baseline tests
plus my 14 new tests run together.

No file overlap between task 022 and task 025 — parallel-safe per the task prompts.

---

## Acceptance Criteria Verification

| Criterion | Status |
|---|---|
| `invoke_playbook` tool's description dynamically populated at chat-agent build time | ✅ — override gate in `ResolveTools` data-driven block |
| List includes name + description (FR-23) | ✅ — `RenderInvokePlaybookDescription` formats `{id}: {name} — {description}` |
| Respects NFR-10 budget (truncation/paging) | ✅ — 1500-char soft cap with "...and N more" suffix; per-entry 120-char trim |
| Empty list → "no playbooks available" | ✅ — `BuildEmptyPlaybookDescription` explicit copy |
| Cache keyed by `tenantId` per ADR-014 (5-min TTL) | ✅ — `r6:chat-tools:invoke-playbook-description:{tenantId}` |
| Tenant isolation enforced | ✅ — per-tenant key prefix + per-tenant playbook query |
| ADR-015 telemetry: count + tenantId + lengthChars only | ✅ — single Information log line; pure renderer logger-free |
| Unit tests cover 5 paths | ✅ — 14 tests cover empty / small / long / null-desc / multiline / cache / tenant isolation |
| No NuGet additions | ✅ |
| No new top-level DI lines | ✅ |
| BFF publish-size delta reported | ✅ — 44.62 MB (≈0 MB delta vs prior baseline) |
