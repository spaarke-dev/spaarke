# Test Scenarios - Universal Dataset Grid PCF Component

**Version**: 1.0.0
**Date**: 2025-10-04
**Status**: Ready for Execution

---

## Overview

This document contains comprehensive test scenarios for validating the Universal Dataset Grid PCF component in a live Dataverse environment.

**Test Coverage**:
- Functional testing (core features)
- Configuration testing (JSON configs)
- Performance testing (large datasets)
- Integration testing (Dataverse APIs)
- User acceptance testing (real-world scenarios)

---

## Test Environment Setup

### Prerequisites
- ✅ Dataverse environment (Dev/Test)
- ✅ Solution imported successfully
- ✅ Test data available (multiple entities)
- ✅ Test user accounts with varying permissions

### Test Entities
1. **Account** (Standard entity)
2. **Contact** (Standard entity)
3. **Custom Entity** (sprk_document or similar)

### Test Data Requirements
- Minimum 10 records per entity (basic testing)
- Minimum 100 records on one entity (virtualization testing)
- Mix of record types (active, inactive, various statuses)

---

## TS-01: Installation & Configuration Tests

### TS-01.1: Solution Import Validation

**Objective**: Verify solution imports without errors

**Steps**:
1. Navigate to make.powerapps.com
2. Select target environment
3. Go to Solutions
4. Import UniversalDatasetGridSolution_managed.zip
5. Monitor import progress
6. Verify import completion

**Expected Results**:
- ✅ Import starts successfully
- ✅ No validation errors
- ✅ Import completes within 5 minutes
- ✅ Solution version 1.0.0 visible
- ✅ Control appears in component library

**Priority**: Critical
**Status**: ⏸️ Pending environment access

---

### TS-01.2: Control Registration Verification

**Objective**: Verify control is registered correctly

**Steps**:
1. Open Power Apps maker portal
2. Go to Settings > Customizations
3. Navigate to Custom Controls
4. Search for "Universal Dataset Grid"

**Expected Results**:
- ✅ Control listed in custom controls
- ✅ Namespace: Spaarke.UI.Components
- ✅ Display name: "Universal Dataset Grid"
- ✅ Description present

**Priority**: Critical
**Status**: ⏸️ Pending environment access

---

### TS-01.3: Form Configuration - Add Control

**Objective**: Successfully add control to entity form

**Test Entity**: Account

**Steps**:
1. Open Account entity in solution explorer
2. Edit "Account Main Form"
3. Add new section "Test Grid"
4. Click "Add Component"
5. Select "Universal Dataset Grid"
6. Configure:
   - Dataset: Account dataset
   - Config JSON: Leave empty
7. Save form
8. Publish customizations

**Expected Results**:
- ✅ Control appears in component picker
- ✅ Control adds to form designer
- ✅ Properties panel shows dataset option
- ✅ Save completes without errors
- ✅ Publish succeeds

**Priority**: Critical
**Status**: ⏸️ Pending environment access

---

## TS-02: Basic Functionality Tests

### TS-02.1: Grid View Rendering

**Objective**: Verify grid view displays records correctly

**Prerequisites**: Control added to Account form

**Steps**:
1. Open Account record in model-driven app
2. Navigate to section with control
3. Observe grid rendering

**Expected Results**:
- ✅ Grid loads within 2 seconds
- ✅ Column headers display
- ✅ Records display in rows
- ✅ Data aligns with column headers
- ✅ No JavaScript errors in console
- ✅ Scrollbar appears if needed

**Validation**:
```
Column Headers Expected:
- Account Name
- Primary Contact
- City
- State/Province
- Main Phone
(Based on default Account columns)
```

**Priority**: Critical
**Status**: ⏸️ Pending environment access

---

### TS-02.2: List View Rendering

**Objective**: Verify list view displays correctly

**Prerequisites**: Control configured with view mode switching enabled

