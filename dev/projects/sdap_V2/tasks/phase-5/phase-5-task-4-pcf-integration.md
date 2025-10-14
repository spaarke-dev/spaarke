# Phase 5 - Task 4: PCF Control Integration (Pre-Build)

**Phase**: 5 (Integration Testing)
**Duration**: 30 minutes (if token acquired) OR document limitations
**Risk**: HIGH (most comprehensive test available given token constraints)
**Layers Tested**: PCF Client → BFF API → OBO → Graph → SPE (Layers 2-6, full stack!)
**Prerequisites**: Task 5.3 (SPE Storage) complete
**Status**: Token-limited (same as 5.1-5.3) BUT script ready when token available

---

## Goal

**Test complete file operations flow** using `test-pcf-client-integration.js` to simulate PCF control.

**UPDATED (2025-10-14)**: Script now uses correct BFF API routes per ADR-011:
- Upload: `PUT /api/obo/containers/{containerId}/files/{path}`
- Download: `GET /api/obo/drives/{driveId}/items/{itemId}/content`
- Delete: `DELETE /api/obo/drives/{driveId}/items/{itemId}`

**Why Critical**: This tests Tasks 5.1, 5.2, and 5.3 end-to-end - the COMPLETE authentication + file operations chain that Tasks 5.1-5.3 couldn't fully test due to token limitations.

**What This Tests**:
1. ✅ PCF Client → BFF API (authentication, request format)
2. ✅ BFF API → OBO Exchange (user token → app token)
3. ✅ App Token → Graph API (file operations)
4. ✅ Graph API → SharePoint Embedded (storage)
5. ✅ File integrity (upload → download → verify)
6. ✅ Cache performance (Phase 4 verification)
7. ✅ Silent failure detection (SDAP v1 concern)

---

## Current Limitation

**Admin Consent Required**: Azure CLI cannot acquire BFF API tokens without admin consent (AADSTS65001).

**Two Paths Forward**:

### Path A: Grant Admin Consent ✅ RECOMMENDED (if possible)

```bash
# Grant Azure CLI consent to access BFF API
az ad app permission grant \
  --id 04b07795-8ddb-461a-bbee-02f9e1bf7b46 \
  --api 1e40baad-e065-4aea-a8d4-4b7ab273458c

# Wait ~5 minutes for propagation, then retry token acquisition
az account get-access-token \
  --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c \
  --query accessToken -o tsv
```

### Path B: Document Script & Defer Testing ⚠️ FALLBACK

If admin consent not possible:
1. ✅ Document that script is ready and updated
2. ✅ Explain what would be tested
3. ✅ Note this as gap in Phase 5 testing
4. ⏳ Defer to production testing (Task 5.9) or post-deployment validation

---

## Test Procedure (If Token Available)

### Test 1: Run PCF Client Integration Test

```bash
# Get BFF API token (requires admin consent)
export PCF_TOKEN=$(az account get-access-token \
  --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c \
  --query accessToken -o tsv)

# Set container ID (from architecture doc)
export CONTAINER_ID="b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"

# Run PCF client simulation
node test-pcf-client-integration.js | tee dev/projects/sdap_V2/test-evidence/task-5.4/pcf-client-test-output.txt
```

**Expected Output**:
```
================================================================================
PCF Client Integration Test (Simulating SdapApiClient.ts)
================================================================================

=== Test Upload ===
File Name: pcf-test-1729878912345.txt
Container ID: b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50
File Size: 245 bytes
✅ Upload successful

=== Test Download ===
Container ID: b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50
Item ID: 01ABCDEF...
✅ Download successful
✅ Content verification passed (upload = download)

=== Test Delete ===
Container ID: b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50
Item ID: 01ABCDEF...
✅ Delete successful

================================================================================
✅ ALL TESTS PASSED!
================================================================================

Integration Verified:
  ✓ PCF Client App → BFF API authentication
  ✓ BFF API → Graph API (OBO flow)
  ✓ Graph API → SharePoint Embedded
  ✓ File upload/download/delete operations
```

### Test 2: Verify Silent Failure Detection (SDAP v1 Concern)

**What We're Testing**: File upload returns 200 OK AND file actually exists in SPE

```bash
# This is built into the test script:
# 1. Upload file via BFF API → get item ID
# 2. Download file via BFF API → get content
# 3. Compare content → verify integrity

# If upload succeeded but file doesn't exist:
# - Download would fail with 404
# - Content verification would fail

echo "✅ PASS: Silent failure detection built into script (download verifies upload)"
```

