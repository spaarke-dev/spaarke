# Step 5: Testing and Validation

**Phase**: 5 of 5
**Duration**: ~2 hours
**Prerequisites**: Step 4 completed (deployed and integrated)

---

## Overview

Perform comprehensive testing to validate the complete SPE File Viewer solution. This includes functional testing, performance testing, error scenario testing, and audit log verification.

**Test Categories**:
1. End-to-end functional testing
2. Custom API direct testing
3. Audit log verification
4. Performance testing
5. Error scenario testing
6. Security validation

---

## Task 5.1: End-to-End Functional Testing

**Goal**: Verify complete user workflow from upload to viewing

### Test Case 1: View Existing Document (PDF)

**Prerequisites**: Existing Document record with PDF file

**Steps**:
1. Navigate to **Documents** in Spaarke app
2. Open Document record with PDF
3. Observe file viewer section

**Expected Results**:
- [ ] Form loads without errors
- [ ] File Preview section visible
- [ ] Loading spinner appears briefly (< 3 seconds)
- [ ] PDF displays in iframe
- [ ] File name appears above viewer
- [ ] PDF toolbar visible (zoom, page navigation)
- [ ] No console errors

**Debug Info**:
```javascript
// Open browser console (F12)
// Check for successful API calls:
// "[CustomApiService] Getting preview URL for document: {guid}"
// "[CustomApiService] Custom API response: {...}"
// "[FileViewer] Preview URL retrieved: {...}"
```

---

### Test Case 2: View Office Document (Word/Excel/PowerPoint)

**Prerequisites**: Document record with .docx, .xlsx, or .pptx file

**Steps**:
1. Open Document record with Office file
2. Observe Office Online viewer

**Expected Results**:
- [ ] Office Online loads in iframe
- [ ] Document displays with Office toolbar
- [ ] Document is **editable** (can type, make changes)
- [ ] Ribbon/menu visible at top
- [ ] Save/AutoSave functionality works
- [ ] No authentication prompts

**Verification**:
- Try editing document content
- Verify changes persist after save
- Refresh page - changes should remain

---

### Test Case 3: Upload New Document and Auto-View

**Prerequisites**: Matter record with upload capability

**Steps**:
1. Navigate to **Matter** record
2. Click **Add Documents** or use Quick Create
3. Upload new PDF or Office file
4. Save and create Document
5. Immediately open the new Document record

**Expected Results**:
- [ ] Document record created successfully
- [ ] File viewer loads automatically on form
- [ ] Uploaded file displays immediately
- [ ] No delay or manual refresh needed

---

### Test Case 4: Auto-Refresh Preview URL

**Prerequisites**: Document with file that takes 10+ minutes to test

**Steps**:
1. Open Document record
2. Note the time file loads
3. Wait **9 minutes** (preview URLs expire in ~10 minutes)
4. Observe auto-refresh behavior

**Expected Results**:
- [ ] Console logs: "Auto-refreshing preview URL" at ~9 min mark
- [ ] New preview URL fetched automatically
- [ ] Iframe reloads seamlessly
- [ ] No user intervention required
- [ ] File continues to display without interruption

**Debug Console Output**:
```
[FileViewer] Scheduling refresh in 9 minutes
... (9 minutes later) ...
[FileViewer] Auto-refreshing preview URL
[CustomApiService] Getting preview URL for document: {guid}
[FileViewer] Preview URL retrieved: {...}
```

---

### Test Case 5: Multiple File Types

Test with various file types:

| File Type | Extension | Expected Behavior |
|-----------|-----------|-------------------|
| PDF | `.pdf` | Browser PDF viewer |
| Word | `.docx` | Office Online (editable) |
| Excel | `.xlsx` | Office Online (editable) |
| PowerPoint | `.pptx` | Office Online (slide show + edit) |
| Image | `.png`, `.jpg` | Image preview |
| Video | `.mp4` | Video player (if supported) |

**Expected Results**:
- [ ] All file types render appropriately
- [ ] No "file type not supported" errors
- [ ] Office files open in edit mode by default

