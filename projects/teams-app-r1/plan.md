# Implementation Plan — Spaarke Teams App (R1)

> **✅ COMPLETE (2026-08-06)** — all 26 tasks ✅; code shipped + deployed to `spaarke-bff-dev` + live-verified in Teams; merged to master (PR #723). Graduation: 6/7 fully verified, criterion 6-live = accepted go-live item (Path A). Wrap-up: `notes/integration-verification-report.md`, `notes/test-diet-report.md`, `notes/lessons-learned.md`. **Next**: go-live checklist (customer-tenant org-catalog install + admin consent + `sprk_primarycontact` linkage); R2 lifts `CallerPrincipalResolver` per `notes/r2-coordination-response.md`.
>
> **Source**: `spec.md` (16 FRs, 7 NFRs) + `design.md` (D1–D11) + `adr-028-amendment-draft.md`
> **Created**: 2026-08-03
> **Execution model**: Sonnet 5 @ effort `high` default; `opus`/`xhigh` for auth-resolver, membership, `tid`→env routing, and ADR-migration tasks. Planning on Opus 4.8.

---

## 1. Executive Summary

**Purpose**: Stand up the Spaarke collaboration surface inside Microsoft Teams — workforce-SSO authenticated, membership-authorized, broker-only documents — as a second host over the shared collaboration core, proving the dual-host pattern end-to-end. This is a **foundation milestone**, not a re-skin of the system-of-record.

**Scope boundary**: Host mechanics + workforce→principal resolution + record-level access-management UX + enterprise deployment posture. AI, native Teams-channel messaging, matters/comms features, and the full UAC layer are explicitly out (R2+).

**Estimated effort**: Medium-Large. Critical path is the auth→membership→enforcement spine (must serialize); the PCF grant surface, Teams packaging, and independent BFF endpoints parallelize. **Spike-first** gates broad build commits.

---

## 2. Architecture Context

**Shared core + thin host adapter** (D1). One collaboration core (feature components + BFF client + authorization contract); divergence confined to the host-adapter seam (auth strategy, bootstrap, framing, theme, nav) and per-host config. No duplicated feature components without a §11 sign-off.

**Two identity planes → one authorization model**:
- Standalone SPA: CIAM MSAL v5 → **contact** (`sprk_externalobjectid`).
- Teams tab (R1): workforce Teams SSO/NAA → **systemuser** (AAD `oid`), else **contact**.
- Both resolve to a **principal**; the BFF enforces `record ∈ accessible(principal)`.

**Accessible-record-set** (D3/D11):
```
accessible(principal) = systemuser → ADR-034 membership (auto)
                      ∪ contact    → sprk_externalrecordaccess grants
                      ∪ contact    → standing-grant runtime membership
```

**Technology stack**: React 18 + Vite + MSAL v5 + Fluent v9 + `@spaarke/ui-components` (client); .NET 8 Minimal API (BFF); Teams JS SDK + M365 Agents Toolkit; Azure Static Web Apps hosting; PCF (`TrackingFieldTrio`). Broker-only SPE via `SpeFileStore` (app-only).

**Integration points**: BFF dual JwtBearer (`AuthorizationModule.cs`); `MembershipResolverService` (`BuildFetchXml`); `IdentityNormalizationService`; `Api/ExternalAccess/*` (`InviteAndGrantExternalUserEndpoint`); `SendEmailDialog`/`sendCommunication` (ADR-045); `external-spa/staticwebapp.config.json` framing; Entra app `1e40baad-…`.

### Discovered Resources

**Applicable ADRs**: ADR-028 (+A1 +proposed A2) auth · ADR-034 membership (contact-anchored extension) · ADR-024 polymorphic resolver · ADR-045 communication · ADR-007 SpeFileStore · ADR-008 endpoint filters · ADR-009 Redis caching · ADR-010 DI minimalism · ADR-019 ProblemDetails · ADR-001 Minimal API · ADR-006/012/021/022 PCF/shared-lib/Fluent-v9/React · ADR-029 BFF publish hygiene.

**Applicable skills**: `office-addins-deploy` (Teams SWA + manifest patterns), `code-page-deploy`, `pcf-deploy` (TrackingFieldTrio), `bff-deploy`, `dataverse-create-schema` (contact standing-grant field), `adr-aware`, `conflict-check` (BFF hot-path — 13+ active worktrees), `code-review`, `adr-check`, `ui-test` (PCF/Teams tab).

**Knowledge docs / canonical impls**: `docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` (host checklist §8 = Teams acceptance test) · `docs/standards/MODAL-DECISION-CRITERIA.md` + `MODAL-DESIGN-SYSTEM.md` (grant modal) · `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` · `.claude/constraints/bff-extensions.md` (§10) · `src/client/office-addins/` `IHostAdapter` · `src/client/external-spa/src/auth/msal-config.ts` · `Services/Ai/Membership/MembershipResolverService.cs` + `IdentityNormalizationService.cs` · `Api/Membership/MembershipEndpoints.cs` (`ResolveSystemUserIdAsync`) · `Infrastructure/ExternalAccess/ExternalCallerContext.cs` (`HasProjectAccess`) · `SpeFileStore.DownloadFileAsync`.

**Scripts**: PCF deploy (`Deploy-PCFWebResources.ps1`), BFF publish-size verification (`dotnet publish -c Release`), `Validate-TaskPoml.ps1` (completeness lint).

---

## 3. Implementation Approach

**Critical path (serialized)**: Spike → ADR-028 A2 amendment → shared standalone-MSAL + Teams SSO strategy + host adapter → workforce→principal resolver → contact-anchored membership → accessible-record-set enforcement → broker download gating.

**Parallel-safe streams** (after the resolver/membership core lands):
- **Stream P (PCF/UX)**: `TrackingFieldTrio` two-icon toolbar + grant modal + email-members.
- **Stream M (Manifest/Deploy)**: Teams manifest v1.29 + framing headers + M365 Agents Toolkit packaging + CI deploy workflow.
- **Stream D (Dataverse/data)**: contact standing-grant field + runtime union logic.
- **Stream E (Enterprise/routing)**: `tid`→environment routing.

**`.claude/`-touching tasks** (ADR-028 A2 amendment application; any ADR-034 note) are **`parallel-safe:false`, main-session-only** (root CLAUDE.md Sub-Agent Write Boundary).

---

## 4. WBS (Work Breakdown Structure)

### Phase 0 — Foundation Spike & ADR (gate before broad build) — FR-16, ADR tension B
- **Objectives**: Prove the auth→principal→membership path in a real Teams tab before committing broad build; land the ADR-028 A2 amendment.
- **Deliverables**: (a) Teams tab spike validating systemuser→membership + contact→contact-anchored membership + SPA-still-works-via-CIAM (desktop + web); (b) ADR-028 A2 amendment applied to concise + full ADR (Path B); (c) spike findings note.
- **Inputs**: spec, design, A2 draft, Entra app `1e40baad-…`.
- **Outputs**: go/no-go spike result; merged ADR-028 A2; validated auth assumptions.
- **Dependencies**: none (first). ADR amendment ordered **before/with** auth tasks.

### Phase 1 — Shared Auth Module & Host Adapter Seam — FR-02, FR-03, ADR-028 A2
- **Objectives**: One shared standalone-MSAL module with pluggable authority; Teams SSO/NAA strategy; host-adapter seam over the shared core.
- **Deliverables**: pluggable-authority MSAL module (CIAM + workforce-multitenant); Teams SSO client strategy; `IHostAdapter`-modeled Teams adapter (bootstrap `app.initialize()` + context, framing, theme, nav); host-detection wiring; SPA continues unchanged (FR-15).
- **Inputs**: Phase 0 (auth validated); `external-spa` base; Office `IHostAdapter` pattern.
- **Outputs**: Teams tab renders the collaboration core with workforce token; SPA regression-free.
- **Dependencies**: Phase 0.

### Phase 2 — BFF Workforce→Principal Resolver & Membership — FR-04, FR-05, FR-06 (critical path)
- **Objectives**: Resolve workforce token → principal; add contact-anchored membership; compose + enforce accessible-record-set.
- **Deliverables**: workforce→principal resolver (reuse `ResolveSystemUserIdAsync` + `TryResolveContactIdAsync`); contact-anchored entry on `MembershipResolverService` (reuse `BuildFetchXml`; `PersonIdentity`-from-`contactId`) filtered to convention-based role allowlist (`sprk_assigned*` via metadata discovery + exclusion list); accessible-record-set composition + positive/negative enforcement gate; collaboration endpoints generalized to the workforce plane.
- **Inputs**: Phase 1 (token reaches BFF); ADR-034 engine.
- **Outputs**: each principal type resolves to exactly its record set; enforcement tested positive + negative.
- **Dependencies**: Phase 1. **Serialize internally**: resolver → membership → enforcement.

### Phase 3 — Broker Document Access — FR-07, NFR-02
- **Objectives**: All principals download via app-only broker, authz-before-stream, no Graph pointers.
- **Deliverables**: collaboration download gated by accessible-set check through `SpeFileStore.DownloadFileAsync`; 403-no-bytes negative path; no `driveId`/`itemId` to client.
- **Inputs**: Phase 2 (accessible-set available).
- **Outputs**: authorized member → bytes; non-member → 403 no bytes (all three principal types).
- **Dependencies**: Phase 2.

### Phase 4 — Access-Management Surface (PCF) — FR-11, FR-12, FR-13, FR-14 [Stream P, parallel after Phase 2]
- **Objectives**: `TrackingFieldTrio` becomes the record governance card.
- **Deliverables**: two-icon toolbar; person-icon grant modal (approve membership candidates → `sprk_externalrecordaccess`; named-user picker; standing-grant option; invite via `InviteAndGrantExternalUserEndpoint` + CIAM onboarding / internal deep-link); email-icon → `SendEmailDialog` pre-populated with membership contacts (ADR-045); Access-Permission Option-A gating (Restricted/Limited/Standard); ADR-021 dark mode; `<ui-tests>`.
- **Inputs**: Phase 2 (contact-anchored candidate source); `TrackingFieldTrio` PCF; `SendEmailDialog`.
- **Outputs**: grantable/revocable external access + email-members from the record.
- **Dependencies**: Phase 2 (candidate source). Parallel-safe with Streams M/D/E.

### Phase 5 — Standing Grant Data & Runtime Union — FR-12 [Stream D, parallel]
- **Objectives**: Per-contact subject-level standing grant.
- **Deliverables**: standing-grant field on `contact` (Dataverse); runtime union into accessible-set for all allowlisted-role records (incl. future); grant-privileged-only write; enable/disable = grant/revoke.
- **Inputs**: Phase 2 (accessible-set composition).
- **Outputs**: standing grant confers live cross-record access without per-record rows.
- **Dependencies**: Phase 2. Parallel-safe with Streams P/M/E.

### Phase 6 — Multitenant Entra, `tid`→Env Routing & Framing — FR-08, FR-09, FR-10, NFR-04 [Stream E/M, parallel]
- **Objectives**: Enterprise multitenant posture + correct-environment routing + Teams framing.
- **Deliverables**: multitenant workforce app config (reuse `1e40baad-…`) + admin-consent onboarding; BFF `tid`→environment routing (three deployment models; misroute impossible by construction); CSP `frame-ancestors` for Teams hosts (no `X-Frame-Options: DENY`); framing config on the SWA host.
- **Inputs**: Phase 1 (workforce scheme); Phase 2 (env-scoped data access).
- **Outputs**: second-tenant admin consent + install; `tid` serves the intended environment.
- **Dependencies**: Phase 1/2. Parallel-safe with Streams P/D.

### Phase 7 — Teams App Package & CI Deploy — FR-01, FR-10, NFR-03 [Stream M, parallel]
- **Objectives**: Ship the installable Teams app to the org catalog.
- **Deliverables**: manifest v1.29 (`staticTabs`, complete `validDomains`, exact `webApplicationInfo`); M365 Agents Toolkit packaging; org-catalog distribution under App Centric Management; new CI deploy workflow (parallels `deploy-external-spa.yml`); Publisher Attestation prep (commercial workstream note).
- **Inputs**: Phases 1/6 (host + framing).
- **Outputs**: passes Teams store validation; installs from org catalog.
- **Dependencies**: Phase 1 (host renders), Phase 6 (framing/routing).

### Phase 8 — Integration, Verification & Wrap-Up — all success criteria
- **Objectives**: End-to-end verification against graduation criteria; project close.
- **Deliverables**: two-tenant install test; positive/negative download tests (3 principal types); no-duplicated-component grep; BFF publish ≤60 MB + CVE check; `090-project-wrap-up` (README status, lessons-learned, test-diet).
- **Inputs**: Phases 0–7.
- **Outputs**: all graduation criteria verified; project archived.
- **Dependencies**: all prior phases.

---

## 5. Placement Justification (CLAUDE.md §10)

New BFF surface = workforce→principal resolver + `tid`→env routing + reuse of membership/authz/SPE/invite services — auth-resolution + hosting concerns that belong in the single backend. **No AI-internal types** injected into collaboration code (use `PublicContracts/` facade if any AI need arises — none in R1). Publish-size impact expected **negligible**: no M365 Agents SDK / Bot packages. Baseline ~49.63 MB incl. PDBs; ceiling **≤60 MB compressed**; measure + report on **every** BFF-touching task per `.claude/constraints/bff-extensions.md`. Run `/conflict-check` before every BFF PR (13+ active BFF worktrees).

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>Y</bff>                <!-- workforce→principal resolver, contact-anchored membership, tid→env routing, collaboration endpoints -->
  <spaarkeai>N</spaarkeai>    <!-- reuses shared-lib components only; does NOT modify src/solutions/SpaarkeAi/** -->
  <ci-workflows>Y</ci-workflows> <!-- new Teams-app deploy workflow (parallels deploy-external-spa.yml) -->
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

---

## 6. Dependencies

**External**: ADR-028 A2 amendment merged before/with the Teams-host auth code (Path B); customer tenant admin consent (per-customer); org app-catalog upload/approval; M365 Agents Toolkit; Publisher Attestation (parallel commercial workstream). Reuse of the deployed external-access base + Entra app `1e40baad-…`.

**Internal**: `external-spa` base; BFF dual JwtBearer schemes; `MembershipResolverService` / `IdentityNormalizationService`; `Api/ExternalAccess/*`; `SendEmailDialog`; `TrackingFieldTrio` PCF + shared core; `SpeFileStore` broker.

**External prerequisites (admin-owned, NOT project tasks)**: `systemuser.sprk_primarycontact` linkage; go-live readiness verification of those links (deployment-checklist item).

---

## 7. Testing Strategy

- **Unit**: resolver mapping (systemuser / contact-only / unresolvable→deny); role-allowlist filter (allowlisted match; adverse/non-allowlisted never match); accessible-set composition per principal.
- **Integration / seam**: vertical-slice seam tests (per ADR-038) for workforce→principal→membership→enforcement; contact-anchored membership with a non-systemuser principal; download authz-before-stream (positive + **403-no-bytes** negative) across 3 principal types.
- **UI (`ui-test`)**: Teams tab renders in Teams desktop + web (no console errors); grant modal writes/revokes; email modal pre-populates membership contacts; ADR-021 dark-mode compliance.
- **Acceptance**: graduation criteria (§README) — two-tenant install + `tid` routing; no-duplicated-component grep; BFF publish ≤60 MB; no new HIGH CVE.

---

## 8. Acceptance Criteria

Copied from README graduation criteria; each success criterion in `spec.md` carries an explicit Verify step. Every BFF-touching task additionally verifies publish size ≤60 MB and reports the diff vs the ~49.63 MB baseline.

---

## 9. Risk Register

| Risk | Mitigation |
|---|---|
| Teams SSO/NAA fails in desktop client (popup handling / Conditional Access) | Spike-first (Phase 0) in desktop + web before broad build; non-redirect auth; CA-tolerant flows (NFR-04). |
| `sprk_primarycontact` not linked → systemuser membership silently skips contact-role assignments | Admin prerequisite + go-live readiness verification (deployment checklist); documented dependency. |
| BFF publish size creeps toward 60 MB | No Agents/Bot packages; measure per task; `/conflict-check` before every BFF PR. |
| ADR-028 A2 amendment not merged before auth code | Phase 0 gates the amendment application ahead of Phase 1 auth work; main-session-only. |
| Merge friction with 13+ active BFF worktrees | `/conflict-check` before every BFF PR; serialize shared-file edits; `parallel-safe:false` on contended files. |
| Duplicated feature component across hosts (dual-host drift) | §11 sign-off required for any duplication; adapter-only divergence enforced by grep in Phase 8. |

---

## 10. Next Steps

1. Task files generated under `tasks/` with `TASK-INDEX.md` (parallel groups + critical path).
2. **Execution is owner-gated** — run waves deliberately via `task-execute` (per owner `notes/pipeline-run-guidance.md`); pipeline does NOT auto-execute.
3. Apply ADR-028 A2 amendment (main-session) before Phase 1 auth tasks.
4. Run `/conflict-check` before every BFF PR.
