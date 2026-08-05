# Email Communication Intelligence — R2 - AI Context

> **Purpose**: This file provides context for Claude Code when working on `email-communication-intelligence-r2`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Development (tasks generated; execution not yet started)
- **Last Updated**: 2026-08-05
- **Current Task**: Not started
- **Next Action**: Execute task 001 via `task-execute` (after operator review of TASK-INDEX)

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - AI-optimized spec (24 FRs / 11 NFRs / 5 pillars) — permanent reference
- [`design.md`](design.md) - Human design charter (source)
- [`README.md`](README.md) - Overview + graduation criteria
- [`plan.md`](plan.md) - Phased WBS
- [`current-task.md`](current-task.md) - **Active task state** (context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker + parallel groups + hot-path coordination

### Project Metadata
- **Project Name**: email-communication-intelligence-r2
- **Type**: BFF (.NET 8) + Shared React UI + Outlook add-in + Dataverse schema
- **Complexity**: High (5 pillars, heavily-contended shared surfaces)
- **Hot-path**: BFF=Y, SpaarkeAi=Y, ci=N, skill=N, root-CLAUDE=N

---

## Context Loading Rules

1. **Always load this file first**.
2. **Check current-task.md** for active work state (especially after compaction).
3. **Reference spec.md** for requirements + acceptance criteria; **plan.md** for phase/dependency shape.
4. **Load the relevant task POML** from `tasks/`.
5. **Apply ADRs** relevant to the technologies (auto via adr-aware).

**Context Recovery**: see [Context Recovery Protocol](../../docs/procedures/context-recovery.md).

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" / "keep going" / "next task" | Execute next pending task (next 🔲 in TASK-INDEX.md) |
| "resume task X" / "continue with task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

task-execute ensures knowledge/ADR loading, checkpointing, Step 9.5 quality gates (code-review + adr-check), and recovery. Parallel tasks each still use task-execute (one message, multiple Skill calls) — **but see the hot-path rule below**.

---

## 🚨 Hot-path coordination (BINDING for this project)

This project touches **heavily-contended shared surfaces**. Before EVERY PR that modifies shared code, run `/conflict-check`; mark shared-lib/shared-service writers `parallel-safe:false` (execute them **sequentially, main-session**):

| Shared surface | Contending active worktrees |
|---|---|
| `Spaarke.Communication.Components` (Pillar E UI) | **email-communication-solution-r5** (PRIMARY — owns the components; update its coordination contract, FR-E6) |
| `DataGrid` framework | **spaarke-dataset-grid-framework-r2** (conflict-check before FR-E2 PR) |
| `Services/Communication/**` persist/emit path | messaging-communication-app r1/r2/r3, spaarke-notification-spine-r1, email-communication-solution-r4 |
| `Services/Compose` `CitationResolver` + `Spaarke.Compose.Components` | spaarkeai-compose-r5 / -fidelity-r4.5 (reuse, do NOT fork — NFR-11) |
| `Services/Ai` internals | spaarke-ai-architecture-redesign-r2 (reach AI ONLY via `Services/Ai/PublicContracts/` — NFR-05/ADR-013) |

---

## Execution Model & Tiering (Sonnet-5)

- **Execution** default: **Sonnet 5 @ effort `high`**. Each POML carries `<model-tier>` + `<effort>`.
- **Opus/xhigh** on the correctness/security-critical tasks: **A1a** (HMAC token signing), **C1** (race-proof dedup + alternate key), **C3** (SPE content-hash detector), **D5** (Job C apply/audit), **E-shell+reader** (citation-map integration glue).
- POMLs are authored explicit + literal (exact files, reference impl to copy, closed-set acceptance incl. negative/auth cases).

---

## Key Technical Constraints

- **Extend, never fork** (ADR-045): new capability is additive rungs on the single Association Engine; no revived node-graph engine; regarding writes ONLY via `RegardingFieldMap` (ADR-024).
- **AI facade** (ADR-013/NFR-05): Communication/UI code reaches AI ONLY via `Services/Ai/PublicContracts/` (`IActionSeam`); never inject `IOpenAiClient`/`IPlaybookService`/node executors.
- **BFF hygiene** (§10 / `.claude/constraints/bff-extensions.md`): Placement Justification in PR; publish size **≤60 MB compressed** (baseline ~45.9 MB excl PDBs) + delta reported per BFF task; no new HIGH CVE; unconditional DI + Null-Object for feature-gated services (ADR-010/032); tests updated in `tests/unit/Sprk.Bff.Api.Tests/`.
- **Config** (ADR-018): footer enable+template, affinity toggle, core-set, category→team map are operator-managed app settings via `IOptionsMonitor` (the `AutoFileOptions`/`AutoFileGate` pattern) — no redeploy. HMAC key in **Key Vault** (ADR-028, Path A).
- **Best-effort/non-fatal** (NFR-04): token stamping, dedup, learning, SPE hashing MUST NOT fail capture or send (a throwing rung = non-match).
- **Dedup structural** (NFR-02): message-level dedup enforced by a Dataverse **alternate key**, not app-level check-then-insert alone.
- **Association precedes proposals** (NFR-10): Job B/C proposals actionable only after the record is confirmed; re-scope on override.
- **One reader, exact citations** (NFR-11): single normalized reader over email body + attachment contents; reuse Compose `CitationResolver` (no second citation mechanism).
- **UI** (ADR-050/021/022/012): reconcile modals = `SprkModal` `FormModal` presets; Fluent v9 semantic tokens + dark-mode; **React-version-agnostic** (dual-use on PCF form mount); shared lib is host-injected/context-agnostic.
- **Schema/solution** (ADR-027): new column/alternate-key/affinity store/seed land in the managed solution.
- **Testing** (ADR-038): seam tests under KEEP paths; golden regression; ban `Mock<HttpMessageHandler>`/DI-registration/ctor-null tests.

### ADR Tensions (accepted at pipeline Step 1.7)
- **ADR-028 → Path A**: HMAC signing secret + rotation in Key Vault; footer on send path (no new auth flow).
- **ADR-040 → Path A**: affinity store is new filing-history state, distinct from session ledger (ADR-040) + participant index (ADR-048).
- **ADR-024 / §10-BFF / ADR-007 → Path C** (comply).

---

## Decisions Made

- **2026-08-05 — SPE dedup gate-after-write.** Read `quickXorHash` from the driveItem metadata *after* upload; reconcile + notify (never silently suppress a document); accept a brief transient blob. Retires spikes 001/002. — Why: removes the post-upload-timing unknown; matches the spec's "notify, never silent" UX.
- **2026-08-05 — SPE Tier-2 deferred out of R2.** Exact-hash Tier-1 only; near-dup is a follow-up.
- **2026-08-05 — FR-E5 task fields = Path B ("add fields").** Create via `IActionSeam.CreateTaskAsync`, then PATCH status/completed-date/base-date/final-due-date via impersonated `UpdateRecordAsync` under the same audit; **add `base-date` + `final-due-date` as new task-entity fields** (schema step in task 034). — Why: keeps the AI facade unchanged (ADR-013) while delivering the full FR-E5 field set structured in R2.
- **2026-08-05 — Backfill forward-only** for the RAG-grounding fix (D1) + canonicalhash column (C3). No historical reprocessing.
- **2026-08-05 — Browse shell = `BrowseModal` preset** (`@spaarke/ui-components`, `SprkModal/presets`; ADR-050 / MODAL-DESIGN-SYSTEM / MODAL-DECISION-CRITERIA), not `RecordNavigationModalShell`.

---

## Implementation Notes

- **FR-A3 is already implemented** in `ThreadContinuityRung` — the work is formalize + regression test (folds into FR-D3), not new rung code.
- **TrackingTokenRung reuses `RungKind.ExplicitReference`** → zero `AssociationStatusMapper` change. `RecipientAliasRung`/`AffinityRung` likely need new `RungKind` members + explicit mapper eligibility.
- **FR-D5 Job C apply = sibling of `CommunicationProposalApplyService`** (caller-impersonated, apply-time re-validation, citation re-verify, single append-only audit row, RFC-7807).
- **create-task queue-feed discriminator is net-new** (`QueueFeedItemKinds` has only `association-exception` + `pending-proposal` today).
- **quickXorHash** (`driveItem.file.hashes`) — `sha256Hash` is DEPRECATED on SPE.
- FR-D1 in-scope `ParentEntity: null` sites: `IncomingCommunicationProcessor` L1031 + `CommunicationEnrichmentService` L388.
- Add `bccRecipients` to the capture Graph `$select` (`IncomingCommunicationProcessor` L228-234) + thread through `GraphMessageNormalizer`.

---

## Deferrals & Issues — tracking obligation

Track deferred work + newly-discovered issues in BOTH `notes/defer-issues.md` (source of truth) AND GitHub Issues (visibility). Invoke `/project-defer-issue-tracking` (alias `/defer`) — writes both in one step. Every entry must name a concrete behavior/contract that fails without the work (§11). `push-to-github` blocks push on unfiled entries.

---

## Resources

### Applicable ADRs
045, 024, 018, 013, 028, 007, 010, 032, 015, 027, 038, 004, 048, 040 *(backend)*; 050, 012, 021, 022 *(UI)*.

### Related Projects
- `email-communication-intelligence-r1` (R1 engine + golden UAT emails + coordination note)
- `email-communication-solution-r5` (shell + shared components + BINDING coordination contract)
- `sdap-file-duplication-detector-r1` (absorbed SPE dedup design + 2 spikes)
- `spaarke-dataset-grid-framework-r2` (DataGrid conflict-check)

### External Documentation
- `docs/architecture/communication-intelligence-architecture.md`, `communication-service-architecture.md`, `office-outlook-teams-integration-architecture.md`, `COMPOSE-READ-REFERENCE-FIDELITY.md`, `SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`
- `docs/standards/MODAL-DESIGN-SYSTEM.md`, `.claude/constraints/bff-extensions.md`, `.claude/agent-memory/researcher/spe-dedup-content-identity-2026-07.md`

---

*Keep this file updated throughout the project lifecycle.*
