# Cross-Browser Compatibility Test Plan

> **Project**: Events Workspace Apps UX R1
> **Created**: 2026-02-04
> **Status**: Testing Checklist
> **Browsers**: Microsoft Edge, Google Chrome

---

## Executive Summary

This document provides comprehensive cross-browser testing coverage for all PCF controls and Custom Pages in the Events Workspace Apps UX R1 project. Both supported browsers (Microsoft Edge and Google Chrome) are Chromium-based, which minimizes rendering and JavaScript API differences.

### Browser Support Matrix

| Browser | Version | Engine | Status |
|---------|---------|--------|--------|
| Microsoft Edge | 120+ | Chromium/Blink | Primary (Dataverse default) |
| Google Chrome | 120+ | Chromium/Blink | Supported |

### Technology Stack Compatibility

| Technology | Edge Support | Chrome Support | Notes |
|------------|-------------|----------------|-------|
| React 16.14.0 | Full | Full | Platform library |
| Fluent UI v9 (9.46.2) | Full | Full | Primary UI framework |
| TypeScript 5.0 | Full | Full | Transpiled to ES2017 |
| Vite 5.0 | Full | Full | Custom Page bundler |
| Xrm.App.sidePanes API | Full | Full | Dataverse native |
| WebAPI | Full | Full | Dataverse native |

---

## Section 1: EventCalendarFilter PCF Testing

### 1.1 Visual Rendering

| Test ID | Test Case | Edge | Chrome | Notes |
|---------|-----------|------|--------|-------|
| ECF-1.1.1 | Calendar renders with correct month layout | [ ] Pass | [ ] Pass | |
| ECF-1.1.2 | Month headers display correctly | [ ] Pass | [ ] Pass | |
| ECF-1.1.3 | Day names render properly | [ ] Pass | [ ] Pass | |
| ECF-1.1.4 | Date cells are correctly sized | [ ] Pass | [ ] Pass | |
| ECF-1.1.5 | Event indicator dots appear on dates | [ ] Pass | [ ] Pass | |
| ECF-1.1.6 | Today's date is highlighted | [ ] Pass | [ ] Pass | |
| ECF-1.1.7 | Selected date(s) show selection state | [ ] Pass | [ ] Pass | |
| ECF-1.1.8 | Version footer visible | [ ] Pass | [ ] Pass | |

### 1.2 Interaction Testing

| Test ID | Test Case | Edge | Chrome | Notes |
|---------|-----------|------|--------|-------|
| ECF-1.2.1 | Single date click selects date | [ ] Pass | [ ] Pass | |
| ECF-1.2.2 | Shift+click creates date range | [ ] Pass | [ ] Pass | |
| ECF-1.2.3 | Clear selection works | [ ] Pass | [ ] Pass | |
| ECF-1.2.4 | Month navigation arrows work | [ ] Pass | [ ] Pass | |
| ECF-1.2.5 | Keyboard navigation (arrow keys) | [ ] Pass | [ ] Pass | |
| ECF-1.2.6 | Focus indicators visible | [ ] Pass | [ ] Pass | |
| ECF-1.2.7 | Hover states on dates | [ ] Pass | [ ] Pass | |

### 1.3 Integration Testing

| Test ID | Test Case | Edge | Chrome | Notes |
|---------|-----------|------|--------|-------|
| ECF-1.3.1 | Filter JSON output is correct format | [ ] Pass | [ ] Pass | |
| ECF-1.3.2 | Grid receives filter updates | [ ] Pass | [ ] Pass | |
| ECF-1.3.3 | Bi-directional sync from grid works | [ ] Pass | [ ] Pass | |

---

## Section 2: UniversalDatasetGrid Enhancements Testing

### 2.1 Visual Rendering

