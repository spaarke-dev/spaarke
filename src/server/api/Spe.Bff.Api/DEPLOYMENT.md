# Deployment Guide - SDAP BFF API

## Prerequisites

### 1. Azure Resources Required

- **Resource Group**: `rg-sdap-{environment}`
- **App Service**: `app-sdap-bff-{environment}`
- **Service Bus Namespace**: `sb-sdap-{environment}`
- **Service Bus Queue**: `sdap-jobs` or `document-events`
- **Azure Cache for Redis**: `redis-sdap-{environment}` (Standard tier recommended for production)
- **User-Assigned Managed Identity**: `mi-sdap-{environment}`
- **Key Vault**: `kv-sdap-{environment}`
- **Application Insights**: `ai-sdap-{environment}`

### 2. App Registrations Required

#### A. BFF API App Registration
- **Name**: `SDAP-BFF-API-{environment}`
- **Redirect URIs**: Not required (backend API)
- **API Permissions**:
  - Microsoft Graph: `Files.Read.All`, `Files.ReadWrite.All` (Application)
  - Dynamics CRM: `user_impersonation` (Delegated)
- **Expose an API**: `api://sdap-bff-api`
- **App Roles**: Define custom roles if needed
- **Secrets**: Create client secret, store in Key Vault

#### B. Dataverse App Registration
- **Name**: `SDAP-Dataverse-Client-{environment}`
- **API Permissions**:
  - Dynamics CRM: `user_impersonation` (Delegated)
- **Secrets**: Create client secret, store in Key Vault

## Step-by-Step Deployment

### Step 1: Create Azure Resources

```bash
# Set variables
ENVIRONMENT="dev"  # or staging, prod
LOCATION="eastus"
RG_NAME="rg-sdap-$ENVIRONMENT"
SUBSCRIPTION_ID="your-subscription-id"

# Create resource group
az group create --name $RG_NAME --location $LOCATION

# Create Service Bus
az servicebus namespace create --name "sb-sdap-$ENVIRONMENT" --resource-group $RG_NAME --location $LOCATION --sku Standard
az servicebus queue create --name "document-events" --namespace-name "sb-sdap-$ENVIRONMENT" --resource-group $RG_NAME

# Create Redis (Optional - only for staging/production)
az redis create --name "redis-sdap-$ENVIRONMENT" --resource-group $RG_NAME --location $LOCATION --sku Standard --vm-size C1

# Create User-Assigned Managed Identity
az identity create --name "mi-sdap-$ENVIRONMENT" --resource-group $RG_NAME

# Get Managed Identity details (save these)
MI_CLIENT_ID=$(az identity show --name "mi-sdap-$ENVIRONMENT" --resource-group $RG_NAME --query clientId -o tsv)
MI_PRINCIPAL_ID=$(az identity show --name "mi-sdap-$ENVIRONMENT" --resource-group $RG_NAME --query principalId -o tsv)

echo "Managed Identity Client ID: $MI_CLIENT_ID"
echo "Managed Identity Principal ID: $MI_PRINCIPAL_ID"

# Create Key Vault
az keyvault create --name "kv-sdap-$ENVIRONMENT" --resource-group $RG_NAME --location $LOCATION

# Grant Managed Identity access to Key Vault
az keyvault set-policy --name "kv-sdap-$ENVIRONMENT" --object-id $MI_PRINCIPAL_ID --secret-permissions get list

# Create App Service Plan
az appservice plan create --name "plan-sdap-$ENVIRONMENT" --resource-group $RG_NAME --sku B1 --is-linux

# Create App Service
az webapp create --name "app-sdap-bff-$ENVIRONMENT" --resource-group $RG_NAME --plan "plan-sdap-$ENVIRONMENT" --runtime "DOTNETCORE:8.0"

# Assign Managed Identity to App Service
az webapp identity assign --name "app-sdap-bff-$ENVIRONMENT" --resource-group $RG_NAME --identities "/subscriptions/$SUBSCRIPTION_ID/resourcegroups/$RG_NAME/providers/Microsoft.ManagedIdentity/userAssignedIdentities/mi-sdap-$ENVIRONMENT"

# Create Application Insights
az monitor app-insights component create --app "ai-sdap-$ENVIRONMENT" --location $LOCATION --resource-group $RG_NAME --application-type web
```

### Step 2: Configure App Registrations

1. **Create BFF API App Registration**:
   - Portal: Azure AD → App Registrations → New Registration
   - Name: `SDAP-BFF-API-{environment}`
   - Create client secret, copy value
   - Configure API permissions (Graph, Dataverse)
   - Grant admin consent for permissions

2. **Create Dataverse App Registration** (if separate):
   - Same process as above
   - Focus on Dataverse permissions only

### Step 3: Store Secrets in Key Vault

