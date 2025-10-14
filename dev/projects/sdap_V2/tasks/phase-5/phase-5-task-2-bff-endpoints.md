# Phase 5 - Task 2: BFF API Endpoint Testing

**Phase**: 5 (Integration Testing)
**Duration**: 2-3 hours
**Risk**: **HIGH** (core file operations must work)
**Layers Tested**: BFF API → Graph API → SharePoint Embedded (Layers 3-5)
**Prerequisites**: Task 5.1 (Authentication) complete

---

## 🤖 AI PROMPT

```
CONTEXT: You are executing Phase 5 Task 2 - BFF API Endpoint Testing. Task 5.1 (Authentication) has passed, so we know auth works. Now we test the core file operation endpoints.

TASK: Systematically test all BFF API endpoints for file operations (upload, download, delete) with various file sizes and types.

CONSTRAINTS:
- Must test small files (<1MB) and large files (>10MB)
- Must test different file types (text, binary, PDF, images)
- Must verify response DTOs (not SDK types like DriveItem)
- Must test error scenarios (invalid drive ID, missing file, etc.)
- Must measure performance (latency for each operation)
- Must verify files actually stored in SPE (not just API success)

CRITICAL SUCCESS FACTORS:
1. Upload must store file in SPE and return metadata
2. Download must return correct file content (integrity check)
3. Delete must remove file from SPE
4. All operations must return DTOs (FileUploadResult, not DriveItem)
5. Latency must meet targets (<2s for 1MB files)
6. Error handling must be clear and actionable

HISTORICAL CONTEXT (SDAP v1 issues):
- Files appeared to upload but weren't stored in SPE
- Download returned wrong file or corrupted content
- Large files timed out without clear error message
- API returned Microsoft SDK types (leaked abstraction)

FOCUS: This task tests the core value proposition - can we reliably store and retrieve files in SharePoint Embedded?
```

---

## Goal

**Validate all BFF API file operation endpoints** work correctly and reliably:
- Upload files to SharePoint Embedded
- Download files from SharePoint Embedded
- Delete files from SharePoint Embedded

**Why High Risk**: These are the core operations users need. If these don't work, the entire system is useless.

**Success Definition**: All file operations work for various file sizes, integrity verified, performance meets targets, errors handled gracefully.

---

## Test Environment Setup

### Get Test Drive ID

```bash
# From Task 5.1, you should have:
echo $DRIVE_ID

# If not set, get from Dataverse
DRIVE_ID=$(pac data read --entity-logical-name sprk_matter \
  --filter "statecode eq 0" \
  --columns sprk_driveid \
  --top 1 | grep "sprk_driveid" | awk '{print $2}')

export DRIVE_ID
echo "Using Drive ID: $DRIVE_ID"
```

### Create Test Files

```bash
# Create test files of various sizes
mkdir -p dev/projects/sdap_V2/test-files

# Small text file (~1KB)
echo "Test file content - small text file
Created: $(date)
Purpose: Phase 5 Task 2 testing" > dev/projects/sdap_V2/test-files/small-text.txt

# Medium text file (~100KB)
for i in {1..1000}; do
  echo "Line $i: Lorem ipsum dolor sit amet, consectetur adipiscing elit. $(date)"
done > dev/projects/sdap_V2/test-files/medium-text.txt

# Large text file (~1MB)
for i in {1..10000}; do
  echo "Line $i: Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Timestamp: $(date +%s%3N)"
done > dev/projects/sdap_V2/test-files/large-text.txt

# Binary file (random data, ~5MB)
dd if=/dev/urandom of=dev/projects/sdap_V2/test-files/binary-file.bin bs=1M count=5 2>/dev/null

echo "Test files created:"
ls -lh dev/projects/sdap_V2/test-files/
```

---

## Test Procedure

### Test 1: Upload Small File

**Goal**: Verify basic upload functionality

#### Test 1.1: Upload Small Text File (<1KB)

