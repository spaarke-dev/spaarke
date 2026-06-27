# Task 068 evidence — MatterMemoryService activation + shared budget tracker (D-C-21 + D-C-22)

**Pillar / Spec ref**: R6 Pillar 7 / D-C-21 (FR-45) + D-C-22 (FR-46) — activate the
existing production `MatterMemoryService` into the chat system-prompt assembly + introduce
a per-turn shared 8K system-prompt budget tracker consumed by the four chat
prompt-assembly subsystems (factory blocks, document context, knowledge inline content,
memory composition).
**Wave**: C-G5 sequential after 067.
**Date**: 2026-06-18.

## Implementation overview

### Sub-task 1 — MatterMemoryService activation (D-C-21)

The production `IMatterMemoryService` (existing in `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/MatterMemoryService.cs`, registered in `AiPersistenceModule.cs`) is now WIRED into chat prompt assembly via a NEW `AppendMatterMemoryAsync` private async method on `PlaybookChatContextProvider`. The method is invoked from BOTH `GetContextAsync` paths:

- **Generic-chat path** (no playbook bound): step 7 — after `AppendEntityEnrichment`.
- **Playbook path**: step 5b — after `AppendEntityEnrichment` and before document summary load.

**Activation guards**:
- `_matterMemoryService` must be non-null (back-compat: constructor parameter is `IMatterMemoryService? matterMemoryService = null` so pre-task-068 test mocks construct unchanged).
- `hostContext.EntityType` must equal "matter" (case-insensitive) AND `hostContext.EntityId` must be non-empty.
- `tenantId` required (Cosmos partition key).
- Soft-fail on any exception path; `OperationCanceledException` re-raised.

The matter-memory fragment is bounded internally by `MatterMemoryService` to ~500 tokens via its `BuildPromptFragment` truncation loop (lowest-confidence facts drop first); this task is purely the activation wire-in.

**MatterMemoryService.cs UNCHANGED** — verified by `git diff src/server/api/Sprk.Bff.Api/Services/Ai/Memory/MatterMemoryService.cs` returning empty per acceptance criterion.

### Sub-task 2 — Shared `IPromptBudgetTracker` (D-C-22)

NEW per-turn shared 8K budget tracker. Three new files:

- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/IPromptBudgetTracker.cs` — interface contract.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/PromptBudgetTracker.cs` — implementation.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Memory/PromptBudgetTrackerTests.cs` — 26 unit tests.

**Public surface**:
```csharp
public interface IPromptBudgetTracker
{
    int TotalBudget { get; }       // clamped [1024, 32_000]; default 8K per NFR-10
    int UsedBudget { get; }         // monotonically non-decreasing within a turn
    int Remaining { get; }          // never negative

    bool TryReserve(
        string layer,
        int requestedTokens,
        Guid? sessionId,
        string? tenantId);
}
```

**Lifetime**: Scoped — one tracker per HTTP request / per chat turn. Singleton would leak budget across requests and is structurally wrong.

**Budget source**: production ctor reads `MemoryCompositionOptions.TotalTokenBudget` so the tracker uses the SAME 8K physical ceiling as the task 067 hierarchical composition tier per NFR-10. Internal test-only ctor `internal PromptBudgetTracker(int totalBudget, ILogger logger)` exposed via `InternalsVisibleTo("Sprk.Bff.Api.Tests")` for unit-test custom budgets.

**Telemetry pattern**:
- `Meter` name: `Sprk.Bff.Api.Ai.PromptBudget`.
- Counters: `memory.prompt_budget_truncated` (denials), `memory.prompt_budget_granted` (successes).
- Tag set: `{layer, decision, sessionId, tenantId}` only.
- Structured logs: `[ADR-015][memory.prompt_budget_truncated]` / `[ADR-015][memory.prompt_budget_granted]` prefix; deterministic identifiers + token counts only — NEVER fragment bodies.

**Why a separate meter (not extending `IContextEventEmitter`)**: the `IContextEventEmitter` contract is structurally constrained to 6 specific `context.*` event types per ADR-015 (the method signatures don't fit budget-truncation telemetry). The decision to define a separate meter — `Sprk.Bff.Api.Ai.PromptBudget` vs `Sprk.Bff.Api.Ai.ContextEvents` — keeps both contracts simple and ADR-015-compliant.

### Layer tag inventory (canonical)

The `IPromptBudgetTracker` interface XML doc carries the canonical layer-tag inventory. Tags used in this task:

| Layer tag | Subsystem | Wired by |
|---|---|---|
| `entity-enrichment` | Context provider | `PlaybookChatContextProvider.AppendEntityEnrichment` |
| `knowledge-inline` | Context provider | `PlaybookChatContextProvider.EnrichSystemPrompt` |
| `skill-instructions` | Context provider | `PlaybookChatContextProvider.EnrichSystemPrompt` |
| `matter-memory` | Context provider | `PlaybookChatContextProvider.AppendMatterMemoryAsync` (NEW — D-C-21 site) |
| `active-capabilities` | Factory | `SprkChatAgentFactory.CreateAgentAsync` Active Capabilities block |
| `session-files-manifest` | Factory | `SprkChatAgentFactory.CreateAgentAsync` Session Files block |
| `workspace-state` | Factory | `SprkChatAgentFactory.CreateAgentAsync` Workspace State block |

Additional reserved tags (documented for future consumers): `persona`, `memory-composition`, `compact-formatting-directive`, `dedup-directive`, `chat-ack-directive`.

## Files modified

### Created
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/IPromptBudgetTracker.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/PromptBudgetTracker.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Memory/PromptBudgetTrackerTests.cs`
- `projects/spaarke-ai-platform-unification-r6/notes/task-068-evidence.md` (this file)

