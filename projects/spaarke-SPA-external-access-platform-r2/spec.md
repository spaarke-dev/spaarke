# Spaarke External Access Platform (R2): Module-Host SPA Foundation + Legal Front Door — AI Implementation Specification

> **Status**: Ready for Review
> **Created**: 2026-07-21
> **Source**: `projects/spaarke-SPA-external-access-platform-r2/design.md`
> **Grounding**: `notes/external-access-capability-synopsis.md` (R1 code synopsis, file:line cited)

## Executive Summary

R2 generalizes R1's single Outside-Counsel SPA into a **module-host SPA platform** serving **all non-core (SPA) users** — a Teams-capable shell whose home is a **card launcher** showing only the **modules** a user is entitled to. Entitlement is dual-plane: internal (unlicensed employee) users are entitled via **Entra App Roles** over **workforce SSO**; external users via **per-Contact** grants over **CIAM** (R1, unchanged). R2 ships the **Legal Front Door** capability (generic typed-intake framework + NDA full workflow + Policy & Procedures) as the second app on the platform, with Outside Counsel refactored into the first registered module.

## Scope

### In Scope
- **Module-host shell** (extends R1 `external-spa`): code-side **module registry** (WorkspaceWidgetRegistry pattern) + **card-launcher** home (reuse `ActionCard`) + `/me`-driven module visibility.
- **Dual identity-plane auth in one shell/deployment**: Teams → Teams SSO (workforce); browser → home-realm discovery → workforce or CIAM authority. BFF adds a workforce-plane external-app auth policy alongside R1's `CiamExternal`.
- **Teams app packaging**: same SWA wrapped by a Teams personal-tab manifest; Teams JS SDK init (no-op outside Teams), Teams theme bridging, CSP `frame-ancestors` allowing Teams.
- **Two-layer access model**: NEW **module-entitlement** layer + keep R1 `sprk_externalrecordaccess` record-participation.
  - Internal entitlement = **Entra App Roles** (per-module, assignable to groups; "any authenticated employee" fallback for org-wide modules).
  - External entitlement = **per-Contact** (curated invite), as R1.
  - `/me` **entitlement endpoint** returns the user's modules; Redis-cached + invalidated (ADR-009).
- **Lazy Contact attribution**: on an internal user's first attributed action (e.g. submitting a request), resolve-or-create their Contact by workforce oid (reuse R1 `ResolveOrCreateContact`/bind-oid); set as requester.
- **Core-user admin UI**: Fluent v9 (dark-mode) surface to grant/revoke module entitlement + record grants.
- **Legal Front Door** (workforce SSO, self-service submitter): intake schema on `sprk_servicerequest`; generic typed-intake framework; **NDA** module (full review/approval/signature); **Policy & Procedures** module; document upload via app-only SPE broker.
- **R1 hardening**: provisioner self-healing (CIAM 409); live-E2E (wrong-issuer→401, oid-bound-not-email-hijacked); SSPR first-run verification.
- **Cleanup**: remove R1's dead Power Pages proxy/config (`vite.config.ts`, `README.md`, `powerpages.config.json`).

### Out of Scope
- **E-billing module** → R3 (data domain `sprk_invoice` exists; external vendor surface is the R3 build).
- Deep Legal Front Door workflows beyond R2's first cut: **approval-to-publish, trademark search/filing, invention disclosure** (framework accommodates them; ship later). E-signature/automated-filing integrations deferred.
- Self-service CIAM public sign-up / registration router (all onboarding remains admin-initiated or workforce-SSO).
- Core-user MDA experience; **E-3 direct-Office** boundary (ADR-028 A1) — permanently out.
- Dataverse-driven module catalog (`sprk_module`) — modules are code-registered in R2.

