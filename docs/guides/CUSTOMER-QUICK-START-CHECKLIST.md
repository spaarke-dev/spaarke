# Spaarke - Customer Quick Start Checklist

> **Version**: 2.0
> **Last Updated**: 2026-03-20
> **Print this page** for a handy deployment reference

---

## Pre-Deployment Checklist

| Done | Item | Notes |
|:----:|------|-------|
| [ ] | Azure subscription access confirmed | Contributor role required |
| [ ] | Power Platform admin access confirmed | System Administrator role |
| [ ] | Azure CLI installed | `az --version` to verify |
| [ ] | Power Platform CLI installed | `pac --version` to verify |
| [ ] | Deployment package received from Spaarke | Contact your representative |
| [ ] | All 7 environment variable values collected | See table below |

### Environment Variable Values to Collect

Collect these values before starting. They are set in Dataverse after solution import (step 4 below).

| Variable | What It Is | Where to Find It |
|----------|-----------|------------------|
| `sprk_BffApiBaseUrl` | BFF API base URL | Spaarke-provided (e.g., `https://api.spaarke.com`) |
| `sprk_BffApiAppId` | BFF API app registration client ID | Entra ID > App Registrations > Sprk.Bff.Api |
| `sprk_MsalClientId` | UI MSAL client ID | Entra ID > App Registrations > Spaarke UI |
| `sprk_TenantId` | Entra ID tenant ID | Entra ID > Overview |
| `sprk_AzureOpenAiEndpoint` | Azure OpenAI endpoint URL | Azure Portal > OpenAI > Keys and Endpoint |
| `sprk_ShareLinkBaseUrl` | Base URL for document share links | Spaarke-provided (e.g., `https://spaarke.app/doc`) |
| `sprk_SharePointEmbeddedContainerId` | SPE Container ID | Provisioned by Spaarke after SPE setup |

---

## Deployment Steps

### Azure Infrastructure

| Done | Step | Command / Action |
|:----:|------|------------------|
| [ ] | Login to Azure | `az login` |
| [ ] | Set subscription | `az account set --subscription "YOUR-SUB"` |
| [ ] | Create resource group | `az group create --name rg-spaarke-COMPANY-prod --location eastus` |
| [ ] | Deploy AI stack | `az deployment group create ...` (use provided template) |
| [ ] | Note OpenAI endpoint | Azure Portal > OpenAI > Keys and Endpoint |
| [ ] | Note AI Search endpoint | Azure Portal > AI Search > Overview |

### Power Platform Solution

| Done | Step | Command / Action |
|:----:|------|------------------|
| [ ] | Authenticate | `pac auth create --url https://ORG.crm.dynamics.com` |
| [ ] | Import SpaarkeCore solution | `pac solution import --path SpaarkeCore_managed.zip` |
| [ ] | Import remaining solutions | `pac solution import --path Spaarke_managed.zip` |
| [ ] | Verify import | `pac solution list` |

### Dataverse Environment Variables

Set all 7 variables after solution import. Use `Provision-Customer.ps1` (automated) or set manually:

**Automated (Recommended):**

```powershell
.\scripts\Provision-Customer.ps1 `
    -CustomerId "COMPANY" `
    -DisplayName "Company Name" `
    -TenantId "YOUR-TENANT-ID" `
    -ClientId "YOUR-CLIENT-ID" `
    -ClientSecret "YOUR-SECRET" `
    -BffApiBaseUrl "https://api.spaarke.com" `
    -BffApiAppId "BFF-APP-ID" `
    -MsalClientId "MSAL-CLIENT-ID" `
    -AzureOpenAiEndpoint "https://YOUR-OPENAI.openai.azure.com/"
```

**Manual (Power Platform Admin Center):**

| Done | Variable | Action |
|:----:|----------|--------|
| [ ] | `sprk_BffApiBaseUrl` | Power Platform Admin > Environments > Environment Variables |
| [ ] | `sprk_BffApiAppId` | Power Platform Admin > Environments > Environment Variables |
| [ ] | `sprk_MsalClientId` | Power Platform Admin > Environments > Environment Variables |
| [ ] | `sprk_TenantId` | Power Platform Admin > Environments > Environment Variables |
| [ ] | `sprk_AzureOpenAiEndpoint` | Power Platform Admin > Environments > Environment Variables |
| [ ] | `sprk_ShareLinkBaseUrl` | Power Platform Admin > Environments > Environment Variables |
| [ ] | `sprk_SharePointEmbeddedContainerId` | Power Platform Admin > Environments > Environment Variables |

### BFF API

| Done | Step | Command / Action |
|:----:|------|------------------|
| [ ] | Configure app settings | Azure Portal > App Service > Configuration |
| [ ] | Set `Ai__OpenAiEndpoint` | Your OpenAI endpoint URL |
| [ ] | Set `DocumentIntelligence__AiSearchEndpoint` | Your AI Search endpoint |
| [ ] | Verify health | `curl https://API/healthz` → "Healthy" |

### User Access

| Done | Step | Command / Action |
|:----:|------|------------------|
| [ ] | Assign security roles | Users + permissions > Users |
| [ ] | Verify Entra ID authentication | Sign in as a test user |

---

## Validation

Run the validation script to confirm the environment is correctly configured:

```powershell
.\scripts\Validate-DeployedEnvironment.ps1 `
    -DataverseUrl "https://ORG.crm.dynamics.com" `
    -BffApiUrl "https://api.spaarke.com"
```

Expected output: All checks pass (exit code 0).

### Verification Tests

| Test | Pass Criteria | Result |
|------|---------------|:------:|
| Env vars validation | All 7 vars set, no empty values | [ ] |
| API Health | Response: "Healthy" | [ ] |
| CORS | API accepts requests from Dataverse origin | [ ] |
| No dev values | Zero hardcoded dev URLs/IDs | [ ] |
| Analysis Tab | Tab loads in Document form | [ ] |
| New Analysis | Dialog opens on button click | [ ] |
| Run Analysis | Analysis completes successfully | [ ] |
| Export | File downloads with content | [ ] |

---

## Key Values to Record

| Item | Value |
|------|-------|
| Customer ID | `_____________` |
| Resource Group | `rg-spaarke-_____________-prod` |
| Dataverse URL | `https://_______________.crm.dynamics.com` |
| BFF API URL | `https://_______________.azurewebsites.net` |
| OpenAI Endpoint | `https://_______________.openai.azure.com/` |
| AI Search Endpoint | `https://_______________.search.windows.net` |
| Config File | `logs/provisioning/environment-config.json` |

---

## Support Contacts

| Need | Contact |
|------|---------|
| Technical Issues | support@spaarke.com |
| Full Documentation | [CUSTOMER-DEPLOYMENT-GUIDE.md](CUSTOMER-DEPLOYMENT-GUIDE.md) |
| Onboarding Runbook | [CUSTOMER-ONBOARDING-RUNBOOK.md](CUSTOMER-ONBOARDING-RUNBOOK.md) |

---

*Spaarke - Quick Start Checklist v2.0*
