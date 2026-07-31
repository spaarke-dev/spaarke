# NEG-04 — Non-agreement document (commercial invoice)

> Negative case for the GENERALIZED agreement-review Action (agreements-r1 task 002, FR-01/FR-06).
> This document is an INVOICE — not an agreement or contract at all. It exercises the generalized
> **agreement-scope guard**: "is this an agreement?" (the NDA-specific "is this an NDA?" guard is gone).
> Expected behavior: DECLINE — an EMPTY flaggedSections array with overallRisk Low. There is no agreement
> to review and no clause taxonomy to attach findings to; producing findings here would be ungrounded.
> (Contrast NEG-01, a residential lease, which IS an agreement — under generalization a lease is reviewed
> if its type standard is retrieved, and declines only for lack of a retrieved standard, not for being
> "the wrong document type".)

---

NORTHWIND SUPPLIES LLC
INVOICE

Invoice No.: INV-2026-04817
Invoice Date: April 12, 2026
Due Date: May 12, 2026

Bill To:
Acme Robotics, Inc.
1200 Innovation Parkway, Suite 400
Springfield

Ship To:
Acme Robotics, Inc. — Receiving Dock B
1210 Innovation Parkway
Springfield

| Line | Description | Qty | Unit Price | Amount |
|---|---|---|---|---|
| 1 | Industrial servo motor, Model SM-220 | 12 | $340.00 | $4,080.00 |
| 2 | Motor controller board, rev C | 12 | $115.00 | $1,380.00 |
| 3 | Shielded cable harness, 3m | 24 | $22.50 | $540.00 |
| 4 | Freight & handling | 1 | $210.00 | $210.00 |

Subtotal: $6,210.00
Sales Tax (7.25%): $450.23
**Total Due: $6,660.23**

Payment Terms: Net 30. Remit to Northwind Supplies LLC, Account 8841-2207, Routing 021000021.
Please reference the invoice number on your remittance. Questions: ar@northwindsupplies.example.

Thank you for your business.
