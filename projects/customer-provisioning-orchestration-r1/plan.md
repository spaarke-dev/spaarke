# Project Plan: Customer Provisioning & Deployment Orchestration (r1)

> **Last Updated**: 2026-08-16
> **Status**: Ready for Tasks
> **Spec**: [spec.md](./spec.md)
> **Design**: [design.md](./design.md) v3.3

---

## 1. Executive Summary

**Purpose**: Ship one orchestrated pipeline for standing up a new Spaarke customer environment (Dataverse + Azure + SPE + BFF), replacing today's three-generation fragmented process (Gen 1 manual guide with 13 known issues; Gen 2 partial PowerShell; Gen 3 registry-only). Two deployment tiers (Model 2 dedicated + Model 1 shared trial) with unified state, tenant-isolation enforcement, and self-service Model 2 consent capture.

**Scope**:
- L1 = 19 idempotent `IProvisioningHandler` handlers (H0/H0.5/H1/H2a/b/H3–H11/H12a/b/c/H13/H14) per ADR-004 (L2-local contract, ADR-004-shaped)
- L2 = new .NET 10 App Service control plane with Cosmos DB serverless state, REST + AAD, fire-and-forget handler execution + state-reconciler
- L3 = `/provision-environment` Claude Code operator skill (Phase D)
- Data model = 12 new columns on `sprk_dataverseenvironment` + Cosmos DB `spaarke-provisioning` database
- Infrastructure = 25 Bicep modules (+ new `uami.bicep`) + `platform.bicep` rebuild + `customer.bicep` extension + `model1-shared.bicep` first-class + `platform-controlplane.bicep` new
- Governance = Phase G canonical naming + Phase H canonical secret-catalog manifest + Phase C UAMI structural migration
- Enforcement = 5 new ArchTests (§4D I1–I5 tenant isolation) + 6 handler post-condition trap-verifications (T1–T6)
- Docs = version-compatibility matrix + 6 U-CB customer-comms templates + consolidated deploy guide
- Acceptance = E2E dry run on `trial-{yyyymmdd}` Model 1 stamp

