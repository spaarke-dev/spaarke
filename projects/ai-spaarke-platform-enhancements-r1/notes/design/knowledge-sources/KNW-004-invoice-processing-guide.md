# KNW-004 — Invoice Processing Guide

> **External ID**: KNW-004
> **Content Type**: Reference
> **Tenant**: system
> **Created**: 2026-02-23
> **Task**: AIPL-032

---

## Overview

This guide documents the standard validation rules, exception handling procedures, and approval workflows for accounts payable (AP) invoice processing. It is designed to support AI-assisted invoice review by providing the reference framework for validating invoice data, identifying discrepancies, and determining appropriate routing.

---

## Part 1: Invoice Types and Components

### 1.1 Invoice Types

| Invoice Type | Description | Typical Trigger |
|---|---|---|
| Standard Invoice | Single-period bill for goods or services | Delivery or service completion |
| Recurring Invoice | Periodic charge for ongoing subscription or service | Monthly/quarterly billing cycle |
| Progress Billing | Partial invoice against a project milestone | Contract milestone completion |
| Retainage Invoice | Final billing releasing held-back amounts | Project completion and acceptance |
| Credit Memo | Negative invoice reducing outstanding balance | Return, overbilling, or dispute resolution |
| Pro Forma Invoice | Preliminary invoice before formal billing | Advance payment or customs clearance |
| Self-Billing | Buyer-issued invoice on behalf of vendor | EDI or vendor-managed inventory programs |

### 1.2 Required Invoice Fields

A complete, valid invoice should include all of the following fields:

**Vendor Identification**:
- Vendor legal name (matching the name on the purchase order or contract)
- Vendor address
- Vendor tax identification number (EIN or TIN in the US; VAT number in international contexts)
- Vendor invoice number (unique per vendor per invoice)
- Vendor remittance address (may differ from vendor address)

**Date Fields**:
- Invoice date (the date the invoice was issued)
- Service period (the dates during which services were rendered or goods were delivered)
- Due date (the date payment is required)
- Payment terms (e.g., Net 30, Net 60, 2/10 Net 30)

**Buyer/Company Fields**:
- Buyer legal entity name
- Buyer billing address or department
- Purchase order number (PO number) — required for PO-backed invoices
- Contract number or statement of work reference — required for service contracts

**Line Item Detail**:
- Description of goods or services
- Quantity (units, hours, licenses, seats, etc.)
- Unit price or rate
- Line item extended amount (quantity × unit price)

**Financial Summary**:
- Subtotal (before taxes and discounts)
- Applicable taxes (itemized by type and jurisdiction)
- Discounts (early payment discounts, volume rebates)
- Shipping and handling (if applicable)
- Total amount due

---

## Part 2: Three-Way Matching

### 2.1 What Is Three-Way Matching?

Three-way matching is the standard AP validation process that compares three documents to confirm that a payment is authorized, the goods or services were received, and the billing matches what was ordered:

1. **Purchase Order (PO)**: The buyer's formal order specifying quantities, prices, and terms
2. **Receiving Report (or Service Confirmation)**: Evidence that the goods or services were received or performed
3. **Vendor Invoice**: The vendor's billing document

All three must agree within defined tolerance thresholds before payment is approved.

### 2.2 Matching Tolerances

Most AP systems allow for minor discrepancies without requiring manual intervention:

| Discrepancy Type | Common Tolerance |
|---|---|
| Price variance | ± $10 or ± 1% of line item, whichever is lower |
| Quantity variance | ± 1 unit or ± 2% of ordered quantity |
| Total amount variance | ± $25 or ± 0.5% of invoice total |

Variances exceeding tolerances require manual review and approval.

### 2.3 Two-Way Matching

For certain invoice types — primarily for services where a formal receiving report is impractical — two-way matching compares the purchase order against the invoice only. Two-way matching is appropriate when:
- The service is ongoing and performance is attested by the business requestor
- The invoice is for a recurring subscription without variable quantities
- No formal PO was issued and payment is based on contract terms only

---

## Part 3: Common Validation Rules

### 3.1 Duplicate Invoice Detection

Duplicate invoices are a major source of overpayment. Validation checks include:

- **Exact duplicate**: Same vendor + same invoice number + same amount
- **Near duplicate**: Same vendor + same amount + invoice date within 30 days
- **PO overbilling**: Cumulative invoiced amount exceeds PO authorized amount

Suspected duplicates should be routed for manual review before payment.

### 3.2 Vendor Master File Validation

Each invoice should be matched against the vendor master file to confirm:
- The vendor is an active, approved vendor
- The remittance address on the invoice matches the vendor master
- The bank account details (for ACH payments) match the vendor master
- The vendor's W-9 or W-8 tax certification is current

