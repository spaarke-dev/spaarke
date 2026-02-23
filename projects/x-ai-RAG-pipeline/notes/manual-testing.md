# PCF to RAG Manual Testing Results

**Task**: 051 - Manual UI testing for PCF to RAG flow
**Status**: PENDING MANUAL TESTING
**Date**: ________

---

## Prerequisites

- [ ] PCF control deployed to Dataverse dev environment
- [ ] API deployed with RAG endpoint enabled
- [ ] Azure AI Search index configured
- [ ] Test documents available (PDF, DOCX)

## Test Environment

| Component | URL/Details |
|-----------|-------------|
| Dataverse Org | https://spaarkedev1.crm.dynamics.com |
| BFF API | https://spe-api-dev-67e2xz.azurewebsites.net |
| AI Search | https://spaarke-search-dev.search.windows.net |
| Test Form | ________ |

---

## Test Cases

### TC-001: Small PDF Upload

**Steps**:
1. Navigate to form with UniversalQuickCreate control
2. Upload small PDF (<1MB)
3. Check browser console for RAG indexing log

**Expected**:
- Upload succeeds
- Console shows: "RAG indexing enqueued"
- No errors in console

**Result**: [ ] Pass / [ ] Fail

**Console Output**:
```
(paste console logs here)
```

**Notes**:

---

### TC-002: Medium DOCX Upload

**Steps**:
1. Navigate to form with UniversalQuickCreate control
2. Upload medium DOCX (1-5MB)
3. Check browser console for RAG indexing log

**Expected**:
- Upload succeeds
- Console shows: "RAG indexing enqueued"
- No errors in console

**Result**: [ ] Pass / [ ] Fail

**Notes**:

---

### TC-003: RAG Search Verification

**Steps**:
1. Wait 30 seconds after upload for indexing
2. Test search via API:
   ```bash
   curl -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/rag/search \
     -H "Authorization: Bearer <token>" \
     -H "Content-Type: application/json" \
     -d '{"query": "<text from uploaded document>", "top": 5}'
   ```

**Expected**:
- Search returns results
- Uploaded document appears in results

**Result**: [ ] Pass / [ ] Fail

**Response**:
```json
(paste response here)
```

---

### TC-004: RAG Endpoint Unavailable (Resilience)

**Steps**:
1. Temporarily disable API RAG endpoint (or use invalid URL)
2. Upload a document
3. Verify upload still succeeds

**Expected**:
- Upload completes successfully
- Console shows warning about RAG indexing failure
- No error shown to user

**Result**: [ ] Pass / [ ] Fail

**Notes**:

---

## Summary

| Test Case | Result | Notes |
|-----------|--------|-------|
| TC-001: Small PDF | [ ] Pass / [ ] Fail | |
| TC-002: Medium DOCX | [ ] Pass / [ ] Fail | |
| TC-003: Search Verification | [ ] Pass / [ ] Fail | |
| TC-004: Resilience | [ ] Pass / [ ] Fail | |

**Overall Result**: [ ] PASS / [ ] FAIL

**Tester**: ________
**Date Completed**: ________

---

## Issues Found

(List any issues discovered during testing)

1.
2.
3.

---

*Template created by Claude Code for ai-RAG-pipeline project*
