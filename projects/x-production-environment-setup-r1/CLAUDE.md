# CLAUDE.md — Production Environment Setup R1

## Project Metadata

| Field | Value |
|-------|-------|
| Project | production-environment-setup-r1 |
| Branch | `feature/production-environment-setup-r1` |
| Type | Infrastructure & DevOps |
| Phases | 5 |
| Tasks | 31 |

## Key Technical Constraints

- **ADR-001**: Single App Service, no Azure Functions. Health check at `/healthz`.
- **ADR-009**: Redis-first caching. Short TTLs for security data.
- **ADR-010**: Options pattern with `ValidateOnStart()`. ≤15 DI registrations.
- **FR-08**: ALL secrets in Key Vault. Zero plaintext secrets anywhere.
- **FR-11**: ALL new resources follow `sprk_`/`spaarke-` naming standard.
- **FR-06**: Demo uses identical scripts as real customers (no special-casing).

## Naming Standard (MANDATORY)

```
Azure long names:    spaarke-{purpose}-{env}        e.g., spaarke-bff-prod
Azure short names:   sprk-{purpose}-{env}           e.g., sprk-platform-prod-kv
Per-customer:        sprk-{customer}-{purpose}-{env} e.g., sprk-demo-prod-kv
Storage accounts:    sprk{customer}{env}sa           e.g., sprkdemoprodsa
Dataverse:           sprk_{name}                     e.g., sprk_documentprofile
Code:                Sprk.{Area}.{Component}         e.g., Sprk.Bff.Api
```

## Owner Decisions

- **Entra ID**: Same tenant as dev (`a221a95e-...`)
- **Domain**: `api.spaarke.com` with Azure-managed SSL
- **Region**: `westus2`
- **Dataverse**: Automate via Power Platform Admin API
- **Runners**: GitHub-hosted (assumed, switchable later)

## Parallel Execution Groups

| Group | Tasks | Max Agents | Notes |
|-------|-------|-----------|-------|
| P1-A | 001, 002, 003 | 3 | Independent Bicep files |
| P1-B | 004, 005 | 2 | Docs + config |
| P2-A | 010, 011, 012, 013 | 4 | Independent scripts |
| P4-A | 030, 031, 032 | 3 | Independent workflows |
| P5-A | 040-044 | 5 | Independent documentation |

## Key File Paths

| File | Purpose |
|------|---------|
| `infrastructure/bicep/modules/` | Reusable Bicep modules (9 modules) |
| `infrastructure/bicep/model2-full.bicep` | Full-stack reference template |
| `scripts/Deploy-BffApi.ps1` | Existing BFF deploy (needs parameterization) |
| `.github/workflows/sdap-ci.yml` | Existing CI pipeline |
| `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` | Naming convention doc |
| `src/server/api/Sprk.Bff.Api/appsettings.json` | BFF API configuration |

## Task Execution Protocol

All tasks MUST be executed via `task-execute` skill. See root CLAUDE.md for protocol.