| Test ID | Test Case | Edge | Chrome | Notes |
|---------|-----------|------|--------|-------|
| UDG-2.1.1 | Grid headers render correctly | [ ] Pass | [ ] Pass | |
| UDG-2.1.2 | Row data displays properly | [ ] Pass | [ ] Pass | |
| UDG-2.1.3 | Checkbox column renders | [ ] Pass | [ ] Pass | |
| UDG-2.1.4 | Hyperlink column styling | [ ] Pass | [ ] Pass | |
| UDG-2.1.5 | Column filter icons visible | [ ] Pass | [ ] Pass | |
| UDG-2.1.6 | Pagination controls render | [ ] Pass | [ ] Pass | |
| UDG-2.1.7 | Loading spinner displays | [ ] Pass | [ ] Pass | |
| UDG-2.1.8 | Empty state message | [ ] Pass | [ ] Pass | |
| UDG-2.1.9 | Version footer visible | [ ] Pass | [ ] Pass | |

### 2.2 Interaction Testing

| Test ID | Test Case | Edge | Chrome | Notes |
|---------|-----------|------|--------|-------|
| UDG-2.2.1 | Row click selects row | [ ] Pass | [ ] Pass | |
| UDG-2.2.2 | Checkbox click toggles selection | [ ] Pass | [ ] Pass | |
| UDG-2.2.3 | Hyperlink click opens side pane | [ ] Pass | [ ] Pass | |
| UDG-2.2.4 | Column sorting works | [ ] Pass | [ ] Pass | |
| UDG-2.2.5 | Column filter popup opens | [ ] Pass | [ ] Pass | |
| UDG-2.2.6 | Filter values apply correctly | [ ] Pass | [ ] Pass | |
| UDG-2.2.7 | Pagination navigation works | [ ] Pass | [ ] Pass | |
| UDG-2.2.8 | Bulk selection via header checkbox | [ ] Pass | [ ] Pass | |

### 2.3 Calendar Integration

| Test ID | Test Case | Edge | Chrome | Notes |
|---------|-----------|------|--------|-------|
| UDG-2.3.1 | Calendar filter applies to grid | [ ] Pass | [ ] Pass | |
| UDG-2.3.2 | Date range filter works | [ ] Pass | [ ] Pass | |
| UDG-2.3.3 | Clear filter resets grid | [ ] Pass | [ ] Pass | |
| UDG-2.3.4 | Row selection syncs to calendar | [ ] Pass | [ ] Pass | |

---

## Section 3: EventDetailSidePane Custom Page Testing

### 3.1 Visual Rendering

| Test ID | Test Case | Edge | Chrome | Notes |
|---------|-----------|------|--------|-------|
| EDS-3.1.1 | Side pane opens at 400px width | [ ] Pass | [ ] Pass | |
| EDS-3.1.2 | Header displays event name/type | [ ] Pass | [ ] Pass | |
| EDS-3.1.3 | Status segmented buttons render | [ ] Pass | [ ] Pass | |
| EDS-3.1.4 | Key fields section displays | [ ] Pass | [ ] Pass | |
| EDS-3.1.5 | Collapsible sections render | [ ] Pass | [ ] Pass | |
| EDS-3.1.6 | Date picker displays correctly | [ ] Pass | [ ] Pass | |
| EDS-3.1.7 | Owner lookup renders | [ ] Pass | [ ] Pass | |
| EDS-3.1.8 | Save/Cancel buttons visible | [ ] Pass | [ ] Pass | |

### 3.2 Interaction Testing

| Test ID | Test Case | Edge | Chrome | Notes |
|---------|-----------|------|--------|-------|
| EDS-3.2.1 | Field editing works | [ ] Pass | [ ] Pass | |
| EDS-3.2.2 | Status change via segmented buttons | [ ] Pass | [ ] Pass | |
| EDS-3.2.3 | Date picker selection | [ ] Pass | [ ] Pass | |
| EDS-3.2.4 | Section expand/collapse | [ ] Pass | [ ] Pass | |
| EDS-3.2.5 | Save button triggers WebAPI | [ ] Pass | [ ] Pass | |
| EDS-3.2.6 | Cancel button discards changes | [ ] Pass | [ ] Pass | |
| EDS-3.2.7 | Unsaved changes prompt | [ ] Pass | [ ] Pass | |
| EDS-3.2.8 | Close pane button works | [ ] Pass | [ ] Pass | |

### 3.3 Side Pane API Testing

