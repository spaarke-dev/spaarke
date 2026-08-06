# Custom Dialogs in Dataverse Web Resources

> **Last Reviewed**: 2026-08-02
> **Reviewed By**: spaarke-modal-system P7 (task 092 retired the DOM-overlay reference impl this file used to teach)
> **Status**: Verified

## When
Use when a ribbon/web-resource command needs a dialog. **Hand-rolled DOM overlays
(`window.top.document` + `createElement` + `position:fixed`) are RETIRED and
BANNED** (spaarke-modal-system FR-18 / ADR-050 MUST NOT; the former reference
impl `showChoiceDialog()` was deleted 2026-08-02). Pick a SUPPORTED path:

- **Simple confirm / 2-3-way choice** → `Xrm.Navigation.openConfirmDialog`
  (chain two for a ternary flow) — reference: `showDocumentLockedDialog()` in
  `sprk_DocumentOperations.js` (the task-092 conversion; both copies).
- **Record/list/dialog opens** → `Xrm.Navigation.navigateTo` with the named OOB
  sizes. Source of truth: `Spaarke.UI.Components/src/utils/adapters/oobModalSizes.ts`
  (`record` 85%×85% · `create-form` 70%×80% · `wizard` 60%×70%). Ribbon JS has no
  bundler — use the literal values WITH a comment citing `oobModalSizes.ts`.
- **Rich 2-4-option choice UI (icons + descriptions)** → that UX lives in React
  surfaces via the `ChoiceModal` preset (`@spaarke/ui-components` SprkModal
  family, choice-dialog-pattern/ADR-023) — not in ribbon JS. If a ribbon command
  truly needs it, route to a React surface; do not rebuild it in DOM.

## Read These Files
1. `src/client/webresources/js/sprk_DocumentOperations.js` — `showDocumentLockedDialog()`: the supported chained-`openConfirmDialog` conversion (keep BOTH copies byte-consistent: `infrastructure/dataverse/ribbon/DocumentRibbons/WebResources/` mirrors it)
2. `src/client/shared/Spaarke.UI.Components/src/utils/adapters/oobModalSizes.ts` — the OOB named-size source of truth
3. `src/client/shared/Spaarke.UI.Components/src/components/SprkModal/presets/ChoiceModal.tsx` — the React home of the rich-choice UX
4. `src/client/shared/Spaarke.UI.Components/src/components/Wizard/WizardShell.tsx` (+ `wizardShellTypes.ts`) — multi-step wizard component (canonical shared copy — NOT the LegalWorkspace fork, see Issue #715)
5. `docs/adr/ADR-023-choice-dialog-pattern.md` — choice-UX design specs (served by `ChoiceModal`)

## Constraints
- **FR-18 / ADR-050**: MUST NOT build dialog DOM in `window.top.document`; MUST NOT hand-roll `position:fixed`/`createElement` overlays — supported `Xrm.Navigation` APIs only
- **ADR-006**: ribbon commands stay JS on supported APIs (acknowledged web-resource exception — not a PCF rewrite trigger)
- **ADR-023**: rich 2-4-option choice semantics live in `ChoiceModal` (React), not ribbon JS

## Key Rules
- Dual-copy discipline: `src/client/webresources/js/` and `infrastructure/dataverse/ribbon/DocumentRibbons/WebResources/` MUST stay in sync for shared files (known pre-existing drift is tracked — see spaarke-modal-system DEF-005)
- Ribbon-XML bindings reference function names — never rename/resignature a bound function without updating `RibbonDiff.xml`
- WizardShell auth: standalone wizard Code Pages MUST call `await initAuth({...})` from `@spaarke/auth` before rendering; inside the tree, consume tokens via `useAuth()` or `authenticatedFetch` from `@spaarke/auth` — never raw `fetch(url, { headers: { Authorization: 'Bearer ...' } })` and never `accessToken: string` props (v2 contract per [ADR-028](../../adr/ADR-028-spaarke-auth-architecture.md))
