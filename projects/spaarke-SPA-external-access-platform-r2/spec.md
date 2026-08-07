# Spaarke External Access Platform (R2): Module-Host SPA Foundation + Legal Front Door — AI Implementation Specification

> **Status**: Ready for Review (updated 2026-08-06 — teams-app-r1 delivery incorporated)
> **Created**: 2026-07-21
> **Source**: `projects/spaarke-SPA-external-access-platform-r2/design.md`
> **Grounding**: `notes/external-access-capability-synopsis.md` (R1 code synopsis, file:line cited); `notes/r2-coordination-response.md` (teams-app-r1 Option-A delivery: `CallerPrincipalResolver` FR-22 shipped)

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
- **Cleanup**: remove R1's dead Power Pages proxy/config (`vite.config.ts`, `README.md`, `powerpages.config.json`); **remove the transitional `/api/v1/collab` group** (`MapWorkforceCollaborationEndpoints` + `WorkforcePrincipalContextEndpoint` + `WorkforceCollaborationDownloadEndpoint`) once no client calls it, and **delete the now-inert `ExternalCallerAuthorizationFilter`** (superseded by `CiamContactPrincipalStrategy`, zero callers) — teams-app-r1 deviations D2/§7.

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
8. **FR-08 (Internal entitlement via App Roles)**: Internal **module** entitlement (Tier 1 — which cards a workforce user sees) is resolved from Entra **App Role** claims (per-module roles assignable to groups); org-wide modules may fall back to "any authenticated workforce user". **R2 ships a single `FrontDoorUser` role**, but the resolver + entitlement schema MUST support **per-module roles later without rework** (owner: "multiple by module in future — don't foreclose"). **Note the two tiers are distinct**: the shipped collaboration ("Assigned Work") module already uses **Tier-1 = "any authenticated workforce user"** + **Tier-2 = ADR-034 membership set** (teams-app-r1); Legal Front Door modules use **Tier-1 = `FrontDoorUser` App Role** + **Tier-2 = `requester == caller`**. App Roles gate Tier-1 (module visibility), never Tier-2 (record scope). Acceptance: assigning the All-Employees group to `FrontDoorUser` entitles every member with no per-user record; the role claim is read from the token (no Graph call); a second per-module role later requires no resolver refactor.
9. **FR-09 (External entitlement per-Contact)**: External (CIAM) module entitlement is granted per-Contact (curated), reusing/extending R1's grant surface. Acceptance: an external Contact is entitled to Assigned Work (+ E-billing later) explicitly.
10. **FR-10 (`/me` entitlement endpoint)**: `GET /me` returns the caller's entitled modules (resolved by App Role for internal, Contact-entitlement for external) + record participations where applicable; Redis-cached, invalidated on entitlement/grant change (ADR-009). Acceptance: entitlement changes are visible within the cache-invalidation window; unentitled modules are absent.
11. **FR-11 (Lazy Contact attribution)**: On an internal user's first attributed action, the BFF resolves-or-creates a Contact by workforce oid (reuse R1 `ResolveOrCreateContact` + bind-oid to `sprk_externalobjectid`) and records it as requester. Acceptance: an employee's first submission creates/links exactly one Contact; subsequent actions reuse it; no Contact is created merely by having access.
12. **FR-12 (Core-user admin UI + workforce role→level grading)**: A Fluent v9 (dark-mode, ADR-021) admin surface grants/revokes module entitlement (external) and record grants, and invokes invite-and-grant/provision for Outside Counsel. **Also (D1)**: `WorkforcePrincipalStrategy` maps the ADR-034 membership `byRole` result to a **graded access level** (owner→FullAccess, collaborator roles→Collaborate, designated view-only role→ViewOnly; default unmapped→Collaborate) — replacing P1's flat `Collaborate` — so internal SPA users have per-project rights at parity with CIAM external users. Acceptance: a core user performs grant + revoke from UI (no curl), renders in dark mode; an internal `owner`-role member can Delete on that project while a view-only-role member is Read-only (server-enforced; negative test).

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

