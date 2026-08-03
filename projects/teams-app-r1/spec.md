# Spaarke Teams App (R1) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-08-03
> **Source**: `projects/teams-app-r1/design.md`
> **Companion**: `projects/teams-app-r1/adr-028-amendment-draft.md` (Amendment A2 — Path B)

## Executive Summary

R1 delivers the **Spaarke collaboration surface inside Microsoft Teams** — the current external-access SPA feature set (Secure Project Workspace: projects + documents + download) rendered as a **Teams personal app / static tab**, authenticated with the user's **workforce Microsoft Entra identity**, and authorized by the user's **record membership**. The external SPA and Teams app form **one collaboration product line, two hosts, one shared core** (directed information-sharing & collaboration — *not* the system-of-record). R1 is a foundation milestone: it stands up the Teams host, workforce-SSO auth, the dual-host architecture, the record-membership authorization model, the record-level access-management surface, and the enterprise deployment posture.

## Scope

### In Scope
- Teams personal app / static tab hosting the collaboration core (projects + documents + download); `external-spa` **extended in place** with a Teams host adapter (one codebase, host-detected).
- Dual-host adapter seam (auth strategy, bootstrap, framing, theme, nav) over one shared core — **no duplicated feature components**.
- Shared **standalone-MSAL** auth module with pluggable authority: CIAM (SPA) + workforce-multitenant + Teams SSO/NAA (Teams).
- **Workforce→principal resolver** (AAD `oid` → systemuser, else → contact).
- **Membership-based record scoping**: automatic ADR-034 membership for systemusers; **contact-anchored membership** (R1 net-new) for contacts, role-allowlist-filtered.
- **Access-management surface** on the `TrackingFieldTrio` PCF: person-icon grant modal (approve membership candidates + named users → `sprk_externalrecordaccess`; invite/notify) and email-icon (email membership contacts via `SendEmailDialog`).
- **Per-contact standing grant** (subject-level policy; runtime union) — **R1** (owner-confirmed).
- Broker-only SPE document access (app-only, authz-before-stream, no Graph pointers) for all principals.
- Multitenant workforce Entra app (reuse `1e40baad-…`) + admin-consent onboarding; BFF `tid`→environment routing.
- Teams app manifest (v1.29) + framing headers + org-catalog packaging via M365 Agents Toolkit.
- **Spike (first)**: validate workforce SSO→systemuser→membership AND workforce→contact→contact-anchored membership in a Teams tab (desktop + web).
- Standalone SPA continues to function unchanged (CIAM plane).

