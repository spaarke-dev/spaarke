# Multi-Environment Portability Strategy

**Date**: 2026-02-11
**Purpose**: Document how Spaarke handles multi-environment deployments (DEV/QA/PROD) for SaaS multi-tenant architecture

---

## Executive Summary

Spaarke uses a **layered approach** to achieve portability across environments without hardcoded values or per-environment configuration files:

1. **Alternate Keys** - For Dataverse record lookups (playbooks, templates, etc.)
2. **Option Set Values** - Stable choice values that travel with solutions
3. **Environment Variables** - Runtime configuration (endpoints, credentials, feature flags)
4. **Dataverse Environment Variables** - Per-environment configuration stored in Dataverse

---

## 1. Hardcoded GUIDs That Should Use Alternate Keys

### Current Status

| Location | Hardcoded GUID | Type | Should Use Alternate Key? | Priority |
|----------|----------------|------|---------------------------|----------|
| **InvoiceExtractionJobHandler** | `00000000-0000-0000-0000-000000000013` (PB-013) | Playbook lookup | ✅ **FIXED** (uses `GetByCodeAsync`) | ✅ DONE |
| **ModelEndpoints.cs** | `50000000-...` (5 model deployment IDs) | Model deployments | ⚠️ **STUB DATA** - Will query Dataverse in production | 🟡 FUTURE |
| **OfficeService.cs** | `00000000-0000-0000-0000-000000000001` | Test job ID | ❌ NO - Test data only | ✅ OK |

### Other GUIDs Found (Runtime Parsing - OK)

These are **NOT hardcoded** - they parse user input or API responses:
- `Guid.Parse(documentIdStr)` - Parsing document ID from request
- `Guid.Parse(matter.GetProperty("sprk_matterid"))` - Extracting GUID from Dataverse JSON response
- `Guid.Parse(recordGuid)` - User-provided record ID from API request

**Verdict**: These are fine - they handle runtime data, not environment-specific configuration.

---

## 2. Recommended Alternate Key Candidates

### High Priority (Implement Next)

| Entity | Current Lookup | Recommended Alternate Key | Use Case |
|--------|---------------|---------------------------|----------|
| **sprk_analysisplaybook** | ✅ **DONE** - `sprk_playbookcode` | `sprk_playbookcode` (PB-013, PB-001, etc.) | Job handlers, tools, workflow references |
| **sprk_aioutputtype** | By ID (not yet used) | `sprk_outputtypecode` (OT-001, OT-002, etc.) | Playbook output type configuration |
| **sprk_analysistool** | By ID (tool registration) | `sprk_toolcode` (TL-009, TL-010, TL-011) | Tool handler discovery and registration |

### Medium Priority (Phase 2)

| Entity | Recommended Alternate Key | Use Case |
|--------|---------------------------|----------|
| **sprk_analysisaction** | `sprk_actioncode` | Playbook action references |
| **sprk_analysisskill** | `sprk_skillcode` | Playbook skill references |
| **sprk_analysisknowledge** | `sprk_knowledgecode` | Knowledge source references |

### Low Priority (Consider for Full SaaS)

| Entity | Recommended Alternate Key | Use Case |
|--------|---------------------------|----------|
| **sprk_modeldeployment** | `sprk_deploymentcode` | Model deployment references |
| **sprk_prompttemplate** | `sprk_templatecode` | Prompt template references |

---

## 3. Option Set Values (Stable Across Environments)

### Why Option Set Values Are SAFE

**Option set values (choice values) are STABLE across environments when deployed via Dataverse solutions.**

Example:
```csharp
// These values REMAIN THE SAME in DEV, QA, PROD
private const int InvoiceStatusToReview = 100000000;
private const int ExtractionStatusExtracted = 100000001;
private const int SignalTypeBudgetExceeded = 100000000;
```

**Why?** When you export a solution:
1. Choice values are part of the solution metadata
2. Import preserves the exact values (100000000, 100000001, etc.)
3. Same code works in all environments

### Catalog of Option Set Values in Codebase

