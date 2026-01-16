# Ribbon Button Manual Testing Checklist

> **Task**: 043 - Manual Testing Checklist for Ribbon Buttons
> **Date**: 2026-01-15
> **Environment**: Dataverse Dev (spaarkedev1.crm.dynamics.com)
> **Feature Requirements**: FR-14 (Archive Received Emails), FR-15 (Archive Sent Emails)

---

## Pre-Test Setup

- [ ] Ensure EmailRibbons solution v1.1.0+ is deployed to target environment
- [ ] Ensure BFF API is running and accessible
- [ ] Clear browser cache (Ctrl+Shift+Delete)
- [ ] Open Dataverse in new browser session

**Environment URLs:**
- Dataverse: https://spaarkedev1.crm.dynamics.com
- BFF API: https://spe-api-dev-67e2xz.azurewebsites.net

---

## Test Scenarios

### TC-01: Received Email - Button Visible

**Objective**: Verify "Archive Email" button appears on received (incoming) email

| Step | Action | Expected Result | Status |
|------|--------|-----------------|--------|
| 1 | Navigate to Emails in Dataverse | Email list loads | |
| 2 | Open a completed received email (directioncode = Incoming) | Email form opens | |
| 3 | Look for "Archive Email" button in command bar | Button is visible | |
| 4 | Verify button is in Export Data section | Button appears near "Run Report" | |

**Notes**:
- Email must be in "Completed" status (statecode = 1)
- Email must NOT already have an associated sprk_document record

---

### TC-02: Received Email - Archive Processing

**Objective**: Verify clicking button triggers email-to-document processing

| Step | Action | Expected Result | Status |
|------|--------|-----------------|--------|
| 1 | Open a completed received email that is NOT archived | Form opens | |
| 2 | Click "Archive Email" button | Progress indicator appears | |
| 3 | Wait for processing | Progress shows "Converting email to document..." | |
| 4 | Observe completion | Success dialog appears | |
| 5 | Click "Open Document" or "Close" in dialog | Dialog closes | |
| 6 | Verify sprk_document record created | Navigate to Documents, find new record | |

**Expected Dialog Text**: "Email archived successfully! The document has been created."

**Notes**:
- Processing typically takes 5-15 seconds
- Document should have sprk_email lookup populated

---

### TC-03: Received Email - Post-Archive Button Hidden

**Objective**: Verify button is hidden after email is archived

| Step | Action | Expected Result | Status |
|------|--------|-----------------|--------|
| 1 | After TC-02 completes, refresh the email form (F5) | Form reloads | |
| 2 | Look for "Archive Email" button | Button is NOT visible | |
| 3 | Verify sprk_document lookup on email | Shows linked document | |

**Notes**:
- DisplayRule `canArchiveEmail` checks for existing sprk_document
- Session cache may need to clear (close and reopen form)

---

### TC-04: Sent Email - Button Visible

**Objective**: Verify "Archive Email" button appears on sent (outgoing) email

| Step | Action | Expected Result | Status |
|------|--------|-----------------|--------|
| 1 | Navigate to Emails in Dataverse | Email list loads | |
| 2 | Filter for sent emails (Direction = Outgoing) | List shows sent emails | |
| 3 | Open a completed sent email | Email form opens | |
| 4 | Look for "Archive Email" button | Button is visible | |

**Notes**:
- Same button works for both directions (implementation is direction-agnostic)
- Email must be completed and not already archived

---

### TC-05: Sent Email - Archive Processing

**Objective**: Verify processing works for sent emails

| Step | Action | Expected Result | Status |
|------|--------|-----------------|--------|
| 1 | Open a completed sent email that is NOT archived | Form opens | |
| 2 | Click "Archive Email" button | Progress indicator appears | |
| 3 | Wait for processing to complete | Success dialog appears | |
| 4 | Verify document created | Check Documents entity | |

**Notes**:
- Same workflow as received emails
- Document should preserve email direction metadata

---

### TC-06: Already Archived Email - Button Hidden

**Objective**: Verify button is hidden for previously archived emails

