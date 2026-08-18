# TASK-INDEX — customer-provisioning-orchestration-r1

> **Last Updated**: 2026-08-16 (by `/project-pipeline` Step 3)
> **Status**: Ready for `task-execute` (Phase A first)
> **Task count**: 78 POMLs (across 10 phases)
> **Spec**: [`../spec.md`](../spec.md) · **Plan**: [`../plan.md`](../plan.md) · **Design**: [`../design.md`](../design.md) v3.3
> **Legend**: 🔲 not-started · 🟡 in-progress · ✅ completed · 🔄 needs-retry · ⏸ blocked · ⏭️ deferred

---

## Quick Recovery

To begin: **`task-execute 001`** (or `/project-continue`). First task is `001-consolidate-deploy-guides` (Phase A, MINIMAL, sonnet/medium, groupA-parallel — no dependencies).

## Applied corrections (2026-08-16 post-generation)

Two discovery-report findings applied to task POMLs before initial task-execute:
- **Task 005** — `GraphAppRoles.cs` actual null count is **11 of 14** (not 10 as spec/design/CLAUDE.md claim). Task 005 updated to 11 GUIDs + doc-reconciliation commit. Task-execute produces the reconciliation PR to fix spec.md + design.md + CLAUDE.md.
- **Task 023** — Registry columns actual count is **12** (spec.md FR-26 says "11 new columns" but enumerates 12 items — missing `sprk_provisionedon`). Task 023 updated to 12 columns + doc-reconciliation commit.

Two additional corrections independently caught by task-gen subagents:
- **Task 032** (`model1-shared.bicep`) — file already exists (309 LOC) per discovery report §6. C2 subagent independently recognized this and reframed the task as review/refactor-if-scaffold-exists with existing-file cited in `<justification><existing>`.
- **Missing coord artifact** — `projects/ci-cd-unit-test-remediation-r1/notes/task-042-063-ci-gate-wiring-deferral.md` referenced by spec.md/plan.md but does not exist. Task 088 (Phase H CI-workflows coord PR) is scoped to reconstitute this artifact OR coordinate via `ci-cd-unit-test-remediation-r1/CLAUDE.md` alternate docs.

---

## Task Registry (78 total)

### Phase A — Doc Consolidation + Audits + `GraphAppRoles.cs` Completion (8 tasks)

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 001 | ✅ | Consolidate 4+ deploy guides into one authoritative `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` | MINIMAL | sonnet / medium | groupA-parallel | none |
| 002 | ✅ | Audit AI Search index catalog vs `Deploy-AllIndexes.ps1` | MINIMAL | sonnet / medium | groupA-parallel | none |
| 003 | ✅ | Reconcile 33 PCF folders → 7 in-use mapping | MINIMAL | sonnet / medium | groupA-parallel | none |
| 004 | ✅ | Resolve two-source AI seed drift | STANDARD | sonnet / medium | groupA-parallel | none |
| 005 | ✅ | **Complete 11 of 14 null `AppRoleId` GUIDs** in `GraphAppRoles.cs` via `az` enum (H10 escalation gate) — CORRECTED from "10 of 14" per discovery §12 | FULL | sonnet / xhigh | none (serial per-GUID commit) | none |
| 006 | ✅ | Publish `docs/deployment/version-compatibility-matrix.md` (initial) | MINIMAL | sonnet / medium | groupA-parallel | none |
| 007 | ✅ | Author 6 U-CB customer-comms templates in `docs/deployment/customer-comms/` | MINIMAL | sonnet / medium | groupA-parallel | none |
| 008 | ✅ | Audit ~28 non-deployer-listed items in `src/solutions/` (§11.1a reconciliation) | MINIMAL | sonnet / medium | groupA-parallel | none |

