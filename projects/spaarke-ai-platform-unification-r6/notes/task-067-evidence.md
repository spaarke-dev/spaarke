# Task 067 evidence — MemoryCompositionService (D-C-20)

**Pillar / Spec ref**: R6 Pillar 7 / D-C-20 / FR-44 — hierarchical memory composition
orchestrating the three Pillar 7 primitives (compression / pinned-context / selective
recall) into a single tagged four-layer memory block consumed by the chat
prompt-assembly path. Enforces the NFR-10 8K total budget with priority-ordered layer
drop; pinned tier is NEVER dropped (FR-42 invariant).
**Wave**: C-G4 sequential after 064/065/066.
**Date**: 2026-06-18.

## Implementation

Added in this task:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/IMemoryCompositionService.cs` —
  contract + `MemoryCompositionRequest` + `MemoryComposition` records.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/MemoryCompositionService.cs` —
  composition orchestrator impl.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/MemoryCompositionOptions.cs` —
  bound via `AddOptions<>().BindConfiguration("MemoryComposition")`.
- DI registration in `Infrastructure/DI/AnalysisServicesModule.cs` (immediately after
  the task 066 registration; inside the same `(Analysis:Enabled &&
  DocumentIntelligence:Enabled)` compound gate).
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Memory/MemoryCompositionServiceTests.cs`
  — 34 unit tests covering all 4 layers + budget paths + FR-42 invariant + soft-fails
  + slicing helpers + cancellation + ctor null-arg defense.

## Key design decisions

- **Four tagged layers** rendered as four record fields on `MemoryComposition`:
  `RecentVerbatim` (last N messages), `CompressedMid` (single System-role summary),
  `RetrievedOld` (top-K similarity-ranked pins), `Pinned` (dictionary keyed by
  `PinType` — ALL pins for the (tenant, user, matter) tuple). Each layer is
  individually present-or-absent so the prompt builder (task 068) can render
  labelled sections in the system prompt.
- **Retrieved-old = pinned-similarity, not chat-turn-similarity**. R6 has no
  independent chat-turn-similarity primitive — the existing
  `IPinnedContextRecallService.RecallAsync` operates over pinned items. Pragmatic
  interpretation of FR-44 "Retrieved old: turns 50+ via similarity recall (from
  task 066)": when the conversation crosses the mid-window end (≥50 turns by
  default), surface the top-K most-similar pins for the current user message as
  the "retrieved older context" tier. Recall acts as a relevance-promoted
  projection over the pinned baseline, not a separate content source. Overlap
  with the pinned tier is intentional (logged for telemetry) — task 068 renders
  retrieved-old BEFORE the unranked pinned block so the LLM sees similarity-
  promoted items first.
- **Pinned aggregation from BOTH user-scope and matter-scope** via two
  `IPinnedContextRepository` calls (`GetByUserAsync` for user-pref / system-rule
  pins; `GetByMatterAsync` for matter-fact pins). Deduplicated by id;
  grouped by `PinType`. Matter scope skipped entirely when `MatterId` is null.
- **Layer drop priority on budget overflow** (NFR-10): retrieved-old →
  compressed-mid → recent-verbatim oldest-first. Pinned NEVER dropped per FR-42.
  When pinned alone exceeds the budget, the service logs a warning and returns
  pinned-only — the chat prompt builder (task 068) owns the final hard guard.
  `DroppedLayers` field on the result carries the priority-ordered list of
  dropped layer tags for telemetry.
- **Soft-failure posture (P2 Quiet)** mirroring the
  `SummarizationCompressionService` (task 064) and `PinnedContextRecallService`
  (task 066) precedents: kill switch off → `MemoryComposition.Empty`; compression
  returns null → that layer omitted, others compose; recall throws → that layer
  omitted; one pin-repo scope throws → other scope still composes.
  `OperationCanceledException` is re-raised.
- **Defensive null-coalescing** on every awaited primitive result
  (`?? Array.Empty<...>()`). Mocks returning default `Task<IReadOnlyList<...>>`
  yield null, so the service guards rather than NRE-ing.
- **Token estimation** is a uniform conservative chars/token estimate (default 4.0,
  matches GPT-4o English-prose tokenisation and the
  `SummarizationCompressionService.CharsPerToken` default). Used uniformly across
  all four layers so the budget arithmetic stays consistent with the upstream
  compression output. Real tokenisation deferred to a follow-up if empirical
  evidence shows the conservative estimate is too lossy.

