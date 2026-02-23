# Accessibility Audit — Home Corporate Workspace R1

> **Task**: 032 — Accessibility Audit (WCAG 2.1 AA)
> **Phase**: 4 — Integration & Polish
> **Created**: 2026-02-18
> **Status**: Complete

---

## Audit Scope

All components in `src/client/pcf/LegalWorkspace/` were audited for WCAG 2.1 AA
compliance including:

- Keyboard navigation
- ARIA labels on icon-only buttons
- Focus indicators
- Screen reader announcements for dynamic content
- Role attributes for custom widgets

---

## Summary

The LegalWorkspace PCF components were already well-structured for accessibility
with Fluent UI v9's built-in semantic component model providing a strong baseline.
This audit identified 7 targeted improvements, all of which were applied.

---

## Findings and Fixes Applied

### 1. FilterBar: role="toolbar" for filter bar (WCAG 4.1.2 — Name, Role, Value)

**File**: `components/ActivityFeed/FilterBar.tsx`

**Before**:
```tsx
<div role="group" aria-label="Filter updates by category">
```

**After**:
```tsx
<div role="toolbar" aria-label="Filter updates by category">
```

**Rationale**: The filter bar is a collection of controls that manipulate the main
content view. WCAG 2.1 SC 4.1.2 and WAI-ARIA practices classify a set of
toggle buttons that control content as a `toolbar`, not a generic `group`.
Using `role="toolbar"` enables arrow key navigation between pills in AT
that support toolbar interaction patterns.

---

### 2. FeedItemCard: AI Summary button — more descriptive aria-label (WCAG 1.3.1)

**File**: `components/ActivityFeed/FeedItemCard.tsx`

**Before**:
```tsx
aria-label="AI Summary"
```

**After**:
```tsx
aria-label="Generate AI summary"
```

**Rationale**: The label should communicate what action the button performs
(generates a summary) rather than naming the feature. This is consistent with
the task specification requirement: `aria-label="Generate AI summary"`.

---

### 3. SmartToDo count badge: aria-live="polite" (WCAG 4.1.3 — Status Messages)

**File**: `components/SmartToDo/SmartToDo.tsx`

**Change**:
```tsx
<Badge
  appearance="filled"
  color="brand"
  size="small"
  aria-label={`${totalCount} to-do item${totalCount === 1 ? "" : "s"}`}
  aria-live="polite"   {/* Added */}
>
  {totalCount}
</Badge>
```

**Rationale**: The count badge updates dynamically when items are added,
dismissed, or restored. WCAG 4.1.3 requires that status messages be
programmatically determinable. `aria-live="polite"` ensures screen readers
announce the updated count without interrupting the user.

---

### 4. ActivityFeed: Filter result count live region (WCAG 4.1.3 — Status Messages)

**File**: `components/ActivityFeed/ActivityFeed.tsx`

**Change**: Added a visually hidden `role="status"` live region that announces
the number of results whenever the active filter changes:

```tsx
<span
  role="status"
  aria-live="polite"
  aria-atomic="true"
  style={{ /* visually hidden */ }}
>
  {filterResultAnnouncement}
</span>
```

**Rationale**: When the user selects a filter pill, the list content changes
silently without any visual focus shift. Screen reader users need an announcement
of how many results are shown for each filter. This is a WCAG 4.1.3 requirement
for status messages.

---

### 5. PageHeader: Notification count badge live region (WCAG 4.1.3 — Status Messages)

**File**: `components/Shell/PageHeader.tsx`

**Change**: Added a visually hidden `role="status"` live region adjacent to the
notification badge:

```tsx
<span
  role="status"
  aria-live="polite"
  aria-atomic="true"
  style={{ /* visually hidden */ }}
>
  {unreadCount > 0 ? `${unreadCount} unread notification${unreadCount === 1 ? "" : "s"}` : ""}
</span>
```

