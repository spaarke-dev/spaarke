# CLAUDE.md — customer-provisioning-orchestration-r1

> # 🔴 READ FIRST — [`INCOMING-D12-D13-REMEDIATION.md`](INCOMING-D12-D13-REMEDIATION.md)
>
> **The deployment model this project was built around has CHANGED** (owner decisions **D-12** + **D-13**,
> merged to master 2026-09-28 in `38f48723e`). The "Model 1 = shared trial/SMB tier" is **RETIRED**; both
> models are dedicated stamps differing only in which **Azure tenant** owns the customer's subscription, and
> the BFF Entra app registration is **per customer in both models (BINDING)**.
>
> 🔴 **Four required code changes are in THIS project**, including one where `H3EntraAppRegHandler` currently
> does the opposite of what D-13 requires. This branch is **812 commits behind master**.
> **Do not start work, and do not resolve a merge conflict, before reading that file.**



> **Per-project AI context. This file is loaded automatically when Claude Code operates in this project directory.**
> **Last Updated**: 2026-08-19 (v3.6 — task 128b Redis Model 1/Model 2 reconciliation; see spec.md/design.md v3.6 CHANGELOG)
> **Root CLAUDE.md rules apply — this file EXTENDS, does not replace.**

---

## What this project does

Enterprise customer-provisioning platform for Spaarke. See [README.md](./README.md) overview + [design.md](./design.md) v3.3 (1,884 lines) for full context.

**One-sentence contract**: An operator invokes `/provision-environment {customerId}`; Claude Code calls L2 REST API; L2 sequences 19 handlers (`IProvisioningHandler`) via Service Bus + Cosmos state; customer environment reaches `Setup Status = Ready` end-to-end.

## Load these first (per task)

**On every task** (via `<knowledge>` in POML):

1. **[spec.md](./spec.md)** — authoritative Functional/Non-Functional Requirements (FR-01..FR-37, NFR-01..NFR-12), Success Criteria (22), Governance sections
2. **[design.md](./design.md)** — full design context, decisions D1–D20, handler catalog H0–H14, §4B trap catalog, §4C rollback, §4D tenant isolation, §14A upgrade model
3. **[notes/r3-handoff.md](./notes/r3-handoff.md)** — r3-shipped mechanisms r1 consumes (tasks 060/061/062/017)
4. **[notes/resource-discovery-2026-08-16.md](./notes/resource-discovery-2026-08-16.md)** — canonical implementations, ADR files, patterns, constraints

**Per-task-tag** — task POMLs pick from `<knowledge>` based on tags. The Tag-to-Knowledge mapping is in `.claude/skills/task-create/SKILL.md` Step 3.4.

## Applicable ADRs