| Entity Field | Constant Name | Value | Used In |
|--------------|---------------|-------|---------|
| **sprk_invoice.sprk_invoicereviewstatus** | `ReviewStatusConfirmedInvoice` | 100000001 | InvoiceReviewService |
| **sprk_invoice.sprk_invoicereviewstatus** | `ReviewStatusRejectedNotInvoice` | 100000002 | InvoiceReviewService |
| **sprk_invoice.sprk_extractionstatus** | `ExtractionStatusExtracted` | 100000001 | InvoiceExtractionJobHandler |
| **sprk_invoice.sprk_extractionstatus** | `ExtractionStatusFailed` | 100000002 | InvoiceExtractionJobHandler |
| **sprk_spendsignal.sprk_signaltype** | `SignalTypeBudgetExceeded` | 100000000 | SignalEvaluationService |
| **sprk_spendsignal.sprk_signaltype** | `SignalTypeBudgetWarning` | 100000001 | SignalEvaluationService |
| **sprk_spendsignal.sprk_signaltype** | `SignalTypeVelocitySpike` | 100000002 | SignalEvaluationService |
| **sprk_spendsignal.sprk_severity** | `SeverityInfo` | 100000000 | SignalEvaluationService |
| **sprk_spendsignal.sprk_severity** | `SeverityWarning` | 100000001 | SignalEvaluationService |
| **sprk_spendsignal.sprk_severity** | `SeverityCritical` | 100000002 | SignalEvaluationService |
| **sprk_spendsnapshot.sprk_periodtype** | `PeriodTypeMonth` | 100000000 | SpendSnapshotService |
| **sprk_spendsnapshot.sprk_periodtype** | `PeriodTypeToDate` | 100000003 | SpendSnapshotService |
| **sprk_billingevent.sprk_visibilitystate** | `VisibilityState_Invoiced` | 100000000 | SpendSnapshotService |
| **sprk_document.sprk_documenttype** | `DocumentTypeEmail` | 100000006 | EmailToDocumentJobHandler |
| **sprk_document.sprk_documenttype** | `DocumentTypeEmailAttachment` | 100000007 | EmailAttachmentProcessor |
| **sprk_documentrelationship.sprk_relationshiptype** | `RelationshipTypeEmailAttachment` | 100000000 | EmailAttachmentProcessor |
| **email.directioncode** | `DirectionReceived` | 100000000 | EmailToEmlConverter |
| **email.directioncode** | `DirectionSent` | 100000001 | EmailToEmlConverter |

**Recommendation**: These are **SAFE to keep as constants** - no alternate keys needed.

---

## 4. Environment Variables Architecture

### Purpose and Scope

Environment variables handle **runtime configuration** that varies by environment but NOT by customer tenant.

| Configuration Type | Storage Mechanism | Varies By Environment | Varies By Tenant | Example |
|-------------------|-------------------|----------------------|------------------|---------|
| **Authentication** | Environment variables | ✅ Yes (DEV/QA/PROD) | ❌ No | `TENANT_ID`, `API_APP_ID`, `API_CLIENT_SECRET` |
| **Azure Resources** | Environment variables | ✅ Yes (DEV/QA/PROD) | ❌ No | `DATAVERSE_URL`, `REDIS_CONNECTION_STRING` |
| **Feature Flags** | Environment variables | ✅ Yes (DEV/QA/PROD) | ❌ No | `ENABLE_AI_FEATURES`, `ENABLE_REDIS` |
| **AI Configuration** | Environment variables | ✅ Yes (DEV/QA/PROD) | ❌ No | `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_KEY` |
| **Business Rules** | Dataverse Environment Variables | ❌ No | ✅ Yes (per tenant) | Budget thresholds, classification confidence |

### Environment Variables Used in Spaarke

#### Core Authentication (Required)

| Variable | Purpose | Example (DEV) | Example (PROD) |
|----------|---------|---------------|----------------|
| `TENANT_ID` | Azure AD tenant for authentication | `a221a95e-6abc-...` | `a221a95e-6abc-...` (same) |
| `API_APP_ID` | BFF API app registration client ID | `1e40baad-e065-...` | `1e40baad-e065-...` (same or different) |
| `API_CLIENT_SECRET` | Client secret for app authentication | `***` (Key Vault) | `***` (Key Vault) |

#### Azure Resources (Required)