---

## Task 5.2: Custom API Direct Testing

**Goal**: Test Custom API independently of PCF control

### Test via Browser Console

Open any Document record and run:

```javascript
// Get document ID from form
const documentId = Xrm.Page.data.entity.getId().replace(/[{}]/g, '');

// Call Custom API
Xrm.WebApi.online.execute({
    getMetadata: function() {
        return {
            boundParameter: "entity",
            parameterTypes: {
                "entity": {
                    "typeName": "mscrm.sprk_document",
                    "structuralProperty": 5
                }
            },
            operationType: 1,
            operationName: "sprk_GetFilePreviewUrl"
        };
    },
    entity: {
        entityType: "sprk_document",
        id: documentId
    }
}).then(
    result => {
        console.log("✅ Custom API Test Result:", result);

        // Validate response structure
        console.assert(result.PreviewUrl, "PreviewUrl missing");
        console.assert(result.FileName, "FileName missing");
        console.assert(result.ExpiresAt, "ExpiresAt missing");
        console.assert(result.CorrelationId, "CorrelationId missing");

        // Check URL validity
        console.assert(result.PreviewUrl.startsWith("https://"), "Invalid PreviewUrl");

        // Check expiration time
        const expiresAt = new Date(result.ExpiresAt);
        const now = new Date();
        const minutesUntilExpiration = (expiresAt - now) / 1000 / 60;
        console.log(`Expires in ${minutesUntilExpiration.toFixed(1)} minutes`);
        console.assert(minutesUntilExpiration > 5 && minutesUntilExpiration < 15, "Expiration time out of expected range");

        console.log("All assertions passed! ✅");
    },
    error => {
        console.error("❌ Custom API Error:", error.message);
    }
);
```

**Expected Output**:
```javascript
✅ Custom API Test Result:
{
  PreviewUrl: "https://spaarke.sharepoint.com/...",
  FileName: "document.pdf",
  FileSize: 102400,
  ContentType: "application/pdf",
  ExpiresAt: "2025-01-21T16:30:00Z",
  CorrelationId: "abc123-def456-789ghi"
}
Expires in 9.5 minutes
All assertions passed! ✅
```

**Validation**:
- [ ] PreviewUrl is valid HTTPS URL
- [ ] FileName matches actual file
- [ ] ExpiresAt is ~10 minutes in future
- [ ] CorrelationId is unique GUID format

---

### Test via REST API (Postman/curl)

```bash
# Get access token
TOKEN=$(az account get-access-token \
    --resource https://your-org.crm.dynamics.com \
    --query accessToken -o tsv)

# Call Custom API
curl -X GET \
    "https://your-org.crm.dynamics.com/api/data/v9.2/sprk_documents({document-id})/Microsoft.Dynamics.CRM.sprk_GetFilePreviewUrl()" \
    -H "Authorization: Bearer $TOKEN" \
    -H "Accept: application/json"
```

**Expected Response (200 OK)**:
```json
{
  "@odata.context": "...",
  "PreviewUrl": "https://...",
  "FileName": "document.pdf",
  "FileSize": 102400,
  "ContentType": "application/pdf",
  "ExpiresAt": "2025-01-21T16:30:00Z",
  "CorrelationId": "..."
}
```

---

## Task 5.3: Audit Log Verification

**Goal**: Verify all Custom API calls are logged to audit table

### Query Audit Logs

```javascript
// In browser console on Dataverse form
Xrm.WebApi.retrieveMultipleRecords(
    "sprk_proxyauditlog",
    "?$filter=sprk_operation eq 'GetFilePreviewUrl'&$orderby=sprk_executiontime desc&$top=10"
).then(
    result => {
        console.log("📋 Recent Audit Logs:", result.entities);

        result.entities.forEach(log => {
            console.log(`
