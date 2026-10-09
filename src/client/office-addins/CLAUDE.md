# CLAUDE.md — Office Add-ins Module

<!-- Change history: git log for this file; the full previous header is in .claude/archive/2026-10-07/modules/office-addins.CLAUDE.md -->
> **Last Updated**: 2026-09-30 (task 078)
> **Purpose**: Where to start reading when working in `src/client/office-addins/**`. Code is the source of truth; the architecture doc explains *why*.
> **Full architecture**: [`docs/architecture/office-outlook-teams-integration-architecture.md`](../../../docs/architecture/office-outlook-teams-integration-architecture.md) — read it before extending the add-ins.

---

## What this is

React 19 + Fluent UI v9 **task-pane add-ins** for **Outlook** and **Word**, hosted on Azure Static Web Apps. They save emails/attachments/documents to SharePoint Embedded, file them against a Dataverse record (Matter/Project/Invoice), create first-class To Dos from an email, and (Outlook) surface AI association + linked to-dos. Every backend call goes to the BFF's `/api/office/*` surface.

## Entry points (start here)

| To understand… | Read |
|---|---|
| The shell composition (tabs, auth gate, save→createTodo wiring) | `shared/taskpane/App.tsx` |
| Host mounts | `outlook/taskpane/index.tsx` · `word/taskpane/index.tsx` |
| Host abstraction (Outlook vs Word) | `shared/adapters/IHostAdapter.ts` + `HostAdapterFactory.ts`; the two live adapters are `shared/adapters/WordAdapter.ts` and `shared/adapters/OutlookAdapter.ts`, each **registered with and constructed by the factory** at its task-pane entry point (task 010 / FR-04) |
| **Auth** | `shared/services/AuthService.ts` (thin wrapper) → `@spaarke/auth` `OfficeNaaStrategy` (`src/client/shared/Spaarke.Auth/src/strategies/OfficeNaaStrategy.ts`) |
| Save flow | `components/views/SaveView.tsx` + `hooks/useSaveFlow.ts` + `components/SaveFlow.tsx` + `components/RelatedToPicker.tsx` |
| Create To Do | `components/views/CreateTodoView.tsx` (form) + `App.tsx` `handleCreateTodo` (`POST /api/office/todo`) + `services/todoChoices.ts` |
| BFF side | `src/server/api/Sprk.Bff.Api/Api/Office/*.cs` + `Services/Office/OfficeService.cs` |

## Load-bearing facts (get these wrong and you break the add-in)

1. **Auth is NAA via `@spaarke/auth`, not a bespoke MSAL service.** `AuthService` wraps `SpaarkeAuthProvider` + `OfficeNaaStrategy`. **Never** `new PublicClientApplication` / `createNestablePublicClientApplication` here (ADR-028) — MSAL construction lives inside `OfficeNaaStrategy`. Desktop/modern-web = silent NAA (`brk-multihub://${hostname}`); Office-web fallback = popup to `${origin}/auth-callback.html`. Each env must register **both** URIs as **SPA** redirects (mismatch → AADSTS7000471).
2. **Navigation is available in both hosts (task 015 / FR-03).** `App.tsx` derives `showNavigation` from `getAvailableTabs(hostType).length > 0`, not a `hostType` conditional. Word: Save + Find + Create To Do. Outlook: Save + Create To Do + Find. `TaskPaneToolbar` is the one live tab-row renderer (mounted by `TaskPaneShell`); `TaskPaneNavigation`'s own tab-row component is an intentionally-unmounted helper/test surface — see its file header.
3. **Share / Search / Recent tabs are placeholders.** Their handlers in `App.tsx` are stubs (`return []` / `console.log`). Do not assume they call the BFF.
4. **Create To Do makes a first-class `sprk_todo`** — never an `sprk_event` type "to do", never the SmartTodo popup wizard. Its regarding = the record the email was filed to (`SaveView.onSaved` → `App.savedContext`).
5. **Config is env-driven.** `BFF_API_BASE_URL` (default `spaarke-bff-dev`) and `ORG_URL` (Quick-Create deep-link; unset → safe no-op). **No hardcoded org URLs.**
6. **`todoChoices.ts` is a sanctioned duplicate** of the wizard's priority/effort score tables (`todoScoreMappings.ts`) — the add-in has no Xrm, so it mirrors the mapping (§11 justified).

