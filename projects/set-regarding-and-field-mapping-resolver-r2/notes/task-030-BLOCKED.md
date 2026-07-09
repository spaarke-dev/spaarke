# Task 030 — PARTIAL COMPLETION + BLOCKED (ESCALATION on Matter→Report Card only)

> ## ✅ RESOLVED 2026-07-09 (main session, owner-approved Path C→A)
> The missing prerequisite was created. `sprk_recordtype_ref` row `5bc206a0-587b-f111-ab0e-7ced8ddc4a05` ("Report Card") + profile `b2915dad-587b-f111-ab0e-7ced8ddc4a05` (Matter→Report Card) + 8 Copy rules now exist and are verified. Task 030 is COMPLETE. See `task-030-notes.md` §8 for the resolution detail. The analysis below is retained for the record.
>
> **Verdict (original)**: ESCALATION (partial) — cleanup + Matter→Event + Matter→Invoice completed and verified; **Matter→Report Card blocked** on a missing prerequisite reference-data record discovered during describe-verification.
> **Date**: 2026-07-09
> **Rigor**: STANDARD · model-tier sonnet · effort high

## What's blocked, and why

`sprk_fieldmappingprofile.sprk_targetrecordtype` (and `sprk_sourcerecordtype`) is a **LOOKUP to `sprk_recordtype_ref`** — a profile record cannot be created without a valid `sprk_recordtype_ref` record for the target entity.

I queried `sprk_recordtype_ref` in full (12 active records, no filter applied that could hide an inactive one):

```
Account, Analysis, Budget, Document, Event, Invoice, Matter, Organization, Person, Project, To Do, Work Assignment
```

**There is no `sprk_recordtype_ref` record for `sprk_reportcard` (Report Card) at all** — active, inactive, or under any name variant (`report`, `kpi`, `reportcard` all return zero rows). This is not a field-name mismatch (the escalation trigger's literal wording); it's a missing structural prerequisite the task/design assumed already existed. design.md §4.5 / spec.md FR-14 both describe seeding "for the Matter→(Event/Invoice/Report Card) pairs" without flagging that the Report Card recordtype-ref registration itself might not exist yet.

## Why I did not just create the missing `sprk_recordtype_ref` record myself

- `sprk_recordtype_ref` is described in design.md §3 as "authoritative" reference data — "already used by the BFF endpoints, already used throughout this codebase's resolver ecosystem" (ADR-024 pattern). It is cross-cutting: `sprk_regardingfield`, `sprk_regardingrecordnumberfield`, `sprk_recordtypecode` conventions on this table plausibly feed other systems (regarding resolvers, the polymorphic pattern) I have no visibility into from a data-seed task.
- Task 030 is explicitly scoped as **"config DATA (not schema, not code)"** (design.md §4.5) — creating a brand-new reference-data row that other subsystems key off of is a step above "seed the attorney matrix," and I have no verified convention for what `sprk_recordtypecode`/`sprk_regardingfield` should be for Report Card.
- The task's own escalation trigger models exactly this situation in spirit: *"If a target's assigned-resource field name does NOT match the matrix... STOP and reconcile before seeding a guessed mapping."* Guessing at a brand-new authoritative reference record carries materially higher risk than guessing a field name.

## What WAS completed and verified (see task-030-notes.md for full detail)

1. Orphaned empty `sprk_fieldmappingrule` (`d2bc58eb-a779-f111-ab0e-7ced8ddc4a05`) — **deleted**, verified gone.
2. Two stale "SRFR-084 UAT" profiles — **deactivated** (statecode=1/statuscode=2), verified.
3. **Matter → Event** profile (`24dc0ed2-537b-f111-ab0e-7ced8ddc4a05`) + 8 Copy rules — created, active, verified.
4. **Matter → Invoice** profile (`25dc0ed2-537b-f111-ab0e-7ced8ddc4a05`) + 4 Copy rules (attorney1/2, paralegal1/2 renamed) — created, active, verified. Law-firm and external/internal omitted (fields don't exist on Invoice); `sprk_assignedto1/2` omitted (no clean Matter source counterpart) — matches the constraint's own decision rule.

## Resolution paths (choose one)

**Path A (recommended) — Owner/main-session creates the `sprk_recordtype_ref` row for Report Card** (via `dataverse-create-schema` or manual Web API insert), following whatever convention governs `sprk_recordtypecode` / `sprk_regardingfield` / `sprk_regardingrecordnumberfield` elsewhere (e.g. Invoice's row as a template, since Report Card's shape — `sprk_regardingrecordtype`, `sprk_regardingrecordnumber`, `sprk_regardingrecordid` — mirrors Invoice's). Once it exists, re-invoke task 030 (or a resumed sub-step) to seed the Matter→Report Card profile + 8 Copy rules (attorney1/2, paralegal1/2, external, internal identical names; lawfirm1→`sprk_assignedtolawfirm1`, lawfirm2→`sprk_assignedlawfirm2` — all already describe-verified present on `sprk_reportcard`, see task-030-notes.md).

**Path B — Confirm Report Card genuinely isn't registered in the resolver ecosystem yet** (e.g. it's mid-rollout via the unmerged/just-merged Invoice/Report Card wizard branch referenced in design.md decision 5a) and treat the recordtype-ref registration as an explicit prerequisite task for a future project step, deferring Matter→Report Card seeding until then.

**Path C — Reject and reconcile**: if Report Card was always meant to key off a different logical name (e.g. `sprk_kpiassessment`, mentioned in spec.md §"Assumptions" as a registry fallback) rather than `sprk_reportcard`, clarify which logical name the field-mapping framework should actually target, then create the ref row under the corrected name.

I have NOT created a `sprk_recordtype_ref` record, guessed at one, or created a Matter→Report Card profile/rules. Awaiting owner/main-session decision.
