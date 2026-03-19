# R2-066 Security Audit Report

**Date**: 2026-03-17
**Auditor**: Claude Code (task-execute R2-066)
**Scope**: All SprkChat Platform Enhancement R2 changes
**Status**: PASS (with fixes applied)

---

## 1. DOMPurify Sanitization Audit

**Result: PASS**

All `dangerouslySetInnerHTML` usages in SprkChat components are protected.

### SprkChat Components (5 usages)

| File | Line | Protection |
|------|------|------------|
| `SprkChatMessageRenderer.tsx` | 348 | `renderMarkdownHtml()` -> `DOMPurify.sanitize()` |
| `SprkChatMessageRenderer.tsx` | 383 | `renderCitationsHtml()` -> `renderMarkdownHtml()` -> `DOMPurify.sanitize()` |
| `SprkChatMessageRenderer.tsx` | 437 | `renderMarkdownHtml()` -> `DOMPurify.sanitize()` |
| `SprkChatMessageRenderer.tsx` | 538 | `renderMarkdownHtml()` -> `DOMPurify.sanitize()` |
| `SprkChatMessage.tsx` | 565 | `renderMarkdownHtml()` -> `DOMPurify.sanitize()` |

### DiffCompareView Components (3 usages)

| File | Line | Protection |
|------|------|------------|
| `DiffCompareView.tsx` | 441 | `computeHtmlDiff()` -> `escapeHtml()` on all text tokens |
| `DiffCompareView.tsx` | 447 | `computeHtmlDiff()` -> `escapeHtml()` on all text tokens |
| `DiffCompareView.tsx` | 471 | `computeHtmlDiff()` -> `escapeHtml()` on all text tokens |

### renderMarkdown.ts Security Design

The shared `renderMarkdown()` function at `src/client/shared/.../services/renderMarkdown.ts` applies:
1. `marked.parse()` for markdown-to-HTML conversion
2. `DOMPurify.sanitize()` with explicit ALLOWED_TAGS and ALLOWED_ATTRS whitelists
3. Tags like `<script>`, `<iframe>`, `<object>`, `<embed>` are stripped
4. Event handler attributes (`onclick`, `onerror`, `onload`) are stripped
5. `javascript:` protocol in href is neutralized

**Test**: `renderMarkdown.security.test.ts` — 15 test cases covering script injection, event handlers, iframe, object/embed, javascript: protocol, style attacks, and legitimate content preservation.

---

## 2. BroadcastChannel Auth Token Audit

**Result: PASS**

`SprkChatBridge.ts` transmits only typed domain payloads. No auth-related fields exist in any event payload type.

### Event Payloads Audited

| Event | Payload Fields | Auth Tokens? |
|-------|---------------|-------------|
| `document_stream_start` | operationId, targetPosition, operationType | None |
| `document_stream_token` | operationId, token (content word), index | None |
| `document_stream_end` | operationId, cancelled, totalTokens | None |
| `document_replaced` | operationId, html, previousVersionId | None |
| `reanalysis_progress` | operationId, percent, message | None |
| `selection_changed` | text, startOffset, endOffset, context | None |
| `context_changed` | entityType, entityId, playbookId | None |

### Defense in Depth
- File header (line 10): `SECURITY: Auth tokens MUST NEVER be transmitted via this bridge.`
- postMessage transport validates `event.origin` against configured `allowedOrigin` (line 184)
- TypeScript discriminated union types enforce typed payloads at compile time

**Test**: `SprkChatBridge.security.test.ts` — verifies all 7 payload types contain no auth-related fields, validates event map completeness.

---

## 3. Document Logging Audit (ADR-015)

**Result: FAIL -> FIXED**

### Violations Found and Fixed

| File | Line | Violation | Fix |
|------|------|-----------|-----|
| `IntentClassificationService.cs` | 50 | Logged full user message: `{Message}` | Changed to `(length={MessageLength})` |
| `AiPlaybookBuilderService.cs` | 284 | Logged full builder message: `{Message}` | Changed to `(length={MessageLength})` |
| `AiPlaybookBuilderService.cs` | 605 | Logged full user message: `{Message}` | Changed to `(length={MessageLength})` |
| `AiPlaybookBuilderService.cs` | 826 | Logged full AI response JSON: `{Response}` | Changed to `(length={ResponseLength})` |
| `AnalysisOrchestrationService.cs` | 176-179 | Logged 200-char document text preview | Removed textPreview; log only `Length={TextLength}` |
| `AnalysisOrchestrationService.cs` | 187 | Logged document extraction fallback text | Changed to `(textLength={TextLength})` |
| `DocumentClassifierHandler.cs` | 703 | Logged full classification response: `{Response}` | Changed to `(length={ResponseLength})` |