```bash
echo "=== Test 1.1: Upload Small File ==="

FILE_NAME="test-small-$(date +%s).txt"
FILE_PATH="dev/projects/sdap_V2/test-files/small-text.txt"

echo "Uploading: $FILE_NAME"
START_TIME=$(date +%s%3N)

RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}\nTIME_TOTAL:%{time_total}" \
  -X PUT \
  -H "Authorization: Bearer $PCF_TOKEN" \
  -H "Content-Type: text/plain" \
  --data-binary @"$FILE_PATH" \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/upload?fileName=$FILE_NAME")

END_TIME=$(date +%s%3N)
ELAPSED=$((END_TIME - START_TIME))

HTTP_STATUS=$(echo "$RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)
TIME_TOTAL=$(echo "$RESPONSE" | grep "TIME_TOTAL:" | cut -d':' -f2)
BODY=$(echo "$RESPONSE" | grep -v "HTTP_STATUS:" | grep -v "TIME_TOTAL:")

echo "HTTP Status: $HTTP_STATUS"
echo "Response Time: ${TIME_TOTAL}s (${ELAPSED}ms client)"

if [ "$HTTP_STATUS" == "200" ]; then
  echo "✅ PASS: Upload successful"

  # Save response for verification
  echo "$BODY" | jq . > dev/projects/sdap_V2/test-evidence/task-5.2/upload-small-response.json

  # Extract item ID for download test
  ITEM_ID=$(echo "$BODY" | jq -r '.id')
  export SMALL_FILE_ITEM_ID=$ITEM_ID
  echo "Item ID: $ITEM_ID"

  # Verify response is DTO (not DriveItem)
  if echo "$BODY" | jq -e '.id and .name and .size' > /dev/null; then
    echo "✅ PASS: Response contains expected DTO fields (id, name, size)"
  else
    echo "❌ FAIL: Response missing expected DTO fields"
  fi

  # Verify no Microsoft SDK types leaked
  if echo "$BODY" | jq -e '."@odata.type"' > /dev/null; then
    echo "⚠️  WARNING: Response contains @odata.type (Microsoft SDK type leaked)"
  else
    echo "✅ PASS: No SDK types in response (clean DTO)"
  fi

else
  echo "❌ FAIL: Upload failed with status $HTTP_STATUS"
  echo "Response: $BODY"
  exit 1
fi

# Verify performance target (<2s for small files)
if (( $(echo "$TIME_TOTAL < 2.0" | bc -l) )); then
  echo "✅ PASS: Performance target met (<2s)"
else
  echo "⚠️  WARNING: Performance slower than target (${TIME_TOTAL}s > 2s)"
fi
```

**Expected**:
- HTTP 200 OK
- Response contains: id, name, size, createdDateTime
- No @odata.type or other SDK types
- Latency < 2 seconds

#### Test 1.2: Verify Upload Created Record in Dataverse (if applicable)

```bash
echo "=== Test 1.2: Verify Dataverse Record ==="

# Query Dataverse for document record (if your system creates them)
# This depends on your implementation - skip if not applicable

# Example:
# pac data read --entity-logical-name sprk_document \
#   --filter "sprk_itemid eq '$ITEM_ID'" \
#   --columns sprk_name,sprk_driveid,sprk_itemid

echo "⏭️  SKIP: Dataverse sync tested in Task 5.5"
```

---

### Test 2: Download File & Verify Integrity

**Goal**: Verify download returns correct content

#### Test 2.1: Download Small File

