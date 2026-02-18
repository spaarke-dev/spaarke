# Invoice Review Queue - Dataverse View Configuration

> **Created**: Task 003 (Foundation Phase)
> **Configured**: Task 047 (Integration Phase)
> **Last Updated**: 2026-02-11

## Overview

The **Invoice Review Queue** is a Dataverse filtered view that displays email attachment documents classified as potential invoices awaiting human review. This is the primary interface for reviewers in the Finance Intelligence pipeline MVP.

## View Details

| Property | Value |
|----------|-------|
| **View Name** | Invoice Review Queue |
| **Entity** | `sprk_document` |
| **View Type** | Public (accessible to Finance team roles) |
| **Created In** | Task 003 (Foundation Phase) |
| **Purpose** | Show classified invoice candidates awaiting review |

## Filter Criteria

The view uses the following filters to show only unreviewed invoice candidates:

```fetchxml
<filter type="and">
  <!-- Only email attachments -->
  <condition attribute="sprk_documenttype" operator="eq" value="100000007" />

  <!-- Only invoice candidates or unknown classifications -->
  <condition attribute="sprk_classification" operator="in">
    <value>100000000</value> <!-- InvoiceCandidate -->
    <value>100000002</value> <!-- Unknown -->
  </condition>

  <!-- Only unreviewed items -->
  <condition attribute="sprk_invoicereviewstatus" operator="eq" value="100000000" /> <!-- ToReview -->
</filter>
```

### Filter Logic Breakdown

1. **Document Type = Email Attachment** (`sprk_documenttype = 100000007`)
   - Only shows documents that are email attachments
   - Excludes .eml files and other document types