Correlation ID: ${log.sprk_correlationid}
Operation: ${log.sprk_operation}
Success: ${log.sprk_success}
Duration: ${log.sprk_duration}ms
Execution Time: ${log.sprk_executiontime}
User: ${log._sprk_userid_value}
            `);
        });
    },
    error => console.error("Error fetching audit logs:", error)
);
```

**Expected Output**:
```
📋 Recent Audit Logs: [10 records]

Correlation ID: abc123-def456
Operation: GetFilePreviewUrl
Success: true
Duration: 342ms
Execution Time: 2025-01-21T15:20:00Z
User: {user-guid}

...
```

**Validation**:
- [ ] Audit logs exist for each Custom API call
- [ ] Correlation ID matches between call and log
- [ ] Success/failure status recorded correctly
- [ ] Duration is reasonable (< 5 seconds)
- [ ] User ID captured correctly
- [ ] Request/response payloads logged (sensitive data redacted)

---

### Verify Sensitive Data Redaction

Check that secrets are not logged:

```javascript
// Query recent audit logs
Xrm.WebApi.retrieveMultipleRecords(
    "sprk_proxyauditlog",
    "?$filter=sprk_operation eq 'GetFilePreviewUrl'&$top=1"
).then(result => {
    const log = result.entities[0];
    const requestPayload = log.sprk_requestpayload;
    const responsePayload = log.sprk_responsepayload;

    console.log("Request Payload:", requestPayload);
    console.log("Response Payload:", responsePayload);

    // Check for redacted secrets
    console.assert(
        !requestPayload.includes("Bearer "),
        "⚠️ Access token not redacted!"
    );
});
```

**Expected**: No access tokens, client secrets, or other sensitive data in logs

---

## Task 5.4: Performance Testing

**Goal**: Verify acceptable performance under various conditions

### Test 1: Initial Load Time

**Metric**: Time from form open to file display

**Steps**:
1. Open Document form (clear browser cache first)
2. Measure time using browser DevTools Performance tab

**Expected Results**:
- [ ] Total load time < 3 seconds
- [ ] Custom API call < 2 seconds
- [ ] File preview renders < 1 second after URL received

**Measurement**:
```javascript
// Add to FileViewer.tsx for timing
const startTime = performance.now();

// After file loads:
const loadTime = performance.now() - startTime;
console.log(`File loaded in ${loadTime.toFixed(0)}ms`);
```

---

### Test 2: Concurrent User Load

**Metric**: System performance with 10+ users viewing files simultaneously

**Steps**:
1. Have 10 users open Document forms at the same time
2. Monitor SDAP BFF API performance in Azure
3. Check Dataverse plugin trace logs

**Expected Results**:
- [ ] All users get preview URLs successfully
- [ ] No timeout errors
- [ ] API response time < 3 seconds for all requests
- [ ] No rate limiting errors

**Azure Monitoring**:
```bash
# Check API metrics
az monitor metrics list \
    --resource /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Web/sites/spe-api-dev-67e2xz \
    --metric "Requests,ResponseTime,Http5xx" \
    --start-time "2025-01-21T15:00:00Z" \
    --end-time "2025-01-21T16:00:00Z"
```

---

### Test 3: Large File Performance

**Metric**: Preview load time for files of varying sizes

| File Size | Expected Load Time |
|-----------|-------------------|
| < 1 MB | < 2 seconds |
| 1-10 MB | < 4 seconds |
| 10-50 MB | < 6 seconds |
| > 50 MB | < 10 seconds |

**Test Files**:
- Small PDF (500 KB)
- Medium Office doc (5 MB)
- Large presentation (25 MB)

