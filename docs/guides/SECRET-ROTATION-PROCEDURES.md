# Secret Rotation Procedures

> **Last Updated**: March 2026
>
> **Applies To**: Spaarke Production Environment
>
> **Automation Script**: `scripts/Rotate-Secrets.ps1`

---

## Overview

Secret rotation is the process of regenerating credentials and updating their stored values in Azure Key Vault. Regular rotation limits the window of exposure if a secret is compromised and satisfies compliance requirements.

All Spaarke secrets are stored in Azure Key Vault (per FR-08 — zero plaintext secrets). The BFF API references secrets via Key Vault references in App Service configuration, so rotating a secret in the vault is sufficient; the application picks up the new value on next Key Vault reference refresh or App Service restart.

---

## Secret Inventory

### Platform Secrets (sprk-platform-{env}-kv)

| Secret Name | Type | Source Resource | Rotation Method |
|-------------|------|-----------------|-----------------|
| `BFF-API-ClientSecret` | Entra ID client secret | Entra ID app registration | Automated (`Rotate-Secrets.ps1 -SecretType EntraId`) |
| `ServiceBus-ConnectionString` | Service Bus access key | sprk-platform-{env}-sb | Automated (`Rotate-Secrets.ps1 -SecretType ServiceBus`) |
| `Redis-ConnectionString` | Redis access key | sprk-platform-{env}-redis | Automated (`Rotate-Secrets.ps1 -SecretType Redis`) |
| `BFF-API-ClientId` | Entra ID application ID | Entra ID app registration | Not rotated (immutable identifier) |

### Customer Secrets (sprk-{customerId}-{env}-kv)

| Secret Name | Type | Source Resource | Rotation Method |
|-------------|------|-----------------|-----------------|
| `Storage-ConnectionString` | Storage account key | sprk{customerId}{env}sa | Automated (`Rotate-Secrets.ps1 -SecretType StorageKey`) |
| `ServiceBus-ConnectionString` | Service Bus access key | sprk-{customerId}-{env}-sb | Automated (`Rotate-Secrets.ps1 -SecretType ServiceBus`) |
| `Redis-ConnectionString` | Redis access key | sprk-{customerId}-{env}-redis | Automated (`Rotate-Secrets.ps1 -SecretType Redis`) |

### Secrets Not Managed by Rotate-Secrets.ps1

