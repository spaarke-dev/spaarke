# Sprint 7B: Updates Summary - Multi-File Adaptive Upload

**Date:** October 6, 2025
**Updated By:** AI-Directed Coding Session
**Status:** ✅ Overview Updated, Ready for Task Review

---

## Summary of Changes

Sprint 7B Overview has been updated to include **multi-file upload with adaptive strategy** based on user requirements for optimized performance and user experience.

---

## Key Updates

### 1. Multi-File Upload Support Added ✅

**Feature:** Users can now upload 1-10 files in a single Quick Create operation.

**Benefits:**
- Upload multiple documents at once from a single form
- Each file creates a separate Document record
- All documents share the same metadata (description, category, etc.)
- Optimized performance based on file size and count

**Example Use Case:**
```
User opens Quick Create from Matter
  ↓
Selects 5 contract files (PDFs, DOCx, etc.)
  ↓
Fills in shared metadata (Description: "Contract documents", Category: "Legal")
  ↓
Clicks Save
  ↓
5 separate Document records created, each with its own file in SharePoint Embedded
```

---

### 2. Adaptive Upload Strategy ⭐

**Problem Solved:** Different file sizes and counts require different upload approaches for optimal performance.

**Solution:** Automatic strategy selection based on:
- Number of files
- Individual file sizes
- Total upload size

#### Strategy 1: Sync Parallel Upload (Fast Path)

**When:**
- 1-3 files AND
- All files <10MB AND
- Total size <20MB

**Performance:**
```
Example: 2 files × 3MB = 6MB total
Timeline: ~4 seconds
User Experience: Form stays open, shows progress, closes when done
```

**Use Case:** Quick uploads of small documents (contracts, agreements, forms)

#### Strategy 2: Long-Running Process (Safe Path)

**When:**
- More than 3 files OR
- Any file >10MB OR
- Total size >20MB

**Performance:**
```
Example: 5 files × 15MB = 75MB total
Timeline: ~25 seconds (batched upload)
User Experience: Form stays open, shows detailed progress, user cannot close during upload
```

**Use Case:** Larger documents, scanned files, multiple exhibits

**Adaptive Batching:**
- Small files (<1MB): Batch of 5
- Medium files (1-5MB): Batch of 3
- Large files (>5MB): Batch of 2

---

### 3. Future Enhancement Planned: Server-Side Batch Processing

**For Future Sprint (when needed):**

**Trigger:**
- More than 10 files OR
- Total size >100MB OR
- User preference/configuration

**Architecture:**
```
Quick Create → Azure Service Bus → Azure Function → SPE + Dataverse
                                       ↓
                                  Email notification
```

**Benefits:**
- Form closes immediately (no waiting)
- Upload continues even if browser closes
- Resumable uploads for very large files
- Email notification when complete

**Note:** This will be implemented when we have requirements for very large batch uploads.

---

## Technical Implementation Details

### New Services Added

1. **MultiFileUploadService.ts**
   - Implements adaptive strategy decision logic
   - Handles sync-parallel upload
   - Handles long-running batched upload
   - Provides progress callbacks

2. **UploadProgress.tsx (Component)**
   - Shows progress for long-running uploads
   - Displays file-by-file status
   - Prevents form close during upload

### Strategy Decision Logic

```typescript
function determineUploadStrategy(files: File[]): 'sync-parallel' | 'long-running' {
    const totalFiles = files.length;
    const largestFile = Math.max(...files.map(f => f.size));
    const totalSize = files.reduce((sum, f) => sum + f.size, 0);

    // Fast path: Small/few files
    if (
        totalFiles <= 3 &&
        largestFile < 10 * 1024 * 1024 &&      // 10MB
        totalSize < 20 * 1024 * 1024            // 20MB
    ) {
        return 'sync-parallel';
    }

    // Safe path: Large/many files
    return 'long-running';
}
```

### Batch Size Calculation

```typescript
function calculateBatchSize(files: File[]): number {
    const avgSize = files.reduce((sum, f) => sum + f.size, 0) / files.length;

    if (avgSize < 1_000_000) return 5;       // <1MB: batch of 5
    if (avgSize < 5_000_000) return 3;       // 1-5MB: batch of 3
    return 2;                                 // >5MB: batch of 2
}
```

---

## Updated Sprint 7B Task Breakdown