**Expected**: Preview URLs load consistently regardless of file size (URL retrieval doesn't download file)

---

### Test 4: Network Latency Simulation

**Metric**: Behavior under slow network conditions

**Steps**:
1. Open Chrome DevTools → Network tab
2. Set throttling to "Slow 3G"
3. Open Document form with file

**Expected Results**:
- [ ] Loading spinner shows immediately
- [ ] User not blocked during load
- [ ] Timeout after 30 seconds (graceful error)
- [ ] Retry option available

---

## Task 5.5: Error Scenario Testing

**Goal**: Verify graceful error handling

### Error Test 1: Document Without File

**Scenario**: Document record has no associated SPE file

**Steps**:
1. Create Document record without uploading file
2. Open Document form

**Expected Results**:
- [ ] PCF control shows error message: "No file associated with this document"
- [ ] Error is user-friendly (not technical stack trace)
- [ ] Form remains functional
- [ ] No console errors that break form

---

### Error Test 2: External Service Config Missing

**Scenario**: SDAP_BFF_API config record deleted or disabled

**Steps**:
1. Disable External Service Config record (sprk_isenabled = false)
2. Open Document form with file

**Expected Results**:
- [ ] Error message: "Service configuration unavailable"
- [ ] Plugin trace log shows: "External service config not found or disabled: SDAP_BFF_API"
- [ ] Audit log records failure
- [ ] User can still access other form features

**Cleanup**: Re-enable External Service Config after test

---

### Error Test 3: SDAP BFF API Down

**Scenario**: SDAP BFF API is unreachable

**Steps**:
1. Stop Azure App Service temporarily
   ```bash
   az webapp stop --resource-group {rg} --name spe-api-dev-67e2xz
   ```
2. Open Document form with file
3. Observe error handling

**Expected Results**:
- [ ] Error message: "Unable to connect to file service"
- [ ] Retry button available
- [ ] Plugin retries 3 times with exponential backoff
- [ ] Final failure after ~10 seconds
- [ ] Audit log records failure with correlation ID

**Cleanup**: Restart App Service
```bash
az webapp start --resource-group {rg} --name spe-api-dev-67e2xz
```

---

### Error Test 4: Invalid Document ID

**Scenario**: Custom API called with non-existent document

**Steps**:
```javascript
// In browser console
Xrm.WebApi.online.execute({
    getMetadata: () => ({
        boundParameter: "entity",
        parameterTypes: {
            "entity": {
                "typeName": "mscrm.sprk_document",
                "structuralProperty": 5
            }
        },
        operationType: 1,
        operationName: "sprk_GetFilePreviewUrl"
    }),
    entity: {
        entityType: "sprk_document",
        id: "00000000-0000-0000-0000-000000000000"  // Invalid ID
    }
}).catch(error => console.error(error));
```

**Expected Results**:
- [ ] BFF API returns 404 Not Found
- [ ] Error message: "Document not found"
- [ ] No server crash or unhandled exception

---

### Error Test 5: Token Expiration

**Scenario**: Service principal token expires during request

**Expected Results**:
- [ ] Plugin automatically acquires new token (Azure.Identity handles refresh)
- [ ] Request completes successfully after token refresh
- [ ] No user-visible error

---

## Task 5.6: Security Validation

**Goal**: Verify access control and security measures

### Security Test 1: Unauthorized Document Access

**Scenario**: User tries to view document they don't have access to

**Prerequisites**: Two users - User A has access, User B does not

**Steps**:
1. As User B, directly navigate to Document record URL (owned by User A)
2. Attempt to open Document form

**Expected Results**:
- [ ] User B sees form but file viewer shows error
- [ ] Error message: "You do not have permission to view this file"
- [ ] BFF API validates user access via Spaarke UAC
- [ ] Audit log records unauthorized attempt
- [ ] No file URL exposed to unauthorized user

---

### Security Test 2: URL Expiration Security

**Scenario**: Verify preview URLs cannot be used after expiration

**Steps**:
1. Get preview URL from Custom API
2. Wait 15 minutes (URLs expire in ~10 min)
3. Try to access expired URL directly

**Expected Results**:
- [ ] URL returns 403 Forbidden or 404 Not Found
- [ ] No file content exposed via expired URL
- [ ] User must request new preview URL

---

### Security Test 3: CORS Protection

**Scenario**: Verify API cannot be called from unauthorized origins

**Steps**:
1. Create test HTML page on different domain
2. Attempt to call SDAP BFF API from that page

**Expected Results**:
- [ ] Browser blocks request with CORS error
- [ ] Only requests from `*.dynamics.com` and `*.powerapps.com` allowed

---

## Final Validation Checklist

### Functional Requirements
- [ ] PDF files display in browser viewer
- [ ] Office files open in Office Online (editable mode)
- [ ] File name displays correctly
- [ ] Auto-refresh works before URL expiration
- [ ] Loading states show appropriately
- [ ] Error messages are user-friendly

### Non-Functional Requirements
- [ ] Initial load time < 3 seconds
- [ ] Custom API response time < 2 seconds
- [ ] No console errors in normal operation
- [ ] Works in Chrome, Edge, Firefox
- [ ] Mobile responsive (works on tablets)

### Security Requirements
- [ ] Unauthorized users cannot view files
- [ ] Preview URLs expire after ~10 minutes
- [ ] Sensitive data redacted in audit logs
- [ ] CORS protection enabled
- [ ] App-only authentication working correctly

### Operational Requirements
- [ ] Audit logs capture all requests
- [ ] Correlation IDs enable request tracing
- [ ] Error scenarios logged properly
- [ ] Plugin trace logs available for debugging
- [ ] Azure App Insights tracking API calls

---

## Test Report Template

Document test results:

```markdown
# SPE File Viewer - Test Report

**Date**: 2025-01-21
**Tester**: {Your Name}
**Environment**: SPAARKE DEV 1

## Summary
- Total Tests: 25
- Passed: 24
- Failed: 1
- Blocked: 0

## Test Results

### Functional Tests (8/8 passed)
✅ View PDF file
✅ View Office file (Word)
✅ View Office file (Excel)
✅ Upload and auto-view
✅ Auto-refresh preview URL
✅ Multiple file types
✅ File name display
✅ Loading spinner

### API Tests (3/3 passed)
✅ Custom API via browser console
✅ Custom API via REST
✅ Response structure validation

### Audit Tests (2/2 passed)
✅ Audit logs created
✅ Sensitive data redacted

### Performance Tests (4/4 passed)
✅ Initial load < 3 seconds
✅ Large file handling
✅ Concurrent users (10+)
✅ Network latency handling

### Error Tests (5/5 passed)
✅ Document without file
✅ Service config missing
✅ API down
✅ Invalid document ID
✅ Token expiration

### Security Tests (3/3 passed)
✅ Unauthorized access blocked
✅ URL expiration enforced
✅ CORS protection

## Issues Found
1. **Minor**: File name truncated for very long names
   - **Severity**: Low
   - **Workaround**: Tooltip shows full name on hover
   - **Fix**: TBD in future release

## Recommendations
- Add loading progress indicator for large files
- Consider caching preview URLs for 5 minutes client-side
- Add "Open in New Tab" button for full-screen viewing

## Sign-Off
**Tester**: ___________________
**Date**: ___________________
```

---

## Success Criteria

The SPE File Viewer solution is **PRODUCTION READY** when:

1. ✅ All functional tests pass
2. ✅ Performance meets SLA (< 3 second load)
3. ✅ Security validation passes
4. ✅ Audit logging works correctly
5. ✅ Error handling is graceful
6. ✅ User acceptance testing successful
7. ✅ Documentation complete
8. ✅ No critical or high severity bugs

---

## Next Steps After Testing

1. **Production Deployment**:
   - Deploy SDAP BFF API to production Azure App Service
   - Import PCF solution to production Dataverse
   - Configure production External Service Config
   - Update production Document forms

2. **User Training**:
   - Create user guide for viewing files
   - Demonstrate Office Online editing
   - Explain auto-refresh behavior

3. **Monitoring Setup**:
   - Configure Azure Application Insights alerts
   - Set up Dataverse audit log monitoring
   - Create dashboard for API metrics

4. **Documentation**:
   - Update system architecture docs
   - Document troubleshooting procedures
   - Create runbook for operations team

---

**Testing Complete! 🎉**

The SPE File Viewer solution is ready for production deployment.
