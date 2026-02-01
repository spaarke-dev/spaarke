# E2E Test: Update Related Push Flow

## Overview

This test validates the "Update Related Records" button functionality, which allows pushing field values from a parent entity (Matter) to all related child records (Events) in a single operation.

**Success Criteria (SC-10, SC-13)**:
- SC-10: "Update Related" button pushes mappings to all children
- SC-13: Push API returns accurate counts

---

## Prerequisites

- UpdateRelatedButton PCF deployed on parent entity form (Matter)
- Field mapping profile configured: Matter → Event with field rules
- Multiple Event records linked to the Matter via the regarding field
- User has modify permissions on all Event records
- Dataverse environment accessible with test data

---

## Test Scenario 1: Push to Multiple Child Records

### Objective
Verify that clicking the "Update Related Records" button successfully pushes field values from parent to all child records.

### Setup Steps

1. **Create parent Matter**
   - Title: "Test Matter for Update Related - Scenario 1"
   - Client: "Acme Corp"
   - Status: Active
   - Other mapped fields (e.g., Practice Area, Matter Type): Set to specific values

2. **Create 3+ related Events**
   - Create Event 1: Title "Deadline", Parent Matter = Test Matter, mapped fields intentionally different
   - Create Event 2: Title "Status Meeting", Parent Matter = Test Matter, mapped fields different
   - Create Event 3: Title "Review", Parent Matter = Test Matter, mapped fields different
   - Note initial values for each field to compare after push

3. **Update Matter mapped fields**
   - Change Client to "Beta Industries"
   - Change Practice Area (if applicable)
   - Save the record

### Test Steps

1. Open Matter form in Dynamics (Model-Driven App)
2. Verify "Update Related Records" button is visible in form ribbon
3. Click "Update Related Records" button
4. Verify confirmation dialog appears showing:
   - "Update 3 related Event records?"
   - Count of affected child records
   - List or count of events to be updated
5. Click "Confirm" in dialog
6. Observe UI state during operation:
   - Progress indicator (spinner) should display
   - Dialog should remain open or show progress
   - Button should be disabled during operation
7. Wait for operation to complete
8. Verify success toast notification displays:
   - Text: "Updated 3 of 3 Event records"
   - Toast should disappear after 3-5 seconds
   - No error toasts present

### Verification Steps

1. **Verify child records updated**
   - Open Event 1 form
   - Check that Client field now shows "Beta Industries"
   - Check that other mapped fields match the Matter values
   - Save and close
   - Repeat for Event 2 and Event 3

2. **Verify via API (optional)**
   - Call GET `/api/v1/field-mappings/verify?sourceId={matter-id}&targetType=sprk_event`
   - Verify response shows all 3 Events have updated values
   - Check response timestamp is recent

### Expected Results

- All 3 Events have updated field values matching the Matter
- No errors in browser console
- Event log entries created for each Event update (if Event Log is enabled)
- Success notification persists for appropriate duration
- All child records show audit trail of update by current user

---

## Test Scenario 2: Partial Failure Handling

### Objective
Verify that the system handles partial failures gracefully when some child records fail to update due to validation errors or permissions.

### Setup Steps

1. **Create parent Matter**
   - Title: "Test Matter for Update Related - Scenario 2 (Partial Failure)"
   - Client: "Gamma Ltd"
   - Status: Active

2. **Create Events with mixed validation states**
   - Event 1 (Success): Title "Good Event", standard configuration, update-eligible
   - Event 2 (Locked): Title "Locked Event", set field with validation rule that will fail
     - Create Event Type that requires "Base Date" field
     - Create Event with this Event Type but leave Base Date empty (will fail on update)
   - Event 3 (Success): Title "Another Good Event", standard configuration, update-eligible
   - Result: 2 should succeed, 1 should fail

3. **Update Matter fields**
   - Change Client to "Delta Solutions"
   - Save

### Test Steps

1. Open Matter form
2. Click "Update Related Records" button
3. Verify dialog shows "Update 3 related Event records"
4. Click "Confirm"
5. Observe progress indicator
6. Wait for operation completion

### Verification Steps

1. **Check toast notification**
   - Toast should show warning level: "Updated 2 of 3 Event records"
   - Or detailed message: "Updated 2 records. 1 failed."
   - Toast should include error details or clickable link to error log