Per [spec.md § Technical Constraints § Applicable ADRs](./spec.md#applicable-adrs). Concise summaries at `.claude/adr/`; full history at `docs/adr/`.

| ADR | Concise | Why relevant |
|---|---|---|
| ADR-004 | `.claude/adr/ADR-004-job-contract.md` | L1 handlers implement the L2-local `IProvisioningHandler` contract (ADR-004-shaped); L2 orchestration is Path A exception (see spec.md ADR Tensions) |
| ADR-010 | `.claude/adr/ADR-010-di-minimalism.md` | Provisioning handlers register in L2 (not BFF); BFF DI additions strictly bounded |
| ADR-013 | `.claude/adr/ADR-013-ai-architecture.md` | H0.5 endpoint MUST NOT inject `IActionResolver`/`IActionRunner`; use `Services/Ai/PublicContracts/` facade if AI needed |
| ADR-014 | `.claude/adr/ADR-014-ai-caching.md` | `spaarke-session-files` tenantId + sessionId dual-filter invariant (§4D I2 strengthens) |
| ADR-017 | `.claude/adr/ADR-017-job-status.md` | Per-handler job status vs ProvisioningRun (different stores per §5.3) |
| ADR-020 | `.claude/adr/ADR-020-versioning.md` | Pinned model deployment versions in H2a Bicep OpenAI config |
| ADR-027 | `.claude/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md` | One Azure subscription **per customer** (ADR amended 2026-09-28); **no** shared-tier exception |
| ADR-028 | `.claude/adr/ADR-028-spaarke-auth-architecture.md` | H4 KV secrets + UAMI RBAC + `keyVaultReferenceIdentity` PATCH follow 21 MUSTs |
| ADR-032 | `.claude/adr/ADR-032-bff-nullobject-kill-switch.md` | SignalR feature-gate follows P1/P2/P3 pattern |
| ADR-034 | `.claude/adr/ADR-034-user-record-membership.md` | Optional SignalR per-customer aligns with realtime pattern |
| ADR-036 | `.claude/adr/ADR-036-background-job-infrastructure.md` | Background-job infrastructure pattern (Service Bus + `IJobHandler` + Redis idempotency) — L2's `ProvisioningHandlerDispatcher` follows the same shape (Redis idempotency reused directly; no compile reference to `IJobHandler`) |
| ADR-038 (full) | `docs/adr/ADR-038-testing-strategy.md` | Integration-heavy pyramid; 5 new ArchTests I1–I5 sequence into r3 forcing-functions |
| ADR-039 | `.claude/adr/ADR-039-grounded-execution-closed-catalogs.md` | Single AI routing surface; H12a seeds `playbook consumers`; `spaarke-playbook-embeddings` retired |
| ADR-044 | `.claude/adr/ADR-044-dataverse-guid-canonicalization.md` | Registry key patterns — `sprk_currentrunid`, `sprk_tenantid`, `sprk_bffversion`, `sprk_solutionversion` |

## Constraints (`.claude/constraints/`)

Per root CLAUDE.md §10 + §11 governance:

| Constraint | Load when |
|---|---|
| `.claude/constraints/bff-extensions.md` | **ANY task touching `src/server/api/Sprk.Bff.Api/**`** (H0.5 endpoint, DemoExpirationService migration, `GraphAppRoles.cs` completion) — MUST load before adding to BFF |
| `.claude/constraints/azure-deployment.md` | H2a Bicep tasks — includes BFF publish-size ≤60 MB ceiling |
| `.claude/constraints/testing.md` | Any tests-modifying task (unconditional code-review + adr-check per root §8) |
| `.claude/constraints/auth.md` | H3, H4, H10 — auth ceremony, KV secrets, MI-Dataverse-App-User |
| `.claude/constraints/jobs.md` | H0.5 endpoint, L1 handler tasks — `IProvisioningHandler` contract (ADR-004-shaped; BFF `IJobHandler` reference-only) |
| `.claude/constraints/data.md` | H5, H6, H7, H10, registry schema extension |
| `.claude/constraints/api.md` | H0.5 endpoint, L2 REST API endpoint tasks |
| `.claude/constraints/ai.md` | H12a AI seed chain |
| `.claude/constraints/config.md` | H4 secrets + FR-35 canonical naming; NFR-05 fail-fast config validation |
| `.claude/constraints/pcf.md` | (No PCF tasks in r1 scope — reserved for reference only) |

## Patterns (`.claude/patterns/`)

| Pattern dir | Load when |
|---|---|
| `.claude/patterns/api/` | H0.5 endpoint, L2 REST API endpoint tasks |
| `.claude/patterns/auth/` | H3, H4, H10 — auth binding + OBO + SSO |
| `.claude/patterns/dataverse/` | H5, H6, H7, H10, H12a/b/c — Dataverse operations |
| `.claude/patterns/caching/` | Redis usage in idempotency service |
| `.claude/patterns/testing/` | Test-adding tasks; includes `.claude/patterns/testing/god-class-ratchet.md` (NFR-07) |
| `.claude/patterns/ui/` | (No UI in r1) |

## Canonical implementations (pattern exemplars)

Discovery report enumerates the strongest exemplars. Key ones the task POMLs reference by name:

| Exemplar | For |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Jobs/**/*Handler.cs` (any of 13 production handlers) | L1 `IJobHandler` implementation pattern |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/**/*Handler.cs` | L1 handler with AI ties (use for H12a/b/c) |
| `src/server/api/Sprk.Bff.Api/Services/Jobs/IdempotencyService.cs` | 3-level idempotency (MessageId + Redis + Dataverse alt-key) |
| `src/server/api/Sprk.Bff.Api/Services/Registration/DemoProvisioningService.cs` (9-step) | H11 user provisioning pattern |
| `src/server/api/Sprk.Bff.Api/Services/Registration/RegistrationDataverseService.cs` | Cross-env token cache + multi-URL ops |
| `src/server/api/Sprk.Bff.Api/Services/Registration/GraphUserService.cs` | H11 user creation + UPN + license |
| `src/server/api/Sprk.Bff.Api/Api/Registration/**` endpoints | Endpoint filter pattern for L2 REST API |
| `.claude/skills/deploy-new-release/SKILL.md` | Reference model for L3 skill `/provision-environment` (Phase D) |
| `infrastructure/bicep/customer.bicep` | H2a Bicep extension (reference — extend, don't recreate) |
| `scripts/Provision-Customer.ps1` | 13-step orchestrator — basis for handler port |
| `scripts/ai-search/Deploy-AllIndexes.ps1` | H2b — 7 canonical indexes; script IS the catalog authority |
| `scripts/Deploy-DataverseSolutions.ps1` | H6 — dependency-ordered solution import (8 solutions per §11.1a) |

## MUST rules (spec-cited, task-execute must enforce)

Full list at [spec.md § Technical Constraints § MUST Rules](./spec.md#must-rules). Highlights that come up on EVERY task:

- **MUST** register provisioning handlers in **L2 control-plane service, not the BFF** (§5.2 + D3/D8/D12)
- **MUST NOT** create a per-customer Entra **tenant** — Model 1 uses one Spaarke tenant (still correct under D-12).
  🔴 **AMENDED 2026-09-28**: the *"+ one multitenant BFF app"* half is **REVERSED**. **MUST** create **one BFF app registration per customer**, in both models — D-13 (BINDING). The app registration determines the Dataverse application user, which determines the business unit every BFF-created record lands in. `projects/unified-access-control-r2/notes/D-13-per-customer-bff-app-registration.md`
- **MUST NOT** re-introduce Dataverse S2S app-reg (r3 task 060 dropped it; zero code consumers)
- 🔴 **REVERSED 2026-09-28 (D-12 §3); product + auth set by T242 (owner D12/D13, 2026-10-04).** Was: *"**MUST NOT** provision Redis per-customer **FOR MODEL 1**"*. Now: **MUST provision Redis per-customer in BOTH models** as **Azure Managed Redis (`Microsoft.Cache/redisEnterprise`) Balanced_B0, high availability on, Microsoft Entra only** (access keys disabled; the stamp UAMI holds the only access-policy assignment; the BFF reads the plain setting `Redis__Endpoint` — no key, no connection string, no Key Vault secret). Redis access control is per-instance (not per-keyspace) and it holds OBO tokens + the `uac-access` authorization cache. `customer.bicep` wires `modules/redis.bicep` unconditionally. (The earlier "Standard tier" wording is superseded — ADR-009 as amended by T242.)
- **MUST** use confidential-client (app-only) token for SPE container-type creation (T6)
- **MUST** PATCH App Service `keyVaultReferenceIdentity` to UAMI on both slots (T1)
- **MUST** apply canonical KV secret + resource naming (Phase G / R1–R4); vault name is Bicep parameter
- **MUST NOT** delete `Dataverse-ClientSecret` / `BFF-API-ClientSecret` (BINDING per r3 handoff)
- **MUST** pre-check LIVE App Service + KV + Dataverse before removing any alias (FR-35 pre-check gate)
- **MUST** ensure all AI Search queries include unconditional `tenantId eq` filter (§4D I2 / FR-29)
- **MUST** ensure all Cosmos reads/writes include partition-key predicate (§4D I3 / FR-30)
- **MUST** take SPE container IDs from the record being served or the stamp's own settings, and pass every app-only SPE call through `SpeContainerOwnershipGuard` — the one definition of this stamp's containers (§4D I4 / FR-31; T227d/T227f)
- **MUST** acquire Graph tokens per-tenant scoped (§4D I5 / FR-32)
- **MUST NOT** hardcode default tenant in provisioning scripts (§4D I1 / FR-28)
- **MUST** report BFF publish size + delta in every BFF-touching task's PR description (NFR-01)
- **MUST** ensure BFF `/health` fails fast at boot on any Tier-1 IOptions misconfig (r3 task 061)
- **MUST** complete 11 of 14 null `AppRoleId` GUIDs in `GraphAppRoles.cs` BEFORE first production customer
- **MUST** enqueue handlers via Service Bus + return 202 Accepted (FR-22 / R20 — no synchronous handler in HTTP path)
- **MUST** use `PublicContracts/` facade if H0.5 needs AI (ADR-013 forcing-function ArchTest per r3 task 040)

## ADR Tensions (per CLAUDE.md §6.5)

Declared in [spec.md § ADR Tensions](./spec.md#adr-tensions-per-claudemd-65--mandatory). 2 Path A (documented exception) + 5 Path C (comply). All rationale concrete. NO Path B (no ADR amendment needed).

**Path A rows** — code-review at PR time expects PR description to cite these:
- **ADR-004**: L2 orchestration is NEW component pattern — a custom state machine over Cosmos rather than Durable Task, and not single-shot (ADR-052 §7 now also permits Durable Task in its own host). Rationale: ADR-004 applies at handler level; L2 orchestration uses its own `ProvisioningHandlerDispatcher` + custom state machine over Cosmos (§5.4 rejected alts).
- **ADR-027**: 🔴 **no longer an exception.** ADR-027 was **amended 2026-09-28** to one Azure subscription **per customer** in both models, which is what this project already does — so there is nothing to except. The former rationale (*"§4D invariants enforce logical isolation"*) is **withdrawn**: I2–I4 key on `tenantId`, which is identical for every Model 1 customer, so they cannot separate customers and pass anyway.

## Sub-Agent Write Boundary (root CLAUDE.md §3)

**Sub-agents CANNOT write to `.claude/` paths.** Applies to r1 tasks touching:
- `.claude/skills/provision-environment/SKILL.md` (Phase D) — main-session-only **(LANDED 2026-08-18 tasks 075 + 076)**
- `.claude/patterns/**` additions (if any) — main-session-only
- `.claude/constraints/**` additions (if any) — main-session-only

task-create Step 3.8 auto-marks these as `parallel-safe: false`. If a parallel agent is accidentally dispatched to a `.claude/` task, it will fail with "Edit denied" — main session picks up sequentially.

## Rigor level defaults for this project (per root CLAUDE.md §8)

Applied by `task-create` Step 3.5.5 per task tags. r1-specific:

| Rigor | When |
|---|---|
| **FULL** | Every task tagged `bff-api` (H0.5 endpoint, DemoExpirationService migration, GraphAppRoles.cs completion), `plugin` (none in r1), `auth` (H3/H4/H10), `deploy` (H9, Phase F acceptance). Also POST-COMPACTION recovery. Also L2 control-plane task groups. |
| **STANDARD** | New file creation without BFF touch (Bicep modules, PowerShell scripts, docs) |
| **MINIMAL** | Documentation-only (Phase A doc consolidation, U-CB customer-comms templates, version-compat matrix) |
| **TEST-MODIFYING (unconditional FULL override per root §8)** | Any task touching `tests/**` OR tagged `testing`/`integration-test` — 5 new ArchTests (I1–I5) all trigger this |

## Model tier defaults for this project (per root CLAUDE.md §8.5)

Applied by `task-create` Step 3.5.5b. r1-specific:

| Tier / Effort | When |
|---|---|
| **Sonnet 5 @ high** (default) | 80% of tasks — mechanical Bicep authoring, PowerShell hardening, doc consolidation, script ports |
| **Opus 4.8 / Fable 5 @ high** | High-blast-radius: Phase C UAMI migration (structural refactor); Phase H canonical secret-catalog manifest generator; L2 control-plane scaffold; any ADR-migration-adjacent task |
| **Sonnet 5 @ xhigh** | Only where clearly justified: complex brownfield DemoExpirationService migration (Phase E — 3 obsolete-option touches into DataverseEnvironmentService); tenant-isolation ArchTest authoring (must think through every AI Search / Cosmos / Graph / SPE call site) |

## Coordination with other worktrees

> **UPDATE 2026-10-02 (owner, T225a escalation):** `ci-cd-unit-test-remediation-r1` **reactivated 2026-08-27**
> (projects/INDEX.md) — which this file had missed: r1 tasks T245b and T225a edited
> `publish-provisioning-arm-artifacts.yml` / its manifest schema / `deploy-infrastructure.yml` believing it dormant (its
> branch tip is stale; its work lands on master). The owner has now ruled that **ci-cd-unit-test-remediation-r1 is
> CLOSED** and that r1 resolves its own provisioning workflow changes here. Before a future r1 task touches
> `.github/workflows/**`, check `projects/INDEX.md` (not a branch tip) for an active CI-governance project.
>
> **`.github/workflows/**` ownership is TEMPORARY-BY-DEFAULT (added 2026-08-19).** r1 holds direct ownership only because `ci-cd-unit-test-remediation-r1`'s declared 28-day window expired with that worktree dormant. This is NOT a permanent reassignment: if `ci-cd-unit-test-remediation-r1` reactivates, or any new CI-governance project starts, ownership of `.github/workflows/**` reverts to the standard coordination model (declared owner + coord-notes) and r1 goes back to authoring coord-notes for that owner rather than committing directly. Re-check this condition before any FUTURE r1 task touches `.github/workflows/**`.

**Active worktrees to coordinate with** (per r3 handoff §7 + INDEX.md hot-path overlap):

| Worktree | Hot-path overlap | Coordination action |
|---|---|---|
| ~~`ci-cd-unit-test-remediation-r1`~~ **[reactivated 2026-08-27; CLOSED per owner 2026-10-02]** | Owner declared window expired 2026-08-19; r1 has taken ownership of `.github/workflows/**` for Phase C'' scope. The 3 queued r1 coord-notes (067 Graph parity, 088 naming-conformance + tenant-isolation, 115 provisioning-sidecar build) were applied directly as of commit `<see git log for the governance commit on this branch>`. If ci-cd-r1 reactivates, coordinate the merge conflict; otherwise proceed. See `projects/INDEX.md` Excluded Worktrees + CI Workflows section for the registry-level record. |
| `code-quality-and-assurance-r3` | BFF=Y (actively decomposing BFF) | Phase E DemoExpirationService migration may bump into r3's dead-code-removal PRs; `/conflict-check` before Phase E PR |
| `spaarke-ai-architecture-redesign-r1/r2` | BFF=Y (broadest AI touch) | If H0.5 endpoint or DemoExpirationService migration touches `Services/Ai/**`, coordinate. Unlikely per our current scope. |
| `spaarke-devops-project-tracking-r1` (PR #453) | skill-directives=Y (modifies project-pipeline SKILL.md) | Cosmetic — our pipeline execution uses local copy; no runtime dependency |
| **19 active BFF worktrees total** | BFF=Y | `/conflict-check` before EVERY BFF PR |

## Task Execution Protocol

**MANDATORY** — When executing r1 tasks, Claude Code MUST invoke `task-execute` skill (per root CLAUDE.md §4). DO NOT read POML files directly and implement manually. See root CLAUDE.md §4 for auto-detection rules.

**Rigor level declaration at task start (per root §8)**: Claude Code MUST output the 🔒 RIGOR LEVEL block. Non-negotiable.

**`/goal` wave loop eligibility**: assigned per-wave by `task-create` Step 3.85 + recorded in TASK-INDEX.md. Wave eligibility is capped for r1 by: security/deploy/irreversible tasks (H4 KV secret writes, H9 BFF deploy, Phase F acceptance, Phase G/H naming remediation) are NEVER goal-eligible. Mechanical Bicep authoring waves + PowerShell hardening waves may be goal-eligible if ≥3 tasks and machine-verifiable end-state.

## Context Management

Per root CLAUDE.md §5. r1-specific:
- `/checkpoint` at 60% context usage (proactive)
- `/checkpoint` + STOP + request `/compact` at 70%
- Checkpoint files: [`current-task.md`](./current-task.md) + [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md) + this `CLAUDE.md`

## Human Escalation Triggers

Per root CLAUDE.md §6 + §6.5. r1-specific escalation triggers:
- Any **`GraphAppRoles.cs`** `az` enumeration returning unexpected role IDs (11 of 14 null must be verified before completing)
- Any **KV secret rename/delete** without prior LIVE App Service + KV + Dataverse pre-check (§7.9 BINDING pre-check)
- Any **tenant-isolation invariant** (I1–I5) failure detected outside expected Phase-A ArchTest work
- Any **Model 2 customer commitment** trigger (unblocks TF migration path — spec.md § Unresolved Questions)
- Any **live-dev KV drift** encountered while executing Phase G/H (owner directive #3: don't remediate live-dev)

## Related Projects

- **Superseded**: `projects/spaarke-environment-factory-r1/` (this project inherits the mission)
- **Predecessor**: `spaarke-environment-provisioning-app` (r1, complete PR #390) — user-provisioning + registry foundation
- **Dependency**: `code-quality-and-assurance-r3` (tasks 060/061/062/017 landed 2026-08-14 per [`notes/r3-handoff.md`](./notes/r3-handoff.md))
- **Coordinated**: `ci-cd-unit-test-remediation-r1` (Phase H CI-wiring)
- **Follow-on (r2)**: registry-aware decommission + fleet management web app
- **Data migration**: `spaarke-data` CLI (separate project — new customers start empty-but-functional)

## Standing directives & gotchas

> **Why this section exists (repo procedure change, 2026-10-06).**
> - **What changed:** `current-task.md` now holds CURRENT state only and is rewritten at each checkpoint (`.claude/skills/context-handoff/SKILL.md` "State, not history").
> - **Why:** here it had grown to 450 KB, because every checkpoint stacked a new block over the old ones, and task-execute reads it at Step 0 + Step 2 of every task.
> - **What this section is:** the items below were stated in that file as standing or binding and are still in force. They moved here so they survive the rewrite and are read on every recovery.
> - **Where the rest went:** the old file is archived verbatim at `notes/handoff-history/current-task-archive-2026-10-06.md`. Items the conversion could not classify, plus stale lines it noticed in THIS file, are in `notes/handoff-history/2026-10-06-conversion-review.md`; resolve them when convenient.
> - **Going forward:** add a new standing directive or gotcha HERE (one dated bullet), not in `current-task.md`.
>
> **Also new, repo-wide:** task-execute Step 9.5 "Finding triage and round limits".
> - F1–F4 fix-now / K1–K4 known-limit.
> - At most 2 fix rounds, each re-verifying the fix diff only.
> - 1 verifier pass per task (2 for auth/security/tenant-isolation).
> - Escalate any F1 still open instead of starting round 3.
>
> The skill files reach this worktree on the next master merge (`work/procedure-throughput-fixes-r1`); the rules apply now.
>
> Items already in `.claude/constraints/provisioning.md`, root `CLAUDE.md` or project memory are NOT repeated here.

**Owner directives**
- **Build the process, not the environment** (owner verbatim 2026-08-23): "The goal is not to install a new environment as quick and easy as we can — it's to build a customer provisioning and deployment process." Never `pac admin copy` from another env as a shortcut; every gap found is fixed here.
- **End state** (2026-08-23 / 2026-09-01): provisioning runs E2E with no human interaction. L2 has no web UI in r1 (REST API + `/provision-environment` only); a "Customer Deployment" web app is a follow-on (`notes/follow-on-customer-deployment-webapp-proposal.md`).
- **T186 (first live E2E) MUST go through `/provision-environment`** — never direct L2 REST calls (standing since 2026-08-30).
- **T218 = DEFINE the complete Spaarke solution package** (owner 2026-09-28; T217 folded in, T216 dropped): audit + consolidate/redesign solutions so ALL components (entities, roles, forms, MDA, Copilot agent, per-customer app regs) ship. Hard blocker for T186.
- **All solutions ship to every customer** — no core/optional split (owner 2026-09-01).
- **No BI in MVP** (owner 2026-09-28 §9 Q1): per-customer Power BI F-SKU later — do NOT procure; placeholder only. **M365 Copilot agent is per customer** (§9 Q2). Do not re-litigate.
- **VNet is optional and stays off for MVP** (owner 2026-09-01).
- **`SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md` is authoritative for r1** (owner 2026-08-30). Never merge an SPE owning app with a BFF app registration.
- **INCOMING doc §8 items belong to unified-access-control-r2** — leave alone (Secure Project→Secure Record rename + 3 Redis subject-discrimination keys) (2026-09-28).
- **`rg-spaarke-shared-prod` (D23, 2026-10-02)** is the single home for shared PROD resources: the prod L2 control plane goes there from its first deploy (change `platform-controlplane.bicep`'s RG name then). Owner chose no lock, no tags for now (2026-10-03).
- **Live Azure/Entra/Dataverse changes need an explicit owner OK per action** ("live; ask first"); record each one in the task POML notes.

**Keep (do not delete)**
- `Spaarke Exchange Admin` (appId `46670ee2-ac0c-44b0-9ac2-d40ae4dcbdd7`) and `Spaarke SPE Model 1 Owner` (appId `bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e`); Graph Explorer's grant on the `Spaarke Model 1` registration (owner KEEP 2026-10-03).
- Exchange group `sprk-t251-spike-scope` (Entra `c709af95-0332-4ea2-a9d4-6925b1666bad`, member testuser1@) = the test group for `Verify-Sidecar-Live.ps1 -InTenant` (owner 2026-10-04). `Enable-OrganizationCustomization` has been run (irreversible).
- Never delete Key Vault secrets during cleanup — the `Exchange-Connect-Cert` sentinel stays (2026-10-04).

**Environment gotchas**
- `sdap-ci.yml` is not a required check and its jobs are `continue-on-error`; a gate that must block goes in `ci-tier1-blocking.yml` (Router) (2026-10-06).
- Before declaring a per-run H13 check fixed, ask where its inputs live at runtime: the Worker publish has no `scripts/` or `infrastructure/`, and the host has no pwsh/pac (2026-10-06).
- `string.Create(IFormatProvider, …)` does not accept `$"" + $""` concatenation — format with `ToString("F2", CultureInfo.InvariantCulture)` (2026-10-06).
- `tests/scripts/Auth-V4-Operator-Script-Gates.Tests.ps1` fails 27/27 locally under Pester 6.2 at HEAD (pre-existing; not in CI) (2026-10-06).
- Dev Redis, its alerts and App Insights (`spe-insights-dev-67e2xz`) live in `spe-infrastructure-westus2`, NOT `rg-spaarke-dev` (2026-10-06).
- Dev BFF deploys come only from master ≥ `c8b93b294` (2026-10-06).
- Granting an MI on an SPE container-type registration: Graph v1.0 `PUT /storage/fileStorage/containerTypeRegistrations/{ct}/applicationPermissionGrants/{appId}` via `Connect-MgGraph -Scopes FileStorageContainerTypeReg.Manage.All`. `Set-SPOApplicationPermission` fails for an MI (2026-10-06).
- `az ad app permission admin-consent` fails ("Consent validation failed"); consent Graph app roles with `POST /servicePrincipals/{graph}/appRoleAssignedTo` (2026-10-02).
- `Deploy-ControlPlane.ps1` via `pwsh -File` stops at the ConfirmImpact=High prompt — run it in-process with `-Confirm:$false` (`-SkipBuild` to reuse a build) (2026-10-04).
- To target another subscription without touching the shared az context, use a private `AZURE_CONFIG_DIR` copy and delete it afterwards (2026-10-04).
- Never run destructive az commands as a "clean slate" (`az account clear` wiped the credential cache, 2026-08-23).
- Before relying on a pac/az flag in a runbook, run its `--help` locally: `pac admin create` silently appends a digit to a taken domain, and `create-environment` is not the command (2026-10-06). *(verify: an older 2026-08-23 note says pac flags once EXECUTED a command; see conversion review)*
- A prereq recipe's tokens must be resolvable at the step that runs its scope — `validate.ps1` checks documentation only (2026-10-06).
- Check Microsoft's per-feature region table before defaulting a regional AI resource to the stamp location ("service available" ≠ "every feature available"; Content Safety defaults to westus) (2026-10-06).
- A Bicep `@description('…')` string must not contain an apostrophe (2026-10-06).
- Pester for `tests/scripts/*.Tests.ps1` needs `Import-Module Pester -RequiredVersion 3.4.0` (6.x rejects `-Script` / `Should Be`) (2026-10-06).
- Edit scripts: write them with the Write tool (bash heredocs fail on quoting). In Python use `'''` when the text holds `"` before `"""` or C# raw strings, and restrict line-prefix replacements to the intended line (2026-10-06).
- Parallel sub-agents share the git index: `git commit --only <paths>`, never `git add -A` / `git add .` (2026-08-19).
- `scripts/check-task-status-drift.ps1` reports false "unpaired" entries for this project: its parser expects `| <marker> <id> |`, but TASK-INDEX uses `| <id> | <marker> |`. Known; not remediated (2026-09-29).
- Azure OpenAI pin refresh due before ~2027-01-14 (T247 one-source set; `PinnedModelCatalog`) (2026-10-06).

---

*Load this file first when operating in this project directory. Individual task POMLs augment with per-task knowledge under `<knowledge>`.*
