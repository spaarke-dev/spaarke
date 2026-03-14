# Incident Response Procedures

> **Last Updated**: 2026-03-13
> **Owner**: Spaarke Operations
> **Scope**: Production Spaarke environments
> **Related**: [Production Deployment Guide](./CUSTOMER-DEPLOYMENT-GUIDE.md) | [Secret Rotation Procedures](./SECRET-ROTATION-PROCEDURES.md)

---

## 1. Severity Levels

| Level | Name | Response Time | Examples | Escalation |
|-------|------|--------------|----------|------------|
| **SEV-1** | Critical | 15 minutes | Production API down, data loss, security breach | Immediate page to on-call + engineering lead |
| **SEV-2** | High | 30 minutes | Major feature unavailable, AI services degraded, >50% users affected | Page on-call engineer |
| **SEV-3** | Medium | 4 hours | Minor feature broken, performance degradation, single customer affected | Slack notification, next business day if after hours |
| **SEV-4** | Low | 24 hours | Cosmetic issues, non-critical warnings, monitoring noise | Track in issue backlog |

### Severity Decision Tree

```
Is production completely down for all users?
  YES → SEV-1

Is a core workflow blocked (document upload, AI analysis, record creation)?
  YES, for >50% of users → SEV-1
  YES, for a single customer → SEV-2

Are AI services returning errors or timeouts?
  YES, all AI features broken → SEV-2
  YES, intermittent failures → SEV-3

Is performance noticeably degraded?
  YES, >5s response times → SEV-2
  YES, occasional slowness → SEV-3

Everything else → SEV-4
```

---

## 2. Common Failure Scenarios

### 2.1 BFF API Down (SEV-1)

**Symptoms**: `/healthz` returns non-200, users see connection errors, App Insights shows 5xx spike.

**Diagnosis**:
```bash
# 1. Check health endpoint
curl https://api.spaarke.com/healthz

# 2. Check App Service status
az webapp show --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod --query "state" -o tsv

# 3. Check recent deployments
az webapp deployment list --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod -o table

# 4. Stream live logs
az webapp log tail --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod

# 5. Check App Service plan (resource exhaustion)
az appservice plan show --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod-plan --query "{sku:sku.name,workers:numberOfWorkers,status:status}" -o json
```

**Resolution**:
1. If caused by a bad deployment: **Rollback via slot swap** (see Section 3.1)
2. If resource exhaustion: Restart the App Service, then investigate root cause
3. If infrastructure issue: Check Azure Status page, open support ticket if needed

```bash
# Restart App Service
az webapp restart --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod
```

---

### 2.2 Dataverse Unavailable (SEV-1 / SEV-2)

**Symptoms**: BFF API returns 502/504 for record operations, `/healthz/dataverse` reports unhealthy.

**Diagnosis**:
```bash
# 1. Check BFF health sub-checks
curl https://api.spaarke.com/healthz

# 2. Check Dataverse directly via PAC CLI
pac org who

# 3. Verify Dataverse app user authentication
az ad sp show --id 720bcc53-3399-488d-9a93-dafde5d9e290 --query "displayName" -o tsv
```

**Resolution**:
1. Check Microsoft 365 Service Health for Dataverse outages
2. If authentication issue: Verify app registration credentials in Key Vault have not expired
3. If Dataverse environment-specific: Verify the `Dataverse__ServiceUrl` app setting is correct
4. If S2S token issue: Re-create the client secret and update Key Vault

```bash
# Check current Dataverse URL setting
az webapp config appsettings list --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod --query "[?name=='Dataverse__ServiceUrl'].value" -o tsv
```

---

### 2.3 AI Services Errors (SEV-2 / SEV-3)

**Symptoms**: Document analysis fails, AI summaries return errors, playbook actions time out.

**Affected Services**:
- Azure OpenAI (`spaarke-openai-prod`)
- AI Search (`spaarke-search-prod`)
- Document Intelligence (`spaarke-docintel-prod`)