| Secret | Location | Rotation Method |
|--------|----------|-----------------|
| Azure-managed SSL certificates | App Service (api.spaarke.com) | Auto-renewed by Azure |
| Managed identity credentials | App Service system-assigned identity | Auto-rotated by Azure (no manual action) |
| GitHub Actions secrets | GitHub repository settings | Manual — see [Manual Procedures](#manual-procedures) |
| Dataverse S2S app secret | sprk-platform-{env}-kv (`Dataverse-S2S-ClientSecret`) | Automated (`Rotate-Secrets.ps1 -SecretType EntraId` after adding to platform orchestration) |

---

## Rotation Schedule

| Secret Type | Frequency | Automated | Notes |
|-------------|-----------|-----------|-------|
| Entra ID client secrets | Every 90 days | Yes | Created with 12-month expiry; rotate well before expiry |
| Storage account keys | Every 90 days | Yes | Zero-downtime via secondary/primary key swap |
| Service Bus access keys | Every 90 days | Yes | Zero-downtime via secondary/primary key swap |
| Redis access keys | Every 90 days | Yes | Zero-downtime via secondary/primary key swap |
| GitHub Actions secrets | Every 180 days | No | Manual update in GitHub repository settings |

**Recommended calendar**:

| Month | Week | Action |
|-------|------|--------|
| January | Week 2 | Rotate all platform + customer secrets |
| April | Week 2 | Rotate all platform + customer secrets |
| July | Week 2 | Rotate all platform + customer secrets |
| October | Week 2 | Rotate all platform + customer secrets |
| January, July | Week 3 | Rotate GitHub Actions secrets (semi-annual) |

---

## Prerequisites

Before running any rotation procedure:

1. **Azure CLI authenticated** with sufficient permissions:
   ```powershell
   az login
   az account show   # Verify correct subscription
   ```

2. **Required RBAC roles**:
   - **Key Vault Secrets Officer** on all target Key Vaults
   - **Contributor** on target resources (storage accounts, Service Bus, Redis)
   - **Application Administrator** (Entra ID) for client secret rotation

3. **Script location**: `scripts/Rotate-Secrets.ps1` in the repository root

---

## Automated Rotation via Rotate-Secrets.ps1

### Script Parameters

| Parameter | Required | Values | Default | Description |
|-----------|----------|--------|---------|-------------|
| `-Scope` | Yes | `Platform`, `Customer`, `All` | — | Which vaults to rotate |
| `-SecretType` | Yes | `StorageKey`, `ServiceBus`, `Redis`, `EntraId`, `All` | — | Which secret type to rotate |
| `-CustomerId` | Conditional | String (e.g., `demo`) | — | Required when Scope is `Customer` |
| `-Environment` | No | `dev`, `staging`, `prod` | `prod` | Target environment |
| `-DryRun` | No | Switch | Off | Preview changes without executing |
| `-Force` | No | Switch | Off | Skip confirmation prompts |
| `-LogPath` | No | File path | `./logs/secret-rotation-{timestamp}.log` | Custom audit log location |

### Procedure: Scheduled Full Rotation

**Use case**: Quarterly rotation of all secrets across all vaults.

**Step 1 — Preview changes (dry run)**:
```powershell
.\scripts\Rotate-Secrets.ps1 -Scope All -SecretType All -DryRun
```
Review the output. Confirm the list of secrets and vaults matches expectations.

**Step 2 — Execute rotation**:
```powershell
.\scripts\Rotate-Secrets.ps1 -Scope All -SecretType All
```
The script will prompt for confirmation. Type `yes` to proceed.

**Step 3 — Review audit log**:
```powershell
# Log location is printed at the end of the script output
Get-Content .\logs\secret-rotation-*.log | Select-Object -Last 30
```
Verify all entries show `Success`. Investigate any `Failed` entries.

**Step 4 — Verify application health**:
The script automatically restarts the App Service and runs a health check after platform rotation. Confirm manually:
```powershell
curl https://api.spaarke.com/healthz
# Expected: 200 OK — "Healthy"
```

**Step 5 — Notify the team**:
Post in the operations channel confirming rotation completed with date and audit log reference.

### Procedure: Rotate Platform Secrets Only

```powershell
# Dry run
.\scripts\Rotate-Secrets.ps1 -Scope Platform -SecretType All -DryRun

# Execute
.\scripts\Rotate-Secrets.ps1 -Scope Platform -SecretType All
```

### Procedure: Rotate a Single Customer

```powershell
# Dry run for demo customer
.\scripts\Rotate-Secrets.ps1 -Scope Customer -CustomerId demo -SecretType All -DryRun

# Execute for demo customer
.\scripts\Rotate-Secrets.ps1 -Scope Customer -CustomerId demo -SecretType All
```

### Procedure: Rotate a Single Secret Type

```powershell
# Rotate only Redis keys for platform
.\scripts\Rotate-Secrets.ps1 -Scope Platform -SecretType Redis

# Rotate only storage keys for a customer
.\scripts\Rotate-Secrets.ps1 -Scope Customer -CustomerId demo -SecretType StorageKey
```

---

## How the Automated Rotation Works

The script follows a zero-downtime rotation pattern for key-based secrets (Storage, Service Bus, Redis):

```
1. Regenerate SECONDARY key at the Azure resource
   (application is still using the primary key — no disruption)

2. Update Key Vault secret with the new secondary key value
   (Key Vault now has the secondary key; app picks it up on next refresh)

3. Verify connectivity using the new key

4. Regenerate PRIMARY key to invalidate the old value
   (old key is now unusable — security exposure window closed)
```

For Entra ID client secrets, the pattern differs:

```
1. Create a NEW client secret with a 12-month expiry
2. Update Key Vault with the new secret value
3. Verify the new credential exists on the app registration
4. Remove ALL old client secrets from the app registration
```

After rotation, the script restarts the App Service to force Key Vault reference refresh, then runs a health check against `/healthz`.

---

## Manual Procedures

### Rotate GitHub Actions Secrets

GitHub Actions secrets are used in CI/CD workflows and cannot be rotated by `Rotate-Secrets.ps1`.

**When**: Semi-annually (January, July) or after any personnel change.

**Secrets to rotate**:

| GitHub Secret | Source | How to Get New Value |
|---------------|--------|---------------------|
| `AZURE_CREDENTIALS` | Azure service principal | `az ad sp credential reset --id <sp-app-id>` |
| `PAC_AUTH_PROFILE` | Power Platform CLI auth | `pac auth create` and export profile |

**Steps**:

1. Generate a new credential (see "How to Get New Value" column above).
2. Navigate to the GitHub repository **Settings > Secrets and variables > Actions**.
3. Click the secret name, then **Update secret**.
4. Paste the new value and save.
5. Trigger a test workflow run to verify the new secret works:
   ```bash
   gh workflow run sdap-ci.yml
   ```
6. Monitor the workflow run for success.

### Rotate Dataverse S2S Client Secret

If the Dataverse S2S app registration (`spaarke-dataverse-s2s-prod`) is not included in the platform Entra ID rotation, rotate manually:

1. Go to **Azure Portal > Entra ID > App registrations > spaarke-dataverse-s2s-prod**.
2. Navigate to **Certificates & secrets > Client secrets**.
3. Click **New client secret**, set expiry to 12 months.
4. Copy the new secret value immediately (it will not be shown again).
5. Update Key Vault:
   ```powershell
   az keyvault secret set `
       --vault-name sprk-platform-prod-kv `
       --name "Dataverse-S2S-ClientSecret" `
       --value "<new-secret-value>"
   ```
6. Restart the App Service:
   ```powershell
   az webapp restart --name spaarke-bff-prod --resource-group rg-spaarke-platform-prod
   ```
7. Verify health: `curl https://api.spaarke.com/healthz`

---

## Emergency Rotation Procedure

**Use when**: A secret may have been compromised, exposed in logs, or an employee with access has departed.

### Immediate Actions (within 1 hour)

**Step 1 — Identify the scope of exposure**:
- Which secret(s) were exposed?
- Which vault(s) contain them?
- Platform-level, customer-level, or both?

**Step 2 — Rotate the compromised secret immediately**:
```powershell
# Example: Storage key for demo customer was exposed
.\scripts\Rotate-Secrets.ps1 -Scope Customer -CustomerId demo -SecretType StorageKey -Force

# Example: Platform Entra ID secret was exposed
.\scripts\Rotate-Secrets.ps1 -Scope Platform -SecretType EntraId -Force

# Example: Everything — rotate all secrets across all vaults
.\scripts\Rotate-Secrets.ps1 -Scope All -SecretType All -Force
```

Use `-Force` to skip confirmation prompts during an emergency.

**Step 3 — Verify the application is healthy**:
```powershell
curl https://api.spaarke.com/healthz
```

If the health check fails, check the audit log for rotation failures:
```powershell
Get-Content .\logs\secret-rotation-*.log | Where-Object { $_ -match "ERROR|Failed" }
```

**Step 4 — Check for unauthorized access**:
- Review Azure Activity Log for the affected resources:
  ```powershell
  az monitor activity-log list --resource-group rg-spaarke-platform-prod --start-time (Get-Date).AddDays(-7).ToString("yyyy-MM-dd") --query "[?authorization.action=='Microsoft.KeyVault/vaults/secrets/getSecret/action']"
  ```
- Review Key Vault diagnostic logs for unusual access patterns.
- Review Entra ID sign-in logs for the affected app registration.

### Follow-up Actions (within 24 hours)

1. **Document the incident**: Create an incident report with timeline, affected secrets, rotation actions taken, and root cause (if known).
2. **Review access**: Audit who has access to the compromised secrets and revoke any unnecessary permissions.
3. **Rotate adjacent secrets**: If one secret was compromised, consider whether related secrets may also be at risk.
4. **Update monitoring**: Add alerts for the access pattern that led to exposure (if applicable).

---

## Verification Steps After Rotation

After any rotation (scheduled or emergency), verify the following:

### 1. Application Health Check

```powershell
# Basic health
curl https://api.spaarke.com/healthz
# Expected: 200 — "Healthy"

# Dataverse connectivity
curl https://api.spaarke.com/healthz/dataverse
# Expected: 200 — "healthy"
```

### 2. Key Vault Secret Versions

Confirm the secret was updated (newest version should be recent):

```powershell
az keyvault secret show --vault-name sprk-platform-prod-kv --name "BFF-API-ClientSecret" --query "attributes.updated" -o tsv
```

### 3. Audit Log Review

Every rotation creates an audit log in `./logs/`:

```powershell
# Find the most recent log
$latestLog = Get-ChildItem .\logs\secret-rotation-*.log | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Get-Content $latestLog.FullName
```

The log contains a JSON summary at the end with counts of succeeded, failed, and skipped operations. All rotations should show `Succeeded > 0` and `Failed == 0`.

### 4. Service-Specific Verification

| Secret Type | Verification Command |
|-------------|---------------------|
| Storage | `az storage container list --account-name sprk{cid}{env}sa --auth-mode login` |
| Service Bus | `az servicebus namespace show --name sprk-{cid}-{env}-sb --resource-group rg-spaarke-{cid}-{env} --query status` |
| Redis | `az redis show --name sprk-{cid}-{env}-redis --resource-group rg-spaarke-{cid}-{env} --query provisioningState` |
| Entra ID | `az ad app credential list --id <app-id> --query "[].{name:displayName, expiry:endDateTime}"` |

---

## Troubleshooting

### Rotation Fails with "Cannot access Key Vault"

**Cause**: The operator does not have the **Key Vault Secrets Officer** role.

**Fix**:
```powershell
# Grant Key Vault Secrets Officer role
az role assignment create `
    --role "Key Vault Secrets Officer" `
    --assignee <operator-email> `
    --scope /subscriptions/<sub-id>/resourceGroups/<rg>/providers/Microsoft.KeyVault/vaults/<vault-name>
```

### Rotation Fails with "Failed to regenerate key"

**Cause**: The operator does not have **Contributor** role on the target resource.

**Fix**:
```powershell
az role assignment create `
    --role "Contributor" `
    --assignee <operator-email> `
    --scope /subscriptions/<sub-id>/resourceGroups/<rg>/providers/Microsoft.Storage/storageAccounts/<account-name>
```

### App Service Unhealthy After Rotation

**Cause**: Key Vault reference has not refreshed yet.

**Fix**:
1. Restart the App Service manually:
   ```powershell
   az webapp restart --name spaarke-bff-prod --resource-group rg-spaarke-platform-prod
   ```
2. Wait 30 seconds, then re-check `/healthz`.
3. If still unhealthy, check App Service logs:
   ```powershell
   az webapp log tail --name spaarke-bff-prod --resource-group rg-spaarke-platform-prod
   ```

### Entra ID Rotation Fails with "Insufficient privileges"

**Cause**: The operator does not have the **Application Administrator** directory role.

**Fix**: An Entra ID Global Administrator must assign the Application Administrator role to the operator, or perform the rotation themselves.

---

## Audit and Compliance

### Audit Log Retention

- Rotation audit logs are written to `./logs/secret-rotation-{timestamp}.log`.
- Retain logs for at least **12 months** for compliance.
- Archive older logs to Azure Blob Storage or a central logging system.

### Compliance Evidence

For audit requests, provide:
1. **Rotation schedule**: This document (rotation frequency table above).
2. **Rotation execution records**: Audit logs from `./logs/` directory.
3. **Key Vault secret versions**: `az keyvault secret list-versions --vault-name <vault> --name <secret>` shows the full rotation history with timestamps.

---

*Procedure maintained by the Spaarke platform team. Review and update quarterly alongside the rotation schedule.*
