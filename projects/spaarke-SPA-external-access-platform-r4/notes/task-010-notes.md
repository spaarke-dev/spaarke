# Task 010 notes — Submit invoice modal over the record + Invoices grid toolbar

## Gate: ebilling-r1 contract review (2026-11-13): agree, with changes

- Build from master: `work/ebilling-r1` has no unmerged changes under `InvoiceSubmission/`.

**1. Hosting.**
- Host the wizard with `embedded` inside the child SprkModal.
- The non-embedded form would nest WizardShell's own dialog inside ours, giving two focus traps and two close buttons.
- Close and Escape go to the wizard's `onClose`, so one component owns the dialog.

**2. `lockedRecord`.** It is additive: when the prop is absent, the wizard behaves exactly as today, and submit is unchanged.
- **(a)** Show the fixed message only when the firm's record list is complete and does not include the locked record. If the read was partial, truncated or threw, show today's read message with Retry, Back and Cancel instead.
- **(b)** Match the locked record on kind plus id.
- **(c)** Re-check on every firm change.
- **(d)** If both are set, `lockedRecord` wins over `initialRecord`; say so in the JSDoc.
- **(e)** Step captions read "Step n of 2", or "Step n of 3" when the firm step is shown.
- **(f)** Tests cover a closed set of cases (copied into the POML acceptance criteria).

**3. Refresh.** Once `onSubmitted` has fired, refresh the record's Invoices section on any close, not only on Done.

**4. Grid toolbar.**
- The DataGrid change is agreed between us and the grid owners.
- The firms-non-empty gate on the button is a convenience only; the server refuses a submission with no firm (EB-D-223).
- Send the diff to ebilling-r1 for review, as in task 007.

## Gate: dataset-grid owners: pending (DataGrid custom-command slot)