### Phase B — Gap Automation Script Hardening (7 tasks)

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 010 | ✅ | Harden `Register-EntraAppRegistrations.ps1` for 14-grant idempotency | STANDARD | sonnet / high | groupB-parallel | none |
| 011 | ✅ | Refactor SPE scripts to confidential-client (T6 fix) | STANDARD | sonnet / high | groupB-parallel | none |
| 012 | ✅ | Extend `Deploy-DataverseSolutions.ps1` to Package Deployer (8 solutions) | STANDARD | sonnet / high | groupB-parallel | none |
| 013 | ✅ | Harden `Deploy-Release.ps1` Phase 4 (`customerId`-driven; remove `spaarkedev1`) | STANDARD | sonnet / high | groupB-parallel | none |
| 014 | ✅ | Add Cosmos DB provisioning module invocation to `customer.bicep` prep | STANDARD | sonnet / high | groupB-parallel | none |
| 015 | ✅ | Author `Grant-GraphAppRoles.ps1` reading `GraphAppRoles.cs` constant | STANDARD | sonnet / high | none (dep 005) | 005 |
| 016 | ✅ | Add H0 preflight quota-check PS modules | STANDARD | sonnet / high | groupB-parallel | none |

### Phase G — Canonical Naming Compliance at Provisioning (4 tasks)

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 018 | ✅ | Parameterize Bicep vault name in `customer.bicep` + `platform.bicep` + `key-vault.bicep` | STANDARD | sonnet / high | groupG-parallel | none |
| 019 | ✅ | Update seeder scripts to canonical KV secret names | STANDARD | sonnet / high | groupG-parallel | none |
| 020 | ✅ | Codify `spaarke-spekvcert` DO-NOT-RENAME dev exception | STANDARD | sonnet / medium | groupG-parallel | none |
| 021 | ✅ | Wire `naming-conformance-check.ps1` into `Validate-DeployedEnvironment.ps1` design | STANDARD | sonnet / medium | none (dep 018-020) | 018, 019, 020 |

### Phase C Wave C1 — Registry + Cosmos Model (3 tasks)

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 023 | ✅ | **Extend `sprk_dataverseenvironment` with 12 new columns** — CORRECTED from "11" per discovery §9 | FULL | sonnet / high | waveC1-parallel | none |
| 024 | ✅ | Author Cosmos DB `spaarke-provisioning`/`runs` schema + BFF-side POCO models | FULL | sonnet / high | waveC1-parallel | none |
| 025 | ✅ | ArchTest guarding no cleartext secret in Cosmos `parameters` | FULL | sonnet / high | none (dep 024) | 024 |

### Phase C Wave C2 — Bicep + UAMI (8 tasks)

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 027 | ✅ | Extend `customer.bicep` — add Cosmos + optional SignalR; remove Redis; UAMI param | FULL | sonnet / high | waveC2-parallel | none |
| 028 | ✅ | Author NEW `infrastructure/bicep/modules/uami.bicep` (Phase C UAMI migration) | FULL | opus / high | waveC2-parallel | none |
| 029 | ✅ | Refactor `app-service.bicep` to consume UAMI (bind both slots — structural T5 fix) | FULL | opus / high | none (dep 028) | 028 |
| 030 | ✅ | Migrate RBAC (KV Secrets User, Storage, Cognitive Services, Cosmos DB) to UAMI principal | FULL | opus / high | none (dep 028, 029) | 028, 029 |
| 031 | ✅ | Rebuild `platform.bicep` to control-plane-only (D12) | FULL | opus / high | none (dep 027) | 027 |
| 032 | ✅ | Review/verify `infrastructure/bicep/stacks/model1-shared.bicep` (EXISTS already per discovery §6 — task reframed from NEW to review) | FULL | opus / high | waveC2-parallel | none |
| 033 | ✅ | Author NEW `infrastructure/bicep/platform-controlplane.bicep` — L2 orchestrator infra | FULL | opus / high | waveC2-parallel | none |
| 034 | ✅ | Integration test: end-to-end Bicep deploy dry-run (dev subscription) | FULL | sonnet / high | none (dep 027-033; test-modifying → unconditional FULL) | 027, 028, 029, 030, 031, 032, 033 |

### Phase C Wave C3 — L2 Control-Plane Scaffold (4 tasks)

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 036 | ✅ | Scaffold NEW .NET 10 project `src/server/services/Sprk.Provisioning.ControlPlane/**` | FULL | opus / high | waveC3-scaffold | none |
| 037 | ✅ | Wire Cosmos client for L2 (partition `/customerId`; §4D I3 partition-key ArchTest coverage) | FULL | opus / high | none (dep 024, 036 — shared Program.cs) | 024, 036 |
| 038 | ✅ | Wire Service Bus client for L2 (handler enqueue path per §4.2) | FULL | opus / high | none (dep 036 — shared Program.cs) | 036 |
| 039 | ✅ | Wire App Insights + Log Analytics for L2 (audit-log with actor `tid`) | FULL | sonnet / high | none (dep 036 — shared Program.cs) | 036 |

