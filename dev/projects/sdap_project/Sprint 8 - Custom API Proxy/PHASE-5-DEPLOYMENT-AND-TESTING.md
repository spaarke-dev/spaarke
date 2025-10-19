# Phase 5: Deployment and Testing

**Status**: 🔲 Not Started
**Duration**: 1 day
**Prerequisites**: Phase 4 complete, PCF control built successfully

---

## Phase Objectives

Deploy and test the complete Custom API Proxy solution end-to-end:
- Export and deploy Custom API Proxy solution to spaarkedev1
- Deploy updated PCF control (v2.1.0)
- Verify external service configuration
- Test all file operations end-to-end
- Performance testing
- Security validation
- Document test results

---

## Context for AI Vibe Coding

### What We're Testing

**End-to-End Flow**:
1. User opens model-driven app in Power Apps
2. User navigates to Documents view with Universal Dataset Grid
3. User selects document and clicks "Download" button
4. PCF control calls `sprk_ProxyDownloadFile` Custom API
5. Custom API plugin authenticates to Spe.Bff.Api
6. Spe.Bff.Api downloads file from SharePoint Embedded
7. File content returned through proxy to PCF
8. Browser downloads file

### Test Coverage
- Functional tests (all operations work)
- Error handling tests (invalid requests, permissions, API errors)
- Performance tests (latency, large files)
- Security tests (unauthorized access, token validation)

---

## Task Breakdown

### Task 5.1: Export Custom API Proxy Solution

**Objective**: Export managed solution from development environment.

**AI Instructions**:

1. **Package solution**:
```bash
cd src/dataverse/Spaarke.CustomApiProxy

pac solution pack \
  --zipfile ../../solutions/SpaarkeCustomApiProxy_managed.zip \
  --packagetype Managed \
  --folder .
```

2. **Verify solution package**:
```bash
ls -lh ../../solutions/SpaarkeCustomApiProxy_managed.zip
# Should be < 5 MB
```

3. **Backup current solution** (if redeploying):
```bash
# Download existing solution from Dataverse first
pac solution export \
  --name SpaarkeCustomApiProxy \
  --path ../../solutions/backup/SpaarkeCustomApiProxy_backup_$(date +%Y%m%d).zip \
  --managed
```

**Validation**:
- Solution ZIP file created
- Package size reasonable
- Backup created if redeploying

---

### Task 5.2: Deploy Custom API Proxy Solution

**Objective**: Import solution to spaarkedev1 environment.

**AI Instructions**:

1. **Connect to environment**:
```bash
pac auth create \
  --name spaarkedev1 \
  --url https://spaarkedev1.crm.dynamics.com \
  --tenant <tenant-id>
```

2. **Import solution**:
```bash
pac solution import \
  --path ../../solutions/SpaarkeCustomApiProxy_managed.zip \
  --async \
  --publish-changes
```

3. **Monitor import**:
```bash
# Get import job ID from previous command output
pac solution import-status --import-id <import-job-id>
```

4. **Verify solution imported**:
```bash
pac solution list --name SpaarkeCustomApiProxy
```

**Expected Output**:
```
Import completed successfully
All customizations published
```

**Validation**:
- Solution imported without errors
- All components present:
  - 2 entities (sprk_externalserviceconfig, sprk_proxyauditlog)
  - 4 Custom APIs
  - 4 plugins
- Customizations published

---

### Task 5.3: Verify Plugin Registration

**Objective**: Verify plugins are properly registered and associated with Custom APIs.

**AI Instructions**:

1. **Launch Plugin Registration Tool**:
```bash
pac tool prt
```

2. **Connect to spaarkedev1**

3. **Verify assembly registered**:
   - Find "Spaarke.Dataverse.CustomApiProxy" assembly
   - Check version matches build
   - Verify isolation mode: Sandbox
   - Verify location: Database

4. **Verify plugin steps**:
   - Each Custom API should have associated plugin
   - No additional steps needed (Custom APIs auto-execute)

5. **Test Custom API** in Plugin Registration Tool:
   - Right-click on Custom API → "Test"
   - Provide test parameters
   - Execute and verify response

**Validation**:
- Assembly registered
- All 4 plugins visible
- Custom APIs can be tested successfully

---

### Task 5.4: Configure External Service

**Objective**: Create/update external service configuration for Spe.Bff.Api.

**AI Instructions**:

1. **Get configuration values**:
```bash
# Get API_APP_ID
az keyvault secret show \
  --vault-name spaarke-dev-kv \
  --name API-APP-ID \
  --query value -o tsv

# Get Client Secret
az keyvault secret show \
  --vault-name spaarke-dev-kv \
  --name Dataverse--ClientSecret \
  --query value -o tsv

# Get Tenant ID
az account show --query tenantId -o tsv
```

