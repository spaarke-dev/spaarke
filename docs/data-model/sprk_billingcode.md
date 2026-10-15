# sprk_billingcodeset and sprk_billingcode (UTBMS code sets)

> **Project**: ebilling-r1, task 018 (FR-08, FR-22, D-32)
> **Last Updated**: 2026-10-15
> **Scripts**: `scripts/ebilling/Deploy-EbillingBillingCodeSchema.ps1` (schema), `scripts/ebilling/Seed-EbillingBillingCodes.ps1` (rows)
> **Seed files**: `scripts/seed-data/ebilling/utbms-*.csv`

Reference data for UTBMS task, activity and expense codes. A **code set** is versioned and mapped as a unit; each **code** carries the guidance text the coding Action (`invoice-line-code`, task 047) reads together with a closed list of codes for the line's code set (FR-22). Both tables are organization-owned, created additively in dev on 2026-10-15; no plugin, business rule or calculated column (ADR-002).

## sprk_billingcodeset (Billing Code Set)

Entity set `sprk_billingcodesets`. Status is the standard `statecode` / `statuscode` (1 = Active / state 0, 2 = Inactive / state 1).

| Logical name | Type | Notes |
|---|---|---|
| `sprk_name` | Text (200), primary name, application-required | Unique by convention; the seed matches a set on this name. Required by the BFF write path, not enforced on API writes. |
| `sprk_body` | Text (100) | Standards body, e.g. `ABA`, `LOC`. |
| `sprk_version` | Text (50) | Published version, e.g. `1997`, `2023-04`. |
| `sprk_sourceurl` | URL (500) | Where the set was downloaded from (utbms.com). |

## sprk_billingcode (Billing Code)

Entity set `sprk_billingcodes`. Status is the standard `statecode` / `statuscode` (Active / Inactive).

| Logical name | Type | Notes |
|---|---|---|
| `sprk_code` | Text (100), primary name, **SystemRequired** (alternate key column) | The code as billed (`L110`, `E101`, `110402`). Unique within its set. The platform refuses an empty code on every write. |
| `sprk_codeset` | Lookup -> `sprk_billingcodeset` (**SystemRequired**, alternate key column; delete **Restrict**) | Relationship `sprk_billingcodeset_billingcode_codeset`. The platform refuses a code without a set on every write. |
| `sprk_codetype` | Choice (local, application-required) | Task = 100000000, Activity = 100000001, Expense = 100000002. Required by the BFF write path and the seed, not enforced on API writes. |
| `sprk_phase` | Text (20) | Phase code the task rolls up to (`L100`, `TR200`, `110400`). Empty for activity and expense codes. |
| `sprk_description` | Text (500) | Short name of the code. |
| `sprk_guidancetext` | Multiline text (10,000) | Definition / usage guidance for the coding Action. **An administrator edit is never overwritten by the seed.** |

### Alternate key

`sprk_billingcode_codeset_code_key` on (`sprk_codeset`, `sprk_code`), index Active. Dataverse lists the key attributes alphabetically (`sprk_code`, `sprk_codeset`).

Web API addressing of a code by its key uses the **lookup value name**, not the logical name or the navigation property:

```
GET /api/data/v9.2/sprk_billingcodes(_sprk_codeset_value=<set id>,sprk_code='L110')
```

(`sprk_codeset=` and `sprk_CodeSet=` are refused with 400.) Filtering also works: `sprk_billingcodes?$filter=_sprk_codeset_value eq <set id>`.

**Required levels.** Dataverse enforces `ApplicationRequired` on forms only; an API write that omits the value succeeds. The two alternate-key columns (`sprk_codeset`, `sprk_code`) are therefore **SystemRequired** (the platform refuses a null on every write: `Attribute 'x' cannot be NULL`); the other "application-required" columns above are required by the BFF write path and the seed, not enforced on API writes. Set by `Deploy-EbillingBillingCodeSchema.ps1` (DEF-006, DEF-009).

Gotcha: the primary-name `MaxLength` in an entity-create body is ignored (Dataverse returns 850), and an alternate key on (lookup, text) fails above 1,700 bytes of index. The schema script shortens `sprk_code` to 100 and `sprk_name` to 200 right after creating the tables (only while they hold no rows).

## Default code set mapping (FR-22)

| Table | Column | Type |
|---|---|---|
| `sprk_mattertype_ref` | `sprk_defaultbillingcodeset` | Lookup -> `sprk_billingcodeset` (relationship `sprk_billingcodeset_mattertype_ref_defaultcodeset`, delete RemoveLink) |
| `sprk_practicearea_ref` | `sprk_defaultbillingcodeset` | Lookup -> `sprk_billingcodeset` (relationship `sprk_billingcodeset_practicearea_ref_defaultcodeset`, delete RemoveLink) |

