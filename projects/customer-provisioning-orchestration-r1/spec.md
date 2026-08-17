# Customer Provisioning & Deployment Orchestration — AI Implementation Specification

> **Status**: Ready for Implementation (pending `/project-pipeline`)
> **Created**: 2026-08-16
> **Source**: [`design.md`](design.md) v3.3 (1,884 lines) + [`PROJECT-UPDATE-2026-08-12.md`](PROJECT-UPDATE-2026-08-12.md) + [`COMPONENT-INVENTORY.md`](COMPONENT-INVENTORY.md) + [`notes/pricing-research-2026-08-12.md`](notes/pricing-research-2026-08-12.md) + [`notes/graph-spe-2026-08-standards-spike.md`](notes/graph-spe-2026-08-standards-spike.md) + [`notes/r3-handoff.md`](notes/r3-handoff.md)
> **Project**: customer-provisioning-orchestration-r1
> **Predecessors**: `spaarke-environment-factory-r1` (superseded); `spaarke-environment-provisioning-app` (r1, complete PR #390); `code-quality-and-assurance-r3` (r3 tasks 060/061/062/017 landed 2026-08-14)

---

## Executive Summary

Build the single systematic process for standing up a new Spaarke customer environment and deploying the platform into it. One orchestrated pipeline, driven by an L2 control-plane App Service with Cosmos DB state, backed by idempotent deterministic L1 handlers (`IJobHandler` per ADR-004), invoked via an L3 Claude Code operator skill. Two deployment tiers per D3: **Model 2 (dedicated stamp)** default for regulated/enterprise; **Model 1 (shared trial/SMB)** for prospects where the fixed-floor cost of a dedicated stamp is uneconomic. Same code, tenancy driven by run parameter.

Spaarke has three generations of provisioning assets (Gen 1 manual guide with 13 known issues; Gen 2 `Provision-Customer.ps1` + 25 Bicep modules; Gen 3 `sprk_dataverseenvironment` registry). This project unifies them behind one pipeline that tracks per-phase progress and reaches `Setup Status = Ready` only when validation passes.

---

## Scope

### In Scope

**Architecture (L1 / L2 / L3):**
1. **L1 handler catalog — 19 idempotent `IJobHandler` handlers**: H0 (preflight + quota checks), H0.5 (Model 2 consent-capture), H1 (subscription readiness), H2a (Bicep infra), H2b (AI Search indexes), H3 (Entra app-reg), H4 (KV secrets + `keyVaultReferenceIdentity` PATCH), H5 (Dataverse env creation), H6 (managed solution import — 8 solutions), H7 (Dataverse env-var values), H8 (SPE container-type + root container, confidential-client), H9 (BFF deploy), H10 (Dataverse App User + Graph app-role parity), H11 (user provisioning), H12a (AI seed chain), H12b (app-config seed), H12c (runtime references), H13 (E2E acceptance gate), H14 (post-deploy integrations)
2. **L2 control-plane service** — standalone .NET 10 App Service (`platform-controlplane.bicep`) with Cosmos DB state, run lifecycle, gate management, REST + AAD (bearer, `Operator`/`Reader` app-roles), fire-and-forget handler execution via Service Bus + state-reconciler `BackgroundService`
3. **L3 operator skill** — `/provision-environment` Claude Code skill invoking L2 REST API (Phase D deliverable)
4. **ProvisioningRun data model** — Cosmos DB `spaarke-provisioning` database, `runs` container (partition `/customerId`), enumerated `gateStates` + `interStepState` shapes, secrets stored as KV URI refs (never cleartext in Cosmos)
5. **Registry extension** — 11 new columns on `sprk_dataverseenvironment` (v3 adds `sprk_currentrunid`, `sprk_tenancymodel`, `sprk_tenantid`; v3.3 adds `sprk_bffversion`, `sprk_solutionversion`, `sprk_ClientCacheBustToken`)

**Gap closure (per PROJECT-UPDATE §6):**
6. **Gap 1** — H12a/b/c config-seed as first-class handlers with declarative manifest resolving two-source AI seed drift (`scripts/seed-data` MVP vs `infra/dataverse` R7)
7. **Gap 2** — `Deploy-Release.ps1` Phase 4 hardening: `customerId`-driven, remove `spaarkedev1` hardcode
8. **Gap 3** — H3 Entra app-reg (~14 permission grants per `Infrastructure/Auth/GraphAppRoles.cs`), H10 Dataverse App User + Graph app-role parity, H0.5 Model 2 consent-capture (D18)
9. **Gap 4** — Single E2E acceptance gate (extended `Validate-DeployedEnvironment.ps1`) + doc consolidation into one authoritative deploy guide + one validated env-var/app-setting manifest

**Fast-follow (per PROJECT-UPDATE §10):**
10. **Per-tenant token-metering layer** (D19) — APIM gateway or app-level custom App Insights metric keyed on `tenantId`
11. **SPE 403 fix** (T6) — confidential-client (app-only) token in H8 with cert bootstrapped from KV
12. **Cosmos DB provisioning added to per-customer Bicep** (R11) — BFF prereq
13. **Silent-failure trap catalog** (§4B, T1–T6) baked into handler post-conditions

**Tooling & infrastructure:**
14. **Hybrid tooling stack** (§4A) — Bicep for Azure stamp; PowerShell + Package Deployer for solutions/config/SPE/AI Search indexes; Terraform Power Platform provider **deferred to first-customer engagement per M-10** (D14 remains the design intent)
15. **Managed-solution packaging** — export/fix/pack-managed/verify via Package Deployer (H6); ships **8 authoritative solutions** per `Deploy-DataverseSolutions.ps1 $SolutionImportOrder` (v3.3 correction — was "~10")
16. **Parameter model** — hybrid profiles (D15) + Model 1 trial-tier profile (§3A A1) + full parameter spec with KV-ref secrets (B3)
17. **AI Search index provisioning** — 7 canonical indexes via H2b invoking `scripts/ai-search/Deploy-AllIndexes.ps1` (files, discovery, records, rag-references, insights, session-files, invoices; `spaarke-playbook-embeddings` retired; `spaarke-knowledge-index` archived)
18. **`platform.bicep` rebuild** — shrink to control-plane-only resources (D12)
19. **`customer.bicep` extension** — add per-customer OpenAI + AI Search + Doc Intelligence + App Insights + **Cosmos + optional SignalR** (v3); **Redis removed** per Q-E FR-12 (v3.2 — Redis is per-env, deployed via `scripts/Deploy-RedisCache.ps1`)
20. **`model1-shared.bicep` as first-class stack** (§3A A1 trial-tier composition)

**v3.2 additions (r3 handoff + owner directive #3 + Fable review):**
21. **Phase G — Canonical naming compliance at provisioning** (per r3 task 063 handoff §4a) — apply canonical KV secret names + `sprk-{env}-kv` vault + `spaarke-spekvcert` DO-NOT-RENAME dev exception in H4 + Bicep param-ization; skip live-dev remediation per owner directive
22. **Phase H — #1 KV federation remediation full** (per r3 task 017 assessment §4b + owner directive #3 "not deferred; done in the context of THIS project") — canonical secret-catalog manifest (single generated source for seeder + Configure script + tokens doc + Bicep KV secret set); alias collapse with LIVE App-Service + KV + Dataverse pre-check FIRST; IaC alignment; external-spa + code-pages runtime `/config.json` fetch
23. **Phase C — UAMI migration** (Fable H-3 correction of v3.1 aspirational claim) — new `uami.bicep` module + `app-service.bicep` refactor to consume UAMI + bind to both slots + RBAC migration + Graph app-role migration + Dataverse App User re-registration on UAMI app ID; structural fix for T5 slot-swap trap
24. **§4C rollback semantics** (Fable §6-1) — failure classification (Resumable / Retryable-with-cleanup / Quarantine-required / Successful-but-drifted); Cosmos `Quarantined` state; new `POST /api/runs/{id}/clear-quarantine` endpoint
25. **§4.2 handler execution model** (Fable M-9) — fire-and-forget via Service Bus + state-reconciler `BackgroundService` in L2 App Service (addresses App Service 230s HTTP timeout vs 30-min handlers)
26. **H0 preflight quota checks** (Fable §6-2) — OpenAI regional TPM headroom, Dataverse env-creation rate, subscription vCPU, SPE cert-bootstrap; blocks run before H1 starts
27. **§4.1a Model 1 vs Model 2 handler behavior differences** (Fable §6-7) — enumerated table (H0/H2a/H2b/H4/H7/H10/H12c/H13 differ per tier)
28. **`GraphAppRoles.cs` completion** (Fable H-3) — complete 11 of 14 null `AppRoleId` GUIDs via `az` enumeration of Graph resource SP; escalation gate before first production customer

**v3.3 additions (owner Q1–Q7 review round + Q5 spike):**
29. **§4.3a Claude Code operator toolchain** (Q4) — 15-tool matrix (pwsh, bash, WebFetch, az, pac, Dataverse MCP, Azure MCP with fallbacks); operator's own AAD identity (not SP); prereqs check as skill Step 0; fallback matrix for MCP disconnects
30. **§4D tenant-isolation invariants** (Q6) — 5 binding invariants I1–I5 enforced via new ArchTests (per r3 task 040 pattern) — no hardcoded default tenant; unconditional `tenantId` filter on AI Search; partition-key predicate on Cosmos; SPE container IDs tenant-scoped-derived; Graph token per-tenant scoped
31. **§9A consolidated identity + config surface** — 14-row single-page reference (per-customer artifacts + who provisions + who verifies + rotation cadence + Model 1 vs Model 2)
32. **§11.1a solutions reconciliation** — authoritative 8 solutions ship via H6 (not "~10" as v3.1 said); Phase A audits the other ~28 items in `src/solutions/`
33. **§14A upgrade model** — 3 upgrade classes (U1 BFF code / U2 solutions / U3 Bicep infra); per-handler upgrade-mode semantics (H2a/H2b/H4/H6/H7/H9/H12a/b/c/H14); version compatibility matrix; 6 breaking-change classes U-CB-1..U-CB-6; drift detection via `az deployment group what-if`
34. **Code fix**: `scripts/Register-EntraAppRegistrations.ps1:63` hardcoded Spaarke tenant DEFAULT removed (§4D I1 enforcement — `-TenantId` now Mandatory)

**Acceptance:**
35. **E2E dry run** — stand up a fresh `trial-{yyyymmdd}` customer stamp (Model 1 profile) using only the new pipeline; reach `Setup Status = Ready`; all 6 §4B silent-fail traps verified cleared; `scripts/naming-conformance-check.ps1` exits 0

### Out of Scope

- **Data migration** (CSV / legacy import / SPE bulk load) — belongs to `spaarke-data` CLI (separate project); new customer starts empty-but-functional
- **Per-customer isolation for Office add-ins / external SPA / M365 Copilot agent** — currently single-shared per INVENTORY §8; per-customer SWA/portal/agent automation is a follow-on
- **Registry-aware decommission pipeline** (D17) — existing `Decommission-Customer.ps1` remains operational as-is; deferred to r2
- **Fleet management web app** (D13) — read-only Cosmos UI deferred; r1 fleet visibility comes from `sprk_dataverseenvironment` + `sprk_currentrunid`
- **Spaarke Assistant front end** — design-acknowledged, built later
- **Changes to `/deploy-new-release`** — consumed as-is
- **Disaster recovery / backup automation**
- **CI/CD workflow changes** — existing workflows consumed as-is; handlers call underlying scripts directly (Phase H coordinated PR with `ci-cd-unit-test-remediation-r1` is the one exception, scoped to naming/tenant-isolation ArchTest wiring)
- **Live dev-env KV naming remediation** — owner directive #3: bake canonical naming into NEW provisioning only; do not remediate live-dev drift
- **TF Power Platform provider adoption** (M-10) — deferred to first-customer engagement; interim H5/H10 use `pac admin` + PPAC + Graph SDK
- **R22 Exchange RBAC-for-Applications migration** — Phase D backlog for r2
- **R23 MI-as-FIC opportunity** — Phase D backlog; not r1 acceptance
- **Zero-downtime SLA** — blue-green slot-swap gives minutes-of-drain; customer-facing SLA is separate engagement work

### Carry-Overs (tracked, not critical path)

- **R5** — `DemoExpirationService` migration off `[Obsolete]` `DemoProvisioningOptions.Environments`/`DefaultEnvironment` → registry lookup (Phase E)
- **R6** — Doc drift fixes rolled into Gap 4 doc consolidation (Phase A)
- r1 live-provisioning sign-off (criteria 5/8/9/11) folded into E2E dry run (Phase F)

### Affected Areas

| Path | What changes |
|---|---|
| `src/server/api/Sprk.Bff.Api/Program.cs` | Register H0.5 consent-callback endpoint |
| `src/server/api/Sprk.Bff.Api/Endpoints/Onboarding/**` (NEW) | `POST /api/onboarding/consent-callback` endpoint (H0.5, D18) |
| `src/server/api/Sprk.Bff.Api/Services/Registration/DemoExpirationService.cs` | Migrate off `[Obsolete]` options → registry lookup (R5) |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs` | Complete 11 of 14 null `AppRoleId` GUIDs (r3 task 062 leftover) |
| `src/server/api/Sprk.Bff.Api/Services/**` (existing) | 5 new ArchTests scan for §4D I1–I5 invariant violations (no code change if compliant) |
| **NEW** `src/server/services/Sprk.Provisioning.ControlPlane/**` | L2 control-plane .NET 10 App Service — REST API + Cosmos state + state-reconciler `BackgroundService` + 19 `IJobHandler`s + endpoint filters |
| `infrastructure/bicep/customer.bicep` | Add Cosmos DB + optional SignalR; remove Redis; UAMI param post-Phase C |
| `infrastructure/bicep/platform.bicep` | Rebuild — shrink to control-plane-only (L2 App Service + Cosmos + platform KV + monitoring) |
| **NEW** `infrastructure/bicep/modules/uami.bicep` | User-Assigned Managed Identity per customer env (Phase C) |
| `infrastructure/bicep/modules/app-service.bicep` | Refactor to consume UAMI + bind to both slots (Phase C) |
| `infrastructure/bicep/modules/cosmos-db.bicep` | Existing name confirmed (v3.2 M-3 correction) |
| **NEW** `infrastructure/bicep/stacks/model1-shared.bicep` | First-class trial-tier stack (§3A A1) |
| **NEW** `infrastructure/bicep/platform-controlplane.bicep` | L2 orchestrator infra |
| `scripts/Provision-Customer.ps1` | PORT + MAJOR EXTEND — basis for handler catalog with 6 new Bicep module invocations |
| `scripts/Register-EntraAppRegistrations.ps1` | ~14 grant idempotency; single BFF app-reg only (S2S dropped); tenant hardcoded default REMOVED (v3.3 code fix — DONE 2026-08-16 in commit `1834b77bc`) |
| `scripts/Create-NewContainerType.ps1` + `Register-*.ps1` + `New-BusinessUnitContainer.ps1` | PORT + confidential-client fix (T6) |
| `scripts/Deploy-Release.ps1` Phase 4 | HARDEN — `customerId`-driven, no `spaarkedev1` hardcode (Gap 2) |
| `scripts/Validate-DeployedEnvironment.ps1` | EXTEND — E2E acceptance gate (Gap 4); verify all §4B T1–T6 traps cleared |
| `scripts/Deploy-DataverseSolutions.ps1` | REUSE + EXTEND to Package Deployer for dependency-ordered import (8 solutions per §11.1a) |
| `scripts/seed-data/Deploy-All-AI-SeedData.ps1` + `Seed-PlaybookConsumers.ps1` | PORT — basis for H12a with declarative manifest resolving two-source drift |
| `scripts/ai-search/Deploy-AllIndexes.ps1` | REUSE — invoked by H2b (7 canonical indexes) |
| `scripts/naming-conformance-check.ps1` (r3 task 063) | CONSUME — H13 acceptance invokes; exit 0 required on r1-owned surfaces |
| **NEW** `scripts/canonical-secret-catalog/**` | Phase H — single source generates seeder + Configure script + tokens doc + Bicep KV secret set |
| **NEW** `.claude/skills/provision-environment/SKILL.md` | L3 operator skill (Phase D) |
| `src/dataverse/solutions/spaarke_core/**` | Registry schema extension — 11 new columns on `sprk_dataverseenvironment` |
| `tests/Spaarke.ArchTests/**` | 5 new ArchTests (I1–I5 tenant isolation) + naming-conformance advisory gate wiring |
| **NEW** `docs/deployment/version-compatibility-matrix.md` | §14A publication at every release tag |
| **NEW** `docs/deployment/customer-comms/**` | U-CB-1..U-CB-6 customer-communication templates |
| `docs/guides/*DEPLOY*.md` / `*ONBOARDING*.md` / `auth-deployment-setup.md` | CONSOLIDATE (Gap 4 / R6) into one authoritative deploy guide |

---

## Requirements

### Functional Requirements

**L1 handlers (H0 – H14):**

1. **FR-01 (H0)**: Preflight validates all run parameters + queries Azure OpenAI regional TPM headroom (150+200+30+350), Dataverse env-creation rate quota, subscription vCPU quota, SPE cert-bootstrap status. **Acceptance**: run blocked with clear error IF any headroom insufficient for +1 provisioning target; blocks before H1 starts. Idempotency key: `preflight-{customerId}-{paramHash}`.
2. **FR-02 (H0.5)**: For Model 2 self-service, `POST /api/onboarding/consent-callback` (Anonymous, HMAC-verified) captures customer admin's `tid` on Microsoft admin-consent grant, seeds run parameters, kicks pipeline. Re-consent semantics: no-op with 200 + link to existing run IF `sprk_dataverseenvironment` row exists with same `tid` + status ∈ {Ready, Running, WaitingOnGate}; restart from H0 IF Failed/Cancelled. **Acceptance**: HMAC signature verified; `tid` claim written to `sprk_dataverseenvironment.sprk_tenantid` + Cosmos `parameters.tenantId`. Idempotency key: `consent-{customerId}-{tid}`.
3. **FR-03 (H1)**: Subscription readiness — ARM verification the target subscription is reachable + (Model 2 `CustomerOwned` only) Lighthouse delegation verified. **Acceptance**: `az account show` succeeds against target sub; Lighthouse RG scope accessible.
4. **FR-04 (H2a)**: Per-customer Bicep infra deploy. Six new module invocations vs current `Provision-Customer.ps1` step 3: Cosmos DB + OpenAI + AI Search + Document Intelligence + App Insights/Log Analytics + optional SignalR. Redis explicitly NOT provisioned (per-env via `Deploy-RedisCache.ps1`). **Acceptance**: all 15 resources per §7.2 deployed to expected names per §7.1 naming convention; `az deployment group what-if` in upgrade mode surfaces drift before apply.
5. **FR-05 (H2b)**: 7 canonical AI Search indexes provisioned via `scripts/ai-search/Deploy-AllIndexes.ps1`. Model 1: verifies presence on shared platform service + provisions per-tenant `tenantId`-filter query template (does NOT re-create indexes). **Acceptance**: all 7 index names present per §8.2; per-index invariant verifier passes (required filterable fields + vector fields + forbidden fields absent).
6. **FR-06 (H3)**: 1 Entra app registration (BFF API) with ~14 Graph + Dynamics permission grants per `GraphAppRoles.cs` (r3 task 062). Sign-in audience `AzureADMultipleOrgs` (v3 change enables Model 2 consent). Client secret stored as KV URI reference in platform KV. Admin consent gate verified via Graph query. S2S Dataverse app-reg explicitly NOT provisioned (r3 task 060 dropped). **Acceptance**: `Register-EntraAppRegistrations.ps1` idempotent; `-TenantId` mandatory; app-reg's 14 permissions returned by Graph `oauth2PermissionGrants` query.
7. **FR-07 (H4)**: KV secrets population. Secret names conform to canonical standard (Phase G per §7.9): env-agnostic; one canonical casing; `sprk-{env}-kv` vault. Consumes canonical secret-catalog manifest (Phase H per r3 task 017 assessment). `keyVaultReferenceIdentity` PATCHed to UAMI on both prod + staging slots. Interim (pre-Phase C): grant KV RBAC to BOTH System-Assigned MI principals. **Acceptance**: ARM read confirms `keyVaultReferenceIdentity == UAMI-resource-id`; BFF `/health` succeeds resolving `Dataverse:ClientSecret` KV ref (r3 task 061 `ValidateOnStart` fails deploy if not).
8. **FR-08 (H5)**: Dataverse environment created. Interim: `pac admin create-environment` + gate verified. Design target (deferred per M-10): TF `powerplatform_environment` resource. **Acceptance**: `sprk_dataverseenvironment.sprk_dataverseurl` populated + env is accessible.
9. **FR-09 (H6)**: Managed solution import via Package Deployer — **8 authoritative solutions** (§11.1a) with dependency ordering: SpaarkeCore (Tier 1), webresources (Tier 2), then CalendarSidePane / DocumentUploadWizard / EventCommands / EventDetailSidePane / EventsPage / LegalWorkspace (Tier 3 parallel). **Acceptance**: all 8 solutions imported at correct versions; `Deploy-DataverseSolutions.ps1` succeeds; upgrade mode retires the holding solution.
10. **FR-10 (H7)**: 7 per-customer Dataverse env-var values set per §10.3 (`sprk_BffApiBaseUrl`, `sprk_BffApiAppId`, `sprk_MsalClientId`, `sprk_TenantId`, `sprk_AzureOpenAiEndpoint`, `sprk_ShareLinkBaseUrl`, `sprk_SharePointEmbeddedContainerId`). **Acceptance**: `environmentvariablevalue` records exist for all 7 with expected values; client startup validates no hardcoded URL fallbacks (per task 024).
11. **FR-11 (H8)**: SPE container-type + root container provisioned. Uses **confidential-client (app-only) token** with cert bootstrapped from KV (T6 fix — delegated 403s with `public client not allowed`). 24h replication is lead-time item (H0 preflight checks cert-bootstrap done). **Acceptance**: container GET via app-only token succeeds; container ID persisted to Dataverse env-var + KV secret `customer-{customerId}-spe-container-id`.
12. **FR-12 (H9)**: BFF deployed via `Deploy-BffApi.ps1` + hardened `Deploy-Release.ps1` Phase 4 (`customerId`-driven, no `spaarkedev1` hardcode). Blue-green via staging slot in upgrade mode with rollback via re-swap. **Acceptance**: `/health` returns 200; slot-swap smoke test produces no cold-start KV-ref failures; r3-era gates (analyzers-as-errors + god-class ratchet + 4 new ArchTests + naming-conformance + Graph app-role parity) all pass.
13. **FR-13 (H10)**: 2 Dataverse Application Users registered (BFF app-reg + UAMI) as System Administrator. Graph app-role parity synced from `GraphAppRoles.cs` constant onto UAMI SP. Interim: PPAC UI + Graph SDK; design target: TF `powerplatform_user`. **Acceptance**: `systemusers?$filter=applicationid eq {uami-app-id}` returns count 1 (T2 verification); UAMI SP `appRoleAssignments` includes all 14 role IDs from `GraphAppRoles.cs` (T3 verification); H10 escalation gate: 11 of 14 null `AppRoleId` GUIDs completed before first production customer.
14. **FR-14 (H11)**: User provisioning per identity preset (D6: `B2BGuest` or `NativeAccount`) via r1 registration flow. B2B needs consent-verification gate. **Acceptance**: users created with correct UPN pattern; license assignment succeeds (per r1 FR-11).
15. **FR-15 (H12a)**: AI seed chain — type-lookups → actions → tools → knowledge → skills → playbooks → output-types → playbook consumers (single AI routing surface per ADR-039). Authoritative source per artifact declared in declarative seed manifest (resolves two-source drift). **Acceptance**: all seed rows present; no duplicates; playbook consumers resolve to shipped playbooks; `sprk_aimodeldeployment` rows placeholder (H12c will populate).
16. **FR-16 (H12b, DAG-parallel with H12a)**: App-config seed — DataGrid configs, field-mapping profiles + rules, system workspace layouts, chart definitions. No dependency on H12a. **Acceptance**: `sprk_gridconfiguration` + `sprk_fieldmapping*` + `sprk_workspacelayout` + chart-definition records seeded per declarative manifest.
17. **FR-17 (H12c)**: Runtime references — `sprk_aimodeldeployment` rows point at customer's OpenAI deployment (Model 2) or shared platform OpenAI (Model 1 with per-tenant metering attribution). **Acceptance**: BFF `AzureOpenAIOptions.Endpoint` resolves to correct endpoint via env-var lookup + `sprk_aimodeldeployment` join.
18. **FR-18 (H13)**: End-to-end acceptance gate. Extended `Validate-DeployedEnvironment.ps1` asserts EFFECTS not intentions (per R7). Checks: BFF `/health`, sample analysis, sample document upload+index, workspace-layout render, wizard field-map, **all 6 §4B T1–T6 silent-fail traps cleared**, `scripts/naming-conformance-check.ps1` exits 0, **all 5 §4D I1–I5 tenant-isolation invariants sample-verified**, cost envelope ≤ target per pricing model (§15 #14). **Acceptance**: registry `Setup Status` transitions to `Ready` only if H13 exits 0.
19. **FR-19 (H14, sub-steps DAG-parallel)**: Post-deploy integration wiring — (a) 2 Exchange `ApplicationAccessPolicy` (BFF app-reg + UAMI, action-and-verify semantics — on 0 or 1 create, on 2+ verify AppIds match else fail with drift diagnostic); (b) Graph webhook subscriptions per Communication/Email module with HMAC signing keys from H4; (c) Dataverse service-endpoint webhooks. S2S consent sub-step (d) explicitly NOT included (r3 task 060). **Acceptance**: `Get-ApplicationAccessPolicy` returns 2 entries with both principals matching; Graph subscriptions active; service-endpoint webhooks fire with correct HMAC.

**L2 control plane (§4.2):**

20. **FR-20**: L2 hosted on .NET 10 App Service in `rg-spaarke-platform-{env}` (parity with BFF per B2). REST API + JWT bearer + audience `api://spaarke-provisioning-controlplane-{env}` + `Operator` / `Reader` app-roles. OpenAPI at `/swagger`. **Acceptance**: L3 skill can auth via `az account get-access-token`; `Operator`-role required for mutating endpoints returns 403 without role.
21. **FR-21**: L2 API surface (all endpoints per §4.2): `POST /api/runs` (init), `POST /api/runs/{id}/preflight` (H0), `GET /api/runs/{id}`, `POST /api/runs/{id}/gates/{gateId}/advance`, `POST /api/runs/{id}/resume`, `GET /api/runs/{id}/phases/{phaseId}/logs`, `POST /api/runs/{id}/cancel`, `POST /api/onboarding/consent-callback` (BFF endpoint), `POST /api/runs/{id}/clear-quarantine` (v3.2 §4C). **Acceptance**: OpenAPI schema exports; each endpoint returns per spec; audit-logging on `clear-quarantine`.
22. **FR-22**: Handler execution model — L2 REST endpoints ENQUEUE via Service Bus + return 202 Accepted (<100ms); state-reconciler `BackgroundService` polls Cosmos every 5s to advance DAG; handlers run in BFF's existing `IJobHandler` infrastructure (ADR-004) with 3-level idempotency (MessageId dedup + Redis idempotency lock + deterministic idempotency key). **Acceptance**: 30-min handler completes without HTTP timeout under load test; concurrency safety verified via Cosmos optimistic concurrency (no duplicate enqueuing when multiple L2 instances run reconciler).
23. **FR-23**: Concurrency + crash recovery (I5 + I6) — same-customer runs serialized via optimistic concurrency on `sprk_dataverseenvironment.sprk_currentrunid` (`null → newRunId`, conflict = 409 with winning run ID); cross-customer runs parallel; on startup L2 scans Cosmos for `status ∈ {Running, WaitingOnGate}` older than 2× median-handler-duration + re-schedules from `currentPhase`. **Acceptance**: concurrent-run test returns 409 on same customer; crash-then-restart resumes orphaned runs.
24. **FR-24 (§4C rollback semantics)**: 4-class failure taxonomy — Resumable (retry after external precondition), Retryable-with-cleanup (re-run handler; own idempotency handles partial write), Quarantine-required (Cosmos `Quarantined` state blocks new runs against same `customerId`; operator explicitly clears via `POST /api/runs/{id}/clear-quarantine` with reason; audit-logged), Successful-but-drifted (H13 detects; operator re-runs affected phases with `resumeFromPhase` param). **Acceptance**: each failure class round-trips through Cosmos correctly per state-transition table §4C; new endpoint requires reason + audit-logs to App Insights.

**L3 operator skill (§4.3a):**

25. **FR-25**: `/provision-environment` skill at `.claude/skills/provision-environment/SKILL.md` (Phase D deliverable). Step 0 = prerequisites check (pwsh ≥ 7.4, az ≥ 2.60, pac ≥ 1.35, git ≥ 2.40, operator's `az login` succeeded, L2 API reachable, MCP status optional). Interactive intake collects `customerId` / `tenantId` / `tenancyModel` / `profile`. Preflight → confirmation gate → execute loop (enqueue → poll → advance → manual gates surface actionable instructions) → completion writes handoff report to `runs/{runId}.md` + updates `sprk_dataverseenvironment` via Dataverse MCP + produces final summary. **Acceptance**: skill runs full flow in dry-run; fallback matrix triggered on MCP disconnect + skill continues via `pac data` or raw Web API PS.

**Data model (§6):**

26. **FR-26**: Extend `sprk_dataverseenvironment` with 11 new columns: `sprk_azuresubscriptionid`, `sprk_resourcegroupname`, `sprk_appservicename`, `sprk_keyvaultname`, `sprk_containertypeid`, `sprk_provisionedon`, `sprk_currentrunid`, `sprk_tenancymodel` (Choice: Model1Shared / Model2Dedicated), `sprk_tenantid`, `sprk_bffversion`, `sprk_solutionversion`, `sprk_ClientCacheBustToken`. **Acceptance**: solution import creates all columns; existing rows migrate cleanly; `sprk_tenantid` populated from H0.5 or run params.
27. **FR-27**: Cosmos DB `spaarke-provisioning` database, `runs` container (partition `/customerId`, TTL 365d). Fields per §6.2 with enumerated `gateStates` + `interStepState` shapes. Secrets in `parameters` stored as KV URI refs only (never cleartext). **Acceptance**: BFF's Cosmos client can read/write with correct partition-key predicate; no cleartext secret string matches any known secret pattern in Cosmos rows.

**Tenant-isolation invariants (§4D — added v3.3):**

28. **FR-28 (I1)**: No hardcoded default tenant in provisioning scripts. Every script that provisions per-customer requires `-TenantId` explicitly; no fallback default. **Acceptance**: pre-commit ArchTest scans provisioning scripts for tenant-shaped GUID defaults; fails on hit. `Register-EntraAppRegistrations.ps1:63` fix already committed in `1834b77bc`.
29. **FR-29 (I2)**: All AI Search queries include unconditional `tenantId eq '{ctx.TenantId}'` filter regardless of index or query shape. **Acceptance**: new ArchTest scans BFF for AI Search client `.Search(...)` calls whose filter doesn't include `tenantId eq`; fails on hit. Phase A audit sweep of every BFF service touching AI Search beyond the 2 Fable-spot-checked (`ReferenceRetrievalService`, `RecordSearchService`).
30. **FR-30 (I3)**: All Cosmos reads/writes include `/tenantId` (or `/customerId` for ProvisioningRun) partition-key predicate. No cross-partition queries against tenant-scoped containers. **Acceptance**: new ArchTest scans for Cosmos SDK `.ReadItemAsync(...)` / `.CreateItemAsync(...)` calls without explicit `PartitionKey`; fails on hit.
31. **FR-31 (I4)**: SPE container IDs always tenant-scoped-derived — BFF's SPE code paths read container ID from current tenant's env-var (`sprk_SharePointEmbeddedContainerId`) or KV secret (`customer-{customerId}-spe-container-id`); no fallback default. **Acceptance**: new ArchTest fails on any string literal matching SPE container ID pattern (`b!...`) in BFF services; Phase A audit enumerates every SPE call site and verifies container-ID resolution goes through single `ITenantContainerResolver` service.
32. **FR-32 (I5)**: Graph token acquisition per-tenant scoped. Delegated calls use OBO with caller's `tid`; app-only calls use `.default` scope with target tenant's `tid` explicitly named (NOT MI credential default tenant). **Acceptance**: new ArchTest scans `GraphClientFactory` for token acquisition without explicit `tenantId`; fails on hit.

**Silent-fail trap catalog (§4B):**

33. **FR-33**: Six silent-fail traps (T1–T6) baked into handler post-conditions as verified assertions (not runbook footnotes). Each handler asserts its trap is cleared before reporting success. **Acceptance**: T1 (App Service `keyVaultReferenceIdentity == UAMI`); T2 (`systemusers?$filter=applicationid eq {mi-app-id}` returns 1); T3 (UAMI SP `appRoleAssignments` includes all 14 from `GraphAppRoles.cs`); T4 (`Get-ApplicationAccessPolicy` returns 2 entries with both principals — H14 creates if missing, verifies drift diagnostic if 2+); T5 (both slot MIs have KV RBAC — interim; structurally impossible post-Phase C UAMI); T6 (SPE container-type creation uses confidential-client cert from KV).

**Upgrade model (§14A — added v3.3):**

34. **FR-34**: Handlers execute in **upgrade mode** when target `sprk_dataverseenvironment.sprk_provisionedon` is not null. Per-handler semantics per §14A.2 table. H0 preflight (upgrade mode) reads `sprk_bffversion` + `sprk_solutionversion` from registry, queries `docs/deployment/version-compatibility-matrix.md`, blocks incompatible pairs (🔴 Red). H2a upgrade-mode runs `az deployment group what-if` first; defaults to REJECT + escalate on drift with report at `runNotes/drift-{customerId}-{timestamp}.md`. H4 upgrade mode is rotation-safe (never overwrites live secrets absent explicit `H4-rotate` variant). H12a/b/c upgrade mode is additive-only by default (`--overwrite-authored-content` flag reserved for security-critical playbook fixes). H9 blue-green via staging slot with rollback via re-swap. **Acceptance**: version-compatibility matrix published at `docs/deployment/version-compatibility-matrix.md`; drift-detection test on H2a returns REJECT-and-report on modified resource; H4 upgrade against KV with live secret returns no-op unless rotate flag set.

**Governance (§7.9 canonical naming, §17 placement):**

35. **FR-35 (Phase G)**: Canonical naming applied at all provisioning entry points. R1 env-agnostic secret names (no `DEV/DEMO/PROD/…` token as delimited segment); R2 one canonical casing per logical secret (kebab-case new, PascalCase grandfathered); R3 vault `sprk-{env}-kv` (dev exception `spaarke-spekvcert` DO-NOT-RENAME, codified in Bicep param); no orphan / duplicate secrets. Reference syntax single form: `@Microsoft.KeyVault(VaultName=sprk-{env}-kv;SecretName=<Canonical-Name>)`. **BINDING pre-check**: before removing any alias/fallback spelling, pre-check LIVE App Service + KV + Dataverse-persisted config; NEVER delete `Dataverse-ClientSecret` / `BFF-API-ClientSecret`. **Acceptance**: `scripts/naming-conformance-check.ps1` exits 0 on r1-owned surfaces post-provisioning; bicep vault name is param (not hardcoded).
36. **FR-36 (Phase H)**: Canonical secret-catalog manifest is the single generated source for seeder + Configure script + tokens doc + Bicep KV secret set (r3 KV federation design Phase 3b). Alias collapse for AI Search key etc. with pre-check protocol per FR-35 pre-check gate. IaC alignment: bicep secret names + app-setting keys canonical. External-spa + code-pages consume `/config.json` runtime endpoint (closes bake-at-build-time pattern). Coordinate `.github/workflows` gate wiring with `ci-cd-unit-test-remediation-r1` per r3 `task-042-063-ci-gate-wiring-deferral.md`. **Acceptance**: manifest generates all 4 outputs identically; alias collapse task-notes cite pre-check evidence; external-spa fetches `/config.json` at bootstrap.
37. **FR-37 (Phase C UAMI)**: New `uami.bicep` module + `app-service.bicep` refactor to consume UAMI as `type: 'UserAssigned', userAssignedIdentities: {...}` on BOTH production + staging slots. Migrate all RBAC (KV Secrets User, Storage Blob Data Contributor, Cognitive Services User, Cosmos DB Data Contributor) from System-Assigned MI principal to UAMI principal. Migrate Graph app-role grants onto UAMI SP. Migrate Dataverse App User registration from System-Assigned MI app ID to UAMI app ID. `keyVaultReferenceIdentity` PATCH to UAMI resource ID. **Acceptance**: `uami.bicep` module exists; slot-swap smoke test produces no cold-start KV-ref failures (T5 structurally impossible); Dataverse `systemusers?$filter=applicationid eq {uami-app-id}` returns 1.

### Non-Functional Requirements

- **NFR-01**: **BFF publish size ≤ 60 MB compressed** (CLAUDE.md §10 NFR-01). Current net10 baseline 44.96 MB incl. PDBs (r3 handoff 2026-08-13). Phase E `DemoExpirationService` migration + Phase C `uami.bicep` (IaC-only, zero NuGet) + H0.5 consent-callback endpoint combined delta < ~0.5 MB. Every BFF-touching task reports absolute + delta in PR description; ≥ +5 MB single-task delta requires explicit justification; ≥ 55 MB cumulative triggers architecture review; ≥ 60 MB HARD STOP.
- **NFR-02**: **Zero HIGH-severity CVEs** from `dotnet list package --vulnerable --include-transitive`. Current net10 baseline: zero vulns.
- **NFR-03**: **Pipeline runtime ≤ 1 hour** end-to-end from H0 to H13 for a fresh Model 2 customer stamp (per §15 north-star). Lead-time items (Azure quota / SPE 24h replication / customer admin consent) are surfaced UP-FRONT in H0 preflight, not counted as pipeline time.
- **NFR-04**: **Cost envelope**: Model 2 empty-environment Azure floor ≤ $400/mo (Redis now per-env, so no per-customer P1 delta); Model 1 marginal per-customer ≤ $430/mo (5–10 users, capped tokens); shared platform floor for Model 1 ≤ $400/mo. H13 flags deviations > 20% as cost drift.
- **NFR-05**: **Fail-fast configuration validation** (r3 task 061 landed) — BFF misconfig causes `/health` startup failure not runtime failure. Any new `IOptions<T>` H0.5 introduces MUST be classified per Tier 1/2/3 (per `task-061-config-validation-classification.md`) and either validated-on-start (Tier 1) or added to Tier-2 exemption list.
- **NFR-06**: **Analyzers-as-errors** (r3 task 041 + net10 stricter analyzers) — every new r1 BFF code file has zero warnings; `TreatWarningsAsErrors=true` in `Directory.Build.props`; CS8601 / CS8604 nullable are errors.
- **NFR-07**: **God-class ratchet** (r3 task 040 pattern) — no NEW `src/server/**/*.cs` file > 2,000 lines; existing 14 frozen files respect their +100 LOC grace. H0.5 endpoint stays small (single controller + verification helper); `DemoExpirationService` refactor respects the frozen file's grace.
- **NFR-08**: **ArchTest coverage** — 5 new ArchTests for §4D I1–I5 tenant-isolation invariants; sequence into r3 forcing-functions ecosystem via CI-wiring coordinated PR with `ci-cd-unit-test-remediation-r1` (workflow-file edits deferred to that PR).
- **NFR-09**: **Graph v6 / Kiota 2.0 error type** (net10 cutover per r3 handoff) — new H10 Graph SDK calls catch `ODataError` (not `ServiceException`); `ResponseStatusCode` is int; `ResponseHeaders` is dict.
- **NFR-10**: **All handlers 3-level idempotent** (ADR-004) — Service Bus MessageId dedup + Redis `IdempotencyService` check/lock + Dataverse alternate-key upsert. Version tokens `{schemaVer}` are deterministic content hashes / semantic versions per §4.1 preamble.
- **NFR-11**: **Auditable operator action** — Claude Code auths via operator's own AAD identity (`az login`), NOT a service principal (§4.3a.2). Every mutating L2 endpoint audit-logs to App Insights with actor `tid`.
- **NFR-12**: **Regional TPM headroom** — H0 preflight fails run before H1 IF projected +1-tenant capacity exceeds Azure OpenAI regional quota (150+200+30+350 per-model TPM sum). Prevents cross-customer concurrency clashes per R19.

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|---|---|
| **ADR-004** (async job contract) | Individual L1 handlers implement `IJobHandler`; one message, one handler, one outcome (§5.1). L2 orchestration is a NEW component NOT governed by ADR-004. See ADR Tensions. |
| **ADR-010** (BFF DI minimalism) | Provisioning handlers register in L2 control-plane service, NOT the BFF (§5.2). BFF DI impact strictly bounded (H0.5 endpoint + DemoExpirationService refactor). |
| **ADR-013** (AI facade `PublicContracts/`) | H0.5 endpoint MUST NOT inject `IActionResolver` / `IActionRunner` directly. If AI needed, use `Services/Ai/PublicContracts/` facade. |
| **ADR-014** (data model — session isolation) | `spaarke-session-files` index carries the canonical `tenantId` + `sessionId` dual-filter invariant. FR-29 (I2) preserves it. |
| **ADR-017** (job status granularity) | Per-handler job status governs individual handler outcomes; ProvisioningRun in Cosmos (D13) provides multi-phase orchestration state. Different concerns, different stores (§5.3). |
| **ADR-020** (AI model version pinning) | §7.4 pins model deployment versions (`gpt-4o` 2024-08-06, `gpt-4o-mini` 2024-07-18, `text-embedding-3-large` v1). |
| **ADR-027** (subscription isolation + 2026-06-02 unmanaged-solution amendment) | D4 subscription-per-customer isolation + billing unit; SpaarkeOwned default or CustomerOwned (Lighthouse). |
| **ADR-028** (auth v2) | H4 KV secrets population, UAMI RBAC assignments, `keyVaultReferenceIdentity` PATCH all follow ADR-028's 21 MUSTs. E-2 fallback: `AzureOpenAI__ApiKey` KV ref when MI auth on `kind=AIServices` unavailable. |
| **ADR-032** (Null-Object kill-switch) | SignalR (`Notifications:SignalRSpine:Enabled=true`) is feature-gated per ADR-032 P1/P2/P3 pattern. Any conditional service in L2 MUST also follow the pattern (per BFF §10 §F.1 anti-pattern rule). |
| **ADR-034** (notifications spine) | Optional SignalR resource per-customer aligns with ADR-034 realtime pattern. |
| **ADR-038** (testing strategy) | Integration-heavy pyramid; 5 new ArchTests (I1–I5) sequence into r3 forcing-functions. Test-modifying tasks trigger unconditional `code-review + adr-check` per root CLAUDE.md §8. |
| **ADR-039** (single AI routing surface) | H12a AI seed chain ends at `playbook consumers` per ADR-039 dispatcher-retirement. `spaarke-playbook-embeddings` index retired (FR-P2-06). |

### MUST Rules

- ✅ **MUST** implement all L1 handlers as `IJobHandler` per ADR-004
- ✅ **MUST** register provisioning handlers in **L2 control-plane service**, not the BFF (per §5.2 + D3/D8/D12)
- ✅ **MUST NOT** create per-customer Entra tenant (§9.1 v3 note); use one Spaarke tenant + one multitenant BFF app + admin-consent-per-customer-tenant
- ✅ **MUST** provision only ONE Entra app registration per customer (BFF API); do NOT re-introduce Dataverse S2S app-reg (r3 task 060 dropped it)
- ✅ **MUST** use **confidential-client (app-only) token** for SPE container-type creation (T6 — delegated 403s with `public client not allowed`)
- ✅ **MUST NOT** provision Redis per-customer (v3.2 Q-E FR-12); Redis is per-environment via `scripts/Deploy-RedisCache.ps1`
- ✅ **MUST** PATCH App Service `keyVaultReferenceIdentity` to UAMI on both prod + staging slots (T1)
- ✅ **MUST** deploy Cosmos DB per customer in H2a (BFF prereq — will not start without it, R11)
- ✅ **MUST** apply canonical KV secret + resource naming at all provisioning entry points (Phase G per §7.9 R1–R4); vault name is Bicep **parameter** not hardcoded
- ✅ **MUST NOT** delete `Dataverse-ClientSecret` / `BFF-API-ClientSecret` (BINDING pre-check per r3 handoff — OBO + shared-lib Dataverse still depend)
- ✅ **MUST** pre-check LIVE App Service + KV + Dataverse-persisted config BEFORE removing any alias/fallback spelling (FR-35 pre-check gate)
- ✅ **MUST** ensure all AI Search queries include unconditional `tenantId eq` filter (§4D I2 / FR-29)
- ✅ **MUST** ensure all Cosmos reads/writes include `/tenantId` (or `/customerId` for ProvisioningRun) partition-key predicate (§4D I3 / FR-30)
- ✅ **MUST** derive SPE container IDs from tenant context via `ITenantContainerResolver` (§4D I4 / FR-31)
- ✅ **MUST** acquire Graph tokens per-tenant scoped (§4D I5 / FR-32)
- ✅ **MUST NOT** hardcode default tenant in provisioning scripts (§4D I1 / FR-28); every provisioning script requires `-TenantId` mandatory
- ✅ **MUST** report BFF publish size + delta in every BFF-touching task's PR description (NFR-01)
- ✅ **MUST** ensure BFF `/health` fails fast at boot on any Tier-1 IOptions misconfig (r3 task 061 landed; NFR-05)
- ✅ **MUST** grant Graph app-roles onto UAMI SP from `GraphAppRoles.cs` constant (T3 parity per r3 task 062)
- ✅ **MUST** complete 11 of 14 null `AppRoleId` GUIDs in `GraphAppRoles.cs` via `az` enumeration BEFORE first production customer provisioning (Phase A escalation gate)
- ✅ **MUST** run `/conflict-check` before every BFF PR (19 active BFF worktrees per r3 handoff §7)
- ✅ **MUST** enqueue handlers via Service Bus + return 202 Accepted; no synchronous handler execution in HTTP request path (FR-22 / R20)
- ✅ **MUST** use `PublicContracts/` facade if H0.5 needs AI capability; MUST NOT inject `IActionResolver` / `IActionRunner` directly
- ✅ **MUST** use `TimeProvider` over `Stopwatch` in new code (per r3-era gates + testing standards)
- ✅ **MUST NOT** downcast `IDataverseService` (use `UnwrapServiceClient` extension per r3 task 028)

### Existing Patterns

- `Services/Jobs/` + `Services/Ai/Jobs/` — 13 production `IJobHandler` implementations serve as pattern exemplars (§11.3)
- `IdempotencyService` (Redis) — 3-level idempotency proven; reuse for provisioning handlers
- `Services/Registration/*Service.cs` — `DemoProvisioningService` (9-step) is direct pattern for H11; `RegistrationDataverseService` cross-env token cache + multi-URL ops directly applicable; `GraphUserService` for user creation + UPN + license
- `.claude/skills/deploy-new-release/SKILL.md` — reference model for L3 skill UX (§4.3a.4)
- `.claude/patterns/auth/spaarke-sso-binding.md` — auth binding pattern for H0.5 consent-callback
- `.claude/constraints/bff-extensions.md` §§ F.1–F.3 — asymmetric-registration Tier 1.5 anti-pattern, Fixture-Config-FIRST inspection, Empirical-Reproduction-FIRST protocol; load before any BFF DI addition
- `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` § "KV-Secret & Resource Naming Standard (Conformance-Gated)" — Phase G/H canonical naming
- `scripts/naming-conformance-check.ps1` — advisory-until-remediated gate; H13 invokes
- `Infrastructure/Auth/GraphAppRoles.cs` (r3 task 062) — 14-role compile-time constant; H10 grants + verifies parity

---

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>N</spaarkeai>
  <ci-workflows>Y</ci-workflows>
  <skill-directives>Y</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**BFF = Y** — Phase D adds `POST /api/onboarding/consent-callback` endpoint; Phase E migrates `DemoExpirationService` off `[Obsolete]` options; Phase A completes 11 of 14 null `AppRoleId` GUIDs in `GraphAppRoles.cs`. Placement Justification per CLAUDE.md §10: **all four load `.claude/constraints/bff-extensions.md`**, use `PublicContracts/` facade if AI needed, verify publish size ≤ 60 MB per PR (NFR-01), update `tests/unit/Sprk.Bff.Api.Tests/` per §10 bullet 6, no new HIGH CVEs (NFR-02). Provisioning handlers register in **L2 control-plane service, not BFF** (§5.2 — BFF DI impact strictly bounded).

**SpaarkeAi = N** — no code-page changes; provisioning is server-only + operator-tool scope.

**ci-workflows = Y** — Phase H requires coordinated PR with `ci-cd-unit-test-remediation-r1` per r3 `task-042-063-ci-gate-wiring-deferral.md` for naming-conformance + tenant-isolation ArchTest wiring. Do NOT edit `.github/workflows/**` in isolation.

**skill-directives = Y** — Phase D creates `.claude/skills/provision-environment/SKILL.md` (new operator skill; L3 delivery).

**root-claude-md = N** — no new binding rules introduced; existing §10 BFF Hygiene + §11 Component Justification + §6.5 ADR Tensions apply as-is.

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| **L2 control-plane .NET 10 App Service** (`src/server/services/Sprk.Provisioning.ControlPlane/**`) | None. `Sprk.Bff.Api` is per-customer; provisioning is fleet-scoped. `DemoProvisioningService` in BFF is user-provisioning only, not multi-phase orchestration. | No — BFF's per-customer nature + ADR-010 DI minimalism + §10 BFF Hygiene binding-rule preclude adding fleet orchestration to BFF. | Without L2, provisioning stays 3-generation fragmented (13 known issues per ENV-GUIDE); no unified state; operator manually merges 3 docs per §2. |
| **`Cosmos DB spaarke-provisioning` database** (`runs` container, partition `/customerId`) | None. BFF's Cosmos runtime container is per-customer + tenant-scoped runtime data (AI sessions, prompts, audit). ProvisioningRun is fleet-scoped orchestration state — different concerns. | No — mixing runtime tenant data with orchestration state breaks §4D I3 partition scoping + Cosmos partition-key discipline. | Without dedicated container, either share BFF Cosmos (breaks I3) or invent state file (breaks I5 concurrency, I6 crash recovery, R20 execution model). |
| **`/provision-environment` Claude Code skill** (`.claude/skills/provision-environment/SKILL.md`) | `/deploy-new-release` skill is release deployment (different lifecycle stage). | No — provisioning has different phases, gates, error semantics (§4C), auth flow (§4.3a.2). Extending `/deploy-new-release` would violate its single-purpose contract. | Without L3 skill, operator invokes L2 REST manually + tracks 19-handler state by hand + loses fallback matrix for MCP disconnects (§4.3a.5). |
| **`infrastructure/bicep/modules/uami.bicep`** (Phase C) | `app-service.bicep` currently emits System-Assigned MI (`identity: enableManagedIdentity`). | No — extending System-Assigned MI cannot bind one identity to both slots; T5 slot-swap trap intrinsic. UAMI is the pre-req structural fix. | Without UAMI, T5 slot-swap 503 window persists forever + task 011 #3b (BFF shared-lib ClientSecret→MI migration) blocked (needs stable identity that outlives App Service). |
| **`infrastructure/bicep/stacks/model1-shared.bicep`** | `customer.bicep` / `model2-full.bicep` are dedicated-stamp composition. | No — Model 1 tier requires SHARED App Service Plan + OpenAI + AI Search per §3A A1; extending model2 with conditionals reintroduces the shared-vs-dedicated conditional-registration anti-pattern (§10 §F.1). | Without Model 1 stack, Spaarke either forces trial/SMB prospects through Model 2 cost floor (uneconomic per PROJECT-UPDATE §5) or refuses to serve them (business loss). |
| **`infrastructure/bicep/platform-controlplane.bicep`** | `platform.bicep` v2 held per-customer AI resources (moved out per D12). | No — rebuild is the design intent (D12); shrinking platform.bicep = renaming + re-scoping. | Without rebuild, per-customer AI resources stuck in platform.bicep violates D3 dedication + D12 control-plane placement. |
| **Canonical secret-catalog manifest** (`scripts/canonical-secret-catalog/**`) | 4 separate outputs today (seeder, Configure script, tokens doc, Bicep KV secret set) each hand-maintained → drift. | No — single-source generation is the design intent (r3 Phase 3b Phase H). | Without manifest, 4-way drift persists indefinitely (already caused 3 AI-Search-key aliases in 3 casings + 6 orphan template references per §7.9 rename map). |
| **`POST /api/onboarding/consent-callback`** BFF endpoint (H0.5, D18) | None. BFF has no onboarding-consent-flow surface today. | No — this is the ONE irreducible customer-tenant admin action; the callback IS the extension point. | Without endpoint, Model 2 self-service impossible — every Model 2 provision requires manual operator ceremony for consent + `tid` capture. |
| **`POST /api/runs/{id}/clear-quarantine`** L2 endpoint (§4C) | None. Quarantine state is new v3.2. | No — direct Cosmos edit would bypass audit-log + concurrency guard. | Without endpoint, `Quarantined` runs stuck indefinitely (blocks new runs against same `customerId` per §4C); operator has no reversible clearing mechanism. |
| **State-reconciler `BackgroundService` in L2** | None. Custom state machine over Cosmos (§5.4 Option A). | No — Durable Functions / Temporal are the rejected alternatives (§5.4 Options B/C); their fixed cost > our single-digit-per-day cadence marginal cost. | Without reconciler, DAG doesn't advance (state stuck at completed-handler); Model 2 30-min handlers hit App Service 230s HTTP timeout (R20). |
| **11 new columns on `sprk_dataverseenvironment`** (`sprk_currentrunid`, `sprk_tenancymodel`, `sprk_tenantid`, `sprk_bffversion`, `sprk_solutionversion`, `sprk_ClientCacheBustToken` + 5 existing v2 additions) | Existing 16-column entity per r1 registration flow. | Extending IS the pattern — 5 columns are net-new v3.3 (for I5 concurrency, §3A A1 tenancy, D18 tenant, §14A version compat, §14A upgrade cache-bust). | Without columns, no per-customer serialization (I5), no tier-driven Bicep composition, no upgrade version-compat matrix enforcement. |
| **5 new ArchTests** (`tests/Spaarke.ArchTests/**` — I1 no-hardcoded-tenant, I2 tenantId-filter, I3 partition-key, I4 SPE-container-resolver, I5 Graph-per-tenant-token) | Existing ArchTest suite (`GodClassGuardTests` per r3 task 040, ADR-013 injection guards, layer-violation guards). | Extending IS the pattern — 5 new tests join the existing suite; sequence into r3 forcing-functions via CI-wiring coordinated PR. | Without ArchTests, tenant-isolation invariants (§4D) drift; next PR introduces cross-tenant leak (CATASTROPHIC severity per §4D I2/I4/I5). |
| **`docs/deployment/version-compatibility-matrix.md`** (§14A.3) | None. Version matrix is new v3.3. | No — no existing publication surface for BFF × Solution version pair compatibility. | Without matrix, H0 upgrade-mode preflight has nothing to query; incompatible BFF/solution pairs deploy + fail with silent-trap galore. |
| **`docs/deployment/customer-comms/**`** (§14A.4 U-CB-1..6 templates) | None. Customer-communication templates are new v3.3. | No — no existing template surface. | Without templates, U-CB breaking-change deploys blindside customers (severity: customer trust). |

---

## ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

Anticipated conflicts between this project's design and existing ADR rules. Each surfaced at design time so it can be resolved as A (exception) / B (amendment) / C (comply) rather than discovered as silent violation at code-review.

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-004** (async job contract) | "MUST NOT use Durable Functions" + "one message, one handler, one outcome" | The **L2 control plane** is multi-phase, stateful, gate-dependent orchestration — arguably at the edge of what ADR-004 anticipates. The custom state machine + reconciler pattern is neither Durable Functions (rejected §5.4 Option B) nor a single IJobHandler. | **A (documented exception at scope: L2 orchestration only)** | ADR-004 applies at two levels (§5.1): individual handlers CONFORM (each is a self-contained IJobHandler); L2 orchestration is a NEW component pattern that ADR-004 doesn't cover but doesn't prohibit either. Rejected alternatives (§5.4): Durable Functions (would violate B2 App-Service-parity + adds replay-model cognitive tax); Temporal (over-investment for single-digit-per-day cadence). Custom state machine is ~500–800 LOC net-new in L2 + reuses existing `IJobHandler` infrastructure. Documented in §5.1 + §5.4 + this row. |
| **ADR-010** (BFF DI minimalism) | BFF DI at 269 registrations already (17× the 15-line limit — acknowledged violation) | Adding H0.5 consent-callback endpoint + DemoExpirationService refactor increments BFF DI. | **C (comply — additions are minimal + placed in dedicated Onboarding/Endpoints folder)** | Provisioning handlers register in **L2 control-plane service, not BFF** (§5.2). BFF-side additions strictly bounded: single `IOnboardingHandler` for H0.5 + HMAC verifier (~2 registrations); `DemoExpirationService` refactor is an existing-service modification (0 net-new registrations). Placement Justification (§17) enforces bounds. |
| **ADR-013** (AI facade `PublicContracts/`) | CRUD code must not inject `IActionResolver` / `IActionRunner` / `IOpenAiClient` / `IPlaybookService` directly | H0.5 consent-callback + L2 handlers may need AI capability (e.g., analyzing customer's initial docs during provisioning acceptance). | **C (comply — use `Services/Ai/PublicContracts/` facade if needed)** | Per r3 forcing-functions ArchTest (task 040), any AI inject in CRUD code fails PR. H0.5 doesn't currently need AI (pure consent-capture). If H13 acceptance sample analysis needs AI, invoke via `PublicContracts/` facade. |
| **ADR-032** (Null-Object kill-switch) | Conditional service registration in `if (flag) { ... }` DI blocks triggers §10 §F.1 asymmetric-registration anti-pattern | Optional SignalR resource (`Notifications:SignalRSpine:Enabled`) is feature-gated. | **C (comply — ADR-032 P1/P2/P3 pattern)** | Any conditional service in L2 follows ADR-032 pattern; SignalR resource is feature-gated at Bicep + BFF DI level via Null-Object per ADR-032. r3 forcing-functions ArchTest enforces. |
| **ADR-038** (testing strategy — coverage never a gate) | Test-modifying tasks trigger unconditional `code-review + adr-check` per root CLAUDE.md §8 | 5 new ArchTests + integration tests for L2 control-plane will trigger the override row. | **C (comply — expected + welcome)** | ADR-038 rigorly aligns with r1's test approach: integration-heavy, KEEP paths, `TimeProvider` over `Stopwatch`, no mocks of `HttpMessageHandler`. r1's 5 new ArchTests (I1–I5) follow the pattern. |
| **ADR-027** (subscription isolation) | "One Azure subscription per customer = isolation + billing unit" | Model 1 (§3A A1) shares fixed-floor resources across trials in one Spaarke platform subscription. | **A (documented exception at scope: Model 1 shared-tier only)** | D3 (v3) rewrites the tenancy model to explicitly include both tiers. Model 1 shared-floor resources sit in Spaarke platform sub with logical `tenantId` isolation (§4D I2/I3 invariants enforce). Model 2 preserves ADR-027 strict per-customer subscription. Documented in D3 + §3A A1 rationale + §12 R19 concurrency-limits risk. |
| **ADR-014** (data model — session isolation) | `spaarke-session-files` requires `tenantId` + `sessionId` dual-filter invariant | FR-29 (I2) requires unconditional `tenantId` filter on ALL AI Search queries — including `spaarke-session-files`. | **C (comply — I2 strengthens ADR-014, doesn't conflict)** | I2 is a superset guarantee: ADR-014 requires `tenantId + sessionId` on session-files; I2 requires `tenantId` on all indexes. Full compliance = both filters present. ArchTest FR-29 explicit-checks. |

No further tensions anticipated at design time. Section may be updated if tensions emerge during implementation. See CLAUDE.md §6.5 for the three resolution paths.

---

## Success Criteria

Per design.md §15 (v3.3, 22 items). North star: automated provisioning ≤ 1h pipeline runtime; customer production-ready within 1 business day of admin consent + Azure quota in place.

1. [ ] **Doc consolidation** — one authoritative deployment guide covers all provisioning phases + one validated env-var/app-setting manifest reconciled to BFF code `[Required]` annotations (Gap 4) — Verify: grep for orphan guides in `docs/guides/*DEPLOY*.md` returns only the authoritative + explicit stubs
2. [ ] **19 idempotent handlers** — each of H0, H0.5, H1, H2a/b, H3–H11, H12a/b/c, H13, H14 idempotent, independently testable, reports outcome to Cosmos run record — Verify: integration test runs each handler twice; second run is no-op (idempotency key match)
3. [ ] **L2 sequencing + serialization + crash recovery** — control plane sequences handlers per DAG, manages gates, enforces per-customer serialization (I5), auto-resumes orphaned runs on startup (I6) — Verify: concurrent-run test returns 409; crash-restart test resumes from `currentPhase`
4. [ ] **Gap 3 fully automated** — Entra app-reg (14 grants, idempotent), SPE container type (confidential-client per T6), Dataverse App User (interim PPAC + Graph SDK, target TF-driven per D14), Model 2 consent-capture (D18) all run unattended — Verify: E2E dry run has zero manual steps for Gap 3 items
5. [ ] **E2E acceptance** — brand-new environment reaches `Setup Status = Ready` via new pipeline; extended `Validate-DeployedEnvironment.ps1` exits 0 asserting sample analysis + sample document upload+index + workspace-layout render + wizard field-map — Verify: `trial-{yyyymmdd}` stamp on Model 1 profile in Phase F
6. [ ] **6 silent-fail traps cleared** — §4B T1–T6 verified by owning handler's post-condition — Verify: H13 reports each trap check with 0-failure status
7. [ ] **DemoExpirationService clean** — `[Obsolete]` `DemoProvisioning__Environments__*` + `__DefaultEnvironment` deleted from Azure; expiration flow verified working (R5) — Verify: `az webapp config appsettings list` shows no `DemoProvisioning__Environments*` keys
8. [ ] **Operator skill functional** — `/provision-environment` executes full flow with confirmation gates + produces handoff report at `runs/{runId}.md` — Verify: manual invocation on trial stamp produces report + updates registry
9. [ ] **Fleet visibility** — ProvisioningRun records in Cosmos queryable for fleet status (count, state); `sprk_currentrunid` visible on `sprk_dataverseenvironment` — Verify: `az cosmosdb sql query` returns aggregate; MDA view shows in-flight runs
10. [ ] **7 canonical AI Search indexes per customer** — files/discovery/records/rag-references/insights/session-files/invoices created; per-index invariant verifier passes; `spaarke-playbook-embeddings` + `spaarke-knowledge-index` NOT provisioned — Verify: `Deploy-AllIndexes.ps1` verifier exit 0
11. [ ] **7 per-customer Dataverse env vars** — all 7 per §10.3 set + validated (no hardcoded URL fallbacks) — Verify: client startup fails fast if any missing; H7 idempotency test
12. [ ] **Model 2 dedicated stamp** — per-customer AI resources (OpenAI, AI Search, Doc Intelligence, Cosmos) deployed isolated; Redis NOT per-customer — Verify: `az resource list` shows dedicated named resources; no `redis-*` per-customer resource
13. [ ] **Model 1 shared trial** — `model1-shared.bicep` deploys shared fixed-floor tier; per-tenant token-metering (D19/A2) enforces `tokenBudgetMonthlyUSD` blocks pipeline over-budget; per-tenant AI Search filter verified on every query — Verify: over-budget attempt returns 429; FR-29 ArchTest passes
14. [ ] **Cost envelope conforms** — Model 2 empty ≤ $400/mo; Model 1 marginal ≤ $430/mo; shared platform floor ≤ $400/mo (per pricing research); deviations > 20% flagged in H13 — Verify: Azure Cost Management report against expected
15. [ ] **BFF publish size** — ≤ 60 MB compressed (net10 baseline 44.96 MB incl PDBs); Phase E + Phase C + H0.5 combined deltas < ~0.5 MB — Verify: per-PR `dotnet publish -c Release` size report
16. [ ] **D20 fail-fast config discipline active** — r3 tasks 060/061/062/017 landed; verified: (a) BFF misconfig fails `/health`; (b) `GraphAppRoles.cs` 14 GUIDs complete (r1 Phase A); (c) ArchTests block IOptions without validation; (d) legacy H4/H10 verification queries retained as safety net — Verify: r3 test suite green + Phase A audit
17. [ ] **Naming compliance** — `scripts/naming-conformance-check.ps1` exits 0 on r1-owned surfaces post-Phase G; canonical vault + secret naming at H4 + Bicep param; `spaarke-spekvcert` DO-NOT-RENAME dev exception codified — Verify: `naming-conformance-check.ps1` output = 0 findings on new-customer provision
18. [ ] **Phase H KV federation landed** — canonical secret-catalog manifest = single source for seeder + Configure + tokens doc + Bicep KV set; alias collapse with pre-check evidence in task notes; external-spa + code-pages consume `/config.json` — Verify: manifest generates 4 outputs identically; external-spa fetches `/config.json` at bootstrap; `Dataverse-ClientSecret` + `BFF-API-ClientSecret` intact
19. [ ] **H0 preflight quota check** — fails run before H1 IF OpenAI regional TPM / Dataverse env-creation rate / subscription vCPU / SPE cert-bootstrap insufficient — Verify: intentional over-provision returns preflight failure with quota diagnostic
20. [ ] **§4.2 handler execution model verified** — L2 REST enqueue-and-return-202 under load test; ≥ 30-min handler completes without HTTP timeout; state-reconciler advances DAG correctly — Verify: load test suite green
21. [ ] **UAMI structural fix landed (Phase C)** — `uami.bicep` module created; `app-service.bicep` refactored to consume UAMI + bind both slots; T5 slot-swap smoke test = no cold-start KV-ref failures — Verify: slot-swap ceremony produces zero 503s
22. [ ] **§5.5 inherited r3-era gates green** — every r1 BFF PR passes analyzers-as-errors + god-class ratchet + 4 new ArchTests + config fail-fast + naming-conformance + Graph app-role parity ArchTest — Verify: CI Tier-1 blocking job = green for every r1 PR

---

## Dependencies

### Prerequisites

- **r3 landed** (2026-08-14): tasks 060 (S2S drop), 061 (ValidateOnStart on 24 Tier-1 IOptions), 062 (`GraphAppRoles.cs` constant), 017 (KV federation assessment) all on master ✅
- **.NET 10 baseline** (2026-08-14 cutover): net10 target framework, Graph SDK v6.5.0 / Kiota 2.0 (`ODataError` catch), analyzers-as-errors ✅
- **r1 Phase A escalation**: complete 11 of 14 null `AppRoleId` GUIDs in `GraphAppRoles.cs` via `az` enumeration BEFORE first production customer (H10 blocking)
- **Operator machine setup** (§4.3a.3): pwsh ≥ 7.4, az ≥ 2.60, pac ≥ 1.35, git ≥ 2.40; operator has `Operator` app-role on L2 control-plane app-reg
- **Owner directive #3** confirmed (2026-08-15): KV federation full scope in r1 (Phase G + Phase H); UAMI migration in r1 (Phase C); no live-dev remediation
- **Q5 research spike** (2026-08-16): confirms Graph v6.5.0 + SPE patterns current + TF Power Platform provider v4.1.0 validates D14; no stale-pattern blockers ✅

### External Dependencies

- **Azure OpenAI regional TPM quota** — 150+200+30+350 sum per model per region; H0 preflight checks headroom (Phase A verification item: ensure `az cognitiveservices` returns quota per model per region; lead time up to 1–3 days for quota bumps per §15 north star)
- **Azure subscription quota** — vCPU per SKU per region; verified in H0
- **Dataverse environment-creation rate** — ~4/hour per tenant typical; H0 checks with `pac admin quota`
- **SPE container-type replication** — up to 24h lead-time (T6 / R1); customer prereq checklist item — NOT in-pipeline wait
- **Customer admin consent** (Model 2) — one-time customer action; H0.5 captures; customer-dependent lead time
- **Exchange RBAC-for-Applications** (R22) — Microsoft's Aug 2026 recommended pattern; NOT a hard cutover; coexistence safe with current `Set-ApplicationAccessPolicy`; Phase D backlog for r2
- **`ci-cd-unit-test-remediation-r1` worktree coordination** — owns `.github/workflows/**` for 28-day window; Phase H CI-wiring is a coordinated PR (per r3 `task-042-063-ci-gate-wiring-deferral.md`)
- **19 active BFF worktrees** (per r3 handoff §7) — `/conflict-check` before every BFF PR

---

## Owner Clarifications

Answers captured during v3.3 review round (2026-08-16 Q1–Q7). Design already incorporates each; this table is the audit trail.

| Topic | Question | Answer | Impact |
|---|---|---|---|
| **State machine** (Q1) | Custom state machine vs Durable Functions vs Temporal for L2? | Custom over Cosmos + IJobHandler + reconciler | ADR Tension row 1 (Path A exception at L2 scope); §5.4 expanded trade-off table |
| **Solutions count** (Q2) | ~10 solutions or 8? What are the other ~28 in `src/solutions/`? | **8 authoritative** per `Deploy-DataverseSolutions.ps1 $SolutionImportOrder`; other ~28 are code pages via `Deploy-Release.ps1` Phase 4 | FR-09 (H6 ships 8, not 10); §11.1a reconciliation; Phase A audits ~28 |
| **Upgrade model** (Q3) | How do existing customers receive updates? | 3 upgrade classes (U1 BFF / U2 solutions / U3 Bicep) + per-handler upgrade-mode + version compat matrix + 6 breaking-change classes + drift detection | FR-34 (upgrade mode); §14A entire section; version-compat matrix + customer-comms templates as new components |
| **Operator toolchain** (Q4) | What tools does Claude Code actually need to execute /provision-environment? | 15-tool matrix + auth flow + prereqs check + fallback matrix | FR-25; §4.3a entire section; operator's own AAD identity (not SP); Phase D delivers skill |
| **Standards spike** (Q5) | Are our patterns still current per Aug 2026 Microsoft standards? | Yes — zero stale-pattern blockers; 3 follow-ons folded (R22 Exchange RBAC, R23 MI-as-FIC, H8 footnote) | R22 + R23 risks added; H8 SPE-privilege footnote; researcher's pinned memory `graph-spe-standards-2026-08-16.md` |
| **Tenant isolation** (Q6) | How do we prevent cross-tenant data bleed? | 5 binding invariants (I1–I5) enforced via 5 new ArchTests + code fix to `Register-EntraAppRegistrations.ps1:63` | FR-28..FR-32; §4D entire section; 5 ArchTests as new components; script fix committed in `1834b77bc` |
| **Identity surface** (Q7) | What's the one-page mental model for a customer environment's identity + config? | 14-row table showing artifacts + provisioning + verification + rotation + Model 1 vs Model 2 differences | §9A entire section (single-page reference) |

Earlier design rounds (v2, v3, v3.1, v3.2) resolved Q1–Q6 → D12–D17; B1–B5 / I1–I3 / I5–I6 / D3-tension / Tooling / Self-service; D18 / D19 / D20 (fail-fast); Fable review H-1/H-2/H-3/M-1..M-9. All documented in §16 + §20 CHANGELOG.

---

## Assumptions

Items where owner did not explicitly specify — proceeding with these:

- **Rigor level default**: task-execute FULL for all `bff-api` / `plugin` / `auth`-tagged tasks; STANDARD for pure Bicep/PS tasks; MINIMAL for pure docs — will confirm at task-create time
- **Model tier default**: Sonnet 5 @ high for execution per root CLAUDE.md §8.5; Opus 4.8 / Fable 5 for high-blast-radius tasks (Phase C UAMI migration, Phase H canonical manifest generator, L2 control-plane scaffold) — will be assigned in `task-create` Step 3.5.5b
- **Test-modifying override**: any task touching `tests/**` or tagged `testing`/`test-reset`/`deletion`/`integration-test` triggers unconditional code-review + adr-check at Step 9.5 per root CLAUDE.md §8 (added 2026-06-26 by ci-cd-unit-test-remediation-r1 spec FR-B07 + ADR-038)
- **`/conflict-check` frequency**: per BFF PR + before every merge to `work/customer-provisioning-orchestration-r1` from another worktree
- **Phase F trial-environment cleanup**: discretionary post-acceptance (not automatic teardown; D17 decommission remains out of scope)
- **Version-compatibility matrix maintenance**: authored by task-execute at each release-tag milestone; not auto-generated (Phase A produces initial version)
- **U-CB customer-comms templates**: 6 templates authored in Phase A alongside version-compat matrix; text-only markdown, no HTML/branded email

---

## Unresolved Questions

Items still needing owner input before or during implementation:

- [ ] **Force-push origin timing** — owner accepted force-push earlier (paused r1 pointer `9265f1101` will be overwritten). When? Before `/project-pipeline` runs OR after first task lands OR at Phase F acceptance? — Blocks: origin visibility of design work
- [ ] **Model 2 pilot customer commitment** — TF Power Platform provider adoption deferred per M-10 to first-customer engagement. Who / when? — Blocks: FR-08 (H5) + FR-13 (H10) TF migration; Phase D backlog re-scoping
- [ ] **AI in H0.5** — does H0.5 consent-callback need any AI capability (e.g., initial welcome message analysis)? Currently designed pure consent-capture. — Blocks: whether ADR-013 `PublicContracts/` facade injection is needed
- [ ] **Phase H external-spa + code-pages `/config.json` scope** — full closure means editing every external-spa + code-page's bootstrap. What surfaces are IN scope? — Blocks: Phase H task decomposition breadth
- [ ] **`GraphAppRoles.cs` enumeration cadence** — 11 of 14 GUIDs need `az` enumeration. Do this in Phase A (r1 owns) OR request r3 fill in via task 062 follow-up? — Blocks: H10 escalation gate before first production customer
- [ ] **Cost drift H13 threshold** — >20% flags in H13 per §15 #14. Fail run or advisory-only? Currently ambiguous. — Blocks: H13 exit code semantics on cost drift

---

*AI-optimized specification. Original design: [`design.md`](design.md) v3.3 (1,884 lines).*
