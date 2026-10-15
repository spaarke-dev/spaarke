# Owner requirements — 2026-10-14

Captured as stated at project start. Input to the solution overview and design; not yet scoped into MVP vs later releases.

## Submission

- Outside counsel submit invoices to the system.
- Invoices arrive as PDF; XML and JSON are future formats.
- Three submission paths:
  1. Upload through the partner SPA, in the Invoice tab.
  2. Email attachment to a designated mailbox (`ap@ebilling.spaarke.com`).
  3. Sent to a Spaarke user's email account and saved to Spaarke with the Outlook add-in.
- The MVP may start with one path (for example the designated mailbox, if that streamlines the project).

## Processing

- Invoices are created as Documents and saved to SPE.
- Invoices are indexed in their own AI Search index.
- The AI system reviews and analyzes invoices, extracting data into the Invoice and Invoice Line Item Dataverse records.
- **Critical:** this process flows into the Ontology project; the two must be coordinated.

## Billing guidelines and rate cards

- Analyzing invoices against the company's billing guidelines and rate cards is a key capability and is part of this system.
- Upload Outside Counsel Guidelines and convert them into rules.
- Upload rate cards and procedures and convert them into rules.
- Support LEDES and UTBMS codes.

## Outcomes

- Dataverse users approve invoices and invoice line items.
- Non-compliant invoices and line items are flagged and surfaced as Decisions.
- Outside counsel are notified of non-compliance (email and in the SPA), with reports.
- When outside counsel upload an invoice in the SPA, they get an immediate rejection if it conflicts with billing guidelines or rates.
