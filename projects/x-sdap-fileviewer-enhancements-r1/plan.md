# Project Plan: SDAP FileViewer Enhancements 1

> **Last Updated**: December 4, 2025
>
> **Status**: Complete
>
> **Related**: [Project README](./README.md) | [Design Spec](./spec.md)

---

## 1. Executive Summary

### 1.1 Purpose

Replace embedded Office Web App editing in FileViewer PCF with "Open in Desktop" functionality and implement improved preview loading UX to eliminate black-screen flash and reduce perceived latency.

### 1.2 Business Value

- **Better User Experience**: Full desktop Office editing vs cramped iframe
- **Reduced Support Tickets**: No more "why can I delete/share from Word?" confusion
- **Faster Perceived Load**: Branded loading state instead of black screen
- **Security Alignment**: Desktop mode prevents unwanted file operations

### 1.3 Success Criteria

| Metric | Target | Measurement Method |
|--------|--------|-------------------|
| Preview load UX | No black screen | Visual testing |
| Edit mode | Opens in desktop app | Functional testing |
| Cold start mitigation | <3s warm-up | Performance testing |

---

## 2. Background & Context

### 2.1 Current State

- FileViewer uses embedded Office Web App iframe for both preview and edit
- Edit mode exposes Delete/Share commands users shouldn't have
- First preview load shows black screen during Office initialization
- Cold starts cause noticeable delays

### 2.2 Desired State

- Preview-only iframe with polished loading overlay
- Single edit action: "Open in Desktop" (Word/Excel/PowerPoint)
- White background with loading skeleton during initialization
- Warm-up patterns minimize cold start impact

### 2.3 Gap Analysis

| Area | Current State | Desired State | Gap |
|------|--------------|---------------|-----|
| Edit Mode | Embedded iframe | Desktop app | New BFF endpoint + PCF button |
| Preview UX | Black screen on load | White + loading overlay | PCF rendering logic |
| Performance | Cold start delays | Pre-warmed | App Service config + warm-up |

---

## 3. Solution Overview

### 3.1 Approach

1. **BFF Endpoint**: Add `/files/{driveId}/{itemId}/open-links` returning desktop protocol URLs
2. **PCF Updates**: Implement loading state machine, remove embedded edit, add desktop edit button
3. **Performance**: Enable Always On, add warm-up endpoint, optimize Graph client usage

### 3.2 Architecture Impact

```
FileViewer PCF → SDAP BFF → SpeFileStore → Microsoft Graph → SPE
                             ↑
                       PCF Pre-load (warm-up)
```

No new compute hosts. Minimal API pattern (ADR-001). All Graph calls through SpeFileStore (ADR-007).

### 3.3 Key Technical Decisions

| Decision | Options Considered | Selected | Rationale |
|----------|-------------------|----------|-----------|
| Edit mode | Embedded web, Desktop app | Desktop app | Better UX, no unwanted commands |
| Loading UX | Spinner only, Skeleton, Both | Overlay + text | Simple, branded, professional |
| Warm-up | Always On only, External ping, Both | Both | Maximum cold start reduction |

---

## 4. Scope Definition

### 4.1 In Scope

| Item | Description | Priority |
|------|-------------|----------|
| BFF open-links endpoint | Return desktop/web URLs for file | Must Have |
| PCF loading overlay | White background + loading message | Must Have |
| PCF desktop edit button | Open in Desktop Word/Excel/PPT | Must Have |
| App Service Always On | Prevent cold starts | Must Have |
| Warm-up endpoint | /ping for external warm-up | Should Have |
| Pre-fetch preview URL | Early fetch in PCF lifecycle | Should Have |

### 4.2 Out of Scope

| Item | Reason | Future Consideration |
|------|--------|---------------------|
| Office.js Share Add-in | Separate feature | Yes - Phase 2 |
| Spaarke AI pane | Separate feature | Yes - Phase 2 |
| Dataverse model changes | Not required | No |
| SPE permission changes | Not required | No |

### 4.3 Assumptions

- SpeFileStore.GetFileMetadataAsync() returns webUrl and mimeType
- Desktop Office apps are installed on user machines
- App Service supports Always On configuration

### 4.4 Constraints

- Must use endpoint filters for authorization (ADR-008)
- Must route Graph calls through SpeFileStore (ADR-007)
- Must use PCF pattern, not webresources (ADR-006)
- GraphServiceClient must be singleton (ADR-010)

---

## 5. Work Breakdown Structure

### Phase 1: BFF Endpoint Development