```bash
# BFF API secrets
az keyvault secret set --vault-name "kv-sdap-$ENVIRONMENT" --name "Graph-ClientSecret" --value "{your-bff-api-secret}"
az keyvault secret set --vault-name "kv-sdap-$ENVIRONMENT" --name "Dataverse-ClientSecret" --value "{your-dataverse-secret}"

# Service Bus connection string
SB_CONN_STRING=$(az servicebus namespace authorization-rule keys list --resource-group $RG_NAME --namespace-name "sb-sdap-$ENVIRONMENT" --name RootManageSharedAccessKey --query primaryConnectionString -o tsv)
az keyvault secret set --vault-name "kv-sdap-$ENVIRONMENT" --name "ServiceBus-ConnectionString" --value "$SB_CONN_STRING"

# Redis connection string (if enabled)
if [ "$ENVIRONMENT" != "dev" ]; then
  REDIS_KEY=$(az redis list-keys --name "redis-sdap-$ENVIRONMENT" --resource-group $RG_NAME --query primaryKey -o tsv)
  REDIS_CONN_STRING="redis-sdap-$ENVIRONMENT.redis.cache.windows.net:6380,password=$REDIS_KEY,ssl=True,abortConnect=False"
  az keyvault secret set --vault-name "kv-sdap-$ENVIRONMENT" --name "Redis-ConnectionString" --value "$REDIS_CONN_STRING"
fi
```

### Step 4: Configure App Service Settings

```bash
# Set Key Vault reference configuration
az webapp config appsettings set --name "app-sdap-bff-$ENVIRONMENT" --resource-group $RG_NAME --settings \
  "Graph__TenantId={your-tenant-id}" \
  "Graph__ClientId={bff-api-client-id}" \
  "Graph__ClientSecret=@Microsoft.KeyVault(SecretUri=https://kv-sdap-$ENVIRONMENT.vault.azure.net/secrets/Graph-ClientSecret/)" \
  "Graph__ManagedIdentity__Enabled=true" \
  "Graph__ManagedIdentity__ClientId=$MI_CLIENT_ID" \
  "Graph__Scopes__0=https://graph.microsoft.com/.default" \
  "Dataverse__EnvironmentUrl=https://your-env.crm.dynamics.com" \
  "Dataverse__ClientId={dataverse-client-id}" \
  "Dataverse__ClientSecret=@Microsoft.KeyVault(SecretUri=https://kv-sdap-$ENVIRONMENT.vault.azure.net/secrets/Dataverse-ClientSecret/)" \
  "Dataverse__TenantId={your-tenant-id}" \
  "ServiceBus__ConnectionString=@Microsoft.KeyVault(SecretUri=https://kv-sdap-$ENVIRONMENT.vault.azure.net/secrets/ServiceBus-ConnectionString/)" \
  "ServiceBus__QueueName=document-events" \
  "ServiceBus__MaxConcurrentCalls=5" \
  "Redis__Enabled=true" \
  "Redis__ConnectionString=@Microsoft.KeyVault(SecretUri=https://kv-sdap-$ENVIRONMENT.vault.azure.net/secrets/Redis-ConnectionString/)" \
  "Redis__InstanceName=sdap:" \
  "Authorization__Enabled=true"
```

### Step 5: Grant Graph API Permissions to Managed Identity

```powershell
# PowerShell script to grant Graph permissions to Managed Identity
Connect-MgGraph -Scopes "Application.ReadWrite.All", "AppRoleAssignment.ReadWrite.All"

$graphAppId = "00000003-0000-0000-c000-000000000000"  # Microsoft Graph
$miObjectId = "{managed-identity-principal-id}"

$graphServicePrincipal = Get-MgServicePrincipal -Filter "appId eq '$graphAppId'"

# Grant Files.Read.All
$appRole = $graphServicePrincipal.AppRoles | Where-Object { $_.Value -eq "Files.Read.All" }
New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $miObjectId -PrincipalId $miObjectId -AppRoleId $appRole.Id -ResourceId $graphServicePrincipal.Id

# Grant Files.ReadWrite.All
$appRole = $graphServicePrincipal.AppRoles | Where-Object { $_.Value -eq "Files.ReadWrite.All" }
New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $miObjectId -PrincipalId $miObjectId -AppRoleId $appRole.Id -ResourceId $graphServicePrincipal.Id
```

### Step 6: Deploy Application

```bash
# Build and publish
dotnet publish src/api/Spe.Bff.Api/Spe.Bff.Api.csproj -c Release -o ./publish

# Create deployment package
cd publish
zip -r ../deploy.zip .
cd ..

# Deploy to App Service
az webapp deployment source config-zip --name "app-sdap-bff-$ENVIRONMENT" --resource-group $RG_NAME --src deploy.zip
```

### Step 7: Verify Deployment

1. **Check application logs**:
   ```bash
   az webapp log tail --name "app-sdap-bff-$ENVIRONMENT" --resource-group $RG_NAME
   ```

2. **Look for startup validation success**:
   ```
   [Information] Starting configuration validation...
   [Information] ✅ Configuration validation successful
   [Information] Configuration Summary:
   [Information]   Graph API:
   [Information]     - TenantId: a221...
   [Information]     - ManagedIdentity: True
   ```