```bash
echo "=== Test 2.1: Download Small File ==="

echo "Downloading Item ID: $SMALL_FILE_ITEM_ID"
START_TIME=$(date +%s%3N)

curl -s -w "\nHTTP_STATUS:%{http_code}\nTIME_TOTAL:%{time_total}" \
  -H "Authorization: Bearer $PCF_TOKEN" \
  -o dev/projects/sdap_V2/test-evidence/task-5.2/downloaded-small.txt \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/items/$SMALL_FILE_ITEM_ID/content" \
  > dev/projects/sdap_V2/test-evidence/task-5.2/download-response.txt

END_TIME=$(date +%s%3N)
ELAPSED=$((END_TIME - START_TIME))

HTTP_STATUS=$(cat dev/projects/sdap_V2/test-evidence/task-5.2/download-response.txt | grep "HTTP_STATUS:" | cut -d':' -f2)
TIME_TOTAL=$(cat dev/projects/sdap_V2/test-evidence/task-5.2/download-response.txt | grep "TIME_TOTAL:" | cut -d':' -f2)

echo "HTTP Status: $HTTP_STATUS"
echo "Response Time: ${TIME_TOTAL}s (${ELAPSED}ms client)"

if [ "$HTTP_STATUS" == "200" ]; then
  echo "✅ PASS: Download successful"
else
  echo "❌ FAIL: Download failed with status $HTTP_STATUS"
  exit 1
fi
```

#### Test 2.2: Verify Content Integrity

```bash
echo "=== Test 2.2: Verify Content Integrity ==="

# Compare uploaded and downloaded files
ORIGINAL="dev/projects/sdap_V2/test-files/small-text.txt"
DOWNLOADED="dev/projects/sdap_V2/test-evidence/task-5.2/downloaded-small.txt"

# Calculate checksums
ORIGINAL_SHA=$(sha256sum "$ORIGINAL" | awk '{print $1}')
DOWNLOADED_SHA=$(sha256sum "$DOWNLOADED" | awk '{print $1}')

echo "Original SHA256:   $ORIGINAL_SHA"
echo "Downloaded SHA256: $DOWNLOADED_SHA"

if [ "$ORIGINAL_SHA" == "$DOWNLOADED_SHA" ]; then
  echo "✅ PASS: Content integrity verified (checksums match)"
else
  echo "❌ FAIL: Content corrupted (checksums don't match)"
  echo "Original content:"
  head -5 "$ORIGINAL"
  echo "Downloaded content:"
  head -5 "$DOWNLOADED"
  exit 1
fi

# Verify performance target (<2s for small files)
if (( $(echo "$TIME_TOTAL < 2.0" | bc -l) )); then
  echo "✅ PASS: Performance target met (<2s)"
else
  echo "⚠️  WARNING: Performance slower than target (${TIME_TOTAL}s > 2s)"
fi
```

**Expected**:
- HTTP 200 OK
- Downloaded file exactly matches uploaded file (SHA256)
- Latency < 2 seconds

---

### Test 3: Delete File

**Goal**: Verify delete removes file from SPE

#### Test 3.1: Delete File

```bash
echo "=== Test 3.1: Delete File ==="

echo "Deleting Item ID: $SMALL_FILE_ITEM_ID"
START_TIME=$(date +%s%3N)

RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}\nTIME_TOTAL:%{time_total}" \
  -X DELETE \
  -H "Authorization: Bearer $PCF_TOKEN" \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/items/$SMALL_FILE_ITEM_ID")

END_TIME=$(date +%s%3N)
ELAPSED=$((END_TIME - START_TIME))

HTTP_STATUS=$(echo "$RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)
TIME_TOTAL=$(echo "$RESPONSE" | grep "TIME_TOTAL:" | cut -d':' -f2)

echo "HTTP Status: $HTTP_STATUS"
echo "Response Time: ${TIME_TOTAL}s (${ELAPSED}ms client)"

if [ "$HTTP_STATUS" == "204" ]; then
  echo "✅ PASS: Delete successful (204 No Content)"
elif [ "$HTTP_STATUS" == "200" ]; then
  echo "✅ PASS: Delete successful (200 OK)"
else
  echo "❌ FAIL: Delete failed with status $HTTP_STATUS"
  exit 1
fi

# Verify performance target (<1s for delete)
if (( $(echo "$TIME_TOTAL < 1.0" | bc -l) )); then
  echo "✅ PASS: Performance target met (<1s)"
else
  echo "⚠️  WARNING: Performance slower than target (${TIME_TOTAL}s > 1s)"
fi
```