### Task 2: File Upload & SPE Integration (Updated)

**Original Scope:**
- Single file upload only

**New Scope:**
- Single file upload (base functionality)
- Multi-file upload with adaptive strategy
- Sync-parallel upload implementation
- Long-running batched upload implementation
- Progress indicators for both strategies
- Testing with 1, 3, 5, and 10 files

**Time Estimate:** 2-3 days (increased from 1-2 days due to multi-file complexity)

### Other Tasks (Unchanged)

- **Task 1:** Quick Create Setup & MSAL Integration (1-2 days)
- **Task 3:** Default Value Mappings & Configuration (1 day)
- **Task 4:** Testing, Deployment & Sprint 7A Validation (1 day)

**Total Sprint Time:** 5-7 days (increased from 4-6 days)

---

## Performance Comparison

### Scenario 1: Small Files

**Files:** 3 files × 2MB each (6MB total)

| Strategy | Time | User Wait | Form Behavior |
|----------|------|-----------|---------------|
| Sync Parallel | ~3 seconds | ⏳ 3s | Closes when done |
| Sequential (old) | ~9 seconds | ⏳ 9s | N/A |

**Winner:** Sync Parallel (3x faster than sequential)

### Scenario 2: Large Files

**Files:** 5 files × 15MB each (75MB total)

| Strategy | Time | User Wait | Form Behavior |
|----------|------|-----------|---------------|
| Long-Running Batch | ~25 seconds | ⏳ 25s | Shows progress, cannot close |
| Sync Parallel (all) | ~10 seconds | ⏳ 10s | High memory, risky |
| Sequential (old) | ~65 seconds | ⏳ 65s | Very slow |

**Winner:** Long-Running Batch (safe + reasonable speed)

### Scenario 3: Very Large Batch (Future)

**Files:** 20 files × 50MB each (1GB total)

| Strategy | Time | User Wait | Form Behavior |
|----------|------|-----------|---------------|
| Server-Side Queue | N/A | ⏳ 0s | Closes immediately |
| Any Client-Side | Minutes | ⏳ Minutes | Unacceptable |

**Winner:** Server-Side Queue (future enhancement)

---

## User Experience Flow

### Small/Few Files (Fast Path)

```
1. User opens Quick Create from Matter subgrid
2. User clicks file picker → Selects 2 PDFs (3MB each)
3. User fills in:
   - Description: "Employment contracts"
   - Category: "HR Documents"
4. User clicks Save
5. Form shows:
   ┌────────────────────────────┐
   │ Uploading 2 files...       │
   │ ████████████████ 100%      │
   │ ✓ Contract_A.pdf           │
   │ ✓ Contract_B.pdf           │
   └────────────────────────────┘
6. [3 seconds later] Form closes
7. Grid refreshes → Shows 2 new Document records
```

**User Reaction:** "That was fast!" ✅

### Large/Many Files (Safe Path)

```
1. User opens Quick Create from Matter subgrid
2. User clicks file picker → Selects 5 scanned documents (15MB each)
3. User fills in:
   - Description: "Discovery documents"
   - Category: "Litigation"
4. User clicks Save
5. Form shows:
   ┌────────────────────────────────────┐
   │ Uploading 5 files...               │
   │ Estimated time: 25 seconds         │
   │                                    │
   │ ████████░░░░░░░░░░░░ 40% (2 of 5) │
   │                                    │
   │ ✓ Doc_01.pdf (15.2 MB)             │
   │ ✓ Doc_02.pdf (14.8 MB)             │
   │ ↻ Doc_03.pdf (uploading...)        │
   │ ⏳ Doc_04.pdf (waiting)             │
   │ ⏳ Doc_05.pdf (waiting)             │
   │                                    │
   │ Please keep this window open       │
   └────────────────────────────────────┘
6. [User waits ~25 seconds, sees progress]
7. ✓ Upload complete! Closing in 2 seconds...
8. Form closes
9. Grid refreshes → Shows 5 new Document records
```

**User Reaction:** "Great, I can see exactly what's happening." ✅

---

## Configuration Options (for future tuning)