3. **Test health endpoint**:
   ```bash
   curl https://app-sdap-bff-{environment}.azurewebsites.net/health
   ```

4. **Test ping endpoint**:
   ```bash
   curl https://app-sdap-bff-{environment}.azurewebsites.net/api/ping
   ```

## Environment-Specific Configuration

### Development
- **Redis**: Disabled (in-memory cache)
- **Service Bus**: Lower concurrency (2)
- **Authorization**: Can be disabled for testing
- **Logging**: Debug level
- **Managed Identity**: Disabled (uses client secret)

### Staging
- **Redis**: Enabled (Standard tier)
- **Service Bus**: Medium concurrency (5)
- **Authorization**: Enabled
- **Logging**: Information level
- **Managed Identity**: Enabled

### Production
- **Redis**: Enabled (Standard tier or higher)
- **Service Bus**: Higher concurrency (10+)
- **Authorization**: Enabled (strict)
- **Logging**: Warning level (structured)
- **Managed Identity**: Required
- **High Availability**: Consider multiple instances

## Troubleshooting

### Configuration Validation Fails

**Error**: `Configuration validation failed. Application cannot start.`

**Solutions**:
- Check application logs for specific validation errors
- Verify all Key Vault references are correct
- Ensure Managed Identity has access to Key Vault
- Check that all required configuration sections exist

### Graph API Errors

**Error**: `Insufficient permissions for Graph API`

**Solutions**:
- Verify Managed Identity has Graph permissions (Files.Read.All, Files.ReadWrite.All)
- Check Graph client configuration (TenantId, ClientId)
- Verify UAMI Client ID is correct in configuration

### Dataverse Connection Fails

**Error**: `Failed to connect to Dataverse`

**Solutions**:
- Verify Dataverse app registration permissions
- Check Dataverse URL is correct (no trailing slash in EnvironmentUrl)
- Verify client secret is valid and not expired
- Ensure Dataverse environment is accessible

### Service Bus Connection Issues

**Error**: `Failed to connect to Service Bus`

**Solutions**:
- Verify Service Bus connection string is correct
- Check queue name matches configuration
- Ensure network rules allow App Service to access Service Bus

### Application Won't Start

**Common Causes**:
1. Missing configuration values → Check logs for validation errors
2. Invalid Key Vault references → Verify secret URIs
3. Managed Identity not assigned → Check App Service identity settings
4. Missing app permissions → Grant admin consent in Azure AD

## Rollback Plan

1. **Stop App Service**:
   ```bash
   az webapp stop --name "app-sdap-bff-$ENVIRONMENT" --resource-group $RG_NAME
   ```

2. **Revert to previous deployment**:
   ```bash
   # If using deployment slots
   az webapp deployment slot swap --name "app-sdap-bff-$ENVIRONMENT" --resource-group $RG_NAME --slot staging --target-slot production

   # Or redeploy previous version
   az webapp deployment source config-zip --name "app-sdap-bff-$ENVIRONMENT" --resource-group $RG_NAME --src previous-deploy.zip
   ```

3. **Verify health endpoint**:
   ```bash
   curl https://app-sdap-bff-{environment}.azurewebsites.net/health
   ```

4. **Resume traffic**:
   ```bash
   az webapp start --name "app-sdap-bff-$ENVIRONMENT" --resource-group $RG_NAME
   ```

## Monitoring & Observability

### Application Insights

Configure Application Insights connection string in App Service:

```bash
AI_CONN_STRING=$(az monitor app-insights component show --app "ai-sdap-$ENVIRONMENT" --resource-group $RG_NAME --query connectionString -o tsv)

az webapp config appsettings set --name "app-sdap-bff-$ENVIRONMENT" --resource-group $RG_NAME --settings \
  "APPLICATIONINSIGHTS_CONNECTION_STRING=$AI_CONN_STRING"
```

### Key Metrics to Monitor

- **Request rate** (requests/minute)
- **Error rate** (5xx errors)
- **Response time** (P95, P99)
- **Dependency failures** (Graph API, Dataverse, Service Bus)
- **Authorization failures** (403 responses)
- **Configuration validation** (startup failures)

### Alerts

Create alerts for:
- High error rate (>5% 5xx responses)
- Slow response times (>2s P95)
- Dependency failures
- Application restarts (configuration validation failures)

## Security Checklist

- [ ] All secrets stored in Key Vault
- [ ] Managed Identity used for production
- [ ] No secrets in appsettings.json (committed to git)
- [ ] Least privilege permissions granted
- [ ] CORS properly configured (no AllowAnyOrigin in production)
- [ ] Authorization enabled in production
- [ ] HTTPS enforced
- [ ] Application Insights configured
- [ ] Network rules configured (if using VNet)

## Next Steps

After successful deployment:

1. Test authorization endpoints with real users
2. Validate Dataverse permission queries
3. Test file operations (upload, download, delete)
4. Monitor Application Insights for errors
5. Set up alerts for critical failures
6. Document any environment-specific configuration
7. Create runbook for common operational tasks
