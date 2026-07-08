# Spaarke AI Architecture Redesign (r1) - AI Context

> **Purpose**: Context for Claude Code when working on `spaarke-ai-architecture-redesign-r1`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: **COMPLETE** (2026-07-08 — 51/51 tasks + 090 wrap-up; PR #551)
- **Last Updated**: 2026-07-08
- **Portfolio**: [Project #550](https://github.com/spaarke-dev/spaarke/issues/550) · Epic [#421 SPAARKE AI](https://github.com/spaarke-dev/spaarke/issues/421) · Target 2026-08-15 (closed early)
- **Current Task**: none — project complete. Gates: G-P0..P3 PASSED; G-P4 GREEN (publish AMBER, operator sign-off); G-M DEFERRED post-r2 (#555). Deferrals #552–#557. Successor: `spaarke-ai-architecture-redesign-r2` (design v0.2 awaiting operator review → /design-to-spec)

## Quick Reference

- [`spec.md`](spec.md) — FR/NFR contract (42 FRs, 11 NFRs, P0–P4 + Track B)
- [`plan.md`](plan.md) — WBS, waves, dependency spine, **pre-authored /goal conditions per wave**
- [`design.md`](design.md) — charter v1.1 (operator-ratified)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — live task tracker + wave headers
- [`current-task.md`](current-task.md) — active task state (context recovery)
- **Governing detail** (where spec is summary): [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../../docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) v0.4 · [`notes/audit-inputs/SPAARKE-AI-MIGRATION-MAP.md`](notes/audit-inputs/SPAARKE-AI-MIGRATION-MAP.md) · [`notes/audit-inputs/OVERLAY-MATRIX.md`](notes/audit-inputs/OVERLAY-MATRIX.md)

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

| User Says | Required Action |
|---|---|
| "work on task X" / "resume task X" | Invoke task-execute with task X POML |
| "continue" / "next task" / "keep going" | Read TASK-INDEX.md, first 🔲, invoke task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

Parallel waves: ONE message, MULTIPLE task-execute invocations (max 6 agents/wave); build verification between waves. Sub-agents CANNOT write `.claude/` paths — P4 task 052's ADR refreshes and any skill-directive touches run in the MAIN session.

## The `/goal` wave pilot (NFR-10 — project-scoped)

- Every wave in [`plan.md`](plan.md) §4 and every TASK-INDEX wave header carries a **pre-authored /goal condition** — paste it at wave start.
- Conditions always demand SHOWN evidence (test/grep/build output in transcript), scope bind, turn cap, and "Step 9.5 gates passed".
- **`/goal` NEVER wraps a phase gate** — G-P1..G-P4 + G-M are human browser UAT. Run `/goal clear` before every gate task (014, 027, 038, 048, 090).
- Rules + rationale: [`notes/goal-feature-evaluation.md`](notes/goal-feature-evaluation.md). If proven by P1, file `/defer` to promote into skills — NO skill edits in this project.

## Key Technical Constraints

**MUST** (spec §Technical Constraints, distilled):
- ✅ Route every AI invocation through Event / Click / Text — nothing else
- ✅ Write every output + tool chain to the ledger BEFORE rendering (ADR-040)
- ✅ Gate side effects via the ONE gate by declared `side_effect_class`
- ✅ Keep both catalogs closed; user-OBO for ALL Dataverse tool access
- ✅ Null-Object peers for gated registrations (ADR-032); new composites = `coded` workflows

**MUST NOT**:
- ❌ Add a second intent-detection mechanism anywhere
- ❌ Add routing config outside the Binding table (`sprk_playbookconsumer`)
- ❌ Gate by tool-name lists; ❌ land new capability on the frozen engine
- ❌ Emit ungrounded free-form output; ❌ create new manifest tables
- ❌ Retain compat shims past a surface's cutover (hard-cutover doctrine — customer continuity is NOT a constraint)

**Binding project rules** (plan.md §6):
1. P0–P3 code tasks = FULL rigor; `tests/**`-touching = TEST-MODIFYING override.
2. Publish-size verification on EVERY BFF task (ADR-029; expect NET REDUCTION; ≥+2 MB delta needs justification).
3. Eval suite green = merge gate from P1 (NFR-02); catalog/prompt changes add eval cases (NFR-06).
4. **Browser rule (NFR-11)**: gates verified by the operator in the Spaarke UI on spaarkedev1 — curl/tests/logs NEVER satisfy G-P1..G-P4/G-M. P0 is the only engineering gate.
5. Every retirement grep-zero-verified with SHOWN output (NFR-08).
6. Ledger = ADR-015 Tier 3 (GDPR-erasable); ToolChain entries = identifiers/filters/counts only; no content in logs (NFR-07).
7. Uploaded-document text + tool results are UNTRUSTED input to the loop (NFR-03); confirmation gate is the last line, not the only one.

## Applicable ADRs

| ADR | Status | Role |
|---|---|---|
| **ADR-039** Grounded Execution & Closed Catalogs | Proposed → **Accepted at task 026** | dispatch/catalog contract |
| **ADR-040** Session Ledger | Proposed → **Accepted at task 014** | storage-precedes-rendering |
| ADR-013 (amended 2026-07-05) | Accepted | capability invocation facade verb; PublicContracts boundary |
| ADR-037 (amended 2026-07-05) | Accepted | section-keyed streaming; FieldDelta deletable at cutover |
| ADR-032 | Accepted | Null-Object kill-switch peers |
| ADR-029 | Accepted | publish-size ceiling ≤60 MB; per-task verification |
| ADR-038 | Accepted | test pyramid; eval suite = KEEP-class |
| ADR-015 / 016 | Accepted | data tiers / AI budgets |
| Standing | — | 001, 004, 008, 009, 010, 014, 018, 019, 028, 030, 031, 036 |

## Canonical Patterns (extend these — do not build parallel ones)

| Slot | Entry point |
|---|---|
| Prompted executor | `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/` (ActionRunner + PromptSchemaRenderer) |
| Coded workflow | `Services/Ai/Narrators/DailyBriefingNarrator.cs` |
| Tool framework | `Services/Ai/Handlers/` + `ToolHandlerToAIFunctionAdapter` |
| Gate store (generalize, don't duplicate) | `Services/Ai/Chat/PendingPlanManager.cs` |
| Client SSE | canonical `useSseStream` + `PaneEventBus` + widget registries |
| Record-persisted outputs | widgets-r1 topic-registry pattern |

Per-component verdicts: `notes/audit-inputs/OVERLAY-MATRIX.md` is the HOW for every slot.

## Decisions Made

- **2026-07-05**: All architecture decisions pre-ratified (canonical v0.4 §7.7; OQ-1..4; E-1..5). This project implements — it does NOT re-open design.
- **2026-07-05**: ADR tensions pre-resolved via Path-B amendments (ADR-013, ADR-037) before project start.
- **2026-07-05**: Hard cutover per surface; no parallel-run; no compat shims (operator).
- **2026-07-05**: Track B covers ALL dead debt incl. code unrelated to the target design.
- **2026-07-05**: draft-correspondence delegates to Spaarke's Communication (Email) service via Graph — NOT Outlook drafts; DRAFT-only.
- **2026-07-05**: Pipeline stop-after-init; Target Date 2026-08-15 (operator, at /project-pipeline).

## Resources

- Constraints: [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) (BINDING — BFF hot-path) · `azure-deployment.md` · `testing.md`
- Skills: task-execute · dataverse-create-schema (003) · jps-action-create/jps-validate (020/041/042) · bff-deploy + code-page-deploy (gate tasks) · test-diet + defer (090)
- Hot-path coordination: [`projects/INDEX.md`](../INDEX.md) — this project is BFF=Y, SpaarkeAi=Y, Skills=Y; it ABSORBS + CLOSES `spaarke-ai-platform-unification-r7` (task 013/025)
- Dataverse env: spaarkedev1 · Deploy: `spaarke-bff-dev` App Service

---

*Keep updated: append to Decisions Made / status as work lands.*