## Governance

- **ADR-010**: registered as `AddScoped<IMemoryCompositionService,
  MemoryCompositionService>()` inside `AnalysisServicesModule.AddAnalysisServices`
  — ZERO new `Program.cs` lines. Interface seam justified by the "genuine
  substitution" carve-out (task 068 unit tests + the prompt-assembly soft-fail
  seam will substitute the impl).
- **ADR-013**: lives under `Services/Ai/Memory/`. Injects
  `ISummarizationCompressionService`, `IPinnedContextRepository`,
  `IPinnedContextRecallService`, `IOptions<MemoryCompositionOptions>`, `ILogger`
  only — all AI-internal collaborators. NO PublicContracts facade per the refined
  2026-05-20 ADR-013 boundary rule for AI-internal callers.
- **ADR-014 / NFR-16**: `tenantId` flows through to every
  `IPinnedContextRepository` + `IPinnedContextRecallService` call as the Cosmos
  partition key. Verified by
  `ComposeAsync_PassesTenantAndMatterUnchangedToAllPrimitives` test which asserts
  the repo + recall are queried with `tenant-b` / `matter-y` and NEVER with
  `tenant-a`.
- **ADR-015**: chat-message content and pin content are user-authored. Service
  telemetry emits ONLY deterministic identifiers (tenantId, userId, matterId,
  layer-drop names, counts) — NEVER message bodies or pin content bodies.
  Verified by visual code inspection of every `_logger.Log*` call.
- **ADR-029 (publish-size)**: see "Publish-size + CVE" section below.
- **§F.1 asymmetric-registration**: registration is INSIDE the compound
  `(Analysis:Enabled && DocumentIntelligence:Enabled)` gate, matching the
  surrounding Memory services (SummarizationCompressionService,
  PinnedContextRepository, PinnedContextRecallService). The Null-Object
  kill-switch posture is INTRINSIC to the service (returns
  `MemoryComposition.Empty` when `Enabled=false`) so no separate Null peer is
  needed at the DI layer.
- **B-G11 hardening**: `MemoryCompositionOptions` has NO `[Required]` on any
  use-site-conditional field; use-site clamping lives inside `ComposeAsync` (kill
  switch + bounds clamps on `RecentVerbatimTurns`, `MidWindowEnd`,
  `RetrievedOldTopK`, `TotalTokenBudget`, `CompressedMidMaxTokens`). App boots
  clean with no `MemoryComposition` section in appsettings — defaults take over
  (8K budget per NFR-10).

## Service shape

Public API surface:
```csharp
public interface IMemoryCompositionService
{
    Task<MemoryComposition> ComposeAsync(
        MemoryCompositionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record MemoryCompositionRequest(
    string TenantId,           // ADR-014 partition key; required
    string UserId,             // pin-scope owning user; required
    string? MatterId,          // optional; null skips matter-scope + retrieved-old
    IReadOnlyList<ChatMessage> Conversation,
    string CurrentUserMessage);

public sealed record MemoryComposition(
    IReadOnlyList<ChatMessage> RecentVerbatim,
    ChatMessage? CompressedMid,
    IReadOnlyList<PinnedContextItem> RetrievedOld,
    IReadOnlyDictionary<PinType, IReadOnlyList<PinnedContextItem>> Pinned,
    int EstimatedTokenCount,
    IReadOnlyList<string> DroppedLayers);
```

Dependencies (constructor-injected, all AI-internal per ADR-013):
- `ISummarizationCompressionService` (task 064)
- `IPinnedContextRepository` (task 065)
- `IPinnedContextRecallService` (task 066)
- `IOptions<MemoryCompositionOptions>`
- `ILogger<MemoryCompositionService>`

Budget behaviour (verified by tests):
- 1st-to-drop: retrieved-old → 2nd: compressed-mid → 3rd: recent-verbatim
  oldest-first (most-recent always preserved) → pinned NEVER dropped (FR-42).

## Tests