**Configuration**:
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "List"
  }
}
```

**Steps**:
1. Add configuration JSON to control
2. Save and publish
3. Open form
4. Observe list view

**Expected Results**:
- ✅ List view renders instead of grid
- ✅ Records display as list items
- ✅ Primary field visible
- ✅ Secondary fields visible
- ✅ Proper spacing between items

**Priority**: High
**Status**: ⏸️ Pending environment access

---

### TS-02.3: Card View Rendering

**Objective**: Verify card view displays correctly

**Configuration**:
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Card"
  }
}
```

**Steps**:
1. Update configuration JSON
2. Save and publish
3. Open form
4. Observe card view

**Expected Results**:
- ✅ Card view renders
- ✅ Records display as cards
- ✅ Cards have visual separation
- ✅ Fields display within cards
- ✅ Responsive layout (cards wrap)

**Priority**: High
**Status**: ⏸️ Pending environment access

---

### TS-02.4: View Mode Switching

**Objective**: Switch between view modes dynamically

**Configuration**:
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "allowViewModeSwitch": true
  }
}
```

**Steps**:
1. Open form with grid view
2. Locate view mode toggle (if visible)
3. Switch to List view
4. Switch to Card view
5. Switch back to Grid view

**Expected Results**:
- ✅ View mode toggle visible (if configured)
- ✅ Each view mode renders correctly
- ✅ Data persists between switches
- ✅ Selection state maintained
- ✅ Smooth transitions (<500ms)

**Priority**: Medium
**Status**: ⏸️ Pending environment access
**Note**: View mode switching may require future enhancement

---

## TS-03: Command & Action Tests

### TS-03.1: Open Command

**Objective**: Open record using built-in command

**Steps**:
1. Display grid with multiple records
2. Click on a record row
3. Click "Open" command (or double-click row)

**Expected Results**:
- ✅ Record form opens
- ✅ Correct record loaded
- ✅ Form opens in same window or modal
- ✅ No errors in console

**Priority**: Critical
**Status**: ⏸️ Pending environment access

---

### TS-03.2: Refresh Command

**Objective**: Refresh data using built-in command

**Steps**:
1. Display grid with records
2. Open another browser tab
3. Create new record in other tab
4. Return to grid
5. Click "Refresh" command

**Expected Results**:
- ✅ Grid reloads data
- ✅ New record appears
- ✅ Selection state cleared or maintained (per config)
- ✅ View state maintained
- ✅ Refresh completes within 2 seconds

**Priority**: High
**Status**: ⏸️ Pending environment access

---

### TS-03.3: Create Command

**Objective**: Create new record using command

**Configuration**:
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "enabledCommands": ["create", "open", "refresh"]
  }
}
```

**Steps**:
1. Configure control with create command
2. Display grid
3. Click "Create" or "New" command

**Expected Results**:
- ✅ New record form opens
- ✅ Form is for correct entity
- ✅ Form opens in create mode
- ✅ After save, grid refreshes (if configured)

**Priority**: High
**Status**: ⏸️ Pending environment access

---

### TS-03.4: Delete Command

**Objective**: Delete record using command

**Configuration**:
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "enabledCommands": ["delete", "open", "refresh"]
  }
}
```

**Steps**:
1. Configure control with delete command
2. Select a test record
3. Click "Delete" command
4. Confirm deletion

**Expected Results**:
- ✅ Confirmation dialog appears
- ✅ After confirm, record deleted
- ✅ Grid refreshes automatically
- ✅ Deleted record no longer visible
- ✅ Success message displayed

**Priority**: High
**Status**: ⏸️ Pending environment access

---

### TS-03.5: Custom Command Execution

**Objective**: Execute custom command via configuration

**Prerequisites**: Custom API created (e.g., sprk_TestCommand)

**Configuration**:
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "customCommands": {
      "test": {
        "label": "Test Command",
        "icon": "TestBeaker",
        "actionType": "customapi",
        "actionName": "sprk_TestCommand",
        "parameters": {
          "RecordId": "{recordId}"
        },
        "refresh": true
      }
    }
  }
}
```

**Steps**:
1. Create Custom API in environment
2. Add configuration
3. Save and publish
4. Select a record
5. Click "Test Command"

