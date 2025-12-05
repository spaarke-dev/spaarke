# SDAP FileViewer Enhancements 1 - AI Context

## Project Status
- **Phase**: Development
- **Last Updated**: December 4, 2025
- **Next Action**: Run `/task-create` to decompose plan into task files

## Key Files
- `spec.md` - Original design specification (permanent reference)
- `README.md` - Project overview and graduation criteria
- `plan.md` - Implementation plan and WBS
- `tasks/` - Individual task files (POML format)

## Context Loading Rules
1. Always load this file first when working on sdap-fileviewer-enhancements-1
2. Reference spec.md for design decisions and requirements
3. Load relevant task file from tasks/ based on current work
4. Check ADR compliance before implementation

## Project Summary

**What**: Replace embedded Office editing with "Open in Desktop" + improve preview loading UX

**Key Deliverables**:
1. BFF endpoint: `/files/{driveId}/{itemId}/open-links`
2. FileViewer PCF: Loading overlay, desktop edit button, no embedded edit
3. Performance: Always On, warm-up endpoint, pre-fetch

## ADR Constraints (Must Follow)

| ADR | Constraint |
|-----|------------|
| ADR-001 | Minimal API only, no Azure Functions |
| ADR-006 | PCF pattern, no webresources |
| ADR-007 | All Graph calls through SpeFileStore |
| ADR-008 | Endpoint filters for auth, not middleware |
| ADR-010 | GraphServiceClient must be singleton |

## Decisions Made
- Desktop edit over embedded web edit (better UX, no unwanted commands)
- White background + loading text for preview loading state
- Pre-fetch preview URL in init() for performance

## Code Locations

| Component | Location |
|-----------|----------|
| BFF API | `src/server/api/Spe.Bff.Api/` |
| SpeFileStore | `src/server/api/Spe.Bff.Api/Services/` |
| FileViewer PCF | `src/client/pcf/FileViewer/` |
| Shared UI | `src/client/shared/` |

## Current Constraints
- Must not break existing FileViewer functionality
- Must maintain backward compatibility
- Desktop protocol requires Office apps installed (fallback to webUrl)
