# Task 014 — Blocker B-014 — `sprk_playbookcode` backfill conflicts

> **Generated**: 2026-06-22 (main session — task 014 read-only assessment)
> **Status**: 🚧 BLOCKED per POML constraint (step 2 STOP-and-escalate)
> **Source**: Dataverse DEV environment via MCP `read_query` on `sprk_analysisplaybook`
> **Tooling**: Dataverse MCP `read_query` (no writes performed)

## Spec §1.7.3 target vs DEV actual state

| # | Spec name | Spec target code | DEV `sprk_name` (actual) | DEV `sprk_playbookcode` (actual) | Status |
|---|---|---|---|---|---|
| 1 | `summarize-document-for-chat@v1` | `summarize-document-chat` | `summarize-document-for-chat@v1` | **NULL** | ✅ safe to backfill |
| 2 | `summarize-document-for-workspace@v1` | `summarize-document-workspace` | `summarize-document-for-workspace@v1` | **NULL** | ✅ safe to backfill |
| 3 | `"Summarize New File(s)"` | `summarize-new-files` | **NOT FOUND in DEV** | n/a | 🚧 **MISSING playbook** — see §A |
| 4 | `"Document Profile"` | `document-profile` | `Document Profile` | **"PB-002"** | 🚧 **CONFLICT** — existing code differs |
| 5 | `"Create New Matter Pre-Fill"` | `create-matter-prefill` | `Create New Matter Pre-Fill` | **"PB-008"** | 🚧 **CONFLICT** — existing code differs |
| 6 | `"Create New Project Pre-Fill"` | `create-project-prefill` | `Create New Project Pre-Fill` | **NULL** | ✅ safe to backfill |

## Detailed findings

### § A — "Summarize New File(s)" not in DEV

A wildcard search confirms the playbook does NOT exist:

```sql
SELECT … WHERE sprk_name LIKE '%Summarize%New%File%' → 0 rows
```

Closest sibling (also pre-existing code, not matching spec):

| GUID | Name | Existing code |
|---|---|---|
| `4a72f99c-a119-f111-8343-7ced8d1dc988` | `Summarize File` | `PB-015` |

This may indicate:
- The spec §1.7.3 row references a name that was never created in DEV
- OR the playbook was renamed at some point (compare `Summarize File`)
- OR DEV is genuinely missing a playbook that exists in another tenant

### § B — Existing "PB-NNN" codes pattern

Two of the production-bound playbooks already carry codes in a `PB-NNN` numbering convention:
- `Document Profile` = `PB-002`
- `Create New Matter Pre-Fill` = `PB-008`

A wider scan finds more (incidental observation; not exhaustive):

| GUID | Name | Code |
|---|---|---|
| `4a72f99c-…` | `Summarize File` | `PB-015` |
| `86c243fe-…` | `Summarize a Non-Disclosure Agreement` | `PB-009` |

This is a parallel coding convention that conflicts with spec §1.7.3's kebab-case stable-code convention. The spec §1.7.3 targets are descriptive slug codes (`document-profile`, `create-matter-prefill`); DEV has opaque numeric codes (`PB-002`, `PB-008`).

## Decision required (owner pick)

| Option | Action on the 3 conflicts (`Document Profile`, `Create New Matter Pre-Fill`, `Summarize File`/missing-row 3) | Trade-off |
|---|---|---|
| **(a) Overwrite** | Backfill all 6 codes to spec §1.7.3 targets, OVERWRITING the existing `PB-NNN` codes | Easiest in code. **Risk**: any existing client (other project, integration, audit log) that resolves by `PB-002` etc. would break silently. Reversible only if we preserve old values in a side note. |
| **(b) Keep old codes, update spec §1.7.3** | Update spec.md §1.7.3 to use the existing `PB-NNN` codes for the 3 already-coded rows; backfill only the 3 currently-NULL rows. Add the missing "Summarize New File(s)" decision as separate work | Preserves existing consumers. **Risk**: spec drift from the WP1 §1.7.3 intent of self-descriptive kebab-case codes (loses the readability benefit at consumer sites — `options.DocumentProfilePlaybookCode = "PB-002"` is opaque). |
| **(c) Dual-code** | Add a new "stable-code" alternate-key column distinct from existing `sprk_playbookcode`, so the existing `PB-NNN` and the new descriptive codes coexist | More work; introduces schema change which the project tried to avoid. **Risk**: Doubles cognitive load on consumers. |
| **(d) Investigate first** | Pause task 014 until the project surveys all known consumers of `sprk_playbookcode = "PB-NNN"` (cross-project grep + Dataverse references) so the impact of option (a) is concrete. | Lowest risk. Cost: 1–2h survey, then re-pick a/b/c. |

### Separately for `Summarize New File(s)` (row 3)

| Option | Action |
|---|---|
| (a') Treat as missing in DEV; create it | Spec §1.7.3 expects this playbook to exist. Author it now (separate task — task 014.5?) with the stable code `summarize-new-files`. |
| (b') Drop from spec §1.7.3 | If "Summarize New File(s)" is not actually production-bound in this project's scope, remove the row from spec §1.7.3 and the affected `WorkspaceOptions` code reference. |
| (c') Map to `Summarize File` | If `Summarize File` (existing `PB-015`) is the same playbook under a renamed name, update spec §1.7.3 row to point at the existing record and pick option (a)/(b) above for the code mismatch. |

## Recommendation (advisory only)

**Pause for owner. Recommend option (d) followed by option (b)** — survey existing `PB-NNN` consumers first to understand blast radius, then preserve the existing codes by updating spec §1.7.3 (cheapest, least risk, but loses one readability benefit). Separately, the missing "Summarize New File(s)" row is likely either obsolete (option b') or a renamed reference (option c'). Both need owner clarification.

## Read-only confirmation

NO writes were performed in this assessment. All evidence is from `mcp__dataverse__read_query`. The 3 rows currently with NULL `sprk_playbookcode` (rows 1, 2, 6) are still NULL — task 014 has not written anything.

## Next step

Owner: reply with chosen option for the 3 conflicts + the missing row, then `continue` to resume.
