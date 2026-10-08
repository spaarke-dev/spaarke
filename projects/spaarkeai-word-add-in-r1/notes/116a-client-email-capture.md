# 116a — Email body and attachments read in the add-in (B2B guest save fix)

> Owner UAT 2026-10-08. A B2B guest (home tenant `deweycheatham.onmicrosoft.com`, signed in to Spaarke's tenant
> as a guest) saved an email from Outlook on the web: the `.eml` had headers only, and the attachment's document
> was not created.

## 1. Finding

The add-in sent only `internetMessageId` (`useSaveFlow.ts`: `body: undefined, // Retrieved server-side via Graph API`).
The BFF then fetched the body and attachments from the mailbox through Microsoft Graph on behalf of the user
(`OfficeEmailEnricher.EnrichEmailFromGraphAsync`, `graphClient.Me.Messages[...]`). A guest's mailbox lives in their
HOME tenant; the token Spaarke's tenant issues cannot reach it. The enricher catches the failure and carries on, so
the save "succeeds" with an empty `.eml`. Every Model 1 customer's staff are such guests.

The ribbon **Quick Save** had the same defect (`quickSaveHelpers.buildEmailSaveRequest` also sent `body: undefined`).
It posts only when the association engine has a prediction, which a guest can still have (an email they exchanged
with a monitored Spaarke member), so it was fixed in the same way.

## 2. Server path — traced end to end (no server code changed)

| Step | Where | What it does with client content |
|---|---|---|
| Graph enrichment | `OfficeEmailEnricher.EnrichEmailFromGraphAsync` | **Skips Graph entirely when `email.body` is non-empty.** Without a body it fetches body + ALL file attachments from Graph and replaces `email.attachments`; on any Graph failure it returns the request unchanged. |
| `.eml` build | `OfficeService.SaveAsync` → `OfficeEmailEnricher.BuildEmlFromMetadata` | MimeKit: HTML body from `email.body`; every `email.attachments[]` with `contentBase64` becomes a MIME part (inline + `contentId` → linked resource). |
| Job row | `OfficeJobStatusService.BuildPayload` | `sprk_payload` strips `body` and every `contentBase64` (task 060 rule holds — content never goes in the job row). |
| Service Bus message | `OfficeJobQueue` → `UploadFinalizationPayload` | Metadata only (`BodyPreview` = first 500 chars, names, `HasAttachments`). No content. |
| Attachment documents | `UploadFinalizationWorker.ProcessEmailAttachmentsAsync` | Downloads the stored `.eml`, `EmailToEmlConverter.ExtractAttachments` (plain MIME parts only), noise filter, then keeps those whose name is in `selectedAttachmentFileNames` (null = all, `[]` = none). |
| Communication record | `EmailUploadCaptureService` | `sprk_body` from `email.body`; `sprk_attachmentcount` from `email.attachments` (metadata). |

So the server **already preferred client content** and used Graph only as the fallback. The fix is client-side.
Request size: `/api/office/save` sets no `RequestSizeLimit`, so Kestrel's default `MaxRequestBodySize`
(**30,000,000 bytes**) applies; nothing else caps it.

## 3. Design

> **Owner decision 2026-10-08 (via the coordinator):** keep the archived `.eml` complete. The `.eml` carries EVERY
> readable attachment; `selectedAttachmentFileNames` stays the ticked list, so only ticked attachments become
> documents (server behaviour unchanged). Budget order: body, then ticked, then unticked.

- **Pane** (`useSaveFlow` email branch): at submit, a live reader returns the HTML body and **every** attachment's
  content; they go in the existing `email.body`, `email.isBodyHtml`, `email.attachments[]`
  (`attachmentId, fileName, size, contentType, contentBase64, isInline`). `email.selectedAttachmentFileNames` = the
  TICKED attachments that were actually sent. `internetMessageId` is still sent.
