# Spaarke Document Intelligence – Analysis Feature
## Design Specification

> **Version**: 1.0  
> **Date**: December 11, 2025  
> **Status**: Draft for Review  
> **Owner**: Spaarke Product / Document Intelligence  
> **Related**: [Overview](Spaarke%20Document%20Intelligence%20AI%20overview.txt)

---

## Executive Summary

This specification defines the **Analysis** feature for Spaarke Document Intelligence, enabling users to execute AI-driven analyses on documents with configurable actions, scopes (Skills, Knowledge, Tools), and outputs. The feature extends the existing SDAP BFF API architecture and leverages the proven patterns established in the Document Summary MVP (v3.2.4).

**Key Capabilities:**
- Analysis Builder UI for configuring custom AI workflows
- Two-column Analysis Workspace (editable output + source preview)
- Conversational AI refinement within Analysis context
- Playbook system for reusable analysis configurations
- Multi-output formats (Document, Email, Teams, Notifications, Workflows)
- Microsoft Foundry integration for prompt flow orchestration and evaluation
- Hybrid RAG deployment (shared and customer-specific AI Search indexes)
- Session-based working document versioning with SPE storage

**Microsoft AI Stack Alignment (December 2025):**
- **Azure AI Foundry** - Prompt flow orchestration, evaluation, and monitoring
- **Azure OpenAI Service** - GPT-4o models with JSON mode and structured outputs
- **Azure AI Search** - Hybrid search with semantic ranking for RAG
- **Azure AI Studio** - Centralized model management and deployment
- **Prompt Flow** - Visual prompt engineering and testing workflows
- **AI Evaluation SDK** - Automated quality, safety, and performance testing

---

## 1. Architecture Context

### 1.1 Existing Foundation

The Document Intelligence Analysis feature builds on proven components:

| Component | Status | Description |
|-----------|--------|-------------|
| **Sprk.Bff.Api** | ✅ Production | ASP.NET Core 8 Minimal API, orchestrates all backend services |
| **DocumentIntelligenceService** | ✅ Production | Text extraction (native, OCR, Document Intelligence) |
| **OpenAiClient** | ✅ Production | Azure OpenAI integration with streaming support |
| **TextExtractorService** | ✅ Production | Multi-format text extraction pipeline |
| **SpeFileStore** | ✅ Production | SharePoint Embedded file access facade |
| **SSE Streaming** | ✅ Production | Server-Sent Events for real-time AI responses |
| **Rate Limiting** | ✅ Production | Per-user throttling (10/min streaming, 20/min batch) |
| **Entity Extraction** | ✅ Production | Structured data extraction to Dataverse fields |

### 1.2 Architecture Alignment

This design strictly adheres to existing ADRs and patterns:

| Principle | Implementation |
|-----------|----------------|
| **ADR-001: Minimal APIs** | All new endpoints in `Api/Ai/AnalysisEndpoints.cs` |
| **ADR-003: Lean Authorization** | Extend existing `AiAuthorizationFilter` for Analysis entities |
| **ADR-007: SpeFileStore Facade** | File access exclusively through `SpeFileStore` |
| **ADR-008: Endpoint Filters** | `AnalysisAuthorizationFilter` for per-resource checks |
| **BFF Orchestration** | BFF coordinates Dataverse + SPE + Azure AI services |
| **OBO Token Flow** | User identity preserved through all service calls |

### 1.3 Component Interaction

```
┌────────────────────────────────────────────────────────────────────────┐
│                       Dataverse Model-Driven App                       │
│  ┌─────────────────────┐          ┌──────────────────────────────────┐ │
│  │  Document Form      │          │  Analysis Workspace (Custom Page)│ │
│  │  ┌───────────────┐  │          │  ┌────────────┬──────────────┐  │ │
│  │  │ Analysis Tab  │──┼──────────┼─▶│ Working    │ Source       │  │ │
│  │  │ (grid + cmd)  │  │          │  │ Document   │ Preview      │  │ │
│  │  └───────────────┘  │          │  │ (editable) │ (read-only)  │  │ │
│  └─────────────────────┘          │  └────────────┴──────────────┘  │ │
│                                   │  │   AI Chat (refinement)       │  │ │
│  ┌─────────────────────┐          │  └──────────────────────────────┘  │ │
│  │  Analysis Builder   │          └──────────────────────────────────┘ │
│  │  (modal)            │          │                                      │
│  │  • Action selector  │          │                                      │
│  │  • Scope config     │          │                                      │
│  │  • Output options   │          │                                      │
│  └─────────────────────┘          │                                      │
└────────────────────────────────────────────────────────────────────────┘
                    │
                    │ HTTPS + Bearer Token (Entra ID)
                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                          Sprk.Bff.Api                                   │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │  /api/ai/analysis/*  (NEW)                                       │  │
│  │  • POST /execute - Execute analysis with streaming               │  │
│  │  • POST /continue - Continue analysis via chat                   │  │
│  │  • POST /save - Save working document to SPE                     │  │
│  │  • GET /{id} - Retrieve analysis history                         │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                                                                         │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │  Services (NEW)                                                  │  │
│  │  • AnalysisOrchestrationService - Coordinates analysis execution│  │
│  │  • ScopeResolverService - Loads Skills, Knowledge, Tools        │  │
│  │  • AnalysisContextBuilder - Builds prompts from scopes          │  │
│  │  • WorkingDocumentService - Manages editable output state       │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                                                                         │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │  Existing Services (REUSE)                                       │  │
│  │  • IOpenAiClient - Azure OpenAI streaming/completions           │  │
│  │  • ITextExtractor - Text extraction pipeline                    │  │
│  │  • SpeFileStore - File access and storage                       │  │
│  │  • IDataverseService - Entity CRUD operations                   │  │
│  └──────────────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────────┘
                    │
                    │ OBO Token Exchange
                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                      Azure Services                                     │
│  ┌──────────────┐  ┌──────────────┐  ┌────────────┐  ┌─────────────┐  │
│  │ Azure OpenAI │  │ Doc Intel    │  │ Dataverse  │  │ SPE         │  │
│  │ gpt-4o-mini  │  │ Text Extract │  │ Entities   │  │ Files       │  │
│  └──────────────┘  └──────────────┘  └────────────┘  └─────────────┘  │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Microsoft AI Services Integration

### 2.1 Azure AI Foundry Architecture

**Microsoft AI Foundry** (formerly Azure AI Studio) provides enterprise-grade AI orchestration. Our implementation leverages these services:

```
┌─────────────────────────────────────────────────────────────────────┐
│                      Azure AI Foundry Hub                           │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌──────────────────┐  ┌──────────────────┐  ┌─────────────────┐  │
│  │ Prompt Flow      │  │ Evaluation       │  │ Content Safety  │  │
│  │ • Analysis flows │  │ • Quality metrics│  │ • Input filters │  │
│  │ • Chat flows     │  │ • Safety checks  │  │ • Output checks │  │
│  │ • Tool chains    │  │ • Performance    │  │ • PII detection │  │
│  └──────────────────┘  └──────────────────┘  └─────────────────┘  │
│                                                                     │
│  ┌──────────────────┐  ┌──────────────────┐  ┌─────────────────┐  │
│  │ Model Catalog    │  │ Index Management │  │ Monitoring      │  │
│  │ • GPT-4o         │  │ • RAG indexes    │  │ • Tracing       │  │
│  │ • GPT-4o-mini    │  │ • Embeddings     │  │ • Cost tracking │  │
│  │ • Fine-tuned     │  │ • Hybrid search  │  │ • Quality KPIs  │  │
│  └──────────────────┘  └──────────────────┘  └─────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      Sprk.Bff.Api Integration                       │
│  • PromptFlowClient - Execute Foundry flows                         │
│  • EvaluationService - Run quality checks                           │
│  • ContentSafetyFilter - Pre/post-processing filters                │
│  • TelemetryCollector - Push metrics to Foundry                     │
└─────────────────────────────────────────────────────────────────────┘
```

### 2.2 Prompt Flow Integration

**Prompt Flows** replace hardcoded prompt templates with versioned, testable workflows.

| Flow Name | Purpose | Inputs | Outputs |
|-----------|---------|--------|----------|
| `analysis-execute` | Main analysis execution | document_text, action, scopes | analysis_output |
| `analysis-continue` | Chat refinement | working_doc, chat_history, user_msg | updated_output |
| `knowledge-rag` | RAG retrieval + grounding | query, index_name, filters | grounded_context |
| `entity-extraction` | Structured extraction | document_text | entities_json |

**Benefits:**
- Visual editing in AI Foundry portal
- Version control and A/B testing
- Built-in evaluation and monitoring
- No code deployment for prompt changes

**Implementation:**
```csharp
public interface IPromptFlowClient
{
    /// <summary>
    /// Execute a Prompt Flow deployed in Azure AI Foundry.
    /// </summary>
    Task<PromptFlowResult> ExecuteFlowAsync(
        string flowName,
        Dictionary<string, object> inputs,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stream a Prompt Flow execution for real-time output.
    /// </summary>
    IAsyncEnumerable<PromptFlowChunk> StreamFlowAsync(
        string flowName,
        Dictionary<string, object> inputs,
        CancellationToken cancellationToken);
}
```

### 2.3 Continuous Improvement: Evaluation Pipeline

**Built-in Quality Assurance** using Azure AI Evaluation SDK:

```
User Analysis → Execute → Collect Trace → Async Evaluation → Dashboard
                   │
                   ├─ Groundedness check (RAG quality)
                   ├─ Relevance check (answers question)
                   ├─ Coherence check (logical flow)
                   ├─ Fluency check (readability)
                   └─ Safety check (content filters)
```

**Evaluation Metrics:**
| Metric | Target | Action on Failure |
|--------|--------|-------------------|
| Groundedness | > 0.8 | Log for prompt tuning |
| Relevance | > 0.7 | Flag for review |
| Safety Score | > 0.95 | Block output |
| Latency P95 | < 5s | Scale resources |

**Tuning Process:**
1. **Collect** - Every analysis execution logs to AI Foundry
2. **Evaluate** - Nightly batch evaluation runs on sample
3. **Analyze** - Dashboard shows metric trends
4. **Tune** - Update Prompt Flows in AI Foundry portal
5. **Deploy** - New flow version auto-picked up by BFF
6. **Validate** - A/B test shows improvement

### 2.4 Hybrid RAG Deployment Models

**Flexible Knowledge Grounding** accommodates multiple deployment scenarios:

#### Model 1: Shared Spaarke Index (Anonymous)
```
┌─────────────────────────────────────────────────────────┐
│  Azure AI Search - Spaarke Shared Tenant               │
│  Index: spaarke-knowledge-shared                        │
│  ────────────────────────────────────────────────────── │
│  • Tenant filter: customerId field                      │
│  • Public knowledge: Spaarke templates, guides          │
│  • Customer documents: Isolated by customerId           │
│  • Cost: Shared across customers                        │
└─────────────────────────────────────────────────────────┘
```

**Use Case:** Small to mid-size customers, cost-sensitive deployments

#### Model 2: Dedicated Customer Index (Spaarke Tenant)
```
┌─────────────────────────────────────────────────────────┐
│  Azure AI Search - Spaarke Tenant                       │
│  Index: customer-{customerid}-knowledge                 │
│  ────────────────────────────────────────────────────── │
│  • Isolated index per customer                          │
│  • Full control over indexing strategy                  │
│  • Higher performance (no tenant filtering)             │
│  • Cost: Billed separately to customer                  │
└─────────────────────────────────────────────────────────┘
```

**Use Case:** Enterprise customers, compliance requirements, high-volume

#### Model 3: Customer-Owned Index (Customer Tenant)
```
┌─────────────────────────────────────────────────────────┐
│  Azure AI Search - Customer's Own Tenant                │
│  Index: {customer-defined-name}                         │
│  ────────────────────────────────────────────────────── │
│  • Spaarke connects via cross-tenant auth               │
│  • Customer controls all data residency                 │
│  • Customer manages costs directly                      │
│  • Spaarke uses managed identity or service principal   │
└─────────────────────────────────────────────────────────┘
```

**Use Case:** Regulated industries, data sovereignty requirements

**Configuration Entity:**
```csharp
/// <summary>
/// sprk_knowledgedeployment - Defines RAG deployment per customer/knowledge source
/// </summary>
public class KnowledgeDeployment
{
    public Guid Id { get; set; }
    public string Name { get; set; } // "Acme Corp Knowledge Base"
    public Guid CustomerId { get; set; }
    