**Diagnosis**:
```bash
# 1. Check Azure OpenAI endpoint
curl -s -o /dev/null -w "%{http_code}" https://spaarke-openai-prod.openai.azure.com/openai/models?api-version=2024-06-01

# 2. Check AI Search endpoint
curl -s -o /dev/null -w "%{http_code}" https://spaarke-search-prod.search.windows.net/indexes?api-version=2024-07-01

# 3. Check Document Intelligence endpoint
curl -s -o /dev/null -w "%{http_code}" https://westus2.api.cognitive.microsoft.com/formrecognizer/info?api-version=2024-02-29-preview

# 4. Check OpenAI model deployments
az cognitiveservices account deployment list --resource-group rg-spaarke-platform-prod --name spaarke-openai-prod -o table

# 5. Check AI quota usage
az cognitiveservices account show --resource-group rg-spaarke-platform-prod --name spaarke-openai-prod --query "properties.quotaLimit" -o json
```

**Resolution**:
1. **Rate limiting (429 errors)**: Wait for quota reset. Consider reducing batch size or adding retry backoff.
2. **Model deployment deleted**: Re-create the model deployment (GPT-4o, GPT-4o-mini, text-embedding-3-large).
3. **Endpoint unreachable**: Check Azure status page. If regional issue, consider failover region.
4. **Document Intelligence errors**: Check document format support, verify API version compatibility.

---

### 2.4 Redis Unavailable (SEV-2)

**Symptoms**: Increased response times, cache miss spikes in App Insights, BFF logs show Redis connection errors.

**Diagnosis**:
```bash
# 1. Check Redis status
az redis show --name spaarke-redis-prod --resource-group rg-spaarke-platform-prod --query "{state:provisioningState,hostName:hostName}" -o json

# 2. Check Redis metrics (connections, memory)
az monitor metrics list --resource /subscriptions/{sub-id}/resourceGroups/rg-spaarke-platform-prod/providers/Microsoft.Cache/Redis/spaarke-redis-prod --metric "connectedclients,usedmemory,serverLoad" --interval PT5M -o table
```

**Resolution**:
1. If Redis__Enabled is false (current state): Non-critical, API falls back to no-cache operation.
2. If Redis is provisioned and unreachable: Restart the Redis cache.
3. If memory pressure: Review TTL settings, flush non-critical keys.
4. If persistent failure: Disable Redis via `Redis__Enabled=false` app setting to restore API functionality.

```bash
# Disable Redis as emergency measure
az webapp config appsettings set --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod --settings Redis__Enabled=false

# Restart Redis
az redis force-reboot --name spaarke-redis-prod --resource-group rg-spaarke-platform-prod --reboot-type AllNodes
```

---

### 2.5 Service Bus Errors (SEV-2)

**Symptoms**: Document processing jobs not completing, queue messages accumulating, BFF logs show Service Bus connection errors.

**Diagnosis**:
```bash
# 1. Check Service Bus namespace status
az servicebus namespace show --name spaarke-servicebus-prod --resource-group rg-spaarke-platform-prod --query "{status:status,sku:sku.name}" -o json

# 2. Check queue message counts
az servicebus queue show --namespace-name spaarke-servicebus-prod --resource-group rg-spaarke-platform-prod --name document-processing --query "{activeMessages:countDetails.activeMessageCount,deadLetter:countDetails.deadLetterMessageCount}" -o json

# 3. Check dead-letter queue (failed messages)
az servicebus queue show --namespace-name spaarke-servicebus-prod --resource-group rg-spaarke-platform-prod --name document-processing --query "countDetails.deadLetterMessageCount" -o tsv
```

**Resolution**:
1. **Dead-letter accumulation**: Investigate failed messages, fix processing errors, replay from dead-letter queue.
2. **Queue not draining**: Check if BFF API background worker is running (`/healthz` should report worker health).
3. **Namespace unreachable**: Check Azure status, verify managed identity has access.

---

### 2.6 Key Vault Access Failures (SEV-1)

**Symptoms**: BFF API fails to start, App Insights shows Key Vault reference resolution errors, secrets return empty.

