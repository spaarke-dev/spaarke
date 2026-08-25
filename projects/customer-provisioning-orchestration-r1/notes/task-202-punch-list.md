# Task 202 Punch List — pre-live-fire lessons audit

> **Task**: 202
> **Author**: 2026-08-24 (SESSION 6)
> **Output-for**: task 203 (executes CLASS-A rows) + separate BFF-owning tasks (CLASS-B rows)
> **Blocks**: task 186 (E2E live-fire) per owner directive 2026-08-24 SESSION 5

## Header summary

| Metric | Count |
|---|---|
| **Total unique lessons** (after dedup across 6 agents / 108 source files) | 62 |
| **Class A (provisioning-owned — task 203 scope)** | 34 |
| **Class B (BFF-owned — routes OUT to `code-quality-and-assurance-r3` or new BFF-quality worktree)** | 22 |
| **Class C (shared/coordination — both projects)** | 6 |
| **Blocks E2E `yes`** | 41 (26 class-A + 12 class-B + 3 class-C) |
| **Blocks E2E `maybe`** | 8 |
| **Blocks E2E `no`** | 13 |

**Verification prerequisite** for task 203: every row below carries a `last_known_status` field from its source document. Task 203 MUST `grep`-verify actual repo state BEFORE applying — many rows may have LANDED via Wave G-8 batch (155–198+) since the source doc was written. Do not double-fix.

**Class distribution rationale**: Class B tasks route to BFF-owning worktrees because provisioning-surfaced BFF bugs (per owner directive 2026-08-24 SESSION 5 BINDING) MUST NOT be fixed on this project's branch. This constraint prevents the "provisioning project doubles as BFF hero-fix branch" anti-pattern. See CLASS-B routing decisions in the "Class B — target-project detail" section below.

---

## SESSION 5 commit `e3a15db91` (IActionSeam hoist) — case study

**Question**: does this commit belong on `work/customer-provisioning-orchestration-r1` or should it be extract-and-relocated to `code-quality-and-assurance-r3`?

### Evaluation

**The fix is CORRECT**:
- Real ADR-032 F.1 asymmetric-registration bug: `IActionSeam` registered inside compound `if (DocIntel && Analysis)` gate at `AnalysisServicesModule.cs:1425`
- `CommunicationRiActionService` requires `IActionSeam` UNCONDITIONALLY (`CommunicationModule.cs:195`)
- Hoisted registration to top-of-module unconditional block (~line 160), matching precedent for `IPinnedContextRepository` / `IContextEventEmitter` / `IFileSummarizeAi`
- Rebuilt 0/0 warnings/errors; published 47.15 MB (under 60 MB NFR-01 ceiling); deployed live to `sprksharedprod-api`; gate cleared

**The routing is DEBATABLE**:
- BINDING owner directive 2026-08-24 SESSION 5: provisioning-surfaced BFF bugs MUST route to BFF-owning projects
- SESSION 5 was diagnosing Model 1 Prod BFF SIGABRT chain — provisioning-surfaced discovery
- Fix touches `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` (BFF code)
- `code-quality-and-assurance-r3` is the active BFF-decomposition worktree per project CLAUDE.md coordination table

### Decision: **KEEP-IN-PLACE + FILE class-B follow-on task**

Rationale:
1. Extraction cost: revert on `work/customer-provisioning-orchestration-r1` + cherry-pick to `code-quality-and-assurance-r3` + coordinate merge sequencing so BFF branch merges BEFORE this project's E2E. Non-zero risk of orphan state.
2. Task 186 E2E live-fire depends on `IActionSeam` fix being live in Model 1 Prod BFF today. Extraction would create window where fix is not present.
3. The commit is atomic + well-documented (see message + `current-task.md` SESSION 5 block). Reviewer can audit it in place on either branch.
4. The MISSING piece is not the fix location — it's the **ArchTest gap** the bug revealed. Existing ADR-032 forcing-function ArchTest catches "kill-switch declared" pattern but NOT "unconditional consumer + conditionally-registered dependency" pattern.

