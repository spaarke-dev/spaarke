# Document Viewer Remediation Plan

> **Created**: 2026-01-15
> **Status**: Draft - Pending Review
> **Related Project**: email-to-document-automation-r2
> **Prior Project**: projects/x-document-checkout-viewer (December 2025)

---

## Executive Summary

This plan addresses technical debt and consolidation needed for SPE document viewing across the platform. Currently, three separate implementations exist:

1. **SpeFileViewer** (v2.0.1) - Bundles React 19 (ADR-022 violation), preview + open features
2. **SpeDocumentViewer** (v1.0.12) - Platform-compliant, full checkout/checkin workflow
3. **AnalysisWorkspace/SourceDocumentViewer** - Duplicated preview implementation

The goal is to consolidate on **SpeDocumentViewer** as the single document viewing solution, then extract shared components for reuse.

---

## Historical Context (CRITICAL)

### Prior Project: x-document-checkout-viewer (December 2025)

A comprehensive project was initiated in December 2025 to build the SpeDocumentViewer PCF control with full checkout/checkin workflow. **The project completed 78% of its tasks (18 of 23) but Phase 5 (Migration) was never started.**

| Phase | Tasks | Status |
|-------|-------|--------|
| Phase 1: Schema | 001-003 | ✅ Complete |
| Phase 2: BFF API | 010-015 | ✅ Complete |
| Phase 3: PCF Control | 020-025 | ✅ Complete |
| Phase 4: Delete & Ribbon | 030-032 | ✅ Complete |
| **Phase 5: Migration** | **050-053, 090** | **❌ NOT STARTED** |

### Phase 5 Tasks That Were Never Completed

| Task | Title | What Was Planned |
|------|-------|------------------|
| 050 | Migrate SpeFileViewer to SpeDocumentViewer | Replace SpeFileViewer usage with SpeDocumentViewer |
| 051 | Migrate SourceDocumentViewer in AnalysisWorkspace | Refactor to use shared component |
| 052 | Integrate AI Analysis on Check-In | Trigger AI processing on document check-in |
| 053 | Update Documentation | Complete project documentation |
| 090 | Project Wrap-up | Finalize and close project |

### What This Means

SpeDocumentViewer **was built and deployed to Dataverse** but **was never added to any forms**. The control exists and works, but the migration work to replace SpeFileViewer and integrate it into forms was never completed.

### Form Audit Results

A search of the codebase confirms:
- **No forms currently use SpeFileViewer** (grep for "SpeFileViewer" in form configurations returned no matches)
- **No forms currently use SpeDocumentViewer** (control deployed but not bound to any forms)
- The Document entity form does not have any SPE viewer control bound

---

## Current State Assessment

### SpeFileViewer (to be deprecated)

| Aspect | Status |
|--------|--------|
| Location | `src/client/pcf/SpeFileViewer/` |
| Version | 2.0.1 |
| React | 19+ (bundled) - **ADR-022 VIOLATION** |
| Platform Libraries | None declared |
| Features | Preview (real-time), Open Desktop, Open Web, Checkout badge |
| API Endpoint | `getViewUrl()` - real-time, no cache |

### SpeDocumentViewer (target solution)

| Aspect | Status |
|--------|--------|
| Location | `src/client/pcf/SpeDocumentViewer/` |
| Version | 1.0.12 |
| React | 16.14.0 (platform-provided) - **COMPLIANT** |
| Platform Libraries | React 16.14.0, Fluent 9.46.2 |
| Features | Preview, Checkout/Checkin/Discard, Delete, Download (BFF proxy), Design mode |
| API Endpoint | `getPreviewUrl()` - has 30-60s cache delay |

### AnalysisWorkspace/SourceDocumentViewer (to be refactored)

| Aspect | Status |
|--------|--------|
| Location | `src/client/pcf/AnalysisWorkspace/control/components/SourceDocumentViewer.tsx` |
| Features | Preview, Refresh, Open in New Tab, Fullscreen |
| Code Duplication | ~300 lines duplicated from SpeDocumentViewer patterns |
| API Endpoint | `getPreviewUrl()` |

---

## Gap Analysis

### Features Missing in SpeDocumentViewer

| Feature | In SpeFileViewer | In SpeDocumentViewer | Priority |
|---------|------------------|----------------------|----------|
| Real-time preview (`getViewUrl`) | ✅ | ❌ | **P1** |
| "Open in Web" button (Office Online) | ✅ | ❌ | **P2** |
| `getOfficeUrl()` API | ✅ | ❌ | **P3** |

