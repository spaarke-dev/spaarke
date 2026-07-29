# Schema Prerequisites — Owner Worksheet (HUMAN GATE)

> **Owner action required BEFORE starting Phase 1/2 code tasks.** Task **010 (schema preflight)** verifies each item
> below exists; if any is missing it BLOCKS tasks 011/012/013/020 and does NOT create the schema itself (owner owns schema).
> Source: `spec.md` Prerequisites + design-discussion §11.7 #3. Confirmed by owner 2026-07-28.

## Legend
- **Status**: ✅ done · 🔲 to create
- All logical names are **owner-final** unless marked "verify".

---

## Table: `sprk_analysis` (existing table — ADD columns)

| # | Column (logical) | Type | Target / Options | Purpose | Status |
|---|---|---|---|---|---|
| 1 | `sprk_worktype` | **Choice** (option set) | 3 options — see below | Drives surface + tool palette + wizard branching | 🔲 |
| 2 | `sprk_regardingmatter` | **Lookup** | → `sprk_matter` | Regarding field-set (RegardingResolver); creates 1:N Matter→Analysis | 🔲 |
| 3 | `sprk_regardingproject` | **Lookup** | → `sprk_project` | Regarding field-set; creates 1:N Project→Analysis | 🔲 |
| 4 | `sprk_regardingdocument` | **Lookup** | → `sprk_document` | Regarding **context** field (separate from `sprk_documentid` SPE hop) | 🔲 |
| 5 | `sprk_description` | Text (multiline) | — | Analysis description | ✅ (created 2026-07-28) |

### `sprk_worktype` option set (item #1)

Owner sets the **integer values** (Dataverse assigns or you pick, e.g. base 3-digit); the **client type (task 011) keys on the logical labels**, so keep these three exactly:

| Option label | Suggested value | Ships this project |
|---|---|---|
| `agreement-analysis` | (owner) | **LIVE** — Agreement Review |
| `legal-research` | (owner) | Card only ("coming soon", disabled) — functional surface is sibling `research-r1` |
| `patent-application` | (owner) | Card only ("coming soon", disabled) — later |

> **Note on type choice**: spec assumes `sprk_worktype` is a **Choice** column. If you'd rather model it as a
> **reference-table lookup** (a `sprk_worktype` entity), tell me — FR-03/FR-10 + task 011/012 adjust (small change).
> Choice is the simpler default and what the plan assumes.

---

## Table: `sprk_aichatsummary` (existing table — ADD column)

| # | Column (logical) | Type | Target | Purpose | Status |
|---|---|---|---|---|---|
| 6 | `sprk_analysis` | **Lookup** | → `sprk_analysis` | Session↔Analysis binding FK (fork-on-analysis; "one Analysis → many sessions") | 🔲 |

---

## Explicitly NO CHANGE (do not touch)

- **`sprk_analysis.sprk_documentid`** — KEEP as the SPE subject-pointer (the file hop). It is a *different role* from
  `sprk_regardingdocument` (context/rollup). For a document-only analysis both may point to the same `sprk_document`
  record — owner accepted this intentional duplicate (different roles).
- **`sprk_analysis.sprk_chathistory`** — will be RETIRED by the project (task 062), not by you. Leave as-is.
- **`sprk_analysischatmessage`** — dead empty shell; the project confirms it stays unused. Do not build on it.
- **Record→Analysis subgrids** on Matter/Project forms — added by the **project** (task 051) as form customizations
  once the regarding lookups (#2–#4) exist. Not owner-pregate; the lookups above are the only owner prerequisite for them.

---

## Summary — what to create before starting

**5 new columns** (1 already done):
1. `sprk_analysis.sprk_worktype` — Choice (3 options above)
2. `sprk_analysis.sprk_regardingmatter` — Lookup → `sprk_matter`
3. `sprk_analysis.sprk_regardingproject` — Lookup → `sprk_project`
4. `sprk_analysis.sprk_regardingdocument` — Lookup → `sprk_document`
5. `sprk_aichatsummary.sprk_analysis` — Lookup → `sprk_analysis`
- (`sprk_analysis.sprk_description` — ✅ already created)

Once these exist, run **`work on task 001`** (green-baseline) — task 010 will then verify this contract and unblock the data-spine phase.
