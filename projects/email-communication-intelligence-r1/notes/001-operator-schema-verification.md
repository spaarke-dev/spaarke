# 001 — Operator Schema Verification (LIVE `spaarkedev1`)

> **Task**: 001 (P0, read-only) · **Date**: 2026-07-29 · **Method**: Dataverse MCP `describe` + `read_query` against live `spaarkedev1`. **No schema or code changed.**
> **Verdict**: ✅ **ALL FOUR operator inputs PRESENT.** Phases 1–3 are UNBLOCKED. Several **naming deltas** below are load-bearing for tasks 010/011/020/025/030/031 — read them before building.

---

## Per-input result (PRESENT / ABSENT)

| # | Input | Result | Exact live shape |
|---|---|---|---|
| 1 | `sprk_regardingreportcard` lookup on `sprk_communication` (gates 010) | ✅ **PRESENT** | `sprk_regardingreportcard LOOKUP (GUID) → sprk_reportcard`. Exact logical name confirmed. |
| 2 | `sprk_recordtype_ref` RPTC row + `sprk_reportcardnumber` (gates 020) | ✅ **PRESENT** | RPTC row present (see roster). `sprk_reportcardnumber` field EXISTS on `sprk_reportcard` (query selected it with no "attribute not found" error; value null on the sampled row = empty data, not missing column). |
| 3 | `sprk_emailupdatefield` allow-list table (gates Phase 3 / Job B) | ✅ **PRESENT** (0 seed rows) | Table exists with ALL FR-11 columns. **Zero rows** — allow-list is empty; Phase 3 is NOT blocked (table exists), but tasks 030/031 need seed rows before Job B can propose anything. |
| 4 | `sprk_recordtype_ref` 7-core roster + data-hygiene map (gates 020) | ✅ **PRESENT** | Full 13-row roster captured verbatim below. **Known typos are now CLEAN**; the real anomaly is the contact row's `sprk_regardingfield` (characterized below). |

---

## 🔴 Load-bearing naming deltas (build against THESE, not the spec/schema-doc names)

| Consumer task | Spec / schema-doc said | **AS-BUILT live name** | Action |
|---|---|---|---|
| **030 / 031** (Job B) | `sprk_targetfield` on `sprk_emailupdatefield` | **`sprk_targetfieldlogicalname`** | Job B reads/writes the target field logical name from `sprk_targetfieldlogicalname`. |
| **011 / 025** (triage persist) | `sprk_triageobligations` (plural) | **`sprk_triageobligation`** (singular) on `sprk_communication` | Persist lean-JSON obligations to `sprk_triageobligation`. |
| **020** (identifier rung — contact) | catalog PERS row `sprk_regardingfield` | catalog says **`sprk_regardingcontact`** but the ACTUAL comm lookup is **`sprk_regardingperson`** | See "contact-row anomaly" — contact is OUT of the 7-core deterministic scope; do NOT trust `sprk_regardingcontact`. Code truth = `RegardingFieldMap.cs` (`contact → sprk_regardingperson`). |

---

## The 7 core records — identifier-rung roster (task 020) — VERBATIM + verified against live `sprk_communication`

Every one of the 7 core records: **catalog `sprk_regardingfield` MATCHES the actual `sprk_communication` lookup**, and the number field is present in the catalog. This roster is clean for reverse-lookup.

| Record | code | `sprk_recordlogicalname` | `sprk_regardingfield` (catalog) | Actual comm lookup | `sprk_regardingrecordnumberfield` | Trust |
|---|---|---|---|---|---|---|
| Matter | MTR | `sprk_matter` | `sprk_regardingmatter` | `sprk_regardingmatter` → sprk_matter ✅ | `sprk_matternumber` | ✅ clean |
| Project | PRJT | `sprk_project` | `sprk_regardingproject` | `sprk_regardingproject` → sprk_project ✅ | `sprk_projectnumber` | ✅ clean (typo fixed) |
| Invoice | INV | `sprk_invoice` | `sprk_regardinginvoice` | `sprk_regardinginvoice` → sprk_invoice ✅ | `sprk_invoicenumber` | ✅ clean |
| Work Assignment | WRK | `sprk_workassignment` | `sprk_regardingworkassignment` | `sprk_regardingworkassignment` → sprk_workassignment ✅ | `sprk_workassignmentnumber` | ✅ clean |
| Budget | BDGT | `sprk_budget` | `sprk_regardingbudget` | `sprk_regardingbudget` → sprk_budget ✅ | `sprk_budgetnumber` | ✅ clean (typo fixed) |
| Service Request | SVCR | `sprk_servicerequest` | `sprk_regardingservicerequest` | `sprk_regardingservicerequest` → sprk_servicerequest ✅ | `sprk_servicerequestnumber` | ✅ clean |
| Report Card | RPTC | `sprk_reportcard` | `sprk_regardingreportcard` | `sprk_regardingreportcard` → sprk_reportcard ✅ | `sprk_reportcardnumber` | ✅ clean |

**Non-core rows also in the catalog** (task 020 iterates the whole catalog — these are NOT in the deterministic-7 but are present): ACCT/account (`accountnumber`), ANA/sprk_analysis (`sprk_analysis_number`), DOC/sprk_document (`sprk_document_number`), EVT/sprk_event (`sprk_eventnumber`), ORG/sprk_organization (`sprk_organization_number`), PERS/contact (**number field = null**), TODO/sprk_todo (**number field = null**).

---

## Read-defensively map for task 020 (what to guard against)

