# Project Plan: Spaarke External Access Platform (R2)

> **Last Updated**: 2026-08-06
> **Status**: Ready for Tasks (INITIALIZE-ONLY)
> **Spec**: [spec.md](spec.md) · **Design**: [design.md](design.md) · **UX Brief**: [notes/ux-brief.md](notes/ux-brief.md)

---

## 1. Executive Summary

**Purpose**: Generalize R1's single external SPA into a Teams-capable **module-host platform**
(entitlement-gated card launcher, dual identity-plane auth) and ship **Legal Front Door** (typed
intake + NDA + Policy & Procedures) as the second module — reusing delivered R1 + teams-app-r1 code.

**Scope**:
- Module-host shell + card launcher + `/me`-driven visibility (P1)
- Dual-plane auth (Teams SSO / workforce / CIAM) + Teams packaging (P1)
- Two-tier access model: module-entitlement (NEW) + record-scope (reuse) + admin UI (P2)
- Legal Front Door intake MVP: schema + framework + NDA + P&P (P3)
- R1 hardening + Front Door depth (P4)
- P0 UX prototype (prototype-first) gating all frontend build

**Timeline**: phased P0→P4 (large project) | **Estimated Effort**: multi-wave; execution owner-gated wave-by-wave.

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-028 (+A1, +A2)**: external identity/auth — CIAM broker-only, Contact-by-oid. R2 authors **Amendment A3** to ratify the dual-plane module framework + principal-agnostic endpoints (A2 already covers the Teams host).
- **ADR-008**: per-endpoint authorization filters / route-group policies (no global middleware).
- **ADR-009**: Redis-first cache for `/me` entitlement + participation; invalidate on change.
- **ADR-007**: `SpeFileStore` facade for all SPE ops (app-only download/upload).
- **ADR-001 / ADR-010 / ADR-019**: Minimal API; DI minimalism (register concretes; interface only with ≥2 impls); ProblemDetails.
- **ADR-021 / ADR-022**: Fluent v9 + React 18 for all SPA/UI surfaces; semantic tokens (dark-mode + Teams theme).
- **ADR-024**: polymorphic regarding for `sprk_servicerequest`.
- **ADR-034**: user-record membership = workforce Tier-2 record-scope.
- **ADR-038**: integration-heavy testing; KEEP-path; ban `Mock<HttpMessageHandler>` / DI-registration / ctor-null tests.
- **ADR-050**: canonical `SprkModal` shell + presets for all dialogs.

**From Spec**:
- Broker-only for CIAM (no OBO); workforce SPA path no-OBO, no Power-Apps-license dependency.
- Two independent tiers: module entitlement (Tier 1) ≠ record visibility (Tier 2); both server-enforced; negative Tier-2 test per module.
- §10 BFF hygiene: Placement Justification per addition; publish ≤60 MB compressed (baseline 46.90 MB incl PDBs); per-module `Map{Module}Endpoints` groups.
- Preserve external-SPA `sessionStorage` per-tab isolation for CIAM (do NOT switch to localStorage/@spaarke/auth on the CIAM path).
- §11: build on donor components; do NOT fork `LegalWorkspaceApp`.

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Lift + generalize FR-22 (delivered) into a module framework | `CallerPrincipalResolver` + `ExternalCollaboration` are tested (9761 green); third-plane seam ready | R2 registers per-module strategy/predicate; handlers/filter untouched |
| Module registry is code-side (not Dataverse `sprk_module`) | Owner decision; simplicity for ~10s users/month | New module = register card + lazy route + entitlement |
| App Roles for internal entitlement (single `FrontDoorUser` in R2) | No per-user provisioning; group-assignable | Resolver must support per-module roles later without rework |
| Prototype-first UX (P0) on existing `spaarke-prototype` infra | Many net-new surfaces; visual validation before build | Production frontend cites approved prototype |

### Discovered Resources

