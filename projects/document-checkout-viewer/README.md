# Document Check-Out/Check-In Viewer

> **Last Updated**: 2025-12-18
>
> **Status**: In Progress

## Overview

A unified document viewing and editing component for the Spaarke platform that provides secure preview mode, full Office Online editing with version control, and file operations. This component consolidates `SpeFileViewer` and `SourceDocumentViewer` into a single reusable component while addressing security concerns with the SharePoint embed Share button exposure.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation phases and deliverables |
| [Design Spec](./spec.md) | Full technical specification |
| [Tasks](./tasks/) | Task files (POML format) |
| [Architecture](./SPE-FILE-VIEWER-ARCHITECTURE.md) | Component architecture details |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Design Complete |
| **Progress** | 10% |
| **Owner** | Spaarke Development Team |

## Problem Statement

1. **Security**: Office Online embed view exposes Share button, allowing users to share documents outside the DMS
2. **Version Control**: No explicit version commit points - document changes are continuous
3. **AI Analysis**: AI needs stable document states to analyze, not in-progress edits
4. **Consistency**: Two separate viewer implementations (`SpeFileViewer`, `SourceDocumentViewer`) violate DRY and ADR-012
5. **File Operations**: Delete functionality not available in current viewers

## Solution Summary

Implement a **Check-Out/Check-In model** with two viewing modes:
- **Preview Mode**: Uses `embed.aspx` (read-only, no Share button) - secure by default
- **Edit Mode**: Uses `embedview` (full Office Online with Share visible) - acceptable during active editing

Create a new `sprk_fileversion` entity for version tracking with full audit trail of check-out/check-in operations.

## Graduation Criteria

The project is considered **complete** when:

- [ ] `SpeDocumentViewer` PCF control deployed and functional
- [ ] Check-out/check-in workflow working with version history
- [ ] `sprk_fileversion` entity created with proper relationships
- [ ] BFF API endpoints (checkout, checkin, discard, delete) operational
- [ ] Delete functionality via Document ribbon working
- [ ] `SpeFileViewer` and `SourceDocumentViewer` migrated to new component
- [ ] AI analysis triggered on check-in
- [ ] All unit and integration tests passing

## Scope

### In Scope

- Unified `SpeDocumentViewer` PCF control with Fluent v9 toolbar
- New `sprk_fileversion` Dataverse entity
- BFF API endpoints: checkout, checkin, discard, delete
- Document delete via ribbon JavaScript webresource
- Migration of existing viewer components
- Version history subgrid on Document form
- AI analysis trigger on check-in

### Out of Scope

- File upload/replace functionality (future phase)
- Offline editing support
- Mobile-specific optimizations
- Version comparison/diff view
- Force unlock admin UI (future phase)

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Use embed.aspx for preview | Hides Share button for security | — |
| Separate sprk_fileversion entity | Full audit trail, supports different check-out/check-in users | — |
| Delete via ribbon JavaScript | Works without PCF loaded, native UX pattern | ADR-006 exception |
| PCF for main viewer | Type-safe, testable, consistent with platform | [ADR-006](../../docs/reference/adr/ADR-006-pcf-over-webresources.md) |
| Unified component | Reduce duplication, shared maintenance | [ADR-012](../../docs/reference/adr/ADR-012-shared-component-library.md) |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Office Online embed parameters change | Medium | Low | Abstract URL building, monitor MS docs |
| SharePoint preview cache delays | Low | Medium | Acceptable - shows committed version |
| Concurrent checkout conflicts | Medium | Low | Proper locking with clear error messages |
| User forgets to check in | Medium | Medium | Auto-reminder after 24 hours (future) |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| BFF API (Sprk.Bff.Api) | Internal | Production | Add new endpoints |
| SharePoint Embedded | External | Production | Embed URL APIs |
| Dataverse | External | Production | New entity + fields |
| MSAL.js | External | Production | Auth for PCF |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Development Team | Overall delivery |
| Developer | Claude Code | Implementation |

---

*Template version: 1.0 | Spaarke Development Lifecycle*
