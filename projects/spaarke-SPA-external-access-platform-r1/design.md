# Spaarke External Access Platform — Custom SPA + Entra External ID (R1) — Design

> **Status**: Design (owner-reviewed — scope decisions locked 2026-07-19; ready for `/design-to-spec`)
> **Created**: 2026-07-17
> **Owner**: Ralph Schroeder
> **Supersedes hosting/identity layer of**: `projects/sdap-secure-project-module` (R1) + `sdap-secure-project-module-r2`
> **Preserves**: BFF `/api/v1/external/*` surface, three-plane access model, `sprk_externalrecordaccess` authorization, React SPA codebase (`src/client/external-spa/`)

---

## Owner Scope Decisions — locked 2026-07-19

Four decisions taken at owner review, folded into the scope below:

1. **Document/SPE file access is IN R1 (minimal).** A read portal that only shows metadata is not shippable, and downloading files directly exercises the broker-only (app-only) thesis. R1 adds an **app-only content-download/open path** (`DownloadContentAsync(driveId, itemId)` via `FileStorageContainer.Selected` + `ReadContent`, Dataverse authz enforced *before* the Graph call) and adds `driveId`/`driveItemId` to the document DTO. **Inline preview / thumbnails UX is deferred to R2** (the DTO fields R1 adds are the enabler R2 needs). Direct-Office features stay out permanently (limitation E-3).
2. **Onboarding is admin-initiated in R1.** An internal Spaarke user initiates the external-user process (grant access → External ID identity + linked Contact). **R1 does NOT build self-service sign-up.** The External ID self-service sign-up user flow and the future **"Legal Front Door"** (an anonymous/unauthenticated request-intake surface where a prospect submits a request *before* having access, then a Spaarke user grants them in) are **deferred to a future project**. R1 MUST NOT introduce architecture that precludes them — keep authority / provisioning / redirect config-driven and the Contact-linking hook agnostic to whether onboarding was admin- or self-initiated. Admin-gated provisioning is inherently gated, so no invite-code / domain-allow-list anti-abuse machinery is needed in R1.
3. **No existing-user migration.** The system is not yet in production — there are no current external users. Phase 3 collapses to Power Pages decommission only.
4. **ADR-028 Amendment A1 accepted.** Applied to canonical ADR-028 (concise + full) at spec-generation time, before/alongside Phase 2 code.
5. **Two user types kept distinct; this project is Type-2 only.** Type-1 (full-license Power App **system users** — internal or Entra-B2B-guest — with model-driven-app access) is provisioned by the *existing* Self-Service / Demo Registration system and is **out of scope**. Type-2 (external **MAU / CIAM** users on a narrowly-tailored SPA, never the MDA) is this project. See "User Types & Relationship to the Registration System" below.
6. **The core-user "Invite to Secure Workspace" trigger IS in R1.** The internal-facing action that a Spaarke core user fires to onboard+grant an external Contact is in scope (leverage the existing management surface or add a thin new one). The provisioning mechanics behind it are the Type-2 CIAM process below.

---

## Executive Summary

The Secure Project Workspace is Spaarke's external-facing portal for law-firm attorneys, clients, and advisers. Today it is a React 18 SPA **hosted on Power Pages** and authenticated via **Entra B2B guest accounts** in the Spaarke workforce tenant. All data and business logic already flow through the BFF (`Sprk.Bff.Api`); Power Pages is reduced to a static host for a single inlined HTML web resource, and the Contact-based access control it nominally provides is **duplicated** by Spaarke's own `sprk_externalrecordaccess` three-plane model.

This project proposes migrating the **hosting + identity layer only** — from Power Pages + Entra B2B guests to a **custom React SPA on Azure Static Web Apps + Microsoft Entra External ID (CIAM)** — while keeping the BFF, the SPA application code, and the three-plane authorization model intact.

**Why now:** the June/July 2026 incident where the external site failed to load (`ERR_NAME_NOT_RESOLVED` on the decommissioned `spe-api-dev-67e2xz` BFF host — fixed 2026-07-17 by a rebuild/redeploy) exposed how brittle the Power Pages web-resource deployment path is, and prompted a re-evaluation of whether Power Pages is still the right platform. A mid-2026 platform review found that **two of the three original reasons for choosing Power Pages have eroded**, and Microsoft platform direction (Entra External ID as CIAM successor to B2C; mandatory Entra B2B for SharePoint external sharing from July 2026) now favors a custom SPA + Entra External ID end state. See [notes/research-power-pages-vs-external-id-2026-07-17.md](notes/research-power-pages-vs-external-id-2026-07-17.md).

---