**Applicable ADRs** (concise at `.claude/adr/`, full at `docs/adr/`):
- ADR-028 (+A1/A2), ADR-008, ADR-009, ADR-007, ADR-001, ADR-010, ADR-019, ADR-021, ADR-022, ADR-024, ADR-034, ADR-038, ADR-050.
- Gaps: no full `docs/adr/` copy of ADR-024/ADR-028 (concise is canonical); no concise ADR-038 (`docs/adr/ADR-038` is standalone).

**Applicable Skills**:
- `bff-deploy`, `dataverse-create-schema`, `dataverse-deploy`, `office-addins-deploy` (SWA), `code-page-deploy`, `fluent-v9-component`, `code-review`, `adr-check`, `test-diet`, `ui-test`, `spe-integration`, `prototype-experiment-init`, `prototype-harness-extend`, `conflict-check`.
- No dedicated Teams-app-packaging skill; `declarative-agent` + `office-addins-deploy` + Teams architecture doc are the closest.

**Knowledge docs / patterns**:
- `.claude/constraints/bff-extensions.md` (binding BFF governance), `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`, `docs/standards/MODAL-DECISION-CRITERIA.md` + `MODAL-DESIGN-SYSTEM.md`.
- `docs/architecture/external-access-spa-architecture.md`, `docs/guides/EXTERNAL-ACCESS-SPA-GUIDE.md`, `EXTERNAL-ACCESS-ADMIN-SETUP.md`, `LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`, `office-outlook-teams-integration-architecture.md`.
- `.claude/patterns/auth/spaarke-sso-binding.md`, `.claude/patterns/api/{endpoint-definition,endpoint-filters,error-handling,service-registration}.md`, `.claude/patterns/ui/modal-shell.md`.

**Reusable code (verified present in worktree)**:
- BFF: `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs` (`:166`/`:244`/`:328`), `WorkforcePrincipalResolver.cs`, `AccessibleRecordSetService.cs` (`ComposeAsync`), `ContactStandingGrantReader.cs`, `Api/Filters/CallerPrincipalAuthorizationFilter.cs`, `AuthPolicies.ExternalCollaboration`, `ExternalParticipationService` (oid resolve/bind), `InviteExternalUserEndpoint.ResolveOrCreateContactAsync`.
- Client: `src/client/external-spa/**` (Teams host + MSAL NAA already present), `Spaarke.AI.Widgets/src/registry/WorkspaceWidgetRegistry.ts`, `Spaarke.UI.Components` `ActionCard`/`ActionCardRow` + `SprkModal` presets + `AccessGrantModal`, `Spaarke.Auth` `AuthStrategy`.
- CI: `.github/workflows/deploy-external-spa.yml` (SWA deploy).
- DELETE-candidates (confirmed): inert `ExternalCallerAuthorizationFilter`; transitional `/api/v1/collab` group + `WorkforceCallerAuthorizationFilter` + `WorkforcePrincipalContextEndpoint` + `WorkforceCollaborationDownloadEndpoint`.

---

## 3. Implementation Approach

### Phase Structure

```
P0: UX design + prototype (prototype-first)  ← gates all frontend build
└─ UX brief + spaarke-prototype experiment (existing infra) + owner visual approval

P1: Module-host foundation (F1–F4)
└─ ADR-028 A3 · shell + launcher · dual-plane bootstrap · Teams packaging · FR-22 generalize · OC-as-module · cleanup

P2: Access-control + entitlement (F3, F5)
└─ module-entitlement schema + BFF · /me endpoint · lazy Contact · workforce policy · D1 grading · admin UI

P3: Legal Front Door intake MVP (L1–L2)
└─ sprk_servicerequest schema · typed-intake framework · NDA module · P&P module · submitter authz + app-only upload

P4: Hardening + Front Door depth (F6, L3)
└─ provisioner self-heal · live-E2E · SSPR · legal handoff via existing MDA
```

### Critical Path

**Blocking dependencies:**
- P0 (prototype approval) BLOCKS all P1/P3 frontend build.
- ADR-028 **A3** amendment (P1, main-session-only) ordered FIRST, before P1 auth code.
- FR-22 generalization (P1) BLOCKS module-framework registration used by P2/P3.
- Module-entitlement schema (P2) BLOCKS `/me` + admin UI + Front Door entitlement.
- teams-app-r1 operator-gated BFF redeploy + live Teams E2E is a P1 prerequisite.

