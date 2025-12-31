# Task 045: R3 Rollback Plan

> **Date**: December 30, 2025
> **Project**: AI Document Intelligence R3
> **Purpose**: Emergency rollback procedures for R3 deployment

---

## Rollback Decision Criteria

### Trigger Rollback If

| Condition | Severity | Action |
|-----------|----------|--------|
| Health check fails for > 5 minutes | Critical | Immediate rollback |
| Error rate > 10% on AI endpoints | Critical | Immediate rollback |
| Authentication failures spike | Critical | Immediate rollback |
| Circuit breakers stuck open | High | Investigate then rollback |
| RAG search returning incorrect results | High | Investigate then rollback |
| P95 latency > 30s on document analysis | Medium | Monitor, rollback if sustained |

---

## Rollback Procedures

### Procedure 1: API Rollback (App Service)

**Time to complete: ~5 minutes**

#### Option A: Azure Portal Deployment Slots

```bash
# Swap back to previous deployment slot
az webapp deployment slot swap \
  --name spe-api-prod \
  --resource-group spe-infrastructure-westus2 \
  --slot staging \
  --target-slot production
```

#### Option B: Redeploy Previous Version

```bash
# Find last successful deployment
gh run list --workflow=deploy-to-azure.yml --status=success -L 5

# Redeploy specific run
gh run rerun <previous-run-id>
```

#### Option C: Git Revert

```bash
# Revert the R3 merge commit
git revert <r3-merge-commit-sha> --no-edit
git push origin main

# Pipeline will auto-deploy reverted code
```

### Procedure 2: RAG Index Rollback

**Time to complete: ~2 minutes**

The RAG index is additive - R3 doesn't modify existing documents.

```bash
# Option 1: Disable RAG endpoints via feature flag
az webapp config appsettings set \
  --name spe-api-prod \
  --resource-group spe-infrastructure-westus2 \
  --settings "Ai:RagEnabled=false"

# Option 2: Delete new R3 documents from index (if needed)
# Documents indexed after deployment can be identified by indexedAt timestamp
```

### Procedure 3: Dataverse Solution Rollback

**Time to complete: ~10 minutes**

```bash
# Import previous solution version
pac solution import \
  --path solutions/SpaarkeAI_v1.x.x_managed.zip \
  --activate-plugins

# Verify PCF controls reverted
pac pcf list
```

### Procedure 4: Configuration Rollback

**Time to complete: ~2 minutes**

```bash
# Restore previous app settings
az webapp config appsettings set \
  --name spe-api-prod \
  --resource-group spe-infrastructure-westus2 \
  --settings @config/appsettings.pre-r3.json

# Restart app
az webapp restart --name spe-api-prod --resource-group spe-infrastructure-westus2
```

---

## Rollback Verification

After any rollback, verify:

```bash
# 1. Health check
curl https://spe-api-prod.azurewebsites.net/healthz
# Expected: "Healthy"

# 2. Basic API check
curl https://spe-api-prod.azurewebsites.net/ping
# Expected: "pong"

# 3. Check logs for errors
az webapp log tail --name spe-api-prod --resource-group spe-infrastructure-westus2

# 4. Verify error rate in App Insights
# Query: requests | where resultCode >= 500 | summarize count() by bin(timestamp, 1m)
```

---

## Data Preservation

### What's Preserved on Rollback

| Data | Location | Status |
|------|----------|--------|
| Existing documents | Dataverse + SPE | ✅ Preserved |
| Existing analyses | Dataverse | ✅ Preserved |
| RAG index (non-R3 docs) | Azure AI Search | ✅ Preserved |
| Playbooks | Dataverse | ✅ Preserved (if not created during R3) |

### What's Lost on Rollback

| Data | Location | Status |
|------|----------|--------|
| R3-created playbooks | Dataverse | ⚠️ May need manual cleanup |
| R3-indexed documents | Azure AI Search | ⚠️ Can filter by indexedAt |
| In-flight analyses | In-memory | ⚠️ Lost on restart |

---

## Communication Template

### Rollback Notification

```
Subject: [ROLLBACK] AI Document Intelligence R3 - Production

Team,

We are initiating a rollback of the R3 deployment due to:
- [REASON]

Timeline:
- Rollback initiated: [TIME]
- Expected completion: [TIME + 10min]
- Service impact: [DESCRIPTION]

Actions:
- [WHO] is executing the rollback
- [WHO] is monitoring error rates
- [WHO] will communicate completion

Updates will follow in #incidents channel.
```

### Rollback Complete Notification

```
Subject: [RESOLVED] R3 Rollback Complete

Team,

R3 rollback completed successfully.

Verification:
- Health check: ✅ Passing
- Error rate: ✅ Normal (<1%)
- API response times: ✅ Normal

Root cause analysis will begin tomorrow.
Next deployment attempt TBD.
```

---

## Contacts

| Role | Name | Contact |
|------|------|---------|
| DevOps Lead | TBD | - |
| Backend Lead | TBD | - |
| On-call Engineer | TBD | - |

---

## Post-Rollback Actions

1. **Immediately**:
   - [ ] Verify health checks passing
   - [ ] Notify team of rollback
   - [ ] Check error rates normalized

2. **Within 1 hour**:
   - [ ] Gather logs around failure time
   - [ ] Identify root cause
   - [ ] Document in incident report

3. **Within 24 hours**:
   - [ ] Complete incident postmortem
   - [ ] Create fix PR for identified issues
   - [ ] Schedule re-deployment

---

*Rollback plan created: December 30, 2025*
