# RAG Troubleshooting Guide

> **Version**: 1.1
> **Created**: 2025-12-29
> **Updated**: 2026-01-20
> **Project**: AI Document Intelligence R3 + Semantic Search Foundation R1

---

## Table of Contents

1. [Common Issues](#common-issues)
2. [Search Issues](#search-issues)
3. [Semantic Search Issues](#semantic-search-issues) *(R1 - NEW)*
4. [Indexing Issues](#indexing-issues)
5. [Embedding Issues](#embedding-issues)
6. [Deployment Model Issues](#deployment-model-issues)
7. [Performance Issues](#performance-issues)
8. [Diagnostic Commands](#diagnostic-commands)
9. [Logging and Monitoring](#logging-and-monitoring)

---

## Common Issues

### RAG Service Not Starting

**Symptom**: API fails to start with RAG-related configuration errors.

**Check 1: App Settings**

```bash
# Verify configuration
az webapp config appsettings list \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query "[?name=='DocumentIntelligence__Enabled' || name=='DocumentIntelligence__AiSearchEndpoint']"
```

**Check 2: AI Search Accessibility**

```bash
# Test AI Search endpoint
curl -I "https://spaarke-search-dev.search.windows.net"
# Expected: HTTP 403 (requires authentication, but endpoint is reachable)
```

**Check 3: Startup Logs**

Look for these log entries on successful startup:
```
✓ RAG Knowledge Deployment service enabled (index: spaarke-knowledge-index-v2)
✓ Embedding cache enabled (Redis)
```

**Resolution**:
1. Ensure `DocumentIntelligence__Enabled=true`
2. Verify `DocumentIntelligence__AiSearchEndpoint` is a valid URL
3. Verify `DocumentIntelligence__AiSearchKey` is set
4. Check network connectivity from App Service to AI Search

---

### Authentication Errors (401/403)

**Symptom**: RAG API calls return 401 Unauthorized or 403 Forbidden.

**For API Authentication (401)**:
- Ensure valid JWT token in Authorization header
- Check token expiration
- Verify PAC CLI is authenticated: `pac auth list`

**For AI Search Authentication (403)**:
- Verify API key is correct and not rotated
- Check Key Vault reference is resolving correctly

```bash
# Test AI Search key
curl -X GET "https://spaarke-search-dev.search.windows.net/indexes?api-version=2024-07-01" \
  -H "api-key: <your-api-key>"
```

---

## Search Issues

### No Results Returned

**Symptom**: Search returns empty results even when documents should match.

**Check 1: Documents Exist**

```bash
# Count documents in index
curl -X GET "https://spaarke-search-dev.search.windows.net/indexes/spaarke-knowledge-index-v2/docs/\$count?api-version=2024-07-01" \
  -H "api-key: <your-api-key>"
```

**Check 2: Tenant Filter**

Ensure documents exist for the tenant being queried:

```bash
# Search for specific tenant
curl -X POST "https://spaarke-search-dev.search.windows.net/indexes/spaarke-knowledge-index-v2/docs/search?api-version=2024-07-01" \
  -H "Content-Type: application/json" \
  -H "api-key: <your-api-key>" \
  -d '{
    "search": "*",
    "filter": "tenantId eq '\''your-tenant-id'\''",
    "count": true
  }'
```

**Check 3: MinScore Too High**

Lower the `minScore` threshold:

```json
{
  "query": "your query",
  "options": {
    "tenantId": "your-tenant",
    "minScore": 0.3
  }
}
```

**Resolution**:
1. Verify documents are indexed for the tenant
2. Lower minScore to 0.3 or 0.2 for testing
3. Check query is not too specific

---

### Poor Search Relevance

**Symptom**: Search returns results but they're not relevant to the query.

**Check 1: Semantic Ranking Enabled**

Ensure the search is using semantic ranking:

```csharp
var searchOptions = new SearchOptions
{
    QueryType = SearchQueryType.Semantic,
    SemanticSearch = new SemanticSearchOptions
    {
        SemanticConfigurationName = "knowledge-semantic-config"
    }
};
```

**Check 2: Content Quality**

Review indexed content quality:
- Is content too short? (< 100 characters)
- Is content chunked properly?
- Does content include relevant keywords?

**Check 3: Embedding Model Match**

Ensure the same embedding model is used for:
- Document indexing
- Query embedding

**Resolution**:
1. Re-index documents with better content chunks
2. Verify semantic configuration is applied
3. Adjust vector search parameters (efSearch)

---

### Search Timeout

**Symptom**: Search requests timeout or take > 10 seconds.

**Check 1: AI Search Service Health**

```bash
# Check service status
az search service show \
  --name spaarke-search-dev \
  --resource-group spe-infrastructure-westus2 \
  --query "status"
```

**Check 2: Index Size**

```bash
# Check index statistics
curl -X GET "https://spaarke-search-dev.search.windows.net/indexes/spaarke-knowledge-index-v2/stats?api-version=2024-07-01" \
  -H "api-key: <your-api-key>"
```

**Resolution**:
1. Increase AI Search replicas for high load
2. Reduce topK parameter
3. Add filters to narrow search scope

---

## Semantic Search Issues

> **Added in**: Semantic Search Foundation R1 (2026-01-20)

### Semantic Search Endpoints Return 500

**Symptom**: POST `/api/ai/search` returns HTTP 500 after deployment.

**Common Cause**: Endpoint mapped but services not registered (configuration mismatch).

**Check 1: Configuration Match**

The semantic search endpoints require BOTH settings to be enabled:
- `DocumentIntelligence__Enabled=true`
- `Analysis__Enabled=true` (defaults to `true` if not set)

```bash
# Verify both settings
az webapp config appsettings list \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query "[?name=='DocumentIntelligence__Enabled' || name=='Analysis__Enabled']"
```

**Check 2: Service Registration**

Look for startup log confirming semantic search services:
```
✓ Semantic Search services registered
```

**Resolution**:
1. Ensure both `DocumentIntelligence__Enabled` and `Analysis__Enabled` are `true`
2. If services are disabled intentionally, the endpoint won't be mapped (returns 404, not 500)
3. Restart the App Service after configuration changes

---

### Invalid Scope Error (400)

**Symptom**: Request returns HTTP 400 with error code `INVALID_SCOPE` or `SCOPE_NOT_SUPPORTED`.

**Common Causes**:
- Using `scope=all` (not supported in R1)
- Typo in scope value

**Check**: Verify request body:

```json
{
  "scope": "entity",   // ✅ Valid: entity, documentIds
  "scope": "all"       // ❌ Invalid: returns SCOPE_NOT_SUPPORTED
}
```

**Resolution**:
1. Use `scope=entity` with `entityType` and `entityId`
2. Use `scope=documentIds` with `documentIds[]` array
3. For cross-entity search, submit multiple requests per entity

---

### Missing Entity Type or ID (400)

**Symptom**: Request returns HTTP 400 with `ENTITY_TYPE_REQUIRED` or `ENTITY_ID_REQUIRED`.

**Cause**: Using `scope=entity` without required fields.

**Resolution**: Include all required fields:

```json
{
  "query": "search terms",
  "scope": "entity",
  "entityType": "matter",      // Required when scope=entity
  "entityId": "guid-here"       // Required when scope=entity
}
```

---

### No Results with Entity Scope

**Symptom**: Search returns empty results even when documents should exist.

**Check 1: Parent Entity Fields Indexed**

Verify documents have parent entity fields populated:

```bash
curl -X POST "https://spaarke-search-dev.search.windows.net/indexes/spaarke-knowledge-index-v2/docs/search?api-version=2024-07-01" \
  -H "Content-Type: application/json" \
  -H "api-key: <your-api-key>" \
  -d '{
    "search": "*",
    "filter": "parentEntityType eq '\''matter'\'' and parentEntityId eq '\''your-entity-id'\''",
    "count": true,
    "top": 5
  }'
```

**Check 2: Documents Re-indexed After Schema Update**

If documents were indexed before R1, they won't have `parentEntityType`, `parentEntityId`, `parentEntityName` fields.

**Resolution**:
1. Re-index documents to populate parent entity fields
2. Verify entity type matches exactly (case-sensitive)
3. Lower `minRelevanceScore` to test if results exist at all

---

### Embedding Generation Fallback

**Symptom**: Search works but `metadata.embeddingGenerated` is `false`.

**Meaning**: Embedding generation failed; search fell back to keyword-only mode.

**Check**: Look for warning logs:
```
Warning: Embedding generation failed for semantic search query. Falling back to keyword-only.
```

**Resolution**:
1. Check Azure OpenAI service health
2. Verify `text-embedding-3-large` deployment exists
3. Check rate limits haven't been exceeded
4. The search still works but may have lower relevance

---

### Authorization Failures (403)

**Symptom**: Request returns HTTP 403 Forbidden.

**Cause**: User doesn't have access to the parent entity or specified documents.

**Check**: `SemanticSearchAuthorizationFilter` logs show which entity/document failed authorization.

**Resolution**:
1. Verify user has read access to the parent entity in Dataverse
2. For `scope=documentIds`, verify user has access to ALL specified document IDs
3. Check tenant ID matches the user's token

---

## Indexing Issues

### Document Indexing Fails

**Symptom**: POST /api/ai/rag/index returns 500 error.

**Check 1: Document Validation**

Ensure required fields:
- `id` - unique identifier
- `tenantId` - tenant for isolation
- `content` - text content to index

**Check 2: Index Schema Compatibility**

```bash
# Get index schema
curl -X GET "https://spaarke-search-dev.search.windows.net/indexes/spaarke-knowledge-index-v2?api-version=2024-07-01" \
  -H "api-key: <your-api-key>"
```

**Check 3: Embedding Generation**

If embedding fails, check Azure OpenAI:
- Deployment exists: `text-embedding-3-large`
- Rate limits not exceeded
- API key is valid

**Resolution**:
1. Validate document structure matches KnowledgeDocument model
2. Check content is not empty
3. Verify Azure OpenAI embedding model is deployed

---

### Batch Indexing Partial Failures

**Symptom**: Some documents in batch succeed, others fail.

**Check**: Review IndexResult for each document:

```csharp
var results = await ragService.IndexDocumentsBatchAsync(documents, ct);
foreach (var result in results)
{
    if (!result.Succeeded)
    {
        _logger.LogError("Failed to index {DocumentId}: {Error}",
            result.Key, result.ErrorMessage);
    }
}
```

**Common Causes**:
- Duplicate document IDs
- Invalid field values (e.g., null required fields)
- Content exceeds maximum size

**Resolution**:
1. Ensure unique IDs in batch
2. Validate all documents before batch
3. Split large batches into smaller chunks (max 1000)

---

## Embedding Issues

### Embedding Cache Not Working

**Symptom**: Cache hit rate is 0% or embeddings are always regenerated.

**Check 1: Redis Connection**

```bash
# Test Redis connection
redis-cli -h <your-redis-host> -p 6380 -a <your-redis-password> --tls ping
```

**Check 2: Cache Keys Exist**

```bash
# List embedding cache keys
redis-cli -h <your-redis-host> -p 6380 -a <your-redis-password> --tls \
  keys "sdap:embedding:*" | head -20
```

**Check 3: Cache Metrics**

Check OpenTelemetry metrics:
- `cache_hits_total{cacheType="embedding"}`
- `cache_misses_total{cacheType="embedding"}`

**Resolution**:
1. Verify Redis connection string
2. Ensure Redis is enabled: `Redis__Enabled=true`
3. Check Redis firewall allows App Service IP

---

### Embedding Generation Fails

**Symptom**: "Embedding Generation Failed" error.

**Check 1: Azure OpenAI Status**

```bash
# List deployments
az cognitiveservices account deployment list \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2 \
  -o table
```

**Check 2: Rate Limits**

Check if hitting token limits:
- text-embedding-3-large: 120,000 TPM

**Check 3: Content Size**

Ensure content is within token limits:
- Max ~8191 tokens per request
- Split long content into chunks

**Resolution**:
1. Verify embedding model deployment exists
2. Wait for rate limit reset (1 minute)
3. Chunk content to stay within limits

---

## Deployment Model Issues

### Dedicated Index Not Created

**Symptom**: Dedicated tenant queries fail with "Index not found".

**Check**: The index is created on first use. Ensure:
1. Deployment config is saved with `Model = Dedicated`
2. At least one document has been indexed

**Create Index Manually** (if needed):

```bash
# Create dedicated index from template
az rest --method PUT \
  --uri "https://spaarke-search-dev.search.windows.net/indexes/{tenantId}-knowledge?api-version=2024-07-01" \
  --headers "Content-Type=application/json" "api-key=<your-api-key>" \
  --body @infrastructure/ai-search/spaarke-knowledge-index-v2.json
```

---

### CustomerOwned Connection Fails

**Symptom**: CustomerOwned deployment returns connection errors.

**Check 1: Validation**

```csharp
var validation = await deploymentService.ValidateCustomerOwnedDeploymentAsync(config);
if (!validation.IsValid)
{
    Console.WriteLine($"Validation failed: {validation.ErrorMessage}");
}
```

**Check 2: Key Vault Secret**

```bash
# Verify secret exists
az keyvault secret show \
  --vault-name spaarke-spekvcert \
  --name "customer-api-key-secret"
```

**Check 3: Customer Endpoint Reachable**

```bash
# Test customer's AI Search endpoint
curl -I "https://customer-search.search.windows.net"
```

**Resolution**:
1. Verify SearchEndpoint URL is correct
2. Ensure ApiKeySecretName matches Key Vault secret
3. Verify customer has whitelisted our IP/service

---

## Performance Issues

### High Search Latency (> 500ms P95)

**Diagnosis**:

1. **Check embedding cache hit rate** - Low hit rate means more OpenAI calls
2. **Check AI Search response time** - Use Application Insights queries
3. **Check semantic ranking overhead** - Adds 100-200ms

**Optimizations**:

| Issue | Solution |
|-------|----------|
| Cache miss rate high | Review query patterns, increase TTL |
| Large result sets | Reduce topK, add filters |
| Complex queries | Simplify search terms |
| Network latency | Deploy in same region as AI Search |

---

### High Memory Usage

**Symptom**: App Service memory pressure, OOM errors.

**Check**: SearchClient caching might accumulate:

```csharp
// Clients are cached per tenant
_searchClients.GetOrAdd(cacheKey, _ => CreateClient(config))
```

**Resolution**:
1. Monitor number of unique tenants
2. Implement LRU eviction for SearchClient cache
3. Scale up App Service plan

---

## Diagnostic Commands

### Check Index Health

```bash
# Index statistics
curl -X GET "https://spaarke-search-dev.search.windows.net/indexes/spaarke-knowledge-index-v2/stats?api-version=2024-07-01" \
  -H "api-key: <your-api-key>"

# Document count
curl -X GET "https://spaarke-search-dev.search.windows.net/indexes/spaarke-knowledge-index-v2/docs/\$count?api-version=2024-07-01" \
  -H "api-key: <your-api-key>"
```

### Test Search Directly

```bash
# Simple search
curl -X POST "https://spaarke-search-dev.search.windows.net/indexes/spaarke-knowledge-index-v2/docs/search?api-version=2024-07-01" \
  -H "Content-Type: application/json" \
  -H "api-key: <your-api-key>" \
  -d '{
    "search": "payment terms",
    "filter": "tenantId eq '\''test-tenant'\''",
    "top": 5,
    "select": "id,documentName,content"
  }'
```

### Test Embedding Generation

```bash
# Via API
curl -X POST "https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/rag/embedding" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <your-token>" \
  -d '{"text": "Test embedding generation"}'
```

### Check Redis Cache

```bash
# Count embedding keys
redis-cli -h <redis-host> -p 6380 -a <password> --tls \
  eval "return #redis.call('keys','sdap:embedding:*')" 0

# Check specific key TTL
redis-cli -h <redis-host> -p 6380 -a <password> --tls \
  ttl "sdap:embedding:your-key-hash"
```

---

## Logging and Monitoring

### Key Log Messages

| Log Level | Message | Meaning |
|-----------|---------|---------|
| Information | "RAG search completed" | Successful search with latency |
| Warning | "Embedding cache miss" | Cache miss, generating new |
| Warning | "Cache lookup failed" | Redis error, continuing without cache |
| Error | "Search failed" | AI Search query error |
| Error | "Indexing failed" | Document indexing error |

### Application Insights Queries

```kusto
// Search latency P95
requests
| where name == "POST RagSearch"
| summarize percentile(duration, 95) by bin(timestamp, 1h)

// Cache hit rate
customMetrics
| where name == "cache_hit_rate"
| where customDimensions.cacheType == "embedding"
| summarize avg(value) by bin(timestamp, 1h)

// Errors by type
exceptions
| where outerMessage contains "RAG"
| summarize count() by type, bin(timestamp, 1h)
```

### Health Check Endpoints

| Endpoint | Expected | Checks |
|----------|----------|--------|
| GET /healthz | 200 "Healthy" | App running |
| GET /ping | 200 "pong" | Basic response |

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| [RAG-ARCHITECTURE.md](RAG-ARCHITECTURE.md) | Architecture overview |
| [RAG-CONFIGURATION.md](RAG-CONFIGURATION.md) | Configuration reference |
| [AI-DEPLOYMENT-GUIDE.md](AI-DEPLOYMENT-GUIDE.md) | Full deployment guide |

---

*Document created: 2025-12-29*
*Updated: 2026-01-20*
*AI Document Intelligence R3 - Phase 1 Complete*
*Semantic Search Foundation R1 - Complete (troubleshooting section added)*