- **Seam**: `SaveFlow.tsx` sits between `SaveView` (holds the adapter) and the hook, and is outside this change. The
  reader travels through a React context, `hooks/emailContentCaptureContext.ts` — `SaveView` provides it, the hook
  reads it. Same "live function, invoked at submit" shape as Word's `captureDocumentContent` (task 045); a retry reads
  again.
- **Gate (NFR-10)**: the reader is provided only when `capabilities.canGetAttachments` (Outlook read mode + Mailbox
  1.8, i.e. `getAttachmentContentAsync`). Otherwise nothing is captured and the request is exactly the old shape — the
  server's Graph fallback. (Sending a body without attachments would switch Graph off and lose them.)
- **Pure decision**: `services/emailContentCapture.ts` (`captureEmailContent`) — shared by the pane and Quick Save.
- **Quick Save**: body + every attachment in the `.eml`; the non-inline ones become documents (inline images are body
  parts).
- **Request log**: `[SaveFlow] Sending request:` now logs `body` / `contentBase64` as `[N chars]`, never the content.

### Attachment kinds

| Kind (Office.js) | Handling |
|---|---|
| `file` (format `base64`) | Sent as is. |
| `item` — attached email (format `eml` text, or `base64` on the web) | UTF-8 → base64, name + `.eml`, **`application/octet-stream`**. Not `message/rfc822`: MimeKit embeds that as a message part, which `ExtractAttachments` skips, so it would never become a document (verified by seeding — see §6). |
| `item` — attached meeting (format `icalendar`) | UTF-8 → base64, name + `.ics`, `text/calendar`. |
| `cloud` (or content returned as `url`) | **Not saved**, ticked or not, reported: "…was not saved with the email: it is a link to a file stored in the cloud (for example OneDrive), not a file in the email. Save that file from where it is stored." Never read. |

## 4. Limits and failure behaviour

| Constant | Value | Why |
|---|---|---|
| `SAVE_REQUEST_MAX_BYTES` | 30,000,000 | Kestrel default; the route sets none. |
| `EMAIL_CONTENT_BUDGET_BYTES` | 28,000,000 | JSON-encoded body + every base64; 2 MB left for the rest of the request. |
| `MAX_ATTACHMENT_BYTES` | 20 MB (20,971,520) | Its base64 (27,962,028) fits the budget alone. |

- **Picker** (`AttachmentSelector`): per-file and total limits were 25 MB / 100 MB "per spec" — achievable only while
  the server fetched from Graph. Now both 20 MB (for the TICKED set, which gets the budget first), so a ticked
  attachment that cannot be carried is refused when ticked ("File exceeds 20 MB limit").
- **Budget order**: body first; then ticked attachments, then unticked ones (each group in email order). Each must fit
  in what is left — checked on the reported size before reading (an attachment that cannot fit is never read) and on
  the real base64 length after. A tight budget therefore drops unticked attachments from the `.eml` before ticked ones.
  The attachments sent keep the email's own order in the `.eml`.
- **Deterministic → skip and say so.** Too large / cloud link (ticked or not): left out of the `.eml` (a ticked one
  then gets no document), the save goes ahead, and `SaveView` shows a warning bar naming each with its reason. Quick
  Save: "Filed to X. N attachment(s) left out: too large, a cloud link or unreadable."
