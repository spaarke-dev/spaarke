# Current Task State - Document Relationship Viewer UX Fixes

> **Last Updated**: 2026-04-05 21:00 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | DocumentRelationshipViewer UX fixes — title, grid columns, graph layout |
| **Step** | 0 of 3: Not yet started |
| **Status** | Ready to implement |
| **Next Action** | Make 3 UX fixes in DocumentRelationshipViewer code page + bump RelatedDocumentCount PCF to v1.20.9 |

### Files Modified This Session (for this task — none yet)
- None — about to start

### Critical Context
The DocumentRelationshipViewer is opened two ways:
1. From RelatedDocumentCount PCF → FindSimilarDialog (Fluent Dialog, iframe) — title change goes in `@spaarke/ui-components/FindSimilarDialog/FindSimilarDialog.tsx`
2. From Corporate Workspace → Xrm.Navigation.navigateTo → sprk_documentrelationshipviewer

Three fixes needed:
1. **Add title "Similar Documents"** to FindSimilarDialog (Fluent Dialog wrapper in @spaarke/ui-components)
2. **Fix grid column overlap** — columns overlap when resized/dragged in RelationshipGrid
3. **Graph default positions** — source document upper-left, matter hub upper-right (not covered by settings panel)

After changes: rebuild @spaarke/ui-components dist → rebuild RelatedDocumentCount PCF v1.20.9 → pack zip for user. Also rebuild DocumentRelationshipViewer code page and deploy.

### Key File Locations
- FindSimilarDialog: `src/client/shared/Spaarke.UI.Components/src/components/FindSimilarDialog/FindSimilarDialog.tsx`
- RelationshipGrid: `src/client/code-pages/DocumentRelationshipViewer/src/components/RelationshipGrid.tsx`
- DocumentGraph: `src/client/code-pages/DocumentRelationshipViewer/src/components/DocumentGraph.tsx`
- Graph layout: `src/client/code-pages/DocumentRelationshipViewer/src/hooks/useForceLayout.ts` or similar
- RelatedDocumentCount PCF: `src/client/pcf/RelatedDocumentCount/` (bump to v1.20.9)

---

## Session Summary (2026-04-03 to 2026-04-05)

### Major Work Completed This Session

#### Find Similar Wizard MVP
1. BFF endpoint: `POST /api/ai/visualization/related-from-content` (text extract → embed → temp AI Search entry)
2. FindSimilarCodePage: document lookup + file upload dialog with @spaarke/auth bootstrap
3. Deployed BFF + code page to Dataverse

#### SSO Silent Token Fix
4. Added `loginHint` from Xrm context to `ssoSilent()` in ALL 8 auth files (shared lib + PCFs + Code Pages)

#### BFF URL `/api` Fix (Once and For All)
5. Created `buildBffApiUrl()` helper in `@spaarke/auth` + PCF shared utils — idempotent, prevents missing/duplicated `/api`
6. Fixed 2 production bugs (NextStepsStep.tsx, matterService.ts)
7. Enhanced `authenticatedFetch` resolveUrl to route through helper as safety net
8. Migrated RelatedDocumentCount hooks to use helper
9. Updated 3 documentation files (constraint, pattern, architecture doc)

#### Auth Architecture Overhaul
10. **SessionStorageStrategy** — new strategy #2 in 6-strategy cascade. Shared across ALL same-origin iframes via sessionStorage. User authenticates once; every other component reads token instantly.
11. **tokenBridge full frame walk** — readBridgeToken now walks entire frame tree (parent → grandparent → top) instead of just 1 level
12. Rebuilt and deployed DocumentRelationshipViewer code page with all auth fixes
13. Rebuilt RelatedDocumentCount v1.20.8 + SemanticSearch v1.1.27 PCFs with sessionStorage auth

#### VisualHost PCF v1.3.6
14. Added `isNull` flag to IAggregatedDataPoint in FieldPivotService
15. GaugeVisual + HorizontalStackedBar show "No data available for this measure" when all source fields are null

#### Document Form Fix
16. Diagnosed RelatedDocumentCount PCF unbound field issue (form save error) — control on form missing `datafieldname`

### Commits on master (this session)
- `9cee57cd` — feat(auth): add loginHint to ssoSilent
- `eeda645b` — fix(pcf): remove blue Badge from RelationshipCountCard (v1.20.6)
- `a3dfc473` — fix(auth): centralize BFF API URL construction with buildBffApiUrl helper
- `00366d06` — chore(pcf): bump RelatedDocumentCount to v1.20.8 with buildBffApiUrl migration
- `697a0585` → rebased — feat(auth): add sessionStorage token cache + full frame walk
- `0e446938` → rebased — fix(pcf): VisualHost v1.3.6 no data available
- `fa1a14a8` → `aa67de82` → rebased — chore(pcf): rebuild RDC + SemanticSearch with sessionStorage auth

### Deployed to Dataverse
- sprk_documentrelationshipviewer (code page) — auth fixes
- sprk_corporateworkspace (code page) — /api bug fix + auth
- sprk_documentuploadwizard + all wizard code pages — /api bug fix + auth
- sprk_findsimilar (code page) — Find Similar Wizard MVP
- VisualHost v1.3.6 PCF — user uploaded zip
- RelatedDocumentCount v1.20.8 PCF — user uploaded zip
- SemanticSearch v1.1.27 PCF — user uploaded zip
- BFF API — deployed with Find Similar endpoint

### PCF Zips Provided to User
- `VisualHostSolution_v1.3.6.zip` — uploaded ✅
- `SpaarkeRelatedDocumentCount_v1.20.8.zip` — uploaded ✅
- `SpaarkeSemanticSearch_v1.1.27.zip` — uploaded ✅
