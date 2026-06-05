# Chat Attachment Policy

> **Status**: Active (binding)
> **Last Updated**: 2026-05-26
> **Owner**: BFF + Shared UI Components
> **Source**: spaarke-ai-platform-unification-r4, task 050 (A-4)
> **Source ADRs**: ADR-001 (Minimal API), ADR-010 (DI minimalism), ADR-013 (AI architecture, refined 2026-05-20), ADR-029 (BFF publish hygiene)
> **Related Constraints**: [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) (§10 BFF Hygiene)

---

## Purpose

This document is the **single source of truth** for the SprkChat multi-file attachment policy: size cap, MIME allow-list, total-text cap, PDF page cap, and the upgrade path for raising any of them in the future.

The policy is enforced by **two independent gates**:

| Gate | Location | Constant | Operates on |
|---|---|---|---|
| 1. Client binary size | `useChatFileAttachment.ts` | `MAX_FILE_BYTES` | Raw file bytes (pre-extraction) |
| 2. Client file count | `useChatFileAttachment.ts` | `MAX_ATTACHMENTS` | Number of files queued |
| 3. Client MIME allow-list | `useChatFileAttachment.ts` | `ALLOWED_MIME_TYPES` | File `type` |
| 4. Client PDF page cap | `useChatFileAttachment.ts` | `MAX_PDF_PAGES` | Pages of the extracted PDF |
| 5. Server text length (per file) | `ChatEndpoints.cs` | `MaxAttachmentTextCharsPerFile` | Extracted character count |
| 6. Server text length (sum) | `ChatEndpoints.cs` | `MaxAttachmentTextCharsTotal` | Sum of extracted char counts |

Both gates MUST agree on intent (the binary cap exists only client-side; the server validates extracted-text envelopes). Either gate rejecting the request returns RFC 7807 ProblemDetails to the user.

---

## Current Policy (as of 2026-05-26)

| Property | Value | Effective |
|---|---|---|
| Max file count per message | **5** files | Client gate |
| Max file size (binary) | **25 MB** per file | Client gate |
| Allowed MIME types | `text/plain`, `text/markdown`, `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document` (DOCX) | Client gate + server allow-list |
| PDF page cap | **200** pages | Client gate (during extraction) |
| Extracted text per file (server) | **2,500,000** chars (≈ 5 MB UTF-16) | Server gate |
| Extracted text total per message (server) | **5,000,000** chars | Server gate |

---

## Rationale

### Why 25 MB binary cap

- **Alignment with the broader Spaarke standard**: `DocumentUploadWizard` and `OfficeService` (Word add-in document save) both operate at 25 MB. Keeping SprkChat at 10 MB created an inconsistent user experience — a file the user could upload via the Documents pane was rejected by the assistant attach button. R4 A-4 (operator decision OC-R4-01, 2026-05-25) raised SprkChat to match.
- **Operational headroom**: legal documents (contracts, briefs, exhibits with embedded images) commonly land in the 10-20 MB range. 25 MB gives operational headroom without exposing the LLM to image-heavy junk that doesn't add semantic value.
- **Not raised further**: 50 MB+ binaries are typically deck/PDF-of-images artifacts where the OCR/extraction value is poor and the upload/extraction latency cost is high. The R4 decision deliberately stopped at 25 MB pending evidence that a higher cap is needed.

### Why MIME allow-list is restrictive

Only 4 MIME types are accepted: plain text, Markdown, PDF, DOCX. Reasons:
- **Predictable extraction surface**: the client has extractors for exactly these 4 types (browser `File.text()` for txt/md; lazy-loaded `pdfjs-dist` for PDF; lazy-loaded `mammoth` for DOCX). Adding a type requires writing + auditing a new extractor + a new bundle-size budget impact.
- **Security**: arbitrary file types (e.g., `.exe`, `.zip`, `.html`) widen the threat surface for prompt injection and untrusted content. Restricting the surface is cheaper than sanitizing content.
- **LLM signal quality**: image-only PDFs and binary blobs add no semantic value to the chat context.

### Why total-text cap is independent of binary cap

The server-side caps `MaxAttachmentTextCharsPerFile` (2.5M chars) and `MaxAttachmentTextCharsTotal` (5M chars) bound the **LLM prompt envelope**, not the binary payload size. They are intentionally NOT scaled with the binary cap because:

- **Text extraction is lossy and asymmetric**: a 25 MB PDF often extracts to <1M chars (image-heavy PDFs even less). A 25 MB DOCX often extracts to <500K chars. A 25 MB plain-text or Markdown file might approach the per-file cap — but those are rare in the legal-document corpus.
- **LLM context cost is in characters, not bytes**: every char extracted costs prompt tokens. The 2.5M per-file cap (~625K tokens at ~4 chars/token average) sets a per-attachment context envelope; 5M total (~1.25M tokens) protects against 5 large text files filling the entire context window.
- **Pre-clip vs reject**: text that exceeds the per-file cap is rejected by the server with RFC 7807 ProblemDetails, not silently truncated. This makes the policy violation visible to the user rather than producing degraded LLM output. (The client could in the future implement pre-truncate-with-warning UX; pending product decision.)

When raising the binary cap, the text caps DO NOT need to scale. Re-evaluate the text caps only when:
- Multi-file legal-brief workflows show >5M total chars in real usage
- The selected LLM model's context window changes (e.g., 200K → 1M tokens)
- A new MIME type (e.g., `text/csv` for spreadsheets) shifts the byte-to-char ratio

