# Current Task State — R6 (Wave C-G5 closeout — task 068 complete)

> **Last Updated**: 2026-06-18 (Wave C-G5 closeout)
> **Mode**: Wave C-G5 (Pillar 7 MatterMemoryService activation + shared budget tracker) — closed
> **Branch**: `work/spaarke-ai-platform-unification-r6`

---

## Wave C-G5 closeout summary

| Task | Status | Title | Tests | Evidence note |
|------|--------|-------|-------|---------------|
| 068 | ✅ | MatterMemoryService activation + shared token budget tracker (D-C-21/22) | 26 / 26 PromptBudget + 1177 / 12 skipped / 0 failed broader Chat/Memory regression | [task-068-evidence.md](notes/task-068-evidence.md) |

**Build status**: BFF clean (0 errors, 16 pre-existing warnings).
**Publish-size**: 44.71 MB compressed (no delta vs task-067 44.71 MB baseline; tracker + activation add no NuGet deps).
**CVE**: no new vulnerabilities; pre-existing Kiota Abstractions 1.21.2 HIGH unchanged.
**MatterMemoryService.cs invariant**: `git diff` empty — service implementation UNTOUCHED.

**Wave C-G5 status**: 1 of 1 tasks closed. Pillar 7 activation is now live; budget tracker is the shared accounting surface for tasks 069 (remember/forget/always recognition) and 070 (Q7 Pinned Memory UI).

## What Wave C-G5 produced

### Sub-task 1 — `MatterMemoryService` activation (D-C-21 / FR-45)

`IMatterMemoryService.ToSystemPromptFragmentAsync(tenantId, matterId, ct)` is now wired into `PlaybookChatContextProvider.GetContextAsync` via a NEW `AppendMatterMemoryAsync` helper. Called in BOTH the generic-chat path (step 7 — after entity enrichment) and the playbook path (step 5b — after entity enrichment). Activation guards:
- Host context must identify `EntityType == "matter"` with non-empty `EntityId` (the matterId).
- Tenant required for Cosmos partition key.
- Service must be wired (constructor `IMatterMemoryService? matterMemoryService = null` for back-compat).
- Soft-fail on any exception path; `OperationCanceledException` re-raised.

**Production `MatterMemoryService.cs` UNCHANGED** — verified by `git diff` returning empty per acceptance criterion. The activation is purely additive at the call site (`PlaybookChatContextProvider`); the service's internal 500-token render budget + confidence filtering + ETag concurrency posture all apply unchanged.

### Sub-task 2 — Shared `IPromptBudgetTracker` (D-C-22 / FR-46)

NEW per-turn shared 8K system-prompt budget tracker (`IPromptBudgetTracker` + `PromptBudgetTracker`). Surface:
- `int TotalBudget { get; }` — clamped to [1024, 32_000]; default 8K from `MemoryCompositionOptions.TotalTokenBudget` (same physical 8K ceiling per NFR-10).
- `int UsedBudget { get; }` — monotonically non-decreasing within a turn.
- `int Remaining { get; }` — never negative.
- `bool TryReserve(string layer, int requestedTokens, Guid? sessionId, string? tenantId)` — all-or-nothing per-layer reservation; emits `[ADR-015][memory.prompt_budget_truncated]` log + counter on denial, `[ADR-015][memory.prompt_budget_granted]` log + counter on success. ADR-015 BINDING: meter tag set is `{layer, decision, sessionId, tenantId}` — NO user-content fields.

**Lifetime**: Scoped (one tracker per chat turn). Singleton would leak budget across requests.

**Meter name**: `Sprk.Bff.Api.Ai.PromptBudget`.
**Counter names**: `memory.prompt_budget_truncated`, `memory.prompt_budget_granted`.
**Decision enum**: `granted` / `truncated` / `noop`.

### 4 subsystem wiring sites

| Subsystem | File | Wiring approach |
|---|---|---|
| **Factory (system-prompt blocks)** | `SprkChatAgentFactory.cs` | Resolves tracker via `scope.ServiceProvider.GetService<IPromptBudgetTracker>()`; wired to 3 sites: Active Capabilities, Session Files manifest, Workspace State block. New static helper `TryReservePromptBudget(tracker, layer, fragment, sessionId, tenantId)` for consistent accounting. |
| **Context provider (knowledge + skills + entity + matter-memory)** | `PlaybookChatContextProvider.cs` | Constructor-injected as optional dep. Wired to 3 sites: `EnrichSystemPrompt` (knowledge-inline + skill-instructions), `AppendEntityEnrichment` (entity-enrichment), `AppendMatterMemoryAsync` (matter-memory — the new D-C-21 site). |
| **Document context** | `DocumentContextService` (no change) | Has its own 30K budget for `DocumentSummary` — physically separate from the 8K system-prompt budget; no tracker wire-in required this task. (NOTE: this is the architectural decision per the existing 30K DocumentContext + 8K SystemPrompt + ~40K history + ~50K response split documented in `DocumentContextService` XML doc.) |
| **Memory composition** | `MemoryCompositionService` (no change) | Has its own internal 8K-budget arithmetic + 4-layer drop priority from task 067. The PromptBudgetTracker's budget reads from `MemoryCompositionOptions.TotalTokenBudget` so the two budgets share the SAME 8K physical ceiling per NFR-10. |

### Telemetry pattern

Mirrors the `IContextEventEmitter` (task 063) BCL pattern — `System.Diagnostics.Metrics.Meter` + `Counter<long>` + `ILogger` with `[ADR-015]`-prefixed structured-log entries. The PromptBudgetTracker emits its own meter (NOT a new method on `IContextEventEmitter`) because the IContextEventEmitter contract is structurally constrained to 6 specific `context.*` event types per ADR-015 (the interface signatures don't fit budget-truncation telemetry). The decision to define a separate meter — `Sprk.Bff.Api.Ai.PromptBudget` vs `Sprk.Bff.Api.Ai.ContextEvents` — keeps both contracts simple and ADR-015-compliant.

## Reminders for resume

- Task 069 (C-G14 — "remember / forget / always" recognition via CapabilityRouter) is now unblocked.
- Task 070 (C-G15 — Q7 expansion: Pinned Memory CRUD + visualization UI) is gated on 069.
- Tasks 063 + 053 + 054 + 055 + 056 cumulative + 067 + 068 mean Pillar 6c + Pillar 7 are jointly in a near-complete state; Pillar 9 (widget-visibility-contract) and Pillar 8 (command-router) remain the major Phase C/D items.
- The `IPromptBudgetTracker` surface is CONTRACT — Pillar 7 downstream consumers (069, 070) will not add new layer tags without explicit sign-off. The layer-tag list in `IPromptBudgetTracker.cs` XML doc is the canonical inventory.

See [`notes/task-068-evidence.md`](notes/task-068-evidence.md) for the full acceptance-criteria verification matrix + 4-subsystem wiring map + governance audit.