**Layer coverage** (FR-44 acceptance criterion #1 — all 4 layers present):
- `ComposeAsync_ProducesAllFourLayers_WhenInputsSupportAll` asserts all 4 are
  non-empty given a 60-turn conversation + recalled + compressed + user/matter
  pins.
- Per-layer presence + absence cases for recent, compressed, retrieved, pinned.

**Pinned always-present (FR-42 invariant)**:
- `ComposeAsync_PinnedNeverDropped_EvenWhenAloneExceedsBudget` — builds a pin set
  whose aggregate alone exceeds the 1024-token budget; asserts pinned tier
  survives (5 items), `DroppedLayers` does NOT contain "pinned", and
  `EstimatedTokenCount > 1024` (honest accounting for caller hard guard).
- `ComposeAsync_PinnedNeverDropped_WhenAllOtherLayersAreDroppable` — generous
  budget; nothing dropped; all 4 layers present including grouped-by-pinType
  pinned tier.

**Budget paths** (NFR-10 drop priority):
- `ComposeAsync_DropsRetrievedOldFirst_WhenOverBudget` — verifies 1st-priority
  drop.
- `ComposeAsync_DropsCompressedMidSecond_WhenStillOverAfterRetrievedDropped` —
  verifies sequence (`ContainInOrder("retrieved-old", "compressed-mid")`).
- `ComposeAsync_DropsRecentOldestFirst_WhenStillOverAfterRetrievedAndCompressedDropped`
  — verifies recent layer sheds oldest-first; most-recent message always
  preserved.

**Soft-fails**:
- Compression returns null → that layer null; others compose.
- Recall throws → retrieved-old empty; others compose.
- `GetByUserAsync` throws → matter-scope pins still returned.
- All wrapped in try/catch with `OperationCanceledException` re-raised.

**Slicing helpers** (`SelectRecentVerbatim` + `SelectMidWindow` as pure functions):
- Recent: less-than-N, more-than-N, empty.
- Mid: at/below recentN → empty; 20-msg conv → window [0,10); 100-msg conv →
  window [50,90).

**Cancellation propagates** + **constructor null-arg defense** (5 deps × 5 throws).

**Counts**: **34 passed / 0 failed** in `MemoryComposition` filter.
**Broader Memory regression**: **91 passed / 0 failed** across all Memory tests
(covers `MemoryComposition`, `PinnedContextRecall`, `PinnedContextRepository`,
`SummarizationCompression`, `MatterMemory`).

## Build + tests

- BFF build: `dotnet build src/server/api/Sprk.Bff.Api/ -nologo -v q` →
  **0 errors, 16 warnings (all pre-existing)**.
- Unit tests: `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter
  "FullyQualifiedName~MemoryComposition" --no-build` → **34/34 pass**.
- Regression: `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter
  "FullyQualifiedName~Memory" --no-build` → **91/91 pass**.

## Publish-size + CVE

- `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`
  → 0 errors.
- Compressed publish: **44.71 MB** (PowerShell `Compress-Archive` measurement).
- Baseline: 44.68 MB (Wave C-G3 closeout per task brief).
- **Delta: +0.03 MB** — well under the +5 MB per-task escalation threshold; far
  below the 55 MB architecture-review threshold and 60 MB hard ceiling
  (NFR-02 / ADR-029).
- CVE: `dotnet list package --vulnerable --include-transitive` → no NEW high
  /critical CVEs introduced. Pre-existing `Microsoft.Kiota.Abstractions
  1.21.2 — High` (GHSA-7j59-v9qr-6fq9) remains; unchanged by this task.

## Files

### Created
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/IMemoryCompositionService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/MemoryCompositionService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/MemoryCompositionOptions.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Memory/MemoryCompositionServiceTests.cs`
- `projects/spaarke-ai-platform-unification-r6/notes/task-067-evidence.md`
  (this file)

### Modified
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs`
  — `MemoryCompositionService` DI registration immediately after the task 066
  block; inside the same compound `(Analysis:Enabled &&
  DocumentIntelligence:Enabled)` gate.
- `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` — task 067
  🔲 → ✅.
- `projects/spaarke-ai-platform-unification-r6/current-task.md` — Wave C-G4
  closeout entry.

## Outstanding

Task 068 (C-G13, Pillar 7 activation) is now unblocked — it wires
`IMemoryCompositionService` into the `SprkChatAgentFactory` per-turn
prompt-assembly path. The composition contract above is the integration
surface task 068 consumes. No further work required on task 067.
