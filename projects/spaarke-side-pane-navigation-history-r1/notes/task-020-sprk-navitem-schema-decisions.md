# Task 020 — sprk_navitem Schema Authoring: Decisions & Deviations

> Author-only task. No environment mutation performed. Deploy is task 021.

## Escalation trigger — evaluated, did NOT fire

Task 020's escalation trigger: *"If the sprk_todo exemplar's field patterns cannot
represent a data-model field (e.g. `sprk_targetid` as GUID vs Text), STOP and escalate the
type choice rather than guessing."*

**Evaluated and resolved without escalation.** The `sprk_todo` exemplar already establishes
the exact precedent needed: `sprk_regardingrecordid` (String, 100 chars, "normalized GUID of
the regarding record") stores a polymorphic-target record id as text, because the target
entity varies row-to-row and is resolved dynamically (not via a typed `Lookup`/relationship).
`sprk_navitem.sprk_targetid` has the identical shape — the target entity varies per row via
`sprk_targetlogicalname`, and is nullable for non-record targets (lists/weblinks). Dataverse
also has no supported mechanism to create a non-PK `Uniqueidentifier` attribute that behaves
as a real FK into a row-varying target entity. **Decision: `sprk_targetid` is String (Text),
MaxLength 100** — same pattern as `sprk_todo.sprk_regardingrecordid`, not Uniqueidentifier.
Documented in `entity-schema.md` "Type Decision" note and in the deploy script's attribute
description.

The exemplar resolving the exact question is why this is a documented design decision rather
than a genuine escalation — there was no case where the exemplar's patterns could not
represent the field.

## Deviations from mirroring sprk_todo verbatim

1. **Ownership**: `UserOwned`, not "User or Team" — required by the task's own constraint
   (NFR-03 per-user isolation) and explicitly called out as an override in both new files.
2. **Primary name field is `sprk_displayname`, not a separate `sprk_name`**: the spec's
   `sprk_navitem` data model table (spec.md §"Data Model — sprk_navitem (per-user)") lists
   `sprk_displayname` ("resolved or user-supplied label") as the only label-shaped field —
   there is no separate `sprk_name` in the field list. Rather than invent an extra field not
   present in the spec, `sprk_displayname` was designated the primary name attribute
   directly. This differs from `sprk_todo`, which has both `sprk_name` (primary name / card
   title) and other detail fields.
3. **No regarding-lookup / `PolymorphicResolverService` pattern (ADR-024)**: `sprk_todo` uses
   11 specific `Lookup` attributes + 4 resolver fields for its regarding relationship.
   `sprk_navitem` intentionally does NOT use this pattern — its targets include shapes
   ADR-024's lookup-based resolver cannot represent (entity lists, custom pages, raw
   weblinks), and per spec's MUST rule, cached labels must be **security-trimmed at render
   time** against the live target rather than relying on Dataverse relationship security. The
   target is denormalized as a `sprk_targetlogicalname` + `sprk_targetid` text pair instead.
   This is consistent with the "no regarding-lookup" framing already reflected in the spec's
   field list (spec.md line ~131-132) and is noted explicitly in `entity-schema.md`.
4. **Global option set naming**: the three global option sets are named exactly
   `sprk_type`, `sprk_source`, `sprk_pagetype` — identical to their bound attribute logical
   names — per the task prompt's explicit naming. This mirrors the convention already used in
   `Deploy-ChartDefinitionEntity.ps1` (global option set `sprk_visualtype` bound to attribute
   `sprk_visualtype`).
5. **Solution-scoping header**: the deploy script sets `MSCRM.SolutionUniqueName` on every
   Web API request (parameterized as `-SolutionUniqueName`, default `SpaarkeCore`) so that all
   created components (option sets, entity, attributes) are added to the SpaarkeCore
   unmanaged solution per the task's explicit constraint ("target the SpaarkeCore unmanaged
   solution"). `Deploy-SprkTodoEntity.ps1` did not need this because component-to-solution
   association was handled outside that script; other exemplars in this repo
   (`Deploy-PrecedentEntity.ps1`, `Deploy-ObservationReviewSurface.ps1`) use the same
   `MSCRM.SolutionUniqueName` header pattern via selective `ExtraHeaders`. This script applies
   it uniformly across all requests (GET included, where Dataverse ignores it harmlessly) for
   simplicity — a minor style difference from the selective-header exemplars, not a
   functional one.

## Idempotency verification (by inspection — not executed)

- Global option sets: guarded by `Test-GlobalOptionSetExists` (GET
  `GlobalOptionSetDefinitions(Name='...')`) before `New-GlobalOptionSetIfMissing` posts.
- Entity: guarded by `Test-EntityExists` before the `EntityDefinitions` POST.
- Every attribute (picklists + text/datetime/integer fields): guarded by
  `Test-AttributeExists` inside the shared `Add-AttributeIfMissing` helper before the
  `Attributes` POST.
- Publish (`PublishXml`) is safe to re-run unconditionally — Dataverse publish is itself
  idempotent (no-op if nothing changed).
- Net result: re-running the full script against an already-provisioned environment performs
  zero creates (all guards report `[SKIP]`) and zero errors.

## Order confirmation (load-bearing constraint)

Script order, confirmed by inspection: **Step 1 (3 global option sets)** →
**Step 2 (UserOwned entity + primary name)** → **Step 3 (remaining attributes, including the
3 picklists that reference the Step-1 global option sets via
`GlobalOptionSet@odata.bind`)** → **Step 4 (publish)** → **Step 5 (verify, read-only)**. The
three picklist attribute creates in Step 3 reference `$TypeOptionSetId` /
`$SourceOptionSetId` / `$PageTypeOptionSetId`, which are resolved from the global option sets
in Step 1 — so the script would fail fast (undefined/null variable) if Step 1 were skipped or
reordered after Step 3, which is a structural guarantee that order cannot silently invert.