## Decision Context: Re-scoring the Original Power Pages Drivers

Power Pages was originally chosen for three reasons. As of mid-2026:

| Original driver | 2026 status | Verdict |
|---|---|---|
| **Contact-based access control** | Still a real Power Pages feature (web roles + table permissions scoped by Contact). **But** Spaarke already re-implements this in the BFF via `sprk_externalrecordaccess` + the three-plane model (Plane 1 Dataverse, Plane 2 SPE, Plane 3 AI Search). It is a *duplicate* authz layer, not a dependency. | Weak — not used |
| **Self-registration** | Now **native in Entra External ID** self-service sign-up user flows (email OTP, social IdP, Microsoft account). No longer Power Pages-exclusive. | Eroded |
| **Capacity-pack licensing** | Still exists (~$200 / 100 authenticated users / site / month at list, per-site). **Entra External ID** MAU model gives the **first 50,000 MAU free**. | Eroded — External ID cheaper |

**Platform-direction signals (2026):**
- **Azure AD B2C is end-of-sale** (no new customers since 2025-05-01; P2 discontinued 2026-03-15; support to ~2030). Any new external identity build must target **Entra External ID**. This closes the architecture doc's noted limitation ("non-Microsoft users would require a B2C configuration").
- **SharePoint Embedded external sharing is converging on mandatory Entra B2B guests** — from **July 2026**, external collaborators without an Entra B2B guest object get access-denied, no opt-out. External users need Entra identities **regardless of Power Pages vs custom SPA**, so SPE access is a wash between the options.
- **Power Pages itself is NOT deprecated** (2026 Release Wave 1 continues investment, esp. Copilot Studio agent embedding). Staying carries zero obsolescence risk — but delivers little value given the current thin usage.

---

## Scope

### In Scope

