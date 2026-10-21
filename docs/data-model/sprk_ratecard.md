# sprk_ratecard and sprk_ratecardrate (rate cards); timekeeper roster additions

> **Project**: ebilling-r1, task 017 (FR-08)
> **Last Updated**: 2026-10-15 (classification and stub-lookup decisions applied; D-84: `sprk_timekeeperrole` is LEDES-only, user-profile role has its own choice)
> **Script**: `scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1` (idempotent; `-WhatIf`; `-RemoveStubInvoiceLookup` for the owner-approved delete); D-84 follow-up: `scripts/ebilling/Deploy-EbillingUserProfileRoleChoice.ps1`
> **Notes**: `projects/ebilling-r1/notes/017-timekeeper-ratecard-schema.md`
> **Task 080 (2026-10-21)**: adds `sprk_procedureversion` to `sprk_ratecard` (script `scripts/ebilling/Deploy-EbillingProcedureSchema.ps1`; run on dev 2026-10-21 and verified).

A **rate card** carries the provenance (source document) and effective period for many **rates**. A rate is the approved hourly rate (and the firm rack rate) for one timekeeper or one classification within an effective period. The ApprovedTimekeeper and ApprovedRate rules (task 071) and roster resolution (task 046) read these tables; the rate import (task 078) writes them and enforces the overlap rule. No plugin, business rule or calculated column (ADR-002). Both tables were created additively in dev on 2026-10-15 and are in the unmanaged `SpaarkeMaster` solution; forms, views and security-role privileges arrive with task 029.

## sprk_ratecard (Rate Card)

Entity set `sprk_ratecards`. **User / team owned.** Primary name `sprk_name` (Text 200, application-required: required by the BFF write path, not enforced on API writes).

| Logical name | Type | Notes |
|---|---|---|
| `sprk_name` | Text (200), primary name | Required. |
| `sprk_firm` | Lookup -> `sprk_organization` (delete **Restrict**) | The firm the rates apply to. Relationship `sprk_organization_ratecard_firm`. |
| `sprk_matter` | Lookup -> `sprk_matter` | Optional core-record scope. At most one of `sprk_matter` / `sprk_project` / `sprk_workassignment` is set; the writer (task 078) enforces it. Relationship `sprk_matter_ratecard_matter`. |
| `sprk_project` | Lookup -> `sprk_project` | Optional core-record scope. Relationship `sprk_project_ratecard_project`. |
| `sprk_workassignment` | Lookup -> `sprk_workassignment` | Optional core-record scope. Relationship `sprk_workassignment_ratecard_workassignment`. |
| `sprk_sourcedocument` | Lookup -> `sprk_document` | The rate card document (Procedure Library). Relationship `sprk_document_ratecard_sourcedocument`. |
| `sprk_procedureversion` | Lookup -> `sprk_procedureversion` (delete **Restrict**, optional) | Task 080 (EB-D-148, model 7.2): the Procedure Version whose published rate card this is; replaces the core-record lookups as the card's link (rate cards are no longer tied to a core record, D-120). Restrict: a Procedure Version with a rate card cannot be deleted. Relationship `sprk_ratecard_procedureversion_procedureversion`. **Field-secured (BFF-only writes).** The three core lookups above and `sprk_firm` stay unchanged; no `sprk_scope` column is added, and no `sprk_createdbyperson` is added to `sprk_ratecard` or `sprk_ratecardrate` (ontology F4-1 limits it to the policy and its version). |
| `sprk_effectivefrom` | Date only | First day the card is in force. |
| `sprk_effectiveto` | Date only | Last day; empty = open ended. |

Status (`statecode` / `statuscode`):

| statuscode | Label | statecode |
|---|---|---|
| 1 | Draft (relabelled from "Active"; the default for a new card) | 0 Active |
| 659490000 | Published | 0 Active |
| 2 | Inactive | 1 Inactive |

## sprk_ratecardrate (Rate Card Rate)

