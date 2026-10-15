# 116 — B2B guest in Outlook on the web: fixes (2026-10-08)

Owner's first real guest run (package 1.1.2, `ralph@deweycheatham.onmicrosoft.com`, guest in Spaarke's tenant, Outlook on the web): the add-in loads, signs in and works — the sign-in design holds for guests. Four defects:

| # | Symptom | Cause | Fix |
|---|---|---|---|
| A | `.eml` with header only; attachment Document not created | The add-in sent only `internetMessageId`; the server fetched body + attachments via Graph OBO. A guest's mailbox is in their HOME tenant — unreachable from Spaarke's tenant — so the fetch failed silently. Affects every Model 1 customer. | Body + attachments read in the add-in (Office.js) and sent with the save; the server already prefers client content (no server change, no BFF deploy). Details, limits and live checks: **`notes/116a-client-email-capture.md`**. |
| B | Copy Link: "Couldn't copy" — "Clipboard API has been blocked because of a permissions policy" | Outlook on the web's iframe does not delegate `clipboard-write`. | `utils/copyText.ts`: Clipboard API, then selection copy (`execCommand('copy')`, not governed by that policy); if both fail, Save shows the link in a read-only field to copy by hand (Diagnostics shows its text block). |
| C | Find: "Document identity resolution failed" (`CAPABILITY_NOT_SUPPORTED`) | `retryDocumentIdentity` ("Try again") was not gated like the automatic run, so in Outlook it called the Word-only `getDocumentUrl()`. | Gated on `canGetDocumentUrl` (`App.tsx`). No App render harness exists, so this one-line guard has no dedicated test; it mirrors the automatic run's existing gate. |

## Decisions
- The archived `.eml` stays COMPLETE (every readable attachment); only ticked attachments become Documents (main session, 2026-10-08) — avoids a member regression where an unticked attachment vanished from the archive.
- Per-attachment cap 20 MB (was 25 MB while the server fetched via Graph): everything now travels in one request (Kestrel 30,000,000 B). Lifting it means per-attachment upload (later).
- Inline images in the body may not render in the stored `.eml` (Office.js exposes no content IDs) — live check.

## Gates
Full add-in jest 96 suites / 1,293 tests; lint 0; typecheck 0 production (test debt 68); server office scope 4,804/0 (new `OfficeEmailClientContentTests`, 4 tests, no server code). New gated suites: emailContentCapture, useSaveFlow.emailContent, SaveView.emailContentCapture, commands.emailContent, OutlookAdapter.attachmentKinds, AttachmentSelector.saveLimit, copyText; SaveFlow.buttonFeedback updated.

## Live checks (owner, as the guest, Outlook on the web)
1. Save an email with a PDF ticked: the .eml has the body and the PDF; a PDF Document exists.
2. Save with the PDF unticked: the .eml has the PDF; no Document.
3. Copy Link: "Copied" (or the copy-by-hand field).
4. Find tab: no identity error.
5. Quick Save from Outlook: body + attachments.