2. **Classification = InvoiceCandidate OR Unknown** (`sprk_classification IN (InvoiceCandidate, Unknown)`)
   - **InvoiceCandidate**: AI classification job determined this is likely an invoice
   - **Unknown**: AI classification confidence was too low to determine
   - Excludes documents classified as "NotInvoice" (don't need review)

3. **Review Status = ToReview** (`sprk_invoicereviewstatus = 100000000`)
   - Only shows documents that haven't been reviewed yet
   - Excludes confirmed invoices and rejected candidates

## View Columns

The view displays the following columns to help reviewers make decisions:

| Column | Field Name | Purpose |
|--------|------------|---------|
| **Document Name** | `sprk_name` | Attachment filename |
| **Classification** | `sprk_classification` | AI classification result (InvoiceCandidate/Unknown) |
| **Confidence** | `sprk_classificationconfidence` | AI confidence score (0-100%) |
| **Vendor Name Hint** | `sprk_invoicevendornamehint` | AI-extracted vendor name |
| **Invoice Number Hint** | `sprk_invoicenumberhint` | AI-extracted invoice number |
| **Total Amount Hint** | `sprk_invoicetotalhint` | AI-extracted total amount |
| **Matter Suggestion** | `sprk_mattersuggestedref` | Entity matching result (top candidate) |
| **Review Status** | `sprk_invoicereviewstatus` | Current review status |
| **Created On** | `createdon` | When attachment was processed |

### Sorting

- **Primary Sort**: `createdon` descending (newest first)
- Ensures reviewers see the most recently classified invoices at the top

## Reviewer Workflow

The review queue supports the following workflow:

### 1. View Pending Invoices

1. Navigate to **Documents** → **Invoice Review Queue** view
2. Review list of classified invoice candidates
3. Examine AI-provided hints (vendor, invoice number, total, matter suggestion)

### 2. Confirm Invoice (Happy Path)

When a document IS an invoice:

1. Open the document record
2. Verify/correct the following fields:
   - **Matter** (`sprk_matterid`) - Required
   - **Project** (`sprk_projectid`) - Optional
   - **Vendor Organization** (`sprk_vendororgid`) - Required
   - **Invoice Number** (if hint is incorrect)
   - **Invoice Date** (if hint is incorrect)
   - **Total Amount** (if hint is incorrect)

3. Call the **Confirm Invoice** endpoint:
   ```http
   POST /api/finance/invoice-review/confirm
   Content-Type: application/json

   {
     "documentId": "{guid}",
     "matterId": "{guid}",
     "projectId": "{guid|null}",
     "vendorOrgId": "{guid}"
   }
   ```

4. Result:
   - Creates `sprk_invoice` record with `sprk_status = ToReview`
   - Enqueues `InvoiceExtractionJob` to extract billing details
   - Document removed from review queue (`sprk_invoicereviewstatus` updated)

### 3. Reject Non-Invoice

When a document is NOT an invoice:

1. Call the **Reject Endpoint**:
   ```http
   POST /api/finance/invoice-review/reject
   Content-Type: application/json

   {
     "documentId": "{guid}"
   }
   ```

2. Result:
   - Sets `sprk_invoicereviewstatus = RejectedNotInvoice`
   - Records reviewer identity and timestamp
   - Document removed from review queue
   - **No invoice record created**
   - Document retained for audit (not deleted)

## Integration with Finance Intelligence Pipeline

```
Email Attachment
  ↓
AttachmentClassificationJobHandler (Task 011)
  ↓
Populates classification fields on sprk_document:
  - sprk_classification (InvoiceCandidate/NotInvoice/Unknown)
  - sprk_classificationconfidence (0-100%)
  - sprk_invoicevendornamehint, sprk_invoicenumberhint, sprk_invoicetotalhint
  - sprk_invoicereviewstatus = ToReview (if candidate/unknown)
  ↓
Document appears in Invoice Review Queue View
  ↓
Human Reviewer → Confirm or Reject
  ↓
If Confirmed:
  - Creates sprk_invoice record
  - Enqueues InvoiceExtractionJobHandler
  - Removes from review queue

If Rejected:
  - Updates sprk_invoicereviewstatus = RejectedNotInvoice
  - Removes from review queue
  - Retains document for audit
```

## Deployment Status

✅ **View Created**: Task 003 (Foundation Phase - Complete)
✅ **Configured**: Task 047 (Integration Phase - Complete)

### Deployment Checklist

- [x] View definition exists in solution
- [x] Filter criteria correctly applied (documenttype, classification, reviewstatus)
- [x] Columns configured for reviewer workflow
- [x] Sort order set (createdon descending)
- [x] View is public and accessible
- [ ] **TODO**: Add view to site map for easy navigation
- [ ] **TODO**: Configure security roles (Finance team access)
- [ ] **TODO**: Test with sample classified documents

## Access Control

The view should be accessible to the following roles:

- **Finance Manager** - Full access to review queue
- **Finance Analyst** - Full access to review queue
- **System Administrator** - Full access (default)

Restrict access from:
- **General users** - Should not see financial data
- **Matter stakeholders** - Use Matter form finance summary instead

## Usage Metrics (Post-Deployment)

After deployment, track these metrics to measure effectiveness:

- **Queue Size**: Number of documents awaiting review (target: < 50)
- **Review Time**: Average time from classification to review (target: < 24 hours)
- **Confirm Rate**: Percentage of candidates confirmed as invoices (target: > 80%)
- **Reject Rate**: Percentage of candidates rejected (target: < 20%)
- **Accuracy**: AI classification precision (InvoiceCandidate → Confirmed)

## Future Enhancements

Per ADR-011, a **PCF Dataset control** may replace this view in future releases, providing:

- Inline confirm/reject actions (no form navigation)
- Side-panel document preview
- Bulk review operations
- Real-time queue updates
- Filtering and search within queue

The Dataset control would use the same underlying filter logic but provide richer UX.

## Troubleshooting

### Queue is Empty

**Symptom**: No documents appear in the review queue

**Possible Causes**:
1. No email attachments classified yet (check AttachmentClassificationJobHandler logs)
2. Feature flag `AutoClassifyAttachments` is disabled (check FinanceOptions configuration)
3. All classified documents have been reviewed already
4. Filter criteria too restrictive

**Resolution**:
- Check job execution logs for classification jobs
- Verify `EmailProcessingOptions.AutoClassifyAttachments = true`
- Query `sprk_document` directly to see classification field values

### Documents Not Appearing After Classification

**Symptom**: Classification job runs successfully but documents don't appear in queue

**Possible Causes**:
1. Classification result was "NotInvoice" (excluded from queue)
2. Review status not set to "ToReview"
3. Wrong document type (not email attachment)

**Resolution**:
- Check `sprk_classification` field value on document
- Check `sprk_invoicereviewstatus` field value
- Check `sprk_documenttype` field value (should be 100000007)

### Reviewed Documents Still Showing

**Symptom**: Confirmed or rejected documents still appear in queue

**Possible Causes**:
1. Review status not updated after confirm/reject
2. Endpoint call failed
3. Caching issue in view

**Resolution**:
- Verify endpoint response (should be 200 or 202)
- Check `sprk_invoicereviewstatus` field on document (should no longer be ToReview)
- Refresh view in browser

## Related Tasks

- **Task 001**: Created `sprk_document` entity (Foundation)
- **Task 002**: Added classification fields to `sprk_document` (Foundation)
- **Task 003**: Created Invoice Review Queue view ✅ (Foundation)
- **Task 011**: Implemented `AttachmentClassificationJobHandler` (AI Services)
- **Task 014**: Implemented Invoice Review Confirm Endpoint (Endpoints)
- **Task 015**: Implemented Invoice Review Reject Endpoint (Endpoints)
- **Task 047**: Configure Invoice Review Queue View ✅ (Integration - This Task)

## References

- **Spec**: `projects/financial-intelligence-module-r1/spec.md` (FR-04, FR-05, FR-14)
- **ADR-011**: Dataset PCF pattern for future review queue upgrade
- **ADR-022**: Unmanaged solutions only

---

*Last Updated: 2026-02-11*
