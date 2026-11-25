# Demo dataset v2 and the demo loader

Populates a **demo customer stamp** (first target: ACME, `https://spaarke-acme.crm.dynamics.com`) with a coherent,
realistic legal-department story, and removes it again. Data plan D6
(`projects/customer-provisioning-orchestration-r1/notes/data-setup-plan.md`, Tier 4).

| File | Purpose |
|---|---|
| `scripts/Load-DemoSampleData.ps1` | The operator script: load, dry run, reset. |
| `scripts/demo-data/demo-records.json` | The dataset (`datasetVersion` 2.0.0). |
| `scripts/demo-data/DemoDataLib.ps1` | Pure functions (table specs, validation, payloads, guard, reset order). No network. Dot-sourced by the loader and the tests. |
| `scripts/demo-data/reference-names.json` | Snapshot of the reference values the dataset needs (taxonomy names, event types, choice labels). The offline oracle for the tests. |
| `scripts/demo-data/sample-documents/` | Eight sample files. Not uploaded today (see "Documents and files"). |
| `tests/scripts/Load-DemoSampleData.Tests.ps1` | Pester 3.4.0 tests. |

This is an **operator** script. It is never a provisioning handler and never runs as part of provisioning.

## Use

Sign in as yourself (`az login`; a service principal is refused, NFR-11). The script never calls `az account set` and
never reads a secret.

```powershell
# 1. See what would happen. Nothing is written. Exit code 1 if anything is missing.
.\scripts\Load-DemoSampleData.ps1 -CustomerId acme -WhatIf

# 2. Load. Safe to run again: rows already loaded are matched and left alone.
.\scripts\Load-DemoSampleData.ps1 -CustomerId acme

# 3. Remove the demo rows (only the ones this script tagged), children first.
.\scripts\Load-DemoSampleData.ps1 -CustomerId acme -Reset -WhatIf
.\scripts\Load-DemoSampleData.ps1 -CustomerId acme -Reset
```

`-CustomerId` looks the stamp up in the environment registry (`sprk_dataverseenvironment`, `sprk_customerid`).
`-DataverseUrl https://<org>.crm.dynamics.com` names it directly; the URL is then checked against the same registry.
`-DryRun` is an alias for `-WhatIf`. `-SkipDocuments` / `-SkipCommunications` leave those sections out.

Per-table summary after every run: `Created / Planned / Existing / Skipped / Errors` for a load,
`Deleted / WouldDelete / Kept / Errors` for a reset. The process exit code is 1 when anything was refused, missing or
failed.

### The demo guard

