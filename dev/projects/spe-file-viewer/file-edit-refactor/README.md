# SpeFileViewer: Office Editor Mode Enhancement

**Project**: SPE File Viewer v1.0.4 - Office Online Editor Integration
**Status**: Planning Phase
**Created**: 2025-11-26
**Owner**: Development Team

## Project Overview

This enhancement adds "Open in Editor" functionality to the SpeFileViewer PCF control, enabling users to edit Office documents (Word, Excel, PowerPoint) directly within Dataverse using Office Online.

### Key Features
- Toggle between preview mode and editor mode
- Automatic Office file type detection
- Read-only permission awareness (user-friendly dialog)
- Security enforcement via existing OBO (On-Behalf-Of) flow
- Minimal architectural changes

### Target Version
**1.0.4** (increment from current 1.0.3)

---

## Documentation Index

### 1. [Technical Overview](./TECHNICAL-OVERVIEW.md)
**Purpose**: Comprehensive technical specification including architecture diagrams, security model, and implementation details.

**Sections**:
- Executive Summary
- Architecture Overview (current vs proposed)
- Security Model (multi-layer enforcement)
- Files to Modify (detailed code changes)
- Implementation Plan (3 phases)
- Testing Strategy
- Risks & Mitigations
- Dependencies
- Success Criteria

**Read this first** to understand the overall approach and technical architecture.

---

### 2. [Task Breakdown](./TASKS.md)
**Purpose**: Detailed task-by-task implementation guide with acceptance criteria.

**Sections**:
- **Phase 1: Core Functionality** (6 tasks)
  - Task 1.1: Add TypeScript Interfaces
  - Task 1.2: Add BffClient.getOfficeUrl() Method
  - Task 1.3: Add FilePreview State & Methods
  - Task 1.4: Update FilePreview Render Method
  - Task 1.5: Add Button Styles
  - Task 1.6: Update Backend API Response

- **Phase 2: Testing & Deployment** (3 tasks)
  - Task 2.1: Build and Package PCF
  - Task 2.2: Deploy to Dataverse
  - Task 2.3: User Acceptance Testing

- **Phase 3: Documentation** (2 tasks)
  - Task 3.1: Update User Documentation
  - Task 3.2: Create ADR

**Use this** for step-by-step implementation with code snippets and acceptance criteria.

---

### 3. [Architecture Decision Record](./ADR-EDITOR-MODE.md)
**Purpose**: Documents the architectural decision-making process.

**Sections**:
- Context and Problem Statement
- Decision Drivers
- Considered Options (4 alternatives analyzed)
- Decision Outcome (Iframe Toggle - Option 1)
- Implementation Details
- Consequences (Positive, Negative, Neutral)
- Alternatives Rejected (with rationale)
- Follow-Up Decisions
- References

**Use this** to understand *why* this approach was chosen over alternatives.

---

## Quick Start

### For Developers

1. **Read the Technical Overview** to understand the architecture
2. **Follow the Task Breakdown** for step-by-step implementation
3. **Reference the ADR** if you need to understand design decisions

### For Project Managers