### Functional Requirements — Cross-cutting foundation (informs P1/P2)
22. **FR-22 (Principal-agnostic module endpoints) — ✅ DELIVERED by teams-app-r1 (task 025); R2 LIFTS + generalizes.** Each module's data endpoint group accepts the scheme(s) for its plane(s) and resolves a **plane-agnostic caller** via `CallerPrincipalResolver`, yielding a common accessible-record-set on which the module's **Tier-2 predicate (NFR-08)** operates. One endpoint set per module, NOT one per plane. **Shipped implementation** (`notes/r2-coordination-response.md`):
    - `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs` — `ICallerPrincipalResolver.ResolveAsync(HttpContext) → CallerPrincipalResolution (Resolved|Denied)`; `ICallerPrincipalStrategy { Plane, ResolveAsync }` with two strategies: **`CiamContactPrincipalStrategy`** (reuses `ExternalParticipationService`) + **`WorkforcePrincipalStrategy`** (wraps `IWorkforcePrincipalResolver` + `IAccessibleRecordSetService`). `CallerPrincipal { Plane, ContactId, SystemUserId?, Email, Oid, ProjectAccess[] }` exposes `GetAccessibleProjectIds/HasProjectAccess/GetAccessLevel/GetEffectiveRights`.
    - `Api/Filters/CallerPrincipalAuthorizationFilter.cs` — group-level ADR-008 filter; sets `CallerPrincipal` on `HttpContext.Items`.
    - `AuthPolicies.ExternalCollaboration` (dual-scheme: `{Ciam, JwtBearerDefaults}`) — supersedes `CiamExternal` on the `/api/v1/external` group. **Plane selection**: CIAM iff validated token `iss` contains `ciamlogin.com` OR `tid == Ciam:TenantId`; else workforce (spoof-safe — read from a cryptographically-validated token).
    - **Third-plane seam is ready**: add one `AddScoped<ICallerPrincipalStrategy, XyzStrategy>()` + a `DeterminePlane` branch; handlers + filter untouched. This is exactly R2's module-framework generalization point.
    - **Verified**: full BFF suite **9761 pass / 0 fail** (27 new tests); CIAM path byte-for-byte preserved; publish **46.90 MB** compressed incl PDBs (< 60 MB).
    - **R2's remaining work on FR-22**: generalize the resolver into the module framework (register per-module strategies/predicates), register "Assigned Work" as the first module over these endpoints, and implement **D1** (grade workforce within-project rights by **ADR-034 role → level** — owner→FullAccess, collaborator roles→Collaborate, view-only role→ViewOnly — in F3/F5 via `WorkforcePrincipalStrategy`; P1 keeps flat `Collaborate`) and cleanup **D2** (`/api/v1/collab` transitional + inert `ExternalCallerAuthorizationFilter` deletion — see Scope→Cleanup).
    - **Operator-gated (P1 coordination)**: BFF rebuild+redeploy to `spaarke-bff-dev` + live Teams E2E remain (teams-app-r1 §9) — shared-infra ops.

### Non-Functional Requirements
- **NFR-01 (Broker-only for CIAM)**: External CIAM tokens authenticate ONLY to the BFF; never exchanged downstream (no OBO on the external path); all external SPE/Dataverse app-only. Workforce-SSO modules use the standard workforce path with no Power-Apps-license dependency and no elevation.
- **NFR-02 (§10 BFF hygiene)**: Placement Justification per BFF addition; `dotnet publish -c Release` ≤60 MB compressed (report delta); no new HIGH CVE; tests in `tests/unit/Sprk.Bff.Api.Tests/` (+ integration KEEP-path); per-module endpoint groups via `Map{Module}Endpoints`. Current reference baseline: **46.90 MB compressed incl PDBs** (post teams-app-r1 task 025 measurement; note the PDB convention when reporting).
- **NFR-03 (Secrets)**: No plaintext secrets; Key Vault references by name; CIAM provisioner cert stays in KV.
- **NFR-04 (Fluent v9 + Teams theming)**: ADR-021/022 — Fluent v9 + React 18; correct light/dark; Teams theme parity in the Teams host.
- **NFR-05 (sessionStorage isolation)**: Preserve external-SPA `sessionStorage` per-tab isolation for CIAM sign-in (documented ADR-028 exception); do NOT switch to localStorage/@spaarke/auth for the CIAM path.
- **NFR-06 (Server-enforced authz)**: All entitlement + participation + submitter-ownership decisions enforced server-side; client flags UX-only; a user cannot reach a module/record they lack (negative test required).
- **NFR-07 (Testing)**: ADR-038 — KEEP-path integration tests; no `Mock<HttpMessageHandler>` / DI-registration / ctor-null tests; live-only properties (FR-19) belong in live-E2E/seam, not false-green in-process mocks.
- **NFR-08 (Two-tier authorization — entitlement ≠ record visibility)**: Module entitlement (**Tier 1** — App Role / Contact-entitlement, "can you open the module") is **independent** of record-level scope (**Tier 2** — a per-module authorization predicate, "which rows within it"). Being entitled to a module NEVER grants visibility to all its records. Each module **declares its Tier-2 record predicate** — e.g. submitter-owned (`requester == caller`, Legal Front Door), participation grant (`sprk_externalrecordaccess`, Outside Counsel/CIAM), or role-scoped queue (legal reviewer). **Both tiers enforced server-side**; neither internal nor external users can see all records by virtue of module access. A negative Tier-2 test is required **per module**. The Tier-2 predicate is a pluggable per-module contract, not a global rule. **Shipped reference (teams-app-r1)** — the collaboration module's Tier-2 predicate is `IAccessibleRecordSetService.ComposeAsync(principal, "sprk_project")`: a **systemuser** principal → **ADR-034 user-record membership** set only; a **contact-only** principal → `sprk_externalrecordaccess` grants **∪** standing-grant runtime membership (only if `contact.sprk_standinggrant` is set). It is **NOT** "all projects" — a workforce user who merely authenticates gets the composed set and nothing more. This is the canonical worked example of an NFR-08 predicate for R2 to follow.

