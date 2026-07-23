# Project Plan: Spaarke External Access Platform — Custom SPA + Entra External ID (R1)

> **Last Updated**: 2026-07-19
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md) · **Design**: [design.md](design.md)

---

## 1. Executive Summary

**Purpose**: Migrate the external Secure Project Workspace's hosting + identity layer from Power Pages + Entra B2B to Azure Static Web Apps + Entra External ID (CIAM), broker-only, with the minimum download + invite capability to make it usable — without touching the BFF business logic or three-plane authorization.

**Scope**:
- SWA hosting + BrowserRouter + security headers (Phase 1, on existing B2B).
- CIAM tenant/app + second JwtBearer scheme + admin-initiated provisioner + invite trigger + oid resolution (Phase 2).
- App-only document download (reuse existing method) + `sprk_externalobjectid` schema (Phase 2).
- Power Pages decommission + doc rewrite (Phase 3).

**Timeline**: sequenced Phase 0→3. **Estimated effort**: ~15–22 dev-days, gated on live Azure/CIAM resource availability (Phase 2 verification spikes are non-blocking).

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-028 (+Amendment A1)**: CIAM authority via a second JwtBearer scheme; resolve Contact by stable `oid`; broker-only app-only invariant (no OBO on external path; no per-user B2B guest); E-3 direct-Office boundary out of scope.
- **ADR-008**: external endpoints keep the per-endpoint `ExternalCallerAuthorizationFilter`.
- **ADR-009**: Redis participation cache (60s TTL) invalidated on grant/revoke.
- **ADR-007**: use the `SpeFileStore` facade for SPE ops.
- **ADR-001/010/019**: Minimal API, DI minimalism, ProblemDetails.

**From Spec / §10 BFF hygiene**:
- BFF publish size ≤60 MB (baseline ~49.63 MB); report size + diff per BFF task.
- Authz-before-stream on the download path (highest-consequence correctness property).
- Preserve external-SPA `sessionStorage` per-tab isolation (ADR-028 exception).
- No new HIGH CVE.

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| CIAM (Entra External ID), not B2C or B2B | B2C end-of-sale; broker-only spike removes dual-identity need | New tenant + app; second JwtBearer scheme |
| Resolve Contact by `oid`, not email | email is mutable/social-IdP-variable; `sub` is pairwise | New `Contact.sprk_externalobjectid` field |
| Admin-initiated onboarding; sign-up disabled | R1 decision; Legal Front Door deferred | `isSignUpAllowed=false`; onboarding-agnostic hook |
| Reuse existing app-only `DownloadFileAsync` | already exists + proven; avoid duplication | Only a thin external endpoint is new |
| Cross-tenant Graph client modeled on `SpeAdminTokenProvider` | `GraphClientFactory` is single-tenant | New per-authority client + KV/MI-FIC credential |

### Discovered Resources

**Applicable Skills**: `azure-deploy` (SWA), `bff-deploy`, `dataverse-create-schema` (`sprk_externalobjectid`), `code-review`, `adr-check`, `ui-test`, `merge-to-master`.

**Knowledge / Docs**: `docs/architecture/external-access-spa-architecture.md`, `docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md`, `EXTERNAL-ACCESS-SPA-GUIDE.md`, `docs/guides/auth-deployment-setup.md`, `.claude/constraints/bff-extensions.md`, `.claude/agent-memory/researcher/ciam-user-provisioning-graph-2026-07-19.md`.