### Architecture Issues

| Issue | Impact | Priority |
|-------|--------|----------|
| SpeFileViewer bundles React 19 | ADR-022 violation, potential conflicts | **P1** |
| Three separate viewer implementations | Maintenance burden, inconsistent UX | **P2** |
| No shared component library for preview | Code duplication | **P3** |

---

## Remediation Tasks

### P1 - Critical (Required for email-to-document project)

These tasks must be completed to enable Document form viewing for .eml files.

#### P1.1: Add `getViewUrl` to SpeDocumentViewer BffClient

**Problem**: SpeDocumentViewer uses `getPreviewUrl()` which has a 30-60 second cache delay. SpeFileViewer uses `getViewUrl()` for real-time preview.

**Solution**: Add `getViewUrl()` method to SpeDocumentViewer's BffClient.

**Files to modify**:
- `src/client/pcf/SpeDocumentViewer/control/BffClient.ts` - Add method
- `src/client/pcf/SpeDocumentViewer/control/types.ts` - Ensure types match

**Effort**: 1 hour

**Acceptance Criteria**:
- [ ] `getViewUrl()` method added matching SpeFileViewer implementation
- [ ] Method returns `FilePreviewResponse` with checkoutStatus
- [ ] Unit test coverage

---

#### P1.2: Switch useDocumentPreview to Real-Time Endpoint

**Problem**: The hook currently calls `getPreviewUrl()`. Need to use `getViewUrl()` for real-time preview.

**Solution**: Update the hook to call the new endpoint.

**Files to modify**:
- `src/client/pcf/SpeDocumentViewer/control/hooks/useDocumentPreview.ts` - Line 148

**Effort**: 30 minutes

**Acceptance Criteria**:
- [ ] Hook calls `bffClient.getViewUrl()` instead of `getPreviewUrl()`
- [ ] Preview shows real-time document state (no cache delay)
- [ ] Checkout status still displayed correctly

---

#### P1.3: Deploy SpeDocumentViewer to Document Form

**Problem**: SpeDocumentViewer is not currently added to any forms.

**Solution**: Add to Document entity main form with appropriate configuration.

**Configuration**:
```
Table column: sprk_name (Document Name)
clientAppId: [from environment]
bffAppId: [from environment]
tenantId: [from environment]
enableEdit: false (for .eml files, true for editable docs)
enableDelete: false
enableDownload: true
```

**Effort**: 30 minutes

**Acceptance Criteria**:
- [ ] SpeDocumentViewer visible on Document main form
- [ ] .eml files preview correctly
- [ ] Download button works for .eml files
- [ ] No errors in form designer (design mode detection working)

---

### P2 - Important (Feature parity with SpeFileViewer)

These tasks ensure SpeDocumentViewer has all features from SpeFileViewer before deprecation.

#### P2.1: Add "Open in Web" Button (Hidden for .eml)

**Problem**: SpeFileViewer has "Open in Web" button for Office Online editing. SpeDocumentViewer lacks this.

**Solution**: Add the button and handler to toolbar, but hide for unsupported file types.

**Files to modify**:
- `src/client/pcf/SpeDocumentViewer/control/SpeDocumentViewer.tsx` - Add handler
- `src/client/pcf/SpeDocumentViewer/control/components/Toolbar.tsx` - Add button with visibility logic

**Implementation**:
```typescript
// Check if file type supports Office Online
const supportsOpenInWeb = useMemo(() => {
    const extension = documentInfo?.fileExtension?.toLowerCase();
    const officeExtensions = ['.docx', '.xlsx', '.pptx', '.doc', '.xls', '.ppt'];
    return extension && officeExtensions.includes(extension);
}, [documentInfo?.fileExtension]);

const handleOpenInWeb = useCallback(async () => {
    const response = await bffClient.current.getOpenLinks(documentId, accessToken, correlationId);
    if (response.webUrl) {
        window.open(response.webUrl, '_blank', 'noopener,noreferrer');
    }
}, [documentId, accessToken, correlationId]);
```

**Effort**: 1 hour

