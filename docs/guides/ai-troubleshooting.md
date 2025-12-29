# AI Document Intelligence - Troubleshooting Guide

> **Last Updated**: 2025-12-29
> **Covers**: Document Analysis, Record Matching, Analysis Workspace

---

## Quick Reference

### Current Deployment Status (Dev)

| Feature | Status | Endpoint |
|---------|--------|----------|
| Document Analysis (SSE) | Deployed | POST /api/ai/document-intelligence/analyze |
| Background Analysis | Deployed | POST /api/ai/document-intelligence/enqueue |
| Record Matching | Deployed | POST /api/ai/document-intelligence/match-records |
| AnalysisBuilder PCF | v1.12.0 | Custom Page hosted |
| AnalysisWorkspace PCF | v1.0.29 | Custom Page hosted |

### Quick Diagnostic Flow

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

4. Check Application Insights for specific error
   Portal → Application Insights → Failures → Filter by "AI"
```

### Field Mapping Reference

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

---

## Quick Diagnostics

### Check AI Service Status

```bash
# Health check
curl -X GET https://your-api/healthz

# Check if AI is enabled
curl -X GET https://your-api/api/ai/status \
  -H "Authorization: Bearer {token}"
```

### View Logs

```kusto
// Application Insights - Recent AI errors
traces
| where message contains "AI" or message contains "OpenAI"
| where severityLevel >= 3
| order by timestamp desc
| take 50
```

---

## Common Issues

### 1. AI Features Return 503 Service Unavailable

**Symptom:** All AI endpoints return `503` with code `ai_disabled`.

**Causes:**
- AI feature flag is disabled
- Missing OpenAI configuration

**Solution:**

1. Check configuration:
```json
{
  "Ai": {
    "Enabled": true,
    "OpenAiEndpoint": "https://...",
    "OpenAiKey": "..."
  }
}
```

2. Verify Key Vault secrets are accessible:
```bash
az keyvault secret show --vault-name {vault} --name ai-openai-key
```

---

### 2. PDF/DOCX Files Return "Extraction Not Configured"

**Symptom:** `503` with code `extraction_not_configured` for PDF/DOCX files.

**Cause:** Document Intelligence service is not configured.

**Solution:**

1. Deploy Azure Document Intelligence resource
2. Add configuration:
```json
{
  "Ai": {
    "DocIntelEndpoint": "https://{resource}.cognitiveservices.azure.com/",
    "DocIntelKey": "@Microsoft.KeyVault(SecretUri=...)"
  }
}
```

---

### 3. Images Return "Vision Not Configured"

**Symptom:** `503` with code `vision_not_configured` for image files.

**Cause:** Vision model is not configured.

**Solution:**

1. Deploy a multimodal model (gpt-4o) in Azure OpenAI
2. Add configuration:
```json
{
  "Ai": {
    "ImageSummarizeModel": "gpt-4o"
  }
}
```

---

### 4. Rate Limit Exceeded (429)

**Symptom:** `429 Too Many Requests` with `Retry-After` header.

**Causes:**
- User exceeded 10 streaming requests/minute
- User exceeded 20 batch requests/minute

**Solution:**

1. Wait for the `Retry-After` duration
2. Implement exponential backoff:
```typescript
async function retryWithBackoff(fn, maxRetries = 3) {
  for (let i = 0; i < maxRetries; i++) {
    try {
      return await fn();
    } catch (error) {
      if (error.status === 429) {
        const retryAfter = error.headers.get('Retry-After') || 60;
        await sleep(retryAfter * 1000);
      } else {
        throw error;
      }
    }
  }
}
```

---

### 5. Circuit Breaker Open (503)

**Symptom:** `503` with code `ai_circuit_open`.

**Cause:** OpenAI service has experienced repeated failures (>50% failure rate).

**Solution:**

1. Wait 30 seconds for circuit to half-open
2. Check Azure OpenAI service status: https://status.azure.com
3. Review logs for underlying errors:

```kusto
traces
| where message contains "circuit breaker"
| order by timestamp desc
```

---

### 6. OpenAI Rate Limit (429)

**Symptom:** `429` with code `openai_rate_limit`.

**Cause:** Azure OpenAI deployment rate limit exceeded.

**Solution:**

1. Wait and retry (check `retryAfterSeconds` in response)
2. Increase Azure OpenAI deployment quota:
   - Go to Azure OpenAI Studio (ai.azure.com)
   - Navigate to Deployments > Your deployment
   - Increase Tokens per Minute (TPM) limit
3. Consider using a higher-tier deployment

---

### 7. Content Filtered (422)

**Symptom:** `422` with code `openai_content_filter`.

**Cause:** Document content triggered Azure OpenAI content safety filters.

**Solution:**

1. Review document for potentially sensitive content
2. Check Azure OpenAI content filtering settings
3. Contact Azure support if content filtering is too aggressive

---

### 8. File Too Large (413)

**Symptom:** `413` with code `file_too_large`.

**Cause:** File exceeds 10MB limit.

**Solution:**

1. Reduce file size before upload
2. For PDFs: compress or reduce resolution
3. Increase limit if needed:
```json
{
  "Ai": {
    "MaxFileSizeBytes": 20971520  // 20MB
  }
}
```

---

### 9. Unsupported File Type (415)

**Symptom:** `415` with code `unsupported_file_type`.

**Cause:** File extension is not in supported list.

**Solution:**

1. Check supported file types:
   - Native: `.txt`, `.md`, `.json`, `.csv`, `.xml`, `.html`
   - Doc Intel: `.pdf`, `.docx`, `.doc`
   - Vision: `.png`, `.jpg`, `.jpeg`, `.gif`, `.tiff`, `.bmp`, `.webp`

2. Enable additional types in configuration:
```json
{
  "Ai": {
    "SupportedFileTypes": {
      ".xlsx": { "Enabled": true, "Method": "DocumentIntelligence" }
    }
  }
}
```

---

### 10. SSE Connection Drops

**Symptom:** SSE stream disconnects mid-summary.

**Causes:**
- Client timeout
- Network interruption
- Load balancer timeout

**Solution:**

1. Implement reconnection logic:
```typescript
const eventSource = new EventSource(url);
eventSource.onerror = () => {
  // Background job will complete summarization
  console.log('Stream interrupted - summary will complete in background');
};
```

2. Check load balancer timeouts (should be > 120s for SSE)
3. Verify the summary was saved to Dataverse

---

### 11. Slow Summarization

**Symptom:** Summarization takes > 30 seconds.

**Causes:**
- Large document
- High OpenAI latency
- Document Intelligence processing time

**Solution:**

1. Check document size and reduce if possible
2. Monitor OpenAI latency:
```kusto
customMetrics
| where name == "ai.summarize.duration"
| summarize avg(value), percentile(value, 95) by bin(timestamp, 1h)
```

3. Consider using `gpt-4o-mini` for faster responses
4. For large documents, truncation is applied automatically

---

### 12. Summary Quality Issues

**Symptom:** Summaries are too short, too long, or miss key points.

**Solution:**

1. Adjust prompt template:
```json
{
  "Ai": {
    "SummarizePromptTemplate": "Your custom prompt here... {documentText}"
  }
}
```

2. Adjust output length:
```json
{
  "Ai": {
    "MaxOutputTokens": 1500  // Longer summaries
  }
}
```

3. Adjust temperature (lower = more deterministic):
```json
{
  "Ai": {
    "Temperature": 0.2
  }
}
```

---

## Monitoring Queries

### Success Rate

```kusto
customMetrics
| where name == "ai.summarize.requests"
| summarize
    total = count(),
    success = countif(customDimensions["ai.status"] == "success"),
    failed = countif(customDimensions["ai.status"] == "failed")
