# Task 025 — eBilling settings table and BFF settings reader (notes)

> Run 2026-10-15 against dev (`https://spaarkedev1.crm.dynamics.com`). Script: `scripts/ebilling/Deploy-EbillingSettingsSchema.ps1`. Rigor STANDARD (brief). Data model: `docs/data-model/sprk_ebillingsetting.md`.
> **Status: DONE. No escalation fired.** Org-level auditing was already on; the alternate key on the single-option choice was accepted.

## 1. Discovery (step 0)

- Organization auditing: `isauditenabled = true`, `isuseraccessauditenabled = true`. Escalation trigger 1 did not fire.
- Global choice `sprk_billingapproverrole` (task 024): 100000000 Assigned Attorney 1, …04 Assigned To Internal (seed roles), …05 "Assigned to External", …06 Assigned To. The script stops with exit 2 if 100000000 / 100000004 differ.
- `sprk_billingcodeset` existed (task 018): eight sets; "UTBMS Litigation" is the seeded default code set. The lookup was created in the same run (not SKIPPED).
- Alternate key on a single-option local choice: accepted; index Active after ~90 s. Escalation trigger 2 did not fire.

## 2. Runs (dev)

| Run | Result |
|---|---|
| `-WhatIf` | 18 creations planned, 0 writes, exit 0. |
| Apply 1 | Table, primary-name length / required level, table auditing, `sprk_singleton`, key (Active), 3 multi-selects and the 7 decimals created; failed at `sprk_relatedworkwindowdays` — see deviation 1. |
| Apply 2, 3 | Script fixes (single-option label read, lookup auditing, never seed without the lookup when the code set exists); remaining columns, the lookup and the seed row created. Verification found the lookup not audited; fixed in the script (read-modify-PUT of the lookup attribute). |
| Apply 4, 5 | `created : 0  changed : 0  skipped : 23`, "nothing changed - no publish", exit 0; a `-WhatIf` also `created 0 changed 0 skipped 23`. |

Live dev changes (all additive): table `sprk_ebillingsetting` (organization-owned, entity set `sprk_ebillingsettings`, in `SpaarkeMaster`), the columns in the data-model page, key `sprk_ebillingsetting_singleton`, relationship `sprk_ebillingsetting_defaultcodeset_billingcodeset`, one seeded row (`92acd624-c6c8-f111-a05c-7c1e52154cae`). No file under `src/dataverse/solutions/SpaarkeMaster/` changed.

## 3. Acceptance evidence (dev, raw Web API)

- **Second row refused**, with and without an explicit `sprk_singleton`: `0x80060892 Entity Key eBilling Settings singleton violated. A record with the same value for Singleton already exists.` The table still holds 1 row.
- **Range refusals**: `sprk_reliabilitythreshold = 1.5` and `-0.1` -> `0x80044330 … outside the valid range(0 to 1)`; `sprk_codingchunksize = 0` -> `0x8004432f … Minimum Value: 1, Maximum value: 200`; `sprk_relatedwindowdays = 91` -> `0x8004432f … Maximum value: 90`. `0.85` saved; restored to `0.90` afterwards.
- **Audit history**: `audits` for the row show the create (op 1) and two updates (op 2); `RetrieveAuditDetails` shows `sprk_reliabilitythreshold 0.9 -> 0.85` at 18:30:12 and `0.85 -> 0.9` at 18:30:39.
- **No overwrite on re-run**: threshold set to 0.85, script re-run ("skipped seed: already has 1 row(s); nothing is overwritten"), value still 0.85, then restored.
- **Seed row** (independent FetchXml read, the same query the reader sends): roles `100000000,100000004` on all three role columns, threshold 0.9, weights 0.5 / 0.25 / 0.15 / 0.1, caps 0.5 / 0.8, window 7, chunk 25, firm-notice AI true, default code set UTBMS Litigation (`827ec4c0-abc8-f111-a05c-0022482913fc`), AP mapping = D-56 column set JSON.

## 4. Provisional values (D-69) — for tasks 046 / 161 / 162 / 163

