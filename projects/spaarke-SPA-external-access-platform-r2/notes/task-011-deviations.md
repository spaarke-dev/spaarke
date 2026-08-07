# Task 011 — Deviations & Decisions (2026-08-06)

Workspace-shell scaffold (extract `external-spa` → module-host shell chassis).

## Deviations from the POML (directional step mode — noted, not silent)

1. **Build command: `npm run build`, NOT `npm run build:prod`.** The POML step 7 + `<tools>`
   prescribe `npm run build:prod` (the PCF prod-build rule from root CLAUDE.md §12). `external-spa`
   is a **Vite React code page, not a PCF** — it has no `build:prod` script; the correct production
   build is `npm run build` (`vite build`, IIFE output for the Power Pages host). **Confirmed by the
   owner** in-session. All verification used `npm run build` (green) + `npx tsc --noEmit` (clean).

2. **`OutsideCounselDashboard.tsx` created via `git mv` of the old `WorkspaceHomePage.tsx`.** The R1
   outside-counsel data dashboard was preserved intact (R1 parity, no regression) rather than
   deleted; `WorkspaceHomePage.tsx` was repurposed as the shell host. Task 016 re-homes those
   sections as entitlement-gated workspace widgets on the new chassis. It is not currently routed.

3. **Assistant pane is a PLACEHOLDER, not `SprkChat`.** POML step 4 says "mount a SprkChat
   placeholder". The shared `SprkChat` requires a live BFF session (apiBaseUrl + authenticatedFetch
   + getAccessToken) and the bounded 2-tool catalog — that wiring is FR-26 (task 051), gated by the
   P5 security spike (task 050). `AssistantPane` is a token-only visual placeholder with a disabled
   composer so the dockable-assistant chassis is demonstrable now.

4. **Lint not run locally.** `npm run lint` failed — `eslint` is not installed in `external-spa`'s
   `node_modules` (pre-existing tooling gap; the lint script exists but the dep is absent post
   `npm install`). CI runs lint. The authoritative local gate used was `npx tsc --noEmit` (clean).

5. **UI tests (`<ui-tests>`) not executed here.** They require a `--chrome` browser session; this run
   had none. They are the owner's visual step (`npm run dev`). Pixel refinement is explicitly
   deferred to the project per the POML notes.

## §11 shared-component decisions (owner CRITICAL: use shared components + standard modals)

- **Shared, reused as-is**: `ThemeToggle`, `ActionCard`/`ActionCardRow`, `SectionPanel`
  (`@spaarke/ui-components/components/WorkspaceShell/*`), `ChoiceModal` (the canonical SprkModal
  preset, ADR-050) — all deep-imported to keep the external SPA bundle Xrm-free.
- **Extended existing**: R1 `AppHeader` → branded portal ShellChrome (kept `SpaarkeLogoSvg`).
- **Bespoke, justified (§11 three-question gate)**:
  - `TabStrip` — no shared tab-strip with a SpaarkeAi-style corner-× close exists; Fluent `Tab`
    cannot nest an interactive close child without a button-in-button a11y violation; SpaarkeAi's
    `WorkspaceTabManagerComponent` is Xrm/ai-widgets-coupled and not a shared export. Ported from the
    owner-approved P0 prototype (the same documented §11 exception).
  - `PortalWorkspaceShell` — no shared tabbed+docked chassis exists (the shared `WorkspaceShell` is a
    config dashboard renderer; SpaarkeAi's `WorkspacePane` is Xrm-coupled). Built from Fluent v9
    primitives; ported from the approved prototype.
  - `useWorkspaceTabs` — reuses the SpaarkeAi `WorkspaceTabManager` PATTERN (pinned home + closable
    widget tabs) without importing the Xrm-coupled class. Task 012 swaps the placeholder open-path for
    the real entitlement-gated widget registry.

## Code-review fixes applied (Step 9.5)

- `useWorkspaceTabs`: refactored to a single atomic state object (removed a nested `setState`-in-
  updater anti-pattern; correct under React StrictMode).
- `WorkspaceHomePage`: destructured the hook return so `useCallback` deps are the stable hook
  functions (memoization was being defeated by the per-render result object).

## Auth non-regression (NFR-05 / ADR-028 A1/A2/A3)

`src/client/external-spa/src/auth/**`, `host/TeamsHostAdapter.ts`, `config.ts`, and `AuthGuard.tsx`
are untouched. `main.tsx` changed only to pass a `teamsHost` prop to `<App>`; the CIAM sessionStorage
cache config + Teams NAA bootstrap path are unchanged. `onSignOut` uses standard
`instance.logoutRedirect()` (no storage/config change).