## Build / typecheck / deploy

```bash
cd src/client/office-addins
npm install --legacy-peer-deps --no-audit --no-fund
npm run build:dev          # dev build; `npm run build` IS the production build — there is no `build:prod` script
npm run typecheck          # production code is clean (0 errors); errors remaining (284 as of 2026-09-10,
                            # 289 when consciously accepted 2026-09-09) are ALL in test files
                            # (__tests__ / __mocks__ / *.test.* / *.spec.*) — see the 2026-09-09 "CONSCIOUSLY
                            # ACCEPTED" decision in projects/spaarkeai-word-add-in-r1/CLAUDE.md § Decisions Made
```

- **Deploy is CI**: every merge to `master` runs GitHub Actions **`deploy-office-addins.yml`** (holds SWA secrets) and deploys the live SWA; a branch build needs `workflow_dispatch` (owner's go). It is **not** an agent-run script. Confirm green via `gh run list --workflow=deploy-office-addins.yml`.
- **Manifests — two eras, keep them apart** (full rules: architecture doc § Manifests):
  - **LIVE = the unified app package** (2026-10-03: the owner removed the TEST package and both legacy XML add-ins from the admin center; unified 1.1.1 is installed). The XML manifests (`outlook/outlook-manifest.xml`, `word/word-manifest.xml`) are legacy — not deployed; Outlook desktop may show a cached legacy button for up to 72 h after removal.
  - **Unified app package (task 078)** — ONE app for Outlook AND Word, built by `packaging/mergeUnifiedManifest.js` (tests in `packaging/__tests__/`) into `dist/spaarke/` and zipped by `scripts/Package-OfficeAddinUnified.ps1`. 3-part `UNIFIED_PACKAGE.VERSION` in `webpack.config.js`. `outlook/manifest.json` / `word/manifest.json` are the per-host SOURCES of it. Rollout: `projects/spaarkeai-word-add-in-r1/notes/078-manifest-decision.md`.
  - ⚠️ Do NOT name a folder `build/` here — the repo-root `.gitignore` ignores every `build/`, so its contents are never committed (task 078 shipped a broken commit that way).

## Conventions

- **Fluent UI v9 only** (ADR-021); Office theme drives dark mode (`hooks/useTheme.ts` / `useOfficeTheme.ts`).
- **Host-agnostic UI**: components take an `IHostAdapter`, never `Office.*` directly (that lives in the adapters).
- **BFF-thin**: the add-in owns UI + host access; all Dataverse/SPE/Graph work is the BFF's (`/api/office/*`, OBO).
- **Reuse the shared libs**: `@spaarke/auth` for auth, `@spaarke/ui-components` where a component already exists; do not fork Xrm-bound wizard components (they won't run without Xrm — recreate the layout instead).

## Tests

Component/unit tests colocate under `shared/taskpane/**`. BFF Office endpoints are covered by contract tests under `tests/integration/contract/Api/Office/` (e.g. `OfficeEndpointsContractTests`). Per the repo test policy, a new endpoint → a contract test; a fixed bug → a regression test.

**`.github/workflows/office-addins-tests.yml`** runs this package's jest suite on every PR touching `src/client/office-addins/**` and REPORTS a real pass/fail (it is not in master's required-status-check list — `Router` is the only required check per ruleset 21824191 — so it does not block a merge). The suite allow-list is `ci-gated-suites.txt` in this package.

---

*Refer to root `CLAUDE.md` for repository-wide standards, and the architecture doc above for the full picture + known pitfalls.*
