# Task 017 — Timekeeper (extend stub) and rate card schema (notes)

> Run 2026-10-15 against dev (`https://spaarkedev1.crm.dynamics.com`). Script: `scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1`. Rigor STANDARD. Data model: `docs/data-model/sprk_ratecard.md` and the Timekeeper rows of `docs/data-model/sprk_financial-related-entities.md`.
> **Status: COMPLETE (2026-10-15).** Schema applied; the owner's classification and `sprk_invoice` decisions (2026-10-15) are applied and recorded in section 8.

## 1. Step 1 — dev baseline (before any write)

- `sprk_timekeeper` (user-owned, primary name `sprk_name`, entity set `sprk_timekeepers`): **0 rows** (`$count`, includes inactive). Custom columns: `sprk_contact` (lookup -> contact, label "Person"), `sprk_invoice` (-> sprk_invoice), `sprk_invoicelineitem` (-> sprk_invoicelineitem), their `...name` text companions, `sprk_name`. statuscode: 1 = Active (state 0), 2 = Inactive (state 1).
- Rows at statuscode 1: **0**. Rows using `sprk_invoice` or `sprk_invoicelineitem`: **0**. Escalation trigger 1 (rows at statuscode 1 whose meaning is not "approved") did **not** fire; the relabel of value 1 to Approved is safe. The script re-checks this before every write (exit 2 if it ever finds rows).
- Relationships referencing the table: `sprk_Contact_Timekeeper_1n`, `sprk_Invoice_Timekeeper_1n`, `sprk_InvoiceLineItem_Timekeeper_1n` (the two obsolete lookups), `sprk_memo_RegardingTimekeeper_n1` (memo regarding), `sprk_sprk_timekeeper_sprk_billingevent_sprk_resolvedtimekeeper` (task 010).
- None of `sprk_ratecard` / `sprk_ratecardrate` existed (404).
- Readers of the two obsolete lookups, by grep of `src/`, `tests/`, `infra/`, `scripts/`: none. Hits for `sprk_timekeeper` are the SpaarkeMaster solution XML (entity, app module, site map, relationships), `RecordContainerResolver.cs` / `CoreAncestorResolver.cs` (they use the table as a Party kind for `sprk_regardingtimekeeper` on memos; neither touches `sprk_invoice` / `sprk_invoicelineitem`), and `Migrate-DataverseData.ps1`'s table list. `userProfileService.ts` only mentions `sprk_timekeeperrole`. No form or view of the timekeeper table references the two lookups other than `Entity.xml` itself.

## 2. Obsolete-lookup decision (step 6; recorded, nothing deleted)

| Lookup | Rows using it | Readers | Decision |
|---|---|---|---|
| `sprk_timekeeper.sprk_invoicelineitem` | 0 | none | Keep for now; it goes with its table in **task 011** (project CLAUDE.md §5). No new reader is allowed. |
| `sprk_timekeeper.sprk_invoice` | 0 | none | **Deleted 2026-10-15 with the owner's explicit OK** (details in section 8.5). The original recommendation stands: the table was empty and invoices reach a timekeeper through `sprk_billingevent.sprk_resolvedtimekeeper` (task 010). |

## 3. Escalation 2 — LEDES classifications without a `sprk_timekeeperrole` equivalent (FIRED; resolved by the owner on 2026-10-15, see section 8)

The global choice `sprk_timekeeperrole` has 14 values (Senior Partner, Partner, Senior Associate, Associate, Paralegal, Specialist, Other, Practice Support, Administrator, Legal Operations, Senior Counsel, General Counsel, Counsel, Associate General Counsel). It is shared by `sprk_billingevent`, `sprk_contactorganization` and `sprk_userprofile`.

Of the 20 LEDES (LOC revised, 2023) law-firm classifications, 4 map exactly (PARTNR, ASSOC, PARALG, NBOTHR), 3 map near (OFCOUN -> Counsel, LGLAST -> Practice Support, CLKSEC -> Administrator), 6 map only loosely onto Specialist (LIBRRN, RSRCHR, ANALST, HLNLPF, LGPRMG, IPAGNT) and **7 have no equivalent**: TRANEE (Associate Trainee), STFATT (Staff Attorney), SECNDE (Secondee), LGLINT (Legal Intern), CSTCNS (Costs Counsel), TEMPAT (Contract Attorney), TEMPOS (Contract Other Staff). The full table is in `docs/data-model/sprk_ratecard.md`. More than two gaps, so the task stops here.

