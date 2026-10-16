# Defer / Issue Tracking — ebilling-r1

> **Source of truth** for deferred work + newly-discovered issues in this project.
> Each entry has a paired GitHub Issue. See `/project-defer-issue-tracking` skill for the protocol.
>
> **Rollup view**: `gh issue list --label ebilling-r1` (visible to whole team via portfolio board)
> **CLAUDE.md §11 rule**: every entry MUST name a concrete behavior or contract that fails without it.

---

## Open (in priority order)

### ISS-001 — InvoiceLiveFactResolver reads non-existent sprk_invoice columns (sprk_total, sprk_status)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-15 |
| **Source** | `/project-pipeline` task authoring (POML 015 discovery) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1904 |

**Description**

`InvoiceLiveFactResolver` reads `sprk_total` and `sprk_status` from `sprk_invoice`. Neither column exists: the table has `sprk_totalamount`, and status is `statecode` / `statuscode` plus the legacy `sprk_invoicestatus`. Requesting a column that doesn't exist fails the Dataverse retrieve, so the invoice live facts fail or come back empty.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Insights/LiveFacts/InvoiceLiveFactResolver.cs:70-77`, `:142-143`, `:297-316`
- `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Invoice/Entity.xml`

**Suggested fix**

Read `sprk_totalamount` and `statuscode`, in task 015's scope.

**Estimated effort**: 1–2 hours

**Blockers**: none

**Related**: #1771. The private invoice-status copies tracked there now carry an added comment: `FinancialCalculationToolHandler.cs:44-45` uses status values 100000002 and 100000003, which do not exist. Both are fixed in scope by task 015.

---