### Compliant Logging (Verified Clean)
- `DocumentContextService.cs` — logs only document ID, chunk counts, token counts
- `ChatSessionManager.cs` — logs only session ID, tenant ID, document ID
- `ChatHistoryManager.cs` — logs only role, session ID, sequence number
- `AgentCostControlMiddleware.cs` — logs only playbook ID, token counts, budget
- `TempBlobStorageService.cs` — logs only blob name, file size, session ID

---

## 4. Session Cleanup Audit (NFR-06)

**Result: PASS**

`TempBlobStorageService.DeleteSessionDocumentsAsync()` physically deletes all blobs:
1. Lists all blobs with prefix `{sessionId}/` (session-scoped isolation)
2. Calls `BlobClient.DeleteIfExistsAsync()` on each blob (physical deletion)
3. Logs the count of deleted documents

The method uses Azure Blob prefix listing to find ALL session-scoped files, ensuring none are missed. Additionally, Azure lifecycle management provides 24-hour auto-expiry as a safety net.

**Test**: `SessionCleanupSecurityTests.cs` — verifies all session blobs are physically deleted, validates zero-upload cleanup, file size enforcement, and tenant-scoped cache keys.

---

## 5. Token Budget Enforcement Audit

**Result: PASS**

### AgentCostControlMiddleware (per-session enforcement)
- Checks `_sessionTokenCount >= _maxTokenBudget` BEFORE every `SendMessageAsync` call (line 73)
- When exceeded: returns polite limit message, does NOT forward to inner agent
- Token estimation: `charCount / 4` heuristic, accumulated per response
- Default budget: 10,000 tokens per session (configurable per playbook)

### DocumentContextService (128K context window allocation)
- 30K token budget for document context (`MaxTokenBudget = 30_000`)
- Per-chunk budget enforcement during selection (lines 572-579, 607-615)
- Multi-document: proportional allocation with leftover redistribution
- Hard cap: total allocations must not exceed `MaxTokenBudget` (line 536)

### Context Window Budget Breakdown
- 8K: Playbook context (PlaybookChatContextProvider)
- 30K: Document context (DocumentContextService)
- ~40K: Conversation history
- ~50K: Response buffer
- Total: ~128K (matches model context window)

---

## 6. Additional Security Findings

### 6a. File Upload Security
- 50MB max file size enforced at upload (`MaxFileSizeBytes = 50 * 1024 * 1024`)
- File name sanitization via `SanitizeFileName()` — removes invalid characters, limits to 100 chars
- SAS URLs are read-only with 24-hour expiry
- Blob container has `PublicAccessType.None` (no anonymous access)

### 6b. SSE Streaming
- No auth tokens in SSE event data (streaming sends only content tokens)
- Endpoint uses `text/event-stream` content type
- Token budget middleware wraps the streaming agent

### 6c. Cache Key Security (ADR-014)
- Pattern: `chat:session:{tenantId}:{sessionId}` — tenant-scoped
- Sliding 24-hour TTL prevents stale session retention
- `BuildCacheKey()` is centralized (single definition point)

### 6d. Xrm.Navigation Parameters
- `SprkChatMessageRenderer` delegates navigation via `onNavigate` callback
- Component does NOT call `Xrm.Navigation` directly (ADR-012 compliant)
- Entity IDs are typed GUIDs, not user-supplied strings

---

## Test Artifacts Created

| Test File | Framework | Coverage |
|-----------|-----------|----------|
| `renderMarkdown.security.test.ts` | Jest | XSS prevention (15 cases) |
| `SprkChatBridge.security.test.ts` | Jest | ADR-015 no-auth-token (9 cases) |
| `SessionCleanupSecurityTests.cs` | xUnit + FluentAssertions | Session cleanup, cache keys (4 cases) |

---

## Summary

| Audit Area | Initial Status | Final Status | Action |
|-----------|---------------|-------------|--------|
| DOMPurify sanitization | PASS | PASS | No changes needed |
| BroadcastChannel auth tokens | PASS | PASS | No changes needed |
| Document content logging | **FAIL** | PASS | Fixed 7 logging violations |
| Session cleanup | PASS | PASS | No changes needed |
| Token budget enforcement | PASS | PASS | No changes needed |
| File upload security | PASS | PASS | No changes needed |

**Overall: PASS** — All identified gaps were fixed in this task.
