# KM: Email Metadata Persistence Fix

**Date**: December 11, 2025
**Issue Type**: Bug - Data Not Persisting to Dataverse
**Severity**: High
**Status**: Resolved

---

## Executive Summary

Email metadata extracted from `.eml` and `.msg` files was not being persisted to Dataverse despite successful extraction. The root cause was a combination of three separate issues in the code path between AI analysis and Dataverse persistence.

**Key Lesson**: When adding new fields to Dataverse entities, you must update **all** implementations of the service interface, not just the one you think is being used.

---

## Problem Statement

When uploading `.eml` or `.msg` email files, the AI analysis correctly extracted metadata (Subject, From, To, Date, Body, Attachments), but these fields remained NULL in Dataverse after the analysis completed.

**Symptoms**:
- AI summary streaming worked correctly
- Logs showed "Email TARGET values" with correct data
- No "Document updated" log appeared after analysis
- Dataverse email fields remained NULL

---

## Root Causes (3 Issues)

### Issue 1: Wrong IDataverseService Implementation

**Problem**: The codebase has two implementations of `IDataverseService`:
- `DataverseWebApiService` - Uses OData/Web API (had all email fields)
- `DataverseServiceClientImpl` - Uses ServiceClient SDK (missing email fields)

The DI registration in `Program.cs` uses `DataverseServiceClientImpl`, but developers added email fields only to `DataverseWebApiService`.

**Discovery Method**:
```
Program.cs:279-283
builder.Services.AddSingleton<IDataverseService>(sp =>
{
    return new DataverseServiceClientImpl(configuration, logger);
});
```

### Issue 2: Wrong Type for OptionSet Field

**Problem**: `SummaryStatus` was set as a raw `int`, but Dataverse OptionSet fields require `OptionSetValue`.

**Error**:
```
FaultException: "Incorrect attribute value type System.Int32"
```

**Original Code** (wrong):
```csharp
document["sprk_filesummarystatus"] = request.SummaryStatus.Value;
```

**Fixed Code**:
```csharp
document["sprk_filesummarystatus"] = new OptionSetValue(request.SummaryStatus.Value);
```

### Issue 3: Wrong OptionSet Values

**Problem**: The `AnalysisStatus` enum used sequential integers (0, 1, 2, 3, 4), but Dataverse OptionSet values use the Power Platform standard range (100000000, 100000001, etc.).

**Error**:
```
FaultException: "The value 2 of 'sprk_filesummarystatus' is outside the valid range.
Accepted Values: 100000000,100000001,100000002,100000003,100000004,100000005,100000006"
```

**Original Enum** (wrong):
```csharp
public enum AnalysisStatus
{
    None = 0,
    Pending = 1,
    Completed = 2,
    OptedOut = 3,
    Failed = 4
}
```

**Fixed Enum**:
```csharp
public enum AnalysisStatus
{
    None = 100000000,
    Pending = 100000001,
    Completed = 100000002,
    OptedOut = 100000003,
    Failed = 100000004,
    NotSupported = 100000005,
    Skipped = 100000006
}
```

---

## Files Modified

### 1. DataverseServiceClientImpl.cs
**Path**: `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs`
**Method**: `UpdateDocumentAsync` (lines 109-220)

Added all missing fields:
- AI Analysis Fields (Summary, TlDr, Keywords, SummaryStatus)
- Extracted Entities Fields (Organizations, People, Fees, Dates, References, DocumentType)
- Email Metadata Fields (EmailSubject, EmailFrom, EmailTo, EmailDate, EmailBody, Attachments)
- Parent Document Fields (ParentDocumentId, ParentFileName, ParentGraphItemId, ParentDocumentLookup)

### 2. DocumentAnalysisJobHandler.cs
**Path**: `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/DocumentAnalysisJobHandler.cs`
**Change**: Updated `AnalysisStatus` enum to use Dataverse OptionSet values

---

## Code Flow Reference

Understanding the complete data flow is critical for debugging and extending:

```
1. PCF Control calls /api/ai/document-intelligence/analyze
         ↓
2. DocumentIntelligenceEndpoints.StreamAnalyze()
         ↓
3. DocumentIntelligenceService.AnalyzeDocumentAsync()
         ↓
4. TextExtractorService.ExtractTextAsync()
   - For .eml/.msg: Returns TextExtractionResult with EmailMetadata
         ↓
5. SummarizeService.StreamSummarizeAsync() [OpenAI call]
         ↓
6. Back in DocumentIntelligenceEndpoints.StreamAnalyze():
   - Captures final result with EmailMetadata attached
         ↓
7. SaveAnalysisToDataverseAsync()
   - Copies EmailMetadata to UpdateDocumentRequest
         ↓
8. IDataverseService.UpdateDocumentAsync()
   - ACTUAL IMPLEMENTATION: DataverseServiceClientImpl
   - Builds Entity object and calls ServiceClient.UpdateAsync()
         ↓
9. Dataverse receives the update
```

---

## Key Files for Email Metadata Feature

| File | Purpose |
|------|---------|
| `DocumentIntelligenceEndpoints.cs` | SSE streaming endpoint, calls SaveAnalysisToDataverseAsync |
| `DocumentIntelligenceService.cs` | Orchestrates extraction + AI analysis |
| `TextExtractorService.cs` | Parses .eml/.msg, extracts EmailMetadata |
| `TextExtractionResult.cs` | Contains EmailMetadata property |
| `EmailMetadata.cs` | Email field definitions (Subject, From, To, Date, Body, Attachments) |
| `Models.cs` (Spaarke.Dataverse) | UpdateDocumentRequest with email properties |
| `DataverseServiceClientImpl.cs` | **THE ACTUAL PERSISTENCE LAYER** |
| `DataverseWebApiService.cs` | Alternative implementation (NOT used in production) |
| `Program.cs` | DI registration determines which service is used |

