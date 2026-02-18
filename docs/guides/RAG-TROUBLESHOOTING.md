# RAG Troubleshooting Guide

> **Version**: 1.2
> **Created**: 2025-12-29
> **Updated**: 2026-01-23
> **Project**: AI Document Intelligence R3 + Semantic Search UI R2 + Semantic Search Foundation R1

---

## Table of Contents

1. [Common Issues](#common-issues)
2. [Search Issues](#search-issues)
3. [Semantic Search Issues](#semantic-search-issues) *(R1)*
4. [Send to Index Issues](#send-to-index-issues) *(R2)*
5. [Automatic Re-indexing Issues](#automatic-re-indexing-issues) *(R2 - NEW)*
6. [PCF Control Issues](#pcf-control-issues) *(R2)*
7. [Indexing Issues](#indexing-issues)
8. [Embedding Issues](#embedding-issues)
9. [Deployment Model Issues](#deployment-model-issues)
10. [Performance Issues](#performance-issues)
11. [Diagnostic Commands](#diagnostic-commands)
12. [Logging and Monitoring](#logging-and-monitoring)

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

## Send to Index Issues

> **Added in**: Semantic Search UI R2 (2026-01-23)

### Ribbon Button Not Appearing

**Symptom**: "Send to Index" button is not visible in the Document grid, form, or subgrid command bar.

**Check 1: Solution Imported**

```powershell
# Check if DocumentRibbons solution is installed
pac solution list | Select-String "DocumentRibbons"
```

**Check 2: Ribbon XML Published**

```powershell
# Re-publish all customizations
pac solution publish
```

**Check 3: User Security Role**

Verify the user has:
- Read access to `sprk_document` entity
- Read access to related entities (Matter, Project, Invoice)

**Resolution**:
1. Import/re-import `DocumentRibbons` solution (v1.4.0.0+)
2. Publish all customizations
3. Clear browser cache and refresh
4. Verify user security roles

---

### Button Click Shows Error

**Symptom**: Clicking "Send to Index" button shows an error notification.

**Check 1: Environment Variable**

```powershell
# Verify BFF API URL is set
pac env variable get --name "sprk_BffApiBaseUrl"
```

**Check 2: Web Resource Version**

Verify `sprk_DocumentOperations.js` is version 1.26.0 or later:
- Navigate to Settings > Customizations > Web Resources
- Find `sprk_/scripts/sprk_DocumentOperations.js`
- Check the display name includes version

**Check 3: Browser Console**

Open browser DevTools (F12) > Console and look for:
- "sendToIndex: No documents selected" - no records selected
- "sendToIndex: Environment variable not found" - missing `sprk_BffApiBaseUrl`
- CORS errors - BFF API CORS configuration

**Resolution**:
1. Set `sprk_BffApiBaseUrl` environment variable
2. Ensure at least one document is selected
3. Check BFF API is running and accessible
4. Verify CORS allows Dataverse origin

---

### Send to Index Returns Partial Failures

**Symptom**: Response shows some documents succeeded, some failed.

**Check Response**:

```json
{
  "totalRequested": 5,
  "successCount": 3,
  "failedCount": 2,
  "results": [
    { "documentId": "...", "success": false, "errorMessage": "Document not found" },
    { "documentId": "...", "success": false, "errorMessage": "Document does not have an associated file" }
  ]
}
```

**Common Errors**:

| Error Message | Cause | Resolution |
|--------------|-------|------------|
| "Document not found" | Invalid document ID | Verify document exists in Dataverse |
| "Document does not have an associated file" | No SPE file linked | Upload a file to the document first |
| "File download failed" | SPE access issue | Check SPE container permissions |
| "Text extraction failed" | Unsupported file type | Check supported file types in RAG-ARCHITECTURE.md |

---

### Dataverse Fields Not Updated After Indexing

**Symptom**: Document indexes successfully but `sprk_searchindexed` remains false.

**Check 1: Field Exists**

Verify these fields exist on `sprk_document`:
- `sprk_searchindexed` (Boolean)
- `sprk_searchindexname` (String)
- `sprk_searchindexedon` (DateTime)

**Check 2: User Permissions**

The user executing the ribbon button needs Update permission on `sprk_document`.

**Check 3: API Response**

The API response includes the index operation result. If `success: true` but fields not updated, check Dataverse service logs.

**Resolution**:
1. Add missing fields to `sprk_document` entity
2. Grant Update permission on `sprk_document`
3. Check BFF API logs for Dataverse update errors

---

## Automatic Re-indexing Issues

> **Added in**: Semantic Search UI R2 (v1.26.0)

### Re-index Not Triggered on Check-in

**Symptom**: Documents are checked in successfully but `aiAnalysisTriggered` is always `false` in the response.

**Check 1: Reindexing Configuration**

```bash
# Verify configuration is set
az webapp config appsettings list \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query "[?name=='Reindexing__Enabled' || name=='Reindexing__TenantId' || name=='Reindexing__TriggerOnCheckin']"
```

**Check 2: TenantId Configured**

The `Reindexing__TenantId` must be set. Look for this log warning:
```
Warning: Re-indexing TenantId not configured - skipping re-index for check-in
```

**Check 3: TriggerOnCheckin Enabled**

Both `Enabled` and `TriggerOnCheckin` must be `true`:
```json
{
  "Reindexing": {
    "Enabled": true,
    "TriggerOnCheckin": true
  }
}
```

**Resolution**:
1. Set `Reindexing__Enabled=true`
2. Set `Reindexing__TenantId` to your Azure AD tenant ID
3. Set `Reindexing__TriggerOnCheckin=true`
4. Restart the App Service

---

### Wrong TenantId - Documents Not Searchable

**Symptom**: Re-indexing triggers, but documents don't appear in semantic search results.

**Common Cause**: Using Dataverse organization ID instead of Azure AD tenant ID.

**Check**: Compare IDs in these locations:

| Location | Expected Value | How to Check |
|----------|----------------|--------------|
| `Reindexing__TenantId` | Azure AD tenant ID | App Service settings |
| `sprk_DocumentOperations.js` | Azure AD tenant ID (from MSAL) | `Config.msal.tenantId` |
| PCF Control `tenantId` prop | Azure AD tenant ID | Form configuration |

**How to Find Azure AD Tenant ID**:
1. Azure Portal → Azure Active Directory → Overview → **Tenant ID**
2. Or from MSAL authentication response (`tenantId` claim)

**Wrong IDs**:
- ❌ Dataverse organization ID (e.g., `org123456.crm.dynamics.com`)
- ❌ Dataverse environment ID
- ❌ Power Platform environment ID

**Correct ID**:
- ✅ Azure AD tenant ID (e.g., `a221a95e-6abc-4434-aecc-e48338a1b2f2`)

**Resolution**:
1. Verify all three locations use the same Azure AD tenant ID
2. If documents were indexed with wrong tenant ID, re-index them

---

### Re-index Job Failures

**Symptom**: Check-in succeeds with `aiAnalysisTriggered: true`, but document not appearing in search.

**Check 1: Job Status**

The re-indexing job is fire-and-forget. Check for processing errors in logs:
```
Error: Failed to enqueue re-index job for document {DocumentId} after check-in
```

**Check 2: Service Bus Queue**

```bash
# Check for dead-letter messages
az servicebus queue show \
  --namespace-name spaarke-servicebus-dev \
  --resource-group spe-infrastructure-westus2 \
  --name sdap-jobs \
  --query "countDetails.deadLetterMessageCount"
```

**Check 3: Job Handler Logs**

Look for `RagIndexingJobHandler` processing logs:
```
Information: Processing RAG indexing job for document {documentId}
Information: RAG indexing completed for document {documentId}, chunks: {count}
```

**Common Errors**:

| Error | Cause | Resolution |
|-------|-------|------------|
| "File download failed" | SPE access issue | Check app-only permissions |
| "Text extraction failed" | Unsupported file type | Verify file type is supported |
| "Embedding generation failed" | OpenAI service issue | Check Azure OpenAI health |

**Resolution**:
1. Check Application Insights for job processing errors
2. Verify Service Bus queue is processing messages
3. Check for dead-letter queue messages

---

### Check-in Performance Degradation

**Symptom**: Check-in operations slower than expected.

**Note**: Re-indexing should NOT slow down check-in. The job is enqueued **fire-and-forget** - the API response returns immediately without waiting for indexing.

**Check**: If check-in is slow, look for other causes:
- SharePoint Embedded API latency
- Dataverse update latency
- Network issues

The re-indexing job enqueueing should add < 10ms to check-in response time.

---

## PCF Control Issues

> **Added in**: Semantic Search UI R2 (2026-01-23)

### Control Not Rendering

**Symptom**: SemanticSearchControl shows blank or "Control failed to load".

**Check 1: Control Version**

Verify the control is imported:
```powershell
pac pcf list
# Look for "sprk_SemanticSearchControl"
```

**Check 2: Form Configuration**

In the form editor:
- Control must be bound to a text field or use unbound mode
- Required properties (`entityType`, `entityId`, `bffApiUrl`, `tenantId`) must be configured

**Check 3: Browser Console**

Open DevTools (F12) > Console and look for:
- React errors
- MSAL authentication errors
- Network errors to BFF API

**Resolution**:
1. Re-import SemanticSearchControl solution
2. Verify form configuration
3. Check required properties are set correctly

---

### Authentication Errors in Control

**Symptom**: Control shows "Authentication failed" or search returns 401.

**Check 1: MSAL Configuration**

The control uses MSAL for OBO authentication. Verify:
- Client ID is registered in Azure AD
- API permissions include BFF API scope
- Redirect URI matches Dataverse URL

**Check 2: Token Acquisition**

Open browser DevTools > Network and filter by "token":
- Look for failed token requests
- Check error messages in response body

**Check 3: BFF API Endpoint**

Verify `bffApiUrl` property is correct:
```
https://spe-api-dev-67e2xz.azurewebsites.net/api
```

**Resolution**:
1. Verify Azure AD app registration
2. Check API permissions and admin consent
3. Ensure BFF API URL is correct and accessible

---

### Search Returns No Results in Control

**Symptom**: Searches execute but always return empty results.

**Check 1: Entity Context**

Verify `entityType` and `entityId` are correctly bound:
- `entityType` should be the logical name (e.g., `sprk_matter`)
- `entityId` should be bound to the record's primary key

**Check 2: Documents Indexed**

Verify documents exist in the index for this entity:
```bash
curl -X POST "https://spaarke-search-dev.search.windows.net/indexes/spaarke-knowledge-index-v2/docs/search?api-version=2024-07-01" \
  -H "Content-Type: application/json" \
  -H "api-key: <your-api-key>" \
  -d '{
    "search": "*",
    "filter": "parentEntityType eq '\''matter'\'' and parentEntityId eq '\''your-entity-id'\''",
    "count": true
  }'
```

**Check 3: Network Requests**

In DevTools > Network, find the `/api/ai/search` request:
- Check request payload has correct `entityType` and `entityId`
- Check response for error messages

**Resolution**:
1. Verify entity binding in form configuration
2. Index documents using "Send to Index" button first
3. Check parent entity fields are populated in index

---

### Control Styling Issues (Dark Mode)

**Symptom**: Control doesn't adapt to dark mode or colors look wrong.

**Check**: The control should use Fluent UI v9 theme tokens (ADR-021).

**Common Issues**:
- Hard-coded colors instead of theme tokens
- Missing `FluentProvider` wrapper
- Incorrect theme detection

**Resolution**:
1. Verify `ThemeService.ts` detects dark mode correctly
2. Check all components use `tokens.colorNeutral*` instead of hard-coded colors
3. Clear browser cache and refresh

---

### Infinite Scroll Not Working

**Symptom**: Only first page of results loads, scroll doesn't load more.

**Check 1: Results Count**

Verify there are more results than the page size (default 10):
- Check `metadata.totalResults` in API response
- If total ≤ page size, no scroll loading needed

**Check 2: Container Height**

The results container needs a fixed height for scroll detection:
- Check CSS for `overflow-y: auto` and `height: <value>`

**Check 3: Hook State**

In DevTools > React DevTools, check `useInfiniteScroll` hook state:
- `hasMore` should be `true` if more results exist
- `isLoading` should toggle on scroll

**Resolution**:
1. Verify results container has proper height/overflow CSS
2. Check hook is detecting scroll events
3. Verify API returns `totalResults` for pagination calculation

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
*Updated: 2026-01-23 - Automatic re-indexing troubleshooting added*
*AI Document Intelligence R3 - Phase 1 Complete*
*Semantic Search Foundation R1 - Complete (troubleshooting section added)*
*Semantic Search UI R2 - Send to Index, automatic re-indexing, and PCF control troubleshooting*
