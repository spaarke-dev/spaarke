# TASK-INDEX — customer-provisioning-orchestration-r1

> **Last Updated**: 2026-08-26 SESSION 10 — **S2∥ wave: A38 split + A44.5 ALL COMPLETE + BUNDLE COMMITTED**. Full A38 re-scope (A38a/A38b/A38c + A44.5) executed cleanly across 4 parallel background agents (~1.5M tokens). 205a (A38a Fable/xhigh) landed manifest half via FileKvSecretManifest served-entry filter + FR-39 seam extension + new marker mechanism (`ISecretFreeMarkerApplier` + `ArmSecretFreeMarkerApplier` + `SecretFreeMarkerConsistencyDetector`); fired `site-inventory-drifted` on 6th site (`Setup-OfficeServiceBus.ps1:172`) → owner disposition (main-session-verified as dead-legacy: target App Service ResourceNotFound, KV secret intentionally removed by auth-v4 task 033, SB infra live via canonical Bicep) folded into 205h A38c gate list + deprecation banner added. 205g (A38b Sonnet/high) landed customer.bicep gate. 205h (A38c Sonnet/high) landed shared `Assert-SpaarkeSecretFreeGate.ps1` helper + 5 call sites across 4 scripts. 205i (A44.5 Fable/xhigh) landed H7/H6 + L2 Worker FR-39 credential seam + conditional Bicep KV-ref. Full suite 1646 pass / 0 fail / 1 skipped. Prior: 205b (A42) COMPLETE path (b) contract-parity in prior wave. Deep review of auth-v4 change request + 11 owner decisions Q1-Q11 RESOLVED SESSION 9. Prior: 2026-08-25 SESSION 9 (Fable deep review of auth-v4 change request + all 11 owner decisions Q1-Q11 RESOLVED + S1∥ A36/A37/A40 landed + A35 master merge both directions); 2026-08-25 SESSION 8 (Task 203a COMPLETE); 2026-08-24 SESSION 7 (task 202 COMPLETE + Class-B verification pass + 11 POMLs authored for 203a/b/c/d + 204a/b/c/d/e/f/g); 2026-08-20 (Wave G-7 Batch G-7E TERMINAL — task 186 authoring + framework-level r1 E2E goal proof landed).
> **Status**: Phase Pre-Live-Fire fully authored across SESSION 7 + SESSION 10 (extended for A38 re-scope) — task 202 (COMPLETE) + 203a (COMPLETE) + 205b (COMPLETE) blocks 203b/c/d + 204a-g + 205a/c/d/e/f/g/h/i; together they block task 186 E2E live-fire per owner directive + task 186 pre-check trigger. Task 204c is the ORIGINAL HARD-BLOCKER (H13 real probes — 10 sub-tasks); task 205 sub-phase is the AUTH-V4 hard-blocker — new critical path with A38 split = {A38a ∥ A38b} (bundle-required) + A42 (✅ landed) → A39. Total revised effort ~15h with parallel lanes (was 11h pre-split; +4h for A44.5 + coordinated bundle logistics).
> **Task count**: 161 POMLs (78 original across 10 phases + 62 Phase C'' across 8 waves + 21 Pre-Live-Fire tasks: 202 + 203a-d + 204a-g + 205a/b/c/d/e/f/g/h/i SESSION 10 2026-08-25 with re-scope split for A38)
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

## Task Registry (80 total — 78 original + 2 Phase H-Prime added 2026-08-24 SESSION 3)

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
| 055 | ✅ | Implement H13 E2E acceptance-gate (ALL 6 T1-T6 traps + ALL 5 I1-I5 invariants + naming-conformance + cost envelope) | H13 | FULL | sonnet / xhigh | none (dep 041-054 + 064-067 + 070-073) | ALL C4 + C6 + C' handlers |

### Phase C Wave C5 — L2 REST Endpoints + State Reconciler (6 tasks)

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 057 | ✅ | Implement L2 REST endpoints (9 endpoints per §4.2) with `Operator`/`Reader` app-roles | FULL | opus / high | none (dep 036, 042) | 036, 042 |
| 058 | ✅ | Implement state-reconciler `BackgroundService` (5s polling + DAG advancement) | FULL | opus / xhigh | none (dep 037, 038, 057) | 037, 038, 057 |
| 059 | ✅ | Implement I5 concurrency guard (optimistic upsert `sprk_currentrunid`; 409 conflict) | FULL | sonnet / xhigh | none (dep 023, 058) | 023, 058 |
| 060 | ✅ | Implement I6 crash recovery (startup scan orphaned `Running`/`WaitingOnGate` runs) | FULL | sonnet / xhigh | none (dep 058, 059) | 058, 059 |
| 061 | ✅ | Implement §4C rollback semantics (4-class taxonomy + `Quarantined` state + clear-quarantine audit-log) | FULL | sonnet / xhigh | none (dep 057, 058) | 057, 058 |
| 062 | ✅ | Load test — L2 REST enqueue-and-return-202 + reconciler DAG advancement (test-modifying → unconditional FULL) | FULL | sonnet / high | none (dep 057-061) | 057, 058, 059, 060, 061 |

### Phase C Wave C6 — Tenant-Isolation ArchTests + Audit Sweep (4 tasks)

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 064 | ✅ | Author 5 new ArchTests for §4D I1–I5 tenant-isolation invariants (test-modifying → unconditional FULL) | FULL | sonnet / xhigh | none (dep 042 for I1 coverage) | 042 |
| 065 | ✅ | Phase A audit sweep of every BFF service touching AI Search / Cosmos / Graph / SPE for I2–I5 compliance | FULL | sonnet / xhigh | none (dep 064) | 064 |
| 066 | ✅ | Verify `Register-EntraAppRegistrations.ps1:63` fix + add pre-commit tenant-shaped GUID scan ArchTest | FULL | sonnet / high | none (dep 064) | 064 |
| 067 | ✅ | Nightly Graph app-role parity ArchTest (test project + BFF↔L2 mirror drift-guard landed; workflow wiring deferred to ci-cd-r1 coord PR per notes/graph-app-role-parity-coord-pr.md) | FULL | sonnet / high | none (coord PR dep) | 005, 053, 064, 088 |

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
| 075 | ✅ | Author `/provision-environment` skill at `.claude/skills/provision-environment/SKILL.md` (Sub-Agent Write Boundary — MAIN-SESSION-ONLY) — LANDED 2026-08-18 main-session; ~470 LOC skill with Step 0 prereqs (6 checks) + Step 1-6 flow + Tool Matrix + Auth Flow + Dry-run + Troubleshooting + Failure Modes; skills registry auto-discovered | STANDARD | opus / high | none (parallel-safe:false) | 057 |
| 076 | ✅ | Author fallback matrix impl in `/provision-environment` skill (MCP disconnect handling) — LANDED 2026-08-18 main-session; added ~155 LOC Fallback Matrix section (F1 MCP disconnect → pac data / raw Web API PS; F2 az token expiry → auto-refresh; F3 L2 unreachable → escalate + resume-from-Cosmos I6) + cross-references from Steps 4/5/6 | MINIMAL | sonnet / medium | none (parallel-safe:false; touches `.claude/skills/**`) | 075 |
| 077 | ✅ | Implement per-tenant token-metering layer (D19 — APIM OR app-level custom App Insights metric) — chose **app-level** (extends existing observability shipped by ai-architecture-redesign-r1 task 054; adds `TenantBudgetPolicy` + `InMemoryTenantTokenLedger` enforcement seam on `OpenAiClient`); build 0/0, 20/20 metering tests pass, publish 44.96 MB (Δ 0.00), CVE clean | FULL | opus / high | none (dep Phase A decision) | 001-008 (Phase A) |
| 078 | ✅ | Verify `POST /api/onboarding/consent-callback` E2E (signed-synthetic-payload path per POML alternative) — 7 E2E tests at `tests/integration/Sprk.Bff.Api.IntegrationTests/Onboarding/` cover 4 POML paths (happy · re-consent idempotency · restart · 401 HMAC) + §4D I1 · missing-sig-header · base64-signature; 7/7 PASS; BFF 10,484/0/97 preserved; publish 44.96 MB Δ +0.00; CVE clean; report `notes/consent-callback-e2e-2026-08-18.md`; 5 documented deviations (all Path C except D-078-4 Path A — L2 state-check scoping) | FULL | sonnet / high | none (dep 042, 057) | 042, 057 |