2. **Navigate to model-driven app**:
   - Open https://spaarkedev1.crm.dynamics.com
   - Go to Advanced Settings → Settings → Custom Entities
   - Find "External Service Configuration"

3. **Create/Update "SpeBffApi" configuration**:

| Field | Value |
|-------|-------|
| Name | SpeBffApi |
| Display Name | SPE BFF API |
| Base URL | https://spe-api-dev-67e2xz.azurewebsites.net |
| Description | SharePoint Embedded Backend-for-Frontend API |
| Authentication Type | Client Credentials |
| Tenant ID | (from Azure) |
| Client ID | (API_APP_ID from Key Vault) |
| Client Secret | (from Key Vault) |
| Scope | api://{API_APP_ID}/.default |
| Timeout | 300 |
| Retry Count | 3 |
| Retry Delay | 1000 |
| Is Enabled | Yes |
| Health Status | Healthy |

4. **Save and close**

**Validation**:
- Configuration record exists
- All required fields populated
- Client secret is masked in UI
- Status is "Enabled"

---

### Task 5.5: Deploy Updated PCF Control

**Objective**: Deploy PCF control v2.1.0 with Custom API integration.

**AI Instructions**:

1. **Disable Directory.Packages.props** (from Sprint 6 learning):
```bash
cd c:/code_files/spaarke

# Check if file exists
if [ -f "Directory.Packages.props" ]; then
    mv Directory.Packages.props Directory.Packages.props.disabled
    echo "Directory.Packages.props disabled"
fi
```

2. **Build PCF control in production mode**:
```bash
cd src/controls/UniversalDatasetGrid

# Clean build
npm run clean

# Production build
npm run build:prod
```

3. **Deploy using pac pcf push**:
```bash
pac pcf push \
  --publisher-prefix sprk \
  --environment spaarkedev1
```

4. **Monitor deployment**:
```bash
# Watch for "Successfully imported solution" message
```

5. **Re-enable Directory.Packages.props**:
```bash
cd c:/code_files/spaarke

if [ -f "Directory.Packages.props.disabled" ]; then
    mv Directory.Packages.props.disabled Directory.Packages.props
    echo "Directory.Packages.props re-enabled"
fi
```

**Expected Output**:
```
Building PCF control...
Packaging solution...
Importing solution to spaarkedev1...
Successfully imported solution
Control version: 2.1.0
```

**Validation**:
- Control deployed successfully
- Version 2.1.0 visible in Power Apps
- No build or deployment errors

---

### Task 5.6: Functional Testing

**Objective**: Test all file operations end-to-end in spaarkedev1.

**AI Instructions**:

**Test Environment Setup**:
1. Open model-driven app: https://spaarkedev1.crm.dynamics.com
2. Navigate to Documents entity
3. Ensure Universal Dataset Grid is on the view
4. Have test documents ready with files attached

**Test Cases**:

#### Test 5.6.1: Download File
**Steps**:
1. Select document row with file attached
2. Click "Download" button
3. Verify browser download dialog appears
4. Verify file downloads successfully
5. Verify file content is correct (open file)

**Expected Result**:
- ✅ Download dialog appears
- ✅ File downloads completely
- ✅ File content matches original
- ✅ No errors in console

**Validation Data**:
- Record correlation ID from console logs
- Check `sprk_proxyauditlog` for audit entry
- Verify audit log shows success and correct duration

#### Test 5.6.2: Delete File
**Steps**:
1. Select document row with file attached
2. Click "Delete" button
3. Confirm deletion in dialog
4. Wait for operation to complete
5. Verify grid refreshes
6. Verify file columns cleared

**Expected Result**:
- ✅ Confirmation dialog appears
- ✅ File deleted from SPE
- ✅ Grid refreshes automatically
- ✅ Document record updated (Has File = false)
- ✅ No errors

**Validation Data**:
- Check `sprk_document` record: `sprk_hasfile` should be false
- Check audit log shows delete operation
- Verify file no longer accessible in SPE

#### Test 5.6.3: Replace File
**Steps**:
1. Select document row with file attached
2. Click "Replace" button
3. Select new file from file picker
4. Wait for operation to complete
5. Verify grid refreshes
6. Download file and verify it's the new file

**Expected Result**:
- ✅ File picker appears
- ✅ File uploads successfully
- ✅ Grid refreshes showing new file metadata
- ✅ Document record updated with new filename
- ✅ Downloaded file matches uploaded file

**Validation Data**:
- Check `sprk_document` record: `sprk_filename` should be new filename
- Check audit log shows replace operation
- Verify file content in SPE

