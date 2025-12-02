# Task 1.1: Add TypeScript Interfaces

**Phase**: 1 - Core Functionality
**Priority**: High (Blocking)
**Estimated Time**: 15 minutes
**Depends On**: None
**Blocks**: Task 1.2, Task 1.3

---

## Objective

Add TypeScript interfaces to support Office editor mode functionality in the SpeFileViewer PCF control.

## Context & Knowledge Required

### What You Need to Know
1. **TypeScript Interface Syntax**: Basic understanding of TypeScript interfaces and type definitions
2. **BFF API Response Structure**: The `/api/documents/{id}/office` endpoint returns structured JSON with office URL and permissions
3. **React Component State**: FilePreview component state needs to track both preview and editor modes
4. **Existing Types**: Familiarity with current `types.ts` file structure

### Files to Review Before Starting
- **Current types.ts**: [C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\types.ts](C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\types.ts)
- **BffClient.ts**: [C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\BffClient.ts](C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\BffClient.ts) (see FilePreviewResponse for pattern)
- **FileAccessEndpoints.cs**: [c:\code_files\spaarke\src\api\Spe.Bff.Api\Api\FileAccessEndpoints.cs](c:\code_files\spaarke\src\api\Spe.Bff.Api\Api\FileAccessEndpoints.cs#L323-L388) (GetOffice endpoint)

---

## Implementation Prompt

### Step 1: Add OfficeUrlResponse Interface

**Location**: `C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\types.ts`
**Insert After**: Existing `FilePreviewResponse` interface (~line 40)

```typescript
/**
 * Response from BFF API /api/documents/{id}/office endpoint
 *
 * This interface defines the structure returned when requesting
 * an Office Online editor URL for a document.
 */
export interface OfficeUrlResponse {
    /**
     * Office Online editor URL (webUrl from Microsoft Graph API)
     *
     * This URL opens the document in Office Online with full
     * editor capabilities (subject to user permissions).
     *
     * Example: "https://tenant.sharepoint.com/_layouts/15/Doc.aspx?..."
     */
    officeUrl: string;

    /**
     * User's permissions on the file
     *
     * Indicates what level of access the authenticated user has.
     * Note: Office Online will ultimately enforce these permissions.
     */
    permissions: {
        /**
         * Can the user edit the file?
         *
         * If false, Office Online will load in read-only mode.
         */
        canEdit: boolean;

        /**
         * Can the user view the file?
         *
         * Should always be true if this endpoint returns successfully.
         */
        canView: boolean;

        /**
         * User's role on the file
         *
         * Possible values: 'owner' | 'editor' | 'reader' | 'unknown'
         */
        role: string;
    };

    /**
     * Correlation ID for distributed tracing
     *
     * Should match the X-Correlation-Id sent in the request.
     * Used for debugging and log correlation.
     */
    correlationId: string;
}
```

### Step 2: Update FilePreviewState Interface

**Location**: Same file, find `FilePreviewState` interface (~line 55)

**Current State**:
```typescript
export interface FilePreviewState {
    previewUrl: string | null;
    isLoading: boolean;
    error: string | null;
    documentInfo: DocumentInfo | null;
}
```

**Updated State**:
```typescript
export interface FilePreviewState {
    /** SharePoint preview URL (embed.aspx with nb=true) */
    previewUrl: string | null;

    /** Office Online editor URL (webUrl from Graph API) */
    officeUrl: string | null;

    /** Loading state for async operations */
    isLoading: boolean;

    /** Error message to display to user */
    error: string | null;

    /** Document metadata from BFF API */
    documentInfo: DocumentInfo | null;

    /**
     * Current display mode
     *
     * 'preview': Read-only preview mode (default)
     * 'editor': Office Online editor mode
     */
    mode: 'preview' | 'editor';

    /**
     * Whether to show read-only permission dialog
     *
     * Set to true when user opens editor but lacks edit permissions.
     */
    showReadOnlyDialog: boolean;
}
```

---

## Validation & Review

### Pre-Commit Checklist

Run these checks **before** committing changes:

1. **TypeScript Compilation**:
   ```bash
   cd C:/code_files/spaarke/src/controls/SpeFileViewer/SpeFileViewer
   npm run build
   ```
   - [ ] No TypeScript errors
   - [ ] No type checking warnings

2. **Interface Alignment**:
   - [ ] `OfficeUrlResponse` matches BFF API response structure
   - [ ] `FilePreviewState` includes all new properties
   - [ ] All properties have JSDoc comments

3. **Naming Conventions**:
   - [ ] Interface names use PascalCase
   - [ ] Property names use camelCase
   - [ ] Consistent with existing types (e.g., `FilePreviewResponse`)

4. **Code Review**:
   ```bash
   # Review the changes
   git diff C:/code_files/spaarke/src/controls/SpeFileViewer/SpeFileViewer/types.ts
   ```
   - [ ] No unintended changes
   - [ ] Comments are clear and helpful

### Post-Implementation Verification

1. **Read the Updated File**:
   ```bash
   # Verify changes are correct
   cat C:/code_files/spaarke/src/controls/SpeFileViewer/SpeFileViewer/types.ts | grep -A 30 "OfficeUrlResponse"
   ```

2. **Check Import Usage**:
   ```bash
   # Ensure no breaking changes to existing imports
   grep -r "FilePreviewState" C:/code_files/spaarke/src/controls/SpeFileViewer/SpeFileViewer/
   ```

---

## Acceptance Criteria

- [x] `OfficeUrlResponse` interface added with complete JSDoc comments
- [x] `permissions` object structure matches BFF API response
- [x] `FilePreviewState` updated with `officeUrl`, `mode`, and `showReadOnlyDialog`
- [x] TypeScript compiles without errors
- [x] All properties have descriptive JSDoc comments
- [x] No breaking changes to existing code
- [x] Git diff shows only intended changes

---

## Common Issues & Solutions

### Issue 1: TypeScript Compilation Errors
**Symptom**: `npm run build` fails with type errors in FilePreview.tsx

**Solution**: This is expected! FilePreview.tsx references the old state structure. This will be fixed in Task 1.3.

**Workaround**: Comment out FilePreview.tsx temporarily to verify types.ts is correct:
```bash
# Backup FilePreview.tsx
cp C:/code_files/spaarke/src/controls/SpeFileViewer/SpeFileViewer/FilePreview.tsx FilePreview.tsx.bak

# Temporarily comment out the component (will restore in Task 1.3)
```

### Issue 2: Interface Property Mismatch
**Symptom**: BFF API returns different properties than defined

**Solution**: Review [FileAccessEndpoints.cs:360-387](c:\code_files\spaarke\src\api\Spe.Bff.Api\Api\FileAccessEndpoints.cs#L360-L387) to confirm exact response structure.

---

## Dependencies

### Required Before This Task
- ✅ None (this is the first task)

### Required After This Task
- Task 1.2: BffClient method will use `OfficeUrlResponse`
- Task 1.3: FilePreview state will use updated `FilePreviewState`

---

## Files Modified

- `C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\types.ts`

---

## Estimated Effort Breakdown

| Activity | Time |
|----------|------|
| Review existing types.ts | 3 min |
| Add OfficeUrlResponse interface | 5 min |
| Update FilePreviewState interface | 4 min |
| Add JSDoc comments | 2 min |
| Verify TypeScript compilation | 1 min |
| **Total** | **15 min** |

---

## Next Task

**Task 1.2**: Add BffClient.getOfficeUrl() Method
- Uses the `OfficeUrlResponse` interface created in this task
- Implements HTTP call to `/api/documents/{id}/office` endpoint