| extend success_rate = round(100.0 * success / total, 2)
```

### Error Breakdown

```kusto
customMetrics
| where name == "ai.summarize.failures"
| summarize count() by tostring(customDimensions["ai.error_code"])
| order by count_ desc
```

### Token Usage (Cost Tracking)

```kusto
customMetrics
| where name == "ai.summarize.tokens"
| summarize total_tokens = sum(value) by tostring(customDimensions["ai.token_type"])
```

### P95 Latency

```kusto
customMetrics
| where name == "ai.summarize.duration"
| summarize percentile(value, 95) by bin(timestamp, 1h)
| render timechart
```

---

## Health Check Endpoints

### GET /healthz

Returns overall API health including AI service status.

```json
{
  "status": "Healthy",
  "checks": {
    "ai": {
      "status": "Healthy",
      "description": "AI services operational"
    }
  }
}
```

### GET /api/ai/status (Authenticated)

Returns detailed AI configuration status.

```json
{
  "enabled": true,
  "streamingEnabled": true,
  "openAiConfigured": true,
  "docIntelConfigured": true,
  "visionConfigured": true,
  "supportedFileTypes": [".txt", ".pdf", ".png", ...]
}
```

---

## Contact Support

If issues persist after following this guide:

1. Collect diagnostic information:
   - Correlation ID from error response
   - Timestamp of the error
   - File type and approximate size
   - Application Insights logs

2. Check Azure service status:
   - https://status.azure.com
   - https://oai.azure.com/portal (OpenAI Studio)

3. Review ADR-013 for architectural guidance:
   - `docs/reference/adr/ADR-013-ai-architecture.md`

---

*Last updated: December 2025*