### Phase C Wave C4 — Handler Implementations (15 tasks — H0 through H13, excluding H12a/b/c/H14)

| ID | Status | Title | Handler | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|---|
| 041 | ✅ | Implement H0 preflight (OpenAI TPM + Dataverse rate + subscription vCPU + SPE cert-bootstrap) | H0 | FULL | sonnet / high | waveC4-parallel | 036 |
| 042 | ✅ | Implement H0.5 consent-capture (BFF endpoint `POST /api/onboarding/consent-callback` + L2 handler) | H0.5 | FULL | sonnet / xhigh | none (BFF touch — serial) | 036 |
| 043 | ✅ | Implement H1 subscription readiness (ARM + Lighthouse) | H1 | FULL | sonnet / high | waveC4-parallel | 036 |
| 044 | ✅ | Implement H2a Bicep infra deploy (T1 owner: `keyVaultReferenceIdentity` PATCH) | H2a | FULL | sonnet / xhigh | waveC4-parallel | 014, 027, 028 |
| 045 | ✅ | Implement H2b AI Search index provisioning | H2b | FULL | sonnet / high | waveC4-parallel | 002 |
| 046 | ✅ | Implement H3 Entra app-reg (14 grants; single BFF app-reg; S2S dropped) | H3 | FULL | sonnet / high | waveC4-parallel | 010 |
| 047 | ✅ | Implement H4 KV-secrets population (interim `StaticKvSecretManifest`; T1 + T5 owner; DI swap path documented for Phase H task 084) | H4 | FULL | sonnet / xhigh | none (Phase H dep 084 — Path A interim placeholder per §11 justification) | 018, 019, (084 deferred) |
| 048 | ✅ | Implement H5 Dataverse env creation (interim `pac admin`) | H5 | FULL | sonnet / high | waveC4-parallel | 036 |
| 049 | ✅ | Implement H6 solution import via Package Deployer (8 solutions) | H6 | FULL | sonnet / high | waveC4-parallel | 012 |
| 050 | ✅ | Implement H7 Dataverse env-var values (5 hard-required per design.md §10.2 reconciliation; 7 total) | H7 | FULL | sonnet / high | Batch 3E parallel | 036 |
| 051 | ✅ | Implement H8 SPE container-type + root container (confidential-client per T6; canonical `SPE-ContainerTypeId` KV name) | H8 | FULL | sonnet / xhigh | Batch 3E parallel | 011 |
| 052 | ✅ | Implement H9 BFF deploy (blue-green slot swap) | H9 | FULL | sonnet / xhigh | none (dep 047 — needs KV ready) | 013, 047 |
| 053 | ✅ | Implement H10 Dataverse App User + Graph app-role parity (T2 + T3 owner; 2 App Users incl. BffAppReg system user) | H10 | FULL | sonnet / xhigh | Batch 3E parallel | 005, 015 |
| 054 | ✅ | Implement H11 user provisioning (identity preset per D6; NativeAccount + B2BGuest as alternative branches per Path C) | H11 | FULL | sonnet / high | Batch 3F parallel | 036 |
| 055 | ⏸ | Implement H13 E2E acceptance-gate (ALL 6 T1-T6 traps + ALL 5 I1-I5 invariants + naming-conformance + cost envelope) | H13 | FULL | sonnet / xhigh | none (dep 041-054 + 064-067 + 070-073) | ALL C4 + C6 + C' handlers |

### Phase C Wave C5 — L2 REST Endpoints + State Reconciler (6 tasks)

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 057 | ✅ | Implement L2 REST endpoints (9 endpoints per §4.2) with `Operator`/`Reader` app-roles | FULL | opus / high | none (dep 036, 042) | 036, 042 |
| 058 | ✅ | Implement state-reconciler `BackgroundService` (5s polling + DAG advancement) | FULL | opus / xhigh | none (dep 037, 038, 057) | 037, 038, 057 |
| 059 | ⏸ | Implement I5 concurrency guard (optimistic upsert `sprk_currentrunid`; 409 conflict) | FULL | sonnet / xhigh | none (dep 023, 058) | 023, 058 |
| 060 | ⏸ | Implement I6 crash recovery (startup scan orphaned `Running`/`WaitingOnGate` runs) | FULL | sonnet / xhigh | none (dep 058, 059) | 058, 059 |
| 061 | ⏸ | Implement §4C rollback semantics (4-class taxonomy + `Quarantined` state + clear-quarantine audit-log) | FULL | sonnet / xhigh | none (dep 057, 058) | 057, 058 |
| 062 | ⏸ | Load test — L2 REST enqueue-and-return-202 + reconciler DAG advancement (test-modifying → unconditional FULL) | FULL | sonnet / high | none (dep 057-061) | 057, 058, 059, 060, 061 |

