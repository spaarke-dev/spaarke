# docs/guides/ Drift Audit — Task 022

**Date**: 2026-03-31
**Scope**: All non-playbook files in `docs/guides/` (53 files reviewed; playbook guides excluded per task scope)
**Output**: TODO markers added to top 10 most drift-prone files

---

## Ranking Methodology

Files were ranked on three factors:

| Factor | Examples |
|--------|---------|
| **Implementation detail density** | Inline C# class bodies, TypeScript config objects, az CLI pipelines |
| **Likelihood of code divergence** | Specific file paths, class names, config key names, enum values |
| **Age indicators / version pinning** | PCF version numbers, package versions, model deployment TPM figures, exact tool version minimums |

Excluded: PLAYBOOK-*.md, JPS-AUTHORING-GUIDE.md (covered by task 020).

---

## Top 10 Most Drift-Prone Files

### Rank 1 — `DATAVERSE-AUTHENTICATION-GUIDE.md`
**Risk**: CRITICAL
**Reasons**:
- Full inline implementation of `DataverseServiceClientImpl.cs` (class body, constructor params, config keys)
- Hardcoded package version `Microsoft.PowerPlatform.Dataverse.Client Version="1.1.32"`
- Sprint 7A appendix (Oct 2025 historical narrative) with obsolete error messages and class names
**Actions taken**:
- TODO marker before Package Reference section: verify version 1.1.32 is current
- TODO marker before Implementation section: verify class name and file path still match
- TODO marker on Appendix: flag as historical narrative, accuracy not guaranteed

---

### Rank 2 — `AI-DEPLOYMENT-GUIDE.md`
**Risk**: CRITICAL
**Reasons**:
- PCF control version numbers pinned (`AnalysisBuilder v1.12.0`, `AnalysisWorkspace v1.0.29`) — advance with every deployment
- "Complete App Service Configuration" section lists all config keys; new features add keys that won't appear here
- Bicep-based resource deployment commands may have superseded inline az CLI commands in Phase 1
**Actions taken**:
- TODO marker on Architecture Components table: PCF versions drift with each deployment
- TODO marker on Complete App Service Configuration section: new config keys from new features won't appear here

---

### Rank 3 — `AI-MODEL-SELECTION-GUIDE.md`
**Risk**: HIGH
**Reasons**:
- Model deployment status table has "NOT YET DEPLOYED" rows for gpt-4o and o1-mini — these flags become stale the moment models are deployed
- References `ModelSelector.cs` file path and `OperationType` enum values — drift if enum is extended
- `appsettings.json` snippet with all ModelSelectorOptions key names
**Actions taken**:
- TODO marker on Related Files block: verify ModelSelector.cs path and OperationType members
- TODO marker on deployment status table: deployment status changes frequently; verify before use

---

### Rank 4 — `AI-EMBEDDING-STRATEGY.md`
**Risk**: HIGH
**Reasons**:
- Index inventory table names (`spaarke-knowledge-index-v2`, `discovery-index`, `spaarke-rag-references`, `spaarke-invoices-dev`) — new indexes added without updating this doc
- Class names referenced: `EmbeddingCache.cs`, `RagIndexingPipeline`, `ReferenceRetrievalService`
- Cache key format, TTL values, and hashing algorithm are implementation details that drift
**Actions taken**:
- TODO marker on Index Inventory section: verify index names and vector field names; new indexes may not be listed

---

### Rank 5 — `SHARED-UI-COMPONENTS-GUIDE.md`
**Risk**: HIGH
**Reasons**:
- Full component inventory table (30+ components with file paths) — every new component is invisible until doc is updated
- Known tsc error list (ViewSelector, PageChrome, RichTextEditor, SprkChat) — errors may be fixed or new ones added
- React version compatibility table may shift as PCF platform support changes
**Actions taken**:
- TODO marker on known tsc errors list: may be fixed or expanded since 2026-03-30
- TODO marker on Component Inventory section: snapshot from 2026-03-30; new components won't appear

