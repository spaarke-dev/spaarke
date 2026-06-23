# Task 064 — Verify-Empty Result (FR-3H3.4, API/server cluster)

> **Task**: R3-064 (P7.1 — server-side FetchXML / OData / LINQ consumer migration)
> **Authored**: 2026-06-21
> **Status**: Final — verify-empty + close
> **Spec reference**: FR-3H3.4 (consumer migration), AC-H3.2 (acceptance gate)
> **Companion**: [`sprk-searchindexed-consumer-inventory.md`](sprk-searchindexed-consumer-inventory.md) (task 060, §3B)
> **Sibling**: Task 063 (UI cluster, parallel) — separate file scope, zero overlap

---

## 1. Decision

**VERIFY-EMPTY + CLOSE.** No in-repo server-side filter consumer of `sprk_searchindexed` exists. Per spec FR-3H3.4 in-repo scope, no migration is required. Maker-side audit (Dataverse model-driven views, advanced finds, Power Automate flows, plugin steps registered against `sprk_document`) is an operator follow-up — out of R3 in-repo scope.

---

## 2. Re-Verification (independent of task 060 inventory)

Task 060's inventory already concluded "API: 0 readers (no FetchXML/OData/LINQ filters on the field)" — re-verified independently for task 064 with targeted greps focused specifically on **filter clauses** (writers / round-trip column-selects / XML-doc comments excluded).

### 2A. Grep commands run

| Pattern | Path | Hits | Interpretation |
|---|---|---|---|
| `sprk_searchindexed` (literal, case-insensitive) | `src/server/**` | 11 hits across 5 files | All in `Spaarke.Dataverse` mapping layer (writers + round-trip GET preservation) + `RagEndpoints.cs` + `RagIndexingJobHandler.cs` XML-doc/inline-comment text — **zero filter occurrences** |
| `sprk_searchindexed` (literal) | `src/dataverse/**` | 0 | No plugin code references the field |
| `$filter.*sprk_searchindexed \| condition.*sprk_searchindexed \| where.*sprk_searchindexed \| \.SearchIndexed\s*==` | `src/**` (case-insensitive) | **0** | No filter call-sites exist |
| `SearchIndexed` (C# property symbol) | `src/server/**` | 23 hits across 5 files | All writers (`= true`), property definitions (`public bool? SearchIndexed`), round-trip column-selects (`entity.GetAttributeValue<bool>("sprk_searchindexed")`), log lines, or XML-doc comments — **zero predicates / LINQ Where / OData `eq` clauses** |

### 2B. Per-file categorization (all server-side `sprk_searchindexed` occurrences)

| File | Lines | Category | Filter? |
|---|---|---|---|
| `src/server/shared/Spaarke.Dataverse/Models.cs` | 176, 189, 303, 319 | XML-doc + property definitions (DTOs) | No |
| `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` | 612, 615, 617, 794–797 | PATCH writer + GET round-trip preservation | No |
| `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` | 122, 124, 535, 539–540, 545–546, 1108, 1111, 1113 | Column-select list + PATCH writer + GET round-trip preservation | No |
| `src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs` | 119, 489–490, 554, 565–566, 597, 709, 717–718 | XML-doc comments + writer call-sites (post-index `SearchIndexed = true`) | No |
| `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/RagIndexingJobHandler.cs` | 206, 218–219, 227 | Inline comments + writer call-site + log line | No |

**No `<condition attribute='sprk_searchindexed' .../>` element exists.**
**No `$filter=sprk_searchindexed eq …` OData string exists.**
**No `.Where(x => x.SearchIndexed)` LINQ predicate exists.**

---

## 3. Cross-Reference

- Task 060 inventory §3B explicitly documented this empty-cluster disposition: "No server-side FetchXML, OData filter, or LINQ query reads `sprk_searchindexed` to drive logic. The field is purely a tracking flag — no code branches on it."
- Task 060 inventory §5A recommended converting tasks 063 + 064 to verify-empty mode.
- Task 061 completion notes flagged "063+064 likely re-scope to 'verify-empty + escalate maker-side audit'".
- Task 062 dual-write (PATCH layer) means writers already emit both legacy (`sprk_searchindexed`) and new (`sprk_searchindexcompletedon`) on every indexing completion — so even if a future filter consumer were introduced, it could target either field with identical results during the dual-write transition.

---

## 4. Operator Follow-up (out of R3 in-repo scope)

The R3-scope satisfaction of FR-3H3.4 is bounded to the **in-repo** server-side code base. **Maker-side artifacts not tracked in this repo** may still filter on `sprk_searchindexed`, including:

- **Model-driven app views** for the `sprk_document` table (any "Indexed documents" / "Unindexed documents" saved query)
- **Advanced Find / personal views** saved by makers
- **Power Automate flows** referencing the field in a "Get rows" or "List rows" filter
- **Business rules** in the Dataverse solution targeting the field
- **Plugin steps** registered against `sprk_document` filtering on the column (none currently exist in `src/dataverse/plugins/`)
- **Solution-exported playbook JSON** if any node-graph filters on the field (none currently in `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/`)

**Recommended operator follow-up before R3 drop-old-field step** (deferred beyond R3 per spec FR-3H3.4 final sentence — "remove after consumer migration confirmed in dev/test"):

1. Export the `Spaarke` unmanaged solution from dev.
2. Grep solution XML for `sprk_searchindexed` references in `Views/*.xml`, `Workflows/*.xml`, `BusinessRules/*.xml`, `PluginAssemblies/*.xml`.
3. For each hit, file a corresponding maker-side migration item (extend filter to `sprk_searchindexed eq true OR sprk_searchindexcompletedon ne null` for the transition; switch to `sprk_searchindexcompletedon ne null` once the legacy field is retired).
4. Logged as: filed as a separate operator follow-up note (NOT a code task) — to be added to the R3 operator-followup ledger if/when discovered.

---

## 5. Acceptance

This document satisfies **FR-3H3.4** for the in-repo server-side cluster:

- ✅ All server-side filter consumers identified (re-verified independent of task 060): **0**.
- ✅ No migration code changes required.
- ✅ Maker-side audit escalation path documented for operator follow-up.
- ✅ Dual-write writer behavior (task 062) ensures both legacy and new field columns are populated, so future filter consumers can target either without breakage during transition.
- ✅ AC-H3.2 (acceptance gate) satisfied for in-repo scope: consumers migrated where present (0), and zero-consumer disposition justified.

**Task 064 status**: completed (verify-empty).