### Phase C Wave C6 — Tenant-Isolation ArchTests + Audit Sweep (4 tasks)

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 064 | ✅ | Author 5 new ArchTests for §4D I1–I5 tenant-isolation invariants (test-modifying → unconditional FULL) | FULL | sonnet / xhigh | none (dep 042 for I1 coverage) | 042 |
| 065 | ✅ | Phase A audit sweep of every BFF service touching AI Search / Cosmos / Graph / SPE for I2–I5 compliance | FULL | sonnet / xhigh | none (dep 064) | 064 |
| 066 | ✅ | Verify `Register-EntraAppRegistrations.ps1:63` fix + add pre-commit tenant-shaped GUID scan ArchTest | FULL | sonnet / high | none (dep 064) | 064 |
| 067 | ⏸ | Nightly Graph app-role parity ArchTest (queued behind CI-wiring per r3 coord) — parallel-safe:false | FULL | sonnet / high | none (coord PR dep) | 005, 053, 064, 088 |

### Phase C' — Config-Seed Manifest + H12/H14 Implementations (5 tasks)

| ID | Status | Title | Handler | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|---|
| 069 | ✅ | Author declarative seed manifest + generator (resolves R14 two-source drift) | — | FULL | opus / high | none (dep 004) | 004 |
| 070 | ✅ | Implement H12a AI seed chain handler (playbook consumers per ADR-039) | H12a | FULL | sonnet / high | waveCp-parallel | 069 |
| 071 | ✅ | Implement H12b app-config seed handler (DAG-parallel with H12a) | H12b | FULL | sonnet / high | waveCp-parallel | 069 |
| 072 | ✅ | Implement H12c runtime references (`sprk_aimodeldeployment` handler; live-schema Path C for tenantId column absent) | H12c | FULL | sonnet / high | Batch 3F parallel | 044, 070, 071 |
| 073 | ✅ | Implement H14 post-deploy integration wiring (parent + 3 sub-handlers; 1 new ADR-028 Path A for Exchange app-only PS) | H14 | FULL | sonnet / high | Batch 3F parallel | 047, 053 |

### Phase D — L3 Skill + BFF Endpoint + Metering (4 tasks)

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 075 | 🔲 | Author `/provision-environment` skill at `.claude/skills/provision-environment/SKILL.md` (Sub-Agent Write Boundary — MAIN-SESSION-ONLY) | STANDARD | opus / high | none (parallel-safe:false) | 057 |
| 076 | 🔲 | Author fallback matrix impl in `/provision-environment` skill (MCP disconnect handling) | MINIMAL | sonnet / medium | none (parallel-safe:false; touches `.claude/skills/**`) | 075 |
| 077 | ✅ | Implement per-tenant token-metering layer (D19 — APIM OR app-level custom App Insights metric) — chose **app-level** (extends existing observability shipped by ai-architecture-redesign-r1 task 054; adds `TenantBudgetPolicy` + `InMemoryTenantTokenLedger` enforcement seam on `OpenAiClient`); build 0/0, 20/20 metering tests pass, publish 44.96 MB (Δ 0.00), CVE clean | FULL | opus / high | none (dep Phase A decision) | 001-008 (Phase A) |
| 078 | ⏸ | Verify `POST /api/onboarding/consent-callback` E2E with actual Model 2 admin-consent flow | FULL | sonnet / high | none (dep 042, 057) | 042, 057 |