Resolution order (FR-22): firm-supplied codes, matter-type mapping, practice-area mapping, AI suggestion (flagged), settings default (Litigation). Matter, Project and Work Assignment reach these through their `sprk_mattertype` / `sprk_practicearea` lookups. No mapping rows are seeded here; the admin (and task 048's confirmed AI proposals) set them.

## Line code lookups (task 010)

`sprk_billingevent` (Invoice Line) carries `sprk_taskcode`, `sprk_activitycode` and `sprk_expensecode`, each a lookup -> `sprk_billingcode`. Task 010 deferred them until this table existed; re-running `Deploy-EbillingInvoiceLineSchema.ps1` after task 018 created all three (see `projects/ebilling-r1/notes/010-invoice-line-columns.md`).

## Seeded code sets (dev, 2026-10-15)

Downloaded from utbms.com on 2026-10-15. Terms of use: "You may download materials displayed on this website"; owner decision D-32 records that UTBMS / LEDES code sets are freely usable by systems.

| Set name | CSV | Body | Version | Codes | Source |
|---|---|---|---|---|---|
| UTBMS Litigation | `utbms-litigation.csv` | ABA | 1997 | 29 | https://utbms.com/wp-content/uploads/2023/11/ABA-UTBMS-Litigation-Codes-1997.xlsx |
| UTBMS Counseling | `utbms-counseling.csv` | ABA | 1997 | 4 | https://utbms.com/wp-content/uploads/2023/11/ABA-UTBMS-Counseling-Codes-1997.xlsx |
| UTBMS Project | `utbms-project.csv` | ABA | 1997 | 15 | https://utbms.com/wp-content/uploads/2023/11/ABA-UTBMS-Project-Codes-1997.xlsx |
| UTBMS Bankruptcy | `utbms-bankruptcy.csv` | ABA | 1997 | 21 | https://utbms.com/wp-content/uploads/2023/11/ABA-UTBMS-Bankruptcy-Codes-1997.xlsx |
| UTBMS IP Patent Prosecution | `utbms-ip-patent.csv` | LOC | 2023-04 | 23 | https://utbms.com/wp-content/uploads/2023/04/LOC-IP-Patent-Prosecution-Codes-4-2023.xlsx |
| UTBMS Trademark | `utbms-trademark.csv` | LOC | 2009 | 34 | https://utbms.com/wp-content/uploads/2023/08/UTBMS-Trademark-Billing-Codes.pdf |
| UTBMS Activity | `utbms-activity.csv` | ABA | 1997 | 11 | https://utbms.com/wp-content/uploads/2023/11/ABA-UTBMS-Activity-Codes-1997.xlsx |
| UTBMS Expense | `utbms-expense.csv` | ABA | 1997 | 24 | https://utbms.com/wp-content/uploads/2023/11/ABA-UTBMS-Expense-Codes-1997.xlsx |

161 codes in total. Rules used to turn the source into rows:

- **One row per billable code.** A phase that has task rows (litigation L100, project P200, every bankruptcy and IP patent phase, every trademark phase) is not a row; its code is the `sprk_phase` of its tasks. A phase with no tasks is itself billable and is a row with `sprk_phase` equal to its own code (all four counseling codes, project P100 and P300 to P800).
- `sprk_description` is the code's published name; `sprk_guidancetext` is the published definition (the "Note" / "Description" column). IP patent guidance is the USPTO documents and PAIR transactions the LOC lists for validating the task. Activity and expense codes have no published definition in the source files, so their guidance is empty until an admin fills it.
- Set-level metadata (name, body, version, source URL, download date) repeats on every CSV row; the seed requires it to agree within a file. The download date lives only in the CSV.

### Not seeded (known gaps, filed as a deferral)

- The 2013 LOC *revised* activity and expense lists (X-series expenses, A112 to A128) and the IP-specific expense codes E125 to E131: utbms.com publishes them only as PDFs whose table layout does not extract reliably. The seeded Activity and Expense sets are the original ABA lists (A101 to A111, E101 to E124).
- Other utbms.com sets (criminal, real estate, workers' compensation, M&A, eDiscovery, GRC, DRI, Canadian, Jackson EW, KM). FR-08 names eight sets.

## Seed contract (`Seed-EbillingBillingCodes.ps1`)

- Set matched on `sprk_name`; code matched on the alternate key. Missing rows are created; **existing rows are never updated** - not `sprk_guidancetext` and not any other column. A column that differs from the CSV is as likely an admin edit as a stale seed, and the script cannot tell them apart. Differences are only reported ("left alone because they differ").
- A re-run with unchanged CSVs performs zero writes (verified in dev: 161 unchanged, 0 created).
- All CSVs are validated before any write; duplicate codes within a set, an unknown code type or a missing column stop the script (exit 2).
- Not part of `scripts/seed-data/manifest.yaml` (that manifest is the AI-seed chain).

## Dev ids

Litigation code set (the settings default for task 025): see `projects/ebilling-r1/notes/018-utbms-code-sets.md`. Row ids are environment-specific; resolve sets by name or by the alternate key.
