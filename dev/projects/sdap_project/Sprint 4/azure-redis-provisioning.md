# Azure Redis Cache Provisioning for Task 4.1

**Date:** October 2, 2025
**Status:** In Progress - Provider Registration
**Resource Group:** spe-infrastructure-westus2
**Location:** westus2

---

## Current Status

### Azure Subscription
- **Subscription:** Spaarke SPE Subscription 1
- **Subscription ID:** 484bc857-3802-427f-9ea5-ca47b43db0f0
- **Tenant ID:** a221a95e-6abc-4434-aecc-e48338a1b2f2
- **User:** ralph.schroeder@spaarke.com

### Existing Resources (spe-infrastructure-westus2)
- `spe-logs-dev-67e2xz` - Log Analytics Workspace
- `spe-plan-dev-67e2xz` - App Service Plan
- `spe-insights-dev-67e2xz` - Application Insights
- `spe-api-dev-67e2xz` - App Service (API)

### Provider Registration Status
- **Microsoft.Cache:** 🔄 **Registering** (in progress)
- Started: October 2, 2025
- Typical registration time: 5-10 minutes

---

## Provisioning Commands

### Step 1: Check Provider Registration Status
```bash
az provider show --namespace Microsoft.Cache --query "registrationState" --output tsv
```

**Expected Output:** `Registered` (when complete)

### Step 2: Provision Redis Cache (Run after registration completes)

**Redis Configuration:**
- **Name:** `spe-redis-dev-67e2xz`
- **Resource Group:** `spe-infrastructure-westus2`
- **Location:** `westus2`
- **SKU:** Basic C0 (250 MB)
- **Cost:** ~$16/month
- **TLS:** 1.2 minimum
- **Non-SSL Port:** Disabled (secure connections only)

**Command:**
```bash
az redis create \
  --name spe-redis-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --location westus2 \
  --sku Basic \
  --vm-size c0 \
  --minimum-tls-version 1.2 \
  --output json
```

**Expected Duration:** 15-20 minutes

---

## Step 3: Retrieve Connection String

Once Redis is provisioned, retrieve the connection details:

**Get Primary Access Key:**
```bash
az redis list-keys \
  --name spe-redis-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query primaryKey \
  --output tsv
```

**Get Hostname:**
```bash
az redis show \
  --name spe-redis-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query hostName \
  --output tsv
```

**Build Connection String:**
```
{hostname}:6380,password={primaryKey},ssl=True,abortConnect=False
```

**Example:**
```
spe-redis-dev-67e2xz.redis.cache.windows.net:6380,password=ABC123XYZ...,ssl=True,abortConnect=False
```

---

## Step 4: Configure App Service

### Option A: App Service Configuration (Recommended for Dev)
```bash
az webapp config appsettings set \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --settings \
  Redis__Enabled=true \
  Redis__ConnectionString="{connection-string-from-step-3}" \
  Redis__InstanceName="sdap-dev:"
```

### Option B: Azure Key Vault (Recommended for Production)