| Variable | Purpose | Example (DEV) | Example (PROD) |
|----------|---------|---------------|----------------|
| `Dataverse:ServiceUrl` | Dataverse environment URL | `https://spaarkedev1.crm.dynamics.com` | `https://spaarkeprod.crm.dynamics.com` |
| `Redis:ConnectionString` | Redis cache connection string | `spaarke-redis-dev.redis.cache...` | `spaarke-redis-prod.redis.cache...` |
| `ServiceBus:ConnectionString` | Azure Service Bus for job queue | `spaarke-servicebus-dev...` | `spaarke-servicebus-prod...` |

#### AI Services (Optional - Feature Flag Controlled)

| Variable | Purpose | Example (DEV) | Example (PROD) |
|----------|---------|---------------|----------------|
| `ai-openai-endpoint` | Azure OpenAI endpoint | `https://spaarke-openai-dev...` | `https://spaarke-openai-prod...` |
| `ai-openai-key` | Azure OpenAI API key | `***` (Key Vault) | `***` (Key Vault) |
| `ai-docintel-endpoint` | Document Intelligence endpoint | `https://westus2.api.cognitive...` | `https://westus2.api.cognitive...` |
| `ai-docintel-key` | Document Intelligence key | `***` (Key Vault) | `***` (Key Vault) |
| `ai-search-endpoint` | Azure AI Search endpoint | `https://spaarke-search-dev...` | `https://spaarke-search-prod...` |
| `ai-search-key` | Azure AI Search admin key | `***` (Key Vault) | `***` (Key Vault) |

#### Feature Flags (Optional)

| Variable | Purpose | Default | DEV | PROD |
|----------|---------|---------|-----|------|
| `Redis:Enabled` | Enable Redis caching | `false` | `false` (uses in-memory) | `true` |
| `DocumentIntelligence:Enabled` | Enable AI features | `false` | `true` | `true` |
| `Analysis:EnableMultiDocumentAnalysis` | Enable multi-doc analysis | `false` | `true` | `true` |

### How Environment Variables Are Loaded

1. **Azure App Service Configuration** (PROD)
   - Set in Azure Portal → App Service → Configuration → Application Settings
   - Automatically injected as environment variables at runtime
   - Secured with Azure Key Vault references: `@Microsoft.KeyVault(SecretUri=...)`

2. **Local Development** (DEV)
   - Set in `appsettings.Development.json` (gitignored)
   - OR set as OS environment variables
   - OR use User Secrets: `dotnet user-secrets set "TENANT_ID" "..."`

3. **CI/CD Pipelines** (GitHub Actions)
   - Stored in GitHub Secrets
   - Injected during deployment via token replacement
   - Example: `${{ secrets.TENANT_ID }}`

---

## 5. Dataverse Environment Variables (Per-Tenant Configuration)

### Purpose

**Dataverse Environment Variables** store **business configuration** that varies by customer tenant but NOT by environment (DEV/QA/PROD).

### When to Use Dataverse Environment Variables

| Use Case | Storage | Example |
|----------|---------|---------|
| **Business thresholds** | Dataverse Environment Variable | Budget warning percentage (80%, 90%, etc.) |
| **Customer-specific rules** | Dataverse Environment Variable | Classification confidence threshold (0.7, 0.8, etc.) |
| **AI endpoints (multi-tenant)** | Dataverse Environment Variable | Per-tenant Prompt Flow endpoints |
| **Feature flags per tenant** | Dataverse Environment Variable | Enable AI features for Customer A, not Customer B |

### Examples in Spaarke

From `AnalysisOptions.cs`:
```csharp
public class AnalysisOptions
{
    // These could be Dataverse Environment Variables in multi-tenant SaaS:
    public bool EnableAiFeatures { get; set; }              // sprk_EnableAiFeatures
    public bool EnableMultiDocumentAnalysis { get; set; }   // sprk_EnableMultiDocumentAnalysis
    public string? PromptFlowEndpoint { get; set; }         // sprk_PromptFlowEndpoint
    public string? RagEndpoint { get; set; }                // sprk_RagEndpoint
}
```