1. **Read the Executive Summary** in [TECHNICAL-OVERVIEW.md](./TECHNICAL-OVERVIEW.md#executive-summary)
2. **Review the Implementation Plan** in [TECHNICAL-OVERVIEW.md](./TECHNICAL-OVERVIEW.md#implementation-plan)
3. **Check Success Criteria** in [TECHNICAL-OVERVIEW.md](./TECHNICAL-OVERVIEW.md#success-criteria)
4. **Review Risks** in [TECHNICAL-OVERVIEW.md](./TECHNICAL-OVERVIEW.md#risks--mitigations)

### For QA/Testers

1. **Read the Testing Strategy** in [TECHNICAL-OVERVIEW.md](./TECHNICAL-OVERVIEW.md#testing-strategy)
2. **Follow UAT Test Cases** in [TASKS.md](./TASKS.md#task-23-user-acceptance-testing)

---

## File Locations

### Frontend (PCF Control)
```
C:\code_files\spaarke\src\controls\SpeFileViewer\
├── SpeFileViewer\
│   ├── types.ts                 ← Add OfficeUrlResponse interface
│   ├── BffClient.ts             ← Add getOfficeUrl() method
│   ├── FilePreview.tsx          ← Add editor mode UI & state
│   └── css\
│       └── SpeFileViewer.css    ← Add button styles
└── SpeFileViewerSolution\
    └── src\Other\
        └── Solution.xml         ← Bump version to 1.0.4
```

### Backend (BFF API)
```
c:\code_files\spaarke\src\api\Spe.Bff.Api\
└── Api\
    └── FileAccessEndpoints.cs   ← Modify GetOffice endpoint (optional)
```

---

## Dependencies

### Existing Infrastructure (No Changes)
- ✅ BFF API `/api/documents/{id}/office` endpoint (already exists)
- ✅ OBO authentication flow (ForUserAsync)
- ✅ Graph API permissions (Sites.Read.All, Sites.ReadWrite.All)
- ✅ MSAL token acquisition in PCF

### New Dependencies (May Require)
- ⚠️ **Fluent UI Dialog Component**
  - Check if `@fluentui/react` includes Dialog
  - If missing: `npm install @fluentui/react --save`

---

## Timeline Estimate

| Phase | Duration | Notes |
|-------|----------|-------|
| **Phase 1: Core Functionality** | 3-4 hours | Frontend + backend changes |
| **Phase 2: Testing & Deployment** | 2-3 hours | Build, deploy, UAT |
| **Phase 3: Documentation** | 1 hour | User docs + ADR finalization |
| **Total** | **6-8 hours** | Single developer, uninterrupted |

---

## Risk Summary

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Office Online iframe doesn't load | High | Low | Test with all Office file types; add error boundary |
| Read-only users confused | Medium | Medium | Clear dialog with actionable message |
| Performance degradation | Low | Low | Reuse existing iframe; minimal overhead |
| Security bypass | Critical | Very Low | Already mitigated by OBO + SPE permissions |

---

## Success Metrics

- ✅ Office files show "Open in Editor" button
- ✅ Editor mode loads within 3 seconds (P95)
- ✅ Read-only dialog shown when appropriate
- ✅ < 1% error rate on `/office` endpoint
- ✅ No security vulnerabilities introduced
- ✅ 30% adoption rate (users clicking "Open in Editor")

---

## Security Assurance

This enhancement **does not introduce new security vulnerabilities** because:

1. **MSAL Authentication**: User must have valid access token (unchanged)
2. **BFF Authorization**: All endpoints require `.RequireAuthorization()` (unchanged)
3. **OBO Flow**: Graph API calls use user's identity via `ForUserAsync()` (unchanged)
4. **SharePoint Embedded**: Final permission enforcement (unchanged)
5. **Office Online**: Re-validates permissions on load (Microsoft-managed)

**Security Model**: Zero-trust, multi-layer enforcement (see [TECHNICAL-OVERVIEW.md](./TECHNICAL-OVERVIEW.md#security-model))

---

## Rollback Plan

If issues occur after deployment:

1. **Immediate** (< 5 minutes): Hide button via CSS hotfix
   ```css
   .spe-file-viewer__open-editor-button { display: none !important; }
   ```

2. **Short-term** (< 30 minutes): Revert to solution v1.0.3
   - Import previous solution package
   - Publish customizations

3. **Long-term**: Fix issues in dev environment and redeploy v1.0.4

---

## Related Documentation

- **Current Implementation**: [SpeFileViewer v1.0.3](C:\code_files\spaarke\src\controls\SpeFileViewer)
- **BFF API**: [FileAccessEndpoints.cs](c:\code_files\spaarke\src\api\Spe.Bff.Api\Api\FileAccessEndpoints.cs)
- **OBO Flow**: [GraphClientFactory.cs](c:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\GraphClientFactory.cs)
- **Microsoft Docs**: [Office Web Apps with SharePoint Embedded](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/concepts/app-concepts/office-experiences)

---

## Contact

**Questions or Issues?**
- Technical Questions: Review [TECHNICAL-OVERVIEW.md](./TECHNICAL-OVERVIEW.md)
- Implementation Help: See [TASKS.md](./TASKS.md)
- Design Decisions: Read [ADR-EDITOR-MODE.md](./ADR-EDITOR-MODE.md)

---

## Changelog

| Date | Version | Author | Changes |
|------|---------|--------|---------|
| 2025-11-26 | 1.0 | Claude Code | Initial project documentation created |

---

## Next Steps

1. ✅ Review all documentation with team
2. ⬜ Approve ADR and technical approach
3. ⬜ Begin implementation (Phase 1)
4. ⬜ Schedule UAT testing session
5. ⬜ Plan production deployment