2. **Verify successful updates**
   - Open Event 1: Client field updated to "Delta Solutions" ✅
   - Open Event 3: Client field updated to "Delta Solutions" ✅

3. **Verify failed record unchanged**
   - Open Event 2: Client field NOT updated (still original value) ✅
   - Event 2 should have error or warning indicator (if applicable)

4. **Check error log or details**
   - System should provide way to see which record failed and why
   - Error message should be clear: e.g., "Required field 'Base Date' missing"

### Expected Results

- Toast shows accurate count: "Updated 2 of 3 records. 1 failed."
- Successful events updated correctly
- Failed event unchanged and accessible for manual correction
- Error details visible (via toast link, error log, or browser console)
- No partial application (all-or-nothing per record, but operation continues for others)

---

## Test Scenario 3: No Related Records

### Objective
Verify appropriate handling when parent record has no related child records.

### Setup Steps

1. **Create parent Matter with no Events**
   - Title: "Lonely Matter - No Events"
   - Client: "Orphan Corp"
   - Do NOT create any Event records linked to this Matter

### Test Steps

1. Open Matter form
2. Click "Update Related Records" button

### Expected Results (Option A: Button Disabled)

- Button is disabled (greyed out)
- Tooltip on button says: "No related Event records to update"
- No dialog appears
- No toast notification

### Expected Results (Option B: Toast Message)

- Dialog appears showing "0 related Event records"
- User can proceed to click "Confirm"
- Toast displays: "No related Event records to update" (info or warning level)
- Operation completes immediately without API call

### Expected Results (Option C: Dialog Only)

- Dialog appears but with count = 0
- "Confirm" button is disabled in dialog
- Cancel button is active
- Closing dialog shows no toast (cancelled operation)

**Recommended**: Option A (disabled button) provides clearest UX

---

## Test Scenario 4: Large Batch Update (Performance)

### Objective
Verify system handles updates to a larger batch of child records efficiently.

### Setup Steps

1. **Create parent Matter**
   - Title: "Large Batch Test Matter"
   - Client: "Bulk Corp"

2. **Create 20+ related Events**
   - Programmatically create 25 Event records linked to Matter
   - Each with different values for mapped fields
   - Use API or test data script for efficiency

3. **Update Matter**
   - Change Client and other mapped fields

### Test Steps

1. Open Matter form
2. Click "Update Related Records"
3. Verify dialog shows "Update 25 related Event records"
4. Click "Confirm"
5. Observe progress indicator during operation
6. Record operation duration

### Expected Results

- Dialog shows accurate count (25)
- Progress indicator visible throughout operation
- Operation completes within 10-30 seconds (depends on system load)
- Toast shows: "Updated 25 of 25 Event records"
- All 25 Events have updated field values
- No timeout or connection errors
- No console errors

---

## Test Scenario 5: Concurrent Updates (Edge Case)

### Objective
Verify behavior when parent record is modified while push operation is in progress.

### Setup Steps

1. **Create parent Matter with Events**
   - Title: "Concurrent Edit Test"
   - Client: "Parallel Inc"
   - Create 3 Events

2. **Update Matter**
   - Client: "Parallel Inc" → "Different Company"
   - Do NOT save yet

### Test Steps

1. From first browser window: Open Matter form, update Client field
2. Click "Update Related Records"
3. Dialog appears asking to update 3 Events
4. **BEFORE confirming in window 1**:
   - From second browser window: Open same Matter form
   - Change Client field again to "Another Company"
   - Click Save in second window
5. Return to first window and click "Confirm" on Update Related dialog

### Expected Results

- First window completes push with original values ("Different Company")
- Second window save conflict detected OR
- System refreshes and uses latest values for push
- Toast shows success (updated with values from one of the states)
- All Events have consistent values
- User notified of concurrent edit if applicable

---

## Test Scenario 6: User Permissions Check

### Objective
Verify that Update Related respects record-level permissions.

### Setup Steps

1. **Create Matter and Events owned by User A**
   - User A owns the Matter and all 3 Events
   - Sufficient permissions granted

2. **Create Event with restricted access**
   - Event 4: Owned by User B with limited sharing
   - User A can read but NOT modify

3. **Link Event 4 to Matter**
   - Add Event 4 to related Events list

### Test Steps

1. Login as User A
2. Open Matter form
3. Click "Update Related Records"
4. Dialog shows "Update 4 related Event records"
5. Click "Confirm"

### Expected Results