**High-risk items:**
- External-access BFF surface collision with teams-app-r1 — Mitigation: `/conflict-check` per BFF PR; `parallel-safe:false` on shared files.
- Cleanup deletions (`/api/v1/collab`, inert filter) — Mitigation: confirm zero callers before removal; sequence after generalization.

---

## 4. Phase Breakdown

### P0: UX Design + Prototype (prototype-first)

**Objectives:** produce + visually validate every net-new surface before production build.
**Deliverables:**
- [ ] `notes/ux-brief.md` (DONE — locked)
- [ ] `spaarke-prototype` experiment via `/prototype-experiment-init` (shell, launcher, realm chooser, Front Door intake/NDA-status/my-requests/upload) on existing `_infra` mocks + templates, consuming shared `@spaarke/ui-components`
- [ ] `sprk_servicerequest` + entitlement factories + 3-persona preset via `/prototype-harness-extend`
- [ ] Owner visual-approval gate against the required-states checklist
**Inputs**: ux-brief, `spaarke-prototype/_infra`, shared component library.
**Outputs**: approved prototype + component map (build target for P1/P3).

### P1: Module-Host Foundation (F1–F4)

**Objectives:** module registry + card launcher + dual-plane auth + Teams shell; OC as first module; cleanup.
**Deliverables:**
- [ ] ADR-028 **Amendment A3** (main-session; read existing A2 first; `.claude/adr/` + `docs/adr/`)
- [ ] Module registry (WorkspaceWidgetRegistry pattern) + card launcher (`ActionCard`/`ActionCardRow`) + `/me`-driven visibility
- [ ] Dual-plane auth bootstrap (extend `@spaarke/auth` `AuthStrategy`: CIAM + realm discovery; adopt teams-app-r1 Teams path)
- [ ] Teams personal-tab manifest + CSP `frame-ancestors`; Teams theme bridging
- [ ] FR-22 generalization: register per-module strategy/predicate over `CallerPrincipalResolver`
- [ ] Outside Counsel refactored to first registered module (R1 parity)
- [ ] Cleanup: dead Power Pages proxy/config; delete inert `ExternalCallerAuthorizationFilter`; remove `/api/v1/collab` group (once zero callers)
**Critical Tasks:** ADR-028 A3 FIRST (main-session, `parallel-safe:false`).
**Inputs**: R1 `external-spa`, teams-app-r1 delivered code, approved P0 prototype.
**Outputs**: deployable module-host shell + Teams package; generalized module framework.

### P2: Access-Control + Entitlement (F3, F5)

**Objectives:** two-tier access model + `/me` + admin UI.
**Deliverables:**
- [ ] Module-entitlement Dataverse schema (shape resolved here; `dataverse-create-schema`)
- [ ] Module-entitlement resolver (App-Role internal + per-Contact external strategies)
- [ ] `/me` entitlement endpoint (Redis-cached + invalidated, ADR-009)
- [ ] Lazy Contact attribution (reuse `ResolveOrCreateContactAsync` + oid bind)
- [ ] Workforce-plane external-app auth policy
- [ ] D1: workforce role→level grading via `WorkforcePrincipalStrategy`
- [ ] Core-user admin UI (Fluent v9, dark-mode; reuse `AccessGrantModal`)
**Inputs**: P1 module framework, teams-app-r1 workforce resolver.
**Outputs**: entitlement layer + `/me` + admin surface.

### P3: Legal Front Door Intake MVP (L1–L2)

**Objectives:** intake schema + framework + NDA + P&P + submitter authz.
**Deliverables:**
- [ ] Extend `sprk_servicerequest` (requester/type/status/document-linkage; ADR-024 regarding preserved)
- [ ] Generic typed-intake framework (typed form → submit → status; `WizardModal`)
- [ ] NDA module (review/approval → "ready for signature"; e-sign deferred)
- [ ] Policy & Procedures module
- [ ] Self-service submitter authz (Tier-2 `requester == caller`) + app-only SPE upload (ADR-007; authz-before-stream negative test)
**Inputs**: P2 entitlement, approved P0 intake prototype.
**Outputs**: Front Door modules on the framework.