**Acceptance Criteria**:
- [ ] "Open in Web" button appears for Office file types (.docx, .xlsx, .pptx, .doc, .xls, .ppt)
- [ ] Button opens Office Online in new tab
- [ ] **Button hidden for .eml files** (no Office Online viewer exists)
- [ ] Button hidden for other non-Office files (.pdf, .txt, etc.)
- [ ] Loading state while fetching URL

---

#### P2.2: Version Bump and Build

**Problem**: Changes require new version deployment.

**Solution**: Bump version to 1.0.13 per PCF-V9-PACKAGING.md protocol.

**Files to modify** (4 locations):
- `control/ControlManifest.Input.xml` - version attribute
- `solution/src/Other/Solution.xml` - Version element
- `solution/src/Controls/.../ControlManifest.xml` - version attribute
- `control/SpeDocumentViewer.tsx` - CONTROL_VERSION constant

**Effort**: 15 minutes

**Acceptance Criteria**:
- [ ] All 4 version locations updated to 1.0.13
- [ ] `npm run build` succeeds
- [ ] Solution zip generated

---

#### P2.3: Deploy Updated SpeDocumentViewer

**Problem**: New version needs deployment to Dataverse.

**Solution**: Use `pac solution import` or solution import wizard.

**Effort**: 30 minutes

**Acceptance Criteria**:
- [ ] Solution imported successfully
- [ ] Version 1.0.13 visible in form control footer
- [ ] All features working

---

### P3 - Technical Debt (Future consolidation)

These tasks address long-term maintainability and code reuse.

#### P3.1: Formally Deprecate SpeFileViewer

**Problem**: SpeFileViewer bundles React 19, violating ADR-022. It should not be used.

**Solution**:
1. Add deprecation notice to SpeFileViewer README
2. Update any forms still using SpeFileViewer to use SpeDocumentViewer
3. Do NOT delete code yet (may have users)

**Files to modify**:
- `src/client/pcf/SpeFileViewer/README.md` - Add deprecation notice
- Forms using SpeFileViewer - Migrate to SpeDocumentViewer

**Effort**: 2 hours (including form migration)

**Acceptance Criteria**:
- [ ] Deprecation notice added
- [ ] No forms actively using SpeFileViewer
- [ ] Documentation updated

---

#### P3.2: Extract Shared DocumentPreview Component

**Problem**: AnalysisWorkspace duplicates ~300 lines of preview logic.

**Solution**: Extract shared component to `@spaarke/ui-components` package.

**Shared components**:
- `useDocumentPreview` hook
- `BffClient` (or shared interface)
- `DocumentPreview` React component with configurable toolbar

**Configuration props**:
```typescript
interface DocumentPreviewProps {
    documentId: string;
    bffApiUrl: string;
    accessToken: string;
    correlationId: string;
    isDarkTheme?: boolean;

    // Feature flags
    enableEdit?: boolean;      // Show edit/checkout buttons
    enableDelete?: boolean;    // Show delete button
    enableDownload?: boolean;  // Show download button
    enableOpenInWeb?: boolean; // Show "Open in Web" button
    enableFullscreen?: boolean; // Show fullscreen button

    // Callbacks
    onRefresh?: () => void;
    onDeleted?: () => void;
    onFullscreen?: () => void;
}
```

**Effort**: 4-6 hours

**Acceptance Criteria**:
- [ ] Shared component in `src/client/shared/components/`
- [ ] SpeDocumentViewer uses shared component
- [ ] Unit tests for shared component

---

#### P3.3: Refactor AnalysisWorkspace to Use Shared Component

**Problem**: SourceDocumentViewer.tsx duplicates preview functionality.

**Solution**: Replace with shared DocumentPreview component.

**Files to modify**:
- `src/client/pcf/AnalysisWorkspace/control/components/SourceDocumentViewer.tsx` - Replace implementation

**Effort**: 2-4 hours

**Acceptance Criteria**:
- [ ] SourceDocumentViewer uses shared component
- [ ] Fullscreen callback working
- [ ] No duplicate code
- [ ] All existing features preserved

---

## Implementation Approach: Resume x-document-checkout-viewer

### Recommendation: Resume Prior Project (PREFERRED)

**Resume `projects/x-document-checkout-viewer`** rather than creating new tasks within email-to-document-automation-r2.

**Rationale**:
1. x-document-checkout-viewer has all context (ADRs, architecture docs, patterns)
2. Phase 5 tasks (050-053) are exactly what needs to be done
3. Avoids duplicate project overhead
4. Maintains project history and decisions