### Phase E — DemoExpirationService Migration (3 tasks) — parallel with C/C'/D

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 080 | ✅ | Refactor `DemoExpirationService.cs` off `[Obsolete]` `DemoProvisioningOptions.Environments`/`DefaultEnvironment` → `DataverseEnvironmentService` | FULL | sonnet / xhigh | none (serial BFF touch; frozen file mod) | none |
| 081 | ✅ | Refactor `RegistrationEndpoints.cs` lines 466/468/469 (remove 4 `[Obsolete]` warnings) | FULL | sonnet / high | none (dep 080) | 080 |
| 082 | ⏸ | Delete `DemoProvisioning__Environments__*` + `__DefaultEnvironment` from Azure config; verify BFF `/health` + publish size delta | FULL | sonnet / high | none (dep 080, 081 + deploy) | 080, 081 |

### Phase H — KV Federation Full Remediation (5 tasks)

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 084 | ✅ | Author canonical secret-catalog manifest + generator (r3 Phase 3b) | FULL | opus / xhigh | none (dep 018-020) | 018, 019, 020 |
| 085 | ✅ | Alias collapse for AI Search key with BINDING pre-check protocol (§7.9) | FULL | sonnet / xhigh | none (dep 084) | 084 |
| 086 | ⏸ | IaC alignment — Bicep secret names + BFF app-setting keys to canonical | FULL | sonnet / high | none (dep 084, 085) | 084, 085 |
| 087 | ⏸ | Implement `/config.json` runtime endpoint for external-spa + code-pages (parallel-safe:false — external-spa surface overlap) | FULL | sonnet / xhigh | none | 086 |
| 088 | ⏸ | Coordinate `.github/workflows/**` gate wiring via PR to `ci-cd-unit-test-remediation-r1` — parallel-safe:false | STANDARD | sonnet / medium | none (dep 064-067 + 084-087) | 064, 065, 066, 067, 084, 085, 086, 087 |

### Phase F — E2E Acceptance (1 task)

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 089 | ⏸ | Provision fresh `trial-{yyyymmdd}` customer stamp using Model 1 profile via new pipeline; verify `Setup Status = Ready` + all 6 traps + all 5 invariants + naming-conformance + cost envelope | FULL | sonnet / xhigh | none (dep ALL) | ALL previous phases |

### Wrap-up (1 task — MANDATORY per task-create Step 3.7)

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 090 | ⏸ | Project wrap-up: README status → Complete; lessons-learned.md; archive per `repo-cleanup`; INDEX.md row update; `/test-diet` per root §7 (parallel-safe:false — writes projects/INDEX.md in main repo) | STANDARD | sonnet / high | none | 089 |

---

## Parallel Execution Groups

Waves per plan.md § 3 Implementation Approach. Groups within a wave are parallel-safe IF `<relevant-files>` don't overlap (task-create Step 3.8 auto-demotion rule).

### Wave 0 — Independent parallel starts (no dependencies)

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **groupA-parallel** | 001, 002, 003, 004, 006, 007, 008 (7 tasks) | none | Doc/audit tasks; different files each; MINIMAL rigor (2 STANDARD) |
| **groupB-parallel** | 010, 011, 012, 013, 014, 016 (6 tasks) | none | Script hardening; different scripts each; STANDARD rigor |
| **groupG-parallel** | 018, 019, 020 (3 tasks) | none | Naming compliance at provisioning; different targets |
| **Serial (Phase A)** | 005 (11 GUID commits) | none | Serial per-GUID commit discipline |
| **Serial (Phase E)** | 080 → 081 → 082 (3 tasks) | none | Existing frozen file modification; sequential dep chain |
| **Serial (Phase D-skill)** | 075 → 076 (2 tasks) | none | `.claude/skills/**` — MAIN-SESSION-ONLY Sub-Agent Write Boundary |

**Max concurrency**: 16 total (per Wave 0 groups combined = 7+6+3+1+1+1 = 19 tasks kickable; group tasks compose per wave). Recommend dispatch 6 at a time per `.claude/skills/project-pipeline/SKILL.md` Step 5 concurrency cap.

### Wave 1 — Depends on Wave 0

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **Serial (Phase A)** | 015 | 005 complete | Grant-GraphAppRoles.ps1 helper reads GraphAppRoles.cs constant |
| **Serial (Phase G)** | 021 | 018, 019, 020 complete | Wire naming-conformance into H13 acceptance gate design |
| **waveC1-parallel** | 023, 024 | 004 complete (drift resolution) | Registry schema + Cosmos schema; different targets |
| **waveC2-parallel** | 027, 028, 032, 033 | 014 (for 027 Cosmos add) | Bicep authoring; different files |
| **waveC3-scaffold** | 036 | 024 (Cosmos POCO for L2 wiring) | L2 project scaffold — batch-of-1 (037/038/039 depend on it) |

