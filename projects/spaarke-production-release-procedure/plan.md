# Implementation Plan: Production Release Procedure & Tooling

**Date**: 2026-04-04
**Estimated Tasks**: ~12-15 tasks across 5 phases

---

## Phase 1: Project Setup (1 task)
- Create project artifacts (this plan, README, CLAUDE.md, task files)

## Phase 2: Procedure Guide — The Blueprint (1-2 tasks)
Create `docs/procedures/production-release.md` — the complete human-readable release procedure.

This is the **source of truth** for everything else. It documents:
- Pre-flight checklist (auth, CI, git status)
- Release phase ordering with dependency diagram
- Per-component deployment: exact commands with production parameters
- Multi-environment deployment flow
- Rollback procedures per component (with exact commands)
- Emergency hotfix abbreviated path
- First-time vs. subsequent release matrix
- Release versioning strategy (git tags)
- Cross-references to `CUSTOMER-DEPLOYMENT-GUIDE.md` and `CUSTOMER-ONBOARDING-RUNBOOK.md`

**All commands reference `https://spaarke-demo.crm.dynamics.com` as the production Dataverse URL.**

## Phase 3: Build the Primitives — Scripts (4-5 tasks)

### Task: Environment Registry
Create `config/environments.json` mapping environment names to their Azure resource details.

### Task: Build-AllClientComponents.ps1
Orchestrates all client builds in dependency order:
1. Shared libs (Auth → SdapClient → UI.Components)
2. Vite solutions (14 solutions)
3. Webpack code pages (5 pages)
4. PCF controls (13 controls)
5. External SPA

Supports `-WhatIf`, `-SkipSharedLibs`, `-Component`.

### Task: Deploy-AllWebResources.ps1
Orchestrates all web resource deployments for one environment. Calls:
- `Deploy-CorporateWorkspace.ps1`
- `Deploy-ExternalWorkspaceSpa.ps1`
- `Deploy-SpeAdminApp.ps1`
- `Deploy-WizardCodePages.ps1`
- `Deploy-EventsPage.ps1`
- `Deploy-PCFWebResources.ps1`
- `Deploy-RibbonIcons.ps1`

### Task: Deploy-Release.ps1
Master orchestrator. Calls all sub-scripts in correct order for one or more environments:
1. Pre-flight → 2. Build → 3. BFF API → 4. Solutions → 5. Web Resources → 6. Validate → 7. Tag

Supports `-EnvironmentUrl` (string or array), `-WhatIf`, `-SkipPhase`, `-Version`.

## Phase 4: Claude Code Skill (2-3 tasks)

### Task: deploy-new-release SKILL.md
Interactive Claude Code skill invoked via `/deploy-new-release`. Steps:
1. Pre-flight checks (git, CI, auth)
2. Select target environment(s) from registry
3. Change detection (diff since last tag)
4. Confirm deployment plan
5. Execute `Deploy-Release.ps1`
6. Report results, prompt for tag

### Task: Register Skill
- Update root `CLAUDE.md` trigger phrases
- Update `.claude/skills/INDEX.md`

## Phase 5: Verification (1-2 tasks)

### Task: Dry-Run Against Dev
Run `Deploy-Release.ps1 -WhatIf` against dev to verify the orchestration logic without deploying.

### Task: Live Test Against Demo
Execute `/deploy-new-release` end-to-end against `https://spaarke-demo.crm.dynamics.com`.

---

## Dependency Graph

```
Phase 1 (setup)
    |
    v
Phase 2 (procedure doc) --- blueprint for everything else
    |
    v
Phase 3 (scripts) --- implements what the procedure describes
    |   Build-AllClientComponents.ps1
    |   Deploy-AllWebResources.ps1
    |   Deploy-Release.ps1
    |   environments.json
    |
    v
Phase 4 (skill) --- makes it invocable via /deploy-new-release
    |
    v
Phase 5 (verification) --- dry-run then live test
```

## Key Constraints

- **Reuse existing scripts** — never reimplement what `Deploy-BffApi.ps1`, `Deploy-DataverseSolutions.ps1`, etc. already do
- **Sequential per environment** — no parallel environment deploys (safety)
- **BFF URL must be host-only** — recurring production issue, validate in pre-flight
- **SpaarkeCore imports first** — solution dependency order enforced by `Deploy-DataverseSolutions.ps1`
- **Shared libs build first** — blocks all downstream client builds
