# design.md — Spaarke Teams App (R1)

> **Status**: 🟡 DRAFT — for owner review + iteration. Not yet spec'd.
> **Created**: 2026-08-02 · **Author**: Claude Code (grounded in 7 parallel investigations across code, docs, and prior projects, 2026-08-02)
> **Next step after sign-off**: iterate this doc → `/design-to-spec` → `/project-pipeline`.
> **Follows / reuses**: `spaarke-SPA-external-access-platform-r1` (the standalone SPA base — deployed), `ai-m365-copilot-integration` (Teams/workforce Entra plumbing — merged), `sdap-teams-app` (prior Teams-surface spec — design-only).
> **Sibling references**: `docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` (host checklist), `.claude/adr/ADR-028-spaarke-auth-architecture.md` (auth), `.claude/adr/ADR-034-user-record-membership.md` (membership).

---

## 1. What R1 is

R1 delivers the **Spaarke collaboration surface inside Microsoft Teams** — the current external-access SPA feature set (Secure Project Workspace: projects + documents + download), rendered as a **Teams personal app / tab**, authenticated with the user's **workforce Microsoft Entra identity**, and authorized by the user's **record membership**.

R1 is a **foundation milestone**: it stands up the Teams host, the workforce-SSO auth path, the dual-host architecture, and the enterprise deployment posture — proving the pattern end-to-end with real content. It is deliberately *not* a re-skin of the full Spaarke system-of-record app.

**Product line framing.** The standalone external-access SPA and the Teams app serve the **same purpose**: directed **information-sharing & collaboration** flows — *not* the full Spaarke applications, and *not* an alternative surface for the system-of-record (the MDA / SpaarkeAi remain the system-of-record). This is a distinct "collaboration front-door" product line with **two hosts over one shared core**.

---

## 2. Core principles (owner-confirmed, 2026-08-02)

1. **Collaboration surface, not system-of-record.** Curated, directed flows for sharing/collaboration. Never a general Dataverse/MDA surface.
2. **Dual-host is binding — but as shared-core + thin adapter, not forced uniformity.** One shared core (feature components, BFF client, authorization contract); divergence is *permitted but confined* to (a) the host-adapter seam and (b) explicit per-host config. **Duplicating a feature component across hosts requires a §11 Component-Justification sign-off** (only when a host constraint makes the shared one genuinely impossible). Reasons the hosts legitimately differ — audience (external vs internal), release cadence (org-catalog approval vs continuous SWA), tenant CSP/CA — are handled by config/feature-gating on the shared core, never by forking.
3. **Reuse over rebuild.** The backend (auth broker, membership, authorization, SPE access) is overwhelmingly built; R1 is primarily host mechanics + a workforce→principal resolver.
4. **Access by membership.** Internal users receive record access *by virtue of their membership* (ADR-034), not per-record manual grants.

### 2.1 Locked decisions

| # | Decision |
|---|---|
| D1 | **Dual-host = shared core + thin host adapter.** Divergence confined to adapter + config; no duplicated feature components without §11 sign-off. |
| D2 | **R1 auth = workforce SSO (Option 2)**, not CIAM-in-Teams. Enterprise-expected; seamless; aligns with customer IT SSO/Conditional Access. |
| D3 | **Record scoping = derive-from-membership (option b).** Internal users via ADR-034 `MembershipResolverService`; external users (SPA) via `sprk_externalrecordaccess`. |
| D4 | **Documents = broker-only SPE**, uniform across hosts and identity types (app-only `SpeFileStore` + authz-before-stream; never OBO, never Graph pointers to client). |
| D5 | **AI is out of R1.** If exposed later, it MUST enforce contact/membership-scoped security trimming + cost governance. |
| D6 | **Multitenant workforce Entra app** (one registration) + per-customer admin consent; hosting/data isolation handled by BFF `tid`→environment routing. |
| D7 | **Reuse the existing BFF Entra app** (`1e40baad-…`, already carries `access_as_user` + Teams redirect URIs + enterprise token store from the Copilot project). |
| D8 | **Do NOT force `@spaarke/auth`.** It is Xrm-bound + workforce-only + MSAL v3. The SPA/Teams collaboration line uses a shared **standalone-MSAL** module with a pluggable authority. |
| D9 | **Native Teams-channel messaging bridge is out of R1** (the reserved `CommunicationType.TeamsMessage` seam is a later layer). |
| D10 | **Extend `external-spa` in place** with a Teams host adapter (one codebase, host-detected) — not a fork. |
| D11 | **Non-systemuser internal staff MUST work in the Teams app** (owner, Option B). R1 adds a **workforce→contact** resolver + **contact-anchored membership** so a person's contact assignments authorize them in Teams regardless of Dataverse license. |

