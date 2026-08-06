# Design — Spaarke External Access Platform (R2): Module-Host SPA Foundation + Legal Front Door

> **Status**: DRAFT scoping brief (input to `/design-to-spec`)
> **Author**: Owner + Claude (post-R1 scoping, 2026-07-21)
> **Predecessor**: `projects/spaarke-SPA-external-access-platform-r1/` — SHIPPED. Migrated the external Secure Project Workspace (Outside Counsel) from Power Pages + B2B guests to Azure Static Web Apps (SWA) + Entra External ID (CIAM), broker-only (ADR-028 Amendment A1). Live + owner-verified.
> **Grounding**: `notes/external-access-capability-synopsis.md` (code synopsis of what R1 shipped, cited to file:line).

---

## 1. What R2 actually is

R1 proved the pattern with **one** app (Outside Counsel). R2 turns it into a **module-host SPA platform** that serves **every non-core user** through one shell, and ships the **second capability** on it (Legal Front Door).

### The real product axis: core user vs SPA user

The boundary is **NOT internal-vs-external**. It is:

- **Core user** — a fully-licensed Power Apps user → the **model-driven app** (full Spaarke). Out of scope here.
- **SPA user** — *everyone else*: a licensed employee **without** a Power Apps license, OR a truly-external party. **If you are not a core user, you are a SPA user.**

