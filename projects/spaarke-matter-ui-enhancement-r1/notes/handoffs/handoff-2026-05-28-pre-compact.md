# Handoff — 2026-05-28 pre-compact

> **Purpose**: Continue the spaarke-matter-ui-enhancement-r1 project from this point in a fresh session. This document is the source of truth for state, queued work, and architectural decisions.

---

## Project status at handoff

**Overall**: Core project work is complete and merged to master. We're in iterative UX polish rounds on the **SemanticSearchControl PCF** based on the user's UAT feedback. The project has shifted from greenfield delivery into a tight UAT/polish loop.

**Tasks**: 31 of 34 task POMLs are ✅ in `tasks/TASK-INDEX.md`. Remaining 3 are user-owned (Phase 6 form XML, Phase 7 task 074 cross-cutting UAT, Phase 8 task 090 project wrap-up).

**Branch**: `work/spaarke-matter-ui-enhancement-r1` is at `b921bc63` (v1.1.50). Core project work + first 4 polish rounds (v1.1.45..v1.1.49) ARE merged to `master` (merge commit `b451bbe1`). v1.1.50 IS pushed to origin but NOT yet merged to master.

**Worktree**: `c:/code_files/spaarke-wt-spaarke-matter-ui-enhancement-r1` (worktree of `C:/code_files/spaarke/.git`).

---

## SemanticSearchControl PCF version history

Each round was packaged as a fresh PCF solution ZIP for the user to import manually. The user runs `pac solution delete --solution-name SpaarkeSemanticSearch` then `pac solution import` to clear Dataverse control cache between rounds. ZIPs live at `src/client/pcf/SemanticSearchControl/Solution/bin/`.

| Version | Commit | Scope | Notes |
|---|---|---|---|
| v1.1.44 | (initial Phase 7 build) | Initial Phase 4-deploy version | Built per pcf-deploy skill |
| **v1.1.45** | `4a1a3f91` | 9 UX items round 1 | Footer cleanup; bulk-action bar inline; CommandBar visibility fix; 480px Name col (later); DataGrid migration with resizable; row height +56px |
| **v1.1.46** | `981d1579` | 5 FilePreviewDialog items | Hidden iframe scrollbar; 960→1280px width; section spacing; X close removed; Prev/Next nav + ←/→ keyboard |
| **v1.1.47** | `88cd4b3a` | 4 items | Name col default 360→480px; `showViewToggle`+`defaultView` PCF props; ResultCard prototype redesign (hatched preview, tiered match badge, large file icon); 3-dot menu cell right-aligned |
| **v1.1.48** | `0ef88e8c` | Cache override | Same code as v1.1.47; pure version bump to force Dataverse + browser cache to pick up the new manifest; built per full pcf-deploy skill compliance |
| **v1.1.49** | `3e6e8a4c` | 9 items round 4 + 1 add-on | Card-view checkbox top-left; single consolidated toolbar; X clear removed; icon-only view toggle with Tooltips; **Item 8 BFF investigation found by-design (not bug)** → client-side `searchUnion` merge; 2-button green/blue scope toggle; lazy-load IntersectionObserver on card grid; AND: Prev/Next relocated from footer to title-bar (just before ⋮) with subtle `<Divider vertical />` |
| **v1.1.50** | `b921bc63` | 8 items round 5 | ListView lazy-load sentinel; list+card preview dialog unified at host level; column refactor (Document/Relationship/Similarity/Type/AI/Menu); Relationship + Similarity Badge pills (semantic = marigold+%; associated = brand-blue no text); AI Sparkle icon in list view; AI summary section hidden in preview modal; Send Email modal-over-modal via shared `SendEmailDialog`; **Item 8 shared-FilePreview: Option B chosen** (deferred to v1.1.51) |

**Current installed version**: User imported v1.1.50.

---

## v1.1.51 follow-up queue

When the user signals to proceed with v1.1.51, the following are queued (recorded in v1.1.50 commit message + `FilePreviewDialog.tsx` header):

### Primary (the user asked about this in round 5, Q9)

**FilePreview promotion (Option A deferred from v1.1.50)** — 3-5 hours:
1. Move the PCF-local `FilePreviewDialog.tsx` (with all v1.1.45-v1.1.50 enhancements: 1280px, 2-col body, Prev/Next title-bar, FR-DOC-01 menu, hidden iframe scrollbar, modal-over-modal email) into `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/`. Replaces or extends the existing 880px version.
2. Adapt prop API: the shared lib's `FilePreview` uses an `IFilePreviewServices` injection pattern; the PCF uses raw callbacks. Bridge or migrate to one pattern.
3. Rebuild shared lib `dist/` (`npm run build` in `Spaarke.UI.Components`).
4. Update SemanticSearchControl PCF to import from the new shared path (deep-path).
5. Update **Document Viewer Code Page** to also use the shared component so it inherits the same surface.
6. Verify across consumers: SemanticSearchControl, Document Viewer Code Page, Office Add-ins.

