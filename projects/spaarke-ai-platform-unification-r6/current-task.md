# Current Task State — R6 (Wave C-G4 closeout — task 067 complete)

> **Last Updated**: 2026-06-18 (Wave C-G4 closeout)
> **Mode**: Wave C-G4 (Pillar 7 hierarchical memory composition) — closed
> **Branch**: `work/spaarke-ai-platform-unification-r6`

---

## Wave C-G4 closeout summary

| Task | Status | Title | Tests | Evidence note |
|------|--------|-------|-------|---------------|
| 067 | ✅ | Hierarchical memory composition (D-C-20) | 34 / 34 MemoryComposition + 91 / 91 broader Memory regression | [task-067-evidence.md](notes/task-067-evidence.md) |

**Build status**: BFF clean (0 errors, 16 pre-existing warnings).
**Publish-size**: 44.71 MB compressed (+0.03 MB vs 44.68 MB baseline).
**CVE**: no new vulnerabilities; pre-existing Kiota Abstractions 1.21.2 HIGH unchanged.

**Wave C-G4 status**: 1 of 1 tasks closed. Pillar 7 composition surface is now
the canonical integration point for task 068 (SprkChatAgentFactory wiring).

## What Wave C-G4 produced (binding contract for task 068)

`IMemoryCompositionService.ComposeAsync(request, ct) → MemoryComposition`:
- **4 tagged layers**: `RecentVerbatim` / `CompressedMid` / `RetrievedOld` /
  `Pinned` (dict keyed by `PinType`).
- **Budget enforcement**: `TotalTokenBudget` default 8K per NFR-10; drop
  priority retrieved-old → compressed-mid → recent oldest-first; **pinned NEVER
  dropped** (FR-42 invariant).
- **Soft-failure posture**: kill switch off → `MemoryComposition.Empty`;
  per-primitive failures degrade gracefully; `OperationCanceledException`
  re-raised.
- **DI**: `AddScoped<IMemoryCompositionService, MemoryCompositionService>()` in
  `AnalysisServicesModule.AddAnalysisServices` (ZERO new Program.cs lines per
  ADR-010).

See [`notes/task-067-evidence.md`](notes/task-067-evidence.md) for the full
acceptance-criteria verification matrix + governance audit.

## Reminders for resume

- Task 068 (C-G13 — `MatterMemoryService` activation + shared token budget
  tracker) is now unblocked. It is the integration call site that wires the
  composition output into `SprkChatAgentFactory.CreateAgentAsync`'s per-turn
  system-prompt assembly.
- Task 063 (Wave C-G3 outstanding — context.* event emission) is still pending;
  see [`notes/task-063-partial-evidence.md`](notes/task-063-partial-evidence.md)
  for the handoff brief. Independent of Pillar 7; can be dispatched in parallel
  with 068 if desired.
- The 4 layers of composition are CONTRACT — Pillar 7 downstream consumers
  (task 068, task 069 "remember/forget/always" recognition, task 070 Q7 Pinned
  Memory UI) all read these fields. Drift here breaks 069/070; sign-off
  required before any field rename / removal.
