# FINDING — Fix #6 "reconciliation widget not in the library" — needs a pointer to WHICH library

> UAT Fix #6. The handoff premise ("the reconciliation widget isn't **registered**") is **wrong** — it IS registered. The real gap is *which curated catalog* the user's "library" reads from. Investigation below; one clarification needed before a safe change.

## Verified: the widget IS registered + deployed
- `communications-reconciliation` is registered in `register-workspace-widgets.ts` (displayName "Reconciliation", category `data`, icon, `contextType: matter-grid`, lazy `ReconciliationWorkspaceWidget` import) — added by THIS project's task 062 (`0e5d0fafd`).
- `registerWorkspaceWidgets()` IS invoked at the package barrel (`Spaarke.AI.Widgets/src/index.ts:8`).
- The registration IS on `origin/master` (so the last `Deploy SpaarkeAi` workflow run included it).
- ∴ the runtime `WorkspaceWidgetRegistry` resolves + mounts it correctly (via `widget_load` / a layout / a tab). It is NOT a missing registration.

## Why it's still not "in the library"
`WorkspaceWidgetRegistry` is a **resolve-by-type map with NO enumeration API** (`resolveWorkspaceWidget(type)` / `getWorkspaceWidgetMetadata(type)` only — no `getAll`/`list`). So no "add-widget" gallery is auto-populated from the registry; the browsable "library" must be a **separate curated catalog**. Candidates found, none of which lists reconciliation:
1. **LegalWorkspace section catalog** — `Spaarke.UI.Components/src/components/WorkspaceShell/sectionMetadataCatalog.ts` (+ `LegalWorkspace/src/sectionRegistry.ts`): the dashboard-section picker used by `WorkspaceLayoutWizard` + the LegalWorkspace-as-dashboard-engine. Has `communications` (dense list) + `email` (Outlook two-pane) sections — but **no `reconciliation` section**. Adding one here = a NEW `SectionRegistration` + catalog entry (a LegalWorkspace section, distinct from the SpaarkeAi widget).
2. **GetStartedCards / Quick Start menu** — `Spaarke.UI.Components/src/components/GetStartedCards/*` (task 064 chooser).
3. **WorkspacePaneMenu** (SpaarkeAi) — a workspace-LAYOUT picker (not per-widget).

## Clarification needed (before a safe change)
Each candidate is a DIFFERENT integration with different blast radius (LegalWorkspace section vs GetStarted card vs a layout). **Which surface did the user open when they saw "no spaarke ai app widget in the library"?** Most likely #1 (add a `reconciliation` section to `sectionMetadataCatalog` + `sectionRegistry`, mirroring the `email` section — email-communication-solution-r5 owns that file → `/conflict-check` first). Do NOT guess — the wrong catalog ships a section that renders in the wrong host.

## Note
Fixes #2/#3/#5 (reconciliation code page) do NOT depend on #6 — they deploy independently via the code-page redeploy.
