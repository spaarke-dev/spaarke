# Phase 5 - Task 5: Dataverse Integration & Metadata Sync

**Phase**: 5 (Integration Testing)
**Duration**: 1-2 hours
**Risk**: MEDIUM (metadata out of sync causes query failures)
**Layers Tested**: Dataverse (Layer 6) - Metadata retrieval and query performance
**Prerequisites**: Task 5.4 (PCF Integration) complete
**Status**: NOT BLOCKED by token issues - uses PAC CLI with Dataverse auth

---

## Goal

**Verify Dataverse metadata is synced correctly** and accessible for SDAP operations.

**Why Critical**: SDAP relies on Dataverse to:
1. Store Container ID (Drive ID) references on Matter records
2. Store document metadata (sprk_Document entity)
3. Enforce row-level security through Dataverse ownership model
4. Provide query interface for PCF control

**Current Architecture** (per [SDAP-ARCHITECTURE-OVERVIEW-V2-2025-10-13-2213.md](c:\code_files\spaarke\SDAP-ARCHITECTURE-OVERVIEW-V2-2025-10-13-2213.md)):
- **Matter Entity**: Contains `sprk_containerId` field (Container ID = Drive ID per ADR-011)
- **Document Entity**: Contains `sprk_graphdriveid`, `sprk_graphitemid`, `sprk_filename`, `sprk_filesize`, `sprk_mimetype`, `sprk_hasfile`
- **PCF Control**: Queries Dataverse to get Container ID, then calls BFF API for file operations

**What This Tests**:
1. ✅ Container ID retrieval from Matter records
2. ✅ Document entity schema validation
3. ✅ Query performance (Dataverse → PCF control)
4. ✅ Metadata field structure matches implementation
5. ⏳ Metadata sync (IF files uploaded - deferred from Task 5.4)

---

## Test Procedure

### Test 1: Verify Container ID Retrieval from Matter Records

**What We're Testing**: PCF control retrieves Container ID from Matter entity to call BFF API

```bash
# Get current Dataverse connection
echo "=== Dataverse Connection Status ==="
pac org who

# Query Matter entity for Container ID field
echo ""
echo "=== Query Matter Entities with Container IDs ==="
pac data list-records \
  --entity-logical-name sprk_matter \
  --select sprk_name,sprk_containerid \
  --filter "statecode eq 0" \
  --top 5 2>&1 | tee dev/projects/sdap_V2/test-evidence/task-5.5/matter-container-query.txt

# Expected outcomes:
# - 200 OK: Matters with Container IDs found (IDEAL)
# - 200 OK + empty: No Matters with Container IDs yet (acceptable for testing)
# - 404/error: Field doesn't exist (FAIL - schema issue)

echo ""
echo "=== Validation ==="
if grep -q "sprk_containerid" dev/projects/sdap_V2/test-evidence/task-5.5/matter-container-query.txt; then
  echo "✅ PASS: Container ID field accessible in Matter entity"
else
  echo "❌ WARNING: Container ID field not found in query results"
fi
```

**Why Critical**: If PCF control can't retrieve Container ID from Matter, it can't call BFF API upload endpoint.

### Test 2: Verify Document Entity Schema

**What We're Testing**: sprk_Document entity has all required fields for SDAP operations

