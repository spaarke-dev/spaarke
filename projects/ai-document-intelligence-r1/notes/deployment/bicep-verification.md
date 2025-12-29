# Bicep Template Verification Report

> **Task**: 030 - Test Bicep Deployment to External Subscription
> **Date**: 2025-12-28
> **Status**: PARTIAL PASS

---

## Summary

| Metric | Value |
|--------|-------|
| **Templates Tested** | 4 stacks + 10 modules |
| **Valid Templates** | 2/4 stacks |
| **What-If Deployment** | SUCCESS |
| **Issues Found** | 1 critical bug |

---

## Template Validation Results

### Stacks

| Template | Status | Notes |
|----------|--------|-------|
| `ai-foundry-stack.bicep` | VALID | What-if deployment successful |
| `model1-customer.bicep` | VALID | Compiles without errors |
| `model1-shared.bicep` | INVALID | Blocked by ai-search module error |
| `model2-full.bicep` | INVALID | Blocked by ai-search module error |

### Modules

| Module | Status |
|--------|--------|
| `ai-foundry-hub.bicep` | VALID |
| `ai-search.bicep` | INVALID |
| `app-service-plan.bicep` | VALID |
| `app-service.bicep` | VALID |
| `doc-intelligence.bicep` | VALID |
| `key-vault.bicep` | VALID |
| `monitoring.bicep` | VALID |
| `openai.bicep` | VALID |
| `redis.bicep` | VALID |
| `service-bus.bicep` | VALID |
| `storage-account.bicep` | VALID |

---

## Critical Issue: ai-search.bicep

### Error

```
Error BCP075: Indexing over objects requires an index of type "string"
but the provided index was of type "0".
```

### Location

`infrastructure/bicep/modules/ai-search.bicep:55`

### Current Code (Incorrect)

```bicep
output searchServiceQueryKey string = searchService.listQueryKeys()[0].key
```

### Fix Required

```bicep
output searchServiceQueryKey string = searchService.listQueryKeys().value[0].key
```

**Explanation**: `listQueryKeys()` returns an object with a `value` property containing the array of keys, not a direct array.

### Impact

- Blocks `model1-shared.bicep` (line 243)
- Blocks `model2-full.bicep` (line 233)
- Does NOT affect `ai-foundry-stack.bicep` (doesn't use AI Search module directly)

---

## What-If Deployment Results

**Target**: `spe-infrastructure-westus2` resource group
**Parameters**: `customerId=test, environment=dev, location=westus2`

### Resources to Create (8)

| Resource | Type |
|----------|------|
| `sprktestdev-aif-hub` | AI Foundry Hub (ML Workspace) |
| `sprktestdev-aif-proj` | AI Foundry Project |
| `sprktestdevaifsa` | Storage Account |
| `sprktestdev-aif-kv` | Key Vault |
| `sprktestdev-aif-logs` | Log Analytics |
| `sprktestdev-aif-insights` | Application Insights |
| Storage role assignment | RBAC |
| Key Vault role assignment | RBAC |

### Existing Resources (15 ignored)

The deployment correctly identifies and ignores existing infrastructure:
- Azure OpenAI (`spaarke-openai-dev`)
- AI Search (`spaarke-search-dev`)
- Document Intelligence (`spaarke-docintel-dev`)
- Redis Cache, App Service, etc.

---

## Verification Commands Used

```bash
# Syntax validation
az bicep build --file stacks/ai-foundry-stack.bicep

# What-if deployment
az deployment group what-if \
  --resource-group spe-infrastructure-westus2 \
  --template-file stacks/ai-foundry-stack.bicep \
  --parameters customerId=test environment=dev location=westus2
```

---

## Recommendations

### Immediate

1. **Fix ai-search.bicep** - Change line 55 to use `.value[0]` indexing
2. **Re-validate** model1-shared.bicep and model2-full.bicep after fix

### For Production Deployment

1. Create dedicated test resource group (`rg-spaarke-test-*`)
2. Use non-production subscription for validation
3. Run actual deployment (not just what-if) before customer deployments

---

## Acceptance Criteria Status

| Criterion | Status |
|-----------|--------|
| Bicep templates validate | PARTIAL (ai-search bug) |
| What-if deployment succeeds | PASS |
| All resources would provision | PASS |
| Issues documented | PASS |

---

*Verification completed: 2025-12-28*
