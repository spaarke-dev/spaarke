# Phase 5 - Task 3: SharePoint Embedded Storage Verification

**Phase**: 5 (Integration Testing)
**Duration**: 30-45 minutes (limited by token access)
**Risk**: **HIGH** (silent failures - files appear stored but aren't)
**Layers Tested**: SharePoint Embedded (Layer 5) - Direct Graph API verification
**Prerequisites**: Task 5.2 (BFF Endpoints) complete
**Status**: Token-limited (same as Tasks 5.1-5.2)

---

## Goal

**Verify files are ACTUALLY stored in SharePoint Embedded**, not just API success responses.

**Why Critical**: SDAP v1 had "silent failures" - API returned 200 OK but files weren't actually in SPE.

**Current Limitation**: Cannot upload files via BFF API (admin consent required for Azure CLI). However, we can:
1. ✅ Verify Graph API routes exist and are accessible
2. ✅ Query existing containers and files (if any)
3. ✅ Validate Graph API endpoint patterns match implementation
4. ⏳ Full upload→verify testing deferred to Task 5.4 (PCF Integration)

## Test Procedure

### Test 1: Verify Graph API Access & Container Query

**What We Can Test**: Direct Graph API access to SPE containers

```bash
# Get Graph API token
GRAPH_TOKEN=$(az account get-access-token \
  --resource https://graph.microsoft.com \
  --query accessToken -o tsv)

echo "Token acquired: ${#GRAPH_TOKEN} characters"

# Test documented container (from architecture doc)
TEST_CONTAINER_ID="b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"

# Query container root (uses /drives/ endpoint per ADR-011)
echo "Querying SPE container via Graph API..."
curl -s -H "Authorization: Bearer $GRAPH_TOKEN" \
  "https://graph.microsoft.com/v1.0/drives/$TEST_CONTAINER_ID/root" | python -m json.tool

# Expected: JSON response with drive root metadata
# If 404: Container doesn't exist or no access
# If 200: Container accessible, SPE storage working
```

**Validation**:
- ✅ Graph API token acquired
- ✅ Graph API endpoint responds (not 500/503)
- ✅ Container accessible (200 OK) OR documented error (404/403)

### Test 2: List Files in Container (if any exist)

```bash
# List children in container root
echo "Listing files in SPE container..."
curl -s -H "Authorization: Bearer $GRAPH_TOKEN" \
  "https://graph.microsoft.com/v1.0/drives/$TEST_CONTAINER_ID/root/children" | python -m json.tool

# Expected outcomes:
# - 200 + empty array: Container exists, no files (normal for new container)
# - 200 + file list: Container has files from previous testing
# - 404: Container not found
# - 403: No permissions
```

**Validation**:
- ✅ Endpoint responds (Graph API operational)
- ✅ Response format correct (JSON with value array)
- ⏳ If files exist, verify metadata structure

### Test 3: Verify Graph API Endpoint Pattern (per ADR-011)

**What We're Verifying**: Graph API uses `/drives/{containerId}` not `/storage/fileStorage/containers/`

```bash
# Test both endpoint patterns to confirm which works

echo "=== Test 1: /drives/ endpoint (ADR-011 pattern) ==="
curl -s -w "\nHTTP_STATUS:%{http_code}\n" \
  -H "Authorization: Bearer $GRAPH_TOKEN" \
  "https://graph.microsoft.com/v1.0/drives/$TEST_CONTAINER_ID/root" | tail -5

echo ""
echo "=== Test 2: /storage/fileStorage/containers/ endpoint (alternative) ==="
curl -s -w "\nHTTP_STATUS:%{http_code}\n" \
  -H "Authorization: Bearer $GRAPH_TOKEN" \
  "https://graph.microsoft.com/v1.0/storage/fileStorage/containers/$TEST_CONTAINER_ID" | tail -5
```

**Expected Results**:
- `/drives/` endpoint: ✅ 200 OK (ADR-011 confirmed)
- `/storage/fileStorage/containers/`: ⚠️ May or may not work (not used in our implementation)

### Test 4: Upload→Verify Flow (Token-Limited)

**Status**: ⚠️ BLOCKED - Requires BFF API token

```bash
# This test would be:
# 1. Upload file via BFF API
# 2. Query Graph API directly to verify file exists
# 3. Compare metadata (silent failure detection)

echo "⏭️  DEFERRED TO TASK 5.4 (PCF Integration)"
echo "Reason: Cannot acquire BFF API token via Azure CLI (admin consent)"
echo ""
echo "Task 5.4 will test:"
echo "  - Upload via test-pcf-client-integration.js"
echo "  - Verify file in Graph API directly"
echo "  - Detect silent failures"
```

## Validation Checklist

**What We Can Validate (Token-Limited)**:
- [ ] Graph API token acquired successfully
- [ ] Graph API endpoints respond (not 503/500)
- [ ] Test container accessible via Graph API
- [ ] `/drives/` endpoint works (ADR-011 validation)
- [ ] Response format correct (JSON structure)
- [ ] Can list container contents (if any files exist)

**Deferred to Task 5.4**:
- [ ] Files verified in Graph API after BFF upload
- [ ] Metadata matches (name, size, timestamps)
- [ ] Silent failure detection (200 OK but no file)

## Pass Criteria (Updated)

**Task 5.3 (Current)**:
- ✅ Graph API accessible and responsive
- ✅ Test container query successful (200 or documented error)
- ✅ ADR-011 endpoint pattern validated (`/drives/` works)
- ✅ Graph API structure matches implementation expectations

**Task 5.4 (Full Testing)**:
- ✅ Upload→verify flow (silent failure detection)
- ✅ Metadata verification (no data corruption)
- ✅ File persistence verification

## Next Task

[Phase 5 - Task 4: PCF Control Integration (Pre-Build)](phase-5-task-4-pcf-integration.md)

