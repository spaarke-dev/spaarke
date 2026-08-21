# Discovery Checklist — blocking before `/design-to-spec`

> **Created**: 2026-08-21 during the R2 re-scope.
> **Why blocking**: [`design.md` §9](../design.md) inherits its field lists **unverified** from the withdrawn 2026-07-05 seed. Every one is `TBD-CONFIRM`. Acceptance criteria cannot be locked against guessed schema.

Use the `dataverse-mcp-usage` skill (`mcp__dataverse__describe`, `mcp__dataverse__read_query`). Record answers **in this file**, then fold the confirmed field lists into `design.md` §9 before running `/design-to-spec`.

---

## Why this is not optional

R1 shipped `MatterHeaderPcf` with a sparkle popover that was **silently empty on every Matter record in production** for multiple releases. The cause: the design assumed `sprk_recordsummary`, but that field is written on **zero** Matter records — the real narrative summaries live in `sprk_mattersummary`, written by an AI document-analysis action. Fixed in v1.0.20 (see the header comment in [`MatterHeaderView.tsx`](../../../src/client/pcf/MatterHeader/control/MatterHeaderView.tsx)).

**Do not assume `sprk_recordsummary` exists or is populated on any entity.** Verify existence *and* population.

---

## A. Per-entity schema (4 entities)

For each of `sprk_project`, `sprk_workassignment`, `sprk_invoice`, `sprk_event`:

- [ ] `PrimaryIdAttribute` and `PrimaryNameAttribute`
- [ ] For each field drafted in design.md §9: does the logical name exist, and what is its `AttributeType`?
- [ ] Which field (if any) holds an AI/narrative summary — and **how many records actually have it populated**? (`$count` with a `ne null` filter, not just field existence.)
- [ ] Any required-level constraints that should drive the `*` marker
- [ ] Confirm the entity is on its expected main form (target for binding)

**Draft field lists to verify** (from design.md §9 — all unverified):

| Entity | Draft fields | Expected new renderers |
|---|---|---|
| `sprk_project` | name, status (optionset), owner (lookup), start date, target end date | date |
| `sprk_workassignment` | name, status (optionset), assigned to (lookup), start date, estimated hours | date, number |
| `sprk_invoice` | name, invoice number, amount (currency), status (optionset), due date | currency, date |
| `sprk_event` | name, type, start (datetime), end (datetime), location | datetime |

---

## B. Lookup metadata derivation (design.md §5.4)

This determines whether R1's hard-coded `LOOKUP_META` disappears entirely.

- [ ] `sprk_mattertype_ref` — confirm `PrimaryIdAttribute === 'sprk_mattertype_refid'` and `PrimaryNameAttribute === 'sprk_mattertypename'`
- [ ] `sprk_practicearea_ref` — confirm `PrimaryIdAttribute === 'sprk_practicearea_refid'` and `PrimaryNameAttribute === 'sprk_practiceareaname'`
- [ ] For each lookup field in §A above: confirm the target entity resolves via `EntityDefinitions(LogicalName='X')/ManyToOneRelationships`

**If either assumption fails**: add an optional `fields[].lookup: { entity, idField, nameField }` escape hatch to the §5.2 schema. Nothing else in the design moves.

Existing query patterns to copy: [`PolymorphicResolverService.ts:481`](../../../src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts#L481) · [`TodoRegardingUpdateBuilder.ts:292`](../../../src/client/shared/Spaarke.UI.Components/src/services/TodoRegardingUpdateBuilder.ts#L292)

---

## C. Toolbar support confirmation

All five entities *should* already be in both maps in [`toolbarLaunchDefaults.ts`](../../../src/client/shared/Spaarke.UI.Components/src/hooks/toolbarLaunchDefaults.ts) — `SUPPORTED_TODO_PARENTS` (11 entities) and `SUPPORTED_MEMO_PARENTS` (6). Confirm, because it determines whether §6.4's auto-hide is needed for this rollout or only for entities added later.

- [ ] `sprk_project` · `sprk_workassignment` · `sprk_invoice` · `sprk_event` · `sprk_matter` present in **both** maps
- [ ] The `sprk_regarding{entity}` lookup on `sprk_todo` / `sprk_memo` matches the map value for each

---

## D. Matter parity baseline (for the §8 migration)

Matter migrates **last** and is the strongest regression test. Capture the baseline **before** any code changes:

- [ ] Screenshot `MatterHeaderPcf` v1.0.20 rendered on the Matter main form (light **and** dark)
- [ ] Record the exact five-field layout + spans so the equivalent `layoutJson` can be written against it
- [ ] Confirm the deployed version in the footer is v1.0.20
- [ ] Note the current Matter form binding (which field `boundField` is bound to) — needed to re-bind

---

## Results

> Fill in below as discovery completes. Fold confirmed field lists into `design.md` §9, then run `/design-to-spec`.

_(not started)_
