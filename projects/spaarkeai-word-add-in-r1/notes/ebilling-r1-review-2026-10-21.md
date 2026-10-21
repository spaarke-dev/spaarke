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

## Effect on this project

None until the owner settles the process in decision 1. Tasks 130 and 131 are unaffected.
