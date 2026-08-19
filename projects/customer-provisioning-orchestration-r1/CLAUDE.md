# CLAUDE.md — customer-provisioning-orchestration-r1

> **Per-project AI context. This file is loaded automatically when Claude Code operates in this project directory.**
> **Last Updated**: 2026-08-16
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
| ADR-027 | `.claude/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md` | D4 sub-per-customer; Model 1 shared-tier is Path A exception |
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
- **MUST NOT** create per-customer Entra tenant; use one Spaarke tenant + one multitenant BFF app (spec.md §9.1 v3)
- **MUST NOT** re-introduce Dataverse S2S app-reg (r3 task 060 dropped it; zero code consumers)
- **MUST NOT** provision Redis per-customer (Q-E FR-12; per-env via `Deploy-RedisCache.ps1`)
- **MUST** use confidential-client (app-only) token for SPE container-type creation (T6)
- **MUST** PATCH App Service `keyVaultReferenceIdentity` to UAMI on both slots (T1)
- **MUST** apply canonical KV secret + resource naming (Phase G / R1–R4); vault name is Bicep parameter
- **MUST NOT** delete `Dataverse-ClientSecret` / `BFF-API-ClientSecret` (BINDING per r3 handoff)
- **MUST** pre-check LIVE App Service + KV + Dataverse before removing any alias (FR-35 pre-check gate)
- **MUST** ensure all AI Search queries include unconditional `tenantId eq` filter (§4D I2 / FR-29)
- **MUST** ensure all Cosmos reads/writes include partition-key predicate (§4D I3 / FR-30)
- **MUST** derive SPE container IDs from tenant context via `ITenantContainerResolver` (§4D I4 / FR-31)
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
- **ADR-004**: L2 orchestration is NEW component pattern (not Durable Functions / not single-shot). Rationale: ADR-004 applies at handler level; L2 orchestration uses its own `ProvisioningHandlerDispatcher` + custom state machine over Cosmos (§5.4 rejected alts).
- **ADR-027**: Model 1 shared-tier is documented exception. Rationale: D3 (v3) rewrites tenancy to include both tiers; §4D invariants enforce logical isolation.

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

**Active worktrees to coordinate with** (per r3 handoff §7 + INDEX.md hot-path overlap):

| Worktree | Hot-path overlap | Coordination action |
|---|---|---|
| `ci-cd-unit-test-remediation-r1` | ci-workflows=Y (DIRECT — owns `.github/workflows/**` for 28-day window) | Phase H CI-gate wiring is a coordinated PR per `task-042-063-ci-gate-wiring-deferral.md`. Do NOT edit `.github/workflows/**` in isolation. |
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

---

*Load this file first when operating in this project directory. Individual task POMLs augment with per-task knowledge under `<knowledge>`.*
