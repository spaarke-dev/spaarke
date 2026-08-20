# Post-Authoring 5-Reviewer Audit — Wave G-8 kickoff basis

> **Date**: 2026-08-20
> **Trigger**: owner challenged "did Fable model do a full review or is this just from the checkpoint?" — main-session admitted shallow assessment based on checkpoint's own gap-list
> **Method**: 5 parallel Fable-tier reviewers, each independently ground-truthing a slice of the r1 codebase against the E2E goal (FR-18 / SC #5). No reviewer inherited checkpoint claims; every finding cites file:line.
> **Verdict**: authoring is genuinely complete for what was authored (28 of 40 FRs closed, all 19 handlers real, H13 gate has real teeth), **but ≥20 gaps sit between "authoring done" and "E2E provable end-to-end."** Owner directed: commit + push audit, then dispatch fix wave.

---

## Reviewer coverage

| Reviewer | Scope | Verdict |
|---|---|---|
| A (Fable @ high) | Handler H0-H14 runtime completeness (real API calls vs stubs) | Framework mostly complete — 3 specific fixes needed; ALL 19 handlers real |
| B (Fable @ high) | Bicep deploy-readiness (platform-controlplane, customer, all modules) | Not deploy-ready — 9 specific fixes; 2 catastrophic silent-fail defects |
| C (Fable @ high) | Deployment scripts + full prerequisite chain | 5 prerequisites missing from the 11-item ceremony backlog |
| D (Fable @ high) | H13 acceptance gate + Phase F harness integrity | Gate has real teeth (no theatre) — but E2E goal claim overstated |
| E (Fable @ high) | Full FR-01..FR-40 + SC #1..#23 closure matrix | 28 FR Closed / 11 Partial / 1 Open; 9 SC Closed / 14 Partial / 0 Open |

---

## What is genuinely real (verified by A + D)

- All 19 handlers use real SDKs (ARM, Graph 6.5.0, KV, Dataverse Web API, BAP REST, Cosmos, AI Search)
- Zero `NotImplementedException`, zero hardcoded tokens, zero dev-endpoint hardcodes
- All auth is UAMI-pinned `DefaultAzureCredential`, KV-sourced `ClientSecretCredential`, or cert-based `ClientCertificateCredential`
- All 11 H13 probes make live API calls (ARM / Graph / DV-REST / KV / Sidecar HTTP)
- `CompositeInvariantVerifier` + `CompositeTrapVerifier` fail-closed on unwired kinds / null returns / throws (InfraFault → Resumable, never false-green)
- Task 184 Ready-writer integer-enum fix verified in code (`BuildPatchBody` writes `sprk_setupstatus` as JSON number 2, no stale string path)
- `PlaceholderTrapVerifier` / `PlaceholderInvariantVerifier` remain on disk but are **unregistered** in composition root
- `HandlerRegistrationCompletenessTests` guards resolvability (has already caught 2 real DI bugs in H5/H7, since fixed)

---

## 🚨 The 30 gaps — consolidated defect list

Numbered for traceability. Ordered by failure-sequence (each blocks the next in a live run).

### Live-deploy blockers (would block Wave H-3)

| # | Gap | Source | Impact | Fix effort |
|---|---|---|---|---|
| 1 | Fleet SB queue `requiresSession` + `requiresDuplicateDetection` mismatch (Bicep says ON, live dev queue OFF) | B, C | `az deployment sub create` errors OR skip until task 108 recreate ceremony | Small (ceremony script) |
| 2 | L2 UAMI subscription-scope Contributor never granted | B | H2a `ArmDeploymentRunner` 403 | Small (Bicep or script) |
| 3 | L2 UAMI Storage Blob Data Reader on artifacts storage never granted | B | H2a / H9 download 403 | Small (Bicep) |
| 4 | L2 UAMI AcrPull on platform ACR never granted | B | Sidecar pull fails | Small (Bicep) |
| 5 | **Provisioning-artifacts storage account doesn't exist** — no `Microsoft.Storage` Bicep module | A, B, C, E (tasks 116/117) | H2a / H6 / H9 all fail — hard blocker | Medium (new Bicep module) |
| 6 | **Worker Bicep missing Redis app setting + `ASPNETCORE_ENVIRONMENT` override** — `DispatchModule.cs:186-197` fail-fasts | A | Worker crash-loops at boot | Small (Bicep) |
| 7 | **Worker Bicep missing 3 `*Options__ProvisioningArtifactsContainerUri` app settings** (BicepInfraDeployOptions, BffDeployOptions, SolutionImportOptions) | A, B | H2a / H6 / H9 `Validate()` throw at first dispatch | Small (Bicep) |
| 8 | L2's own `keyVaultReferenceIdentity` PATCH unowned — Bicep defers to H4; H4 only targets customer stamps | B, C | All L2 KV refs resolve null → cascading downstream failures | Small (add PATCH step to Deploy-ControlPlane.ps1) |
| 9 | Platform-KV secrets never seeded — fresh envs pass literal `@Microsoft.KeyVault(...)` strings, `Validate()` succeeds (non-empty!) | B, C | Garbage propagates as secret values; T1-family silent fail | Medium (new `Seed-PlatformKeyVault.ps1`) |
| 10 | ACR + sidecar image chain unauthored — NO `Microsoft.ContainerRegistry` anywhere in `infrastructure/**` (sidecar CI header falsely claims task 101 authored it) | B, C | H14a `/apply-policy` 404s against static-site container | Medium (new Bicep module + wiring) |
| 11 | `acrImageTag` / `sidecarAuthType` not surfaced as top-level params on platform-controlplane.bicep | B | Can never deploy real sidecar image | Small (Bicep) |

### customer.bicep silent-fail defects (deploy green, run 403)

| # | Gap | Source | Impact | Fix effort |
|---|---|---|---|---|
| 12 | **customer.bicep UAMI → Cosmos DB data-plane Data Contributor NEVER granted** — `cosmosDb` invoked with `appServicePrincipalId: bffPrincipalId` (default `''`, H2a never passes it) | B | 🔥 BFF 403s on every Cosmos call — deploys green | Small (Bicep param) |
| 13 | customer.bicep UAMI → Storage Blob Data Contributor never granted | B | Storage RBAC missing (mitigated by KV account-key fallback) | Small (Bicep param) |
| 14 | customer.bicep `bffApi` module passes ZERO `appSettings` — Model 2 BFF boots with no config, no `AZURE_CLIENT_ID` UAMI pin | B | H9 health probe 404s post-zip-deploy | Small (Bicep) |
| 15 | **`kv-secrets.generated.bicep` clobbers never-delete secrets on any re-deploy** — comment claims skip-if-exists but code is unconditional ternary | B | 🔥 Violates BINDING never-delete invariant — destroys `BFF-API-ClientSecret` + `Dataverse-ClientSecret` on FR-34 upgrade path | Small-medium (generator fix) |
| 16 | `healthCheckPath: '/health'` default in app-service.bicep — BFF actually maps `/healthz` (staging slot module defaults `/healthz` — asymmetric) | B | Prod site probes 404, instances marked unhealthy | Trivial (Bicep) |

### FR-38 acceptance violation (Path Y residue)

| # | Gap | Source | Impact | Fix effort |
|---|---|---|---|---|
| 20 | **FR-38 Path-Y residue** — `dataverseClientSecretName` param + `Dataverse__ClientSecret` app-settings still emit in `platform-controlplane.bicep:135`, `controlplane-app-service.bicep:153`, `controlplane-worker-app-service.bicep:215` despite Path X migration | E | Acceptance criterion explicitly violated | Small (delete + recompile ARM JSON) |

### Prerequisite chain gaps

| # | Gap | Source | Impact | Fix effort |
|---|---|---|---|---|
| 17 | CI workflow to publish `dataverse-solutions-latest.json` doesn't exist — H6 `DataverseWebApiSolutionImporter.cs:251` refuses local-filesystem fallback | C, E | H6 solution import blocks | Medium (new workflow) |
| 18 | **BFF `/api/diagnostics/tenant-container-resolver` endpoint doesn't exist** — I4 probe queries it but zero code implements it (repo-wide grep confirmed) | D, E | I4 invariant probe parks at InfraFault forever → Ready unreachable live | Small (BFF endpoint) |
| 19 | `Grant-ControlPlaneIdentity.ps1:643` — stale `$LASTEXITCODE` after piped `az` probe | C | Grant errors silently swallowed | Trivial |

### H13 acceptance scope gaps

| # | Gap | Source | Impact | Fix effort |
|---|---|---|---|---|
| 21 | **SC #5 sample-workload checks permanently skipped** in `E2EValidationRunner.cs:304-316` (AI analysis, doc upload+index, layout render, wizard field-map) | D, E | The stated E2E goal's OWN measurement is incomplete | Medium (implement 4 probes) — ✅ **CLOSED 2026-08-20 by Wave G-8 Batch 11**: 4 authenticated live checks implemented inside `E2EValidationRunner` (agent `/api/agent/message` full-workflow; `/api/ai/search/count` capability-diagnostic; `/api/workspace/layouts` full-workflow; `/api/v1/field-mappings/profiles` capability-diagnostic). Bearer via shared UAMI TokenCredential scope `{bff}/.default` (I4-probe parity); 60s per-check timeout + 1 transient retry; 404/401/403/token-failure → explicit reason-suffixed ChecksSkipped (not false Pass, not spurious Fail). Static skip list shrank 7→3. |
| 22 | SC #11 dev-leakage + env-var presence checks skipped (L2 lacks Dataverse identity on H13 envelope) | D, E | Env-var closure unmeasured | Medium |

### Requirement completions

| # | Gap | Source | Impact | Fix effort |
|---|---|---|---|---|
| 23 | **FR-40 (I6 OBO app-reg derivation ArchTest)** — no task, no test, no mention in `tests/Spaarke.ArchTests/TenantIsolation/` (I1–I5 only) | E | Only fully Open FR | Small (author test) |
| 24 | FR-34 H0 upgrade-mode clause — version-compat matrix query unimplemented (zero code refs to matrix doc) | E | Upgrade path incomplete | Medium |
| 25 | SC #9 fleet visibility — no MDA view evidence for in-flight-runs | E | SC #9 unmeasured | Small |

### Deletion + doc drift

| # | Gap | Source | Impact | Fix effort |
|---|---|---|---|---|
| 26 | `ArmDeploymentRunner.cs` header "BLOCKING DISCOVERY" comment stale — customer.bicep DOES have UAMI/AppService/OpenAI now | B | Cosmetic misdirection | Trivial |
| 27 | `Worker Program.cs:784-795` comments claim placeholders in use — actually unregistered | A | Cosmetic misdirection | Trivial |
| 28 | `customer-template.bicepparam` hardcodes `platformKeyVaultName = 'sprk-platform-prod-kv'` — contradicts canonical `sprk-{env}-kv` | B | Small deployment risk if used | Trivial |
| 29 | `notes/sidecar-live-verification-runbook.md` prereqs 4/5 credit "H4 / task 125-126" for platform-KV seed + L2 kvRefIdentity PATCH — H4 never runs against L2 stamp | C | Prereqs currently have NO owner | Small (runbook edit) |
| 30 | ~25 retired shell-out classes remain on disk in `.Core` (`AzCli*`, `*ScriptRunner`, `PacAdmin*`, `PowerShellAppConfigSeeder`) — SC #2 literal grep-verify fails | E | Compensating proof exists (composition-root tests); cleanup pending | Small (deletion sweep) |

---

## Wave G-8 dispatch plan

12 parallel agents, batched by file boundary to avoid git conflicts.

| Batch | Agent scope | Fixes | Files touched | Model |
|---|---|---|---|---|
| 1 | customer.bicep amendments (Cosmos RBAC + Storage RBAC + bffApi appSettings + kv-secrets skip-if-exists + healthCheckPath default) | #12, #13, #14, #15, #16 | `customer.bicep`, `modules/kv-secrets.generated.bicep`, `modules/app-service.bicep` | Fable @ high |
| 2 | platform-controlplane consolidated (new artifacts-storage module + new ACR module + role assignments + FR-38 Path-Y deletion + top-level ACR params) | #2, #4, #5, #10, #11, #20 | `platform-controlplane.bicep`, `modules/controlplane-artifacts-storage.bicep` (NEW), `modules/controlplane-acr.bicep` (NEW), `controlplane-app-service.bicep`, `controlplane-worker-app-service.bicep` (Path Y removal only) | Fable @ high |
| 3 | Worker Bicep boot-blocker fixes (Redis + ASPNETCORE_ENVIRONMENT + 3 artifacts URI settings) | #6, #7 | `controlplane-worker-app-service.bicep` | Fable @ high |
| 4 | Scripts — Deploy-ControlPlane.ps1 L2 kvRefIdentity PATCH + new Seed-PlatformKeyVault.ps1 | #8, #9 | `scripts/provisioning/Deploy-ControlPlane.ps1`, `scripts/provisioning/Seed-PlatformKeyVault.ps1` (NEW) | Sonnet 5 @ high |
| 5 | Grant-ControlPlaneIdentity.ps1 `$LASTEXITCODE` fix | #19 | `scripts/provisioning/Grant-ControlPlaneIdentity.ps1` | Sonnet 5 @ high |
| 6 | BFF diagnostic endpoint `/api/diagnostics/tenant-container-resolver` | #18 | `src/server/api/Sprk.Bff.Api/Endpoints/Diagnostics/*.cs` (NEW), Program.cs registration | Fable @ high |
| 7 | CI workflow — new `.github/workflows/publish-dataverse-solutions-manifest.yml` | #17 | `.github/workflows/publish-dataverse-solutions-manifest.yml` (NEW) | Sonnet 5 @ high |
| 8 | Doc drift sweep + retired shell-out class deletion | #25, #26, #27, #28, #29, #30 | Multiple `.cs` header comments, `.bicepparam`, `.md`, deletion of ~25 files | Sonnet 5 @ high |
| 9 | FR-40 I6 ArchTest — new test class | #23 | `tests/Spaarke.ArchTests/TenantIsolation/I6_ObAppRegDerivationTests.cs` (NEW) | Fable @ high |
| 10 | FR-34 H0 upgrade-mode version-compat matrix query | #24 | `Handlers/Preflight/H0PreflightHandler.cs` + new probe class + tests | Fable @ high |
| 11 | SC #5 4 sample-workload checks in E2EValidationRunner | #21 | `Handlers/E2EAcceptance/E2EValidationRunner.cs` + new probe classes + tests | Fable @ high |
| 12 | SC #9 MDA view + spec text fixes (SC #12 Model 2 Redis alignment + add SC rows for FR-24/34/39/40 + SC count 22 vs 23) | #25, spec fixes | Dataverse view XML + `projects/customer-provisioning-orchestration-r1/spec.md` | Sonnet 5 @ high |

**Concurrency**: r1 project caps parallel task-execute at 6. Batches 1-6 dispatch first; on completion 7-12 dispatch.

**Verification gate before Wave H-3**: main session runs `dotnet build src/server/api/Sprk.Bff.Api/` + Bicep `what-if` locally + inspects diff.

---

## Wave H-3 dependencies (owner-in-the-loop, sequenced)

Only executable **after Wave G-8 lands green**. Order:

1. Recreate `sprk-provisioning-jobs` SB queue (delete + `az deployment sub create`)
2. Deploy `platform-controlplane.bicep` (with G-8 Batch 2 + 3 fixes → live)
3. Run L2 `keyVaultReferenceIdentity` PATCH via new Deploy-ControlPlane.ps1 step (G-8 Batch 4)
4. Run new `Seed-PlatformKeyVault.ps1` for 4 KV secrets (G-8 Batch 4)
5. Build + push sidecar to ACR via existing `build-provisioning-sidecar.yml`
6. Set `PROVISIONING_ARTIFACTS_STORAGE_ACCOUNT` + `SIDECAR_ACR_LOGIN_SERVER` GitHub repo vars
7. Trigger CI publish of `dataverse-solutions-latest.json` (G-8 Batch 7)
8. Run `Grant-ControlPlaneIdentity.ps1` (with G-8 Batch 5 fix)
9. Run `Deploy-ControlPlane.ps1` (with `-Swap`)
10. Verify `/healthz` 200 + sidecar `/policies` 200
11. Flip TASK-INDEX 108/110/113 🟡 → ✅ + commit

## Wave H-4 (owner-in-the-loop, first live E2E)

1. Deploy `customer.bicep` trial stamp (with G-8 Batch 1 UAMI role fixes)
2. Invoke `/provision-environment {customerId}` via `.claude/skills/provision-environment/SKILL.md` → 202 + runId
3. Poll `GET /api/runs/{runId}` — watch H0 → H14 tick through
4. Handle manual gates: H0.5 admin consent (Global Admin), H1 quota (owner), H8 SPE 24h wait
5. H13 fires: composite verifiers green + Ready writer PATCH → `sprk_setupstatus = 2`
6. Verify via Dataverse MCP: `sprk_dataverseenvironment.sprk_setupstatus = Ready`
7. Run Phase F acceptance harness (task 186) against fresh customer

**Gate = project done**: One customer reaches Ready + Phase F acceptance harness green (all real checks + 4 sample-workload checks from G-8 Batch 11).

---

## Effort estimate

| Wave | Hours | Nature |
|---|---|---|
| G-8 (12 parallel agents) | 4-6 wall-clock | Autonomous authoring |
| H-3 | 3-4 | Owner-in-the-loop execution (11 sequential steps) |
| H-4 | 3-6 | Owner-in-the-loop first live E2E (with 2-3 iteration loops for surface unknowns) |
| **Total to first successful E2E** | **10-16 hrs** | ~4-6 autonomous + 6-10 owner-in-the-loop |

---

## Traceability

Every audit finding cites file:line in the source Fable reviewer's report. Reviewer outputs preserved in session transcript. Rewriting this audit from scratch is unnecessary — the defect list above is the working ledger for Wave G-8.

Reviewer output paths:
- Reviewer A (handlers): `agentId a10277833be6e752d`
- Reviewer B (Bicep): `agentId a958b19b7d6f25625`
- Reviewer C (scripts + prereqs): `agentId adcec55230d13ca80`
- Reviewer D (H13 gate + Phase F): `agentId addf30ea2dd4196ba`
- Reviewer E (FR/SC closure): `agentId ab8523a94fcfdc772`