Discrepancies between the invoice remittance instructions and the vendor master are a common indicator of payment fraud (particularly Business Email Compromise schemes). Any change to bank account information should trigger a separate verification process.

### 3.3 Purchase Order Validation

For PO-backed invoices:
- The PO number must exist in the PO system and be in an open (unfulfilled) status
- The invoiced line items must correspond to open PO line items
- The invoiced quantities must not exceed the undelivered/unreceived quantities on the PO
- The invoiced unit prices must not exceed the PO unit prices (within tolerance)

### 3.4 Contract Compliance Validation

For contract-backed invoices (without a formal PO):
- The billing rates must match the contract rate schedule
- The service period must fall within the contract term
- Total invoiced amount must not exceed the contract ceiling (for fixed-price contracts)
- Milestone billing must correspond to completed and accepted milestones

### 3.5 Tax Validation

- Sales tax or use tax should be applied only to taxable items
- Tax rates should correspond to the applicable jurisdiction
- Tax-exempt purchases should have a valid exemption certificate on file
- International invoices should correctly reflect VAT, GST, or other applicable indirect taxes

---

## Part 4: Exception Handling

### 4.1 Invoice Exception Types

| Exception Code | Description | Standard Resolution |
|---|---|---|
| PRICE_VARIANCE | Invoice price differs from PO/contract by more than tolerance | Route to purchaser for approval or vendor for credit memo |
| QUANTITY_VARIANCE | Invoice quantity differs from received quantity | Route to receiving department for confirmation |
| MISSING_PO | Invoice references no valid PO number | Route to requestor to create a PO or authorize payment |
| DUPLICATE_INVOICE | Invoice appears to be a duplicate | Hold; contact vendor to confirm; reject if confirmed duplicate |
| VENDOR_INACTIVE | Vendor is not active in vendor master | Hold; contact procurement to reactivate or redirect |
| BANK_MISMATCH | Remittance details differ from vendor master | Hold; verify with vendor through established communication channel |
| MISSING_SERVICE_PERIOD | Invoice does not specify service period | Route to vendor for correction |
| MATH_ERROR | Line item extensions or totals do not compute correctly | Route to vendor for corrected invoice |
| EXPIRED_PO | PO has expired or been closed | Route to purchaser for PO extension or new PO |
| OVER_PO | Cumulative invoices exceed PO value | Route to purchaser for PO amendment or rejection |

### 4.2 Exception Escalation Matrix

| Exception Severity | Dollar Threshold | Approver |
|---|---|---|
| Low | < $1,000 | AP Team Lead |
| Medium | $1,000 – $10,000 | Department Manager |
| High | $10,001 – $50,000 | Controller or VP Finance |
| Critical | > $50,000 | CFO or delegated authority |

### 4.3 Vendor Credit Memo Process

When an invoice must be adjusted (overcharge, returned goods, disputed service), the vendor should issue a credit memo. Credit memos should include:
- Reference to the original invoice number
- Reason for the credit
- Line item detail of the credited amount
- Net amount after credit

Credit memos are applied against open invoices or held for future offset.

---

## Part 5: Payment Terms and Early Payment

### 5.1 Standard Payment Terms

| Payment Terms Code | Meaning |
|---|---|
| Net 30 | Payment due 30 days from invoice date |
| Net 60 | Payment due 60 days from invoice date |
| Net 90 | Payment due 90 days from invoice date |
| 2/10 Net 30 | 2% discount if paid within 10 days; full amount due in 30 days |
| EOM | Payment due at end of the month in which invoice is received |
| CIA | Cash in advance — payment required before delivery |
| COD | Cash on delivery — payment at time of delivery |

### 5.2 Early Payment Discount Capture

Early payment discounts (e.g., 2/10 Net 30) represent a significant annualized return (2/10 Net 30 = approximately 36.7% annualized). AP should have a defined process for identifying discountable invoices and processing them within the discount window.

---

## Part 6: Approval Workflow

### 6.1 Standard Approval Routing

A typical invoice approval workflow:
1. **Receipt and capture**: Invoice received via email, EDI, or portal; data extracted and validated
2. **Matching**: Three-way or two-way matching performed
3. **Exception routing**: Exceptions routed to appropriate approvers
4. **Business approval**: Coded to cost center/GL account; approved by budget owner
5. **Final AP review**: AP confirms all validations passed and approvals received
6. **Payment scheduling**: Invoice queued for payment on next payment run

### 6.2 Approver Delegation and Out-of-Office Coverage

Approval workflows should include:
- Delegation rules for when primary approvers are unavailable
- Auto-escalation timers (e.g., escalate to manager if not approved within 5 business days)
- Audit trail of all approvals, rejections, and comments

---

*This guide is a reference document for AI-assisted invoice processing and review. It does not constitute accounting or legal advice. Specific thresholds and procedures vary by organization.*