### Why PDF page cap (200 pages)

- Extraction latency: pdfjs-dist parses page-by-page sequentially. 200 pages is ~10-15 seconds on a typical client device.
- LLM signal: a 200-page PDF often exceeds the per-file text cap anyway. Failing fast at page count is better UX than running extraction to completion and then rejecting on chars.
- Operational ceiling: legal briefs and contracts rarely exceed 200 pages; the few that do (exhibits, depositions) are better handled via the Documents pane and explicit RAG indexing.

---

## Upgrade Path

### To raise the binary cap

1. Update `MAX_FILE_BYTES` in `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatFileAttachment.ts`.
2. Update the JSDoc on the constant to reflect the new value + rationale.
3. Update boundary tests in `useChatFileAttachment.test.ts`: the `R4 A-4 boundary cases` block. Add new "at cap" and "over cap" cases.
4. Update this policy doc's "Current Policy" table and add a row to "Change History".
5. **No server change required** — the server cap operates on extracted text, not binary.
6. **NFR-01 publish-size verification NOT required** for binary-cap-only changes (no new server code or packages).
7. Open a §10 Placement Justification entry per [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md). Even though the change is client-only, the policy doc IS BFF-coupled (server tests assert these constants).

### To raise the server text caps

1. Update `MaxAttachmentTextCharsPerFile` and/or `MaxAttachmentTextCharsTotal` in `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs`.
2. Update the server tests in `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/ChatEndpointsAttachmentsTests.cs` (the `ChatEndpoints_Constants_MatchFr07Nfr04Spec` test locks the values).
3. **Verify LLM context budget**: the selected model's context window must accommodate the new total + system prompt + tool messages + reply tokens. Currently the platform uses Azure OpenAI GPT-4o-class models with 128K context.
4. Update this policy doc.
5. **Run NFR-01 publish-size verification**: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` and confirm ≤60 MB compressed. (Constant changes have negligible impact, but the discipline is non-negotiable per CLAUDE.md §10.)
6. Open §10 Placement Justification entry.

### To add a new MIME type

1. Add the new MIME to `ALLOWED_MIME_TYPES` in `useChatFileAttachment.ts` AND `AllowedAttachmentContentTypes` in `ChatEndpoints.cs`. The two MUST match.
2. Implement a client-side extractor (browser native if possible; lazy-loaded NPM library otherwise — NFR-12 dynamic-import rule).
3. Update the JSDoc on `ALLOWED_MIME_TYPES`.
4. Update the bundle-size budget per NFR-08 if a new NPM library is added (use the bundle audit script in `scripts/`).
5. Add extraction tests in `useChatFileAttachment.test.ts`.
6. Add allow-list tests in `ChatEndpointsAttachmentsTests.cs`.
7. Update this policy doc's "Current Policy" + "Allowed MIME types" rationale.

---

## Single-LLM-Call Invariant

Per R2 D-01 (lessons-learned), attachments MUST flow through exactly ONE chat message + ONE LLM call. The current architecture preserves this:

- Client extracts text inline in `useChatFileAttachment.ts`.
- Extracted text is passed in the `attachments[]` array on `POST /api/ai/chat/sessions/{id}/messages`.
- Server composes the final LLM prompt via `ComposeMessageWithAttachments()` — user message + structured attachment blocks — and dispatches a single LLM call.

**MUST NOT** introduce a separate pre-extraction round-trip to the BFF (e.g., a `/api/ai/chat/extract` endpoint that returns extracted text). This would double the request count and break the invariant.

---

## §10 Placement Justification (BFF Hygiene)

Per CLAUDE.md §10 and [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md), every BFF change must answer the decision criteria. For R4 A-4:

| Decision criterion (ADR-013 §"Decision Criteria") | Answer |
|---|---|
| Latency/TTFB <500ms vs BFF state? | YES → BFF |
| Writes to BFF-managed session/audit state in same request? | YES → BFF |
| Retroactive annotation of streaming response? | N/A (validation is pre-stream) |
| Event-driven (timer, queue, webhook)? | NO → BFF |

**Boundary preservation**:
- No new direct CRUD→AI dependencies — this is AI-internal code in `Api/Ai/`, NOT CRUD code consuming AI. No `IBffAiPublicContracts` facade is required.
- No new DI feature module (ADR-010) — validation logic is private static methods on the existing endpoint class.
- No new NuGet packages — pure constant updates.
- No new endpoints (ADR-001) — modifies the existing `POST /sessions/{id}/messages` handler.
- Endpoint filter (ADR-008) preserved — auth posture unchanged.

**Publish-size impact** (NFR-01): 0 net bytes (constants only). Verified per task 050 deployment notes.

**CVE impact** (NFR-09): 0 new packages, 0 new HIGH-severity findings.

---

## Change History

| Date | Change | Source |
|---|---|---|
| 2026-05-26 | Initial publish; client binary cap raised 10 MB → 25 MB. Server text caps unchanged. | R4 task 050 (A-4) — OC-R4-01 |

---

## Related

- [ADR-013 AI Architecture (refined 2026-05-20)](../../docs/adr/ADR-013-ai-architecture.md) — placement criteria
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — §10 BFF Hygiene
- [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../assessments/bff-ai-extraction-assessment-2026-05-20.md) — evidence base
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatFileAttachment.ts` — client gate
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` — server gate (`ValidateAttachments`)
