# Multi-Environment Portability Strategy

**Date**: 2026-02-11
**Purpose**: Document how Spaarke handles multi-environment deployments (DEV/QA/PROD) for SaaS multi-tenant architecture

---

## Executive Summary

Spaarke uses a **layered approach** to achieve portability across environments without hardcoded values or per-environment configuration files:

1. **Alternate Keys** — For Dataverse record lookups (playbooks, templates, etc.)
2. **Option Set Values** — Stable choice values that travel with solutions
3. **Environment Variables** — Runtime configuration (endpoints, credentials, feature flags)
4. **Dataverse Environment Variables** — Per-environment configuration stored in Dataverse

---

## 1. Alternate Keys

### Why Alternate Keys

Dataverse primary key GUIDs are **auto-generated on solution import** — the same record will have a different GUID in DEV, QA, and PROD. Code that hardcodes GUIDs will break across environments.

**Decision**: Use alternate keys (stable string codes) for all Dataverse record lookups from code. GUIDs are only used when parsing user input or API responses.

### Alternate Key Candidates by Entity

| Entity | Alternate Key Field | Code Format | Use Case |
|--------|-------------------|-------------|----------|
| `sprk_analysisplaybook` | `sprk_playbookcode` | `PB-013`, `PB-001` | Job handlers, tools, workflow references |
| `sprk_aioutputtype` | `sprk_outputtypecode` | `OT-001`, `OT-002` | Playbook output type configuration |
| `sprk_analysistool` | `sprk_toolcode` | `TL-009`, `TL-010` | Tool handler discovery |
| `sprk_analysisaction` | `sprk_actioncode` | Action codes | Playbook action references |
| `sprk_analysisskill` | `sprk_skillcode` | Skill codes | Playbook skill references |
| `sprk_analysisknowledge` | `sprk_knowledgecode` | Knowledge codes | Knowledge source references |
| `sprk_modeldeployment` | `sprk_deploymentcode` | Deployment codes | Model deployment references |
| `sprk_prompttemplate` | `sprk_templatecode` | Template codes | Prompt template references |

### What Is Not an Alternate Key Issue

GUIDs parsed from user input or API responses are **not** a portability concern — they handle runtime data, not environment-specific configuration:
- `Guid.Parse(documentIdStr)` — document ID from API request
- `Guid.Parse(matter.GetProperty("sprk_matterid"))` — GUID extracted from Dataverse response

---

## 2. Option Set Values

### Why Option Set Values Are Safe

**Option set values (choice values) are STABLE across environments** when deployed via Dataverse solutions. When you export a solution, choice values are part of the solution metadata. Import preserves the exact integer values (100000000, 100000001, etc.). The same code works in all environments without any translation.

**Decision**: Option set constants in code are safe to keep. No alternate keys needed for choice values.

---

## 3. Environment Variables Architecture

### Two Distinct Mechanisms

| Configuration Type | Storage Mechanism | Varies By Environment | Varies By Tenant |
|-------------------|-------------------|----------------------|------------------|
| **Client auth config** | Dataverse Environment Variables | Yes | Yes |
| **Server auth/infra** | Azure App Service config + Key Vault | Yes | No (shared BFF) |
| **Feature Flags** | Azure App Service config | Yes | No |
| **AI Configuration** | Azure App Service config + Key Vault | Yes | No |
| **Business Rules** | Dataverse Environment Variables | No | Yes |

**Key architectural constraint**: Client-side components (code pages, PCF controls, Office add-ins) run in the browser inside a Dataverse page and cannot safely read Azure App Service settings. Instead, they call `resolveRuntimeConfig()` from `@spaarke/auth` at startup — this queries the 7 Dataverse Environment Variables via REST API using session cookie auth (before MSAL is initialized).

### Client-Side: Dataverse Environment Variables (7 vars)

Set once per Dataverse environment after solution import. No hardcoded values ship in the solution package.

| Variable | Purpose |
|----------|---------|
| `sprk_BffApiBaseUrl` | BFF API base URL |
| `sprk_BffApiAppId` | BFF API OAuth audience (app client ID) |
| `sprk_MsalClientId` | UI MSAL client ID for Entra ID sign-in |
| `sprk_TenantId` | Entra ID tenant ID |
| `sprk_AzureOpenAiEndpoint` | Azure OpenAI endpoint |
| `sprk_ShareLinkBaseUrl` | Base URL for document share links |
| `sprk_SharePointEmbeddedContainerId` | SPE Container ID for this environment |

**How they're read**: `resolveRuntimeConfig()` in `@spaarke/auth` queries Dataverse REST API at startup, caches in memory for session lifetime.

**Fail behavior**: If any required variable is missing, `resolveRuntimeConfig()` throws — no silent fallbacks to dev values.

### Server-Side: Azure App Service + Key Vault

Configures Sprk.Bff.Api running in Azure App Service. Sensitive values stored as Key Vault references. In local dev, use `appsettings.Development.json`, OS environment variables, or user secrets (`dotnet user-secrets`).

### Dataverse Environment Variables (Per-Tenant Business Config)

Dataverse Environment Variables store **business configuration** that varies by customer tenant but NOT by environment (DEV/QA/PROD). Examples: budget warning percentage, classification confidence threshold, per-tenant feature flag states.

**When to use**: Business thresholds, customer-specific rules, per-tenant AI endpoint overrides, per-tenant feature flags.

**Migration path**: Single-tenant MVP may store business rules in `appsettings.json`. Multi-tenant SaaS moves these to Dataverse Environment Variables for per-tenant isolation.

---

## 4. Decision Matrix: What Goes Where

| Type | Example | Storage | Varies By Env | Varies By Tenant | Deployment |
|------|---------|---------|---------------|------------------|------------|
| **Alternate Keys** | `sprk_playbookcode = "PB-013"` | Dataverse field | No | No | Solution export/import |
| **Primary Keys** | `sprk_playbookid = GUID` | Dataverse (auto) | Yes (regenerates) | No | Auto-generated on import |
| **Option Set Values** | `InvoiceStatus = 100000001` | Dataverse metadata | No | No | Solution export/import |
| **Client Auth Config** | `sprk_BffApiBaseUrl`, `sprk_MsalClientId` | Dataverse env var | Yes | Yes | Set per environment |
| **Server Env Variables** | `DATAVERSE_URL`, `REDIS_CONNECTION_STRING` | App Service config | Yes | No | Set per environment |
| **Secrets** | `API_CLIENT_SECRET` | Azure Key Vault | Yes | No | Set per environment |
| **Business Config** | `sprk_BudgetWarningPercentage` | Dataverse env var | No | Yes | Set per tenant |
| **Feature Flags** | `Redis:Enabled` | App Service config | Yes | No | Set per environment |

---

## 5. Anti-Patterns to Avoid

| Anti-Pattern | Correct Approach | Why |
|--------------|------------------|-----|
| Hardcode primary key GUIDs in code | Use alternate keys | GUIDs change across environments |
| Store environment URLs in code | Use environment variables | URLs vary by environment |
| Create config files per environment | Use environment variables + Dataverse env vars | Reduces deployment complexity |
| Use alternate keys for runtime data | Parse GUIDs from user input | Alternate keys are for configuration, not transactional data |
| Store secrets in appsettings.json | Use Azure Key Vault | Security best practice |
| Hardcode business rules in code | Use Dataverse Environment Variables | Enables per-tenant configuration |

---

This strategy enables Spaarke to scale to **100+ customer environments** with **zero manual GUID mapping** and **minimal per-environment configuration overhead**.