**Class-B follow-on to file in `code-quality-and-assurance-r3`** (or dedicated new BFF-quality worktree):

- **Task**: Add ArchTest `Spaarke.ArchTests.ADR032.AsymmetricRegistrationTests.UnconditionalConsumerMustHaveUnconditionalDependency` that scans all `*Module.cs` DI files for pattern: service registered inside `if (flag)` block AND some other unconditionally-registered service's ctor injects it → FAIL with the specific asymmetric pair.
- **Scope**: `src/server/api/Sprk.Bff.Api/Infrastructure/DI/**/*Module.cs`
- **Effort**: ~4-6h (ArchTest pattern already exists per ADR-032 P1/P2/P3; needs new predicate).
- **Blocks E2E**: NO (existing bug fixed; ArchTest prevents recurrence, not blocking).
- **Cross-ref**: this punch list + `e3a15db91` commit message + `bff-extensions.md` § F.1 (asymmetric-registration Tier 1.5 anti-pattern).

**Coordination**: `code-quality-and-assurance-r3` owner runs `/conflict-check` against active BFF worktrees; this ArchTest task has zero code overlap with in-flight decomposition work (adds a new test file only).

---

## Punch list — CLASS A (provisioning-owned, task 203 scope)

Format: `{lesson-id | title | landing-spot | effort-h | blocks-e2e | dependencies | last-known-status | source-file}`

Sorted by `blocks-e2e DESC`, then `effort ASC` within each group.

### A-blocks-E2E YES (26 rows) — task 203 must apply BEFORE task 186

