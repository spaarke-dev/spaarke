# E2E Test: Refresh from Parent Flow

**Project**: events-and-workflow-automation-r1
**Feature**: Refresh from Parent button (Manual Refresh sync mode)
**Success Criteria**: SC-09
**Date Created**: 2026-02-01

---

## Overview

The "Refresh from Parent" button allows users to manually sync values from the parent record after parent data changes. This test validates the Manual Refresh sync mode functionality, ensuring that clicking the button correctly re-applies field mappings from the parent record.

---

## Prerequisites

- **Dataverse Environment**: Dev environment with events-and-workflow-automation solution deployed
- **Records Setup**:
  - Event record with a regarding record (Matter) set
  - Field mapping profile exists for the Event→Matter entity pair
  - Parent (Matter) record has updated values different from the Event
- **User Permissions**:
  - Can read Event and Matter records
  - Can update Event record
  - Can access Event form with AssociationResolver and UpdateRelatedButton controls
- **Browser Requirements**:
  - Chrome/Edge with Dynamics 365 access
  - Form load time < 3 seconds

---

## Test Scenario 1: Refresh Overwrites All Fields

### Goal
Verify that clicking "Refresh from Parent" and confirming re-applies all field mappings, even if user modified them locally.

### Preconditions
1. Event record linked to Matter via regarding field
2. Field Mapping Profile exists with rules like:
   - Matter.Description → Event.sprk_description
   - Matter.Account → Event.sprk_relatedaccount
   - Matter.Category → Event.sprk_category (if exists)
3. Matter has updated values:
   - Matter.Description = "Updated description from Matter"
   - Matter.Account = "Acme Corp"
   - Matter.Category = "Legal"
4. Event currently has different values:
   - Event.sprk_description = "Modified description by user"
   - Event.sprk_relatedaccount = "Old Account"
   - Event.sprk_category = "Financial"

### Steps

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Open Event form in Dataverse | Event form loads with all mapped fields visible |
| 2 | Note current field values in Event | Values match precondition (user-modified) |
| 3 | Open Matter record in another browser tab/window | Verify Matter has new values |
| 4 | Return to Event form tab | Event still shows user-modified values (form not auto-refreshed) |
| 5 | Locate and click "Refresh from Parent" button | Confirmation dialog appears with message like "This will overwrite Event fields with values from Matter. Continue?" |
| 6 | Verify confirmation dialog shows correct parent entity name | Dialog shows "Matter" (not generic "parent") |
| 7 | Click "Confirm" button | Dialog closes, form refreshes |
| 8 | Verify Event fields now match Matter values | All fields show updated values: Description = "Updated description from Matter", Account = "Acme Corp", Category = "Legal" |
| 9 | Verify toast notification appears | Toast shows "Refreshed 3 fields from Matter" (or similar, with correct count) |
| 10 | Refresh the form in browser (F5) | Values persist after refresh (confirmed in database) |

### Expected Results

✅ **Acceptance Criteria**:
- Confirmation dialog appears before overwriting
- Dialog confirms user is overwriting user-modified values
- All mapped fields updated to match parent record
- Even fields modified by user are overwritten
- Toast notification shows correct count of fields refreshed
- Values persist after form refresh (database confirmed)

✅ **Not Accepted**:
- If dialog doesn't appear (accidental overwrites)
- If only unmapped fields update
- If user-modified fields are skipped
- If field count in toast is incorrect

---

## Test Scenario 2: Cancel Refresh Dialog

### Goal
Verify that clicking "Cancel" in the confirmation dialog leaves Event unchanged.

### Preconditions
1. Event record linked to Matter
2. Field Mapping Profile exists for Event→Matter
3. Event has different values than Matter:
   - Event.sprk_description = "My custom value"
   - Matter.Description = "Updated from Matter"

### Steps

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Open Event form | Form loads |
| 2 | Note current field values | sprk_description = "My custom value" |
| 3 | Click "Refresh from Parent" button | Confirmation dialog appears |
| 4 | Verify dialog has two buttons: "Confirm" and "Cancel" | Both buttons present and enabled |
| 5 | Click "Cancel" button | Dialog closes immediately |
| 6 | Verify Event fields unchanged | sprk_description still = "My custom value" |
| 7 | Verify no toast appears | No notification displayed |
| 8 | Save the form | No changes to save |

### Expected Results

✅ **Acceptance Criteria**:
- Dialog closes without changes
- Event fields remain unchanged
- No toast notification displayed
- No changes persist to database

✅ **Not Accepted**:
- If any fields are updated after cancel
- If toast appears despite cancel
- If form is marked as "changed" after cancel