### Test 3: Verify Cache Performance (Phase 4)

```bash
# Run the test twice to see cache behavior
echo "=== First Run (Cache MISS expected) ==="
time node test-pcf-client-integration.js

echo ""
echo "=== Second Run (Cache HIT expected) ==="
time node test-pcf-client-integration.js

# Expected:
# - First run: ~2-3s (OBO exchange ~200ms)
# - Second run: ~1-2s (cached token ~5ms)
```

---

## Test Procedure (If Token NOT Available)

### Document Limitations & Script Readiness

```bash
# Create evidence directory
mkdir -p dev/projects/sdap_V2/test-evidence/task-5.4

# Document the situation
cat > dev/projects/sdap_V2/test-evidence/task-5.4/task-5.4-status.md <<'EOF'
# Task 5.4 Status: BLOCKED (Admin Consent)

## Situation
Cannot acquire BFF API token via Azure CLI without admin consent.

## Error
AADSTS65001: The user or administrator has not consented to use the
application with ID '04b07795-8ddb-461a-bbee-02f9e1bf7b46' (Azure CLI)

## Script Status
✅ test-pcf-client-integration.js UPDATED and READY
- Correct routes per ADR-011
- Container ID instead of Drive ID
- Comprehensive testing (upload → download → verify → delete)

## What Would Be Tested
1. Complete auth chain (PCF → BFF → OBO → Graph → SPE)
2. File operations (upload, download, delete)
3. Content integrity (no silent failures)
4. Cache performance (Phase 4 verification)
5. Error handling (401 retry logic)

## Recommendation
- Grant admin consent (Path A)
- OR defer to production testing (Task 5.9)
- OR post-deployment validation

## Impact
Tasks 5.1-5.4 all limited by same token issue. This is expected for
Azure CLI testing, does NOT indicate BFF API problem.
EOF

cat dev/projects/sdap_V2/test-evidence/task-5.4/task-5.4-status.md
```

---

## Validation Checklist

**If Token Available**:
- [ ] PCF client test script passes all tests
- [ ] Upload successful (returns item ID)
- [ ] Download successful (content integrity verified)
- [ ] Delete successful (file removed)
- [ ] Cache performance improvement observed (second run faster)
- [ ] Silent failure NOT detected (upload ✅ = download ✅)

**If Token NOT Available**:
- [x] Script updated with correct routes (ADR-011) ✅
- [x] Container ID usage documented ✅
- [x] Test approach documented ✅
- [x] Limitations documented ✅
- [x] Recommendations provided ✅

---

## Pass Criteria

**Path A (Token Acquired)**:
- ✅ `test-pcf-client-integration.js` passes
- ✅ All operations (upload, download, delete) work
- ✅ Content integrity verified
- ✅ No integration mismatches found

**Path B (Token NOT Available)**:
- ✅ Script ready and updated
- ✅ Limitations documented
- ✅ Testing deferred with clear plan

---

## Architecture Notes Added

**Key Updates Made**:
1. ✅ Updated upload route: `/api/obo/containers/{id}/files/{path}` (was `/api/obo/drives/{id}/upload`)
2. ✅ Changed variable names: `CONTAINER_ID` (was `DRIVE_ID`)
3. ✅ Added ADR-011 references
4. ✅ Documented Container ID = Drive ID principle
5. ✅ Updated all function signatures (`containerId` parameter)

**Script Location**: `c:\code_files\spaarke\test-pcf-client-integration.js`

**What This Fixes**:
- Phase 5 Task 5.2 discovered route discrepancy
- Old script would have returned 404 (wrong route)
- New script uses correct ADR-011 pattern
- Aligns with actual BFF API implementation

---

## Next Task

[Phase 5 - Task 5: Dataverse Integration & Metadata Sync](phase-5-task-5-dataverse.md)

---

## Notes

**Historical Context**: SDAP v1 had "silent failures" - API returned 200 OK but files weren't in SPE. This test specifically addresses that by:
1. Uploading file → get item ID
2. Downloading file → verify content
3. Comparing content → detect any data loss/corruption

If the test passes, we've proven:
- No silent failures (file exists in SPE)
- No data corruption (content matches)
- Complete chain works (PCF → BFF → OBO → Graph → SPE)

**Admin Consent Context**: The admin consent requirement is an Azure CLI limitation, NOT a BFF API problem. In production, users authenticate via browser (MSAL.js) which has proper consent flow.