**Multi-paragraph rationale** is preserved in `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/FilePreviewDialog.tsx` lines 1-39 — read that for context.

### Secondary (smaller follow-ups)

- **SendEmailDialog sizing** — Currently caps at 520px. The modal-over-modal email composer launched from inside the 1280px FilePreviewDialog looks small by comparison. Either widen the shared `SendEmailDialog` to ~1280px OR add a `maxWidth` prop the consumer can override. Need to verify impact on other SendEmailDialog consumers first.
- **Cleanup unused styles** — Prune `summaryTldr` / `summaryBody` styles + `IFilePreviewDialogSummary` interface references in `FilePreviewDialog.tsx` (left in v1.1.50 for back-compat after AI summary section was removed in Item 6).
- **Refactor** — Extract `mergeForRelationship(prior, fresh)` helper in `SemanticSearchApiService.searchUnion` for unit-testability.
- **Type column "—" fallback** — Show `—` placeholder when both `documentType` and `fileType` are empty (currently displays blank).

### Tertiary (deferred from v1.1.50 follow-up flag)

- **List view list-view lazy-load** — IntersectionObserver was extended to ListView in v1.1.50; UAT should verify it actually triggers correctly at the right scroll positions. If anomalies, debounce or adjust threshold/rootMargin.

---

## Architecture notes / answers to recurring questions

### Q: Where does the Semantic Search PCF live and what's the file layout?

```
src/client/pcf/SemanticSearchControl/
├── SemanticSearchControl/           # Source
│   ├── ControlManifest.Input.xml    # version (location #1 of 5 for bumps)
│   ├── SemanticSearchControl.tsx    # Main host; version footer (location #2)
│   ├── components/
│   │   ├── FilePreviewDialog.tsx    # Preview modal (1280px 2-col body); CRITICAL for v1.1.51
│   │   ├── ListView.tsx             # DataGrid; resizable cols; lazy-load sentinel
│   │   ├── CommandBar.tsx           # Filter dropdowns + AssociatedOnly + view toggle
│   │   ├── BulkActionBar.tsx        # Icon-only inline; no X (v1.1.49)
│   │   ├── ResultCard.tsx           # Prototype card design (v1.1.47)
│   │   ├── ResultsList.tsx          # Card grid via CSS grid auto-fill
│   │   └── FilterPanel.tsx          # Legacy; no longer rendered (kept as rollback hatch)
│   ├── hooks/
│   │   ├── useDocumentListPrefs.ts  # localStorage view/pin/sort/colWidths per (userId, matterId)
│   │   └── useSemanticSearch.ts     # State machine + searchUnion + loadMore
│   ├── services/
│   │   └── SemanticSearchApiService.ts  # searchUnion = client-side merge of associatedOnly true+false
│   └── types/
│       └── search.ts                # SearchResult with relationship: 'associated' | 'semantic'
├── Solution/                        # Pack target
│   ├── solution.xml                 # location #3
│   ├── pack.ps1                     # location #5
│   └── Controls/sprk_Sprk.SemanticSearchControl/
│       ├── ControlManifest.xml      # location #4
│       ├── bundle.js                # Copied from out/ post-build
│       └── styles.css
└── out/                             # Build output (gitignored)
    └── controls/SemanticSearchControl/  # NOT out/controls/control/ — name matches manifest constructor
        ├── bundle.js
        └── ControlManifest.xml
```

### Q: How do PCF version bumps work?

Per pcf-deploy skill, ALL 5 locations must be updated:
1. `SemanticSearchControl/ControlManifest.Input.xml` → `version="X.Y.Z"` + `(vX.Y.Z)` in description-key
2. `SemanticSearchControl/SemanticSearchControl.tsx` → version footer string `vX.Y.Z • Built YYYY-MM-DD`
3. `Solution/solution.xml` → `<Version>X.Y.Z</Version>`
4. `Solution/Controls/sprk_Sprk.SemanticSearchControl/ControlManifest.xml` → `version="X.Y.Z"` + `(vX.Y.Z)`
5. `Solution/pack.ps1` → `$version = "X.Y.Z"`

**Critical**: location #1 must be incremented or Dataverse silently keeps the old control even after import. Location #2 is the UI footer the user sees ("v1.1.50 • Built 2026-05-28" in the lower-right of the rendered PCF).

