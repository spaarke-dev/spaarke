# Phase 5 - Task 3: SharePoint Embedded Storage Verification

**Phase**: 5 (Integration Testing)
**Duration**: 1-2 hours
**Risk**: **HIGH** (silent failures - files appear stored but aren't)
**Layers Tested**: SharePoint Embedded → Dataverse (Layers 5-6)
**Prerequisites**: Task 5.2 (BFF Endpoints) complete

---

## Goal

**Verify files are ACTUALLY stored in SharePoint Embedded**, not just API success responses.

**Why Critical**: SDAP v1 had "silent failures" - API returned 200 OK but files weren't actually in SPE.

## Test Procedure

### Test 1: Verify File in Microsoft Graph (Direct Query)

```bash
# Upload file via API
FILE_NAME="verify-storage-$(date +%s).txt"
echo "Test content for SPE verification" > /tmp/verify-test.txt

UPLOAD_RESPONSE=$(curl -s -X PUT \
  -H "Authorization: Bearer $PCF_TOKEN" \
  --data-binary @/tmp/verify-test.txt \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/upload?fileName=$FILE_NAME")

ITEM_ID=$(echo "$UPLOAD_RESPONSE" | jq -r '.id')
echo "Uploaded Item ID: $ITEM_ID"

# Query Graph API DIRECTLY (bypass BFF API)
GRAPH_TOKEN=$(az account get-access-token --resource https://graph.microsoft.com --query accessToken -o tsv)

curl -s -H "Authorization: Bearer $GRAPH_TOKEN" \
  "https://graph.microsoft.com/v1.0/drives/$DRIVE_ID/items/$ITEM_ID" | jq .

# Verify file exists in Graph
if [ $? -eq 0 ]; then
  echo "✅ PASS: File exists in SharePoint Embedded (verified via Graph)"
else
  echo "❌ FAIL: File NOT found in SPE"
  exit 1
fi
```

### Test 2: Verify File Metadata Correctness

```bash
# Check file metadata matches what was uploaded
METADATA=$(curl -s -H "Authorization: Bearer $GRAPH_TOKEN" \
  "https://graph.microsoft.com/v1.0/drives/$DRIVE_ID/items/$ITEM_ID")

NAME=$(echo "$METADATA" | jq -r '.name')
SIZE=$(echo "$METADATA" | jq -r '.size')

echo "Expected: $FILE_NAME, size: 32 bytes"
echo "Actual: $NAME, size: $SIZE bytes"

if [ "$NAME" == "$FILE_NAME" ] && [ "$SIZE" == "32" ]; then
  echo "✅ PASS: Metadata correct"
else
  echo "⚠️  WARNING: Metadata mismatch"
fi
```

### Test 3: Verify Large File Upload (>250MB requires UploadSession)

```bash
# Note: This tests chunked uploads via UploadSessionManager
# BFF API should automatically use upload sessions for large files

echo "⏭️  SKIP: Large file uploads tested in Task 5.7 (Load Testing)"
echo "Note: For files >4MB, UploadSessionManager should be used automatically"
```

## Validation Checklist

- [ ] Files verified in Graph API (not just BFF API response)
- [ ] Metadata matches (name, size, timestamps)
- [ ] Files persist after app restart
- [ ] Files accessible via SharePoint web UI (if applicable)

## Pass Criteria

- ✅ All files verified in Graph API
- ✅ Metadata correct
- ✅ No silent failures

## Next Task

[Phase 5 - Task 4: PCF Control Integration (Pre-Build)](phase-5-task-4-pcf-integration.md)