## Technical Constraints

### Applicable ADRs
- **ADR-028 (+Amendment A1)**: external identity/auth — CIAM broker-only, Contact-by-oid; **extended** by R2 to add a workforce-SSO external-app plane (see ADR Tensions).
- **ADR-008**: per-endpoint authorization filters / route-group policies (no global middleware).
- **ADR-009**: Redis-first cache for `/me` entitlement + participation; invalidate on change.
- **ADR-007**: `SpeFileStore` facade for all SPE ops (app-only download/upload).
- **ADR-001 / ADR-010 / ADR-019**: Minimal API; DI minimalism (register concretes); ProblemDetails.
- **ADR-021 / ADR-022**: Fluent v9 + React 18 for all SPA/UI surfaces.
- **ADR-024**: polymorphic regarding for `sprk_servicerequest`.
- **ADR-034 (user-record membership)**: the workforce Tier-2 record-scope for the collaboration module — systemuser principals derive their accessible-project set from ADR-034 membership (teams-app-r1 `IAccessibleRecordSetService`). R2 reuses this; any new workforce-plane module's Tier-2 predicate references it.
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
- **teams-app-r1 DELIVERED components (reuse as-is — do not rebuild)** — `notes/r2-coordination-response.md`:
  - `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs` + `ICallerPrincipalStrategy` (`CiamContactPrincipalStrategy`, `WorkforcePrincipalStrategy`) + `CallerPrincipal` — the FR-22 core; R2 generalizes into the module framework (third-plane seam ready).
  - `Api/Filters/CallerPrincipalAuthorizationFilter.cs` (group-level ADR-008 filter) + `AuthPolicies.ExternalCollaboration` (dual-scheme).
  - `IWorkforcePrincipalResolver` (teams-app-r1 task 020) — resolve/deny a workforce principal (systemuser/contact).
  - `IAccessibleRecordSetService.ComposeAsync` (teams-app-r1 task 022) — the workforce Tier-2 accessible-record-set (ADR-034 membership ∪ `sprk_externalrecordaccess` ∪ `sprk_standinggrant`).
  - Teams host on `external-spa` (host detection, MSAL v5 NAA `createNestablePublicClientApplication`) + the Entra recipe (applied in dev).
  - **Superseded/transitional**: `ExternalCallerAuthorizationFilter` is now inert (logic reproduced in `CiamContactPrincipalStrategy`); `/api/v1/collab` is transitional. R2 deletes both (Scope→Cleanup).

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
| `CallerPrincipalResolver` (plane-agnostic caller → accessible-record-set) | ✅ **DELIVERED by teams-app-r1 (task 025)** — resolver + 2 strategies + `CallerPrincipal` + filter + `ExternalCollaboration` policy, 9761 tests green | **Reuse as-is + generalize** into the module framework (register per-module strategy/predicate; third-plane seam ready) | N/A — built; R2 lifts it. (Not new R2 surface: R2 generalizes an existing, tested component.) |
| Legal Front Door endpoint group + intake schema | `sprk_servicerequest` (stub — name/statecode/regarding only) | Extend the stub entity; new endpoints | Without it, there is no intake/submit/status capability |
| Admin UI (command-bar/MDA) | none (onboarding is API-only, DI-029-01) | No | Without it, no module is operable by its core-user persona without curl |

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-028 (+A1)** | A1 sanctions CIAM as the external identity plane (broker-only) pinned to `/api/v1/external`; it does not describe a **workforce-SSO** plane for unlicensed-internal SPA users, nor **dual-schemed principal-agnostic** collaboration endpoints | The dual-plane workforce+CIAM model + principal-agnostic dual-scheme collaboration endpoints are now **SHIPPED** by teams-app-r1 (`ExternalCollaboration` policy, `CallerPrincipalResolver`) | **B (amendment)** | The design is already implemented and tested; the amendment now **ratifies shipped reality**. Propose **ADR-028 Amendment A2** formalizing: (1) the dual-plane external-app model (CIAM external + workforce-SSO internal-unlicensed); (2) principal-agnostic module endpoints (`CallerPrincipalResolver`, `ExternalCollaboration` dual-scheme, plane selection by `iss`/`tid`) as the canonical pattern; (3) broker-only preserved for CIAM + no-OBO for the workforce SPA path (verified — workforce token authenticates to BFF only). **R2 P1 deliverable**: author A2 to match the teams-app-r1 implementation (not to design it — it exists). Also cite **ADR-034** for workforce record membership. |
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
- **teams-app-r1 operator-gated remainder (P1 coordination)**: BFF rebuild+redeploy to `spaarke-bff-dev` (shared env) + live Teams E2E (workforce user opens the tab → workspace + records load via `/api/v1/external/*`). Code + tests + Entra config are complete; only the shared-infra deploy + live sign-in remain (teams-app-r1 §9). R2 P1 builds on the deployed result.
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
| Workforce within-project rights (D1) | Flat Collaborate, or graded per project like CIAM? | **Grade by ADR-034 role → level** (owner→FullAccess, collaborator→Collaborate, view-only→ViewOnly) for CIAM parity; role data already exists. P1 flat; role→level map in F3/F5 | FR-22, FR-12 |