**Rationale**: When new notifications arrive and the badge count changes, screen
reader users should be informed without losing focus. The badge itself is
`aria-hidden="true"` (correct — it's decorative), so the live region provides
the accessible equivalent.

---

### 6. WizardDialog: Form validation error aria-live (WCAG 4.1.3)

**File**: `components/CreateMatter/WizardDialog.tsx`

**Change**:
```tsx
<MessageBar intent="error" role="alert">   {/* role="alert" added */}
```

**Rationale**: When matter creation fails (e.g., Dataverse error), the error
message appears in the content area below the form. Adding `role="alert"`
ensures the message is announced immediately by screen readers as it appears
(equivalent to `aria-live="assertive"`).

---

### 7. BriefingDialog: Add close button in DialogTitle (WCAG 2.1.1 — Keyboard)

**File**: `components/GetStarted/BriefingDialog.tsx`

**Change**: Added a dismiss button via the `action` prop on `DialogTitle`:

```tsx
<DialogTitle
  action={
    <Button
      appearance="subtle"
      aria-label="Close Portfolio Briefing dialog"
      size="small"
      icon={<DismissRegular aria-hidden="true" />}
      onClick={onClose}
    />
  }
>
```

**Rationale**: While Fluent v9 `Dialog` natively supports Escape key dismissal,
having a visible close button in the dialog header is a WCAG 2.1.1 keyboard
accessibility best practice. It also provides a consistent UX pattern matching
the AISummaryDialog and TodoAISummaryDialog which already had this button.
Note: `DismissRegular` was also added to the import statement.

---

## Pre-existing Accessibility Features (Verified Good)

The following features were already implemented correctly and required no changes:

### Icon-Only Buttons — All Have aria-label

| Component | Button | aria-label |
|-----------|--------|------------|
| `FeedItemCard` | Flag toggle | "Flag as to-do" / "Remove flag" / "Flag error: {msg}" |
| `FeedItemCard` | AI Summary | "Generate AI summary" (fixed in this audit) |
| `PageHeader` | Notification bell | "Notifications ({n} unread)" |
| `PageHeader` | Theme toggle | "Current theme: {mode}. Click to switch to {next}." |
| `SmartToDo` | Refresh button | "Refresh to-do list" |
| `TodoItem` | Dismiss button | `Dismiss "${event.sprk_subject}"` |
| `DismissedSection` | Restore button | `Restore "${event.sprk_subject}"` |
| `AISummaryDialog` | Close button | "Close AI Summary dialog" |
| `TodoAISummaryDialog` | Close button | "Close AI Summary dialog" |
| `NotificationPanel` | Close button | "Close notifications panel" |
| `NotificationPanel` | Refresh button | "Refresh notifications" |
| `WizardDialog` | Close button | "Close dialog" |
| `ActivityFeed` | Refresh button | "Refresh updates feed" |

### ARIA Live Regions

| Component | Region | Type |
|-----------|--------|------|
| `SmartToDo.TodoEmptyState` | "All caught up" status | `role="status" aria-live="polite"` |
| `AISummaryDialog` | Loading spinner | `aria-live="polite" aria-busy="true"` |
| `TodoAISummaryDialog` | Loading spinner | `aria-live="polite" aria-busy="true"` |
| `AddTodoBar` | Validation error | `role="alert"` (assertive) |
| `ActivityFeedList` | List | `aria-busy={isLoadingMore}` |

### Keyboard Navigation

| Widget | Keyboard Behavior |
|--------|-------------------|
| Fluent `Dialog` | Escape closes dialog (native Fluent behavior) |
| Fluent `OverlayDrawer` | Escape closes drawer (native Fluent behavior) |
| `FeedItemCard` | Tab-focusable (`tabIndex={0}`), focus ring via `:focus-visible` |
| `TodoItem` | Tab-focusable (`tabIndex={0}`), focus ring via `:focus-visible` |
| `DismissedSection` | `aria-expanded` on toggle button, `aria-controls` on list |
| `FilterBar` | Each pill is a `ToggleButton` with `aria-pressed` |
| `AddTodoBar` | Enter key submits (explicit `onKeyDown` handler) |
| `ThemeToggle` | Fully keyboard accessible via Fluent `Button` |

### Focus Indicators

All interactive cards use `makeStyles` with `:focus-visible` selectors providing
a 2px brand-colored outline:

```ts
":focus-visible": {
  outlineStyle: "solid",
  outlineWidth: "2px",
  outlineColor: tokens.colorBrandStroke1,
  outlineOffset: "-2px",
},
```

This meets WCAG 2.4.11 (Focus Appearance) minimum requirements.

### Role Attributes for Custom Widgets

| Widget | Role | Notes |
|--------|------|-------|
| `FilterBar` | `role="toolbar"` | Fixed in this audit |
| `FilterBar` pills | `aria-pressed` (via ToggleButton) | Active/inactive state |
| `DismissedSection` | `role="region"` | Region with label |
| `DismissedSection` toggle | `aria-expanded` | Expandable section |
| `ActivityFeedList` | `role="list"` | List of feed items |
| `FeedItemCard` | `role="listitem"` | Individual feed items |
| `SmartToDo` | `role="region"` | Region with live count in label |
| `TodoItem` | `role="listitem"` | Individual to-do items |
| `DismissedItem` | `role="listitem"` | Dismissed items in list |
| `NotificationPanel` list | `role="list" aria-live="polite"` | Notification list |
| `PageHeader` | `role="banner"` | Page header landmark |
| `ActivityFeed` | `role="region"` | Activity feed region |

---

## WCAG 2.1 AA Criteria Verification

| Criterion | Description | Status |
|-----------|-------------|--------|
| 1.1.1 | Non-text Content — all icons aria-hidden or have alt text | PASS |
| 1.3.1 | Info and Relationships — semantic HTML + ARIA roles | PASS |
| 1.4.3 | Contrast — all colors use Fluent v9 semantic tokens (auto contrast) | PASS |
| 2.1.1 | Keyboard — all interactive elements reachable via Tab | PASS |
| 2.1.2 | No Keyboard Trap — Escape closes dialogs/drawers | PASS |
| 2.4.3 | Focus Order — visual L→R, T→B matches DOM order | PASS |
| 2.4.7 | Focus Visible — `:focus-visible` outlines on all interactive elements | PASS |
| 2.4.11 | Focus Appearance (AA 2.2) — 2px brand outline on focus | PASS |
| 3.3.1 | Error Identification — role="alert" on validation messages | PASS |
| 4.1.2 | Name, Role, Value — all controls have accessible names + roles | PASS |
| 4.1.3 | Status Messages — aria-live regions for dynamic count updates | PASS |

---

## Files Modified in This Audit

1. `src/client/pcf/LegalWorkspace/components/ActivityFeed/FilterBar.tsx`
   - Changed `role="group"` to `role="toolbar"` on the filter bar container

2. `src/client/pcf/LegalWorkspace/components/ActivityFeed/FeedItemCard.tsx`
   - Updated AI Summary button `aria-label` from "AI Summary" to "Generate AI summary"

3. `src/client/pcf/LegalWorkspace/components/SmartToDo/SmartToDo.tsx`
   - Added `aria-live="polite"` to the item count Badge

4. `src/client/pcf/LegalWorkspace/components/ActivityFeed/ActivityFeed.tsx`
   - Added visually hidden `role="status" aria-live="polite"` region for filter result counts

5. `src/client/pcf/LegalWorkspace/components/Shell/PageHeader.tsx`
   - Added visually hidden `role="status" aria-live="polite"` region for notification count

6. `src/client/pcf/LegalWorkspace/components/CreateMatter/WizardDialog.tsx`
   - Added `role="alert"` to the creation error MessageBar

7. `src/client/pcf/LegalWorkspace/components/GetStarted/BriefingDialog.tsx`
   - Added close button (`DismissRegular`) in `DialogTitle` `action` slot
   - Added `DismissRegular` to imports

---

*Audit completed: 2026-02-18*