### Affected Areas
- `src/client/external-spa/**` — extended into the reusable module-host shell (frame base).
- `src/client/shared/Spaarke.AI.Widgets/**`, `src/client/shared/Spaarke.UI.Components/**` — donor components reused (registry pattern, `ActionCard`, theme); do **not** fork `LegalWorkspaceApp`.
- `src/client/shared/Spaarke.Auth/**` — extend `AuthStrategy` for CIAM + per-context authority selection.
- `src/server/api/Sprk.Bff.Api/**` — new module-entitlement service + `/me` endpoint; workforce-plane external-app auth policy; Legal Front Door endpoint group; extend `ExternalAccessModule`/`ExternalCallerAuthorizationFilter`.
- `src/solutions/**` — new Legal Front Door module(s); admin UI (command-bar/MDA).
- Dataverse — module-entitlement schema (new), `sprk_servicerequest` intake fields (extend the stub), App Role definitions on the app registration.
- `.github/workflows/**` — per-app SWA deploy (extend `deploy-external-spa.yml`), Teams manifest packaging.

## Requirements

### Functional Requirements — Phase P1: Module-host foundation
1. **FR-01 (Module registry)**: A code-side module registry (WorkspaceWidgetRegistry pattern) registers each module as `{ id, title, icon, route, lazyLoader, requiredEntitlement }`. Acceptance: a module is added by registering it + a lazy route; unknown modules degrade gracefully.
2. **FR-02 (Card launcher)**: The shell home renders `ActionCard`s for **only** the modules the current user is entitled to per `/me`. Acceptance: a user with entitlement to {A,B} sees exactly cards A,B; C is neither shown nor routable.
3. **FR-03 (Dual-plane auth bootstrap)**: One shell/deployment selects the authority at bootstrap — in Teams → Teams SSO (workforce); in browser → home-realm discovery (chooser default) → workforce or CIAM. Acceptance: an external user (CIAM) and an internal user (workforce) each sign in and reach their launcher from the same URL.
4. **FR-04 (Teams app — build on teams-app-r1 prior art)**: The same SWA installs as a Teams personal-tab app: Teams JS SDK init (no-op outside Teams), Teams theme (light/dark/contrast) → Fluent v9, CSP `frame-ancestors` allows Teams origins (`'self' https://teams.microsoft.com https://*.cloud.microsoft`, no `X-Frame-Options`). **`teams-app-r1` has already proven this live** — host detection + Teams host adapter selecting the workforce strategy + **MSAL v5 NAA** (`createNestablePublicClientApplication`) acquiring a workforce token — so R2 adopts that host as the P1 seed rather than rebuilding it. Requires the Entra recipe in Dependencies (NAA `brk-multihub://` redirect, pre-authorized Teams client app-ids, v1-token audience). Acceptance: the app loads + authenticates (Teams SSO/NAA) inside Teams, renders in Teams dark mode, **and loads module data** (via FR-22 — not just `/me`).
5. **FR-05 (Outside Counsel as a module)**: R1's Outside Counsel workspace is refactored into a registered module ("Assigned Work") with no loss of R1 behavior. Acceptance: an outside-counsel Contact signs in (CIAM) and uses Assigned Work exactly as R1.
6. **FR-06 (Shell scaffold + cleanup)**: The shell is extracted from R1 `external-spa`; dead Power Pages proxy/config removed (`vite.config.ts` proxy, `README.md`, `powerpages.config.json`). Acceptance: build/deploy green; zero Power Pages references remain.

