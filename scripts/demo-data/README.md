# Demo dataset v2 and the demo loader

Populates a **demo customer stamp** (first target: ACME, `https://spaarke-acme.crm.dynamics.com`) with a coherent,
realistic legal-department story, and removes it again. Data plan D6
(`projects/customer-provisioning-orchestration-r1/notes/data-setup-plan.md`, Tier 4).

| File | Purpose |
|---|---|
| `scripts/Load-DemoSampleData.ps1` | The operator script: load, dry run, reset. |
| `scripts/demo-data/demo-records.json` | The dataset (`datasetVersion` 2.0.0). |
| `scripts/demo-data/DemoDataLib.ps1` | Pure functions (table specs, validation, payloads, guard, reset order). No network. Dot-sourced by the loader and the tests. |
| `scripts/demo-data/reference-names.json` | The reference values no seed file carries: choice labels (English) and record types. Taxonomy names and event types are NOT copied here: they are read from the D4 pack (`scripts/seed-data/reference/packs/legal-standard/*.json`) and the event-type seed (`scripts/seed-data/reference/sprk_eventtype_ref.json`) by `Get-DemoSeedReference`, so they cannot drift. |
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
`-AllowNonDemo` overrides that for a Development, Sandbox, Trial, Partner or Training row, or for "the registry was read
successfully and has no row for this URL", and prints a warning that says which. **A Production row is refused even with
`-AllowNonDemo`.** (The plan says "never on a production customer"; there is no override for it by design.) The ACME
registry row is Demo and active (checked 2026-11-25).

A registry read that **fails** (token, network, permission) stops the script. It is never treated as "no row", because "no
row" is exactly what `-AllowNonDemo` accepts. URLs are compared by host on both sides (case, trailing slash, path, scheme and
port do not matter), a registry value too malformed to read still counts as a match when it mentions the target host, a
Production row that names the URL wins over a Demo row for the same URL (also when `-CustomerId` picked the Demo one), and two
active rows for one URL are an error.

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
an **untagged** event someone filed under it, and a cascade-delete relationship (a note or an activity regarding a contact)
would silently delete the child. Reset therefore checks **every** 1:N relationship into the table (system tables included;
only created/modified-by and owner columns are skipped) and every many-to-many association, and **keeps** a tagged row while
any row this reset does not delete still points at it:

- a referencing row blocks unless it is a tagged row that this run has deleted (or, under `-WhatIf`, would delete). Children
  are deleted before parents, so a parent is checked after its demo children are gone; a demo child that is itself kept
  keeps its parent too;
- a many-to-many association blocks unless the other side is also a tagged demo row;
- a relationship that cannot be read keeps every row of that table (**fails closed**) and is listed. The one exception is a
  system table the platform refuses to retrieve at all (`RetrieveMultiple ... does not support entities of type`, for example
  `postrole`): it is noted, not treated as a failure;
- the summary ends with how many tagged rows were **kept** and what still points at each. The exit code does not change
  (a kept row is the safe outcome).

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

Matter numbers are `MTR-DEMO-001`..`010` and project numbers `PRJ-DEMO-001`..`009`, so they cannot collide with the v1
dataset (`MTR-2026-001`..`005`, `PRJ-2026-001`..`003`).

Taxonomy values are the **D4 pack names exactly** (matter types `Patent` and `Trademark`; project type `Intellectual
Property - Patent` with a plain hyphen). Add a row by adding it to the JSON. A taxonomy name that is not in the pack, or an
event type code that is not in the event-type seed, fails the loader's dataset check and the Pester tests; a new choice
label goes in `reference-names.json` in the same change. Run the tests before committing.

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
- The taxonomy names come from the D4 pack files. If a pack changes a name, `demo-records.json` must follow: the loader
  checks the dataset against the pack files first, then resolves each name on the target and does not guess.
- The signed-in operator can create and delete rows in the demo tables and read the registry on the admin environment.

## Upgrading a stamp that has the v1 data

The v1 script created **untagged** rows, and this loader never touches an untagged row, so v1 rows stay next to the v2 story
until you remove them by hand (model-driven app, or Advanced Find then delete). The v1 rows are:

- 5 contacts: `sarah.mitchell@contoso.com`, `james.chen@fabrikam.com`, `maria.rodriguez@northwindtraders.com`,
  `david.kim@adventureworks.com`, `emily.thompson@woodgrove.com`;
- 5 matters `MTR-2026-001`..`005` and 3 projects `PRJ-2026-001`..`003`;
- 5 events (`Contract Renewal Deadline - Contoso`, `GDPR Audit Preparation Meeting`, `IP License Fee Payment Due`,
  `Board Compliance Report`, `Vendor Risk Assessment - Northwind`);
- 8 documents (`Contoso Supply Agreement v3.2`, `Fabrikam Patent License Agreement`, `Northwind DPA - GDPR Compliant`,
  `Employment Non-Compete Analysis`, `Woodgrove Compliance Checklist 2026`, `Vendor Risk Assessment Template`,
  `Q1 2026 Invoice Summary - Fabrikam`, `Board Meeting Minutes - March 2026`);
- the v1 chart definitions and AI seed rows (product configuration; leave them if H12a/H12b seeded the stamp).

Delete the events and documents first, then the projects and matters, then the contacts. v2 uses different numbers
(`MTR-DEMO-*`, `PRJ-DEMO-*`) and `example.com` emails, so loading v2 first does not collide; it only shows both stories.

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
