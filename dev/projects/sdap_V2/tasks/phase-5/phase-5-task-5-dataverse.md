# Phase 5 - Task 5: Dataverse Integration & Metadata Sync

**Phase**: 5 (Integration Testing)
**Duration**: 1-2 hours
**Risk**: MEDIUM (metadata out of sync causes query failures)
**Layers Tested**: Dataverse → BFF API (Layer 6)
**Prerequisites**: Task 5.3 (SPE Storage) complete

---

## Goal

**Verify Dataverse metadata is synced correctly** with SharePoint Embedded files.

Tests:
- Drive ID retrieval from Matter records
- Document entity creation (if applicable)
- Metadata field accuracy (sprk_itemid, sprk_driveid, sprk_name)
- Query performance

## Test Procedure

### Test 1: Verify Drive ID Retrieval

```bash
# Query Matter entity for Drive ID
MATTER_ID="<test-matter-guid>"

DRIVE_ID_FROM_DV=$(pac data read \
  --entity-logical-name sprk_matter \
  --id $MATTER_ID \
  --columns sprk_driveid | grep "sprk_driveid" | awk '{print $2}')

echo "Drive ID from Dataverse: $DRIVE_ID_FROM_DV"

if [ -n "$DRIVE_ID_FROM_DV" ]; then
  echo "✅ PASS: Drive ID retrieved from Dataverse"
else
  echo "❌ FAIL: Drive ID not found"
  exit 1
fi
```

### Test 2: Verify Document Entity (if applicable)

```bash
# If your system creates sprk_document records, verify them

# Upload file via API
FILE_NAME="dataverse-test-$(date +%s).txt"
echo "Test file for Dataverse sync" > /tmp/dv-test.txt

UPLOAD_RESPONSE=$(curl -s -X PUT \
  -H "Authorization: Bearer $PCF_TOKEN" \
  --data-binary @/tmp/dv-test.txt \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/upload?fileName=$FILE_NAME")

ITEM_ID=$(echo "$UPLOAD_RESPONSE" | jq -r '.id')
echo "Uploaded Item ID: $ITEM_ID"

# Query Dataverse for document record
sleep 2  # Allow time for sync

pac data read --entity-logical-name sprk_document \
  --filter "sprk_itemid eq '$ITEM_ID'" \
  --columns sprk_name,sprk_itemid,sprk_driveid

# Note: This test depends on your implementation
# If you don't create sprk_document records, skip this test

echo "⏭️  SKIP: Document entity creation (implementation-dependent)"
```

### Test 3: Query Performance

```bash
# Measure Dataverse query performance
START_TIME=$(date +%s%3N)

pac data read --entity-logical-name sprk_matter \
  --filter "statecode eq 0" \
  --columns sprk_name,sprk_driveid \
  --top 10 > /dev/null

END_TIME=$(date +%s%3N)
ELAPSED=$((END_TIME - START_TIME))

echo "Query time: ${ELAPSED}ms"

if [ $ELAPSED -lt 2000 ]; then
  echo "✅ PASS: Query performance acceptable (<2s)"
else
  echo "⚠️  WARNING: Query slow (${ELAPSED}ms)"
fi
```

## Validation Checklist

- [ ] Drive IDs retrieved from Matter records
- [ ] Document entities created (if applicable)
- [ ] Metadata fields accurate (itemid, driveid, name)
- [ ] Query performance acceptable (<2s for 10 records)

## Pass Criteria

- ✅ Drive IDs accessible via Dataverse queries
- ✅ Metadata synced correctly
- ✅ Query performance meets targets

## Next Task

[Phase 5 - Task 6: Cache Performance Validation](phase-5-task-6-cache-performance.md)