**Diagnosis**:
```bash
# 1. Check Key Vault status
az keyvault show --name sprk-platform-prod-kv --query "{vaultUri:properties.vaultUri,provisioningState:properties.provisioningState}" -o json

# 2. List secrets (verify they exist)
az keyvault secret list --vault-name sprk-platform-prod-kv --query "[].{name:name,enabled:attributes.enabled}" -o table

# 3. Verify managed identity has access
az role assignment list --scope /subscriptions/{sub-id}/resourceGroups/rg-spaarke-platform-prod/providers/Microsoft.KeyVault/vaults/sprk-platform-prod-kv --query "[?principalType=='ServicePrincipal'].{principal:principalName,role:roleDefinitionName}" -o table
```

**Resolution**:
1. **Secret deleted or disabled**: Restore from soft-delete or re-create the secret.
2. **RBAC changed**: Re-grant "Key Vault Secrets User" role to the App Service managed identity.
3. **Key Vault in recovery mode**: Wait for recovery or contact Azure support.

```bash
# Managed identity IDs for reference:
# Production slot: 8990e956-237d-4274-9a44-4e91bd736237
# Staging slot:    5f275d9f-4ecf-4ef1-92e3-5a4d3e6bb76c
```

---

### 2.7 SSL/Domain Issues (SEV-2)

**Symptoms**: Users see certificate warnings, `api.spaarke.com` returns connection errors, HTTPS handshake failures.

**Diagnosis**:
```bash
# 1. Check custom domain binding
az webapp hostname list --resource-group rg-spaarke-platform-prod --webapp-name spaarke-bff-prod -o table

# 2. Check SSL binding
az webapp config ssl list --resource-group rg-spaarke-platform-prod -o table

# 3. Test DNS resolution
nslookup api.spaarke.com

# 4. Test SSL certificate
curl -vI https://api.spaarke.com 2>&1 | grep -E "SSL|issuer|expire"
```

**Resolution**:
1. **Certificate expired**: Azure-managed certificates auto-renew. If failed, delete and re-create the binding.
2. **DNS misconfigured**: Verify CNAME record points to `spaarke-bff-prod.azurewebsites.net`.
3. **Fallback**: Use the direct Azure URL `https://spaarke-bff-prod.azurewebsites.net` while resolving domain issues.

---

### 2.8 SharePoint Embedded (SPE) Errors (SEV-2)

**Symptoms**: Document uploads fail, file listing returns errors, container operations time out.

**Diagnosis**:
```bash
# 1. Check SPE endpoint through BFF
curl -s -o /dev/null -w "%{http_code}" https://api.spaarke.com/api/containers

# 2. Verify BFF API app registration has Graph permissions
az ad app show --id 92ecc702-d9ae-492d-957e-563244e93d8c --query "requiredResourceAccess" -o json

# 3. Check BFF logs for Graph API errors
az webapp log tail --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod --filter "Graph|SPE|Container"
```

**Resolution**:
1. **Token acquisition failure**: Verify client secret in Key Vault is current, check Entra ID app registration.
2. **Permission changes**: Re-consent Graph API permissions for the BFF API app registration.
3. **SPE service outage**: Check Microsoft 365 Service Health for SharePoint incidents.

---

## 3. Rollback Procedures

### 3.1 BFF API Rollback via Slot Swap

The primary rollback mechanism for the BFF API is swapping the staging slot back to production. After each deployment, the previous production version is preserved in the staging slot.

**Automatic rollback** (built into Deploy-BffApi.ps1):
The deployment script automatically rolls back if the post-swap health check fails (`-RollbackOnFailure $true` is the default).

**Manual rollback**:
```bash
# Swap staging (previous version) back to production
az webapp deployment slot swap \
  --resource-group rg-spaarke-platform-prod \
  --name spaarke-bff-prod \
  --slot staging \
  --target-slot production

# Verify production health after rollback
curl https://api.spaarke.com/healthz
```

**Rollback verification**:
```powershell
# Run smoke tests to verify rollback
.\scripts\Test-Deployment.ps1 -Environment prod
```

**Time to recover**: ~30 seconds (slot swap is near-instant).

