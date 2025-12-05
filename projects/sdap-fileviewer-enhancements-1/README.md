# SDAP FileViewer Enhancements 1

> **Last Updated**: December 4, 2025
>
> **Status**: Complete

## Overview

This project replaces embedded Office Web App editing in the FileViewer PCF with "Open in Desktop" functionality, and implements improved loading-state UX for document preview. The changes eliminate cramped editing experiences, unwanted Word commands, and black-screen flash during initialization.

## Quick Links

| Document | Description |
|----------|-------------|
| [Design Spec](./spec.md) | Technical design specification |
| [Project Plan](./plan.md) | Implementation plan and timeline |
| [Tasks](./tasks/) | Task breakdown and status |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Complete |
| **Progress** | 100% |
| **Completed** | December 2025 |
| **Owner** | Spaarke Engineering |

## Problem Statement

The current FileViewer PCF uses an embedded Office Web App for edit mode, causing:
- Exposure of unwanted Word commands (Delete, Share)
- SPE Copilot limitations
- Narrow, cramped editing UX inside the model-driven form
- Slow load time on first preview due to App Service / SPE / Office cold start
- Black-screen flash during Office preview initialization

## Solution Summary

Replace embedded "Edit in Web" with "Open in Desktop" for Word/Excel/PowerPoint editing. Implement a polished loading-state UX with white background and loading skeleton instead of black boxes. Add warm-up patterns to reduce perceived latency on first load.

## Graduation Criteria

The project is considered **complete** when:

- [x] BFF `/files/{driveId}/{itemId}/open-links` endpoint deployed and functional
- [x] FileViewer PCF updated with loading overlay and "Open in Desktop" button
- [x] No black-screen flash during preview initialization
- [x] App Service "Always On" enabled with warm-up endpoint
- [x] All existing tests passing, new functionality tested
- [x] Deployed to production

## Scope

### In Scope

- BFF endpoint for open-links (desktop URL, web URL, MIME type)
- FileViewer PCF preview UX improvements (loading state, white background)
- FileViewer PCF "Open in Desktop" edit mode
- App Service warm-up configuration
- Performance optimizations (pre-fetch, singleton Graph client)

### Out of Scope

- Office.js "Share via Spaarke" Add-in
- Spaarke AI pane inside Word desktop
- Dataverse Document/UAC model modifications
- SPE container-level permission changes

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Desktop edit over embedded web edit | Better UX, no unwanted commands, full Office experience | — |
| Endpoint filters for auth | Per ADR-008, no global middleware | [ADR-008](../../docs/reference/adr/ADR-008-authorization-endpoint-filters.md) |
| Graph calls through SpeFileStore | Per ADR-007, isolate Graph SDK | [ADR-007](../../docs/reference/adr/ADR-007-spe-storage-seam-minimalism.md) |
| PCF control pattern | Per ADR-006, no legacy webresources | [ADR-006](../../docs/reference/adr/ADR-006-prefer-pcf-over-webresources.md) |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Desktop app not installed | Medium | Low | Fallback to webUrl if desktop protocol fails |
| Cold start latency persists | Medium | Medium | Always On + warm-up endpoint + pre-fetch |
| Cross-browser iframe timing | Low | Medium | Test across browsers, use onload event |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| SpeFileStore.GetFileMetadataAsync | Internal | Ready | Existing method |
| App Service configuration | Internal | Ready | Requires deployment config change |
| Microsoft Graph API | External | Ready | Already integrated |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Spaarke Engineering | Overall accountability |
| Developer | AI Agent | Implementation |
| Reviewer | Human | Code review, design review |

## Known Limitations

### "Open in Web" Exposes Share and Delete Commands

**Issue:** When users click "Open in Web", Office Online opens in a new browser tab with full functionality, including the Share and Delete buttons in the Office toolbar.

**Current Behavior:**
- Share button is visible but sharing outside the organization is blocked by tenant policy
- Delete button is visible and functional
- These buttons exist because Office Online provides the full SharePoint document experience

**Impact:** Medium - Users could inadvertently delete documents via the Office Online interface rather than through the controlled SDAP workflow.

**Recommended Resolution (Future Enhancement):**
1. **Option A: SharePoint Embedded URL customization** - Investigate if SPE supports URL parameters to disable specific commands (similar to `&nb=true` for disabling navigation bar)
2. **Option B: Conditional Access Policy** - Use Azure AD Conditional Access or SharePoint policies to restrict delete operations
3. **Option C: SPE permissions model** - Adjust container-level or item-level permissions to remove delete rights for specific user roles
4. **Option D: Custom Office Add-in** - Build an Office.js add-in that intercepts and hides specific ribbon commands

**Workaround:** For now, rely on organizational training and the fact that "Open in Desktop" is the primary recommended workflow. "Open in Web" is positioned as a fallback for users without desktop Office.

---

### Office Protocol URL Format

**Issue Resolved:** The standard Office protocol URL format (`ms-word:ofe|u|{url}`) was blocked by Windows Security Zones for SharePoint Embedded `/contentstorage/` paths.

**Solution:** Changed to abbreviated protocol format (`ms-word:{url}`) which bypasses the zone restriction. Files open in Protected View, and users click "Enable Editing" to switch to edit mode.

**Location:** `src/server/shared/Spaarke.Core/Utilities/DesktopUrlBuilder.cs`

---

## Lessons Learned

### PCF Solution Packaging - File Lock Workaround

**Issue:** When running `pac pcf push`, the build completes successfully but fails during cleanup with:
```
error MSB3231: Unable to remove directory "obj\Debug\Metadata". The process cannot access the file because it is being used by another process.
```

**Root Cause:** Windows file lock on the temporary metadata folder during Solution Packager cleanup phase.

**Workaround:** The solution zip file IS generated successfully before the cleanup error. Import it directly:

```bash
# Instead of relying on pac pcf push to complete
pac solution import \
  --path /c/code_files/spaarke/src/client/pcf/SpeFileViewer/obj/PowerAppsToolsTemp_sprk/bin/Debug/PowerAppsToolsTemp_sprk.zip \
  --async true \
  --publish-changes true
```

**Reference:** This workaround should be added to `docs/reference/research/KM-PCF-COMPONENT-DEPLOYMENT.md`

---

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2025-12-04 | 1.0 | Initial project setup from spec | AI Agent |
| 2025-12-04 | 1.1 | Added "Open in Desktop" feature with abbreviated protocol | AI Agent |
| 2025-12-04 | 1.2 | Added "Open in Web" fallback button | AI Agent |
| 2025-12-04 | 1.3 | Documented known limitations and lessons learned | AI Agent |
| 2025-12-04 | 1.4 | Project complete - all phases deployed to production | AI Agent |

---

*Generated from spec.md via project-init skill*
