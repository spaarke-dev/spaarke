# Email Communication Intelligence — R1 (Phase 1) — AI Context

> **Purpose**: Context for Claude Code when working on `email-communication-intelligence-r1`.
> **Always load this file first** when working on any task in this project.
> **Status (2026-07-28)**: PLANNING — plan authored; task POMLs **not yet generated**.

---

## What this project is

r1 is the **intelligence and record-currency layer** over Spaarke's shipped communication engine (r4). It **activates the already-produced-but-dark AI classification**, computes a real **RI-confidence** score (fixing the hardcoded-0 gap that leaves the notification path inert), extends deterministic **email→record association** to all 7 core record types, and makes matched records **current from email** (human-confirmed, cited, audited field updates — **Job B**).

**Builds on (shipped, merged to master)**: `email-communication-solution-r4` (`Services/Communication/**` engine + enrichment) · `spaarke-notification-spine-r1` (RI action delivery) · `spaarke-ai-architecture-redesign-r2` (`Services/Ai/PublicContracts/` — **complete; no live owner to route through — the `projects/INDEX.md` "sole owner" row is stale**) · `email-communication-solution-r5` (**complete; owns all review/reading UI**).

r1 builds **no UI** — it *feeds* r5's shipped surfaces via a feed + apply contract (`notes/email-intelligence-r1-coordination.md`).

---

## Project Status

- **Phase**: Planning — task POMLs pending plan approval.
- **Branch**: `work/email-communication-intelligence-r1`
- **Current Task**: None active.
- **Next Action**: generate task POMLs (`/task-create`), then `work on task 001`.