### Phase E — DemoExpirationService Migration (4 tasks — 081.5 added 2026-08-18 per task 082 escalation) — parallel with C/C'/D

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 080 | ✅ | Refactor `DemoExpirationService.cs` off `[Obsolete]` `DemoProvisioningOptions.Environments`/`DefaultEnvironment` → `DataverseEnvironmentService` | FULL | sonnet / xhigh | none (serial BFF touch; frozen file mod) | none |
| 081 | ✅ | Refactor `RegistrationEndpoints.cs` lines 466/468/469 (remove 4 `[Obsolete]` warnings) | FULL | sonnet / high | none (dep 080) | 080 |
| 081.5 | ✅ | Refactor `RegistrationDataverseService` ctor off `[Obsolete]` `DemoProvisioningOptions` fallback (unblocks 082); pre-deploy Azure config gate (`DATAVERSE_URL` on dev) + 4-config-gap fix (`PublicConfig__{BffUrl,MsalClientId,TenantId}` + `Onboarding__EnableDevBypass=true`); code deploy + `/healthz` verify — **LANDED 2026-08-18 main-session. Subagent's initial deploy failed with container exit 134; root cause was NOT the refactor but 4 missing Tier-1 IOptions from Wave 4 tasks 042/087 that were never rolled out to dev App Service (subagent misdiagnosed as deploy-path flakiness — missed the `StartupLogs/*_failure.log` file with the actual `OptionsValidationException`). Main-session applied the 4 config values → `/healthz` recovered → republished + redeployed refactored code via direct `az webapp deploy` → `RuntimeSuccessful` on first attempt → `/healthz` 200 first poll. Full postmortem in `notes/task-081.5-rollback.md`. Publish 44.96 MB Δ 0.00. Tests 10,484/0. CVE clean.** | FULL | sonnet / high | none (serial BFF ctor + deploy) | 080, 081 |
| 082 | ✅ | Delete `DemoProvisioning__Environments__*` + `__DefaultEnvironment` from Azure config; verify BFF `/health` + publish size delta — **LANDED 2026-08-18 main-session (subagent crash-recovery). Subagent successfully deleted 9 Azure config keys + removed 2 [Obsolete] properties from `DemoProvisioningOptions.cs` (build 0/0), then died mid-run with API error before commit. Main-session inspected state → keys confirmed deleted + `/healthz` = 200 + class change safe (all remaining grep hits are historical doc-comments only). Published class-cleanup binary + deployed via direct `az webapp deploy` → `RuntimeSuccessful` 32s → `/healthz` = 200 first poll. FR-33 [Obsolete] retirement fully complete: source removed + binary redeployed + Azure config aligned. Publish 44.96 MB Δ 0.00.** | FULL | sonnet / high | none (dep 080, 081, 081.5 + deploy) | 080, 081, 081.5 |