- Toast shows warning: "Updated 3 of 4 records. 1 failed due to permissions."
- Events 1-3 updated successfully
- Event 4 unchanged (permission denied)
- Error details indicate permission issue, not validation issue
- Clear message to user about which record failed and why

---

## Test Scenario 7: Mapped Fields Validation

### Objective
Verify that only configured field mappings are applied.

### Setup Steps

1. **Create Field Mapping Profile**
   - Profile: "Matter to Event - Limited"
   - Rules:
     - Matter.Client → Event.Client (INCLUDE)
     - Matter.PracticeArea → Event.PracticeArea (INCLUDE)
     - Matter.Status → NOT MAPPED (should be excluded)

2. **Create Matter with Events**
   - Title: "Mapping Validation Test"
   - Client: "Mapped Corp"
   - PracticeArea: "Corporate Law"
   - Status: Active
   - Create 2 Events

3. **Set Event Status to "Cancelled" (different from Matter)**

### Test Steps

1. Open Matter form
2. Update Client to "New Mapped Corp"
3. Update Status to "Completed"
4. Click "Update Related Records"
5. Dialog shows update details
6. Click "Confirm"

### Expected Results

- Event.Client updated to "New Mapped Corp" ✅
- Event.PracticeArea updated to "Corporate Law" ✅
- Event.Status remains "Cancelled" (NOT changed to "Completed") ✅
- Only mapped fields are updated
- System respects field mapping profile rules

---

## Test Scenario 8: Toast Messages and User Feedback

### Objective
Verify user receives clear, actionable feedback throughout operation.

### Setup Steps

1. **Create Matter with 3 Events**
   - Title: "Toast Test Matter"
   - Client: "Feedback Corp"

2. **Update Matter fields**
   - Client: "Updated Corp"
   - PracticeArea: "Litigation"

### Test Steps

1. Open Matter form
2. Click "Update Related Records"
3. Observe all messages throughout flow:
   - Initial toast (if any): "Preparing update for 3 records..."
   - Dialog message: "Update X related Event records?"
   - During progress: Visual indicator (spinner/progress bar)
   - Completion toast: "Updated 3 of 3 Event records"

### Expected Results

- All messages are clear and professional
- No technical jargon in user-facing text
- Toast font size readable (not too small)
- Toast color appropriate for message level (green for success, orange for warning)
- Toast dismissible or auto-dismisses after 3-5 seconds
- No console errors despite operation success

---

## Common Issues to Monitor

| Issue | Detection | Resolution |
|-------|-----------|-----------|
| Stale field values | Child records show old values after push | Clear browser cache, refresh Dataverse session |
| Timeout on large batch | Toast shows "Operation timed out" | Increase API timeout setting, reduce batch size |
| Locked records | Toast shows "X failed" | Check record ownership, sharing settings |
| Missing mapping | Field not updated despite rule configured | Verify field mapping profile is active, rule exists |
| Permission denied | Event shows as failed | Grant modify permission to user for child records |
| API 500 error | Console shows server error | Check API logs, verify field types compatible |

---

## Success Criteria Summary

All test scenarios PASS if:

1. ✅ Single-click operation updates all child records
2. ✅ Confirmation dialog shows accurate child count
3. ✅ Progress indicator visible during operation
4. ✅ Success toast shows accurate count: "Updated X of Y records"
5. ✅ Failed records reported with reasons
6. ✅ No child records have partial/corrupted data
7. ✅ Operation respects field mapping profile
8. ✅ User permissions enforced
9. ✅ All UI elements disabled during operation (prevent double-click)
10. ✅ Appropriate error handling and user feedback

---

## Test Data Cleanup

After all scenarios complete:

1. Delete test Matter records and related Events
2. Verify cascade delete removes Event Log entries
3. Clear any test Field Mapping Profiles created
4. Restore environment to clean state for next test run

---

## Related Tests

- Task 060: Event creation with regarding record
- Task 061: Field mapping auto-application
- Task 062: Refresh from Parent flow
- Task 064: Dark mode verification (verify Update Related button in dark mode)

---

## Notes

- These test scenarios validate success criteria SC-10 and SC-13 from spec.md
- Test can run in parallel with Tasks 060, 061, 062 (Parallel Group F)
- Recommend execution in dev environment first, then staging before production
- Document any deviations or product decisions (e.g., Option A vs B for No Records scenario)
- Update this document if test results indicate product behavior changes needed