What was done anyway: everything that does not depend on the choice (all of §4). What is held: `sprk_classification` on `sprk_timekeeper` and on `sprk_ratecardrate`. A column cannot be re-pointed to another choice without deleting it, so creating it now would risk a later delete.

### Options for the owner

1. **Reuse `sprk_timekeeperrole` unchanged** (the LEDES code maps to the nearest value; the seven gaps map lossily, e.g. TRANEE and SECNDE -> Associate, STFATT -> Counsel, LGLINT / TEMPAT / TEMPOS -> Other, CSTCNS -> Specialist). One vocabulary everywhere, and it is what `sprk_billingevent.sprk_timekeeperrole` already stores on every invoice line, so ApprovedRate compares like with like. Cost: rates cannot be set apart for the lossy classes (an Of Counsel rate shares Counsel's rate; a Staff Attorney shares Counsel's).
2. **Reuse and add the values that matter for rates** (for example Of Counsel, Legal Assistant, Staff Attorney, Legal Intern, Contract Attorney). Better rate fidelity, still one vocabulary. Cost: the shared choice gains values that also show in the contact-organization and user-profile pickers.
3. **New choice** (for example `sprk_ledesclassification`, the LEDES codes) on the timekeeper and rate. Exact LEDES fidelity and independent of the shared pickers. Cost: a second vocabulary next to `sprk_billingevent.sprk_timekeeperrole`, so the rule code would translate between the two on every line; LEDES intake is out of scope in R1.

**Original recommendation: option 1 (superseded: the owner chose to reuse the shared list and make it match LEDES exactly, section 8).** R1 reads classifications from PDF invoices, where the lines carry free text rather than LEDES codes; rate cards are usually keyed to Partner / Associate / Paralegal; a specific individual can always have a timekeeper-specific rate. The values that lose fidelity can be added later (option 2) without schema change if a customer needs them. To apply: `pwsh scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1 -ApplyClassification` (the code path was tested in `-WhatIf` only; it creates two Picklist columns bound to the existing choice by MetadataId).

Tasks that read the classification (071 ApprovedRate, 046 roster resolution, 078 rate import) must not start until this is decided.

## 4. Schema runs (dev)

| Run | Result |
|---|---|
| `-WhatIf` | Plan: 20 items created, 3 changed, 0 writes, exit 0. |
| Apply 1 | `created : 20  changed : 5  skipped : 0`, published, verified, exit 0 (no error on first apply; one MetadataCache retry x2 after creating `sprk_ratecard`, handled). |
| Apply 2 (no change) | `created : 0  changed : 0  skipped : 25`, "nothing changed - no publish", exit 0. |
| Script fix | The primary-name **RequiredLevel** in an entity-create body is ignored too (came back None). `Set-PrimaryNameRequired` sets ApplicationRequired afterwards. |
| Apply 3 | `created : 0  changed : 2` (the two primary names), published, exit 0. |
| Apply 4 + `-WhatIf` | `created : 0  changed : 0  skipped : 27`, "nothing changed - no publish", exit 0 for both. |
| `-WhatIf -ApplyClassification` | Plans exactly the two `sprk_classification` Picklist columns, 0 writes. |

Live dev schema changes (all additive): `sprk_timekeeper` + `sprk_firm` (lookup -> sprk_organization, Restrict), `sprk_effectivefrom`, `sprk_effectiveto` (date only), `sprk_contact` display name "Person" -> "Linked contact", statuscode 1 "Active" -> "Approved", inserted "Pending" (659490000, state 0); table `sprk_ratecard` (user-owned) with `sprk_name`, `sprk_firm`, `sprk_matter`, `sprk_project`, `sprk_workassignment`, `sprk_sourcedocument`, `sprk_effectivefrom`, `sprk_effectiveto`, statuscode 1 -> "Draft", inserted "Published" (659490000, state 0); table `sprk_ratecardrate` (user-owned) with `sprk_name`, `sprk_ratecard` (parental, cascade delete), `sprk_timekeeper`, `sprk_approvedrate`, `sprk_rackrate`, `sprk_effectivefrom`, `sprk_effectiveto`. Primary names shortened to 200 and set application-required (both tables empty). Both tables are in the unmanaged `SpaarkeMaster` solution (`rootcomponentbehavior = 0`), so task 029's export picks them up. No file under `src/dataverse/solutions/SpaarkeMaster/` changed.