| Step | Action | Expected Result | Status |
|------|--------|-----------------|--------|
| 1 | Find an email that was previously archived | Email with sprk_document | |
| 2 | Open the email form | Form opens | |
| 3 | Look for "Archive Email" button | Button is NOT visible | |

**Notes**:
- Prevents duplicate archiving
- canArchiveEmail() checks sprk_document existence

---

### TC-07: Open/Draft Email - Button Disabled

**Objective**: Verify button is disabled for non-completed emails

| Step | Action | Expected Result | Status |
|------|--------|-----------------|--------|
| 1 | Find or create an email with status "Open" | Draft email | |
| 2 | Open the email form | Form opens | |
| 3 | Look for "Archive Email" button | Button is visible but DISABLED (grayed out) | |

**Notes**:
- EnableRule `canSaveToDocument` checks statecode === 1
- Button appears but cannot be clicked

---

### TC-08: Error Handling - Network Error

**Objective**: Verify appropriate error message on network failure

| Step | Action | Expected Result | Status |
|------|--------|-----------------|--------|
| 1 | Open browser DevTools (F12) | DevTools opens | |
| 2 | Go to Network tab, enable "Offline" mode | Network disabled | |
| 3 | Open a completed, unarchived email | Form opens (may use cache) | |
| 4 | Click "Archive Email" button | Progress appears then error | |
| 5 | Observe error dialog | Shows "Unable to archive email" message | |
| 6 | Disable offline mode in DevTools | Network restored | |

**Expected Error**: Generic network error message with retry suggestion

---

### TC-09: Error Handling - Already Processed (409 Conflict)

**Objective**: Verify conflict error when email was archived in another session

| Step | Action | Expected Result | Status |
|------|--------|-----------------|--------|
| 1 | Open same email in two browser tabs | Both forms open | |
| 2 | In Tab 1, click "Archive Email" and complete | Document created | |
| 3 | In Tab 2 (without refresh), click "Archive Email" | Error dialog appears | |
| 4 | Verify error message | Shows "Email has already been archived" | |

**Notes**:
- 409 status code from API triggers specific error handling
- Suggests refreshing the form

---

### TC-10: Success Dialog - Open Document Action

**Objective**: Verify "Open Document" button in success dialog works

| Step | Action | Expected Result | Status |
|------|--------|-----------------|--------|
| 1 | Complete archive process for an email | Success dialog appears | |
| 2 | Click "Open Document" button | Dialog closes | |
| 3 | Observe navigation | New tab/window opens to document record | |

**Notes**:
- Uses Xrm.Navigation.openForm with document ID
- Should open in new window

---

## Test Summary

| Test Case | Description | Result | Tester | Date |
|-----------|-------------|--------|--------|------|
| TC-01 | Received Email - Button Visible | | | |
| TC-02 | Received Email - Archive Processing | | | |
| TC-03 | Received Email - Post-Archive Button Hidden | | | |
| TC-04 | Sent Email - Button Visible | | | |
| TC-05 | Sent Email - Archive Processing | | | |
| TC-06 | Already Archived Email - Button Hidden | | | |
| TC-07 | Open/Draft Email - Button Disabled | | | |
| TC-08 | Error Handling - Network Error | | | |
| TC-09 | Error Handling - Already Processed | | | |
| TC-10 | Success Dialog - Open Document Action | | | |

**Overall Result**: ___ / 10 Passed

---

## Issues Found

| Issue # | Test Case | Description | Severity | Status |
|---------|-----------|-------------|----------|--------|
| | | | | |

---

## Feature Requirement Verification

| Requirement | Description | Verified By | Status |
|-------------|-------------|-------------|--------|
| FR-14 | Archive received emails via ribbon button | TC-01, TC-02, TC-03 | |
| FR-15 | Archive sent emails via ribbon button | TC-04, TC-05 | |

---

## Sign-Off

- [ ] All test cases executed
- [ ] All P1/P2 issues resolved
- [ ] FR-14 and FR-15 verified as working
- [ ] Ready for Phase 5 deployment (Task 049)

**Tested By**: _______________
**Date**: _______________
**Environment Version**: EmailRibbons v1.1.0