### Required Task Updates

The existing Phase 5 tasks need modification before resuming:

| Original Task | Status | Required Updates |
|--------------|--------|------------------|
| 050 | not-started | Remove SpeFileViewer migration (no forms use it), Add: deploy to Document form |
| 051 | not-started | Keep as-is (SourceDocumentViewer migration) |
| 052 | not-started | Keep as-is (AI integration on check-in) |
| 053 | not-started | Keep as-is (documentation) |
| 090 | not-started | Keep as-is (wrap-up) |

### New Tasks to Insert (Before 050)

| New ID | Title | Priority | Description |
|--------|-------|----------|-------------|
| 045 | Add getViewUrl to BffClient | P1 | Real-time preview without cache delay |
| 046 | Switch to Real-Time Preview | P1 | Update useDocumentPreview hook |
| 047 | Add Open in Web Button | P2 | Hidden for .eml, visible for Office files |

### Updated Execution Order

```
Resume x-document-checkout-viewer at Phase 5:

NEW Tasks (Insert before 050):
├── 045: Add getViewUrl to SpeDocumentViewer BffClient (P1)
├── 046: Switch useDocumentPreview to getViewUrl (P1)
├── 047: Add "Open in Web" button (hidden for .eml) (P2)
└── Version bump to 1.0.13

Existing Tasks (Update and execute):
├── 050: Deploy SpeDocumentViewer to Document form (SIMPLIFIED - no migration needed)
├── 051: Migrate SourceDocumentViewer in AnalysisWorkspace (P3)
├── 052: Integrate AI Analysis on Check-In (defer if not in scope)
├── 053: Update Documentation
└── 090: Project Wrap-up
```

---

## Alternative: New Tasks in email-to-document-automation-r2

If preferred, create new tasks in current project instead of resuming x-document-checkout-viewer.

**Tradeoffs**:
| Approach | Pros | Cons |
|----------|------|------|
| Resume x-document-checkout-viewer | Full context preserved, no duplication | Need to move/copy worktree focus |
| New tasks in email-to-document-r2 | Stays in current project | Loses prior project context |

---

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| `getViewUrl` endpoint doesn't exist in BFF | Low | High | Verify endpoint exists before starting |
| Office Online URLs not available for all docs | Medium | Low | Hide button for unsupported types |
| AnalysisWorkspace regression | Medium | Medium | Thorough testing before P3.3 |
| SpeFileViewer still in use somewhere | Low | Medium | Search codebase before deprecation |

---

## Success Metrics

- [ ] Document form shows .eml preview without errors
- [ ] Download button works for email-to-document created files
- [ ] No React version conflicts
- [ ] Single viewer implementation (SpeDocumentViewer) handles all use cases
- [ ] AnalysisWorkspace preview working with shared code

---

## Resolved Questions

1. **Are there other forms currently using SpeFileViewer?**
   - ✅ **RESOLVED**: Audit confirmed no forms currently use SpeFileViewer
   - grep search for "SpeFileViewer" in form configurations returned no matches
   - Safe to deprecate without form migration

2. **Should "Open in Web" be enabled for .eml files?**
   - ✅ **RESOLVED**: Hide button for .eml files
   - SharePoint/Office Online has no web viewer for .eml format
   - Button will check file extension and hide for `.eml`

3. **Is the P3 shared component work in scope for email-to-document project?**
   - ✅ **RESOLVED**: Yes, P3 is in scope for this project
   - Completing Phase 5 migration work that was never finished in x-document-checkout-viewer

---

## Appendix: File References

| Component | Path |
|-----------|------|
| SpeFileViewer | `src/client/pcf/SpeFileViewer/` |
| SpeDocumentViewer | `src/client/pcf/SpeDocumentViewer/` |
| SourceDocumentViewer | `src/client/pcf/AnalysisWorkspace/control/components/SourceDocumentViewer.tsx` |
| BffClient (FileViewer) | `src/client/pcf/SpeFileViewer/control/BffClient.ts` |
| BffClient (DocViewer) | `src/client/pcf/SpeDocumentViewer/control/BffClient.ts` |
| useDocumentPreview | `src/client/pcf/SpeDocumentViewer/control/hooks/useDocumentPreview.ts` |
| Toolbar | `src/client/pcf/SpeDocumentViewer/control/components/Toolbar.tsx` |