#### Test 3.2: Verify File Deleted (Download Should Fail)

```bash
echo "=== Test 3.2: Verify File Deleted ==="

# Try to download deleted file (should return 404)
RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}" \
  -H "Authorization: Bearer $PCF_TOKEN" \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/items/$SMALL_FILE_ITEM_ID/content")

HTTP_STATUS=$(echo "$RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)

echo "HTTP Status: $HTTP_STATUS"

if [ "$HTTP_STATUS" == "404" ]; then
  echo "✅ PASS: File deleted (404 Not Found)"
else
  echo "❌ FAIL: File still exists (expected 404, got $HTTP_STATUS)"
  exit 1
fi
```

**Expected**:
- HTTP 204 No Content (or 200 OK)
- Subsequent download returns 404 Not Found
- Latency < 1 second

---

### Test 4: Upload Medium File (~100KB)

**Goal**: Test with larger text file

```bash
echo "=== Test 4: Upload Medium File ==="

FILE_NAME="test-medium-$(date +%s).txt"
FILE_PATH="dev/projects/sdap_V2/test-files/medium-text.txt"
FILE_SIZE=$(wc -c < "$FILE_PATH")

echo "Uploading: $FILE_NAME (Size: $FILE_SIZE bytes)"
START_TIME=$(date +%s%3N)

RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}\nTIME_TOTAL:%{time_total}" \
  -X PUT \
  -H "Authorization: Bearer $PCF_TOKEN" \
  -H "Content-Type: text/plain" \
  --data-binary @"$FILE_PATH" \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/upload?fileName=$FILE_NAME")

END_TIME=$(date +%s%3N)
ELAPSED=$((END_TIME - START_TIME))

HTTP_STATUS=$(echo "$RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)
TIME_TOTAL=$(echo "$RESPONSE" | grep "TIME_TOTAL:" | cut -d':' -f2)
BODY=$(echo "$RESPONSE" | grep -v "HTTP_STATUS:" | grep -v "TIME_TOTAL:")

echo "HTTP Status: $HTTP_STATUS"
echo "Response Time: ${TIME_TOTAL}s (${ELAPSED}ms client)"

if [ "$HTTP_STATUS" == "200" ]; then
  echo "✅ PASS: Medium file upload successful"

  ITEM_ID=$(echo "$BODY" | jq -r '.id')
  export MEDIUM_FILE_ITEM_ID=$ITEM_ID
  echo "Item ID: $ITEM_ID"

  # Verify reported size matches
  REPORTED_SIZE=$(echo "$BODY" | jq -r '.size')
  if [ "$REPORTED_SIZE" == "$FILE_SIZE" ]; then
    echo "✅ PASS: Reported size matches actual size ($REPORTED_SIZE bytes)"
  else
    echo "⚠️  WARNING: Size mismatch (actual: $FILE_SIZE, reported: $REPORTED_SIZE)"
  fi

else
  echo "❌ FAIL: Upload failed with status $HTTP_STATUS"
  echo "Response: $BODY"
  exit 1
fi

# Performance expectation: ~2-5s for 100KB
echo "Performance: ${TIME_TOTAL}s for ${FILE_SIZE} bytes"
```

**Expected**:
- HTTP 200 OK
- Reported size matches actual size
- Latency < 5 seconds for 100KB file

---

### Test 5: Upload Large File (~1MB)

**Goal**: Test with 1MB file (performance baseline)

