# AI Summary Service Troubleshooting Guide

> **Generated**: December 11, 2025  
> **Purpose**: Diagnose and fix issues with the Spaarke AI Summary Service

---

## ✅ Configuration Status

According to diagnostics, your configuration is **correct**:
- ✓ All required fields exist in Dataverse Models
- ✓ All field mappings are correct in DataverseWebApiService
- ✓ Azure OpenAI is properly configured
- ✓ Services are registered correctly
- ✓ Endpoints are mapped

---

## 🔍 Common Runtime Issues

Since configuration is correct, here are the most likely runtime issues:

### 1. **Azure OpenAI API Key or Endpoint Issues**

**Symptoms:**
- 401 Unauthorized errors
- "Invalid API key" messages
- Connection failures to OpenAI endpoint

**Check:**
```powershell
# Test the OpenAI endpoint directly
$endpoint = "https://spaarke-openai-dev.openai.azure.com/"
$apiKey = "5ac9b4ba7d524d8d86462e3bbff4a8a4"  # From your config

$headers = @{
    "api-key" = $apiKey
}

Invoke-RestMethod -Uri "$endpoint/openai/deployments?api-version=2024-02-01" -Headers $headers
```

**Fix:**
- Verify the API key hasn't been rotated in Azure Portal
- Check that the endpoint URL is correct
- Ensure the deployment name "gpt-4o-mini" exists in your Azure OpenAI resource

---

### 2. **Model Deployment Name Mismatch**

**Symptoms:**
- "Deployment not found" errors
- 404 errors when calling OpenAI API

**Your config says:**
- Model: `gpt-4o-mini`
- Deployment: `gpt-4o-mini`