### Key Files
- [`spec.md`](spec.md) — AI spec (17 FRs, 8 NFRs) — implementation source of truth
- [`design.md`](design.md) — design charter; **§0 authoritative** (code-directed reconciliation)
- [`plan.md`](plan.md) — phase/wave WBS + discovered resources + critical path
- [`current-task.md`](current-task.md) — active task state (context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — all tasks + parallel groups + dependency graph
- [`notes/email-intelligence-r1-coordination.md`](notes/email-intelligence-r1-coordination.md) — r5 feed/apply contract

---

## Owner decisions (all LOCKED 2026-07-28 — design §0.11)

| Ref | Decision |
|---|---|
| D-01 | Triage unit = **`sprk_communication`** — no new entity |
| D-02/D-04 | Legacy OOB `email`-activity stack — **moot (retired by r4)**; build only on `sprk_communication`/`.eml` |
| D-03 | Category taxonomy + priority weights — **Dataverse-tuneable**, starter set seeded (FR-16) |
| D-05 | Triage-outcome term = **"review outcome"** (avoids ADR-040 "disposition" clash) |
| D-06 | Obligation storage = **lean JSON** on `sprk_communication` |
| D-07 | Mailbox coverage = **shared/central mailbox** (FR-15; already supported by shipped code — 051a). **M365 group mailbox DEFERRED** (owner 2026-07-29, §6.5 path A — forked capture pipeline + tenant-wide `Group.Read.All` not justified now; see `notes/050-mailbox-capture-spike.md`). |
| D-08 | Priority scorer = **email-specific** (triage urgency + RI-confidence); do NOT reuse Workspace/Portfolio scoring |
| D-10 | Surface placement = r5's (dual-use); **r1 feeds, builds no widget** |
| D-11 | AI in P1 = **Triage Action + RAG grounding**; SprkChat-over-mail → P2 |
| D-12/D-13 | **IP Auto-Docketing REMOVED entirely** — no docketing entity, cascade engine, or IP playbook |
| C-1 | Auto-file only on **rung 0 + rung 1**; rung 2/3 → `Suggested` |
| Job B | **FULL** (propose → confirm → apply → audit), not propose-only |
| C-3 | Exceptions Queue — **r5 builds the surface; r1 supplies the feed only** |
| C-4 | Job B allow-list home = **new `sprk_emailupdatefield` table** (operator creates; r1 reads) |

---

## Key constraints (binding)

- **Code-directed Action + Binding only** — the **node-graph playbook engine is FROZEN** (Insights family only; ADR-039). MUST NOT land new capability on `PlaybookOrchestrationService` / `UpdateRecordNodeExecutor` / `IInvokePlaybookAi` (deleted). New capability = catalog data (`sprk_analysisaction` + `sprk_playbookconsumer`) + `coded`/`prompted` Action reached via `Services/Ai/PublicContracts/`.
- **Auto-file C-1, rung 0+1 only** — narrow `AssociationStatusMapper`/`AutoFileGate`; rung 2 (participant) + rung 3 (structural) → `Suggested`. Bare-numeric identifiers never auto-file alone (need reinforcement); multi-entity → `Ambiguous`; AI-tier never auto-files.
- **Job B is FULL** — propose → **human-confirm** → apply via `IActionSeam.UpdateRecordAsync` under **Dataverse impersonation** (`MSCRMCallerID` = confirming user's `systemuserid`; owner Option 2, 2026-07-29 — no OBO token exchange needed since the apply is user-initiated; gives native `modifiedby` = the human + intersection privileges; see `notes/031-write-identity-decision.md`) → `sprk_emailreviewlog` audit row. Allow-listed fields only (`sprk_emailupdatefield`); every proposal cited + confidence; verify cited text exists (NFR-06); nothing deadline-bearing/privilege-adjacent auto-finalizes (ADR-015).
- **IP docketing OUT** — removed entirely; do not add any docketing/deadline-cascade surface.
- **Surfaces owned by completed r5** — r1 builds no UI; feeds r5 via feed + apply endpoints (C-3).
- **AI facade discipline (NFR-03 / ADR-013)** — no `IOpenAiClient`/`IPlaybookService` injected into Communication code; reach AI only via `Services/Ai/PublicContracts/`.
- **Best-effort / non-fatal (NFR-04 / ADR-045 NFR-06)** — triage / RI-confidence / proposal generation MUST NOT fail the capture or send path.
- **Extend, never fork** — one Association Engine (add one rung), one regarding mechanism (ADR-045 / ADR-024). Numbering schemes stay **out of code** (value-based match; roster from `sprk_recordtype_ref`).
- **"related-to" ≠ "regarding"** — represent the FR-12 relationship distinctly without a second regarding mechanism (ADR-024 path-A exception).

---

## Applicable ADRs

**ADR-045** (communication architecture — extend, never fork) · **ADR-024** (polymorphic regarding; resolver-only write) · **ADR-039** (grounded execution / closed catalogs / Action + Binding) · **ADR-013** (BFF AI facade; PublicContracts only) · **ADR-040** (session ledger; owns "disposition") · **ADR-041 / ADR-043 / ADR-047** (judgment gate / execution spine / notification spine — **Proposed, in-flight**; pin to current shape) · **ADR-015** (privilege flagged, never decided) · **ADR-018 / ADR-016 / ADR-014** (kill-switch / caching / budgets) · **ADR-028** (auth — app-only background, OBO writes) · **ADR-004 / ADR-036** (job contract) · **ADR-038** (testing — seam tests DoD).

Full-doc paths in [`plan.md`](plan.md) §2.

---

## Existing code to reuse (canonical files — extend, don't rebuild)

All under `src/server/api/Sprk.Bff.Api/` unless noted.

- **Rung pattern**: `Services/Communication/Engine/Rungs/ExplicitReferenceRung.cs` + `ThreadContinuityRung.cs` + `IAssociationRung.cs`
- **Regarding write map**: `Services/Communication/Engine/RegardingFieldMap.cs` *(add `sprk_reportcard`→`sprk_regardingreportcard`)*
- **Auto-file C-1**: `Services/Communication/Engine/AssociationStatusMapper.cs` + `AutoFileGate.cs`
- **Enrichment / RI emit**: `Services/Communication/CommunicationEnrichmentService.cs` (`RunAssessmentEmissionAsync` ~L238)
- **RI action path**: `Services/Communication/ICommunicationAssessedProducer.cs` + `CommunicationRuleGate.cs` + `CommunicationRiActionService.cs`
- **Classification substrate**: `Services/Communication/Engine/Rungs/AiClassificationRung.cs` + `Models/Ai/Communication/CommunicationClassificationResult.cs`
- **Write cores**: `Services/Ai/PublicContracts/IActionSeam.cs` + `Services/Ai/Nodes/ActionCore/UpdateRecordActionCore.cs` + `TaskActionCore.cs`
- **AI facade**: `Services/Ai/PublicContracts/ICommunicationClassificationAi.cs`
- **Endpoints**: `Api/CommunicationEndpoints.cs` *(extend the `suggest-associations` family)*
- **Capture (FR-15)**: `Services/Communication/GraphSubscriptionManager.cs`
- **Binding reference**: `infra/dataverse/sprk_playbookconsumer-rows.json` + `infra/dataverse/inputschemas/create-task-v1.input.schema.json`

---

## 🚨 MANDATORY: Task Execution Protocol

All task work MUST use the `task-execute` skill (root CLAUDE.md §4). DO NOT read POML files directly and implement manually.

| User Says | Required Action |
|---|---|
| "work on task X" | Execute task X via `task-execute` |
| "continue" / "next task" | Read `TASK-INDEX.md`, find first 🔲, invoke `task-execute` |
| "resume task X" | Execute task X via `task-execute` |
| "pick up where we left off" | Load `current-task.md`, invoke `task-execute` |

**Sub-Agent Write Boundary** (root §3): sub-agents CANNOT write to `.claude/` paths. Any task touching `.claude/` is `parallel-safe: false` and runs from the main session.

**Model tiers (§8.5)**: default **sonnet @ high**. **opus** on 020, 030, 031, 041, 042, 050. **effort xhigh** on 020, 042. Schema/config **sonnet @ medium**; 001/061/090 low/MINIMAL.

---

## 🚨 BINDING: Hot-Path Coordination (shared `Services/Communication/` + `Services/Ai/PublicContracts/`)

- **Run `/conflict-check` at project start and before EVERY BFF PR.** r1 edits shared `Services/Communication/` (identifier rung, `RegardingFieldMap`, `AssociationStatusMapper`/`AutoFileGate`, enrichment, endpoints) and consumes `Services/Ai/PublicContracts/` seams.
- **BFF writers to shared `Services/Communication/` are `parallel-safe: false` among each other** — never run two concurrently. Characterization-test existing email/messaging flows and keep them green BEFORE extending.
- **ADR-041/043/047 are Proposed / in-flight** — pin to current shape; check the r2/successor charters before building against the gate or seams.

## 🚨 BINDING: BFF Hygiene (root CLAUDE.md §10)

For every BFF-touching task:
1. Load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) before designing.
2. State **Placement Justification** in the PR (even when "in BFF") — see [`plan.md`](plan.md) §3.
3. Use `Services/Ai/PublicContracts/` facades for any CRUD→AI need — never inject `IOpenAiClient`/`IPlaybookService`.
4. **Verify publish-size**: report absolute + delta vs **~49.63 MB** baseline (incl. PDBs). Ceiling ≤60 MB; ≥+5 MB single-task → justify; ≥55 MB → architecture review; ≥60 MB → HARD STOP. Triage adds no new heavy dep → expected delta ≈0.
5. Verify no new HIGH CVE (`dotnet list package --vulnerable --include-transitive`).
6. Update tests in `tests/unit/Sprk.Bff.Api.Tests/` + seam tests (ADR-038); feature-gated services use ADR-032 Null-Object + unconditional endpoint registration.
7. Every new Action/Binding adds a golden-utterance eval case (NFR-07 — blocking merge gate).

---

## Phase 0 — Verify Before Build

Task **001** (read-only) verifies operator-created inputs exist in `spaarkedev1`: `sprk_emailupdatefield` (Job B allow-list), `sprk_regardingreportcard` on `sprk_communication`, `sprk_recordtype_ref` RPTC row + `sprk_reportcardnumber`; and checks `sprk_recordtype_ref` `sprk_regardingfield` typos + `contact`-row anomaly. Do NOT skip — grounds the identifier rung (020) + gates Job B (Phase 3).

---

## Decisions Made
*Appended by `task-execute` during execution.*

## Implementation Notes
*No notes yet — planning phase.*
