# Task 202 Punch List — pre-live-fire lessons audit

> **Task**: 202
> **Author**: 2026-08-24 (SESSION 6)
> **Amended**: 2026-08-24 SESSION 7 — verification pass + Class-B re-scope (see amendment below)
> **Output-for**: task 203 (executes CLASS-A rows) + task 204 (executes CLASS-B verified-open rows IN THIS PROJECT — routing amendment)
> **Blocks**: task 186 (E2E live-fire) per owner directive 2026-08-24 SESSION 5

## PARALLEL WAVE EXECUTION RESULTS — 2026-08-24 SESSION 7 END

**Wave scope**: 4 parallel-safe tasks (203b + 204a + 204f + 204g) dispatched as background agents; all 4 completed clean. Details per row in linked execution-results files.

### Task 203b — Class-A bicep hardening (12 rows) — see [`task-203b-execution-results.md`](task-203b-execution-results.md)

| Row | State | Commit | Note |
|---|---|---|---|
| A13 | ✅ already-applied | — | SB Data Receiver RBAC landed prior wave |
| A14 | ✅ already-applied | — | Config-key aliases landed prior wave |
| A17 | ✅ already-applied | — | Artifacts storage module landed prior wave |
| A18 | ✅ already-applied | — | ACR module landed prior wave |
| A19 | ✅ already-applied | — | L2 UAMI 3 sub-scope grants landed prior wave |
| A20 | ✅ applied | `3b4f400c5` | New `modules/model1-shared-l2-rbac.bicep` — 6 UAMI roles on Model 1 shared services (closes task 200 Deferred #1) |
| A21 | ✅ applied | `3b4f400c5` + `9eee99de6` | New `modules/customer-l2-bff-rbac.bicep` — Website Contributor on shared (Model 1) + per-customer BFF (Model 2) (closes task 201 Deferred #1) |
| A22 | ⏭️ deferred-architectural | — | Model 2 already applied via `customer.bicep:655` kvSecrets (task 129); Model 1 uses H4-shared runtime per deliberate architectural boundary at `model1-shared.bicep:638-654`. Path A per §6.5. |
| A23 | ✅ already-applied | — | KV-secrets skip-if-absent + BINDING never-declared landed |
| A25 | ✅ applied | `3b4f400c5` | Added `userAssignedIdentityPrincipalId: sharedBffUami.outputs.principalId` to `sharedKeyVault` in `stacks/model1-shared.bicep` (closes wave-4-drift-5) |
| A26 | ⏭️ deferred-live-Azure | — | Requires destructive `az servicebus queue delete` per runbook §7 — beyond safe scope of sub-agent. Human operator executes per runbook, sequenced after task 107 code is live in L2 |
| A27 | ✅ applied | `9eee99de6` | Added 5 `CustomerRunGuard__*` app-settings + 3 params (tenantId/clientId/kill-switch); Enabled=false default per ADR-032 null-object |

### Task 204a — Class-B verify-first (6 rows) — see [`task-204a-execution-results.md`](task-204a-execution-results.md)

| Row | State | Commit | Note |
|---|---|---|---|
| B10 | ✅ applied | `74efa5053` | H6 `SolutionImportOptions__ClientSecret` KV binding wired in `controlplane-worker-app-service.bicep`; H7 was already applied |
| B14 | ✅ already-applied | — | `ManagedIdentityCredentialFactory` already pins `DefaultAzureCredentialOptions.TenantId` (Wave 4 Batch 4D drift-1 to task 065); I5 ArchTest scan dirs include `Infrastructure/Auth` |
| B16 | ✅ already-applied | — | `CanonicalIndexCatalog.cs:40-56` carries FULL retired lineage (7 names); H2b step 5 fires guard for both Model 1 + Model 2 branches → QuarantineRequired |
| B18 | ⏭️ not-applicable | — | Row premise conflates `KeyVaultSecretRef` (Cosmos-persistence safety for `RunParameters.Secrets`) with `EnvVarValuesOptions.ClientSecret` (App Service KV Reference resolves before code sees it). Refactoring would BREAK standard pattern. Documented in `wave-4-batch-4a-archtest-debt.md:50` |
| B20 | ✅ already-applied | — | Line numbers shifted; current `Worker/Program.cs:858-866` says "REAL types resolve from Worker DI, not the placeholders" — comment already correct |
| B22 | 🔔 **ESCALATED — scope-corrected follow-on** | — | Actual endpoint count is 121 methods / 28 files (row said ~30). Runtime 429 already wired via `RateLimitingModule.cs:266` (63 `.RequireRateLimiting` in 19 files). Residual gap is OpenAPI docs (14 files) + policy decisions on 9 non-rate-limited endpoints — NOT the mechanical 8h wiring task the row assumed. Needs scope-corrected follow-on task (new row: **B22-refined**) |

### Task 204f — Docs drift removal (1 row) — see [`task-204f-execution-results.md`](task-204f-execution-results.md)

| Row | State | Commit | Note |
|---|---|---|---|
| B17 | ✅ applied | `0d3ae5c39` | Removed `PLAYBOOK_EMBEDDINGS_INDEX_NAME` from `appsettings.tokens.md`; zero C# consumers confirmed; build clean |

### Task 204g — Spec SC #2 amendment (1 row) — see [`task-204g-execution-results.md`](task-204g-execution-results.md)

| Row | State | Commit | Note |
|---|---|---|---|
| B21 | ✅ applied | `6746e48e6` | SC #2 amended to allow retired-on-disk-with-banner convention + 3-sub-check verification recipe (idempotency + shell-out scan allowing banner-headed files + active-registration scan) |

### Wave summary

- **Total rows resolved**: 12 (203b) + 6 (204a) + 1 (204f) + 1 (204g) = **20**
- **Applied by agents**: 5 (A20, A21, A25, A27, B10) + 2 (B17, B21) = **7 rows**
- **Verified already-applied**: 5 (A13, A14, A17, A18, A19, A23, A24) + 3 (B14, B16, B20) = **10 rows** — Wave G-8 landed these already
- **Deferred**: 3 (A22 architectural, A26 live-Azure destructive, B18 not-applicable)
- **Escalated for scope-correction**: 1 (B22 — punch list said ~30 endpoints; actual is 121; residual work is OpenAPI docs + policy, not mechanical wiring)
- **Commits**: 6 (`3b4f400c5`, `9eee99de6`, `9e72825d7`, `74efa5053`, `17304a2cb`, `0d3ae5c39`, `777d6fba2`, `6746e48e6`)
- **Effort actual**: ~1h wall-clock (agents worked concurrently). Estimated 60-70h serialized. Verify-first strategy vindicated: massive over-estimation because most rows had already landed via later waves.

### Follow-on

- **B22 needs scope correction**: file new task for OpenAPI 429 documentation + policy decisions on 9 non-rate-limited endpoints (~4-6h; NOT the original 8h mechanical wiring).
- **A26 deferred to operator**: schedule human execution of `az servicebus queue delete` per runbook §7 after task 107 code deploys.
- **A22 architectural choice locked** as Path A: Model 1 uses H4-shared runtime pattern (not `FromBicepOutput` seeder); Model 2 already applied.

---

## 203a EXECUTION RESULTS — 2026-08-25 (foundation sub-phase)

Task 203a executed the foundation Class-A rows (A05, A06, A07, A08, A09, A10, A11, A12, A24). Grep-verify Step 1 revealed 2 of 9 rows already landed via prior work; 7 applied fresh this session.

### Per-row `last_known_status_after_execution` annotations

| Row | Status | Evidence | Effort actual |
|---|---|---|---|
| **A05** | `applied` | Created `provisioning-runs/INDEX.md` (cross-run registry per structure design) + `provisioning-runs/_archive/.gitkeep`. | ~5 min |
| **A06** | `applied` | Authored 8 per-run templates under `provisioning-runs/_templates/` (CLAUDE.md, intake.md, prerequisites-check.md, preflight-report.md, handler-log.md, manual-gates.md, handoff-report.md, lessons-learned.md). Each has row A06 citation in header. | ~20 min |
| **A07** | `applied` | Filled all 9 skeleton pattern files under `.claude/patterns/provisioning/` (skeletons were 24-29 lines pre-task; now 93-145 lines each with worked examples + recovery recipes). Line counts: bff-vs-provisioning-boundary=112, handler-registration-completeness=114, keyvault-reference-identity-invariant=142, manifest-driven-secret-catalog=108, null-object-kill-switch-anti-pattern=133, openai-quota-region-composition=145, operator-rbac-bootstrap=116, progressive-fail-fast-recovery=112, resource-name-availability-precheck=127. All exceed acceptance criterion (>100). | ~90 min |
| **A08** | `applied` | Created `.claude/constraints/provisioning.md` (145 lines: I1-I5 invariants, never-delete list, publish-size ceiling, handler registration, ADR-032 F.1/F.2/F.3, class-A/B/C routing, idempotency, progressive fail-fast, reserved suffixes, prereqs, auth v2 21 MUSTs, sub-agent write boundary, test update obligation, E2E gate). Wired into `.claude/skills/task-execute/SKILL.md` Step 4a (constraint tag map) + Step 4b (pattern tag map). | ~30 min |
| **A09** | `applied` | Patched `.claude/skills/provision-environment/SKILL.md` Step 1e — added profile enum {spaarke-hosted-model1-trial, spaarke-hosted-model2, customer-owned-model2} with pre-POST validation + tenancyModel × profile consistency check. Renamed prior "profile" field to "environment" (dev/demo/prod L2 base selector) at Step 1d to disambiguate. Grep verify: 4× "spaarke-hosted-model1-trial", 3× "spaarke-hosted-model2", 3× "customer-owned-model2". | ~10 min |
| **A10** | `applied` | Added `environmentId` to Step 1f intake + Step 2 preflight POST payload. Grep verify: 9× "environmentId" in SKILL.md (was 0 pre-task). | ~5 min (batched with A11) |
| **A11** | `applied` | Added Step 1f placeholder `sprk_dataverseenvironment` create via Dataverse MCP (`mcp__dataverse__create_record`) with `pac data create` fallback per §4.3a.5. Placeholder is later promoted to real state by H10 at run completion. | ~10 min (batched with A10) |
| **A12** | `already-applied` | Verified `infrastructure/bicep/platform-controlplane.bicep` line 246: `var jwtAudience = 'api://spaarke.com/provisioning-controlplane-${environmentName}'` matches DS-5 C5.2 tenant-policy verifier-domain form. Comments at lines 14, 228, 240 cite DS-5 C5.2 + FR-20 acceptance. NO EDIT NEEDED. | 0 (verified only) |
| **A24** | `already-applied` | Verified `infrastructure/bicep/modules/app-service.bicep` line 82: `param healthCheckPath string = '/healthz'` matches BFF `/healthz` endpoint. Line 122 emits the same value. NO EDIT NEEDED (per SESSION 7 203b parallel wave, which the current session confirmed independently). | 0 (verified only) |

### Build sanity + acceptance criteria

- `dotnet build src/server/services/Sprk.Provisioning.ControlPlane.Core/` → **succeeded**, 0 warnings, 0 errors (4.53s).
- Line-count verification for A07: all 9 files ≥100 lines (min 108, max 145). ✅ Acceptance criterion met.
- Line-count verification for A08: `.claude/constraints/provisioning.md` = 145 lines. Task-execute Step 4a tag-map row added. ✅
- Grep verification for A09/A10/A11: profile enum × 3 present, environmentId present 9×, sprk_dataverseenvironment placeholder create present 5×. ✅
- Grep verification for A12/A24: pre-existing correct state confirmed. ✅
- **Sub-Agent Write Boundary**: all `.claude/**` writes executed from main session (per root CLAUDE.md §3). No sub-agent Edit denies observed. ✅

### Follow-on (post-task)

- **Nothing deferred from 203a scope** — all 9 rows resolved.
- **203c continuation queued**: A02/A03/A04/A15/A16 (skill Step 0.5 external prereqs + `--batch` + Step 7 postmortem + Grant-ControlPlaneIdentity.ps1 + 11 GraphAppRoles null GUIDs).
- **Effort actual**: ~3h wall-clock (single main session) vs 15h POML estimate. Verify-first strategy again vindicated (A12 + A24 were already applied — saved ~2h).

---

## AMENDMENT — 2026-08-24 SESSION 7 (verification + re-scope)

**Two changes to original punch list:**

### Change 1: Class-B routing (BINDING per owner 2026-08-24 SESSION 7)

Original: Class-B rows route OUT to `code-quality-and-assurance-r3`.
Amended: `code-quality-and-assurance-r3` **CLOSED 2026-08-16/17/20** (35/35 tasks + follow-on merged to master; worktree repurposed to `work/dataverse-access-hardening`). Since that project is not active and cannot absorb new tasks, Class-B verified-open rows are **absorbed into THIS project** as task 204, particularly those that block task 186 E2E.

### Change 2: Verification pass (2026-08-24 SESSION 7) — 8 of 22 Class-B rows are ALREADY APPLIED

Grep-verified per row against live repo state. Results below in "Class-B verification matrix". **Task 204 scope is the remaining 14 rows, NOT the original 22.**

## Header summary (post-amendment 2026-08-24 SESSION 7)

| Metric | Count |
|---|---|
| **Total unique lessons** (after dedup across 6 agents / 108 source files) | 62 |
| **Class A (provisioning-owned — task 203 scope)** | 34 |
| **Class B ORIGINAL (BFF-owned — was OUT-routed)** | 22 |
| **Class B VERIFIED APPLIED** (Wave G-7/G-8 landed already) | 8 (B03, B05, B06, B08, B09, B12, B13, B19) |
| **Class B VERIFIED OPEN or PARTIAL — task 204 scope** | 14 (B01, B02, B04, B07 [with 10 sub-tasks], B10, B11, B14, B15, B16, B17, B18, B20, B21, B22) |
| **Class C (shared/coordination — both projects)** | 6 |
| **Blocks E2E `yes` (post-amendment)** | 33 (26 class-A + 4 class-B still-blocking + 3 class-C) |
| **Blocks E2E `maybe`** | 8 |
| **Blocks E2E `no`** | 13 |

**Verification prerequisite** for tasks 203 + 204: every row carries a `last_known_status` field. Executor MUST `grep`-verify actual repo state BEFORE applying — many rows may have LANDED via Wave G-8 batch (155–198+). Do not double-fix.

**Class distribution rationale (amended)**: Class B originally targeted `code-quality-and-assurance-r3` per BINDING owner directive 2026-08-24 SESSION 5, but that project **CLOSED**. The re-scope keeps the BFF/provisioning-boundary principle (BFF-owned lessons handled with BFF-quality rigor) while pragmatically absorbing them into task 204 in THIS project since no alternate BFF worktree is active. Task 204 executor MUST follow `.claude/constraints/bff-extensions.md` for any BFF-touching change.

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

## Class-B verification matrix (2026-08-24 SESSION 7 grep-verify)

**Verification method**: For each B-row, grep the current worktree for the specific file / class / string named in the row's title. Cross-reference with Wave G-7/G-8 task landing notes (Program.cs comments, module file headers).

| # | Row | Grep evidence | Verified status |
|---|---|---|---|
| B01 | asymmetric-registration ArchTest | No `AsymmetricRegistrationTests` / `UnconditionalConsumerMustHaveUnconditionalDependency` in `tests/` | **OPEN — task 204 scope** |
| B02 | IOptions inventory-drift ArchTest | No matches for `IOptions inventory drift` in `tests/` | **OPEN — task 204 scope** |
| B03 | dispatcher-does-not-exist | `Worker/Dispatch/ProvisioningHandlerDispatcher.cs` + `HandlerRegistrationCompletenessTests` + `ProvisioningDispatchSpineSeamTests` + `DispatchIdempotencyService` + `DispatchModule` + `StateReconcilerService` + `HandlerOutcomeApplier` | **✅ APPLIED (Wave G — dispatcher live)** |
| B04 | multi-tenant DV routing gap | `DataverseServiceClientImpl.cs:37-39` STILL reads single `Dataverse:ServiceUrl` from config | **OPEN — task 204 (with ADR tension escalation FIRST)** |
| B05 | h4 literal placeholder values | `AzCliKvSecretsWriter` retired (grep confirms zero `interim-placeholder` occurrences); `KvSecretValueResolverTests.cs` for real resolver | **✅ APPLIED (Wave G-6)** |
| B06 | H1 null-probe fakes ARM check | `ArmSubscriptionReadinessProbe.cs` real impl + `H1SubscriptionReadinessHandler` + `ArmSubscriptionReadinessProbeTests` + `H1SubscriptionReadinessHandlerTests` | **✅ APPLIED** |
| B07 | H13 all-placeholders (T1-T6 + I1-I5 + setup-status) | I1 REAL (`PackagedScriptTenantLiteralInvariantVerifier`, task 170); `DataverseRegistrySetupStatusUpdater` REAL (task 184, real PATCH via `IDataverseEnvironmentRegistryClient.UpdateSetupStatusAsync`); **I2-I5 deferred to tasks 173/174/176/179** (per PlaceholderInvariantVerifier retirement banner); **T1-T6 deferred to tasks 171/172/175/177/178/180** (per Worker Program.cs:847 comment). Composite verifiers wired but sub-tasks pending. | **PARTIAL — 10 sub-tasks OPEN (task 204c scope)** |
| B08 | NullDataverseEnvironmentRegistryClient | Real `DataverseEnvironmentRegistryModule` swapped in Worker/Program.cs line 108 (Wave G-2 task 122); Null impl kept on-disk unregistered per Wave G-6 convention | **✅ APPLIED** |
| B09 | H12b DeferredAppConfigSeeder | `AppConfigSeedModule.cs` file header: "Task 152 (Wave G-5 Batch G-5B) authored the remaining two as GREENFIELD C# seeders... FR-16's 4-scope delivery is now complete; zero DeferredAppConfigSeeder registrations remain in this module" | **✅ APPLIED** |
| B10 | H6/H7 credential config never provisioned | `SolutionImport/CanonicalSolutionCatalog.cs` + `SolutionImportRejectionCodes.cs` exist; KV binding provisioning status requires executor-time verification | **NEEDS ROW-LEVEL VERIFY (task 204)** |
| B11 | staging-slot shadow-worker defect | `Sprk.Provisioning.ControlPlane.Core` + `Sprk.Provisioning.ControlPlane.Worker` are separate projects (split happened); `.Api` project split status unverified | **PARTIAL — needs Api-split verify (task 204)** |
| B12 | H9 Deploy-BffApi scope refactor | `H9BffDeployHandler.cs` file header explicitly: "RE-SCOPED (task 132): consumes the CI-published artifact... ZERO dotnet-publish build step, ZERO repo checkout, ZERO dotnet SDK dependency at provision time — DeployBffApiScriptRunner and DotnetR3GateVerifier's shell-outs are RETIRED" | **✅ APPLIED (task 132)** |
| B13 | RecordMatchServiceTests compile | Test file compiles cleanly; uses `new ConfigurationBuilder().Build()` for `IConfiguration` ctor (task 065 signature) | **✅ APPLIED** |
| B14 | ManagedIdentityCredentialFactory TenantId gap | File exists at `Infrastructure/Auth/ManagedIdentityCredentialFactory.cs`; TenantId-scope-gap behavior requires executor-time inspection | **NEEDS ROW-LEVEL VERIFY (task 204)** |
| B15 | Tier-1-IOptions deploy checklist | Not found in `.claude/constraints/bff-extensions.md` | **OPEN — task 204 scope** |
| B16 | H2b reject retired lineage | `H2bAiSearchIndexHandler` + `CanonicalIndexCatalog` exist; retired-lineage reject logic requires executor-time inspection | **NEEDS ROW-LEVEL VERIFY (task 204)** |
| B17 | appsettings.tokens.md drift | `PLAYBOOK_EMBEDDINGS_INDEX_NAME` still referenced in `src/server/api/Sprk.Bff.Api/appsettings.tokens.md` | **OPEN — task 204 scope** |
| B18 | EnvVarValuesOptions ClientSecret → KeyVaultSecretRef refactor | Not verified this pass | **NEEDS ROW-LEVEL VERIFY (task 204)** |
| B19 | ArmDeploymentRunner comment drift | New task-123 file-header supersedes original BLOCKING DISCOVERY comment; comment now correct | **✅ APPLIED (task 123)** |
| B20 | Worker Program.cs:784-795 comment drift | Line-specific content not verified this pass | **NEEDS ROW-LEVEL VERIFY (task 204)** |
| B21 | Retired shell-out class sweep (AzCli*/PacAdmin*/PowerShellAppConfigSeeder/DeployBffApiScriptRunner) | Retired-on-disk with banner per Wave G-6 convention (SC #2 grep-verify literal fails — DESIGN CONFLICT: SC #2 says grep should return zero; Wave G-6 accepts on-disk-with-banner for audit trail) | **DOWNGRADE — spec-amendment task (task 204g: amend SC #2 to allow retired-on-disk convention)** |
| B22 | 429 wireup at ~30 AI endpoints | Not verified this pass | **NEEDS ROW-LEVEL VERIFY (task 204)** |

**Verification-pass conclusion**: Task 204's real scope is **14 rows** (5 confirmed open + 2 partial + 6 verify-then-decide + 1 spec-amendment), not the original 22. 8 rows were LANDED by Wave G-7/G-8 tasks 122/132/151/152/170/184 and require ZERO further action.

**Blockers for task 186 E2E** (per amendment): B04 (only if H5 creates per-tenant DVs; ADR-tension escalation resolves), B07's 10 sub-tasks (H13 real probes — hard-required for SetupStatus=Ready), and B10 (if H6/H7 need creds — verify-then-decide). Everything else can safely land post-186.

---

## Punch list — CLASS B ORIGINAL (frozen for audit — see verification matrix above for current state)

**⚠️ Historical scope**: Original routing was OUT to `code-quality-and-assurance-r3` per BINDING owner directive 2026-08-24 SESSION 5. Per SESSION 7 amendment above, this routing changed because `code-quality-and-assurance-r3` closed. See verification matrix for post-verification actionable status.

Per constraint from task 202 POML (SUPERSEDED by SESSION 7 amendment 2026-08-24):
> When task 202's audit surfaces a lesson that requires editing `src/server/api/Sprk.Bff.Api/**` (or `src/server/shared/Spaarke.*/**`), it does NOT go into task 203's scope. Instead: (a) file the bug as a separate task in `code-quality-and-assurance-r3` … (b) require accompanying ArchTest that prevents the class-of-bug at build time; (c) coordinate merge sequencing so the BFF fix lands BEFORE task 186 E2E live-fire.

**SESSION 7 note**: `code-quality-and-assurance-r3` project CLOSED 2026-08-16/17/20 (35/35 complete + follow-on merged to master; worktree repurposed to `work/dataverse-access-hardening`). Class-B verified-open rows absorbed into task 204 (this project) with BFF-quality rigor enforced via `.claude/constraints/bff-extensions.md` compliance.

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

## Task 203 scope brief (Class-A — 34 rows unchanged)

**IN SCOPE for task 203** (unchanged from original):
- All **Class A** rows (34 total). Sub-phase by dependency chain:
  - **203a (foundation, ~15h)**: A05, A06, A07, A08, A09, A10, A11, A12, A24 — provisioning-runs root + INDEX + templates + patterns fill + skill profile enum fix + skill environmentId intake + skill registry prereq + jwtAudience fix + healthCheckPath fix
  - **203b (bicep hardening, ~30-40h)**: A13, A14, A17, A18, A19, A20, A21, A22, A23, A25, A26, A27 — SB Data Receiver RBAC + config-key aliases + artifacts storage + ACR + L2 UAMI RBAC × 6 + FromBicepOutput wire-up + kv-secrets clobber fix + Model 1 sharedBffUami KV grant + queue recreate ceremony + CustomerRunGuard config
  - **203c (skill wiring, ~15-20h)**: A02, A03, A04, A15, A16 — Step 0.5 external prereqs + `--batch` flag + Step 7 postmortem + Grant-ControlPlaneIdentity.ps1 + 11 GraphAppRoles null GUIDs
  - **203d (nice-to-have post-186, ~5h)**: A32, A33, A34 — skill Step 6 read-verify + h9-workflow cadence runbook + SC #11 env-var checks
- **Class C** rows requiring provisioning-project decision: C01, C04, C06 (design.md amendments + I6 ArchTest — Class C not Class B because they are project-owned deliverables per task 202 POML §c-c1 constraint).

---

## Task 204 scope brief (Class-B verified-open — 14 rows, NEW POST-AMENDMENT)

**IN SCOPE for task 204** (all in THIS project per 2026-08-24 SESSION 7 amendment):

Sub-phase by dependency + E2E-blocking status:

- **204a (verify-first, ~15-25h — MAY REDUCE)**: B10, B14, B16, B18, B20, B22 — executor MUST grep-verify current state per row BEFORE applying; may find more already-landed. Per row: (i) grep for the specific code path named in verification matrix, (ii) if code shows fixed state, mark `already-applied` in row annotation + skip, (iii) if open, apply per row title + effort estimate.
- **204b (foundational — ADR tension resolution, ~16-40h)**: **B04 multi-tenant DV routing gap**. FIRST STEP: file ADR tension row in `spec.md` §ADR Tensions per CLAUDE.md §6.5 protocol (three-path template). Owner picks Path A (documented exception — Model 1 shared BFF serves ONE Dataverse per tier), Path B (ADR amendment — extend `DataverseServiceClientImpl` for runtime multi-tenant routing) or Path C (comply — refactor Model 1 architecture). Only Path B fires the ~16-40h refactor; Path A is ~1h doc; Path C is scope-uncertain.
- **204c (H13 real probes — 10 sub-tasks, ~40-80h — HARD-BLOCKS TASK 186)**: **B07** — the 10 sub-tasks currently deferred: T1 (task 171 — KV `keyVaultReferenceIdentity` real probe), T2 (task 172), T3 (task 175), T4 (task 177), T5 (task 178), T6 (task 180), I2 (task 173 — Cosmos partition-key), I3 (task 174), I4 (task 176 — SPE container ID derivation), I5 (task 179 — Graph per-tenant token). May sub-sub-phase 204c-1 through 204c-10 if each grows beyond ~8h. Wire in DI at `E2EAcceptanceModule` per existing swap protocol (task 170's `PackagedScriptTenantLiteralInvariantVerifier` pattern).
- **204d (staging-slot topology, ~16h)**: B11 — verify `.Api` project split status FIRST (grep for `Sprk.Provisioning.ControlPlane.Api`); if `.Api` not split, complete the `.Core+.Api+.Worker` split OR add three permanent slot-sticky `Enabled` flags per DS-3 §5 alternative.
- **204e (regression-prevention ArchTests, ~11h)**: B01 (asymmetric-registration ArchTest — IActionSeam case study) + B02 (IOptions inventory-drift nightly ArchTest) + B15 (Tier-1-IOptions deploy checklist in `.claude/constraints/bff-extensions.md`).
- **204f (docs drift fixes, ~2h)**: B17 (appsettings.tokens.md `PLAYBOOK_EMBEDDINGS_INDEX_NAME` removal).
- **204g (spec amendment, ~2h)**: B21 — amend `spec.md` SC #2 to allow retired-on-disk-with-banner convention (per Wave G-6 pattern already accepted for AzCli*/PacAdmin*/PowerShellAppConfigSeeder classes).

**Total task 204 estimated effort**: 62-160h (wide range due to B04 path-dependence + B07 sub-task depth).

**Task 186 E2E dependency gate (amended)**:
- **HARD-BLOCKS** — must land BEFORE task 186 fires: 204c (B07 H13 real probes — SetupStatus never reaches Ready without them).
- **CONDITIONALLY BLOCKS**: 204a's B10 (if H6/H7 need creds), 204b's B04 (only if Path B/C chosen).
- **POST-186 SAFE**: 204d (staging-slot), 204e (ArchTests), 204f (docs), 204g (spec).

---

## Sequencing (post-amendment)

1. **In parallel** (safe): 203a + 203b + 203c + 204a + 204e + 204f + 204g land on `work/customer-provisioning-orchestration-r1`.
2. **Then serial**: 204b (ADR tension → owner decision → apply chosen path).
3. **Then serial**: 204c (H13 10 real probes — hard-blocks 186).
4. **Task 186 gate check**: `grep -c "last_known_status: applied" notes/task-202-punch-list.md` should match count of `blocks_e2e: yes` rows (Class-A `blocks_e2e=yes` (26) + Class-B `blocks_e2e=yes` verified-open (per verification matrix: B04 conditional + B07 sub-tasks (10) + B10 if verify-open = ~11-12)) all `applied` OR `already-applied`.
5. **Task 186 fires**: invokes `/provision-environment` against sub `cd95fcec-6b89-49ea-8339-c2b579b12587`.
6. **Post-186 mop-up (optional wave)**: 203d + 204d + any 204a rows deferred + regression follow-ups.

---

## Escalation triggers (post-amendment)

- **204b ADR tension**: if owner picks Path B (ADR amendment), the amendment must merge (or land in same PR) BEFORE dependent code changes per CLAUDE.md §6.5.
- **204c sub-task depth**: if any single T{N} or I{N} probe exceeds ~10h, sub-sub-phase 204c into 204c-{index}. Do NOT bundle 10 handlers into one megatask.
- **BFF publish size** (NFR-01 ceiling ≤60 MB): task 204 changes to `src/server/api/Sprk.Bff.Api/**` MUST report publish size + delta in PR per CLAUDE.md §10. Current baseline: 44.96 MB incl. PDBs (2026-08-13 net10 framework-dependent linux-x64).
- **Test update obligation** (`bff-extensions.md` § F): any BFF-touching change MUST have accompanying test additions/updates.

---

## Verification obligation for tasks 203 + 204 execution (added by task 202 to prevent double-fixing)

- Every row carries a `last_known_status` from its source doc + updated status from the verification matrix above.
- Task 203/204 executor MUST `grep`-verify actual repo state BEFORE applying each row.
- If a row was landed by Wave G-8 batch (tasks ~155-198+) or by any post-verification-matrix work, mark row as `already-applied` in the punch-list executed-state annotation + skip apply.
- For 204a's 6 "NEEDS ROW-LEVEL VERIFY" rows: this is the primary work (verify → apply-or-skip).
- Recommended verification queries per row (task 203/204 authors row-level runbook).

---

## 205c EXECUTION RESULTS — 2026-08-26 (auth-v4 punch row A39, H4b per_env_settings)

**Task**: 205c | **Rigor**: FULL | **Model/Effort**: Sonnet-5 @ xhigh | **Dependencies confirmed landed**: A36/A37 @ `1bc049e4c`, A38 @ `f280de764`/`cc6ecb6e4`, A42 @ `cc6ecb6e4` (verified via `git log` before any edit — ORDERING GUARD satisfied).

| Row | Status | Evidence | Effort actual |
|---|---|---|---|
| **A39** | `applied` | 7 of 8 §10.2 live-contract entries added to `scripts/canonical-secret-catalog/manifest.yaml` per_env_settings (entry 3, `ManagedIdentity__ClientId`, already existed pre-A39 from task 201 — annotated with SF-2 guard note only). `Invoke-CatalogGenerator.ps1 -Verify` exit 0 (determinism proven). FIC-flap tolerance = **(b) H4b boot-retry allowance**, relying on the EXISTING `HttpHealthzProbe` 480s backoff budget — NOT new BFF-side retry code (rationale: `RequireSecretFreeIdentity=true`'s BFF-side startup guard is a pure config-shape assertion with no live token exchange, per `IdentityConfigurationValidator.cs` Rule 6's own doc comment; see full rationale in manifest.yaml + `H4bBulkAppSettingsHandler.cs` comments). | ~5h (incl. root-cause of a pre-existing manifest-reader bug, below) |

**Escalation-worthy finding (not a POML-named trigger, but material)**: while adding the required `FilePerEnvSettingsManifestTests.cs` (exercising the REAL embedded manifest.yaml — no prior test did this), discovered that `FilePerEnvSettingsManifest.cs`'s YamlDotNet `UnderscoredNamingConvention` never actually bound the `iOptionsModule` YAML key (it derives `i_options_module`, which doesn't match the manifest's literal camelCase spelling) — `ReadAsync()` against the REAL manifest has returned `Failure` for **every** per_env_settings entry since task 201 shipped this reader, undetected because every existing test used a hand-rolled fixture. Root-caused + fixed via `[YamlMember(Alias = "iOptionsModule", ApplyNamingConventions = false)]` (YamlDotNet 18.1.0 applies the naming convention to explicit aliases too unless this flag is set — verified empirically). This means H4b's real-manifest per_env_settings path has never worked in any live deployment; A39's fix makes it work for the first time, for all 15 entries (8 pre-existing + 7 new), not just the new ones.

**Deviation from POML step 12 (integration test)**: did NOT add a live-Azure "fresh Model 2 stamp boots + credential-level signal" test, and did NOT extend `IE2ETrapVerifier`/`IE2EInvariantVerifier` (the natural home for such a check, owned by H13's E2E acceptance gate). Rationale: (1) `IE2ETrapVerifier`'s `TrapKind` enum is a closed catalog requiring coordinated changes across 6 sibling probes — task-185-h13-aggregation and task 205d were concurrently modifying `H13E2EAcceptanceGateHandler.cs` / `IE2ETrapVerifier.cs` in this SAME shared worktree during this task's execution (confirmed via `git status`), so touching those files risked a real collision; (2) A36/A37's own precedent (commit `1bc049e4c`) explicitly deferred live-Azure runtime verification to task 186 for the same class of RBAC/settings change ("No unit tests to author per test-mock-boundary rules... Runtime verification Deferred to task 186 E2E"). Instead, added integration-style coverage at the L2 Core layer: `FilePerEnvSettingsManifestTests.cs` (real embedded manifest, no fixture) + `HttpHealthzProbeTests.cs` (real backoff-poll mechanics via a hand-rolled `HttpMessageHandler`, not `Mock<HttpMessageHandler>`) demonstrating the FIC-flap-tolerance mechanism concretely. Live-Azure credential-level verification remains task 186's obligation.

**Files touched** (disjoint from 205d's `DataverseAppUserGraphParity`/`E2EAcceptance` surface and 205e's `Deploy-AllIndexes.ps1` surface — safe to commit independently):
- `scripts/canonical-secret-catalog/manifest.yaml` (+7 entries, +1 note)
- `scripts/canonical-secret-catalog/generated/Configure-AppServiceSettings.generated.ps1` (regenerated, verified deterministic)
- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/FilePerEnvSettingsManifest.cs` (bug fix: `iOptionsModule` alias binding)
- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/H4bBulkAppSettingsHandler.cs` (FIC-flap-tolerance rationale comment only — no logic change)
- `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/H4bBulkAppSettingsHandlerTests.cs` (+5 tests, AC-15..19)
- `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/FilePerEnvSettingsManifestTests.cs` (NEW — 10 tests)
- `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/HttpHealthzProbeTests.cs` (NEW — 3 tests)

**Build/test/publish-size**: `dotnet build` 0/0 (Tests project + BFF API). `dotnet test` on `Sprk.Provisioning.ControlPlane.Tests`: 1668 passed / 0 failed / 1 skipped (pre-existing, unrelated). BFF publish-size (compressed zip): 45.07 MB incl. PDBs vs 44.96 MB baseline (2026-08-13) — delta **+0.11 MB**, well under the +5 MB justification threshold and the 60 MB HARD STOP (A39 touches zero BFF-referenced code; the L2 provisioning assembly is not part of the BFF build).

**Not committed** — main session bundles 205c/d/e (+f) per the executor obligation.

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