### Out of Scope
- AI exposure (chat/RAG/search) on the collaboration surface — forward constraints only (security-trimming + cost governance).
- Native Teams-channel messaging bridge (`CommunicationType.TeamsMessage` seam + bot).
- Communications, matters, service requests, work assignments, invoicing/e-billing features (R2+).
- The full `sprk_accessgrant` Unified Access Control orchestration layer (design-only `unified-access-control`).
- Extending `sprk_externalrecordaccess` beyond `sprk_project` (matters/other entities = R2).
- Teams conversational bot (`spaarke-bot-dev` is the Copilot project's, unrelated to the tab); Teams mobile (V1 = desktop + web).
- Folding the collaboration hosts onto `@spaarke/auth` (MSAL v3→v5 estate migration = separate future effort).

### Affected Areas
- `src/client/external-spa/**` — extended with Teams host adapter, Teams SSO strategy, framing config.
- `src/client/pcf/TrackingFieldTrio/**` + `src/client/shared/Spaarke.UI.Components/src/components/TrackingFieldTrio/**` — two-icon toolbar + grant modal.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/**` — contact-anchored entry on `MembershipResolverService` / `IdentityNormalizationService`.
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/**`, `Infrastructure/ExternalAccess/**` — collaboration endpoints generalized to the workforce plane; grant/invite reuse.
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs` — workforce collaboration scheme wiring; `tid`→env routing (new cross-cutting BFF layer).
- New Teams app package (manifest + `validDomains` + `webApplicationInfo` on `1e40baad-…`); new CI deploy workflow (parallels `deploy-external-spa.yml`).
- Dataverse: `sprk_externalrecordaccess` (reuse); standing-grant field on `contact` (new); `systemuser.sprk_primarycontact` (data backfill).

## Requirements

### Functional Requirements

1. **FR-01 — Teams tab host**: Ship a Teams personal app / static tab that renders the collaboration core (projects, documents, download). Acceptance: the tab loads inside Teams (desktop + web) and renders the current SPA feature set.
2. **FR-02 — Dual-host adapter seam**: One shared core + a thin host adapter isolating auth/bootstrap/framing/theme/nav. Acceptance: the same feature components render in the standalone SPA and the Teams tab with **no duplicated feature component** (only the adapter differs); a §11 sign-off is required for any duplicated feature component.
3. **FR-03 — Shared standalone-MSAL auth, pluggable authority**: One auth module serving CIAM (SPA) and workforce-multitenant + Teams SSO/NAA (Teams). Acceptance: host selects the correct strategy; both yield a BFF-valid token.
4. **FR-04 — Workforce→principal resolver (BFF)**: Resolve a workforce token to a systemuser (AAD `oid` → `systemuser.azureactivedirectoryobjectid`), else to a contact (`contact.azureactivedirectoryobjectid`/verified email). Acceptance: a systemuser caller resolves to (systemuserId + derived contactId); a non-systemuser caller resolves to (contactId only); an unresolvable caller is denied.
5. **FR-05 — Contact-anchored membership**: Add a contact-only entry to `MembershipResolverService` (reusing `BuildFetchXml`), building a `PersonIdentity` from a `contactId`, filtered to the **access-conferring role allowlist** (R1 `sprk_project`: `sprk_assignedattorney1/2`, `sprk_assignedparalegal1/2`, `sprk_assignedtoexternal`, `sprk_assignedtointernal` — see Resolved Decisions). Acceptance: given a contactId, returns records where that contact is on an allowlisted contact-role lookup, across entities; non-allowlisted/adverse fields never match.
6. **FR-06 — Accessible-record-set authorization**: Compose `accessible(principal) = systemuser→ADR-034 membership (auto) ∪ contact→sprk_externalrecordaccess grants ∪ contact→standing-grant runtime membership`; enforce `record ∈ set`. Acceptance: each principal type sees exactly its set; enforcement is a positive/negative-tested gate.
7. **FR-07 — Broker-only SPE document access**: All principals download via app-only `SpeFileStore.DownloadFileAsync`, authz-before-stream, no Graph pointers to client. Acceptance: authorized member gets bytes; non-member gets **403 with no bytes**; no `driveId`/`itemId` reaches the browser.
8. **FR-08 — Multitenant workforce Entra + admin consent**: Reuse `1e40baad-…` as a multitenant workforce app; per-customer admin consent. Acceptance: a second (customer) tenant admin can consent and install.
9. **FR-09 — BFF `tid`→environment routing**: Route an authenticated `tid` to the correct Dataverse/BFF environment for the three deployment models. Acceptance: a request's `tid` resolves to the intended environment; misroute is impossible by construction.
10. **FR-10 — Teams app package + framing + org catalog**: Manifest v1.29 (`staticTabs`, complete `validDomains`, exact `webApplicationInfo`), CSP `frame-ancestors` for Teams hosts, no `X-Frame-Options: DENY`, packaged via M365 Agents Toolkit for org-catalog distribution. Acceptance: passes Teams store validation; installs from the org catalog.
11. **FR-11 — Access-grant modal (person icon on TrackingFieldTrio)**: Approve membership candidates (from FR-05, allowlist-filtered) → `sprk_externalrecordaccess` rows (provenance `membership-approved`); named-user person-picker → grant rows; send access link/invite via the built `InviteAndGrantExternalUserEndpoint` + `SendCiamOnboardingEmailAsync` (external) / deep-link (internal). Acceptance: approving/adding a contact writes a grant + (optionally) sends an invite; grants are audited + revocable.
12. **FR-12 — Standing grant (subject-level policy)** *(R1)*: A per-contact toggle (Contact form + modal option) that unions runtime contact-anchored membership into that contact's accessible set for all records with allowlisted roles (incl. future). Acceptance: enabling it grants live access across matching records without per-record rows; disabling revokes; only a grant-privileged systemuser may set it.
13. **FR-13 — Email members (email icon on TrackingFieldTrio)** *(R1)*: Opens `SendEmailDialog` (`EmailComposer`, ADR-045) pre-populated with the record's membership contacts. Acceptance: composes an email to all membership contacts; send flows through the `sprk_communication` pipeline.
14. **FR-14 — Access-Permission posture**: The existing record-level Standard/Limited/Restricted OptionSet governs the grant modal as a **sharing gate** (proposed Option A — see Unresolved Q1), *distinct* from the per-grant `sprk_accesslevel`. Acceptance: on Restricted the modal blocks external grants; on Limited only named grants (no standing); on Standard all grant types.
15. **FR-15 — SPA unchanged**: The standalone external-access SPA continues functioning on the CIAM plane. Acceptance: no regression to external CIAM sign-in / access.
16. **FR-16 — Foundation spike (first)**: Validate end-to-end in a Teams tab (desktop + web): (a) systemuser → membership; (b) contact-only workforce user → contact-anchored membership; (c) SPA still works via CIAM. Acceptance: all three verified before broad build commits.

### Non-Functional Requirements
- **NFR-01 — BFF publish hygiene (ADR-029)**: ≤60 MB compressed (baseline ~49.63 MB incl. PDBs); **no** M365 Agents SDK / Bot packages added. Measure + report per BFF-touching task.
- **NFR-02 — Broker-only invariant**: The user token authenticates to the BFF only; **never** exchanged downstream (no OBO on the collaboration path); all SPE/Dataverse access app-only.
- **NFR-03 — Enterprise posture**: Org-catalog distribution under App Centric Management; least-privilege delegated scopes; **Publisher Attestation** (minimum) → M365 Certified (ideal) as a parallel commercial workstream; privacy policy + ToU.
- **NFR-04 — Iframe framing**: `Content-Security-Policy: frame-ancestors 'self' https://teams.microsoft.com https://*.cloud.microsoft`; non-redirect auth (Teams SSO/NAA/popup); tolerant of Conditional Access.
- **NFR-05 — Role-allowlist safety**: Membership-derived access (candidate discovery + standing grants) MUST be filtered to access-conferring roles; adverse/informational roles (opposing counsel, regarding-person) MUST NOT confer access.
- **NFR-06 — Best-effort notify + audit**: Invite/email failures MUST NOT fail the grant write; all grants carry `grantedby`/date provenance and are revocable.
- **NFR-07 — UX**: Fluent v9 dark-mode support (ADR-021); correlation IDs Teams→BFF→workers.

## Technical Constraints

### Applicable ADRs
- **ADR-028** (+A1, +proposed **A2**) — Spaarke Auth; collaboration standalone-MSAL + pluggable authority (CIAM + workforce); broker-only.
- **ADR-034** — User-Record Membership (extended with a contact-anchored entry).
- **ADR-024** — Polymorphic Resolver / regarding family (person = `contact`).
- **ADR-045** — Communication Architecture (canonical `EmailComposer`/`SendEmailDialog`).
- **ADR-007** — `SpeFileStore` facade for SPE ops.
- **ADR-008** — endpoint filters for authorization (not global middleware).
- **ADR-009** — Redis-first caching (participation/membership cache).
- **ADR-010 / ADR-019 / ADR-001** — DI minimalism / ProblemDetails / Minimal API.
- **ADR-006 / ADR-012 / ADR-021 / ADR-022** — PCF over webresources / shared component library / Fluent v9 / React.
- **ADR-029** — BFF publish hygiene (≤60 MB).

### MUST Rules
- ✅ MUST authenticate Teams users with **workforce Entra** via Teams SSO/NAA (multitenant); MUST NOT use CIAM inside Teams.
- ✅ MUST keep the collaboration path **broker-only** (no OBO; app-only downstream).
- ✅ MUST filter membership-derived access to the **role allowlist** (NFR-05).
- ✅ MUST use `SpeFileStore` (ADR-007), endpoint filters (ADR-008), and ProblemDetails; MUST NOT inject Graph SDK types.
- ✅ MUST reuse `sprk_externalrecordaccess`, `MembershipResolverService`, `InviteAndGrantExternalUserEndpoint`, `SendEmailDialog`, `TrackingFieldTrio` — extend, do not fork.
- ❌ MUST NOT duplicate a feature component across hosts without a §11 sign-off.
- ❌ MUST NOT add M365 Agents SDK / Bot packages (no conversational bot in R1).

### Existing Patterns
- Host adapter: `src/client/office-addins/` `IHostAdapter`.
- Auth strategy: `src/client/external-spa/src/auth/msal-config.ts` (MSAL v5) + `@spaarke/auth` `strategies/` (pattern only).
- Membership: `Services/Ai/Membership/MembershipResolverService.cs` (`BuildFetchXml`), `IdentityNormalizationService.cs`, `Api/Membership/MembershipEndpoints.cs` (`ResolveSystemUserIdAsync`).
- External access: `Api/ExternalAccess/*` (`GrantExternalAccessEndpoint`, `InviteAndGrantExternalUserEndpoint`), `Infrastructure/ExternalAccess/ExternalCallerContext.cs` (`HasProjectAccess`), `ExternalParticipationService`.
- Modal: `docs/standards/MODAL-DECISION-CRITERIA.md`, `RecordNavigationModalShell`.
- Framing: `src/client/external-spa/staticwebapp.config.json`.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>Y</bff>                <!-- workforce→principal resolver, contact-anchored membership, tid→env routing, collaboration endpoints -->
  <spaarkeai>N</spaarkeai>    <!-- does NOT modify src/solutions/SpaarkeAi/**; reuses shared-lib components only -->
  <ci-workflows>Y</ci-workflows> <!-- new Teams-app deploy workflow (parallels deploy-external-spa.yml) -->
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
**Placement Justification (BFF=Y)**: new BFF surface is a workforce→principal resolver + `tid`→env routing + reuse of membership/authz/SPE/invite services — auth-resolution + hosting concerns that belong in the single backend. No AI-internal types in collaboration code. Publish-size impact expected negligible (no Agents/Bot deps). Cite `.claude/constraints/bff-extensions.md`; measure ≤60 MB per task.

### New Components (§11)
| New component | Existing overlap (grep) | Extend? | Cost-of-doing-nothing (concrete) |
|---|---|---|---|
| Teams host adapter | Office `IHostAdapter`; no Teams host | New | No Teams tab can bootstrap (`app.initialize`, context, framing) |
| Teams SSO auth strategy | `@spaarke/auth` strategies (Xrm/v3); external-spa MSAL v5 | New (in shared standalone-MSAL) | Teams user cannot authenticate seamlessly (workforce SSO) |
| workforce→principal resolver | `MembershipEndpoints.ResolveSystemUserIdAsync`; `IdentityNormalizationService.TryResolveContactIdAsync` | Extend (reuse both) | Workforce token can't be scoped to systemuser or contact on collaboration endpoints |
| contact-anchored membership entry | `MembershipResolverService.BuildFetchXml` matches `ContactId`; no contact-only entry | Extend (add entry + `PersonIdentity`-from-`contactId`) | Non-systemuser staff (Option B) get no membership-based access; grant-modal has no candidate source |
| grant modal + 2-icon toolbar | `TrackingFieldTrio` PCF; `sprk_externalrecordaccess`; `SendEmailDialog` | Extend PCF (reuse tables/dialogs) | No record-level UX to grant external access or email members |
| BFF `tid`→env routing | none | New | Spaarke-hosted multi-customer / dedicated-env deployments route to the wrong environment |
| standing-grant field on `contact` + runtime union | `unified-access-control` (design-only); `ExternalParticipationService` cache | New field + logic | In-house counsel cannot grant an outside-counsel contact standing access across all their records |

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-028 | "auth MUST flow through `@spaarke/auth`; no direct `PublicClientApplication`" | The Teams host is a workforce-authenticated **standalone (non-Xrm)** app; `@spaarke/auth` is Xrm-bound + MSAL v3 and cannot serve it | **B (amendment)** | A2 draft widens the A1 exemption to a shared standalone-MSAL module with pluggable authority (CIAM + workforce). Internal Xrm surfaces unaffected. See `adr-028-amendment-draft.md`. |
| ADR-034 | Membership entry is systemuser-anchored (self-/systemuser-only endpoint) | R1 needs a contact-only entry for non-systemuser principals | **C (comply-with-intent) + additive note** | Reuses the existing engine (`BuildFetchXml` already matches `ContactId`); an additive contact-only entry point + role allowlist — not a second mechanism. Note in ADR when it lands. |

## Success Criteria
1. [ ] A systemuser opens the Teams tab, signs in via workforce SSO with **no second login**, and sees exactly their **membership** records — Verify: manual + integration test in Teams desktop + web.
2. [ ] A **contact-only** workforce user sees exactly their **contact-anchored membership** records — Verify: seam test with a non-systemuser principal.
3. [ ] Document download returns bytes for an authorized member and **403 with no bytes** for a non-member, for all three principal types — Verify: positive + negative tests.
4. [ ] The same feature components render in the SPA and the Teams tab with no duplicated feature component — Verify: code inspection / grep for host forks.
5. [ ] The grant modal writes `sprk_externalrecordaccess` (approved membership + named users) and sends an invite; grants are revocable — Verify: end-to-end grant + access + revoke test.
6. [ ] The app installs from the org catalog in a second (customer) tenant via admin consent; `tid`→env routing serves the correct environment — Verify: two-tenant install test.
7. [ ] BFF publish ≤60 MB; no new HIGH CVE — Verify: `dotnet publish -c Release` + `dotnet list package --vulnerable`.

## Dependencies

- **ADR-028 A2 amendment** merged before/with the Teams-host auth code.
- Reuse of the deployed external-access base (`spaarke-SPA-external-access-platform-r1`) + Entra app `1e40baad-…` (Teams redirect URIs + `access_as_user` already present).

### External Prerequisites (admin-owned — NOT project tasks, per owner)
- **`systemuser.sprk_primarycontact` linked** for internal systemusers — an **admin activity outside this project**, assumed complete (owner, 2026-08-03). Required for a systemuser's membership to reach contact-role assignments. *(The contact side for non-systemuser workforce users resolves by verified email, so it needs no admin pre-link.)*
- **Go-live readiness verification**: before launch, admin/ops confirms the `sprk_primarycontact` links are set on the target org (a deployment-checklist item — this is the reframed "empirical check", not project work).

### External Dependencies
- Customer tenant admin consent (per-customer); org app-catalog upload/approval.
- M365 Agents Toolkit for build/publish; Publisher Attestation (commercial workstream).

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Teams auth | CIAM-in-Teams vs workforce SSO for R1? | **Workforce SSO (Option 2)** — enterprise-expected | Multitenant workforce app; workforce→principal resolver |
| Non-systemusers | Must non-systemuser internal staff work in Teams? | **Yes (Option B)** | Adds workforce→contact + contact-anchored membership |
| Base app | Extend external-spa or fork? | **Extend in place** + Teams host adapter | One codebase, host-detected |
| Record scoping | Membership vs manual grants for internal? | **Membership (derive-from-membership)** | ADR-034 (systemuser) + contact-anchored (contact) |
| Contact access | Auto vs deliberate? | **Grant-mediated** (per-record modal + standing grant); nothing auto-granted to a contact | One `sprk_externalrecordaccess` enforcement path; role allowlist |
| Grantor UX | Where to manage access? | **Extend `TrackingFieldTrio` PCF** with person + email icons | Grant modal + email-members |
| Standing grant | Auto-grant a contact across all assigned records? | **Yes** — subject-level runtime policy; **R1** | UAC `sprk_accesssubject` seed; runtime union |
| Access-Permission | What does Standard/Limited/Restricted govern? | **Option A** — record-level sharing gate (Restricted=off / Limited=named-only / Standard=all) | Grant-modal gating; distinct from per-grant `sprk_accesslevel` |
| Role allowlist | Which contact fields confer access + future-proofing? | **Convention-based** (`sprk_assigned*` via metadata discovery) + exclusion list; new fields auto-qualify | No hardcoded list; FR-05 filter; extensible per owner note |
| primarycontact / phasing | Backfill ownership; standing+email phasing | primarycontact = **admin, out of scope**; standing grants + email = **R1** | External prerequisite; full R1 scope |

## Resolved Decisions (2026-08-03)

- **Phasing** ✅ — standing grants (FR-12) + email icon (FR-13) are **R1** (not deferred).
- **Role allowlist — convention-based + extensible** ✅ (owner-confirmed, incl. future extensibility). Access-conferring = **any `contact`-target lookup whose logical name matches the `sprk_assigned*` convention**, resolved by metadata discovery (the same ADR-034 `MembershipFieldDiscoveryService` mechanism) — **NOT a hardcoded field list**. So **new `sprk_assigned*` fields added later auto-qualify with no code change** (owner note, 2026-08-03). An explicit **exclusion list** (config/data-driven) handles any future adverse `contact` field (e.g., an opposing-counsel lookup) without a code deploy. R1 verified set on `sprk_project`: `sprk_assignedattorney1/2`, `sprk_assignedparalegal1/2`, `sprk_assignedtoexternal`, `sprk_assignedtointernal` (all → `contact`; no adverse field present). Polymorphic `sprk_regardingrecord*` are not contact lookups → naturally excluded.
- **Access-Permission posture (FR-14) — Option A (sharing gate)** ✅ (owner-confirmed). Record-level gate, distinct from per-grant `sprk_accesslevel`: **Restricted** = external access off (modal blocks grants); **Limited** = named grants only (no standing/auto); **Standard** = all grant types incl. standing grants + membership auto-approval.
- **`sprk_primarycontact`** ✅ — admin-owned, out of project scope (see External Prerequisites).
- **Empirical check** ✅ — reframed to a go-live readiness verification (admin), not project work (see External Prerequisites).
- **Notify branch** — external contact → CIAM onboarding email (built); internal workforce contact → deep-link notification (small addition).

## Unresolved Questions
> All design-time questions resolved (2026-08-03). None blocking `/project-pipeline`.

---
*AI-optimized specification. Original design: `design.md`.*