    public DeploymentModel Model { get; set; } // Shared, Dedicated, CustomerOwned
    
    // For Model 1 (Shared)
    public string TenantFilterField { get; set; } = "customerId";
    
    // For Model 2 (Dedicated)
    public string IndexName { get; set; } // "customer-guid-knowledge"
    
    // For Model 3 (Customer Owned)
    public string CustomerTenantId { get; set; }
    public string CustomerSearchEndpoint { get; set; }
    public string CustomerSearchKey { get; set; } // Stored in Key Vault
    
    public bool IsActive { get; set; }
}
```

### 2.5 Microsoft Foundry Document Intelligence Integration

**Leverage Foundry's Built-in Document Processing:**

| Foundry Service | Usage in Spaarke | Benefits |
|-----------------|------------------|----------|
| **Document Intelligence Skill** | Text extraction from PDFs, images | Managed service, no DIY code |
| **Chunking Strategy** | Split documents for RAG | Optimized chunk sizes |
| **Embedding Generation** | Create vector embeddings | Cached, optimized |
| **Index Management** | Create/update AI Search indexes | Automated lifecycle |

**Architecture Decision:**
- **Phase 1:** Keep existing `ITextExtractor` for backward compatibility
- **Phase 2:** Migrate to Foundry Document Intelligence skill
- **Phase 3:** Deprecate custom extraction code

---

## 3. Multi-Tenant Deployment Architecture

### 3.1 Deployment Models

Spaarke supports two primary deployment models with distinct architectural considerations:

#### **Model 1: Spaarke-Hosted SaaS**
- Customer subscribes to Spaarke service
- Deployed in Spaarke's Azure tenant
- Spaarke manages all infrastructure
- Multi-customer isolation via Dataverse security
- Shared Azure resources with tenant filtering

#### **Model 2: Customer-Deployed (Bring Your Own Tenant)**
- Customer deploys Spaarke in their own Azure/Power Platform tenant
- Customer owns and manages infrastructure
- Customer controls data residency and compliance
- **Delivered as Managed Application + Managed Power App Solution**
- Spaarke provides updates via managed solution versioning

**This design spec prioritizes Model 2 readiness** while maintaining Model 1 compatibility.

### 3.2 Parameterization Strategy for Multi-Tenant Deployment

#### **Power Platform: Environment Variables**

All tenant-specific and environment-specific configurations use **Dataverse Environment Variables** (not hard-coded values).

**Recommended Environment Variables:**

| Display Name | Schema Name | Type | Default Value | Purpose |
|--------------|-------------|------|---------------|---------|
| BFF API Base URL | `sprk_BffApiBaseUrl` | Text | `https://spe-api-dev.azurewebsites.net` | BFF endpoint for API calls |
| Azure OpenAI Endpoint | `sprk_AzureOpenAiEndpoint` | Text | (empty) | Customer's OpenAI endpoint |
| Azure AI Search Endpoint | `sprk_AzureAiSearchEndpoint` | Text | (empty) | Customer's AI Search endpoint |
| Document Intelligence Endpoint | `sprk_DocIntelEndpoint` | Text | (empty) | Customer's Doc Intelligence endpoint |
| Application Insights Key | `sprk_AppInsightsInstrumentationKey` | Text | (empty) | Customer's telemetry |
| Enable AI Features | `sprk_EnableAiFeatures` | Yes/No | Yes | Feature flag for AI |
| Default RAG Deployment Model | `sprk_DefaultRagModel` | Text | `Shared` | Default: Shared, Dedicated, CustomerOwned |
| SPE Container Type ID | `sprk_ContainerTypeId` | Text | (customer-specific) | SharePoint Embedded container type |

**Usage in PCF Controls:**

```typescript
// Before (hard-coded):
const apiBaseUrl = "https://spe-api-dev.azurewebsites.net";

// After (environment variable):
const apiBaseUrl = Xrm.Utility.getGlobalContext()
    .organizationSettings
    .environmentVariables["sprk_BffApiBaseUrl"];
```

**Usage in Canvas Apps:**

```
// Reference environment variable
Environment('BFF API Base URL')
```

**Benefits:**
- ✅ No code changes for different tenants
- ✅ Admin-configurable via Power Platform admin center
- ✅ Survives solution imports/updates
- ✅ Supports multiple environments (dev, test, prod)
- ✅ Can reference Azure Key Vault secrets

#### **Azure Infrastructure: Bicep Parameters**

All Azure resources provisioned via **parameterized Bicep templates**.

**Main Bicep Parameter File Structure:**

```bicep
// main.bicepparam
using './main.bicep'

// Tenant-specific parameters
param customerName = 'acme-corp'
param environment = 'prod'
param location = 'eastus'

// Resource naming
param resourceGroupName = 'rg-${customerName}-spaarke-${environment}'
param appServiceName = 'app-${customerName}-spaarke-bff-${environment}'
param openAiName = 'oai-${customerName}-spaarke-${environment}'
param searchName = 'srch-${customerName}-spaarke-${environment}'
param docIntelName = 'di-${customerName}-spaarke-${environment}'

// Networking
param allowedIpRanges = ['203.0.113.0/24'] // Customer's IP ranges
param vnetIntegration = true
param privateEndpoints = false

// AI Configuration
param openAiSkuName = 'S0'
param openAiDeployments = [
  {
    name: 'gpt-4o-mini'
    model: 'gpt-4o-mini'
    version: '2024-07-18'
    capacity: 10
  }
]

// Feature flags
param enableAiFeatures = true
param enableMultiDocumentAnalysis = false // Phase 2

// Key Vault
param keyVaultName = 'kv-${customerName}-spaarke-${environment}'
param keyVaultSecretReaderPrincipalId = '' // Customer's managed identity

// Tags for cost tracking
param costCenter = 'IT-Legal'
param owner = 'john.doe@acme.com'
```

**Bicep Module: BFF API App Service**

```bicep
// modules/bff-api.bicep
param appServiceName string
param appServicePlanId string
param location string
param environmentVariables array

resource appService 'Microsoft.Web/sites@2023-01-01' = {
  name: appServiceName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: appServicePlanId
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      appSettings: concat([
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'ApplicationInsights__InstrumentationKey'
          value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/appinsights-key/)'
        }
        {
          name: 'Ai__Enabled'
          value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/ai-enabled/)'
        }
        {
          name: 'Ai__OpenAiEndpoint'
          value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/ai-openai-endpoint/)'
        }
        {
          name: 'Ai__OpenAiKey'
          value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/ai-openai-key/)'
        }
      ], environmentVariables)
    }
  }
}
```

#### **BFF API: Configuration Providers**

**appsettings.json Structure (Parameterized):**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "#{TENANT_ID}#",  // Token replacement
    "ClientId": "#{API_CLIENT_ID}#",
    "ClientSecret": "#{API_CLIENT_SECRET}#",
    "Audience": "api://#{API_CLIENT_ID}#"
  },
  "Ai": {
    "Enabled": "#{AI_ENABLED}#",
    "OpenAiEndpoint": "#{AZURE_OPENAI_ENDPOINT}#",
    "OpenAiKey": "#{AZURE_OPENAI_KEY}#",
    "DocIntelEndpoint": "#{DOC_INTEL_ENDPOINT}#",
    "DocIntelKey": "#{DOC_INTEL_KEY}#",
    "PromptFlowEndpoint": "#{PROMPT_FLOW_ENDPOINT}#"
  },
  "Dataverse": {
    "ServiceUrl": "#{DATAVERSE_URL}#",
    "ClientId": "#{API_CLIENT_ID}#",
    "ClientSecret": "#{API_CLIENT_SECRET}#"
  },
  "Redis": {
    "ConnectionString": "#{REDIS_CONNECTION_STRING}#",
    "InstanceName": "#{CUSTOMER_NAME}#"
  }
}
```

**Configuration Transformation Pipeline:**

```yaml
# azure-pipelines-deploy.yml (Azure DevOps)
steps:
  - task: FileTransform@1
    displayName: 'Transform appsettings.json'
    inputs:
      folderPath: '$(Build.ArtifactStagingDirectory)'
      fileType: 'json'
      targetFiles: '**/appsettings.json'

  - task: AzureRmWebAppDeployment@4
    displayName: 'Deploy BFF API to Customer Tenant'
    inputs:
      azureSubscription: 'CustomerAzureConnection'
      appType: 'webAppLinux'
      WebAppName: '$(AppServiceName)'
      deployToSlotOrASE: true
      ResourceGroupName: '$(ResourceGroupName)'
      SlotName: 'production'
```

**GitHub Actions Alternative:**

```yaml
# .github/workflows/deploy-customer.yml
- name: Replace tokens in appsettings.json
  uses: cschleiden/replace-tokens@v1
  with:
    files: '**/appsettings*.json'
  env:
    TENANT_ID: ${{ secrets.CUSTOMER_TENANT_ID }}
    API_CLIENT_ID: ${{ secrets.API_CLIENT_ID }}
    AZURE_OPENAI_ENDPOINT: ${{ secrets.AZURE_OPENAI_ENDPOINT }}
