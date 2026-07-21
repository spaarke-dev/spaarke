# Spaarke External Access Platform — Custom SPA + Entra External ID (R1) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-19
> **Source**: `design.md` (owner-reviewed 2026-07-19)
> **Owner**: Ralph Schroeder

## Executive Summary

Migrate the **hosting + identity layer** of Spaarke's external-facing Secure Project Workspace — from **Power Pages + Entra B2B guests** to a **custom React SPA on Azure Static Web Apps + Microsoft Entra External ID (CIAM)** — while leaving the BFF business logic, the SPA feature set, and the three-plane `sprk_externalrecordaccess` authorization model unchanged. The migration is tractable because Spaarke already routes all external data through the BFF (`Sprk.Bff.Api`) and owns its own authorization; only the front door (host + identity provider) changes. R1 also adds the minimum file-content download path and the core-user invite trigger needed to make the migrated portal usable.

This is a **Type-2 (external MAU / CIAM)** project only. Type-1 full-license Power-App/MDA user provisioning (the existing demo-registration system) is explicitly out of scope.

## Scope

### In Scope

**Hosting & routing (Phase 1 — on existing B2B identity, to isolate routing regressions)**
- Deploy `src/client/external-spa/` to **Azure Static Web Apps** (replaces the Power Pages `sprk_externalworkspace` web-resource deploy path).
- `HashRouter` → **`BrowserRouter`** with SWA `navigationFallback` rewrite + an in-app not-found (404) route.
- **Deep-link-through-login**: preserve the intended route across the auth redirect (MSAL `state` / `redirectStartPage`).
- **Security headers** on SWA: `Referrer-Policy` (`no-referrer` or `same-origin`) + explicit CSP `frame-ancestors`.
- **CORS + redirect URIs**: add the new SWA origin to the BFF CORS allow-list and the SPA app-registration redirect URIs.

**Identity & provisioning (Phase 2 — Entra External ID / CIAM)**
- Stand up an **Entra External ID (CIAM) tenant**; configure user flow `isSignUpAllowed=false` + SSPR (Email OTP).
- Stand up a **CIAM-tenant app registration** with Graph `User.ReadWrite.All` (cert-in-Key-Vault or Managed-Identity-as-Federated-Identity-Credential).
- Add a **second JwtBearer scheme** to the BFF for the CIAM authority (`*.ciamlogin.com`), distinct from workforce-token validation.
- **Type-2 CIAM provisioner** (admin-initiated):
  - Onboard: resolve/create Dataverse `Contact` by email → create CIAM account via Graph `POST /users` (temp password + `forceChangePasswordNextSignIn`, `passwordPolicies: DisablePasswordExpiration`) → persist returned `oid` to `Contact.sprk_externalobjectid` → send BFF-authored branded onboarding email driving SSPR set-password. Idempotent: skip account creation if `oid` already present.
  - Grant: keep `sprk_externalrecordaccess` (Contact × Project × access level); **drop** the synthetic `contact_{guid}` SPE container membership (broker-only).
  - Resolve: `ExternalCallerAuthorizationFilter` validates the CIAM token and resolves `Contact` by **`oid`** (`sprk_externalobjectid`), email only as first-login fallback.
- **New Dataverse column** `Contact.sprk_externalobjectid` (text) — the stable CIAM `oid` ↔ Contact link.
- **Core-user "Invite to Secure Workspace" trigger** — internal-facing action (reuse the existing `/api/v1/external-access/invite` + `/grant` surface or add a thin Matter/Project command) that fires onboard + grant for a specific attorney **Contact** at an access level. Grant stays explicit + audited (`sprk_grantedby`), never auto-fired by a field edit.

**Document content (Phase 2)**
- New BFF **app-only** `SpeFileStore.DownloadContentAsync(driveId, itemId)` + external download endpoint, with Dataverse authorization enforced **before** the Graph read. Add `driveId`/`driveItemId` to the external document DTO (`ExternalProjectDtos`).

**Teams-readiness (architecture only)**
- Own framing/CSP headers on the SWA domain; keep MSAL interaction mode abstracted; keep authority / redirect URIs / scopes in config. **No** Teams manifest, Teams JS SDK, NAA, or Conditional Access work.

**Cutover (Phase 3)**
- Decommission the Power Pages site + `Deploy-ExternalWorkspaceSpa.ps1` after parity. Rewrite `external-access-spa-architecture.md` + the `EXTERNAL-ACCESS-*` guides. Apply ADR-028 Amendment A1.