```bash
echo "=== Test 5: Upload Large File ==="

FILE_NAME="test-large-$(date +%s).txt"
FILE_PATH="dev/projects/sdap_V2/test-files/large-text.txt"
FILE_SIZE=$(wc -c < "$FILE_PATH")

echo "Uploading: $FILE_NAME (Size: $FILE_SIZE bytes, ~$(echo "scale=2; $FILE_SIZE/1024/1024" | bc)MB)"
START_TIME=$(date +%s%3N)

RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}\nTIME_TOTAL:%{time_total}" \
  --max-time 30 \
  -X PUT \
  -H "Authorization: Bearer $PCF_TOKEN" \
  -H "Content-Type: text/plain" \
  --data-binary @"$FILE_PATH" \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/upload?fileName=$FILE_NAME")

END_TIME=$(date +%s%3N)
ELAPSED=$((END_TIME - START_TIME))

HTTP_STATUS=$(echo "$RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)
TIME_TOTAL=$(echo "$RESPONSE" | grep "TIME_TOTAL:" | cut -d':' -f2)
BODY=$(echo "$RESPONSE" | grep -v "HTTP_STATUS:" | grep -v "TIME_TOTAL:")

echo "HTTP Status: $HTTP_STATUS"
echo "Response Time: ${TIME_TOTAL}s (${ELAPSED}ms client)"

if [ "$HTTP_STATUS" == "200" ]; then
  echo "✅ PASS: Large file upload successful"

  ITEM_ID=$(echo "$BODY" | jq -r '.id')
  export LARGE_FILE_ITEM_ID=$ITEM_ID
  echo "Item ID: $ITEM_ID"

  # Performance check (target: <10s for 1MB)
  if (( $(echo "$TIME_TOTAL < 10.0" | bc -l) )); then
    echo "✅ PASS: Performance acceptable (<10s for 1MB)"
  else
    echo "⚠️  WARNING: Performance slower than expected (${TIME_TOTAL}s > 10s)"
  fi

else
  echo "❌ FAIL: Upload failed with status $HTTP_STATUS"
  echo "Response: $BODY"
  exit 1
fi

# Clean up (delete large file to save space)
echo "Cleaning up large file..."
curl -s -X DELETE \
  -H "Authorization: Bearer $PCF_TOKEN" \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/items/$LARGE_FILE_ITEM_ID"
echo "✅ Large file deleted"
```

**Expected**:
- HTTP 200 OK
- Latency < 10 seconds for ~1MB file
- No timeout errors

---

### Test 6: Upload Binary File

**Goal**: Test with non-text binary file

```bash
echo "=== Test 6: Upload Binary File ==="

FILE_NAME="test-binary-$(date +%s).bin"
FILE_PATH="dev/projects/sdap_V2/test-files/binary-file.bin"
FILE_SIZE=$(wc -c < "$FILE_PATH")

echo "Uploading: $FILE_NAME (Size: $FILE_SIZE bytes, ~$(echo "scale=2; $FILE_SIZE/1024/1024" | bc)MB)"
START_TIME=$(date +%s%3N)

RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}\nTIME_TOTAL:%{time_total}" \
  --max-time 60 \
  -X PUT \
  -H "Authorization: Bearer $PCF_TOKEN" \
  -H "Content-Type: application/octet-stream" \
  --data-binary @"$FILE_PATH" \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/upload?fileName=$FILE_NAME")

END_TIME=$(date +%s%3N)
ELAPSED=$((END_TIME - START_TIME))

HTTP_STATUS=$(echo "$RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)
TIME_TOTAL=$(echo "$RESPONSE" | grep "TIME_TOTAL:" | cut -d':' -f2)
BODY=$(echo "$RESPONSE" | grep -v "HTTP_STATUS:" | grep -v "TIME_TOTAL:")

echo "HTTP Status: $HTTP_STATUS"
echo "Response Time: ${TIME_TOTAL}s (${ELAPSED}ms client)"

if [ "$HTTP_STATUS" == "200" ]; then
  echo "✅ PASS: Binary file upload successful"

  ITEM_ID=$(echo "$BODY" | jq -r '.id')
  export BINARY_FILE_ITEM_ID=$ITEM_ID

  # Download and verify integrity
  echo "Downloading binary file for integrity check..."
  curl -s -H "Authorization: Bearer $PCF_TOKEN" \
    -o dev/projects/sdap_V2/test-evidence/task-5.2/downloaded-binary.bin \
    "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/items/$ITEM_ID/content"

  # Compare checksums
  ORIGINAL_SHA=$(sha256sum "$FILE_PATH" | awk '{print $1}')
  DOWNLOADED_SHA=$(sha256sum dev/projects/sdap_V2/test-evidence/task-5.2/downloaded-binary.bin | awk '{print $1}')

  if [ "$ORIGINAL_SHA" == "$DOWNLOADED_SHA" ]; then
    echo "✅ PASS: Binary file integrity verified"
  else
    echo "❌ FAIL: Binary file corrupted"
    exit 1
  fi

  # Clean up
  curl -s -X DELETE \
    -H "Authorization: Bearer $PCF_TOKEN" \
    "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/items/$ITEM_ID"
  echo "✅ Binary file deleted"

else
  echo "❌ FAIL: Binary upload failed with status $HTTP_STATUS"
  exit 1
fi
```