**Expected Results**:
- ✅ Command appears in toolbar
- ✅ Command executes API call
- ✅ Parameters passed correctly
- ✅ Grid refreshes after execution
- ✅ Success/error message shown

**Priority**: Medium
**Status**: ⏸️ Pending environment access + Custom API setup

---

## TS-04: Selection & Interaction Tests

### TS-04.1: Single Record Selection

**Objective**: Select single record

**Steps**:
1. Display grid with records
2. Click checkbox on first record
3. Verify selection state
4. Click checkbox again to deselect

**Expected Results**:
- ✅ Checkbox becomes checked
- ✅ Row highlights (visual feedback)
- ✅ Selection count updates
- ✅ Deselect removes highlight
- ✅ Commands enable/disable based on selection

**Priority**: High
**Status**: ⏸️ Pending environment access

---

### TS-04.2: Multiple Record Selection

**Objective**: Select multiple records

**Steps**:
1. Display grid with records
2. Click checkboxes on 3 different records
3. Verify selection state
4. Deselect one record
5. Verify remaining selections

**Expected Results**:
- ✅ All selected records highlighted
- ✅ Selection count shows "3 selected"
- ✅ After deselect, count shows "2 selected"
- ✅ Correct records remain selected

**Priority**: High
**Status**: ⏸️ Pending environment access

---

### TS-04.3: Select All / Deselect All

**Objective**: Use select all functionality

**Steps**:
1. Display grid with 10+ records
2. Click "Select All" (if available)
3. Verify all visible records selected
4. Click "Deselect All"
5. Verify all records deselected

**Expected Results**:
- ✅ Select All selects all visible records
- ✅ Selection count shows total
- ✅ Deselect All clears all selections
- ✅ Selection count shows 0

**Priority**: Medium
**Status**: ⏸️ Pending environment access

---

### TS-04.4: Selection Persistence

**Objective**: Verify selection persists during view operations

**Steps**:
1. Select 3 records
2. Scroll down
3. Scroll back up
4. Verify selection maintained

**Expected Results**:
- ✅ Selected records remain selected after scroll
- ✅ Selection state preserved during refresh (if configured)
- ✅ Selection cleared on data reload (if not configured to persist)

**Priority**: Medium
**Status**: ⏸️ Pending environment access

---

## TS-05: Configuration Tests

### TS-05.1: Default Configuration

**Objective**: Verify default behavior without configuration

**Configuration**: None (empty configJson)

**Steps**:
1. Add control without configuration
2. Open form

**Expected Results**:
- ✅ Grid view displayed (default)
- ✅ Standard commands available (open, refresh)
- ✅ Normal toolbar (not compact)
- ✅ Virtualization enabled (if >50 records)

**Priority**: High
**Status**: ⏸️ Pending environment access

---

### TS-05.2: Entity-Specific Configuration

**Objective**: Apply different configs per entity

**Configuration**:
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enabledCommands": ["open", "refresh"]
  },
  "entityConfigs": {
    "account": {
      "viewMode": "Card",
      "compactToolbar": true
    },
    "contact": {
      "viewMode": "List",
      "enabledCommands": ["open", "create", "refresh"]
    }
  }
}
```

**Steps**:
1. Add control to Account form
2. Add control to Contact form
3. Add configuration
4. Test both forms

**Expected Results**:
- ✅ Account form: Card view, compact toolbar
- ✅ Contact form: List view, 3 commands
- ✅ Other entities: Grid view, 2 commands
- ✅ Entity-specific settings override defaults

**Priority**: High
**Status**: ⏸️ Pending environment access

---

### TS-05.3: Compact Toolbar

**Objective**: Verify compact toolbar configuration

**Configuration**:
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "compactToolbar": true
  }
}
```

**Steps**:
1. Apply configuration
2. Open form

**Expected Results**:
- ✅ Toolbar displays in compact mode
- ✅ Command icons visible
- ✅ Command labels hidden or shortened
- ✅ More horizontal space for grid