---

## 3. Architecture — one collaboration core, two hosts

```
        ┌─────────────────────────── Shared Collaboration Core ───────────────────────────┐
        │  Feature components (projects, documents, download; R2+: comms, matters, …)       │
        │  BFF client + API contract     Authorization contract (per-principal record set)   │
        └───────────────▲───────────────────────────────────────────────▲──────────────────┘
                        │ host-adapter seam (auth strategy, bootstrap, framing, theme, nav)  │
        ┌───────────────┴───────────────┐                   ┌───────────────┴────────────────┐
        │  Host A: Standalone SPA (SWA)  │                   │  Host B: Teams tab / personal   │
        │  Users: EXTERNAL (CIAM)        │                   │  Users: INTERNAL (workforce SSO)│
        │  Auth: CIAM MSAL → contact     │                   │  Auth: Teams SSO → systemuser   │
        └───────────────┬───────────────┘                   └───────────────┬────────────────┘
                        │                                                     │
                        └──────────────────────► BFF ◄────────────────────────┘
                          dual JwtBearer (workforce default + Ciam) · tid→env routing ·
                          membership (ADR-034) · sprk_externalrecordaccess · AuthorizationService ·
                          SpeFileStore broker (app-only, authz-before-stream)
```

**Base app.** R1 builds on the **deployed external-access SPA** (`src/client/external-spa/`) — React 18 + Vite + MSAL v5 + Fluent v9 + `@spaarke/ui-components`, hosted on Azure Static Web Apps, **pure BFF-broker (no `Xrm.WebApi`)**, with its own `staticwebapp.config.json` framing headers. Its own spec already lists "Teams integration (future)" as anticipated. R1 either extends it with a Teams host adapter or forks a sibling Teams build sharing the same core (decided in spec — see Open Questions).

**Host adapter seam.** Modeled on the Office add-ins' proven `IHostAdapter` pattern. Per-host concerns: auth strategy selection, bootstrap (Teams JS `app.initialize()` + context vs browser MSAL redirect), framing/CSP, theme source (Teams-provided vs SPA toggle), notifications, navigation/deep-linking.

---

## 4. Authentication

Two strategies behind one interface; both resolve to a **principal** the BFF can authorize:

| Host | Identity plane | Client strategy | BFF scheme | Resolves to |
|---|---|---|---|---|
| Standalone SPA | External (CIAM / Entra External ID) | Standalone MSAL v5 → `*.ciamlogin.com` authority | `Ciam` scheme (built) | **Contact** by `sprk_externalobjectid` (oid) |
| Teams tab (R1) | Workforce Microsoft Entra | Teams SSO (`getAuthToken`) / NAA → workforce token | Workforce default scheme (built) | **systemuser** (AAD `oid` → `systemuser.azureactivedirectoryobjectid`) |

**Why workforce SSO for Teams (D2).** Teams is inherently a workforce-Entra host — a Teams user is always a workforce identity or a guest; there is no clean CIAM sign-in inside Teams. Workforce SSO is seamless (no second login), aligns with each customer's SSO/Conditional-Access policy, and — per the Membership finding — a workforce systemuser already resolves to their membership + Dataverse-native enforcement with **no new authz model**.