| # | lesson-id | title | landing-spot | effort-h | deps | last-known-status | source |
|---|---|---|---|---|---|---|---|
| A01 | prereqs-file-authored | Author `docs/guides/PROVISIONING-PREREQUISITES.md` + `scripts/provisioning-prereqs/prereqs.yaml` | prereqs.md + yaml | ✅ DONE (this task 202) | none | Applied 2026-08-24 | task 202 |
| A02 | skill-step-0.5-external-prereqs | Extend `/provision-environment` Step 0.5 to read `prereqs.yaml` + iterate check recipes + HARD STOP on failure | skill | 6 | A01 | not-applied | `.claude/skills/provision-environment/SKILL.md` |
| A03 | skill-step-1-batch-mode | Add `--batch` flag + JSON Schema at `scripts/provisioning-prereqs/intake.schema.json` | skill + json-schema | 4 | A02 | not-applied | `provisioning-run-agent-autonomy-design.md` |
| A04 | skill-step-7-postmortem | Author mandatory Step 7 (writes `lessons-learned.md` per run) | skill | 3 | A05 | not-applied | `provisioning-run-structure-design.md` |
| A05 | provisioning-runs-root | Create `provisioning-runs/` root + `INDEX.md` + `_archive/` folder | new folder | 2 | none | not-applied | `provisioning-run-structure-design.md` |
| A06 | provisioning-runs-templates | Author per-run 8-file templates (CLAUDE.md / intake.md / prerequisites-check.md / preflight-report.md / handler-log.md / manual-gates.md / handoff-report.md / lessons-learned.md) | templates in `provisioning-runs/_templates/` | 4 | A05 | not-applied | `provisioning-run-structure-design.md` |
| A07 | patterns-provisioning-content | Fill 9 skeleton pattern files under `.claude/patterns/provisioning/` (skeletons created by A20) | 9 .md files | 6 | A20 | not-applied | `provisioning-run-structure-design.md` |
| A08 | constraint-provisioning-authored | Author `.claude/constraints/provisioning.md` + wire into `task-execute` Step 4a tag map | 2 files | 3 | A07 | not-applied | `provisioning-run-structure-design.md` |
| A09 | c6-1-skill-profile-enum | Skill uses profile=dev/demo/prod; L2 requires `spaarke-hosted-model1-trial` / `spaarke-hosted-model2` / `customer-owned-model2` (400 on wrong value) | skill Step 1 | 1 | none | not-applied per DS-5 | design-study-ds5, r1-gap-analysis c6-1 |
| A10 | c6-2-skill-missing-environmentid | Skill Step 1 must include `environmentId` intake (L2 400s without it) | skill Step 1 | 1 | A09 | not-applied per DS-5 | design-study-ds5, r1-gap-analysis c6-2 |
| A11 | c6-3-skill-registry-prereq | Skill must include placeholder `sprk_dataverseenvironment` create step before POST /api/runs | skill Step 1 | 3 | A10 | not-applied per DS-5 | design-study-ds5, r1-gap-analysis c6-3 |
| A12 | c5.2-jwt-audience-wrong | `platform-controlplane.bicep` `jwtAudience` = `api://spaarke-provisioning-controlplane-{env}` should be `api://spaarke.com/provisioning-controlplane-{env}` (tenant-policy verifier-domain form) | bicep | 1 | none | not-applied per DS-5 | design-study-ds5 c5.2 |
| A13 | c5.1-sb-data-receiver-rbac | L2 UAMI needs Azure Service Bus Data Receiver on SB namespace (Data Sender live-manual today) | bicep | 3 | none | not-applied per DS-5 | design-study-ds5 c5.1 |
| A14 | c5.1-config-key-aliases | Bicep emits `Cosmos__Endpoint` / `ServiceBus__ConnectionString` / no `ManagedIdentity__ClientId`; code reads `Cosmos:AccountEndpoint` / `ServiceBus:FullyQualifiedNamespace` / `ManagedIdentity:ClientId`. Fix Bicep (not code) BEFORE any stamp redeploy | bicep | 4 | none | not-applied per DS-5 + T113 | design-study-ds5, task-113-deviations |
| A15 | c5.8-grant-controlplane-identity-script | Author `Grant-ControlPlaneIdentity.ps1` — grants L2 UAMI Graph app-role assignments (via `az rest servicePrincipals/{spId}/appRoleAssignments`) + Path X Dataverse App User + scoped `Spaarke Provisioning Registry` role | new script | 8 | c5.9 (11 GraphAppRoles null GUIDs completed) | not-applied per DS-5, DS-8 | design-study-ds5 c5.8, DS-8 |
| A16 | c5.9-graphapproles-null-guids | Complete 11 of 14 null `AppRoleId` GUIDs in `GraphAppRoles.cs` via live `az` enumeration BEFORE first production customer | shared/const file | 3 | none | escalation-owed per spec.md MUST rule | spec.md MUST rule + design.md H10 |
| A17 | audit-gap-05-artifacts-storage | Author `infrastructure/bicep/modules/controlplane-artifacts-storage.bicep` + wire into `platform-controlplane.bicep` (H2a + H9 publish workflows both block without it) | bicep | 4 | none | not-applied per audit-gap-05 | post-authoring-audit, h2a-bicep-precompile-ci |
| A18 | audit-gap-10-acr-sidecar-chain | Author `infrastructure/bicep/modules/controlplane-acr.bicep` + wire into `platform-controlplane.bicep` + surface `acrImageTag` / `sidecarAuthType` params (H14a sidecar image cannot deploy without it) | bicep | 5 | none | not-applied per audit-gap-10, sidecar-ci-coord-pr | post-authoring-audit, sidecar-ci-workflow |
| A19 | audit-gap-02-04-l2-uami-rbac | L2 UAMI: sub Contributor (`--assignee {l2Uami} --scope /subscriptions/{sub}`) + Storage Blob Data Reader on artifacts storage + AcrPull on ACR | bicep | 3 | A17 + A18 | not-applied per audit-gaps-02/03/04 | post-authoring-audit |
| A20 | T200-h4shared-uami-rbac | H4-shared handler needs 5-6 UAMI role assignments on shared source services (Cognitive Services User × 2, Search Service Contributor, SB Data Owner, Storage Contributor, Redis Cache Contributor) | bicep | 4 | none | not-applied — task 200 deferred | task-200-completion-notes |
| A21 | T201-h4b-website-contributor | L2 UAMI needs Website Contributor on target BFF App Service (for Kudu docker-log fetch) | bicep | 1 | none | not-applied — task 201 deferred | task-201-completion-notes |
| A22 | T126-frombicepoutput-gap | `KvSecretValueResolver` returns Failed for `FromBicepOutput` entries not already on target vault; H4 quarantines fresh customer — CRITICAL. Wire `kv-secrets.generated.bicep` into `customer.bicep` / `model{1,2}-*.bicep` OR extend InterStepState with H2a outputs OR pre-seed via generated seeder | bicep | 8 | none | not-applied — MAJOR GAP per T126 | task-126-deviations |
| A23 | audit-gap-15-kv-secrets-clobber | `kv-secrets.generated.bicep` clobbers `BFF-API-ClientSecret` + `Dataverse-ClientSecret` on any re-deploy (comment claims skip-if-exists, code is unconditional ternary) — BINDING never-delete violation | bicep | 4 | none | not-applied per audit-gap-15 | post-authoring-audit |
| A24 | audit-gap-16-healthcheckpath | `app-service.bicep` defaults `healthCheckPath=/health` but BFF maps `/healthz` (staging slot default is /healthz — asymmetric). Prod instances marked unhealthy | bicep | 1 | none | not-applied per audit-gap-16 | post-authoring-audit |
| A25 | wave-4-drift-5-sharedbff-uami-kv-user | KV Secrets User grant on `sharedBffUami` never wired in `model1-shared.bicep` — Model 1 BFF cannot resolve `@Microsoft.KeyVault(...)` refs when Model 1 tier moves to acceptance | bicep | 2 | none | not-applied per wave-4-drift-5 | wave-4-drift-5 |
| A26 | T108-queue-recreate-ceremony | `sprk-provisioning-jobs` queue: session receiver requires `requiresSession=true` + `requiresDuplicateDetection=true` (creation-time-only). Live queue lacks both. Bicep authored per task 108; live delete-and-recreate DEFERRED. RUN CEREMONY per `notes/queue-recreate-runbook-2026-08.md` | operator runbook | 2 | A13 | authored-not-executed per T108 | task-108-deviations, DS-2 |