### Phase H — KV Federation Full Remediation (5 tasks)

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 084 | ✅ | Author canonical secret-catalog manifest + generator (r3 Phase 3b) | FULL | opus / xhigh | none (dep 018-020) | 018, 019, 020 |
| 085 | ✅ | Alias collapse for AI Search key with BINDING pre-check protocol (§7.9) | FULL | sonnet / xhigh | none (dep 084) | 084 |
| 086 | ✅ | IaC alignment — Bicep secret names + BFF app-setting keys to canonical | FULL | sonnet / high | none (dep 084, 085) | 084, 085 |
| 087 | ✅ | Implement `/config.json` runtime endpoint for external-spa + code-pages (parallel-safe:false — external-spa surface overlap) | FULL | sonnet / xhigh | none | 086 |
| 088 | ✅ | Coordinate `.github/workflows/**` gate wiring via PR to `ci-cd-unit-test-remediation-r1` (umbrella spec notes/phase-h-ci-wiring-coord-pr.md: naming-conformance + I1-I5 ArchTests + nightly Graph parity; consolidates 067's partial as §3 detail source) — parallel-safe:false | STANDARD | sonnet / medium | none (dep 064-067 + 084-087) | 064, 065, 066, 067, 084, 085, 086, 087 |

## Phase C'' — Execution-Engine Build (Waves G-1..G-7, 58 tasks)

> Authored by task decomposition of DS-4 (handler audit) / DS-1b (Option D hybrid) / DS-2 + DS-2b (dispatcher design) / DS-3 (API/Worker split) / DS-5 (Cat 4/5/6 remediation) / DS-8 (Path X UAMI-Dataverse-App-User). Delivers the r1 stated goal: E2E customer provisioning per FR-18 / SC #5. All new tasks start at status not-started (🔲).

### Phase C'' Wave G-1 -- Foundation (19 tasks)

_L2 project split, dispatcher, keyed-DI, C4.5 serializer fix, queue recreate, Bicep config fixes, Path X grant script, C1.4 registry client, deploy script, sidecar image, CI coordination_

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 100 | ✅ | Split L2 project into .Core / .Api / .Worker (DS-3 Option 2) | FULL | opus / high | none | none |
| 101 | ✅ | Author .Worker App Service Bicep module + wire into platform-controlplane.bicep | STANDARD | sonnet / high | waveG1-parallel | 100 |
| 102 | ✅ | Implement ProvisioningHandlerDispatcher BackgroundService (ServiceBusSessionProcessor) in .Worker | FULL | opus / xhigh | none | 100, 101 |
| 103 | ✅ | HandlerIds catalog + keyed DI registration for 20 dispatchable handlers (C1.2) | FULL | sonnet / high | none | 100 |
| 104 | ✅ | Extract IHandlerOutcomeApplier from StateReconcilerService (C2.1 wiring hook) | FULL | sonnet / high | none | 100 |
| 105 | ✅ | Dispatcher decision + idempotency test suite (DispatchCoreAsync table-driven, DispatchIdempotencyService, envelope round-trip) | FULL | sonnet / high | none | 102 |
| 106 | ✅ | Fix C4.5: dual Newtonsoft StringEnumConverter on RunStatus/GateState/QuarantineState + serializer-contract test + Cosmos seam test | TEST-MODIFYING (unconditional FULL) | sonnet / high | waveG1-parallel | none |
| 107 | ✅ | Add attempt field to ReconcilerEnqueuePayload (L1 dedup vs §4C retry interaction fix) | TEST-MODIFYING (unconditional FULL) | sonnet / high | none | 108 |
| 108 | ✅ | Bicep: recreate sprk-provisioning-jobs queue with sessions + dedup (C5.4/C4.6) + drain-verify runbook — authoring complete, LIVE DELETE+RECREATE EXECUTED 2026-08-21 (Wave H-3 Step 1). Queue now has requiresSession=true, requiresDuplicateDetection=true, duplicateDetectionHistoryTimeWindow=PT1H. RBAC baseline preserved. See notes/queue-recreate-runbook-2026-08.md | FULL | sonnet / high | none | none |
| 109 | ✅ | Bicep: fix config-key/audience/secret-name source drift (C5.1-C5.3) — renamed Cosmos__* keys, MI-only ServiceBus FQNS+QueueName (dropped KV-ref conn-string), added ManagedIdentity__ClientId, spaarke.com/ audience, dataverseClientSecretName deprecated-pending-C1.4, dev.bicepparam B1->P1v3 (auth-v4 §8 item 4). Also regenerated stale controlplane-app-service.json + platform-controlplane.json (last compiled at task 033, never regenerated by tasks 101/108). Follow-on filed: controlplane-worker-app-service.bicep (task 101) carries the identical C5.1 key-shape drift, out of this task's scope. | FULL | sonnet / high | none | none |
| 110 | ✅ | Bicep: SB Data Receiver (+Sender) RBAC role assignments for L2 UAMI(s) (C5.5) — authoring complete + LIVE GRANTED 2026-08-21 (Wave H-3 Step 2). Both Bicep-managed with deterministic guids: Sender=006895e1-b286-58be-8482-ab25d09354a2, Receiver=e95043c4-7226-5496-afb7-a28eb07a8557. Ad-hoc Sender assignment (2efad74b-…) deleted + replaced under Bicep management (Path A cleanup). FOLDED-IN: fixed IDENTICAL C5.1 key-shape bugs task 109 flagged in controlplane-worker-app-service.bicep (Cosmos__* rename, ServiceBus__ConnectionString -> FQNS+QueueName MI-only, added ManagedIdentity__ClientId); removed now-fully-dead serviceBusKeyVaultSecretName param from platform-controlplane.bicep. | FULL | sonnet / high | none | 100, 101 |
| 111 | ✅ | Author Grant-ControlPlaneIdentity.ps1 (Path X Dataverse App User + scoped custom role + C5.8 Graph app-role grants) | FULL | opus / high | waveG1-parallel | none |
| 112 | ✅ | Implement C1.4 L2 Dataverse registry client (MI-native, DefaultAzureCredential) | FULL | opus / high | none | 111 |
| 113 | ✅ | Author Deploy-ControlPlane.ps1 (L2 repeatable deploy script, C5.9/C1.7) -- authored + LIVE EXECUTED 2026-08-21 (Wave H-3 Step 9). Deploys .Api + .Worker code to spaarkedev1 with kvRefIdentity PATCH + /healthz verification. Both sites now HTTP 200 on /healthz. Path deviation from POML: .Api staging slot health check fails due to slot-not-inheriting-app-settings gap (deferred as H-3.5 residual); .Api deployed direct-to-prod as MVP workaround. Sidecar sitecontainer removed (has Bicep app-setting-reference bug — deferred as H-3.5 residual). Script itself needed `-Confirm:$false` for NonInteractive PowerShell; also revealed 4 fix-at-discovery patches (Grant-GraphAppRoles.ps1 `$select URL, Grant-ControlPlaneIdentity.ps1 Depth+root-role, Bicep sitecontainer UAMI + AzureAd:ClientId). All 5 fix patches committed at 5076b0a14 + 0ba11c1c7 + follow-on. | FULL (overridden up from STANDARD -- .cs touch) | sonnet / high | none | 100, 101, 108, 109 |
| 114 | ✅ | Build Exchange sidecar image (Dockerfile + pwsh HTTP listener + Set-ExchangeApplicationAccessPolicy.ps1 port) | FULL | opus / high | waveG1-parallel | none |
| 115 | ✅ | GitHub Actions workflow for sidecar build/push (ACR + Trivy gate) -- coordinated with ci-cd-r1; drafted YAML + escalation (closed coord window, stale worktree) in notes/sidecar-ci-workflow-coord-pr.md; NOT committed to .github/workflows/** | STANDARD | sonnet / high | waveG1-parallel | 114 |
| 116 | ✅ | BFF artifact publish workflow extension for H9 (blob store + latest.json manifest) -- committed DIRECTLY to .github/workflows/deploy-bff-api.yml (Path C deviation from "coord-note only" per r1's 2026-08-19 direct-ownership governance; see notes/h9-artifact-publish-ci-coord-pr.md); new .github/workflows/schemas/bff-artifact-manifest.json; storage account escalation documented (not created) | STANDARD | sonnet / high | waveG1-parallel | none |
| 117 | ✅ | CI coordination: Bicep->ARM-JSON pre-compile step for H2a -- NEW workflow `.github/workflows/publish-provisioning-arm-artifacts.yml` committed DIRECTLY (Path C deviation, same 2026-08-19 direct-ownership governance as task 116; see notes/h2a-bicep-precompile-ci-coord-pr.md); new `.github/workflows/schemas/provisioning-arm-manifest.json`; compiles customer.bicep + stacks/model1-shared.bicep (the exact 2 templates per FileBicepTemplateInspector.ResolveTemplatePath) to versioned ARM JSON in the SAME provisioning-artifacts container as task 116; storage account escalation re-flagged (not created, same gap task 116 filed) | STANDARD | sonnet / high | waveG1-parallel | none |
| 118 | ✅ | Integration seam test: dispatch spine (message -> handler -> Cosmos transition -> DAG advance) -- `.Tests/Seam/ProvisioningDispatchSpineSeamTests.cs` (2 Facts), real Worker DI + real DispatchCoreAsync/HandlerOutcomeApplier/StateReconcilerService/DagAdvancer/CosmosActiveRunScanner, only IHandlerEnqueuer faked; env-guarded via COSMOS_L2_SMOKE_ENDPOINT (skip-by-default, CI-safe); commit c56be622d | TEST-MODIFYING (unconditional FULL) | sonnet / high | none | 102, 104, 106, 108 |

### Phase C'' Wave G-2 -- Entry + resources (7 tasks)

_H0/H1/H0.5/H2a/H2b/H4 SDK ports incl. H4 real-value-sourcing correctness gate_

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 120 | ✅ | H0: SDK-port 4 preflight probes (ARM.CognitiveServices TPM, BAP-REST env-rate, ARM.Compute vCPU, KV cert-bootstrap) | FULL | sonnet / high | waveG2-parallel | 102, 103 |
| 121 | ✅ | H1: real ARM subscription-reachability + Lighthouse probe (replace NullSubscriptionReadinessProbe) | FULL | sonnet / high | waveG2-parallel | 102, 103 |
| 122 | ✅ | H0.5: DI-swap onto C1.4 registry client (Null-Object unregistered from composition root — class kept for its own ADR-032 P2 contract tests; +Bicep AdminEnvironmentUrl wiring + completeness ArchTest) | FULL | sonnet / high | none | 112 |
| 123 | ✅ | H2a: ARM.Resources deployment port (ArmDeployment.CreateOrUpdateAsync + WhatIfAtSubscriptionScopeAsync) + T1 KV-ref probe port | FULL | sonnet / xhigh | waveG2-parallel | 102, 103, 117 |
| 124 | ✅ | H2b: SearchIndexClient port + REAL AI Search tenant-filter template provisioner (replace Stub) | FULL | sonnet / high | waveG2-parallel | 102, 103, 123 |
| 125 | ✅ | H4: SecretClient family port + ARM.AppService KeyVaultReferenceIdentity PATCH (T1) both slots + ARM.Authorization role assignment (T5) | FULL | sonnet / high | none | 102, 103, 123 |
| 126 | ✅ | H4: real value-sourcing per KvSecretValueSource (generate/copy/reference) + task-084 canonical manifest DI-swap (C2.2) | FULL | sonnet / xhigh | none | 125 |

### Phase C'' Wave G-2.5 -- customer.bicep completion (4 tasks, Path 1 owner decision post-Wave-G-2; 128b added 2026-08-19 during task 128's authoring per E1/E2 escalation)

_Task 123's + task 126's discovery notes both flagged that customer.bicep (the only Bicep template task 123's ArmDeploymentRunner deploys in production) is missing resources every downstream Wave G-3/G-4 handler assumes exist. Owner chose Path 1: close these gaps BEFORE dispatching Wave G-3's H3/H8/H9 handler ports. 127+128 are parallel-safe against each other (disjoint insertion zones in customer.bicep). 128b is sequential after 127+128 (needs 127's UAMI output + 128's AI-Search-adjacency zone) and was authored mid-wave when 127's and 128's own escalation triggers (E1: DocIntel/AppInsights out of both tasks' declared scope) and 129's escalation trigger (E2: Redis per-env-vs-per-customer conflict, owner-reconciled for Model2Dedicated only) surfaced a third gap. 129 is sequential after 127+128 (and, per 128b's own escalation trigger 3, should ideally be re-verified against 128b's landed state before executing, since 128b resolves 3-4 of 129's originally-omitted kv-secrets entries)._

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 127 | ✅ | customer.bicep: wire UAMI + BFF App Service (plan + prod/staging slots) -- reuses existing modules/uami.bicep + app-service*.bicep (tasks 028/029), currently orphaned | FULL | sonnet / high | waveG2point5-parallel | 102, 103, 123 |
| 128 | ✅ | customer.bicep: wire Azure OpenAI + AI Search modules -- reuses existing modules/openai.bicep + ai-search.bicep, currently orphaned | FULL | sonnet / high | waveG2point5-parallel | 102, 103, 123, 124 |
| 128b | ✅ | customer.bicep: wire Document Intelligence + App Insights + Log Analytics + Redis -- reuses existing modules/doc-intelligence.bicep + monitoring.bicep + redis.bicep, currently orphaned; Redis wiring reverses a v3.2 documented decision per owner E2 reconciliation, folded-in Path B spec.md/design.md/CLAUDE.md amendment landed as **v3.6** (not v3.3 -- that number was already taken by the time this task executed; see design.md §20 CHANGELOG for the correction note) | FULL | sonnet / high | none | 102, 103, 123, 127, 128 |
| 129 | ✅ | customer.bicep: invoke kv-secrets.generated.bicep (task 084) with real values from sibling-module outputs -- closes 10 of 15 FromBicepOutput gaps (6 original + 4 newly-resolvable via 128b's Redis/DocIntel/AppInsights modules). Step 6: manifest.yaml reclassified BFF-API-ClientId/Audience from FromBicepOutput to FromRunParameters (owner E3), regenerated kv-secrets.generated.bicep via canonical generator. Only 3 permanent SPE-* runtime-only entries remain omitted (expected, H8/H9 resolve them at runtime via H4's FromRunParameters path). Wave G-2.5 customer.bicep completion is now DONE. | FULL | sonnet / high | none | 102, 103, 123, 127, 128 |

### Phase C'' Wave G-3 -- Identity + deploy (3 tasks)

_H3 Graph app-reg + real consent verifier, H8 confidential-client, H9 artifact-based rebuild_

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 130 | ✅ | H3: Graph app-reg port (Applications/ServicePrincipals/Oauth2PermissionGrants) + real consent verifier + Model 1/2 branch + FIC + KV writes (14-grant + Dataverse app-user reuse deviated to H10 — see task notes) | FULL | sonnet / xhigh | none | 102, 103, 125, 126 |
| 131 | ✅ | H8: Graph containerTypes port (ClientCertificateCredential, T6 cert from KV) -- GOTCHA: SharePoint-REST applicationPermissions replaced by native Graph containerTypeRegistrations; new WaitingOnGate (24h replication) outcome | FULL | sonnet / high | none | 102, 103, 125, 126 |
| 132 | ✅ | H9: artifact-based rebuild -- handler side (manifest verify + blob download + Kudu zip-deploy + SwapSlotAsync + rollback re-swap) | FULL | sonnet / high | none | 102, 103, 116 |

### Phase C'' Wave G-4 -- Dataverse chain (5 tasks)

_H5/H6/H7 SDK ports + credential config, H10/H11 live verification_

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 140 | ✅ | H5: BAP-REST env-create + async-operation-polling port | FULL | sonnet / high | waveG4-parallel | 102, 103, 123 |
| 141 | ✅ | H6: Web-API import port (ImportSolution/StageAndUpgrade + ImportJob polling) + ZIP artifact packaging | FULL | sonnet / xhigh | none | 102, 103, 140 |
| 142 | ✅ | H7: credential provisioning (EnvVarValues:ClientSecret KV ref) + NFR-05 validation | STANDARD | sonnet / high | waveG4-parallel | 126 |
| 143 | ✅ | H10: live verification post C5.8 grants (5 REST/Graph seams, code already real) | FULL | sonnet / high | none | 111, 140 |
| 144 | ✅ | H11: live verification post C5.8 grants (Graph REST + B2B invitation + consent verifier, code already real) | FULL | sonnet / high | none | 111, 143 |

### Phase C'' Wave G-5 -- Seed (4 tasks)

_H12a YamlDotNet engine, H12b DV-REST ports + 2 greenfield seeders (completes FR-16), H12c config_

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 150 | ✅ | H12a: YamlDotNet manifest engine + DV-REST seed writes | FULL | sonnet / high | waveG5-parallel | 141 |
| 151 | ✅ | H12b: 2 near-mechanical DV-REST ports (DataGrid + workspace-layout seeders) | STANDARD | sonnet / high | waveG5-parallel | 141 |
| 152 | ✅ | H12b: 2 greenfield seeders (field-mapping + chart-def) -- completes FR-16 | FULL | sonnet / high | none | 151 |
| 153 | ✅ | H12c: credential config only (no code delta) | STANDARD | sonnet / high | waveG5-parallel | 123, 150, 151, 152 |

### Phase C'' Wave G-6 -- Integration wiring (3 tasks)

_H14 KV-reader swap, H14a sidecar client wiring, sidecar live verification_

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 160 | ✅ | H14: KV-reader swap (AzCliKvSecretReader -> SecretClient) | FULL | sonnet / high | waveG6-parallel | 125, 153 |
| 161 | ✅ | H14a: sidecar client wiring (ExchangePolicySidecarClient : IExchangePolicyApplier) + envelope round-trip contract test | FULL | opus / high | none | 114, 160 |
| 162 | 🟡 | Sidecar live verification against dev L2 Worker App Service (localhost sitecontainer binding + per-boot shared-secret header) — authoring-complete, live-ceremony-pending; harness: [`Verify-Sidecar-Live.ps1`](../../scripts/provisioning/Verify-Sidecar-Live.ps1) + [`ExchangePolicySidecarLiveVerificationTests.cs`](../../src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/ExchangePolicySidecarLiveVerificationTests.cs); runbook: [`notes/sidecar-live-verification-runbook.md`](../notes/sidecar-live-verification-runbook.md); ceremony step 8 | FULL | opus / high | none | 101, 113, 114, 161 |

### Phase C'' Wave G-7 -- Acceptance (17 tasks)

_11 real T1-T6/I1-I5 probes, 3 runner ports, real Ready writer, gate aggregation, real E2E rerun_

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 170 | ✅ | I1: naming/tenant-literal invariant probe (pure C#, no live deps -- easiest first) | FULL | sonnet / high | waveG7-parallel | none |
| 171 | ✅ | T1: keyVaultReferenceIdentity trap probe (pipelined with H2a/H4) | FULL | sonnet / high | waveG7-parallel | 123, 125 |
| 172 | ✅ | T5: slot-MI KV RBAC role-assignment probe (pipelined with H4) | FULL | sonnet / high | waveG7-parallel | 125 |
| 173 | ✅ | I2: AI Search tenant filter probe (pipelined with H2b) | FULL | sonnet / high | waveG7-parallel | 124 |
| 174 | ✅ | I3: Cosmos partition-key probe (pipelined with H2a) + CompositeInvariantVerifier + IInvariantProbe seam (extended by tasks 170/173/179 adapters) | FULL | sonnet / high | waveG7-parallel | 123 |
| 175 | ✅ | T6: SPE confidential-client trap probe (pipelined with H8) | FULL | sonnet / high | waveG7-parallel | 131 |
| 176 | ✅ | I4: SPE container resolver probe (pipelined with H9) -- landed commit `54a348ed8` (H13 I4 SPE container resolver invariant probe); TASK-INDEX drift corrected 2026-08-20 by task 186 alongside Wave G-7 Batch G-7E close | FULL | sonnet / high | waveG7-parallel | 132 |
| 177 | ✅ | T2: Dataverse App User pair probe (pipelined with H10) | FULL | sonnet / high | waveG7-parallel | 143, 111 |
| 178 | ✅ | T3: Graph app-role parity (14) probe (pipelined with H10) | FULL | sonnet / high | waveG7-parallel | 143 |
| 179 | ✅ | I5: Graph token tenant scope probe (pipelined with C5.8 grants) | FULL | sonnet / high | waveG7-parallel | 111 |
| 180 | ✅ | T4: Exchange policy count probe (sidecar read-route, pipelined with H14a) | FULL | sonnet / high | waveG7-parallel | 114, 161, 162 |
| 181 | ✅ | IE2EValidationRunner C# port (replaces Validate-DeployedEnvironment.ps1) | FULL | sonnet / high | none | 132, 141, 142, 173 |
| 182 | ✅ | INamingConformanceChecker pure-C# port | STANDARD | sonnet / high | waveG7-parallel | none |
| 183 | ✅ | ICostEnvelopeChecker ARM.CostManagement port | FULL | sonnet / high | waveG7-parallel | 123 |
| 184 | ✅ | IRegistrySetupStatusUpdater real DV-REST PATCH (Ready writer) -- the acceptance-target transition itself | FULL | sonnet / high | none | 112, 181, 182, 183 |
| 185 | ✅ | H13 gate aggregation wiring -- assemble all 11 probes + 3 runners + Ready writer into final acceptance logic | FULL | opus / high | none | 170, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182, 183, 184 |
| 186 | 🟡 | Real Phase F E2E acceptance rerun (task 089 for real this time) -- **AUTHORING + FRAMEWORK-LEVEL PROOF COMPLETE, LIVE-CEREMONY PENDING** per task 162 precedent (Path C deferral: L2 Worker App Service does not yet exist on Azure; sidecar not pushed to ACR; `Deploy-ControlPlane.ps1` never live-run; `customer.bicep` never live-deployed). Landed: `E2EAcceptanceCompositionRootTests.cs` (21 new tests, all pass; 1481/1 skip/0 fail L2 total, +21 vs Batch G-7D baseline). Report: [`notes/phase-c-double-prime-e2e-acceptance-real-run.md`](../notes/phase-c-double-prime-e2e-acceptance-real-run.md) documents framework-level r1 E2E goal proof (spec.md FR-18 / SC #5) + deferrals + live-ceremony recipe. Cross-referenced from task 089's SPLIT-MODE report. Flip to ✅ once owner completes live ceremony per §11 handoff of the report. **2026-08-26 SESSION 13**: first live batch dispatch attempted, halted pre-Step-0.5 via §6.5 escalation on 3 discoveries (task 023 script never deployed + missing sprk_customerid column + SKILL.md Step 1f/6a drift). Task 199 filed + all 3 fixes executed atomically same session — task 186 is now UNBLOCKED for next-session re-dispatch. | FULL | sonnet / xhigh | none | 185, 113, 162, 199 |
| 199 | ✅ | **SESSION 13 reconciliation** — deploy task 023 schema (12 columns) to spaarkedev1 + add missing `sprk_customerid` (13th column, ALT-KEY per L2 `CustomerRunGuard`) via new `scripts/Add-CustomerIdColumn.ps1` + fix `/provision-environment` SKILL.md Step 1f (drop fictional `sprk_profile`/`sprk_upgrademode`, add 5 required NOT-NULL fields, use enum-int `sprk_setupstatus=1`), Step 6a (`sprk_setupstatus=2` not `200000004`), Fallback Matrix (same corrections + registry-env pointer). Discovered + resolved same session via §6.5 protocol during first live batch dispatch of task 186. Final schema on spaarkedev1: 30 sprk_ columns + `sprk_customerid_key` alt-key. Unblocks task 186. | FULL | sonnet / high | none | 023, 186 |

**Note on task 089 vs task 186**: The original task 089 (below) is recorded SPLIT MODE — its scaffolding (harness + report skeleton + operator runbook) landed, but the actual E2E acceptance run against a genuinely-functional pipeline never happened, because the pipeline was not genuinely functional (per the r1-gap-analysis this Phase C'' build responds to: no dispatcher existed, 11 of 19 handlers shelled out to unavailable tools, several handlers were placeholder-backed). Task 186 is the REAL rerun once Waves G-1..G-7 land; it supersedes 089 as the project's actual acceptance evidence. Do not close out this project on 089 alone.

### Phase H-Prime — Absorb F19/F20 first-live findings into handler catalog (2 tasks — designed 2026-08-24 SESSION 3)

Post-Model-1-Prod-first-live-standup (2026-08-22 through 2026-08-24) revealed CLUSTER FINDING: F17 + F19 + F20 share ONE root cause — Bicep provisions Model 1 Prod infra but does NOT seed BFF runtime state (App Service code + KV secrets + App Service app settings). H9 (task 052) closes the code-deploy gap. These two new handlers close the KV-secret-seed gap (H4-shared) and the App Service app-settings gap (H4b). Together they eliminate the F20 progressive-fail-fast chain observed live (SIGABRT exit 134 on missing `SpeAdmin:KeyVaultUri` → next missing config → next → ~40 IOptions modules deep). Live-fire test bed: Model 1 Prod (`sprksharedprod-api` + `sprk-prod-kv`).

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 200 | ✅ | H4-shared: seed shared KV from source Azure services (extract-from-source pattern; F19 automation; extends task 084 manifest schema with `value_source: from-shared-service` + `service_ref` field; 5-branch SdkSourceServiceKeyExtractor Search/CogSvc/ServiceBus/Storage/Redis) — Phase A `ad32b3a8c` + Phase B+C landing next commit; 1531 tests pass (19 new H4-shared + 32 existing H4 + all others); `-Verify` exit 0 | FULL | opus / xhigh | none (serial after 084; blocks 201) | 036, 044, 084 |
| 201 | ✅ | H4b-BulkAppSettings: apply canonical BFF app-settings in ONE batch → ONE restart (F20/F20a automation; Option A design: extends task 084 manifest with `per_env_settings:` top-level list; thin H4b handler shells to generated Configure-AppServiceSettings.generated.ps1; IHealthzProbe with 30/60/90/120/180s backoff + KuduContainerLogFetcher parses docker-logs on failure for actionable diagnostic; sequences AFTER H4-shared + H4-per-tenant, BEFORE H9) — 1555 tests pass (23 new H4b + all prior); `-Verify` exit 0 with 32 secrets + 8 per_env_settings; BINDING guard OK (BFF-API-ClientSecret + Dataverse-ClientSecret NOT in per_env_settings — remain KV-ref secrets) | FULL | opus / xhigh | none (serial after 200) | 036, 044, 084, 200 |

### Phase Pre-Live-Fire — Consolidate lessons + structural gaps before E2E (18 tasks — designed 2026-08-24 SESSION 5, sub-phased SESSION 7, extended SESSION 10 with 205 auth-v4 integration)

Owner directive 2026-08-24: BEFORE invoking task 186 E2E ceremony, audit + apply all captured-but-unapplied lessons so live-fire proves the platform's automation rather than iterating through fail-cycles. SESSION 5 attempted manual BFF boot (5 config gates cleared + IActionSeam code fix `e3a15db91`) — owner recognized this was symptom-fixing not platform validation. Corrective sequence: 202 audits + designs + produces punch list → 203 applies Class-A rows (4 sub-phases 203a/b/c/d) + 204 applies Class-B verified-open rows (7 sub-phases 204a-g) + **205 integrates auth-v4 runtime-contract deltas (6 sub-phases 205a-f)** → 186 fires cleanly. Also: formalize PROVISIONING-PREREQUISITES.md as codified reference (owner directive: "documented somewhere that can then feed into the process — formalized, file/app/table") + design provisioning-runs/{customerId}-{runId}/ project structure mirroring coding-project pattern (owner directive: "shouldn't there be a project structure within which a new environment provisioning project runs?").

**SESSION 7 amendment (2026-08-24)**: `code-quality-and-assurance-r3` (original Class-B routing target) CLOSED 2026-08-16/17/20 (35/35 tasks + follow-on merged; worktree repurposed to `work/dataverse-access-hardening`). Class-B verified-open rows absorbed into task 204 in THIS project. Also 8 of 22 Class-B rows verified as ALREADY APPLIED by Wave G-7/G-8 (B03/B05/B06/B08/B09/B12/B13/B19) — task 204 real scope is 14 rows, not 22. See `notes/task-202-punch-list.md` §Class-B verification matrix (2026-08-24 SESSION 7 grep-verify) for full row-by-row status.

**SESSION 10 amendment (2026-08-25)**: Task **205 sub-phase authored** — 6 POMLs (205a–f) covering auth-v4 §10 addendum Δ1-Δ5 + §10.5 traps + §10 DELIVERED consumption, per SESSION 9 Fable deep-review deliverables (`notes/auth-v4-integration-draft-punch-rows.md` + `notes/auth-v4-integration-remediation-plan.md` + `notes/decisions/adr-028-a4-integration-conflict-resolution.md` APPROVED owner 2026-08-25 + `notes/auth-v4-integration-open-questions.md` 11-of-11 RESOLVED). All 6 POMLs authored by 6 parallel background agents (workflow `wf_5f003120-ab3`, ~5min wall-clock, 802K tokens); shape matches 203c template. Critical path to 186: A38 ∥ A42 → A39 = 11h serial; A41/A43/A44 parallel-lanes. See `notes/auth-v4-integration-draft-punch-rows.md` §Sequencing note for full dispatch model.

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 202 | ✅ | Pre-live-fire lessons audit + PROVISIONING-PREREQUISITES formalization + provisioning-run structure design — LANDED 2026-08-24 SESSION 6. Outputs shipped: [`docs/guides/PROVISIONING-PREREQUISITES.md`](../../../docs/guides/PROVISIONING-PREREQUISITES.md) + [`scripts/provisioning-prereqs/prereqs.yaml`](../../../scripts/provisioning-prereqs/prereqs.yaml) (27 prereqs across 4 scopes) + [`notes/provisioning-run-structure-design.md`](../notes/provisioning-run-structure-design.md) + [`notes/provisioning-run-agent-autonomy-design.md`](../notes/provisioning-run-agent-autonomy-design.md) + [`notes/task-202-punch-list.md`](../notes/task-202-punch-list.md) **(62 rows: 34 class-A + 22 class-B + 6 class-C; 41 blocks_e2e=yes; amended SESSION 7 with class-B verification matrix)** + [`.claude/patterns/provisioning/`](../../../.claude/patterns/provisioning/) 9 skeleton pattern files + task 186 pre-check trigger. Rigor OVERRIDDEN UP to FULL (POML STANDARD → FULL per hot-path tags + 11 steps + SESSION 5 explicit override). IActionSeam commit `e3a15db91` case study: KEEP-IN-PLACE + file class-B ArchTest follow-on (now absorbed into 204e). | FULL | opus / high | none (parallel-safe:false; touches `.claude/patterns/provisioning/`) | 200, 201 |
| 203a | ✅ (2026-08-25) | Apply Class-A punch list — sub-phase foundation: 9 rows resolved (7 applied + 2 already-applied). A05 provisioning-runs root; A06 8 per-run templates; A07 9 pattern files filled (108-145 lines each); A08 constraints/provisioning.md + task-execute tag-map wired; A09/A10/A11 SKILL.md Step 1 (profile enum + environmentId + placeholder create); A12 verified already-applied (platform-controlplane.bicep:246); A24 verified already-applied (app-service.bicep:82). Build sanity: ControlPlane.Core succeeded 0/0. Effort actual ~3h vs 15h estimate (verify-first saved ~2h on A12+A24). See punch list §203a EXECUTION RESULTS. | FULL | sonnet / high | none (parallel-safe:false; touches `.claude/**`) | 202 |
| 203b | 🔲 (blocked by 202) | Apply Class-A punch list — sub-phase bicep hardening: A13/A14/A17/A18/A19/A20/A21/A22/A23/A25/A26/A27 (SB Data Receiver RBAC + config-key aliases + artifacts storage + ACR + L2 UAMI RBAC ×6 + FromBicepOutput wire-up + kv-secrets clobber fix + Model 1 sharedBffUami KV grant + queue recreate ceremony + CustomerRunGuard config). Row-by-row grep-verify BEFORE apply. Pure bicep + scripts. ~30-40h. | FULL | sonnet / high | groupPreLive-parallel (with 203c/203d parallel-safe:true) | 202 |
| 203c | ✅ **(2026-08-26 SESSION 12)** | Apply Class-A punch list — sub-phase skill wiring: A02/A03/A04/A15/A16. Grep-verify SESSION 12 discovered A15 + A16 ALREADY APPLIED (task 111 authored `scripts/provisioning/Grant-ControlPlaneIdentity.ps1`; task 005 + 144 populated all 15 GraphAppRoles GUIDs — POML path was stale). Applied A02 (SKILL.md Step 0.5 external-prereqs iteration at line 174 — dynamic YAML parse via `powershell-yaml`, scope filter, HARD STOP), A03 (SKILL.md Step 1.0 batch mode + new `scripts/provisioning-prereqs/intake.schema.json` JSON Schema Draft 2020-12 + conditional `tenancyModel × profile` invariant + 2 examples), A04 (SKILL.md Step 7 postmortem at line 909 — MANDATORY, consumes 203a template). Actual effort ~2h vs 15-20h estimate (A15+A16 saved ~11h; no BFF publish-size verify needed since A16 was no-op). | FULL | sonnet / xhigh | landed | 202 |
| 203d | 🔲 (blocked-post-186) | Apply Class-A punch list — sub-phase nice-to-have (POST-186): A32/A33/A34 (skill Step 6 read-verify + h9-workflow cadence runbook + SC #11 env-var checks). Gate: `deferred-post-186` per punch list §Sequencing. ~5h. | STANDARD | sonnet / high | none (parallel-safe:false; touches `.claude/**`) | 202, 186 |
| 204a | 🔲 (blocked by 202) | Class-B verify-first rows: B10/B14/B16/B18/B20/B22 (H6/H7 KV cred binding + ManagedIdentityCredentialFactory TenantId gap + H2b retired-lineage reject + EnvVarValuesOptions KeyVaultSecretRef refactor + Worker Program.cs comments + 429 wireup at ~30 AI endpoints). Executor's primary work is grep-verify per row THEN apply-or-annotate-already-applied. ~15-25h (MAY reduce after verify). | FULL | sonnet / high | groupPreLive-parallel (with 203b/204e/204f/204g parallel-safe:true) | 202 |
| 204b | ✅ **(2026-08-26 SESSION 12 Path A applied, Opus)** | Class-B B04 multi-tenant Dataverse routing — ADR tension resolution per CLAUDE.md §6.5 three-path protocol. Owner Q1 SESSION 11 (2026-08-26) pre-resolved as **Path A** (Model 1 uses ONE shared Dataverse env per shared BFF app-reg per env; `DataverseServiceClientImpl.cs:62` single-URL shape is correct-by-design; Model 2 preserves ADR-027 strict per-customer sub). SESSION 12 wave applied Path A: new §ADR Tensions row in `spec.md` (ADR-027 + ADR-028) + new `design.md` §17 Placement Justification bullet + `notes/task-202-punch-list.md` B04 annotated. NO code change to `DataverseServiceClientImpl.cs`. Actual effort ~1h vs 1-40h path-dependent estimate. | FULL | opus / high | landed | 202 |
| 204c | ✅ **(2026-08-26 SESSION 12 — 9 already-applied + I4 REPLACE)** | Class-B B07 H13 real probes (10 sub-tasks). SESSION 12 wave grep-verify revealed **9 of 10 ALREADY APPLIED** by prior Wave G tasks (task 171 T1 KeyVaultReferenceIdentity + task 172 T5 SlotMIKvRbac + task 173 I2 AiSearchTenantFilter + task 174 I3 CosmosPartitionKey + task 175 T6 SpeConfidentialClient + task 177 T2 DataverseAppUserPair + task 178 T3 GraphAppRoleParity + task 179 I5 GraphTokenTenantScope + task 180 T4 ExchangePolicyCount) + composite wiring by task 185. Only I4 authored new code: **owner-directed REPLACE 2026-08-26** — retired task 176's `SpeContainerResolverInvariantProbe` (BFF-diagnostic trust-me pattern; retained on disk with Wave G-6 retirement banner) + registered new `SpeContainerTenantDerivationInvariantProbe` (INDEPENDENT ARM app-settings direct re-verification per 204c dispatch principle; 651 LOC + 627 LOC tests, 24/24 pass). Composition-root test I4 kind mapping updated (`CR9_InvariantProbe_ForEachKind_ResolvesToExpectedConcreteType`). Tests 148/148 pass (E2EAcceptance + SpeContainer + ApiHostShadowWorker filter). Zero tenant-isolation-invariant real bugs surfaced. Punch-list B07 stale OPEN markers annotated. Actual effort ~2h (I4 swap + verification) vs 40-80h estimate. | FULL | sonnet / high | landed | 202, 204b |
| 204d | ✅ **(2026-08-26 SESSION 12 — Path SPLIT already done + regression guard added, Opus)** | Class-B B11 staging-slot topology split. SESSION 12 wave grep-verify: **Path SPLIT ALREADY DONE by Wave G-1 tasks 100/101/102 (2026-08-19)** — 4 csproj (`.Api`/`.Core`/`.Worker`/`.Tests`); `.Api/Program.cs` registers zero AddHostedService + zero keyed IProvisioningHandler; `.Worker/Program.cs` owns all 21 handlers + StateReconcilerService + CrashRecoveryStartupService + ProvisioningHandlerDispatcher; `controlplane-worker-app-service.bicep` declares Worker as SLOTLESS. Defect structurally closed. Path FLAGS rejected per §11 minimalism. Wave added `ApiHostShadowWorkerGuardTests.cs` (191 LOC regression guard — asserts `.Api` never registers hosted services) + `notes/task-204d-path-decision.md` + design.md §4.2a.1. Quality gates (code-review + adr-check) PASSED (0 Critical / 0 Warning / 1 non-blocking Suggestion). Actual effort ~2h vs 16h estimate. Owner directive SESSION 11 authorized PRE-186 execution (overriding `deferred-post-186` gate). | FULL | opus / high | landed | 202 |
| 204e | 🔲 (blocked by 202) | Class-B regression-prevention ArchTests + IOptions checklist: B01 asymmetric-registration ArchTest (`Spaarke.ArchTests.ADR032.AsymmetricRegistrationTests.UnconditionalConsumerMustHaveUnconditionalDependency` — IActionSeam case-study prevention) + B02 IOptions inventory-drift nightly ArchTest + B15 Tier-1-IOptions deploy checklist in `.claude/constraints/bff-extensions.md`. ~11h. | FULL | sonnet / xhigh | none (parallel-safe:false; B15 touches `.claude/**`) | 202 |
| 204f | 🔲 (blocked by 202) | Class-B B17 docs-drift fix: remove `PLAYBOOK_EMBEDDINGS_INDEX_NAME` from `src/server/api/Sprk.Bff.Api/appsettings.tokens.md:29,114` (zero code consumers post task 035; playbook-embeddings index retired). ~2h. | MINIMAL | sonnet / high | groupPreLive-parallel (with 203b/204a/204e/204g parallel-safe:true) | 202 |
| 204g | 🔲 (blocked by 202) | Class-B B21 spec amendment: SC #2 currently says grep-verify should return zero matches for retired shell-out classes, but Wave G-6 accepted "retired-on-disk with banner" as design convention (preserves audit trail). Amend SC #2 in `projects/customer-provisioning-orchestration-r1/spec.md` + update verification recipe. ~2h. | MINIMAL | sonnet / high | groupPreLive-parallel (with 203b/204a/204e/204f parallel-safe:true) | 202 |
| 205a | ✅ **(2026-08-25 A38a manifest half applied, Fable/xhigh; bundle-committed with 205g)** | **Auth-v4 integration — row A38a (revised from A38 post-escalation 2026-08-25)**: A38 originally fired `partial-omit-set-discovered` at grep-verify Step 2 (5 upsert sites verified; owner APPROVED full split). Old POML `205a-a38-h4-kv-manifest-secret-free-omit.poml` SUPERSEDED (git-rm'd) → revised POML at `205a-a38a-*.poml`. A38a manifest half APPLIED via `FileKvSecretManifest` served-entry filter DOWNSTREAM of BINDING invariant (:151→:219; behavior byte-equivalent); `SecretFreeIdentityOmitTargets` public set; H4 + H4-shared handlers extend existing task-126 `ficOmitSecretNames`/`OmitCanonicalNames` seam (union at :390) + marker step; new `ISecretFreeMarkerApplier` + `ArmSecretFreeMarkerApplier` (KV tag + `sprk_credentialmode` state field, ADR-032 P2 Null-Object) + `SecretFreeMarkerConsistencyDetector` (§5.3 Model-2 fleet-consistency). 25 new tests. Also fired `site-inventory-drifted` (6th site: `Setup-OfficeServiceBus.ps1:172`) → owner disposition folded into 205h A38c. Full suite: 1646 pass / 0 fail / 1 skipped. | FULL | fable / xhigh | landed | 202, 203a, 205g |
| 205b | ✅ **(2026-08-25 path (b) contract-parity, Fable/xhigh)** | **Auth-v4 integration — row A42** (`-FicOnly` consume/reconcile FR-C4): applied via path (b) per owner Q5 + spec.md:279 Option-D no-shell-out MUST NOT. `Assert-SpaarkeFicTenancy` PORTED → `GraphAppRegistrationProvisioner.AssertFicTenancy` + new `CrossTenantFicRefusedException` + rejection code `appreg-cross-tenant-fic-refused` (SF-5 closed; unconditional refusal = PS parity; inert under reading (a)). Triple-keyed idempotency (`FindEquivalentByTriple` any-name + exactly-one-audience) replaces name-first check incl. re-GET verify (SF-7 closed; fixed latent task-130 bug). New `FicExchangeOutcomeClassifier` ports 70025/70021 exact-numeric-match retry + 700211/700213/7000215 fail-fast + authorization-layer acceptance + 5s-doubling-cap-30/600s-budget (SF-6 closed). Exit codes typed as `FicVerificationState` enum (0/1/2); every L2 creation = exit-2 equivalent → `InterStepState.FicPendingPostAppServiceVerification=true` (SF-8 closed; discharged by H13/T4). **26 new tests** (A42a-g); task-130 I6 tests UNMODIFIED + green (23/23, `ProvisionCallCount==0` preserved + stronger A42e guard); full suite 1581 pass / 0 fail / 1 skipped. §11 invariants 1+2 first-exercised at unit level (700213 wrong-subject named; 8-failure/~130s-order flap retried on virtual clock); LIVE exercise still owed to task 186. **Written contract**: `notes/decisions/205b-a42-fic-parity-contract.md`. 0 escalations; Step 9.5 code-review W-1+W-2 fixed; adr-check 0 violations. NFR-01 N/A (no BFF touch). | FULL | fable / xhigh | landed | 202, 203a |
| 205c | ✅ **(2026-08-26 A39 H4b per_env_settings applied, Sonnet/xhigh, FULL rigor)** | **Auth-v4 integration — row A39** (H4b `per_env_settings` — 8 §10.2 live-contract entries): 7 of 8 entries ADDED to `manifest.yaml` (entry 3, `ManagedIdentity__ClientId`, already existed from task 201 — SF-2 note added only); generator `-Verify` exit 0. FIC-flap tolerance = option (b) — H4b's EXISTING `HttpHealthzProbe` 480s backoff budget (no new BFF-side retry code); rationale documented in manifest.yaml + `H4bBulkAppSettingsHandler.cs`. **Material collateral fix**: `FilePerEnvSettingsManifest.cs`'s YamlDotNet naming convention never bound `iOptionsModule` — `ReadAsync()` had returned `Failure` for ALL 15 per_env_settings entries (not just A39's 7) since task 201, undetected because no prior test exercised the real embedded manifest; fixed via `[YamlMember(Alias, ApplyNamingConventions=false)]` + new `FilePerEnvSettingsManifestTests.cs` (10 tests) proving the real manifest now parses. New `HttpHealthzProbeTests.cs` (3 tests, hand-rolled `HttpMessageHandler` not `Mock<>`) + 5 new `H4bBulkAppSettingsHandlerTests.cs` cases (AC-15..19). **Deviation**: AC-8's live-Azure "fresh Model 2 stamp + credential-level signal" test NOT added — `IE2ETrapVerifier`/H13 (natural home) were concurrently modified by 205d/task-185-h13-aggregation in this shared worktree; substituted L2-layer integration-style tests; live-Azure verification remains task 186's obligation (A36/A37 precedent). Build 0/0 (Tests+BFF). `dotnet test` 1668/0-fail/1-skip. BFF publish 45.07 MB vs 44.96 MB baseline (+0.11 MB). code-review 2 findings (1 blast-radius disclosure, 1 low-sev suggestion); adr-check 0 violations. Full record: `notes/task-202-punch-list.md` § "205c EXECUTION RESULTS". No commit (main session handles). | FULL | sonnet / xhigh | none (parallel-safe:false; ORDERING GUARD dep on 205a+205b) | 202, 203a, 205a, 205b |
| 205d | ✅ **(2026-08-26 A41 H10 dual-row + Q8 extension applied, Sonnet/high, FULL rigor)** | **Auth-v4 integration — row A41** (H10 dual DV app-user + Q8 extension): dedupe found task 053 (`Wave-3E-053-H10-AppUser`) already created BOTH systemuser rows, but neither it nor `ds8-uami-dv-appuser` (a design study, silent on the trap) closed the §10.4 objectid trap — NO scope-collapse. `DataverseAppUserCreationRequest` + `H10DataverseAppUserGraphParityHandler` now write `azureactivedirectoryobjectid` EXPLICITLY for the UAMI row (= `InterStepState.MiObjectId`, never `MiClientId`). `IDataverseAppUserVerifier.Verified` + `TrapVerificationRequest` (additive optional fields) + `DataverseAppUserPairT2Probe` now byte-compare the UAMI row's observed `azureactivedirectoryobjectid` against the expected principalId — PASS distinguishes "byte-equality verified" from backward-compatible "count only — DEGRADED"; mismatch (incl. the literal clientId-in-objectid trap) fails loud naming §10.4. **Q8 SCOPE EXTENSION applied**: design.md:61 D3 — replaced "Managed Identity" per-customer wording with explicit shared-`sprk-{env}-shared-bff-uami` clause (verified against `model1-shared.bicep:116-117`); design.md:153 — cites `GraphAppRoles.cs` as source of truth, corrected to its ACTUAL state (all 15 GUIDs populated, not "10/14 null"); design.md:~658 Naming Standards — added Model 1 UAMI row alongside relabeled Model 2 row. Runbook `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §12.7 added (dual-row + trap-recognition + PATCH recovery). Tests: H10 handler AC-1 extended + T2 probe +4 cases (T12); targeted run 79/79 pass; full suite 1658/10-fail(pre-existing, unrelated 205c/A39-in-flight files)/1-skip; `dotnet build` BFF + Control-Plane both 0/0. Full execution record: `notes/auth-v4-integration-draft-punch-rows.md` § "A41 execution record". Effort ~3h vs 3.5h estimate (no scope-collapse discount — trap code-edit was the substantive work). No commit (main session handles). | FULL | sonnet / high | groupAuthV4-parallel (with 205e; 205f main-session-only) | 202, 203a |
| 205e | ✅ **(2026-08-26 A43 Deploy-AllIndexes silent-fallback gate applied, Sonnet/high, FULL rigor)** | **Auth-v4 integration — row A43** (`Deploy-AllIndexes.ps1` silent-fallback gate — §10.5 trap 2): NEW `Resolve-AiSearchAuthContext` function replaces the old `:340-366` admin-key resolution block with a 3-branch gate: (1) secret-free marker present + KV secret missing → `Write-Error` naming marker + linking `.claude/constraints/auth.md` + punch row A38a, `exit 10`, `az search admin-key show` never reached; (2) `AiSearch__ManagedIdentity__Enabled=true` → AAD Bearer token via `Get-AzAccessToken -ResourceUrl "https://search.azure.com/"` (Az.Accounts 5.3.0 primary; `az account get-access-token` fallback) with SecureString-safe `.Token` unwrap; (3) neither signal → pre-existing KV-then-live-admin-key fallback byte-identical. Reuses shared `scripts/common/Assert-SpaarkeSecretFreeGate.ps1` → `Test-SpaarkeSecretFreeMarker` dot-source (single source of truth per §11 — no 4th independent copy). A38 tag scheme FINALIZED (`spaarke-secret-free-identity=true`, no TODO). Existing `:617-660` `-CutoverBffSettings` gate untouched (line-shifted only). Deviation: exit 10 (branch 1) + exit 11 (branch 2 token-fail) instead of illustrative "exit 6" (would collide 3 meanings). NEW tests: `tests/scripts/Deploy-AllIndexes.Tests.ps1` (17/17 passing — 13 unit + 4 idempotency-integration). Script parse-check `pwsh -DryRun` exit 0. Full record: `notes/auth-v4-integration-draft-punch-rows.md` § A43 landing annotation. No commit (main session handles). | FULL | sonnet / high | groupAuthV4-parallel (with 205d; 205f main-session-only) | 202, 203a |
| 205f | ✅ **(2026-08-26 A44 §6.5 EDIT package + companion sweep + doc sweep applied, Sonnet/high, FULL rigor, MAIN-SESSION-ONLY)** | **Auth-v4 integration — row A44**: **(a) DROPPED — scope-drift** (owner-disposed Path C via `AskUserQuestion`) — live-code verified H4b + H9 do NOT iterate `appsettings.template.json` (H4b iterates `IPerEnvSettingsManifest`; H9 does Kudu zip-deploy of CI artifact; only `NamingConformanceChecker` reads the template file for name-conformance R1/R2/R3 scan). Actual §10.5 trap 1 mitigation lives at auth-v4 obligation 051-E (template file — deferred 2026-11-23) + 205c A39's `ServiceBus__FullyQualifiedNamespace` runtime setting (landed this wave). **(b) §6.5 EDIT package APPLIED VERBATIM** per decision doc 2026-08-25 (Q3 APPROVED + Q7 owner narrowing to prong 3 folded in): EDIT 1 `.claude/constraints/provisioning.md:27-36` (4-prong §KV credential lifecycle); EDIT 2 root `CLAUDE.md` §17 fragment; EDIT 3+4 `spec.md:259+:275`. Companion sweep applied to 6 of 7 sites (Site F `azure-deploy/SKILL.md:100` was already cured — verified in place): provision-environment SKILL.md :63+:1068; manifest-driven-secret-catalog.md :27+:48; SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md :391; manifest.yaml Dataverse-ClientSecret + BFF-API-ClientSecret exception_notes; design.md :805+:861+:1233 (annotate-only, no history rewrite). Post-merge cured `oauth-obo-patterns.md` + BFF `CLAUDE.md` grep-verified present (no local edits). **(c) DOC SWEEP applied**: mirror 280 → 642 lines (633-line canonical + 9-line header, 2-step atomic replace-then-delete `copy.md`); rotation-cadence line at `.claude/constraints/provisioning.md:149` (post-EDIT 1 shift) annotated retired-for-BFF-identity + retained-for-E-1; SF-1 UAMI decoy trap fixed at 3 sites (POML said 2; found 3 via grep) — `Grant-GraphAppRoles.ps1:52` + `Grant-ControlPlaneIdentity.ps1:103+:111` — added ARM resource ID PREFERRED + ⚠️ decoy warning naming `spaarke-bff-identity` NOT-BFF's-UAMI. **Generator**: `Invoke-CatalogGenerator.ps1` regenerated + `-Verify` OK (drift on `appsettings.tokens.generated.md` was expected from exception_note edits; re-projected). **Build**: `dotnet build src/server/api/Sprk.Bff.Api/ -c Release` 0/0. **Test suite**: NOT re-run (205c wave verified 1668/0/1 co-green; my edits are docs + metadata + regenerated artifacts). **BFF publish**: NOT re-measured (zero BFF-referenced code touched; wave at +0.11 MB per 205c). **Escalation triggers fired**: sub-scope (a) scope-drift → owner Path C. Full record: `notes/auth-v4-integration-draft-punch-rows.md` § A44 execution record. No commit (main session commits wave atomically). | FULL | sonnet / high | none (parallel-safe:false; touches `.claude/**` + root CLAUDE.md) | 202, 203a, 205a, 205e |
| 205g | ✅ **(2026-08-25 A38b customer.bicep gate applied, Sonnet/high; bundle-committed with 205a)** | **Auth-v4 integration — row A38b (NEW, added 2026-08-25 per owner-approved A38 re-scope)**: `customer.bicep` secret-free gate LANDED. `param requireSecretFreeIdentity bool = false` + `kvSecretValues = union(kvSecretValuesBase, kvSecretValuesGated)` (ternary omits `AiSearch--AdminKey` + `ServiceBus-ConnectionString` when flag true). `customer.json` regenerated. H2a wiring: `BicepDeployRequest.RequireSecretFreeIdentity` field + `H2aBicepInfraDeployHandler` extraction (mirrors `signalrEnabled`); `ArmDeploymentRunner.BuildParametersPayload` emits ARM-wire param. 2 new tests. Build 0/0; 1584/0/1 tests. | FULL | sonnet / high | landed | 202, 203a, 205a |
| 205h | ✅ **(2026-08-25 executed + 2026-08-26 Setup-Office fold-in per owner disposition)** | **Auth-v4 integration — row A38c (NEW, added 2026-08-25 per owner-approved A38 re-scope; extended 2026-08-26 to cover site-inventory-drift discovery)**: operator-script marker pre-check gates via shared `scripts/common/Assert-SpaarkeSecretFreeGate.ps1` helper. Gates (5 call sites across 4 scripts): `Rotate-Secrets.ps1` (SB rotation platform + per-customer), `Seed-ProductionKeyVault.ps1` (SB + admin key seed), `Provision-Customer.ps1` (legacy 13-step SB write), and — per owner disposition post-diagnosis — **`Setup-OfficeServiceBus.ps1:172`** (6th site discovered by 205a peer via `site-inventory-drifted` trigger; azure diagnosis 2026-08-26 confirmed script is 80% dead: SB namespace+queues live via canonical Bicep, Step-5 target App Service `spe-api-dev-67e2xz` deleted, Step-4 KV secret intentionally removed by auth-v4 task 033 — retained with deprecation banner + A38c gate rather than deleted). Detection: KV tag `spaarke-secret-free-identity=true` (primary, per A38a `ArmSecretFreeMarkerApplier`) OR `-CredentialMode` param pass-through. Fail-LOUD shape mirrors A43 `Deploy-AllIndexes.ps1:617-660`. Backwards-compat preserved for pre-migration envs. 26/26 Pester tests pass; Step 9.5 caught + fixed 2 real bugs (JMESPath escape + `Set-StrictMode` scope leak). | FULL | sonnet / high | landed | 202, 203a |
| 205i | ✅ **(2026-08-25 A44.5 H7/L2-Worker credential seam applied, Fable/xhigh)** | **Auth-v4 integration — row A44.5 (NEW, added 2026-08-25 per owner-approved A38 re-scope)**: L2 Worker credential seam LANDED. New `Handlers/Credentials/` seam: `CredentialKind.cs` (enum mirrors BFF names, `KeyVaultCertificate` deliberately absent), `WorkerCredentialSelectionOptions.cs` (FR-39 order + `RequireSecretFreeIdentity`), `WorkerDataverseCredentialFactory.cs` (MI-FIC via `ClientAssertionCredential`; fail-closed exhausted-chain forbids sentinel workaround). H7/H6 handlers chain-aware; `ClientSecret` widened to string?; production credential routed via factory. Bicep: `controlplane-worker-app-service.bicep` `requireSecretFreeIdentity` param + all three `BFF-API-ClientSecret` KV-refs gated TOGETHER (partial gating would re-open A38 trap); `platform-controlplane.json` regenerated. 26 new tests (10 factory + 5 real-Worker boot via WebApplicationFactory + 11 handler-extension). Build 0/0; 1646/0/1 tests. Follow-on: `CustomerRunGuard` Bicep KV-ref gated here; its C# seam (`DataverseRegistryConcurrencyStore.cs:298`) queued for a factory-unification row. | FULL | fable / xhigh | landed | 202, 203a, 205b |

### Phase F — E2E Acceptance (1 task)

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 089 | 🟡 | **[SPLIT MODE, amended 2026-08-18]** Provision fresh `trial-2026-08-18` customer stamp using **Model 2 (dedicated) profile** (Path A exception — swapped from Model 1 primary; Model 1 now discretionary) via new pipeline; verify `Setup Status = Ready` + all 6 traps + all 5 invariants + naming-conformance + cost envelope. Scaffolding half (harness + report skeleton + operator runbook) landed by subagent; owner interactive invocation of `/provision-environment` pending. | FULL | sonnet / xhigh | none (dep ALL) | ALL previous phases |

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
| **Serial (Phase E)** | 080 → 081 → 081.5 → 082 (4 tasks) | none | Existing frozen file modification; sequential dep chain; 081.5 added 2026-08-18 per task 082 pre-verification escalation (RegistrationDataverseService ctor fallback + Azure config pre-gate) |
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
| **Phase F** | 089 (E2E dry run on `trial-2026-08-18` — Model 2 dedicated primary per 2026-08-18 Path A exception; Model 1 discretionary) | ALL previous phases | Final acceptance — SPLIT MODE (scaffolding landed, owner invocation pending) |

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
- **⚠️ Coordination window has closed (found by task 115, 2026-08-19)**: `ci-cd-unit-test-remediation-r1` started 2026-06-25; its 28-day window closed ~2026-07-23. The worktree (`C:/code_files/spaarke-wt-ci-cd-unit-test-remediation-r1`) still exists but its HEAD commit is dated 2026-06-28 — **no activity in ~52 days**. **Three r1 coord-notes are now queued and unapplied against this dormant worktree**: task 067 (`notes/graph-app-role-parity-coord-pr.md`), task 088 (`notes/phase-h-ci-wiring-coord-pr.md`), task 115 (`notes/sidecar-ci-workflow-coord-pr.md`). Recommend an owner decision on `.github/workflows/**` ownership before any further r1 task queues a 4th coord-note against the same target (see task 115 note § 0 for full analysis + options).
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
| **Total tasks** | 152 (78 original + 58 Phase C'' Wave G-1..G-7, added 2026-08-18 + 2 Phase H-Prime, added 2026-08-24 SESSION 3 for F19/F20 automation + 12 Phase Pre-Live-Fire, added 2026-08-24 SESSION 5+7 for pre-186 lessons apply) |
| **not-started** 🔲 | 79 (203a completed 2026-08-25; was 80: 11 original + 58 Phase C'' + 0 Phase H-Prime — tasks 200 + 201 both landed 2026-08-24 SESSION 4; Phase H-Prime CLOSED — + 10 Pre-Live-Fire remaining: 203b/c/d + 204a/c/d/e/f/g; 204b is ⏸ owner-decision-gated so counted separately) |
| **in-progress** 🟡 | 0 |
| **completed** ✅ | 67 (Wave 0: 18; Wave 1: 9; Wave 2: 7; Wave 3: 19; Wave 4A: 081+084; Wave 4B: 052+057+064+077; Wave 4C: 058+065+066+085; Wave 4D: 059+060+061+086) |
| **Phase C'' task decomposition (2026-08-18)** | 58 new POMLs (100-186) authored per DS-4/DS-1b/DS-2/DS-2b/DS-3/DS-5/DS-8 across Waves G-1..G-7 — the execution-engine build phase delivering FR-18/SC#5 E2E provisioning. See dedicated Phase C'' section above. All status not-started; NOT yet executed. |
| **Wave 4 Batch 4D COMPLETE (2026-08-18)** | 4 POML tasks + 3 drift fixes + 1 Path A drift wrap-up landed clean · 059 `3964bba4c` (I5 concurrency guard — 18 new tests, Quarantined reason-code fully wired) · 060 `869b650ab` (I6 crash recovery — 24 new tests, MessageId byte-identity to reconciler) · 061 `22ad121a8` (§4C rollback — Rollback module with 4-class taxonomy + QuarantineClearService) · 086 `fa121e534` (5 Bicep files + platform.json regen, 0 orphans to delete, publish 43.64 MB Δ −1.32 MB) · Drift 1 `ed4cdee42`+`cf6de1d3b` (MI factory TenantId + I5 broadened to Infrastructure/Auth/**) · Drift 2 `55997daf3` (2 prod PS scripts, source only) · Drift 3 `1d204667e` (RecordMatchServiceTests compile fix unblocked BFF verification) · Drift 4 `a70c4bf54` (ArchTest Path A for CustomerRunGuardOptions.ClientSecret) |
| **Wave 4 Batch 4C COMPLETE (2026-08-17)** | 4 parallel subagents landed clean · 058 commit `1b0297c7b` (state-reconciler BackgroundService — 524/524 L2 tests, N=5 dedup verified) · 066 commit `e54cfb6e5` (verify 1834b77bc + regression seed test) · 085 commits `4ab4fbeda`+`06db97468` (AI Search alias collapse — 2 dev KV aliases deleted, health 200 after each step, soft-delete recovery until 2026-11-16) · 065 commit `f66a6add7` (12 baseline violations fixed + 47-site audit report; all 5 §4D ArchTests PASS 22/22 with neg-controls) |
| **Wave 4 Batch 4B COMPLETE (2026-08-17)** | 057 `b8dcdfaeb` · 052 `67e8830ba` · 077 `111773ffc` · 064 `40b09f837` |
| **Wave 4 Batch 4A COMPLETE (2026-08-17)** | ArchTest debt `3b67a7b8d` · 081 `0b8ca53ba` · 084 `70abd9992` |
| **All 5 §4D tenant-isolation ArchTests GREEN (2026-08-17 post-4C)** | I1 (PS scripts), I2 (AI Search tenantId filter), I3 (Cosmos PartitionKey), I4 (SPE literals), I5 (Graph per-tenant token) all pass. Total 65/65 ArchTests suite pass. Zero baseline violations remaining. |
| **Follow-on drift surfaced during 4C (per fix-at-discovery principle)** | **(a) LANDED in Batch 4D drift-1 (see next row)** — `ManagedIdentityCredentialFactory.cs` now sets `options.TenantId` (mirrors task 065 GraphClientFactory:132 fix); I5 ArchTest scope broadened to also scan `Infrastructure/Auth/**`. **(b) LANDED in Batch 4D drift-2 (see next row)** — prod-side `Seed-ProductionKeyVault.ps1` + `Configure-ProductionAppSettings.ps1` fixed. `platform.json` regen still owned by task 086. |
| **Wave 4 Batch 4D drift-1 LANDED (2026-08-17)** | Commit `ed4cdee42`. ManagedIdentityCredentialFactory tenant-scoping + I5 ArchTest broadened to include `Infrastructure/Auth/**`. Follow-on to task 065 audit §7.2. Files: `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/ManagedIdentityCredentialFactory.cs` (add `options.TenantId` from AZURE_TENANT_ID/TENANT_ID) + `tests/Spaarke.ArchTests/TenantIsolation/I5_GraphPerTenantTokenTests.cs` (ScanRelDirs array with 2 roots). Verify: I5 ArchTest 5/5 PASS with broadened scope; full ArchTests 65/65 PASS; publish size 44.96 MB (Δ 0.00 vs task 065 baseline). Report: `notes/wave-4-drift-1-mi-factory-fix.md`. |
| **Wave 4 Batch 4D drift-2 LANDED (2026-08-17)** | Prod-side AI Search alias source fix: `Seed-ProductionKeyVault.ps1` + `Configure-ProductionAppSettings.ps1` now reference canonical `AiSearch--AdminKey` (was alias `ai-search-key`). SOURCE ONLY — no live prod mutation. Report: `notes/wave-4-drift-2-prod-ai-search-alias.md`. Verify: `Grep 'ai-search-key' scripts/` returns 0 hits in prod scripts (remaining hits only in manifest + generator output — task 084 owned). |
| **Wave 4 Batch 4E drift-5 LANDED (2026-08-18)** | Bicep BCP035/BCP037/BCP053 cleanup in `infrastructure/bicep/stacks/model{1,2}-*.bicep` — task 029 refactored `app-service.bicep` to UAMI-only (dropped `keyVaultName`, `enableManagedIdentity`, output `appServicePrincipalId`; added required `userAssignedIdentityResourceId`), but the two stack callers were never migrated (task 029 header explicitly deferred as follow-on). Discovered during Wave 4C task 086 subagent; owner authorized pre-4E cleanup so task 089 E2E dry-run isn't blocked. Fix: each stack now invokes `modules/uami.bicep` for a stable BFF UAMI and repoints 5 downstream `appServicePrincipalId` reads to `<uami>.outputs.principalId` (storage, KV RBAC, membership-topic sender/receiver, `apiPrincipalId` output; model1: only the output). Both `az bicep build` exit 0 (pre-existing dashboard BCP036 + conditional-module BCP318 warnings unchanged). `Invoke-CatalogGenerator.ps1 -Verify` still OK (32 secrets). Report: `notes/wave-4-drift-5-bicep-bcp-cleanup.md`. |
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
