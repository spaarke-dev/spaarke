# sprk_ratecard and sprk_ratecardrate (rate cards); timekeeper roster additions

> **Project**: ebilling-r1, task 017 (FR-08)
> **Last Updated**: 2026-10-15
> **Script**: `scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1` (idempotent; `-WhatIf`; `-ApplyClassification` after the owner decision below)
> **Notes**: `projects/ebilling-r1/notes/017-timekeeper-ratecard-schema.md`

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
| `sprk_classification` | Choice (global `sprk_timekeeperrole`, optional) | **Held pending the owner decision** (see below); created by `-ApplyClassification`. |
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
| `sprk_classification` | Choice (global `sprk_timekeeperrole`) | **Held pending the owner decision** (see below). |

`sprk_name` stays the name. Status (`statuscode`): 1 = **Approved** (statecode 0; relabelled from "Active"; dev had 0 timekeeper rows, so no row changed meaning), 659490000 = **Pending** (statecode 0), 2 = **Inactive** (statecode 1). A new row that does not name a status defaults to value 1 (Approved), so writers set Pending explicitly.

Obsolete stub lookups `sprk_invoice` and `sprk_invoicelineitem` are still present (nothing was deleted). Dev holds no timekeeper rows and no code reads either lookup. The invoice-line-item lookup goes with its table in task 011; the `sprk_invoice` lookup needs the owner's delete decision. Invoice lines point to a timekeeper through `sprk_billingevent.sprk_resolvedtimekeeper` (task 010).

## Classification: LEDES mapping and the open decision

`sprk_classification` uses the **existing** global choice `sprk_timekeeperrole` (the choice `sprk_billingevent.sprk_timekeeperrole`, `sprk_contactorganization` and `sprk_userprofile` already share). Values (14): Senior Partner 100000000, Partner 100000001, Senior Associate 100000002, Associate 100000003, Paralegal 100000004, Specialist 100000005, Other 100000006, Practice Support 100000007, Administrator 100000008, Legal Operations 100000009, Senior Counsel 100000010, General Counsel 100000011, Counsel 100000012, Associate General Counsel 100000013.

The LEDES (LOC revised timekeeper classification, 2023) law-firm codes and their nearest value:

| LEDES code | Classification | sprk_timekeeperrole value | Fit |
|---|---|---|---|
| PARTNR | Partner | Partner (100000001) | exact |
| ASSOC | Associate | Associate (100000003) | exact |
| PARALG | Paralegal | Paralegal (100000004) | exact |
| NBOTHR | Other | Other (100000006) | exact |
| OFCOUN | Of Counsel | Counsel (100000012) | near |
| LGLAST | Legal Assistant | Practice Support (100000007) | near |
| CLKSEC | Clerk or Secretary | Administrator (100000008) | near |
| LIBRRN, RSRCHR, ANALST, HLNLPF, LGPRMG, IPAGNT | Librarian, Researcher, Analyst, High-level non-lawyer professional, Legal project manager, IP agent | Specialist (100000005) | near (lossy: six codes share one value) |
| TRANEE | Associate Trainee | none | **no equivalent** |
| STFATT | Staff Attorney | none | **no equivalent** |
| SECNDE | Secondee | none | **no equivalent** |
| LGLINT | Legal Intern | none | **no equivalent** |
| CSTCNS | Costs Counsel | none | **no equivalent** |
| TEMPAT | Contract Attorney | none | **no equivalent** |
| TEMPOS | Contract Other Staff | none | **no equivalent** |

Seven LEDES law-firm classifications have no equivalent, so the task's escalation fired: the owner chooses between reusing `sprk_timekeeperrole` (as is, or with added values) and a new choice. Until the owner decides, `sprk_classification` is **not created** on either table. Senior Partner, Senior Associate and the in-house values (Legal Operations, Senior Counsel, General Counsel, Counsel, Associate General Counsel) have no LEDES code.