**Priority**: Low
**Status**: ⏸️ Pending environment access

---

### TS-05.4: Command Customization

**Objective**: Enable/disable specific commands

**Configuration**:
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "enabledCommands": ["open", "refresh"]
  }
}
```

**Steps**:
1. Apply configuration
2. Open form
3. Observe available commands

**Expected Results**:
- ✅ Only "Open" and "Refresh" commands visible
- ✅ Create, Delete, Export commands hidden
- ✅ Disabled commands not clickable

**Priority**: High
**Status**: ⏸️ Pending environment access

---

### TS-05.5: Virtualization Control

**Objective**: Enable/disable virtualization

**Configuration A** (Enabled):
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "enableVirtualization": true,
    "virtualizationThreshold": 50
  }
}
```

**Configuration B** (Disabled):
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "enableVirtualization": false
  }
}
```

**Steps**:
1. Test with 100+ records
2. Apply Config A - observe virtualization
3. Apply Config B - observe full rendering

**Expected Results**:
- ✅ Config A: Only visible rows rendered
- ✅ Config B: All rows rendered
- ✅ Performance difference measurable

**Priority**: Medium
**Status**: ⏸️ Pending environment access + large dataset

---

## TS-06: Performance Tests

### TS-06.1: Initial Load Performance

**Objective**: Measure initial grid load time

**Prerequisites**: Entity with 50 records

**Steps**:
1. Open form with control
2. Measure time from form load to grid display
3. Record in browser performance tools

**Expected Results**:
- ✅ Load time < 2 seconds
- ✅ No visible rendering delay
- ✅ Smooth appearance

**Measurement Tool**: Browser DevTools Performance tab

**Priority**: High
**Status**: ⏸️ Pending environment access

---

### TS-06.2: Large Dataset Performance

**Objective**: Test with 100+ records

**Prerequisites**: Entity with 100+ records

**Steps**:
1. Configure control on entity with 100+ records
2. Open form
3. Scroll through records
4. Measure scroll performance

**Expected Results**:
- ✅ Virtualization activates
- ✅ Smooth scrolling (60 fps)
- ✅ No freezing or lag
- ✅ Memory usage stable

**Measurement**:
- Initial load: < 3 seconds
- Scroll performance: Smooth
- Memory: < 100MB increase

**Priority**: High
**Status**: ⏸️ Pending environment access + large dataset

---

### TS-06.3: Command Execution Performance

**Objective**: Measure command response time

**Steps**:
1. Click "Refresh" command
2. Measure time to completion
3. Click "Open" command
4. Measure time to form open

**Expected Results**:
- ✅ Refresh: < 2 seconds
- ✅ Open: < 1 second
- ✅ No delays or freezing

**Priority**: Medium
**Status**: ⏸️ Pending environment access

---

### TS-06.4: Concurrent User Load

**Objective**: Test with multiple users

**Prerequisites**: 5+ test user accounts

**Steps**:
1. Have 5 users open same form simultaneously
2. All users interact with control
3. Monitor performance for all users

**Expected Results**:
- ✅ No degradation in performance
- ✅ Each user has independent state
- ✅ No conflicts or errors

**Priority**: Low
**Status**: ⏸️ Pending environment access + test users

---

## TS-07: Integration Tests

### TS-07.1: Dataverse Permission Integration

**Objective**: Verify control respects user permissions

**Prerequisites**: Test user with limited permissions

**Steps**:
1. Create test user with Read-only access
2. Login as test user
3. Open form with control
4. Attempt to use commands

**Expected Results**:
- ✅ Grid displays (read permission)
- ✅ Open command works
- ✅ Create command disabled (no create permission)
- ✅ Delete command disabled (no delete permission)
- ✅ Refresh works

**Priority**: Critical
**Status**: ⏸️ Pending environment access + test user

---

### TS-07.2: Field-Level Security

**Objective**: Verify field-level security is respected

**Prerequisites**: Field-level security configured on entity

**Steps**:
1. Configure FLS on specific fields
2. Login as user without access
3. View grid

**Expected Results**:
- ✅ Secured fields not displayed
- ✅ Accessible fields displayed
- ✅ No error messages
- ✅ Grid renders properly

**Priority**: High
**Status**: ⏸️ Pending environment access + FLS setup

---

### TS-07.3: Business Rule Integration

**Objective**: Verify interaction with business rules

**Prerequisites**: Business rule on test entity

**Steps**:
1. Create business rule (e.g., make field required)
2. Open record via control
3. Modify field
4. Save

**Expected Results**:
- ✅ Business rules execute on form
- ✅ Validation occurs
- ✅ Grid refreshes after save
- ✅ Updated data visible

**Priority**: Medium
**Status**: ⏸️ Pending environment access

---

### TS-07.4: Plugin Integration

**Objective**: Verify control works with plugins

**Prerequisites**: Plugin on entity (e.g., pre-create)

**Steps**:
1. Use Create command
2. Create new record
3. Plugin executes
4. Verify grid refreshes with plugin changes

**Expected Results**:
- ✅ Plugin executes normally
- ✅ Grid reflects plugin changes
- ✅ No errors from plugin/control interaction

**Priority**: Medium
**Status**: ⏸️ Pending environment access + plugin setup

---

## TS-08: Theme & Appearance Tests

### TS-08.1: Light Theme

**Objective**: Verify appearance in light theme

**Prerequisites**: App configured with light theme

**Steps**:
1. Open app in light theme
2. View control

**Expected Results**:
- ✅ Light background colors
- ✅ Dark text for readability
- ✅ Proper contrast ratios
- ✅ Fluent UI design consistency

**Priority**: Medium
**Status**: ⏸️ Pending environment access

---

### TS-08.2: Dark Theme

**Objective**: Verify appearance in dark theme

**Prerequisites**: App configured with dark theme

**Steps**:
1. Open app in dark theme
2. View control

**Expected Results**:
- ✅ Dark background colors
- ✅ Light text for readability
- ✅ Proper contrast ratios
- ✅ Fluent UI design consistency

**Priority**: Medium
**Status**: ⏸️ Pending environment access

---

### TS-08.3: High Contrast Mode

**Objective**: Verify high contrast accessibility

**Steps**:
1. Enable high contrast mode in Windows
2. Open app
3. View control

**Expected Results**:
- ✅ High contrast colors applied
- ✅ Text readable
- ✅ Controls visible
- ✅ Focus indicators clear

**Priority**: Low
**Status**: ⏸️ Pending environment access

---

## TS-09: Accessibility Tests

### TS-09.1: Keyboard Navigation

**Objective**: Navigate control using keyboard only

**Steps**:
1. Tab to control
2. Use arrow keys to navigate rows
3. Press Enter to open record
4. Press Escape to cancel
5. Use Space to select records

**Expected Results**:
- ✅ Tab focuses on control
- ✅ Arrow keys navigate cells/rows
- ✅ Enter activates commands
- ✅ Escape cancels actions
- ✅ Space toggles selection
- ✅ Focus indicators visible

**Priority**: High
**Status**: ⏸️ Pending environment access

---

### TS-09.2: Screen Reader (NVDA)

**Objective**: Test with NVDA screen reader

**Steps**:
1. Enable NVDA
2. Navigate to control
3. Navigate through grid
4. Execute commands

**Expected Results**:
- ✅ Control announced as "Grid"
- ✅ Column headers announced
- ✅ Cell content announced
- ✅ Selection state announced
- ✅ Commands announced

**Priority**: High
**Status**: ⏸️ Pending environment access + NVDA

---

### TS-09.3: Screen Reader (JAWS)

**Objective**: Test with JAWS screen reader

**Steps**:
1. Enable JAWS
2. Navigate to control
3. Navigate through grid
4. Execute commands

**Expected Results**:
- ✅ Control announced correctly
- ✅ Navigation works
- ✅ Content readable
- ✅ Commands accessible

**Priority**: Medium
**Status**: ⏸️ Pending environment access + JAWS

---

## TS-10: Browser Compatibility Tests

### TS-10.1: Chrome

**Browser**: Google Chrome (latest)

**Steps**:
1. Open control in Chrome
2. Test all core features
3. Check console for errors

**Expected Results**:
- ✅ Control loads
- ✅ All features work
- ✅ No console errors
- ✅ Performance acceptable

**Priority**: Critical
**Status**: ⏸️ Pending environment access

---

### TS-10.2: Edge

**Browser**: Microsoft Edge (latest)

**Steps**:
1. Open control in Edge
2. Test all core features
3. Check console for errors

**Expected Results**:
- ✅ Control loads
- ✅ All features work
- ✅ No console errors
- ✅ Performance acceptable

**Priority**: Critical
**Status**: ⏸️ Pending environment access

---

### TS-10.3: Firefox

**Browser**: Mozilla Firefox (latest)

**Steps**:
1. Open control in Firefox
2. Test all core features
3. Check console for errors

**Expected Results**:
- ✅ Control loads
- ✅ All features work
- ✅ No console errors
- ✅ Performance acceptable

**Priority**: High
**Status**: ⏸️ Pending environment access

---

### TS-10.4: Safari

**Browser**: Safari 14+ (macOS)

**Steps**:
1. Open control in Safari
2. Test all core features
3. Check console for errors

**Expected Results**:
- ✅ Control loads
- ✅ All features work
- ✅ No console errors
- ✅ Performance acceptable

**Priority**: Medium
**Status**: ⏸️ Pending environment access

---

## TS-11: Mobile Tests

### TS-11.1: iOS Safari

**Device**: iPhone/iPad

**Steps**:
1. Open model-driven app on iOS
2. Navigate to form with control
3. Test touch interactions

**Expected Results**:
- ✅ Control renders responsively
- ✅ Touch navigation works
- ✅ Commands accessible
- ✅ Performance acceptable

**Priority**: Medium
**Status**: ⏸️ Pending environment access + iOS device

---

### TS-11.2: Android Chrome

**Device**: Android phone/tablet

**Steps**:
1. Open model-driven app on Android
2. Navigate to form with control
3. Test touch interactions

**Expected Results**:
- ✅ Control renders responsively
- ✅ Touch navigation works
- ✅ Commands accessible
- ✅ Performance acceptable

**Priority**: Medium
**Status**: ⏸️ Pending environment access + Android device

---

## Test Summary

### Test Coverage

| Category | Total Scenarios | Critical | High | Medium | Low |
|----------|----------------|----------|------|--------|-----|
| Installation | 3 | 3 | 0 | 0 | 0 |
| Basic Functionality | 4 | 1 | 3 | 0 | 0 |
| Commands & Actions | 5 | 1 | 3 | 1 | 0 |
| Selection | 4 | 0 | 2 | 2 | 0 |
| Configuration | 5 | 0 | 3 | 2 | 0 |
| Performance | 4 | 0 | 2 | 1 | 1 |
| Integration | 4 | 1 | 1 | 2 | 0 |
| Theme & Appearance | 3 | 0 | 0 | 2 | 1 |
| Accessibility | 3 | 0 | 2 | 1 | 0 |
| Browser Compatibility | 4 | 2 | 1 | 1 | 0 |
| Mobile | 2 | 0 | 0 | 2 | 0 |
| **TOTAL** | **41** | **8** | **17** | **13** | **3** |

### Execution Status

- ✅ **Completed**: 0
- ⏸️ **Pending Environment Access**: 41
- ❌ **Failed**: 0

### Prerequisites for Testing

1. ✅ Test scenarios documented
2. ⏸️ Dataverse environment access
3. ⏸️ Solution deployed
4. ⏸️ Test data prepared
5. ⏸️ Test users configured

---

**Ready for Execution**: Once Dataverse environment is available
**Estimated Test Execution Time**: 8-12 hours (full suite)
**Recommended Approach**: Prioritize Critical and High priority tests first

---

**Document Status**: ✅ Complete
**Last Updated**: 2025-10-04
