# Task 113: remaining wizards in-app — deviations and decisions

> Date: 2026-10-08 · Branch `feat/wizards-in-app-113` (worktree `C:\wts-113`) · Base `origin/master` · Builds on task 112 (PR #1422)

## D1 — Three of the four wizards are not in the shared library

- **POML**: reuse `InAppWizardHost`, "extend it only with the adapters these wizards need".
- **Found**: only Summarize Files lives in `Spaarke.UI.Components`. Upload Documents (`DocumentUploadWizard`), Find Similar (`FindSimilarCodePage`) and the Workspace layout wizard (`WorkspaceLayoutWizard`) are code-page solutions a shared library cannot import.
- **Done**: Summarize Files is a built-in host case. The host takes an optional `renderers` prop; `SpaarkeAi/.../shell/inAppWizardRenderers.tsx` supplies the three, importing the solutions' sources (precedent: SpaarkeAi already bundles `LegalWorkspace/src`; `vite.config.ts` gained the three source paths). `registerInAppWizardHost(opener, names)` now declares which names the host can open, so a host mounted without a renderer leaves that launch on `navigateTo` (`canOpenInApp(name)` exposes the same check). The wizard code was not moved.
- Each solution gained a pure `launchParams.ts` (Upload, Layout) so the code page and the in-app renderer parse the same launch data with one function. Find Similar, Layout and Upload gained an explicit in-app mode (`inApp` / `embedded=false`): `SprkModal`/`WizardShell` modal, host `onClose` only.

## D2 — Close hacks guarded

In-app the parent-document `dialogCloseIconButton` click and `window.close()` never run: Layout wizard (cancel x, success Done, Delete), Find Similar Cancel, Upload `SuccessScreen` Done (`closeWindow`). The `navigateTo` path is unchanged (tests plant a platform close button and assert it is still clicked there).

## D3 — User-visible changes

- Summarize Files, Upload Documents, Find Similar and the Workspace layout wizard (create / edit / save-as, from the Console, Manage Workspaces and LegalWorkspace-in-Console) open in the Console's themed modal, no platform header.
- Find Similar is now `sm` (was a 60% x 70% dialog); the other three use the `wizard` named size. Find Similar's title and Cancel / Find Similar buttons now sit in the standard modal header and footer.
- Create Project and Find Similar workspace widget tabs open in-app.
- Assign Work from Quick Start now reports the created record (#1420).
- The Personalize banner's wizard opens at the `wizard` size in-app (was 80% x 80%); hostless it keeps 80% x 80%.

## D4 — Known limits (K-class)

- **K**: Find Similar's record lookup (`lookupObjects`) and its follow-on Relationship Viewer (`sprk_documentrelationshipviewer`, a non-wizard dialog, out of scope) still open platform surfaces.
- **K**: `makeStyles` in `FindSimilarCodePage/App.tsx` uses the unsupported shorthand `borderColor` (Griffel warns in dev; pre-existing).

## D5 — Defects found, not caused by this task (reported)

- **Fixed in the PR after review (no parking):** #1479 (`FindSimilarApp` ignored `documentId` / `containerId`; now read in both hosts via `launchParams.ts`, `containerId` carried but unused); the shared launch helper now logs non-cancel `navigateTo` failures with the surface name; `codePageMains.test.tsx` tests the Create Work Assignment and Find Similar `main.tsx` entry points directly.
- Pre-existing failures on master (same on a clean baseline): `buildDynamicWorkspaceConfig` case (h) (#1345); `WorkspaceLayoutWizard` rowHeight / sectionInstanceAdvanced (14 tests); AI.Widgets `ContextWidgetAdapter` and `register-workspace-widgets`; `DocumentUploadWizard` jest config cannot resolve `@spaarke/ui-components`.

## Measurements

- **SpaarkeAi bundle** (`npm run build`, clean `.vite`): 5,928,045 bytes on master `65177354f` -> 6,068,818 bytes; **+140,773 bytes (+2.4%)**. The Upload, Find Similar and Layout wizard code is now bundled into the Console (Summarize Files was already in).
- Not run: the POML `<ui-tests>` (live open of each wizard, dark mode). Task 114 deploys.
