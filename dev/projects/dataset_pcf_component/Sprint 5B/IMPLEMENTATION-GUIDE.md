# Sprint 5B Implementation Guide

**Sprint:** 5B - Universal Dataset Grid Compliance
**Target:** Complete Fluent UI v9 compliance and PCF best practices
**Duration:** 1 week (5-7 days)
**Created:** 2025-10-05

---

## Quick Start

### Prerequisites
✅ Restore point created: `restore-point-universal-grid-v2.0.2`
✅ Current bundle: 3.71 MB (under 5 MB limit)
✅ React 18 partially migrated
✅ Fluent UI v9 partially compliant

### Execution Order

**Day 1-3: Phase A (CRITICAL)**
1. Task A.1: Single React Root (4-6 hours)
2. Task A.2: Fluent DataGrid (6-8 hours)
3. Task A.3: Fluent Toolbar (2-3 hours)

**Day 4-5: Phase B (IMPORTANT)**
1. Task B.1: Dynamic Theme Resolution (3-4 hours)
2. Task B.2: Replace Inline Styles (3-4 hours)
3. Task B.3: makeStyles (optional, 2 hours)

**Day 5-6: Phase C (OPTIMIZATION)**
1. Task C.1: Dataset Paging (4-5 hours)
2. Task C.2: State Optimization (2-3 hours)
3. Task C.3: Virtualization (optional, 4 hours)

**Day 6-7: Phase D (QUALITY)**
1. Task D.1: Logging & Errors (3-4 hours)
2. Task D.2: ESLint Rules (2 hours)
3. Task D.3: Documentation (2-3 hours)
4. Task D.4: Tests (optional, 4 hours)

---

## Implementation Workflow

### For Each Task:

1. **Read Task Document**
   - Location: `Sprint 5B/TASK-*.md` or `PHASE-*-OVERVIEW.md`
   - Review AI Coding Instructions
   - Check dependencies

2. **Create Files/Make Changes**
   - Follow AI Coding Instructions exactly
   - Use code samples provided
   - Maintain existing functionality

3. **Build & Test**
   ```bash
   cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid
   npm run build
   npm run lint
   ```

4. **Deploy to Dev**
   ```bash
   # Disable CPM
   cd c:\code_files\spaarke
   mv Directory.Packages.props Directory.Packages.props.disabled

   # Deploy
   cd src\controls\UniversalDatasetGrid
   pac pcf push --publisher-prefix sprk

   # Re-enable CPM
   cd c:\code_files\spaarke
   mv Directory.Packages.props.disabled Directory.Packages.props
   ```

5. **Validate**
   - Check all items in task's "Testing Checklist"
   - Verify "Validation Criteria"
   - Test in Power Apps

6. **Commit**
   ```bash
   git add -A
   git commit -m "feat: [Task ID] - [Description]"
   git push origin master
   ```

---

## Phase A: Architecture Refactor (CRITICAL)

### Task A.1: Single React Root

**Files to Create:**
- `components/UniversalDatasetGridRoot.tsx` (NEW)

**Files to Modify:**
- `components/CommandBar.tsx` (remove class wrapper)
- `providers/ThemeProvider.ts` (simplify to utility)
- `index.ts` (refactor to single root)

**Testing:**
```bash
# Build
npm run build

# Deploy
pac pcf push --publisher-prefix sprk

# Validate in Power Apps:
# - Control loads without errors
# - Command bar renders
# - Console shows "Creating single React root"
# - No memory leaks
```

**Success Criteria:**
- ✅ Only ONE createRoot() call
- ✅ No ReactDOM.render() anywhere
- ✅ No manual DOM manipulation in updateView()
- ✅ Control works in Power Apps

---

### Task A.2: Fluent DataGrid

**Files to Create:**
- `components/DatasetGrid.tsx` (NEW)

**Files to Modify:**
- `components/UniversalDatasetGridRoot.tsx` (use DatasetGrid)
- `index.ts` (remove renderMinimalGrid, createToolbar, etc.)

**Important Notes:**
- Check if DataGrid is in @fluentui/react-components
- May need separate @fluentui/react-table package
- Follow Fluent DataGrid documentation for exact API

**Testing:**
```bash
# Build
npm run build

# Deploy
pac pcf push --publisher-prefix sprk

# Validate in Power Apps:
# - Grid displays all columns
# - Grid displays all rows
# - Selection works
# - Keyboard navigation works
# - No <table> elements in DOM
```