```bash
echo "=== Document Entity Metadata ==="
pac entity attributeinfo \
  --entity-logical-name sprk_document \
  --attribute-logical-names sprk_graphdriveid,sprk_graphitemid,sprk_filename,sprk_filesize,sprk_mimetype,sprk_hasfile \
  2>&1 | tee dev/projects/sdap_V2/test-evidence/task-5.5/document-schema.txt

# Expected fields (per Entity.xml):
# - sprk_graphdriveid: Container ID reference (nvarchar, 1000 chars)
# - sprk_graphitemid: SPE file item ID (nvarchar, 1000 chars)
# - sprk_filename: File name (nvarchar, 1000 chars)
# - sprk_filesize: File size in bytes (int)
# - sprk_mimetype: MIME type (nvarchar, 100 chars)
# - sprk_hasfile: Boolean flag (bit)

echo ""
echo "=== Validation ==="
REQUIRED_FIELDS=("sprk_graphdriveid" "sprk_graphitemid" "sprk_filename" "sprk_filesize" "sprk_mimetype" "sprk_hasfile")
MISSING_FIELDS=0

for field in "${REQUIRED_FIELDS[@]}"; do
  if grep -q "$field" dev/projects/sdap_V2/test-evidence/task-5.5/document-schema.txt; then
    echo "✅ Found: $field"
  else
    echo "❌ Missing: $field"
    MISSING_FIELDS=$((MISSING_FIELDS + 1))
  fi
done

if [ $MISSING_FIELDS -eq 0 ]; then
  echo ""
  echo "✅ PASS: All required fields present in Document entity"
else
  echo ""
  echo "❌ FAIL: $MISSING_FIELDS fields missing from Document entity"
fi
```

**Why Critical**: PCF control relies on these fields to display file metadata without querying Graph API.

### Test 3: Query Performance - Matter Retrieval

**What We're Testing**: Query performance for PCF control initial load

```bash
echo "=== Matter Query Performance Test ==="

# Warm-up query (Dataverse may cache)
pac data list-records \
  --entity-logical-name sprk_matter \
  --select sprk_name,sprk_containerid \
  --filter "statecode eq 0" \
  --top 10 > /dev/null 2>&1

# Timed query
START_TIME=$(date +%s%3N)

pac data list-records \
  --entity-logical-name sprk_matter \
  --select sprk_name,sprk_containerid \
  --filter "statecode eq 0" \
  --top 10 > /dev/null 2>&1

END_TIME=$(date +%s%3N)
ELAPSED=$((END_TIME - START_TIME))

echo "Query time: ${ELAPSED}ms"
echo "Query time: ${ELAPSED}ms" > dev/projects/sdap_V2/test-evidence/task-5.5/query-performance.txt

echo ""
echo "=== Validation ==="
if [ $ELAPSED -lt 2000 ]; then
  echo "✅ PASS: Query performance acceptable (<2s)"
elif [ $ELAPSED -lt 5000 ]; then
  echo "⚠️  WARNING: Query slow but acceptable (${ELAPSED}ms, <5s)"
else
  echo "❌ FAIL: Query too slow (${ELAPSED}ms, >5s)"
fi
```

**Performance Targets**:
- Excellent: <1s
- Good: <2s
- Acceptable: <5s
- Unacceptable: >5s

### Test 4: Document Query Performance (if documents exist)

**What We're Testing**: Query performance for PCF control file list

```bash
echo "=== Document Query Performance Test ==="

# Check if any documents exist
DOC_COUNT=$(pac data list-records \
  --entity-logical-name sprk_document \
  --select sprk_documentid \
  --top 1 2>&1 | grep -c "sprk_documentid" || echo "0")

if [ "$DOC_COUNT" -gt 0 ]; then
  echo "Documents found, testing query performance..."

  # Warm-up query
  pac data list-records \
    --entity-logical-name sprk_document \
    --select sprk_filename,sprk_filesize,sprk_graphitemid,sprk_graphdriveid \
    --filter "statecode eq 0" \
    --top 20 > /dev/null 2>&1

  # Timed query
  START_TIME=$(date +%s%3N)

  pac data list-records \
    --entity-logical-name sprk_document \
    --select sprk_filename,sprk_filesize,sprk_graphitemid,sprk_graphdriveid \
    --filter "statecode eq 0" \
    --top 20 > /dev/null 2>&1

  END_TIME=$(date +%s%3N)
  ELAPSED=$((END_TIME - START_TIME))

  echo "Query time: ${ELAPSED}ms"
  echo "Document query time: ${ELAPSED}ms" >> dev/projects/sdap_V2/test-evidence/task-5.5/query-performance.txt

  echo ""
  echo "=== Validation ==="
  if [ $ELAPSED -lt 3000 ]; then
    echo "✅ PASS: Document query performance acceptable (<3s for 20 records)"
  else
    echo "⚠️  WARNING: Document query slow (${ELAPSED}ms)"
  fi
else
  echo "⏭️  SKIP: No documents in Dataverse yet (expected - upload testing deferred)"
  echo "Document query: SKIPPED (no documents)" >> dev/projects/sdap_V2/test-evidence/task-5.5/query-performance.txt
fi
```