**Multitenant + hosting (D6/D7).** Register **one multitenant workforce Entra app** (reuse `1e40baad-…`); each customer org's admin consents. The "supported account types" badge is about *where user identity lives* (multitenant = users from any customer tenant), **independent** of the hosting/data topology. The three deployment models (Spaarke-hosted dedicated env / customer-hosted / true SaaS) are handled by a new BFF **`tid`→environment routing** layer, not by the Entra registration.

**New R1 work (small):** a **workforce-token→systemuser resolver** wired into the collaboration endpoints (the AAD-oid→systemuser conversion already exists in `MembershipEndpoints`); `tid`→env routing; the Teams SSO client strategy in the shared standalone-MSAL module.

---

## 5. Authorization — accessible-record set, computed per identity plane (D3 / D11)

Authorization is uniform: **"is this record in the principal's accessible-record set?"** (the check the external path already models as `ExternalCallerContext.HasProjectAccess`). The *set* is composed per principal:

```
accessible(principal) =
    systemuser  → ADR-034 membership (auto — trusted internal staff, Dataverse-governed)
  ∪ contact     → sprk_externalrecordaccess grants (per-record, materialized by the grant modal §5.1)
  ∪ contact     → standing-grant runtime membership (if the contact has a standing grant §5.1)
```

**Internal systemusers get automatic membership; all CONTACT access is grant-mediated** — either per-record (deliberate, via the modal) or per-contact standing (a subject-level policy). Nothing is auto-granted to a contact without an explicit human decision — this is what keeps membership safe for external parties (an "opposing counsel" contact never gets access unless granted).

| Principal | Resolve | Accessible-record set | Status |
|---|---|---|---|
| **Workforce SSO → systemuser** (Ralph) | AAD `oid` → `systemuser` (+ derived contact via `sprk_primarycontact`) | **ADR-034 membership**, automatic — systemuser + contact-target lookups; any entity | ✅ Built (needs `sprk_primarycontact` link) |
| **Workforce SSO → contact only** (Mike) | AAD `oid` → `contact` (`azureactivedirectoryobjectid`/verified email) | `sprk_externalrecordaccess` grants **∪** standing-grant runtime membership | 🔨 R1 (grant model + resolver) |
| **CIAM → contact** (external SPA) | CIAM `oid` → `contact` (`sprk_externalobjectid`) | `sprk_externalrecordaccess` grants **∪** standing-grant runtime membership | ✅ Built (grants) + 🔨 standing |

