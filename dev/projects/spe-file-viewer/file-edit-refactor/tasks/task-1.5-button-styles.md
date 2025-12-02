# Task 1.5: Add Button Styles (CSS)

**Phase**: 1 - Core Functionality
**Priority**: High
**Estimated Time**: 20 minutes
**Depends On**: Task 1.4 (FilePreview Render)
**Blocks**: None (can be done in parallel with 1.6)

---

## Objective

Add CSS styles for "Open in Editor" and "Back to Preview" buttons with Fluent UI design patterns, floating positioning, and accessibility features.

## Context & Knowledge Required

### What You Need to Know
1. **CSS Positioning**: Absolute positioning for floating buttons
2. **Fluent UI Design**: Microsoft's design system colors and patterns
3. **CSS Transitions**: Smooth hover/focus effects
4. **Accessibility**: Focus outlines, keyboard navigation

### Files to Review Before Starting
- **SpeFileViewer.css (current)**: [C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\css\SpeFileViewer.css](C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\css\SpeFileViewer.css)
- **Existing Retry Button**: See lines 38-62 for similar button style pattern

---

## Implementation Prompt

### Step 1: Update Preview Container for Positioning

**Location**: `C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\css\SpeFileViewer.css`
**Find**: `.spe-file-viewer__preview` (~line 70)

**Current Code**:
```css
.spe-file-viewer__preview {
    display: flex;
    flex-direction: column;
    height: 100%;
    overflow: hidden;
}
```

**Replace With**:
```css
.spe-file-viewer__preview {
    display: flex;
    flex-direction: column;
    height: 100%;
    overflow: hidden;
    position: relative; /* Enable absolute positioning for buttons */
}
```

---

### Step 2: Add Action Button Styles

**Location**: After `.spe-file-viewer__iframe` styles (~line 87)

**Insert**:
```css
/* ========== Action Buttons ========== */

/* Open in Editor button (floating top-right) */
.spe-file-viewer__open-editor-button {
    position: absolute;
    top: 12px;
    right: 12px;
    z-index: 10;
    padding: 8px 16px;
    background-color: #0078d4; /* Fluent UI primary blue */
    color: white;
    border: none;
    border-radius: 2px;
    font-size: 14px;
    font-weight: 600;
    cursor: pointer;
    transition: background-color 0.2s ease;
    box-shadow: 0 2px 4px rgba(0, 0, 0, 0.2);
}

.spe-file-viewer__open-editor-button:hover {
    background-color: #106ebe; /* Darker blue on hover */
}

.spe-file-viewer__open-editor-button:active {
    background-color: #005a9e; /* Even darker on click */
}

.spe-file-viewer__open-editor-button:focus {
    outline: 2px solid #605e5c; /* High contrast focus outline */
    outline-offset: 2px;
}

.spe-file-viewer__open-editor-button:disabled {
    background-color: #c8c6c4; /* Gray when disabled */
    cursor: not-allowed;
    opacity: 0.6;
}

/* Back to Preview button (floating top-left) */
.spe-file-viewer__back-to-preview-button {
    position: absolute;
    top: 12px;
    left: 12px;
    z-index: 10;
    padding: 8px 16px;
    background-color: #f3f2f1; /* Fluent UI neutral gray */
    color: #323130; /* Dark text */
    border: 1px solid #8a8886;
    border-radius: 2px;
    font-size: 14px;
    font-weight: 600;
    cursor: pointer;
    transition: background-color 0.2s ease, border-color 0.2s ease;
    box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
}

.spe-file-viewer__back-to-preview-button:hover {
    background-color: #e1dfdd; /* Slightly darker gray */
    border-color: #605e5c;
}

.spe-file-viewer__back-to-preview-button:active {
    background-color: #d2d0ce; /* Even darker on click */
}

.spe-file-viewer__back-to-preview-button:focus {
    outline: 2px solid #605e5c;
    outline-offset: 2px;
}
```

---

### Step 3: Add Responsive Adjustments (Optional)

**Location**: In existing responsive section (~line 92)

