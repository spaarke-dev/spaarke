# Task 010 — `GET /api/documents/{id}/eml-render` endpoint contract

> Handoff for **task 033** (client sandboxed-iframe render branch) and the §10 PR record.
> Status: implemented + gated. FR-07 / NFR-01 / NFR-03. Do NOT open a PR from this note (orchestrator owns).

## Route

`GET /api/documents/{documentId}/eml-render`

- Group: `/api/documents` (`.RequireAuthorization()`).
- Per-document authz: `.AddDocumentAuthorizationFilter("read")` (ADR-008). **Fails closed** — unauthorized → 403, unauthenticated → 401, missing document/file → 404, all with **no HTML body**. This route is deliberately locked down tighter than its sibling `/download` (which relies on group auth only), because it is on the untrusted-email-HTML path.

## Responses

| Status | When | Body |
|---|---|---|
| 200 | Authorized; `.eml` resolved + parsed | `text/html; charset=utf-8` — **sanitized** HTML (see below) |
| 400 | `documentId` not a GUID | RFC 7807 ProblemDetails (`invalid_id`) |
| 401 | Unauthenticated | ProblemDetails — no HTML |
| 403 | Authenticated but not authorized for the document | ProblemDetails — no HTML |
| 404 | Document not found, or no `.eml` file in SPE (`file_not_found`) | ProblemDetails — **client degrades to `sprk_body`** |
| 409 | SPE pointers missing/partial (`ValidateSpePointers`) | ProblemDetails |

### 200 response headers

- `Content-Type: text/html; charset=utf-8`
- `Cache-Control: public, max-age=31536000, immutable` — the archived `.eml` is content-immutable (NFR-01), so repeat opens hit the HTTP cache. **No bespoke server-side render cache** was added.

## HTML contract (what task 033 receives)

The body is **already sanitized server-side** — this is the authoritative XSS boundary (NFR-03). Task 033's sandboxed iframe is **defense-in-depth on top**, not the primary control. Guarantees:

- No `<script>`, `<iframe>`, `<object>", `<embed>`, `<form>`, `<base>`, `<link>`, `<meta>`, `<frame>`/`<frameset>`.
- No `on*` event-handler attributes.
- URL schemes restricted to `http` / `https` / `mailto`, plus `data:` **only for inline images** (`data:image/...`); any other `data:` (e.g. `data:text/html`) is stripped.
- Inline `cid:` references (from the archived `multipart/related` parts written by `GraphMessageToEmlConverter`) are resolved to `data:image/...;base64,...` URIs. Unresolved `cid:` refs are dropped safely.
- Plain-text-only emails (no HTML part) are rendered as HTML-encoded text with line breaks preserved (`<br>`), never raw.

Client guidance for 033: render in a sandboxed iframe (e.g. `sandbox` without `allow-scripts`); on 404 fall back to `sprk_body`.

## Implementation

- Endpoint handler `GetEmlRender` + `BuildEmlRenderResponseAsync` + `SanitizedEmlHtmlResult` in `src/server/api/Sprk.Bff.Api/Api/FileAccessEndpoints.cs`.
- Pure renderer `EmlToHtmlRenderer` (reverse of `GraphMessageToEmlConverter`) in `src/server/api/Sprk.Bff.Api/Services/Communication/EmlToHtmlRenderer.cs`; DI singleton in `CommunicationModule`.
- Download reuses `SpeFileStore.DownloadFileAsync(driveId, itemId, ct)` (ADR-007) — no new facade method, no `GraphServiceClient`.

## Placement Justification (CLAUDE.md §10 / §11)

The `.eml` → sanitized-HTML render belongs **server-side**: MimeKit parsing + the `SpeFileStore` facade are server-side, and server sanitize is the single trusted point for untrusted email HTML (a client-only sanitizer is bypassable). It **reuses** the existing document-resolution + `DownloadFileAsync` + `DocumentAuthorizationFilter` pipeline (not forked). Exactly one new endpoint; one new pure internal class + one DI singleton; zero new CRUD→AI dependency (ADR-013 clean). Extension of `TextExtractorService` was rejected — it strips HTML for AI (opposite intent).

## BFF §10 gate evidence

- **New package**: `HtmlSanitizer` 9.1.973 (namespace `Ganss.Xss`) — owner-approved **Path-A exception** under CLAUDE.md §6.5 (BFF had no HTML sanitizer, direct or transitive; regex-sanitizing an XSS boundary is an anti-pattern). MimeKit already referenced (no new package for parsing).
- **Publish size**: **48.08 MB compressed** (`Compress-Archive` of `deploy/api-publish`) — well under the 60 MB ceiling. New-package footprint is deterministic: `AngleSharp.dll` 1006 KB + `AngleSharp.Css.dll` 574 KB + `HtmlSanitizer.dll` 51 KB ≈ **1.63 MB uncompressed** (~<1 MB compressed) — under the +5 MB single-task threshold.
- **CVE**: `dotnet list package --vulnerable --include-transitive` — **0 NEW HIGH** from this task. `HtmlSanitizer`/`AngleSharp` are not in the vulnerable list. (The one HIGH shown, `System.Security.Cryptography.Xml 8.0.3`, is a pre-existing JWT/identity-stack transitive pinned before this task — out of scope.)
- **Build**: `dotnet build src/server/api/Sprk.Bff.Api/ -c Release` green (0 errors).
- **Tests**: `EmlToHtmlRendererTests` (8), `EmlRenderResponseTests` (3), `EndpointGroupingTests.EmlRenderEndpoint_Unauthenticated_Returns401AndLeaksNoHtml` (1) — all pass; real `.eml` fixtures, no `Mock<HttpMessageHandler>` (ADR-038).

## Follow-up (test-diet / 090 wrap-up)

Renderer tests are pure-domain (parse/sanitize) and could relocate to `tests/unit/domain/**`; the fail-closed 401 assertion is a contract test that could relocate to `tests/integration/contract/**`. Kept beside the sibling `DocumentDownloadEndpointTests` (same `tests/unit/Sprk.Bff.Api.Tests/` placement) per the task instruction; flag for the `/test-diet` KEEP-path pass.