```

### 3.3 Managed Solution Design Patterns

#### **Power Platform Solution Structure**

```
📦 Spaarke_DocumentIntelligence_1_0_0_0 (Managed)
├── 📁 Entities
│   ├── sprk_analysis
│   ├── sprk_analysisaction
│   ├── sprk_analysisskill
│   ├── sprk_analysisknowledge
│   ├── sprk_knowledgedeployment
│   └── ... (all new entities)
├── 📁 Environment Variables
│   ├── sprk_BffApiBaseUrl
│   ├── sprk_AzureOpenAiEndpoint
│   ├── sprk_EnableAiFeatures
│   └── ... (all parameters)
├── 📁 Connection References (Phase 2)
│   ├── sprk_DataverseConnection
│   └── sprk_Office365Connection (for email)
├── 📁 Custom Pages
│   ├── sprk_AnalysisWorkspace
│   └── sprk_AnalysisBuilder
├── 📁 PCF Components
│   ├── SpeFileViewer (existing)
│   └── AnalysisWorkspace (new)
├── 📁 Web Resources
│   ├── JavaScript libraries
│   └── TypeScript API clients
├── 📁 Forms
│   ├── sprk_document (modified with Analysis tab)
│   └── Analysis entity forms
└── 📁 Security Roles
    ├── Spaarke Analysis User
    ├── Spaarke Analysis Admin
    └── Spaarke Knowledge Manager
```

#### **Connection References (Future-Proofing)**

While not implemented in Phase 1, the solution structure supports **Connection References** for Canvas Apps and Flows:

```xml
<!-- ConnectionReference.xml -->
<connectionreference 
    connectionreferencelogicalname="sprk_sharedoffice365">
  <connectionreferenceinstancename>
    shared_office365
  </connectionreferenceinstancename>
  <connectionid></connectionid>
  <iscustomizable>1</iscustomizable>
  <statecode>0</statecode>
  <statuscode>1</statuscode>
</connectionreference>
```

**Usage:** Admin provides connection at solution import time.

### 3.4 Customer-Deployed Infrastructure: Managed Application

For **Azure resources**, customers deploy via **Azure Managed Application** (ARM template package).

#### **Managed Application Structure**

```
📦 spaarke-document-intelligence-app.zip
├── mainTemplate.json          # ARM/Bicep compiled template
├── createUiDefinition.json    # Azure portal deployment wizard
└── viewDefinition.json        # Custom resource provider views
```

**createUiDefinition.json (Deployment Wizard):**

```json
{
  "$schema": "https://schema.management.azure.com/schemas/2021-09-09/createUiDefinition.json#",
  "handler": "Microsoft.Azure.CreateUIDef",
  "version": "0.1.2-preview",
  "parameters": {
    "basics": [
      {
        "name": "customerName",
        "type": "Microsoft.Common.TextBox",
        "label": "Organization Name",
        "placeholder": "acme-corp",
        "constraints": {
          "required": true,
          "regex": "^[a-z0-9-]{3,24}$"
        }
      }
    ],
    "steps": [
      {
        "name": "aiConfig",
        "label": "AI Configuration",
        "elements": [
          {
            "name": "enableAiFeatures",
            "type": "Microsoft.Common.CheckBox",
            "label": "Enable AI Document Intelligence",
            "defaultValue": true
          },
          {
            "name": "openAiDeployment",
            "type": "Microsoft.Common.DropDown",
            "label": "Azure OpenAI Deployment",
            "defaultValue": "New",
            "toolTip": "Create new or use existing Azure OpenAI resource",
            "constraints": {
              "allowedValues": [
                { "label": "Create new Azure OpenAI resource", "value": "New" },
                { "label": "Use existing Azure OpenAI resource", "value": "Existing" }
              ]
            }
          }
        ]
      }
    ]
  }
}
```

**Benefits:**
- ✅ Guided deployment wizard in Azure portal
- ✅ Customer controls resource placement
- ✅ Integrated cost tracking
- ✅ Spaarke can publish updates via Marketplace

### 3.5 Deployment Checklist for Multi-Tenant Readiness

**Phase 1 Implementation Requirements:**

- [ ] **Environment Variables Created**
  - [ ] `sprk_BffApiBaseUrl` with default value
  - [ ] `sprk_AzureOpenAiEndpoint` (empty, customer-provided)
  - [ ] `sprk_EnableAiFeatures` (Yes/No toggle)
  - [ ] All AI service endpoints as env vars
  
- [ ] **PCF Controls Use Environment Variables**
  - [ ] Remove hard-coded API URLs
  - [ ] Add `getGlobalContext().environmentVariables` access
  - [ ] Fallback to default if env var missing (dev experience)
  
- [ ] **BFF API Parameterized**
  - [ ] Token-based `appsettings.json`
  - [ ] No hard-coded tenant IDs or endpoints
  - [ ] Key Vault references for all secrets
  - [ ] Configuration validation on startup
  
- [ ] **Bicep Templates Parameterized**
  - [ ] `main.bicepparam` with all customer-specific values
  - [ ] Separate param files for dev/test/prod
  - [ ] Resource naming follows conventions
  - [ ] Tags for cost center and owner
  
- [ ] **Managed Solution Package**
  - [ ] All entities in single solution
  - [ ] Environment variables exported
  - [ ] Security roles exported
  - [ ] Solution marked as managed (for export)
  - [ ] Version number follows semver (1.0.0.0)
  
- [ ] **Documentation for Customer Deployment**
  - [ ] Prerequisites (Azure subscription, Power Platform env)
  - [ ] Step-by-step Bicep deployment guide
  - [ ] Environment variable configuration guide
  - [ ] Post-deployment validation checklist
  - [ ] Troubleshooting common issues

### 3.6 Migration Path: Unmanaged → Managed

**Current State:** Spaarke uses unmanaged solutions for development flexibility.

**Future State:** Customers receive managed solutions for version control and updates.

**Transition Strategy:**

1. **Phase 1 (This Release):** Continue unmanaged development
   - Implement all parameterization (env vars, Bicep params)
   - Test deployment to separate tenant (unmanaged)
   - Validate all hard-coded values removed
   
2. **Phase 2 (Next Release):** Introduce managed solution export
   - Export as managed solution for customer deployments
   - Keep separate unmanaged solution for Spaarke development
   - Test managed solution upgrade scenarios
   
3. **Phase 3 (Production):** Managed Application Marketplace
   - Publish managed application to Azure Marketplace (optional)
   - Automated updates via Marketplace
   - Customer-controlled update cadence

**No Conversion Required Now:** Continue unmanaged development; managed export happens at release time.

### 3.7 Configuration Documentation Template

Each environment variable will be documented in deployment guide:

```markdown
## Environment Variable: sprk_BffApiBaseUrl

**Type:** Text
**Required:** Yes
**Default:** `https://spe-api-dev.azurewebsites.net` (development)

**Purpose:** Base URL for the Spaarke BFF API. This is the endpoint that PCF controls 
and Canvas Apps call to interact with Azure services (OpenAI, Document Intelligence, 
SharePoint Embedded).

**Customer Deployment:**
1. After deploying Azure resources via Bicep, note the App Service URL
2. In Power Platform admin center, navigate to Environment Variables
3. Update `sprk_BffApiBaseUrl` to your deployed App Service URL
4. Format: `https://app-{customerName}-spaarke-bff-prod.azurewebsites.net`

**Validation:**
- URL must be HTTPS
- App Service must be accessible from Power Platform tenant
- Test with: `GET {url}/healthz` (should return "Healthy")

