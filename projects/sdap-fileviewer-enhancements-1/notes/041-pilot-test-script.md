# Task 041 - Pilot Validation Test Script

## Date: December 4, 2025

## Status: Ready for Pilot Sessions

This document provides the test script and feedback templates for pilot validation with stakeholders.

---

## Pilot Requirements

### Participant Criteria
- **Count:** 2-5 users
- **Prerequisite:** Microsoft Office desktop installed (Word, Excel, PowerPoint)
- **Access:** Test Dataverse environment
- **Browsers:** Edge (primary), Chrome (secondary)

### Session Details
- **Duration:** 30 minutes per user
- **Format:** Screen share (remote) or in-person
- **Recording:** With permission only

---

## Pre-Session Setup

### For Facilitator
1. Prepare test documents (Word, Excel, PowerPoint) in test environment
2. Verify test environment is accessible
3. Have backup documents ready
4. Prepare feedback form
5. Test recording setup (if applicable)

### For Participant
1. Verify Office desktop is installed and updated
2. Log into test Power Apps environment
3. Close unnecessary applications
4. Enable screen sharing

---

## Test Script

### Introduction (2 minutes)

> "Thank you for participating in this pilot validation. Today we're testing a new feature that lets you open documents directly in your desktop Office applications from Dataverse.
>
> I'll ask you to perform a few tasks while I observe. Please think aloud - tell me what you expect, what you see, and any confusion.
>
> There are no wrong answers. We're testing the software, not you."

---

### Task 1: View Document (5 minutes)

**Scenario:** Open a Word document in the FileViewer

**Steps:**
1. Navigate to: `[Test Environment URL]`
2. Open any sprk_document record with a Word file

**Observe:**
- [ ] Loading indicator appears
- [ ] Preview loads within 10 seconds
- [ ] Document is readable in preview

**Questions:**
- "What did you expect to see when opening this record?"
- "Is the loading experience acceptable?"
- "Can you read the document preview clearly?"

---

### Task 2: Edit in Desktop - Word (8 minutes)

**Scenario:** Open the document in Word desktop application

**Steps:**
1. Look at the FileViewer toolbar
2. Find and click the "Edit in Desktop" button
3. Wait for Word to open

**Observe:**
- [ ] User finds Edit button easily
- [ ] Button shows loading state when clicked
- [ ] Word opens with correct document
- [ ] Document is editable

**Questions:**
- "How would you rate finding the Edit button (1-5)?"
- "Did Word open as expected?"
- "Is the document ready for editing?"

**Follow-up Task:**
1. Make a small change to the document
2. Save the document
3. Return to Dataverse

---

### Task 3: Edit in Desktop - Excel (5 minutes)

**Scenario:** Open an Excel spreadsheet in desktop

**Steps:**
1. Navigate to a record with an Excel file
2. Click "Edit in Desktop"
3. Verify Excel opens

**Observe:**
- [ ] Excel opens correctly
- [ ] Spreadsheet data visible
- [ ] User can edit

---

### Task 4: Edit in Desktop - PowerPoint (5 minutes)

**Scenario:** Open a PowerPoint presentation in desktop

**Steps:**
1. Navigate to a record with a PowerPoint file
2. Click "Edit in Desktop"
3. Verify PowerPoint opens

**Observe:**
- [ ] PowerPoint opens correctly
- [ ] Slides visible
- [ ] User can edit

---

### Task 5: Unsupported File Type (3 minutes)

**Scenario:** Open a PDF document

**Steps:**
1. Navigate to a record with a PDF file
2. Look for Edit button

**Observe:**
- [ ] Edit button is disabled or hidden
- [ ] User understands why (if they ask)

**Questions:**
- "What do you expect to happen with a PDF?"
- "Is it clear that desktop editing isn't available?"

---

### Wrap-up Questions (5 minutes)

1. "Overall, how would you rate this feature (1-5)?"
2. "Would you use this feature in your daily work?"
3. "What would make this feature better?"
4. "Did anything confuse you?"
5. "Any other feedback?"

---

## Feedback Template

Copy and fill out for each participant:

```markdown
## Participant Feedback

**Participant ID:** P[X]
**Date:** YYYY-MM-DD
**Session Duration:** XX minutes
**Browser:** [Edge/Chrome/Firefox]
**Office Version:** [Office 365/2021/2019]

### Task Completion

| Task | Completed | Time | Issues |
|------|-----------|------|--------|
| View Document | Yes/No | Xs | |
| Edit Word | Yes/No | Xs | |
| Edit Excel | Yes/No | Xs | |
| Edit PowerPoint | Yes/No | Xs | |
| Unsupported File | Yes/No | - | |

### Observations

**Loading Experience:**
- [ ] Fast and smooth
- [ ] Acceptable
- [ ] Slow or confusing
- Notes:

**Edit Button Discovery:**
- [ ] Found immediately
- [ ] Found after looking
- [ ] Needed help
- Notes:

**Desktop App Opening:**
- [ ] Opened quickly
- [ ] Opened slowly
- [ ] Failed to open
- Notes:

### Ratings (1-5, 5 = best)

| Aspect | Rating |
|--------|--------|
| Overall experience | /5 |
| Ease of finding Edit button | /5 |
| Speed of desktop app opening | /5 |
| Clarity of loading states | /5 |

### Would Use Feature?
- [ ] Yes, definitely
- [ ] Yes, probably
- [ ] Maybe
- [ ] Probably not
- [ ] Definitely not

### Issues Encountered
1.
2.
3.

### Suggestions
1.
2.

### Verbatim Quotes
> ""
> ""
```

---

## Go/No-Go Decision Template

After all pilot sessions, complete:

```markdown
## Go/No-Go Decision

**Date:** YYYY-MM-DD
**Pilots Completed:** X/X

### Summary

| Metric | Result |
|--------|--------|
| Average Rating | X/5 |
| Would Use | X/X users |
| Critical Issues | X |
| Blockers | X |

### Critical Issues

| Issue | Impact | Resolution |
|-------|--------|------------|
| | | |

### Decision

- [ ] **GO** - Deploy to production
- [ ] **GO with fixes** - Fix issues, then deploy
- [ ] **NO-GO** - Do not deploy, requires significant work

### Rationale

[Explain decision]

### Required Fixes (if applicable)

1.
2.

### Sign-off

- [ ] Product Owner
- [ ] Technical Lead
- [ ] QA Lead
```

---

## Sample Test Documents

Ensure these exist in test environment:

| File Type | Name | Size | Notes |
|-----------|------|------|-------|
| Word | TestDocument.docx | ~50KB | Simple document with text |
| Excel | TestSpreadsheet.xlsx | ~30KB | Spreadsheet with data |
| PowerPoint | TestPresentation.pptx | ~100KB | 3-5 slides |
| PDF | TestPDF.pdf | ~100KB | For unsupported file test |

---

## Pilot Schedule

| Date | Time | Participant | Status |
|------|------|-------------|--------|
| TBD | TBD | P1 | Scheduled |
| TBD | TBD | P2 | Scheduled |
| TBD | TBD | P3 | Scheduled |
| TBD | TBD | P4 (optional) | - |
| TBD | TBD | P5 (optional) | - |