**Success Criteria:**
- ✅ No raw <table> elements
- ✅ Uses Fluent UI DataGrid
- ✅ Selection syncs with Power Apps
- ✅ Keyboard navigation works

---

### Task A.3: Fluent Toolbar

**Files to Modify:**
- `components/CommandBar.tsx` (use Toolbar component)
- `components/UniversalDatasetGridRoot.tsx` (add onRefresh callback)

**Testing:**
```bash
# Build & deploy same as above

# Validate in Power Apps:
# - Toolbar styled correctly
# - Buttons work
# - Refresh button refreshes dataset
# - Visual dividers between button groups
```

**Success Criteria:**
- ✅ Uses Fluent UI Toolbar
- ✅ Uses ToolbarButton
- ✅ No inline styles on wrapper
- ✅ All buttons functional

---

## Phase B: Theming & Design Tokens

### Task B.1: Dynamic Theme Resolution

**Files to Modify:**
- `providers/ThemeProvider.ts` (enhance resolveTheme function)

**Testing:**
```bash
# Build & deploy

# Validate in Power Apps:
# - Switch Power Apps to dark mode -> control switches to dark theme
# - Switch to high-contrast mode -> control uses high-contrast theme
# - Check console for theme detection logs
```

**Success Criteria:**
- ✅ Detects light/dark mode from Power Apps
- ✅ Supports high-contrast mode
- ✅ Updates when theme changes
- ✅ Graceful fallback if context unavailable

---

### Task B.2: Replace Inline Styles

**Files to Modify:**
- All `.tsx` files with inline styles

**Find & Replace Guide:**
```typescript
// Colors:
'#ffffff' -> tokens.colorNeutralBackground1
'#f3f2f1' -> tokens.colorNeutralBackground2
'#323130' -> tokens.colorNeutralForeground1

// Spacing:
'8px' -> tokens.spacingVerticalS / tokens.spacingHorizontalS
'12px' -> tokens.spacingVerticalM / tokens.spacingHorizontalM
'16px' -> tokens.spacingVerticalL / tokens.spacingHorizontalL

// Typography:
'Segoe UI' -> tokens.fontFamilyBase
'14px' -> tokens.fontSizeBase300
'12px' -> tokens.fontSizeBase200
```

**Testing:**
```bash
# Build & deploy

# Validate in all themes:
# - Light mode: proper contrast
# - Dark mode: proper contrast
# - High-contrast: readable
# - No hard-coded colors visible
```

**Success Criteria:**
- ✅ No hard-coded hex colors
- ✅ No hard-coded fonts
- ✅ All spacing uses tokens
- ✅ Works in all theme modes

---

## Phase C: Performance & Dataset Optimization

### Task C.1: Dataset Paging

**Files to Modify:**
- `components/DatasetGrid.tsx` (add paging support)

**Testing:**
```bash
# Build & deploy

# Validate with large dataset:
# - Create view with 200+ records
# - First page loads quickly
# - "Load More" button appears
# - Clicking "Load More" loads next page
# - No duplicate records
```

**Success Criteria:**
- ✅ Paging API integrated
- ✅ Page size set to 50
- ✅ Load More button works
- ✅ Performance good with 1000+ records

---

### Task C.2: State Optimization

**Files to Modify:**
- `components/UniversalDatasetGridRoot.tsx` (add debouncing, memoization)

**Testing:**
```bash
# Build & deploy

# Performance validation:
# - Open React DevTools Profiler
# - Select/deselect rows rapidly
# - Check re-render count (should be minimal)
# - Check notifyOutputChanged call frequency
```

**Success Criteria:**
- ✅ notifyOutputChanged debounced
- ✅ Selection changes don't cause full re-render
- ✅ useMemo/useCallback used appropriately
- ✅ No performance regressions

---

## Phase D: Code Quality & Standards

### Task D.1: Logging & Error Handling

**Files to Create:**
- `utils/logger.ts` (NEW)
- `components/ErrorBoundary.tsx` (NEW)

**Files to Modify:**
- All files: replace console.log with logger.debug
- `index.ts`: wrap in ErrorBoundary

**Testing:**
```bash
# Build & deploy

# Error boundary test:
# - Force an error in DatasetGrid
# - Verify error boundary catches it
# - Verify user sees error message
# - Verify "Try Again" button works
```

**Success Criteria:**
- ✅ No console.log in production
- ✅ Error boundary catches errors
- ✅ Graceful error display
- ✅ Errors logged properly

---

### Task D.2: ESLint Rules

**Files to Modify:**
- `eslint.config.mjs` (add new rules)