| ID | Task | Estimate | Dependencies |
|----|------|----------|--------------|
| 001 | Create DesktopUrlBuilder utility | 2h | None |
| 002 | Implement /open-links endpoint | 3h | 001 |
| 003 | Add endpoint authorization filter | 2h | 002 |
| 004 | Write unit tests for endpoint | 2h | 002 |

### Phase 2: FileViewer PCF Updates

| ID | Task | Estimate | Dependencies |
|----|------|----------|--------------|
| 010 | Implement loading state machine | 2h | None |
| 011 | Create loading overlay component | 2h | 010 |
| 012 | Update render logic (show iframe after onload) | 3h | 011 |
| 013 | Remove embedded edit mode | 1h | None |
| 014 | Add "Open in Desktop" button | 2h | 002 |
| 015 | Integrate with open-links endpoint | 2h | 014, 002 |
| 016 | Add CSS for loading states | 1h | 011 |

### Phase 3: Performance Enhancements

| ID | Task | Estimate | Dependencies |
|----|------|----------|--------------|
| 020 | Enable App Service Always On | 1h | None |
| 021 | Add /ping warm-up endpoint | 1h | None |
| 022 | Move preview URL fetch to init() | 1h | 012 |
| 023 | Verify Graph client is singleton | 1h | None |

### Phase 4: Testing & Validation

| ID | Task | Estimate | Dependencies |
|----|------|----------|--------------|
| 030 | Integration testing - BFF | 2h | 004 |
| 031 | E2E testing - FileViewer | 3h | 015, 016 |
| 032 | Cross-browser testing | 2h | 031 |
| 033 | Performance validation | 2h | 020, 021, 022 |

### Phase 5: Deployment

| ID | Task | Estimate | Dependencies |
|----|------|----------|--------------|
| 040 | Deploy to test environment | 1h | 030, 031 |
| 041 | Pilot validation | 2h | 040 |
| 042 | Production deployment | 1h | 041 |

---

## 6. Timeline & Milestones

### 6.1 Estimated Timeline

| Phase | Estimate |
|-------|----------|
| Phase 1: BFF Endpoint | 9 hours |
| Phase 2: PCF Updates | 13 hours |
| Phase 3: Performance | 4 hours |
| Phase 4: Testing | 9 hours |
| Phase 5: Deployment | 4 hours |
| **Total** | **~39 hours** |

### 6.2 Key Milestones

| Milestone | Criteria | Status |
|-----------|----------|--------|
| M1: BFF Complete | /open-links endpoint tested | ✅ Complete |
| M2: PCF Complete | Loading overlay + desktop edit working | ✅ Complete |
| M3: Performance Complete | Always On + warm-up configured | ✅ Complete |
| M4: Testing Complete | All tests passing | ✅ Complete |
| M5: Production Deploy | Live in production | ✅ Complete |

---

## 7. Risk Management

### 7.1 Risk Register

| ID | Risk | Impact | Likelihood | Mitigation |
|----|------|--------|------------|------------|
| R1 | Desktop app not installed | Medium | Low | Fallback to webUrl |
| R2 | iframe onload timing varies | Low | Medium | Test cross-browser |
| R3 | Graph client not singleton | Medium | Low | Verify DI registration |

---

## 8. ADR Alignment

| ADR | Requirement | How Addressed |
|-----|-------------|---------------|
| ADR-001 | Minimal API, no Azure Functions | BFF endpoint only |
| ADR-005 | Flat storage, SPE headless | No changes to storage |
| ADR-006 | PCF over webresources | FileViewer remains PCF |
| ADR-007 | Graph through SpeFileStore | GetFileMetadataAsync used |
| ADR-008 | Endpoint filters for auth | Filter on /open-links |
| ADR-010 | DI minimalism, singleton clients | Verify Graph singleton |

---

## 9. Acceptance Criteria

### 9.1 Functional Requirements

| ID | Requirement | Acceptance Test |
|----|-------------|-----------------|
| FR1 | /open-links returns desktop URL | API returns ms-word:// format |
| FR2 | Preview shows loading overlay | White bg + message visible |
| FR3 | No black screen on load | iframe hidden until onload |
| FR4 | Open in Desktop works | Button opens desktop app |
| FR5 | No embedded edit mode | Edit iframe removed |

### 9.2 Non-Functional Requirements

| ID | Requirement | Target |
|----|-------------|--------|
| NFR1 | Preview warm-up | <3s after form load |
| NFR2 | No flicker on preview | iframe only shown after load |
| NFR3 | Security | No delete/share from desktop |

---

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2025-12-04 | 1.0 | Initial plan from spec | AI Agent |

---

*Generated from spec.md via project-init skill*
