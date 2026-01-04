# AI Document Intelligence - Quick Start Checklist

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
| [ ] | Note OpenAI key | Azure Portal > OpenAI > Keys and Endpoint |
| [ ] | Note AI Search endpoint | Azure Portal > AI Search > Overview |
| [ ] | Note AI Search key | Azure Portal > AI Search > Keys |

### Power Platform Solution

| Done | Step | Command / Action |
|:----:|------|------------------|
| [ ] | Authenticate | `pac auth create --url https://ORG.crm.dynamics.com` |
| [ ] | Import solution | `pac solution import --path Spaarke_managed.zip` |
| [ ] | Verify import | `pac solution list` |

### BFF API

| Done | Step | Command / Action |
|:----:|------|------------------|
| [ ] | Configure app settings | Azure Portal > App Service > Configuration |
| [ ] | Set `Ai__OpenAiEndpoint` | Your OpenAI endpoint URL |
| [ ] | Set `Ai__OpenAiKey` | Your OpenAI key |
| [ ] | Set `DocumentIntelligence__AiSearchEndpoint` | Your AI Search endpoint |
| [ ] | Set `DocumentIntelligence__AiSearchKey` | Your AI Search key |
| [ ] | Verify health | `curl https://API/healthz` → "Healthy" |

### Configuration

| Done | Step | Command / Action |
|:----:|------|------------------|
| [ ] | Set BFF API URL in Power Platform | Environment Variables |
| [ ] | Assign security roles | Users + permissions > Users |

---

## Verification Tests

| Test | Pass Criteria | Result |
|------|---------------|:------:|
| API Health | Response: "Healthy" | [ ] |
| Analysis Tab | Tab loads in Document form | [ ] |
| New Analysis | Dialog opens on button click | [ ] |
| Run Analysis | Analysis completes successfully | [ ] |
| Export | File downloads with content | [ ] |

---

## Key Values to Record

| Item | Value |
|------|-------|
| Resource Group | `rg-spaarke-_____________-prod` |
| OpenAI Endpoint | `https://_______________.openai.azure.com/` |
| AI Search Endpoint | `https://_______________.search.windows.net` |
| API URL | `https://_______________.azurewebsites.net` |
| Dynamics URL | `https://_______________.crm.dynamics.com` |

---

## Support Contacts

| Need | Contact |
|------|---------|
| Technical Issues | support@spaarke.com |
| Full Documentation | [CUSTOMER-DEPLOYMENT-GUIDE.md](CUSTOMER-DEPLOYMENT-GUIDE.md) |

---

*Spaarke AI Document Intelligence - Quick Start v1.0*