---

## Test Scenario 3: Button Disabled Without Field Mapping Profile

### Goal
Verify that "Refresh from Parent" button is hidden or disabled when no field mapping profile exists for the entity pair.

### Preconditions
1. Event record linked to Account (Account is supported entity type)
2. **NO** Field Mapping Profile exists for Event→Account pair
3. Event form loads normally with AssociationResolver control set to "Account"

### Steps

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Open Event form | Form loads |
| 2 | Locate "Refresh from Parent" button | Button is visible but in disabled state (grayed out) |
| 3 | Hover over button | Tooltip appears: "No field mapping profile for this entity type" |
| 4 | Attempt to click disabled button | Click has no effect |
| 5 | Create a Field Mapping Profile for Event→Account | Profile created with at least one mapping rule |
| 6 | Refresh Event form (F5 or navigate away and back) | Form refreshes |
| 7 | Verify "Refresh from Parent" button is now enabled | Button is no longer grayed out, tooltip gone |
| 8 | Hover over enabled button | Button shows standard hover state, no tooltip |

### Expected Results

✅ **Acceptance Criteria**:
- Button disabled when no profile exists
- Clear tooltip explains why button is disabled
- Button becomes enabled when profile is created
- Button click has no effect when disabled

✅ **Not Accepted**:
- If button is hidden instead of disabled (user should know it exists)
- If button is clickable without profile (error handling must occur)
- If tooltip is missing or unclear

---

## Test Scenario 4: Button Hidden Without Regarding Record

### Goal
Verify that "Refresh from Parent" button is hidden when Event has no regarding record set.

### Preconditions
1. Event record with **NO** regarding record set (sprk_regarding{entity} fields are all empty)
2. AssociationResolver control exists on form but no entity selected

### Steps

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Open Event form | Form loads |
| 2 | Verify no regarding record is set | AssociationResolver shows empty/no selection |
| 3 | Check for "Refresh from Parent" button | Button is **hidden** (not visible at all, not disabled) |
| 4 | Select a regarding record in AssociationResolver | Choose "Matter" and select a Matter record |
| 5 | Verify "Refresh from Parent" button appears | Button is now visible |

### Expected Results

✅ **Acceptance Criteria**:
- Button hidden when no regarding record
- Button appears when regarding record is selected
- No confusion about missing button (only appears when meaningful)

✅ **Not Accepted**:
- If button is disabled instead of hidden
- If button appears but is non-functional when no regarding record

---

## Test Scenario 5: Refresh with Partial Mappings

### Goal
Verify that "Refresh from Parent" updates only fields that have mappings.

### Preconditions
1. Event record linked to Matter
2. Field Mapping Profile has 2 rules:
   - Matter.Description → Event.sprk_description
   - Matter.Account → Event.sprk_relatedaccount
3. Event has values in 4 fields:
   - sprk_description = "Old Event description"
   - sprk_relatedaccount = "Old Account"
   - sprk_category = "Category from manual entry"
   - sprk_notes = "Notes from manual entry"
4. Matter has values:
   - Description = "New Matter Description"
   - Account = "New Account"
   - category = (not mapped)
   - notes = (not mapped)

### Steps

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Open Event form | Form loads with all 4 fields visible |
| 2 | Note values in all fields | All 4 fields have values as in precondition |
| 3 | Click "Refresh from Parent" | Confirmation dialog appears |
| 4 | Click "Confirm" | Dialog closes, refresh executes |
| 5 | Verify mapped fields updated | sprk_description and sprk_relatedaccount changed to Matter values |
| 6 | Verify unmapped fields unchanged | sprk_category and sprk_notes still = "Category..." and "Notes..." |
| 7 | Verify toast shows correct count | Toast says "Refreshed 2 fields from Matter" (not 4) |

### Expected Results

✅ **Acceptance Criteria**:
- Only mapped fields are updated
- Unmapped fields retain their values
- Toast count reflects mapped fields only
- User understands which fields changed and which didn't

✅ **Not Accepted**:
- If unmapped fields are affected
- If toast shows incorrect count

---

## Test Scenario 6: Type Compatibility Validation During Refresh

### Goal
Verify that "Refresh from Parent" respects type compatibility rules (Strict Mode).

### Preconditions
1. Event record linked to Matter
2. Field Mapping Profile has incompatible rule (will be skipped):
   - Matter.Category (OptionSet) → Event.sprk_category (Text field)
3. Compatible rule exists:
   - Matter.Description (Text) → Event.sprk_description (Text)