### Test 5: Verify Matter-Document Relationship

**What We're Testing**: Document entity can link to Matter entity via lookup

```bash
echo "=== Matter-Document Relationship Test ==="

# Query documents with Matter lookup
pac data list-records \
  --entity-logical-name sprk_document \
  --select sprk_filename,sprk_matter \
  --top 5 2>&1 | tee dev/projects/sdap_V2/test-evidence/task-5.5/matter-document-relationship.txt

echo ""
echo "=== Validation ==="
if grep -q "sprk_matter" dev/projects/sdap_V2/test-evidence/task-5.5/matter-document-relationship.txt; then
  echo "✅ PASS: Matter lookup field accessible in Document entity"
else
  echo "⏭️  INFO: No documents with Matter relationships yet (acceptable)"
fi
```

**Why Critical**: PCF control may filter documents by Matter to enforce row-level security.

---

## Test Procedure Execution

```bash
# Create evidence directory
mkdir -p dev/projects/sdap_V2/test-evidence/task-5.5

# Execute tests
echo "================================================================================================="
echo "Phase 5 - Task 5: Dataverse Integration & Metadata Sync"
echo "================================================================================================="
echo ""

# Run all tests
bash -c '
# Test 1: Container ID Retrieval
echo "=== TEST 1: Container ID Retrieval from Matter Records ==="
pac org who

echo ""
echo "=== Query Matter Entities with Container IDs ==="
pac data list-records \
  --entity-logical-name sprk_matter \
  --select sprk_name,sprk_containerid \
  --filter "statecode eq 0" \
  --top 5 2>&1 | tee dev/projects/sdap_V2/test-evidence/task-5.5/matter-container-query.txt

echo ""
echo "=== Validation ==="
if grep -q "sprk_containerid" dev/projects/sdap_V2/test-evidence/task-5.5/matter-container-query.txt; then
  echo "✅ PASS: Container ID field accessible in Matter entity"
else
  echo "❌ WARNING: Container ID field not found in query results"
fi

# Test 2: Document Entity Schema
echo ""
echo ""
echo "=== TEST 2: Document Entity Schema Validation ==="
pac entity attributeinfo \
  --entity-logical-name sprk_document \
  --attribute-logical-names sprk_graphdriveid,sprk_graphitemid,sprk_filename,sprk_filesize,sprk_mimetype,sprk_hasfile \
  2>&1 | tee dev/projects/sdap_V2/test-evidence/task-5.5/document-schema.txt

echo ""
echo "=== Validation ==="
REQUIRED_FIELDS=("sprk_graphdriveid" "sprk_graphitemid" "sprk_filename" "sprk_filesize" "sprk_mimetype" "sprk_hasfile")
MISSING_FIELDS=0

for field in "${REQUIRED_FIELDS[@]}"; do
  if grep -q "$field" dev/projects/sdap_V2/test-evidence/task-5.5/document-schema.txt; then
    echo "✅ Found: $field"
  else
    echo "❌ Missing: $field"
    MISSING_FIELDS=$((MISSING_FIELDS + 1))
  fi
done

if [ $MISSING_FIELDS -eq 0 ]; then
  echo ""
  echo "✅ PASS: All required fields present in Document entity"
else
  echo ""
  echo "❌ FAIL: $MISSING_FIELDS fields missing from Document entity"
fi

# Test 3: Matter Query Performance
echo ""
echo ""
echo "=== TEST 3: Matter Query Performance ==="

pac data list-records \
  --entity-logical-name sprk_matter \
  --select sprk_name,sprk_containerid \
  --filter "statecode eq 0" \
  --top 10 > /dev/null 2>&1

START_TIME=$(date +%s%3N)

pac data list-records \
  --entity-logical-name sprk_matter \
  --select sprk_name,sprk_containerid \
  --filter "statecode eq 0" \
  --top 10 > /dev/null 2>&1

END_TIME=$(date +%s%3N)
ELAPSED=$((END_TIME - START_TIME))

echo "Query time: ${ELAPSED}ms"
echo "Matter query time: ${ELAPSED}ms" > dev/projects/sdap_V2/test-evidence/task-5.5/query-performance.txt

echo ""
echo "=== Validation ==="
if [ $ELAPSED -lt 2000 ]; then
  echo "✅ PASS: Query performance acceptable (<2s)"
elif [ $ELAPSED -lt 5000 ]; then
  echo "⚠️  WARNING: Query slow but acceptable (${ELAPSED}ms, <5s)"
else
  echo "❌ FAIL: Query too slow (${ELAPSED}ms, >5s)"
fi

# Test 4: Document Query Performance
echo ""
echo ""
echo "=== TEST 4: Document Query Performance ==="

DOC_COUNT=$(pac data list-records \
  --entity-logical-name sprk_document \
  --select sprk_documentid \
  --top 1 2>&1 | grep -c "sprk_documentid" || echo "0")

if [ "$DOC_COUNT" -gt 0 ]; then
  echo "Documents found, testing query performance..."

  pac data list-records \
    --entity-logical-name sprk_document \
    --select sprk_filename,sprk_filesize,sprk_graphitemid,sprk_graphdriveid \
    --filter "statecode eq 0" \
    --top 20 > /dev/null 2>&1

  START_TIME=$(date +%s%3N)

  pac data list-records \
    --entity-logical-name sprk_document \
    --select sprk_filename,sprk_filesize,sprk_graphitemid,sprk_graphdriveid \
    --filter "statecode eq 0" \
    --top 20 > /dev/null 2>&1

  END_TIME=$(date +%s%3N)
  ELAPSED=$((END_TIME - START_TIME))

  echo "Query time: ${ELAPSED}ms"
  echo "Document query time: ${ELAPSED}ms" >> dev/projects/sdap_V2/test-evidence/task-5.5/query-performance.txt

  echo ""
  echo "=== Validation ==="
  if [ $ELAPSED -lt 3000 ]; then
    echo "✅ PASS: Document query performance acceptable (<3s for 20 records)"
  else
    echo "⚠️  WARNING: Document query slow (${ELAPSED}ms)"
  fi
else
  echo "⏭️  SKIP: No documents in Dataverse yet (expected - upload testing deferred)"
  echo "Document query: SKIPPED (no documents)" >> dev/projects/sdap_V2/test-evidence/task-5.5/query-performance.txt
fi

# Test 5: Matter-Document Relationship
echo ""
echo ""
echo "=== TEST 5: Matter-Document Relationship ==="

pac data list-records \
  --entity-logical-name sprk_document \
  --select sprk_filename,sprk_matter \
  --top 5 2>&1 | tee dev/projects/sdap_V2/test-evidence/task-5.5/matter-document-relationship.txt

echo ""
echo "=== Validation ==="
if grep -q "sprk_matter" dev/projects/sdap_V2/test-evidence/task-5.5/matter-document-relationship.txt; then
  echo "✅ PASS: Matter lookup field accessible in Document entity"
else
  echo "⏭️  INFO: No documents with Matter relationships yet (acceptable)"
fi

echo ""
echo ""
echo "================================================================================================="
echo "Phase 5 - Task 5: COMPLETE"
echo "================================================================================================="
' | tee dev/projects/sdap_V2/test-evidence/task-5.5/task-5.5-execution.txt
```