## 5. Independent metadata read-back (separate script, raw Web API)

- `sprk_timekeeper`: `sprk_firm` Lookup -> sprk_organization; `sprk_effectivefrom` / `sprk_effectiveto` DateTime, Format DateOnly, Behavior DateOnly; `sprk_contact` Lookup -> contact, label "Linked contact"; `sprk_invoice` / `sprk_invoicelineitem` untouched. statuscode: 1 = Approved / state 0, 659490000 = Pending / state 0, 2 = Inactive / state 1.
- `sprk_ratecard` (UserOwned): lookups `sprk_firm` -> sprk_organization, `sprk_matter` -> sprk_matter, `sprk_project` -> sprk_project, `sprk_workassignment` -> sprk_workassignment, `sprk_sourcedocument` -> sprk_document; two DateOnly dates; statuscode: 1 = Draft / state 0, 659490000 = Published / state 0, 2 = Inactive / state 1.
- `sprk_ratecardrate` (UserOwned): `sprk_ratecard` Lookup -> sprk_ratecard, ApplicationRequired, relationship `sprk_ratecard_ratecardrate_ratecard` cascade Delete = Cascade, Assign = Cascade, Reparent = Cascade; `sprk_timekeeper` -> sprk_timekeeper; `sprk_approvedrate`, `sprk_rackrate` Money precision 2 (+ `_base`); two DateOnly dates.
- `sprk_billingevent.sprk_resolvedtimekeeper` -> sprk_timekeeper (task 010) still intact.

## 6. Gotchas worth keeping

- Another entity-create-body property Dataverse ignores: the primary name's **RequiredLevel** (like MaxLength). Set both afterwards, only while the table is empty.
- A new row on the timekeeper table that names no status gets value 1 (**Approved**), because value 1 is the default status of statecode 0 (`DefaultStatus` 1). The roster writers (046, 078) and any admin default must set Pending explicitly when approval is still to be given. Not changed here (an owner call: Pending as the default would be the fail-safe choice).
- User-owned tables create `transactioncurrencyid`, `exchangerate` and `_base` companions as soon as a Money column exists.
- The rate-card relationship is parental; to delete a card its rates go with it. The two timekeeper/firm lookups use Restrict so a firm or timekeeper with rates cannot be deleted by mistake.

## 7. Quality gates and class check