4. Matter.Description = "New description"
5. Event.sprk_description = "Old description"
6. Event.sprk_category = "User-set value"

### Steps

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Open Event form | Form loads |
| 2 | Click "Refresh from Parent" | Confirmation dialog |
| 3 | Click "Confirm" | Refresh executes |
| 4 | Verify compatible field updated | sprk_description = "New description" |
| 5 | Verify incompatible field NOT updated | sprk_category = "User-set value" (unchanged) |
| 6 | Verify toast explains partial success | Toast says "Refreshed 1 of 2 fields from Matter" or similar |
| 7 | Check error log or form message | Clear message about incompatible rule (if shown) |

### Expected Results

✅ **Acceptance Criteria**:
- Compatible fields update successfully
- Incompatible fields skipped without error
- Toast accurately reports which fields were updated
- User understands partial success is expected

✅ **Not Accepted**:
- If incompatible field is forced with wrong value
- If toast shows success when some fields failed

---

## Test Scenario 7: Concurrent Edits - Parent Updated While Dialog Open

### Goal
Verify behavior when parent record is updated while user has confirmation dialog open.

### Preconditions
1. Event record linked to Matter
2. Field Mapping Profile exists with Matter.Description → Event.sprk_description
3. Matter.Description = "Original"
4. Event.sprk_description = "Original"
5. Second user/tab ready to update Matter record

### Steps

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Open Event form (Tab 1) | Form loads |
| 2 | Click "Refresh from Parent" (Tab 1) | Confirmation dialog appears |
| 3 | In another tab (Tab 2), open Matter and update | Matter.Description = "Updated by other user" |
| 4 | Return to Tab 1 | Dialog still open (no auto-close) |
| 5 | Click "Confirm" on dialog (Tab 1) | Dialog closes, refresh executes |
| 6 | Verify Event refreshed with latest parent values | Event.sprk_description = "Updated by other user" (gets latest, not stale) |

### Expected Results

✅ **Acceptance Criteria**:
- Dialog doesn't auto-close when parent changes
- Refresh gets latest parent values (not cached)
- User sees most recent data
- No data integrity issues

✅ **Not Accepted**:
- If refresh uses stale cached values
- If application crashes on concurrent updates

---

## Test Scenario 8: Toast Notification Accuracy and Timing

### Goal
Verify toast notifications appear correctly and show accurate information.

### Preconditions
1. Event linked to Matter with 3 mapped fields
2. All fields have current values in both records
3. User can observe toast area (bottom of form or notification center)

### Steps

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click "Refresh from Parent" | Dialog appears |
| 2 | Click "Confirm" | Dialog closes |
| 3 | Observe notification area | Toast appears within 500ms |
| 4 | Read toast message | Message says "Refreshed 3 fields from Matter" (or equivalent) |
| 5 | Verify toast placement | Toast is visible, not hidden behind other elements |
| 6 | Wait for auto-dismiss | Toast auto-dismisses after 3-5 seconds |
| 7 | Verify toast doesn't interfere | Can still interact with form while toast visible |

### Expected Results

✅ **Acceptance Criteria**:
- Toast appears promptly (< 500ms)
- Message clearly shows field count
- Toast auto-dismisses
- Toast doesn't interfere with form usage
- Multiple refreshes show correct counts each time

✅ **Not Accepted**:
- If toast doesn't appear
- If message is vague ("Refreshed" without count)
- If toast blocks form access
- If count is incorrect

---

## Test Scenario 9: Long-Running Refresh (Many Fields)

### Goal
Verify refresh completes successfully with many mapped fields and shows progress/status.

### Preconditions
1. Event linked to Matter
2. Field Mapping Profile has 10+ mapped fields:
   - Description, Account, Category, Owner, Type, Status, Priority, Urgency, Duration, Notes, etc.
3. All Matter fields have new values
4. Event fields currently have old values

### Steps

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click "Refresh from Parent" | Dialog appears |
| 2 | Click "Confirm" | Dialog closes, refresh begins |
| 3 | Observe form during refresh | Form doesn't freeze, user can see progress (optional: spinner/progress bar) |
| 4 | Wait for completion | All 10+ fields update within 2 seconds |
| 5 | Verify all fields populated | All mapped fields show new Matter values |
| 6 | Verify toast appears | Shows "Refreshed 10+ fields from Matter" |

### Expected Results

✅ **Acceptance Criteria**:
- Refresh completes without errors
- Form responsive during refresh (no UI freeze)
- All fields update correctly
- Performance acceptable (< 2 seconds for 10+ fields)

