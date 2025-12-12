# AI Summary Service - Quick Issue Reference

## 🎯 **Top 5 Most Common Issues**

### 1. **"Deployment Not Found" Error**
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

### 2. **Analysis Completes But Fields Are Empty**
**Problem**: Dataverse field names don't match  
**Quick Check**: Run diagnostic script
```powershell
.\scripts\Diagnose-AiSummaryService.ps1
```
**Common Cause**: Custom Dataverse solution has different field logical names

---

### 3. **"401 Unauthorized" from Azure OpenAI**
**Problem**: API key is invalid or expired  
**Quick Fix**:
```bash
# Get current key
az cognitiveservices account keys list \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2
```
Update in `config/ai-config.local.json` or user secrets.

---

### 4. **PDF/DOCX Files Won't Analyze**
**Problem**: Document Intelligence not configured  
**Quick Fix**: Add to `appsettings.Development.json`:
```json
"DocIntelEndpoint": "https://westus2.api.cognitive.microsoft.com/",
"DocIntelKey": "your-doc-intel-key"
```
Or test with `.txt` files only during development.

---

### 5. **"Service Temporarily Unavailable" (503)**
**Problem**: Circuit breaker is open  
**Quick Fix**: 
- Wait 30 seconds
- Check logs for root cause
- Fix underlying issue (usually #1 or #3)
- Restart application

---

## 🔍 **Quick Diagnostic Flow**

```
1. Can you reach Azure OpenAI?
   NO → Check network/firewall
   YES → Go to 2

2. Does deployment "gpt-4o-mini" exist?
   NO → Update SummarizeModel config
   YES → Go to 3

3. Do Dataverse fields exist?
   NO → Run Dataverse solution import
   YES → Go to 4

4. Does user have write permissions?
   NO → Grant Write access to sprk_document
   YES → Go to 5

5. Check Application Insights for specific error
```

---

## 📊 **Field Mapping Reference**

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

---

## 🚨 **Error Code Reference**

| Error | Meaning | Fix |
|-------|---------|-----|
| AADSTS50013 | Token audience mismatch | Check API app registration |
| AADSTS70011 | Invalid scope | Use `.default` scope |
| 401 Unauthorized | Bad API key | Regenerate key in Azure |
| 404 Not Found | Deployment doesn't exist | Check deployment name |
| 429 Too Many Requests | Rate limit hit | Wait or increase quota |
| 503 Service Unavailable | Circuit breaker open | Wait 30s, fix root cause |

---

## 🧪 **Quick Test Command**

```powershell
# Test with curl (replace values)
curl -X POST http://localhost:5000/api/ai/document-intelligence/analyze `
  -H "Content-Type: application/json" `
  -H "Authorization: Bearer YOUR_TOKEN" `
  -d '{
    "documentId": "guid-here",
    "driveId": "container-id",
    "itemId": "item-id"
  }'
```

---

## 📞 **Need More Help?**

Full troubleshooting guide: `docs/guides/TROUBLESHOOTING-AI-SUMMARY.md`

Run diagnostics: `.\scripts\Diagnose-AiSummaryService.ps1`

Check logs: `Application Insights → Failures → Filter by AI`

---

*Last Updated: December 11, 2025*