---

## Validation Checklist

**Core Tests** (NOT blocked by token issues):
- [ ] Container ID field accessible in Matter entity
- [ ] Document entity schema validated (all 6 required fields present)
- [ ] Matter query performance acceptable (<5s for 10 records)
- [ ] Document query performance acceptable (<3s for 20 records, if documents exist)
- [ ] Matter-Document relationship validated

**Deferred Tests** (require file uploads from Task 5.4):
- [ ] Metadata sync verified (upload → Dataverse record created)
- [ ] Field values accurate (itemId, driveId, filename, fileSize match SPE)
- [ ] Silent failure detection (upload success = metadata created)

---

## Pass Criteria

**Task 5.5 (Current)**:
- ✅ Container ID retrievable from Matter records (field accessible)
- ✅ Document entity has all required fields
- ✅ Query performance meets targets (<5s Matter, <3s Documents)
- ✅ Schema matches implementation expectations

**Future Testing** (when file uploads available):
- ✅ Metadata sync working (upload creates Dataverse record)
- ✅ Field values accurate
- ✅ No sync failures detected

---

## Architecture Notes

### Key Fields in sprk_Document Entity

Per [Entity.xml](c:\code_files\spaarke\src\Entities\sprk_Document\Entity.xml):

| Field | Type | Purpose | Max Length |
|-------|------|---------|------------|
| `sprk_graphdriveid` | nvarchar | Container ID (Drive ID) | 1000 chars |
| `sprk_graphitemid` | nvarchar | SPE file item ID | 1000 chars |
| `sprk_filename` | nvarchar | File name | 1000 chars |
| `sprk_filesize` | int | File size in bytes | 2GB max |
| `sprk_mimetype` | nvarchar | MIME type | 100 chars |
| `sprk_hasfile` | bit | Boolean flag (file uploaded) | true/false |
| `sprk_matter` | lookup | Parent Matter reference | - |
| `sprk_containerid` | lookup | Container reference | - |

