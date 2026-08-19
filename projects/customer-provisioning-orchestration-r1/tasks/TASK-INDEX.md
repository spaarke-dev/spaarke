# TASK-INDEX — customer-provisioning-orchestration-r1

> **Last Updated**: 2026-08-18 (Phase C'' Wave G-1..G-7 task decomposition appended — 58 new POMLs per DS-4/DS-1b/DS-2/DS-2b/DS-3/DS-5/DS-8)
> **Status**: Ready for `task-execute` (Phase A first); Phase C'' (100-186) is the execution-engine build phase delivering FR-18/SC#5 E2E provisioning
> **Task count**: 136 POMLs (78 original across 10 phases + 58 Phase C'' across 7 waves)
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
| 102 | 🔲 | Implement ProvisioningHandlerDispatcher BackgroundService (ServiceBusSessionProcessor) in .Worker | FULL | opus / xhigh | none | 100, 101 |
| 103 | 🔲 | HandlerIds catalog + keyed DI registration for 20 dispatchable handlers (C1.2) | FULL | sonnet / high | none | 100 |
| 104 | 🔲 | Extract IHandlerOutcomeApplier from StateReconcilerService (C2.1 wiring hook) | FULL | sonnet / high | none | 100 |
| 105 | 🔲 | Dispatcher decision + idempotency test suite (DispatchCoreAsync table-driven, DispatchIdempotencyService, envelope round-trip) | FULL | sonnet / high | none | 102 |
| 106 | ✅ | Fix C4.5: dual Newtonsoft StringEnumConverter on RunStatus/GateState/QuarantineState + serializer-contract test + Cosmos seam test | TEST-MODIFYING (unconditional FULL) | sonnet / high | waveG1-parallel | none |
| 107 | ✅ | Add attempt field to ReconcilerEnqueuePayload (L1 dedup vs §4C retry interaction fix) | TEST-MODIFYING (unconditional FULL) | sonnet / high | none | 108 |
| 108 | 🟡 | Bicep: recreate sprk-provisioning-jobs queue with sessions + dedup (C5.4/C4.6) + drain-verify runbook — authoring complete, live delete/recreate deferred to operator (see notes/queue-recreate-runbook-2026-08.md) | FULL | sonnet / high | none | none |
| 109 | 🔲 | Bicep: fix config-key/audience/secret-name source drift (C5.1-C5.3) | FULL | sonnet / high | none | none |
| 110 | 🔲 | Bicep: SB Data Receiver (+Sender) RBAC role assignments for L2 UAMI(s) (C5.5) | FULL | sonnet / high | none | 100, 101 |
| 111 | ✅ | Author Grant-ControlPlaneIdentity.ps1 (Path X Dataverse App User + scoped custom role + C5.8 Graph app-role grants) | FULL | opus / high | waveG1-parallel | none |
| 112 | ✅ | Implement C1.4 L2 Dataverse registry client (MI-native, DefaultAzureCredential) | FULL | opus / high | none | 111 |
| 113 | 🔲 | Author Deploy-ControlPlane.ps1 (L2 repeatable deploy script, C5.9/C1.7) | STANDARD | sonnet / high | none | 100, 101, 108, 109 |
| 114 | ✅ | Build Exchange sidecar image (Dockerfile + pwsh HTTP listener + Set-ExchangeApplicationAccessPolicy.ps1 port) | FULL | opus / high | waveG1-parallel | none |
| 115 | ✅ | GitHub Actions workflow for sidecar build/push (ACR + Trivy gate) -- coordinated with ci-cd-r1; drafted YAML + escalation (closed coord window, stale worktree) in notes/sidecar-ci-workflow-coord-pr.md; NOT committed to .github/workflows/** | STANDARD | sonnet / high | waveG1-parallel | 114 |
| 116 | ✅ | BFF artifact publish workflow extension for H9 (blob store + latest.json manifest) -- committed DIRECTLY to .github/workflows/deploy-bff-api.yml (Path C deviation from "coord-note only" per r1's 2026-08-19 direct-ownership governance; see notes/h9-artifact-publish-ci-coord-pr.md); new .github/workflows/schemas/bff-artifact-manifest.json; storage account escalation documented (not created) | STANDARD | sonnet / high | waveG1-parallel | none |
| 117 | ✅ | CI coordination: Bicep->ARM-JSON pre-compile step for H2a -- NEW workflow `.github/workflows/publish-provisioning-arm-artifacts.yml` committed DIRECTLY (Path C deviation, same 2026-08-19 direct-ownership governance as task 116; see notes/h2a-bicep-precompile-ci-coord-pr.md); new `.github/workflows/schemas/provisioning-arm-manifest.json`; compiles customer.bicep + stacks/model1-shared.bicep (the exact 2 templates per FileBicepTemplateInspector.ResolveTemplatePath) to versioned ARM JSON in the SAME provisioning-artifacts container as task 116; storage account escalation re-flagged (not created, same gap task 116 filed) | STANDARD | sonnet / high | waveG1-parallel | none |
| 118 | 🔲 | Integration seam test: dispatch spine (message -> handler -> Cosmos transition -> DAG advance) | TEST-MODIFYING (unconditional FULL) | sonnet / high | none | 102, 104, 106, 108 |

### Phase C'' Wave G-2 -- Entry + resources (7 tasks)

_H0/H1/H0.5/H2a/H2b/H4 SDK ports incl. H4 real-value-sourcing correctness gate_

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 120 | 🔲 | H0: SDK-port 4 preflight probes (ARM.CognitiveServices TPM, BAP-REST env-rate, ARM.Compute vCPU, KV cert-bootstrap) | FULL | sonnet / high | waveG2-parallel | 102, 103 |
| 121 | 🔲 | H1: real ARM subscription-reachability + Lighthouse probe (replace NullSubscriptionReadinessProbe) | FULL | sonnet / high | waveG2-parallel | 102, 103 |
| 122 | 🔲 | H0.5: DI-swap onto C1.4 registry client (remove NullDataverseEnvironmentRegistryClient) | FULL | sonnet / high | none | 112 |
| 123 | 🔲 | H2a: ARM.Resources deployment port (ArmDeployment.CreateOrUpdateAsync + WhatIfAtSubscriptionScopeAsync) + T1 KV-ref probe port | FULL | sonnet / xhigh | waveG2-parallel | 102, 103, 117 |
| 124 | 🔲 | H2b: SearchIndexClient port + REAL AI Search tenant-filter template provisioner (replace Stub) | FULL | sonnet / high | waveG2-parallel | 102, 103, 123 |
| 125 | 🔲 | H4: SecretClient family port + ARM.AppService KeyVaultReferenceIdentity PATCH (T1) both slots + ARM.Authorization role assignment (T5) | FULL | sonnet / high | none | 102, 103, 123 |
| 126 | 🔲 | H4: real value-sourcing per KvSecretValueSource (generate/copy/reference) + task-084 canonical manifest DI-swap (C2.2) | FULL | sonnet / xhigh | none | 125 |

### Phase C'' Wave G-3 -- Identity + deploy (3 tasks)

_H3 Graph app-reg + real consent verifier, H8 confidential-client, H9 artifact-based rebuild_

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 130 | 🔲 | H3: Graph app-reg port (Applications/ServicePrincipals/AppRoleAssignedTo/Oauth2PermissionGrants) + real consent verifier + KV writes + H10-idiom app-user assign | FULL | sonnet / xhigh | none | 102, 103, 125, 126 |
| 131 | 🔲 | H8: Graph containerTypes port (ClientCertificateCredential, T6 cert from KV) | FULL | sonnet / high | none | 102, 103, 125, 126 |
| 132 | 🔲 | H9: artifact-based rebuild -- handler side (manifest verify + blob download + Kudu zip-deploy + SwapSlotAsync + rollback re-swap) | FULL | sonnet / high | none | 102, 103, 116 |

### Phase C'' Wave G-4 -- Dataverse chain (5 tasks)

_H5/H6/H7 SDK ports + credential config, H10/H11 live verification_

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 140 | 🔲 | H5: BAP-REST env-create + async-operation-polling port | FULL | sonnet / high | waveG4-parallel | 102, 103, 123 |
| 141 | 🔲 | H6: Web-API import port (ImportSolution/StageAndUpgrade + ImportJob polling) + ZIP artifact packaging | FULL | sonnet / xhigh | none | 102, 103, 140 |
| 142 | 🔲 | H7: credential provisioning (EnvVarValues:ClientSecret KV ref) + NFR-05 validation | STANDARD | sonnet / high | waveG4-parallel | 126 |
| 143 | 🔲 | H10: live verification post C5.8 grants (5 REST/Graph seams, code already real) | FULL | sonnet / high | none | 111, 140 |
| 144 | 🔲 | H11: live verification post C5.8 grants (Graph REST + B2B invitation + consent verifier, code already real) | FULL | sonnet / high | none | 111, 143 |

### Phase C'' Wave G-5 -- Seed (4 tasks)

_H12a YamlDotNet engine, H12b DV-REST ports + 2 greenfield seeders (completes FR-16), H12c config_

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 150 | 🔲 | H12a: YamlDotNet manifest engine + DV-REST seed writes | FULL | sonnet / high | waveG5-parallel | 141 |
| 151 | 🔲 | H12b: 2 near-mechanical DV-REST ports (DataGrid + workspace-layout seeders) | STANDARD | sonnet / high | waveG5-parallel | 141 |
| 152 | 🔲 | H12b: 2 greenfield seeders (field-mapping + chart-def) -- completes FR-16 | FULL | sonnet / high | none | 151 |
| 153 | 🔲 | H12c: credential config only (no code delta) | STANDARD | sonnet / high | waveG5-parallel | 123, 150, 151, 152 |

### Phase C'' Wave G-6 -- Integration wiring (3 tasks)

_H14 KV-reader swap, H14a sidecar client wiring, sidecar live verification_

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 160 | 🔲 | H14: KV-reader swap (AzCliKvSecretReader -> SecretClient) | FULL | sonnet / high | waveG6-parallel | 125, 153 |
| 161 | 🔲 | H14a: sidecar client wiring (ExchangePolicySidecarClient : IExchangePolicyApplier) + envelope round-trip contract test | FULL | opus / high | none | 114, 160 |
| 162 | 🔲 | Sidecar live verification against dev L2 Worker App Service (localhost sitecontainer binding + per-boot shared-secret header) | FULL | opus / high | none | 101, 113, 114, 161 |

### Phase C'' Wave G-7 -- Acceptance (17 tasks)

_11 real T1-T6/I1-I5 probes, 3 runner ports, real Ready writer, gate aggregation, real E2E rerun_

| ID | Status | Title | Rigor | Model / Effort | Parallel Group | Deps |
|---|---|---|---|---|---|---|
| 170 | 🔲 | I1: naming/tenant-literal invariant probe (pure C#, no live deps -- easiest first) | FULL | sonnet / high | waveG7-parallel | none |
| 171 | 🔲 | T1: keyVaultReferenceIdentity trap probe (pipelined with H2a/H4) | FULL | sonnet / high | waveG7-parallel | 123, 125 |
| 172 | 🔲 | T5: slot-MI KV RBAC role-assignment probe (pipelined with H4) | FULL | sonnet / high | waveG7-parallel | 125 |
| 173 | 🔲 | I2: AI Search tenant filter probe (pipelined with H2b) | FULL | sonnet / high | waveG7-parallel | 124 |
| 174 | 🔲 | I3: Cosmos partition-key probe (pipelined with H2a) | FULL | sonnet / high | waveG7-parallel | 123 |
| 175 | 🔲 | T6: SPE confidential-client trap probe (pipelined with H8) | FULL | sonnet / high | waveG7-parallel | 131 |
| 176 | 🔲 | I4: SPE container resolver probe (pipelined with H9) | FULL | sonnet / high | waveG7-parallel | 132 |
| 177 | 🔲 | T2: Dataverse App User pair probe (pipelined with H10) | FULL | sonnet / high | waveG7-parallel | 143, 111 |
| 178 | 🔲 | T3: Graph app-role parity (14) probe (pipelined with H10) | FULL | sonnet / high | waveG7-parallel | 143 |
| 179 | 🔲 | I5: Graph token tenant scope probe (pipelined with C5.8 grants) | FULL | sonnet / high | waveG7-parallel | 111 |
| 180 | 🔲 | T4: Exchange policy count probe (sidecar read-route, pipelined with H14a) | FULL | sonnet / high | waveG7-parallel | 114, 161, 162 |
| 181 | 🔲 | IE2EValidationRunner C# port (replaces Validate-DeployedEnvironment.ps1) | FULL | sonnet / high | none | 132, 141, 142, 173 |
| 182 | 🔲 | INamingConformanceChecker pure-C# port | STANDARD | sonnet / high | waveG7-parallel | none |
| 183 | 🔲 | ICostEnvelopeChecker ARM.CostManagement port | FULL | sonnet / high | waveG7-parallel | 123 |
| 184 | 🔲 | IRegistrySetupStatusUpdater real DV-REST PATCH (Ready writer) -- the acceptance-target transition itself | FULL | sonnet / high | none | 112, 181, 182, 183 |
| 185 | 🔲 | H13 gate aggregation wiring -- assemble all 11 probes + 3 runners + Ready writer into final acceptance logic | FULL | opus / high | none | 170, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182, 183, 184 |
| 186 | 🔲 | Real Phase F E2E acceptance rerun (task 089 for real this time) | FULL | sonnet / xhigh | none | 185, 113, 162 |

**Note on task 089 vs task 186**: The original task 089 (below) is recorded SPLIT MODE — its scaffolding (harness + report skeleton + operator runbook) landed, but the actual E2E acceptance run against a genuinely-functional pipeline never happened, because the pipeline was not genuinely functional (per the r1-gap-analysis this Phase C'' build responds to: no dispatcher existed, 11 of 19 handlers shelled out to unavailable tools, several handlers were placeholder-backed). Task 186 is the REAL rerun once Waves G-1..G-7 land; it supersedes 089 as the project's actual acceptance evidence. Do not close out this project on 089 alone.

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
| **Total tasks** | 136 (78 original + 58 Phase C'' Wave G-1..G-7, added 2026-08-18) |
| **not-started** 🔲 | 69 (11 original + 58 Phase C'') |
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