---

### 3.2 Dataverse Solution Rollback

Managed solutions can be uninstalled to revert Dataverse changes. Uninstall in reverse dependency order.

```bash
# List installed solutions with versions
pac solution list

# Uninstall a specific solution (this removes all components in that solution)
pac solution delete --solution-name SpaarkeFeatureName
```

**Caution**: Uninstalling a managed solution removes all components (entities, forms, views, web resources) included in that solution. Data in custom entities will be lost.

**Safer alternative**: Import the previous version of the managed solution (overwrite). Keep previous solution ZIP files in version control or a release archive.

---

### 3.3 Infrastructure Rollback (Bicep)

Azure resource changes via Bicep are harder to roll back. Prevention is the best strategy.

**Before deploying infrastructure changes**:
```bash
# Always run what-if first
az deployment group what-if \
  --resource-group rg-spaarke-platform-prod \
  --template-file infrastructure/bicep/platform.bicep \
  --parameters infrastructure/bicep/platform-prod.bicepparam
```

**If infrastructure change causes issues**:
1. Re-deploy the previous Bicep template from the last known good commit.
2. For App Service configuration changes: Use `az webapp config appsettings set` to revert specific settings.
3. For networking changes: May require manual Azure Portal intervention.

---

## 4. Escalation Paths

### Contact Matrix

| Role | Contact | Availability | Escalation Channel |
|------|---------|-------------|-------------------|
| On-call Engineer | (Assigned weekly) | 24/7 | PagerDuty / Phone |
| Engineering Lead | Ralph Schroeder | Business hours + SEV-1 | Slack / Teams / Phone |
| Azure Admin | Ralph Schroeder | Business hours | Slack / Teams |
| Dataverse Admin | Ralph Schroeder | Business hours | Slack / Teams |

### Escalation Timeline

| Time Since Detection | Action |
|---------------------|--------|
| 0 min | On-call engineer acknowledges alert |
| 15 min (SEV-1) | If not resolved, escalate to Engineering Lead |
| 30 min (SEV-1) | If not resolved, begin customer communication |
| 1 hour (SEV-1) | Open Azure support ticket if Azure-related |
| 4 hours (SEV-2) | Escalate to Engineering Lead if no progress |

### External Escalation