**Troubleshooting:**
- If PCF controls show "API unavailable", verify this URL is correct
- Check App Service is running and accessible
- Verify CORS settings allow Power Platform origin
```

---

## 4. Data Model

### 2.1 New Dataverse Entities

#### **sprk_analysis** (Primary Entity)

The Analysis entity represents a single AI-executed analysis on a Document.

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| Name | `sprk_name` | Text (200) | ✅ | Analysis title/name |
| Document | `sprk_documentid` | Lookup (sprk_document) | ✅ | Parent document |
| Action | `sprk_actionid` | Lookup (sprk_analysisaction) | ✅ | Analysis action definition |
| Status | `sprk_status` | Choice | ✅ | NotStarted, InProgress, Completed, Failed |
| Status Reason | `statuscode` | Status | ✅ | Standard status reason |
| Working Document | `sprk_workingdocument` | Multiline Text | ❌ | Current working output (Markdown) |
| Working Document File | `sprk_workingdocumentfile` | File (SPE) | ❌ | Working doc stored in SPE for versioning |
| Session ID | `sprk_sessionid` | Text (50) | ❌ | Current editing session identifier |
| Final Output | `sprk_finaloutput` | Multiline Text | ❌ | Completed analysis output |
| Output File | `sprk_outputfileid` | Lookup (sprk_document) | ❌ | Saved output as new Document |
| Started On | `sprk_startedon` | DateTime | ❌ | Analysis start timestamp |
| Completed On | `sprk_completedon` | DateTime | ❌ | Analysis completion timestamp |
| Error Message | `sprk_errormessage` | Multiline Text | ❌ | Error details if failed |
| Input Tokens | `sprk_inputtokens` | Whole Number | ❌ | Token usage (input) |
| Output Tokens | `sprk_outputtokens` | Whole Number | ❌ | Token usage (output) |

**Relationships:**
- N:1 to `sprk_document` (parent)
- N:1 to `sprk_analysisaction`
- 1:N to `sprk_analysischatmessage` (chat history)
- 1:N to `sprk_analysisworkingversion` (working doc versions)
- N:N to `sprk_analysisskill` (via `sprk_analysis_skill`)
- N:N to `sprk_analysisknowledge` (via `sprk_analysis_knowledge`)
- N:N to `sprk_analysistool` (via `sprk_analysis_tool`)

#### **sprk_analysisworkingversion** (Working Document Versions)

Stores snapshots of working document during refinement sessions.

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| Name | `sprk_name` | Text (200) | ✅ | Version name (auto: "Session 1 - Step 3") |
| Analysis | `sprk_analysisid` | Lookup (sprk_analysis) | ✅ | Parent analysis |
| Session ID | `sprk_sessionid` | Text (50) | ✅ | Session identifier |
| Version Number | `sprk_versionnumber` | Whole Number | ✅ | Sequential version within session |
| Content | `sprk_content` | Multiline Text | ❌ | Markdown content snapshot |
| SPE File | `sprk_spefile` | File (SPE) | ✅ | Working doc file in SPE |
| Drive ID | `sprk_driveid` | Text (200) | ✅ | SPE container drive ID |
| Item ID | `sprk_itemid` | Text (200) | ✅ | SPE file item ID |
| Created On | `createdon` | DateTime | ✅ | Version timestamp |
| Created By Chat | `sprk_createdbychat` | Lookup (sprk_analysischatmessage) | ❌ | Chat message that created this version |
| Token Delta | `sprk_tokendelta` | Whole Number | ❌ | Tokens added/removed from previous version |

**Versioning Strategy:**
- **Session-based:** Each user session (workspace open) gets unique `sessionId`
- **Auto-save:** Version created after each chat interaction
- **Manual save:** User can create named checkpoint
- **Storage:** Files stored in SPE container under `/analyses/{analysisId}/working/`
- **Retention:** Keep all versions until analysis marked complete, then retain last 10
- **Naming:** `working-session-{sessionId}-v{versionNumber}.md`

#### **sprk_analysisaction** (Action Definitions)

Defines what the AI should do (e.g., "Summarize", "Review Agreement", "Prepare Response").

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| Name | `sprk_name` | Text (200) | ✅ | Action name (e.g., "Summarize Document") |
| Description | `sprk_description` | Multiline Text | ❌ | User-facing description |
| System Prompt | `sprk_systemprompt` | Multiline Text | ✅ | Base prompt template |
| Is Active | `statecode` | State | ✅ | Active/Inactive |
| Sort Order | `sprk_sortorder` | Whole Number | ❌ | Display order in UI |

#### **sprk_analysisskill** (Skills - How to Work)

Defines behavioral instructions (e.g., "Write concisely", "Use legal terminology").

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| Name | `sprk_name` | Text (200) | ✅ | Skill name |
| Description | `sprk_description` | Multiline Text | ❌ | User-facing description |
| Prompt Fragment | `sprk_promptfragment` | Multiline Text | ✅ | Instruction to add to prompt |
| Category | `sprk_category` | Choice | ❌ | Tone, Style, Format, Expertise |
| Is Active | `statecode` | State | ✅ | Active/Inactive |

#### **sprk_analysisknowledge** (Knowledge - Grounding Sources)

Defines knowledge sources for RAG (rules, policies, templates, prior work).

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| Name | `sprk_name` | Text (200) | ✅ | Knowledge source name |
| Description | `sprk_description` | Multiline Text | ❌ | User-facing description |
| Type | `sprk_type` | Choice | ✅ | Document, Rule, Template, RAG_Index |
| Content | `sprk_content` | Multiline Text | ❌ | Inline content (for rules/templates) |
| Document | `sprk_documentid` | Lookup (sprk_document) | ❌ | Reference document |
| Deployment | `sprk_deploymentid` | Lookup (sprk_knowledgedeployment) | ❌ | RAG deployment configuration |
| Is Active | `statecode` | State | ✅ | Active/Inactive |

#### **sprk_knowledgedeployment** (RAG Deployment Configuration)

Defines hybrid RAG deployment model per customer or knowledge source.

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| Name | `sprk_name` | Text (200) | ✅ | Deployment name |
| Description | `sprk_description` | Multiline Text | ❌ | Deployment description |
| Customer | `sprk_customerid` | Lookup (account) | ❌ | Customer owning this deployment |
| Model | `sprk_deploymentmodel` | Choice | ✅ | Shared, Dedicated, CustomerOwned |
| Index Name | `sprk_indexname` | Text (200) | ✅ | AI Search index name |
| Tenant Filter Field | `sprk_tenantfilterfield` | Text (100) | ❌ | Field for tenant isolation (Model 1) |
| Customer Tenant ID | `sprk_customertenantid` | Text (100) | ❌ | Customer's Azure tenant ID (Model 3) |
| Search Endpoint | `sprk_searchendpoint` | Text (500) | ❌ | Custom search endpoint (Model 3) |
| Search Key Secret | `sprk_searchkeysecret` | Text (200) | ❌ | Key Vault secret name for search key |
| Embedding Model | `sprk_embeddingmodel` | Choice | ✅ | text-embedding-ada-002, text-embedding-3-small |
| Is Active | `statecode` | State | ✅ | Active/Inactive |

#### **sprk_analysistool** (Tools - Function Helpers)

Defines reusable AI tools (extractors, analyzers, generators).

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| Name | `sprk_name` | Text (200) | ✅ | Tool name |
| Description | `sprk_description` | Multiline Text | ❌ | User-facing description |
| Tool Type | `sprk_tooltype` | Choice | ✅ | Extractor, Analyzer, Generator, Validator |
| Handler Class | `sprk_handlerclass` | Text (200) | ✅ | C# class implementing tool |
| Configuration | `sprk_configuration` | Multiline Text | ❌ | JSON config for tool |
| Is Active | `statecode` | State | ✅ | Active/Inactive |

#### **sprk_analysisplaybook** (Playbooks - Saved Configurations)

Reusable combinations of Action + Scopes + Output settings.

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| Name | `sprk_name` | Text (200) | ✅ | Playbook name |
| Description | `sprk_description` | Multiline Text | ❌ | User-facing description |
| Action | `sprk_actionid` | Lookup (sprk_analysisaction) | ✅ | Default action |
| Output Type | `sprk_outputtype` | Choice | ✅ | Document, Email, Teams, Notification, Workflow |
| Is Public | `sprk_ispublic` | Two Options | ✅ | Visible to all users |
| Is Active | `statecode` | State | ✅ | Active/Inactive |

**Relationships:**
- N:N to `sprk_analysisskill`
- N:N to `sprk_analysisknowledge`
- N:N to `sprk_analysistool`

#### **sprk_analysisemailmetadata** (Email Integration)

Stores email-specific metadata for analyses exported as emails.

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| Name | `sprk_name` | Text (200) | ✅ | Email subject line |
| Analysis | `sprk_analysisid` | Lookup (sprk_analysis) | ✅ | Parent analysis |
| Email Activity | `sprk_emailactivityid` | Lookup (email) | ❌ | Linked Power Apps email activity |
| To Recipients | `sprk_torecipients` | Multiline Text | ❌ | Email addresses (newline-separated) |
| CC Recipients | `sprk_ccrecipients` | Multiline Text | ❌ | CC email addresses |
| Include Source Link | `sprk_includesourcelink` | Two Options | ✅ | Attach link to source document |
| Include Analysis File | `sprk_includeanalysisfile` | Two Options | ✅ | Attach analysis as PDF/DOCX |
| Email Sent | `sprk_emailsent` | Two Options | ✅ | Email successfully sent |
| Sent On | `sprk_senton` | DateTime | ❌ | Email send timestamp |

#### **sprk_analysischatmessage** (Chat History)

Stores conversational refinement within an Analysis.

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| Analysis | `sprk_analysisid` | Lookup (sprk_analysis) | ✅ | Parent analysis |
| Role | `sprk_role` | Choice | ✅ | User, Assistant, System |
| Content | `sprk_content` | Multiline Text | ✅ | Message content |
| Created On | `createdon` | DateTime | ✅ | Message timestamp |
| Token Count | `sprk_tokencount` | Whole Number | ❌ | Tokens in this message |

### 2.2 Entity Relationship Diagram

```
┌──────────────────────┐
│  sprk_document       │
│  (EXISTING)          │
└──────────┬───────────┘
           │ 1:N
           │
           ▼
┌──────────────────────┐         ┌──────────────────────┐
│  sprk_analysis       │   N:1   │  sprk_analysisaction │
│  ──────────────────  │────────▶│  (Action Definition) │
│  • Working Document  │         └──────────────────────┘
│  • Final Output      │
│  • Status            │         ┌──────────────────────┐
│  • Token Usage       │   N:N   │  sprk_analysisskill  │
└──────────┬───────────┘◀───────▶│  (How to work)       │
           │                     └──────────────────────┘
           │ 1:N
           │                     ┌──────────────────────┐
           ▼                N:N  │ sprk_analysisknowledge│
┌──────────────────────┐◀───────▶│  (Grounding)         │
│sprk_analysischatmsg  │         └──────────────────────┘
│  (Chat History)      │
└──────────────────────┘         ┌──────────────────────┐
                            N:N  │  sprk_analysistool   │
                          ◀──────│  (Function helpers)  │
                                 └──────────────────────┘