### Q: How does the shared library factor in?

`@spaarke/ui-components` lives at `src/client/shared/Spaarke.UI.Components/`. PCFs deep-path import from `dist/` (NOT the barrel — barrel pulls Lexical which fails React 16 boundary per ADR-022). Pattern: `import { Foo } from '@spaarke/ui-components/dist/components/Foo'`.

**Must recompile `dist/` BEFORE any PCF build that depends on shared lib changes**: `cd src/client/shared/Spaarke.UI.Components && npm run build`. PCF webpack bundles pre-compiled JS from `dist/` — stale `dist/` causes silent old-code packaging.

### Q: Why does Document Relationship Viewer show 32 docs but Semantic Search shows 4?

The Semantic Search PCF dedupes by `documentId` (one row per unique document). The Document Relationship Viewer shows the join rows (one per matter-document relationship instance). Same document uploaded with 4 different dates = 4 rows in DRV, 1 row in SS. User confirmed this is expected; they'll dedupe DRV too in a separate effort.

### Q: searchUnion behavior (v1.1.49 Item 8)

User reported `associatedOnly=true` showed 8 docs (direct matter associations) but `associatedOnly=false` showed only 4 (semantic only). Agent's BFF investigation confirmed this is by design:
- `SemanticSearchService.cs:94-97` — `if (request.AssociatedOnly)` branch bypasses Azure AI Search entirely and queries Dataverse directly via `SearchAssociatedOnlyAsync` (line 618) → `GetDocumentsByMatterAsync` etc.
- `false` path goes through Azure AI Search with `parentEntityType eq ... and parentEntityId eq ...` scope filter from `BuildEntityScopeFilter` (line 117).