- **Transient → stop (for what the user asked for).** The body or a TICKED attachment cannot be read: the save stops,
  nothing is sent, and the error names what failed ("Couldn't read the attachment "X", so nothing was saved. Try again,
  or untick it to save the email without it as a document."). An UNTICKED attachment that cannot be read does NOT stop
  the save — the user did not ask for it as a document — but it is left out of the `.eml` with a named warning
  ("…was not saved with the email: Outlook could not read it.").
- A body larger than the whole budget is refused, never truncated.

## 5. Known limits

1. **20 MB per attachment, ~20 MB of attachments per save.** Through Graph a member could save a 20–25 MB attachment
   (Graph had no request limit); now one save request carries at most the 28,000,000-byte content budget. A ticked
   attachment over 20 MB is refused in the picker; anything that does not fit at save time is named in the warning.
   Lifting this needs content to leave the single JSON request (a separate upload per attachment) — a design change,
   not in this task.
2. **Inline images** (live check 7): Office.js gives no Content-ID, so an inline image is stored as a plain attachment
   part, and `cid:` images in the body may not render inside the stored `.eml` (Graph's path embedded them as linked
   resources).
3. An email whose body is EMPTY still triggers the Graph attempt (the server's skip test is "body non-empty"); for a
   member Graph then supplies everything, for a guest the client content stands. Harmless; noted for completeness.

## 6. Tests

Client (jest, all NEW files — ADR-038):
- `shared/taskpane/services/__tests__/emailContentCapture.test.ts` (20) — every attachment read, ticked first; sent in
  email order; only ticked names; an unticked attachment in the content but not in the names; nothing ticked → all in
  the `.eml`, `[]` names; budget order (unticked dropped before a ticked one, even when listed first); a ticked
  attachment that cannot fit gets its message while the rest save; body counts against the budget; real base64
  length re-checked; cloud/url skip (ticked and unticked); body / ticked read failures stop; unticked read failure is
  a warning; attached email/meeting encoding + names; limit arithmetic.
- `shared/taskpane/hooks/__tests__/useSaveFlow.emailContent.test.tsx` (7) — request carries captured body/attachments/
  names + `internetMessageId`; an unticked attachment on the wire in `email.attachments` but not in
  `selectedAttachmentFileNames`; reader gets attachments + selection; read failure sends nothing and surfaces; retry
  re-reads; no reader → pre-116a shape; log redaction.
- `shared/taskpane/components/views/__tests__/SaveView.emailContentCapture.test.tsx` (5) — provider gated on
  `canGetAttachments`; mount reads nothing; reads body + every attachment via the adapter, names only the ticked one;
  skipped-attachment warning shown; read failure.
- `outlook/commands/__tests__/commands.emailContent.test.ts` (3) — Quick Save sends body + every attachment (inline
  included) and names the non-inline ones; cloud link reported in the notice; read failure posts nothing; no Mailbox
  1.8 → old shape.
- `shared/adapters/__tests__/OutlookAdapter.attachmentKinds.test.ts` (5) — `attachmentType` / `contentFormat` mapping.
- `shared/taskpane/components/__tests__/AttachmentSelector.saveLimit.test.tsx` (2) — 20 MB per-file and total.

Server (test-only, no server code change): `tests/unit/Sprk.Bff.Api.Tests/Services/Office/OfficeEmailClientContentTests.cs`
(4) — body present → Graph never called; no body → Graph tried, request kept when unreachable; `.eml` from client
content round-trips through `ExtractAttachments` by the sent names (incl. the `.eml` item); the add-in's JSON binds
onto `SaveRequest`. **Seeded**: switching the attached email to `message/rfc822` fails the round-trip test (1 of 2
extracted) — the `application/octet-stream` choice is load-bearing.

## 7. Live checks (owner, after the add-in deploys — no BFF deploy needed)

1. Guest (deweycheatham) in Outlook on the web: save an email with a PDF ticked → the `.eml` opens with its body and
   the PDF; a child document for the PDF exists.
2. Same email, nothing ticked → `.eml` has the body AND the PDF; no attachment documents.
3. An email with a forwarded email attached, ticked → a `.eml` child document that opens.
4. An email with a OneDrive "Share link" attachment ticked → warning bar names it; the rest saves.
5. A > 20 MB attachment → the picker refuses it.
6. Member in classic Outlook desktop (item attachments arrive as `eml` format there) → same as 1 and 3.
7. An email with inline images → note whether the body's images render in the stored `.eml` (§5.3).
8. Ribbon Quick Save on an email that has a prediction → `.eml` has body + attachments; a cloud link is named in the
   notification.