From `FinanceOptions.cs`:
```csharp
public class FinanceOptions
{
    // Business rules - could be Dataverse Environment Variables:
    public decimal ClassificationConfidenceThreshold { get; set; } = 0.7M;  // sprk_ClassificationConfidenceThreshold
    public int BudgetWarningPercentage { get; set; } = 80;                  // sprk_BudgetWarningPercentage
    public decimal VelocitySpikePct { get; set; } = 0.5M;                   // sprk_VelocitySpikePct

    // AI configuration - could be Dataverse Environment Variables:
    public string ClassificationDeploymentName { get; set; } = "gpt-4o-mini"; // sprk_ClassificationDeploymentName
    public string ExtractionDeploymentName { get; set; } = "gpt-4o";         // sprk_ExtractionDeploymentName
}
```

### How to Access Dataverse Environment Variables

**In Plugins:**
```csharp
// Direct access in plugin context
var budgetThreshold = context.OrganizationService
    .GetEnvironmentVariableValue<decimal>("sprk_BudgetWarningPercentage");
```

**In BFF API:**
```csharp
// Query via IDataverseService
var envVarValue = await _dataverseService.GetEnvironmentVariableValueAsync(
    "sprk_BudgetWarningPercentage",
    ct);
```

### Migration Path to Multi-Tenant SaaS