```typescript
export interface MultiFileUploadConfig {
    maxFiles: number;                    // Default: 10
    maxFileSize: number;                 // Default: 50MB per file
    maxTotalSize: number;                // Default: 200MB total
    syncParallelThreshold: {
        maxFiles: number;                // Default: 3
        maxFileSize: number;             // Default: 10MB
        maxTotalSize: number;            // Default: 20MB
    };
    batchSize: number;                   // Default: 3 (or adaptive)
    adaptiveBatching: boolean;           // Default: true
    allowedFileTypes: string[];          // Default: all files
    showProgressFor: 'all' | 'large';   // Default: 'large'
}
```

These can be configured later via:
- PCF manifest parameters
- Dataverse environment variables
- User preferences

---

## Success Criteria (Updated)

### Sprint 7B Completion

- [ ] Single file upload working
- [ ] Multi-file upload working (1-10 files)
- [ ] Adaptive strategy selection working
- [ ] Sync-parallel upload tested (1-3 small files)
- [ ] Long-running upload tested (5+ files or large files)
- [ ] Progress indicators working for both strategies
- [ ] One Document record per file created
- [ ] Shared metadata applied to all documents
- [ ] Form behavior correct for both strategies
- [ ] MSAL authentication working throughout
- [ ] Real test files created for Sprint 7A validation

---

## Testing Plan (Added to Task 4)

### Test Scenarios

**Scenario 1: Single File (Baseline)**
- Upload 1 file (5MB PDF)
- Expected: Sync-parallel, ~2 seconds

**Scenario 2: Few Small Files (Sync-Parallel)**
- Upload 3 files (2MB each)
- Expected: Sync-parallel, ~3-4 seconds

**Scenario 3: Threshold Test (Boundary)**
- Upload 3 files (10MB each, exactly 30MB total)
- Expected: Long-running (exceeds 20MB total threshold)

**Scenario 4: Many Files (Long-Running)**
- Upload 5 files (3MB each, 15MB total)
- Expected: Long-running (exceeds 3 file threshold)

**Scenario 5: Large Files (Long-Running)**
- Upload 2 files (20MB each, 40MB total)
- Expected: Long-running (exceeds 10MB per file threshold)

**Scenario 6: Maximum Files (Stress Test)**
- Upload 10 files (5MB each, 50MB total)
- Expected: Long-running with batching, ~35 seconds

**Scenario 7: Mixed Sizes (Adaptive Batching)**
- Upload 6 files (1MB, 2MB, 5MB, 8MB, 12MB, 15MB)
- Expected: Long-running with adaptive batch sizes

---

## Documentation Updated

✅ **SPRINT-7B-OVERVIEW.md** - Updated with:
- Multi-File Upload section (300+ lines)
- Adaptive strategy decision tree
- Both upload strategy implementations
- Future enhancement (server-side batch)
- Performance comparisons
- User experience flows
- Updated task breakdown
- Updated success criteria

📋 **Next:** Review and update individual task documents (TASK-7B-1 through TASK-7B-4)

---

## Critical Reminders for Implementation

### 🔴 MSAL Authentication
- Use Sprint 8 MSAL pattern from day one
- Token caching makes multi-file upload fast (5ms after first file)
- Handle race conditions (MSAL initialization)

### 🔴 Error Handling
- If one file fails, continue with remaining files
- Show summary: "4 of 5 files uploaded successfully"
- Collect failed files and allow retry

### 🔴 User Experience
- Always show progress for long-running uploads
- Prevent form close during upload
- Auto-close form 2 seconds after completion
- Clear visual feedback (✓ uploaded, ↻ uploading, ⏳ waiting)

### 🔴 Performance
- Use adaptive batch sizing based on file sizes
- Don't exceed browser connection limits (6-8 parallel connections)
- Sequential Dataverse record creation (avoid throttling)

### 🔴 Future Extensibility
- Code should allow easy addition of server-side strategy
- Configuration-driven thresholds (not hardcoded)
- Plan for Service Bus queue integration

---

## Next Steps

1. ✅ Overview updated with multi-file adaptive strategy
2. → Review TASK-7B-1 (Quick Create Setup) - ensure alignment
3. → Update TASK-7B-2 (File Upload) - add multi-file implementation details
4. → Review TASK-7B-3 (Default Values) - no changes expected
5. → Update TASK-7B-4 (Testing) - add multi-file test scenarios
6. → Begin Sprint 7B Task 1 implementation

---

**Document Owner:** Sprint 7B Planning
**Created:** October 6, 2025
**Status:** ✅ Overview Complete
**Next Action:** Review individual task documents
