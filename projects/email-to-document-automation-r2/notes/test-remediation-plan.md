# R2 Test Remediation Plan

> **Created**: 2026-01-16
> **Status**: In Progress
> **Source**: User Acceptance Testing Feedback

---

## Test Results Summary

| Category | Passed | Failed | Total |
|----------|--------|--------|-------|
| Download Endpoint (DL) | 3 | 4 | 7 |
| Attachment Processing (ATT) | 4 | 0 | 4 |
| AppOnly Analysis (AOS) | 0 | 5 | 5 |
| Email Analysis Playbook (EAP) | 0 | 2 | 2 |
| Toolbar/Ribbon (TC) | 0 | 12 | 12 |

---

## Priority 1 - Critical (Blocks R2 Completion)

### Issue 1: AI Summary Not Saved to Documents

**Failures**: AOS-01, AOS-02, EAP-01, EAP-02
**Impact**: Core R2 functionality broken - no AI profile fields populated

**Root Cause Investigation**:
- [ ] Check if `AppOnlyAnalysisService` is being called after document upload
- [ ] Verify Dataverse field mapping in analysis result storage
- [ ] Check if Document entity has required AI fields (`sprk_summary`, `sprk_tldr`, `sprk_keywords`, `sprk_entities`)
- [ ] Verify API logs for analysis execution errors

**Fix Tasks**:
- [ ] **1.1**: Debug `AppOnlyAnalysisService.AnalyzeDocumentAsync()` - verify it's invoked
- [ ] **1.2**: Verify Dataverse Document entity has AI profile fields
- [ ] **1.3**: Fix field mapping in `AnalysisResultStorageService` (or equivalent)
- [ ] **1.4**: Add integration test for end-to-end AI analysis storage
- [ ] **1.5**: Retest AOS-01, AOS-02

---

### Issue 2: .eml File Missing Attachments

**Failures**: DL-04, EAP-02
**Impact**: Archived emails don't contain their attachments - data loss

**Root Cause Investigation**:
- [ ] Check `EmailToEmlConverter` - does it include attachments?
- [ ] Verify MIME structure of generated .eml files
- [ ] Test with MimeKit to confirm attachment embedding

**Fix Tasks**:
- [ ] **2.1**: Review `EmailToEmlConverter.ConvertToEml()` method
- [ ] **2.2**: Ensure all inline and attachment parts are preserved
- [ ] **2.3**: Add unit test validating attachment presence in .eml output
- [ ] **2.4**: Retest DL-04

---

### Issue 3: Email Toolbar Buttons Not Showing

**Failures**: TC-01 through TC-11
**Impact**: Users cannot manually archive emails - entire Phase 5 non-functional

**Root Cause Investigation**:
- [ ] Verify EmailRibbons solution v1.1.0 is deployed
- [ ] Check `sprk_documentprocessing` field - is it on the Email form?
- [ ] Verify EnableRule/DisplayRule JavaScript functions
- [ ] Check browser console for JavaScript errors

**Fix Tasks**:
- [ ] **3.1**: Verify EmailRibbons solution deployment status
- [ ] **3.2**: Add `sprk_documentprocessing` field to Email form (if missing)
- [ ] **3.3**: Debug `canSaveToDocument()` and `canArchiveEmail()` functions
- [ ] **3.4**: Verify web resource `sprk_emailactions.js` is published
- [ ] **3.5**: Test button visibility in clean browser session
- [ ] **3.6**: Retest TC-01, TC-02

---

## Priority 2 - Important (Should Fix Before Release)

### Issue 4: Parent Document Should Be Lookup

**Failure**: DL-05
**Impact**: Can't use subgrid to show related documents

**Current State**: `sprk_parentdocument` is text field (stores GUID as string)
**Desired State**: `sprk_ParentDocumentLookup` as lookup to `sprk_document`

**Fix Tasks**:
- [ ] **4.1**: Create new lookup field `sprk_ParentDocumentLookup` on Document entity
- [ ] **4.2**: Update attachment processing to populate lookup instead of text
- [ ] **4.3**: Migrate existing data (if any)
- [ ] **4.4**: Add subgrid to Document form showing child documents
- [ ] **4.5**: Deprecate text field `sprk_parentdocument`

---

### Issue 5: Version Number Not Meaningful

**Failure**: DL-06
**Impact**: Version field doesn't reflect document history

**Investigation**:
- [ ] What is `sprk_version` currently populated with?
- [ ] Should it be auto-incremented on updates?
- [ ] Or should it match SPE file version?

**Fix Tasks**:
- [ ] **5.1**: Define version numbering strategy
- [ ] **5.2**: Implement version increment logic on document update
- [ ] **5.3**: Display version in document list/form