**Current (Single Tenant MVP):**
- Business rules in `appsettings.json` or environment variables
- Same configuration for all customers (because there's only one customer)

**Future (Multi-Tenant SaaS):**
- Business rules in Dataverse Environment Variables
- Per-tenant configuration stored in each tenant's Dataverse environment
- BFF API reads from Dataverse at runtime

---

## 6. Multi-Environment Deployment Flow

### Scenario: Deploy Finance Intelligence Module to PROD

**Step 1: Code Deployment (Same Everywhere)**
```bash
# Build once, deploy everywhere
dotnet publish -c Release

# Deploy to DEV App Service
az webapp deploy --name spe-api-dev-67e2xz --src-path ./publish.zip

# Deploy to QA App Service (SAME CODE)
az webapp deploy --name spe-api-qa-abc123 --src-path ./publish.zip

# Deploy to PROD App Service (SAME CODE)
az webapp deploy --name spe-api-prod-xyz789 --src-path ./publish.zip
```

**Step 2: Solution Deployment (Dataverse)**
```bash
# Export from DEV
pac solution export --name Spaarke --path Spaarke_1_0_0_0.zip

# Import to QA (GUIDs regenerate, alternate keys preserved)
pac solution import --path Spaarke_1_0_0_0.zip --environment qa-env

# Import to PROD (GUIDs regenerate, alternate keys preserved)
pac solution import --path Spaarke_1_0_0_0.zip --environment prod-env
```

**Step 3: Environment-Specific Configuration**

| Configuration | DEV | QA | PROD | How Set |
|---------------|-----|-----|------|---------|
| `Dataverse:ServiceUrl` | `https://spaarkedev1.crm...` | `https://spaarkeqa1.crm...` | `https://spaarkeprod.crm...` | Azure App Service Config |
| `Redis:ConnectionString` | `spaarke-redis-dev...` | `spaarke-redis-qa...` | `spaarke-redis-prod...` | Azure App Service Config |
| `ai-openai-endpoint` | `spaarke-openai-dev...` | `spaarke-openai-qa...` | `spaarke-openai-prod...` | Azure Key Vault |
| `sprk_playbookcode = "PB-013"` | Same playbook record | Same playbook record | Same playbook record | Dataverse (travels with solution) |
| `sprk_BudgetWarningPercentage` | 80 | 80 | 80 | Dataverse Environment Variable |

**Step 4: Verification**
```bash
# Test playbook lookup works in all environments
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz
curl https://spe-api-qa-abc123.azurewebsites.net/healthz
curl https://spe-api-prod-xyz789.azurewebsites.net/healthz

# Verify playbook lookup by code (not GUID)
# DEV: Returns playbook with GUID 1e657651-9308-...
# QA:  Returns playbook with GUID 9a8b7c6d-1234-... (DIFFERENT!)
# PROD: Returns playbook with GUID 4f5e6d7c-8901-... (DIFFERENT!)
# All using same code: GetByCodeAsync("PB-013")
```

---

## 7. Comparison Matrix: What Goes Where

| Type | Example | Storage | Varies By Env | Varies By Tenant | Deployment |
|------|---------|---------|---------------|------------------|------------|
| **Alternate Keys** | `sprk_playbookcode = "PB-013"` | Dataverse field | ❌ No | ❌ No | Solution export/import |
| **Primary Keys** | `sprk_playbookid = GUID` | Dataverse (auto) | ✅ Yes (regenerates) | ❌ No | Auto-generated on import |
| **Option Set Values** | `InvoiceStatus = 100000001` | Dataverse metadata | ❌ No | ❌ No | Solution export/import |
| **Environment Variables** | `DATAVERSE_URL` | App Service config | ✅ Yes | ❌ No | Set per environment |
| **Secrets** | `API_CLIENT_SECRET` | Azure Key Vault | ✅ Yes | ❌ No | Set per environment |
| **Dataverse Env Vars** | `sprk_BudgetWarningPercentage` | Dataverse env var | ❌ No | ✅ Yes | Set per tenant |
| **Feature Flags** | `Redis:Enabled` | App Service config | ✅ Yes | ❌ No | Set per environment |

---

## 8. Recommendations and Next Steps

### Immediate Actions (This Sprint)

1. ✅ **DONE**: Implement PlaybookLookupService with alternate keys
2. ✅ **DONE**: Update InvoiceExtractionJobHandler to use `GetByCodeAsync("PB-013")`
3. ⏭️ **TODO**: Add `sprk_playbookcode` alternate key field to Dataverse
4. ⏭️ **TODO**: Backfill existing playbook records with codes (PB-001, PB-013, etc.)

### Phase 2 (Next Sprint)

1. Create `ILookupService<TEntity>` generic interface for alternate key lookups
2. Implement alternate keys for AI tools: `sprk_toolcode` (TL-009, TL-010, TL-011)
3. Implement alternate keys for output types: `sprk_outputtypecode`
4. Update tool registration to use alternate key lookups

### Phase 3 (Multi-Tenant Preparation)

1. Migrate business rules from `FinanceOptions` to Dataverse Environment Variables
2. Create `IEnvironmentVariableService` for reading Dataverse env vars
3. Update configuration loading to support per-tenant overrides
4. Document per-tenant configuration deployment process

### Phase 4 (Full SaaS)

1. Implement tenant isolation in codebase (tenant ID header routing)
2. Deploy separate Dataverse environments per customer tenant
3. Implement cross-tenant deployment automation (Bicep templates)
4. Build admin UI for managing Dataverse Environment Variables per tenant

---

## 9. Decision Matrix: When to Use Each Approach

| Requirement | Use This | Example |
|-------------|----------|---------|
| Reference a **specific Dataverse record** across environments | **Alternate Keys** | Playbook lookup by code |
| Store **choice values** that are part of data model | **Option Set Values** | Invoice status, signal type |
| Configure **Azure resource endpoints** per environment | **Environment Variables** | Dataverse URL, Redis connection |
| Store **secrets** (keys, passwords) | **Azure Key Vault** + Environment Variables | OpenAI API key |
| Configure **business rules** that vary per tenant | **Dataverse Environment Variables** | Budget thresholds |
| Toggle **features** per environment | **Environment Variables** (feature flags) | Enable Redis in PROD only |
| Toggle **features** per tenant | **Dataverse Environment Variables** | Enable AI for Tenant A only |

---

## 10. Anti-Patterns to Avoid

| ❌ Anti-Pattern | ✅ Correct Approach | Why |
|----------------|-------------------|-----|
| Hardcode primary key GUIDs in code | Use alternate keys | GUIDs change across environments |
| Store environment URLs in code | Use environment variables | URLs vary by environment |
| Create config files per environment | Use environment variables + Dataverse env vars | Reduces deployment complexity |
| Use alternate keys for runtime data | Parse GUIDs from user input | Alternate keys are for configuration, not transactional data |
| Store secrets in appsettings.json | Use Azure Key Vault | Security best practice |
| Hardcode business rules in code | Use Dataverse Environment Variables | Enables per-tenant configuration |

---

This strategy ensures Spaarke can scale to **100+ customer environments** with **zero manual GUID mapping** and **minimal per-environment configuration overhead**.
