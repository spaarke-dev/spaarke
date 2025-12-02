# ADR: Office Online Editor Integration

**Status**: Proposed
**Date**: 2025-11-26
**Decision Makers**: Development Team
**Consulted**: Architecture Team

## Context and Problem Statement

Users viewing Office documents (Word, Excel, PowerPoint) in the SpeFileViewer PCF control need the ability to edit those documents without leaving the Dataverse interface. Currently, the control only supports preview mode (read-only SharePoint embed.aspx).

**Key Requirements**:
1. Enable in-place editing of Office documents
2. Maintain security (user permissions must be enforced)
3. Provide clear UX when users have read-only access
4. Minimize architectural changes (reuse existing infrastructure)

## Decision Drivers

- **User Experience**: Seamless transition from preview to editing
- **Security**: Must enforce SharePoint Embedded permissions via OBO flow
- **Maintainability**: Reuse existing BFF endpoints and authentication
- **Performance**: No significant degradation in load times
- **Complexity**: Minimize code changes and deployment risk

## Considered Options

### Option 1: Iframe Toggle (Same Window)
**Description**: Add button to toggle iframe src between preview URL and Office Online editor URL within the same PCF control.

**Pros**:
- ✅ Consistent UX - user stays in same context
- ✅ Reuses existing iframe infrastructure
- ✅ Minimal code changes (state management only)
- ✅ "Back to Preview" button provides easy navigation
- ✅ No popup blockers or new window management

**Cons**:
- ❌ Slightly more complex state management
- ❌ Must handle two iframe sources (preview + editor)

---

### Option 2: New Browser Window/Tab
**Description**: "Open in Editor" button opens Office Online in a new browser tab.

**Pros**:
- ✅ Simpler implementation (just window.open())
- ✅ No iframe complexity
- ✅ User can keep preview and editor open simultaneously

**Cons**:
- ❌ Popup blockers may interfere
- ❌ Breaks user context (loses Dataverse form)
- ❌ Inconsistent with preview UX
- ❌ User must manually navigate back to Dataverse

---

### Option 3: Modal Dialog with Editor
**Description**: Open Office Online editor in a Fluent UI modal dialog overlay.

**Pros**:
- ✅ Keeps user in Dataverse context
- ✅ Visually distinct from preview mode
- ✅ Can add custom header/footer controls

**Cons**:
- ❌ Modal may feel constrained (especially for large documents)
- ❌ Accessibility concerns (nested iframes in dialogs)
- ❌ More complex CSS and layout management
- ❌ May conflict with Dataverse's own modal system

---

### Option 4: Separate PCF Control
**Description**: Create a new "SpeFileEditor" PCF control specifically for editing.

**Pros**:
- ✅ Clean separation of concerns
- ✅ Can be used independently

**Cons**:
- ❌ Code duplication (BffClient, error handling, etc.)
- ❌ More deployment complexity (two solutions to maintain)
- ❌ Users must manually switch controls
- ❌ Inconsistent UX

---

## Decision Outcome

**Chosen Option**: **Option 1 - Iframe Toggle (Same Window)**

**Rationale**:
1. **Best UX**: User stays in the same context, seamless transition
2. **Simplest Architecture**: Reuses existing iframe, BFF endpoint, and OBO flow
3. **Lowest Risk**: Minimal code changes, incremental enhancement
4. **Consistent Design**: Matches preview mode interaction pattern

## Implementation Details

### Frontend Changes (PCF)
- Add state: `mode: 'preview' | 'editor'`
- Add button: "Open in Editor" (preview mode)
- Add button: "Back to Preview" (editor mode)
- Toggle iframe src: `previewUrl` vs `officeUrl`

### Backend Changes (BFF)
- Activate existing `/api/documents/{id}/office` endpoint
- Return structured response: `{ officeUrl, permissions, correlationId }`
- Use OBO flow (no changes to authentication)

### Security Model
- **Authentication**: MSAL bearer token (unchanged)
- **Authorization**: BFF `.RequireAuthorization()` (unchanged)
- **Permission Enforcement**: OBO flow + SharePoint Embedded (unchanged)
- **Read-Only Handling**: Office Online automatically enforces read-only mode; PCF shows informative dialog

### UX Flow
```
User opens document → Preview mode (default)
    ↓
User clicks "Open in Editor" (Office files only)
    ↓
PCF calls BFF /office endpoint
    ↓
BFF returns Office Online webUrl
    ↓
Iframe src switches to editor URL
    ↓
Office Online loads:
    - Edit permissions → Full editor
    - Read-only → Editor in read-only mode + PCF dialog
    ↓
User clicks "Back to Preview" → Returns to preview mode
```

## Consequences

### Positive
- ✅ Enhances user productivity (no context switching)
- ✅ Maintains zero-trust security model
- ✅ Reuses 95% of existing code
- ✅ Easy to rollback (hide button via CSS hotfix)
- ✅ Provides foundation for future enhancements (e.g., co-authoring indicators)

### Negative
- ⚠️ Slightly more complex state management (manageable)
- ⚠️ Depends on Office Online iframe compatibility (Microsoft-supported scenario)
- ⚠️ Read-only users may initially be confused (mitigated by dialog)

### Neutral
- ℹ️ Requires versioning bump (1.0.3 → 1.0.4)
- ℹ️ Needs user acceptance testing before production

## Alternatives Considered and Rejected

### Why Not New Window (Option 2)?
- Breaks user context and workflow
- Inconsistent with preview UX
- Popup blockers cause friction

### Why Not Modal Dialog (Option 3)?
- Office documents need full screen for good UX
- Accessibility concerns with nested iframes
- More complex implementation for marginal benefit

### Why Not Separate PCF (Option 4)?
- Code duplication
- User confusion (which control to use?)
- Deployment complexity

## Follow-Up Decisions

### Phase 2 Enhancements (Future)
If user feedback indicates need:
- Add permission pre-check (call Graph API to detect read-only before loading)
- Add "Don't show again" option for read-only dialog
- Add co-authoring indicators (if multiple users editing)
- Add file lock warnings (if file is checked out)

### Monitoring
- Track usage: % of Office files opened in editor mode
- Track errors: `/office` endpoint failure rate
- Track performance: P95 load time for editor mode
- Track UX: User feedback on read-only dialog clarity

## References

- **Microsoft Graph API**: [DriveItem.webUrl](https://learn.microsoft.com/en-us/graph/api/resources/driveitem#properties)
- **Office Online**: [Office Web Apps Integration](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/concepts/app-concepts/office-experiences)
- **Existing Implementation**: [FileAccessEndpoints.cs - GetOffice](c:\code_files\spaarke\src\api\Spe.Bff.Api\Api\FileAccessEndpoints.cs#L323-L388)
- **OBO Flow**: [GraphClientFactory.ForUserAsync](c:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\GraphClientFactory.cs#L107)

## Approval

- [ ] Product Owner: _________________
- [ ] Tech Lead: _________________
- [ ] Security Review: _________________
- [ ] Date Approved: _________________

---

## Revision History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-11-26 | Claude Code | Initial ADR |
