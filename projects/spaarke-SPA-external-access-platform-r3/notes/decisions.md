# Decisions — Spaarke External Access Platform R3

Full owner clarifications live in `spec.md` §Owner Clarifications; the auth path and its rationale are in `notes/r3-auth-path.md`. This file records decisions made during planning and execution, with rationale. One entry per decision; superseded entries are marked, not deleted.

## 2026-10-10 — Planning decisions (project-pipeline)

- **Feed "last seen" marker in Redis (ADR-009), not a Dataverse column.** Spec lists ADR-009 for the feed/last-seen and the owner chose "no new table". Losing the marker only re-marks the last 30 days as unread. Revisit if owner wants it durable across cache loss.
- **Service-request documents link through the existing `sprk_document.sprk_relatedservicerequest` lookup** (live describe, dev, 2026-10-10: relationship `sprk_document_RelatedServiceRequest_sprk_servicerequest`). No new lookup is needed.
- **Intake schema folded into task 020.** `sprk_servicerequest` has no request-type or intake-answers column (R2 task 030 was never built). Task 020 adds `sprk_requesttype` (choice: NDA Assessment, Invention Submission, Policy Question, Trademark Search) and `sprk_intakedata` (multiline JSON), with owner sign-off.
- **Spec wording "WizardModal / WizardRegistry"** maps to the current `Wizard/WizardShell` + `InAppWizardHost` (the old names were deleted 2026-10-03).
- **Creator stamp for service requests is `sprk_requestedby`** (already present). `sprk_createdbycontact` is added to document, to-do, event and communication only.
- **One ADR-028 amendment task (001) for R3's three rule changes**, coordinated with `spaarke-auth-system-of-record-r1`, which owns the §12b items. Owner approval is needed to edit `.claude/adr/`.
- **C3 email builds on UAC-r2 PR #1583** (`GrantAccessNotifier`) instead of a parallel notifier.