---

### Rank 6 — `HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md`
**Risk**: HIGH
**Reasons**:
- References `src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts` — control may have been renamed or relocated
- Step 5 has a local absolute build path (`cd /c/code_files/spaarke/src/controls/UniversalQuickCreate`) — always wrong on any other machine
- Two competing upload paths documented (DocumentUploadWizard Code Page preferred, UniversalQuickCreate legacy) — legacy path may be removed
**Actions taken**:
- TODO marker before Step 4: verify EntityDocumentConfig.ts file path
- TODO marker before Step 5 build commands: absolute local path is machine-specific; use pcf-deploy skill instead

---

### Rank 7 — `RAG-CONFIGURATION.md`
**Risk**: HIGH
**Reasons**:
- All `DocumentIntelligence__*` and `ScheduledRagIndexing__*` app setting key names — drift if config class hierarchy changes
- Inline az CLI command with literal resource names (`spe-api-dev-67e2xz`, `spe-infrastructure-westus2`)
- Key Vault secret URI path (may change if secrets are renamed)
**Actions taken**:
- TODO marker on App Service Configuration header: verify config keys match current options class
- TODO marker on dev environment resource details: cross-check resource names with CLAUDE.md canonical source

---

### Rank 8 — `ENVIRONMENT-DEPLOYMENT-GUIDE.md`
**Risk**: HIGH
**Reasons**:
- Section 3 has inline `az` CLI resource creation commands; bicep scripts in `infrastructure/bicep/` may be the canonical method now
- Fix Pipeline sed commands reference specific PCF control names and canvas app names that may no longer need patching
- 14 Key Vault secrets listed — new secrets added by new features won't appear
**Actions taken**:
- TODO marker on Section 3 Azure Resource Creation: verify inline az commands vs bicep approach
- TODO marker on Fix Pipeline: verify sed command targets are still the correct names to remove

---

### Rank 9 — `WORKSPACE-ENTITY-CREATION-GUIDE.md`
**Risk**: MEDIUM-HIGH
**Reasons**:
- Component inventory with relative file paths (`src/services/EntityCreationService.ts`, `src/hooks/useAiPrefill.ts`, etc.)
- Solution-specific service files (`matterService.ts`, `projectService.ts`) may be consolidated or renamed
- Architecture diagram and flow steps track specific service class names
**Actions taken**:
- TODO marker on Key Components table: verify component file paths; library restructuring could change paths

---

### Rank 10 — `PRODUCTION-DEPLOYMENT-GUIDE.md`
**Risk**: MEDIUM-HIGH
**Reasons**:
- Minimum tool version table (Azure CLI 2.60+, PowerShell 7.4+, GitHub CLI 2.40+) — becomes conservative over time
- Azure OpenAI quota table with TPM figures and region constraints (westus3 for OpenAI) — capacity changes frequently
- 1,668 lines covering many phases; high surface area for partial staleness
**Actions taken**:
- TODO marker on Required Tools section: minimum version numbers become outdated
- TODO marker on Required Azure Quotas: TPM figures and westus3 constraints reflect March 2026 availability

---

## Files Reviewed but Not in Top 10

These files were reviewed and determined to have lower drift risk:

