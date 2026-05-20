# Current Task State - SemanticSearch PCF Enhancements

> **Last Updated**: 2026-04-05 22:30 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | SemanticSearch PCF UX enhancements — filter toggle, full viewer button, icon cleanup |
| **Step** | 0 of 3: Not yet started |
| **Status** | Ready to implement |
| **Next Action** | Implement 3 changes in SemanticSearch PCF, bump to v1.1.29, rebuild + pack zip |

### Files to Modify
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/FilterPanel.tsx` — replace "Document Type" dropdown with "Associated Only" toggle
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/SearchInput.tsx` — change "+ Add Document" to icon-only button
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/SemanticSearchControl.tsx` — add full viewer toolbar button, filter logic, toolbar layout
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/hooks/useSemanticSearch.ts` — or filter in component

### Three Changes Required
1. **Replace "Document Type" filter with "Associated Only" toggle** — simple boolean filter on search results. When ON, only show documents whose parentEntityId matches the current record. The search response already includes parent entity data. No new Dataverse query needed — just client-side filtering.
2. **Add "Open Full Viewer" icon button** in toolbar (next to refresh) — opens DocumentRelationshipViewer via Xrm.Navigation.navigateTo with the current record's context
3. **Change "+ Add Document" text button to icon-only** with tooltip

### Version Bump
- Current deployed: v1.1.27 (user hasn't imported v1.1.28 zip yet)
- v1.1.28 zip exists with doc counter + auth fix
- Next version for these changes: v1.1.29

### Key Context
- SemanticSearch PCF queries BFF `/api/ai/search` — returns AI Search index results scoped by entity
- The 68 results are ALL indexed documents above similarity threshold, not just the 26 Dataverse-associated ones
- "Associated Only" toggle = client-side filter on existing results, not a new API call
- DocumentRelationshipViewer code page changes (title, grid, graph) were deployed earlier today

---

## Session Summary (2026-04-03 to 2026-04-05)

### Completed This Session (condensed)

**Find Similar Wizard MVP**: BFF endpoint + frontend + deployed
**Auth Overhaul**: sessionStorage strategy, full frame walk, loginHint on ssoSilent
**BFF URL /api Fix**: buildBffApiUrl() helper + ALL legacy JS normalized (4 files)
**RAG Indexing Fix**: missing tenantId in upload orchestrator → documents now index
**Dead Code Removal**: removed legacy /api/ai/tools/document-profile/enqueue call
**VisualHost v1.3.6**: "No data available for this measure" for null fields
**RelatedDocumentCount**: v1.20.6→v1.20.9 (badge removal, /api fix, auth, viewer title)
**SemanticSearch v1.1.28**: doc counter + auth (zip ready, not yet imported by user)
**DocumentRelationshipViewer**: title, grid overlap fix, graph positions
**Ribbon Fix**: Send to Index double /api fixed in sprk_DocumentOperations.js

### All PCF Zips Available
- `VisualHostSolution_v1.3.6.zip` — uploaded ✅
- `SpaarkeRelatedDocumentCount_v1.20.9.zip` — ready for upload
- `SpaarkeSemanticSearch_v1.1.28.zip` — ready for upload (will be superseded by v1.1.29)
