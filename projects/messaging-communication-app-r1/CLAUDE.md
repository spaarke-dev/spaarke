# Messaging Communication App R1 — AI Context

> **Purpose**: Context for Claude Code when working on messaging-communication-app-r1.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Implementation — ready for Wave 0 (task 001)
- **Last Updated**: 2026-07-16
- **Current Task**: None active (pipeline complete)
- **Next Action**: Run `task-execute` on task 001 to begin Wave 0

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI spec (18 FRs, 8 NFRs) — implementation source of truth
- [`design.md`](design.md) — Human design (rev 2) — rationale, current-state truth, hot-path declaration
- [`plan.md`](plan.md) — Wave WBS + critical path + discovered resources
- [`current-task.md`](current-task.md) — Active task state (context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — All tasks + status + parallel groups + dependency graph

### Project Metadata
- **Type**: Mixed — server (C#) ACS integration / channel provider / ingestor / thread resolver + Dataverse schema + client (TS) polling timeline component + PCFs + ADR authoring
- **Complexity**: High (net-new ACS transport + inbound seam + thread model; edits shared `Services/Communication/`; security-sensitive privacy layer)
- **Branch**: `work/messaging-communication-app-r1`
- **Builds on (complete)**: `email-communication-solution-r4` — ADR-045 channel seams shipped
- **Coordinates with**: `spaarke-notification-spine-r1` (messaging is its R2 consumer)

---

## 🚨 MANDATORY: Task Execution Protocol

All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

| User Says | Required Action |
|---|---|
| "work on task X" | Execute task X via task-execute |
| "continue" / "next task" | Read `TASK-INDEX.md`, find first 🔲, invoke task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load `current-task.md`, invoke task-execute |

**Sub-Agent Write Boundary**: Sub-agents CANNOT write to `.claude/` paths. Tasks touching `.claude/` are `parallel-safe: false` and run from the main session. Affected: **007** (ADR-046). See root CLAUDE.md §3.

**Max concurrency**: 6 agents per wave. Dispatch each task's subagent with its `<model-tier>` + `<effort>` (default `sonnet` @ `high`).

---

## 🚨 BINDING: Hot-Path Coordination (shared `Services/Communication/`)

This project **edits shared `Services/Communication/` code** that `email-communication-solution-r4` shipped (the `IThreadResolver` extension to `ThreadContinuityRung` + `CommunicationService`, task 040). `spaarke-notification-spine-r1` also touches this area — messaging is its R2 consumer.

- **Run `/conflict-check` at project start and before every BFF wave.**
- **Task 040 is the shared-path edit**: characterization-test the existing email inbound/outbound flows and keep them green BEFORE extending. It is `parallel-safe: false` within W4 (serializes the shared edit).
- Align the **`threadId` contract + `kind` taxonomy** with `notification-spine-r1` at joint intake (`communication-assessed` / `communication-arrived`). **Not an R1 blocker** — R1 polls; the contract binds messaging R2.

---

## 🚨 BINDING: BFF Hygiene (root CLAUDE.md §10)

This project heavily touches `Sprk.Bff.Api`. For every BFF-touching task:
1. Load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) before designing the addition.
2. State the **Placement Justification** in the PR/design (even when "in BFF") — see [`plan.md`](plan.md) §3.
3. Use the `Services/Ai/PublicContracts/` facade for any CRUD→AI need; do NOT inject AI-internal types directly (ADR-013).
4. **Verify publish-size** on every BFF task: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`; report absolute + delta vs **~45.30 MB** baseline (post-R4). Ceiling ≤60 MB; ≥+5 MB single-task → justify; ≥55 MB → architecture review; ≥60 MB → HARD STOP. ACS BFF SDKs are thin (§8.9 design).
5. Verify no new HIGH CVE (`dotnet list package --vulnerable --include-transitive`).
6. Update tests in `tests/unit/Sprk.Bff.Api.Tests/`; feature-gated services use ADR-032 Null-Object + unconditional endpoint registration.

---

## Key Technical Constraints (from spec — binding)

- ✅ MUST implement messaging over the shipped **ADR-045** seams (sender/archiver + net-new ingestor); dispatch by `sprk_communicationtype`.
- ✅ MUST persist **every** message as `sprk_communication`; ACS is transport only (Dataverse is the record).
- ✅ MUST mint ACS tokens **server-side only**; clients hold no ACS admin capability; **no client-side ACS SDK in R1** (NFR-04).
- ✅ MUST derive open-thread membership from `MembershipResolverService` (ADR-034); private via existing per-record sharing grant; ACS membership is a reconciled **projection** of Dataverse access.
- ✅ MUST extend the ADR-024 regarding family for thread anchoring (no second mechanism).
- ✅ MUST keep enrichment + thread assignment **best-effort and non-fatal** (NFR-02).
- ✅ MUST make capture **idempotent** — dedupe on ACS message id (NFR-03; echo-dedup).
- ✅ MUST inject central `TokenCredential` + canonical Dataverse interfaces (ADR-028); MUST NOT `new` a credential / `ConfidentialClientApplication`.
- ✅ MUST measure BFF publish size per BFF-touching task (NFR-01).
- ❌ MUST NOT use Activities / OOB email / portal comments / native Teams chat.
- ❌ MUST NOT introduce a live client / ACS client SDK / ACS composites in R1.
- ❌ MUST NOT let AI decide privilege (flag only — ADR-015).
- ❌ MUST NOT build a messaging-only notification hub (R1 polls; R2 consumes `notification-spine-r1`).

---

## ADR Tensions (spec §6.5 — on the record)

| ADR | Rule | Path | Resolution |
|---|---|---|---|
| **ADR-026** | "Code Pages are default for new UI" | **A (exception)** | R1's surface is the **OOB main form + PCFs** (mirrors email-r4's shipped W4 pivot). Timeline PCF is form-bound → within ADR-006. Cite in PRs touching the UI surface. |
| **ADR-045** | Outbound persistence | **C (comply)** | Follow ADR-045 rule 3: outbound **persist-on-send** + direction-symmetric enrichment. Not a tension. |

New surface (ACS transport, thread model, ingestor seam) is codified in **ADR-046** (task 007), not tensioned against an existing ADR.

---

## Phase 0 — Verify Before Build (design §12)

Wave 0 spikes de-risk the net-new areas. Do NOT skip:
1. Live `sprk_communication` schema (which columns exist vs must add) + `Message=100000004` choice integer — **task 001**.
2. Private-thread membership mechanism (`GrantAccess` vs `sprk_externalrecordaccess`) — **task 002** (security-sensitive; the one open architectural question).
3. ACS spike: thread + server-minted token + Event Grid round-trip; latency, echo-dedup, publish-size — **task 003** (gates W1/W2).

---

## Decisions Made
*Appended by `task-execute` during execution.*

## Implementation Notes
*No notes yet — first task starts in Wave 0.*