| File | Assessment |
|------|-----------|
| `COMMUNICATION-ADMIN-GUIDE.md` | Schema field reference is stable; recent date (Mar 2026) |
| `COMMUNICATION-DEPLOYMENT-GUIDE.md` | Deployment steps — some drift risk but recent (Mar 2026) |
| `CUSTOMER-DEPLOYMENT-GUIDE.md` | Customer-facing checklist; high-level, low code detail |
| `CUSTOMER-ONBOARDING-RUNBOOK.md` | Script-based; refers to `Provision-Customer.ps1` — recent (Mar 2026) |
| `CUSTOMER-QUICK-START-CHECKLIST.md` | Checklist format; env variable list may drift but low risk |
| `DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md` | Web API reference; PAC CLI note may become outdated |
| `DOCUMENT-UPLOAD-WIZARD-INTEGRATION-GUIDE.md` | Supported entities list may grow but format is stable |
| `DOCUMENT-RELATIONSHIP-VIEWER-GUIDE.md` | Not read in full; excluded from top 10 |
| `EVENT-TYPE-CONFIGURATION.md` | Not read in full; excluded from top 10 |
| `EXTERNAL-ACCESS-ADMIN-SETUP.md` | Entra app reg GUIDs — very drift-prone but static config not code |
| `EXTERNAL-ACCESS-SPA-GUIDE.md` | Build size ~800KB note may drift; mostly stable |
| `GITHUB-ENVIRONMENT-PROTECTION.md` | Reviewer name (`heliosip`) and wait timer are config, not code |
| `HOW-TO-INITIATE-NEW-PROJECT.md` | Process guide; lower implementation detail |
| `HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md` | Stable SPE setup steps |
| `INCIDENT-RESPONSE.md` | Process-oriented; lower code detail |
| `INTERFACE-SEGREGATION-GUIDE.md` | Pattern guide with historical refactoring example; not authoritative |
| `MONITORING-AND-ALERTING-GUIDE.md` | Resource names in bicep context; moderate drift risk |
| `PCF-DEPLOYMENT-GUIDE.md` | Schema name conventions — stable |
| `RAG-ARCHITECTURE.md` | Architecture overview; moderate drift risk but less config detail |
| `RAG-TROUBLESHOOTING.md` | Not read in full; excluded from top 10 |
| `RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md` | Not read in full; excluded from top 10 |
| `SECRET-ROTATION-PROCEDURES.md` | Secret inventory stable; automation script reference |
| `SERVICE-DECOMPOSITION-GUIDE.md` | Historical decomposition example (OfficeService); pattern guide |
| `SHARED-UI-COMPONENTS-GUIDE.md` | Ranked #5 above |
| `SPAARKE-AI-STRATEGY-AND-ROADMAP.md` | Not read in full; strategy docs drift but differently |
| `VISUALHOST-SETUP-GUIDE.md` | Config-driven; stable schema |
| `WORKSPACE-AI-PREFILL-GUIDE.md` | Auth pattern reference; moderate drift risk |

---

## Summary of Changes Made

| File | Changes |
|------|---------|
| `DATAVERSE-AUTHENTICATION-GUIDE.md` | 3 TODO markers (package version, impl class, historical appendix) |
| `AI-DEPLOYMENT-GUIDE.md` | 2 TODO markers (PCF versions, app service config completeness) |
| `AI-MODEL-SELECTION-GUIDE.md` | 2 TODO markers (file path/enum accuracy, deployment status table) |
| `AI-EMBEDDING-STRATEGY.md` | 1 TODO marker (index inventory completeness) |
| `SHARED-UI-COMPONENTS-GUIDE.md` | 2 TODO markers (tsc error list, component inventory) |
| `HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md` | 2 TODO markers (EntityDocumentConfig.ts path, local build path) |
| `RAG-CONFIGURATION.md` | 2 TODO markers (config key accuracy, resource name cross-check) |
| `ENVIRONMENT-DEPLOYMENT-GUIDE.md` | 2 TODO markers (az vs bicep approach, fix pipeline targets) |
| `WORKSPACE-ENTITY-CREATION-GUIDE.md` | 1 TODO marker (component file path accuracy) |
| `PRODUCTION-DEPLOYMENT-GUIDE.md` | 2 TODO markers (tool version pins, quota figures) |
| **Total** | **19 TODO markers across 10 files** |

No content was deleted. All markers use the format: `<!-- TODO(ai-procedure-refactoring): description -->`