- STANDARD rigor, configuration-only task (no `tests/**`, no BFF or app code): Step 9.5 code-review / adr-check skipped per the skip rule; checked by hand against ADR-002 (no plugin, business rule, calculated column or flow), CLAUDE.md §11 (justification is in the POML) and the project constraints (additive only, no delete or rename by logical name, SpaarkeMaster source untouched).
- Class check (Step 9.5 item 6): the "create-body property silently ignored" class (primary-name MaxLength, now also RequiredLevel). Scripts that create tables inline: `grep -l IsPrimaryName scripts` lists 17+ (listed in 018's notes); not opened or verified, out of scope. The shortening/required steps in this script and in `Deploy-EbillingBillingCodeSchema.ps1` are the model if any of them needs a key or a required name.
- Defect found in task 018's table (verified by a metadata read): `sprk_billingcode.sprk_code` and `sprk_billingcodeset.sprk_name` read back `RequiredLevel = None` although `Deploy-EbillingBillingCodeSchema.ps1` asks for ApplicationRequired. Filed as **DEF-006**, GitHub #1931 (`notes/defer-issues.md`); not fixed here because the script belongs to task 018.


## 8. Owner decisions applied (2026-10-15) — classification and the stub lookup

Owner decision, verbatim: *"Reuse the current shared role list and revise and extend it so that it matches the LEDES classifications; we do not want interpretation; there's no technical reason to deviate."* The shared list is the global choice `sprk_timekeeperrole`. A second owner decision approved deleting `sprk_timekeeper.sprk_invoice`. The script `Deploy-EbillingTimekeeperRateCardSchema.ps1` now does both (the `-ApplyClassification` switch is gone; classification is always applied; `-RemoveStubInvoiceLookup` performs the delete after its guards).

### 8.1 Inventory of `sprk_timekeeperrole` before the change

Columns bound to the global choice (from `RetrieveDependenciesForDelete`, component type 9, plus a repo grep): `sprk_billingevent.sprk_timekeeperrole`, `sprk_contactorganization.sprk_role`, `sprk_userprofile.sprk_primaryrole`. Row counts per value in dev (all rows, inactive included; the choice is also read by hard-coded value maps in `userProfileService.ts` and `StatedProfileReader.cs`, which only map the 14 original values):

| Value | Label | ExternalValue (before) | billingevent | contactorganization | userprofile |
|---|---|---|---|---|---|
| 100000000 | Senior Partner | - | 0 | 1 | 0 |
| 100000001 | Partner | - | 0 | 1 | 0 |
| 100000002 | Senior Associate | - | 0 | 0 | 0 |
| 100000003 | Associate | - | 0 | 0 | 0 |
| 100000004 | Paralegal | - | 0 | 0 | 0 |
| 100000005 | Specialist | - | 0 | 0 | 0 |
| 100000006 | Other | - | 0 | 0 | 0 |
| 100000007 | Practice Support | - | 0 | 0 | 0 |
| 100000008 | Administrator | - | 0 | 0 | 0 |
| 100000009 | Legal Operations | - | 0 | 0 | 1 |
| 100000010 | Senior Counsel | - | 0 | 0 | 0 |
| 100000011 | General Counsel | - | 0 | 0 | 0 |
| 100000012 | Counsel | - | 0 | 0 | 0 |
| 100000013 | Associate General Counsel | - | 0 | 0 | 0 |

(`sprk_billingevent` has 1 row and no role value; `sprk_contactorganization` 2 rows, holding values 100000000 and 100000001; `sprk_userprofile` 1 row, holding 100000009.)

### 8.2 Revision

The choice now holds the 20 LEDES law-firm classifications (LOC Revised Timekeeper Classification Codes, ledes.org, revised 6/2023; `notes/research/ledes-utbms-ocg-reference.md` section 2): label = LEDES name, ExternalValue = LEDES code. An existing option was matched only where it is the same thing by name: Partner (PARTNR), Associate (ASSOC), Paralegal (PARALG), Other (NBOTHR). Their labels already were the LEDES names, so only the ExternalValue was set and no integer value or label changed. The other 16 LEDES classifications are new options numbered 100000014 to 100000029 in LEDES-list order (full table in `docs/data-model/sprk_ratecard.md`). Judgment calls kept literal: Counsel is not Of Counsel (a Counsel can be in-house), Practice Support is not Legal Assistant, Administrator is not Clerk or Secretary, Specialist is not any one of the six specialist codes, so none of them was relabelled. NBOTHR "Other" is matched by name; the LEDES text for NBOTHR is "Other" (the reference note adds "non-billing system user").

### 8.3 After: 30 options (20 LEDES + 10 kept)

| Value | Label | ExternalValue | LEDES? |
|---|---|---|---|
| 100000001 | Partner | PARTNR | existing, matched |
| 100000003 | Associate | ASSOC | existing, matched |
| 100000004 | Paralegal | PARALG | existing, matched |
| 100000006 | Other | NBOTHR | existing, matched |
| 100000014 | Of Counsel | OFCOUN | new |
| 100000015 | Associate Trainee | TRANEE | new |
| 100000016 | Staff Attorney | STFATT | new |
| 100000017 | Secondee | SECNDE | new |
| 100000018 | Legal Intern | LGLINT | new |
| 100000019 | Legal Assistant | LGLAST | new |
| 100000020 | Clerk or Secretary | CLKSEC | new |
| 100000021 | Librarian | LIBRRN | new |
| 100000022 | Research Specialist/Researcher | RSRCHR | new |
| 100000023 | Analyst | ANALST | new |
| 100000024 | High-Level Non-Lawyer Professional | HLNLPF | new |
| 100000025 | Legal Project Manager | LGPRMG | new |
| 100000026 | IP Agent | IPAGNT | new |
| 100000027 | Costs Counsel | CSTCNS | new |
| 100000028 | Contract Attorney | TEMPAT | new |
| 100000029 | Contract Other Staff | TEMPOS | new |

Kept, no exact LEDES equivalent, NOT deleted (deleting needs the owner's OK): Senior Partner 100000000 (1 contactorganization row), Senior Associate 100000002, Specialist 100000005, Practice Support 100000007, Administrator 100000008, Legal Operations 100000009 (1 userprofile row), Senior Counsel 100000010, General Counsel 100000011, Counsel 100000012, Associate General Counsel 100000013 (all other usage 0). The ExternalValue of an option is set through `UpdateOptionValue` (the call that also carries the label); it is eventually consistent (about 10 s), and the script reads it back before continuing.

### 8.4 Classification columns

`sprk_timekeeper.sprk_classification` and `sprk_ratecardrate.sprk_classification` created as Picklist columns bound to the global choice (metadata read: `GlobalOptionSet.Name = sprk_timekeeperrole`, `IsGlobal = True`, required level None). The script refuses (exit 2) a same-named column bound to another choice.

### 8.5 Stub lookup `sprk_timekeeper.sprk_invoice` deleted (owner-approved)

Guards run in the script before the delete: `sprk_timekeeper` rows 0 and rows holding `sprk_invoice` 0 (exact counts); `RetrieveDependenciesForDelete` for the column (type 2) 0 and for the relationship `sprk_Invoice_Timekeeper_1n` (type 10) 0. A separate read found that none of the 4 timekeeper system forms or 7 views references `sprk_invoice`, and no personal views exist. The relationship was deleted (the column and its `sprk_invoicename` companion went with it); the column then reads 404. The Invoice main form carried one hidden (`Show="false"`) navigation entry `nav_sprk_Invoice_Timekeeper_1n` for that relationship; the platform did not remove it, so it was removed from the form XML (234 characters) and `sprk_invoice` was published, to avoid a dangling relationship in task 029's export. `sprk_invoicelineitem` is untouched (task 011).

### 8.6 Runs

| Run | Result |
|---|---|
| `-WhatIf -RemoveStubInvoiceLookup` | 18 created, 4 changed (3 ExternalValues + the delete), 0 writes, exit 0. |
| Apply | 16 options inserted, 3 ExternalValues set (Partner's had been set by an earlier probe), 2 columns created, relationship deleted, published, verified (all 20 LEDES options with value, label and code; all 20 expected columns), exit 0. Usage report printed for the 10 kept options. |
| Re-run | `created : 0  changed : 0  skipped : 51`, "nothing changed - no publish", exit 0. |

Files: `scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1`, `docs/data-model/sprk_ratecard.md`, `docs/data-model/sprk_financial-related-entities.md` (Timekeeper rows), `docs/data-model/INDEX.md`. Not changed: anything under `src/dataverse/solutions/SpaarkeMaster/` (task 029 exports the revised choice, the two columns and the removed lookup). Follow-ups for others: the Billing Event row of `sprk_financial-related-entities.md` still lists the original 7 options of `sprk_timekeeperrole`; `userProfileService.ts` (`PRIMARY_ROLE_OPTIONS`) and `StatedProfileReader.cs` hold the original 14 only, so the user-profile picker does not offer the new LEDES options. Neither is wrong for existing data.

### 8.7 Update 2026-10-15 (D-84): the 10 non-LEDES options are deleted

The "Kept, no exact LEDES equivalent, NOT deleted" options in 8.3 and the follow-up about the user-profile picker are superseded by decision D-84. `Deploy-EbillingUserProfileRoleChoice.ps1` (run against dev on 2026-10-15) created the global choice `sprk_userprofilerole` ("User Profile Role") with the original 14 values and labels, recreated `sprk_userprofile.sprk_primaryrole` on it (same logical name, schema name `sprk_PrimaryRole`, display name, required level None; back on the User Profile main form at the same position; the single stored value Legal Operations restored, pre-drop state in `notes/d84-userprofile-primaryrole-predrop.json`), and then deleted the 10 non-LEDES options from `sprk_timekeeperrole` after checking that no row held one. `sprk_timekeeperrole` now has exactly the 20 LEDES options of 8.3 with their ExternalValue codes; the columns still bound to it are `sprk_billingevent.sprk_timekeeperrole`, `sprk_contactorganization.sprk_role`, `sprk_timekeeper.sprk_classification` and `sprk_ratecardrate.sprk_classification`. The inventory in 8.1 is the state before D-84; its `sprk_userprofile.sprk_primaryrole` column is no longer bound to this choice. `userProfileService.ts` and `StatedProfileReader.cs` are unchanged and still match the 14 options of `sprk_userprofilerole`.

rework: fix-round-1 task 017
