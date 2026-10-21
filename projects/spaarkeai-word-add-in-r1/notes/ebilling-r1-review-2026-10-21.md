# ebilling-r1 design review: owner decisions (2026-10-21)

This project reviewed ebilling-r1's `projects/ebilling-r1/notes/procedure-and-file-model.md` v4 (`c260f9de11`), §9.1 and §14, at the owner's request. The review went to ebilling-r1 on 2026-10-21. They record the owner's answers in their `procedure-model-reviews.md` §4 (`502da83085`). This note is our copy.

## Owner decisions (given in this project's session, 2026-10-21)

1. **Submit Invoice in the add-in: yes.** Creating the invoice needs a different process: the user flags an attachment as going to the invoice file save, instead of a Document being created from it. The process itself is to be discussed with the owner separately. ebilling-r1's constraint stands: invoices are created only by submission (their FR-17, FR-20).
2. **The race (ebilling K5): fix it by filing the email first.** The owner asked what other options exist; they are below.
3. **Internal-only email or invoice sources: allowed.** The copy carries the source's isolation forward:
   - the invoice file stores the source document and communication ids;
   - Firm-visible = No for copies that arrived by email;
   - it is kept out of search, indexes and chat grounding unless it passes T7. Extraction still reads the bytes.

   Who may read an internal-only copy under the invoice is an access question for uac-r2, in ebilling's v5 review.
4. **Invoice as an add-in save target, versus ebilling O-8:** goes with item 1. Review the process so it isn't overcomplicated. The email must be associated with the matter, project or work assignment, and with the invoice.

**Owner decision (2026-10-21): Restricted sources and sources whose thread is not Open are both allowed, on the same terms as internal-only.**
- A Restricted source is a task-130 document whose email couldn't be identified.
- "Not Open" means the thread's privacy setting (`sprk_communicationthread`) is Private, so partners can't see it (R3 task 065, `CommunicationOpenThreadGate`).
- B carries the isolation forward: the invoice file stays Restricted, or hidden from partners, as its source is.
- The extracted fields don't follow the source. Who may read them stays with uac-r2.

**Owner decision on the race (2026-10-21, later the same day): fix both, A + B.** File the email first, and make the invoice file follow its source.

## Race options (sent to the owner; the owner chose A + B)

| Option | What it does | What it misses |
|---|---|---|
| A. File the email first (the owner's lean) | Submit Invoice files the unfiled communication to M first, through `PATCH /api/communications/{id}/filing` with the caller's rights. Our save then reports AlreadyFiled. | A later manual refile to S still leaves the invoice file exposing the email under M. |
| B. The invoice file follows its source | The stored source ids are read by T7 on every access, so a later refile, Restricted or internal-only flag is honoured. | Nothing on visibility; needs a T7 rule for invoice files (uac-r2). |
| C. Row-version check at commit | Fails the commit if the communication changed after the check. | Refiles after commit. |
| D. Block refiling | An email behind a submitted invoice can't be refiled. | Blocks legitimate corrections, and couples filing to eBilling. |
| E. Reconcile afterwards | A job finds mismatches and hides the file. | There is a window before it runs. |

**Recommendation: A + B.** A makes the email and invoice agree from the start; B keeps them correct after any later change. B is also the mechanism decision 3 needs.

## v5 re-check (ebilling `70fc2cc616`; gaps recorded in their v5 §15, `cadd4a1a1b`)

v5 §9.1 matches the decisions above. Three add-in gaps were accepted:
1. **No source document** (a flagged attachment): the caller's Read on the communication; the communication id plus the attachment identity on the Stored File; B follows the communication alone; no document-row guard; the post-commit check runs against the communication's filing and thread.
2. **An email that can't be identified:** Submit Invoice captures the email first, as `/office/save` does. If capture fails, the copy is created with its own Restricted in the same create. **Owner item, ours to raise when the process is discussed.**
3. **Keys:** the RFC Message-ID, never the Exchange item id; the endpoint has its own idempotency key that includes the flag.

Separately, uac-r2 found that a source already isolated at submit protects only the Stored File, not the derived invoice rows. The options are in their v5 §15 for the owner; the recommended one carries the isolation onto the invoice.

## Owner input on the add-in invoice flow (2026-10-21; HELD for discussion, not sent to ebilling-r1)

The owner stated two rules:
- when an email is saved to an invoice, its attachments go to the invoice's files, not Documents;
- a new invoice must be related to a Matter or Project.

They asked whether an explicit flag is needed for which attachment is the invoice.

Our recommendation:
- **One attachment is explicitly marked "Invoice"** (pre-selected when there is exactly one PDF); the rest are "Supporting" or "Don't save". The server never guesses.
- Saving to an existing invoice adds supporting files (eBilling's internal-upload route).
- "+New invoice" (today a blank quick-create with no core-record link, `OfficeService` QuickCreate Invoice leg) is replaced by Submit Invoice, with the Matter or Project required.

Open questions:
- Work Assignment in the lookup?
- Does the .eml stay as a communication filed to the matter?
- LEDES files?

The owner chose to discuss the process before anything is sent.

**Owner decisions after the discussion (2026-10-21; sent to ebilling-r1):**
1. **An explicit invoice mark.** One attachment is Invoice, the rest Supporting or Don't save; pre-selected when there is exactly one PDF.
2. **Corrections are resubmissions** (ebilling D-34/D-37/D-43): the old invoice closes as superseded, and a new linked invoice ({invoice}.{n}) keeps the history. No in-place PDF replacement. A correction is refused once the invoice is Approved, Sent to AP or Paid (our recommendation; the owner said ok).
3. **A separate "Invoice" task-pane view**, opened by a "Submit Invoice" ribbon button, like Add To Do:
   - new invoice, with the core record required;
   - existing invoice: add supporting files, or submit a corrected invoice.
4. **Removed from the normal Save flow:** Invoice as a save target, and the blank "+New invoice" quick-create.
5. **Core records:** Matter, Project and Work Assignment; Agreement when ebilling R2 adds it. The list comes from ebilling.
6. **The email is always saved as the .eml file in SPE**, filed to the core record. It is not optional and never an invoice file. That save must not create the attachments as child Documents; ebilling defines the contract.
7. **LEDES is out of scope.**

**ebilling v5.7 (`ef9b1a9e9b`), relayed 2026-10-21:**
- **Finding the .eml.** They took our fix for v5.6 point 1. A plain "exactly one Document linked to the communication" breaks after task 130. The .eml is now found in three steps:
  1. `sprk_isemailarchive = true`;
  2. then the target core record, with no parent;
  3. then the SHA-256 of the file marked Invoice.

  If no Document matches, or more than one, the server refuses and the user can retry. ebilling adds a list version of `GetEmailArchiveByCommunicationAsync`.
- **Owner picks that change what the add-in shows:**
  - **F1-a (i):** an .eml created by `from-email` is saved Restricted on its own, with a submission marker. The firm still sees its invoice; other outside participants can't open the email.
  - **F1-c:** if the email can't be identified (no Message-ID, a failed Graph read, or no communication), `from-email` refuses with a retryable "try again" and saves nothing. The Invoice view shows a retry, and suggests the internal upload route if it keeps failing.
- ebilling lists our re-save gap #2209 (ISS-022) as a known limit.

**Work for this project (not yet a task)**, once ebilling's endpoint contract exists:
- the Invoice view and ribbon button;
- removing Invoice from Save and quick-create;
- the email-only .eml save.

## Effect on this project

None until the owner settles the process in decision 1. Tasks 130 and 131 are unaffected.