#### Test 5.6.4: Upload File
**Steps**:
1. Select document row WITHOUT file attached
2. Click "Upload" or "Add File" button
3. Select file from file picker
4. Wait for operation to complete
5. Verify grid refreshes
6. Verify document now has file

**Expected Result**:
- ✅ File picker appears
- ✅ File uploads successfully
- ✅ Grid refreshes showing new file
- ✅ Document record updated (Has File = true)
- ✅ Download URL populated

**Validation Data**:
- Check `sprk_document` record: all file fields populated
- Check audit log shows upload operation
- Verify file accessible in SPE

---

### Task 5.7: Error Handling Testing

**Objective**: Test error scenarios and verify proper error handling.

**AI Instructions**:

#### Test 5.7.1: Invalid Document ID
**Steps**:
1. Manually call Custom API with invalid document ID
2. Verify proper error message returned

**Expected Result**:
- ❌ Operation fails with clear error message
- ✅ Error message: "User does not have access to document..."
- ✅ No system crash or generic errors

#### Test 5.7.2: Unauthorized User
**Steps**:
1. Login as user without access to document
2. Try to download file
3. Verify permission error

**Expected Result**:
- ❌ Operation fails
- ✅ Error message: "You do not have permission..."
- ✅ Audit log records failed attempt

#### Test 5.7.3: External API Down
**Steps**:
1. Temporarily disable Spe.Bff.Api (or use invalid URL in config)
2. Try to download file
3. Verify retry logic and error handling

**Expected Result**:
- ✅ Plugin retries 3 times
- ❌ Operation fails after retries
- ✅ Error message: "Server error occurred..."
- ✅ Audit log shows failure

#### Test 5.7.4: Invalid File Content
**Steps**:
1. Try to upload file with invalid Base64 encoding
2. Verify error handling

**Expected Result**:
- ❌ Operation fails
- ✅ Error message: "Invalid file content"
- ✅ No data corruption

---

### Task 5.8: Performance Testing

**Objective**: Test performance with various file sizes and concurrent operations.

**AI Instructions**:

#### Test 5.8.1: Small File (< 1 MB)
**Steps**:
1. Download small PDF file
2. Measure time from button click to download complete
3. Record duration from audit log

**Expected Result**:
- ✅ Operation completes in < 2 seconds
- ✅ No performance issues

#### Test 5.8.2: Medium File (1-10 MB)
**Steps**:
1. Download medium-sized file
2. Measure performance

**Expected Result**:
- ✅ Operation completes in < 5 seconds
- ✅ No timeout errors

#### Test 5.8.3: Large File (10-50 MB)
**Steps**:
1. Download large file
2. Measure performance
3. Verify no timeout

**Expected Result**:
- ✅ Operation completes in < 30 seconds
- ✅ No memory issues
- ⚠️ Consider showing progress indicator for large files

#### Test 5.8.4: Concurrent Operations
**Steps**:
1. Open multiple browser tabs
2. Trigger downloads simultaneously
3. Verify all complete successfully

**Expected Result**:
- ✅ All operations complete
- ✅ No race conditions
- ✅ No Dataverse API limit errors

---

### Task 5.9: Security Validation

**Objective**: Verify security controls are working properly.

**AI Instructions**:

#### Security Checklist:

- [ ] **Authentication**:
  - Custom API requires valid Dataverse session
  - Cannot call Custom API without authentication
  - Tokens never exposed to client-side JavaScript

- [ ] **Authorization**:
  - Users can only access documents they have permissions for
  - Custom API validates document access before proxying
  - Row-level security enforced

- [ ] **Data Security**:
  - Client secrets not visible in UI
  - Audit logs redact sensitive data
  - File content never logged

- [ ] **API Security**:
  - External API (Spe.Bff.Api) validates tokens
  - Service-to-service auth uses dedicated service principal
  - Tokens cached securely

- [ ] **Transport Security**:
  - All requests over HTTPS
  - Certificates valid

**Validation**:
- All security checks pass
- No security warnings in browser console
- Audit logs show proper access control

---

### Task 5.10: Document Test Results

**Objective**: Create test results documentation.

**AI Instructions**:

Create file: `dev/projects/sdap_project/Sprint 8 - Custom API Proxy/PHASE-5-TEST-RESULTS.md`

**Template**:

```markdown
# Phase 5: Test Results

**Test Date**: [Date]
**Environment**: spaarkedev1.crm.dynamics.com
**Tester**: [Name]

## Deployment Verification

- [x] Custom API Proxy solution deployed successfully
- [x] PCF control v2.1.0 deployed successfully
- [x] External service configured
- [x] Plugins registered

## Functional Test Results

### Download File
- **Status**: ✅ Pass / ❌ Fail
- **Duration**: [X] seconds
- **File Size**: [X] MB
- **Notes**: [Any observations]

### Delete File
- **Status**: ✅ Pass / ❌ Fail
- **Notes**: [Any observations]

### Replace File
- **Status**: ✅ Pass / ❌ Fail
- **Notes**: [Any observations]

### Upload File
- **Status**: ✅ Pass / ❌ Fail
- **Notes**: [Any observations]

## Error Handling Test Results

### Invalid Document ID
- **Status**: ✅ Pass / ❌ Fail
- **Error Message**: [Error message displayed]

### Unauthorized User
- **Status**: ✅ Pass / ❌ Fail

### External API Down
- **Status**: ✅ Pass / ❌ Fail
- **Retry Attempts**: [X]

### Invalid File Content
- **Status**: ✅ Pass / ❌ Fail

## Performance Test Results

### Small File (< 1 MB)
- **Duration**: [X] seconds
- **Status**: ✅ Pass / ❌ Fail

### Medium File (1-10 MB)
- **Duration**: [X] seconds
- **Status**: ✅ Pass / ❌ Fail

### Large File (10-50 MB)
- **Duration**: [X] seconds
- **Status**: ✅ Pass / ❌ Fail

### Concurrent Operations
- **Status**: ✅ Pass / ❌ Fail
- **Notes**: [Observations]

## Security Validation Results

- [x] Authentication working
- [x] Authorization enforced
- [x] Data security verified
- [x] API security validated
- [x] Transport security confirmed

## Issues Found

### Issue 1: [Title]
- **Severity**: Critical / High / Medium / Low
- **Description**: [Description]
- **Steps to Reproduce**: [Steps]
- **Workaround**: [If any]
- **Status**: Open / Fixed

### Issue 2: [Title]
...

## Overall Assessment

**Status**: ✅ Ready for Production / ⚠️ Issues to Address / ❌ Significant Problems

**Summary**: [Overall summary of test results]

**Recommendations**: [Any recommendations]
```

**Validation**:
- Test results documented
- All test cases executed
- Issues tracked
- Overall assessment provided

---

## Deliverables

✅ Custom API Proxy solution deployed to spaarkedev1
✅ PCF control v2.1.0 deployed
✅ External service configured
✅ All functional tests passed
✅ Error handling validated
✅ Performance acceptable
✅ Security validated
✅ Test results documented

---

## Validation Checklist

- [ ] Solution deployed without errors
- [ ] All Custom APIs callable
- [ ] PCF control shows v2.1.0
- [ ] Download file works end-to-end
- [ ] Delete file works end-to-end
- [ ] Replace file works end-to-end
- [ ] Upload file works end-to-end
- [ ] Error messages user-friendly
- [ ] Performance acceptable (< 5 seconds for typical files)
- [ ] Security controls working
- [ ] Audit logs capturing operations
- [ ] Test results documented

---

## Next Steps

Proceed to **Phase 6: Documentation and Handoff**

**Phase 6 will**:
- Complete architecture documentation
- Create deployment runbook
- Document how to add new external services
- Create troubleshooting guide
- Update Sprint 7A completion status
- Handoff to team

---

## Knowledge Resources

### Internal Documentation
- [Phase 4 PCF Integration](./PHASE-4-PCF-INTEGRATION.md)
- [Sprint 6 Deployment Guide](../Sprint%206/TASK-2-DEPLOYMENT-COMPLETE.md)
- [Dataverse Authentication Guide](../../../docs/DATAVERSE-AUTHENTICATION-GUIDE.md)

### External Resources
- [Solution Lifecycle Management](https://learn.microsoft.com/en-us/power-platform/alm/)
- [PCF Deployment](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/publish-components-framework-controls)
- [Testing Best Practices](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/best-practices/)

---

## Notes for AI Vibe Coding

**Deployment Tips**:

1. **Always disable Directory.Packages.props**: This is critical for PCF deployment
2. **Use pac pcf push**: Faster than solution pack/import for PCF-only changes
3. **Monitor import jobs**: Use `--async` and check status to avoid timeout
4. **Backup before redeployment**: Always export existing solution first

**Testing Tips**:

1. **Clear browser cache**: Between deployments to ensure latest version
2. **Use incognito mode**: To avoid caching issues
3. **Check console logs**: For detailed error information
4. **Verify audit logs**: For complete operation tracing

**Common Issues**:

1. **"Operation timed out"**: Increase timeout in external service config
2. **"Failed to acquire token"**: Check client secret and scope
3. **"User does not have access"**: Check security roles and document permissions
4. **"Invalid Base64"**: Check file encoding in PCF control