The registry row of the target must be **Environment Type = Demo** and **active**. Otherwise the script refuses.
`-AllowNonDemo` overrides that for a Development, Sandbox, Trial, Partner or Training row, or for a URL with no registry
row, and prints a warning. **A Production row is refused even with `-AllowNonDemo`.** (The plan says "never on a production
customer"; there is no override for it by design.) The ACME registry row is Demo and active (checked 2026-11-25).

### What is checked before anything is written

Every value the dataset refers to is resolved on the target first: taxonomy rows **by name**
(`sprk_mattertype_ref`, `sprk_practicearea_ref`, `sprk_projecttype_ref`, `sprk_contacttype_ref`,
`sprk_organizationtype_ref`, active rows only, ambiguity is an error), event types by `sprk_eventtypecode`, record types
by `sprk_recordlogicalname`, choice labels from the column metadata, every column the script writes, every lookup
navigation property, and the owner team. **Every missing value is listed**, then the script stops (under `-WhatIf` it
still prints the full plan using placeholders, so you can read what would be created). A half-loaded story with
dangling references is never created.

## The tag (why reset is safe)

Every row the loader creates carries the standard column **`importsequencenumber` = 2026112501**. Reset deletes
`importsequencenumber eq 2026112501` and nothing else.

Why this column: it exists on every table, is writable on create, is filterable, is on no form, and nothing in Spaarke
uses it, so no user edits it by accident and no product code reads it. The alternative, a `[DEMO]` name prefix, can be
renamed away (orphaning the row), clutters every grid, and does not work on contacts (computed full name). The tag is
independent of the natural key, so a user can rename a demo matter and reset still removes it; a user's own matter that
happens to share a name is never touched.

Reset also checks relationships. All Spaarke lookups are RemoveLink, so deleting a tagged matter would silently detach
an **untagged** event someone filed under it. Reset therefore keeps (and reports) any tagged parent that an untagged row
points at, and fails closed if a relationship cannot be read.

Re-running a load matches existing rows by **tag + natural key** (matter number, project number, organization name,
contact email, event/to-do/communication/document name). It never adopts or changes an untagged row; if an untagged
row already uses a key, the create is reported as an error.

## Who owns the rows

Rows are created with `ownerid` = the **default Owner team of the customer's business unit**. The unit is named by the
registry row (`sprk_businessunitname`, falling back to `sprk_name`, the same convention the provisioning handler uses
to create it). Override with `-OwnerBusinessUnitName` / `-OwnerTeamName`; `-OwnerIsOperator` leaves the operator as the
owner (they then see the rows only through their own role). The root unit, "Secure Record" and "Spaarke Ontology
Writer" are refused as owners.

Why a team and not the operator: users of the customer unit hold Basic User roles that read the demo tables at
**Deep** (the unit and its children). Rows owned by the operator, who sits in another unit, would be invisible to the
people the demo is for. Read depth was checked per table on ACME: matter, project, event, to-do, document,
organization and contact read at Deep; `sprk_communication` reads at Basic, so it is visible only because the team
owns the row. Re-check (Security roles > Basic User) if a table's depth changes.

## Dataset shape

`demo-records.json` sections, in create order: `organizations`, `contacts`, `matters`, `projects`, `events`, `todos`,
`communications`, `documents` (reset runs the reverse). Counts: 15, 18, 10, 9, 29, 12, 12, 18 (123 rows).

Each row: `key` (stable id used by `links`), `fields` (literal columns), `dates` (day offsets from the load day, e.g.
`-3d`, `+14d 09:30`), `reference` (taxonomy names and the event type code), `options` (choice labels), `links` (the
`key` of another row). Matters and projects are typed (type + practice area / project type), organizations and contacts
are typed, and events use the product's event types (16 of the 17 appear; the system "To Do" type is used by the to-do
table, not here). Events, to-dos and communications are filed under a matter or a project (the typed lookup plus the
`sprk_regarding*` stamp the product itself writes). A matter has no subtype column, so none is written.

Add a row by adding it to the JSON; add a taxonomy name by adding it to `reference-names.json` in the same change (the
test fails otherwise). Run the tests before committing.

## Documents and files

Document rows are **metadata only** (`sprk_hasfile = false`, no file pointer columns). TODO: a real file needs an
upload through the stamp's BFF (SharePoint Embedded) with a **user** token, and the pointer columns are owned by the
server write path. The operator's `az` token cannot obtain that token, and writing the pointer columns directly would
make rows that point at nothing. `sampleFile` on 8 documents names a file in `sample-documents/` for a future upload
step; the loader ignores it today.

## Extension point: eBilling

Owner decision F: eBilling demo records (invoices, budgets, billing events) are **out of this change**. The dataset has an
`extensionPoints` object that names the sections to add (`budgets`, `invoices`, `billingEvents`) and the open question:
the invoice family is written by the BFF, so a loader that writes it directly needs an owner decision. To add a section:
add its spec to `Get-DemoTableSpecs` and its position to the create order in `DemoDataLib.ps1`, rows to the JSON, and tests.

## Prerequisites on the target stamp

- Event types seeded (data plan D2 / ISS-039) and the taxonomy pack seeded (D4, the `legal-standard` pack of §6). Until
  they are, the loader reports exactly which names are missing and writes nothing.
- The taxonomy names come from data plan §6. If D4 ships different names, change `demo-records.json` and
  `reference-names.json` to match; the loader resolves by name on the target and does not guess.
- The signed-in operator can create and delete rows in the demo tables and read the registry on the admin environment.

## What changed from v1

Removed, on purpose: AI seed data (`Deploy-All-AI-SeedData.ps1`), chart definitions, the SharePoint Embedded file
listing and the AI Search index sync (it pointed at the **production** search service), and `-Force`. AI content and charts
are product configuration seeded per stamp (H12a / H12b), not demo data. To rebuild demo rows: `-Reset`, then load.
The old dataset (generic Contoso rows, no taxonomy, no owner, no tag) is replaced, not extended.

## Tests

```powershell
pwsh -NoProfile -Command "Import-Module Pester -RequiredVersion 3.4.0; Invoke-Pester tests/scripts/Load-DemoSampleData.Tests.ps1"
```

Offline (no Dataverse, no az). Pester 3.4.0 only: the 5.x/6.x modules reject the `Should Be` syntax used here, and a
bare `Should Throw` always fails in 3.4, so every throw test passes the expected message.
