# AI Document Intelligence - Customer Deployment Guide

> **Version**: 1.0
> **Date**: January 2026
> **Audience**: IT Administrators and Technical Staff

---

## Quick Start Checklist

Before you begin, ensure you have:

- [ ] Azure subscription with Contributor access
- [ ] Power Platform environment with System Administrator access
- [ ] Azure CLI installed (version 2.50+)
- [ ] Power Platform CLI installed (latest version)
- [ ] 30-60 minutes for initial deployment

---

## Table of Contents

1. [Overview](#overview)
2. [Prerequisites](#prerequisites)
3. [Deployment Steps](#deployment-steps)
4. [Configuration](#configuration)
5. [Verification](#verification)
6. [Troubleshooting](#troubleshooting)
7. [Support](#support)

---

## Overview

AI Document Intelligence enables intelligent document analysis within your Dynamics 365 environment. It provides:

- **AI-Powered Analysis**: Automated document summarization and extraction
- **Contract Review**: Clause analysis with risk identification
- **Entity Extraction**: Automatic identification of parties, dates, and amounts
- **Export Capabilities**: Export analysis results to Word, PDF, or email

### System Components

| Component | Purpose |
|-----------|---------|
| Azure AI Services | Document processing and analysis |
| Power Platform Solution | Dataverse entities and UI components |
| BFF API | Backend service connecting components |

### Estimated Deployment Time

| Step | Time |
|------|------|
| Azure Infrastructure | 15-20 minutes |
| Power Platform Solution | 10-15 minutes |
| Configuration | 10-15 minutes |
| Verification | 5-10 minutes |
| **Total** | **40-60 minutes** |

---

## Prerequisites

### 1. Required Permissions

| Resource | Required Access |
|----------|----------------|
| Azure Subscription | Contributor role or higher |
| Power Platform | System Administrator security role |
| Azure AD | Application Administrator (for app registrations) |

### 2. Required Tools

Install these tools before starting:

#### Azure CLI

```bash
# Windows (PowerShell as Administrator)
winget install Microsoft.AzureCLI

# Verify installation
az --version
```

#### Power Platform CLI

```bash
# Windows (PowerShell as Administrator)
winget install Microsoft.PowerAppsCLI

# Verify installation
pac --version
```

### 3. Verify Access

Before proceeding, verify your access:

```bash
# Login to Azure
az login

# Verify subscription access
az account show

# Login to Power Platform
pac auth create --url https://YOUR-ORG.crm.dynamics.com
```

---

## Deployment Steps

### Step 1: Deploy Azure Infrastructure

#### 1.1 Login to Azure

```bash
az login
az account set --subscription "YOUR-SUBSCRIPTION-NAME"
```

#### 1.2 Create Resource Group

```bash
az group create \
  --name rg-spaarke-YOUR-COMPANY-prod \
  --location eastus
```

**Replace**: `YOUR-COMPANY` with your company identifier (e.g., `contoso`)

#### 1.3 Deploy AI Services

Contact your Spaarke representative to receive the deployment template, then run:

```bash
az deployment group create \
  --resource-group rg-spaarke-YOUR-COMPANY-prod \
  --template-file ai-foundry-stack.bicep \
  --parameters customerId=YOUR-COMPANY environment=prod location=eastus
```

#### 1.4 Note the Deployed Resources

After deployment completes, note these values for later configuration:

| Resource | How to Find |
|----------|-------------|
| Azure OpenAI Endpoint | Azure Portal > Your OpenAI resource > Keys and Endpoint |
| Azure OpenAI Key | Azure Portal > Your OpenAI resource > Keys and Endpoint |
| AI Search Endpoint | Azure Portal > Your AI Search resource > Overview |
| AI Search Key | Azure Portal > Your AI Search resource > Keys |

---

### Step 2: Deploy Power Platform Solution

#### 2.1 Authenticate to Power Platform

```bash
pac auth create --url https://YOUR-ORG.crm.dynamics.com
```

#### 2.2 Import the Solution

```bash
pac solution import \
  --path Spaarke_DocumentIntelligence_managed.zip \
  --activate-plugins
```

#### 2.3 Verify Import

```bash
pac solution list
```

You should see `Spaarke Document Intelligence` in the list.

---

### Step 3: Deploy BFF API

The BFF API is deployed to Azure App Service. Your Spaarke representative will provide:

1. The App Service deployment package
2. Configuration values for your environment

#### 3.1 Configure App Settings

In Azure Portal, navigate to your App Service and add these settings:

| Setting | Value |
|---------|-------|
| `Ai__Enabled` | `true` |
| `Ai__OpenAiEndpoint` | `https://YOUR-OPENAI.openai.azure.com/` |
| `Ai__OpenAiKey` | `YOUR-OPENAI-KEY` |
| `Ai__SummarizeModel` | `gpt-4o-mini` |
| `DocumentIntelligence__Enabled` | `true` |
| `DocumentIntelligence__AiSearchEndpoint` | `https://YOUR-SEARCH.search.windows.net` |
| `DocumentIntelligence__AiSearchKey` | `YOUR-SEARCH-KEY` |

#### 3.2 Verify API Health

```bash
curl https://YOUR-API.azurewebsites.net/healthz
# Expected response: "Healthy"

curl https://YOUR-API.azurewebsites.net/ping
# Expected response: "pong"
```

---

## Configuration

### Environment Variables in Power Platform

After solution import, configure these environment variables:

1. Open **Power Platform Admin Center**
2. Navigate to **Environments** > Select your environment
3. Open **Solutions** > **Spaarke Document Intelligence**
4. Find **Environment Variables** and update:

| Variable | Value |
|----------|-------|
| BFF API Base URL | `https://YOUR-API.azurewebsites.net/api` |

### Security Role Assignment

Assign users to the appropriate security roles:

| Role | Who Should Have It |
|------|-------------------|
| Spaarke AI Analysis User | All users who need to run analyses |
| Spaarke AI Analysis Admin | Administrators who manage playbooks |

To assign roles:
1. Open **Power Platform Admin Center**
2. Navigate to **Environments** > Select your environment
3. Click **Settings** > **Users + permissions** > **Users**
4. Select users and click **Manage security roles**

---

## Verification

Follow these steps to verify successful deployment:

### Test 1: API Health Check

```bash
# Check API is running
curl https://YOUR-API.azurewebsites.net/healthz
# Expected: "Healthy"
```

**Pass Criteria**: Response is "Healthy"

### Test 2: Access Dynamics 365

1. Open your Dynamics 365 environment
2. Navigate to a Document record
3. Click the **Analysis** tab

**Pass Criteria**: Analysis tab loads without errors

### Test 3: Create Analysis

1. Open a Document record with an attached file
2. Click **+ New Analysis** button in the ribbon
3. The Analysis Builder dialog should open

**Pass Criteria**: Analysis Builder dialog opens and loads configuration options

### Test 4: Run Analysis

1. In Analysis Builder, select analysis options
2. Click **Execute Analysis**
3. Wait for analysis to complete

**Pass Criteria**:
- Analysis completes without errors
- Results display in Analysis Workspace

### Test 5: Export Results

1. In Analysis Workspace, click the export button
2. Select Word or PDF format
3. Download the exported file

**Pass Criteria**: Export downloads successfully with analysis content

---

## Troubleshooting

### Common Issues

#### Issue: API returns 401 Unauthorized

**Symptom**: API calls fail with 401 error

**Solution**:
1. Verify the Azure AD app registration is configured correctly
2. Check that the user has the appropriate security role
3. Ensure the authentication token is being passed correctly

#### Issue: Analysis Builder won't open

**Symptom**: Clicking "New Analysis" does nothing

**Solution**:
1. Ensure the document record is saved (not a new unsaved record)
2. Clear browser cache (Ctrl+Shift+R)
3. Check browser console for JavaScript errors
4. Verify the Custom Page exists in the solution

#### Issue: Analysis fails with "File not accessible"

**Symptom**: Analysis starts but fails with file access error

**Solution**:
1. Verify the document has a file attached
2. Check SharePoint Embedded permissions
3. Ensure the user has access to the document

#### Issue: Export button not working

**Symptom**: Export button doesn't respond or shows error

**Solution**:
1. Verify analysis has completed successfully
2. Check API connectivity
3. Ensure export services are enabled in configuration

### Checking Logs

#### Azure API Logs

1. Open Azure Portal
2. Navigate to your App Service
3. Click **Monitoring** > **Log stream**

#### Power Platform Logs

1. Open Power Platform Admin Center
2. Navigate to **Analytics** > **Dataverse analytics**

### Getting Help

If issues persist:

1. **Collect Information**:
   - Screenshot of the error
   - Steps to reproduce
   - Browser console output (F12 > Console)
   - API response details

2. **Contact Support**:
   - Email: support@spaarke.com
   - Include the collected information

---

## Post-Deployment Checklist

Use this checklist to confirm everything is working:

- [ ] Azure infrastructure deployed successfully
- [ ] Power Platform solution imported
- [ ] BFF API health check passes
- [ ] Environment variables configured
- [ ] Security roles assigned to users
- [ ] Test analysis completed successfully
- [ ] Export functionality verified
- [ ] Users trained on basic operations

---

## Maintenance

### Regular Monitoring

| Check | Frequency | How |
|-------|-----------|-----|
| API Health | Daily | `curl https://YOUR-API/healthz` |
| Error Logs | Weekly | Azure Portal > App Service > Logs |
| Usage Metrics | Monthly | Application Insights dashboard |

### Updates

Spaarke will provide solution updates via:
1. Managed solution packages
2. API deployments via Azure DevOps

Follow the same import process for solution updates.

---

## Appendix

### Deployed Resources

| Resource Type | Name Pattern | Purpose |
|---------------|--------------|---------|
| Resource Group | `rg-spaarke-{customer}-{env}` | Container for all resources |
| Azure OpenAI | `spaarke-openai-{env}` | AI model hosting |
| AI Search | `spaarke-search-{env}` | Document search and RAG |
| App Service | `spe-api-{env}-{suffix}` | BFF API hosting |
| Application Insights | `spaarke-appinsights-{env}` | Monitoring |

### Key URLs

| Service | URL Pattern |
|---------|-------------|
| API Base URL | `https://spe-api-{env}-{suffix}.azurewebsites.net` |
| Health Check | `https://spe-api-{env}-{suffix}.azurewebsites.net/healthz` |
| Dynamics 365 | `https://{org}.crm.dynamics.com` |
| Azure Portal | `https://portal.azure.com` |

### Contact Information

| Type | Contact |
|------|---------|
| Technical Support | support@spaarke.com |
| Sales | sales@spaarke.com |
| Documentation | docs.spaarke.com |

---

*Last Updated: January 2026*
*Version: 1.0*