- **Hosting migration**: deploy `src/client/external-spa/` to **Azure Static Web Apps** (replacing the Power Pages `sprk_externalworkspace` web-resource deploy path). Real static hosting → standard cache/versioning, CI/CD.
- **Routing → BrowserRouter (DECIDED)**: switch from `HashRouter` to `BrowserRouter` (clean URLs) with a SWA `navigationFallback` rewrite + an in-app not-found page. **Sequenced in Phase 1 on the existing Entra B2B identity** (before the External ID swap) so any routing/deep-link regression is attributable to routing, not the IdP change. The old Power Pages `HashRouter` constraint no longer applies on SWA.
- **Emailed deep-link support**: preserve the intended route through the login / sign-up redirect (MSAL `state` / `redirectStartPage`) so an emailed link to a specific project/document lands correctly after auth. With BrowserRouter clean paths, no query-param bridge is needed (paths survive enterprise mail link-rewriting where fragments are fragile).
- **Identity migration**: stand up a **Microsoft Entra External ID (CIAM) tenant**; replace Entra B2B guest auth with External ID sign-in. (Self-service sign-up user flows exist in External ID but are **NOT enabled/built in R1** — see onboarding below.)
- **Admin-initiated onboarding (R1)**: an internal Spaarke user initiates the external-user process; the BFF hook looks up / creates the Dataverse `Contact` and links it to the External ID identity (replacing the current `adx_invitation` / B2B redemption path). The Contact-linking hook MUST be **agnostic to how onboarding was initiated** so a future self-service / Legal Front Door path is additive, not a rewrite. Full mechanics in **Type-2 CIAM Provisioning Process** below.
- **Core-user "Invite to Secure Workspace" trigger (R1)**: the internal-facing action a Spaarke core user fires to onboard + grant an external Contact to a Project (choose access level). Reuse the existing `/api/v1/external-access/invite` + `/grant` management surface or add a thin command on the Matter/Project; the trigger calls the Type-2 CIAM provisioner. Grant stays explicit + audited (`sprk_grantedby`), never auto-fired by a field edit.
- **CIAM identity provisioning (R1)**: create the External ID account (Graph `POST /users` against the CIAM tenant via a CIAM-tenant app), persist the returned `oid` on the Contact (new `sprk_externalobjectid`), and send a BFF-authored onboarding email driving SSPR set-password. Configure the CIAM tenant `isSignUpAllowed=false` (admin-pre-create posture). See provisioning section.
- **App-only document content access (R1)**: add a BFF **app-only** `DownloadContentAsync(driveId, itemId)` path (stream/open file bytes) with Dataverse authz enforced *before* the Graph call; add `driveId`/`driveItemId` to the external document DTO. Subject to §10 BFF hygiene (tests + publish-size verification). Inline preview/thumbnails UX is **R2** (the DTO fields land now as the enabler).
- **BFF auth changes**: add a **second JwtBearer scheme** for the CIAM authority (`*.ciamlogin.com`), distinct from the existing workforce-token validation. `ExternalCallerAuthorizationFilter` validates External ID-issued tokens (new issuer/authority + audience) and resolves the Dataverse `Contact` **by stable `oid`** (`sprk_externalobjectid`), with email used only for first-login matching/display — replacing `preferred_username`/B2B resolution. Keep the three-plane authorization untouched downstream.
- **Cross-origin bookkeeping**: update the BFF **CORS allow-list** and the app-registration **redirect URIs** for the new SWA origin (the SPA's origin changes when it leaves Power Pages).
- **Security headers on SWA**: set `Referrer-Policy` (`no-referrer` or `same-origin`) and an explicit CSP `frame-ancestors` (we now own these headers — see Teams-readiness note).
- **Teams-deployable *readiness* (architecture only, NOT Teams integration)**: make choices that don't preclude embedding in Teams later, at ~zero cost — (a) own the framing/CSP headers on our SWA domain, (b) keep the MSAL **interaction mode abstracted** so a future NAA/popup strategy is additive (precedent: existing `OfficeNaaStrategy`), (c) keep authority / redirect URIs / scopes in config. **No Teams manifest, Teams JS SDK, NAA implementation, or Conditional Access work in R1.**
- **SPE external access** aligned to the mandatory-Entra-B2B reality (verify item-level grant path; see Cross-Domain & Identity Risks).
- **Decommission plan** for the Power Pages site + web-resource deployment script once parity is verified.

### Out of Scope

- Any change to the three-plane access model (`sprk_externalrecordaccess`, SPE container membership, AI Search scope filter) — preserved as-is.
- Changes to the SPA's feature set / pages (Documents, Events, Tasks, Contacts) — this is a hosting + identity migration, not a feature project.
- Internal Spaarke surfaces (`@spaarke/auth`, PCFs, Code Pages) — unaffected; this remains an external-only surface.
- Migrating the internal Corporate Workspace management endpoints (`/api/v1/external-access/*`) — they keep working; only the identity of the *external caller* changes.
- **Teams-specific integration** — Teams app package/manifest, Teams JS SDK wiring, Teams SSO (NAA) implementation, and Teams-targeted Conditional Access are **out of R1**. R1 only avoids *precluding* them (see Teams-readiness in In Scope). Adopting Teams is a separate future project.
- **UI/UX redesign** — visual refresh and any new pages are deferred to a later R2. (Note: `BrowserRouter`/clean URLs are NOT part of that deferral — they land in R1 Phase 1 as plumbing.)
- **Self-service sign-up + "Legal Front Door"** — External ID self-service sign-up user flows and the anonymous/unauthenticated request-intake surface (prospect submits a request before having access; a Spaarke user then grants them in) are a **future project**. R1 is admin-initiated only. R1 MUST NOT preclude them (config-driven authority/provisioning; onboarding-agnostic Contact-linking hook).
- **Inline document preview / thumbnails UX** — deferred to R2. R1 ships download/open of file content and the enabling DTO fields; the preview surface is a separate UX effort.
- **Existing-user migration** — none required (no production users yet). Phase 3 is Power Pages decommission only.

### Affected Files (preliminary)

| Area | File / Path | Change Type |
|------|-------------|-------------|
| SPA config | `src/client/external-spa/src/config.ts` | New External ID authority/tenant vars |
| SPA auth | `src/client/external-spa/src/auth/msal-config.ts` | External ID authority, sign-up flow |
| SPA auth | `src/client/external-spa/src/auth/msal-auth.ts` | Token scope/authority for External ID |
| SPA routing | `src/client/external-spa/src/App.tsx` | `HashRouter` → `BrowserRouter` (Phase 1) |
| SPA routing | in-app not-found / 404 route | New — SWA fallback returns 200 for unknown paths, app must handle |
| SPA auth | deep-link preservation through login (MSAL `state`/`redirectStartPage`) | New |
| SPA build/deploy | new SWA workflow + `staticwebapp.config.json` (navigationFallback, security headers) | Replaces `Deploy-ExternalWorkspaceSpa.ps1` |
| BFF | CORS allow-list | Add new SWA origin |
| App registration | SPA redirect URIs | Add new SWA origin |
| BFF auth pipeline | `Program.cs` (auth scheme registration) | **New second JwtBearer scheme** for CIAM (`*.ciamlogin.com`) authority |
| BFF | `Api/ExternalAccess/ExternalCallerAuthorizationFilter` | Validate CIAM token; resolve Contact by `oid` (`sprk_externalobjectid`), email fallback on first login |
| BFF | `Api/ExternalAccess/InviteExternalUserEndpoint.cs` | Replace Graph B2B invitation with CIAM `POST /users` account creation + `oid` persist + onboarding email |
| BFF | `Api/ExternalAccess/GrantExternalAccessEndpoint.cs` | Drop synthetic SPE container membership (`contact_{guid}`) — broker-only |
| BFF | new app-only content-download path (`SpeFileStore.DownloadContentAsync` + external endpoint) | New — authz-before-stream; add `driveId`/`driveItemId` to `ExternalProjectDtos` |
| BFF | new onboarding email (reuse `Services/Registration/RegistrationEmailService` pattern) | New — branded "set your password (SSPR)" email |
| Dataverse schema | `Contact.sprk_externalobjectid` (text) | New — stable CIAM `oid` ↔ Contact link |
| Invite trigger (UI) | existing management surface or thin Matter/Project command | Onboard + grant action for core users (R1) |
| App registration (CIAM tenant) | new app-reg with Graph `User.ReadWrite.All` | New — cert-in-KeyVault or MI-as-FIC (preview); creates/manages CIAM users |
| CIAM tenant config | user flow `isSignUpAllowed=false`; SSPR (Email OTP) enabled | New — admin-pre-create posture; future Legal Front Door flips sign-up on |
| BFF | External-caller authN config | New issuer/audience for External ID |
| Docs | `docs/architecture/external-access-spa-architecture.md` | Rewrite identity + hosting + onboarding sections |
| Config | `config/environments.json` | External ID tenant + SWA hostnames + CIAM app-reg |
| ADR | ADR-028 Amendment A1 (see ADR Tensions) | Apply to concise + full |

---

## Architecture

### Current (Power Pages + Entra B2B)
```
External user (M365 account, Entra B2B guest in workforce tenant)
  │  MSAL auth-code + PKCE, sessionStorage
  ▼
Power Pages site  ──serves──► sprk_externalworkspace web resource (inlined single-file React SPA)
  │
  ▼  Bearer token
BFF /api/v1/external/*  ──► ExternalCallerAuthorizationFilter (resolve Contact by preferred_username)
  │
  ├─ Plane 1: Dataverse sprk_externalrecordaccess
  ├─ Plane 2: SPE container membership (Graph)
  └─ Plane 3: AI Search scope filter
```

### Target (Custom SPA + Entra External ID)
```
External user (admin-provisioned CIAM identity — password/SSPR; NO workforce account, NO B2B guest)
  │  MSAL auth-code + PKCE, sessionStorage, CIAM authority (*.ciamlogin.com)
  ▼
Azure Static Web Apps  ──serves──► React SPA (standard static hosting, BrowserRouter)
  │
  ▼  Bearer token (CIAM issuer)  ── validated by BFF 2nd JwtBearer scheme
BFF /api/v1/external/*  ──► ExternalCallerAuthorizationFilter (validate CIAM token, resolve Contact by oid)
  │        (broker-only: user token NEVER exchanged downstream — all SPE/Dataverse is app-only)
  ├─ Plane 1: Dataverse sprk_externalrecordaccess   (UNCHANGED — source of truth)
  ├─ Plane 2: SPE app-only content read/download     (broker-only; NO per-user B2B guest, NO container membership)
  └─ Plane 3: AI Search scope filter                 (UNCHANGED)
```

The BFF **below the auth filter is unchanged**. This is the crux of why the migration is tractable: Spaarke deliberately routed all external data through the BFF and built its own authorization, so swapping the front door (host + IdP) does not disturb the business logic.

---

## User Types & Relationship to the Registration System

Two external-user setups must not be conflated. **This project is Type-2 only.**

| | **Type 1 — Full-license Power App user** | **Type 2 — External MAU / CIAM user** *(this project)* |
|---|---|---|
| Identity | Real **Entra `systemuser`** (internal *or* Entra-B2B guest), licensed | **CIAM identity** in a separate External ID tenant — no workforce account |
| Lands in | A **model-driven app** (full Dataverse environment) | A **narrowly-tailored SPA** (Secure Project Workspace; never the MDA) |
| Dataverse | `systemuser` + security role + BU + team | **Contact** + `sprk_externalobjectid` + `sprk_externalrecordaccess` (no systemuser) |
| Licensing | Per-user paid license | **MAU** (first 50k free) |
| Access model | Dataverse security roles | Three-plane broker-only |
| Provisioned by | **Existing Self-Service / Demo Registration system** | **This project** (Type-2 CIAM provisioner) |

**The existing Self-Service / Demo Registration system is out of this project's blast radius.** It is a *separate, shipped* subsystem (`projects/spaarke-self-service-registration-app` + `spaarke-environment-provisioning-app`): a public form on `spaarke.com/demo` → BFF `RegistrationEndpoints` → `Registration Request` Dataverse record → admin "Approve Demo Access" ribbon in the internal MDA → `DemoProvisioningService` mints a **Type-1** internal Entra account. Its form is on the marketing site and its approval UI is the internal MDA — **neither touches Power Pages**, so the hosting migration does not affect it, and it is not part of the decommission.

**North-star (future, NOT R1) — one intake, an access-type router.** The demo system already proves the "public form → request record → admin approve → provision" pipeline. The future **Legal Front Door** (a *different* Type-2 SPA for business users to request legal support) should reuse that intake/approval *scaffolding* and branch on an access-type discriminator to a **provisioning router**: `Demo → DemoProvisioningService` (exists) vs `ExternalClient → CIAM provisioner` (R1 builds). R1 builds only the Type-2 CIAM provisioner, shaped to be routable (same approve→provision contract shape), so the Legal Front Door is additive — it flips `isSignUpAllowed` on (or adds a gated auth-extension) and registers the CIAM provisioner as a second branch. **R1 must not preclude this; R1 must not build it.**

---

## Type-2 CIAM Provisioning Process

The **Dataverse Contact is the anchor.** A CIAM user is not a separate entity: `CIAM user = Contact + sprk_externalobjectid (oid) + one-or-more sprk_externalrecordaccess grants`. The CIAM account is only the credential; the Contact is the durable identity; the access records authorize. Mechanics confirmed against Microsoft sources in [notes/spike-spe-external-id-bridge-2026-07-18.md](notes/spike-spe-external-id-bridge-2026-07-18.md) + the CIAM-provisioning researcher spike (2026-07-19).

```
A. Onboard (admin-initiated)          B. Grant              C. Auth / resolve            D. Content (new)
1 Contact (create/match by email)     1 sprk_external-      1 2nd JwtBearer scheme        app-only Download-
2 Graph POST /users → CIAM account      recordaccess          (*.ciamlogin.com)           ContentAsync(driveId,
  (CIAM-tenant app; oid returned;       (Contact×Project×    2 resolve Contact by oid       itemId), Dataverse
  temp pw + forceChangePassword)        access level)         (sprk_externalobjectid)      authz enforced BEFORE
3 persist oid → sprk_externalobjectid  2 [DROP synthetic     3 load participations          the Graph read
4 BFF onboarding email → SSPR set-pw     SPE membership]     (three-plane UNCHANGED)
Tenant config: isSignUpAllowed=false + SSPR (Email OTP) enabled
```

**Confirmed mechanics (researcher spike 2026-07-19):**
- **No B2B-style redemption email exists for CIAM local accounts.** Supported "admin creates → user sets own credential" path = create with temp password + `forceChangePasswordNextSignIn=true` + `passwordPolicies: DisablePasswordExpiration`, enable **SSPR (Email OTP)**, and the **BFF sends its own branded onboarding email** telling the user to use "Forgot password". OTP-only for a *Graph-created* account is unconfirmed → default to password.
- **Stable link = `oid`** (persist to `sprk_externalobjectid`). Not `email` (mutable/social-IdP-variable); explicitly not `sub` (pairwise per-app).
- **Cross-tenant app**: a workforce MI cannot hold Graph app permissions on the separate CIAM tenant → a **CIAM-tenant app registration** with `User.ReadWrite.All` is required. Secret stewardship: **cert-in-Key-Vault** or **Managed-Identity-as-Federated-Identity-Credential** (preview — prefer if GA at build time).
- **`isSignUpAllowed=false`** (beta `authenticationEventsFlows`) is the supported "admin pre-creates, user just signs in" posture — it also blocks JIT federated creation, so every user is Graph-provisioned first (matches R1). It is the clean seam the future Legal Front Door flips on.

**Deferred to Phase 2 (verification, not gates):** (1) 30-min spike — can a Graph-created account sign in via hosted-flow Email OTP, or is password mandatory; (2) confirm MI-as-FIC GA status vs provision a cert; (3) verify the `email` claim is present in the CIAM token (add claim mapping if absent — `oid` is the link regardless).

### Worked use case — invite outside counsel

> `sprk_assignedoutsidecounsel` is a lookup to **`sprk_organization`** (the *firm*), not a person. CIAM auth and the SPA grant are **per-person**, so the grantee is a **Contact** at that firm, not the firm itself.

1. Core user sets `sprk_assignedoutsidecounsel` = Firm X on the Matter *(matter metadata — grants nothing)*.
2. Core user fires **"Invite to Secure Workspace"** for a specific attorney **Contact** at Firm X, choosing an access level.
3. **Onboard (idempotent)** — if the Contact has no `sprk_externalobjectid`: create the CIAM account, persist `oid`, send onboarding email. If `oid` already exists (returning counsel), **skip** creation.
4. **Grant** — create `sprk_externalrecordaccess` (Contact × Project × level). This is what authorizes.
5. Attorney sets password (SSPR) → signs into the SPA → BFF resolves `oid` → Contact → participations → sees Firm X's Project, documents (app-only download), events, tasks.

**Property:** one CIAM identity + one Contact per person, many project grants — a second matter for the same attorney is just a new `sprk_externalrecordaccess` row (no new account, lighter "access granted" email). Grants are additive and per-project revocable (deactivate the row). *(Contact↔firm association improvements and auto-invite-on-firm-assignment are explicitly future, not R1.)*

---

## Placement Justification (per root CLAUDE.md §10)

This project modifies `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/` (auth filter + Contact linking). No new AI dependencies, no new packages anticipated beyond identity config. The change is **in the BFF** because the external caller's identity validation must happen server-side at the single auditable data path — consistent with the existing `ExternalCallerAuthorizationFilter` (ADR-008 per-endpoint filter pattern). No CRUD→AI coupling introduced. Publish-size impact expected ≈ 0 (config + token-validation changes, no new heavy dependencies) — to be verified per NFR-01 on any BFF-touching task.

## Hot-Path Declaration (per root CLAUDE.md §10 / bff-extensions §G)

```xml
<hot-path-declaration project="spaarke-SPA-external-access-platform-r1">
  <bff>Y</bff>                <!-- ExternalAccess auth filter + /external/me + invite/Contact-linking -->
  <spaarke-ai>N</spaarke-ai>
  <ci-workflows>Y</ci-workflows>  <!-- new Azure Static Web Apps deploy workflow -->
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

## ADR Tensions (per root CLAUDE.md §6.5)

- **ADR-028 (Spaarke Auth Architecture)** — The external SPA is already a *documented exception* to ADR-028 (direct MSAL + `sessionStorage`, not `@spaarke/auth`). This project **deepens** that exception by introducing a second identity provider (Entra External ID) distinct from the workforce tenant. **Resolution path (B) — amendment DRAFTED**: see [adr-028-amendment-draft.md](adr-028-amendment-draft.md) (Amendment A1) — sanctions External ID for the external surface, mandates the broker-only app-only SPE invariant, and documents the direct-Office boundary (E-3). Pending owner sign-off before merge into canonical ADR-028 (concise + full); merges before/alongside Phase 2 code.
- **Identity provider change (Entra B2B → Entra External ID)** — the current architecture doc records "Entra B2B guests (not B2C)" as a design decision with rationale "external users already have M365 accounts." That rationale weakens once self-registration for non-M365 users is a requirement and B2C is end-of-sale. This is a genuine reversal of a prior decision → **path (B) amendment** candidate, not a silent override.

## Component Justification (per root CLAUDE.md §11)

New surface introduced by this project is **hosting + identity infrastructure**, not new application components:
1. **Existing** — the SPA, BFF endpoints, and authz model all already exist; this reuses them wholesale.
2. **Extension** — Azure Static Web Apps + Entra External ID replace (not add to) the Power Pages host + B2B identity. Net component count is expected to *decrease* (retire the web-resource deploy script + Power Pages site config).
3. **Cost-of-doing-nothing** — concrete failing behaviors without the migration: (a) non-Microsoft external users cannot authenticate at all (B2C dead end); (b) self-registration remains manual invite-only; (c) per-site capacity-pack cost scales with external user count vs 50k-MAU-free; (d) brittle web-resource deploy (the incident that started this).

---

## Requirements Traceability — the three original drivers, in the target design

| Requirement | Target-design mechanism |
|---|---|
| Contact-based access control | Preserved and **primary** via `sprk_externalrecordaccess` three-plane model in the BFF (no longer duplicated by Power Pages web roles). External ID identity → Dataverse Contact linkage at sign-up. |
| Self-registration | **Deferred to a future project.** R1 uses **admin-initiated** onboarding (Spaarke user grants access) → BFF Contact create/link hook. The hook is onboarding-agnostic so External ID self-service sign-up + the Legal Front Door drop in additively later. Replaces `adx_invitation` / B2B redemption. |
| Capacity-based / low-friction licensing | **Entra External ID MAU** (first 50k free) replaces per-site authenticated-user capacity packs. |

---

## Cross-Domain, Embedding & Identity Risks

Two production failure modes carried over from prior experience — worth stating explicitly. **The routing choice (BrowserRouter) is orthogonal to both** (same-origin, client-side); these are driven by the hosting + identity moves.

### Cross-domain / embedding
- **CORS (SPA → BFF)**: the SPA origin changes on the move to SWA → BFF CORS allow-list + app-registration redirect URIs must include the new origin, or all API calls / login redirects break. Routine but mandatory.
- **Iframe framing (the "Power Apps page blocked in Teams" class of problem)**: caused by restrictive `X-Frame-Options` / CSP `frame-ancestors` that Microsoft-hosted surfaces (Power Pages / Dataverse) send and you don't control. **Migrating to our own SWA domain is a net improvement** — we own these headers and set `frame-ancestors` deliberately. Caveat for any future embedded scenario: MSAL's hidden-iframe silent-token flow breaks under third-party-cookie / storage-partitioning; the answer is **NAA (Nested App Authentication)** or popup auth — kept *possible* by the Teams-readiness architecture decisions, but **not built in R1**.

### Login conflict — corporate account vs our external identity
- **Today's B2B-guest model is the *cause* of the corporate-vs-guest login errors**: the external user's browser is SSO'd into their corporate (home) tenant, but the app needs them as a **guest in our workforce tenant** — two tenants in play → "signed in as X but this app needs Y", home-realm-discovery confusion, unexpected consent, AADSTS errors (worse on shared browsers / multiple cached accounts).
- **Entra External ID is the remedy, not a new risk**: the user self-registers a **distinct identity in our CIAM tenant** (authenticating at `*.ciamlogin.com`), independent of their corporate tenant — the cross-tenant guest collision disappears for the identity path. Use `prompt=select_account` to prevent silent wrong-account selection. *Caveat*: if we later federate External ID to "sign in with your Microsoft work account," some multi-account dynamics return (but as clean federation into CIAM, not cross-tenant guest access).
- **The SPE identity bridge — RESOLVED GREEN (2026-07-18)**: the question was whether one External ID identity gives BOTH app access AND SPE document access, or whether SPE forces a parallel workforce B2B guest per user. **Answer: no B2B guest is required.** The external portal is a **pure BFF-broker** — the external user's identity never touches SPE/Graph; all external-surface SPE + Dataverse access is **app-only / managed identity** (`GrantExternalAccessEndpoint.cs:237`, `ExternalDataService.cs:580`; the container grant uses a synthetic non-Entra `contact_{guid}` login and is non-fatal). Microsoft confirms app-only `FileStorageContainer.Selected` + `ReadContent` streams document content with no workforce user identity. So a CIAM identity for BFF login suffices; the dual-identity tension does **not** arise for a read/download portal. **Narrow exception (limitation E-3):** direct-Office features (Word-for-Web co-authoring, desktop open via `webUrl`, user-identity Copilot grounding, Microsoft Search) *would* require a workforce B2B guest — these are out of scope. See [adr-028-amendment-draft.md](adr-028-amendment-draft.md) and [notes/spike-spe-external-id-bridge-2026-07-18.md](notes/spike-spe-external-id-bridge-2026-07-18.md).
- **Known feature gap (not a blocker):** the external surface currently exposes document **metadata only** — there is no file-content/download endpoint. Exposing downloads/previews needs a new **app-only** `DownloadContentAsync(driveId, itemId)` BFF path with Dataverse authz enforced first (subject to §10 BFF hygiene). App-only → CIAM-compatible; this is R1 implementation, not a feasibility risk.

---

## Risks & Open Questions

1. **SPE item-level grants + app-only** — research flags that SPE **item-level additive invite grants do not support app-only** (require OBO/user context), and mandatory Entra B2B lands July 2026. **Verify** the current grant-projection path (`provision-project` / grant endpoints) doesn't assume app-only for item-level invites, and confirm the `grantedToV2.user.id` claim used for invited Contacts under the B2B-guest-mandatory rule. (Also flagged as an open TODO in `knowledge/sharepoint-embedded/NOTES.md`.)
2. **External ID ↔ SPE identity bridge** — if SPE still requires an Entra B2B guest object per external user, does an External ID (CIAM-tenant) identity satisfy that, or is a workforce-tenant B2B guest still needed in parallel? **This is the highest-uncertainty item** and should be spiked first — it determines whether External ID fully replaces B2B or must coexist with it for document access.
3. **Contact linking (admin-initiated)** — race/duplication handling when a provisioned user's email matches an existing Dataverse Contact vs creates a new one. Anti-abuse is **not an R1 concern** (admin-initiated onboarding is inherently gated); it becomes relevant only when the deferred self-service / Legal Front Door path is built (then invite-code / domain allow-list / approval apply).
4. **App-only content-download authz ordering** — the new `DownloadContentAsync` path MUST enforce `sprk_externalrecordaccess` (and document→project scoping) *before* streaming bytes; verify no code path can return content for a document the caller's Contact isn't granted. Do NOT reuse the OBO `DownloadFileAsUserAsync` path for the external surface (per spike note 2).
5. **Cost verification** — pull the live per-MAU rate above 50k and model against realistic external-user + site counts before committing.
6. **~~Migration of existing external users~~** — **N/A**: no production users yet (owner decision 2026-07-19). Removed from scope.
7. **CI/CD** — new Azure Static Web Apps deploy workflow; retire `Deploy-ExternalWorkspaceSpa.ps1` only after parity.

---

## Proposed Phased Plan (MVP-first)

- **Phase 0 — Spike (GATE): architecture question RESOLVED GREEN 2026-07-18.** The SPE identity-bridge question is answered at the architecture/code level: the portal is broker-only (app-only SPE), so a CIAM identity suffices and no workforce B2B guest is required for document read/download. Residual verification (deferred into Phase 2, not a gate): a live end-to-end run against a real External ID tenant + SPE container, and a 30-min confirmation that app-only `/thumbnails` + `ReadContent` cover the preview UX. Full analysis: [notes/spike-spe-external-id-bridge-2026-07-18.md](notes/spike-spe-external-id-bridge-2026-07-18.md).
- **Phase 1 — Hosting migration + routing:** Deploy the existing SPA to Azure Static Web Apps **still on Entra B2B auth** to decouple from Power Pages and de-risk the deploy path independently of identity. **Flip `HashRouter` → `BrowserRouter` here** (SWA `navigationFallback` + in-app 404 + deep-link-through-login), plus BFF CORS + redirect-URI updates and the security headers — all verified against the known-good existing IdP so any routing regression is unambiguous.
- **Phase 2 — Identity migration + provisioning + document content:** Swap SPA + BFF to Entra External ID (2nd JwtBearer scheme; CIAM authority). Stand up the CIAM tenant (`isSignUpAllowed=false`, SSPR Email OTP) + CIAM-tenant app-reg (cert-in-KV or MI-as-FIC). Add `Contact.sprk_externalobjectid`. Implement the **Type-2 CIAM provisioner** (onboard: `POST /users` + oid persist + onboarding email; grant: keep `sprk_externalrecordaccess`, drop synthetic SPE membership; resolve by oid) and the **core-user "Invite to Secure Workspace" trigger**. Add the **app-only `DownloadContentAsync`** path + DTO `driveId`/`driveItemId` (authz-before-stream). Run the three deferred verification spikes. Apply ADR-028 Amendment A1 before/alongside this phase.
- **Phase 3 — Cutover & decommission:** Retire Power Pages site + web-resource script; rewrite architecture doc + `EXTERNAL-ACCESS-*` guides (onboarding section). (No existing-user migration — none exist.)

---

## Related

- [notes/research-power-pages-vs-external-id-2026-07-17.md](notes/research-power-pages-vs-external-id-2026-07-17.md) — full platform research + citations
- [docs/architecture/external-access-spa-architecture.md](../../docs/architecture/external-access-spa-architecture.md) — current-state architecture (to be rewritten)
- `projects/sdap-secure-project-module` (R1) / `sdap-secure-project-module-r2` — the platform this hosting/identity layer sits on
- [.claude/adr/ADR-028-spaarke-auth-architecture.md](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — auth architecture (amendment candidate)
- `knowledge/sharepoint-embedded/NOTES.md` — SPE external-access notes + open `grantedToV2.user.id` TODO
- `projects/spaarke-self-service-registration-app` + `projects/spaarke-environment-provisioning-app` — the **Type-1** demo-registration system (out of scope; north-star pattern for the future Legal Front Door router)
- `docs/guides/SPAARKE-SELF-SERVICE-USER-REGISTRATION.md` — Type-1 registration ops guide
- `.claude/agent-memory/researcher/` — CIAM provisioning spike findings (2026-07-19): Graph `POST /users`, oid linkage, cross-tenant app, SSPR onboarding, `isSignUpAllowed=false`

---

*Owner-reviewed 2026-07-19 — scope decisions locked (see top). Two-user-type framing, Type-2 CIAM provisioning process, and the core-user invite trigger folded in from the 2026-07-19 review + researcher spike. ADR-028 Amendment A1 accepted; Phase 0 spike GREEN. Ready to proceed to `/design-to-spec` → `/project-pipeline`.*