### Functional Requirements — Phase P2: Access-control + entitlement
7. **FR-07 (Two-layer access model)**: A NEW module-entitlement layer ("which modules a user gets") sits alongside `sprk_externalrecordaccess` (record participation). Acceptance: Front Door works with entitlement-only (no participation rows); Outside Counsel works with entitlement + Project participations.
8. **FR-08 (Internal entitlement via App Roles)**: Internal module entitlement is resolved from Entra **App Role** claims (per-module roles assignable to groups); org-wide modules may fall back to "any authenticated workforce user". **R2 ships a single `FrontDoorUser` role**, but the resolver + entitlement schema MUST be designed to support **per-module roles later without rework** (owner: "multiple by module in future — don't foreclose"). Acceptance: assigning the All-Employees group to `FrontDoorUser` entitles every member with no per-user record; the role claim is read from the token (no Graph call); adding a second per-module role later requires no resolver refactor.
9. **FR-09 (External entitlement per-Contact)**: External (CIAM) module entitlement is granted per-Contact (curated), reusing/extending R1's grant surface. Acceptance: an external Contact is entitled to Assigned Work (+ E-billing later) explicitly.
10. **FR-10 (`/me` entitlement endpoint)**: `GET /me` returns the caller's entitled modules (resolved by App Role for internal, Contact-entitlement for external) + record participations where applicable; Redis-cached, invalidated on entitlement/grant change (ADR-009). Acceptance: entitlement changes are visible within the cache-invalidation window; unentitled modules are absent.
11. **FR-11 (Lazy Contact attribution)**: On an internal user's first attributed action, the BFF resolves-or-creates a Contact by workforce oid (reuse R1 `ResolveOrCreateContact` + bind-oid to `sprk_externalobjectid`) and records it as requester. Acceptance: an employee's first submission creates/links exactly one Contact; subsequent actions reuse it; no Contact is created merely by having access.
12. **FR-12 (Core-user admin UI)**: A Fluent v9 (dark-mode, ADR-021) admin surface grants/revokes module entitlement (external) and record grants, and invokes invite-and-grant/provision for Outside Counsel. Acceptance: a core user performs grant + revoke from UI (no curl); renders correctly in dark mode.

### Functional Requirements — Phase P3: Legal Front Door intake MVP
13. **FR-13 (Intake schema)**: Extend `sprk_servicerequest` (stub today) with requester (Contact lookup), request type (option set incl. NDA, PolicyProcedures, + extensible), status workflow, and submitted-document linkage. Acceptance: a request persists requester + type + status; polymorphic regarding preserved (ADR-024).
14. **FR-14 (Generic typed-intake framework)**: A reusable intake module framework the request types plug into (typed form → submit → status). Acceptance: adding a new request type is config/schema + a form, not a new app.
15. **FR-15 (NDA module)**: NDA submission with review/approval workflow, **stopping at "approved + ready for signature"** — e-signature-provider integration is **deferred beyond R2** (owner decision). Acceptance: an employee submits an NDA request, it routes for review/approval and reaches the "ready for signature" state; signature handled out-of-band; status tracked end-to-end up to that boundary.
16. **FR-16 (Policy & Procedures module)**: A submit/read Policy & Procedures module on the framework. Acceptance: an employee submits a P&P request and views status.
17. **FR-17 (Self-service submitter authz)**: A Front Door user (workforce SSO) can create and see **only their own** requests (Tier-2 predicate `requester == caller`, one instance of the per-module record-scope mechanism in NFR-08); document upload streams **app-only** via the SPE broker (no OBO, no pointer exposure). Acceptance: user A cannot see user B's requests; an upload is app-only; authz enforced server-side (negative case included).

### Functional Requirements — Phase P4: Hardening + Front Door depth
18. **FR-18 (Provisioner self-healing)**: On CIAM `POST /users` 409 (create-ok/persist-fail window, DI-025-01), recover the existing oid by email identity and continue (persist + email). Acceptance: a re-invoke after a persist failure self-heals without manual oid binding.
19. **FR-19 (Live-E2E)**: Live tests (DI-030-01): a wrong-issuer token → 401 on external routes; an oid-bound Contact is not hijacked by a mismatched-email token. Acceptance: both pass against live CIAM + Dataverse.
20. **FR-20 (SSPR first-run)**: Verify + document the freshly-provisioned CIAM user SSPR "Forgot password" → set → first sign-in path. Acceptance: a new external user completes SSPR and signs in.
21. **FR-21 (Legal-side processing handoff — existing MDA)**: Submitted requests route/assign to legal for review **in the existing internal model-driven app** — R2 builds **no new legal-review surface** (owner decision). R2's obligation is that a submitted `sprk_servicerequest` is correctly created + routed so it surfaces to legal in the MDA. Acceptance: a submitted request appears + is actionable for legal in the existing MDA (no new review UI).