### Wave 2 — Depends on Wave 1

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **Serial (Wave C1)** | 025 | 024 complete | ArchTest — cleartext secret scan against Cosmos |
| **waveC2-dep-chain** | 029, 030, 031 | 027, 028 complete | UAMI refactor + RBAC migration + platform.bicep rebuild |
| **waveC3-parallel** | 037, 038, 039 | 036 complete | Wire Cosmos + Service Bus + App Insights (sequential within — shared Program.cs) |

### Wave 3 — Depends on Wave 2

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **Bicep integration test** | 034 | 027-033 complete | Test-modifying → unconditional FULL |
| **waveC4-parallel (11 handlers)** | 041, 043, 045, 046, 048, 049, 050, 051, 054 | Various Phase B + C1 + C3 tasks | 11 handler impls — DAG-parallel per §4.1 |
| **waveC4-serial-BFF** | 042 (H0.5 BFF endpoint) | 036 + Wave C6 for I1/I5 ArchTest coverage | Serial (BFF touch) |
| **waveC4-serial-Deps** | 044 (H2a), 047 (H4), 053 (H10) | Various | Serial due to complex deps |
| **waveCp-parallel** | 069 → then 070, 071 (parallel) → then 072, 073 | 004, 044, 047, 053 | Seed manifest → H12a/b (parallel) → H12c + H14 |

### Wave 4 — Depends on Wave 3

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **Wave C5** | 057 → 058 → 059/060/061 → 062 | 036, 042, 037, 038, 023, 024 | L2 REST + reconciler + concurrency + rollback + load test |
| **Wave C6** | 064 → 065, 066 (parallel) | 042 (for I1 coverage) | ArchTests + audit sweep |
| **Serial (H9 dep)** | 052 (H9 BFF deploy) | 013, 047 | Slot-swap semantics; separate from wave |
| **Phase H** | 084 → 085 → 086 → 087 | 018, 019, 020 (Phase G) | Canonical secret-catalog manifest chain |

### Wave 5 — Depends on Wave 4

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **Phase H CI coord** | 088 | 064, 065, 066, 067 + 084-087 | Coordinated PR with `ci-cd-unit-test-remediation-r1` |
| **Phase D** | 077 (metering) | Phase A decision | APIM vs custom metric |
| **Phase D verify** | 078 | 042 + 057 | E2E consent-callback verification |
| **Wave C6 nightly** | 067 | 005, 053, 064, 088 | Nightly Graph parity ArchTest (queued behind CI-wiring) |
| **H13 acceptance handler** | 055 | ALL C4 + C6 + C' handlers | Final gate assertion logic |

### Wave 6 — Depends on Wave 5

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **Phase F** | 089 (E2E dry run on `trial-{yyyymmdd}` Model 1) | ALL previous phases | Final acceptance |

### Wave 7 — Wrap-up

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **090 wrap-up** | 090 | 089 | Updates `projects/INDEX.md` in main repo — parallel-safe:false |

---

## Critical Path

Longest dependency chain (Wave 0 → Wave 6):

**001-004/006/007/008 (Phase A parallel doc-audits) → 005 (11 GUID commits, serial) → 015 (Grant-GraphAppRoles.ps1) → 023 (registry 12 columns) → 024 (Cosmos schema) → 036 (L2 scaffold) → 037/038/039 (L2 wiring, sequential Program.cs) → 041 (H0 preflight) → 044 (H2a Bicep deploy) → 047 (H4 KV secrets — via Phase H dep on 084) → 052 (H9 BFF deploy) → 053 (H10 Graph parity) → 070/071/072 (H12a/b/c) → 073 (H14 integrations) → 055 (H13 acceptance handler) → 062 (Load test) → 088 (CI coord) → 089 (E2E dry run) → 090 (wrap-up)**

**Estimated critical-path duration**: ~40–60 hours of focused work (single-implementer + Claude Code); ~10–14 weeks calendar with proactive checkpointing + wave build-verification gates per plan.md § 6.

