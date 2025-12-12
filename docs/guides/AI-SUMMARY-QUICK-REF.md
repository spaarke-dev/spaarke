# AI Services - Quick Issue Reference

> **Last Updated**: December 11, 2025
> **Covers**: Phase 1 (Document Analysis) + Phase 2 (Record Matching)

---

## Current Deployment Status

| Feature | Status | Endpoint |
|---------|--------|----------|
| Document Analysis (SSE) | Deployed | POST /api/ai/document-intelligence/analyze |
| Background Analysis | Deployed | POST /api/ai/document-intelligence/enqueue |
| Record Matching | Deployed | POST /api/ai/document-intelligence/match-records |
| Record Association | Deployed | POST /api/ai/document-intelligence/associate-record |
| Index Sync (Admin) | Deployed | POST /api/admin/record-matching/sync |
| Index Status (Admin) | Deployed | GET /api/admin/record-matching/status |

---

## Top 5 Most Common Issues

### 1. "Deployment Not Found" Error
**Problem**: Model deployment name doesn't match
**Quick Fix**:
```bash
# Check deployments in Azure
az cognitiveservices account deployment list \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2 \
  -o table
```
Then update `DocumentIntelligence:SummarizeModel` to match exact deployment name.

---

### 2. Analysis Completes But Fields Are Empty
**Problem**: Dataverse field names don't match
**Quick Check**: Verify fields exist on sprk_document entity
**Common Cause**: Custom Dataverse solution has different field logical names

**Expected Fields**:
- sprk_filesummary, sprk_filetldr, sprk_filekeywords
- sprk_extractorganization, sprk_extractpeople, sprk_extractfees
- sprk_extractdates, sprk_extractreference, sprk_extractdocumenttype

---

### 3. "401 Unauthorized" from Azure OpenAI
**Problem**: API key is invalid or expired
**Quick Fix**:
```bash
# Get current key
az cognitiveservices account keys list \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2
```
Update in `config/ai-config.local.json` or Azure App Service settings.

---

### 4. PDF/DOCX Files Won't Analyze
**Problem**: Document Intelligence not configured
**Quick Fix**: Add to app settings:
```
DocumentIntelligence__DocIntelEndpoint=https://westus2.api.cognitive.microsoft.com/
DocumentIntelligence__DocIntelKey=<your-key>
```
Or test with `.txt` files only during development.

---

### 5. "Service Temporarily Unavailable" (503)
**Problem**: App startup failure or missing configuration
**Quick Fix**:
1. Check app logs: `az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2`
2. Verify all required settings are configured
3. For record matching: ensure `AiSearchEndpoint` and `AiSearchKey` are set

---

## Phase 2: Record Matching Issues

### 6. 404 on /match-records Endpoint
**Problem**: Record matching not enabled
**Quick Fix**:
```bash
az webapp config appsettings set \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --settings "DocumentIntelligence__RecordMatchingEnabled=true"
```

---

### 7. No Match Results Returned
**Problem**: AI Search index is empty
**Quick Fix**:
```bash
# Trigger index sync
curl -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/record-matching/sync \
  -H "Authorization: Bearer $TOKEN"

# Check index status
curl https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/record-matching/status \
  -H "Authorization: Bearer $TOKEN"
```

---

### 8. Low Confidence Scores
**Problem**: Entities don't match indexed records
**Understanding**: Confidence scoring weights:
- Reference numbers: 50% (exact match)
- Organizations: 25% (fuzzy match)
- People: 15% (fuzzy match)
- Keywords: 10% (term overlap)

**Fix**: Ensure records in Dataverse have populated:
- Reference numbers (matter numbers, invoice IDs)
- Organization/company names
- Contact/person names

---

## Quick Diagnostic Flow

```
1. Can you reach the API?
   curl https://spe-api-dev-67e2xz.azurewebsites.net/ping
   NO → Check App Service is running
   YES → Go to 2

2. Is AI enabled?
   Check: DocumentIntelligence__Enabled=true
   NO → Enable in app settings
   YES → Go to 3

3. Does OpenAI deployment exist?
   az cognitiveservices account deployment list ...
   NO → Create deployment or fix SummarizeModel
   YES → Go to 4

4. For record matching - is search configured?
   Check: RecordMatchingEnabled, AiSearchEndpoint, AiSearchKey
   NO → Add all three settings
   YES → Go to 5

5. Check Application Insights for specific error
   Portal → Application Insights → Failures → Filter by "AI"
```