| Issue Type | Escalation Target | How |
|-----------|-------------------|-----|
| Azure platform issues | Microsoft Azure Support | [Azure Portal](https://portal.azure.com) > Support + Troubleshooting |
| Dataverse / Power Platform | Microsoft Support | [Power Platform Admin Center](https://admin.powerplatform.microsoft.com) |
| SharePoint Embedded | Microsoft Graph Support | Azure Support ticket (category: Microsoft Graph) |
| DNS / Domain | Domain registrar | Registrar admin portal |

---

## 5. Communication Templates

### 5.1 Initial Incident Notification (Internal)

```
INCIDENT ALERT - SEV-{LEVEL}

Service: {Service Name}
Impact: {Brief description of user impact}
Detected: {Timestamp}
Status: Investigating

Initial observations:
- {What we see}
- {What we've checked so far}

Next steps:
- {What we're doing now}

Incident Commander: {Name}
Updates every: {15 min for SEV-1, 30 min for SEV-2}
```

### 5.2 Customer-Facing Status Update

```
Subject: [Spaarke] Service Disruption - {Brief Description}

We are currently experiencing {brief description of the issue}.

Impact: {What features are affected}
Status: {Investigating / Identified / Monitoring / Resolved}

We are actively working to resolve this and will provide updates every {30 minutes}.

If you have questions, please contact {support channel}.

Last updated: {Timestamp}
```

### 5.3 Incident Resolution Notification

```
Subject: [Spaarke] RESOLVED - {Brief Description}

The issue affecting {service} has been resolved.

Duration: {Start time} to {End time} ({total duration})
Root Cause: {Brief root cause}
Resolution: {What was done to fix it}

We will conduct a post-incident review and share findings.

Apologies for any inconvenience.
```

---

## 6. Diagnostic Tools and Commands

### Quick Health Check

```powershell
# Run the full smoke test suite
.\scripts\Test-Deployment.ps1 -Environment prod

# Run targeted test groups
.\scripts\Test-Deployment.ps1 -Environment prod -SkipGroups "AI,SPE"
```

### Log Access

```bash
# Stream live BFF API logs
az webapp log tail --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod

# Stream staging slot logs
az webapp log tail --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod --slot staging

# Download log files
az webapp log download --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod --log-file logs.zip
```

### App Insights Queries (KQL)

```kql
// Recent exceptions (last 1 hour)
exceptions
| where timestamp > ago(1h)
| summarize count() by type, outerMessage
| order by count_ desc

// Failed requests by endpoint
requests
| where timestamp > ago(1h) and success == false
| summarize count() by name, resultCode
| order by count_ desc

// Slow requests (>2 seconds)
requests
| where timestamp > ago(1h) and duration > 2000
| summarize avg(duration), count() by name
| order by avg_duration desc

// Dependency failures (external service calls)
dependencies
| where timestamp > ago(1h) and success == false
| summarize count() by type, target, resultCode
| order by count_ desc
```

---

## 7. Post-Incident Review Process

After every SEV-1 or SEV-2 incident, conduct a blameless post-incident review within 48 hours.

### Review Template

```
## Post-Incident Review

**Incident ID**: INC-{YYYY-MM-DD}-{NN}
**Date**: {Date}
**Severity**: SEV-{Level}
**Duration**: {Start} to {End}
**Impact**: {Number of users/customers affected, features degraded}

### Timeline
| Time | Event |
|------|-------|
| {Time} | {What happened} |

### Root Cause
{Detailed root cause analysis}

### Resolution
{What was done to resolve the incident}

### What Went Well
- {Things that worked during response}

### What Could Be Improved
- {Process gaps, missing tooling, slow detection}

### Action Items
| Item | Owner | Due Date | Status |
|------|-------|----------|--------|
| {Action} | {Name} | {Date} | Open |
```

### Review Participants
- Incident Commander
- On-call engineer who responded
- Engineering Lead
- Any engineers involved in resolution
- (Optional) Customer Success if customer-impacting

---

## 8. Production Environment Reference

### Resource Quick Reference

| Resource | Name | Resource Group |
|----------|------|---------------|
| App Service | `spaarke-bff-prod` | `rg-spaarke-platform-prod` |
| Staging Slot | `spaarke-bff-prod/staging` | `rg-spaarke-platform-prod` |
| Key Vault | `sprk-platform-prod-kv` | `rg-spaarke-platform-prod` |
| Azure OpenAI | `spaarke-openai-prod` | `rg-spaarke-platform-prod` |
| AI Search | `spaarke-search-prod` | `rg-spaarke-platform-prod` |
| Document Intelligence | `spaarke-docintel-prod` | `rg-spaarke-platform-prod` |
| Redis | `spaarke-redis-prod` | `rg-spaarke-platform-prod` |
| Service Bus | `spaarke-servicebus-prod` | `rg-spaarke-platform-prod` |

### Key URLs

| Service | URL |
|---------|-----|
| Production API | `https://api.spaarke.com` |
| Health Check | `https://api.spaarke.com/healthz` |
| Staging Slot | `https://spaarke-bff-prod-staging.azurewebsites.net` |
| Azure Portal | `https://portal.azure.com` |
| App Insights | Azure Portal > `rg-spaarke-platform-prod` > Application Insights |

### Entra ID App Registrations

| App | App ID | Purpose |
|-----|--------|---------|
| BFF API (prod) | `92ecc702-d9ae-492d-957e-563244e93d8c` | Graph + SPE + Dataverse |
| Dataverse S2S (prod) | `720bcc53-3399-488d-9a93-dafde5d9e290` | Dataverse server-to-server |

### Managed Identities

| Slot | Identity ID |
|------|-------------|
| Production | `8990e956-237d-4274-9a44-4e91bd736237` |
| Staging | `5f275d9f-4ecf-4ef1-92e3-5a4d3e6bb76c` |