---

## High-Risk Items

Per plan.md § 8 Risk Register — items requiring extra vigilance during execution:

| Task | Risk | Mitigation |
|---|---|---|
| **005** | Wrong GUID silently fails T3 → app-only Graph 403s in production (11 nulls; corrected from spec's "10") | Per-GUID isolated commit with `az` output cited; **escalation trigger** in POML if any role not found |
| **029, 030** | UAMI refactor blast radius = entire BFF startup path | `code-review` + `adr-check` at Step 9.5 FULL; slot-swap smoke test acceptance; interim mitigation (dual-slot System-Assigned MI grants) stays in place |
| **036, 057** | L2 control-plane scaffold = net-new .NET 10 project | Use existing 13 production `IJobHandler`s as pattern exemplars; separate DI/Program.cs from BFF; **escalation trigger** on DI registration conflict |
| **044 (H2a)** | Publish-size delta > 5 MB single-task | NFR-01 mandates per-PR reporting; **escalation trigger** if delta > 5 MB |
| **047 (H4)** | `keyVaultReferenceIdentity` PATCH silently fails (T1) | ARM read post-PATCH; **escalation trigger** in POML |
| **084 (Phase H manifest generator)** | Single-source generation must produce 4 outputs identically | Opus 4.8 / Fable 5 tier; BINDING pre-check protocol on task 085 (alias collapse) |
| **085 (alias collapse)** | Live consumer breaks silently | §7.9 BINDING pre-check protocol — LIVE App Service + KV + Dataverse-persisted config FIRST; **NEVER** delete `Dataverse-ClientSecret` / `BFF-API-ClientSecret` |
| **053 (H10)** | 10-of-14 GUIDs (actually 11) MUST be complete before first production customer | H10 escalation gate in POML dep on 005 |
| **087 (`/config.json`)** | External-spa surface shared with `spaarke-SPA-external-access-platform-r1/r2` | `parallel-safe:false`; `/conflict-check` before PR |
| **055 (H13)** | Final gate — ANY trap OR invariant OR cost drift > 20% fails run | Comprehensive assertion logic per POML acceptance-criteria |
| **089 (Phase F)** | E2E acceptance on trial stamp — cost overruns + regional TPM headroom | H0 preflight catches upstream; **escalation trigger** on ANY trap/invariant/cost fail |

---

## Coordination Signals

Per `projects/INDEX.md` hot-path overlap analysis + discovery report §11:

- **`ci-cd-unit-test-remediation-r1`** (owns `.github/workflows/**` for 28-day window) — Phase H task 088 is coordinated PR. **Missing artifact**: `task-042-063-ci-gate-wiring-deferral.md` referenced but does not exist — task 088 must reconstitute OR coordinate via `ci-cd-unit-test-remediation-r1/CLAUDE.md`.
- **`code-quality-and-assurance-r3`** (actively decomposing BFF) — Phase E tasks 080/081/082 may bump into r3 dead-code-removal PRs; `/conflict-check` before Phase E PR.
- **`spaarke-ai-architecture-redesign-r1/r2`** (broadest BFF AI touch) — H0.5 endpoint (task 042) unlikely to touch `Services/Ai/**` per current scope; verify at execution time.
- **`spaarke-devops-project-tracking-r1` (PR #453)** — modifies `project-pipeline` SKILL.md itself; our execution uses local copy, no runtime dependency.
- **`spaarke-SPA-external-access-platform-r1/r2`** — Phase H task 087 `/config.json` runtime endpoint touches external-spa surface; coordinate.
- **19 active BFF worktrees** — `/conflict-check` before EVERY BFF PR (per r3 handoff §7).

---

## r1-Specific Discovery Findings (applied)

Per `notes/resource-discovery-2026-08-16.md`:

1. **`GraphAppRoles.cs`**: actual = 11 nulls (not 10 as spec/design/CLAUDE.md claim). Task 005 corrected in-file; will produce doc-reconciliation commit updating spec.md + design.md + CLAUDE.md.
2. **Registry columns**: actual = 12 new (spec.md FR-26 says "11" but enumerates 12 items — missing `sprk_provisionedon`). Task 023 corrected in-file; will produce doc-reconciliation commit.
3. **`model1-shared.bicep`**: EXISTS already (309 LOC per discovery §6). Task 032 caught by C2 subagent — reframed as review/refactor-if-scaffold-exists with existing-file cited in `<justification><existing>`.
4. **ADR path drift**: `docs/adr/` (44 files) vs `.claude/adr/` (47 files) don't overlap perfectly. Individual tasks reference the correct path per ADR (verified in each POML's `<knowledge>` `<files>`).
5. **Missing coord artifact**: `task-042-063-ci-gate-wiring-deferral.md` referenced but does not exist. Task 088 must reconstitute OR coordinate via `ci-cd-unit-test-remediation-r1/CLAUDE.md` alternate docs.

---

## Progress Summary

| Metric | Value |
|---|---|
| **Total tasks** | 78 |
| **not-started** 🔲 | 15 |
| **in-progress** 🟡 | 0 |
| **completed** ✅ | 63 (Wave 0: 18; Wave 1: 9; Wave 2: 7; Wave 3: 19; Wave 4A: 081+084; Wave 4B: 052+057+064+077; Wave 4C: 058+065+066+085) |
| **Wave 4 Batch 4C COMPLETE (2026-08-17)** | 4 parallel subagents landed clean · 058 commit `1b0297c7b` (state-reconciler BackgroundService — 524/524 L2 tests, N=5 dedup verified) · 066 commit `e54cfb6e5` (verify 1834b77bc + regression seed test) · 085 commits `4ab4fbeda`+`06db97468` (AI Search alias collapse — 2 dev KV aliases deleted, health 200 after each step, soft-delete recovery until 2026-11-16) · 065 commit `f66a6add7` (12 baseline violations fixed + 47-site audit report; all 5 §4D ArchTests PASS 22/22 with neg-controls) |
| **Wave 4 Batch 4B COMPLETE (2026-08-17)** | 057 `b8dcdfaeb` · 052 `67e8830ba` · 077 `111773ffc` · 064 `40b09f837` |
| **Wave 4 Batch 4A COMPLETE (2026-08-17)** | ArchTest debt `3b67a7b8d` · 081 `0b8ca53ba` · 084 `70abd9992` |
| **All 5 §4D tenant-isolation ArchTests GREEN (2026-08-17 post-4C)** | I1 (PS scripts), I2 (AI Search tenantId filter), I3 (Cosmos PartitionKey), I4 (SPE literals), I5 (Graph per-tenant token) all pass. Total 65/65 ArchTests suite pass. Zero baseline violations remaining. |
| **Follow-on drift surfaced during 4C (per fix-at-discovery principle)** | (a) Task 065 flagged `ManagedIdentityCredentialFactory.cs:34-40` has same "no TenantId on options bag" gap as GraphClientFactory but sits OUTSIDE I5 ArchTest scope (`Infrastructure/Graph/**` only) — audit report §7.2 for owner decision: broaden I5 or targeted PR. (b) Task 085 flagged prod-side `Seed-ProductionKeyVault.ps1` + `Configure-ProductionAppSettings.ps1` + stale `platform.json` compiled artifact still reference `ai-search-key` — dev-scope fix succeeded but prod-side deferred. Both items follow-on candidates. |
| **blocked** ⏸ | (per dep chains — resolvable) |
| **Ready to start (no deps)** | 21 tasks: 001, 002, 003, 004, 005, 006, 007, 008, 010, 011, 012, 013, 014, 016, 018, 019, 020, 032, 033, 080 (Phase E), plus 023, 024 (Wave C1 pending 004) |

---

## Next Action

**Owner review** of this TASK-INDEX + spec.md + plan.md → invoke `task-execute 001` (or `/project-continue`) to begin Phase A.

Recommended Wave 0 dispatch (dispatch 6 in parallel, then next 6):
1. **Batch 1** (6 tasks, groupA-parallel + serial): 001, 002, 003, 004, 006, 007
2. **Batch 2** (6 tasks): 008, 010, 011, 012, 013, 014
3. **Batch 3** (5 tasks): 016, 018, 019, 020, 080
4. **Serial**: 005 (11 GUID commits per isolated commit discipline)
5. **Wait for** {004, 005, 018, 019, 020} → Wave 1 can start

---

*Registry maintained by `task-execute` (per-task status updates) + `context-handoff` (proactive checkpoint) + `/project-continue` (session-recovery cross-check).*