**Expected**:
- HTTP 200 OK
- Binary integrity preserved (checksums match)
- No corruption

---

### Test 7: Error Handling Tests

**Goal**: Verify error scenarios are handled gracefully

#### Test 7.1: Invalid Drive ID

```bash
echo "=== Test 7.1: Invalid Drive ID ==="

INVALID_DRIVE_ID="00000000-0000-0000-0000-000000000000"

RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}" \
  -X PUT \
  -H "Authorization: Bearer $PCF_TOKEN" \
  -H "Content-Type: text/plain" \
  --data "test" \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$INVALID_DRIVE_ID/upload?fileName=test.txt")

HTTP_STATUS=$(echo "$RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)
BODY=$(echo "$RESPONSE" | grep -v "HTTP_STATUS:")

echo "HTTP Status: $HTTP_STATUS"
echo "Error message: $BODY"

if [ "$HTTP_STATUS" == "404" ] || [ "$HTTP_STATUS" == "400" ]; then
  echo "✅ PASS: Invalid Drive ID rejected (404 or 400)"

  # Check for clear error message
  if echo "$BODY" | jq -e '.error or .message' > /dev/null 2>&1; then
    echo "✅ PASS: Error message provided"
  else
    echo "⚠️  WARNING: No clear error message in response"
  fi
else
  echo "❌ FAIL: Expected 404/400, got $HTTP_STATUS"
fi
```

#### Test 7.2: Download Non-Existent File

```bash
echo "=== Test 7.2: Download Non-Existent File ==="

FAKE_ITEM_ID="01FAKEITXM6Y2GOVW7725BZO354PWSELUA"

RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}" \
  -H "Authorization: Bearer $PCF_TOKEN" \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/items/$FAKE_ITEM_ID/content")

HTTP_STATUS=$(echo "$RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)
BODY=$(echo "$RESPONSE" | grep -v "HTTP_STATUS:")

echo "HTTP Status: $HTTP_STATUS"
echo "Error message: $BODY"

if [ "$HTTP_STATUS" == "404" ]; then
  echo "✅ PASS: Non-existent file returns 404"

  # Check for user-friendly error message
  if echo "$BODY" | grep -iq "not found"; then
    echo "✅ PASS: User-friendly error message"
  else
    echo "⚠️  WARNING: Error message could be more user-friendly"
  fi
else
  echo "❌ FAIL: Expected 404, got $HTTP_STATUS"
fi
```

#### Test 7.3: Empty File Upload

