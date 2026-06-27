# Spaarke Environment Factory — Design Specification

> **Status**: ⚠️ SUPERSEDED (2026-06-13) by `projects/customer-provisioning-orchestration-r1/`.
> This draft remains as input/source material — its lifecycle coverage matrix, gap analysis, and
> registry-extension thinking carry forward into that project's Phase 0 discovery report. The
> *execution architecture* here (operator-run PowerShell orchestrator as the end state) is replaced
> by the locked three-layer model (ADR-004 job handlers → control-plane API + MCP server → swappable
> front ends). Read the successor project, not this, for current direction.
>
> **Created**: 2026-06-12
> **Author**: Ralph Schroeder / Claude Code
> **Project**: spaarke-environment-factory-r1
> **Predecessor**: spaarke-environment-provisioning-app (r1, complete — see its notes/lessons-learned.md)

---

## Executive Summary

Build the **single, systematic process for standing up a new Spaarke environment and deploying the platform into it** — one procedure, one orchestrator, one interactive skill, all driven by the `sprk_dataverseenvironment` registry entity created in r1.

Spaarke already has most of the automation. What it lacks is unification: the assets span three generations that don't reference each other, five buildout phases are still guided-manual, and nothing connects an environment's registry record to its actual provisioning state.

---

## Problem Statement

**Current state** — three fragmented generations of provisioning assets:

| Generation | Assets | Character |
|---|---|---|
| Gen 1 — demo deployment (2026-03) | `docs/guides/ENVIRONMENT-DEPLOYMENT-GUIDE.md` (14 sections) | Validated but heavily manual CLI; 13 documented workarounds |
| Gen 2 — customer onboarding | `scripts/Provision-Customer.ps1` (13 steps, idempotent, resumable), `infrastructure/bicep/{customer,platform}.bicep` + 24 modules, `CUSTOMER-ONBOARDING-RUNBOOK.md`, `Decommission-Customer.ps1`, `Validate-DeployedEnvironment.ps1` | Strong automation, but unaware of Gen 1 guide and Gen 3 registry |
| Gen 3 — environment registry (r1) | `sprk_dataverseenvironment` entity, registration/user-provisioning flow, `auth-deployment-setup.md` (auth v2, 21 MUSTs) | Registry exists but only the registration flow consumes it |

An operator standing up a new environment today must mentally merge three documents, decide which script generation applies, and execute 5 phases by hand. Nothing records which phase an environment has reached.

**Desired state**: one canonical procedure + one orchestrator + one skill; the registry record is created at provisioning start, tracks per-phase progress, and reaches `Setup Status = Ready` only when validation passes. Estimated effort for a repeat operator drops from ~2–3 hours of guide-juggling to a supervised scripted run.

---

## Lifecycle Coverage Matrix (current state, inventoried 2026-06-12)

| # | Phase | Existing asset | Automation level |
|---|-------|----------------|------------------|
| 1 | Power Platform env creation | Provision-Customer.ps1 step 5 | Scripted |
| 2 | Azure subscription setup | ENVIRONMENT-DEPLOYMENT-GUIDE §2 | Guided-manual |
| 3 | Azure infrastructure | customer.bicep / platform.bicep + 24 modules | Scripted |
| 4 | Entra ID app registrations | ENVIRONMENT-DEPLOYMENT-GUIDE §4 (11 permission GUIDs) | **Guided-manual — automate** |
| 5 | Key Vault secrets | Provision-Customer.ps1 step 4 | Scripted |
| 6 | Solution export & fix pipeline | ENVIRONMENT-DEPLOYMENT-GUIDE §6 (8 manual fix steps) | **Guided-manual — automate** |
| 7 | Solution import | Deploy-DataverseSolutions.ps1 | Scripted |
| 8 | Dataverse environment variables | Provision-Customer.ps1 step 8 | Scripted |
| 9 | SPE container type + root container | §9 manual SPO PowerShell; partial Create-NewContainerType.ps1 | **Guided-manual — automate (idempotent)** |
| 10 | BFF deploy + app settings | Deploy-BffApi.ps1 + auth-deployment-setup.md §3/§3.5 | Scripted + checklist |
| 11 | Dataverse application user | PPAC UI only | Manual **by platform design** — document |
| 12 | Validation | Validate-DeployedEnvironment.ps1, Test-Deployment.ps1 | Scripted (exit-code gate) |
| 13 | User provisioning | r1 registration app + registry lookup | Scripted (done in r1) |
| 14 | Data seeding | per-module seed scripts; spaarke-data-cli (design only, separate repo) | Partial — out of scope here |
| 15 | Ongoing releases | Deploy-Release.ps1 + /deploy-new-release skill | Scripted/interactive (done) |

---

## Scope

### In Scope

1. **Canonical procedure**: `docs/procedures/new-environment-provisioning.md` — the phase table above with one automation entry point per phase; supersedes Gen 1 guide for buildout (banner added); links Gen 2 runbook and auth-deployment-setup as phase detail.
2. **Registry integration**: extend `sprk_dataverseenvironment` with provisioning fields (Azure subscription ID, resource group, App Service name, Key Vault name, container-type ID, per-phase checklist JSON or status fields). Orchestrator creates/updates the record; `sprk_setupstatus → Ready` only on validation pass.
3. **Gap automation** (the three automatable manual phases):
   - `scripts/Register-SpaarkeEnvironmentApps.ps1` — Entra app registrations + the 11 permission grants, idempotent, outputs IDs for Key Vault/env vars (build on existing `Register-EntraAppRegistrations.ps1`)
   - `scripts/Initialize-SpeContainerType.ps1` — container type create + billing + Graph registration + root container, with existence checks and the westus-billing + token-propagation-wait workarounds encoded (extend `Create-NewContainerType.ps1`)
   - `scripts/Export-FixedSolutions.ps1` — scripted solution export/fix pipeline (the 8 sed-style fixes from §6) with post-fix verification