| Test ID | Test Case | Edge | Chrome | Notes |
|---------|-----------|------|--------|-------|
| EDS-3.3.1 | Xrm.App.sidePanes.createPane works | [ ] Pass | [ ] Pass | |
| EDS-3.3.2 | Pane reuse on event switch | [ ] Pass | [ ] Pass | |
| EDS-3.3.3 | canClose property respected | [ ] Pass | [ ] Pass | |
| EDS-3.3.4 | webResourceParams passed correctly | [ ] Pass | [ ] Pass | |

---

## Section 4: DueDatesWidget PCF Testing

### 4.1 Visual Rendering

| Test ID | Test Case | Edge | Chrome | Notes |
|---------|-----------|------|--------|-------|
| DDW-4.1.1 | Widget container renders | [ ] Pass | [ ] Pass | |
| DDW-4.1.2 | Event cards display correctly | [ ] Pass | [ ] Pass | |
| DDW-4.1.3 | Overdue items show red styling | [ ] Pass | [ ] Pass | |
| DDW-4.1.4 | Today items show amber styling | [ ] Pass | [ ] Pass | |
| DDW-4.1.5 | Event type badges render | [ ] Pass | [ ] Pass | |
| DDW-4.1.6 | Days-until-due countdown shows | [ ] Pass | [ ] Pass | |
| DDW-4.1.7 | "All Events" link visible | [ ] Pass | [ ] Pass | |
| DDW-4.1.8 | Empty state when no events | [ ] Pass | [ ] Pass | |
| DDW-4.1.9 | Version footer visible | [ ] Pass | [ ] Pass | |

### 4.2 Interaction Testing

| Test ID | Test Case | Edge | Chrome | Notes |
|---------|-----------|------|--------|-------|
| DDW-4.2.1 | Card click navigates to Events tab | [ ] Pass | [ ] Pass | |
| DDW-4.2.2 | Card click opens side pane | [ ] Pass | [ ] Pass | |
| DDW-4.2.3 | "All Events" link navigation | [ ] Pass | [ ] Pass | |
| DDW-4.2.4 | Hover state on cards | [ ] Pass | [ ] Pass | |
| DDW-4.2.5 | Focus indicators for accessibility | [ ] Pass | [ ] Pass | |

---

## Section 5: Events Custom Page Testing

### 5.1 Visual Rendering

| Test ID | Test Case | Edge | Chrome | Notes |
|---------|-----------|------|--------|-------|
| ECP-5.1.1 | Page layout renders (Calendar + Grid) | [ ] Pass | [ ] Pass | |
| ECP-5.1.2 | Filter controls display | [ ] Pass | [ ] Pass | |
| ECP-5.1.3 | "Regarding" column visible | [ ] Pass | [ ] Pass | |
| ECP-5.1.4 | Assigned To filter dropdown | [ ] Pass | [ ] Pass | |
| ECP-5.1.5 | Record Type filter dropdown | [ ] Pass | [ ] Pass | |
| ECP-5.1.6 | Status filter dropdown | [ ] Pass | [ ] Pass | |
| ECP-5.1.7 | Page title shows "Events" | [ ] Pass | [ ] Pass | |

### 5.2 Interaction Testing

| Test ID | Test Case | Edge | Chrome | Notes |
|---------|-----------|------|--------|-------|
| ECP-5.2.1 | Calendar selection filters grid | [ ] Pass | [ ] Pass | |
| ECP-5.2.2 | Assigned To filter works | [ ] Pass | [ ] Pass | |
| ECP-5.2.3 | Record Type filter works | [ ] Pass | [ ] Pass | |
| ECP-5.2.4 | Status filter works | [ ] Pass | [ ] Pass | |
| ECP-5.2.5 | Filter combination works | [ ] Pass | [ ] Pass | |
| ECP-5.2.6 | Clear all filters | [ ] Pass | [ ] Pass | |
| ECP-5.2.7 | Row click opens side pane | [ ] Pass | [ ] Pass | |
| ECP-5.2.8 | "Regarding" link navigation | [ ] Pass | [ ] Pass | |

### 5.3 Cross-Component Communication

| Test ID | Test Case | Edge | Chrome | Notes |
|---------|-----------|------|--------|-------|
| ECP-5.3.1 | Calendar -> Grid communication | [ ] Pass | [ ] Pass | |
| ECP-5.3.2 | Grid -> Side Pane communication | [ ] Pass | [ ] Pass | |
| ECP-5.3.3 | Side Pane save -> Grid refresh | [ ] Pass | [ ] Pass | |
| ECP-5.3.4 | Filter state persistence | [ ] Pass | [ ] Pass | |

