# AI Document Intelligence - Deployment Guide

> **Version**: 2.0
> **Created**: 2025-12-28
> **Updated**: 2025-12-29
> **Projects**: AI Document Intelligence R1 + R2

---

## Table of Contents

1. [Overview](#overview)
2. [Prerequisites](#prerequisites)
3. [Phase 1: Azure Infrastructure (R1)](#phase-1-azure-infrastructure-r1)
4. [Phase 2: Dataverse Solution (R1)](#phase-2-dataverse-solution-r1)
5. [Phase 3: PCF Controls (R2)](#phase-3-pcf-controls-r2)
6. [Phase 4: Custom Pages (R2)](#phase-4-custom-pages-r2)
7. [Phase 5: Form Integration (R2)](#phase-5-form-integration-r2)
8. [Environment Configuration](#environment-configuration)
9. [Verification Procedures](#verification-procedures)
10. [Known Issues](#known-issues)
11. [Troubleshooting](#troubleshooting)
12. [Reference](#reference)

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

### Architecture Components

| Component | Purpose | Version/Status |
|-----------|---------|----------------|
| BFF API | Backend-for-Frontend serving PCF controls | Deployed |
| Azure OpenAI | LLM for summarization and analysis | gpt-4o-mini |
| AI Search | RAG knowledge retrieval | spaarke-search-dev |
| Document Intelligence | Document parsing | westus2 |
| AI Foundry | Prompt Flow orchestration | sprkspaarkedev-aif-hub |
| Dataverse | Entity storage for Analysis records | sprk_analysis |
| AnalysisBuilder PCF | Analysis configuration dialog | v1.12.0 |
| AnalysisWorkspace PCF | Analysis workspace with chat | v1.0.29 |
| Custom Pages | Power Apps hosts for PCF controls | Deployed |

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

## Environment Configuration

### BFF API App Settings

Configure the following in Azure App Service > Configuration:

#### AI Services

| Setting | Example Value |
|---------|---------------|
| `Ai__Enabled` | `true` |
| `Ai__OpenAiEndpoint` | `https://spaarke-openai-dev.openai.azure.com/` |
| `Ai__OpenAiKey` | `{from Azure OpenAI Keys}` |
| `Ai__SummarizeModel` | `gpt-4o-mini` |
| `Ai__DocIntelEndpoint` | `https://westus2.api.cognitive.microsoft.com/` |
| `Ai__DocIntelKey` | `{from Document Intelligence Keys}` |

#### Document Intelligence Feature

| Setting | Example Value |
|---------|---------------|
| `DocumentIntelligence__Enabled` | `true` |
| `DocumentIntelligence__AiSearchEndpoint` | `https://spaarke-search-dev.search.windows.net` |
| `DocumentIntelligence__AiSearchKey` | `{from AI Search Keys}` |
| `DocumentIntelligence__AiSearchIndexName` | `spaarke-records-index` |
| `DocumentIntelligence__RecordMatchingEnabled` | `true` |

### Dataverse Environment Variables

Configure in Power Platform Admin Center > Solutions > Spaarke Document Intelligence:

| Variable | Description |
|----------|-------------|
| `sprk_BffApiBaseUrl` | BFF API URL (e.g., `https://spe-api-dev-67e2xz.azurewebsites.net/api`) |
| `sprk_OpenAiEndpoint` | Azure OpenAI endpoint |
| `sprk_OpenAiDeploymentName` | Deployment name (gpt-4o-mini) |
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
| [SPAARKE-AI-ARCHITECTURE.md](./SPAARKE-AI-ARCHITECTURE.md) | Full AI architecture |
| [ai-troubleshooting.md](./ai-troubleshooting.md) | Troubleshooting guide |
| [PCF-V9-PACKAGING.md](./PCF-V9-PACKAGING.md) | PCF build/deploy guide |

---

*Document created: 2025-12-28*
*Last updated: 2025-12-29*
*Projects: AI Document Intelligence R1 + R2*