---

### Issue 6: Large Document Token Limit

**Failure**: AOS-03
**Impact**: 7MB+ documents fail AI analysis

**Fix Tasks**:
- [ ] **6.1**: Check current Azure OpenAI subscription limits
- [ ] **6.2**: Request quota increase if needed
- [ ] **6.3**: Implement chunking for large documents
- [ ] **6.4**: Add graceful degradation message for oversized docs

---

### Issue 7: Long-Running Analysis UX

**Failure**: AOS-04
**Impact**: Users don't know analysis is still running for large docs

**Requested Behavior**:
> For >30 second operations: popup "Your large document AI analysis is taking a while. We'll notify you when completed" with Close button

**Fix Tasks**:
- [ ] **7.1**: Add timeout detection in PCF (30 second threshold)
- [ ] **7.2**: Show notification toast/popup after timeout
- [ ] **7.3**: Implement Dataverse toast notification on completion
- [ ] **7.4**: Allow user to close upload pane while analysis continues

---

### Issue 8: Analysis Should Save to Analysis Entity

**Failure**: AOS-05
**Impact**: Analysis results not tracked in Analysis entity

**Fix Tasks**:
- [ ] **8.1**: Create Analysis record when AI analysis runs on document upload
- [ ] **8.2**: Link Analysis to Document via lookup
- [ ] **8.3**: Store full analysis output in Analysis entity

---

### Issue 9: Recommend 'Regarding' Matter

**Failure**: TC-12
**Impact**: Users must manually find related Matter

**Fix Tasks**:
- [ ] **9.1**: Analyze email recipients/subject for Matter matching
- [ ] **9.2**: Use AI to suggest related Matter
- [ ] **9.3**: Present suggestion in Archive dialog

---

## Priority 3 - Minor (Nice to Have)

### Issue 10: Remove Custom Refresh Button

**Failure**: DL-07
**Impact**: UI clutter

**Fix Tasks**:
- [ ] **10.1**: Identify which form has custom Refresh button
- [ ] **10.2**: Remove from form customization
- [ ] **10.3**: Publish changes

---

## New Feature Requests

### Feature: Embed AI Monitoring in Spaarke Admin

**Request**: Embed Application Insights AI dashboard in Spaarke Admin app

**Options**:
1. **Embed Azure Dashboard** - Use Azure Portal embedding (requires AAD auth)
2. **Custom PCF Control** - Build PCF that queries App Insights API
3. **Power BI Embed** - Create Power BI report connected to App Insights, embed in model-driven app
4. **iFrame Web Resource** - Simple embed if dashboard is publicly accessible (not recommended for security)

**Recommended Approach**: Power BI Embed
- Create Power BI report with App Insights data source
- Embed in Spaarke Admin using Power BI embedded control
- Maintains security context

**Tasks**:
- [ ] **F1.1**: Create Power BI report for AI metrics
- [ ] **F1.2**: Configure Power BI workspace for embedding
- [ ] **F1.3**: Add Power BI embedded control to Admin app
- [ ] **F1.4**: Test dashboard access and refresh

---

## Remediation Sequence

### Phase A: Critical Fixes (Do First)

| Order | Issue | Est. Effort | Assignee |
|-------|-------|-------------|----------|
| A1 | Issue 3: Ribbon buttons | 2-4 hours | |
| A2 | Issue 1: AI summary storage | 4-8 hours | |
| A3 | Issue 2: .eml attachments | 2-4 hours | |

### Phase B: Important Fixes

| Order | Issue | Est. Effort | Assignee |
|-------|-------|-------------|----------|
| B1 | Issue 4: Parent lookup | 2-4 hours | |
| B2 | Issue 8: Analysis entity | 2-4 hours | |
| B3 | Issue 7: Long-running UX | 4-8 hours | |
| B4 | Issue 6: Token limits | 1-2 hours | |
| B5 | Issue 5: Version number | 2-4 hours | |
| B6 | Issue 9: Regarding suggestion | 4-8 hours | |

### Phase C: Minor & Features

| Order | Issue | Est. Effort | Assignee |
|-------|-------|-------------|----------|
| C1 | Issue 10: Remove Refresh | 30 min | |
| C2 | Feature: AI Dashboard embed | 8-16 hours | |

---

## Decision Required

Before proceeding, need owner input on:

1. **Scope**: Should all issues be fixed in R2, or defer some to R3?
2. **Priority**: Confirm Phase A order is correct
3. **Dashboard**: Confirm Power BI approach for AI monitoring embed
4. **Timeline**: Target completion date for critical fixes

---

*Created from UAT feedback 2026-01-16*