Entity set `sprk_ratecardrates`. **User / team owned.** Primary name `sprk_name` (Text 200, application-required: required by the BFF write path, not enforced on API writes; composed by the rate import, e.g. timekeeper or classification and period).

| Logical name | Type | Notes |
|---|---|---|
| `sprk_name` | Text (200), primary name | Required. |
| `sprk_ratecard` | Lookup -> `sprk_ratecard` (**SystemRequired**, so the platform refuses a rate without its card on every write; **parental**: cascade delete, assign, share, unshare, reparent) | Relationship `sprk_ratecard_ratecardrate_ratecard`. Deleting a card deletes its rates. |
| `sprk_timekeeper` | Lookup -> `sprk_timekeeper` (optional; delete **Restrict**) | Empty = the rate is by classification. Relationship `sprk_timekeeper_ratecardrate_timekeeper`. |
| `sprk_classification` | Choice (global `sprk_timekeeperrole`, optional) | LEDES classification the rate applies to; see "Classification" below. |
| `sprk_approvedrate` | Money (2 decimals, 0 to 1,000,000,000) | Hourly rate the client approved. |
| `sprk_rackrate` | Money (same shape) | Firm standard (undiscounted) hourly rate. |
| `sprk_effectivefrom` | Date only | First day the rate applies. |
| `sprk_effectiveto` | Date only | Last day; empty = open ended. |

Money columns carry the platform's `transactioncurrencyid`, `exchangerate` and `_base` companions (`sprk_approvedrate_base`, `sprk_rackrate_base`).

**Overlap rule (FR-08).** Overlapping effective-date ranges for the same timekeeper or classification on one card are refused by the rate import / publish step (task 078), not by schema.

## sprk_timekeeper additions (extends the stub; no new timekeeper table)

| Logical name | Type | Notes |
|---|---|---|
| `sprk_firm` | Lookup -> `sprk_organization` (delete **Restrict**) | Relationship `sprk_organization_timekeeper_firm`. |
| `sprk_effectivefrom` | Date only | First day approved to bill for the firm. |
| `sprk_effectiveto` | Date only | Last day; empty = open ended. |
| `sprk_contact` (existing) | Lookup -> `contact` | Display name changed "Person" -> **Linked contact**; logical name unchanged. |
| `sprk_classification` | Choice (global `sprk_timekeeperrole`) | The timekeeper's LEDES classification; see "Classification" below. |

`sprk_name` stays the name. Status (`statuscode`): 1 = **Approved** (statecode 0; relabelled from "Active"; dev had 0 timekeeper rows, so no row changed meaning), 659490000 = **Pending** (statecode 0), 2 = **Inactive** (statecode 1). A new row that does not name a status defaults to value 1 (Approved), so writers set Pending explicitly.

Obsolete stub lookups: `sprk_invoice` was **deleted** on 2026-10-15 with the owner's OK (0 timekeeper rows, 0 readers, no form or view referenced it; the relationship `sprk_Invoice_Timekeeper_1n` and the hidden navigation entry on the Invoice main form went with it). The stub lookup to the superseded invoice-line-item table is still present in dev and goes with that table in task 011 (the delete needs the owner's OK: the table still holds four test rows); no code reads it. Invoice lines point to a timekeeper through `sprk_billingevent.sprk_resolvedtimekeeper` (task 010).

## Classification: the LEDES list (owner decision 2026-10-15)

`sprk_classification` (on `sprk_timekeeper` and `sprk_ratecardrate`) is a Choice bound to the **global choice `sprk_timekeeperrole`**, the role list that `sprk_billingevent.sprk_timekeeperrole` and `sprk_contactorganization.sprk_role` also use. Since D-84 (2026-10-15) these four columns are the only ones bound to it; `sprk_userprofile.sprk_primaryrole` has its own choice (see "User-profile role" below). The owner decided to reuse that list and revise and extend it so it matches the LEDES classifications, without interpretation. The script therefore makes the list hold the 20 law-firm classifications of the LOC Revised Timekeeper Classification Codes (ledes.org, revised 6/2023): **label = LEDES name, ExternalValue = LEDES code**. Existing integer values never change. An existing option is matched to a LEDES classification only where it is the same thing by name (Partner, Associate, Paralegal, Other); every other LEDES classification is a new option.