**The contact-anchored membership resolver** (R1 net-new, reuses ADR-034's `BuildFetchXml` which already matches `ContactId`) serves **two** consumers: (a) the grant modal's **candidate list** (§5.1), and (b) the **standing-grant runtime path**. It is entered from a `ContactId` (no systemuser required) and filtered to the **access-conferring role allowlist** (assigned attorney/paralegal/assigned-to — never adverse roles).

**Load-bearing dependency:** `systemuser.sprk_primarycontact` MUST be populated for internal systemusers, or their contact-role assignments are silently skipped (`member_skipped: no_systemuser_mapping`). See §9 prerequisites.

**Known gaps (documented, mostly deferrable):**
1. **No single cross-subject "accessible records" query.** R1 composes the sources above behind the BFF; the full `sprk_accessgrant` UAC layer (design-only `unified-access-control`) is **out of scope** — though §5.1's per-record grants + standing grants are the deliberate first primitives of it (the `sprk_accesssubject`/`sprk_accessrole` concepts).
2. **`sprk_externalrecordaccess` is `sprk_project`-scoped** — extending to matters/other entities is **R2**. (Membership already spans all entities via ADR-034, so standing grants cover them natively.)
3. **Enforcement for contact-principals** uses the accessible-set check (record ∈ set), not `AuthorizationService.RetrievePrincipalAccess` (which requires a systemuser); mirrors the built `HasProjectAccess`.

### 5.1 Access-management surface — Tracking Field Trio + grant/email actions

The **`TrackingFieldTrio` PCF** (`src/client/pcf/TrackingFieldTrio/` + shared core in `@spaarke/ui-components`) becomes the record's **governance card**. It already renders **Monitor** / **High Priority** / **Access Permission** (OptionSet: Standard/Limited/Restricted). R1 extends it with a small **toolbar (two icons)**:

- **👤 Person icon → Access-grant modal.** Writes the one built table, `sprk_externalrecordaccess`:
  1. **Approve membership candidates** — the modal lists the record's **contact-role members** (from the contact-anchored resolver, filtered to the access-conferring role allowlist); owner ticks who gets access + approve → each materializes an `sprk_externalrecordaccess` row (provenance = `membership-approved`).
  2. **Named users** — a person-picker for arbitrary contacts → explicit `sprk_externalrecordaccess` rows.
  3. **Standing grant option** — when adding a contact, optionally mark them a **standing grant** (subject-level: auto-access to all records where they hold an access-conferring role, now + future — enforced at runtime, not materialized). Canonical home is a toggle on the **Contact form**; the modal offers it inline.
  4. **Send access link / invite** — reuses the **built** `InviteAndGrantExternalUserEndpoint` (`/api/v1/external-access/invite-and-grant`) + `SendCiamOnboardingEmailAsync`. Branch: external contact → CIAM onboarding email (built); internal workforce contact → deep-link notification (small addition — they already have M365).
- **✉️ Email icon → email all membership contacts.** Opens the canonical **`SendEmailDialog`** (`EmailComposer` engine, ADR-045) pre-populated with the record's membership contacts as recipients — so it flows through the tracked `sprk_communication` pipeline. Reuse: `sendCommunication()` / `SendEmailDialog` from `@spaarke/ui-components`.

**Access-Permission posture (Standard/Limited/Restricted)** governs the modal: **Restricted** = external access off; **Limited** = named/approved only (no standing grants); **Standard** = standing grants permitted. *(Confirm mapping with owner.)*

**Reuse:** `TrackingFieldTrio` (shared core + PCF); `sprk_externalrecordaccess`; `InviteAndGrantExternalUserEndpoint` + CIAM onboarding email; contact-anchored membership resolver (candidate + standing-grant source); `SendEmailDialog`/`sendCommunication`; `RecordNavigationModalShell` / Fluent v9 dialog (per `MODAL-DECISION-CRITERIA.md`).
**Net-new:** the two-icon toolbar + grant modal; standing-grant field on Contact + runtime union in the accessible-set; internal deep-link notify branch; entity-scope extension of `sprk_externalrecordaccess` beyond `sprk_project` (matters = R2).
**Phasing:** grant modal + named users + invite = **R1 core** (external access must be grantable). Standing grants + email icon = **R1 stretch or R2** (owner call).

---

## 6. Document access — broker-only SPE (D4)

The collaboration surface uses the **built external download path** for all hosts and identity types: app-only `SpeFileStore.DownloadFileAsync`, **authz-before-stream**, **no Graph `driveId`/`itemId` ever sent to the client**. Even for workforce Teams users, documents are served through the broker gated by the user's record access (membership or grant) — **not** OBO / their own SPE permissions. This keeps document access identical across hosts and reuses the security-reviewed path.

---

## 7. AI resources (out of R1 — forward constraints, D5)

R1 (projects + documents) exposes no AI. When AI (chat, RAG, semantic search) is later exposed on the collaboration surface, it MUST:
- Enforce **membership/contact-scoped security trimming** (Plane 3) — RAG/search must never surface content the principal cannot access (a document leak via RAG is the classic failure).
- Apply **cost/abuse governance** (collaboration users hitting AI is a cost + abuse surface).
- Scope **playbook/knowledge visibility** per principal.
The three-plane model already carries the AI-Search scope-filter hook; exposing the AI agent itself is its own security + cost design.

---

## 8. Enterprise posture (regulated legal customers)

- **Distribution:** the **org app catalog** under **App Centric Management** (legacy app-permission-policies deprecated Apr 2025); admin upload or submission-API + admin approval; ACM group assignment + app-setup-policy pinning. Not sideloading; not the public Store for V1.
- **Consent:** multitenant workforce app; per-customer **admin consent**; least-privilege delegated scopes; server-side OBO only where a downstream call needs it (collaboration reads are broker/app-only).
- **Manifest:** **v1.29**, `staticTabs` (+ optional configurable tab), complete `validDomains`, exact `webApplicationInfo { id, resource }`.
- **Framing:** `Content-Security-Policy: frame-ancestors 'self' https://teams.microsoft.com https://*.cloud.microsoft`; **no** `X-Frame-Options: DENY`. (Model on `external-spa/staticwebapp.config.json`, which already owns framing headers — change `'self'` → Teams hosts.)
- **Tooling:** **Microsoft 365 Agents Toolkit** (renamed Teams Toolkit) for build + `Publish to Organization`.
- **Trust:** **Publisher Attestation** (minimum for a legal customer's security review) → **M365 Certified** (ideal) as a parallel commercial workstream. Privacy policy + ToU published.
- **Host checklist:** reuse `LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` §8 as the Teams-host acceptance test.

---

## 9. R1 scope

**In scope**
- Teams personal app / static tab hosting the collaboration core (current SPA feature set: projects, documents, download); `external-spa` extended in place with a Teams host adapter (D10).
- Dual-host adapter seam; shared standalone-MSAL auth module with pluggable authority (CIAM + workforce-multitenant + Teams SSO).
- **Workforce→principal resolver** on the collaboration endpoints: AAD `oid` → systemuser, else → contact.
- **Membership-based record scoping** for both workforce planes: ADR-034 systemuser-anchored **and NEW contact-anchored** membership (D11); enforcement via accessible-set check (+ Dataverse-native where a systemuser applies).
- Multitenant workforce Entra app (reuse `1e40baad-…`) + admin-consent flow; BFF `tid`→environment routing.
- Teams app manifest (v1.29) + framing headers + org-catalog packaging via M365 Agents Toolkit.
- **Spike (first):** validate end-to-end in a Teams tab (desktop + web) — (a) systemuser → membership, (b) contact-only workforce user → contact-anchored membership, (c) SPA still works via CIAM — before the rest of R1 commits.
- Standalone SPA continues to function unchanged (CIAM plane).

**Prerequisites / data dependencies**
- **`systemuser.sprk_primarycontact` populated** for internal systemusers (or membership skips contact-role assignments). Add a verification/backfill + data-quality check.
- **Re-run the empirical check on a customer/production org** (dev org is demo data: 7 contacts / 163 systemusers; use Web API/FetchXML since the TDS endpoint rejects `IS NOT NULL`/`isdisabled` filters) — count systemusers with `sprk_primarycontact` set and contacts with `azureactivedirectoryobjectid`.

**Out of scope**
- AI exposure (D5); native Teams-channel messaging bridge (D9); communications/matters/service-requests features (R2+); the unified `sprk_accessgrant` layer; extending `sprk_externalrecordaccess` beyond projects; a Teams conversational bot (the Copilot project's `spaarke-bot-dev` is unrelated to the tab); Teams mobile (V1 desktop + web, per prior Teams spec).

**Graduation criteria (draft)**
- An internal **systemuser** opens the Spaarke tab in Teams, signs in via workforce SSO with no second login, and sees exactly the projects/documents their **membership** grants.
- An internal **contact-only** user (no systemuser) signs in via workforce SSO and sees exactly their **contact-anchored membership** records.
- A document download succeeds for an authorized member and returns **403 with no bytes** for a non-member (positive + negative) across all three principal types.
- The same collaboration core renders in both the standalone SPA (CIAM) and the Teams tab (workforce) with no duplicated feature components.
- The app installs from the org catalog in a second (customer) tenant via admin consent, and `tid`→env routing serves the correct environment.

---

## 10. Roadmap (post-R1)

- **R2** — communications (conversation widget) + matters on the collaboration surface (extend external grants to matters; wire the comms widget behind the shared core).
- **R3+** — legal front door / service requests; work assignments; invoicing / e-billing.
- **Later** — native Teams-channel messaging bridge (`CommunicationType.TeamsMessage` seam + bot); AI exposure with security-trimming + governance; unified access-control (`sprk_accessgrant`) if the two-path model proves insufficient.

---

## 11. Reuse map vs net-new (grounding)

**Reuse (built):** external-access SPA base (`src/client/external-spa/`); BFF dual JwtBearer schemes (`Infrastructure/DI/AuthorizationModule.cs`); AAD-oid→systemuser conversion (`Api/Membership/MembershipEndpoints.cs`); ADR-034 membership (`Services/Ai/Membership/MembershipResolverService.cs`); enforcement (`Spaarke.Core/Auth/AuthorizationService.cs`, `DocumentAuthorizationFilter`); external contact access (`Infrastructure/ExternalAccess/*`, `sprk_externalrecordaccess`); broker SPE download (`SpeFileStore.DownloadFileAsync`); Entra app `1e40baad-…` (Teams redirect URIs + `access_as_user` + enterprise token store); SWA hosting + framing pattern; Office-addins manifest/versioning/admin-deploy discipline; `LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` host checklist.

**Net-new (R1):** Teams host adapter + Teams SSO client strategy; **workforce→principal resolver** (AAD oid → systemuser, else → contact); **contact-anchored membership entry** on `MembershipResolverService` (reuses the built `BuildFetchXml` engine — a contact-only entry point + `IdentityNormalizationService` building a `PersonIdentity` from a `contactId`; relax the systemuser-only 401 gate for collaboration endpoints); BFF `tid`→environment routing; Teams app manifest + org-catalog packaging; framing-header changes for Teams; the dual-host seam refactor of the SPA core.

---

## 12. Placement Justification (CLAUDE.md §10)

New BFF surface is minimal and belongs in the BFF (the single backend for every client surface): (a) a **workforce-token→systemuser resolver** for the collaboration endpoints — auth resolution is a BFF concern; (b) **`tid`→environment routing** — a cross-cutting hosting concern; (c) reuse of existing membership/authorization/SPE services (no new business logic). No AI-internal types are injected into collaboration code. **Publish-size impact:** expected negligible — R1 adds a resolver + routing over existing services and needs **no** M365 Agents SDK / Bot packages (those are only for a conversational bot, which is out of scope). To be measured per the §10 rule on every BFF-touching task; current baseline ~49.63 MB incl. PDBs (ceiling ≤60 MB).

## 13. Hot-Path Declaration (CLAUDE.md §10 §G)

```xml
<hot-path-declaration>
  <bff>Y</bff>                <!-- workforce→systemuser resolver, tid→env routing on collaboration endpoints -->
  <spaarke-ai>N</spaarke-ai>  <!-- does NOT modify src/solutions/SpaarkeAi/**; reuses shared-lib widgets only -->
  <ci-workflows>Y</ci-workflows> <!-- new Teams-app deploy workflow (parallels deploy-external-spa.yml) -->
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
Register in `projects/INDEX.md` at project start; run `/conflict-check` before BFF PRs (13+ active worktrees contend on the BFF).

## 14. Component Justification (CLAUDE.md §11)

| New component | Existing overlap | Extend vs new | Cost-of-doing-nothing (concrete) |
|---|---|---|---|
| Teams host adapter | Office `IHostAdapter`; no Teams host exists | New (no Teams host today) | Without it, no Teams tab can bootstrap (`app.initialize`, context, framing) |
| Teams SSO auth strategy | `@spaarke/auth` strategies (Xrm/MSAL v3); external-spa MSAL v5 | New strategy in the shared standalone-MSAL module (reuse pattern) | Without it, a Teams user cannot authenticate seamlessly (workforce SSO) |
| workforce→principal resolver (collaboration) | AAD-oid→systemuser in `MembershipEndpoints`; `IdentityNormalizationService.TryResolveContactIdAsync` | Extend (reuse both conversions) | Without it, a workforce token can't be scoped to the caller's systemuser **or** contact on collaboration endpoints |
| contact-anchored membership entry | `MembershipResolverService.BuildFetchXml` already matches `ContactId`; no contact-only entry point | Extend (add entry + `PersonIdentity`-from-`contactId`; reuse the engine) | Without it, non-systemuser internal staff (Option B) get no membership-based access in Teams |
| BFF `tid`→env routing | none | New | Without it, Spaarke-hosted multi-customer / dedicated-env deployments route to the wrong environment |

## 15. ADR Tensions (CLAUDE.md §6.5)

- **ADR-028 (Spaarke Auth v2).** The external SPA is already a documented exemption (Amendment A1) from the "no direct `PublicClientApplication` / use `@spaarke/auth`" rule. R1 adds a **workforce-SSO path in a standalone (non-Xrm) app** and a shared standalone-MSAL module with dual authorities (CIAM + workforce). **Proposed path: (B) ADR-028 amendment** extending the collaboration-app auth exemption to cover the Teams workforce plane + the shared standalone-MSAL module. To be drafted with the spec.
- **ADR-034 (User-Record Membership).** R1 **extends** it with a contact-anchored entry point (contact-only principals) reusing the existing engine — a deliberate, additive extension of the systemuser-only entry, not a second mechanism. To note in the ADR when the entry point lands.

---

## 16. Resolved decisions + remaining owner items

**Resolved (2026-08-02/03):**
1. **Base-app approach** ✓ — extend `external-spa` in place with a Teams host adapter (one codebase, host-detected). (D10)
2. **Non-systemuser workforce users** ✓ — **Option B**: they MUST work in Teams; R1 adds workforce→contact + contact-anchored membership. (D11)
4. **Empirical check** ✓ — dev org inconclusive (demo data); re-run on a customer/production org (now a §9 prerequisite).

**Remaining:**
3. **ADR-028 amendment (Path B)** — approved to draft; being drafted alongside this design (see `adr-028-amendment-draft.md` in this folder).
5. **`sprk_primarycontact` backfill** — confirm ownership of the data-quality step that ensures internal systemusers are contact-linked (prerequisite for their membership to reach contact-role assignments).

---

## 17. References

- Base: `projects/spaarke-SPA-external-access-platform-r1/` (design.md, adr-028-amendment-draft.md), `src/client/external-spa/`
- Teams plumbing: `projects/ai-m365-copilot-integration/`, Entra app `1e40baad-…`
- Prior Teams spec: `projects/sdap-teams-app/` (design.md, spec.md — surfaces + governance)
- Auth: `.claude/adr/ADR-028-spaarke-auth-architecture.md`; `src/client/shared/Spaarke.Auth/src/`; `Infrastructure/DI/AuthorizationModule.cs`
- Membership/authz: `.claude/adr/ADR-034-user-record-membership.md`; `Services/Ai/Membership/`; `Infrastructure/ExternalAccess/`; `Spaarke.Core/Auth/AuthorizationService.cs`
- Host contract: `docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`
- Enterprise Teams requirements: researcher MEMORY + `reference_teams-integration-options-2026-08` (auto-memory)
- Unified access (design-only): `projects/unified-access-control/`

---

*Draft — 2026-08-02. Iterate with owner, then `/design-to-spec`.*
