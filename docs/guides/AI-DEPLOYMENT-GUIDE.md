# AI Document Intelligence - Deployment Guide

> **Version**: 2.3
> **Created**: 2025-12-28
> **Updated**: 2026-01-16
> **Projects**: AI Document Intelligence R1 + R2 + R3 + Email-to-Document R2 + RAG Pipeline R1

---

## Table of Contents

1. [Overview](#overview)
2. [Prerequisites](#prerequisites)
3. [Phase 1: Azure Infrastructure (R1)](#phase-1-azure-infrastructure-r1)
4. [Phase 2: Dataverse Solution (R1)](#phase-2-dataverse-solution-r1)
5. [Phase 3: PCF Controls (R2)](#phase-3-pcf-controls-r2)
6. [Phase 4: Custom Pages (R2)](#phase-4-custom-pages-r2)
7. [Phase 5: Form Integration (R2)](#phase-5-form-integration-r2)
8. [Phase 6: RAG Infrastructure (R3)](#phase-6-rag-infrastructure-r3)
9. [Phase 7: Email-to-Document Automation](#phase-7-email-to-document-automation)
10. [Phase 8: RAG Document Indexing Pipeline](#phase-8-rag-document-indexing-pipeline)
11. [Azure Services Reference](#azure-services-reference)
12. [Complete App Service Configuration](#complete-app-service-configuration)
13. [Verification Procedures](#verification-procedures)
14. [Known Issues](#known-issues)
15. [Troubleshooting](#troubleshooting)
16. [Reference](#reference)

---

## Overview

This guide covers the complete deployment of AI Document Intelligence for the Spaarke platform, including:

**R1 Scope (Infrastructure + Backend)**:
- Azure AI Foundry Hub and Project
- Azure OpenAI with GPT-4o-mini
- Azure AI Search
- Azure Document Intelligence
- Dataverse entities and security roles
- BFF API endpoints

**R2 Scope (Analysis UI)**:
- AnalysisBuilder PCF Control
- AnalysisWorkspace PCF Control
- Custom Pages for hosting PCF controls
- Document form integration (Analysis tab, subgrid, ribbon button)

**R3 Scope (RAG Infrastructure)** *(Phase 1 Complete)*:
- RAG knowledge index (`spaarke-knowledge-index-v2`) with hybrid search (3072-dim vectors)
- Multi-tenant deployment models (Shared, Dedicated, CustomerOwned)
- `IKnowledgeDeploymentService` for SearchClient routing
- `IRagService` for hybrid search with semantic ranking
- Redis caching for embeddings

**Email-to-Document R2 Scope**:
- Automated email archival to SharePoint Embedded
- Email attachment processing
- Auto-enqueue for AI analysis
- Optional RAG indexing for emails and attachments

**RAG Pipeline R1 Scope**:
- `IFileIndexingService` for unified file indexing
- Text extraction, chunking, embedding, indexing pipeline
- `RagIndexingJobHandler` for background processing
- Idempotency with Redis-based locks

### Architecture Components

| Component | Purpose | Version/Status |
|-----------|---------|----------------|
| BFF API | Backend-for-Frontend serving PCF controls | Deployed |
| Azure OpenAI | LLM for summarization and analysis | gpt-4o-mini, text-embedding-3-large |
| AI Search | RAG knowledge retrieval | spaarke-search-dev |
| Document Intelligence | Document parsing | westus2 |
| AI Foundry | Prompt Flow orchestration | sprkspaarkedev-aif-hub |
| Dataverse | Entity storage for Analysis records | sprk_analysis |
| AnalysisBuilder PCF | Analysis configuration dialog | v1.12.0 |
| AnalysisWorkspace PCF | Analysis workspace with chat | v1.0.29 |
| Custom Pages | Power Apps hosts for PCF controls | Deployed |
| KnowledgeDeploymentService | Multi-tenant RAG deployment routing | R3 (Active) |
| FileIndexingService | Unified RAG file indexing pipeline | R3+RAG-R1 (Active) |
| spaarke-knowledge-index-v2 | RAG vector index (3072 dims) | **Primary** |
| spaarke-records-index | Record matching index | Active |
| Service Bus | Async job processing | Active |
| Redis | Caching (embeddings, idempotency) | Active |

---

## Prerequisites

### Required Tools

| Tool | Version | Installation |
|------|---------|-------------|
| Azure CLI | 2.50+ | `winget install Microsoft.AzureCLI` |
| Bicep CLI | 0.39+ | `az bicep upgrade` |
| Power Platform CLI | Latest | `pac install latest` |
| .NET SDK | 8.0+ | `winget install Microsoft.DotNet.SDK.8` |
| Node.js | 18+ | `winget install OpenJS.NodeJS.LTS` |
| npm | 9+ | Included with Node.js |

### Required Access

| Resource | Access Level |
|----------|-------------|
| Azure Subscription | Contributor role |
| Power Platform Environment | System Administrator |
| Dataverse | Solution Publisher privileges |

### Verify Tool Installation

```bash
# Azure CLI
az --version

# Bicep
az bicep version

# PAC CLI
pac --version

# .NET
dotnet --version

# Node.js
node --version
npm --version
```

---

## Phase 1: Azure Infrastructure (R1)

### Step 1: Authenticate to Azure

```bash
# Login to Azure
az login

# Set subscription
az account set --subscription "Spaarke SPE Subscription 1"

# Verify
az account show
```

### Step 2: Create Resource Group (if needed)

```bash
# Create resource group
az group create \
  --name rg-spaarke-{customer}-{env} \
  --location eastus
```

### Step 3: Deploy AI Foundry Stack

The AI Foundry stack deploys:
- Storage Account
- Key Vault
- Log Analytics
- Application Insights
- AI Foundry Hub
- AI Foundry Project

```bash
# Navigate to infrastructure directory
cd infrastructure/bicep

# Deploy AI Foundry stack
az deployment group create \
  --resource-group rg-spaarke-{customer}-{env} \
  --template-file stacks/ai-foundry-stack.bicep \
  --parameters customerId={customer} environment={env} location=eastus
```

### Step 4: Verify Deployment

```bash
# List deployed resources
az resource list \
  --resource-group rg-spaarke-{customer}-{env} \
  --output table
```

### Expected Resources

| Resource Type | Name Pattern | Purpose |
|---------------|-------------|---------|
| Storage Account | `sprk{customer}{env}aifsa` | AI Foundry storage |
| Key Vault | `sprk{customer}{env}-aif-kv` | Secrets |
| Log Analytics | `sprk{customer}{env}-aif-logs` | Monitoring |
| App Insights | `sprk{customer}{env}-aif-insights` | APM |
| ML Workspace (Hub) | `sprk{customer}{env}-aif-hub` | AI Foundry Hub |
| ML Workspace (Project) | `sprk{customer}{env}-aif-proj` | AI Foundry Project |

---

## Phase 2: Dataverse Solution (R1)

### Step 1: Authenticate to Power Platform

```bash
# List current auth profiles
pac auth list

# Create new profile (if needed)
pac auth create --url https://{org}.crm.dynamics.com

# Select profile
pac auth select --index {n}
```

### Step 2: Import Solution

Solution files are located at:
- `infrastructure/dataverse/solutions/Spaarke_DocumentIntelligence_unmanaged.zip`
- `infrastructure/dataverse/solutions/Spaarke_DocumentIntelligence_managed.zip`

```bash
# Import unmanaged solution (for development environments)
pac solution import \
  --path infrastructure/dataverse/solutions/Spaarke_DocumentIntelligence_unmanaged.zip

# OR import managed solution (for production environments)
pac solution import \
  --path infrastructure/dataverse/solutions/Spaarke_DocumentIntelligence_managed.zip \
  --activate-plugins
```

### Step 3: Verify Solution Import

```bash
# List solutions
pac solution list
```

### Included Components

| Component Type | Components |
|----------------|------------|
| Entities | 10 Analysis entities (sprk_analysis, sprk_analysisaction, etc.) |
| Security Roles | Spaarke AI Analysis User, Spaarke AI Analysis Admin |
| Environment Variables | 15 configuration variables |

---

## Phase 3: PCF Controls (R2)

### 3.1 AnalysisBuilder PCF Control

**Purpose**: Modal dialog for configuring and executing AI document analyses

**Source Location**: `src/client/pcf/AnalysisBuilder/`

**Deployed Version**: v1.12.0

#### Control Properties

| Property | Direction | Type | Description |
|----------|-----------|------|-------------|
| `documentId` | Input | Text (Required) | GUID of sprk_document record to analyze |
| `documentName` | Input | Text | Display name of document |
| `containerId` | Input | Text | SharePoint Embedded Container ID |
| `fileId` | Input | Text | SharePoint Embedded File ID |
| `apiBaseUrl` | Input | Text | BFF API base URL (from env var) |
| `selectedPlaybookId` | Output | Text | Selected playbook GUID |
| `selectedActionId` | Output | Text | Selected action GUID |
| `selectedSkillIds` | Output | Text | Comma-separated skill GUIDs |
| `selectedKnowledgeIds` | Output | Text | Comma-separated knowledge GUIDs |
| `selectedToolIds` | Output | Text | Comma-separated tool GUIDs |
| `createdAnalysisId` | Output | Text | Created sprk_analysis record GUID |
| `shouldClose` | Output | Boolean | Signals dialog should close |

#### Build and Deploy

```bash
# Navigate to control directory
cd src/client/pcf/AnalysisBuilder

# Install dependencies
npm install

# Build
npm run build

# Deploy to Dataverse
pac pcf push --publisher-prefix sprk
```

### 3.2 AnalysisWorkspace PCF Control

**Purpose**: AI Document Analysis Workspace with Rich Text Editor (Lexical) for WYSIWYG editing

**Source Location**: `src/client/pcf/AnalysisWorkspace/`

**Deployed Version**: v1.0.29

#### Control Properties

| Property | Direction | Type | Description |
|----------|-----------|------|-------------|
| `analysisId` | Bound | Text (Required) | GUID of sprk_analysis record |
| `documentId` | Input | Text | Parent sprk_document GUID |
| `containerId` | Input | Text | SharePoint Embedded Container ID |
| `fileId` | Input | Text | SharePoint Embedded File ID |
| `apiBaseUrl` | Input | Text | BFF API base URL (from env var) |
| `workingDocumentContent` | Output | Multiple | Working document content (Markdown) |
| `chatHistory` | Output | Multiple | Chat message history (JSON) |
| `analysisStatus` | Output | Text | Status: Draft, InProgress, Completed, Failed |

#### Build and Deploy

```bash
# Navigate to control directory
cd src/client/pcf/AnalysisWorkspace

# Install dependencies
npm install

# Build
npm run build

# Deploy to Dataverse
pac pcf push --publisher-prefix sprk
```

### 3.3 Environment Variable Configuration

Both PCF controls read the BFF API URL from the Dataverse environment variable `sprk_BffApiBaseUrl`.

**Resolution Hierarchy**:
1. Input parameter (from Custom Page)
2. Dataverse environment variable
3. Hardcoded default: `https://spe-api-dev-67e2xz.azurewebsites.net/api`

**Environment Variable Setup**:

| Variable | Schema Name | Default Value |
|----------|-------------|---------------|
| BFF API Base URL | `sprk_BffApiBaseUrl` | `https://spe-api-dev-67e2xz.azurewebsites.net/api` |

---

## Phase 4: Custom Pages (R2)

### 4.1 Analysis Builder Custom Page

**Page ID**: `sprk_analysisbuilder_40af8`

**Purpose**: Hosts the AnalysisBuilder PCF control in a modal dialog

**Launch Method**: Called from Document form ribbon button

#### Required Page Properties

| Property | Description | Example |
|----------|-------------|---------|
| `documentId` | Document record GUID | `{sprk_documentid}` |
| `documentName` | Document name | `{sprk_name}` |
| `containerId` | SPE Container ID | `{sprk_containerid}` |
| `fileId` | SPE File ID | `{sprk_itemid}` |

#### Navigation JavaScript

```javascript
// Opens Analysis Builder from Document form
function openAnalysisBuilder(executionContext) {
    var formContext = executionContext.getFormContext();
    var documentId = formContext.data.entity.getId().replace(/[{}]/g, '');
    var documentName = formContext.getAttribute("sprk_name").getValue();
    var containerId = formContext.getAttribute("sprk_containerid").getValue();
    var fileId = formContext.getAttribute("sprk_itemid").getValue();

    Xrm.Navigation.navigateTo({
        pageType: "custom",
        name: "sprk_analysisbuilder_40af8",
        entityName: "sprk_document",
        recordId: documentId
    }, {
        target: 2, // Dialog
        position: 1, // Center
        width: { value: 80, unit: "%" },
        height: { value: 80, unit: "%" }
    });
}
```

### 4.2 Analysis Workspace Custom Page

**Page ID**: `sprk_analysisworkspace_52748`

**Purpose**: Hosts the AnalysisWorkspace PCF control for full analysis work

**Launch Method**: Navigated from Analysis Builder after execution, or from Analysis subgrid

#### Required Page Properties

| Property | Description | Example |
|----------|-------------|---------|
| `analysisId` | Analysis record GUID | `{sprk_analysisid}` |
| `documentId` | Parent document GUID | `{_sprk_document_value}` |
| `containerId` | SPE Container ID | From document record |
| `fileId` | SPE File ID | From document record |

---

## Phase 5: Form Integration (R2)

### 5.1 Analysis Tab on Document Form

**Entity**: `sprk_document`
**Form**: Main form
**Tab Name**: Analysis

The Analysis tab contains:
1. Analysis subgrid showing related `sprk_analysis` records
2. Quick access to create new analyses

### 5.2 Analysis Subgrid

**Location**: Analysis tab on Document form

**Configuration**:
| Setting | Value |
|---------|-------|
| Entity | `sprk_analysis` |
| Relationship | `sprk_document` lookup |
| Default View | Related Analyses |
| Columns | Name, Status, Created On |
| Add New | Enabled |
| Delete | Enabled |

### 5.3 New Analysis Ribbon Button

**Button Label**: `+ New Analysis`

**Button Location**: Document form command bar

**Action**: Opens Analysis Builder Custom Page as dialog

**Enable Rule**: Record must be saved (not new)

#### Ribbon Button Command

```javascript
// Command action for New Analysis button
function newAnalysisCommand(executionContext) {
    openAnalysisBuilder(executionContext);
}

// Enable rule - only for saved records
function isRecordSaved(executionContext) {
    var formContext = executionContext.getFormContext();
    var entityId = formContext.data.entity.getId();
    return entityId && entityId.length > 0;
}
```

### 5.4 Web Resource Deployment

**Web Resource Name**: `sprk_/scripts/analysis_navigation.js`

**Content**: JavaScript functions for Analysis Builder and Workspace navigation

**Deployment**:
```bash
# Export solution containing web resource
pac solution export \
  --path Spaarke_AI_updated.zip \
  --name Spaarke_AI

# Modify and reimport
pac solution import \
  --path Spaarke_AI_updated.zip \
  --publish-changes
```

---

## Phase 6: RAG Infrastructure (R3)

### Overview

Phase 6 adds multi-tenant RAG (Retrieval-Augmented Generation) infrastructure with hybrid search capabilities.

### 6.1 Deploy RAG Knowledge Index

The `spaarke-knowledge-index-v2` is the primary RAG index with 3072-dimensional vectors:

```bash
# Verify index exists
az search index list \
  --service-name spaarke-search-dev \
  --resource-group spe-infrastructure-westus2 \
  -o table

# Expected output should include: spaarke-knowledge-index-v2
```

**Index Definition**: [`infrastructure/ai-search/spaarke-knowledge-index-v2.json`](../../infrastructure/ai-search/spaarke-knowledge-index-v2.json)

#### Index Schema

| Field | Type | Purpose |
|-------|------|---------|
| `id` | Edm.String | Unique chunk ID |
| `tenantId` | Edm.String | Multi-tenant isolation |
| `content` | Edm.String | Chunk text content |
| `contentVector3072` | Collection(Edm.Single) | 3072-dim embedding (text-embedding-3-large) |
| `documentVector3072` | Collection(Edm.Single) | Document-level embedding |
| `speFileId` | Edm.String | SharePoint Embedded file ID |
| `fileName` | Edm.String | Original file name |
| `deploymentId` | Edm.String | Deployment model ID |
| `deploymentModel` | Edm.String | Shared/Dedicated/CustomerOwned |

### 6.2 RAG Services Registration

The following services are registered in `Program.cs` when AI Search is configured:

| Service | Registration | Purpose |
|---------|--------------|---------|
| `SearchIndexClient` | Singleton | Azure SDK client for index management |
| `IKnowledgeDeploymentService` | Singleton | Multi-tenant SearchClient routing |
| `IRagService` | Singleton | Hybrid search with semantic ranking |
| `IFileIndexingService` | Singleton | Unified RAG indexing pipeline |
| `IEmbeddingCache` | Singleton | Redis-based embedding cache |
| `IVisualizationService` | Singleton | Document similarity visualization |

**Required Configuration**: These services only register if `DocumentIntelligence:AiSearchEndpoint` and `DocumentIntelligence:AiSearchKey` are both configured. If missing, the console logs:
```
⚠ RAG services disabled (requires DocumentIntelligence:AiSearchEndpoint/Key)
```

### 6.3 Deployment Models

The RAG system supports 3 deployment models configured per tenant:

| Model | Index Location | Configuration |
|-------|---------------|---------------|
| **Shared** | `spaarke-knowledge-index-v2` | Default, `tenantId` filter for isolation |
| **Dedicated** | `{tenantId}-knowledge` | Per-customer index, requires index creation |
| **CustomerOwned** | Customer Azure AI Search | Requires Key Vault secret for API key |

**Default Shared Index**: Configure via `Analysis__SharedIndexName` in App Service. Default: `spaarke-knowledge-index-v2`.

### 6.4 Verify RAG Infrastructure

```bash
# 1. Check index is accessible
curl -X GET "https://spaarke-search-dev.search.windows.net/indexes/spaarke-knowledge-index-v2/docs/\$count?api-version=2024-07-01" \
  -H "api-key: <your-api-key>"

# 2. Verify service is registered (check API startup logs)
# Look for: "✓ RAG services enabled (hybrid search + embedding cache + visualization + file indexing)"

# 3. Test embeddings model deployment
az cognitiveservices account deployment list \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2 \
  -o table
# Should show: text-embedding-3-large (primary for 3072-dim vectors)
```

### 6.5 R3 Phase 1 Status (Complete)

| Task | Status | Description |
|------|--------|-------------|
| 001 | ✅ Complete | Verify R1/R2 Prerequisites |
| 002 | ✅ Complete | Create RAG Index Schema (`spaarke-knowledge-index`) |
| 003 | ✅ Complete | Implement `IKnowledgeDeploymentService` |
| 004 | ✅ Complete | Implement `IRagService` with Hybrid Search |
| 005 | ✅ Complete | Add Redis Caching for Embeddings (`IEmbeddingCache`) |
| 006 | ✅ Complete | Test Shared Deployment Model |
| 007 | ✅ Complete | Test Dedicated Deployment Model |
| 008 | ✅ Complete | Document RAG Implementation |

### 6.6 RAG Documentation

| Document | Purpose |
|----------|---------|
| [RAG-ARCHITECTURE.md](RAG-ARCHITECTURE.md) | Full architecture overview |
| [RAG-CONFIGURATION.md](RAG-CONFIGURATION.md) | Configuration reference |
| [RAG-TROUBLESHOOTING.md](RAG-TROUBLESHOOTING.md) | Troubleshooting guide |

### 6.7 Embedding Cache Configuration

The embedding cache uses Redis (ADR-009) to reduce Azure OpenAI API costs and latency.

| Setting | Default | Description |
|---------|---------|-------------|
| Cache Key Format | `sdap:embedding:{sha256-hash}` | SHA256 hash of content for deterministic keys |
| TTL | 7 days | Embeddings are deterministic for same model |
| Serialization | Binary (Buffer.BlockCopy) | Efficient float[] → byte[] conversion |

**Services:**
- `IEmbeddingCache` / `EmbeddingCache` - Redis-based caching
- Integrated into `RagService.SearchAsync` and `RagService.GetEmbeddingAsync`
- Uses existing `CacheMetrics` with `cacheType="embedding"` for OpenTelemetry

**Error Handling:** Cache failures are graceful - embedding generation continues without cache.

---

## Phase 7: Email-to-Document Automation

### 7.1 Overview

Email-to-Document automation converts Dataverse email records to documents stored in SharePoint Embedded.

### 7.2 Required App Service Settings

| Setting | Required | Description | Default |
|---------|----------|-------------|---------|
| `EmailProcessing__EnableWebhook` | Yes | Enable Dataverse webhook triggers | `true` |
| `EmailProcessing__WebhookSecret` | Yes | Shared secret for webhook validation | — |
| `EmailProcessing__DefaultContainerId` | Yes | SPE Container ID for email storage | — |
| `EmailProcessing__EnablePolling` | No | Enable backup polling for missed emails | `true` |
| `EmailProcessing__AutoIndexToRag` | No | Auto-queue emails for RAG indexing | `false` |

### 7.3 RAG Integration

When `EmailProcessing__AutoIndexToRag=true`:
- Archived emails (.eml files) are queued for RAG indexing
- Email attachments are separately indexed
- Both use the `RagIndexingJobHandler` via Service Bus

### 7.4 Verify Email Processing

```bash
# Check Service Bus queue for pending jobs
az servicebus queue show \
  --namespace-name spaarke-servicebus-dev \
  --name spaarke-jobs \
  --resource-group spe-infrastructure-westus2 \
  --query "countDetails"
```

---

## Phase 8: RAG Document Indexing Pipeline

### 8.1 Overview

The RAG Document Indexing Pipeline provides end-to-end file indexing:

```
File → Download → Extract Text → Chunk → Generate Embeddings → Index to AI Search
```

### 8.2 Pipeline Components

| Component | Purpose |
|-----------|---------|
| `IFileIndexingService` | Orchestrates the full pipeline |
| `ITextChunkingService` | Splits text with configurable overlap |
| `IRagService` | Generates embeddings and indexes chunks |
| `RagIndexingJobHandler` | Background job processing via Service Bus |
| `IIdempotencyService` | Prevents duplicate processing |

### 8.3 Entry Points

| Method | Auth Mode | Use Case |
|--------|-----------|----------|
| `IndexFileAsync` | OBO (user token) | User-initiated indexing |
| `IndexFileAppOnlyAsync` | App-only | Background jobs, automation |
| `IndexContentAsync` | N/A | Direct content indexing |

### 8.4 API Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/ai/rag/index-file` | POST | Index a file (OBO) |
| `/api/ai/rag/search` | POST | Hybrid search |
| `/api/ai/rag/index` | POST | Index document chunks |
| `/api/ai/rag/index/batch` | POST | Batch index chunks |

### 8.5 Required Configuration

**Critical**: RAG endpoints require both settings or services won't register:
- `DocumentIntelligence__AiSearchEndpoint`
- `DocumentIntelligence__AiSearchKey`

Without these, you'll see: `⚠ RAG services disabled (requires DocumentIntelligence:AiSearchEndpoint/Key)`

---

## Azure Services Reference

### Quick Reference - All Endpoints (Dev Environment)

| Service | Endpoint |
|---------|----------|
| BFF API | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| Azure OpenAI | `https://spaarke-openai-dev.openai.azure.com/` |
| Document Intelligence | `https://westus2.api.cognitive.microsoft.com/` |
| Azure AI Search | `https://spaarke-search-dev.search.windows.net/` |
| Redis Cache | (internal - managed by App Service) |
| Service Bus | `spaarke-servicebus-dev.servicebus.windows.net` |
| Key Vault | `https://spaarke-spekvcert.vault.azure.net/` |

### Azure OpenAI

| Property | Value |
|----------|-------|
| **Resource Name** | `spaarke-openai-dev` |
| **Resource Group** | `spe-infrastructure-westus2` |
| **Region** | West US 2 |
| **Endpoint** | `https://spaarke-openai-dev.openai.azure.com/` |

#### Model Deployments

| Deployment Name | Model | Purpose |
|-----------------|-------|---------|
| `gpt-4o-mini` | gpt-4o-mini | Document analysis, chat |
| `text-embedding-3-large` | text-embedding-3-large | **Primary** - RAG embeddings (3072 dims) |
| `text-embedding-3-small` | text-embedding-3-small | Deprecated (1536 dims) |

### Azure AI Search

| Property | Value |
|----------|-------|
| **Resource Name** | `spaarke-search-dev` |
| **Resource Group** | `spe-infrastructure-westus2` |
| **Endpoint** | `https://spaarke-search-dev.search.windows.net/` |

#### Indexes

| Index Name | Purpose | Status |
|------------|---------|--------|
| `spaarke-knowledge-index-v2` | RAG knowledge (3072-dim vectors) | **Primary** |
| `spaarke-records-index` | Record matching (Matters, Projects) | Active |
| `spaarke-knowledge-index` | Legacy RAG (1536-dim) | Deprecated |

### Azure Document Intelligence

| Property | Value |
|----------|-------|
| **Resource Name** | `spaarke-docintel-dev` |
| **Region** | West US 2 |
| **Endpoint** | `https://westus2.api.cognitive.microsoft.com/` |

### Dataverse

| Property | Value |
|----------|-------|
| **Environment** | `spaarkedev1.crm.dynamics.com` |
| **Primary Entities** | `sprk_document`, `sprk_analysis`, `email` |

---

## Complete App Service Configuration

**App Service Name**: `spe-api-dev-67e2xz`

### Core Settings

| Setting | Value | Description |
|---------|-------|-------------|
| `ASPNETCORE_ENVIRONMENT` | `Development` | Environment name |
| `TENANT_ID` | (configured) | Azure AD Tenant ID |
| `API_APP_ID` | (configured) | App Registration ID |
| `API_CLIENT_SECRET` | (configured) | App Registration Secret |

### Azure AD Authentication

| Setting | Value | Description |
|---------|-------|-------------|
| `AzureAd__Instance` | `https://login.microsoftonline.com/` | Azure AD instance |
| `AzureAd__TenantId` | (configured) | Tenant ID |
| `AzureAd__ClientId` | (configured) | App Registration Client ID |
| `AzureAd__ClientSecret` | (configured) | Client secret for OBO flow |
| `AzureAd__Audience` | `api://spe-api-dev` | API audience |

### Microsoft Graph

| Setting | Value | Description |
|---------|-------|-------------|
| `Graph__TenantId` | (configured) | Tenant ID |
| `Graph__ClientId` | (configured) | Graph client ID |
| `Graph__CertificateSource` | `KeyVault` | Certificate location |
| `Graph__KeyVaultUrl` | (configured) | Key Vault URL |
| `Graph__KeyVaultCertName` | (configured) | Certificate name |
| `Graph__ManagedIdentity__Enabled` | `true` | Use managed identity |
| `Graph__ManagedIdentity__ClientId` | (configured) | UAMI Client ID |
| `Graph__Scopes__0` | `.default` | Default scope |

### SharePoint Embedded

| Setting | Value | Description |
|---------|-------|-------------|
| `SharePointEmbedded__ContainerTypeId` | (configured) | SPE Container Type ID |
| `DEFAULT_CT_ID` | (configured) | Default Container Type |

### AI Services (Legacy Ai__ namespace)

| Setting | Value | Description |
|---------|-------|-------------|
| `Ai__Enabled` | `true` | Enable AI features |
| `Ai__OpenAiEndpoint` | `https://spaarke-openai-dev.openai.azure.com/` | OpenAI endpoint |
| `Ai__OpenAiKey` | (configured) | OpenAI API key |
| `Ai__SummarizeModel` | `gpt-4o-mini` | Summarization model |
| `Ai__DocIntelEndpoint` | `https://westus2.api.cognitive.microsoft.com/` | Document Intelligence |
| `Ai__DocIntelKey` | (configured) | Document Intelligence key |

### Document Intelligence Feature

| Setting | Value | Description |
|---------|-------|-------------|
| `DocumentIntelligence__Enabled` | `true` | Enable analysis features |
| `DocumentIntelligence__OpenAiEndpoint` | `https://spaarke-openai-dev.openai.azure.com/` | OpenAI endpoint |
| `DocumentIntelligence__OpenAiKey` | (configured) | OpenAI API key |
| `DocumentIntelligence__SummarizeModel` | `gpt-4o-mini` | Summarization model |
| `DocumentIntelligence__DocIntelEndpoint` | `https://westus2.api.cognitive.microsoft.com/` | Doc Intel endpoint |
| `DocumentIntelligence__DocIntelKey` | (configured) | Doc Intel key |
| `DocumentIntelligence__AiSearchEndpoint` | `https://spaarke-search-dev.search.windows.net/` | **REQUIRED for RAG** |
| `DocumentIntelligence__AiSearchKey` | (configured) | **REQUIRED for RAG** |
| `DocumentIntelligence__AiSearchIndexName` | `spaarke-records-index` | Record matching index |
| `DocumentIntelligence__RecordMatchingEnabled` | `true` | Enable record matching |

### Analysis Services (RAG)

| Setting | Value | Description |
|---------|-------|-------------|
| `Analysis__SharedIndexName` | `spaarke-knowledge-index-v2` | **Primary RAG index** |

### Email Processing

| Setting | Value | Description |
|---------|-------|-------------|
| `EmailProcessing__EnableWebhook` | `true` | Enable webhook processing |
| `EmailProcessing__WebhookSecret` | (configured) | Webhook validation secret |
| `EmailProcessing__DefaultContainerId` | (configured) | SPE Container for emails |
| `EmailProcessing__EnablePolling` | `true` | Enable backup polling |
| `EmailProcessing__AutoIndexToRag` | `false` | Auto-queue for RAG (set to `true` for testing) |

### Dataverse

| Setting | Value | Description |
|---------|-------|-------------|
| `Dataverse__EnvironmentUrl` | `https://spaarkedev1.crm.dynamics.com` | Dataverse URL |
| `Dataverse__ServiceUrl` | `https://spaarkedev1.crm.dynamics.com` | Service URL |
| `Dataverse__ClientId` | (configured) | App Registration |
| `Dataverse__ClientSecret` | (configured) | App secret |
| `Dataverse__TenantId` | (configured) | Tenant ID |

### Service Bus

| Setting | Value | Description |
|---------|-------|-------------|
| `ServiceBus__ConnectionString` | (configured) | Service Bus connection string |
| `ServiceBus__QueueName` | `spaarke-jobs` | Job queue name |

### Redis

| Setting | Value | Description |
|---------|-------|-------------|
| `Redis__Enabled` | `true` | Enable Redis caching |

### Logging

| Setting | Value | Description |
|---------|-------|-------------|
| `Logging__LogLevel__Default` | `Information` | Default log level |
| `Logging__LogLevel__Microsoft.AspNetCore` | `Warning` | ASP.NET Core log level |

### Managed Identity

| Setting | Value | Description |
|---------|-------|-------------|
| `ManagedIdentity__ClientId` | (configured) | UAMI Client ID |
| `UAMI_CLIENT_ID` | (configured) | UAMI Client ID (alternate) |

### Application Insights

| Setting | Value | Description |
|---------|-------|-------------|
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | (configured) | App Insights connection |
| `APPINSIGHTS_INSTRUMENTATIONKEY` | (configured) | Instrumentation key |
| `ApplicationInsightsAgent_EXTENSION_VERSION` | `~3` | Agent version |

### Dataverse Environment Variables

Configure in Power Platform > Solutions > Spaarke Document Intelligence:

| Variable | Description |
|----------|-------------|
| `sprk_BffApiBaseUrl` | `https://spe-api-dev-67e2xz.azurewebsites.net/api` |
| `sprk_OpenAiEndpoint` | Azure OpenAI endpoint |
| `sprk_OpenAiDeploymentName` | `gpt-4o-mini` |
| `sprk_AiSearchEndpoint` | AI Search endpoint |
| `sprk_AiSearchIndexName` | Index name |

---

## Verification Procedures

### Step 1: Verify API Health

```bash
# Check API health
curl https://{api-url}/healthz
# Expected: "Healthy" (200 OK)

# Check ping
curl https://{api-url}/ping
# Expected: "pong" (200 OK)
```

### Step 2: Verify PCF Controls

```bash
# Check PCF control versions in solution
pac solution list

# Verify AnalysisBuilder version
# Expected: v1.12.0 or higher

# Verify AnalysisWorkspace version
# Expected: v1.0.29 or higher
```

### Step 3: Verify Custom Pages

1. Open Power Apps maker portal (make.powerapps.com)
2. Navigate to Apps > Custom Pages
3. Verify `sprk_analysisbuilder_40af8` exists
4. Verify `sprk_analysisworkspace_52748` exists

### Step 4: Verify Form Integration

1. Open a `sprk_document` record in model-driven app
2. Verify "Analysis" tab exists
3. Verify Analysis subgrid displays on tab
4. Verify "+ New Analysis" button in ribbon
5. Click button - Analysis Builder should open as dialog

### Step 5: End-to-End Navigation Test

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open Document record | Document form loads |
| 2 | Click Analysis tab | Tab displays with subgrid |
| 3 | Click "+ New Analysis" | Analysis Builder dialog opens |
| 4 | Configure and execute analysis | Navigates to Analysis Workspace |
| 5 | Click back/close in Workspace | Returns to Document form |

---

## Known Issues

### BUG-001: Analysis Workspace Toolbar Hover/Click

**Severity**: Medium | **Status**: Not blocking

**Symptom**: Screen blinks/hides on toolbar button hover or click in Analysis Workspace.

**Impact**: Visual disruption but functionality works.

### Data Layer Issues (Deferred to R3)

| Issue | Description | Impact |
|-------|-------------|--------|
| Analysis Persistence | BFF API uses in-memory storage | Analysis sessions lost on API restart |
| Analysis Builder Empty | No scopes displayed | Builder UI shows no content |
| Analysis Workspace Empty | No analysis data loaded | Workspace shows empty state |

**Root Cause**: `AnalysisOrchestrationService.cs:36` uses static dictionary instead of Dataverse persistence.

**Resolution**: Scheduled for R3 - Dataverse integration implementation.

---

## Troubleshooting

### Azure Deployment Issues

#### Error: 403 Access Denied During AI Analysis

**Symptom**: AI analysis fails with "Access denied" or 403 error when downloading file from SharePoint Embedded

**Root Cause**: The analysis service was using app-only authentication (`DownloadFileAsync`) instead of OBO authentication for SPE file access.

**Resolution**: Analysis orchestration must use `DownloadFileAsUserAsync(httpContext, ...)` with On-Behalf-Of (OBO) authentication. This requires:
1. `HttpContext` to be passed from endpoints through orchestration service
2. Use `ISpeFileOperations.DownloadFileAsUserAsync(httpContext, driveId, itemId, ct)`
3. Never use `DownloadFileAsync` (app-only) for AI file access

**Verification**:
```bash
# Check Application Insights logs
az monitor app-insights query \
  --app sprkspaarkedev-aif-insights \
  --analytics-query "traces | where message contains 'Access denied' | project timestamp, message" \
  --offset 1h
```

See [SDAP Auth Patterns - Pattern 4](../architecture/sdap-auth-patterns.md#pattern-4-obo-for-ai-analysis-spe-file-access) for implementation details.

#### Error: Service Bus Connection String Missing

**Symptom**: API fails to start with Service Bus configuration error

**Resolution**: Configure `ServiceBus:ConnectionString` in App Settings

#### Error: Bicep ai-search Module BCP075

**Symptom**: `model1-shared.bicep` or `model2-full.bicep` fails to compile

**Resolution**: Change `listQueryKeys()[0].key` to `listQueryKeys().value[0].key`

### PCF Control Issues

#### Control Not Loading

**Symptom**: PCF control shows blank or error

**Resolution**:
1. Verify control is deployed: `pac solution list`
2. Clear browser cache (Ctrl+Shift+R)
3. Check browser console for JavaScript errors
4. Verify environment variable `sprk_BffApiBaseUrl` is set

#### Version Mismatch

**Symptom**: Old version showing despite deployment

**Resolution**:
1. Republish Custom Page in Power Apps maker
2. Run `pac solution publish-all`
3. Hard refresh browser (Ctrl+Shift+R)
4. Check version in control footer

### Custom Page Issues

#### Page Not Opening

**Symptom**: Clicking button does nothing or shows error

**Resolution**:
1. Verify Custom Page exists in solution
2. Check JavaScript console for navigation errors
3. Verify record is saved (unsaved records won't navigate)

### Form Integration Issues

#### Ribbon Button Not Visible

**Symptom**: "+ New Analysis" button missing from ribbon

**Resolution**:
1. Verify ribbon customization is published
2. Check security role has access
3. Verify enable rule (record must be saved)

---

## Reference

### Key Endpoints

| Service | URL Pattern |
|---------|-------------|
| BFF API | `https://spe-api-{env}-{suffix}.azurewebsites.net` |
| Azure OpenAI | `https://spaarke-openai-{env}.openai.azure.com/` |
| AI Search | `https://spaarke-search-{env}.search.windows.net` |
| Dataverse | `https://{org}.crm.dynamics.com` |

### Deployed Components (Dev Environment)

| Component | Identifier | Status |
|-----------|------------|--------|
| AnalysisBuilder PCF | v1.12.0 | Deployed |
| AnalysisWorkspace PCF | v1.0.29 | Deployed |
| Analysis Builder Custom Page | sprk_analysisbuilder_40af8 | Deployed |
| Analysis Workspace Custom Page | sprk_analysisworkspace_52748 | Deployed |
| Document Form - Analysis Tab | sprk_document main form | Deployed |
| Analysis Subgrid | On Analysis tab | Deployed |
| Navigation JavaScript | Web resource | Deployed |
| New Analysis Ribbon Button | Document form ribbon | Deployed |

### Solution Files

| File | Purpose |
|------|---------|
| `Spaarke_DocumentIntelligence_unmanaged.zip` | Development environments |
| `Spaarke_DocumentIntelligence_managed.zip` | Production environments |

### Source Code Locations

| Component | Path |
|-----------|------|
| AnalysisBuilder PCF | `src/client/pcf/AnalysisBuilder/` |
| AnalysisWorkspace PCF | `src/client/pcf/AnalysisWorkspace/` |
| Environment Variables | `control/utils/environmentVariables.ts` |
| BFF API Endpoints | `src/server/api/Sprk.Bff.Api/Api/Ai/` |

### Related Documentation

| Document | Purpose |
|----------|---------|
| [RAG-ARCHITECTURE.md](RAG-ARCHITECTURE.md) | RAG system architecture |
| [RAG-CONFIGURATION.md](RAG-CONFIGURATION.md) | RAG configuration reference |
| [RAG-TROUBLESHOOTING.md](RAG-TROUBLESHOOTING.md) | RAG troubleshooting guide |
| [ai-troubleshooting.md](./ai-troubleshooting.md) | General AI troubleshooting |
| [PCF-V9-PACKAGING.md](./PCF-V9-PACKAGING.md) | PCF build/deploy guide |

---

*Document created: 2025-12-28*
*Last updated: 2026-01-16*
*Projects: AI Document Intelligence R1 + R2 + R3 + Email-to-Document R2 + RAG Pipeline R1*
