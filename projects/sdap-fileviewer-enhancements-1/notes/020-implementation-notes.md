# Task 020 Implementation Notes

## Date: December 4, 2025

## Summary

**Status: Manual Task Required**

No Bicep infrastructure-as-code files exist in this repository. The App Service "Always On" setting must be configured manually or through Azure CLI.

## Investigation

Searched for Bicep files in `/infrastructure/`:
- No `.bicep` files found
- Infrastructure is managed outside this repository or through portal/CLI

## Manual Configuration Options

### Option 1: Azure Portal
1. Navigate to Azure Portal → App Services → `spe-api-dev-67e2xz`
2. Settings → Configuration → General settings
3. Set "Always On" = **On**
4. Save

### Option 2: Azure CLI
```bash
# For Development environment
az webapp config set --name spe-api-dev-67e2xz --resource-group <resource-group> --always-on true

# Verify
az webapp config show --name spe-api-dev-67e2xz --resource-group <resource-group> --query "alwaysOn"
```

### Option 3: Create Bicep file (future)
If infrastructure-as-code is desired, create `infrastructure/bicep/modules/app-service.bicep`:

```bicep
resource webApp 'Microsoft.Web/sites@2022-03-01' = {
  name: appServiceName
  location: location
  properties: {
    serverFarmId: appServicePlan.id
    siteConfig: {
      alwaysOn: true
      // other config...
    }
  }
}
```

## Prerequisites

- App Service Plan must be **Basic tier or higher**
- Free and Shared tiers do not support Always On

## Verification

After configuration:
```bash
# Should return "true"
az webapp config show --name <app-name> --resource-group <rg> --query "alwaysOn"
```

## Recommendation

Enable Always On via Azure CLI for both environments:
- Development: `spe-api-dev-67e2xz`
- Production: Configure when available

## Acceptance Criteria

| Criterion | Status |
|-----------|--------|
| Always On enabled in Bicep | ⏸️ No Bicep in repo |
| bicep build succeeds | N/A |
| Azure Portal shows Always On = On | ⏸️ Manual required |
| Cold start < 3 seconds | ⏸️ Verify after config |

## Next Steps

- [ ] Coordinate with infrastructure team to enable Always On
- [ ] Consider adding Bicep IaC to repository for future automation
