# CLAUDE.md — Production Release Procedure

> Project-specific AI context for the release automation system

## Project Summary
Build an automated production release system: procedure guide + orchestrator scripts + Claude Code skill (`/deploy-new-release`).

## Applicable ADRs
| ADR | Relevance |
|-----|-----------|
| ADR-001 | Minimal API — BFF API deployment via `Deploy-BffApi.ps1` |
| ADR-006 | PCF vs Code Page — different build tools (pcf-scripts vs webpack/Vite) |
| ADR-012 | Shared component library — must build `@spaarke/ui-components` before consumers |
| ADR-022 | PCF platform libraries — React 16 PCF vs React 18 Code Pages build differences |

## Key Architecture Decisions
1. **Reuse existing scripts** — never reimplement Deploy-BffApi.ps1, Deploy-DataverseSolutions.ps1, etc.
2. **Procedure guide is the blueprint** — scripts implement what the guide documents
3. **Provision-Customer.ps1 = new environment; Deploy-Release.ps1 = update existing** — shared sub-scripts
4. **Sequential per-environment deployment** — no parallel env deploys (safety + monitoring)
5. **Git tags for release versioning** — enables change detection between releases

## Production Environment
- **Dataverse URL**: `https://spaarke-demo.crm.dynamics.com`
- **BFF API URL**: `https://api.spaarke.com`
- **App Service**: `spaarke-bff-prod` (with staging slot)
- **Key Vault**: `sprk-platform-prod-kv`
- **Resource Group**: `rg-spaarke-platform-prod`

## Existing Scripts to Reuse (DO NOT REIMPLEMENT)
| Script | Purpose |
|--------|---------|
| `scripts/Deploy-BffApi.ps1` | BFF API to App Service (staging slot swap) |
| `scripts/Deploy-DataverseSolutions.ps1` | 10 solutions in dependency order |
| `scripts/Deploy-CorporateWorkspace.ps1` | Corporate workspace HTML |
| `scripts/Deploy-ExternalWorkspaceSpa.ps1` | External SPA via Web API |
| `scripts/Deploy-SpeAdminApp.ps1` | SPE Admin app |
| `scripts/Deploy-WizardCodePages.ps1` | Batch wizard deploy (~12 pages) |
| `scripts/Deploy-EventsPage.ps1` | Events page |
| `scripts/Deploy-PCFWebResources.ps1` | PCF control bundles |
| `scripts/Deploy-RibbonIcons.ps1` | Ribbon icon resources |
| `scripts/Configure-ProductionAppSettings.ps1` | 40+ Key Vault references |
| `scripts/Validate-DeployedEnvironment.ps1` | Post-deploy validation |
| `scripts/Provision-Customer.ps1` | Customer onboarding (reference only) |

## Build Dependency Order
```
1. Shared libs (must be first):
   src/client/shared/Spaarke.Auth/
   src/client/shared/Spaarke.SdapClient/
   src/client/shared/Spaarke.UI.Components/

2. Vite solutions (14 total):
   src/solutions/{LegalWorkspace,SpeAdminApp,AnalysisBuilder,...}/

3. Webpack code pages (5 total):
   src/client/code-pages/{AnalysisWorkspace,DocumentRelationshipViewer,...}/

4. PCF controls:
   src/client/pcf/

5. External SPA:
   src/client/external-spa/
```

## MANDATORY: Task Execution Protocol
When executing tasks in this project, Claude Code MUST invoke the task-execute skill. See root CLAUDE.md for complete protocol.
