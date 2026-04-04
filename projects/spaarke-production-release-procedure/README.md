# Production Release Procedure & Tooling

> **Branch**: `feature/production-environment-setup-r2`
> **Status**: Planning
> **Created**: 2026-04-04

## Summary

Build an automated, repeatable release system for deploying Spaarke updates to production environments. The system has three layers:

1. **Procedure guide** (`docs/procedures/production-release.md`) — human-readable source of truth
2. **Orchestrator scripts** — `Build-AllClientComponents.ps1`, `Deploy-AllWebResources.ps1`, `Deploy-Release.ps1`
3. **Claude Code skill** — `/deploy-new-release` for interactive, automated execution

## Key Design Decisions

- **Reuse existing scripts** — all new orchestrators call proven deploy scripts (`Deploy-BffApi.ps1`, `Deploy-DataverseSolutions.ps1`, etc.)
- **Aligned with customer onboarding** — shares the same sub-scripts as `Provision-Customer.ps1`; clear distinction: Provision = new environment, Deploy-Release = update existing
- **Multi-environment** — deploy to one or many environments sequentially
- **GitHub CI/CD for BFF API** — Dataverse deployments stay PowerShell-based (PAC CLI)
- **Git tagging for releases** — enables change detection between releases

## Deliverables

| # | File | Type |
|---|------|------|
| 1 | `docs/procedures/production-release.md` | Procedure guide |
| 2 | `scripts/Build-AllClientComponents.ps1` | Build orchestrator |
| 3 | `scripts/Deploy-AllWebResources.ps1` | Web resource deploy orchestrator |
| 4 | `scripts/Deploy-Release.ps1` | Master release orchestrator |
| 5 | `config/environments.json` | Environment registry |
| 6 | `.claude/skills/deploy-new-release/SKILL.md` | Claude Code skill |

## Related Documents

- [spec.md](spec.md) — Full requirements
- [plan.md](plan.md) — Implementation plan
- [CUSTOMER-DEPLOYMENT-GUIDE.md](../../docs/guides/CUSTOMER-DEPLOYMENT-GUIDE.md) — Customer deployment guide
- [CUSTOMER-ONBOARDING-RUNBOOK.md](../../docs/guides/CUSTOMER-ONBOARDING-RUNBOOK.md) — Onboarding runbook