**Timeline**: ~8–10 weeks calendar (single implementer + Claude Code); ~180–220 hours estimated effort
**Estimated task count**: 60–80 tasks across 10 phases (+ mandatory 090 wrap-up)

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (per [spec.md § Applicable ADRs](./spec.md#applicable-adrs)):

- **ADR-004**: ADR-004 job-contract shape (one message, one handler, one outcome) — L1 handlers implement the L2-local `IProvisioningHandler` contract; L2 orchestration is Path A exception (documented rationale §5.1 + §5.4)
- **ADR-010**: BFF DI minimalism — provisioning handlers register in L2, NOT BFF. BFF adds strictly bounded to 3 touches
- **ADR-013**: AI facade `PublicContracts/` — H0.5 endpoint MUST NOT inject `IActionResolver`/`IActionRunner` directly
- **ADR-014**: Session isolation `tenantId + sessionId` dual-filter on `spaarke-session-files` (I2 strengthens)
- **ADR-020**: Model version pinning — 4 model deployments in H2a Bicep (gpt-4o 2024-08-06, gpt-4o-mini 2024-07-18, text-embedding-3-large v1)
- **ADR-027**: Subscription-per-customer — Model 1 shared-tier is Path A exception
- **ADR-028**: 21 MUSTs for auth ceremony — H4 KV secrets + UAMI RBAC + `keyVaultReferenceIdentity` PATCH
- **ADR-032**: Null-Object kill-switch — SignalR feature-gate follows P1/P2/P3 pattern
- **ADR-036**: Background-job infrastructure (Service Bus + `IJobHandler` + Redis idempotency)
- **ADR-038**: Integration-heavy pyramid + 7 KEEP paths + no mock of `HttpMessageHandler`
- **ADR-039**: Single AI routing surface — `playbook consumers` (H12a end state); `spaarke-playbook-embeddings` retired

**From spec.md**:
- Hot-Path Declaration: BFF=Y · SpaarkeAi=N · ci-workflows=Y (Phase H coord) · skill-directives=Y (new `/provision-environment`) · root-CLAUDE-md=N
- 37 Functional Requirements + 12 Non-Functional Requirements + 22 Success Criteria
- 2 ADR Path A exceptions (ADR-004 L2 scope + ADR-027 Model 1 scope); 5 ADR Path C comply

### Key Technical Decisions

Per [spec.md § Owner Clarifications](./spec.md#owner-clarifications) audit trail:

| Decision | Rationale | Impact |
|---|---|---|
| Custom state machine over Cosmos (not Durable Functions / Temporal) | §5.4 — single-digit runs/day cadence; custom = ~500-800 LOC one-time; workflow product = ongoing tax | L2 owns reconciler `BackgroundService`; ADR-004 Path A |
| **8 shipped solutions** (not "~10") | §11.1a — authoritative per `Deploy-DataverseSolutions.ps1 $SolutionImportOrder` | H6 imports 8: SpaarkeCore + webresources + 6 Tier-3 features |
| Custom L2 execution: fire-and-forget via Service Bus + state-reconciler | §4.2 — App Service 230s HTTP timeout vs 30-min handlers (R20) | L2 endpoints return 202 Accepted <100ms; DAG advances via 5s polling |
| Model 2 self-service consent capture in BFF | D18 — only irreducible customer-tenant admin action | New endpoint `POST /api/onboarding/consent-callback` (Anonymous + HMAC) |
| UAMI Phase C in r1 (not r2) | Owner directive 2026-08-15 + Fable H-3 correction | New `uami.bicep` + `app-service.bicep` refactor + RBAC migration |
| KV federation full scope in r1 | Owner directive #3 2026-08-15 — "not deferred; done in the context of THIS project" | Phase G (naming) + Phase H (canonical secret-catalog manifest) |
| TF Power Platform provider DEFERRED | M-10 2026-08-15 — dev-only reality + 0 pending customers | Interim `pac admin` + PPAC + Graph SDK for H5/H10 |
| Trial-env acceptance target (not demo/prod) | R18 — demo/prod decommissioned for budget | Phase F stands up fresh `trial-{yyyymmdd}` Model 1 stamp |
| Operator's own AAD identity (not SP) | §4.3a.2 — auditable operator action | Claude Code `az account get-access-token` per-run; operator has `Operator` app-role |
| 5 ArchTests for §4D tenant isolation | Q6 concern — cross-tenant bleed is CATASTROPHIC | New tests sequence into r3 forcing-functions via coord PR |

### Discovered Resources

**Applicable Skills**:
- `.claude/skills/task-execute/SKILL.md` — MANDATORY per-task orchestrator (root CLAUDE.md §4)
- `.claude/skills/code-review/SKILL.md` — Step 9.5 quality gate at FULL rigor
- `.claude/skills/adr-check/SKILL.md` — Step 9.5 ADR-compliance gate at FULL rigor
- `.claude/skills/context-handoff/SKILL.md` — checkpoint proactively every 3 steps + at 60% context
- `.claude/skills/conflict-check/SKILL.md` — REQUIRED before every BFF PR (19 active BFF worktrees)
- `.claude/skills/deploy-new-release/SKILL.md` — REFERENCE MODEL for L3 `/provision-environment` skill design (Phase D)
- `.claude/skills/bff-deploy/SKILL.md` — H9 BFF deploy handler consumes patterns
- `.claude/skills/azure-deploy/SKILL.md` — H2a Bicep deploy patterns
- `.claude/skills/dataverse-deploy/SKILL.md` — H6 solution import patterns
- `.claude/skills/dataverse-create-schema/SKILL.md` — Registry extension (12 new columns)
- `.claude/skills/dataverse-mcp-usage/SKILL.md` — Registry read/write during L3 skill

**Knowledge Articles / Constraints** (per `.claude/constraints/`):
- `.claude/constraints/bff-extensions.md` — BFF Hygiene checklist (MANDATORY for H0.5 + Phase E + GraphAppRoles.cs)
- `.claude/constraints/azure-deployment.md` — publish-size ≤60 MB (NFR-01)
- `.claude/constraints/testing.md` — 7 KEEP paths + ADR-038 compliance
- `.claude/constraints/auth.md` — H3, H4, H10 auth ceremony
- `.claude/constraints/jobs.md` — L1 handler `IProvisioningHandler` contract (ADR-004-shaped; BFF `IJobHandler` reference-only)
- `.claude/constraints/config.md` — canonical naming standard + fail-fast

**Patterns** (per `.claude/patterns/`):
- `.claude/patterns/auth/` — H3/H4/H10 auth binding + OBO + SSO patterns
- `.claude/patterns/dataverse/` — H5/H6/H7/H10/H12a-c Dataverse operations
- `.claude/patterns/testing/god-class-ratchet.md` — NFR-07 enforcement
- `.claude/patterns/api/` — H0.5 endpoint + L2 REST API endpoint patterns
- `.claude/patterns/caching/` — Redis idempotency service usage

**Reusable Code** (canonical implementations from [CLAUDE.md § Canonical implementations](./CLAUDE.md#canonical-implementations-pattern-exemplars)):
- 13 production `IJobHandler` implementations at `Services/Jobs/**/*Handler.cs` + `Services/Ai/Jobs/**/*Handler.cs`
- `IdempotencyService.cs` — 3-level idempotency
- `DemoProvisioningService.cs` (9-step) — H11 pattern
- `RegistrationDataverseService.cs` — cross-env token cache
- `GraphUserService.cs` — H11 user + UPN + license
- `infrastructure/bicep/customer.bicep` — H2a extension baseline
- `scripts/Provision-Customer.ps1` — 13-step orchestrator (basis for handler ports)
- `scripts/ai-search/Deploy-AllIndexes.ps1` — H2b 7-index catalog authority

Full discovery report: [`notes/resource-discovery-2026-08-16.md`](./notes/resource-discovery-2026-08-16.md) (generated by `/project-pipeline` Step 2 via Explore agent).

---

## 3. Implementation Approach

### Phase Structure

Per [design.md §14 Phasing](./design.md#14-phasing-v32-refreshed-2026-08-15):

```
Phase A (Doc consolidation + audits + GraphAppRoles.cs completion)      ─┐
Phase B (Gap automation script hardening + H10 grant from constant)      ├─ Parallel
Phase G (Canonical naming at provisioning + Bicep param-ization)         ─┘

    ↓ (A, B, G complete)

Phase C (Registry schema + Cosmos model + customer.bicep extension +
         NEW uami.bicep + app-service.bicep UAMI refactor +
         platform.bicep rebuild + L2 control-plane scaffold + 19 handlers)

    ↓ (C complete)

Phase C' (H12a/b/c config-seed manifest + H14 integration wiring)

Phase D (/provision-environment skill + L2 REST integration +
         Model 2 consent-capture BFF endpoint + per-tenant token metering)

Phase E (DemoExpirationService migration + Azure legacy-config deletion)  ← parallel with C/C'/D (BFF task)

Phase H (KV federation full — canonical secret-catalog manifest +
         alias collapse + IaC alignment + /config.json runtime endpoint)   ← after G, C

    ↓ (all above complete)

Phase F (E2E dry run: trial-{yyyymmdd} Model 1 stamp end-to-end)

    ↓

090 Wrap-up (mandatory: README status → Complete + lessons-learned + INDEX.md archival)
```

### Critical Path

**Blocking dependencies**:
- Phase C **BLOCKED BY** {Phase A, Phase B, Phase G} — needs GraphAppRoles.cs GUIDs complete + hardened scripts + bicep vault param
- Phase C' **BLOCKED BY** {Phase A drift resolution, Phase C} — needs seed manifest baseline + L2 scaffold
- Phase H **BLOCKED BY** {Phase G, Phase C} — needs canonical naming baseline + L2 endpoint surface
- Phase D **BLOCKED BY** {Phase C} — needs L2 REST API
- Phase F **BLOCKED BY** ALL — final acceptance
- 090 wrap-up **BLOCKED BY** Phase F acceptance

**High-Risk Items**:
- **Phase C UAMI migration** — structural refactor of App Service identity + RBAC + Graph roles + Dataverse App User. Blast radius: entire BFF startup path. Mitigation: `code-review` + `adr-check` at Step 9.5 FULL rigor; slot-swap smoke test as acceptance criteria; interim mitigation (dual-slot System-Assigned MI KV RBAC) stays in place until UAMI proven.
- **Phase H canonical secret-catalog manifest** — single-source generation must produce 4 outputs identically. Manifest generator is Opus 4.8 or Fable 5 tier per model-tier rubric. Alias collapse has BINDING pre-check (§7.9) — never delete `Dataverse-ClientSecret` or `BFF-API-ClientSecret`.
- **L2 control-plane scaffold** — 19 handlers implementing `IProvisioningHandler`, Cosmos state store, REST + AAD, state-reconciler `BackgroundService`. Blast radius: net-new .NET 10 project. Mitigation: scaffold in dedicated `src/server/services/Sprk.Provisioning.ControlPlane/**`; separate DI/Program.cs from BFF; use existing 13 production handlers as pattern exemplars.
- **`GraphAppRoles.cs` GUID completion** — 11 of 14 `AppRoleId` null. Wrong GUID → app-only Graph 403s silently → T3 fails. Mitigation: `az` enumeration of Graph resource SP; commit each GUID in a small isolated commit with `az` output cited.

---

## 4. Phase Breakdown

### Phase A — Doc Consolidation + Audits + GraphAppRoles.cs Completion

**Objectives**:
1. Consolidate 4+ deploy guides into one authoritative deploy guide (Gap 4)
2. Audit AI Search index catalog vs `Deploy-AllIndexes.ps1` (INVENTORY §12)
3. Reconcile 33 PCF folders → 7 in-use mapping (INVENTORY §12)
4. Resolve two-source AI seed drift (INVENTORY §12)
5. Complete 11 of 14 null `AppRoleId` GUIDs in `Infrastructure/Auth/GraphAppRoles.cs` via `az` enumeration
6. Publish initial version-compatibility matrix + 6 U-CB customer-comms templates
7. Audit ~28 non-deployer-listed items in `src/solutions/` (per §11.1a) → publish `notes/solutions-reconciliation-2026-08.md`
8. Cross-reference `sprk_dataverseenvironment` existing 16 columns against 12 new columns to add (Phase C prep)

**Deliverables**:
- [ ] `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` (one authoritative)
- [ ] `docs/deployment/version-compatibility-matrix.md` (initial version)
- [ ] `docs/deployment/customer-comms/` (6 U-CB templates)
- [ ] `notes/solutions-reconciliation-2026-08.md` (Phase A audit result)
- [ ] `notes/pcf-33-vs-7-mapping-2026-08.md`
- [ ] `notes/ai-seed-drift-resolution-2026-08.md`
- [ ] `Infrastructure/Auth/GraphAppRoles.cs` — all 14 `AppRoleId` GUIDs populated
- [ ] `notes/registry-column-audit-2026-08.md` (12 new columns confirmed non-existent)

**Inputs**: [design.md](./design.md), [notes/r3-handoff.md](./notes/r3-handoff.md), `docs/architecture/AI-SEARCH-INDEX-CATALOG.md`, `scripts/ai-search/Deploy-AllIndexes.ps1`, existing 4+ deploy guides

**Outputs**: See Deliverables

**Rigor**: MINIMAL for docs; STANDARD for GraphAppRoles.cs (BFF touch — auto-triggers §10 checklist)

---

### Phase B — Gap Automation Script Hardening

**Objectives**:
1. Harden `scripts/Register-EntraAppRegistrations.ps1` — full 14-grant idempotency (Gap 3 / R4)
2. Harden `scripts/Create-NewContainerType.ps1` + related to use confidential-client cert (T6 / R10)
3. Extend `scripts/Deploy-DataverseSolutions.ps1` to Package Deployer for dependency-ordered import
4. Harden `scripts/Deploy-Release.ps1` Phase 4 — `customerId`-driven, no `spaarkedev1` hardcode (Gap 2 / R16)
5. Add Cosmos DB provisioning to `customer.bicep` prep (R11)
6. Author H10 Graph app-role grant handler that reads from `GraphAppRoles.cs` constant + syncs UAMI SP (r3 task 062 landed)

**Deliverables**:
- [ ] `scripts/Register-EntraAppRegistrations.ps1` — 14 grants idempotent (v3.3 mandatory `-TenantId` already landed in `1834b77bc`)
- [ ] `scripts/Create-NewContainerType.ps1` + `Register-*.ps1` + `New-BusinessUnitContainer.ps1` — confidential-client refactor
- [ ] `scripts/Deploy-DataverseSolutions.ps1` — Package Deployer invocation
- [ ] `scripts/Deploy-Release.ps1` Phase 4 — customerId-driven
- [ ] `scripts/Grant-GraphAppRoles.ps1` (or C# helper) — reads `GraphAppRoles.cs` + `az ad app permission grant`

**Inputs**: Existing scripts, `Infrastructure/Auth/GraphAppRoles.cs`, `notes/r3-handoff.md`, design.md §4.1 handler catalog

**Outputs**: See Deliverables

**Rigor**: STANDARD (script hardening, no BFF touch)

---

### Phase G — Canonical Naming Compliance at Provisioning

**Objectives**:
1. Apply canonical KV secret names at all provisioning entry points (§7.9 R1–R4 + FR-35)
2. Convert `infrastructure/bicep/**` vault name to Bicep parameter (not hardcoded)
3. Codify `spaarke-spekvcert` DO-NOT-RENAME dev exception in bicep + config
4. Add invocation of `scripts/naming-conformance-check.ps1` to H13 acceptance gate design
5. Owner directive #3: bake into new-customer provisioning; skip live-dev remediation

**Deliverables**:
- [ ] `infrastructure/bicep/customer.bicep` + `platform.bicep` — vault name as parameter
- [ ] `infrastructure/bicep/modules/key-vault.bicep` — vault name as parameter
- [ ] Existing seeder scripts — canonical KV secret names applied
- [ ] Design update: H13 invokes `naming-conformance-check.ps1`, requires exit 0
- [ ] Documentation of `spaarke-spekvcert` exception in bicep params + `notes/naming-exception-registry.md`

**Inputs**: `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md`, `scripts/naming-conformance-check.ps1`, r3 task 063 handoff, spec.md §7.9

**Outputs**: See Deliverables

**Rigor**: STANDARD (Bicep + PS)

---

### Phase C — Registry + Cosmos + Bicep + UAMI + L2 Control Plane

**Objectives** (BIG phase — split into sub-waves):
1. Extend `sprk_dataverseenvironment` with 12 new columns (per FR-26)
2. Model Cosmos DB `spaarke-provisioning` database + `runs` container schema (per FR-27)
3. Extend `customer.bicep` — add Cosmos DB + optional SignalR; remove Redis
4. NEW `uami.bicep` module — per-customer User-Assigned Managed Identity
5. Refactor `app-service.bicep` to consume UAMI + bind both slots (structural T5 fix per Phase C UAMI)
6. Migrate RBAC (KV Secrets User, Storage Blob Data Contributor, Cognitive Services User, Cosmos DB Data Contributor) from System-Assigned MI → UAMI principal
7. Migrate Graph app-role grants + Dataverse App User registration to UAMI app ID
8. Rebuild `platform.bicep` — shrink to control-plane-only (App Service + Cosmos + platform KV + monitoring)
9. NEW `model1-shared.bicep` stack — first-class trial-tier composition
10. NEW `platform-controlplane.bicep` — L2 orchestrator infra
11. Scaffold `src/server/services/Sprk.Provisioning.ControlPlane/**` .NET 10 project
12. Implement 19 `IProvisioningHandler` handlers (H0/H0.5/H1/H2a/b/H3–H11/H12a/b/c/H13/H14) with 3-level idempotency
13. Implement L2 REST endpoints per §4.2 API surface + AAD bearer + `Operator`/`Reader` app-roles
14. Implement state-reconciler `BackgroundService` (5s Cosmos polling + optimistic-concurrency guard)

**Deliverables** (organized as sub-waves):

**Wave C1 (schema + Cosmos)**:
- [ ] Registry: 12 new columns on `sprk_dataverseenvironment` (`sprk_currentrunid`, `sprk_tenancymodel`, `sprk_tenantid`, `sprk_bffversion`, `sprk_solutionversion`, `sprk_ClientCacheBustToken` + 6 v2 additions incl. `sprk_provisionedon`)
- [ ] Cosmos schema: `spaarke-provisioning/runs` container definition (partition `/customerId`, TTL 365d, KV URI ref pattern)

**Wave C2 (Bicep)**:
- [ ] `infrastructure/bicep/customer.bicep` — Cosmos + optional SignalR added; Redis removed; UAMI param
- [ ] NEW `infrastructure/bicep/modules/uami.bicep`
- [ ] `infrastructure/bicep/modules/app-service.bicep` — UAMI consumption + both slots
- [ ] RBAC bicep updates — UAMI principal
- [ ] `infrastructure/bicep/platform.bicep` — rebuild to control-plane-only
- [ ] NEW `infrastructure/bicep/stacks/model1-shared.bicep`
- [ ] NEW `infrastructure/bicep/platform-controlplane.bicep`

**Wave C3 (L2 scaffold)**:
- [ ] NEW project `src/server/services/Sprk.Provisioning.ControlPlane/**`
  - Program.cs + Startup + DI + JWT bearer + `Operator`/`Reader` app-role auth
  - Cosmos client wiring (partition `/customerId`)
  - Service Bus client wiring for handler enqueue
  - App Insights + Log Analytics wiring

**Wave C4 (handlers, parallel-safe where deps allow)**:
- [ ] H0 preflight handler (quota checks: OpenAI TPM, Dataverse env-creation rate, subscription vCPU, SPE cert-bootstrap)
- [ ] H0.5 consent-capture handler + BFF endpoint (`POST /api/onboarding/consent-callback`; Anonymous + HMAC; re-consent semantics)
- [ ] H1 subscription readiness (ARM verification + Lighthouse if CustomerOwned)
- [ ] H2a Bicep infra deploy handler
- [ ] H2b AI Search index provisioning handler (invokes `scripts/ai-search/Deploy-AllIndexes.ps1`)
- [ ] H3 Entra app-reg (BFF API only; 14 grants; hardened script from Phase B)
- [ ] H4 KV secrets population (Phase H canonical manifest consumption; `keyVaultReferenceIdentity` PATCH; both-slot MI grants interim + UAMI post-C2)
- [ ] H5 Dataverse env creation (interim `pac admin` + gate)
- [ ] H6 solution import via Package Deployer (8 solutions, dependency-ordered)
- [ ] H7 Dataverse env-var values (7 canonical per §10.3)
- [ ] H8 SPE container-type + root container (confidential-client per T6, hardened from Phase B)
- [ ] H9 BFF deploy handler
- [ ] H10 Dataverse Application User + Graph app-role parity from `GraphAppRoles.cs` constant
- [ ] H11 user provisioning (identity preset per D6)
- [ ] H12a AI seed chain (playbook consumers per ADR-039)
- [ ] H12b app-config seed (DAG-parallel with H12a)
- [ ] H12c runtime references (`sprk_aimodeldeployment`)
- [ ] H13 E2E acceptance gate (extended `Validate-DeployedEnvironment.ps1` invocation; all 6 traps + all 5 invariants + cost envelope + naming-conformance)
- [ ] H14 post-deploy integrations (2 Exchange policies + Graph webhooks + service-endpoint webhooks; DAG-parallel sub-steps)

**Wave C5 (L2 REST + reconciler)**:
- [ ] L2 REST endpoints per §4.2: `POST /api/runs`, `POST /api/runs/{id}/preflight`, `GET /api/runs/{id}`, `POST /api/runs/{id}/gates/{gateId}/advance`, `POST /api/runs/{id}/resume`, `GET /api/runs/{id}/phases/{phaseId}/logs`, `POST /api/runs/{id}/cancel`, `POST /api/runs/{id}/clear-quarantine` (v3.2 §4C)
- [ ] State-reconciler `BackgroundService` — 5s Cosmos polling, DAG advancement, optimistic-concurrency guard, Service Bus MessageId dedup + Redis idempotency lock
- [ ] I5 concurrency guard: same-customer serialization via `sprk_currentrunid` optimistic upsert (409 on conflict with winning run ID)
- [ ] I6 crash recovery: scan Cosmos for orphaned `Running`/`WaitingOnGate` older than 2× median-handler-duration on startup + re-schedule from `currentPhase`
- [ ] §4C rollback semantics: 4-class failure taxonomy + Cosmos `Quarantined` state + audit-log on clear-quarantine

**Wave C6 (tenant-isolation ArchTests + Register-EntraAppRegistrations.ps1 code fix consumption)**:
- [ ] 5 new ArchTests in `tests/Spaarke.ArchTests/**` per §4D I1–I5 (already scoped in spec.md FR-28..FR-32)
- [ ] Phase A audit sweep of BFF service inventory (beyond 2 Fable-spot-checked services) for I2/I3/I4/I5 compliance
- [ ] Test-modifying tasks trigger unconditional code-review + adr-check at Step 9.5 (per root §8 override)

**Inputs**: 13 production `IJobHandler` implementations (canonical pattern), `Infrastructure/Auth/GraphAppRoles.cs`, existing Bicep modules, spec.md, design.md §4.1/§4.2/§4C/§4D, PSScripts to be hardened by Phase B

**Outputs**: See Deliverables

**Rigor**: FULL for all BFF touches (H0.5 endpoint, GraphAppRoles.cs, DemoExpirationService — but Phase E owns that), all L2 handler tasks (bff-api tag), all Phase C UAMI tasks (auth tag), all test-modifying tasks (unconditional override)

**Model tier**: Opus 4.8 / Fable 5 for L2 scaffold + UAMI migration + reconciler. Sonnet 5 @ high for individual handler implementations. Sonnet 5 @ xhigh for ArchTest authoring (must reason through every BFF service call site).

---

### Phase C' — Config-Seed Manifest + Integration Wiring

**Objectives**:
1. Author declarative seed manifest resolving R14 two-source AI seed drift (per FR-15)
2. Implement H12a (AI seed chain) + H12b (app-config seed, DAG-parallel) + H12c (runtime references)
3. Implement H14 integration wiring — 2 Exchange policies (T4 action-and-verify) + Graph webhooks + service-endpoint webhooks; DAG-parallel sub-steps

**Deliverables**:
- [ ] `scripts/seed-data/manifest.yaml` — authoritative source per artifact declaration (resolves scripts/seed-data MVP vs infra/dataverse R7 drift)
- [ ] H12a implementation (invokes `Deploy-All-AI-SeedData.ps1` + `Seed-PlaybookConsumers.ps1`)
- [ ] H12b implementation (DataGrid + field-mapping + workspace-layout + chart-def seeders)
- [ ] H12c implementation (`sprk_aimodeldeployment` runtime refs)
- [ ] H14 implementation (Exchange + Graph webhook + service-endpoint webhook sub-handlers)

**Inputs**: Phase A drift resolution + Wave C4 handler scaffolds

**Outputs**: See Deliverables

**Rigor**: FULL (config-seed layer + BFF integration touches)

---

### Phase D — L3 Skill + BFF Integration Endpoints + Metering

**Objectives**:
1. Author `/provision-environment` Claude Code operator skill at `.claude/skills/provision-environment/SKILL.md` per §4.3a
2. Implement 15-tool matrix + prereqs check (Step 0) + fallback matrix for MCP disconnects
3. BFF endpoint `POST /api/onboarding/consent-callback` (H0.5 endpoint on BFF side)
4. Per-tenant token-metering layer (D19) — APIM gateway OR app-level custom App Insights metric keyed on `tenantId`

**Deliverables**:
- [ ] NEW `.claude/skills/provision-environment/SKILL.md` (main-session-only; sub-agent boundary applies)
- [ ] BFF endpoint at `src/server/api/Sprk.Bff.Api/Api/Onboarding/**`
- [ ] Token-metering layer implementation (Phase A decides APIM vs custom metric)

**Inputs**: `.claude/skills/deploy-new-release/SKILL.md` (reference model), spec.md §4.3a, L2 REST API from Phase C

**Outputs**: See Deliverables

**Rigor**: FULL for BFF endpoint + token metering; MINIMAL for skill authoring (docs) but Sub-Agent Write Boundary applies (main-session-only)

---

### Phase E — DemoExpirationService Migration

**Objectives**:
1. Migrate `DemoExpirationService.cs` off `[Obsolete]` `DemoProvisioningOptions.Environments` + `DefaultEnvironment` → `DataverseEnvironmentService` (R5 carry-over)
2. Delete `DemoProvisioning__Environments__*` + `__DefaultEnvironment` from Azure App Service configuration
3. Verify expiration flow working post-migration
4. Verify BFF publish size delta < ~0.5 MB combined with Phase C UAMI + Phase D H0.5 endpoint (NFR-01)

**Deliverables**:
- [ ] `src/server/api/Sprk.Bff.Api/Services/Registration/DemoExpirationService.cs` — refactored to use `DataverseEnvironmentService`
- [ ] `src/server/api/Sprk.Bff.Api/Api/RegistrationEndpoints.cs` — remove 4 `[Obsolete]` warnings (lines 466/468/469)
- [ ] Azure App Service config: remove `DemoProvisioning__Environments__*` + `__DefaultEnvironment` keys post-verification
- [ ] Verification: BFF publish size delta report + before/after warning count

**Inputs**: R5 carry-over, existing DemoExpirationService.cs (frozen file per NFR-07 god-class ratchet — respect +100 LOC grace), `DataverseEnvironmentService`

**Outputs**: See Deliverables

**Rigor**: FULL (BFF touch + existing frozen file modification)

---

### Phase H — KV Federation Full Remediation

**Objectives** (per owner directive #3 + r3 task 017 handoff §4b):
1. Author canonical secret-catalog manifest (r3 Phase 3b) — single generated source for seeder + Configure script + tokens doc + Bicep KV secret set
2. Alias collapse for AI Search key etc. — with BINDING pre-check (LIVE App Service + KV + Dataverse) FIRST (§7.9)
3. IaC alignment — bicep secret names + app-setting keys canonical
4. External-spa + code-pages runtime `/config.json` fetch (closes bake-at-build-time pattern)
5. Coordinate `.github/workflows/**` gate wiring with `ci-cd-unit-test-remediation-r1` (task-042-063-ci-gate-wiring-deferral.md)

**Deliverables**:
- [ ] NEW `scripts/canonical-secret-catalog/manifest.yaml` + generator script
- [ ] Generated: seeder PS + Configure script + tokens.md + Bicep KV secret set
- [ ] Alias collapse for AI-Search key: pre-check evidence + collapse + verification (in task notes per §7.9)
- [ ] BFF app-settings + templates: canonical keys applied
- [ ] External-spa + code-pages: `/config.json` runtime endpoint (BFF exposes; consumers fetch at bootstrap)
- [ ] Coordinated PR spec for `.github/workflows/**` wiring (opened after this Phase's tasks complete; `/conflict-check` before merge)

**Inputs**: `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md`, r3 task 017 KV federation assessment output (in this worktree post-master-merge), spec.md §7.9 + FR-36

**Outputs**: See Deliverables

**Rigor**: FULL (multi-surface: BFF + external-spa + code-pages + IaC + skills coordination)

**Model tier**: Opus 4.8 / Fable 5 for manifest generator (high-blast-radius); Sonnet 5 @ high for consumer updates

---

### Phase F — E2E Acceptance (Trial Stamp)

**Objectives** (per R18 + H-6 decision + FR-18 acceptance criteria):
1. Provision fresh `trial-{yyyymmdd}` customer stamp using Model 1 profile via new pipeline
2. Reach `Setup Status = Ready` end-to-end
3. Verify all 6 §4B silent-fail traps (T1–T6) cleared by owning handler post-conditions
4. Verify all 5 §4D tenant-isolation invariants (I1–I5) sample-verified
5. Verify `scripts/naming-conformance-check.ps1` exits 0 on r1-owned surfaces
6. Verify cost envelope conforms per pricing model (§15 #14)
7. Verify Model 1 vs Model 2 handler behavior differences per §4.1a table (if reasonable to dry-run both tiers)

**Deliverables**:
- [ ] Provisioning run: fresh `trial-{yyyymmdd}` stamp reaches `Setup Status = Ready`
- [ ] Verification report at `notes/phase-f-e2e-acceptance-2026-XX-XX.md` with all trap + invariant + naming + cost checks
- [ ] Model 1/2 differences verified per §4.1a (or explicit note if only Model 1 dry-runnable)
- [ ] `/health` succeeds; sample analysis + sample doc upload+index + workspace-layout render + wizard field-map all succeed
- [ ] Cleanup: discretionary teardown OR leave trial for reference

**Inputs**: All Phase A/B/C/C'/D/E/G/H deliverables

**Outputs**: See Deliverables

**Rigor**: FULL (deployment gate + acceptance)

---

### 090 — Wrap-up (Mandatory per task-create Step 3.7)

**Objectives**:
1. Update README.md status → Complete + set Completed Date
2. Write `notes/lessons-learned.md` with r1-specific learnings (Model 1/2 tier decisions, canonical naming remediation experience, UAMI migration pain points, `GraphAppRoles.cs` GUID enumeration story, tenant-isolation invariant enforcement)
3. Archive project artifacts per `.claude/skills/repo-cleanup/SKILL.md`
4. Update `projects/INDEX.md` row: mark as Complete OR move to `## Archived` section (per `devops-project-archive` skill)
5. Run `/test-diet` per root CLAUDE.md §7 project-close gate (BINDING — 5 new ArchTests + integration tests all reconciled against 17-ban classifier per ADR-038 §7)

**Deliverables**:
- [ ] README.md status → Complete
- [ ] `notes/lessons-learned.md`
- [ ] INDEX.md row updated
- [ ] `/test-diet` report at `notes/test-diet-report.md`
- [ ] Wrap-up PR description cites test-diet report OR documents skip rationale

**Inputs**: All completed phases

**Outputs**: See Deliverables

**Rigor**: STANDARD

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|---|---|---|---|
| Azure OpenAI regional TPM quota (150+200+30+350) | Ready dev; per-customer verified in H0 | Medium (quota bump 1–3 day lead) | H0 preflight; front-load quota-bump request |
| Azure subscription quota per SKU per region | Ready dev | Low | H0 preflight |
| Dataverse env-creation rate (~4/hour) | Ready | Low | H0 `pac admin quota` check |
| SPE container-type replication (up to 24h) | Lead-time item | High (first customer only) | H0 preflight checks cert-bootstrap done; customer prereq checklist |
| Customer admin consent (Model 2) | Customer-dependent | High | H0.5 self-service consent-capture endpoint |
| Exchange RBAC-for-Apps migration | Coexistence safe (Aug 2026) | Low | Phase D backlog for r2 (R22) |
| `ci-cd-unit-test-remediation-r1` for Phase H CI wiring | Coordinated PR pending | Medium | Coordinate before Phase H PR |

### Internal Dependencies

| Dependency | Location | Status |
|---|---|---|
| r3 tasks 060/061/062/017 | Master (via merge `be3f12bb6`) | Complete ✅ |
| .NET 10 baseline + Graph v6.5.0 / Kiota 2.0 | Master | Ready ✅ |
| 13 production `IJobHandler` implementations | `src/server/api/Sprk.Bff.Api/Services/Jobs/**` + `Services/Ai/Jobs/**` | Production |
| `IdempotencyService` + Redis | `src/server/api/Sprk.Bff.Api/Services/Jobs/` | Production |
| Existing 25 Bicep modules | `infrastructure/bicep/modules/` | Production |
| `scripts/Provision-Customer.ps1` + `Deploy-*Solutions.ps1` + `Register-*` + `Create-New*` | `scripts/` | Production (basis for ports) |
| `Infrastructure/Auth/GraphAppRoles.cs` (r3 task 062) | `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/` | Landed with 11 of 14 GUIDs null (Phase A completion obligation) |
| `scripts/naming-conformance-check.ps1` (r3 task 063) | `scripts/` | Landed ✅ |

---

## 6. Testing Strategy

Per [design.md §15](./design.md#15-success-criteria-v3-refreshed-2026-08-12) + [spec.md § NFR-08](./spec.md#non-functional-requirements) + ADR-038 (integration-heavy pyramid).

**Unit Tests** (existing pyramid + additions):
- Handler-level unit tests for each of 19 handlers (idempotency-key generation, gate-verification logic, Cosmos writes)
- L2 REST endpoint tests (JWT bearer validation, app-role authorization, 409 on concurrent same-customer)
- State-reconciler `BackgroundService` tests (DAG advancement, orphaned-run recovery)

**Integration Tests** (critical paths — sequenced into r3 KEEP paths per ADR-038 §7):
- H0 preflight → H1 subscription readiness → H2a Bicep deploy (isolated Azure subscription)
- H3 Entra app-reg (with `az` CLI-invoked verification)
- H6 solution import (against dev Dataverse)
- L2 REST full run lifecycle: `POST /api/runs` → progress → `Setup Status = Ready`
- Fire-and-forget execution model verified under load (≥30-min handler completes without HTTP timeout — NFR-03/FR-22 acceptance)
- Same-customer 409 concurrency test
- Crash-then-restart resume test

**ArchTests** (net-new, sequence into r3 forcing-functions via coord PR):
- I1: no hardcoded tenant-shaped GUID default in provisioning scripts
- I2: unconditional `tenantId eq` filter on all AI Search queries
- I3: partition-key predicate on all Cosmos SDK operations
- I4: SPE container ID literal detection in BFF services (any `b!...` = fail)
- I5: `GraphClientFactory` token acquisition without explicit `tenantId` = fail

**E2E Tests**:
- Phase F acceptance: fresh `trial-{yyyymmdd}` stamp end-to-end
- Model 1 vs Model 2 both dry-run (if reasonable per H-6)
- Slot-swap smoke test post-Phase C UAMI (no cold-start KV-ref failures — T5 structurally impossible)

**Coverage** (per ADR-038 — observation not gate; binding ≥6 months from 2026-06-26):
- Not a gate; not a target. Tests exist to catch regressions in the KEEP paths above.

---

## 7. Acceptance Criteria

Full list at [spec.md § Success Criteria (22 items)](./spec.md#success-criteria).

### Technical Acceptance (per phase)

**Phase A**:
- [ ] All 14 `AppRoleId` GUIDs in `GraphAppRoles.cs` populated + `az` enumeration output cited per GUID
- [ ] One authoritative deploy guide replaces 4+ old guides + explicit stubs added
- [ ] Solutions reconciliation audit `notes/solutions-reconciliation-2026-08.md` published
- [ ] Version-compatibility matrix v1 published at `docs/deployment/version-compatibility-matrix.md`

**Phase B**:
- [ ] `Register-EntraAppRegistrations.ps1` idempotent for 14 grants
- [ ] SPE scripts use confidential-client cert (T6 fixed)
- [ ] `Deploy-Release.ps1` Phase 4 customerId-driven (no `spaarkedev1`)

**Phase G**:
- [ ] `scripts/naming-conformance-check.ps1` exits 0 on Bicep vault-name parameterization
- [ ] `spaarke-spekvcert` DO-NOT-RENAME dev exception codified in Bicep + config

**Phase C** (biggest — sub-wave acceptance):
- C1: 12 new columns on `sprk_dataverseenvironment`; Cosmos schema defined
- C2: `uami.bicep` module exists; `app-service.bicep` binds UAMI both slots; T5 slot-swap smoke test produces no 503s
- C3: L2 project scaffolds + builds + all `IProvisioningHandler` handlers register in DI
- C4: All 19 handlers idempotent (integration test: second run = no-op)
- C5: L2 REST endpoints per §4.2 spec; 30-min handler completes without HTTP timeout under load test
- C6: 5 new ArchTests I1–I5 pass in `dotnet test tests/Spaarke.ArchTests/`

**Phase C'**:
- H12a/b/c seed manifest resolves R14 drift; runs additively in upgrade mode
- H14 sub-steps all DAG-parallel; T4 action-and-verify semantics

**Phase D**:
- `/provision-environment` skill runs full dry-run against L2; fallback matrix triggers on MCP disconnect
- BFF `POST /api/onboarding/consent-callback` accepts consent + HMAC verifies + kicks pipeline
- Per-tenant token metering blocks over-budget attempts (429 to caller)

**Phase E**:
- `DemoProvisioning__Environments__*` + `__DefaultEnvironment` keys deleted from Azure config; no runtime break
- 4 `[Obsolete]` warnings on `RegistrationEndpoints.cs` + `DemoExpirationService.cs` resolved
- BFF publish size delta report shows < ~0.5 MB combined with Phase C/D

**Phase H**:
- Canonical secret-catalog manifest generates 4 outputs identically
- Alias collapse for AI Search key with pre-check evidence in task notes
- External-spa + code-pages fetch `/config.json` at bootstrap (not bake-at-build)
- Coord PR spec ready for `ci-cd-unit-test-remediation-r1`

**Phase F**:
- Fresh `trial-{yyyymmdd}` stamp reaches `Setup Status = Ready` via new pipeline
- All 6 §4B T1–T6 traps verified cleared
- All 5 §4D I1–I5 invariants sample-verified
- Cost envelope per pricing model (§15 #14) confirmed
- `naming-conformance-check.ps1` exits 0 on r1-owned surfaces

### Business Acceptance

- [ ] Provisioning cadence: single-customer full runtime ≤ 1 hour (north star; lead-time items surfaced up-front by H0)
- [ ] Model 2 self-service: customer admin consent grant kicks pipeline without operator intervention
- [ ] Fleet visibility: `sprk_currentrunid` visible on registry; Cosmos queryable for aggregate status

---

## 8. Risk Register

Per [design.md §12](./design.md#12-risk-register-v32-refreshed-2026-08-15) R1–R23. Key risks bearing on plan execution:

| ID | Risk | Probability | Impact | Mitigation |
|---|---|---|---|---|
| R20 | Handler HTTP timeout under App Service 230s | Certain if synchronous | HIGH | §4.2 fire-and-forget execution model (Phase C Wave C5) |
| R21 | UAMI migration debt (T5 persists) | High until Phase C | HIGH | Phase C early priority; interim dual-slot System-Assigned MI grants |
| R11 | Cosmos absent from orchestrator | Low after Wave C2 | HIGH | H2a Bicep includes Cosmos (Phase B → Wave C2 handoff) |
| R17 | KV-secret naming drift persists at new customers | High without Phase G | HIGH | Phase G bakes canonical at provisioning; Phase H closes durable via manifest |
| Cross-tenant bleed | Would-be silent | CATASTROPHIC | Wave C6 5 new ArchTests + Phase A audit sweep + H13 sample verification |
| `GraphAppRoles.cs` GUID wrong from `az` enumeration | Medium if rushed | HIGH (T3 silent 403) | Phase A per-GUID isolated commit with `az` output cited |
| Phase H alias-collapse breaks live consumer | Would-be silent + prod-visible | HIGH | BINDING pre-check per §7.9 — LIVE App Service + KV + Dataverse before removing any alias |
| Phase C sub-wave dep miss (C4 handler needs C3 scaffold) | Medium (many parallel handlers) | LOW-MEDIUM | Wave sub-numbering + task-create Step 3.8 parallel-group discipline; `/conflict-check` per BFF PR |
| Model 2 customer commitment blocks TF migration path | Business-dependent | LOW (M-10 accepted deferral) | spec.md `Unresolved Questions` tracks; not r1 acceptance blocker |

---

## 9. Next Steps

1. **Review this plan.md** with owner — confirm phase sequencing + wave breakdown
2. **Run `/task-create customer-provisioning-orchestration-r1`** (invoked by `/project-pipeline` Step 3) to generate task POMLs
3. **Owner reviews TASK-INDEX.md** — task count, dependencies, parallel groups
4. **Begin Phase A** — invoke `task-execute` on task 001

---

**Status**: Ready for Task Generation
**Next Action**: `/project-pipeline` Step 3 generates task POMLs + TASK-INDEX.md

---

*For Claude Code: This plan drives task decomposition. Each phase deliverable → 1–5 task POMLs. Reference the corresponding spec.md FR when authoring each task's `<goal>`.*
