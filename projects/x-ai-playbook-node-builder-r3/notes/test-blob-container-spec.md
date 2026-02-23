# Test Blob Container Specification

> **Project**: ai-playbook-node-builder-r3
> **Task**: 030 - Create Dedicated Blob Container for Playbook Tests
> **Status**: Specification Complete
> **Date**: 2026-01-19

---

## Overview

This specification defines the Azure Blob Storage container configuration required for playbook test execution in "Quick Test" mode. The container provides temporary storage for test documents with automatic 24-hour cleanup.

---

## 1. Container Configuration

### Container Details

| Property | Value |
|----------|-------|
| **Container Name** | `playbook-test-documents` |
| **Storage Account** | `sprkshareddevsa` (to be created) or use existing |
| **Resource Group** | `spe-infrastructure-westus2` |
| **Region** | West US 2 |
| **Access Level** | Private (no anonymous access) |
| **Purpose** | Temporary storage for playbook test document uploads |

### Alternative: Use Existing Storage Account

If a storage account already exists in the subscription, identify it with:

```bash
az storage account list \
  --resource-group spe-infrastructure-westus2 \
  --query "[].{name:name, location:location, sku:sku.name}" \
  --output table
```

---

## 2. Azure CLI Commands

### 2.1 Create Storage Account (if needed)

```bash
# Create storage account following naming convention: sprk{purpose}{env}sa
az storage account create \
  --name sprkshareddevsa \
  --resource-group spe-infrastructure-westus2 \
  --location westus2 \
  --sku Standard_LRS \
  --kind StorageV2 \
  --access-tier Hot \
  --https-only true \
  --allow-blob-public-access false \
  --min-tls-version TLS1_2

# Enable soft delete for blob recovery (7 days)
az storage account blob-service-properties update \
  --account-name sprkshareddevsa \
  --resource-group spe-infrastructure-westus2 \
  --enable-delete-retention true \
  --delete-retention-days 7
```

### 2.2 Create Container

```bash
# Create the test documents container
az storage container create \
  --name playbook-test-documents \
  --account-name sprkshareddevsa \
  --auth-mode login \
  --public-access off
```

### 2.3 Configure Lifecycle Management Policy

Create a lifecycle management policy for automatic 24-hour blob deletion:

```bash
# Create policy JSON file
cat > /tmp/lifecycle-policy.json << 'EOF'
{
  "rules": [
    {
      "enabled": true,
      "name": "delete-test-documents-after-24h",
      "type": "Lifecycle",
      "definition": {
        "actions": {
          "baseBlob": {
            "delete": {
              "daysAfterCreationGreaterThan": 1
            }
          }
        },
        "filters": {
          "blobTypes": ["blockBlob"],
          "prefixMatch": ["playbook-test-documents/"]
        }
      }
    }
  ]
}
EOF

# Apply the lifecycle policy
az storage account management-policy create \
  --account-name sprkshareddevsa \
  --resource-group spe-infrastructure-westus2 \
  --policy @/tmp/lifecycle-policy.json
```

**Note**: Azure lifecycle management runs once per day, so actual deletion may take up to 48 hours in practice. For stricter cleanup, consider implementing application-level deletion after test completion.

---

## 3. Authentication Options

### Option A: Connection String (Simple, for Development)

Store the connection string in Key Vault:

```bash
# Get the connection string
CONNECTION_STRING=$(az storage account show-connection-string \
  --name sprkshareddevsa \
  --resource-group spe-infrastructure-westus2 \
  --query connectionString \
  --output tsv)

# Store in Key Vault
az keyvault secret set \
  --vault-name spaarke-spekvcert \
  --name PlaybookTest-StorageConnectionString \
  --value "$CONNECTION_STRING"
```

### Option B: Managed Identity (Recommended for Production)

Grant the App Service managed identity access to the storage account:

```bash
# Get App Service managed identity principal ID
PRINCIPAL_ID=$(az webapp identity show \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query principalId \
  --output tsv)

# Get storage account resource ID
STORAGE_ID=$(az storage account show \
  --name sprkshareddevsa \
  --resource-group spe-infrastructure-westus2 \
  --query id \
  --output tsv)

# Assign Storage Blob Data Contributor role
az role assignment create \
  --assignee $PRINCIPAL_ID \
  --role "Storage Blob Data Contributor" \
  --scope $STORAGE_ID
```

---

## 4. BFF Configuration

### 4.1 appsettings.json Updates

Add the following section to `src/server/api/Sprk.Bff.Api/appsettings.template.json`:

```json
{
  "PlaybookTesting": {
    "Enabled": true,
    "StorageAccountName": "#{PLAYBOOK_TEST_STORAGE_ACCOUNT}#",
    "ContainerName": "playbook-test-documents",
    "ConnectionString": "@Microsoft.KeyVault(SecretUri=#{KEY_VAULT_URL}#secrets/PlaybookTest-StorageConnectionString)",
    "UseManagedIdentity": false,
    "MaxTestFileSizeMB": 10,
    "TestFileRetentionHours": 24
  }
}
```

### 4.2 Local Development (User Secrets)

For local development, use dotnet user-secrets:

```bash
cd src/server/api/Sprk.Bff.Api

# Set the connection string
dotnet user-secrets set "PlaybookTesting:ConnectionString" "DefaultEndpointsProtocol=https;AccountName=sprkshareddevsa;AccountKey=xxx;EndpointSuffix=core.windows.net"

# Or set storage account name for managed identity
dotnet user-secrets set "PlaybookTesting:StorageAccountName" "sprkshareddevsa"
dotnet user-secrets set "PlaybookTesting:UseManagedIdentity" "true"
```

### 4.3 Azure App Service Settings

```bash
# Using connection string
az webapp config appsettings set \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --settings \
    "PlaybookTesting__Enabled=true" \
    "PlaybookTesting__StorageAccountName=sprkshareddevsa" \
    "PlaybookTesting__ContainerName=playbook-test-documents" \
    "PlaybookTesting__ConnectionString=@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/PlaybookTest-StorageConnectionString/)" \
    "PlaybookTesting__UseManagedIdentity=false" \
    "PlaybookTesting__MaxTestFileSizeMB=10"

# OR using managed identity (recommended)
az webapp config appsettings set \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --settings \
    "PlaybookTesting__Enabled=true" \
    "PlaybookTesting__StorageAccountName=sprkshareddevsa" \
    "PlaybookTesting__ContainerName=playbook-test-documents" \
    "PlaybookTesting__UseManagedIdentity=true" \
    "PlaybookTesting__MaxTestFileSizeMB=10"
```

---

## 5. Verification Steps

### 5.1 Verify Container Creation

```bash
# List containers in storage account
az storage container list \
  --account-name sprkshareddevsa \
  --auth-mode login \
  --query "[].name" \
  --output table

# Verify lifecycle policy
az storage account management-policy show \
  --account-name sprkshareddevsa \
  --resource-group spe-infrastructure-westus2
```

### 5.2 Test Upload/Download

```bash
# Create test file
echo "Test content for playbook testing" > /tmp/test-document.txt

# Upload test blob
az storage blob upload \
  --account-name sprkshareddevsa \
  --container-name playbook-test-documents \
  --name "test-run-001/test-document.txt" \
  --file /tmp/test-document.txt \
  --auth-mode login

# List blobs
az storage blob list \
  --account-name sprkshareddevsa \
  --container-name playbook-test-documents \
  --auth-mode login \
  --query "[].name" \
  --output table

# Download to verify
az storage blob download \
  --account-name sprkshareddevsa \
  --container-name playbook-test-documents \
  --name "test-run-001/test-document.txt" \
  --file /tmp/downloaded-test.txt \
  --auth-mode login

# Cleanup test blob
az storage blob delete \
  --account-name sprkshareddevsa \
  --container-name playbook-test-documents \
  --name "test-run-001/test-document.txt" \
  --auth-mode login
```

### 5.3 Verify BFF Access

After configuration, test from the BFF API:

```bash
# Check health endpoint includes storage connectivity
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz

# Or run integration test (when implemented in Task 031)
```

---

## 6. Blob Naming Convention

Test documents should follow this naming pattern:

```
playbook-test-documents/
  {test-run-id}/
    {original-filename}
```

**Example**:
```
playbook-test-documents/
  run-20260119-abc123/
    sample-lease.pdf
    contract-addendum.docx
```

**Rationale**:
- Enables easy cleanup by test run ID
- Prevents filename collisions across concurrent tests
- Supports audit trail for test execution history

---

## 7. Security Considerations

| Aspect | Configuration |
|--------|--------------|
| **Access Level** | Private only - no anonymous access |
| **Authentication** | Managed Identity (prod) or Connection String (dev) |
| **Encryption** | Azure Storage encryption at rest (default) |
| **Network** | Consider VNet integration for production |
| **Soft Delete** | 7-day retention for accidental deletion recovery |
| **TLS** | Minimum TLS 1.2 |

---

## 8. Cost Estimate

| Item | Estimated Monthly Cost |
|------|----------------------|
| Storage (Hot tier, ~1GB) | ~$0.02/GB = $0.02 |
| Transactions (~10K/month) | ~$0.05 |
| Data transfer (minimal) | ~$0.01 |
| **Total** | **~$0.10/month** |

**Note**: Costs are minimal due to 24-hour lifecycle policy and limited test usage.

---

## 9. Implementation Checklist

- [ ] Create or identify storage account
- [ ] Create `playbook-test-documents` container
- [ ] Configure lifecycle management policy
- [ ] Store connection string in Key Vault (or configure managed identity)
- [ ] Update BFF appsettings.template.json
- [ ] Configure Azure App Service settings
- [ ] Verify upload/download operations
- [ ] Update BFF health check to include storage connectivity

---

## 10. Related Tasks

| Task | Dependency |
|------|-----------|
| **Task 031** | Implement Test Modes - will use this container |
| **Task 032** | Test Execution Endpoint - will orchestrate blob operations |

---

## References

- [Azure Blob Storage Lifecycle Management](https://docs.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-policy-configure)
- [Azure Storage Naming Convention](../../docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md)
- [Azure Resources Reference](../../docs/architecture/auth-azure-resources.md)
- [Spaarke Infrastructure](../../infrastructure/)

---

*Specification created: 2026-01-19*
*Ready for manual infrastructure creation*
