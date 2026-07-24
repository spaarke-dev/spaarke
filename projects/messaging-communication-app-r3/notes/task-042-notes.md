# Task 042 — Attachments: open/preview/download + attach-on-compose (FR-20)

## Summary
Delivered FR-20 by REUSING existing surfaces — no new previewer, no new send/upload path, no new BFF endpoint.

## Part 1 — Open/preview/download a message attachment (SPE path reuse)
- New shared chip component `CommunicationTimeline/subcomponents/MessageAttachments.tsx` renders attachment chips with an OPEN affordance. Authored once, consumed by `MessageBubble`, `EmailInFlowBlock` (ConversationView) and `MessageRow` (CommunicationTimeline) — §11 (one component, not three copies).
- New optional callback prop `onOpenAttachment(attachment, message)` threaded: `ConversationView` → MessageBubble/EmailInFlowBlock; `CommunicationTimeline` (thread mode) → MessageRow.
- The host wires `onOpenAttachment` to the **existing** SPE document-viewer path: shared `RichFilePreviewDialog` fed by `/api/documents/{id}/preview-url` + `/open-links` — the identical wiring `CommunicationAttachmentsApp` (PCF) already uses. ConversationView/CommunicationTimeline stay context-agnostic (ADR-012) — they never mount the viewer. No new inline previewer built (escalation NOT fired — the existing path serves message attachments).
- EmailInFlowBlock previously rendered NO attachments; it now renders them (with the open affordance).

## Part 2 — Attach-on-compose (25 MB + MIME per CHAT-ATTACHMENT-POLICY)
- `EmailComposer.reducer.ts`: added `ATTACHMENT_MAX_FILE_BYTES` (25 MB per-file binary) + `ALLOWED_ATTACHMENT_MIME_TYPES` (txt/md/pdf/docx — the policy's single-source-of-truth allow-list) + pure `validateLocalAttachmentFile()`.
- `AttachmentList.tsx`: the local file picker now validates each pick BEFORE it enters state; an oversize/disallowed file is rejected with a **visible** `role="alert"` error and is never added / never counted / never sent (FR-20 negative criterion). Scoped to `source === 'local'` — governed Documents (spe/related/wizard) are exempt (they passed their own upload gates).
- `validateState` gained defense-in-depth per-local-file size + MIME checks (raises `ATTACHMENT_TOO_LARGE` / `ATTACHMENT_BLOCKED_TYPE` — the latter code was previously declared-but-unused).
- Made local picks SENDABLE through the EXISTING path: new optional `onUploadLocalAttachment(file)` prop resolves a validated local file → `sprk_document`; the composer patches `documentId` (new `RESOLVE_ATTACHMENT_DOCUMENT` action) so it flows into `mapStateToSendRequest` → `attachmentDocumentIds`. No second send/upload path (ADR-045); the send engine is unchanged. Absent resolver → local picks stay display-only (back-compat).

## Access-filtering (NFR-01)
Two layers, no over-disclosure:
1. The open affordance is gated on a resolved `documentId`, which only exists on attachments the impersonated, access-filtered thread read returned for a message this caller may read (no membership-union). No client path fabricates a retrieval id.
2. The BFF `/api/documents/{id}/preview-url` + `/open-links` endpoints re-enforce document-level access under OBO (`RequireAuthorization` + `ForUserAsync`). A user without access gets 403/404 — cannot retrieve.

## BFF
NO change. Retrieval reuses existing `FileAccessEndpoints`. No new endpoint, no publish-size / CVE impact.

## ADR / policy notes
- The CHAT-ATTACHMENT-POLICY MIME allow-list is only 4 types (txt/md/pdf/docx). This is narrow for general email attachments (images/xlsx/pptx). Applied as-instructed (the policy is the binding doc for FR-20 and the negative acceptance criterion tests a disallowed-MIME rejection). Flagged as a potential future product/ADR tension (Path A candidate) if broader email attachment types are needed — would raise via §6.5, not silently widened here.

## Scope boundary
CommunicationTimeline **regarding** mode (ThreadGroup) left unchanged — it is a read-only discovery/grouping view, not the primary "message in the conversation" surface. Attachments there still render as passive chips.

## Tests (jest)
- `CommunicationTimeline/__tests__/MessageAttachments.test.tsx` — open button hands attachment+message back; NEGATIVE: no button when no documentId / no handler (access-filtering).
- `EmailComposer/__tests__/attachOnCompose.test.tsx` — policy gate (25 MB + MIME + empty type), validateState defense-in-depth (local vs governed exemption), RESOLVE → send payload, AttachmentList visible rejection + not-added, composer upload→send-eligible wiring.
