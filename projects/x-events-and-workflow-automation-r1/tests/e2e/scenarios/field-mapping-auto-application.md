# E2E Test: Field Mapping Auto-Application

**Test ID**: FMA-001
**Component**: Field Mapping Service + AssociationResolver PCF
**Objective**: Verify that field mappings are automatically applied when a regarding record is selected in the AssociationResolver control
**Related Spec**: SC-08 (Field mappings apply on child record creation), SC-11 (Type compatibility validation)

---

## Test Environment Setup

### Prerequisites

1. **Dataverse Access**
   - Target environment: Dev (https://spaarkedev1.crm.dynamics.com)
   - User: Power User or Admin role with Event create/edit permissions
   - Solution deployed: Latest build with all Phase 6 components (tasks 036, 058)

2. **Field Mapping Profile**
   - Name: "Matter to Event - Test"
   - Source Entity: Matter (sprk_matter)
   - Target Entity: Event (sprk_event)
   - Mapping Rules (minimum 3):
     - Rule 1: `sprk_matter.sprk_client` → `sprk_event.sprk_description` (Text → Memo)
     - Rule 2: `sprk_matter.sprk_budget` → `sprk_event.sprk_estimatedhours` (Number → Number)
     - Rule 3: `sprk_matter.sprk_subject` → `sprk_event.sprk_notes` (Text → Memo)

3. **Test Data: Matter Record**
   - **Logical Name**: sprk_matter
   - **ID**: {test-matter-id} (created in Step 1)
   - **Field Values**:
     - `sprk_client`: "ACME Corp" (required)
     - `sprk_budget`: 50000 (required)
     - `sprk_subject`: "Q1 2026 Legal Analysis" (required)
     - Additional context fields populated

4. **Test Data: Event Form**
   - Configured with AssociationResolver PCF control
   - AssociationResolver supports 8 entity types including Matter
   - Field mapping integration enabled

---

## Test Scenario 1: Auto-Apply on Record Selection

**Objective**: Verify that selecting a Matter in AssociationResolver automatically populates mapped fields with values from the source record.

### Test Steps

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Dataverse App: Model-driven app with Event form | Event form loads successfully |
| 2 | Click **New** Event button | Create new Event record page opens |
| 3 | Observe AssociationResolver control on form | Control displays with entity type dropdown (currently empty/default) |
| 4 | Click entity type dropdown | Dropdown shows: Matter, Project, Invoice, Analysis, Account, Contact, Work Assignment, Budget |
| 5 | Select **"Matter"** from dropdown | Entity type changes to "Matter", search field appears below |
| 6 | Click search field and type "ACME" (partial match) | Search executes, returns list of Matters containing "ACME" |
| 7 | Click the test Matter record from search results | Matter record selected, regarding field populates with Matter ID |
| 8 | Observe form fields immediately | **Expected**: Toast notification appears: "Applied 3 field mappings from Matter" |
| 9 | Observe form field values | **Expected**: Mapped fields populated:<br/>- description: "ACME Corp"<br/>- estimatedhours: 50000<br/>- notes: "Q1 2026 Legal Analysis" |
| 10 | Verify field mapping source link | AssociationResolver shows Matter ID or display name for reference |

### Acceptance Criteria

✅ **PASS** if:
- All 3 field mappings are applied
- Toast notification displays correct count
- Field values match source Matter exactly
- Mapping applies immediately (no save required)
- User can see which fields were populated

❌ **FAIL** if:
- Any mapped field remains empty
- Toast shows incorrect count or missing
- Values don't match source (shows wrong value)
- Delay > 2 seconds before mapping applies
- No visual feedback of mapping action

### Test Data Validation

```
Before Mapping:
  description: [empty]
  estimatedhours: [empty]
  notes: [empty]

After Mapping (EXPECTED):
  description: "ACME Corp"
  estimatedhours: 50000
  notes: "Q1 2026 Legal Analysis"
```

---

## Test Scenario 2: User-Modified Fields NOT Overwritten

**Objective**: Verify that fields manually modified by the user are protected and NOT overwritten by subsequent mapping operations.

### Test Steps

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Start with Event from Scenario 1 complete (fields populated) | Event has mapped values visible |
| 2 | Manually edit `description` field | User typing "My Custom Notes" replaces "ACME Corp" |
| 3 | Leave field focused (mark as user-modified) | Field is marked as changed |
| 4 | Click entity type dropdown | Dropdown opens |
| 5 | Select **Matter** again (or switch to different Matter) | New Matter is selected |
| 6 | Mapping re-applies to unmapped-yet fields | Toast shows "Applied 2 of 3 mappings from Matter" |
| 7 | Verify field values after re-mapping | **Expected**:<br/>- description: "My Custom Notes" (NOT overwritten)<br/>- estimatedhours: {new value from new Matter}<br/>- notes: {new value from new Matter} |

### Acceptance Criteria

✅ **PASS** if:
- User-modified description field retains "My Custom Notes"
- Toast shows "2 of 3" (acknowledges 1 was skipped)
- Other unmapped fields still receive new values
- Mapping respects user intent

❌ **FAIL** if:
- description was overwritten (shows Matter client instead of custom notes)
- Toast shows "3 of 3" (incorrectly suggests all were mapped)
- Other fields failed to update
- No acknowledgment of skipped field

### Implementation Note

Field modification tracking mechanism:
- **Approach**: Track fields touched by user via onChange events
- **Storage**: Store modified field list in component state
- **Logic**: During mapping, skip fields in modified list
- **Reset**: Clear tracking when regarding record changes

---

## Test Scenario 3: Type Compatibility Validation (SC-11)

**Objective**: Verify that type compatibility validation prevents creation of incompatible mapping rules (demonstrating Strict mode enforcement).

### Test Steps

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to FieldMappingAdmin control | Admin panel for creating/editing mapping rules loads |
| 2 | Select mapping profile: "Matter to Event - Test" | Profile shows existing rules |
| 3 | Click **Add Rule** button | New rule input row appears |
| 4 | Set Source Field | Matter field: `sprk_client` (Text type) |
| 5 | Set Target Field | Event field: `sprk_regardingmatter` (Lookup type) |
| 6 | Attempt to save rule | **Expected**: Validation error appears:<br/>"Text cannot map to Lookup in Strict mode. Valid targets for Text: Text, Memo" |
| 7 | Click **Save** button | Save is disabled/blocked until error is fixed |

### Acceptance Criteria

✅ **PASS** if:
- Type compatibility validation triggers
- Error message is clear and actionable
- Shows valid target types for source field type
- Save button blocked until corrected
- User can correct and save successfully (once compatible target selected)

❌ **FAIL** if:
- Incompatible rule is saved (type validation bypassed)
- Error message is unclear or missing
- No hint on how to fix (valid target types)
- Save proceeds despite validation error

### Supported Type Compatibility (Strict Mode)

| Source Type | Valid Target Types |
|-------------|-------------------|
| Text | Text, Memo |
| Memo | Text, Memo |
| Lookup | Lookup, Text |
| OptionSet | OptionSet, Text |
| Number | Number, Text |
| DateTime | DateTime, Text |
| Boolean | Boolean, Text |

---

## Test Scenario 4: Multiple Regarding Record Changes

**Objective**: Verify that changing the regarding record multiple times applies correct mappings each time without residual values.

### Prerequisites for This Scenario

- Two Matter records prepared with different field values:
  - **Matter A**: client="ACME Corp", budget=50000, subject="Matter A Subject"
  - **Matter B**: client="Beta Inc", budget=75000, subject="Matter B Subject"

### Test Steps

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Start with fresh Event record | Form is empty |
| 2 | Select **Matter A** in AssociationResolver | Mapped fields populate with Matter A values |
| 3 | Verify values | description="ACME Corp", estimatedhours=50000, notes="Matter A Subject" |
| 4 | Change entity type dropdown to **Project** (or Matter selection) | Entity changes, search resets |
| 5 | Search and select **Matter B** | Toast: "Applied 3 field mappings from Matter" |
| 6 | Verify new values | **Expected**:<br/>- description: "Beta Inc" (NOT "ACME Corp")<br/>- estimatedhours: 75000 (NOT 50000)<br/>- notes: "Matter B Subject" (NOT "Matter A Subject") |
| 7 | Clear regarding (select empty) | Clear button or entity type = None |
| 8 | Select **Matter A** again | Fields repopulate with Matter A values (idempotent) |

### Acceptance Criteria

✅ **PASS** if:
- Each Matter selection applies its own values correctly
- No residual values from previous selections
- Idempotent: Same selection produces same result
- Values fully replace previous values

❌ **FAIL** if:
- Previous Matter values remain (mixing of data)
- Toast shows inconsistent counts
- Re-selecting same Matter shows old cached values
- Fields partially update (some old, some new)

---

## Test Scenario 5: Cascading Mappings (SC-12)

**Objective**: Verify that cascading mappings execute correctly when one rule's source is populated by another rule's target.

### Test Setup

Create two rules in mapping profile:
- **Rule A**: sprk_matter.sprk_client → sprk_event.sprk_description
- **Rule B**: sprk_event.sprk_description → sprk_event.sprk_notes (intra-field cascading)
  - *Alternative*: Use Matter.sprk_subject (direct) for notes if cascading not yet implemented

### Test Steps

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Configure Matter with: client="ACME Corp" | Test data ready |
| 2 | Create new Event and select Matter | Field mapping executes |
| 3 | Verify Rule A applies | description="ACME Corp" |
| 4 | Verify Rule B applies (if implemented) | notes field also gets value from description or has expected value |
| 5 | Document execution order in test results | Two-pass or single-pass execution? |

### Acceptance Criteria

✅ **PASS** if:
- Cascading mappings execute in correct order
- All rules apply without conflict
- No infinite loops or partial application
- Execution completes in <1 second

❌ **FAIL** if:
- Rule B doesn't execute (cascading broken)
- Circular dependency causes hang
- Values incorrect due to execution order

---

## Test Scenario 6: Toast Notification Accuracy

**Objective**: Verify that toast notifications accurately report mapping results.

### Test Cases

| Case | Setup | Action | Expected Toast | PASS / FAIL |
|------|-------|--------|-----------------|-------------|
| 1 | Profile with 3 rules, all applicable | Select Matter | "Applied 3 field mappings from Matter" | |
| 2 | Profile with 3 rules, 1 type incompatible (skip) | Select Matter | "Applied 2 of 3 mappings from Matter" | |
| 3 | Profile with 3 rules, 2 user-modified (skip) | Reselect Matter | "Applied 1 of 3 mappings from Matter" | |
| 4 | No profile configured | Select Matter | "No mapping profile configured" (or no toast) | |
| 5 | Profile configured but no rules | Select Matter | "No rules in profile" or "0 field mappings applied" | |

### Acceptance Criteria

✅ **PASS** if:
- Toast message accurately reflects mapping count
- "Applied X" = count actually applied
- "Applied X of Y" = clear when some skipped
- Message provides user feedback without being overwhelming

---

## Test Scenario 7: Performance & User Experience

**Objective**: Verify that field mapping auto-application is fast and doesn't degrade form responsiveness.

### Test Measurements

| Metric | Threshold | Measured | Result |
|--------|-----------|----------|--------|
| Time to populate 3 fields | < 500ms | TBD | PASS / FAIL |
| Form interaction responsiveness | No lag | TBD | PASS / FAIL |
| Toast appearance | < 200ms after selection | TBD | PASS / FAIL |
| Multiple selections (5 times) | Consistent timing | TBD | PASS / FAIL |

### Test Steps

1. Open developer tools (F12) → Performance tab
2. Select Matter in AssociationResolver
3. Measure time from selection to field population complete
4. Repeat 5 times and verify consistency
5. Record results

### Acceptance Criteria

✅ **PASS** if:
- Field population completes in < 500ms
- Form remains responsive (no frozen UI)
- No memory leaks on repeated selections
- Consistent performance across 5 runs

---

## Test Scenario 8: Error Handling

**Objective**: Verify that errors during mapping are handled gracefully.

### Error Cases

| Error Scenario | Setup | Expected Behavior |
|---|---|---|
| Matter record deleted | Matter selected, then deleted externally | Toast: "Source record no longer available" |
| Insufficient permissions | User lacks read access to Matter | Toast: "Access denied to Matter" or graceful skip |
| Network timeout | Network disrupted during fetch | Toast: "Failed to retrieve mappings" + Retry button |
| Invalid mapping rule | Rule points to deleted field | Toast: "Rule X: Field not found" |
| Null/empty source value | Matter.sprk_client is null | Target field left empty (not mapped) |

### Test Steps

1. Configure mapping profile with rules
2. Manually delete source Matter via browser dev tools or backend
3. Attempt to select Matter in AssociationResolver
4. Observe error handling

### Acceptance Criteria

✅ **PASS** if:
- Error is caught and reported
- User sees actionable message
- Form doesn't crash
- User can dismiss and try again

❌ **FAIL** if:
- Unhandled exception shown to user
- No error feedback
- Form becomes non-functional
- Console errors logged

---

## Test Execution Checklist

### Pre-Test Setup
- [ ] Test Matter records created with correct field values
- [ ] Field Mapping Profile configured with 3+ rules
- [ ] Event form deployed with AssociationResolver control
- [ ] User logged into Dev environment
- [ ] Browser console cleared for error monitoring

### Scenario Execution
- [ ] Scenario 1 - Auto-Apply on Record Selection
- [ ] Scenario 2 - User-Modified Fields NOT Overwritten
- [ ] Scenario 3 - Type Compatibility Validation
- [ ] Scenario 4 - Multiple Regarding Record Changes
- [ ] Scenario 5 - Cascading Mappings (if applicable)
- [ ] Scenario 6 - Toast Notification Accuracy
- [ ] Scenario 7 - Performance & User Experience
- [ ] Scenario 8 - Error Handling

### Post-Test Validation
- [ ] All acceptance criteria verified
- [ ] No console errors or warnings
- [ ] Toast messages accurate and user-friendly
- [ ] Performance acceptable
- [ ] Test results documented

---

## Test Results Documentation

**Test Date**: {date}
**Tester**: {name}
**Environment**: Dev (https://spaarkedev1.crm.dynamics.com)
**Build Version**: {version-with-tasks-036-058}

### Scenario Results

| Scenario | Status | Notes |
|----------|--------|-------|
| 1. Auto-Apply | PASS / FAIL | {notes} |
| 2. User-Modified | PASS / FAIL | {notes} |
| 3. Type Validation | PASS / FAIL | {notes} |
| 4. Multiple Changes | PASS / FAIL | {notes} |
| 5. Cascading | PASS / FAIL / SKIP | {notes} |
| 6. Toast Accuracy | PASS / FAIL | {notes} |
| 7. Performance | PASS / FAIL | {notes} |
| 8. Error Handling | PASS / FAIL | {notes} |

### Issues Found

| ID | Severity | Description | Impact | Status |
|----|----------|-------------|--------|--------|
| BUG-001 | High / Medium / Low | {description} | {impact} | Open / Resolved |

### Sign-Off

- **Tester Name**: ___________________
- **Date**: ___________________
- **Overall Result**: ✅ PASS / ❌ FAIL
- **Ready for Production**: YES / NO
- **Notes**: ___________________

---

## Related Documentation

- **Spec**: `spec.md` - SC-08, SC-11, SC-12
- **Design**: `design.md` - Field Mapping Framework section
- **Task Completion**: Task 061 E2E test documentation
- **Tasks Complete**: 036 (Event Form Controls), 058 (BFF API)
- **Related Tests**: Task 060 (E2E Event Creation), Task 062 (E2E Refresh from Parent), Task 063 (E2E Update Related)

---

## Notes & Observations

### Key Testing Insights

1. **User Modification Tracking**: Critical for real-world usage where users partially override mappings
2. **Toast Clarity**: "Applied X of Y" format helps users understand what happened
3. **Type Compatibility**: Strict mode prevents invalid rules from being created
4. **Cascading Complexity**: Two-pass execution may be needed for complex scenarios

### Future Enhancements (Not in R1 Scope)

- Resolve mode type compatibility (Text can resolve to Lookup via record selection)
- Conditional mapping (if Field A = value, apply Rule B)
- Scheduled refresh (auto-sync at intervals)
- Bidirectional cascading

---

*Test scenario document created for Phase 6: Integration & Testing*
*Last Updated: 2026-02-01*