### P4: Hardening + Front Door Depth (F6, L3)

**Objectives:** reliability + verification + legal handoff.
**Deliverables:**
- [ ] Provisioner self-healing on CIAM `POST /users` 409
- [ ] Live-E2E: wrong-issuer→401; oid-bound not email-hijacked
- [ ] SSPR first-run verification + doc
- [ ] Legal-side processing handoff via existing MDA (no new review surface)
**Outputs**: hardened, verified platform.

### Wrap-up

- [ ] `090-project-wrap-up.poml`: README→Complete, lessons-learned, `/test-diet`.

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Entra App Roles + test group | Blocked | Med | Ops/portal; `FrontDoorUser` + "All Employees" |
| Teams app reg + manifest + SSO | Blocked | Med | Reuse teams-app-r1 Entra recipe |
| Azure SWA resource + deploy token | Ready | Low | R1 `deploy-external-spa.yml` |
| CIAM test user | Blocked | Low | Live-E2E + SSPR |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| teams-app-r1 FR-22 code | `Infrastructure/ExternalAccess/**` | Merged (present in worktree) |
| teams-app-r1 BFF redeploy + live Teams E2E | shared `spaarke-bff-dev` | Operator-gated |
| R1 `external-spa` frame | `src/client/external-spa/` | Production |
| Shared UI library | `src/client/shared/Spaarke.UI.Components` | Production |

---

## 6. Testing Strategy

**Unit Tests**: `tests/unit/Sprk.Bff.Api.Tests/` for new services (resolver, entitlement, intake); no `Mock<HttpMessageHandler>` / DI-registration / ctor-null tests (ADR-038).
**Integration Tests**: KEEP-path + `tests/integration/seam/ExternalAccess/**` vertical-slice-seam for dual-plane resolution + Tier-2 predicates.
**Live-E2E** (FR-19): wrong-issuer→401, oid-bound-not-email-hijacked against live CIAM + Dataverse (NOT in-process mocks).
**UI tests** (`ui-test`): launcher entitlement gating, intake flow, dark-mode + Teams-theme parity (ADR-021).
**Negative tests (required)**: unentitled module not routable; Tier-2 per module (user A can't see user B's requests); authz-before-stream on upload/download.

---

## 7. Acceptance Criteria

See [README.md Graduation Criteria](./README.md#graduation-criteria) — each maps to a spec Success
Criterion with a verification method (sign-in as differing entitlements; two-user isolation test;
Contact-creation-on-first-submission; R1 parity pass; admin grant/revoke round-trip; FR-18/19/20
tests; new-module recipe walkthrough; P0 visual approval).

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | External-access BFF collision with teams-app-r1 | Med | High | `/conflict-check` per BFF PR; `parallel-safe:false` shared files; merge-order coordination |
| R2 | teams-app-r1 redeploy/E2E not done before P1 auth | Med | Med | P1 prerequisite; sequence accordingly |
| R3 | Module-entitlement schema churn | Low | Med | Resolve in P2 with `dataverse-create-schema`; owner sign-off |
| R4 | BFF publish size creep | Low | Med | ≤60 MB ceiling; report delta each task |
| R5 | UX drift across net-new screens | Med | Med | P0 prototype + locked UX brief |
| R6 | Cleanup deletion breaks a live caller | Low | High | Confirm zero callers; sequence after generalization; tests |

---

## 9. Next Steps

1. **Review** this plan + `tasks/TASK-INDEX.md`.
2. **Owner-gate execution** wave-by-wave: start with P0 prototype, then "work on task 001" via `task-execute`.
3. **Before first BFF wave**: `dotnet build src/server/api/Sprk.Bff.Api/` (verify the merged baseline) + `/conflict-check`.

---

**Status**: Ready for Tasks (INITIALIZE-ONLY — execution owner-gated)
**Next Action**: Owner reviews TASK-INDEX; begins P0 prototype when ready.

---

*For Claude Code: load relevant phase sections when executing tasks. Coordination guardrails are binding — see CLAUDE.md.*
