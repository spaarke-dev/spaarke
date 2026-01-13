# Email-to-Document Automation R2

> **Status**: Planning
> **Created**: 2026-01-13
> **Type**: API Enhancement + PCF

---

## Quick Links

- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Design Specification](spec.md)
- [R1 Architecture Reference](../../docs/guides/EMAIL-TO-DOCUMENT-ARCHITECTURE.md)

---

## Overview

R2 enhancements for Email-to-Document Automation: fix user access to app-uploaded files, extract attachments as child documents, enable background AI analysis, and add UI for processing existing emails.

---

## Problem Statement

R1 email processing successfully archives emails as .eml files in SharePoint Embedded (SPE). However, several gaps remain:

1. **Download Access**: Users cannot download .eml files uploaded via app-only auth because SPE permissions require user context
2. **Attachment Visibility**: Email attachments are embedded in .eml but not exposed as separate searchable/analyzable documents
3. **AI Analysis**: App-uploaded documents cannot use `AnalysisOrchestrationService` (requires OBO auth via HttpContext)
4. **Manual Processing**: No UI to process existing emails or sent emails that weren't auto-archived

---

## Proposed Solution

Five-phase implementation addressing each gap:

1. **Phase 1**: API-proxied download endpoint with app-only auth, allowing users to download .eml files through the BFF
2. **Phase 2**: Attachment extraction service that uploads meaningful attachments as child Documents with parent-child relationship
3. **Phase 3**: AppOnlyAnalysisService for background AI analysis without user context
4. **Phase 4**: Email Analysis Playbook combining email + attachments in single AI call
5. **Phase 5**: Ribbon toolbar buttons for processing existing/sent emails

---

## Scope

### In Scope

- API-proxied download endpoint (`GET /api/v1/documents/{id}/download`)
- Attachment extraction with filtering (signature logos, tracking pixels, calendar files)
- AppOnlyAnalysisService for background AI analysis
- Email Analysis Playbook (extract-combine-analyze approach)
- Ribbon toolbar for existing/sent emails

### Out of Scope

- New monitoring dashboards
- Explicit R1 rework (unless refinement needed for R2)
- Changes to Server-Side Sync configuration
- New custom pages or PCF controls (ribbon buttons only)

---

## Graduation Criteria

- [ ] Users can download .eml files uploaded by email processing via the download endpoint
- [ ] Attachments extracted and uploaded as child Documents with `sprk_ParentDocumentLookup` relationship
- [ ] AI analysis works for app-uploaded documents via AppOnlyAnalysisService
- [ ] Email analysis combines email + attachments (Email entity AI fields populated)
- [ ] Ribbon buttons work for existing/sent emails (manual test from email form)
- [ ] All metrics meet NFR targets (P95 < 2s download, >99% extraction, >95% AI analysis)
- [ ] No regression in R1 functionality

---

## Dependencies

### Prerequisites
- Email-to-Document R1 complete (PR #104 merged)
- Existing email processing pipeline operational

### External Dependencies
- Playbook Module: Email Analysis Playbook requires playbook entity coordination

---

*Last Updated: 2026-01-13*