---

## Field Mapping Reference

### Phase 1: Document Analysis Fields

| Model Property | Dataverse Field | Type |
|---------------|-----------------|------|
| Summary | sprk_filesummary | Multiline Text |
| TlDr | sprk_filetldr | Multiline Text |
| Keywords | sprk_filekeywords | Text |
| SummaryStatus | sprk_filesummarystatus | Choice (0-4) |
| ExtractOrganization | sprk_extractorganization | Multiline Text |
| ExtractPeople | sprk_extractpeople | Multiline Text |
| ExtractFees | sprk_extractfees | Multiline Text |
| ExtractDates | sprk_extractdates | Multiline Text |
| ExtractReference | sprk_extractreference | Multiline Text |
| ExtractDocumentType | sprk_extractdocumenttype | Text |
| DocumentType | sprk_documenttype | Choice |

### Phase 1b: Email Fields

| Model Property | Dataverse Field | Type |
|---------------|-----------------|------|
| EmailSubject | sprk_emailsubject | Text |
| EmailFrom | sprk_emailfrom | Text |
| EmailTo | sprk_emailto | Text |
| EmailDate | sprk_emaildate | DateTime |
| EmailBody | sprk_emailbody | Multiline Text |
| Attachments | sprk_attachments | Multiline Text (JSON) |

### Phase 2: Record Matching Lookup Fields

| Record Type | Lookup Field |
|-------------|--------------|
| sprk_matter | sprk_matter |
| sprk_project | sprk_project |
| sprk_invoice | sprk_invoice |

---

## Error Code Reference

| Error | Meaning | Fix |
|-------|---------|-----|
| AADSTS50013 | Token audience mismatch | Check API app registration |
| AADSTS70011 | Invalid scope | Use `.default` scope |
| 401 Unauthorized | Bad API key | Regenerate key in Azure |
| 404 Not Found | Deployment/endpoint doesn't exist | Check deployment name or enable feature |
| 429 Too Many Requests | Rate limit hit | Wait or increase quota |
| 503 Service Unavailable | App startup failure | Check logs, fix missing config |

---

## Quick Test Commands

### Test Document Analysis
```bash
# Replace TOKEN with valid bearer token
curl -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/document-intelligence/analyze \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "documentId": "guid-here",
    "driveId": "container-id",
    "itemId": "item-id"
  }'
```

### Test Record Matching
```bash
curl -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/document-intelligence/match-records \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "entities": {
      "organizations": ["Acme Corp"],
      "references": ["MAT-2024-001"]
    },
    "recordTypeFilter": "all",
    "maxResults": 5
  }'
```

### Test Index Status
```bash
curl https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/record-matching/status \
  -H "Authorization: Bearer $TOKEN"
```

---

## Configuration Reference

### Required App Settings (Phase 1)
```
DocumentIntelligence__Enabled=true
DocumentIntelligence__OpenAiEndpoint=https://spaarke-openai-dev.openai.azure.com/
DocumentIntelligence__OpenAiKey=<key>
DocumentIntelligence__SummarizeModel=gpt-4o-mini
```

### Optional: Document Intelligence (for PDF/DOCX)
```
DocumentIntelligence__DocIntelEndpoint=https://westus2.api.cognitive.microsoft.com/
DocumentIntelligence__DocIntelKey=<key>
```

### Required for Record Matching (Phase 2)
```
DocumentIntelligence__RecordMatchingEnabled=true
DocumentIntelligence__AiSearchEndpoint=https://spaarke-search-dev.search.windows.net
DocumentIntelligence__AiSearchKey=<key>
DocumentIntelligence__AiSearchIndexName=spaarke-records-index
```

---

## Need More Help?

- **Implementation Status**: `docs/ai-knowledge/guides/AI-IMPLEMENTATION-STATUS.md`
- **Full Troubleshooting**: `docs/guides/TROUBLESHOOTING-AI-SUMMARY.md`
- **Architecture Overview**: `docs/ai-knowledge/guides/SPAARKE-AI-ARCHITECTURE.md`
- **Check Logs**: Application Insights > Failures > Filter by "AI"

---

*Last Updated: December 11, 2025*