### Out of Scope

- Any change to the three-plane access model (`sprk_externalrecordaccess`, SPE membership semantics, AI Search scope filter) beyond dropping the vestigial synthetic container grant.
- SPA feature set / page changes (Documents, Events, Tasks, Contacts).
- Internal Spaarke surfaces (`@spaarke/auth`, PCFs, Code Pages).
- **Type-1 full-license / MDA user provisioning** — the existing Self-Service / Demo Registration system (`spaarke-self-service-registration-app` + `spaarke-environment-provisioning-app`). Not in the migration blast radius (form on marketing site + approval in internal MDA; neither touches Power Pages).
- **Self-service sign-up + "Legal Front Door"** — deferred to a future project. R1 must not preclude them (config-driven authority/provisioning; onboarding-agnostic hook; `isSignUpAllowed` flippable).
- **Auto-invite-on-firm-assignment** and **Contact↔firm linkage improvements** — future.
- **Inline document preview / thumbnails UX** — R2 (R1 ships download + the enabling DTO fields).
- **UI/UX redesign** — R2. (BrowserRouter/clean URLs are R1 plumbing, not part of this deferral.)
- **Existing-user migration** — N/A (no production users).
- **Teams integration** — future project.

### Affected Areas

- `src/client/external-spa/src/config.ts` — External ID authority/tenant vars.
- `src/client/external-spa/src/auth/msal-config.ts`, `msal-auth.ts` — CIAM authority, token scope/authority.
- `src/client/external-spa/src/App.tsx` — `HashRouter` → `BrowserRouter`; in-app 404 route.
- `src/client/external-spa/` — deep-link-through-login (MSAL `state`/`redirectStartPage`); new SWA workflow + `staticwebapp.config.json` (navigationFallback + security headers), replacing `scripts/Deploy-ExternalWorkspaceSpa.ps1`.
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs` (via `Program.cs:54`) — new `"Ciam"` JwtBearer scheme + `Ciam` config section. **(CORS allow-list also updated here / in Program.)**
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalAccessEndpoints.cs:38` — pin `AuthenticationSchemes=["Ciam"]` on the `/api/v1/external` group only.
- `src/server/api/Sprk.Bff.Api/Api/Filters/ExternalCallerAuthorizationFilter.cs` (+ `Infrastructure/ExternalAccess/ExternalParticipationService.cs`) — **extend** to resolve Contact by `oid`.
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/InviteExternalUserEndpoint.cs` — replace Graph B2B invitation with CIAM account creation (cross-tenant Graph client) + `oid` persist + onboarding email; reuse `GraphUserService` payload/`PasswordGenerator`/`TrackingIdGenerator`/`RegistrationDataverseService`.
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/GrantExternalAccessEndpoint.cs:242-257` — drop synthetic SPE membership (`contact_{guid}`).
- **New**: cross-tenant CIAM Graph client — model on `src/server/api/Sprk.Bff.Api/Services/SpeAdmin/SpeAdminTokenProvider.cs` (`GetOrCreateMsalApp`).
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalProjectDataEndpoints.cs` — new `GET .../documents/{documentId}/content` endpoint **reusing** `Infrastructure/Graph/SpeFileStore.DownloadFileAsync` + `DocumentStorageResolver.GetSpePointersAsync` (authz-before-resolve). No DTO pointer exposure.
- `src/server/api/Sprk.Bff.Api/Services/Registration/RegistrationEmailService.cs` + `EmailTemplates/CiamOnboardingTemplate.html` (new) — onboarding email (pipeline reuse).
- Dataverse schema — `Contact.sprk_externalobjectid` (text).
- CIAM tenant — app registration (`User.ReadWrite.All`); user flow `isSignUpAllowed=false`; SSPR Email OTP.
- Invite trigger UI — existing management surface or thin Matter/Project command.
- `config/environments.json` — External ID tenant + SWA hostnames + CIAM app-reg.
- Docs — `docs/architecture/external-access-spa-architecture.md`, `docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md`, `EXTERNAL-ACCESS-SPA-GUIDE.md`.

## Requirements

### Functional Requirements

1. **FR-01 (SWA hosting)**: The SPA is served from Azure Static Web Apps with CI/CD. — Acceptance: SPA loads from the SWA origin; deploy runs via the new workflow; no dependency on the Power Pages web resource.
2. **FR-02 (BrowserRouter)**: The SPA uses `BrowserRouter`; deep links to `/project/{id}` resolve. — Acceptance: direct navigation to a deep path returns the app (SWA `navigationFallback`), not a 404; unknown paths render the in-app not-found route.
3. **FR-03 (deep-link-through-login)**: An emailed deep link lands on the intended route after authentication. — Acceptance: unauthenticated hit on `/project/{id}` → login → returns to `/project/{id}`.
4. **FR-04 (security headers)**: SWA sends `Referrer-Policy` and an explicit CSP `frame-ancestors`. — Acceptance: response headers present and match config; no `X-Frame-Options`/CSP conflict.
5. **FR-05 (CORS + redirect URIs)**: BFF CORS and SPA app-registration redirect URIs include the SWA origin. — Acceptance: pre-flight OPTIONS from the SWA origin returns 204 with `Access-Control-Allow-Origin`; login redirect succeeds.
6. **FR-06 (CIAM tenant + sign-up disabled)**: An Entra External ID tenant is configured with `isSignUpAllowed=false` and SSPR (Email OTP). — Acceptance: unprovisioned users cannot self-sign-up; a provisioned user can set a password via "Forgot password".
7. **FR-07 (second JwtBearer scheme)**: Add a `"Ciam"` JwtBearer scheme in `Infrastructure/DI/AuthorizationModule.cs` (append to the returned builder — do NOT add a third `AddAuthentication`), and **pin `AuthenticationSchemes=["Ciam"]` on the `/api/v1/external` group's `.RequireAuthorization(...)`** (`ExternalAccessEndpoints.cs:38`), leaving the internal `/api/v1/external-access` group on the workforce default. Add a `Ciam` config section mirroring `AzureAd`. — Acceptance: a valid CIAM token authenticates on `/api/v1/external/*`; a workforce token still validates on `/api/v1/external-access/*` and internal endpoints; an invalid-issuer token is 401; the default-scheme `PostConfigure` audience-merge is not applied to `Ciam`.
8. **FR-08 (admin-initiated onboarding — mostly reuse)**: Onboarding creates a CIAM account, persists `oid` to `sprk_externalobjectid`, and sends the onboarding email. **Reuse**: the `GraphUserService.CreateUserAsync` payload shape + `PasswordGenerator`; `TrackingIdGenerator`; `RegistrationDataverseService` resolve-or-create-by-email + idempotency guard; the `RegistrationEmailService`→`CommunicationService` email pipeline (add one `CiamOnboardingTemplate.html` + send method). **New**: a cross-tenant CIAM Graph client (modeled on `SpeAdmin/SpeAdminTokenProvider.GetOrCreateMsalApp` — per-authority `WithAuthority` + Key-Vault secret), and the CIAM `identities` (email local account) block on the user payload. — Acceptance: a new Contact gets a CIAM account + onboarding email; `sprk_externalobjectid` populated; **idempotent** — re-invoking for an already-provisioned Contact (oid present) creates no second account.
9. **FR-09 (Contact resolution by oid — extend, don't fork)**: **Extend** `ExternalCallerAuthorizationFilter` (`Api/Filters/ExternalCallerAuthorizationFilter.cs:63`) + `ExternalParticipationService.ResolveContactByEmailAsync` to add oid resolution (`sprk_externalobjectid`), email only as first-login fallback then hardened to oid. Do NOT create a parallel filter. — Acceptance: a signed-in CIAM user resolves to the correct Contact by oid; email mismatch does not grant access when oid is bound; participation cache still keys correctly under the CIAM `tid`.
10. **FR-10 (grant unchanged; drop synthetic SPE membership)**: `/grant` creates `sprk_externalrecordaccess` and no longer posts the synthetic `contact_{guid}` container permission. — Acceptance: grant succeeds; no synthetic SPE permission is written; participation cache invalidated.
11. **FR-11 (invite trigger)**: A core user can invite a specific attorney Contact to a Project at an access level in one action (onboard + grant). — Acceptance: the trigger provisions/links the CIAM identity (idempotent) and creates the access record; the attorney can subsequently sign in and see that Project.
12. **FR-12 (app-only content download — REUSE)**: Add a new external endpoint `GET /api/v1/external/projects/{projectId}/documents/{documentId}/content` (in `ExternalProjectDataEndpoints`, under `AddExternalCallerAuthorizationFilter`) that **reuses** the existing app-only `SpeFileStore.DownloadFileAsync(driveId, itemId)` and resolves pointers via `DocumentStorageResolver.GetSpePointersAsync(documentId)`. Enforce authorization (`HasProjectAccess(projectId)` + document→project scoping) **before** resolving pointers / calling Graph. **Do NOT** add a new download method to `SpeFileStore`/`DriveItemOperations`; **do NOT** use the OBO `DownloadFileAsUserAsync` path. — Acceptance: an authorized Contact downloads a document's bytes; an **unauthorized** Contact receives 403 and **no bytes**; no new SPE download method is introduced.
13. **FR-13 (documentId-keyed, no pointer exposure)**: The content endpoint is keyed on `documentId`; Graph pointers (`driveId`/`driveItemId`) are resolved **server-side** and are **NOT** added to the client DTO (broker-only — the browser never receives Graph identifiers). — Acceptance: `ExternalProjectDtos` gains no `driveId`/`driveItemId`; the client requests content by `documentId` only. *(Supersedes the design's "add driveId/driveItemId to the DTO" — dropped per BFF audit as an anti-pattern; R2 preview will likewise resolve pointers server-side.)*
14. **FR-14 (decommission)**: After parity, the Power Pages site + `Deploy-ExternalWorkspaceSpa.ps1` are retired and docs rewritten. — Acceptance: the SPA serves only from SWA; the web-resource deploy path is removed; architecture + guides reflect CIAM/SWA.

### Non-Functional Requirements

- **NFR-01 (publish size)**: BFF compressed publish output stays **≤60 MB** (baseline ~49.63 MB incl. PDBs). Every BFF-touching task reports absolute size + diff. ≥+5 MB single-task delta requires justification.
- **NFR-02 (broker-only invariant)**: The external user's token MUST NOT be exchanged for a downstream Graph/SPE/Dataverse token (no OBO on the external path). All external SPE/Dataverse access is app-only / managed identity.
- **NFR-03 (authz-before-stream)**: The download path MUST enforce `sprk_externalrecordaccess` + document→project scoping before any Graph content read. Negative/authorization test cases required.
- **NFR-04 (no new HIGH CVE)**: `dotnet list package --vulnerable --include-transitive` shows no new HIGH-severity CVE.
- **NFR-05 (token storage)**: Preserve `sessionStorage` per-tab isolation (documented ADR-028 exception); do NOT switch to `localStorage`.
- **NFR-06 (secret stewardship)**: CIAM-tenant app credential stored in Key Vault (cert) or eliminated via MI-as-FIC; no secrets in source/config.
- **NFR-07 (test obligations)**: PRs modifying `Sprk.Bff.Api/Services/` or `Api/ExternalAccess/` add/update tests in `tests/unit/Sprk.Bff.Api.Tests/` per §10 bullet 6.

## Technical Constraints

### Applicable ADRs

- **ADR-028** (Spaarke Auth Architecture) — external-SPA exemption; **extended by Amendment A1** (CIAM authority, broker-only invariant, E-3 boundary). See ADR Tensions.
- **ADR-008** (per-endpoint authorization filter) — external endpoints keep `ExternalCallerAuthorizationFilter`.
- **ADR-009** (Redis participation cache, 60s TTL) — invalidate on grant/revoke.
- **ADR-001 / ADR-010 / ADR-019** (Minimal API / DI minimalism / ProblemDetails) — apply to new endpoints/services.

### MUST Rules

- ✅ MUST authenticate external users against the CIAM authority (`*.ciamlogin.com`) via a second JwtBearer scheme.
- ✅ MUST resolve the external caller to a Dataverse Contact by **`oid`** and enforce authorization server-side via `sprk_externalrecordaccess`.
- ✅ MUST keep all external-surface SPE + Dataverse access app-only (BFF-brokered).
- ✅ MUST enforce Dataverse authorization **before** streaming file content.
- ❌ MUST NOT provision a per-external-user workforce Entra B2B guest for read/download.
- ❌ MUST NOT exchange the external user's token for a downstream token (no OBO on the external path).
- ❌ MUST NOT use `BrowserRouter` without the SWA `navigationFallback` rewrite + in-app 404.
- ❌ MUST NOT switch external-SPA token storage from `sessionStorage`.

### Existing Patterns to Follow

- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/*` — current invite/grant/filter (the surface being modified).
- `Infrastructure/Graph/GraphClientFactory.cs` → `ForApp()` — app-only Graph.
- `Services/Registration/RegistrationEmailService.cs` — branded email pattern (onboarding email).
- `Services/SpeFileStore.cs` — SPE facade (ADR-007) for the new app-only download.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration project="spaarke-SPA-external-access-platform-r1">
  <bff>Y</bff>                 <!-- ExternalAccess auth filter, CIAM provisioner, invite, app-only download -->
  <spaarkeai>N</spaarkeai>
  <ci-workflows>Y</ci-workflows>  <!-- new Azure Static Web Apps deploy workflow -->
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**Placement Justification (BFF=Y)**: All new server surface lives in the BFF because external-caller identity validation and app-only brokering must happen server-side at the single auditable data path, consistent with the existing `ExternalCallerAuthorizationFilter` (ADR-008). No CRUD→AI coupling introduced. Publish-size impact expected small (config + token validation + one download path, no new heavy dependencies) — verify ≤60 MB per NFR-01 on each BFF task. Cite `.claude/constraints/bff-extensions.md`.

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `Contact.sprk_externalobjectid` (column) | `sprk_externalobjectid` — none found; `adx_externalidentity` is Power-Pages-only | No — need a stable CIAM `oid` link off Power Pages | Without it, Contact resolution falls back to mutable/spoofable email; social-IdP email drift breaks login and risks wrong-Contact resolution |
| CIAM provisioner (onboard path in `InviteExternalUserEndpoint`) | `InviteExternalUserEndpoint.cs` (B2B invitation) | **Extend** — replace the B2B step in the existing endpoint | Without it, no CIAM identity is created; admin-initiated onboarding cannot mint a credential |
| Second JwtBearer `"Ciam"` scheme (`AuthorizationModule.cs`) | Existing workforce JwtBearer + `RagApiKey`; no `AddPolicyScheme`/issuer-forwarding exists | Add alongside (additive scheme, pin on `/api/v1/external` group) | Without it, CIAM tokens fail validation; the entire external surface is unauthenticated-reachable or 401s |
| External download **endpoint** (reuses `SpeFileStore.DownloadFileAsync`) | `SpeFileStore.DownloadFileAsync(driveId,itemId)` app-only **already exists** (`Infrastructure/Graph/SpeFileStore.cs:81`) | **Reuse the method** — new work is only a thin external endpoint + `DocumentStorageResolver` | Without an external endpoint, external users cannot download documents (metadata-only portal — not shippable). A new *download method* is NOT needed (duplication). |
| Cross-tenant CIAM Graph client | `GraphClientFactory` is single-tenant (no authority param); `SpeAdminTokenProvider.GetOrCreateMsalApp` is cross-tenant | No — factory can't target the CIAM tenant; **model on `SpeAdminTokenProvider`** | Without it, the BFF cannot create/manage CIAM users (cross-tenant Graph app-permission requirement) |
| Onboarding email | `Services/Registration/RegistrationEmailService` (Type-1 pattern) | **Extend/reuse** the email pattern | Without it, a CIAM-provisioned user has no way to learn how to set a password / sign in |
| Invite trigger (UI action) | existing `/external-access/invite`+`/grant` management surface | **Extend** existing surface where possible | Without it, core users cannot onboard outside counsel — the primary R1 use case fails |
| CIAM-tenant app registration | none (workforce MI cannot reach CIAM tenant) | No — Graph app permission must live in the CIAM tenant | Without it, the BFF cannot create/manage CIAM users (cross-tenant Graph app-permission requirement) |

## ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-028 | External identity modeled as Entra **B2B guests**; "B2C portal" out of scope | R1 introduces a **second IdP/tenant** (Entra External ID / CIAM) distinct from the workforce tenant, which ADR-028 does not sanction | **B — amendment** | Azure AD B2C is end-of-sale; External ID is the successor; broker-only spike (GREEN) removes the dual-identity concern. **Amendment A1 DRAFTED + owner-accepted 2026-07-19** (`adr-028-amendment-draft.md`); apply to concise + full before/alongside Phase 2. |

**Documented boundary (limitation E-3, in Amendment A1)**: direct-Office features (Word-for-Web co-authoring, desktop open via `webUrl`, user-identity Copilot grounding, Microsoft Search) require a workforce identity reaching SPE and are **not available to CIAM-only external users** — permanently out of scope; a future project needing them must reintroduce workforce B2B guests and file a superseding amendment.

No other ADR tensions surfaced. ADR-008/009/001/010/019 apply without exception.

## Success Criteria

1. [ ] SPA loads and functions from the SWA origin with clean-URL deep links. — Verify: navigate to a deep link directly + through login.
2. [ ] A core user invites an outside-counsel Contact; the attorney signs in via CIAM and sees the assigned Project + documents. — Verify: end-to-end run (Phase 2 live-tenant).
3. [ ] Onboarding is idempotent — re-invite does not create a second CIAM account. — Verify: invite twice, assert one account + one oid.
4. [ ] An authorized Contact downloads a document; an unauthorized Contact gets 403 with no bytes. — Verify: positive + negative authorization tests (NFR-03).
5. [ ] No workforce B2B guest is created for any external user. — Verify: inspect Entra; assert none.
6. [ ] BFF publish size ≤60 MB; no new HIGH CVE. — Verify: `dotnet publish` size report + `dotnet list package --vulnerable`.
7. [ ] Power Pages site + web-resource script decommissioned after parity; docs rewritten. — Verify: SPA served only from SWA; deploy script removed.
8. [ ] ADR-028 Amendment A1 applied to concise + full. — Verify: both ADR files updated.

## Dependencies

### Prerequisites
- Entra External ID (CIAM) tenant provisioned.
- CIAM-tenant app registration + credential path decided (cert-in-KV or MI-as-FIC).
- Azure Static Web Apps resource + CI/CD wiring.

### External Dependencies
- Microsoft Graph `User.ReadWrite.All` (application) consented in the CIAM tenant.
- SSPR (Email OTP) enabled in the CIAM tenant.
- Owner sign-off to apply ADR-028 Amendment A1 (obtained 2026-07-19).

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Document download | Include file download in R1 or defer? | R1 (critical); preview/thumbnails → R2 | Adds app-only `DownloadContentAsync` + DTO fields to R1 |
| Onboarding model | Self-service sign-up or admin-initiated? | Admin-initiated in R1; self-service + Legal Front Door future; must not preclude | R1 builds admin-initiated CIAM provisioner; `isSignUpAllowed=false`; onboarding-agnostic hook |
| Existing users | Migration plan for current external users? | None — not yet in production | Phase 3 = decommission only |
| ADR-028 | Accept Amendment A1 (path B)? | Accepted | Apply to canonical ADR at spec/Phase-2 time |
| User types | Should external workspace users get MDA access like demo users? | No — external = Type-2 CIAM broker-only; MDA/full-license = Type-1 (separate demo-registration system, out of scope) | Two-user-type separation; provisioning router is future |
| Identity anchor | Are CIAM users Contacts? | Yes — Contact is the anchor; CIAM = credential | `CIAM user = Contact + sprk_externalobjectid + sprk_externalrecordaccess` |
| Linkage key | email vs oid? | `oid` (owner agreed to new Contact field) | New `Contact.sprk_externalobjectid`; resolve by oid |
| Invite trigger | In R1 or assumed to exist? | In R1 (leverage existing or create new) | Core-user "Invite to Secure Workspace" action in scope |
| Auto-invite by firm | Auto-provision when firm assigned? | No (future feature) | Grant stays explicit; not auto-fired |
| Contact↔firm linkage | Improve in this project? | Out of scope | Deferred |

## Assumptions

- **Invite trigger placement**: assuming reuse/extension of the existing `/external-access/invite`+`/grant` management surface is preferred over a net-new UI, unless a thin Matter/Project command is clearly better — final placement decided at task time.
- **Onboarding email transport**: assuming reuse of the existing `RegistrationEmailService` email pipeline (Graph/managed-identity send) rather than a new transport.
- **Credential model**: assuming password + `forceChangePasswordNextSignIn` + SSPR (per researcher spike); passwordless/OTP-only remains a Phase-2 spike, not assumed.

## Unresolved Questions (Phase-2 verification spikes — not gates)

- [ ] Can a Graph-created CIAM account sign in via hosted-flow **Email OTP**, or is password mandatory? (~30 min) — Blocks: whether passwordless onboarding can be offered (default password otherwise).
- [ ] Is **MI-as-FIC** GA/acceptable at build time, or provision a **certificate** in Key Vault? — Blocks: CIAM-tenant app credential mechanism (NFR-06).
- [ ] Does the CIAM token carry the **`email`** claim, or is a claim mapping needed? — Blocks: nothing critical (oid is the link); affects display/first-login matching.
- [ ] Live end-to-end run against a real External ID tenant + SPE container; confirm app-only `/thumbnails` + `ReadContent` cover preview UX (R2 enabler). — Blocks: R2 preview planning, not R1.

---

*AI-optimized specification. Original design: `design.md`. Two-user-type framing + Type-2 CIAM provisioning + invite trigger folded in from the 2026-07-19 owner review and CIAM-provisioning researcher spike.*