Two different paths return non-overlapping sets. The user expected "All Documents" to be a union. Solution: **client-side merge** via new `SemanticSearchApiService.searchUnion()` method that fires both calls in parallel and dedupes by `documentId` (associated wins on tie because it's stronger). NO BFF changes — fully client-side. UX: replaced the Switch with a 2-button toggle ("All Documents" = green; "Associated Only" = brand-blue).

---

## Other state at handoff

### Form XML (task 060)

**Owner**: The user. Not yet started per their decision in the original deploy planning.

When ready, the 5 Visual Host instances need binding to chart def lookups (record GUIDs are in [README.md handoff](../README.md) + the original deploy message):
- Matter Health Composite: `a8b8df8b-f359-f111-a825-3833c5d9bcab` (Donut)
- Matter Budget: `7bf5b79e-f359-f111-a825-3833c5d9bcab` (HSBar)
- Matter Tasks: `c4feb098-f359-f111-a825-3833c5d9bcab` (MetricCard with badge)
- Matter Next Date: `154bd4a4-f359-f111-a825-3833c5d9bcab` (DueDateCard)
- Matter Activity: `1a4bd4a4-f359-f111-a825-3833c5d9bcab` (MetricCard with descriptionColor=brand)

Each Visual Host instance: `showTitle = true` (opts into CardChrome wrapper). Layout: 2-col 66/34, left = Matter Info + Documents PCF, right = 5 stacked Visual Hosts.

### BFF deploy

**Status**: DEPLOYED to `spaarke-bff-dev` (verified live; SHA-256 hash-verified; `/healthz`+`/ping` return 200; `POST /api/documents/bulk-download` returns 401 confirming route registered with auth filter).

The polish rounds (v1.1.45..v1.1.50) did NOT require any BFF redeploy. Item 8 in v1.1.49 investigated whether the BFF had a logic bug and concluded **by design** — fix is client-side `searchUnion`.

If v1.1.51 promotion of FilePreviewDialog requires BFF endpoint changes (e.g., adding a `size` field to `SearchResult`), the BFF redeploy procedure is:
- Script: `scripts/Deploy-BffApi.ps1`
- Run with `pwsh` (NOT `powershell.exe` — see Failure Mode entry in `.claude/skills/bff-deploy/SKILL.md`)
- Pre-flight: `az webapp show -g rg-spaarke-dev -n spaarke-bff-dev --query state -o tsv`

### Skill drift fixes already shipped to master

In the BFF deploy round, we found `spe-api-dev-67e2xz`/`spe-infrastructure-westus2` no longer existed — App Service had been renamed to `spaarke-bff-dev`/`rg-spaarke-dev`. Updated:
- `scripts/Deploy-BffApi.ps1` — param defaults + doc comments
- `.claude/skills/bff-deploy/SKILL.md` — Quick Reference + curl examples + 2 new Failure Modes entries (ResourceNotFound prescribes `az webapp show` pre-flight; Get-FileHash prescribes `pwsh`)

This is in commit `6632c94d` (merged to master via `b451bbe1`).

---

## Resuming in a new session

The new session can pick up by reading:
1. **This handoff** (full state)
2. **`projects/spaarke-matter-ui-enhancement-r1/CLAUDE.md`** (project AI context)
3. **`projects/spaarke-matter-ui-enhancement-r1/current-task.md`** (active state; Quick Recovery section is updated alongside this handoff)
4. **Latest commit on `work/spaarke-matter-ui-enhancement-r1`**: `b921bc63` (v1.1.50)

### Next probable user requests

- **Continue polish to v1.1.51**: user signals UAT feedback on v1.1.50; next round bundles whatever they flag PLUS the queued v1.1.51 follow-ups (especially the FilePreview promotion if the user wants it next)
- **"merge to master"**: when UAT signs off on a version, merge `work/spaarke-matter-ui-enhancement-r1` into master (process: `cd C:/code_files/spaarke && git fetch origin && git merge --no-ff origin/work/spaarke-matter-ui-enhancement-r1 -m "Merge: ..." && git push origin master`)
- **Form XML help**: if the user starts task 060 and hits issues with chart def lookups or 2-col layout, support is needed
- **Task 074 UAT**: cross-cutting validation (axe / dark mode / NFR-05 regression smoke / App Insights events in <60s)
- **Task 090 wrap-up**: README → Complete, lessons-learned.md, `/repo-cleanup`, final merge

### Patterns established this session

- **Sub-agent dispatch for polish rounds**: 1 agent per multi-item round; agent does code + bump + build + copy artifacts; main session does pack + commit + push
- **Cache override**: when user reports "old version showing", bump version + clean rebuild + recommend `pac solution delete` before re-import
- **Verification chain for builds**: shared lib `dist/` fresh → version bump in 5 locations grep-verified → clean `out/` + `bin/` → `npm run build:prod` → bundle size ≤1 MB → copy from `out/controls/SemanticSearchControl/` → pack via `pack.ps1` → verify ZIP entry contents via PowerShell extract
- **Iconography**: 20-size icons throughout toolbar (matches existing Add20/ArrowSync20). Icon-only buttons wrapped in `<Tooltip relationship="label" content="...">` for accessibility
- **Pill convention**: Fluent v9 `Badge appearance="filled"` with semantic color tokens (success/brand/danger/warning); marigold for the medium-relevance tier

### Coding constraints (still binding)

- Per ADR-022: PCF uses React 16/17 platform-provided. No React 18 APIs.
- Per ADR-021: tokens-only colors. Zero hex/rgb. Dark mode parity required.
- Per ADR-012: shared components consumed via deep-path imports (NOT barrel) due to Lexical/React 16 boundary.
- Per ADR-028: BFF calls via `authenticatedFetch` from `@spaarke/auth`. NEVER `PublicClientApplication`, never raw bearer headers, never `accessToken` typed props.
- Spec FR-DOC-06 line 359-375 (now ~513-530 in v1.1.50): **AssociatedOnly auto-search behavior MUST be preserved verbatim** across every polish round. Tested verbatim every round.
- Per pcf-deploy skill: `npm run build:prod` (NOT `npm run build`). Bundle size sanity: 700-800 KB is current normal; if >1 MB, build:prod is misconfigured.

### Deploy notes (still binding)

- PCF imports use `pac solution delete` + `pac solution import` to clear Dataverse control cache, then user hard-refreshes browser (`Ctrl+Shift+R`)
- Both PCFs (SemanticSearchControl + VisualHost) are user-imported — main session builds + packs + provides ZIP path
- BFF deploy via `pwsh -File scripts/Deploy-BffApi.ps1` (NEVER `powershell.exe` — Get-FileHash issue)

---

## Files this session frequently modified

- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/FilePreviewDialog.tsx` (every preview-modal round)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/ListView.tsx` (column / lazy-load / preview routing rounds)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/ResultCard.tsx` (card view redesigns)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/CommandBar.tsx` (filter UX + view toggle)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/BulkActionBar.tsx` (bulk action bar)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/SemanticSearchControl.tsx` (host integration)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/SemanticSearchApiService.ts` (searchUnion in v1.1.49)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/hooks/useSemanticSearch.ts` (lazy-load state + dedupe in v1.1.49)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/hooks/useDocumentListPrefs.ts` (column widths persistence)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/types/search.ts` (relationship field in v1.1.50)

Plus the 5 version-bump files per round.

---

*This handoff captures everything needed to continue the project in a fresh session. Read it first; then read `current-task.md` Quick Recovery; then pick up wherever the user directs.*
