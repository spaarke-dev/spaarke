# Step 2.5 clarification questions — worked examples

## Contents

- Worked example per gap type (the seven types in SKILL.md Step 2.5)
- A full sample question set as presented to the owner

Illustrative only. The rules and the question format are in `SKILL.md` Step 2.5; never copy these questions into a
real interview — every question must come from a gap in the design being processed.

## Worked example per gap type

1. **Undefined terms**
   - Gap: design says "handle large files"
   - Question: "What file size is 'large'? >10MB? >100MB? This determines if we need chunked uploads or streaming."

2. **Implicit assumptions**
   - Gap: design assumes users are authenticated
   - Question: "Should anonymous users see the document list (read-only)? Currently assuming authenticated-only access."

3. **Unspecified behavior**
   - Gap: "Export to PDF" mentioned but no details
   - Question: "When exporting to PDF, should it include comments/annotations? This affects the export library choice."

4. **Missing error handling**
   - Gap: document upload flow, no failure handling
   - Question: "If upload fails mid-stream (network error), should we: [A] Auto-retry 3x, [B] Save partial and resume,
     [C] Fail and delete?"

5. **Conflicting requirements**
   - Gap: "Real-time sync" vs "Offline support"
   - Question: "Real-time sync conflicts with offline-first. Priority? This determines the sync architecture."

6. **Scope boundary unclear**
   - Gap: design mentions "mobile users" but scope is unclear
   - Question: "Is mobile-responsive UI in scope, or desktop-only? This affects component library choices."

7. **Missing quantitative requirements**
   - Gap: no concurrent user expectations
   - Question: "Expected concurrent users? <100 (simple), <1000 (caching needed), >1000 (requires scaling strategy)?"

## Sample question set as presented

```
🎯 Targeted Clarification Questions

Based on gaps in the design document, I need your input on these specific items:

🔴 BLOCKING (need answers to proceed):

1. 📍 Context: "The design mentions 'batch processing for large document sets'"
   ❓ Question: What defines a 'large' set? 10 docs? 100? 1000?
   💡 Impact: Determines if we need background job queuing or can process inline.
   🔘 Options: [A] <50 inline [B] <500 queue [C] >500 chunked batches

2. 📍 Context: "Users can share documents with external parties"
   ❓ Question: External = any email, or only pre-approved domains?
   💡 Impact: Affects auth flow and security review requirements.

🟡 IMPORTANT (proceeding with assumptions if not answered):

3. 📍 Context: "Support for common document formats"
   ❓ Question: Which formats exactly? PDF, DOCX, XLSX, PPTX? Images?
   💡 Impact: Determines preview/conversion libraries needed.
   ⚠️ Assuming: PDF, DOCX, XLSX, PPTX only

Please answer the BLOCKING questions. For IMPORTANT, reply with answers or 'ok' to accept assumptions.
```