**Testing:**
```bash
# Test ESLint rules
npm run lint

# Try to add violation:
# - Add <table> element -> should error
# - Add inline style -> should warn
# - Verify hooks rules work
```

**Success Criteria:**
- ✅ ESLint catches raw <table>
- ✅ ESLint warns about inline styles
- ✅ React hooks rules enforced
- ✅ All existing code passes lint

---

### Task D.3: Documentation

**Files to Create:**
- `README.md` (NEW)
- `ARCHITECTURE.md` (optional)
- `CONTRIBUTING.md` (optional)

**Content Requirements:**
- Architecture diagram
- Component structure
- Development instructions
- Standards and conventions
- Testing guidelines

**Success Criteria:**
- ✅ README complete and accurate
- ✅ Architecture documented
- ✅ Clear development instructions
- ✅ Standards documented

---

## Troubleshooting

### Build Errors

**Error:** DataGrid not found
```bash
# Install separate package
npm install @fluentui/react-table --save
```

**Error:** Module not found
```bash
# Clean and rebuild
npm run clean
npm install
npm run build
```

### Deployment Errors

**Error:** Central package management
```bash
# Disable CPM before deploying
mv Directory.Packages.props Directory.Packages.props.disabled
# Deploy
# Re-enable
mv Directory.Packages.props.disabled Directory.Packages.props
```

**Error:** Bundle size exceeds 5 MB
```bash
# Check bundle composition
npm run build -- --stats
# Analyze with webpack-bundle-analyzer if needed
```

### Runtime Errors

**Error:** Control doesn't load
- Check browser console for errors
- Verify bundle.js loaded in Network tab
- Check PCF logs in Power Apps

**Error:** Selection not working
- Verify dataset.setSelectedRecordIds() called
- Verify notifyOutputChanged() called
- Check console for selection logs

---

## Validation Checklist

### After Each Phase:

**Build:**
- [ ] `npm run build` succeeds
- [ ] No TypeScript errors
- [ ] No ESLint errors/warnings
- [ ] Bundle size < 5 MB

**Deploy:**
- [ ] `pac pcf push` succeeds
- [ ] Control published to environment
- [ ] No deployment warnings

**Functional:**
- [ ] Control loads in Power Apps
- [ ] All features work as expected
- [ ] No console errors
- [ ] Selection works
- [ ] Commands work

**Visual:**
- [ ] Matches Fluent UI design
- [ ] Proper spacing and alignment
- [ ] Works in all themes
- [ ] Responsive layout

**Performance:**
- [ ] Loads quickly (< 2 seconds)
- [ ] Smooth scrolling
- [ ] No UI lag
- [ ] Memory usage acceptable

---

## Final Acceptance Criteria

Sprint 5B is complete when ALL of the following are true:

### Technical Compliance:
- ✅ Single React root architecture
- ✅ All UI uses Fluent UI v9 components
- ✅ No raw HTML elements
- ✅ All styles use design tokens
- ✅ Dynamic theme support
- ✅ Dataset paging implemented
- ✅ Error boundaries in place
- ✅ ESLint rules enforced

### Functional Requirements:
- ✅ Control works in Power Apps
- ✅ Dataset displays correctly
- ✅ Selection works
- ✅ Commands work
- ✅ Refresh works
- ✅ Themes switch correctly
- ✅ Performance acceptable

### Code Quality:
- ✅ No console.log statements
- ✅ Proper error handling
- ✅ Documentation complete
- ✅ ESLint passes
- ✅ Build succeeds
- ✅ Bundle < 5 MB

### Ready for SDAP Integration:
- ✅ Clean architecture for adding SDAP features
- ✅ Command handlers ready for implementation
- ✅ Error handling in place
- ✅ Performance optimized

---

## Post-Sprint Tasks

After Sprint 5B completion:

1. **Create Restore Point**
   ```bash
   git tag -a "restore-point-sprint-5b-complete" -m "Sprint 5B Complete"
   git push origin "restore-point-sprint-5b-complete"
   ```

2. **Update Documentation**
   - Update codebase assessment
   - Document lessons learned
   - Update ADR compliance status

3. **Plan Sprint 6 Phase 3**
   - Review SDAP integration tasks
   - Update timeline
   - Schedule kickoff

4. **Celebrate! 🎉**
   - Universal Dataset Grid is now fully compliant
   - Ready for production features
   - Clean foundation for future work

---

_Document Version: 1.0_
_Created: 2025-10-05_
_Last Updated: 2025-10-05_
