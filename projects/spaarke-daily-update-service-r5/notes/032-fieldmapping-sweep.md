# Task 032 — UpdateRecord fieldMapping string→Choice sweep + restore `sprk_documenttype`

> **Date**: 2026-07-09 · **FR-C3** · depends on 030 ✅ · **Dataverse MCP live** (spaarkedev1)

## Restore — `sprk_documenttype` on the Profile Document node (DONE, data-verified)

- **Node**: `sprk_playbooknode = 0fa4e8db-b216-f111-8343-7c1e520aa4df` ("Update Record", `sprk_executortype = 22`, in the Profile Document playbook `18cf3cc8-02ec-f011-8406-7c1e520aa4df`).
- **Restored as `type:"choice"`** (NOT `type:"string"`), with the full options map read from live `sprk_document` metadata:
  `{ Contract:100000000, Invoice:100000001, Proposal:100000002, Report:100000003, Letter:100000004, Memo:100000005, Email:100000006, Agreement:100000007, Statement:100000008, Patent:100000009, Trademark:100000010, "Non-Disclosure Agreement":100000011, Other:100000012 }`, value binding `{{output_aiAnalysis.output.sprk_documenttype}}` (unchanged).
- **Why `type:"choice"` and not `type:"string"`**: note 06 confirms `CoerceFieldValue` **already** handled `type:"choice"` correctly *before* task 030 — 030 only added a `type:"string"` runtime safety net. So `type:"choice"` is **deploy-independent**: it works on the current spaarkedev1 BFF regardless of whether 030's coercion is live, it is the correct self-documenting authoring form (matches the new jps-validate R7-V-08 rule from task 031), and it cannot re-introduce the R7 500 the way a `type:"string"` restore could if 030 weren't deployed yet.
- **Method**: config rebuilt faithfully via script (6 original mappings preserved byte-identical + the 7th appended), applied via `mcp__dataverse__update_record`, then **read back and confirmed exact** (7 fieldMappings; documenttype = type:choice + 13-option map). Reversible: re-drop the mapping to return to the R7 6-field state.

### ⏳ Round-trip EXECUTION verification — UAT follow-up (not MCP-doable)
Criterion 3 ("end-to-end Profile Document run → read back `sprk_documenttype` = numeric enum matching the AI label") requires **executing the playbook**, which the Dataverse MCP cannot trigger (it does data CRUD + describe, not AI-pipeline runs). The data-layer restore is verified and low-risk (native `type:"choice"` path + metadata-matched options). **Confirm the end-to-end round-trip during the Document Upload smoke at the Phase B deploy/UAT (task 038)** — profile a document, then read back `sprk_document.sprk_documenttype` and confirm the enum matches the AI label. Per the task escalation trigger, if that run 500s or writes a wrong enum, STOP and re-open the defect (do NOT silently re-drop).

## Sweep — string→Choice audit of the Profile Document Update Record node

Every current fieldMapping on the node, checked against live `sprk_document` metadata (`mcp__dataverse__describe`):

| Field | mapping type | Actual column type | Verdict |
|---|---|---|---|
| sprk_filesummary | string | MULTILINE TEXT | ✅ OK (text) |
| sprk_filetldr | string | MULTILINE TEXT | ✅ OK (text) |
| sprk_filekeywords | string | MULTILINE TEXT | ✅ OK (text) |
| sprk_extractorganization | string | MULTILINE TEXT | ✅ OK (text) |
| sprk_extractpeople | string | MULTILINE TEXT | ✅ OK (text) |
| sprk_filetype | string | **NVARCHAR(10)** | ✅ OK (text — NOT a Choice; note 06's "filetype" is a plain string column) |
| **sprk_documenttype** | **choice** (restored) | **CHOICE (13 options)** | ✅ FIXED — restored as type:choice |

**No other string→Choice violation exists in this node** — `sprk_filetype` (the only non-obvious candidate) is `NVARCHAR(10)` text, not a Choice.

## Candidates — other `sprk_document` Choice columns (NOT restored; listed per constraint)

These Choice columns exist on `sprk_document` but are **not** in the Profile Document node's fieldMappings, so there is no active string→Choice bug for them here. They are candidates only if some *other* playbook maps them as `type:"string"` — flagged for a future broader sweep, not changed in this task (constraint: do not bulk-restore without evidence a mapping was previously dropped):

`sprk_classification`, `sprk_documentstatus`, `sprk_filesummarystatus`, `sprk_emaildirection`, `sprk_invoicereviewstatus`, `sprk_relationshiptype`, `sprk_sourcetype` (+ `statecode`/`statuscode`).

**Broader cross-playbook sweep (Matter/Project/Todo + all UpdateRecord nodes, per note 06 test case 4)**: not performed in this task — it requires enumerating every `sprk_playbooknode` with `sprk_executortype = 22` and describing each target entity. The **jps-validate R7-V-08 rule added in task 031** is the durable authoring-time guard that now flags this class on every future validate run; a one-time historical sweep across all playbooks can be a separate `/defer` item at wrap-up (090) if the operator wants the existing corpus swept proactively.

## BFF / build impact

**Data-only change** (a Dataverse playbook-node row edit) — **no BFF `.cs`/`.ts` source changed**, so no publish-size verification applies and no new CVE surface.
