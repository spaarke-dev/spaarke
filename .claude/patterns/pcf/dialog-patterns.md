# Dialog Patterns

## When
Opening dialogs, side panels, or standalone pages from PCF controls or Dataverse forms.

## Read These Files
1. `src/client/code-pages/DocumentRelationshipViewer/src/index.tsx` — Code Page exemplar (React 18, standalone)
2. `src/solutions/CreateMatterWizard/src/main.tsx` — Wizard dialog exemplar (navigateTo webresource)
3. `src/client/pcf/SemanticSearchControl/SemanticSearchControl/SemanticSearchControl.tsx` — PCF opening a dialog

## Constraints
- **ADR-006**: Standalone dialogs/pages → Code Page (React 18); NOT PCF wrapper + custom page
- **ADR-022**: Code Pages bundle React 18; PCF uses platform React 16 — never mix
- **ADR-021**: Dialogs must support dark mode via FluentProvider

## Key Rules
- Open Code Page: `Xrm.Navigation.navigateTo({ pageType: "webresource", webresourceName: "sprk_pagename", data: params }, { target: 2 })`
- Pass data via URL params (`data` field) — parse with `new URLSearchParams(window.location.search)`
- Code Page bootstrap: `resolveRuntimeConfig()` → `setRuntimeConfig()` → `ensureAuthInitialized()` → `render`
- Side panels: `Xrm.App.sidePanes.createPane()` with `webresourceName`
- MUST NOT use custom page + PCF wrapper pattern for standalone dialogs
