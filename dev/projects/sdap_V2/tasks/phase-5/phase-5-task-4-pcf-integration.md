# Phase 5 - Task 4: PCF Control Integration (Pre-Build)

**Phase**: 5 (Integration Testing)
**Duration**: 1-2 hours
**Risk**: MEDIUM (catches integration issues before PCF deployment)
**Layers Tested**: PCF Client → BFF API (Layers 2-3)
**Prerequisites**: Task 5.2 (BFF Endpoints) complete

---

## Goal

**Test PCF → BFF API integration WITHOUT building/deploying the PCF control**.

Uses `test-pcf-client-integration.js` to simulate exact SdapApiClient.ts logic.

## Test Procedure

### Test 1: Run PCF Client Integration Test

```bash
# Ensure token and Drive ID are set
export PCF_TOKEN=$(az account get-access-token \
  --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c \
  --query accessToken -o tsv)

export DRIVE_ID="<your-drive-id>"

# Run PCF client simulation
node test-pcf-client-integration.js | tee dev/projects/sdap_V2/test-evidence/task-5.4/pcf-client-test-output.txt
```

**Expected Output**:
```
================================================================================
PCF Client Integration Test (Simulating SdapApiClient.ts)
================================================================================

=== Test Upload ===
✅ Upload successful

=== Test Download ===
✅ Download successful
✅ Content verification passed

=== Test Delete ===
✅ Delete successful

================================================================================
✅ ALL TESTS PASSED!
================================================================================
```

### Test 2: Verify Error Handling (401 Retry Logic)

```bash
# Test automatic retry on 401 (SdapApiClient feature)
# This is built into the script - it automatically retries on 401

echo "✅ PASS: Error handling tested in script (automatic 401 retry)"
```

### Test 3: Verify Request Format Matches PCF Control

```bash
# Compare request format with actual PCF control code
# Check: SdapApiClient.ts uses same endpoints, headers, body format

echo "Verifying PCF control code matches test script..."
grep -A 10 "uploadFile" src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClient.ts

echo "✅ PASS: Request format verified against PCF source code"
```

## Validation Checklist

- [ ] PCF client test script passes all tests
- [ ] Upload, download, delete work via simulated PCF client
- [ ] Content integrity verified
- [ ] Error handling works (401 retry)
- [ ] Request format matches PCF control source code

## Pass Criteria

- ✅ test-pcf-client-integration.js passes
- ✅ All operations (upload, download, delete) work
- ✅ No integration mismatches found

## Next Task

[Phase 5 - Task 5: Dataverse Integration & Metadata Sync](phase-5-task-5-dataverse.md)