**Create Key Vault (if doesn't exist):**
```bash
az keyvault create \
  --name spe-keyvault-dev \
  --resource-group spe-infrastructure-westus2 \
  --location westus2
```

**Store Redis Connection String:**
```bash
az keyvault secret set \
  --vault-name spe-keyvault-dev \
  --name RedisConnectionString \
  --value "{connection-string-from-step-3}"
```

**Configure App Service to Reference Key Vault:**
```bash
# Get Key Vault secret URI
SECRET_URI=$(az keyvault secret show \
  --vault-name spe-keyvault-dev \
  --name RedisConnectionString \
  --query id \
  --output tsv)

# Configure App Service
az webapp config appsettings set \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --settings \
  Redis__Enabled=true \
  Redis__ConnectionString="@Microsoft.KeyVault(SecretUri=${SECRET_URI})" \
  Redis__InstanceName="sdap-dev:"
```

**Grant App Service Access to Key Vault:**
```bash
# Enable system-assigned managed identity
az webapp identity assign \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2

# Get the identity's principal ID
PRINCIPAL_ID=$(az webapp identity show \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query principalId \
  --output tsv)

# Grant access to Key Vault
az keyvault set-policy \
  --name spe-keyvault-dev \
  --object-id $PRINCIPAL_ID \
  --secret-permissions get list
```

---

## Step 5: Verify Configuration

### Check App Service Settings
```bash
az webapp config appsettings list \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query "[?name=='Redis__Enabled' || name=='Redis__ConnectionString' || name=='Redis__InstanceName']" \
  --output table
```

### Check Application Logs
```bash
az webapp log tail \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2
```

**Expected Log Entry:**
```
info: Distributed cache: Redis enabled with instance name 'sdap-dev:'
```

---

## Step 6: Test Redis Connectivity

### Test from Azure CLI
```bash
# Install redis-cli (if needed)
# Windows: Download from https://github.com/microsoftarchive/redis/releases
# Linux/Mac: sudo apt-get install redis-tools or brew install redis

# Get connection details
REDIS_HOST=$(az redis show \
  --name spe-redis-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query hostName \
  --output tsv)

REDIS_KEY=$(az redis list-keys \
  --name spe-redis-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query primaryKey \
  --output tsv)

# Test connection
redis-cli -h $REDIS_HOST -p 6380 -a $REDIS_KEY --tls ping
```

**Expected Response:** `PONG`

### Test from Application
1. Deploy updated application with Redis configuration
2. Submit a test job with a known JobId
3. Submit the same job again (should be blocked by idempotency)
4. Check logs for: `"Job {JobId} already processed"`

---

## Troubleshooting

### Provider Registration Taking Too Long
```bash
# Check all registered providers
az provider list --query "[?namespace=='Microsoft.Cache']" --output table

# If stuck after 15 minutes, try re-registering
az provider unregister --namespace Microsoft.Cache
az provider register --namespace Microsoft.Cache
```

### Redis Creation Fails
- **Check Quota:** Ensure subscription has available quota for Redis Cache
- **Check Region:** Verify `westus2` supports Basic tier Redis
- **Check Naming:** Redis names must be globally unique, may need to change suffix

### App Service Can't Connect to Redis
- **Check Firewall:** Redis may have firewall rules restricting access
- **Check TLS Version:** Ensure app uses TLS 1.2+
- **Check Connection String Format:** Must include `ssl=True` and port `6380`

### Key Vault Access Denied
- **Check Managed Identity:** Ensure App Service has system-assigned identity enabled
- **Check Access Policy:** Verify identity has `get` and `list` permissions on secrets
- **Check Network Rules:** Key Vault may have network restrictions

---

## Cost Estimation

### Azure Redis Cache (Basic C0)
- **Monthly Cost:** ~$16 USD
- **Size:** 250 MB
- **Connections:** 256 concurrent
- **Bandwidth:** 10 Mbps

### Upgrade Path
For production, consider upgrading to:
- **Standard C1:** ~$58/month (1 GB, replication, 99.9% SLA)
- **Premium P1:** ~$450/month (6 GB, clustering, persistence)

---

## Monitoring

### Key Metrics to Monitor
```bash
# Get Redis metrics
az monitor metrics list \
  --resource spe-redis-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --resource-type Microsoft.Cache/redis \
  --metric-names \
    connectedclients \
    totalcommandsprocessed \
    cachehits \
    cachemisses \
    usedmemory \
    evictedkeys
```

### Application Insights Queries
```kusto
// Cache hit rate
customMetrics
| where name == "CacheHitRate"
| summarize avg(value) by bin(timestamp, 1h)

// Redis connection errors
exceptions
| where outerMessage contains "Redis" or outerMessage contains "StackExchange"
| summarize count() by bin(timestamp, 1h), outerMessage
```

---

## Rollback Plan

If Redis causes issues:

1. **Disable Redis via Configuration:**
   ```bash
   az webapp config appsettings set \
     --name spe-api-dev-67e2xz \
     --resource-group spe-infrastructure-westus2 \
     --settings Redis__Enabled=false
   ```

2. **Restart App Service:**
   ```bash
   az webapp restart \
     --name spe-api-dev-67e2xz \
     --resource-group spe-infrastructure-westus2
   ```

3. **Delete Redis Cache (if needed):**
   ```bash
   az redis delete \
     --name spe-redis-dev-67e2xz \
     --resource-group spe-infrastructure-westus2 \
     --yes
   ```

---

## Next Steps

Once Redis provisioning is complete:

1. ✅ Verify provider registration: `az provider show --namespace Microsoft.Cache`
2. ⏳ Create Redis Cache (15-20 min)
3. ⏳ Retrieve connection string
4. ⏳ Configure App Service settings
5. ⏳ Restart App Service
6. ⏳ Verify logs show "Redis enabled"
7. ⏳ Test idempotency with duplicate job submission
8. ⏳ Monitor cache hit/miss metrics

---

## Documentation References

- [Azure Redis Cache Documentation](https://learn.microsoft.com/en-us/azure/azure-cache-for-redis/)
- [Azure CLI Redis Commands](https://learn.microsoft.com/en-us/cli/azure/redis)
- [StackExchange.Redis Configuration](https://stackexchange.github.io/StackExchange.Redis/Configuration.html)
- [Task 4.1 Implementation Guide](TASK-4.1-DISTRIBUTED-CACHE-FIX.md)

---

**Created:** October 2, 2025
**Last Updated:** October 2, 2025
**Status:** Waiting for Microsoft.Cache provider registration to complete