All SPA users get **one Teams-capable module-host SPA**: a home **card launcher** that shows the **modules** the user has been granted (via a Dataverse access-control table). A module is a lazy-loaded feature. Identity plane (where the user's account lives) is a **sign-in detail**, not a product boundary.

| Module (examples) | Audience | Typical sign-in plane |
|---|---|---|
| **Assigned Work** (= R1 Outside Counsel workspace, refactored to a module) | outside counsel | CIAM |
| **E-billing** (R3) | outside vendors | CIAM |
| **NDA submission** | internal business users | Workforce SSO |
| **Policy & Procedures** | internal business users | Workforce SSO |
| **Invention submission** | internal inventors | Workforce SSO |

A user sees exactly the cards their grants allow (e.g. a law-firm ops contact → Assigned Work + E-billing; an internal inventor → Invention submission). Same shell, same access table, same BFF.

### Owner decisions (2026-07-21)
- **Program shape**: R2 = **module-host Foundation + Legal Front Door** modules; **R3 = E-billing** module.
- **Both identity planes required**: CIAM (external) **and** Workforce Entra SSO (internal-unlicensed). Legal Front Door business users are employees with M365 accounts — **workforce SSO, no CIAM account/provisioning**.
- **Teams compatibility is a first-class requirement**: the module-host shell must install as a Teams app (personal tab). Teams SSO = workforce identity; external users use the browser. Same shell, same deployment.
- **Volume**: ~10s of SPA users/month (not 100s) — per-app scoping + correct authz matter more than bulk-provisioning automation.

### One SPA, not two — identity plane handled at bootstrap
A single MSAL instance targets one authority, so the only real complexity is **which authority the shell logs a user into**:
- **In Teams** → Teams SSO = workforce identity (automatic).
- **In a browser** → home-realm discovery (email-domain or a "my organization / partner" chooser) → workforce or CIAM authority.

This is **one codebase, one SWA deployment**, *also* wrapped by a Teams manifest (a Teams app is an iframe pointing at the same content URL). The BFF already validates both schemes (workforce default + CIAM from R1). Not a one-way door: if a single unified URL is later required for all, the shell already does realm discovery.

---

## 2. What R1 shipped (the baseline — see synopsis for citations)

- **Hosting**: SWA, BrowserRouter + navigationFallback + security headers; `deploy-external-spa.yml`.
- **Identity (CIAM)**: second `"Ciam"` JwtBearer scheme pinned to `/api/v1/external`; Contact-by-`oid` (`Contact.sprk_externalobjectid`); broker-only (no OBO, app-only downstream, no B2B guest).
- **Authorization (Outside Counsel)**: `(Contact → Project)` participation grants (`sprk_externalrecordaccess`, 3 access levels → effective rights); server-enforced; Redis cache invalidation.
- **Onboarding**: `invite` / `invite-and-grant` / `grant` / `revoke` / `provision-project` — **API-only, no admin UI**.
- **Limitations R2 removes**: access is **app-agnostic + Project-shaped**; no module/app concept; grant hardwired to `sprk_projectid`; one CIAM identity plane; no admin UI; no card launcher.

---

## 3. Reuse baseline (what we build ON, per §11)

The module-host is assembled from **two donor sources + net-new**, verified against code:

| Layer | Source | Verdict | Notes (file:line) |
|---|---|---|---|
| **App frame** (standalone SPA, MSAL, router, AuthGuard, theme, deep-link, SWA hosting) | **R1 `src/client/external-spa`** | **Extend** | Closest existing frame; already a standalone MSAL SPA. Extend for dual-plane auth-bootstrap + Teams. |
| **Module registry** (lazy `register(type, meta, () => import())` / `resolve`) | `Spaarke.AI.Widgets` `WorkspaceWidgetRegistry.ts` | **Reuse pattern** | `{displayName, icon, category}` metadata ≈ module descriptor 1:1. Adopt registry; drop the PaneEventBus/tab mount machinery. |
| **Card launcher primitives** | `Spaarke.UI.Components` `WorkspaceShell/ActionCard.tsx`, `ActionCardRow.tsx` | **Reuse as-is** | Fluent v9 icon+label cards, hover/focus/keyboard/dark-mode, context-agnostic. |
| **Theme** | `@spaarke/ui-components` `useTheme`/`ThemeToggle` | **Reuse as-is** | Fluent v9 light/dark; host owns one `FluentProvider`. |
| **Auth base** | `@spaarke/auth` `AuthStrategy` | **Extend** | Pluggable strategy + config-driven authority; today workforce-only → add CIAM + per-user/per-context plane selection. |
| **Embedded-mode discipline** (host owns chrome/theme/auth; content owns itself) | `LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` | **Reuse discipline only** | Sound split for module-in-shell and shell-in-Teams. NOT the `Xrm.WebApi` contract — external/Teams hosts have no Xrm. |
| **Principal-agnostic BFF endpoints (FR-22)** — `CallerPrincipalResolver` + 2 strategies + `CallerPrincipal` + `CallerPrincipalAuthorizationFilter` + `ExternalCollaboration` dual-scheme policy | ✅ **DELIVERED by teams-app-r1 (task 025)** — `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs` etc. | **Reuse as-is + generalize** | The core BFF pattern for one-endpoint-set-per-module across planes is BUILT + tested (9761 green, CIAM preserved). R2 generalizes it into the module framework; third-plane seam ready. |
| **Workforce record-scope** — `IWorkforcePrincipalResolver` (t020) + `IAccessibleRecordSetService.ComposeAsync` (t022) | ✅ **DELIVERED by teams-app-r1** | **Reuse as-is** | The workforce Tier-2 predicate: **ADR-034** membership (systemuser) ∪ `sprk_externalrecordaccess` ∪ `sprk_standinggrant` (contact). NOT all-projects. Canonical NFR-08 worked example. |
| **Access-gated module visibility** (Tier-1 entitlement: App Role / Contact-entitlement + `/me` modules + card-gating) | — | **NET-NEW** | No *module-entitlement* gating exists today. (Record-scope Tier-2 is delivered above; module-entitlement Tier-1 is the core new surface.) |

**Do NOT fork `LegalWorkspaceApp`** (the "dashboard engine") — it hard-requires `Xrm.WebApi` + a Dataverse user GUID and cannot serve external users. It is a component/pattern donor, not a shell to adopt.

> **FR-22 status (2026-08-06)**: teams-app-r1 shipped the principal-agnostic collaboration endpoints (Option A) built to R2's guardrails — see `notes/r2-coordination-response.md`. R2 no longer *builds* FR-22; it **lifts + generalizes** it into the module framework. Follow-ups from the delivery: **D1 RESOLVED** (owner 2026-08-06) — grade workforce within-project rights by **ADR-034 role → level** (owner→FullAccess, collaborator→Collaborate, view-only→ViewOnly) for CIAM parity, implemented in F3/F5 via `WorkforcePrincipalStrategy` (P1 keeps flat Collaborate); and **D2** cleanup of the transitional `/api/v1/collab` + inert `ExternalCallerAuthorizationFilter`. Operator-gated: BFF redeploy + live Teams E2E.

---

## 4. Foundation work (the module-host platform) — R2 core

### F1 — Module registry + access-gated card launcher  ·  **the centerpiece**
- **Deliver**: the reusable module framework — a module registry (adopt `WorkspaceWidgetRegistry` pattern), a home **card launcher** (reuse `ActionCard`), and `/me`-driven visibility so a user sees only granted modules. Adding a module = register a card + a lazy route + a grant type.
- **Cost-of-doing-nothing**: without it, each capability is a separate hand-copied SPA with drift.

### F2 — Dual identity-plane auth in one shell  ·  gap G4
- **Deliver**: context/realm-aware auth bootstrap in the shell — Teams → Teams SSO (workforce); browser → home-realm discovery → workforce or CIAM authority. Extend `@spaarke/auth` with a CIAM strategy + plane selection. BFF: reuse R1's additive-scheme pattern; add a workforce-plane external-app policy alongside `CiamExternal`. **No OBO on any SPA path.**
- **Cost-of-doing-nothing**: the shell can't serve both external and internal SPA users.

### F3 — Access-control table: grant MODULES to any SPA user  ·  gaps G1+G2
- **Deliver**: generalize the grant model so it grants **module access** (not just Project), and generalizes the **grantee** to cover both a **Contact** (external/CIAM) and an **internal directory user** (workforce oid / systemuser) — because internal specialty users authenticate with their workforce identity, not a Contact. Plug per-module authz models: **granted-participation** (Assigned Work, E-billing) and **self-service submitter** (Legal Front Door — "see my own requests"). Add a BFF `/me` entitlement endpoint returning the user's modules. Redis-cached + invalidated on grant/revoke (ADR-009), extending R1.
- **Cost-of-doing-nothing**: no way to say "this user gets these modules"; the Project-grant model doesn't fit intake or internal users.

### F4 — Teams-capable module-host shell scaffold  ·  builds on R1 external-spa
- **Deliver**: extend R1's external-spa frame into the reusable shell: SWA config, BrowserRouter/deep-link, theme, BFF client, PLUS Teams JS SDK init (no-op outside Teams), Teams theme bridging (light/dark/contrast → Fluent v9), configurable `frame-ancestors` CSP (allow Teams origins), and the Teams-SSO path. Package the same SWA as a Teams app (manifest). Clean up R1's dead Power Pages proxy/config (`vite.config.ts`, `README.md`, `powerpages.config.json`).
- **Cost-of-doing-nothing**: Teams requirement unmet; every module re-solves embedding.

### F5 — Core-user admin UI  ·  gap G5 / DI-029-01
- **Deliver**: a Dataverse command-bar / model-driven admin surface (Fluent v9, dark-mode per ADR-021) for the core-user actions every module needs — grant/revoke module access, invite-and-grant / provision for Outside Counsel — reusable across modules.
- **Cost-of-doing-nothing**: no module is operable by its core-user persona without curl.

### F6 — R1 reliability + verification hardening  ·  DI-025-01 / DI-030-01 / SSPR
- Provisioner self-healing on the CIAM `POST /users` 409 window; live-E2E (wrong-issuer→401, oid-bound not email-hijacked); verify SSPR first-run.

---

## 5. Legal Front Door modules (the R2 second capability) — largely greenfield

`sprk_servicerequest` exists but is a **stub** (`sprk_name` + statecode + regarding lookups only — no submitter, request-type, or workflow-status fields). Legal Front Door needs real intake schema + modules.

### L1 — Intake data model (greenfield schema)
Extend `sprk_servicerequest` (or add child entities) with: **submitter** (workforce user), **request type** (NDA review/approval/signature · approval-to-publish · trademark search/filing · invention disclosure · policy & procedure), **status workflow**, routing/assignment to legal, per-type fields.

### L2 — Legal Front Door modules (workforce SSO, self-service submitter)
Modules registered in the shell — e.g. **NDA submission**, **Invention submission**, **Policy & Procedures** — where an employee (workforce SSO) submits a typed request, uploads documents (app-only SPE broker pattern), and tracks **their own** requests' status (F3 submitter authz).

### L3 — Legal-side processing handoff
Where legal reviews/approves/routes submitted requests (internal MDA / existing surfaces). R2 defines the intake→processing handoff; deep automation (e-signature, trademark filing) phases later.

> **Scope question for design-to-spec**: which request *types* are R2's first cut (recommend: generic intake + NDA + one more) vs. phased. NDA review/approval/signature is the richest workflow; others may start as typed intake + manual routing.

---

## 6. Cross-cutting constraints (carry forward — binding)

- **ADR-028 (+A1) broker-only** for CIAM modules: external token authenticates ONLY to the BFF, never exchanged downstream; app-only SPE/Dataverse. Workforce-SSO modules use the standard workforce path, still no Power-Apps-license dependency and no elevation.
- **§10 BFF Hygiene**: Placement Justification per BFF addition; `dotnet publish` size (≤60 MB, baseline ~49.63 MB); no new HIGH CVE; tests in `tests/unit/Sprk.Bff.Api.Tests/`; per-module endpoint groups via `Map{Module}Endpoints`.
- **§11 Component Justification**: reuse the donor components in §3 (registry pattern, `ActionCard`, theme, `@spaarke/auth`, R1 frame); the new surface (entitlement model + `/me` modules) is justified by ≥2 concrete consumers. Do NOT fork `LegalWorkspaceApp`.
- **ADR-038 testing**; **ADR-021/022 Fluent v9 + React 18**; **no plaintext secrets**; **preserve external-SPA sessionStorage** per-tab isolation for CIAM sign-in.

---

## 7. Out of scope (R2)

- **E-billing module** → R3 (data domain `sprk_invoice` exists; external vendor surface is the R3 build).
- Deep Legal Front Door workflow automation beyond R2's first-cut request types (e-signature, automated trademark filing).
- Self-service CIAM public sign-up / registration router — still deferred; all onboarding is admin-initiated or workforce-SSO.
- Core-user MDA experience; the E-3 direct-Office boundary (ADR-028 A1, permanently out).

---

## 8. Draft success criteria (design-to-spec formalizes as FRs/NFRs)

1. One module-host SPA renders a **card launcher** showing only the modules a user is **granted** via the access-control table; a user cannot reach a module they lack.
2. The shell serves **both planes**: an external user (CIAM) and an internal unlicensed employee (workforce SSO) each sign in and see their modules — and it **installs and runs as a Teams app** (workforce SSO) with correct theme + framing.
3. Legal Front Door: an employee submits a typed service request with document upload and tracks **their own** requests' status (self-service submitter authz).
4. Outside Counsel (R1) works unchanged **as a registered module**.
5. A core user grants/revokes module access **from a UI** (no curl); CIAM provisioner self-heals; live-E2E + SSPR verified.
6. Adding a **new module** is documented + largely register-a-card + lazy-route + grant-type — not a new SPA/app-reg/deploy.

---

## 9. Sequencing note (R2 is large — phase within it)

Recommend design-to-spec phase R2, e.g.: **P1** module-host foundation (F1–F4: registry + launcher + dual-plane bootstrap + Teams shell, with Outside Counsel as the first module) → **P2** access-control model + entitlement endpoint (F3) + admin UI (F5) → **P3** Legal Front Door intake MVP (L1–L2: generic + NDA) → **P4** hardening (F6) + Front Door workflow depth (L3 + more request types). Owner confirms cut lines during design-to-spec.

---

## 10. References

- R1 project + code synopsis: `projects/spaarke-SPA-external-access-platform-r1/`, `projects/spaarke-SPA-external-access-platform-r2/notes/external-access-capability-synopsis.md`
- Reuse donors: `src/client/shared/Spaarke.AI.Widgets/src/registry/WorkspaceWidgetRegistry.ts`, `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/ActionCard.tsx`, `src/client/shared/Spaarke.Auth/src/index.ts`, `src/client/external-spa/` (frame base)
- Architecture: `docs/architecture/external-access-spa-architecture.md`, `LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`, `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`; ADR-028 (+A1)
- Data models: `docs/data-model/sprk_servicerequest.md` (stub — needs intake schema), `sprk_invoice.md` (R3), `sprk_externalrecordaccess` (`src/solutions/SpaarkeCore/entities/sprk_externalrecordaccess/`)
- Teams: Teams JS SDK + Teams SSO (workforce identity), personal-tab manifest pointing at the SWA content URL
- North-star for a future public router: `projects/spaarke-self-service-registration-app`