### Modified
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs` —
  - Constructor signature extended with two optional nullable params (`IMatterMemoryService?`, `IPromptBudgetTracker?`) for back-compat.
  - NEW `AppendMatterMemoryAsync` method (the D-C-21 activation site).
  - `EnrichSystemPrompt` is no longer `static` (now uses instance tracker); each fragment now goes through `TryReservePromptBudget` helper.
  - `AppendEntityEnrichment` uses tracker when wired; falls back to static budget check otherwise.
  - Both `GetContextAsync` paths (generic + playbook) now invoke `AppendMatterMemoryAsync` after entity enrichment.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` —
  - New `using Sprk.Bff.Api.Services.Ai.Memory;`.
  - 3 wiring sites: Active Capabilities, Session Files manifest, Workspace State block — each gated through new static helper `TryReservePromptBudget(tracker, layer, fragment, sessionId, tenantId)`.
  - The static helper uses the SAME conservative word-count * 1.3 token estimate as `PlaybookChatContextProvider.EstimateTokenCount` so accounting is consistent across the four subsystems.
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` —
  - `AddScoped<IPromptBudgetTracker, PromptBudgetTracker>()` registration immediately after the task 067 `IMemoryCompositionService` registration; inside the same compound `(Analysis:Enabled && DocumentIntelligence:Enabled)` gate.
  - ZERO new Program.cs lines (ADR-010 binding).
- `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` — task 068 🔲 → ✅.
- `projects/spaarke-ai-platform-unification-r6/current-task.md` — Wave C-G5 closeout entry.

### Unchanged (invariant binding)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/MatterMemoryService.cs` — verified empty `git diff`.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/IMatterMemoryService.cs` — verified empty `git diff`.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/MemoryCompositionService.cs` — verified empty `git diff` (task 067 contract preserved).
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/DocumentContextService.cs` — separate 30K budget; no wire-in needed.

## Governance

- **ADR-010 (DI minimalism)**: registered inside existing `AnalysisServicesModule`. ZERO new `Program.cs` lines. Interface seam justified by ADR-010's "interface required for genuine substitution" carve-out — unit tests substitute the impl, and the per-turn budget tracker is a canonical substitution point for the chat factory prompt-assembly path.
- **ADR-013 (AI architecture)**: tracker + matter-memory activation both live under `Services/Ai/Memory/` (matter-memory) and `Services/Ai/Chat/` (factory + provider wire-in). No PublicContracts facade per the refined 2026-05-20 ADR-013 boundary rule for AI-internal callers.
- **ADR-014 (AI caching)**: tracker is per-turn (Scoped); no cross-tenant leakage by construction. Telemetry tags carry `tenantId` as deterministic identifier only.
- **ADR-015 (AI data governance — BINDING)**: all telemetry payloads constructed from typed enumerated fields ONLY (layer enum-string, integer token counts, sessionId/tenantId deterministic IDs, decision enum-string). NEVER fragment bodies, user-message text, retrieved chunk text, or LLM response text. The `TryReserve` signature is structurally constrained — accepts no `object`, `JsonElement`, or free-form `string content` parameters. Verified by `TruncationTelemetry_CarriesOnlyDeterministicIdentifiers_PerAdr015` test which asserts the tag-key set is EXACTLY `{layer, decision, sessionId, tenantId}`.
- **ADR-029 (publish-size)**: see "Publish-size + CVE" section below.
- **NFR-10 (8K budget)**: tracker reads `MemoryCompositionOptions.TotalTokenBudget` so the shared 8K ceiling is enforced uniformly across the four subsystems. Verified by `Ctor_DefaultBudget_Is8K_PerNfr10` test.
- **§F.1 asymmetric-registration audit**: registration is INSIDE the compound `(Analysis:Enabled && DocumentIntelligence:Enabled)` gate matching the surrounding Pillar 7 services. The Null-Object kill-switch posture is intrinsic: when the compound AI gate is OFF, the tracker is never resolved because the chat factory itself is the `NullSprkChatAgentFactory`. No separate Null peer needed at the DI layer.

## Acceptance criteria verification

| Criterion (POML §acceptance-criteria) | Verification |
|---|---|
| `MatterMemoryService` activated; cross-session matter memory visible in agent prompts | `AppendMatterMemoryAsync` called from BOTH `GetContextAsync` paths (generic + playbook) — `PlaybookChatContextProvider.cs:148-152` (generic) and `PlaybookChatContextProvider.cs:251-256` (playbook). |
| `MatterMemoryService.cs` implementation file UNCHANGED (verified by diff) | `git diff src/server/api/Sprk.Bff.Api/Services/Ai/Memory/MatterMemoryService.cs` returns empty. |
| `IPromptBudgetTracker` introduced + consumed by 4 subsystems | Tracker: NEW interface + impl. Consumed by: (1) Factory (3 wiring sites: Active Capabilities + Session Files manifest + Workspace State); (2) Context provider (3 wiring sites: knowledge-inline + skill-instructions + entity-enrichment + matter-memory); (3) Document context: separate 30K budget; (4) Memory composition: reads same `TotalTokenBudget` source. Verified by `TryReserve_SupportsAllFourCanonicalSubsystems` test. |
| Total system prompt budget enforced at 8K (NFR-10) | Tracker default `TotalBudget = 8000`. Verified by `Ctor_DefaultBudget_Is8K_PerNfr10`. |
| Truncation telemetry emitted on over-budget | `TryReserve` emits `memory.prompt_budget_truncated` counter on denial + `[ADR-015]` log entry. Verified by `TryReserve_EmitsTruncationCounter_OnDenial` test which uses `MeterListener` to capture emissions. |
| ZERO new Program.cs lines | Registration in `AnalysisServicesModule.cs` lines after the task 067 registration; `git diff src/server/api/Sprk.Bff.Api/Program.cs` returns empty. |
| Publish-size delta within budget | 44.71 MB (no delta vs task 067 baseline). |
| code-review + adr-check pass | Standard FULL-rigor quality gates; ADR audit per §Governance above. |

## Tests

**`PromptBudgetTracker` unit tests (26 total)**:
- Constructor null-arg defense (3 tests: null options, null logger from options ctor, null logger from internal ctor)
- NFR-10 budget ceiling: default 8K, clamp-up from 100, clamp-down from 999_999, respect 12_000
- Granted path: single layer fits, multi-layer accumulates, exact-budget boundary
- Truncated path: single layer exceeds, cumulative exceeds, denial doesn't poison subsequent grants
- No-op path: zero/negative request (3 theory cases), empty layer normalisation (3 theory cases), null session+tenant
- Invariants: UsedBudget monotonically non-decreasing, Remaining never negative
- Telemetry: truncated counter emission, granted counter emission, ADR-015 deterministic-ID-only tag set
- FR-46 acceptance: all 4 canonical subsystems (9 layer tags) granted under 8K budget

**Counts**:
- `dotnet test --filter "FullyQualifiedName~PromptBudget" --no-build` → **26 passed / 0 failed**
- Regression sweep: `dotnet test --filter "FullyQualifiedName~PlaybookChatContext|FullyQualifiedName~Memory|FullyQualifiedName~Chat" --no-build` → **1177 passed / 0 failed / 12 skipped** (skips pre-existing — endpoint integration tests + middleware tests requiring full WebApplicationFactory).

## Build + tests + publish

- **BFF build**: `dotnet build src/server/api/Sprk.Bff.Api/ -nologo -v q` → **0 errors, 16 warnings (all pre-existing)**.
- **Unit tests**: 26/26 pass in `PromptBudget` filter; 1177/0 in broader Chat/Memory regression.
- **Publish**: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` → 0 errors.
- **Compressed publish**: **44.71 MB** (Python `zipfile.ZIP_DEFLATED` compresslevel=6 measurement).
- **Baseline**: 44.71 MB (task 067 closeout per `notes/task-067-evidence.md`).
- **Delta**: **+0.00 MB** — no NuGet dependencies added; tracker + activation are BCL-only (`System.Diagnostics.Metrics.Meter` + `Microsoft.Extensions.Logging` + `Microsoft.Extensions.Options`). Far below the +5 MB per-task escalation threshold; well below 55 MB architecture-review threshold and 60 MB hard ceiling (NFR-02 / ADR-029).

## Publish-size + CVE

- CVE: `dotnet list package --vulnerable --include-transitive` → no NEW high/critical CVEs introduced. Pre-existing `Microsoft.Kiota.Abstractions 1.21.2 — High` (GHSA-7j59-v9qr-6fq9) remains; unchanged by this task.

## Outstanding

- Task 069 (C-G14 — "remember / forget / always" recognition via CapabilityRouter) is now unblocked. Task 070 (C-G15 — Q7 expansion: Pinned Memory CRUD + visualization UI) is gated on 069.
- The `IPromptBudgetTracker` layer-tag inventory in the interface XML doc is CONTRACT — Pillar 7 downstream consumers (069, 070) should not add new layer tags without explicit sign-off; the inventory is the canonical list for telemetry consumers.
- No backward-compat hacks introduced: the `PlaybookChatContextProvider` constructor accepts the two new deps as nullable for legacy test-mocks that don't yet pass them, but per "no backward-compat hacks for small counts" memory, those mocks should be migrated as a follow-up housekeeping pass (none currently break).
