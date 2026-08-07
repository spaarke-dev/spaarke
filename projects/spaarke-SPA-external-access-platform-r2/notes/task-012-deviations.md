# Task 012 — Widget Registry + Tabbed Workspace + Quick Start + Widget Library — Notes & Deviations

> Written 2026-08-06, on task completion. Companion to `notes/task-011-deviations.md`.

## Summary

Built the external `WorkspaceWidgetRegistry` (adopting the `@spaarke/ai-widgets` PATTERN, not
importing the package — it's Xrm/SpaarkeAi-coupled), wired role-defaulted tabs on top of task
011's shell chassis, populated the pinned Quick Start tab with role-gated action cards, and built
an entitlement-gated widget library modal. All widget bodies are placeholders (task 016+ swaps
them for real data views) but carry the correct entitlement/role metadata today.

## Files

| File | Status | Purpose |
|---|---|---|
| `src/client/external-spa/src/api/me-client.ts` | NEW | `/me` entitlement contract (`Plane`, `MeEntitlementsResponse`) + mock `fetchMeEntitlements` |
| `src/client/external-spa/src/registry/widgetRegistry.ts` | NEW | `WIDGET_DEFINITIONS` (11 entries) + entitlement/default-tab/lazy-resolution helpers |
| `src/client/external-spa/src/registry/PlaceholderWidgetBody.tsx` | NEW | Shared placeholder body every widget definition resolves to today |
| `src/client/external-spa/src/components/shell/WidgetLibraryModal.tsx` | NEW | Entitlement-gated "Add widget" library (`FormModal` + `ActionCard` grid) |
| `src/client/external-spa/src/components/shell/QuickStartPane.tsx` | REWRITTEN | Role-gated Quick Start action cards + "More Services" `FormModal` |
| `src/client/external-spa/src/components/shell/index.ts` | MODIFIED | Barrel exports for `WidgetLibraryModal` + `QuickStartPaneProps` |
| `src/client/external-spa/src/pages/WorkspaceHomePage.tsx` | REWRITTEN | `/me` fetch, default-tab seeding, the `onEntitledOpenWidget` choke point, `renderWidget` |
| `src/client/external-spa/src/App.tsx` | MODIFIED (1 line) | Threads `teamsHost` prop to `WorkspaceHomePage` |

## Design decisions

### `/me` entitlement contract (`me-client.ts`)
The real entitlement endpoint is task 022 (not yet built). Per the POML's explicit instruction
("consume the real /me contract; mock the payload where the endpoint is not yet deployed"),
`fetchMeEntitlements(teamsHost)` returns a client-side mock keyed by plane. **`plane` is derived
from the REAL `teamsHost` bootstrap signal** (Teams tab → `workforce`, standalone browser →
`ciam`) already established by `main.tsx`/`TeamsHostAdapter.ts` — only `entitlements` is mocked.
A dev-only `?dv_persona=workforce|ciam|admin` override, gated behind the existing
`VITE_DEV_MOCK` flag (same convention as `mocks/mock-service.ts`), lets every persona (including
`admin`, which has no real bootstrap signal in this SPA yet) be exercised locally without a live
backend. It never activates outside `VITE_DEV_MOCK=true`.

**Known limitation (by design, not a bug)**: until task 022 ships, `entitlements` for any given
caller are the SAME canned mock per plane — not a real per-user Tier-1 lookup. This is
UX-honest-only gating (NFR-06 explicitly makes the client non-authoritative); server-side
Tier-1/Tier-2 enforcement lives in tasks 015/022, unaffected by this limitation.

### Widget registry shape
Extended the POML's literal descriptor shape (`{id,title,icon,defaultForRoles,requiredEntitlement,lazyLoader}`)
additively with:
- `planes: Plane[]` — a hard gate (the plane(s) allowed to ever see/open the widget), independent
  of the entitlement-string check. Needed because some widgets (e.g. `messages`) have NO
  `requiredEntitlement` (entitled to everyone) but ARE still plane-restricted.
- `ariaLabel` / `description` — library-card display text (mirrors the P0 prototype's registry).

11 definitions registered, matching `notes/workspace-shell-foundation.md`'s role-default table
exactly: workforce defaults = `my-requests`, `inventions`, `messages`, `policy-library`; ciam
defaults = `projects`, `matters`, `work-assignments`, `documents`, `invoices`; admin defaults =
`admin`, `messages`. `nda` is registered (workforce, `legal-front-door` entitlement) but NOT a
default — it opens only via the Quick Start "NDA Assessment" card, per the explicit note "NDA is
accessed via the Quick Start card, not a default widget."

### Entitlement gating — single choke point
`WorkspaceHomePage.onEntitledOpenWidget(widgetId)` is the ONLY path that ever calls
`useWorkspaceTabs.openTab`. Both `QuickStartPane` (card clicks) and `WidgetLibraryModal` ("Add")
route through it. It re-checks `isEntitledToWidgetId(me, widgetId)` even though both callers
already filter their own lists to entitled items — so an unentitled OR unknown id is refused at
the single choke point regardless of caller, satisfying the negative acceptance criterion ("the
tab-open path is guarded" / "not routable... even by direct route"). `getWidgetDefinition`
returns `undefined` for unregistered ids, and `isEntitledToWidgetId` treats unknown ids as
never-entitled — the two negative cases (unentitled, unknown) share one code path.

### Quick Start card → widget mapping
- **NDA Assessment** → opens the `nda` widget tab (registered, non-default).
- **Submit Policy Question** → opens the `policy-library` widget tab, per the explicit UX
  refinement "Submit Policy Question available inside the Policy Library page/widget" (rather
  than building a separate policy-question widget/modal, which would be P3 scope).
- **Invention Submission** → opens the existing `inventions` default widget (no new registration
  needed).
- **Trademark Search Request** → no widget/wizard exists yet (P3 scope); falls through to the
  "More Services" modal, shown there with a "Coming soon" badge rather than inventing an unbuilt
  widget just to give the card something to open.

### Shared-component usage (CLAUDE.md §11)
- `FormModal` (`@spaarke/ui-components/components/SprkModal`) for both "More Services" (replacing
  task 011's placeholder `ChoiceModal` — a >4-item catalog is outside `ChoiceModal`'s 2–4-choice
  scope per ADR-050) and the widget library — mirrors the approved P0 prototype's own choice of
  `FormModal` for both surfaces.
- `ActionCard` / `ActionCardRow` / `SectionPanel` (`@spaarke/ui-components/components/WorkspaceShell/*`,
  deep-imported per-component — NOT the barrel, which pulls in Xrm-coupled `wizardLaunchers.ts`;
  same discipline task 011 already established).
- No hand-rolled dialogs anywhere in task 012's surface.

### DRY fix during self-review
`QuickStartPane.tsx`'s local action-gating check initially duplicated `widgetRegistry.ts`'s
`isWidgetEntitled` logic verbatim. Extracted a shared `EntitlementGate` interface + `isEntitled()`
helper in `widgetRegistry.ts`; both `QuickStartPane` and `widgetRegistry`'s own `isWidgetEntitled`
now call the one implementation.

## Build verification

- `npx tsc --noEmit` (in `src/client/external-spa`): **clean, exit 0**, both before and after the
  DRY fix.
- `npm run build:prod` does not exist for this Vite Code Page (confirmed — consistent with task
  011's finding); per the orchestrating session's explicit instruction, `npm run build` was
  intentionally NOT run here (deferred to the orchestrating session's end-of-wave build to avoid
  a `dist/` race with the other Group B tasks running in parallel).
- `npm run lint` could not run — the external-spa's ESLint config predates ESLint 9's flat-config
  requirement (`eslint.config.js` missing); this is a pre-existing project condition, not
  introduced by this task.

## Quality gates (Step 9.5, FULL rigor)

- **code-review** (self-invoked): one Warning (the DRY duplication above) — fixed. No Critical
  findings. No hardcoded colors, no Xrm coupling, no try/catch-log-rethrow, no null-checks on
  non-nullable types found in the new/changed files.
- **adr-check** (self-invoked): grep sweep across all task-012 files for hardcoded hex, Fluent v8
  imports (`@fluentui/react'`), raw `Authorization:`/`fetch(` patterns, `PublicClientApplication`,
  and `accessToken: string` — zero matches. Clean on ADR-021 (Fluent v9 tokens), ADR-050 (canonical
  modal shell), ADR-028 (auth contract — this task doesn't touch auth acquisition at all), and
  ADR-022 (React-19 Code Page, not a PCF).

## Acceptance criteria — status

| # | Criterion | Status |
|---|---|---|
| 1 | Role-defaulted tabs open behind the pinned Quick Start tab per persona (workforce/ciam/admin) | Met — `defaultWidgetIdsFor` + seeding effect + `selectTab(QUICK_START_TAB)` restores focus after seeding |
| 2 | Widget library lists ONLY entitled widgets | Met — `libraryWidgetsFor(me)` |
| 3 | Negative: unentitled/unknown widget id is not routable | Met — `onEntitledOpenWidget` single choke point |
| 4 | Quick Start pinned tab: role-relevant cards + "More Services" modal; tab non-closable | Met — `QuickStartPane`; non-closable already established by task 011's `PortalWorkspaceShell`/`TabStrip` (untouched) |
| 5 | Unknown widget id degrades gracefully (no crash) | Met — `renderWidget` fallback to `PlaceholderWidgetBody` "Widget unavailable" |
| 6 | Dark mode + Teams theme: zero hardcoded hex | Met — semantic tokens only, verified by grep |

## What was NOT built (explicitly out of scope, per POML)

- Real data widget bodies (task 016+).
- The real `/me` entitlement endpoint (task 022) — server-side Tier-1 enforcement.
- Full wizards for Policy Question / Invention / Trademark Search (P3, FR-23–27).