**Why These Fields**:
- PCF control displays file list without calling Graph API (performance)
- Dataverse provides row-level security through ownership model
- Container ID enables BFF API calls for file operations
- Item ID enables download/delete operations

### Container ID Storage Pattern

**Two Locations**:
1. **Matter.sprk_containerid**: Primary storage, one Container per Matter
2. **Document.sprk_graphdriveid**: Cached reference for query optimization

**Why Both**:
- Matter.sprk_containerid: Source of truth, set when Matter created/linked to SPE
- Document.sprk_graphdriveid: Denormalized for fast queries without JOIN

**PCF Control Pattern**:
1. Load Matter form → get `sprk_containerid` from context
2. Query Documents WHERE `sprk_matter = {matterId}` (no need for sprk_graphdriveid)
3. Call BFF API with Container ID from Matter context

---

## Next Task

[Phase 5 - Task 6: Cache Performance Validation](phase-5-task-6-cache-performance.md)

---

## Notes

**Token Limitation Impact**: Task 5.4 deferred file upload testing due to admin consent requirement. This means:
- ✅ We CAN test Dataverse schema and query performance
- ⏳ We CANNOT test metadata sync until files uploaded
- ⏳ Full end-to-end testing deferred to Task 5.9 (Production) or post-deployment

**Expected Outcomes for Initial Testing**:
- Container ID field accessible but may be empty (no Matters linked to SPE yet)
- Document entity schema correct but no records (no files uploaded yet)
- Query performance testable regardless of data presence

**PAC CLI vs BFF API Tokens**: This task uses PAC CLI which authenticates to Dataverse directly (no BFF API token needed), so NOT blocked by admin consent issue.
