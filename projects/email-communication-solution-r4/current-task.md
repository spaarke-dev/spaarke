# Current Task — email-communication-solution-r4

> **Purpose**: Active task state tracker for context recovery. Reset by `task-execute` on each task transition.

---

## Active Task

- **Task**: 001 — `sprk_communication` schema pass
- **Status**: in-progress (discovery done; live mutation pending — CHECKPOINTED before mutating)
- **Wave**: W0 Foundation (W0-A)
- **Env**: spaarkedev1 (pac active profile [3])
- **Next Action**: Execute the 4 schema ops below via `dataverse-create-schema` (Web API MetadataService + PowerShell; pac token). Read `.claude/skills/dataverse-create-schema/SKILL.md` first.

## Progress (task 001)
- ✅ Step 1–3 (discovery, read-only): described live `sprk_communication` + `sprk_servicerequest`.
- ⏳ Step 4 (mutation): NOT YET RUN — 4 ops below.

### EXACT execution plan (decision-locked)
Live `sprk_communication` verified 2026-07-14. Ops:
1. **WIDEN** `sprk_internetmessageid`: currently `NVARCHAR(100)` → set MaxLength **255** + `IsSearchable=true` (indexed). *(Owner decision: widen, not keep — non-destructive.)* Verify current IsSearchable first.
2. **CREATE** `sprk_inreplyto`: String, MaxLength 255, `IsSearchable=true`. (Holds parent Internet-Message-Id string — NOT a lookup.)
3. **CREATE** `sprk_associationprovenance`: Memo (multiline text) — JSON provenance.
4. **CREATE** `sprk_regardingservicerequest`: Lookup → `sprk_servicerequest` (target table CONFIRMED exists).
Then: re-`describe` to verify all 6; update `docs/data-model/sprk_communication.md` (close §1.2 drift); solution export if added; mark 001 ✅.

### Decisions this task
- 2026-07-14: Widen `sprk_internetmessageid` 100→255 + index (owner-approved) rather than keep-at-100 — long RFC-2822 Message-IDs would truncate in the W1 thread rung.

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