---

## Diagnostic Queries

### Check for Exceptions
```kusto
exceptions
| where timestamp > ago(15m)
| project timestamp, type, outerMessage
| order by timestamp desc
```

### Check Email Processing Logs
```kusto
traces
| where timestamp > ago(15m)
| where message contains 'Email' or message contains 'Document updated'
| project timestamp, message
| order by timestamp desc
```

### Verify Dataverse Field Type
```powershell
$token = az account get-access-token --resource https://YOUR_ORG.crm.dynamics.com --query accessToken -o tsv
$uri = "https://YOUR_ORG.api.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName='sprk_document')/Attributes?`$filter=LogicalName eq 'FIELD_NAME'&`$select=LogicalName,AttributeType"
Invoke-RestMethod -Uri $uri -Headers @{ Authorization = "Bearer $token" } -Method Get
```

### Get OptionSet Values
```powershell
$uri = "https://YOUR_ORG.api.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName='sprk_document')/Attributes(LogicalName='FIELD_NAME')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$expand=OptionSet"
$result = Invoke-RestMethod -Uri $uri -Headers @{ Authorization = "Bearer $token" } -Method Get
$result.OptionSet.Options | ForEach-Object { Write-Host "Value: $($_.Value) - Label: $($_.Label.LocalizedLabels[0].Label)" }
```

---

## Best Practices for Future Development

### 1. When Adding New Dataverse Fields

**Checklist**:
- [ ] Add column to Dataverse entity
- [ ] Add property to `UpdateDocumentRequest` in `Models.cs`
- [ ] Update **BOTH** Dataverse service implementations:
  - [ ] `DataverseServiceClientImpl.UpdateDocumentAsync()`
  - [ ] `DataverseWebApiService.UpdateDocumentAsync()` (if still maintained)
- [ ] Verify the field type matches (String, DateTime, OptionSet, etc.)
- [ ] For OptionSet fields, query Dataverse for actual values

### 2. OptionSet/Choice Fields

**Always**:
1. Query Dataverse for the actual OptionSet values before coding
2. Use `new OptionSetValue(int)` when setting via ServiceClient SDK
3. Create an enum with explicit values matching Dataverse exactly
4. Add a comment referencing the Dataverse field name

**Example**:
```csharp
/// <summary>
/// Analysis status values matching Dataverse sprk_filesummarystatus choice.
/// Values must match the Dataverse OptionSet exactly.
/// </summary>
public enum AnalysisStatus
{
    None = 100000000,
    Pending = 100000001,
    Completed = 100000002,
    // ... etc
}
```

### 3. Debugging Data Persistence Issues

**Step-by-step approach**:
1. Check App Insights for exceptions at the time of the operation
2. Look for the "Document updated" log - if missing, the update failed
3. Verify which `IDataverseService` implementation is registered in `Program.cs`
4. Check if the field is in the implementation's `UpdateDocumentAsync` method
5. For OptionSet fields, verify both type (`OptionSetValue`) and value range

### 4. Service Implementation Consistency

**Problem**: Having multiple implementations of the same interface where only one is used creates maintenance risk.

**Solutions**:
- Consider deprecating/removing `DataverseWebApiService` if not used
- Or create a shared mapping method both implementations call
- Add integration tests that verify all fields persist correctly

### 5. Enum Value Conventions

For Dataverse OptionSet fields:
- **Never** use sequential integers (0, 1, 2, 3...)
- **Always** use Power Platform standard range (100000000+)
- Query Dataverse metadata before creating the enum
- Include all options, even if not currently used (for forward compatibility)

---

## Testing Recommendations

### Manual Test
1. Upload a `.eml` file
2. Wait for AI analysis to complete
3. Check Dataverse record - all email fields should be populated

### Integration Test (Recommended Addition)
```csharp
[Fact]
public async Task UpdateDocumentAsync_WithEmailMetadata_PersistsAllFields()
{
    var request = new UpdateDocumentRequest
    {
        EmailSubject = "Test Subject",
        EmailFrom = "sender@test.com",
        EmailTo = "recipient@test.com",
        EmailDate = DateTime.UtcNow,
        EmailBody = "Test body content",
        Attachments = "[{\"filename\":\"test.pdf\"}]",
        SummaryStatus = (int)AnalysisStatus.Completed
    };

    await _dataverseService.UpdateDocumentAsync(documentId, request);

    var document = await _dataverseService.GetDocumentAsync(documentId);
    Assert.Equal("Test Subject", document.EmailSubject);
    // ... verify all fields
}
```

---

## Related ADRs

- **ADR-001**: Minimal API architecture
- **ADR-010**: DI minimalism (explains singleton registration pattern)
- **ADR-013**: AI Architecture (dual pipeline pattern)

---

## Timeline of Discovery

| Time | Event |
|------|-------|
| 21:02 | First upload - "Document updated" logged but no field count |
| 21:19 | PDF upload - worked (no email fields) |
| 21:30 | Email upload - "Incorrect attribute value type System.Int32" exception |
| 21:39 | Fixed OptionSetValue type, redeployed |
| 22:15 | Email upload - "Value 2 outside valid range" exception |
| 22:22 | Fixed enum values to match Dataverse, redeployed |
| 22:25 | Confirmed working - email fields now populate |

---

## Conclusion

This issue highlights the importance of:
1. Understanding which service implementation is actually used in production
2. Matching C# types exactly to Dataverse column types
3. Querying Dataverse metadata for OptionSet values rather than assuming
4. Maintaining consistency across multiple implementations of the same interface

When extending Dataverse integration in the future, always verify the complete data flow from endpoint to persistence layer, and ensure all implementations are updated.
