# Field Mapping Admin Guide

> **Last Updated**: 2026-07-09
> **Purpose**: How a maker authors field-mapping profiles and rules in the native Dataverse form — no code, no custom PCF — so fields auto-populate when a wizard creates a new Event, Invoice, Report Card, or other child record from a Matter or Project.
> **Audience**: Admin / maker (no developer involvement required for authoring new mappings)

---

## Prerequisites

- [ ] Maker/admin access to the Dataverse environment with create/edit rights on `sprk_fieldmappingprofile` and `sprk_fieldmappingrule`
- [ ] The source and target tables already exist and are registered in `sprk_recordtype_ref` (ask a developer to add a registry row if a target entity is missing — see Troubleshooting)
- [ ] Know the exact logical field names on both the source and target tables (use the table's column list, or ask a developer to run a schema `describe`)

There is **no custom admin app or PCF control** for this. Profiles and rules are authored directly on the standard Dataverse model-driven forms for `sprk_fieldmappingprofile` and `sprk_fieldmappingrule`, the same way you'd edit any other configuration record. This is a deliberate design choice (see `docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md`) — the authoring volume (a handful of profiles, a handful of rules each) does not justify a bespoke UI.

---

## Quick Reference

| Item | Value |
|------|-------|
| Profile table | `sprk_fieldmappingprofile` |
| Rule table | `sprk_fieldmappingrule` (subgrid on the profile form) |
| Profile source/target | `sprk_sourcerecordtype` / `sprk_targetrecordtype` — lookups to `sprk_recordtype_ref` |
| Rule → profile link | `sprk_fieldmappingprofile` on the rule |
| Rule ordering | `sprk_executionorder` (integer, lower runs first) |
| Rule enable/disable | `sprk_isactive` (boolean) |
| Mapping type field | `sprk_mapping_type` (choice: Copy / Default / Concat / Template) |
| Copy config | `sprk_sourcefield`, `sprk_targetfield`, `sprk_sourcefieldtype`, `sprk_targetfieldtype` |
| Default config | `sprk_defaultvalue` (literal, ≤100 chars) |
| Concat/Template config | `sprk_expression` (format string, ≤2000 chars) with `{sprk_field}` placeholders |
| When mappings apply | **Creation time only** — the moment a wizard creates the child record. Existing records are refreshed only via the manual "Push Updates to Related Records" ribbon button (unchanged, separate mechanism). |

---

## Procedure

### Step 1: Open (or create) the profile

1. Navigate to the `sprk_fieldmappingprofile` table (Advanced Find, a subarea, or a direct table view).
2. To create a new profile: **+ New**. To extend the seeded attorney matrix, open the existing profile for the pair you want (e.g. "Matter → Event").
3. Fill in:
   - **Name** — a descriptive label (e.g. "Matter → Event (Attorney Matrix)").
   - **Source Record Type** (`sprk_sourcerecordtype`) — lookup to `sprk_recordtype_ref`, pick the parent entity (e.g. Matter).
   - **Target Record Type** (`sprk_targetrecordtype`) — lookup to `sprk_recordtype_ref`, pick the child entity being created (e.g. Event).
   - **Is Active** — must be checked for the profile to be used.
4. Save the record. Saving unlocks the **Field Mapping Rules** subgrid on the form.

**Expected output**: the profile form shows Name, Source Record Type, Target Record Type, Is Active, and (after first save) an editable subgrid of rules.

### Step 2: Add a rule to the subgrid

For each field you want to inherit, add one row to the `sprk_fieldmappingrule` subgrid (or open the rule table directly and set the **Field Mapping Profile** lookup to this profile). Every rule needs:

| Column | Required for | Notes |
|---|---|---|
| **Mapping Type** (`sprk_mapping_type`) | all | Choice: Copy, Default, Concat, or Template — see the mapping-type reference below |
| **Execution Order** (`sprk_executionorder`) | all | Integer; rules apply in ascending order. Use gaps (10, 20, 30…) or sequential (1, 2, 3…) — the seeded attorney matrix uses 1-8 |
| **Is Active** | all | Must be checked, or the rule is skipped entirely |
| **Source Field** (`sprk_sourcefield`) | Copy | Logical name on the **source** (parent) entity |
| **Target Field** (`sprk_targetfield`) | Copy | Logical name on the **target** (child) entity |
| **Source Field Type** / **Target Field Type** | Copy | Set to **Lookup** when either field is a lookup column (this drives `@odata.bind` binding for lookup-to-lookup copies); otherwise leave as the scalar type |
| **Default Value** (`sprk_defaultvalue`) | Default | The literal string written to the target field |
| **Expression** (`sprk_expression`) | Concat, Template | The format string — see the placeholder syntax below |

5. Save each rule.

**Expected output**: the subgrid lists one row per rule, with Mapping Type, Source Field / Target Field (or Default Value / Expression), Execution Order, and Is Active visible as columns.

---

## Configuration — Mapping-Type Reference

The engine supports exactly four mapping types (`sprk_mapping_type`). Every rule is one of these — there is no fifth type and no plan to add one without a design change.

| Setting (`sprk_mapping_type`) | What it needs | What happens |
|---|---|---|
| **Copy** | `sprk_sourcefield` + `sprk_targetfield` (+ `sprk_sourcefieldtype`/`sprk_targetfieldtype` set to **Lookup** when either field is a lookup) | The value on the source (parent) field is copied verbatim to the target field on record creation. Works for both scalar fields (text, number, choice) and lookup fields — a lookup Copy is bound via `@odata.bind`, which is why the field-type flags matter: get them wrong and the copy either fails type-compatibility or writes the wrong shape. |
| **Default / Constant** | `sprk_defaultvalue` | The literal text in `sprk_defaultvalue` is written to the target field, regardless of the source record's data. Useful for "always set Status = New" style rules. Field is limited to 100 characters. If `sprk_defaultvalue` is blank, the rule is skipped with a warning — it does not write an empty string. |
| **Concat** | `sprk_expression` | Resolves a format string against the parent record and writes the joined result to a **text/memo** target field. Semantically: "join these fields together." |
| **Template** | `sprk_expression` | Same resolver as Concat — the only difference is authoring intent (a fixed scaffold with fields dropped in, vs. a straight join). There is one placeholder engine underneath both; pick whichever label better describes your intent. |

Concat and Template **only target text/memo fields** — you cannot use a format string to populate a lookup. A Concat/Template rule pointed at a lookup target is skipped with a warning at apply time.

---

## Writing a `sprk_expression` Concat/Template string

`sprk_expression` (NVARCHAR, up to 2000 characters) holds a format string with `{sprk_field}` placeholders. Each placeholder names a **logical field on the source (parent) record**. At creation time, the engine:

1. Fetches every field referenced by any placeholder from the parent record in a single batched read.
2. Substitutes each `{sprk_field}` token with that field's resolved value.
3. Writes the fully-substituted string to the target field.

**Example** — a Concat rule joining matter number and matter name:

```
{sprk_matternumber} - {sprk_mattername}
```

If the parent Matter has `sprk_matternumber = "M-2026-014"` and `sprk_mattername = "Acme Corp Acquisition"`, the target field is set to:

```
M-2026-014 - Acme Corp Acquisition
```

A Template expression can mix literal text with placeholders the same way:

```
{sprk_matternumber} - {sprk_mattername} ({sprk_practicearea})
```

**Unresolved placeholders — important behavior**: if a placeholder names a field that doesn't exist on the source entity, or the parent record's value for that field is empty, the engine does **not** leave the literal `{sprk_field}` text in the output and does **not** throw an error. It logs a warning (visible in the wizard's non-fatal warnings, same convention as other mapping failures) and **omits that token** from the resolved string. Always verify your placeholder field names against the actual source entity schema before relying on a Concat/Template rule — a typo silently produces a shorter string rather than an error you'd notice immediately.

---

## Worked Example — the seeded Matter → Event attorney matrix

The following 8 Copy rules are seeded live in the dev environment as the profile **"Matter → Event"** (source = Matter, target = Event). All 8 are the exact schema-match case — attorney/paralegal/law-firm/external/internal fields have identical logical names on both Matter and Event, so each rule copies a lookup field to the same-named lookup field on the child:

| # | Source Field (Matter) | Target Field (Event) | Mapping Type | Field Types | Execution Order |
|---|---|---|---|---|---|
| 1 | `sprk_assignedattorney1` | `sprk_assignedattorney1` | Copy | Lookup / Lookup | 1 |
| 2 | `sprk_assignedattorney2` | `sprk_assignedattorney2` | Copy | Lookup / Lookup | 2 |
| 3 | `sprk_assignedparalegal1` | `sprk_assignedparalegal1` | Copy | Lookup / Lookup | 3 |
| 4 | `sprk_assignedparalegal2` | `sprk_assignedparalegal2` | Copy | Lookup / Lookup | 4 |
| 5 | `sprk_assignedlawfirm1` | `sprk_assignedlawfirm1` | Copy | Lookup / Lookup | 5 |
| 6 | `sprk_assignedlawfirm2` | `sprk_assignedlawfirm2` | Copy | Lookup / Lookup | 6 |
| 7 | `sprk_assignedtoexternal` | `sprk_assignedtoexternal` | Copy | Lookup / Lookup | 7 |
| 8 | `sprk_assignedtointernal` | `sprk_assignedtointernal` | Copy | Lookup / Lookup | 8 |

Every rule is `sprk_isactive = true`, and both the Matter → Event profile and this rule set are active in `spaarkedev1` today — when a wizard creates a new Event from a Matter, these 8 fields inherit automatically at creation time.

**Field-name divergence across targets — read the target schema, don't assume it matches Matter.** The same 8 source fields map differently depending on the target entity, because each target entity's actual column names differ:

- **Matter → Invoice** (also seeded): Invoice renames attorney/paralegal fields (`sprk_assignedattorney1` → `sprk_assignedtoattorney1`, etc.) and has **no law-firm field at all** and **no external/internal field at all** — those 4 rules are correctly omitted from the Invoice profile, not mapped to something that doesn't exist.
- **Matter → Report Card** (also seeded): Report Card matches Matter's names for attorney/paralegal/external/internal, but renames law-firm 1 specifically: `sprk_assignedlawfirm1` (Matter) → `sprk_assignedtolawfirm1` (Report Card). Law-firm 2 keeps the same name (`sprk_assignedlawfirm2`).

**The lesson for authoring your own rules**: always confirm the target entity's actual field logical names (view the table's column list, or ask a developer for a schema `describe`) before authoring a Copy rule. Never assume a target field shares the source's name just because a sibling target happened to.

**A Concat/Template example on the same profile** (illustrative — not currently seeded): a rule with `sprk_mapping_type = Concat`, `sprk_expression = "{sprk_matternumber} - {sprk_mattername}"`, targeting a text field such as `sprk_description` on Event, would write `"M-2026-014 - Acme Corp Acquisition"` into that field at creation time, following the same placeholder rules described above.

---

## Verification

1. **Confirm the profile is live** — open the profile record: `sprk_isactive` is checked, `sprk_sourcerecordtype` and `sprk_targetrecordtype` point at the correct `sprk_recordtype_ref` rows.
2. **Confirm each rule is live** — every rule you expect to apply has `sprk_isactive` checked and a valid `sprk_executionorder`.
3. **Create a test child record** via the appropriate "+" wizard (e.g. create an Event from a Matter) with the parent's mapped source fields populated.
4. **Inspect the new record** — expected result: every mapped field (per the profile's active rules) is populated on the new record exactly as configured, with no manual data entry needed.
5. **No profile / no matching pair** — expected result: the wizard completes exactly as it does today, with no error and no fields populated (graceful no-op) — this is by design, not a bug.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| A new child record doesn't inherit any mapped fields | The profile isn't active | Open the profile, confirm `sprk_isactive` is checked. |
| A new child record doesn't inherit any mapped fields | No `sprk_recordtype_ref` row exists for the source and/or target entity | Ask a developer to check the full `sprk_recordtype_ref` table (not a filtered query — a missing row won't show up in a search) and add the missing registry row if the entity is genuinely new. This happened for Report Card during initial seeding and required a developer-added registry row before a profile could reference it at all. |
| A specific field doesn't inherit, but others on the same profile do | That rule is inactive, or its `sprk_executionorder`/config is wrong | Open the individual rule; confirm `sprk_isactive` is checked and the mapping-type-specific fields (Source/Target Field for Copy, Default Value for Default, Expression for Concat/Template) are populated. |
| A lookup field is left blank on the new record, but the source clearly had a value | `sprk_sourcefieldtype` and/or `sprk_targetfieldtype` aren't set to **Lookup** | Edit the rule and set both field-type columns to Lookup — a Copy rule between two lookup fields needs the type flags set correctly for the engine to build the `@odata.bind` binding instead of attempting a plain scalar copy. |
| A Copy rule targets a field that doesn't exist on the target entity | Field names copied from another target's rule set without checking the actual target schema (see the field-name divergence note above) | Verify the target entity's real logical field name before saving the rule; correct the Target Field value. |
| A Concat/Template field is shorter than expected, missing a segment | One `{sprk_field}` placeholder didn't resolve (typo'd field name, or the parent record's value for that field was empty) | Re-check every placeholder in `sprk_expression` against the actual source entity's field names; check the source record actually has a value in that field. The engine omits unresolved tokens silently (with only a background warning) rather than erroring, so a typo won't be obvious from the output alone. |
| A Concat/Template rule doesn't populate anything | The rule's target field is a lookup | Concat/Template can only write to text/memo targets; change the target field or switch the rule to Copy against a compatible scalar field. |
| Fields were correct on creation but a later edit to the parent didn't cascade | Expected — mappings only apply at child-record **creation** time | Use the existing "Push Updates to Related Records" ribbon button on the parent to manually re-push mapped fields to already-existing children; this is a separate, unchanged mechanism from creation-time mapping. |

---

## Related

- [`docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md`](../architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md) — technical architecture: the two tables, the BFF contract, the client engine, the four mapping types' implementation, the creation-time-vs-update-time boundary, and same-entity support (if this file does not yet exist, it is being authored in parallel — see `projects/set-regarding-and-field-mapping-resolver-r2/`)