**Check in Azure Portal:**
1. Go to Azure OpenAI Studio (https://oai.azure.com/)
2. Select your resource: `spaarke-openai-dev`
3. Go to "Deployments"
4. Verify a deployment named **exactly** `gpt-4o-mini` exists

**Fix if mismatch:**
```json
// In appsettings.Development.json or user secrets
"DocumentIntelligence": {
  "SummarizeModel": "actual-deployment-name-here"
}
```

---

### 3. **Dataverse Field Schema Mismatch**

**Symptoms:**
- Updates succeed but fields are empty
- "Field not found" errors
- Fields saving as null

**Verify Dataverse fields exist:**
```powershell
# Use Dataverse Web API to check field metadata
# Replace with your Dataverse environment URL
$dataverseUrl = "https://your-org.crm.dynamics.com"
$token = "your-bearer-token"

$headers = @{
    "Authorization" = "Bearer $token"
    "Accept" = "application/json"
}

# Get sprk_document entity metadata
Invoke-RestMethod -Uri "$dataverseUrl/api/data/v9.2/EntityDefinitions(LogicalName='sprk_document')/Attributes" -Headers $headers
```

**Required Dataverse fields:**
- `sprk_filesummary` (Multiline Text)
- `sprk_filetldr` (Multiline Text)
- `sprk_filekeywords` (Text)
- `sprk_filesummarystatus` (Choice: 0=None, 1=Pending, 2=Completed, 3=OptedOut, 4=Failed)
- `sprk_extractorganization` (Multiline Text)
- `sprk_extractpeople` (Multiline Text)
- `sprk_extractfees` (Multiline Text)
- `sprk_extractdates` (Multiline Text)
- `sprk_extractreference` (Multiline Text)
- `sprk_extractdocumenttype` (Text)
- `sprk_documenttype` (Choice field)

---

### 4. **Circuit Breaker Open**

**Symptoms:**
- "Service temporarily unavailable" errors
- 503 Service Unavailable responses
- Error mentions "circuit breaker"

**Cause:** The circuit breaker opens after 5 consecutive failures to protect the service.

**Check:**
- Look for logs mentioning "circuit breaker OPENED"
- Check Application Insights for repeated OpenAI failures

**Fix:**
- Wait 30 seconds for circuit breaker to half-open
- Fix underlying OpenAI connectivity issue
- Restart the application to reset circuit breaker

---

### 5. **File Type Not Supported**

**Symptoms:**
- "File type not supported" errors
- Analysis doesn't start

**Check your file extension:**
```csharp
// Supported types (from appsettings.json):
// Native: .txt, .md, .json, .csv, .xml, .html
// DocumentIntelligence: .pdf, .docx, .doc
// Email: .eml, .msg
```

**Fix:**
- Ensure the file extension is in `SupportedFileTypes` config
- Ensure `Enabled: true` for that extension

---

### 6. **Document Intelligence Service Not Available**

**Symptoms:**
- PDFs/DOCX files fail to analyze
- "Document Intelligence service unavailable" errors

**Check:**
```powershell
# Test Document Intelligence endpoint (if configured)
$docIntelEndpoint = "https://westus2.api.cognitive.microsoft.com/"
$docIntelKey = "your-key-here"

$headers = @{
    "Ocp-Apim-Subscription-Key" = $docIntelKey
}

Invoke-RestMethod -Uri "$docIntelEndpoint/formrecognizer/info?api-version=2023-07-31" -Headers $headers
```

**Fix:**
- For Development: Add `DocIntelEndpoint` and `DocIntelKey` to `appsettings.Development.json`
- For Production: Verify Key Vault secrets are accessible
- Alternative: Only test with native text files (.txt, .md) during development

---

### 7. **Dataverse Authentication Issues**

**Symptoms:**
- "Unauthorized" errors when saving to Dataverse
- Analysis completes but doesn't save

**Check:**
- User has proper permissions to update `sprk_document` entity
- Service principal (if using app-only auth) has correct roles
- Dataverse connection string is valid

**Test Dataverse connectivity:**
```powershell
# From the app root
dotnet run --project src/server/api/Sprk.Bff.Api
# Then hit the health check: http://localhost:5000/health
```

---

### 8. **JSON Parsing Failures**

**Symptoms:**
- `ParsedSuccessfully` is false in logs
- Analysis returns raw text instead of structured data
- Missing entities/keywords

**Check logs for:**
```
Failed to parse AI response as JSON, falling back to raw text
```

**Causes:**
- AI returned invalid JSON
- AI included markdown code fences (```json)
- Response truncated

**Debug:**
- Check `RawResponse` field in the result
- Look for `CleanJsonResponse` method output in logs
- Verify AI prompt is requesting valid JSON

**Fix:**
- The service already has fallback logic
- If persistent, adjust the prompt template
- Check `MaxOutputTokens` isn't too low (should be 1000+)

---

## 🧪 Testing the Service

### Test with a Simple Text File

1. Create a test file `test.txt`:
```
This is a test contract between Acme Corp and XYZ Industries dated January 15, 2025.
The total contract value is $100,000 with payment terms of Net 30.
Contract reference number: CN-2025-001.
Key contacts: John Smith (Acme) and Jane Doe (XYZ).
```

2. Upload to your container via the UI

3. Trigger analysis via API:
```http
POST /api/ai/document-intelligence/analyze
Content-Type: application/json
Authorization: Bearer {your-token}

{
  "documentId": "guid-of-document",
  "driveId": "container-id",
  "itemId": "item-id"
}
```

4. Verify in Dataverse:
```sql
-- Query in Dataverse to check fields
SELECT sprk_filesummary, sprk_filetldr, sprk_filekeywords,
       sprk_extractorganization, sprk_extractpeople,
       sprk_extractfees, sprk_extractdates, sprk_extractreference
FROM sprk_document
WHERE sprk_documentid = 'your-document-id'
```

---

## 📋 Diagnostic Checklist

Run through this checklist systematically:

- [ ] Azure OpenAI endpoint is accessible
- [ ] API key is valid and not expired
- [ ] Deployment name "gpt-4o-mini" exists in Azure
- [ ] All Dataverse fields exist with correct logical names
- [ ] User has write permissions to sprk_document entity
- [ ] DocumentIntelligence:Enabled is true
- [ ] File extension is in SupportedFileTypes and enabled
- [ ] Circuit breaker is not open (check logs)
- [ ] No CORS errors (if calling from browser)
- [ ] Application Insights shows no repeated failures

---

## 🔬 Enable Detailed Logging

Add to `appsettings.Development.json`:

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Sprk.Bff.Api.Services.Ai": "Debug",
    "Sprk.Bff.Api.Api.Ai": "Debug",
    "Spaarke.Dataverse": "Debug"
  }
}
```

This will show:
- Detailed AI request/response logs
- Dataverse update payloads
- Field mapping operations
- Error details

---

## 🚨 Most Likely Issues (Priority Order)

Based on your configuration being correct, check these in order:

1. **API Key Expired/Rotated** - Check Azure Portal for current key
2. **Deployment Name Mismatch** - Verify "gpt-4o-mini" exists
3. **Dataverse Fields Missing** - Verify all sprk_* fields exist
4. **Permissions** - User lacks write access to sprk_document
5. **Network/Firewall** - Can't reach Azure OpenAI endpoint
6. **Rate Limiting** - Hitting OpenAI rate limits (10K TPM)

---

## 📞 Quick Debug Commands

```powershell
# Check if service is running
curl http://localhost:5000/health

# Check if AI endpoints are registered
curl http://localhost:5000/swagger/index.html
# Look for /api/ai/document-intelligence endpoints

# View app logs in real-time
dotnet run --project src/server/api/Sprk.Bff.Api | Select-String "DocumentIntelligence"

# Test OpenAI connectivity
az cognitiveservices account keys list --name spaarke-openai-dev --resource-group spe-infrastructure-westus2
```

---

## 📄 Related Files

If you need to modify code:

- **Service**: `src/server/api/Sprk.Bff.Api/Services/Ai/DocumentIntelligenceService.cs`
- **Endpoints**: `src/server/api/Sprk.Bff.Api/Api/Ai/DocumentIntelligenceEndpoints.cs`
- **Models**: `src/server/api/Sprk.Bff.Api/Models/Ai/`
- **Dataverse**: `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs`
- **Config**: `src/server/api/Sprk.Bff.Api/Configuration/DocumentIntelligenceOptions.cs`

---

**Next Steps:**

1. Run the diagnostic script: `.\scripts\Diagnose-AiSummaryService.ps1`
2. Check Application Insights for errors
3. Enable debug logging and attempt a summary
4. Review the specific error message
5. Match error to troubleshooting section above

---

*Generated by AI Summary Service Diagnostics - December 2025*