## Assumptions
- **Home-realm discovery** (browser): default to an explicit "my organization / partner" chooser (simplest, reliable); email-domain sniffing may be added later. Affects FR-03.
- **`sprk_externalobjectid`** semantics broaden from "CIAM oid" to "external identity object id (CIAM **or** workforce oid)" — one field, both planes (oids are globally unique). Affects FR-11.
- **App Roles** are defined on the **BFF app registration** (roles claim in the workforce token validated by the BFF), assignable to groups; "any authenticated employee" is the fallback for org-wide modules. Affects FR-08.
- **Teams packaging** targets a **personal tab**; channel/meeting tabs out of scope for R2.
- R2 is **phased** (P1→P4 per design §9); phase cut-lines confirmed at project-pipeline/task-create.

## Unresolved Questions
*(All design-time questions RESOLVED. The teams-app-r1 delivery closed the endpoint-topology + workforce-Tier-2 + token-audience items. Remaining items are deferred-by-design to pipeline/execution, not blockers.)*
- [ ] **Module-entitlement schema shape** (new entity vs. lightweight join keyed to Contact/App-Role) — deferred by owner to project-pipeline resource discovery. — Affects: FR-07 schema (not a blocker to decomposition).
- [x] **Graded workforce per-project levels (teams-app-r1 D1)** — RESOLVED (owner, 2026-08-06): **grade workforce within-project rights by ADR-034 role → access level**, for full parity with CIAM external users. Mapping (F3/F5): membership role `owner` → **FullAccess** (Read+Create+Write+Delete); collaborator roles (`assignedAttorney`, `member`, …) → **Collaborate**; a designated view-only role → **ViewOnly**. Implemented via `WorkforcePrincipalStrategy` mapping the ADR-034 `byRole` result to graded levels (teams-app-r1's named extension point) instead of the flat `Collaborate`. **P1 keeps teams-app-r1's flat `Collaborate`** (plumbing-first); the role→level map lands in **F3/F5** (FR-12). The exact role→level table (which discovered roles map to which level; default unmapped → Collaborate) is finalized during F3/F5 with the schema work. — Affects: FR-12/F3, FR-22.
- [x] **teams-app-r1 coordination** — RESOLVED + DELIVERED: Option A shipped (task 025), built to all 8 guardrails, 9761 tests green, CIAM preserved. R2 lifts `CallerPrincipalResolver` + `ExternalCollaboration` + the workforce Tier-2 (`IAccessibleRecordSetService`, ADR-034 membership ∪ `sprk_externalrecordaccess` ∪ `sprk_standinggrant`). Response: `notes/r2-coordination-response.md`. Only operator-gated deploy + live Teams E2E remain (see Prerequisites).
- [x] **v1 vs v2 token audiences across planes** — RESOLVED: the BFF workforce default scheme (`api://1e40baad-…`) accepts the Teams NAA **v1** token with **no config change** (Microsoft.Identity.Web handles the v1 issuer); CIAM validates v2/GUID audience on the Ciam scheme only. No cross-validation, no CIAM regression (teams-app-r1 §5).

---

## R2 Scope Amendment — Post-P0-Review Additions (2026-08-06)

> Surfaced by the owner during the P0 prototype review; grounded by 4 investigations
> (`notes/review-additions-analysis.md`). **Owner decision: all five land in R2; P&P = both.**
> **Unifying principle**: each is an existing Spaarke capability re-hosted on the R2 external module
> framework + `CallerPrincipalResolver` (broker-only, dual-scheme, entitlement/participation-scoped,
> app-only, no OBO). None is a from-scratch build. **Plan restructure**: P3 absorbs FR-23/24/25;
> new **P5 (Collaboration Surfaces)** carries FR-26/27.

### New Functional Requirements

23. **FR-23 (NDA AI assessment & triage — 3-outcome)** *(refined 2026-08-06 per owner)* — On NDA upload (workforce-internal submitter), the system runs an **AI compliance assessment against the Spaarke NDA Standard** and returns **one of three outcomes**, each with a distinct next action:
    - **(a) Fully compliant** → *"This agreement meets all policy requirements and can be signed 'as is'."* + a **link to the e-signature process** (a **STUB in R2** — future third-party integration or build).
    - **(b) Minor / low-risk issues** → *"This agreement requires a few minor edits. Please make the following changes:"* + the list + a **redlined version** the user can **download or have emailed** directly.
    - **(c) Requires legal review** → *"This agreement requires review by the legal department. Please submit the NDA request."* → the **"Submit NDA request" button → intake wizard**, with the form **pre-filled from the uploaded document**.
    **Reuse**: the Spaarke NDA Standard (`Api/Ai/NdaStandardEndpoints.cs`, Baseline NDA Standard KNW-011) + the **agreements-r1 Agreement-Analysis review machine** (classifier + orientation + review-depth + **redline generation via the Compose redline engine**) + `DocumentClassifierHandler` (NDA) for the assessment; the pre-fill engine (`MatterPreFillService`/`IWorkspacePrefillAi` → `PreFillResponse {fields, confidence, prefilledFields[]}`) for the (c) form pre-fill; the FR-24 email path for emailing the redline. **New**: a **workforce-plane, entitlement-scoped external NDA-assessment endpoint** (`POST /api/v1/external/legal-front-door/nda/assess`, app-only SPE staging, **no OBO**) returning `{outcome: compliant|minor|review, message, redlineDocumentId?, prefill?}`; an NDA-intake extraction Action + binding row for the (c) pre-fill (no executor code). Acceptance: an NDA that fully complies → "sign as is" + e-sign stub link; a minor-issue NDA → edit list + downloadable/emailable redline; a review-required NDA → submit-request wizard pre-filled from the doc; the endpoint is app-only workforce-plane, never OBO (negative).

    **Grounded reuse vs net-new (investigation 2026-08-06)** — the **triage classification is ~80% reuse**: the `agreement-review` Action (`infra/dataverse/actions/agreement-review.action.json`, generalized from `nda-review`) already returns `{overallRisk: Low|Medium|High|Critical, flaggedSections[{sectionRef, quotedText, riskLevel, flaggedClause, assessment, standardRef}]}`, and **`overallRisk` maps almost verbatim to the 3 outcomes** (Low→sign-as-is, Medium→minor-edits, High/Critical→legal-review); the disposition logic already exists in `ComposeSummaryPageGenerator.BuildRecommendation:90-102`. NDA detection + (c) pre-fill reuse `DocumentClassifierHandler` (NDA category) + `AppOnlyAnalysisService` (**app-only MI path exists**). Standard reveal = `NdaStandardClauseProvider` (16 clauses B1–B16, KNW-011). **Net-new**: (1) the triage orchestrator (classify→assess→map→branch) + the external app-only endpoint; (2) **⚠️ the outcome-(b) auto-generated *redlined DOCX* is the genuinely novel/hard piece** — the `agreement-review` Action is prompt-**forbidden** from proposing replacement text, and agreements-r1/nda-r1 deliberately posture as **advisory-only, user-driven redlines ("MUST NOT auto-apply redlines")**. What exists is a **Review Summary Memo** (before/after table, downloadable/emailable via `ReviewMemoDocumentBuilder`+`IExportService`), NOT a tracked-change redline. A true auto-redline needs a **new finding→edit synthesizer** feeding `ComposeShadowPatchEngine` — an **ADR tension** with the advisory-only posture; (3) OBO-decoupling: assessment/classifier/memo are pure/app-only-capable, but bytes-in (external SPE broker read) + email export (currently `/me` OBO) need app-only variants. **OWNER DECISION (2026-08-06, resolved)**: **reuse ALL existing NDA plumbing** (`agreement-review` Action + risk enum + `ReviewMemo*` + `ComposeShadowPatchEngine` redline engine + `NdaStandard` + classifier/pre-fill). The open question is the **redline SURFACE** — the SPA **business-user** surface is intentionally *different* from what an **attorney** uses (the full Compose/agreement-review review surface). Do NOT pick memo-vs-tracked-change up front: **prototype mocks the business-user redline surface (done)**, and a **SPIKE task** in planning evaluates the best surface for the non-attorney SPA persona (which existing plumbing to expose, what artifact, app-only decoupling from the OBO/Compose-save path). The spike's outcome sets the FR-23 (b) production contract. Not a P0 blocker.

24. **FR-24 (Self-service review feedback loop)** — A submitted request's **decision + response documents flow back to the requester**: (a) **email-with-documents** via the existing `POST /api/communications/send` (arbitrary recipient email + `AttachmentDocumentIds` resolved from SPE **app-only** + request association) triggered on legal's MDA review; (b) **in-app** — the "my requests" detail renders legal's decision/response + downloadable response docs via the `requester==caller` endpoints. Additive schema on `sprk_servicerequest`: a decision/outcome field + response text + a **response**-document linkage distinct from the submitted-doc linkage. **Amends FR-21** (which built no feedback path). Live push/badge deferred (notification spine is `systemuser`-keyed; CIAM uses read-on-request). Acceptance: a reviewed request emails the requester with legal's docs attached (app-only) AND shows the decision on "my requests"; user A never sees user B's feedback (Tier-2 negative).

25. **FR-25 (Policy Library + "Review Policy Question")** *(refined 2026-08-06 per owner)* — R2 builds **both**: (i) a **browse/read policy library**, and (ii) **"Review Policy Question"** — a user submits a **policy question and receives an *official human* response from the law department** (NOT an AI answer — that's FR-26). Library home — **REUSE `sprk_document` (NOT a new `sprk_policy` entity — eliminated 2026-08-06 per grounding)** + a new **`sprk_documentcategory`** discriminator optionset (`Operational`/`Policy`/`Reference-Golden`; do NOT overload the single-valued AI-populated `sprk_documenttype`) + governance fields (status/effective/expiration) + **SPE** storage + the **existing document→AI-Search indexing lifecycle**, which is **already multi-index + per-record-routable** (`sprk_ai_search_index` lookup + `SearchIndexNameResolver` + allow-list, shipped by multi-container-multi-index-r1) — so routing Policy docs to a policy index is a **small category→index wire-up**, not new plumbing. **Golden/Reference RAG docs reuse the SAME mechanism** (a `sprk_document` category=`Reference-Golden` feeds a `sprk_analysisknowledge` delivery=RagIndex → existing `ReferenceIndexingService` → `spaarke-rag-references`) — no new entity for them either. **Two distinct read roles, kept separate**: (a) *grounding* — Ask Legal `policy_search` (FR-26) hits the **AI Search policy index**; (b) *browse* — the Policy Library grid reads **`sprk_document` via `sprk_gridconfiguration` + `BffDataverseClient`** (read-only; upload via SPE broker) with Dataverse entitlement + published filter. Both filter published/effective. **Genuinely new (the P5 spike trim item)**: governance-state trim fields (status/effective/published) are **not on any index schema today** → add index fields + query-time filter so retrieval never surfaces an unpublished/expired/restricted policy. **MUST NOT** use Dataverse `knowledgearticle` — beyond the licensing landmine (restricted table → D365 Customer Service Enterprise license per author + multiplexing for unlicensed external readers), it would be a **licensed intermediary we'd still have to index into AI Search anyway**: since semantic search requires an AI Search index over content we own+chunk+trim, an owned SPE/`sprk_policy` store is the correct source. "Review Policy Question" is a **Front Door request type** (`PolicyQuestion`) that reuses the intake + **FR-24 official-human-response feedback loop** (legal answers in the MDA; the answer returns to the requester in-app + email). **Authoring & maintenance (owner Q, 2026-08-06)**: policies are created/maintained by the **law department as core (licensed) users in the model-driven app** — the external portal is **read-only** on P&P. Reuse the existing **document upload wizard** (`sprk_documentuploadwizard`/`DocumentUploadWizardDialog`) + SPE + the existing **`sprk_document-search-index-lifecycle`** (SPE → AI Search) rather than new plumbing. Author flow: upload policy doc to SPE → set governance metadata (category/owner/version/status/effective/expiration) → **Publish** (status→Published) triggers (re)index with trim metadata → visible to the Policy Library + Ask Legal. Lifecycle: Draft→InReview→Published→Retired/Expired (SPE native versioning + version field). **§11 schema decision (RESOLVED 2026-08-06 by grounding)**: **reuse `sprk_document` + a new `sprk_documentcategory` discriminator** (Policy / Reference-Golden / Operational) + governance fields — **NO new `sprk_policy` entity**. A policy = a `sprk_document` of category=Policy; it inherits SPE + the (multi-index, per-record-routable) search-index lifecycle + the DataGrid framework. This same pattern serves Reference/Golden RAG docs (category=Reference-Golden) via `sprk_analysisknowledge`+`ReferenceIndexingService` — a general "typed document → typed RAG index → typed grid" capability, not a policy one-off. **R2 scope**: governance fields + reuse upload/index + a **basic MDA authoring form** (config-heavy); rich approval workflow / multi-language **deferrable**. Served to SPA users via the broker-only path (app-only). The library also grounds FR-26. Acceptance: an SPA user browses/reads a published policy (app-only, no Power-Apps license); expired/unpublished policies never surface (RAG security-trimmed on `sprk_policy` status/effective-date — negative); a user submits a policy question and receives an official human legal response tracked end-to-end (in-app + email).

26. **FR-26 (Q&A Assistant — "Ask Legal", WORKFORCE-INTERNAL only)** *(refined 2026-08-06 per owner)* — Embed the Spaarke AI Assistant (`SprkChat`, context-agnostic per ADR-012) in the Legal Front Door for **internal workforce users** (NOT external/outside-counsel). **BOUNDED SCOPE (owner, 2026-08-06) — Ask Legal does EXACTLY TWO things:** (1) **semantic search over the P&P library** (RAG-grounded answers from published policies — *instant, non-authoritative*, distinct from FR-25's official human answer), and (2) **direction/routing to the defined wizard services** (surface-launch to NDA / Policy Question / Invention / Trademark / etc.). **Explicitly EXCLUDED**: no "upload a file → summarize", no "upload a file → analyze", no general-purpose document ops, no data queries beyond P&P search — **file analysis happens ONLY inside a defined wizard** (e.g., the FR-23 NDA assessment), never in the assistant. **UI constraint**: the Ask Legal chat composer is **text-only — there is NO file-upload / attach / drop affordance in the chat box at all** (no paperclip, no drop zone); files are handled exclusively by wizards. (Removing the ingest surface entirely is itself a security control — nothing to exploit.) This narrow scope is the primary security control (see NFR-EXT-AI). **Implementation**: single **workforce-plane, entitlement-scoped** assistant endpoint (no CIAM dual-scheme); an **ADR-039 closed tool catalog of exactly two tools** — `policy_search` (RAG over the P&P library) + `launch_wizard` (route to a defined wizard) — **no other tools, no file-ingest tool**; **app-only RAG grounding limited to the P&P library** (published/effective policies only; no OBO `RetrievePrincipalAccess`; the core `/api/ai/chat` assumes a core-user Dataverse identity + OBO and cannot be used directly). Reuses the surface-launch spine to route "submit NDA" → the intake wizard. Acceptance: a workforce SPA user gets grounded answers scoped to their entitlements (out-of-entitlement → no grounded leakage — negative); the assistant launches/submits a Front Door request; the tool catalog excludes all core-user capabilities (negative); no OBO on any path; **Ask Legal is not shown to external/outside-counsel users**. **⚠️ SECURITY (not yet fully designed)**: because the assistant runs **app-only** (backend sees everything), the access/permission model — RAG grounding security-trim per caller, per-tool Tier-2 authz, prompt-injection/jailbreak boundary (server-side, not prompt-based), and the legal-advice guardrail (beyond a system prompt) — is an **open design item**. A **P5 security design spike ("External Assistant Access & Permission Model")** MUST precede and gate the FR-26 build; security-sensitive → human sign-off (§6). See `notes/external-assistant-access-model.md` + **NFR-EXT-AI** (assistant MUST NOT ground on or return content outside the caller's Tier-1/Tier-2 scope; enforcement server-side; negative "no cross-scope leakage" test per tool + for grounding). **Forward-looking (NOT R2)**: exposing Spaarke AI capabilities to *outside counsel* (external CIAM) in surfaces they access is a future broker-only-external-AI opportunity — recorded, deferred.

27. **FR-27 (Cross-boundary Messaging — MDA ↔ external chat)** — Surface the Messaging feature **as a tab within the Legal Front Door module** *(placement refined 2026-08-06 per owner — not a top-level launcher card)* so an **internal MDA/core user (`systemuser`) and an external workspace user (`contact`)** chat in the **same thread**. Reuse as-is: the `sprk_communicationparticipant` mixed-principal junction, `sprk_communicationthread`, `ConversationView`/`ConversationWorkspace`, and server-side `AcsIdentityService` (mints ACS identity for both principal types; no browser ACS token for the polling UI; no OBO). New: **one** thin `/api/v1/external/.../threads` group (`ExternalCollaboration` policy, **app-only, participation-scoped** to the contact's junction rows, re-running `CommunicationAccessFilter` with `IsInternalUser=false`) + external ACS mint + adding the external contact to the ACS membership reconciliation. MDA posts via existing internal `/send`; external posts via the new broker write; both land on the same `sprk_thread`. Acceptance: an MDA user and an external user exchange messages in one thread; `sprk_isinternalonly` messages are never returned to the contact (negative, already fail-closed); external reads are participation-scoped (a non-participant contact sees nothing — negative). **Coordination**: `Services/Communication/**` is a heavily-shared surface — `/conflict-check` before every PR.

### New / amended ADR Tensions (per §6.5)

| ADR | Rule | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-013 / §10** | BFF AI facade + hygiene; AI-internal types not injected into non-AI code | FR-23/26 expose AI (pre-fill + assistant) on the **external plane** | **C (comply)** | Use the `Services/Ai/PublicContracts/` facade + a new external-scoped endpoint group; Placement Justification per endpoint; publish ≤60 MB. No AI-internal types leak to the external surface. |
| **ADR-039** | Closed agent tool catalog | FR-26 external assistant must expose only a **subset** of tools | **C (comply)** | Add an explicit **external tool-catalog projection** (Q&A + submit-request only); no core-user tools reachable by SPA users. |
| **ADR-028 (+A3)** | Dual-plane principal-agnostic external endpoints | FR-23/26/27 add more `/api/v1/external/*` groups (assistant, pre-fill, threads) | **covered by A3** | A3 already ratifies the dual-plane principal-agnostic pattern; these new groups are instances of it — cite A3, no new amendment. |
| **ADR-040 / §10 (Communication surface)** | `Services/Communication/**` ownership + shared-surface coordination | FR-27 adds an external thread group on the shared Communication surface | **A (project-scoped exception + coordination)** | Additive external group; `/conflict-check` mandatory (email-r5, messaging-r1/r2/r3, notification-spine share this surface). |

### Scope deltas
- **In-scope (added)**: FR-23 NDA auto-fill; FR-24 feedback loop (email + in-app); FR-25 P&P library (both); FR-26 Q&A assistant (external); FR-27 cross-boundary messaging.
- **Still out-of-scope**: live SignalR push to CIAM contacts (feedback badge — read-on-request instead); e-signature (FR-15); E-billing (R3); browser-held ACS token / live WebSocket chat (polling only, per messaging R1–R3).
- **New Dataverse**: **NO `sprk_policy` entity** (eliminated — policies are `sprk_document` category=Policy). Additive: a `sprk_documentcategory` optionset + governance fields (status/effective/expiration) on `sprk_document`; policy-index governance-trim fields (status/effective/published) on the AI Search schema; `sprk_gridconfiguration` record(s) for the Policy Library grid; additive `sprk_servicerequest` fields (decision/outcome, response text, response-doc linkage, AI-extracted markers); NDA-intake `sprk_playbookconsumer` binding row.

---
*AI-optimized specification. Original design: `design.md`. Amended 2026-08-06 (post-P0-review, FR-23–FR-27).*