---

## Section 6: Dark Mode Compatibility

### 6.1 Theme Detection

| Test ID | Test Case | Edge | Chrome | Notes |
|---------|-----------|------|--------|-------|
| DM-6.1.1 | Detects Dataverse dark mode | [ ] Pass | [ ] Pass | |
| DM-6.1.2 | Detects system preference | [ ] Pass | [ ] Pass | |
| DM-6.1.3 | Respects user localStorage | [ ] Pass | [ ] Pass | |
| DM-6.1.4 | FluentProvider theme applied | [ ] Pass | [ ] Pass | |

### 6.2 Component Appearance

| Test ID | Test Case | Edge | Chrome | Notes |
|---------|-----------|------|--------|-------|
| DM-6.2.1 | EventCalendarFilter dark mode | [ ] Pass | [ ] Pass | |
| DM-6.2.2 | UniversalDatasetGrid dark mode | [ ] Pass | [ ] Pass | |
| DM-6.2.3 | EventDetailSidePane dark mode | [ ] Pass | [ ] Pass | |
| DM-6.2.4 | DueDatesWidget dark mode | [ ] Pass | [ ] Pass | |
| DM-6.2.5 | Events Custom Page dark mode | [ ] Pass | [ ] Pass | |
| DM-6.2.6 | No hard-coded colors visible | [ ] Pass | [ ] Pass | ADR-021 compliance |

---

## Section 7: Browser-Specific Testing Areas

### 7.1 Known Potential Differences

| Area | Edge Behavior | Chrome Behavior | Resolution |
|------|--------------|-----------------|------------|
| Default font rendering | ClearType optimized | SubPixel antialiasing | Use system font stack |
| Date input format | Regional settings | Regional settings | Use Fluent DatePicker |
| Smooth scrolling | Native | Native | No polyfill needed |
| CSS Grid/Flexbox | Full support | Full support | No polyfill needed |
| Web Animations API | Full support | Full support | No polyfill needed |
| ResizeObserver | Full support | Full support | No polyfill needed |
| IntersectionObserver | Full support | Full support | No polyfill needed |

### 7.2 JavaScript API Compatibility

| API | Edge | Chrome | Usage |
|-----|------|--------|-------|
| fetch() | Native | Native | API calls |
| Promise | Native | Native | Async operations |
| async/await | Native | Native | Code patterns |
| Array methods | Native | Native | Data manipulation |
| Map/Set | Native | Native | Data structures |
| localStorage | Native | Native | Theme preference |
| sessionStorage | Native | Native | Temp state |

### 7.3 Polyfill Assessment

**No polyfills required for:**
- ES6+ features (transpiled by TypeScript)
- CSS Grid/Flexbox
- Modern DOM APIs
- Fetch API

**Reason:** Both browsers are modern Chromium-based browsers (version 120+) with equivalent JavaScript/CSS engine support.

---

## Section 8: Performance Comparison

### 8.1 Initial Load Time

| Component | Edge | Chrome | Target |
|-----------|------|--------|--------|
| EventCalendarFilter | [ ] ms | [ ] ms | < 500ms |
| UniversalDatasetGrid | [ ] ms | [ ] ms | < 500ms |
| EventDetailSidePane | [ ] ms | [ ] ms | < 500ms |
| DueDatesWidget | [ ] ms | [ ] ms | < 500ms |
| Events Custom Page | [ ] ms | [ ] ms | < 1000ms |

### 8.2 Interaction Response Time

| Action | Edge | Chrome | Target |
|--------|------|--------|--------|
| Calendar date selection | [ ] ms | [ ] ms | < 100ms |
| Grid row click | [ ] ms | [ ] ms | < 100ms |
| Filter application | [ ] ms | [ ] ms | < 200ms |
| Side pane open | [ ] ms | [ ] ms | < 300ms |
| Side pane save | [ ] ms | [ ] ms | < 500ms |

---

## Section 9: Console Error Checking

### 9.1 Error Categories to Check

