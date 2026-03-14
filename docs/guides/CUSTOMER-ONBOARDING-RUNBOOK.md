# Customer Onboarding Runbook

> **Version**: 1.0
> **Last Updated**: 2026-03-13
> **Owner**: Platform Operations
> **Applies To**: Spaarke Production Environments
> **Related Scripts**: `Provision-Customer.ps1`, `Test-Deployment.ps1`, `Decommission-Customer.ps1`

---

## Overview

This runbook covers the complete process of onboarding a new customer to the Spaarke platform, from initial request through fully operational environment and customer handoff. The process is automated via `Provision-Customer.ps1`, which orchestrates all Azure, Dataverse, and application-level setup.

**Important**: The demo environment was provisioned using this exact process (FR-06). There is no special-casing for any customer.

**Estimated Duration**: 20-30 minutes (automated), plus 1-2 hours for verification and handoff preparation.

---

## Table of Contents

1. [Pre-Onboarding Checklist](#1-pre-onboarding-checklist)
2. [Prerequisites and Tooling](#2-prerequisites-and-tooling)
3. [Provisioning Execution](#3-provisioning-execution)
4. [Post-Provisioning Verification](#4-post-provisioning-verification)
5. [User Access Setup](#5-user-access-setup)
6. [Customer Handoff and Training](#6-customer-handoff-and-training)
7. [Rollback and Decommissioning](#7-rollback-and-decommissioning)
8. [Escalation Procedures](#8-escalation-procedures)
9. [Appendix: Resource Naming Reference](#9-appendix-resource-naming-reference)

---

## 1. Pre-Onboarding Checklist

Complete all items before starting provisioning. All items are mandatory unless marked optional.

### 1a. Business Requirements

| # | Item | Details | Status |
|---|------|---------|--------|
| 1 | Customer agreement signed | Legal/commercial agreement in place | [ ] |
| 2 | Customer ID assigned | Lowercase, alphanumeric, 3-10 characters (e.g., `acme`, `globex`) | [ ] |
| 3 | Customer display name confirmed | Human-readable name (e.g., "Acme Legal Services") | [ ] |
| 4 | Power Platform license confirmed | Customer needs Dataverse environment entitlement | [ ] |
| 5 | Azure subscription quota verified | Sufficient quota for Storage, Key Vault, Service Bus, Redis in target region | [ ] |
| 6 | Primary contact identified | Name, email, phone for the customer's technical lead | [ ] |

### 1b. Technical Requirements

| # | Item | Details | Status |
|---|------|---------|--------|
| 7 | Service principal credentials available | `ClientId` + `ClientSecret` (or `CertificateThumbprint`) for PAC CLI and Admin API auth | [ ] |
| 8 | Tenant ID confirmed | Entra ID tenant: `a221a95e-...` (same for all customers in current architecture) | [ ] |
| 9 | Azure region confirmed | Default: `westus2`. Customer can request alternate if needed. | [ ] |
| 10 | Dataverse region confirmed | Default: `unitedstates`. Must match Azure region locality. | [ ] |
| 11 | B2B guest access requirements | List of external users who need access (if applicable) | [ ] |
| 12 | SPE container requirements | Number and naming of SharePoint Embedded containers needed | [ ] |

### 1c. Validation

| # | Item | Details | Status |
|---|------|---------|--------|
| 13 | Customer ID uniqueness verified | Confirm no existing resource group `rg-spaarke-{customerId}-prod` | [ ] |
| 14 | Naming collision check | Confirm Key Vault name `sprk-{customerId}-prod-kv` is available (globally unique) | [ ] |
| 15 | Storage account name available | Confirm `sprk{customerId}prodsa` is available (globally unique, max 24 chars) | [ ] |

**To verify naming availability:**

```powershell
# Check resource group does not exist
az group exists --name "rg-spaarke-{customerId}-prod"
# Should return "false"

# Check Key Vault name availability
az keyvault list-deleted --query "[?name=='sprk-{customerId}-prod-kv']"
# Should return empty array

# Check storage account name availability
az storage account check-name --name "sprk{customerId}prodsa"
# nameAvailable should be true
```

---

## 2. Prerequisites and Tooling

### Required Tools

| Tool | Minimum Version | Install Command | Purpose |
|------|----------------|-----------------|---------|
| Azure CLI (`az`) | 2.55+ | `winget install Microsoft.AzureCLI` | Azure resource management |
| Power Platform CLI (`pac`) | 1.30+ | `dotnet tool install --global Microsoft.PowerApps.CLI.Tool` | Dataverse operations |
| PowerShell | 7.0+ | Pre-installed on Windows | Script execution |

### Required Permissions

| Permission | Scope | Who |
|-----------|-------|-----|
| Azure Contributor | Target subscription | Operator running the script |
| Key Vault Secrets Officer | Platform Key Vault (`sprk-platform-prod-kv`) | Operator (to read shared secrets) |
| Power Platform Admin | Tenant-level | Service principal or operator |
| Microsoft Graph (SPE) | Application-level | Service principal |

### Authentication

```powershell
# 1. Azure CLI login
az login
az account set --subscription "<production-subscription-id>"

# 2. Verify Azure identity
az account show
# Confirm correct subscription and user

# 3. PAC CLI authentication (for Dataverse)
pac auth create --url "https://spaarke-{customerId}.crm.dynamics.com" `
    --tenant "a221a95e-..." --applicationId "<client-id>" --clientSecret "<secret>"
```

---

## 3. Provisioning Execution

### 3a. Script Overview

`Provision-Customer.ps1` orchestrates 10 steps in sequence:

| Step | Action | Duration | Idempotent |
|------|--------|----------|------------|
| 1 | Validate inputs and prerequisites | ~10s | Yes |
| 2 | Create resource group `rg-spaarke-{customerId}-{env}` | ~15s | Yes |
| 3 | Deploy `customer.bicep` (Storage, Key Vault, Service Bus, Redis) | ~5 min | Yes |
| 4 | Populate customer Key Vault with secrets | ~30s | Yes |
| 5 | Create Dataverse environment via Power Platform Admin API | ~2 min | Yes |
| 6 | Wait for Dataverse environment provisioning | ~5-15 min | Yes |
| 7 | Import managed solutions (`Deploy-DataverseSolutions.ps1`) | ~5 min | Yes |
| 8 | Provision SPE containers | ~1 min | Yes |
| 9 | Register customer in BFF API tenant registry | ~10s | Yes |
| 10 | Run smoke tests (`Test-Deployment.ps1`) | ~2 min | Yes |

### 3b. Preview Mode (Recommended First)

Always run a preview before actual provisioning:

```powershell
.\scripts\Provision-Customer.ps1 `
    -CustomerId "acme" `
    -DisplayName "Acme Legal Services" `
    -TenantId "a221a95e-..." `
    -ClientId "<service-principal-client-id>" `
    -ClientSecret "<service-principal-secret>" `
    -WhatIf
```

Review the output to confirm:
- Resource names match expectations
- No naming collisions detected
- All prerequisites pass validation

### 3c. Execute Provisioning

```powershell
.\scripts\Provision-Customer.ps1 `
    -CustomerId "acme" `
    -DisplayName "Acme Legal Services" `
    -TenantId "a221a95e-..." `
    -ClientId "<service-principal-client-id>" `
    -ClientSecret "<service-principal-secret>" `
    -EnvironmentName "prod" `
    -Location "westus2" `
    -DataverseRegion "unitedstates" `
    -BffApiBaseUrl "https://api.spaarke.com"
```

**All parameters:**

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `-CustomerId` | Yes | — | Lowercase alphanumeric, 3-10 chars |
| `-DisplayName` | Yes | — | Human-readable customer name |
| `-TenantId` | Yes | — | Entra ID tenant ID |
| `-ClientId` | Yes | — | Service principal client ID |
| `-ClientSecret` | Yes* | — | Service principal secret (*or use `-CertificateThumbprint`) |
| `-CertificateThumbprint` | Yes* | — | Certificate thumbprint (*or use `-ClientSecret`) |
| `-EnvironmentName` | No | `prod` | Target environment: dev, staging, prod |
| `-Location` | No | `westus2` | Azure region |
| `-PlatformKeyVaultName` | No | `sprk-platform-prod-kv` | Shared platform Key Vault |
| `-PlatformResourceGroup` | No | `rg-spaarke-platform-prod` | Shared platform resource group |
| `-BffApiBaseUrl` | No | `https://api.spaarke.com` | BFF API URL for tenant registration |
| `-DataverseRegion` | No | `unitedstates` | Power Platform region for Dataverse |
| `-ResumeFromStep` | No | 0 (auto-detect) | Resume from specific step (1-10) |
| `-SkipDataverse` | No | `$false` | Skip Dataverse steps 5-7 |

### 3d. Resuming After Failure

The script is idempotent (FR-10) and tracks progress in a state file:

```
logs/provisioning/provision-{customerId}-{env}.state.json
```

**Automatic resume** — Re-run the same command. The script detects the last completed step and resumes from the next one.

**Manual resume** — Force resume from a specific step:

```powershell
.\scripts\Provision-Customer.ps1 `
    -CustomerId "acme" `
    -DisplayName "Acme Legal Services" `
    -TenantId "a221a95e-..." `
    -ClientId "<client-id>" `
    -ClientSecret "<secret>" `
    -ResumeFromStep 5
```

**Skip Dataverse** — If the Dataverse environment was created manually:

```powershell
.\scripts\Provision-Customer.ps1 `
    -CustomerId "acme" `
    -DisplayName "Acme Legal Services" `
    -TenantId "a221a95e-..." `
    -ClientId "<client-id>" `
    -ClientSecret "<secret>" `
    -SkipDataverse
```

### 3e. Log Files

All provisioning output is logged to:

```
logs/provision-{customerId}-{env}-{yyyyMMdd-HHmmss}.log
```

Review this log for troubleshooting if any step fails.

---

## 4. Post-Provisioning Verification

### 4a. Automated Smoke Tests

The provisioning script runs `Test-Deployment.ps1` as its final step. You can also run it independently:

```powershell
.\scripts\Test-Deployment.ps1 `
    -Environment prod `
    -CustomerId "acme" `
    -Verbose
```

**Test groups and what they verify:**

| Group | Tests | Critical |
|-------|-------|----------|
| BFF API | Health endpoint (200), ping, auth (401 without token), response time (<2s) | Yes |
| Dataverse | PAC CLI auth, SpaarkeCore solution installed, org accessible | Yes |
| SPE | Container endpoint reachable, drive endpoint reachable | Yes |
| AI Services | OpenAI endpoint, model deployments, AI Search, Document Intelligence | Yes |
| Redis | Cache resource exists, SSL port accessible | Yes |
| Service Bus | Namespace exists and active, `document-processing` queue exists | Yes |

**Expected result**: All critical tests pass. Non-critical warnings are acceptable.

### 4b. Manual Verification Checklist

| # | Check | Command / Action | Expected Result | Status |
|---|-------|-----------------|-----------------|--------|
| 1 | Resource group exists | `az group show --name rg-spaarke-acme-prod` | Returns resource group details | [ ] |
| 2 | Key Vault accessible | `az keyvault secret list --vault-name sprk-acme-prod-kv` | Lists secrets without error | [ ] |
| 3 | Storage account accessible | `az storage account show --name sprkacmeprodsa` | Returns account details | [ ] |
| 4 | Dataverse environment online | `pac org who` (with correct auth profile) | Shows org details | [ ] |
| 5 | Solutions imported | `pac solution list` | All 10 Spaarke solutions listed | [ ] |
| 6 | BFF API health | `curl https://api.spaarke.com/healthz` | Returns `Healthy` (HTTP 200) | [ ] |
| 7 | Tenant registered | BFF API admin endpoint or database check | Customer appears in tenant registry | [ ] |
| 8 | SPE containers created | BFF API admin endpoint or Graph API check | Customer containers listed | [ ] |

### 4c. Dataverse Solution Verification

Verify all 10 managed solutions are imported in correct order:

```powershell
pac solution list
```

Expected solutions (in dependency order):
1. SpaarkeCore
2. Web resources (sprk_webresources)
3. Feature solutions (remaining 8 in dependency order)

If any solution is missing, run:

```powershell
.\scripts\Deploy-DataverseSolutions.ps1 -EnvironmentUrl "https://spaarke-acme.crm.dynamics.com"
```

---

## 5. User Access Setup

### 5a. Internal Users (Same Tenant)

For users within the same Entra ID tenant:

1. Assign Dataverse security roles:
   - Navigate to Power Platform Admin Center > Environments > spaarke-acme
   - Settings > Users + permissions > Security roles
   - Assign appropriate Spaarke roles to users

2. Verify BFF API access:
   - User authenticates via Entra ID
   - BFF API resolves tenant from user's token
   - Access granted based on Dataverse security roles

### 5b. External Users (B2B Guest Access)

For users in a different Entra ID tenant:

**Automated (Recommended):**

Use `Invite-DemoUsers.ps1` to automate B2B invitations and verify access:

```powershell
# 1. Edit demo-users.json to add/update user list
# 2. Run the invitation script
.\scripts\Invite-DemoUsers.ps1

# Or preview first
.\scripts\Invite-DemoUsers.ps1 -WhatIf

# Verify existing access only (no changes)
.\scripts\Invite-DemoUsers.ps1 -VerifyOnly
```

The script handles:
- Sending B2B guest invitations via Microsoft Graph API
- Checking for existing guest users (idempotent)
- Guiding Dataverse security role assignment
- Verifying end-to-end access (Entra ID + Dataverse + SPE)

User list is managed in `scripts/demo-users.json`.

**Manual (Alternative):**

1. **Invite as B2B guest**:
   - Azure Portal > Entra ID > Users > New guest user
   - Or use PowerShell: `New-AzureADMSInvitation -InvitedUserEmailAddress "user@external.com" -InviteRedirectUrl "https://app.spaarke.com"`

2. **Assign Dataverse access**:
   - Power Platform Admin Center > Environments > spaarke-{customerId}
   - Settings > Users + permissions > Users > Add user
   - Assign security roles: `Basic User` + `Spaarke User`

3. **Verify access**:
   - Guest user completes invitation acceptance
   - Guest can authenticate to BFF API
   - Guest can access Dataverse records per their security role
   - Run `.\scripts\Invite-DemoUsers.ps1 -VerifyOnly` to confirm

### 5c. Service Account Setup (Optional)

For automated integrations:

1. Create dedicated app registration in Entra ID
2. Grant necessary API permissions (Graph, Dataverse)
3. Register as application user in Dataverse environment
4. Store credentials in customer Key Vault (`sprk-{customerId}-prod-kv`)

---

## 6. Customer Handoff and Training

### 6a. Handoff Checklist

| # | Item | Delivered To | Status |
|---|------|-------------|--------|
| 1 | Environment URLs shared | Customer technical lead | [ ] |
| 2 | User accounts created and verified | All identified users | [ ] |
| 3 | Access tested by customer representative | Customer technical lead | [ ] |
| 4 | Training session scheduled | Customer team | [ ] |
| 5 | Support escalation path communicated | Customer technical lead | [ ] |
| 6 | SLA terms reviewed | Customer technical lead | [ ] |

### 6b. Information to Provide to Customer

| Information | Value |
|-------------|-------|
| Application URL | `https://app.spaarke.com` (or customer-specific URL) |
| Dataverse Environment | `https://spaarke-{customerId}.crm.dynamics.com` |
| Support Email | (as per agreement) |
| Status Page | (if applicable) |

### 6c. Training Topics

1. **Document management**: Upload, browse, search documents in SPE
2. **AI features**: Document analysis, semantic search, playbook execution
3. **Workspace navigation**: Corporate workspace, matter workspace, project workspace
4. **Administration**: User management, security roles, configuration

### 6d. Post-Handoff Monitoring

For the first 7 days after handoff:

- Monitor customer-specific logs in App Insights (filter by `customerId` dimension)
- Check for elevated error rates
- Verify AI service usage is within expected bounds
- Confirm no performance degradation for other customers (NFR-03)

---

## 7. Rollback and Decommissioning

### 7a. Partial Rollback

If provisioning succeeds but customer onboarding is cancelled:

```powershell
# Preview what will be deleted (always run first)
.\scripts\Decommission-Customer.ps1 -CustomerId "acme" -DryRun

# Execute decommission with confirmation prompt
.\scripts\Decommission-Customer.ps1 -CustomerId "acme"
```

### 7b. Decommission Script Steps

`Decommission-Customer.ps1` performs a safe, ordered teardown:

| Step | Action | Safety Check |
|------|--------|-------------|
| 1 | Validate customer exists | Resource group name matches expected pattern |
| 2 | Safety checks | Blocked if resource group is a platform group |
| 3 | De-register from BFF API tenant registry | Handles 404 (already removed) gracefully |
| 4 | Remove SPE containers | Via Microsoft Graph API |
| 5 | Delete Dataverse environment | Via PAC CLI `admin delete` |
| 6 | Delete Azure resource group | Async with wait + Key Vault soft-delete purge |
| 7 | Verify cleanup complete | Confirms resource group and Key Vault are gone |

**Parameters:**

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `-CustomerId` | Yes | — | Customer to decommission |
| `-Environment` | No | `prod` | Target environment |
| `-DryRun` | No | `$false` | Preview mode (no changes) |
| `-Force` | No | `$false` | Skip confirmation prompts |
| `-SkipDataverse` | No | `$false` | Skip Dataverse deletion |
| `-SkipSpe` | No | `$false` | Skip SPE container removal |
| `-BffApiUrl` | No | `https://api.spaarke.com` | BFF API URL |
| `-Region` | No | `westus2` | Azure region |
| `-LogPath` | No | Auto-generated | Log file path |

**Safety features:**
- DryRun mode lists resources without deleting
- Requires typing `YES` to confirm (unless `-Force`)
- Platform resource groups are explicitly blocked from deletion
- Resource group name must match `rg-spaarke-{id}-(dev|staging|prod)` pattern

### 7c. Selective Decommission

```powershell
# Azure resources only (keep Dataverse and SPE)
.\scripts\Decommission-Customer.ps1 -CustomerId "acme" -SkipDataverse -SkipSpe

# Non-interactive (for CI/CD)
.\scripts\Decommission-Customer.ps1 -CustomerId "acme" -Force
```

---

## 8. Escalation Procedures

### 8a. Provisioning Failures

| Failure | Likely Cause | Resolution | Escalation |
|---------|-------------|------------|------------|
| Step 1 fails (validation) | Missing tool or auth | Install tools, re-authenticate | None — operator error |
| Step 2 fails (resource group) | Subscription quota or permissions | Check quota: `az vm list-usage --location westus2` | Azure admin |
| Step 3 fails (Bicep deploy) | Template error or resource conflict | Check deployment: `az deployment group show --name ... --resource-group ...` | Platform team |
| Step 4 fails (Key Vault) | Access policy or naming collision | Verify RBAC roles on Key Vault | Platform team |
| Step 5-6 fails (Dataverse) | License, quota, or API limitation | Check Power Platform Admin Center for errors | Power Platform admin |
| Step 7 fails (solutions) | Solution dependency or version conflict | Check `pac solution list` for partial imports | Platform team |
| Step 8 fails (SPE) | Graph API permissions | Verify app registration has SPE permissions | Platform team |
| Step 9 fails (tenant registry) | BFF API unreachable or auth error | Check `https://api.spaarke.com/healthz` | Platform team |
| Step 10 fails (smoke tests) | One or more services not ready | Re-run: `.\scripts\Test-Deployment.ps1 -CustomerId acme -Verbose` | See specific test group |

### 8b. Post-Provisioning Issues

| Issue | Diagnostic Steps | Escalation |
|-------|-----------------|------------|
| Users cannot authenticate | Check Entra ID B2B invitation status, Dataverse security roles | Identity team |
| Documents not accessible | Check SPE container permissions, Graph API connectivity | Platform team |
| AI features not working | Check OpenAI/AI Search endpoints in App Insights | AI/Platform team |
| Slow performance | Check App Insights for latency spikes, Redis connectivity | Platform team |
| Data isolation concern | Verify resource group separation, tenant registry configuration | Security team |

### 8c. Emergency Contacts

| Role | Responsibility | Contact |
|------|---------------|---------|
| Platform Operations | Infrastructure, deployments, monitoring | (as configured) |
| Power Platform Admin | Dataverse environments, licensing | (as configured) |
| Security Team | Access control, data isolation | (as configured) |
| Azure Support | Azure resource issues | Azure Portal > Support |

---

## 9. Appendix: Resource Naming Reference

All resources follow the naming convention defined in `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md`.

### Per-Customer Resources Created by Provision-Customer.ps1

| Resource Type | Naming Pattern | Example (`acme`, `prod`) |
|--------------|----------------|--------------------------|
| Resource Group | `rg-spaarke-{customerId}-{env}` | `rg-spaarke-acme-prod` |
| Storage Account | `sprk{customerId}{env}sa` | `sprkacmeprodsa` |
| Key Vault | `sprk-{customerId}-{env}-kv` | `sprk-acme-prod-kv` |
| Service Bus | `spaarke-{customerId}-{env}-sb` | `spaarke-acme-prod-sb` |
| Redis Cache | `spaarke-{customerId}-{env}-cache` | `spaarke-acme-prod-cache` |
| Dataverse Env | `spaarke-{customerId}` | `spaarke-acme` |
| Dataverse URL | `https://spaarke-{customerId}.crm.dynamics.com` | `https://spaarke-acme.crm.dynamics.com` |

### Shared Platform Resources (Not Customer-Specific)

| Resource | Name |
|----------|------|
| Platform Resource Group | `rg-spaarke-platform-prod` |
| BFF API App Service | `spaarke-bff-prod` |
| BFF API URL | `https://api.spaarke.com` |
| Platform Key Vault | `sprk-platform-prod-kv` |
| Azure OpenAI | `spaarke-openai-prod` |
| AI Search | `spaarke-search-prod` |
| Document Intelligence | `spaarke-docintel-prod` |

### State and Log Files

| File | Path | Purpose |
|------|------|---------|
| Provisioning state | `logs/provisioning/provision-{customerId}-{env}.state.json` | Resumability tracking |
| Provisioning log | `logs/provision-{customerId}-{env}-{timestamp}.log` | Detailed execution log |
| Decommission log | `logs/decommission-{customerId}-{timestamp}.log` | Teardown audit trail |

---

*End of Customer Onboarding Runbook. For incident response procedures, see `INCIDENT-RESPONSE-PROCEDURES.md`. For secret rotation, see `SECRET-ROTATION-PROCEDURES.md`.*
