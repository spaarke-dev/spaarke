# Cross-project note: Cowork "request a review" = a Spaarke skill over this project's tools

> **Date**: 2026-11-26. **For**: the owner and the `spaarke-legal-ai-accelerator-r1` project (owner of the `nda-review` Cowork skill).
> **Status**: recorded at the owner's request. No change to this project's scope, tools or design.md.

## Context

In accelerator-r1, NDA evaluation in Microsoft 365 Copilot Cowork was tested with the Spaarke `nda-review` skill (v0.1.2-poc).
The skill runs intake, the playbook positions, the edit plan with an approval step, the Word redline set, a dashboard and an audit pack, all by script in the Cowork sandbox. Its close-out offers to email the result.
The owner asked whether the Cowork surface for "request a review" in this project could be done with such a plugin.

## Position

**Yes, as a layer on top of this project's tools, not a replacement for them.**

- **What the skill does:** the workflow and the judgment (intake, review, redline, deliverables). This project does not do this: FR-05 `spaarke_run_playbook` only starts a Spaarke playbook run and returns status plus a Console link.
- **What this project does:** Spaarke data and actions as the signed-in caller, over `/mcp/legal-operations` (one connector). As written, the skill reads the NDA from the user's own files and never ties the review to a matter.
- **The pairing:** a Spaarke Cowork skill calls this project's tools:
  - `spaarke_search_documents` / `spaarke_find_records` to find the matter and the agreement;
  - `spaarke_get_document` for its metadata and open links;
  - `spaarke_run_playbook` / `spaarke_get_playbook_run` when the review should run as a Spaarke playbook (durable run record, FR-13);
  - `spaarke_compose_email` to hand off the cover email.

## Two gaps to settle before relying on the pairing

1. **Getting the document into the sandbox.**
   - The skill's scripts need the `.docx` bytes. `spaarke_get_document` deliberately returns metadata and open links only: NFR-02 limits each result to under 64 KiB, the Cowork inline cap, and ADR-015 limits model-visible data.
   - Whether Cowork's SharePoint connector can reach SharePoint Embedded container files is **unverified**. It needs a `researcher` check against current Microsoft Learn.
   - Until it is settled, a Spaarke-sourced review means the user downloads the file and hands it to the skill.
   - A content-returning tool would be new surface: it would need the CLAUDE.md §11 justification and an owner decision, and it is not in this project.
2. **Email path.**
   - The skill's close-out sends through Cowork's Outlook connector, with the model offering the send.
   - This project's FR-12 made the opposite choice for matter email:
     - the model cannot send (`spaarke_send_email` is app-only);
     - the user presses Send in the compose widget;
     - the send goes through `CommunicationService` with its sender-enabled and recipient rules;
     - the email is archived to the matter with its attachments.
   - Recommendation: for matter-related reviews the skill ends with `spaarke_compose_email`, pre-filling the regarding matter, the subject, the cover body and suggested documents. An Outlook send for "email it to me" is lower risk but still skips the matter record.

## Who does what

- **accelerator-r1:** owns the skill change, if the owner wants the pairing:
  - the email step uses `spaarke_compose_email` for matter work;
  - the intake can take a Spaarke document;
  - the gap-1 approach is decided.
- **This project:** no action. The tools named above are already in its plan:
  - done: 022 search, 023 documents;
  - pending: FR-05 runs, FR-12 email.