| Setting | Seeded | Meaning |
|---|---|---|
| `sprk_weightanalyzer` | 0.50 | analyzer (OCR) confidence |
| `sprk_weightarithmetic` | 0.25 | arithmetic consistency (hours x rate = amount, lines -> totals) |
| `sprk_weightreferencematch` | 0.15 | reference-data match (roster, billing period) |
| `sprk_weightselfreport` | 0.10 | model self-report (minor input) |
| `sprk_arithmeticfailcap` | 0.50 | ceiling on combined confidence when arithmetic fails |
| `sprk_referencemissingcap` | 0.80 | ceiling on combined confidence with no reference match |

Weights sum to 1.00 but the platform does not enforce it: the combiner must normalize. "Caps" is interpreted as a ceiling (the POML's reading); both sit below the 0.90 threshold on purpose, so a field with failed arithmetic or no reference match cannot support a rejection (I-5). Task 046 confirms or changes the interpretation. Owner-accepted as provisional (D-69).

## 5. BFF

- `Services/Finance/Models/EbillingSettings.cs` (immutable record, typed defaults equal to the seed, `BillingApproverRole` enum with the dev option values), `Services/Finance/IEbillingSettingsReader.cs`, `Services/Finance/EbillingSettingsReader.cs`. Registered once in `AddFinanceModule` (`AddSingleton`, unconditional per the flag rule); one line plus a comment, appended after the `EbillingOptions` block.
- Reader: one FetchXml `RetrieveMultipleAsync` (`statecode = 0`, `top="2"`, ordered by `createdon`); cache `IMemoryCache`, absolute TTL **30 s** (≤ 60 s), ADR-009 exception documented on the class (copying `DataverseAllowedIndexesProvider`: singleton + scope per load). No row -> typed defaults + one warning per cycle. A cleared column keeps the default for that property (so an empty approver-role list can never reach task 103). An unknown role value (option added later) is skipped with a warning. A Dataverse failure propagates and is not cached.
- `ApExportMappingJson` is `null` when the column is blank (task 150 uses its built-in D-56 default); `DefaultCodeSetId` is `null` when the lookup is empty (task 047 falls back to "UTBMS Litigation" by name).
- The seeded AP mapping JSON shape (`version`, `columns[]`, `lineColumns[]`; each `name`, dotted `source`, optional `format`) is a first shape for **task 150, which owns the parser and may extend it**; 150's POML says "025's mapping JSON shape", so it should read `docs/data-model/sprk_ebillingsetting.md` "AP export mapping".

### Tests (`tests/unit/Sprk.Bff.Api.Tests/Services/Finance/EbillingSettingsReaderTests.cs`, 8 tests, FakeTimeProvider, no real waiting)

Row mapped (every column incl. multi-select and the lookup); missing optional columns keep defaults and the lookup is null; unknown role skipped + warned; no row -> defaults equal to the seed + one warning + cached; second call within TTL makes no second Dataverse call; after TTL the changed value is returned; failure propagates and is not cached; TTL ≤ 60 s. Mutation check: swallowing the failure into defaults and zeroing the TTL each made tests fail (2 failed / 6 passed), restored to 8 / 8.

### Build, tests, size

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. `dotnet list package --vulnerable --include-transitive`: none. No package added.
- `Sprk.Bff.Api.Tests`: Finance + Api.Finance + Infrastructure.DI = 263 passed; `EbillingSettingsReaderTests` 8 passed. `Spaarke.ArchTests`: 1045 passed (after the ceiling change; before it `ADR-010: Services should be concrete unless seam required` failed 159 -> 160).
- **ADR-010 ceiling 159 -> 160** (`tests/Spaarke.ArchTests/ADR010_DITests.cs`), seam justification written next to the ceiling: the interface is the Dataverse boundary every eBilling consumer (046, 047, 103, 131, 150) fakes with a Moq double. If another branch also raises the ceiling (task 043's client, for example) the merge needs the two additions summed, not one winning.
- **Publish size** (CLAUDE.md §10 bullet 4; command `dotnet publish -c Release -r linux-x64 --self-contained false`, framework-dependent linux-x64, **PDBs included**, `Compress-Archive -CompressionLevel Optimal` over the publish folder, fresh `git archive` trees at `C:\wt025b|p|m`, 192 files on every side): origin/master `09dbed8f3` = 38,902,411 bytes (**37.10 MB**); this branch = 38,926,945 bytes (**37.12 MB**), delta vs fresh master **+24,534 bytes (+0.023 MB)** (includes the earlier eBilling tasks already on the branch); this task alone vs its parent `d065e2b94` (38,921,157 bytes) **+5,788 bytes (+0.006 MB)**. Far under +5 MB / 55 MB / 60 MB. The local `master` ref is stale (`43b4eae92`, before the 2026-10-13 CVE pin bumps) and fails a fresh restore with NU1903; the comparison therefore uses `origin/master`.

### §11 component justification (three questions per new surface)

| New surface | 1 Existing | 2 Extension | 3 Cost of doing nothing |
|---|---|---|---|
| Table `sprk_ebillingsetting` (+ 15 columns, key) | `Configuration/*Options.cs` (appsettings, deploy-time, unaudited); no Finance settings table exists | No: appsettings are not admin-editable or audited, which FR-48 / D-59 require | Threshold, confidence weights and approver defaults need a deployment to change; an empty `sprk_billingapproverrole` has no default to resolve to (FR-14) |
| `IEbillingSettingsReader` / `EbillingSettingsReader` / `EbillingSettings` | `DataverseAllowedIndexesProvider` (copied pattern, but a different table and shape); `EbillingOptions` (flag only) | No: neither reads this row; extending `EbillingOptions` would put admin-edited data in appsettings | Every consumer (046, 047, 103, 131, 150) would hand-roll a Dataverse read and a cache, or hard-code the values |
| `BillingApproverRole` enum | Option values exist only as a Dataverse global choice; no C# type | No existing type to extend | Task 103 / settings consumers would compare raw ints |
| 1:1 interface (ceiling +1) | `IAllowedIndexesProvider` (precedent) | A concrete registration would put FetchXml + scope plumbing into every consumer test | Consumer tests need a double |
| DI registration (1 line, `AddFinanceModule`) | the `EbillingOptions` block above it | Same module, no new module | The reader cannot be injected |
| `Deploy-EbillingSettingsSchema.ps1` | `EbillingSchema.psm1` helpers (reused); no helper for decimal / boolean / audit | Local helpers added in the script (the module is a shared append point across parallel tasks) | No repeatable way to create the table |

## 6. Deviations and judgment calls

1. **`sprk_relatedwindowdays`, not `sprk_relatedworkwindowdays`.** The first dev run used a schema name that produced `sprk_relatedwindowdays` (a typo against the POML) and then failed on its own read-back. Creating the correct name would leave an orphan column, and deleting the orphan is a destructive change that needs the owner's OK, so the created name is kept and the script, docs and reader use it. Nothing outside the 025 task text names the column (consumers use `EbillingSettings.RelatedWorkWindowDays`). If the owner prefers the POML name: delete the (empty, unreferenced) column and re-run the script with the schema name changed.
2. **SystemRequired on `sprk_singleton`** (not ApplicationRequired, DEF-009): so an API write is not accepted without the key value. In dev the platform also defaulted the value on a create that omitted it, and the key then refused the second row.
3. **Table audit and lookup audit are set by the script itself**, not by `EbillingSchema.psm1` (`Add-Table` / `Add-Lookup` have no audit flag). A shared-module change would be a clean follow-up (an `-Audit` switch) but the module is a parallel append point; left as a note, not filed (below the filing bar).
4. **Table display name** "eBilling Setting" (plural "eBilling Settings").
5. **Seed bug found and fixed in-run**: the first seed ran before the new lookup's navigation property was readable and wrote the row without the default code set; the row was patched once (it was my own unedited seed) and the script now waits for the navigation property and refuses to seed without the lookup when the code set exists.

## 7. For other tasks

- **029**: admin form, view, sitemap; export-to-temp; re-run this script afterwards. Show `sprk_singleton` read-only (default Default).
- **103**: read `MatterApproverRoles` etc.; "Assigned To" (100000006) resolves only on Work Assignment; Matter's own column is still single-select (DEF-011).
- **150**: `ApExportMappingJson` -> snapshot verbatim into `sprk_billingexport.sprk_mappingsnapshot`.

rework: fix-round-2 task 025
