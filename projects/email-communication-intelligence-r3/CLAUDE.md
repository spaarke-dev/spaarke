# Email Communication Intelligence — R3 - AI Context

> **Purpose**: Context for Claude Code when working on `email-communication-intelligence-r3`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Charter DRAFT (design.md written; awaiting owner review of §5 gates).
- **Last Updated**: 2026-09-07
- **Next Action**: Owner review of [`design.md`](design.md) §5 open gates → `/design-to-spec` → `/project-pipeline`.

---

## Quick Reference

### Key Files
- [`design.md`](design.md) — human design charter (source of scope). **Read first.**
- [`README.md`](README.md) — overview + graduation criteria + explicit non-scope.
- `spec.md` / `plan.md` / `tasks/` — not yet generated (`/design-to-spec` → `/project-pipeline`).

### Project Metadata
- **Type**: BFF (.NET 10) engine rung + AI facade + eval harness + Shared React UI wiring.
- **Hot-path**: BFF=Y (engine rung + `PublicContracts` facade), SpaarkeAi=Y (reconciliation host wiring), ci=N, skill=N, root-CLAUDE=N.

---

## Scope in one screen (full detail in design.md)

| WS | What | ADR posture |
|---|---|---|
| **S — R3-SIGNAL-1** | Split ranking from auto-file eligibility in FR-12 so "related to X" ranks high but never auto-files | ADR-045/FR-12 change — **surface per §6.5** (Path A refinement; preserves the auto-file invariant). **Gate on G4; never tune the constant by feel.** |
| **G4 — eval harness** | Golden labeled set (`tests/integration/seam/AssociationGolden/`), per-rung precision/recall runner, "no silent override" invariant seam test | none. Report is **observation-only, never a CI gate** (ADR-038). De-identify fixtures (ADR-015). |
| **G5 — learned matching** | Party/relationship graph (deterministic read-model) + `RungKind.LearnedLinkage` suggest-only scorer via a new `Services/Ai/PublicContracts/` facade | **No amendment.** ADR-013 = facade-routed; ADR-045 = suggest-tier only, **never auto-files**. |
| **C — card real-names** | Wire host `resolveDisplayName(entity,id)` through the two reconciliation hosts so GUID-only cards show real names | none. Shared-lib stays host-injected/context-agnostic (ADR-012). |
| **D — email-metadata facets** | OPEN owner Y/N — structured from/to/thread/date facets in `spaarke-files-index` (needs schema migration + reindex) | none |

---

## Key Constraints (BINDING)

- **Extend, never fork** (ADR-045/024): additive rung (`LearnedLinkage`) + a party-graph read-model + an FR-12 ranking/auto-file split. No new engine, no revived node graph, regarding writes ONLY via `RegardingFieldMap`.
- **AI/ML never auto-files** (ADR-045): the learned scorer and semantic rungs emit **suggest-tier only**, joining noisy-OR like `SemanticMatch`. Deterministic signals own auto-file. The ONLY amendment case (learned auto-file) is **out of scope**.
- **AI facade** (ADR-013/NFR): reach AI/ML ONLY via `Services/Ai/PublicContracts/`; never inject `IOpenAiClient`/`IPlaybookService`/node executors into the engine.
- **Measurement precedes tuning**: no confidence/threshold/cap change (Workstream S) lands without the G4 harness showing the before/after on the golden set.
- **BFF hygiene** (§10 / `.claude/constraints/bff-extensions.md`): Placement Justification in the PR; publish-size delta reported per BFF task (≤60 MB); no new HIGH CVE; unconditional DI + kill-switch (ADR-018/032) for the feature-gated scorer; tests in `tests/`.
- **Config** (ADR-018): learned-scorer kill-switch is per-tenant operator config via `IOptionsMonitor` — no redeploy.
- **Testing** (ADR-038): golden set under KEEP `tests/integration/seam/**`; observation-only report; ban `Mock<HttpMessageHandler>`/DI-registration/ctor-null tests.

---

## 🚨 Task Execution Protocol

All task work MUST use `task-execute` (do NOT read POML files directly). Applies once `/project-pipeline` generates tasks.

| User Says | Required Action |
|---|---|
| "work on task X" | Execute task X via task-execute |
| "continue" / "next task" | Execute next 🔲 in TASK-INDEX.md |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

## Hot-path coordination (BINDING)

`/conflict-check` before EVERY PR touching `Services/Communication/Engine/**`, `Services/Ai/PublicContracts/`, or `Spaarke.Communication.Components`. Mark shared-lib/engine writers `parallel-safe:false` (execute sequentially, main-session). `email-communication-solution-r5` is CLOSED (code on master) → shared-lib contention is low, but still check `projects/INDEX.md`.

---

## Related Projects
- `email-communication-intelligence-r2` (parent — shipped the engine hardening, dedup, reconciliation surface, and R3-CARD-1/2; its `notes/` hold the r3 source scope).
- `email-communication-intelligence-r1` (R1 engine + golden UAT emails).

## External Documentation
- `docs/architecture/communication-intelligence-architecture.md` §3–§7 (13-rung engine — update when `LearnedLinkage` + party graph land).
- `.claude/constraints/bff-extensions.md`, `docs/adr/ADR-038-testing-strategy.md`, ADR-013 / ADR-045 / ADR-024 / ADR-018 / ADR-015 / ADR-012.

---

*Keep this file updated throughout the project lifecycle.*