1. **Known typos are NOW CLEAN.** The `schema-to-create.md` note flagged `sprk_regarrdingbudget` (double-r) and `sprk_egardingproject` (missing r) — **the live catalog shows `sprk_regardingbudget` and `sprk_regardingproject`, both correct.** The operator cleaned them. No active typo remains in any of the 7 core rows. Task 020 should STILL guard defensively (trim/null-check each `sprk_regardingfield`) but does not need a typo-normalization table.
2. **Contact-row anomaly (characterized).** Catalog PERS row: `sprk_recordlogicalname = contact` (correct, NOT `sprk_contact`), `sprk_regardingfield = sprk_regardingcontact` — **but no such lookup exists on `sprk_communication`; the real field is `sprk_regardingperson`** (per live describe + shipped `RegardingFieldMap.cs` mapping `("contact","sprk_regardingperson")`). Impact is **nil for the deterministic rung**: contact/person has `sprk_regardingrecordnumberfield = null`, so it is never a reverse-lookup target. Task 020 MUST skip catalog rows whose `sprk_regardingrecordnumberfield` is null (PERS, TODO) — which naturally excludes the anomaly. Do NOT write `sprk_regardingcontact`; if contact ever needs a regarding write, use `RegardingFieldMap.FieldFor("contact")` = `sprk_regardingperson`.
3. **Null number-field rows** (PERS, TODO) → never eligible for identifier reverse-lookup. Filter on `sprk_regardingrecordnumberfield IS NOT NULL` when building the rung roster.
4. **Roster source of truth for regarding WRITES stays `RegardingFieldMap.cs`**, not the catalog `sprk_regardingfield` column — the catalog column is a hint that agrees with code for the 7 core records but diverges for contact. Task 010 adds nothing here: `sprk_regardingreportcard` is already in a clean state on both sides (catalog row + comm lookup); 010 only adds the `("sprk_reportcard","sprk_regardingreportcard")` entry to `RegardingFieldMap.cs` (currently absent from the code list).

---

## `sprk_emailupdatefield` — FR-11 column verification (gates Phase 3)

Table `sprk_emailupdatefield` (collection `sprk_emailupdatefields`) — **all FR-11 columns present**:

| FR-11 column | Live column | Type |
|---|---|---|
| target entity → `sprk_recordtype_ref` | `sprk_targetentity` | LOOKUP → `sprk_recordtype_ref` ✅ |
| target field | **`sprk_targetfieldlogicalname`** (⚠ not `sprk_targetfield`) | NVARCHAR(100) |
| enabled | `sprk_enabled` | BIT |
| field type | `sprk_fieldtype` | CHOICE — Text(100000000)/Lookup(100000001)/Option Set(100000002)/Number(100000003)/Date Time(100000004)/Boolean(100000005)/Memo(100000006)/Currency(100000007) — matches as-built |
| require confirm | `sprk_requireconfirm` | BIT |
| extraction guidance | `sprk_extractionguidance` | MULTILINE TEXT |

**Seed rows: 0** (allow-list empty). Phase 3 is **not blocked** (table exists), but 030/031 must seed a starter allow-list (e.g. Matter→`sprk_closingdate`, Invoice→amount/due-date) before Job B can propose. Owner: `organizationid` (org-owned config). ✅ correct.

---

## Bonus findings (de-risk downstream tasks — verified live, not in the plan)

- **Task 011 is now fully verify-only.** All 6 triage fields already exist on `sprk_communication`: `sprk_triagecategory` (LOOKUP → `sprk_triagecategory` ✅), `sprk_triagepriority` (CHOICE Urgent/High/Medium/Low), `sprk_triagesummary` (MULTILINE), **`sprk_triageobligation`** (MULTILINE, singular — see delta table), `sprk_riconfidence` (DECIMAL), `sprk_reviewoutcome` (CHOICE File/Update/Route/Dismiss/Pending). Task 011 creates nothing.
- **Task 021 (C-1) does not need to add option-set values.** `sprk_associationstatus` already carries `Suggested (100000003)` + `Ambiguous (100000004)` alongside Resolved/Pending Review/Unresolved. 021 only narrows the *mapper logic* (rung 0+1 → Resolved auto-file; rung 2/3 → Suggested), not the schema.
- **`sprk_triagecategory` config table** exists as a lookup target (the FR-16 taxonomy table) — task 013 seeds rows, not schema.
- Full `sprk_emailreviewlog` (audit, task 012) was NOT described in this task's scope; task 012 should `describe tables/sprk_emailreviewlog` first — the operator reported it created, and the as-built option-sets (`sprk_actortype`, `sprk_action`) are recorded in `schema-to-create.md`.

---

## Downstream unblock status

| Task | Depends on input | Status |
|---|---|---|
| 010 (RegardingFieldMap report-card entry) | #1 `sprk_regardingreportcard` | ✅ **UNBLOCKED** — add `("sprk_reportcard","sprk_regardingreportcard")` to `RegardingFieldMap.All` |
| 011 (triage fields) | #4 + triage schema | ✅ **UNBLOCKED — verify-only** (all 6 fields live; note `sprk_triageobligation` singular) |
| 012 (`sprk_emailreviewlog`) | audit table | ✅ **UNBLOCKED** (verify-only; `describe` the table at task start) |
| 013 (taxonomy seed) | `sprk_triagecategory` table | ✅ **UNBLOCKED** (seed rows only) |
| 020 (identifier rung) | #4 roster + defensive map | ✅ **UNBLOCKED** — filter roster on `sprk_regardingrecordnumberfield IS NOT NULL`; 7 core rows clean |
| 022 (TRIAGE-EMAIL Action) | none (catalog authoring) | ✅ **UNBLOCKED** |
| 030 / 031 (Job B) | #3 `sprk_emailupdatefield` | ✅ **UNBLOCKED** — use `sprk_targetfieldlogicalname`; seed allow-list rows before proposing |

**No input ABSENT. No downstream task blocked.**