4. **Unified orchestrator**: `scripts/Provision-Environment.ps1` — evolution of `Provision-Customer.ps1` that (a) calls the new gap scripts, (b) reads/writes the registry record, (c) keeps idempotency/resume/WhatIf semantics, (d) ends with the validation gate.
5. **Interactive skill**: `.claude/skills/provision-environment/` modeled on `deploy-new-release` — pre-flight, parameter collection (into the registry record), confirmation gates, phase execution, handoff report.
6. **r1 carry-over closure**:
   - Migrate `DemoExpirationService` off `[Obsolete]` `DemoProvisioningOptions.Environments`/`DefaultEnvironment` — resolve environment per-request via the registration record's `sprk_dataverseenvironmentid` lookup; then delete `DemoProvisioning__Environments__*` + `__DefaultEnvironment` from Azure (closes r1 criterion 10).
   - Doc drift: `auth-azure-resources.md` App Service names (`spe-api-dev-67e2xz` → `spaarke-bff-dev`/`rg-spaarke-dev`; prod `spaarke-bff-prod`/`rg-spaarke-platform-prod`).
   - r1 spec/design column names → deployed reality (`sprk_envaccountdomain`, `sprk_mdaappid`).
   - Live provisioning sign-off round (r1 criteria 5, 8, 9, 11) — folded into the E2E dry run.
7. **E2E dry run (program acceptance)**: stand up one brand-new environment using only the new procedure + orchestrator + skill.

### Out of Scope

- Demo/customer data seeding tooling (`spaarke-data-cli` — separate repo/project)
- Multi-tenant architecture changes (see spe-multi-tenant-architecture-r1)
- Disaster recovery / backup automation
- Environment health monitoring beyond the validation gate
- Changes to the release process (`/deploy-new-release` is consumed as-is)

---

## Data Model — `sprk_dataverseenvironment` extension (indicative; finalize in spec)

| Schema Name | Type | Purpose |
|---|---|---|
| `sprk_azuresubscriptionid` | Text(100) | Azure subscription hosting this environment's resources |
| `sprk_resourcegroupname` | Text(200) | Resource group |
| `sprk_appservicename` | Text(200) | BFF App Service |
| `sprk_keyvaultname` | Text(200) | Key Vault |
| `sprk_containertypeid` | Text(100) | SPE container type |
| `sprk_provisioningstatejson` | Multiline(4000) | Per-phase checklist written by the orchestrator (phase, status, timestamp, notes) |
| `sprk_provisionedon` | DateTime | When validation first passed |

Constraint carried from r1: no plugins (ADR-002); orchestrator and BFF write via Web API.

---

## Placement Justification (CLAUDE.md §10)

- New scripts + skill + procedure doc: live in `scripts/`, `.claude/skills/`, `docs/procedures/` — no BFF impact.
- The only BFF change is the `DemoExpirationService` migration (modifying an existing registered service to use the existing `DataverseEnvironmentService`; no new endpoints, packages, or DI registrations). Publish-size verification per task as usual; expected delta ≈ 0.
- Registry schema extension is Dataverse-only.

---

## Phasing (input to /project-pipeline)

| Phase | Content | Depends on |
|---|---|---|
| A | Canonical procedure doc + Gen-1 guide supersession + doc-drift fixes | — |
| B | Gap automation scripts (Entra apps, SPE container type, solution export/fix) — independently testable | — (parallel with A) |
| C | Registry schema extension + Provision-Environment.ps1 orchestrator integrating B | A, B |
| D | /provision-environment skill | C |
| E | DemoExpirationService migration + Azure legacy-config deletion + verification | — (parallel; BFF task, FULL rigor) |
| F | E2E dry run: new environment end-to-end + r1 live sign-off items + wrap-up | C, D, E |

---

## Success Criteria

1. [ ] One procedure doc covers all 15 phases with a single automation entry point or explicit manual-by-design marking per phase
2. [ ] `Provision-Environment.ps1` runs phases 1–12 with registry state tracking, resumable, `-WhatIf` supported
3. [ ] Entra app registration, SPE container type, and solution export/fix run unattended and idempotently
4. [ ] A brand-new environment reaches `Setup Status = Ready` via the new process; `Validate-DeployedEnvironment.ps1` exits 0
5. [ ] `DemoProvisioning__Environments__*` and `__DefaultEnvironment` deleted from Azure; expiration flow verified working
6. [ ] r1 manual sign-off items (live provisioning into Dev + Demo 1, bulk grid approve) pass during the dry run
7. [ ] `/provision-environment` skill executes the full flow with confirmation gates and produces a handoff report

## Open Questions (for owner before /design-to-spec)

1. **Decommission**: should `Decommission-Customer.ps1` be upgraded to registry-aware in this project, or deferred?
2. **Environment kinds**: does the orchestrator need different profiles (demo vs customer vs trial — e.g., skip per-customer Bicep for shared-platform demo envs)? Suggested: a `-Profile` parameter mapping to bicep stack selection.
3. **Where does the dry-run environment live** (new Azure sub vs existing dev sub; gets decommissioned after acceptance or kept as a template env)?