### Functional Requirements — Cross-cutting foundation (informs P1/P2; resolves the teams-app-r1 blocker)
22. **FR-22 (Principal-agnostic module endpoints)**: Each module's data endpoint group accepts the auth scheme(s) for the plane(s) it serves and resolves a **plane-agnostic caller** via a `CallerPrincipalResolver` — CIAM contact (by `sprk_externalobjectid`/oid) **OR** workforce user (systemuser/contact via the teams-app-r1 `WorkforcePrincipalResolver`) — yielding a common **accessible-record-set** on which the module's **Tier-2 predicate (NFR-08)** operates. There is **one endpoint set per module, NOT one per plane** (chosen over plane-partitioned parallel sets — the latter multiplies endpoints per plane and does not scale to N modules). This is the canonical platform pattern and it **resolves the teams-app-r1 gap** (workforce host authenticates but the CIAM-only `/api/v1/external/*` data endpoints 401 it). Concretely, R2 **dual-schemes the collaboration read + download endpoints** (accept CIAM **and** workforce) and consolidates the minimal `/api/v1/collab` group into the principal-agnostic surface. Acceptance: the same collaboration `/me` + data endpoints serve a CIAM user (browser) and a workforce user (Teams) with **identical response shape**; **existing CIAM behavior is unchanged** (regression test — FR-15-of-R1 parity); the workforce Teams host loads full module data with no 401.

