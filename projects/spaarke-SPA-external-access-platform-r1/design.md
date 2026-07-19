# Spaarke External Access Platform — Custom SPA + Entra External ID (R1) — Design

> **Status**: Design (draft)
> **Created**: 2026-07-17
> **Owner**: Ralph Schroeder
> **Supersedes hosting/identity layer of**: `projects/sdap-secure-project-module` (R1) + `sdap-secure-project-module-r2`
> **Preserves**: BFF `/api/v1/external/*` surface, three-plane access model, `sprk_externalrecordaccess` authorization, React SPA codebase (`src/client/external-spa/`)

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
- **Identity migration**: stand up a **Microsoft Entra External ID (CIAM) tenant**; replace Entra B2B guest auth with External ID user flows (sign-in + **self-service sign-up**).
- **Self-registration flow**: External ID sign-up user flow → BFF hook to look up / create the Dataverse `Contact` and link it to the authenticated External ID identity (replacing the current `adx_invitation` / B2B redemption path).
- **BFF auth changes**: `ExternalCallerAuthorizationFilter` validates External ID-issued tokens (new issuer/authority + audience) and resolves the Dataverse `Contact` from the External ID token claim (replacing `preferred_username`/B2B resolution). Keep the three-plane authorization untouched downstream.
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
| App registration | redirect URIs | Add new SWA origin |
| BFF | `Api/ExternalAccess/*` (auth filter, `/external/me`, invite) | External ID token validation + Contact linking |
| BFF | External-caller authN config | New issuer/audience for External ID |
| Docs | `docs/architecture/external-access-spa-architecture.md` | Rewrite identity + hosting sections |
| Config | `config/environments.json` | External ID tenant + SWA hostnames |
| ADR | new/amended ADR for external identity (see ADR Tensions) | New |

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
External user (self-registered External ID identity — email OTP / social / MSA)
  │  MSAL auth-code + PKCE, sessionStorage, External ID authority
  ▼
Azure Static Web Apps  ──serves──► React SPA (standard static hosting, BrowserRouter)
  │
  ▼  Bearer token (External ID issuer)
BFF /api/v1/external/*  ──► ExternalCallerAuthorizationFilter (validate External ID token, resolve/link Contact)
  │
  ├─ Plane 1: Dataverse sprk_externalrecordaccess   (UNCHANGED)
  ├─ Plane 2: SPE container membership (Graph)       (UNCHANGED downstream; B2B guest object still required)
  └─ Plane 3: AI Search scope filter                 (UNCHANGED)
```

The BFF **below the auth filter is unchanged**. This is the crux of why the migration is tractable: Spaarke deliberately routed all external data through the BFF and built its own authorization, so swapping the front door (host + IdP) does not disturb the business logic.

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
| Self-registration | **Entra External ID self-service sign-up user flow** → BFF Contact create/link hook. Replaces `adx_invitation` / B2B redemption. |
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
3. **Contact linking at sign-up** — race/duplication handling when a self-registered user's email matches an existing Dataverse Contact vs creates a new one; anti-abuse (open sign-up to a legal portal needs gating — invite-code, domain allow-list, or admin approval).
4. **Cost verification** — pull the live per-MAU rate above 50k and model against realistic external-user + site counts before committing.
5. **Migration of existing external users** — how existing B2B-guest external users move to External ID identities (or coexistence window).
6. **CI/CD** — new Azure Static Web Apps deploy workflow; retire `Deploy-ExternalWorkspaceSpa.ps1` only after parity.

---

## Proposed Phased Plan (MVP-first)

- **Phase 0 — Spike (GATE): architecture question RESOLVED GREEN 2026-07-18.** The SPE identity-bridge question is answered at the architecture/code level: the portal is broker-only (app-only SPE), so a CIAM identity suffices and no workforce B2B guest is required for document read/download. Residual verification (deferred into Phase 2, not a gate): a live end-to-end run against a real External ID tenant + SPE container, and a 30-min confirmation that app-only `/thumbnails` + `ReadContent` cover the preview UX. Full analysis: [notes/spike-spe-external-id-bridge-2026-07-18.md](notes/spike-spe-external-id-bridge-2026-07-18.md).
- **Phase 1 — Hosting migration + routing:** Deploy the existing SPA to Azure Static Web Apps **still on Entra B2B auth** to decouple from Power Pages and de-risk the deploy path independently of identity. **Flip `HashRouter` → `BrowserRouter` here** (SWA `navigationFallback` + in-app 404 + deep-link-through-login), plus BFF CORS + redirect-URI updates and the security headers — all verified against the known-good existing IdP so any routing regression is unambiguous.
- **Phase 2 — Identity migration:** Swap SPA + BFF to Entra External ID; implement self-service sign-up + Contact linking, on a routing setup already proven in Phase 1.
- **Phase 3 — Cutover & decommission:** Migrate existing external users; retire Power Pages site + web-resource script; rewrite architecture doc; amend ADR-028.

---

## Related

- [notes/research-power-pages-vs-external-id-2026-07-17.md](notes/research-power-pages-vs-external-id-2026-07-17.md) — full platform research + citations
- [docs/architecture/external-access-spa-architecture.md](../../docs/architecture/external-access-spa-architecture.md) — current-state architecture (to be rewritten)
- `projects/sdap-secure-project-module` (R1) / `sdap-secure-project-module-r2` — the platform this hosting/identity layer sits on
- [.claude/adr/ADR-028-spaarke-auth-architecture.md](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — auth architecture (amendment candidate)
- `knowledge/sharepoint-embedded/NOTES.md` — SPE external-access notes + open `grantedToV2.user.id` TODO

---

*Draft — pending owner review of the ADR Tensions (ADR-028 amendment path) and the Phase 0 spike result before proceeding to `/design-to-spec` → `/project-pipeline`.*
