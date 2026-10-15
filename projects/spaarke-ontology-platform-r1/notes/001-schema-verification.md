# Task 001 - the four missing columns: verification record

> **Date**: 2026-10-03 · **Status**: complete · **Env**: `spaarkedev1` · solution `OntologyPlatformSolution`
> The columns were created by the **owner** in the maker UI; this task's remaining work was verification
> against the spec, and correcting what did not match.

## Verified state (Web API metadata, read back after the fix)

| Table.Column | Type | Required | Notes |
|---|---|---|---|
| `sprk_decisionrecord.sprk_action` | Lookup -> `sprk_analysisaction` | None | Correct: D-2 requires a nullable action reference because **a deny path has no action** |
| `sprk_decisionrecord.sprk_actioncode` | NVARCHAR(100) | None | Spec said Text(50); 100 is harmless. Mirrors the `sprk_policy`/`sprk_policycode` pair |
| `sprk_servicerequest.sprk_direction` | Picklist: Inbound 100000000 / Outbound 100000001 | **ApplicationRequired** | **Corrected by this task** - see below |
| `sprk_servicerequest.sprk_disposition` | Picklist: Write-off 100000000 / Budget Revised 100000001 / Scope Approved 100000002 / No Action 100000003 | None | Correct |
| `sprk_servicerequest.sprk_responseduedate` | DateOnly | None | Behavior is `DateOnly`, not `TimeZoneIndependent` - see divergence below |

Relationship: `sprk_decisionrecord_Action_sprk_analysisaction`, `Delete: RemoveLink`, everything else
`NoCascade` (plus the platform's paired `Archive: RemoveLink`). Matches the spec; only the auto-generated
schema name differs from the drafted `sprk_decisionrecord_action`, which is cosmetic.

## One correction applied

`sprk_direction` was created **Optional**; the spec requires **Business Required** (`ApplicationRequired`)
so an Inquiry cannot be saved without a direction. Fixed by `PUT` on the attribute metadata -> **HTTP 204**,
re-read as `ApplicationRequired`. Safe to change: the table is at **0 rows** and `CanBeChanged` was `true`.

> **Owner action may be needed**: a global publish (`PublishAllXml`) was **not** run - it is a tenant-wide
> operation and was correctly refused as a shared-resource change. The metadata is live for API writes
> immediately; the **form** may need a publish before it renders the field as required.

## A divergence to record rather than fix

`sprk_servicerequest.sprk_responseduedate` has `DateTimeBehavior = DateOnly`, while the column it was
deliberately named to match - `sprk_workassignment.sprk_responseduedate` - has `TimeZoneIndependent`.

- **Not a defect**: both are `DateOnly` *format* and neither converts time zones, so `now > dueDate`
  comparisons agree across the two.
- **But `DateTimeBehavior` is immutable once set to `DateOnly`**, so this cannot be unified later.
- **Why it is written down**: the naming parity was justified on the grounds that the SLA-breach signal
  comes nearly free from BR-2's existing *"work assignment past `sprk_responseduedate`"* rule. That reuse
  argument still holds, but any future task that tries to share a single typed SLA predicate across both
  columns must not **assume** the behaviors match. They do not.

## Negative check

No `sprk_subjecttype` was added to `sprk_signal` (schema delta 3 is deliberate): `sprk_dedupekey` composes
from `sprk_regardingrecordtype` + `sprk_regardingrecordid`. Confirmed still absent.