| Category | Edge | Chrome | Resolution |
|----------|------|--------|------------|
| JavaScript runtime errors | [ ] None | [ ] None | |
| React warnings | [ ] None | [ ] None | |
| Network failures (4xx/5xx) | [ ] None | [ ] None | |
| CORS errors | [ ] None | [ ] None | |
| Content Security Policy | [ ] None | [ ] None | |
| Deprecated API warnings | [ ] None | [ ] None | |

### 9.2 Expected Console Messages

| Message | Severity | Status |
|---------|----------|--------|
| PCF control initialized | Info | Expected |
| FluentProvider mounted | Info | Expected |
| WebAPI request completed | Info | Expected |

---

## Section 10: Accessibility Cross-Browser

### 10.1 Screen Reader Compatibility

| Feature | Edge (Narrator) | Chrome (VoiceOver/NVDA) | Notes |
|---------|-----------------|-------------------------|-------|
| Calendar navigation | [ ] Works | [ ] Works | |
| Grid row announcement | [ ] Works | [ ] Works | |
| Filter dropdown | [ ] Works | [ ] Works | |
| Side pane focus | [ ] Works | [ ] Works | |
| Button labels | [ ] Works | [ ] Works | |

### 10.2 Keyboard Navigation

| Feature | Edge | Chrome | Notes |
|---------|------|--------|-------|
| Tab order correct | [ ] Pass | [ ] Pass | |
| Focus visible | [ ] Pass | [ ] Pass | |
| Enter/Space activation | [ ] Pass | [ ] Pass | |
| Escape to close | [ ] Pass | [ ] Pass | |
| Arrow key navigation | [ ] Pass | [ ] Pass | |

---

## Issues Log

### Identified Issues

| Issue ID | Component | Browser | Description | Severity | Resolution |
|----------|-----------|---------|-------------|----------|------------|
| | | | | | |

### Issue Severity Levels

- **Critical**: Functionality broken, no workaround
- **High**: Major feature impacted, workaround exists
- **Medium**: Minor visual/UX difference
- **Low**: Cosmetic difference only

---

## Test Execution Summary

### Sign-Off

| Browser | Tester | Date | Overall Status |
|---------|--------|------|----------------|
| Microsoft Edge | | | [ ] Pass / [ ] Fail |
| Google Chrome | | | [ ] Pass / [ ] Fail |

### Test Coverage

| Section | Total Tests | Edge Pass | Chrome Pass |
|---------|-------------|-----------|-------------|
| 1. EventCalendarFilter | 18 | | |
| 2. UniversalDatasetGrid | 21 | | |
| 3. EventDetailSidePane | 16 | | |
| 4. DueDatesWidget | 14 | | |
| 5. Events Custom Page | 18 | | |
| 6. Dark Mode | 10 | | |
| 7. Browser-Specific | N/A | | |
| 8. Performance | 10 | | |
| 9. Console Errors | 6 | | |
| 10. Accessibility | 10 | | |
| **Total** | **123** | | |

---

## Recommendations

### Pre-Testing Setup

1. **Browser Versions**: Ensure both Edge and Chrome are updated to latest stable versions
2. **Dataverse Environment**: Use development environment (spaarkedev1.crm.dynamics.com)
3. **User Account**: Test with standard user permissions
4. **Cache**: Clear browser cache before testing
5. **Extensions**: Disable browser extensions that may interfere

### Test Execution Order

1. Start with EventCalendarFilter (foundation component)
2. Test UniversalDatasetGrid integration
3. Test EventDetailSidePane in isolation
4. Test DueDatesWidget on Matter/Project forms
5. Test Events Custom Page (full integration)
6. Run dark mode tests across all components
7. Check console for errors
8. Measure performance metrics

### Known Considerations

1. **Chromium Base**: Both browsers share the same rendering engine, so major incompatibilities are unlikely
2. **Fluent UI v9**: Officially supports both Edge and Chrome
3. **React 16**: Stable support in both browsers
4. **Dataverse APIs**: Platform-provided, browser-agnostic

---

*This test plan follows the testing standards defined in `.claude/constraints/testing.md`.*
*Related NFRs from spec.md: NFR-05 (Dark mode), NFR-06 (Accessibility).*