| LEDES code | Label | Value | Origin |
|---|---|---|---|
| PARTNR | Partner | 100000001 | existing |
| OFCOUN | Of Counsel | 100000014 | new |
| ASSOC | Associate | 100000003 | existing |
| TRANEE | Associate Trainee | 100000015 | new |
| STFATT | Staff Attorney | 100000016 | new |
| SECNDE | Secondee | 100000017 | new |
| LGLINT | Legal Intern | 100000018 | new |
| PARALG | Paralegal | 100000004 | existing |
| LGLAST | Legal Assistant | 100000019 | new |
| CLKSEC | Clerk or Secretary | 100000020 | new |
| NBOTHR | Other | 100000006 | existing |
| LIBRRN | Librarian | 100000021 | new |
| RSRCHR | Research Specialist/Researcher | 100000022 | new |
| ANALST | Analyst | 100000023 | new |
| HLNLPF | High-Level Non-Lawyer Professional | 100000024 | new |
| LGPRMG | Legal Project Manager | 100000025 | new |
| IPAGNT | IP Agent | 100000026 | new |
| CSTCNS | Costs Counsel | 100000027 | new |
| TEMPAT | Contract Attorney | 100000028 | new |
| TEMPOS | Contract Other Staff | 100000029 | new |

The 10 options that were **not** LEDES classifications (Senior Partner 100000000, Senior Associate 100000002, Specialist 100000005, Practice Support 100000007, Administrator 100000008, Legal Operations 100000009, Senior Counsel 100000010, General Counsel 100000011, Counsel 100000012, Associate General Counsel 100000013) were **deleted on 2026-10-15** (decisions D-83 / D-84, owner-approved) by `scripts/ebilling/Deploy-EbillingUserProfileRoleChoice.ps1`, after checking that no row in any of the four bound columns held one (`sprk_contactorganization`'s one Senior Partner row had been remapped to Partner earlier). `sprk_timekeeperrole` now holds exactly the 20 LEDES options above, each with its ExternalValue code.

## User-profile role: `sprk_userprofile.sprk_primaryrole` (D-84)

| Column | Type | Notes |
|---|---|---|
| `sprk_userprofile.sprk_primaryrole` (schema `sprk_PrimaryRole`, display "Primary Role", required level None) | Choice, bound to the new global choice **`sprk_userprofilerole`** ("User Profile Role") | The Console user's own role (in-house, e.g. General Counsel, Legal Operations). 14 options, the values and labels the column always had: 100000000 Senior Partner, 100000001 Partner, 100000002 Senior Associate, 100000003 Associate, 100000004 Paralegal, 100000005 Specialist, 100000006 Other, 100000007 Practice Support, 100000008 Administrator, 100000009 Legal Operations, 100000010 Senior Counsel, 100000011 General Counsel, 100000012 Counsel, 100000013 Associate General Counsel. |

The Console picker (`src/solutions/SpaarkeAi/src/components/assistant/userProfileService.ts`, `PRIMARY_ROLE_OPTIONS`) and `StatedProfileReader.cs` hard-code exactly these 14 values and labels, so neither changed. Reading the list from Dataverse instead of code stays open as GitHub #1954 (Console scope). The column was dropped and recreated on 2026-10-15 because a column cannot be re-bound to another choice in place; its one stored value (Legal Operations, 100000009) was saved to `projects/ebilling-r1/notes/d84-userprofile-primaryrole-predrop.json` and restored, and the column was put back on the User Profile main form at the same position.

The LEDES eDiscovery, external-provider, client-side and adjustment/flat-fee codes (DSCATT ... TPADMN, ADJSMT, FLTFEE) are not in the list: they are not law-firm timekeeper classifications for an invoice reviewed here (reference: `projects/ebilling-r1/notes/research/ledes-utbms-ocg-reference.md` section 2).