```bash
echo "=== Test 7.3: Empty File Upload ==="

# Create empty file
touch dev/projects/sdap_V2/test-files/empty.txt

RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}" \
  -X PUT \
  -H "Authorization: Bearer $PCF_TOKEN" \
  -H "Content-Type: text/plain" \
  --data-binary @dev/projects/sdap_V2/test-files/empty.txt \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/upload?fileName=empty-$(date +%s).txt")

HTTP_STATUS=$(echo "$RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)
BODY=$(echo "$RESPONSE" | grep -v "HTTP_STATUS:")

echo "HTTP Status: $HTTP_STATUS"

if [ "$HTTP_STATUS" == "200" ] || [ "$HTTP_STATUS" == "400" ]; then
  echo "✅ PASS: Empty file handled (200 = accepted, 400 = rejected)"

  if [ "$HTTP_STATUS" == "200" ]; then
    # Clean up
    ITEM_ID=$(echo "$BODY" | jq -r '.id')
    curl -s -X DELETE -H "Authorization: Bearer $PCF_TOKEN" \
      "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/items/$ITEM_ID"
  fi
else
  echo "⚠️  WARNING: Unexpected status $HTTP_STATUS for empty file"
fi
```

---

## Validation Checklist

**Upload Tests**:
- [ ] Small file (<1KB) uploads successfully
- [ ] Medium file (~100KB) uploads successfully
- [ ] Large file (~1MB) uploads successfully
- [ ] Binary file uploads successfully
- [ ] Response contains DTO fields (id, name, size)
- [ ] No SDK types leaked (@odata.type)
- [ ] Performance meets targets (<2s for 1MB)

**Download Tests**:
- [ ] Small file downloads successfully
- [ ] Content integrity verified (SHA256 match)
- [ ] Binary file downloads without corruption
- [ ] Performance meets targets (<2s for 1MB)

**Delete Tests**:
- [ ] Delete returns 204 No Content (or 200 OK)
- [ ] Deleted file returns 404 on subsequent download
- [ ] Performance meets targets (<1s)

**Error Handling**:
- [ ] Invalid Drive ID returns 404/400 with clear message
- [ ] Non-existent file returns 404 with clear message
- [ ] Empty file handled appropriately
- [ ] All error messages are user-friendly

**Performance Summary**:
- [ ] Small file upload: ___s (target: <2s)
- [ ] Large file upload: ___s (target: <10s for 1MB)
- [ ] Download: ___s (target: <2s for 1MB)
- [ ] Delete: ___s (target: <1s)

---

## Pass Criteria

**Task 5.2 is COMPLETE when**:
- ✅ All upload tests pass (small, medium, large, binary)
- ✅ Content integrity verified (SHA256 matches)
- ✅ All download tests pass
- ✅ All delete tests pass
- ✅ Error handling tested and clear
- ✅ Performance meets targets
- ✅ Evidence collected (response JSONs, checksums, timings)

**If ANY test fails**:
- 🛑 STOP - File operations must work before continuing
- 🔍 Investigate root cause
- 🔧 Fix issue (code or infrastructure)
- 🔄 Re-run Task 5.2 from start

---

## Evidence Collection

**Required Evidence**:
1. ✅ Upload responses (JSON for each file size)
2. ✅ Download checksums (original vs downloaded)
3. ✅ Performance timings (upload, download, delete)
4. ✅ Error message screenshots (404, 400 scenarios)
5. ✅ Test file listing (all files created)

**Save to**: `dev/projects/sdap_V2/test-evidence/task-5.2/`

---

## Troubleshooting

### Issue: Upload Returns 413 (Payload Too Large)

**Cause**: File exceeds size limit

**Fix**: Check Azure Web App configuration for request size limits

### Issue: Upload Returns 500

**Cause**: Graph API error or SPE issue

**Fix**: Check application logs for Graph API errors

### Issue: Download Returns Corrupted Content

**Cause**: Encoding issue or streaming problem

**Fix**: Verify Content-Type headers, check DriveItemOperations implementation

### Issue: Delete Returns 404

**Cause**: File doesn't exist or Drive ID wrong

**Fix**: Verify Drive ID and Item ID are correct, check Graph API response

---

## Next Task

✅ **If all tests pass**: [Phase 5 - Task 3: SharePoint Embedded Storage Verification](phase-5-task-3-spe-storage.md)

---

**Last Updated**: 2025-10-14
**Status**: ✅ Template ready for execution
