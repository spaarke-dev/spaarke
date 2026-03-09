# End-to-End Validation — SemanticSearch Code Page

> **Date**: 2026-02-25
> **Environment**: https://spaarkedev1.crm.dynamics.com
> **Tester**: _________________

---

## Pre-requisites

| Deployment | Status | Notes |
|-----------|--------|-------|
| Code Page (sprk_semanticsearch) | Built (awaiting manual upload) | `src/client/code-pages/SemanticSearch/out/sprk_semanticsearch.html` |
| BFF API (spe-api-dev-67e2xz) | Deployed + Healthy | GET /healthz → 200 |
| Sitemap Entry | Pending manual config | See instructions in deployment-log.md |

---

## Section 1: Navigation

| # | Test | Result | Notes |
|---|------|--------|-------|
| 1.1 | Navigate to Semantic Search via sitemap left navigation | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 1.2 | Navigate via command bar button (if added) | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 1.3 | URL param: `?query=lease&domain=documents` loads correctly | ☐ PASS / ☐ FAIL / ☐ SKIP | |

## Section 2: Document Search (Default Domain)

| # | Test | Result | Notes |
|---|------|--------|-------|
| 2.1 | Query "commercial lease" → results appear in grid | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 2.2 | Document columns: Title, Document Type, File Type, Matter, Score | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 2.3 | Status bar shows total count and search time | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 2.4 | Filter by Document Type → results filtered | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 2.5 | Filter by Date Range → results filtered | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 2.6 | Clear filters → full results return | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 2.7 | Scroll to bottom → infinite scroll loads more | ☐ PASS / ☐ FAIL / ☐ SKIP | |

## Section 3: Domain Tab Switching

| # | Test | Result | Notes |
|---|------|--------|-------|
| 3.1 | Switch to "Matters" tab — query preserved, Matter results | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 3.2 | Matter columns: Matter Number, Matter Type, Status, Score | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 3.3 | Switch to "Projects" tab — Project results + columns | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 3.4 | Switch to "Invoices" tab — Invoice results + columns | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 3.5 | Switch back to "Documents" — Document results return | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 3.6 | File Type filter hidden on non-Document tabs | ☐ PASS / ☐ FAIL / ☐ SKIP | |

## Section 4: Graph View

| # | Test | Result | Notes |
|---|------|--------|-------|
| 4.1 | Switch to graph view via toggle button | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 4.2 | Graph renders with cluster + record nodes | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 4.3 | Cluster nodes show category label + count | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 4.4 | Click cluster → expands to show record nodes | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 4.5 | Change cluster category → graph re-clusters | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 4.6 | Switch back to grid → same results displayed | ☐ PASS / ☐ FAIL / ☐ SKIP | |

## Section 5: Saved Search Favorites

| # | Test | Result | Notes |
|---|------|--------|-------|
| 5.1 | Execute search with filters applied | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 5.2 | Save search as "Test Search 1" | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 5.3 | Clear search state (refresh) | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 5.4 | Load "Test Search 1" → query + filters restored | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 5.5 | Delete "Test Search 1" → removed from dropdown | ☐ PASS / ☐ FAIL / ☐ SKIP | |

## Section 6: Command Bar Actions

| # | Test | Result | Notes |
|---|------|--------|-------|
| 6.1 | Select 1 row → Delete button enabled | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 6.2 | Select multiple rows → multi-select state | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 6.3 | Click Email a Link → email client opens | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 6.4 | Click Reindex (Documents only) → action triggered | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 6.5 | Click Refresh → results re-fetched | ☐ PASS / ☐ FAIL / ☐ SKIP | |

## Section 7: Entity Record Dialog

| # | Test | Result | Notes |
|---|------|--------|-------|
| 7.1 | Click document row → entity dialog opens | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 7.2 | Dialog shows sprk_document entity form | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 7.3 | Close dialog → search results preserved | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 7.4 | Click matter row → sprk_matter form opens | ☐ PASS / ☐ FAIL / ☐ SKIP | |

## Section 8: Dark Mode

| # | Test | Result | Notes |
|---|------|--------|-------|
| 8.1 | Append `?theme=dark` → dark mode loads | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 8.2 | All text legible (no contrast issues) | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 8.3 | Filter pane, tabs, command bar, grid themed | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 8.4 | Graph view renders correctly in dark mode | ☐ PASS / ☐ FAIL / ☐ SKIP | |

## Section 9: DocRelViewer Regression

| # | Test | Result | Notes |
|---|------|--------|-------|
| 9.1 | DocRelViewer relationship grid renders | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 9.2 | Column sorting works | ☐ PASS / ☐ FAIL / ☐ SKIP | |
| 9.3 | Row click opens record | ☐ PASS / ☐ FAIL / ☐ SKIP | |

---

## Summary

| Section | Total | Pass | Fail | Skip |
|---------|-------|------|------|------|
| 1. Navigation | 3 | | | |
| 2. Document Search | 7 | | | |
| 3. Domain Switching | 6 | | | |
| 4. Graph View | 6 | | | |
| 5. Saved Searches | 5 | | | |
| 6. Command Bar | 5 | | | |
| 7. Entity Dialogs | 4 | | | |
| 8. Dark Mode | 4 | | | |
| 9. DocRelViewer | 3 | | | |
| **Total** | **43** | | | |

---

## Fix Notes

_Document any failures and their resolutions here._

---

*Validation checklist for Task 073. Complete all sections before marking task done.*
