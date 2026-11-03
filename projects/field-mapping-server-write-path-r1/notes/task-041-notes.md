# Task 041 — the search index follows the content type (FR-40, #2502): notes

> Rigor FULL. Model: opus @ high, from the POML. Step mode: prescriptive. Started 2026-11-03.
> Branch: `task/fm-041`, cut from the work-branch tip 9935ef2dcf (master merged in; no open PR touches 041's files).

## Step 1 — read-only dev count (AC1)

Run 2026-11-03 ~15:40Z with the Dataverse MCP `read_query` only, against `spaarkedev1`. **No Dataverse write was made, and no chunk or column was cleaned** (owner 2026-11-02: leave old chunks).

### Catalog (`sprk_aisearchindex`)

`SELECT sprk_aisearchindexid, sprk_searchindexname, sprk_isdefault, statecode FROM sprk_aisearchindex`

| Id | Name | Default | State |
|---|---|---|---|
| `fdcc183b-8b71-f111-ab0d-7ced8ddc4cc6` | spaarke-files-index | **Yes** | Active |
| `ffcc183b-8b71-f111-ab0d-7ced8ddc4cc6` | spaarke-discovery-index | No | Active |
| `df04…`, `e104…`, `e304…`, `929a…`, `949a…`, `979a…` (6 rows) | spaarke-records-index | No | Active |
| `c104e55f-…` | spaarke-knowledge-index-v2 | No | Inactive |
| `dd04e55f-…` | spaarke-file-index | No | Inactive |

### (a) `sprk_document.sprk_searchindexname`

`SELECT sprk_searchindexname, COUNT(sprk_documentid) AS n FROM sprk_document GROUP BY sprk_searchindexname`

| Value | Rows |
|---|---|
| (null) | 64 |
| spaarke-files-index | 490 |
| **spaarke-knowledge-index-v2** | **76** |
| **spaarke-file-index** | **1** |

The total is 631. **77 stamps name an index other than files or discovery.** Escalation trigger 1 says to record these and continue, and the owner said to leave them. These are historical stamps for retired indexes, and 041 changes none of them.

### (b) `_sprk_ai_search_index_value` on the six search-indexed tables

`SELECT sprk_ai_search_index, COUNT(<id>) AS n FROM <table> GROUP BY sprk_ai_search_index`

| Table | null | = files-index row `fdcc183b` | Another catalog row |
|---|---|---|---|
| sprk_matter | 65 | 0 | **0** |
| sprk_project | 29 | 1 | **0** |
| sprk_workassignment | 31 | 1 | **0** |
| sprk_event | 93 | 2 | **0** |
| sprk_invoice | 12 | 0 | **0** |
| sprk_document | 631 | 0 | **0** |

### (c) Business units with an index

`SELECT businessunitid, name, sprk_searchindexname, sprk_ai_search_index FROM businessunit`

| BU | `sprk_searchindexname` | `sprk_ai_search_index` |
|---|---|---|
| Spaarke Business Unit 1 (`cb15f587-…`) | spaarke-files-index | `fdcc183b` (files) |
| Spaarke Demo (`9271b764-…`) | spaarke-files-index | null |
| Spaarke (root, `06fbf21c-…`) | spaarke-files-index | null |
| Secure Record, Spaarke Dev 1, Spaarke Test 1 | null | null |

**No business unit names an index other than `spaarke-files-index`.** Escalation trigger 1's STOP condition does not fire, and the "no behaviour change" premise of design §14.6 holds on dev.