┌──────────────────────┐
│sprk_analysisplaybook │         (Connects to Action + Scopes)
│  (Saved Config)      │◀───N:N───▶(Skills, Knowledge, Tools)
└──────────────────────┘
```

---

## 3. API Design

### 3.1 New Endpoints

All endpoints follow `POST /api/ai/analysis/*` pattern with JWT authentication and rate limiting.

#### **POST /api/ai/analysis/execute**

Execute a new analysis with Server-Sent Events streaming.

**Request:**
```json
{
  "documentIds": ["guid"], // Array supports multi-doc in Phase 2; Phase 1 uses [0] only
  "actionId": "guid",
  "skillIds": ["guid", "guid"],
  "knowledgeIds": ["guid"],
  "toolIds": ["guid"],
  "outputType": "document", // document | email | teams | notification | workflow
  "playbookId": "guid" // optional - pre-populates scopes
}
```

**Multi-Document Design (Phase 2 Ready):**
- Request accepts array of `documentIds`
- Analysis entity adds N:N relationship to `sprk_document` (vs. current N:1)
- `ITextExtractor` already supports batch extraction
- Prompt builder concatenates with section markers: `## Document 1: {name}\n{content}\n\n## Document 2: {name}...`
- Phase 1 validation: `if (request.DocumentIds.Length > 1) throw new NotImplementedException("Multi-document support coming in Phase 2");`

**Response:** SSE stream with chunks:
```
data: {"type":"metadata","analysisId":"guid","documentName":"contract.pdf"}

data: {"type":"chunk","content":"## Executive Summary\n\nThis agreement..."}

data: {"type":"chunk","content":" establishes terms between parties..."}

data: {"type":"done","analysisId":"guid","tokenUsage":{"input":1500,"output":800}}
```

**Error Codes:**
- `400` - Invalid request (missing required fields)
- `404` - Document/Action/Playbook not found
- `429` - Rate limit exceeded (10/min)
- `503` - AI service unavailable

#### **POST /api/ai/analysis/{analysisId}/continue**

Continue an existing analysis via conversational chat.

**Request:**
```json
{
  "message": "Make this more concise and focus on liability clauses"
}
```

**Response:** SSE stream with updated working document chunks

#### **POST /api/ai/analysis/{analysisId}/save**

Save working document to SharePoint Embedded and create new Document record.

**Request:**
```json
{
  "fileName": "Agreement Summary.docx",
  "format": "docx" // docx | pdf | md | txt
}
```

**Response:**
```json
{
  "documentId": "guid",
  "driveId": "b!...",
  "itemId": "01ABC...",
  "webUrl": "https://..."
}
```

#### **GET /api/ai/analysis/{analysisId}**

Retrieve analysis record with chat history.

**Response:**
```json
{
  "id": "guid",
  "documentId": "guid",
  "documentName": "contract.pdf",
  "action": { "id": "guid", "name": "Summarize" },
  "status": "completed",
  "workingDocument": "## Summary\n...",
  "finalOutput": "## Summary\n...",
  "chatHistory": [
    { "role": "user", "content": "Analyze this document", "timestamp": "2025-12-11T10:00:00Z" },
    { "role": "assistant", "content": "Here's the analysis...", "timestamp": "2025-12-11T10:00:15Z" }
  ],
  "tokenUsage": { "input": 2000, "output": 1200 },
  "startedOn": "2025-12-11T10:00:00Z",
  "completedOn": "2025-12-11T10:02:30Z"
}
```

#### **POST /api/ai/analysis/{analysisId}/export**

Export analysis output in various formats.

**Request:**
```json
{
  "format": "email", // email | teams | pdf | docx
  "options": {
    "emailTo": ["user@example.com"],
    "emailCc": ["manager@example.com"],
    "emailSubject": "Agreement Analysis Results",
    "includeSourceLink": true,
    "includeAnalysisFile": true,
    "attachmentFormat": "pdf" // pdf | docx
  }
}
```

**Response (Email):**
```json
{
  "exportType": "email",
  "success": true,
  "details": {
    "emailActivityId": "guid",
    "emailMetadataId": "guid",
    "status": "pending_send", // pending_send | sent | failed
    "openEmailUrl": "https://org.crm.dynamics.com/main.aspx?etn=email&id=..."
  }
}
```

**Email Integration Notes:**
- Creates Power Apps `email` activity record (not immediate send)
- Links to `sprk_analysisemailmetadata` for tracking
- Returns URL to open email in MDA for user review/editing
- User clicks "Send" in Power Apps email form
- Server-side sync handles actual delivery
- Email activity tracks status (Draft, Sent, Failed)
- Maintains full audit trail and relationship to Analysis

**Benefits of Power Apps Email Entity:**
- ✅ Email templates and signatures auto-applied
- ✅ Server-side sync handles delivery and tracking
- ✅ Full email history in timeline
- ✅ Reply tracking and threading
- ✅ Compliance and retention policies applied
- ✅ User can edit before sending
- ✅ Attachments managed through Dataverse

**Downside/Complexity:**
- ⚠️ Requires server-side sync configuration per user
- ⚠️ User must have email enabled in Dataverse
- ⚠️ Cannot send "on behalf of" other users without delegation
- ⚠️ Extra step (user must click Send) vs. direct API send

**Implementation Decision:** Use Power Apps email entity for better integration with MDA workflows and compliance. For automated scenarios (workflow triggers), provide fallback to Graph API direct send.

### 3.2 Rate Limiting

Reuse existing policies from Document Summary:

| Endpoint Pattern | Policy | Limit |
|------------------|--------|-------|
| `/execute`, `/continue` | `ai-stream` | 10 requests/minute per user |
| `/save`, `/export` | `ai-batch` | 20 requests/minute per user |
| `/playbooks/*` (config) | None | No limit (read-heavy) |

### 3.3 Authorization

Extend existing `AiAuthorizationFilter`:

```csharp
public class AnalysisAuthorizationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, 
        EndpointFilterDelegate next)
    {
        var analysisId = context.GetArgument<Guid>(0);
        var dataverseService = context.HttpContext.RequestServices
            .GetRequiredService<IDataverseService>();
        var userId = context.HttpContext.User.GetUserId();

        // Check: User has read access to parent Document
        var analysis = await dataverseService.GetAnalysisAsync(analysisId);
        var hasAccess = await dataverseService.CheckDocumentAccessAsync(
            analysis.DocumentId, userId);

        if (!hasAccess)
            return Results.Problem("Forbidden", statusCode: 403);

        return await next(context);
    }
}
```

---

## 4. Service Layer

### 4.1 New Services

#### **IAnalysisOrchestrationService**

Coordinates analysis execution across multiple services.

**Design for Multi-Document (Phase 2):** All methods accept `documentIds` array parameter but Phase 1 only processes first document. Internal architecture supports multiple documents to minimize refactoring in Phase 2.

```csharp
public interface IAnalysisOrchestrationService
{
    /// <summary>
    /// Execute a new analysis with streaming results.
    /// Creates Analysis record in Dataverse and orchestrates:
    /// 1. Scope resolution (Skills, Knowledge, Tools)
    /// 2. Context building (prompt construction)
    /// 3. File extraction (via ITextExtractor) - supports multiple docs
    /// 4. AI execution (via IOpenAiClient)
    /// 5. Working document updates
    /// </summary>
    /// <param name="request">Analysis request with documentIds array</param>
    /// <remarks>
    /// Phase 1: Only request.DocumentIds[0] is processed.
    /// Phase 2: All documents in array are processed and synthesized.
    /// </remarks>
    IAsyncEnumerable<AnalysisChunk> ExecuteAnalysisAsync(
        AnalysisExecutionRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Continue existing analysis via chat.
    /// Loads analysis context + chat history and streams updated output.
    /// </summary>
    IAsyncEnumerable<AnalysisChunk> ContinueAnalysisAsync(
        Guid analysisId,
        string userMessage,
        CancellationToken cancellationToken);

    /// <summary>
    /// Save working document to SPE and create Document record.
    /// </summary>
    Task<SavedDocumentResult> SaveWorkingDocumentAsync(
        Guid analysisId,
        SaveDocumentRequest request,
        CancellationToken cancellationToken);
}
```

#### **IScopeResolverService**

Loads and resolves Skills, Knowledge, and Tools.

```csharp
public interface IScopeResolverService
{
    /// <summary>
    /// Load scope definitions from Dataverse.
    /// </summary>
    Task<ResolvedScopes> ResolveScopesAsync(
        Guid[] skillIds,
        Guid[] knowledgeIds,
        Guid[] toolIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Load scopes from a Playbook.
    /// </summary>
    Task<ResolvedScopes> ResolvePlaybookScopesAsync(
        Guid playbookId,
        CancellationToken cancellationToken);
}

public record ResolvedScopes(
    AnalysisSkill[] Skills,
    AnalysisKnowledge[] Knowledge,
    AnalysisTool[] Tools);
```

#### **IAnalysisContextBuilder**

Builds prompts by combining Action + Scopes + Document content.

```csharp
public interface IAnalysisContextBuilder
{
    /// <summary>
    /// Build system prompt from Action and Skills.
    /// </summary>
    string BuildSystemPrompt(
        AnalysisAction action,
        AnalysisSkill[] skills);

    /// <summary>
    /// Build user prompt with document content and Knowledge grounding.
    /// </summary>
    Task<string> BuildUserPromptAsync(
        string documentText,
        AnalysisKnowledge[] knowledge,
        CancellationToken cancellationToken);

    /// <summary>
    /// Build continuation prompt with chat history.
    /// </summary>
    string BuildContinuationPrompt(
        ChatMessage[] history,
        string userMessage,
        string currentWorkingDocument);
}
```

#### **IWorkingDocumentService**

Manages transient working document state during analysis refinement.

```csharp
public interface IWorkingDocumentService
{
    /// <summary>
    /// Update working document in Dataverse as chunks stream in.
    /// Uses optimistic concurrency to avoid conflicts.
    /// </summary>
    Task UpdateWorkingDocumentAsync(
        Guid analysisId,
        string content,
        CancellationToken cancellationToken);

    /// <summary>
    /// Mark analysis as completed and copy working document to final output.
    /// </summary>
    Task FinalizeAnalysisAsync(
        Guid analysisId,
        int inputTokens,
        int outputTokens,
        CancellationToken cancellationToken);
}
```

### 4.2 Reused Services

The following services are used as-is from the Document Summary implementation:

| Service | Usage in Analysis |
|---------|-------------------|
| `IOpenAiClient` | Stream AI completions for analysis and chat continuation |
| `ITextExtractor` | Extract text from source document |
| `SpeFileStore` | Read source files, save output files |
| `IDataverseService` | CRUD operations on Analysis entities |

---

## 5. UI Components

### 5.1 Document Form - Analysis Tab

**Location:** Extends existing `sprk_document` main form

**Components:**
- **Analysis Grid** - Shows all analyses for this document
  - Columns: Name, Action, Status, Started On, Completed On
  - Click to open Analysis Workspace
- **Command Bar**
  - "+ New Analysis" button → Opens Analysis Builder modal

**Implementation:** Standard Dataverse form customization, no custom PCF required.

### 5.2 Analysis Builder (Modal)

**Purpose:** Configure a new analysis before execution.

**UI Structure:**
```
┌─────────────────────────────────────────────────────┐
│  New Analysis                                  [X]  │
├─────────────────────────────────────────────────────┤
│                                                     │
│  Document: contract-2025.pdf                        │
│                                                     │
│  ┌──────────────────────────────────────────────┐  │
│  │ 1. Choose Action                             │  │
│  │    ○ Summarize Document                      │  │
│  │    ○ Review Agreement                        │  │
│  │    ○ Prepare Response to Email               │  │
│  │    ○ Extract Key Terms                       │  │
│  └──────────────────────────────────────────────┘  │
│                                                     │
│  ┌──────────────────────────────────────────────┐  │
│  │ 2. Configure Scopes (Optional)               │  │
│  │                                              │  │
│  │  Skills:                                     │  │
│  │    ☑ Concise writing                         │  │
│  │    ☐ Legal terminology                       │  │
│  │    ☐ Executive-level language                │  │
│  │                                              │  │
│  │  Knowledge:                                  │  │
│  │    ☑ Company policies                        │  │
│  │    ☐ Prior agreements (RAG)                  │  │
│  │                                              │  │
│  │  Tools:                                      │  │
│  │    ☐ Entity extractor                        │  │
│  │    ☐ Clause analyzer                         │  │
│  └──────────────────────────────────────────────┘  │
│                                                     │
│  ┌──────────────────────────────────────────────┐  │
│  │ 3. Output Options                            │  │
│  │    ● Working Document (default)              │  │
│  │    ○ Email draft                             │  │
│  │    ○ Teams message                           │  │
│  │    ○ Workflow trigger                        │  │
│  └──────────────────────────────────────────────┘  │
│                                                     │
│  Or use a Playbook:                                │
│  [Select Playbook ▼]                               │
│                                                     │
│                     [Cancel]  [Start Analysis]     │
└─────────────────────────────────────────────────────┘
```

**Implementation:** 
- Power Apps Canvas component embedded in Custom Page
- Calls `/api/ai/analysis/execute` on submit
- Redirects to Analysis Workspace on success

### 5.3 Analysis Workspace (Custom Page)

**Purpose:** Interactive workspace for viewing, editing, and refining analysis output.

**UI Structure:**
```
┌──────────────────────────────────────────────────────────────────────┐
│  Analysis: Agreement Summary - contract-2025.pdf            [Save ▼] │
├───────────────────────────────┬──────────────────────────────────────┤
│  Working Document             │  Source Document                    │
│  (Editable)                   │  (Read-only Preview)                │
│ ┌───────────────────────────┐ │ ┌────────────────────────────────┐ │
│ │ ## Executive Summary      │ │ │                                │ │
│ │                           │ │ │   [PDF/DOCX Preview]           │ │
│ │ This Service Agreement    │ │ │                                │ │
│ │ between ABC Corp and...   │ │ │   (via SpeFileViewer PCF)      │ │
│ │                           │ │ │                                │ │
│ │ ## Key Terms              │ │ │                                │ │
│ │ - Term: 12 months         │ │ │                                │ │
│ │ - Fees: $50,000           │ │ │                                │ │
│ │                           │ │ │                                │ │
│ │ ## Risk Assessment        │ │ │                                │ │
│ │ [User can edit here]      │ │ │                                │ │
│ │                           │ │ │                                │ │
│ └───────────────────────────┘ │ └────────────────────────────────┘ │
├───────────────────────────────┴──────────────────────────────────────┤
│  AI Assistant                                                        │
│ ┌────────────────────────────────────────────────────────────────┐  │
│ │ You: Make this more concise and focus on liability              │  │
│ │                                                                  │  │
│ │ AI: I'll revise the summary to be more concise...              │  │
│ │     [Streaming response appears in real-time]                  │  │
│ │                                                                  │  │
│ │ [Type your message...                              ] [Send →]   │  │
│ └────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────┘
```

**Key Features:**
1. **Two-Column Layout**
   - Left: Monaco editor (or rich text) for working document
   - Right: SpeFileViewer PCF showing source document preview
   - Resizable split pane

2. **AI Chat Panel**
   - Conversational refinement interface
   - SSE streaming for real-time responses
   - Working document updates in left pane as AI responds

3. **Save Options**
   - Save as new Document (DOCX/PDF/MD)
   - Email draft
   - Teams message
   - Export to workflow

**Implementation:**
- Power Apps Custom Page
- Reuse existing `SpeFileViewer` PCF for source preview
- New `AnalysisWorkspace` PCF (React) for working document + chat
- Calls `/api/ai/analysis/{id}/continue` for chat
- Calls `/api/ai/analysis/{id}/save` for export

---

## 6. Prompt Engineering

### 6.1 System Prompt Construction

The system prompt combines Action + Skills:

```
{Action.SystemPrompt}

## Instructions

{foreach Skill in Skills}
- {Skill.PromptFragment}
{/foreach}

## Output Format

Provide your analysis in Markdown format with appropriate headings and structure.
```

**Example Result:**
```
You are an AI assistant helping legal professionals analyze documents.

## Instructions

- Write in a concise, professional tone suitable for executive review
- Use legal terminology when appropriate but explain complex terms
- Structure your analysis with clear headings and bullet points
- Focus on identifying key obligations, deadlines, and risk factors

## Output Format

Provide your analysis in Markdown format with appropriate headings and structure.
```

### 6.2 User Prompt with Knowledge Grounding

```
# Document to Analyze

{DocumentText}

{if Knowledge contains RAG indexes}
# Related Context

I've reviewed similar documents and found these relevant points:
{RAG search results}
{/if}

{if Knowledge contains inline rules/templates}
# Reference Materials

{foreach Knowledge item}
## {Knowledge.Name}
{Knowledge.Content}
{/foreach}
{/if}

---

Please analyze the document above according to the instructions.
```

### 6.3 Continuation Prompt

```
# Current Analysis

{WorkingDocument}

# Conversation History

{foreach message in ChatHistory}
{message.Role}: {message.Content}
{/foreach}

# New Request

User: {UserMessage}

Please update the analysis based on this feedback. Provide the complete updated analysis, not just the changes.
```

---

## 7. Implementation Phases

> **Implementation Standards:** All phases follow procedures defined in:
> - `docs/reference/procedures/INDEX.md` - Software development procedures
> - `docs/reference/procedures/04-ai-execution-protocol.md` - AI task execution
> - `docs/reference/procedures/05-poml-reference.md` - POML task definitions
> - `docs/reference/procedures/06-context-engineering.md` - Context management

**Deliverables per Phase:**
- ✅ `PLAN.md` - Detailed implementation plan with context engineering
- ✅ `tasks/TASK-INDEX.md` - Complete task breakdown
- ✅ `tasks/*.poml` - Individual task files with full context
- ✅ Dataverse solution files (.zip) - Deployed and tested
- ✅ Bicep infrastructure templates - Deployed to Azure
- ✅ BFF API code - Built, tested, deployed to App Service
- ✅ PCF components - Built, packaged, deployed to Dataverse
- ✅ End-to-end deployment verification

### Phase 1: Core Infrastructure (Week 1-2)

**Goal:** Establish data model, Azure resources, and basic API endpoints

**Tasks:**
1. **Multi-Tenant Parameterization (PRIORITY)**
   - Create all Environment Variables (see Section 3.2)
   - Create Bicep parameter template (`infrastructure/bicep/customer-deployment.bicepparam`)
   - Create token-replacement `appsettings.json` template
   - Document all configurable parameters
   - Create deployment guide for customers
   
2. **Infrastructure Deployment**
   - Create parameterized Bicep templates (no hard-coded values)
   - Create Azure AI Foundry hub and project (parameterized)
   - Deploy Prompt Flows (analysis-execute, analysis-continue)
   - Configure hybrid RAG indexes (shared Spaarke index)
   - Update Key Vault references in Bicep
   - Test deployment to separate test tenant
   
3. **Dataverse Schema**
   - Create entities: Analysis, Action, Skill, Knowledge, Tool, KnowledgeDeployment, WorkingVersion, EmailMetadata
   - Create all Environment Variables in solution
   - Configure relationships and lookups
   - Add security roles and field-level security
   - Create default data (sample Actions, Skills)
   - Export Dataverse solution (unmanaged) and commit
   - Validate solution import to clean environment
   
4. **BFF API Implementation**
   - **Parameterization:** Remove all hard-coded endpoints/tenant IDs
   - **Configuration:** Implement token replacement for appsettings.json
   - **Validation:** Add startup configuration validation
   - Implement `AnalysisEndpoints.cs` with `/execute`, `/continue`, `/save`, `/export`
   - Implement `AnalysisOrchestrationService` (single-doc flow)
   - Implement `ScopeResolverService` with Redis caching
   - Implement `AnalysisContextBuilder` with Prompt Flow integration
   - Implement `WorkingDocumentVersionService` with SPE storage
5. **Deployment & Verification**
   - Build and publish BFF API to Azure App Service (Spaarke tenant)
   - Test deployment to customer-simulated tenant (separate subscription)
   - Verify all environment variables resolve correctly
   - Run integration tests against dev environment
   - Verify SSE streaming works end-to-end
   - Load test with 50 concurrent users

**Acceptance Criteria:**
- ✅ Azure AI Foundry hub provisioned via parameterized Bicep
- ✅ All Environment Variables created in Dataverse solution
- ✅ Zero hard-coded values in PCF controls or BFF API
- ✅ Bicep deployment succeeds in separate test subscription
- ✅ All Dataverse entities created and solution imports cleanly
- ✅ API endpoints return correct status codes (200/202/404/400/429)
- ✅ Analysis records created in Dataverse with proper relationships
- ✅ SSE streaming works for `/execute` with real AI responses
- ✅ Working document versions save to SPE successfully
- ✅ Unit test coverage > 80%
- ✅ Integration tests pass in dev environment
- ✅ **Customer deployment guide tested by non-developer**eal AI responses
- ✅ Working document versions save to SPE successfully
- ✅ Unit test coverage > 80%
- ✅ Integration tests pass in dev environment

### Phase 2: UI Components (Week 3-4)

**Goal:** Build and deploy user-facing UI for Analysis creation and workspace

**Tasks:**
1. **Form Customizations**
   - Extend Document form with Analysis tab + grid
   - Add "+ New Analysis" command button
   - Configure grid columns and views
   - Add form scripts for navigation
   - Export form customizations to solution
3. **Analysis Workspace**
   - Build Analysis Workspace custom page
   - Build `AnalysisWorkspace` PCF component (React, TypeScript)
   - **PCF Parameterization:** Use `getGlobalContext().environmentVariables` for API URL
   - **No hard-coded endpoints** - validate at dev-time with fallback
   - Integrate Monaco editor for working document
   - Reuse `SpeFileViewer` PCF for source preview
   - Implement resizable split pane
   - Implement SSE streaming for chat
   - Add auto-save for working document versions
4. **PCF Packaging & Deployment**
   - Build PCF solution (`pac pcf push`)
   - **Validate:** No hard-coded API URLs in PCF bundle
   - Create solution for UI components (unmanaged for now)
   - Deploy to dev environment
   - Test in MDA with environment variables configured
   - Export solution and commit to repo
   - Test import to clean environment (simulates customer)

**Acceptance Criteria:**
- ✅ Users can create Analysis from Document form
- ✅ Analysis Builder shows all Actions, Skills, Knowledge with descriptions
- ✅ Playbook selector pre-populates scopes correctly
- ✅ Analysis Workspace displays two-column layout with resizable panes
- ✅ Chat interface streams AI responses in real-time
- ✅ Working document auto-saves after each chat interaction
- ✅ Source document preview loads correctly (Office, PDF, images)
- ✅ Error messages are user-friendly and actionable
- ✅ PCF solution deployed to dev environment successfully
- ✅ **PCF reads API URL from environment variable, not hard-coded**
- ✅ **Solution imports cleanly to test environment**

**Acceptance Criteria:**
- ✅ Users can create Analysis from Document form
- ✅ Analysis Builder shows all Actions, Skills, Knowledge with descriptions
- ✅ Playbook selector pre-populates scopes correctly
- ✅ Analysis Workspace displays two-column layout with resizable panes
- ✅ Chat interface streams AI responses in real-time
- ✅ Working document auto-saves after each chat interaction
- ✅ Source document preview loads correctly (Office, PDF, images)
- ✅ Error messages are user-friendly and actionable
- ✅ PCF solution deployed to dev environment successfully

### Phase 3: Scope System & Hybrid RAG (Week 5-6)

**Goal:** Implement Skills, Knowledge, Tools configuration with hybrid RAG deployment

**Tasks:**
1. **Admin UI**
   - Build model-driven forms for Action, Skill, Knowledge, Tool entities
   - Create admin views with filtering and search
   - Add validation rules (e.g., prompt fragment max length)
   - Build KnowledgeDeployment configuration forms
   - Add ribbon buttons for "Test Prompt" and "Preview Knowledge"
   
2. **Hybrid RAG Infrastructure**
   - Deploy shared Spaarke AI Search index (Model 1)
   - Implement `IKnowledgeDeploymentService` for multi-model support
   - Add cross-tenant authentication for customer-owned indexes (Model 3)
   - Implement index provisioning automation (Model 2)
   - Configure Azure AI Foundry indexing pipeline
   
3. **Knowledge RAG Integration**
   - Implement `IRagService` with hybrid deployment support
   - Add embedding generation (text-embedding-3-small)
   - Implement hybrid search (vector + keyword)
   - Add semantic ranking configuration
   - Implement tenant filtering for shared index
   - Add caching layer for frequently-accessed knowledge
   
4. **Tool Handler Framework**
   - Define `IAnalysisToolHandler` interface
   - Implement dynamic tool loading (reflection-based)
   - Build sample tools: EntityExtractor, ClauseAnalyzer, DocumentClassifier
   - Add tool configuration validation
   - Implement tool error handling and fallbacks
   
5. **Seed Data & Testing**
   - Create 5 default Actions (Summarize, Review, Extract, Compare, Respond)
   - Create 10 default Skills (Concise, Legal, Executive, Technical, etc.)
   - Create sample Knowledge entries (templates, rules)
   - Configure default KnowledgeDeployment (shared model)
   - Test prompt construction with all scope combinations
   - Performance test RAG retrieval (<500ms P95)

**Acceptance Criteria:**
- ✅ Admins can create/edit Actions, Skills, Knowledge, Tools via MDA forms
- ✅ All three RAG deployment models (Shared, Dedicated, CustomerOwned) functional
- ✅ RAG Knowledge sources retrieve relevant context within 500ms
- ✅ Prompt templates correctly combine Action + Skills + Knowledge
- ✅ Tools execute successfully with proper error handling
- ✅ Seed data imported and tested
- ✅ Hybrid RAG switching works without code changes

### Phase 4: Playbooks & Export (Week 7-8)

**Goal:** Reusable configurations and multi-format output with full deployment

**Tasks:**
1. **Playbook System**
   - Implement Playbook entity and N:N associations
   - Build Playbook management UI (create, edit, clone)
   - Add "Save as Playbook" button in Analysis Builder
   - Implement Playbook sharing (private vs. public)
   - Add Playbook preview before use
   - Create 5 default Playbooks (Quick Summary, Legal Review, etc.)
   
2. **Export Infrastructure**
   - Implement `IDocumentExportService` with format converters
   - Add Markdown-to-DOCX converter (using OpenXML SDK)
   - Add Markdown-to-PDF converter (using Azure Functions PDF service)
   - Implement file naming conventions and metadata
   - Add export history tracking
   
3. **Email Integration (Power Apps)**
   - Create `sprk_analysisemailmetadata` entity
   - Implement `IEmailActivityService` for email record creation
   - Build email composition pre-fill logic
   - Add attachment handling (analysis file + source link)
   - Integrate with server-side sync
   - Create email template for analysis results
   - Add "Open in Email" redirect to MDA
   
4. **Teams Integration**
   - Implement `ITeamsMessageService` using Graph API
   - Build channel selector UI
   - Add adaptive card formatting for analysis summary
   - Implement deep link to Analysis Workspace
   - Add @mention support for stakeholders
   - Test with Teams bot registration
   
5. **Workflow Triggers**
   - Design workflow trigger payload schema
   - Implement `IWorkflowTriggerService`
   - Create sample Power Automate flows
   - Add trigger configuration in Playbooks
   - Document custom trigger development guide
   
6. **Deployment & Documentation**
   - Deploy all export services to Azure
   - Update Dataverse solution with email entity
   - Register Teams bot (if needed)
   - Create user documentation for exports
   - Create admin guide for Playbooks

**Acceptance Criteria:**
- ✅ Users can save Playbooks and load them in Analysis Builder
- ✅ Playbooks correctly restore Action + Scopes configuration
- ✅ Export to DOCX creates valid, formatted documents
- ✅ Export to PDF generates print-ready files with branding
- ✅ Email export creates email activity with pre-filled content
- ✅ User can edit email before sending from MDA
- ✅ Teams export posts formatted message with link to workspace
- ✅ Workflow triggers fire Power Automate flows successfully
- ✅ All export options tested end-to-end in dev environment

### Phase 5: Production Readiness & Evaluation (Week 9-10)

**Goal:** Production deployment with monitoring, optimization, and continuous improvement

**Tasks:**
1. **Performance Optimization**
   - Implement Redis caching for Scopes and RAG results
   - Add prompt compression for large documents
   - Optimize token usage (remove redundant instructions)
   - Implement connection pooling for Dataverse/Graph
   - Add CDN for PCF static assets
   - Run load testing (100+ concurrent users)
   - Profile and optimize hot paths
   
2. **Azure AI Foundry Evaluation**
   - Configure evaluation pipeline in Foundry
   - Implement evaluation metrics collection
   - Set up nightly batch evaluation runs
   - Create evaluation dashboard in Foundry portal
   - Configure alerts for quality degradation
   - Document evaluation results and trends
   - Tune prompts based on evaluation data
   
3. **Telemetry & Monitoring**
   - Add Application Insights custom events
   - Implement distributed tracing (W3C Trace Context)
   - Add cost tracking per customer
   - Create dashboards: usage, performance, errors, costs
   - Configure alerts: error rate, latency, token budget
   - Add user journey tracking
   - Integrate with Azure AI Foundry monitoring
   
4. **Error Handling & Resilience**
   - Add comprehensive error messages
   - Implement circuit breaker for AI services
   - Add graceful degradation (fallback prompts)
   - Improve rate limit handling
   - Add retry logic with exponential backoff
   - Test failure scenarios
   
5. **Security & Compliance**
   - Complete security review (penetration test)
   - Verify data isolation (multi-tenant)
   - Audit authorization checks
   - Review PII handling (Content Safety filters)
   - Document compliance controls
   - Test cross-tenant RAG access
   - Verify Key Vault secret rotation
   
6. **Production Deployment**
   - Deploy infrastructure to production Azure subscription
   - Deploy Dataverse solution to production org
   - Deploy BFF API to production App Service
   - Configure production Key Vault and secrets
   - Run smoke tests in production
   - Enable monitoring and alerts
   - Create runbook for incident response
   
7. **Documentation & Training**
   - User guide: Creating analyses, using workspace, exporting
   - Admin guide: Managing Actions/Skills/Knowledge, Playbooks, RAG deployments
   - **Customer Deployment Guide** (NEW - CRITICAL)
     - Azure infrastructure deployment (Bicep)
     - Power Platform solution import
     - Environment variable configuration
     - Post-deployment validation checklist
     - Network/security configuration
     - Troubleshooting guide
   - Developer guide: Adding new tools, extending prompt flows
   - Video tutorials (3-5 minutes each)
   - Release notes and changelog

**Acceptance Criteria:**
- ✅ System handles 100+ concurrent analyses without degradation
- ✅ P95 latency < 2s for SSE stream start
- ✅ All endpoints have comprehensive error handling with user-friendly messages
- ✅ Security review completed with no critical or high-severity issues
- ✅ Azure AI Foundry evaluation pipeline running nightly
- ✅ Evaluation dashboard shows quality metrics > targets
- ✅ Production deployment completed successfully
- ✅ Monitoring dashboards operational with alerts configured
- ✅ Documentation complete and published
- ✅ Training materials delivered to users and admins
- ✅ **Customer deployment guide validated by partner organization**
- ✅ **Solution deployed successfully to external test tenant**

---

## 8. Managed Solution Upgrade Strategy

### 8.1 Version Management

**Semantic Versioning for Solutions:**

```
Major.Minor.Patch.Build
  │     │     │     └─ Build number (auto-increment, CI/CD)
  │     │     └─ Bug fixes, no schema changes
  │     └─ New features, backward-compatible schema changes
  └─ Breaking changes, major schema refactoring
```

**Example Versions:**
- `1.0.0.0` - Initial release (Phase 1)
- `1.1.0.0` - Scope system and hybrid RAG (Phase 3)
- `1.2.0.0` - Playbooks and export (Phase 4)
- `2.0.0.0` - Multi-document support (Phase 2 of next phase)

### 8.2 Schema Change Categories

| Change Type | Example | Managed Solution Behavior | Requires Manual Action |
|-------------|---------|---------------------------|------------------------|
| **Additive** | New field, new entity | Automatically merged | No |
| **Modification** | Change field type, max length | Preserves data, updates schema | Sometimes (if data incompatible) |
| **Deletion** | Remove deprecated field | Field hidden, data preserved | Admin must delete manually |
| **Breaking** | Change relationship cardinality | Import blocked | Yes - requires data migration |

### 8.3 Upgrade Scenarios

#### **Scenario 1: Minor Update (1.0.0 → 1.1.0)**

**Changes:**
- Add new fields to existing entities
- Add new scopes (Skills, Knowledge)
- Update Prompt Flows in Azure AI Foundry
- Bug fixes in PCF controls

**Customer Impact:**
- ✅ Automatic upgrade via solution import
- ✅ No data loss
- ✅ Environment variables unchanged
- ⚠️ May need to update BFF API (config change only)

**Upgrade Steps:**
1. Customer downloads new solution package
2. Import solution to environment (managed upgrade)
3. Review release notes for new features
4. Optionally: Update BFF API via Bicep re-deployment
5. Test new features in sandbox first

#### **Scenario 2: Major Update (1.x → 2.0.0)**

**Changes:**
- Multi-document analysis (N:N relationship change)
- New RAG deployment models
- Breaking API changes
- Database migrations

**Customer Impact:**
- ⚠️ Requires testing and validation
- ⚠️ May require data migration scripts
- ⚠️ BFF API update mandatory
- ⚠️ Potential downtime during upgrade

**Upgrade Steps:**
1. Customer backs up environment (full export)
2. Review migration guide (provided by Spaarke)
3. Deploy new Azure infrastructure (Bicep 2.0)
4. Run data migration scripts (if any)
5. Import solution 2.0.0 (managed upgrade)
6. Update environment variables (if new ones added)
7. Test thoroughly before production rollout
8. Rollback plan: Restore from backup if issues

### 8.4 Rollback Strategy

**Managed Solutions: Limited Rollback**
- ❌ Cannot downgrade managed solution versions
- ✅ Can deploy side-by-side (solution A + solution B)
- ✅ Can uninstall and reinstall from backup

**Recommended Approach:**
1. **Always test in sandbox environment first**
2. **Take full environment backup before major upgrades**
3. **Use solution staging**: Import to test → validate → import to prod
4. **Monitor for 24 hours** post-upgrade before considering stable

### 8.5 Environment Variable Evolution

**Adding New Environment Variables:**

```xml
<!-- Solution 1.0.0 -->
<environmentvariable schemaname="sprk_BffApiBaseUrl">
  <defaultvalue>https://api.spaarke.com</defaultvalue>
</environmentvariable>

<!-- Solution 1.1.0 - New variable added -->
<environmentvariable schemaname="sprk_PromptFlowEndpoint">
  <defaultvalue></defaultvalue>
  <!-- Empty default - customer must configure -->
</environmentvariable>
```

**Impact:** Customer must configure new environment variable post-upgrade.

**Mitigation:**
- Release notes highlight new required variables
- BFF API logs warnings if new variables missing
- Graceful degradation (features disabled if not configured)

### 8.6 Deprecation Policy

**Field/Feature Deprecation Timeline:**

| Phase | Version | Actions |
|-------|---------|---------|
| **Announce** | N.x | Add deprecation warning in release notes |
| **Warn** | N.x+1 | System logs warnings when deprecated feature used |
| **Disable** | N.x+2 | Feature disabled by default (can re-enable with flag) |
| **Remove** | N+1.0 | Completely removed in next major version |

**Example:** Deprecating old RAG model field:

```
Version 1.0: Field `sprk_ragindexname` exists and works
Version 1.1: Field marked deprecated, new `sprk_deploymentid` recommended
Version 1.2: System logs warning if old field used
Version 2.0: Old field removed, data migrated to new model
```

---

## 9. Non-Functional Requirements

### 8.1 Performance

| Metric | Target | Measurement |
|--------|--------|-------------|
| SSE stream start latency | < 2 seconds | 95th percentile |
| Token throughput | > 50 tokens/second | Average |
| Concurrent analyses per user | 3 simultaneous | Hard limit |
| Working document save | < 500ms | 95th percentile |
| Analysis history load | < 1 second | 95th percentile |

### 8.2 Scalability

| Dimension | Limit | Notes |
|-----------|-------|-------|
| Analyses per Document | Unlimited | Pagination in UI |
| Chat messages per Analysis | 1000 | Soft limit, oldest pruned |
| Working document size | 100KB | Markdown text |
| Knowledge sources per Analysis | 10 | Hard limit (context window) |
| Skills per Analysis | 5 | Hard limit (prompt clarity) |

### 8.3 Security

| Requirement | Implementation |
|-------------|----------------|
| Authentication | Entra ID JWT tokens (existing) |
| Authorization | `AnalysisAuthorizationFilter` checks Document access |
| Data isolation | Multi-tenant via Dataverse security roles |
| Token protection | Azure Key Vault for API keys |
| Audit logging | All Analysis operations logged to App Insights |
| PII handling | No PII in telemetry; content stays in Dataverse/SPE |

### 8.4 Monitoring

**Key Metrics:**
- Analysis execution success rate (target: > 95%)
- Average token usage per analysis
- Rate limit hit rate (target: < 5% of requests)
- OpenAI API error rate (target: < 1%)
- Working document save failures (target: < 0.1%)

**Alerts:**
- Circuit breaker open for OpenAI API
- Analysis failure rate > 10% over 5 minutes
- Rate limit rejections > 20% over 1 minute
- Average response time > 5 seconds

---

## 9. Risk Assessment

### 9.1 Technical Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| **Context Window Limits** | High | Implement Knowledge pruning, summarize long chat histories |
| **Token Costs** | Medium | Monitor usage, implement budget alerts, cache common prompts |
| **RAG Performance** | Medium | Pre-index documents, use hybrid search, cache embeddings |
| **UI Complexity** | Medium | Phased rollout, extensive user testing, fallback to simple mode |
| **Tool Handler Errors** | Low | Graceful degradation, try-catch all tool calls, log failures |

### 9.2 User Experience Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| **Prompt Confusion** | Medium | Provide clear examples, in-app tooltips, sample Playbooks |
| **Output Quality** | High | Test extensively with real documents, tune prompts iteratively |
| **Overwhelming Options** | Medium | Start with 3-5 pre-built Playbooks, hide advanced options initially |
| **Slow Responses** | Medium | Set expectations (progress indicators), optimize backend |

### 9.3 Business Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| **Adoption Challenges** | High | Change management, training, clear value proposition |
| **Cost Overruns** | Medium | Implement cost tracking, per-customer budgets, alerts |
| **Compliance Concerns** | High | Legal review of AI usage, data residency compliance, audit trail |

---

## 10. Testing Strategy

### 10.1 Unit Tests

**Services:**
- `AnalysisOrchestrationService` - Mock all dependencies
- `ScopeResolverService` - Test scope loading and caching
- `AnalysisContextBuilder` - Verify prompt construction
- `WorkingDocumentService` - Test concurrency handling

**Target:** 80% code coverage

### 10.2 Integration Tests

**API Endpoints:**
- `/execute` - End-to-end with test document and scopes
- `/continue` - Chat continuation with history
- `/save` - File creation in SPE test container
- Authorization filters - Access control scenarios

**Target:** All happy path + error scenarios covered

### 10.3 E2E Tests

**User Scenarios:**
1. Create Analysis from Document form
2. Execute Analysis with default settings
3. Refine Analysis via chat
4. Save output as new Document
5. Export Analysis to Email
6. Use Playbook for quick Analysis

**Target:** All critical user journeys automated

### 10.4 Performance Tests

**Load Tests:**
- 50 concurrent analysis executions
- 100 chat messages over 1 minute
- 500 analysis history loads

**Stress Tests:**
- Large documents (10MB PDF)
- Long chat histories (100+ messages)
- Complex scopes (5 Skills + 10 Knowledge sources)

---

## 11. Open Questions

1. **Knowledge RAG Integration:** ~~Do we provision a new Azure AI Search index per customer, or use a shared index with tenant filters?~~
   - **✅ RESOLVED:** Hybrid approach with 3 deployment models (Shared, Dedicated, CustomerOwned) - see Section 2.4

2. **Working Document Versioning:** ~~Should we keep versions of working document as user refines?~~
   - **✅ RESOLVED:** Yes, session-based versioning with SPE storage - see `sprk_analysisworkingversion` entity
   - **Storage Strategy:** Files in SPE container `/analyses/{analysisId}/working/session-{sessionId}-v{n}.md`
   - **Why SPE:** Leverages existing file infrastructure, enables Office preview, consistent with document storage model
   - **Retention:** Keep all versions during active sessions; archive last 10 on completion; purge after 90 days
   - **Benefits:** Version history, rollback capability, audit trail, disaster recovery

3. **Tool Handler Security:** How do we sandbox custom Tool handlers to prevent malicious code?
   - **Recommendation:** Phase 1 uses pre-built tools only; Phase 3 explores sandboxing

4. **Email Integration:** ~~Power Apps email dialog or direct Graph API send?~~
   - **✅ RESOLVED:** Power Apps email entity with server-side sync - see Section 3.2 export endpoint
   - **Rationale:** Better MDA integration, email templates, compliance, audit trail
   - **Trade-off:** Extra user step (review/send) vs. immediate send, but aligns with user expectations for email review

5. **Multi-Document Analyses:** ~~Should Phase 1 support analyzing multiple documents together?~~
   - **✅ RESOLVED:** Phase 1 focuses on single document, BUT all components designed for multi-doc readiness
   - **API Design:** Accepts `documentIds` array (validates length=1 in Phase 1)
   - **Data Model:** Analysis entity structure supports N:N to documents (implemented in Phase 2)
   - **Service Layer:** `ITextExtractor` batch methods, prompt concatenation logic prepared
   - **Minimal Refactoring:** Phase 2 removes validation check and adds relationship; core logic unchanged

---

## 12. Success Criteria

### 11.1 Technical Success

- [ ] All API endpoints operational with < 2s P95 latency
- [ ] SSE streaming works reliably across browsers
- [ ] Analysis records persist correctly in Dataverse
- [ ] File export works for DOCX, PDF, Email
- [ ] Rate limiting prevents abuse
- [ ] Authorization prevents unauthorized access
- [ ] **Zero hard-coded tenant-specific values**
- [ ] **Environment variables drive all configuration**
- [ ] **Solution imports cleanly to external tenant**
- [ ] **Bicep deployment succeeds in customer subscription**

### 11.2 User Success

- [ ] Users can create Analysis in < 5 clicks
- [ ] Analysis Workspace loads in < 3 seconds
- [ ] Chat refinement produces improved outputs
- [ ] Playbooks reduce configuration time by 80%
- [ ] Export options meet 90% of use cases

### 11.3 Business Success

- [ ] 50% of documents have at least one Analysis within 30 days
- [ ] 80% of Analyses reach "Completed" status
- [ ] Token costs stay within $0.10/document budget
- [ ] User satisfaction score > 4/5
- [ ] No critical security incidents

### 11.4 Multi-Tenant Readiness Success (NEW)

- [ ] **Solution deploys to 3 different test tenants without code changes**
- [ ] **Customer deployment guide tested by 2 non-Spaarke users**
- [ ] **All configuration via Environment Variables (no code edits required)**
- [ ] **Bicep templates deploy to 2 different Azure subscriptions successfully**
- [ ] **Solution upgrade from 1.0 → 1.1 tested in managed mode**
- [ ] **Documentation reviewed and approved by Solutions Architect**

---

## 12. Appendices

### Appendix A: Glossary

| Term | Definition |
|------|------------|
| **Analysis** | A single AI-executed action on a Document |
| **Action** | Defines what the AI should do (e.g., Summarize) |
| **Skill** | Defines how the AI should work (e.g., tone, style) |
| **Knowledge** | Grounding sources for RAG (rules, templates, prior work) |
| **Tool** | Function-style helper for extraction/analysis |
| **Playbook** | Reusable configuration of Action + Scopes + Output |
| **Working Document** | Editable in-progress analysis output |
| **Final Output** | Completed analysis result |
| **Scope** | Collective term for Skills, Knowledge, and Tools |

### Appendix B: Reference Architecture Documents

| Document | Purpose |
|----------|---------|
| [SPAARKE-AI-ARCHITECTURE.md](../../docs/ai-knowledge/guides/SPAARKE-AI-ARCHITECTURE.md) | Overall AI architecture principles |
| [sdap-bff-api-patterns.md](../../docs/ai-knowledge/architecture/sdap-bff-api-patterns.md) | BFF API patterns and conventions |
| [sdap-component-interactions.md](../../docs/ai-knowledge/architecture/sdap-component-interactions.md) | Component interaction patterns |
| [auth-AI-azure-resources.md](../../docs/ai-knowledge/architecture/auth-AI-azure-resources.md) | Azure AI service configuration |
| [ai-document-summary.md](../../docs/guides/ai-document-summary.md) | Document Summary API reference |
| [ai-troubleshooting.md](../../docs/guides/ai-troubleshooting.md) | AI feature troubleshooting guide |
| [power-apps-custom-pages.md](../../docs/ai-knowledge/reference/power-apps-custom-pages.md) | **Custom Pages patterns and best practices** |
| [pcf-component-patterns.md](../../docs/ai-knowledge/reference/pcf-component-patterns.md) | **PCF control development patterns** |

### Appendix C: Related ADRs

| ADR | Title | Relevance |
|-----|-------|-----------|
| ADR-001 | Minimal APIs + BackgroundService | Endpoint implementation pattern |
| ADR-003 | Lean Authorization Seams | Authorization filter design |
| ADR-007 | SpeFileStore Facade | File access abstraction |
| ADR-008 | Endpoint Filters | Per-resource authorization |
| ADR-009 | Redis-First Caching | Scope caching strategy |
| ADR-013 | AI Architecture | AI feature architecture principles |

---

**Document Status:** Draft for Review  
**Next Steps:** Review with engineering team, validate feasibility, refine estimates  
**Target Review Date:** December 13, 2025
