# UX Brief — External Access Platform R2 (Module-Host SPA + Legal Front Door)

> **Status**: LOCKED (owner-approved, 2026-08-06, during `/project-pipeline` planning)
> **Purpose**: North-star UX definition that gates the P0 prototype and every downstream frontend
> build task. Production frontend tasks (P1/P3) cite this brief + the approved prototype as their
> build target — no net-new UX is invented downstream.
> **Grounding**: internal precedent only — `spaarke-prototype/projects/2025-01-corporate-legal-home`
> (design language) + the Spaarke shared component library. No external/market research (owner
> decision: ~10s-of-users/month enterprise tool; anchor to established internal patterns).

---

## 1. Design principles (inherited from `corporate-legal-home` + Spaarke standards)

1. **Fluent v9 only, dark-mode-first.** Semantic tokens (`makeStyles`/`tokens`); **zero** hardcoded
   hex/RGB/shadow/typography (ADR-021). Verified in light, dark, **and** Teams theme
   (light/dark/high-contrast).
2. **Build on the shared library — never hand-roll** (CLAUDE.md §11). Every surface composes
   `@spaarke/ui-components` primitives. New bespoke UI must justify itself against an existing
   component first.
3. **Hub, not destination.** The launcher routes to self-contained modules; modules don't deep-nest.
   (Mirrors corporate-legal-home's "workspace is a hub" navigation contract.)
4. **One shell; the identity plane is invisible.** A CIAM outside-counsel user and a workforce
   employee get the *same* chrome. Plane only surfaces at sign-in — never in module UI.
5. **Entitlement-honest.** A user sees only the cards/routes they're entitled to — no
   disabled-but-visible teasers. Reinforces the server-truth model (NFR-06 / NFR-08); the client
   never implies access it can't back.

## 2. Personas → primary goal → success signal

| Persona | Plane | Primary goal | Success signal |
|---|---|---|---|
| **Outside counsel** | CIAM | "Get to my assigned work fast" | Assigned Work reachable in ≤1 click from launcher; full R1 parity |
| **Internal employee** (unlicensed) | Workforce SSO | "Submit an NDA / P&P request and track it" | Submits a typed request + upload and sees only their own requests, in one uninterrupted flow |
| **Core-user admin** | MDA | "Grant/revoke module + record access without curl" | Grant/revoke round-trip from a UI; no PowerShell/API-only step |

## 3. Per-surface interaction pattern + shared-component mapping

| Surface | Interaction pattern | Shared components (reuse) |
|---|---|---|
| **Sign-in / realm chooser** (browser) | Explicit "My organization / Partner" chooser (spec assumption; email-domain sniffing deferred). Teams = silent SSO, no chooser. | `external-spa` auth bootstrap; Fluent v9 `Button`; theme from `@spaarke/ui-components` |
| **Home card launcher** | M365-waffle-style entitlement-gated grid of module cards; graceful empty-state when none entitled | `ActionCardRow` + `ActionCard` (icon+label; hover/focus/keyboard/dark-mode built in) |
| **Shell chrome** | Header (logo, `ThemeToggle`, user menu) + light nav; Teams-embedded hides redundant chrome | extend R1 `AppHeader` / `NavigationBar`; `ThemeToggle` / `useTheme` |
| **Assigned Work** (Outside Counsel) | R1 workspace refactored to a registered module — reuse as-is, R1 parity | R1 `WorkspaceHomePage` / `ProjectPage` / `DocumentLibrary` |
| **Legal Front Door — typed intake** | **`WizardModal`** multi-step: pick request type → typed form → review → submit → confirmation. Single-step types use `FormModal`. | `WizardModal`, `FormModal` (from `SprkModal`); prototype seed uses `multi-step-dialog-variant-template.tsx` |
| **My requests** | List + status chips → detail; filtered **server-side** to `requester == caller` (Tier-2) | Fluent `DataGrid`/list + status `Badge`; `PreviewModal` for submitted docs |
| **Document upload** | Inline drop/pick → app-only SPE stream; explicit progress + error states | reuse R1 `DocumentUploadPage` pattern; `BrowseModal` where picking existing |
| **NDA workflow status** | Submitter sees a read-only status timeline up to "approved + ready for signature" (e-sign deferred, FR-15) | Fluent timeline/steps; `ConfirmModal` on submit |
| **Core-user admin UI** | MDA command-bar surface → grant/revoke dialog | **`AccessGrantModal`** (direct reuse), `ChoiceModal` / `ConfirmModal` |

### Opportunistic cleanup (do while in the code, §11)
Migrate R1's hand-rolled dialogs to presets: `external-spa` `AiToolbar` result dialog → `PreviewModal`;
`InviteUserDialog` → `FormModal`/`WizardModal`.

## 4. Required states for EVERY surface (design-review checklist)

Loading · empty · error · **unentitled/denied** · dark mode · **Teams theme** · keyboard-only ·
**narrow width** (Teams personal-tab). A surface is not "done" in the prototype until all nine are
demonstrated.

## 5. Explicitly NOT designed here

- **Legal-reviewer queue UX** — stays in the existing internal MDA (FR-21); R2 builds no new
  review surface.
- **E-signature UX** — deferred beyond R2 (FR-15).
- **E-billing module** — R3.
- **Self-service CIAM public sign-up** — deferred (all onboarding admin-initiated or workforce-SSO).

---

## 6. How this brief is used

- **P0 prototype** (`spaarke-prototype`, via `/prototype-experiment-init` + `/prototype-harness-extend`
  on existing `_infra` mocks/factories) builds each Section-3 surface to the Section-4 state
  checklist, using the Section-3 shared components. Owner visual-approval closes P0.
- **P1/P3 production frontend tasks** cite this brief + the approved prototype as the build target.
  Any deviation from a shared component must clear the §11 three-question gate in the task's
  `<justification>`.