**Add to small screens media query**:
```css
/* Small screens (< 600px) */
@media (max-width: 599px) {
    /* Existing styles... */

    /* Adjust button sizing for mobile */
    .spe-file-viewer__open-editor-button,
    .spe-file-viewer__back-to-preview-button {
        padding: 6px 12px;
        font-size: 13px;
        top: 8px;
    }

    .spe-file-viewer__open-editor-button {
        right: 8px;
    }

    .spe-file-viewer__back-to-preview-button {
        left: 8px;
    }
}
```

---

## Validation & Review

### Pre-Commit Checklist

1. **CSS Validation**:
   ```bash
   # No build needed for CSS, but verify file syntax
   cat C:/code_files/spaarke/src/controls/SpeFileViewer/SpeFileViewer/css/SpeFileViewer.css | grep -A 10 "open-editor-button"
   ```
   - [ ] No syntax errors (missing semicolons, braces)
   - [ ] Proper nesting and indentation

2. **Visual Review** (after deployment):
   - [ ] Buttons float over iframe content (not pushing it down)
   - [ ] "Open in Editor" button in top-right corner
   - [ ] "Back to Preview" button in top-left corner
   - [ ] z-index: 10 ensures buttons appear above iframe

3. **Accessibility**:
   - [ ] Focus outlines are visible (2px solid)
   - [ ] Hover states change background color
   - [ ] Disabled state has reduced opacity
   - [ ] Cursor: pointer on enabled, not-allowed on disabled

4. **Fluent UI Alignment**:
   - [ ] Primary button uses #0078d4 (Microsoft blue)
   - [ ] Secondary button uses #f3f2f1 (neutral gray)
   - [ ] Font size: 14px (Fluent UI standard)
   - [ ] Border radius: 2px (Fluent UI standard)

---

## Acceptance Criteria

- [x] `.spe-file-viewer__preview` has `position: relative`
- [x] "Open in Editor" button styles added (blue primary button)
- [x] "Back to Preview" button styles added (gray secondary button)
- [x] Buttons use absolute positioning (float over content)
- [x] Hover, active, focus, disabled states defined
- [x] Responsive adjustments for small screens
- [x] z-index: 10 ensures buttons visible
- [x] Transition effects for smooth interactions

---

## Common Issues & Solutions

### Issue 1: Buttons push content down instead of floating
**Symptom**: Iframe shrinks when button appears

**Solution**: Verify `.spe-file-viewer__preview` has `position: relative` and buttons use `position: absolute`

### Issue 2: Buttons overlap document content
**Symptom**: Buttons obscure important text

**Solution**: This is by design for floating buttons. If problematic, adjust top/left/right values:
```css
top: 20px; /* Increase spacing from edge */
```

### Issue 3: Focus outline not visible in high contrast mode
**Symptom**: Keyboard users can't see focused button

**Solution**: Already handled by `outline: 2px solid #605e5c; outline-offset: 2px;`

---

## Files Modified

- `C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\css\SpeFileViewer.css`

---

## Fluent UI Color Reference

| Element | Color | Hex Code | Usage |
|---------|-------|----------|-------|
| Primary Button BG | Communication Blue | #0078d4 | "Open in Editor" |
| Primary Hover | Communication Blue (dark) | #106ebe | Hover state |
| Primary Active | Communication Blue (darker) | #005a9e | Click state |
| Secondary Button BG | Neutral Gray Light | #f3f2f1 | "Back to Preview" |
| Secondary Hover | Neutral Gray | #e1dfdd | Hover state |
| Secondary Active | Neutral Gray Dark | #d2d0ce | Click state |
| Border | Neutral Gray | #8a8886 | Secondary button border |
| Focus Outline | Neutral Gray Darker | #605e5c | Keyboard focus |
| Disabled BG | Neutral Gray Light | #c8c6c4 | Disabled state |

---

## Next Task

**Task 1.6**: Update Backend API Response (Optional)
- Modify `/office` endpoint to return permissions
- OR keep simplified response (rely on Office Online enforcement)