### A-blocks-E2E MAYBE (5 rows)

| # | lesson-id | title | landing-spot | effort-h | last-known-status | source |
|---|---|---|---|---|---|---|
| A27 | c5.6-customer-run-guard | `CustomerRunGuard` config (`TargetDataverseUrl`/`TenantId`/`ClientId`/`ClientSecret`/`Enabled=true`) never provisioned; I5 serialization currently NOT enforced at runtime — silent | infra-bicep + code | 4 | not-applied per r1-gap-analysis c5-6 | r1-gap-analysis |
| A28 | c4.5-runstatus-serialization | `RunStatus`/`GateState`/`QuarantineState` need DUAL `[JsonConverter]` (STJ + Newtonsoft) — Cosmos writes as int, reconciler queries as string, 0 rows forever | code | 2 | LIKELY LANDED via T106 completion runbook — VERIFY | task-106-serializer-fix-runbook, DS-2 |
| A29 | c1-3-handler-execution-environment | Handler runtime (sidecar approach adopted per G-1 tasks 114/115/162); ACR chain still missing (audit gap #10 above). Load-bearing | code + bicep | subsumed by A18 | partial | r1-gap-analysis c1-3 |
| A30 | c1-4-l2-registry-client | Real L2 Dataverse registry client (Path X) tasks 177/184 landed per T177/T184 completion notes; sentinel-for-FIC-migrated-envs contract still open | design.md | 2 | partial per r1-gap-analysis c1-4 | r1-gap-analysis, AUTH-V4-RESPONSE h4-h7-fic-sentinel-contract |
| A31 | h9-artifact-manifest-graph-parity-live | H9 handler MUST perform its OWN live per-environment Graph parity check (manifest key is `Skipped` by design) — task 132 re-scope | code | 4 | not-applied per h9-artifact-publish-ci-coord | h9-artifact-publish-ci-coord-pr |

### A-blocks-E2E NO (3 rows — nice-to-have; can land AFTER 186)

| # | lesson-id | title | landing-spot | effort-h | last-known-status | source |
|---|---|---|---|---|---|---|
| A32 | c6.6-skill-h13-write-semantics | Skill Step 6 must change from `write sprk_setupstatus=Ready` to `read-verify` once C3.3 lands (H13 authoritative writer) | skill | 1 | not-applied per r1-gap c6-6 | r1-gap-analysis |
| A33 | h9-workflow-dispatch-cadence | Runbook must document "BFF-artifact workflow MUST run before customer provisioning" OR add `workflow_run` follow-on trigger | operator-runbook | 1 | partial per h9-artifact-publish | h9-artifact-publish-ci-coord-pr |
| A34 | sc-11-envvar-checks-skipped | SC #11 dev-leakage + env-var presence checks skipped; L2 lacks Dataverse identity on H13 envelope | code | 3 | not-applied per audit-gap-22 | post-authoring-audit |

---

## Punch list — CLASS B (BFF-owned — route OUT of task 203)

**Routing target**: `code-quality-and-assurance-r3` (active BFF-decomposition worktree) OR new dedicated BFF-quality worktree if class-B count exceeds `code-quality-and-assurance-r3`'s scope.

Per constraint from task 202 POML (BINDING per owner 2026-08-24):
> When task 202's audit surfaces a lesson that requires editing `src/server/api/Sprk.Bff.Api/**` (or `src/server/shared/Spaarke.*/**`), it does NOT go into task 203's scope. Instead: (a) file the bug as a separate task in `code-quality-and-assurance-r3` … (b) require accompanying ArchTest that prevents the class-of-bug at build time; (c) coordinate merge sequencing so the BFF fix lands BEFORE task 186 E2E live-fire.

### B-blocks-E2E YES (12 rows)

| # | lesson-id | title | landing-spot | effort-h | target-project | source |
|---|---|---|---|---|---|---|
| B01 | e3a15db91-archtest | ArchTest `UnconditionalConsumerMustHaveUnconditionalDependency` (IActionSeam case study — prevents ADR-032 F.1 recurrence) | new ArchTest file | 5 | code-quality-and-assurance-r3 | SESSION 5 + `e3a15db91` commit |
| B02 | ioptions-inventory-drift-archtest | Nightly ArchTest that scans `AddOptions<T>.ValidateOnStart()` in BFF DI and diffs against `per_env_settings` manifest (per T201 deferred + T081.5) | new ArchTest file | 6 | code-quality-and-assurance-r3 | task-201-completion-notes, task-081.5-rollback |
| B03 | dispatcher-does-not-exist | `ProvisioningHandlerDispatcher` (BackgroundService hosting `ServiceBusSessionProcessor`) never written — nothing consumes `sprk-provisioning-jobs` today. THE root gap | code | 24 | code-quality-and-assurance-r3 or NEW BFF-quality worktree | design-study-ds2 |
| B04 | multi-tenant-dv-routing-gap | `DataverseServiceClientImpl.cs:39` reads ONE `Dataverse:ServiceUrl` at DI setup, contradicts Model 1's per-tenant Dataverse design. Design question: Model 1 shared BFF serves ONE Dataverse (design walkback) OR H5 creates per-tenant Dataverse envs needing runtime routing (architectural refactor)? File as ADR Tension via §6.5 path B | design decision + code | 16-40 (depends on path A/B/C) | ADR tension + BFF worktree | SESSION 5 Lesson 3 |
| B05 | h4-writes-literal-placeholder-values | `AzCliKvSecretsWriter.ResolveValueForEntry` writes literal `{name}-interim-placeholder-{customerId}` strings; every downstream KV consumer receives placeholder secrets | code | 4 | code-quality-and-assurance-r3 | design-study-ds4 |
| B06 | h1-null-probe-fakes-arm-check | `NullSubscriptionReadinessProbe` returns `Passed` with no ARM call; FR-03 verification is fictional (~150-250 LOC to implement real probe: `ARM.Resources` subscription GET + Lighthouse `registrationAssignments` list) | code | 12 | code-quality-and-assurance-r3 | design-study-ds4 |
| B07 | h13-all-placeholders | `PlaceholderTrapVerifier` returns InfraFault for T1-T6; `PlaceholderInvariantVerifier` returns InfraFault for I1-I5; `DataverseRegistrySetupStatusUpdater` returns Success without writing (~1200-1600 LOC of 11 real probes + 3 runner ports + Ready writer) | code | 40 | code-quality-and-assurance-r3 or NEW BFF-quality worktree | design-study-ds4, task-055-deviations |
| B08 | h0.5-null-registry-client | `NullDataverseEnvironmentRegistryClient` always returns null — FR-02 re-consent inert. Requires C1.4 registry client DI-swap | code | 8 | code-quality-and-assurance-r3 | design-study-ds4, task-042-deviations |
| B09 | h12b-2-of-4-scopes-undelivered | FR-16 half undelivered — field-mapping + chart-def seeders are `DeferredAppConfigSeeder` no-ops (~450-600 LOC: 2 mechanical ports + 2 greenfield seeders) | code | 16 | code-quality-and-assurance-r3 | design-study-ds4, r1-gap-analysis c3-8 |
| B10 | h6-h7-credential-config-never-provisioned | `SolutionImport`/`EnvVarValues` `ClientSecret` KV wiring deferred to nonexistent Wave C5; options fields carry `ClientSecret`, KV bindings never provisioned. Decision-coupled to Path X (A15 above) | code | 4 (or delete if Path X wins) | code-quality-and-assurance-r3 | design-study-ds4 |
| B11 | staging-slot-shadow-worker-defect | One-process L2 topology has structural staging-slot shadow-worker defect (reconciler + crash-recovery + dispatcher run against PRODUCTION Cosmos+SB while pre-production build is deployed). Split into .Core+.Api+.Worker OR three permanent slot-sticky `Enabled` flags | code refactor | 16 | code-quality-and-assurance-r3 | design-study-ds3 |
| B12 | h9-deploy-bff-scope-refactor | `H9 Deploy-BffApi.ps1` runs `dotnet publish` at provision time — cannot ship under any runtime option. Re-scope to artifact-fetch + zip-deploy + gates | code | 8 | code-quality-and-assurance-r3 | design-study-ds1 |

### B-blocks-E2E MAYBE (3 rows)

| # | lesson-id | title | landing-spot | effort-h | target-project | source |
|---|---|---|---|---|---|---|
| B13 | recordmatchservice-test-compile | `RecordMatchServiceTests.cs:28,45` fails to compile (CS7036 missing `IConfiguration` param from task 065). Breaks CI signal certifying task 186 | test file | 1 | code-quality-and-assurance-r3 | wave-4-drift-1 |
| B14 | i5-managedid-factory-scope-gap | `ManagedIdentityCredentialFactory` has same TenantId-on-options gap as `GraphClientFactory` but outside I5 ArchTest scope (Infrastructure/Auth vs Infrastructure/Graph) | code + ArchTest | 3 | code-quality-and-assurance-r3 | task-065-deviations |
| B15 | tier1-ioptions-deploy-checklist | Highest-leverage systemic — `AddOptions<T>.ValidateOnStart()` chains have no CI/config-catalog cross-check. Add Tier-1-IOptions deploy checklist to `bff-extensions.md` + CI script | constraint + CI | 6 | code-quality-and-assurance-r3 | task-081.5-rollback |

### B-blocks-E2E NO (7 rows — hygiene / documentation drift)

| # | lesson-id | title | effort-h | target-project | source |
|---|---|---|---|---|---|
| B16 | h2b-reject-retired-lineage | H2b handler needs to reject full retired lineage (not just spaarke-playbook-embeddings) | 2 | code-quality-and-assurance-r3 | ai-search-catalog-audit |
| B17 | bff-appsettings-tokens-drift | `appsettings.tokens.md:29,114` declares `PLAYBOOK_EMBEDDINGS_INDEX_NAME` as active token though zero code consumers post task 035 | 1 | code-quality-and-assurance-r3 | ai-search-catalog-audit |
| B18 | wave-4-c5-refactor-secrets-to-keyvaultsecretref | `EnvVarValuesOptions`/`EnvVarValuesWriteRequest` carry cleartext `ClientSecret`; refactor to `KeyVaultSecretRef` after task 084 lands runtime resolver seam | 6 | code-quality-and-assurance-r3 | wave-4-batch-4a-archtest-debt |
| B19 | doc-drift-armdeploymentrunner | `ArmDeploymentRunner.cs` BLOCKING DISCOVERY header comment stale; customer.bicep DOES have UAMI/AppService/OpenAI now | 1 | code-quality-and-assurance-r3 | post-authoring-audit gap-26 |
| B20 | doc-drift-worker-program | Worker `Program.cs:784-795` comments claim placeholders in use; actually unregistered | 1 | code-quality-and-assurance-r3 | post-authoring-audit gap-27 |
| B21 | shellout-deletion-sweep | ~25 retired shell-out classes remain on disk (AzCli*, *ScriptRunner, PacAdmin*, PowerShellAppConfigSeeder in .Core) — SC #2 literal grep-verify fails | 4 | code-quality-and-assurance-r3 | post-authoring-audit gap-30 |
| B22 | h7-endpoint-429-wireup | `429` response wiring at ~30 AI-consuming endpoints deferred to endpoint-owner tasks (T077 deferred) | 8 | code-quality-and-assurance-r3 or new BFF-quality worktree | task-077-deviations |

---

## Punch list — CLASS C (shared/coordination)

| # | lesson-id | title | landing-spot | effort-h | blocks-e2e | source |
|---|---|---|---|---|---|---|
| C01 | h4-h7-fic-sentinel-contract | Undefined KV-secret contract for FIC-migrated customer (tasks 126/142) — omit vs write documented sentinel. Coord auth-v4 | design.md + code | 4 | maybe | AUTH-V4-CHANGE-REQUEST-RESPONSE |
| C02 | task-130-fic-extension-dep | Wave G-3 task 130 (H3 heavy port) SHOULD invoke auth-v4's extended `Register-EntraAppRegistrations.ps1` rather than duplicate FIC-creation. Coord auth-v4 | code | 2 | no | AUTH-V4-CHANGE-REQUEST-RESPONSE |
| C03 | config-yaml-audience-drift | Stale `sign_in_audience` in `config/spaarke-resources.yaml`; phantom App Service names. Auth-v4 owns; r1 cross-check in Wave G-1 wrap-up | docs | 1 | no | PROVISIONING-CHANGE-REQUEST |
| C04 | fr40-i6-archtest | I6 (OBO app-reg per-tenant derivation) ArchTest — Model 1 only — `Spaarke.ArchTests.TenantIsolation.I6_ObApp*` | ArchTest | 4 | yes | PROVISIONING-CHANGE-REQUEST |
| C05 | prod-ai-search-alias-legacy-backcompat | `Deploy-AllIndexes.ps1` legacy back-compat write path still writes `AzureAISearchApiKey` alias; source-only change; no live prod mutation | scripts | 1 | no | wave-4-drift-2 |
| C06 | spec-design-v3.4-amendment | 16 spec.md + 20 design.md amendments queued to reflect Wave A locked decisions (FR-22 root muddle in prose contradicts MUST rules). Cosmetic but landmine for future work | design.md + spec.md | 4 | no | design-study-ds6 |

---

## Task 203 scope brief

**IN SCOPE for task 203**:
- All **Class A** rows (34 total). Sub-phase by dependency chain:
  - 203a (foundation): A05, A06, A07, A08, A09, A10, A11, A12, A24 (10-15h)
  - 203b (bicep hardening): A13, A14, A17, A18, A19, A20, A21, A22, A23, A25, A26, A27 (30-40h)
  - 203c (skill wiring): A02, A03, A04, A15, A16 (15-20h)
  - 203d (nice-to-have post-186): A32, A33, A34 (5h)
- **Class C** rows requiring provisioning-project decision: C01, C04, C06 (design.md amendments + I6 ArchTest — Class C not Class B because they are project-owned deliverables per task 202 POML §c-c1 constraint).

**OUT OF SCOPE for task 203** (route OUT):
- All **Class B** rows (22 total). Each becomes a separate task in `code-quality-and-assurance-r3` (or new BFF-quality worktree if count exceeds capacity per POML escalation trigger).
- **Class C** rows C02, C03, C05 (auth-v4-owned coordination items — file in auth-v4 project).

**Merge sequencing** (per POML BINDING constraint on Class B routing):
1. Class-A + Class-C-owned work lands on `work/customer-provisioning-orchestration-r1` (task 203).
2. Class-B tasks land on `code-quality-and-assurance-r3` (or new BFF-quality worktree). Coordinate via `/conflict-check`.
3. **Task 186 E2E live-fire prerequisite gate**: All Class-A `blocks_e2e=yes` (A02-A26) applied + all Class-B `blocks_e2e=yes` (B01-B12) applied (via routed tasks) + `RecordMatchServiceTests` (B13) passes so CI signal is trustworthy.
4. Sequence: 203 landing → BFF-worktree class-B PRs merged → this project's branch pulls master (picks up Class-B fixes) → task 186 invokes `/provision-environment` against sub `cd95fcec-6b89-49ea-8339-c2b579b12587`.

**Escalation triggers for task 203** (per task 202 POML §escalation):
- If Class-B lesson count exceeds `code-quality-and-assurance-r3` capacity: initiate new BFF-quality worktree.
- If Class-A lesson count exceeds ~20h for a sub-phase: split into 203a/203b/203c/203d (as tabled above).
- If SESSION 5 Lesson 3 (B04 multi-tenant DV routing) requires path B (ADR amendment): file the tension row in `spec.md` §ADR Tensions FIRST — this is currently missing (per Agent 6 spec+design audit: "the tension row that SESSION 5 Lesson 3 asks about does not currently exist").

**Verification obligation for task 203 execution** (added by task 202 to prevent double-fixing):
- Every row above carries a `last_known_status` from its source doc.
- Task 203 MUST `grep`-verify actual repo state BEFORE applying each row.
- If a row was landed by Wave G-8 batch (tasks ~155-198+), mark row as `already-applied` in the punch-list executed-state annotation + skip apply.
- Recommended verification queries per row (task 203 authors runbook per row).

---

## Related project files (for task 203 executor context)

- [`docs/guides/PROVISIONING-PREREQUISITES.md`](../../../docs/guides/PROVISIONING-PREREQUISITES.md) — codified prereqs (task 202 output)
- [`scripts/provisioning-prereqs/prereqs.yaml`](../../../scripts/provisioning-prereqs/prereqs.yaml) — YAML source of truth (task 202 output)
- [`projects/customer-provisioning-orchestration-r1/notes/provisioning-run-structure-design.md`](./provisioning-run-structure-design.md) — 7-file per-run folder design (task 202 output)
- [`projects/customer-provisioning-orchestration-r1/notes/provisioning-run-agent-autonomy-design.md`](./provisioning-run-agent-autonomy-design.md) — --batch flag + gate classification design (task 202 output)
- [`.claude/patterns/provisioning/INDEX.md`](../../../.claude/patterns/provisioning/INDEX.md) — pattern scaffolding (task 202 output; task 203 fills 9 files)
- [`.claude/skills/provision-environment/SKILL.md`](../../../.claude/skills/provision-environment/SKILL.md) — the L3 operator skill (task 203 extends Step 0.5 + Step 7)
- [`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](../../../docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) — human operator guide (task 203 cross-refs PREREQUISITES + provisioning-runs)
- [`projects/customer-provisioning-orchestration-r1/tasks/186-real-phase-f-e2e-acceptance-rerun.poml`](../tasks/186-real-phase-f-e2e-acceptance-rerun.poml) — E2E live-fire task (task 202 adds pre-check trigger)