✅ **Not Accepted**:
- If refresh times out
- If form freezes during update
- If some fields don't update
- If performance is poor (> 5 seconds)

---

## Test Scenario 10: Error Handling - Parent Record Deleted

### Goal
Verify graceful error handling when parent record is deleted.

### Preconditions
1. Event linked to Matter
2. Field Mapping Profile exists
3. Matter record will be deleted before refresh

### Steps

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click "Refresh from Parent" | Confirmation dialog appears |
| 2 | Delete Matter record in another tab | Record removed from system |
| 3 | Click "Confirm" on dialog | Dialog closes, refresh attempts to execute |
| 4 | Observe form behavior | Form shows error message: "Unable to refresh: parent record (Matter) not found" |
| 5 | Verify Event unchanged | Event fields not modified from before attempt |
| 6 | Verify error is clear | User understands what went wrong |
| 7 | Verify user can dismiss error | Error message can be closed |

### Expected Results

✅ **Acceptance Criteria**:
- No crash or unhandled exception
- Clear error message explains issue
- Event fields not modified
- Error can be dismissed

✅ **Not Accepted**:
- If application crashes
- If error message is cryptic or vague
- If fields are partially updated then error shown

---

## Test Data Setup

### Matter Record (Test)
```
Name: "Test Matter - Refresh Parent"
Description: "Updated description - use for Refresh test"
Account: "Contoso Ltd"
Category: "Commercial"
Owner: "System Administrator"
Type: "Litigation"
Status: "Active"
Priority: "High"
```

### Event Record (Test)
```
Name: "Test Event - Refresh Test"
Regarding: Test Matter (linked)
sprk_description: "Old event description"
sprk_relatedaccount: "Old Account"
sprk_category: "Finance"
sprk_owner: (blank)
sprk_type: (blank)
```

### Field Mapping Profile (Test)
```
Name: "Event-Matter-Refresh-Test"
Source Entity: Matter
Target Entity: Event
Rules:
  - Matter.Description → Event.sprk_description (Text → Text)
  - Matter.Account → Event.sprk_relatedaccount (Lookup → Lookup)
  - Matter.Category → Event.sprk_category (OptionSet → OptionSet)
  - Matter.Owner → Event.sprk_owner (Lookup → Lookup)
  - Matter.Type → Event.sprk_type (OptionSet → OptionSet)
```

---

## Success Criteria Summary

All test scenarios must pass for this feature to be complete:

| Scenario | Pass Criteria |
|----------|---------------|
| 1. Refresh Overwrites All Fields | All fields updated, toast shows count, values persist |
| 2. Cancel Refresh | Dialog closes, no changes, no toast |
| 3. Button Disabled Without Profile | Button disabled with clear tooltip |
| 4. Button Hidden Without Regarding Record | Button hidden when no parent, appears when set |
| 5. Partial Mappings | Only mapped fields update, toast shows correct count |
| 6. Type Compatibility | Compatible fields update, incompatible skipped |
| 7. Concurrent Edits | Gets latest parent values, no crashes |
| 8. Toast Accuracy | Toast appears promptly with correct count and message |
| 9. Long-Running Refresh | Completes in < 2 seconds, all fields update |
| 10. Error Handling | Graceful error, clear message, no data corruption |

---

## Regression Tests (Run After Every Change)

1. **Smoke Test**: Open Event with regarding record, verify "Refresh from Parent" button appears and is enabled
2. **Quick Refresh**: Click refresh, confirm, verify one field updates with correct toast
3. **No Profile Test**: Open Event with entity pair that has no profile, verify button is disabled
4. **No Regarding Test**: Open Event with no regarding record, verify button is hidden

---

## Notes for Testers

- **Timing**: These tests typically take 15-20 minutes per scenario
- **Environment**: Must use dev environment with solution deployed
- **Data**: Create test records in dev environment, don't modify production data
- **Browser**: Use Chrome or Edge for best compatibility
- **Accessibility**: Test with keyboard navigation where applicable
- **Performance**: Monitor browser console for JavaScript errors
- **Permissions**: Ensure user has read/write permissions on Event and Matter

---

## References

- **Specification**: SC-09 (Refresh from Parent button re-applies mappings)
- **Related Feature**: AssociationResolver PCF control
- **Related Feature**: UpdateRelatedButton PCF control
- **Architecture**: Field Mapping Framework (ADR-012)
- **UI**: Fluent UI v9, dark mode compatible (ADR-021)

---

**Document Version**: 1.0
**Last Updated**: 2026-02-01
**Status**: Ready for Testing
