# Task 040 — Thread pin field, additive Dataverse schema (FR-24)

**Status**: created + solution-added + published, target env **spaarkedev1** (`https://spaarkedev1.crm.dynamics.com`) · STANDARD rigor · prescriptive.

## ⭐ Exact field logical name (task 041 reads this)

> **`sprk_ispinned`** — Boolean (Two Options) on **`sprk_communicationthread`**. Schema name `sprk_IsPinned`. Display name "Pinned". `RequiredLevel: None`. `DefaultValue: false` (applies to new-record create only — see caveat below).

Task 041 (pin UI + BFF wiring) MUST reference `sprk_ispinned` on `sprk_communicationthread` — do not guess a different name.

## What was created (evidence)

1. **Attribute created** via Web API `POST EntityDefinitions(LogicalName='sprk_communicationthread')/Attributes` with `BooleanAttributeMetadata` (`SchemaName=sprk_IsPinned`, `TrueOption=1/"Yes"`, `FalseOption=0/"No"`, `DefaultValue=false`).
2. **Verified** via `GET .../Attributes(LogicalName='sprk_ispinned')` → `AttributeType: Boolean`, `RequiredLevel.Value: None`. MetadataId `0d8f728d-6885-f111-8076-7ced8ddc4cc6`.
3. **Published** customizations for `sprk_communicationthread` via `POST PublishXml`.
4. **Added to solution `SpaarkeCore`** (unmanaged, solutionid `fbfef485-e2a8-4b04-a795-7fa607402903`) via `POST AddSolutionComponent` (`ComponentType=2` = Attribute, per repo convention in `scripts/Check-SprkEventTodoFieldDeps.ps1:40`). Confirmed present in `solutioncomponents` (`solutioncomponentid` `280ce627-6a85-f111-8076-7ced8ddc4a05`).
5. **Queried live via Web API**: `GET sprk_communicationthreads?$select=...,sprk_ispinned` returns the column on existing rows (see caveat below).

`SpaarkeCore` was chosen (not a new/other solution) because it is the confirmed Tier-1 base-entities unmanaged solution that already owns `sprk_communicationthread`/`sprk_communication` sibling entities (see `src/solutions/SpaarkeCore/solution.xml`, `.claude/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md`) and this project's own task 044 notes used the same `SolutionUniqueName: SpaarkeCore` header against `sprk_communication`.

No export/import cycle was run separately — the target env for this task IS spaarkedev1 (per the task brief), and the attribute was created + solution-added + published directly against it, so the field is already live and queryable there. No solution ZIP export/import step was required to reach the target env.

## ⚠️ Caveat for task 041 — `DefaultValue` does not backfill existing rows

Querying existing thread records (e.g. `d1775f03-...`, `5105d254-...`) returns `sprk_ispinned: null`, **not** `false`. This is standard Dataverse behavior: `DefaultValue` on a Boolean/OptionSet attribute is a form/UI default applied at new-record creation — it does **not** retroactively populate existing rows, and is not a SQL-level column default enforced on every Web API create either (a create that omits the field can still land `null` depending on caller). Backfilling existing rows would be a data migration, which is explicitly out of scope / disallowed by this task's constraints ("MUST NOT require a data migration").

**Consequence for task 041**: any UI/BFF read of `sprk_ispinned` MUST treat `null` as falsy/unpinned — do not use a strict `=== false` check; use `!!value` / `value === true` for "is pinned", or equivalently `value !== true` for "is not pinned". This is functionally equivalent to `false` for every consumer that follows normal JS/`.NET` `bool?` falsy-null handling and requires no data migration.

## Schema-collision check (step 1)

Enumerated all `sprk_*` attributes on `sprk_communicationthread` before creating: `sprk_communicationthreadid`, `sprk_isdefaultthread`, `sprk_name`, `sprk_nameisautoderived`, `sprk_privacyeffectivefrom`, `sprk_privacystate`, `sprk_regarding*` (8 regarding-family lookups), `sprk_threadtype`. **No existing pin/favorite/archive/mute field** — confirms spec §11 "Existing overlap: none". No collision with `sprk_ispinned`.

## Out-of-scope fields NOT added (per constraint)

No archive, mute, category, or tag field was added — pin only, per spec FR-24 + task constraint.

## Docs updated

`docs/data-model/sprk_communication.md` — added a "Communication Thread — Pin Field (R3 task 040, FR-24)" section (new field table row) + a changelog entry at the top. The rest of the `sprk_communicationthread` entity remains undocumented in that file (pre-existing gap, out of scope for this task — flagged, not silently expanded).

## Deviations / blockers

None. No pre-existing pin field found (escalation trigger did not fire). One nuance flagged above (`null` vs `false` on pre-existing rows) — not a blocker, just a required read-side convention for task 041.
