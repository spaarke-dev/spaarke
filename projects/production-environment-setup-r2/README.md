# Production Environment Setup R2: Environment-Agnostic Configuration

> Last Updated: 2026-03-20
> Status: Complete
> Completion Date: 2026-03-20

## Overview
Make all Spaarke components environment-agnostic. Build once, deploy anywhere. Remove hardcoded dev URLs/IDs from 9 code pages, 7+ PCF controls, BFF API, Office add-ins, and 30+ scripts.

## Quick Links
| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan with parallel execution groups |
| [Design Spec](./spec.md) | Full design specification |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown and status |
| [Project Context](./CLAUDE.md) | AI context for task execution |

## Current Status
Phase: Complete
Progress: 100%
Owner: AI-Assisted Development

## Problem Statement
Deploying Spaarke to a new environment requires manual discovery and patching of environment-specific values scattered across 9 code pages, 7+ PCF controls, BFF API, Office add-ins, shared libraries, and 30+ scripts. N code pages × M environments = N×M builds.

## Solution Summary
Establish a single source of truth: 5 canonical environment values (Tenant ID, BFF API URL, BFF App ID, MSAL Client ID, Dataverse Org URL) flow from infrastructure provisioning → deployment pipeline → runtime resolution. Code pages resolve config from Dataverse Environment Variables at runtime. PCF controls use the existing environmentVariables.ts pattern consistently. BFF API injects config via IOptions. Scripts accept parameters with environment variable fallbacks.

## Graduation Criteria
- [x] All code pages resolve BFF URL and MSAL Client ID from Dataverse Environment Variables at runtime (no build-time baking)
- [x] All PCF controls use @spaarke/auth + environmentVariables.ts (no hardcoded IDs)
- [x] BFF API has zero hardcoded environment-specific URLs or app IDs
- [x] No "spaarkedev1" or dev client IDs in production-facing code
- [x] Validate-DeployedEnvironment.ps1 passes in dev environment
- [x] All scripts accept parameters with env var fallbacks (no hardcoded org URLs)
- [x] Dataverse Environment Variable definitions included in SpaarkeCore solution XML

## Scope

### In Scope
- BFF API: Fix 3 hardcoded locations (OfficeDocumentPersistence, OfficeService ×2, CORS)
- Shared Libraries: resolveRuntimeConfig() in @spaarke/auth, clean environmentVariables.ts defaults
- Code Pages (9): Migrate from build-time .env.production to runtime Dataverse Environment Variable resolution
- PCF Controls (7+): Migrate hardcoded auth to @spaarke/auth + environmentVariables.ts
- Office Add-ins: Parameterize auth config, manifest URLs
- Legacy JS webresources (7): Remove hardcoded BFF URL/scope
- Scripts (30+): Add parameters with env var fallbacks
- Infrastructure: Create Validate-DeployedEnvironment.ps1, add env var definitions to solution XML
- Provisioning: Add Dataverse Environment Variable creation to Provision-Customer.ps1

### Out of Scope
- Azure resource reprovisioning (already environment-agnostic via Bicep)
- Key Vault secret management (already parameterized)
- Redis/Service Bus configuration (already parameterized)
- SPE container setup (95% parameterized, minor gap addressed)
- New feature development

## Key Decisions
| Decision | Rationale | ADR |
|----------|-----------|-----|
| Runtime resolution via Dataverse Environment Variables | Code pages can't use PCF webApi; Dataverse REST API available to all authenticated components | — |
| MSAL client ID from Xrm context | Available before auth in web resources; avoids chicken-and-egg problem | — |
| Remove dev defaults (fail loudly) | Dev defaults mask config failures; silent fallback to dev in prod is worse than a clear error | — |
| Keep .env.development for local dev | Only .env.production is the problem; .env.development is fine for local workflow | — |

## Risks & Mitigations
| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| MSAL client ID chicken-and-egg | High | Low | Xrm context available before auth; build-time fallback as safety net |
| Runtime Dataverse query adds latency | Low | High | Cache for 5 min (same as PCF pattern); query is tiny (~100ms) |
| Removing dev defaults breaks local dev | Medium | Low | .env.development files remain untouched |
| Migration breaks existing code pages | High | Medium | Incremental rollout, one at a time, verify, then batch |
| Provisioning script gaps | High | Low | Validate-DeployedEnvironment.ps1 catches missing vars |

## Dependencies
| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| @spaarke/auth shared library | Internal | Ready | Existing extensibility points (window globals) |
| environmentVariables.ts PCF utility | Internal | Ready | Existing, needs cleanup |
| Dataverse Environment Variables | Internal | Partial | Some exist, others need creation |
| SpaarkeCore solution | Internal | Ready | Needs env var definitions added |

## Changelog
| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-03-18 | 1.0 | Initial project setup | Claude Code |
| 2026-03-20 | 2.0 | All 39 tasks complete — project marked complete | Claude Code |
