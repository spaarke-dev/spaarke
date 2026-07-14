# Current Task — email-communication-solution-r4

> **Purpose**: Active task state tracker for context recovery. Reset by `task-execute` on each task transition.

---

## Active Task

- **Task**: none active — W0-A complete (001, 002, 005 ✅)
- **Status**: ready for W0 remainder
- **Wave**: W0 Foundation → next: W0-B (003, 004) + W0-C (006, 007); W2 task 020 also unblocked (needs 001✅)
- **Next Action**: Run the next W0 wave. Available (deps satisfied): **003, 004** (need 001✅), **006** (needs 001✅+005✅), **007** (needs 005✅). 006/007 are BFF → `/conflict-check` before PR.

## Completed (W0-A, this session)
- ✅ **005** — ADR-045 authored (both forms + INDEXes).
- ✅ **001** — schema pass on `sprk_communication` (live spaarkedev1): created `sprk_inreplyto`, `sprk_associationprovenance`, `sprk_regardingservicerequest`; widened `sprk_internetmessageid` 100→1000 (owner-approved); confirmed `sprk_receiveddate` + `sprk_associationstatus`. Data-model doc updated (§1.2 drift closed). **Solution-export for prod promotion = deploy-time follow-up (ADR-027), not done here.**
- ✅ **002** — added `sprk_associationstatus` values **Suggested (100000003)** + **Ambiguous (100000004)**; avoided the `100000002` collision (legacy "Unresolved"). Reconciliation documented (Unresolved→Pending Review).

### Decisions
- 2026-07-14: Widened `sprk_internetmessageid` (owner-approved) — long RFC-2822 Message-IDs would truncate at 100 in the W1 thread rung. Columns came out at NVARCHAR(1000) (≥ spec 255; fine).
- 2026-07-14: Retain legacy `Unresolved (100000002)`; R4 engine (task 015) treats it as `Pending Review` (no data migration).

### FYIs for later tasks (DO NOT lose)
- **Task 003**: `sprk_servicerequest` has reverse lookup `sprk_regardingcommunication → sprk_communication`; forward lookup now added — relationship is bidirectional (note in schema doc + wire `RegardingLookupMap`/catalog/priority).
- **Task 004**: `sprk_communication.sprk_regardingorganization` **already targets `sprk_organization`** — 004's "correct org target" is about the sender-domain MATCH CODE writing `account`, NOT this column.
- **Task 015**: implement the `Unresolved`→`Pending Review` equivalence.

### FYIs surfaced (for later tasks — DO NOT lose)
- **Task 002**: `sprk_associationstatus` option `100000002` is ALREADY "Unresolved" — DEC-5's tentative Suggested/Ambiguous integers collide. Use `100000003/4`. Also reconcile legacy "Unresolved" vs R4 "Pending Review" semantics (002/015).
- **Task 003**: `sprk_servicerequest` already has reverse lookup `sprk_regardingcommunication → sprk_communication`. Forward `sprk_communication.sprk_regardingservicerequest` is still correct (ADR-024 write-on-communication) but the relationship is bidirectional — note in the schema doc.
- Existing regarding lookups on `sprk_communication` confirmed: matter/project/invoice/analysis/budget/**organization (sprk_organization ✓)**/person/workassignment. Note: org already targets `sprk_organization` here — task 004's "correct org target" fix is about the sender-domain MATCH CODE writing `account`, not this column.

## Completed this session
- ✅ **005** — ADR-045 authored (`.claude/adr/` + `docs/adr/` + both INDEXes). Root CLAUDE.md NOT edited (reachable via §17→INDEX; keeps root-claude-md hot-path = N). Full `/adr-check` deferred to W0 PR gate.
- Model-tier bumps applied: 010/011/051/052 → opus (architectural/high-blast-radius per §8.5).

## Parallel Execution
- Active group: none
- Agents in flight: none

## Recovery Notes
- Project initialized via `/design-to-spec` → `/project-pipeline` on 2026-07-14.
- W0 blocks all waves. W1‖W2 run in parallel after W0. **W5 is gated on task 050 (r2-core coordination).**
- Before any BFF PR: run `/conflict-check` (Services/Ai ownership — see CLAUDE.md).