### Non-Functional Requirements
- **NFR-01 (Broker-only for CIAM)**: External CIAM tokens authenticate ONLY to the BFF; never exchanged downstream (no OBO on the external path); all external SPE/Dataverse app-only. Workforce-SSO modules use the standard workforce path with no Power-Apps-license dependency and no elevation.
- **NFR-02 (§10 BFF hygiene)**: Placement Justification per BFF addition; `dotnet publish -c Release` ≤60 MB compressed (baseline ~49.63 MB, report delta); no new HIGH CVE; tests in `tests/unit/Sprk.Bff.Api.Tests/` (+ integration KEEP-path); per-module endpoint groups via `Map{Module}Endpoints`.
- **NFR-03 (Secrets)**: No plaintext secrets; Key Vault references by name; CIAM provisioner cert stays in KV.
- **NFR-04 (Fluent v9 + Teams theming)**: ADR-021/022 — Fluent v9 + React 18; correct light/dark; Teams theme parity in the Teams host.
- **NFR-05 (sessionStorage isolation)**: Preserve external-SPA `sessionStorage` per-tab isolation for CIAM sign-in (documented ADR-028 exception); do NOT switch to localStorage/@spaarke/auth for the CIAM path.
- **NFR-06 (Server-enforced authz)**: All entitlement + participation + submitter-ownership decisions enforced server-side; client flags UX-only; a user cannot reach a module/record they lack (negative test required).
- **NFR-07 (Testing)**: ADR-038 — KEEP-path integration tests; no `Mock<HttpMessageHandler>` / DI-registration / ctor-null tests; live-only properties (FR-19) belong in live-E2E/seam, not false-green in-process mocks.
- **NFR-08 (Two-tier authorization — entitlement ≠ record visibility)**: Module entitlement (**Tier 1** — App Role / Contact-entitlement, "can you open the module") is **independent** of record-level scope (**Tier 2** — a per-module authorization predicate, "which rows within it"). Being entitled to a module NEVER grants visibility to all its records. Each module **declares its Tier-2 record predicate** — e.g. submitter-owned (`requester == caller`, Legal Front Door), participation grant (`sprk_externalrecordaccess`, Outside Counsel), or role-scoped queue (legal reviewer). **Both tiers enforced server-side**; neither internal nor external users can see all records by virtue of module access. A negative Tier-2 test (a user of the module cannot read another user's/unauthorized record) is required **per module**. The Tier-2 predicate is a pluggable per-module contract in the module-endpoint group, not a global rule.

## Technical Constraints

### Applicable ADRs
- **ADR-028 (+Amendment A1)**: external identity/auth — CIAM broker-only, Contact-by-oid; **extended** by R2 to add a workforce-SSO external-app plane (see ADR Tensions).
- **ADR-008**: per-endpoint authorization filters / route-group policies (no global middleware).
- **ADR-009**: Redis-first cache for `/me` entitlement + participation; invalidate on change.
- **ADR-007**: `SpeFileStore` facade for all SPE ops (app-only download/upload).
- **ADR-001 / ADR-010 / ADR-019**: Minimal API; DI minimalism (register concretes); ProblemDetails.
- **ADR-021 / ADR-022**: Fluent v9 + React 18 for all SPA/UI surfaces.
- **ADR-024**: polymorphic regarding for `sprk_servicerequest`.
- **ADR-038**: integration-heavy testing; test bans.

### MUST Rules
- ✅ MUST resolve external Contacts by `oid`; MUST bind oid on first login/submission.
- ✅ MUST enforce authz-before-stream on any document download/upload (403/no-bytes/no-Graph; negative test).
- ✅ MUST keep the CIAM path broker-only (no OBO); MUST use App Roles/claims (not downstream token exchange) for internal entitlement.
- ✅ MUST route each module's data through a per-module BFF endpoint group with an explicit auth policy.
- ❌ MUST NOT fork `LegalWorkspaceApp` (Xrm-bound; cannot serve external users).
- ❌ MUST NOT expose Graph pointers to any SPA; MUST key endpoints on documentId.
- ❌ MUST NOT create a Contact merely to grant internal access (entitlement = App Role; Contact = attribution, lazy).

### Existing Patterns to Follow / Reuse (per §11)
- Frame base: `src/client/external-spa/` (standalone MSAL SPA — extend).
- Module registry: `src/client/shared/Spaarke.AI.Widgets/src/registry/WorkspaceWidgetRegistry.ts` (adopt pattern).
- Card primitives: `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/ActionCard.tsx`, `ActionCardRow.tsx`.
- Theme: `@spaarke/ui-components` `useTheme`/`ThemeToggle`.
- Auth base: `src/client/shared/Spaarke.Auth/src/index.ts` (`AuthStrategy`) — extend for CIAM + context authority selection.
- BFF external surface: `Api/ExternalAccess/*`, `Api/Filters/ExternalCallerAuthorizationFilter.cs`, `Infrastructure/ExternalAccess/*`, `Services/Registration/*` (extend, don't fork).
- Embedded-mode discipline (not the Xrm contract): `docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`.
- **teams-app-r1 prior art (proven live)**: the Teams host on `external-spa` (host detection + Teams host adapter → workforce strategy), **MSAL v5 NAA** (`createNestablePublicClientApplication`), the `WorkforcePrincipalResolver`, and the minimal `/api/v1/collab` group. R2 **adopts + completes** this (FR-22), it does not rebuild it. Source: `projects/spaarke-SPA-external-access-platform-r1/notes/spa-v2-handoff-workforce-endpoint-gap.md`.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>N</spaarkeai>
  <ci-workflows>Y</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
BFF=Y: module-entitlement service + `/me` endpoint + Legal Front Door endpoint group + workforce-plane auth policy are new BFF surface — Placement Justification required per component (cite `.claude/constraints/bff-extensions.md`); ≤60 MB publish ceiling applies per BFF-touching task. ci-workflows=Y: per-app SWA deploy + Teams packaging. (SpaarkeAi=N: R2 *reuses* `Spaarke.AI.Widgets`/`Spaarke.UI.Components` library components but does not modify the SpaarkeAi app.)

### New Components (§11 three-question gate)
| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| Module registry + card launcher (client) | `WorkspaceWidgetRegistry` (widget/tab mounting; not a launcher) | Reuse the *pattern*; the launcher/router is new (no existing home-launcher) | Without it, each of 3+ apps is a hand-copied SPA that drifts; no access-gated home exists |
| Module-entitlement layer (Dataverse + BFF) | `sprk_externalrecordaccess` (record participation only, Project-shaped) | No — record grants can't express "self-service submitter with no record" or App-Role-based internal entitlement | Without it, Front Door users can't be entitled without a fabricated Project grant; no per-module gating |
| `/me` entitlement endpoint | R1 `/api/v1/external/me` (returns Project participations only) | Extend the concept, new resolver (App Role + Contact-entitlement) | Without it, the launcher can't decide which cards to show |
| Workforce-plane external-app auth policy | R1 `CiamExternal` policy (CIAM only) | Extend the additive-scheme pattern; new policy | Without it, Legal Front Door (workforce SSO) can't authenticate |
| `CallerPrincipalResolver` (plane-agnostic caller → accessible-record-set) | R1 `ExternalCallerAuthorizationFilter` (CIAM contact only) + teams-app-r1 `WorkforcePrincipalResolver` (workforce only) | Compose the two into one resolver behind a common principal abstraction (FR-22) | Without it, each module needs parallel per-plane endpoint sets (teams-app-r1 gap: workforce host authenticates but 401s on CIAM-only data endpoints) |
| Legal Front Door endpoint group + intake schema | `sprk_servicerequest` (stub — name/statecode/regarding only) | Extend the stub entity; new endpoints | Without it, there is no intake/submit/status capability |
| Admin UI (command-bar/MDA) | none (onboarding is API-only, DI-029-01) | No | Without it, no module is operable by its core-user persona without curl |

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-028 (+A1)** | A1 sanctions CIAM as the external identity plane (broker-only) pinned to `/api/v1/external`; it does not describe a **workforce-SSO** plane for unlicensed-internal SPA users, nor **dual-schemed principal-agnostic** collaboration endpoints | R2 adds a second external-app identity plane (workforce Entra SSO + App Roles) AND makes the collaboration data endpoints accept both schemes (FR-22) | **B (amendment)** | The core-vs-SPA-user model + "all employees → Front Door" + the teams-app-r1 workforce-host gap need a workforce plane and a principal-agnostic endpoint stance. Propose **ADR-028 Amendment A2** formalizing: (1) the dual-plane external-app model (CIAM external + workforce-SSO internal-unlicensed), (2) principal-agnostic module endpoints (one set, resolver-per-plane) as the canonical pattern, (3) broker-only preserved for the CIAM path and no-OBO for the workforce SPA path. Merge alongside the F2/FR-22 work. |
| **ADR-028** | Client contract: use `@spaarke/auth` + `authenticatedFetch`; sessionStorage exception is R1-scoped | The module-host reuses R1's sessionStorage per-tab isolation for the CIAM path and a custom BFF client, not the standard `@spaarke/auth` localStorage contract | **A (project-scoped exception)** | R1 already carries this documented exception for the external per-tab threat model; R2 continues it for CIAM sign-in and cites it. Workforce/Teams path may use the standard contract where compatible. |
| **ADR-010** (DI minimalism) | Prefer concretes; avoid interfaces with a single implementation | The module-entitlement resolver has ≥2 real implementations (App-Role internal, Contact external) → an interface is justified | **C (comply)** | Two concrete strategies make the seam legitimate, not speculative — consistent with ADR-010's testing-seam allowance. |

## Success Criteria
1. [ ] One module-host SPA renders a card launcher showing only entitled modules; an unentitled module is neither shown nor routable — Verify by: sign in as users with differing entitlements; attempt direct-route to an unentitled module (expect denied).
2. [ ] The same URL serves both planes and installs as a Teams app — Verify by: external CIAM sign-in (browser), internal workforce sign-in (browser + Teams), Teams dark mode render.
3. [ ] "All employees" entitlement via one App-Role/group assignment, no per-user provisioning — Verify by: assign group→role; a member with no prior record reaches Front Door.
4. [ ] Front Door: employee submits a typed request (NDA / P&P) with app-only document upload, sees only their own requests; requester Contact lazily created once — Verify by: two-user isolation test + Contact-creation-on-first-submission test.
5. [ ] Outside Counsel works unchanged as a registered module — Verify by: R1 parity pass on Assigned Work.
6. [ ] Core user grants/revokes module entitlement + record access from UI — Verify by: admin-UI grant/revoke round-trip.
7. [ ] Provisioner self-heals the 409 window; live-E2E wrong-issuer→401 + no email-hijack; SSPR first-run verified — Verify by: FR-18/19/20 tests.
8. [ ] Adding a new module = register card + lazy route + entitlement — Verify by: documented recipe + reviewer walkthrough.

## Dependencies

### Prerequisites
- R1 shipped + live (done). CIAM tenant + app-regs + BFF Ciam config (done, DI-028-01 resolved).
- Entra **App Role** definitions on the app registration (ops) + a test group ("All Employees" or equivalent) for Front Door.
- CIAM test user for live-E2E (FR-19) + SSPR verification (FR-20).
- Legal Front Door intake schema sign-off (request types, status model).

### External Dependencies
- Entra ID (App Roles / group assignment) — ops/portal.
- Microsoft Teams app registration + manifest submission (personal-tab), Teams SSO configured against the workforce app.
- Azure Static Web Apps resource(s) + deploy token(s).

### Entra/Teams config recipe (from teams-app-r1, reusable for any workforce/Teams host)
On the **workforce app registration** (dev: `1e40baad-…`):
1. Multitenant (`AzureADMultipleOrgs`) + expose `access_as_user` scope.
2. **Pre-authorize the Teams client apps** on `access_as_user`: `1fec8e78-bce4-4aaf-ab1b-5451cc387264` (desktop/mobile) + `5e3ce6c0-2b1f-4285-8d4b-75ee78787346` (web) — without this Teams SSO cannot issue a token.
3. **SPA redirect URIs** MUST include the app origin `https://{swa-host}` **and** the NAA broker redirect **`brk-multihub://{swa-host}`** (registered as an SPA reply address) — the latter was the final blocker (`AADSTS700046`).
4. This app issues **v1 access tokens** (`requestedAccessTokenVersion` = null) — confirm the BFF **workforce** scheme accepts the `api://{workforce-app}` audience + v1 issuer (distinct from the CIAM v2/GUID-audience path).
SWA framing: `staticwebapp.config.json` CSP `frame-ancestors 'self' https://teams.microsoft.com https://*.cloud.microsoft`, no `X-Frame-Options`.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Product axis | Is the boundary internal-vs-external? | **No — core-user vs SPA-user.** Not a core (licensed) user ⇒ SPA user (internal-unlicensed OR external) | One module-host serves all SPA users; identity plane is a sign-in detail |
| Program shape | How to split the work? | **R2 = Foundation + Legal Front Door; R3 = E-billing** | R2 scope set; E-billing out |
| Front Door identity | How do internal business users authenticate? | **Workforce Entra SSO** (employees, unlicensed) | Dual-plane foundation; F2 |
| Modules vs SPAs | One shell w/ modules, or discrete SPAs? | **One module-host shell**, modules gated by access; Teams-installable | F1/F4 centerpiece |
| Teams | Required? | **Yes** — module-host must install as a Teams app | F4 |
| Grantee/entitlement | How to represent internal users for access? | **Hybrid**: internal = Entra **App Roles** (assignable to groups; "any employee" fallback); external = per-Contact | F3/F8/F9 |
| Attribution | If not by Contact, how is the requester tracked? | **Contact** as requester, **lazily created by oid on first submission** (reuse R1 machinery) — entitlement (App Role) is decoupled from attribution (Contact) | FR-11 |
| Access surface | Model module access how? | **Two-layer**: new module-entitlement + keep `sprk_externalrecordaccess` | FR-07 |
| Front Door first cut | Which request types in R2? | **Generic intake framework + NDA (full) + Policy & Procedures** | FR-14/15/16 |
| Module registry | Code or Dataverse-driven? | **Code-side** registry + config (WorkspaceWidgetRegistry pattern) | FR-01 |
| Record visibility | Does module access = seeing all records in it? | **No** — two independent tiers: module entitlement (Tier 1) vs per-module record predicate (Tier 2: requester-owned / participation / role-queue). Neither internal nor external users see all records by virtue of module access | NFR-08, FR-17 |
| NDA depth | E-signature in R2? | **No** — stop at "approved + ready for signature"; e-signature deferred | FR-15 |
| Legal processing | New review surface or existing MDA? | **Existing MDA** for now — no new legal-review UI in R2 | FR-21 |
| App Role granularity | One role or per-module? | **One `FrontDoorUser` role** for R2, but design to **not foreclose** per-module roles later | FR-08 |
| Module-entitlement schema | Define now or later? | **Define when needed** (project-pipeline resource discovery) | FR-07 (Unresolved) |
| Endpoint topology (teams-app-r1 blocker) | Principal-agnostic (one set, N planes) or plane-partitioned (parallel sets)? | **Principal-agnostic** (Option A) — canonical platform pattern; dual-scheme collab endpoints + `CallerPrincipalResolver`; resolves the teams-app-r1 gap | FR-22 |

## Assumptions
- **Home-realm discovery** (browser): default to an explicit "my organization / partner" chooser (simplest, reliable); email-domain sniffing may be added later. Affects FR-03.
- **`sprk_externalobjectid`** semantics broaden from "CIAM oid" to "external identity object id (CIAM **or** workforce oid)" — one field, both planes (oids are globally unique). Affects FR-11.
- **App Roles** are defined on the **BFF app registration** (roles claim in the workforce token validated by the BFF), assignable to groups; "any authenticated employee" is the fallback for org-wide modules. Affects FR-08.
- **Teams packaging** targets a **personal tab**; channel/meeting tabs out of scope for R2.
- R2 is **phased** (P1→P4 per design §9); phase cut-lines confirmed at project-pipeline/task-create.

## Unresolved Questions
*(The four design-time questions are RESOLVED — see Owner Clarifications: NDA=no e-sign in R2; legal processing=existing MDA; App Role=one now, don't foreclose; endpoint topology=principal-agnostic. Remaining items are deferred-by-design to pipeline/execution, not blockers.)*
- [ ] **Module-entitlement schema shape** (new entity vs. lightweight join keyed to Contact/App-Role) — deferred by owner to project-pipeline resource discovery. — Affects: FR-07 schema (not a blocker to decomposition).
- [ ] **teams-app-r1 coordination**: FR-22 makes the principal-agnostic call that unblocks teams-app-r1. Confirm whether teams-app-r1 lands its Option-A slice independently and R2 builds on it, OR R2 absorbs the endpoint-generalization work. — Affects: task sequencing (P1) + avoiding duplicate work across worktrees.
- [ ] **v1 vs v2 token audiences across planes**: the workforce app issues v1 tokens (`api://…` audience); the CIAM app issues v2 (GUID audience). Confirm both are accepted by their respective BFF schemes when the endpoints are dual-schemed (FR-22). — Affects: FR-22 implementation detail.

---
*AI-optimized specification. Original design: `design.md`.*