**Reusable Code** (from BFF audit — REUSE, don't rebuild):
- `Services/SpeAdmin/SpeAdminTokenProvider.cs` → cross-tenant CIAM Graph client template
- `Services/Registration/GraphUserService.cs` + `PasswordGenerator.cs` → user-create payload
- `Services/Registration/RegistrationEmailService.cs` + `EmailTemplates/` → onboarding email
- `Infrastructure/Graph/SpeFileStore.cs` `DownloadFileAsync` + `DocumentStorageResolver` → download
- `Api/Filters/ExternalCallerAuthorizationFilter.cs` + `Infrastructure/ExternalAccess/ExternalParticipationService.cs` → auth (extend)
- `Infrastructure/DI/AuthorizationModule.cs` → JwtBearer scheme registration
- `Services/Registration/TrackingIdGenerator.cs`, `RegistrationDataverseService.cs` → tracking/dedup

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: Foundations (infra + schema)
└─ CIAM tenant + app-reg (User.ReadWrite.All, isSignUpAllowed=false, SSPR Email OTP)
└─ Azure Static Web Apps resource + CI/CD scaffold
└─ Contact.sprk_externalobjectid schema field

Phase 1: Hosting migration + routing (on existing B2B — isolate routing regressions)
└─ SWA deploy of external-spa; HashRouter → BrowserRouter + navigationFallback + 404
└─ deep-link-through-login; security headers; BFF CORS + redirect URIs

Phase 2: Identity + provisioning + document content (CIAM)
└─ second "Ciam" JwtBearer scheme; pin on /api/v1/external group
└─ cross-tenant CIAM Graph client; admin-initiated provisioner (account + oid + email)
└─ core-user "Invite to Secure Workspace" trigger; grant cleanup (drop synthetic SPE)
└─ Contact resolution by oid; app-only download endpoint (reuse DownloadFileAsync)

Phase 3: Cutover & decommission
└─ verify parity; retire Power Pages site + Deploy-ExternalWorkspaceSpa.ps1
└─ rewrite architecture doc + EXTERNAL-ACCESS-* guides
```

### Critical Path

- **Phase 0 blocks Phase 1/2** (need SWA resource + CIAM tenant/app + schema field before wiring).
- **Phase 1 precedes Phase 2** (prove routing/hosting on known-good B2B before swapping IdP).
- **Phase 2 precedes Phase 3** (parity before decommission).
- Within Phase 2: CIAM scheme + Graph client BLOCK the provisioner and oid-resolution.

**High-Risk Items:**
- Cross-tenant Graph credential (MI-as-FIC preview vs cert) — Mitigation: default to cert-in-KV; confirm MI-FIC GA in a spike.
- Authz-before-stream correctness on the download path — Mitigation: explicit negative/authorization test as a closed acceptance criterion.
- Live CIAM tenant availability for end-to-end verification — Mitigation: Phase-2 verification spikes are non-blocking; architecture already GREEN.

---

## 4. Phase Breakdown

### Phase 0: Foundations

**Objectives:** Stand up the infra + schema prerequisites so Phases 1–2 can wire against real resources.

**Deliverables:**
- [ ] Entra External ID (CIAM) tenant provisioned; user flow `isSignUpAllowed=false`; SSPR (Email OTP) enabled.
- [ ] CIAM-tenant app registration with Graph `User.ReadWrite.All`; credential decided (cert-in-KV or MI-as-FIC) + stored.
- [ ] Azure Static Web Apps resource + CI/CD workflow scaffold.
- [ ] `Contact.sprk_externalobjectid` (text) created via `dataverse-create-schema`.

**Inputs**: Azure subscription access; spec §Dependencies. **Outputs**: tenant/app IDs, SWA hostname, KV secret ref, schema field — recorded in `config/environments.json`.

### Phase 1: Hosting migration + routing (on existing B2B)

**Objectives:** Decouple from Power Pages; prove clean-URL routing against the known-good IdP.

**Deliverables:**
- [ ] `external-spa` deployed to SWA via new workflow + `staticwebapp.config.json` (navigationFallback + security headers).
- [ ] `HashRouter` → `BrowserRouter`; in-app 404; deep-link-through-login (MSAL `state`/`redirectStartPage`).
- [ ] BFF CORS allow-list + SPA app-reg redirect URIs updated for the SWA origin.
- [ ] Security headers (`Referrer-Policy`, CSP `frame-ancestors`) verified.

**Critical Tasks:** SWA deploy + routing flip — verified on existing B2B so any regression is attributable to routing, not IdP.

**Inputs**: Phase 0 SWA resource. **Outputs**: SPA live on SWA (still B2B), CORS/redirect updated.

### Phase 2: Identity + provisioning + document content (CIAM)

**Objectives:** Swap to CIAM; implement admin-initiated provisioning + invite trigger + oid resolution; add app-only download.

**Deliverables:**
- [ ] `"Ciam"` JwtBearer scheme in `AuthorizationModule`; pinned on the `/api/v1/external` group; internal group stays on workforce default.
- [ ] Cross-tenant CIAM Graph client (model on `SpeAdminTokenProvider`).
- [ ] Admin-initiated provisioner: `POST /users` (identities/email local account, temp pw + force-change) + persist `oid` + onboarding email (reuse `RegistrationEmailService`); idempotent.
- [ ] Core-user "Invite to Secure Workspace" trigger (reuse `/external-access/invite`+`/grant` surface or thin command).
- [ ] `ExternalCallerAuthorizationFilter` + `ExternalParticipationService` extended to resolve Contact by `oid`.
- [ ] `GrantExternalAccessEndpoint` drops synthetic SPE membership.
- [ ] App-only download endpoint `GET .../documents/{documentId}/content` reusing `SpeFileStore.DownloadFileAsync` + `DocumentStorageResolver` (authz-before-resolve).
- [ ] SPA points at CIAM authority/scope/config.
- [ ] Unit tests in `tests/unit/Sprk.Bff.Api.Tests/` (incl. authz-before-stream negative case); publish-size + CVE checks.

**Critical Tasks:** CIAM scheme + Graph client (block provisioner + oid-resolution).

**Inputs**: Phase 0 tenant/app/schema; Phase 1 routing. **Outputs**: CIAM-authenticated portal with working invite + download.

### Phase 3: Cutover & decommission

**Objectives:** Verify parity; retire Power Pages; refresh docs.

**Deliverables:**
- [ ] Parity verification (functional pass on SWA+CIAM).
- [ ] Retire Power Pages site + `Deploy-ExternalWorkspaceSpa.ps1`.
- [ ] Rewrite `external-access-spa-architecture.md` + `EXTERNAL-ACCESS-*` guides (CIAM/SWA/onboarding).

**Inputs**: Phase 2 complete + verified. **Outputs**: single hosting/identity path; refreshed docs.

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Entra External ID (CIAM) tenant | GA | Med | Provision in Phase 0; verification spikes non-blocking |
| Graph `User.ReadWrite.All` (CIAM tenant) | GA | Low | Admin consent in CIAM tenant |
| Managed-Identity-as-FIC (cross-tenant) | Preview | Med | Default to cert-in-Key-Vault if not GA |
| Azure Static Web Apps | GA | Low | Standard resource |
| SSPR (Email OTP) in CIAM | GA | Low | Enable in Phase 0 |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| SpeFileStore.DownloadFileAsync (app-only) | `Infrastructure/Graph/SpeFileStore.cs` | Production |
| SpeAdminTokenProvider (cross-tenant template) | `Services/SpeAdmin/SpeAdminTokenProvider.cs` | Production |
| GraphUserService / RegistrationEmailService | `Services/Registration/` | Production |
| ExternalCallerAuthorizationFilter / ExternalParticipationService | `Api/Filters/`, `Infrastructure/ExternalAccess/` | Production |
| ADR-028 (+Amendment A1) | `.claude/adr/` | Current |

---

## 6. Testing Strategy

**Unit Tests** (BFF): CIAM token validation (scheme), provisioner idempotency, oid resolution, grant-without-synthetic-SPE, download authz-before-stream (**positive + negative/unauthorized**). Location: `tests/unit/Sprk.Bff.Api.Tests/`.

**Integration / seam**: external-caller dispatch through the new scheme + filter → participation → data (per ADR-038 seam category where applicable).

**E2E (Phase-2 live)**: core user invite → CIAM sign-in → project + document download; unauthorized download blocked.

**UI**: SPA routing (deep-link direct + through-login), Fluent v9 dark mode (ADR-021) via `ui-test`.

---

## 7. Acceptance Criteria

Mirrors README graduation criteria. Phase gates:
- **Phase 1**: SPA live on SWA (B2B), clean-URL deep links resolve, headers present, CORS/redirect updated.
- **Phase 2**: CIAM auth works on `/api/v1/external/*`; invite onboards+grants idempotently; download enforces authz-before-stream (negative test passes); no B2B guest created; publish ≤60 MB; no new HIGH CVE.
- **Phase 3**: Power Pages retired; docs rewritten; parity verified.

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Cross-tenant Graph credential (MI-FIC preview) | Med | Med | Default cert-in-KV; spike MI-FIC GA |
| R2 | Authz bypass on download path | Low | High | Authz-before-stream + negative test as closed acceptance criterion |
| R3 | CIAM token missing `email` claim | Med | Low | Link by `oid` regardless; add claim mapping if needed |
| R4 | OTP-only unsupported for Graph-created account | Med | Low | Default password + force-change + SSPR |
| R5 | Hot-path BFF collision with other active worktrees | Med | Low | Reuse-in-place minimizes surface; coordinate via `projects/INDEX.md` |
| R6 | Live CIAM tenant unavailable for E2E | Med | Med | Verification spikes non-blocking; architecture GREEN |

---

## 9. Next Steps

1. **Review this plan** + spec.
2. **Run** `/task-create` to decompose into POML task files.
3. **Begin** Phase 0 (foundations) — gated on Azure/CIAM resource provisioning.

---

**Status**: Ready for Tasks
**Next Action**: `/task-create projects/spaarke-SPA-external-access-platform-r1`
