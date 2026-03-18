# R2-050: SSE Streaming End-to-End Integration Findings

> **Date**: 2026-03-17
> **Status**: Complete

## Integration Verification Summary

The BFF SSE endpoint (R2-010) and frontend useSseStream hook (R2-031) are **already correctly wired** from their respective task implementations. This integration task verified end-to-end compatibility and applied one fix.

## Verification Checklist

### URL Matching
- BFF endpoint: `POST /api/ai/chat/sessions/{sessionId}/messages`
- Frontend URL: `${baseUrl}/api/ai/chat/sessions/${session.sessionId}/messages`
- **Result**: Match confirmed. URL normalization in `handleSend` strips trailing slashes and `/api` suffix to prevent double-prefix.

### SSE Event Format
- BFF emits: `data: {"type":"token","content":"..."}\n\n` via `WriteChatSSEAsync`
- Frontend parses: `parseSseEvent()` strips `data: ` prefix, JSON-parses payload
- **Result**: Format compatible. camelCase JSON serialization (BFF `JsonNamingPolicy.CamelCase`) matches frontend expectations.

### SSE Event Types Handled

| Event Type | BFF Emits | Frontend Handles | Verified |
|-----------|-----------|-----------------|----------|
| `typing_start` | Before AI generation | Sets `isTyping=true`, shows `SprkChatTypingIndicator` | Yes |
| `token` | Per-token content | Hides typing, accumulates content via `setContent` | Yes |
| `typing_end` | After last token | Sets `isTyping=false` | Yes |
| `suggestions` | After response complete | Parsed via `parseSuggestions()`, shown as chips | Yes |
| `citations` | After response, before done | Mapped via `mapSseCitations()`, passed to messages | Yes |
| `plan_preview` | On compound intent | Stores `pendingPlanId`/`pendingPlanData` | Yes |
| `done` | Stream complete | Sets `isDone=true`, stops streaming | Yes |
| `error` | On exception | Throws Error, caught by error handler, shown in banner | Yes |

### Cancellation Flow
- Frontend: `AbortController.abort()` on cancel button click
- Fetch: Throws `AbortError` (DOMException), caught and handled gracefully
- BFF: `httpContext.RequestAborted` CancellationToken fires, triggers `OperationCanceledException`
- BFF logs: "Client disconnected during SendMessage" (clean close, no error event sent)
- **Result**: Cancellation propagates correctly end-to-end.

### SSE Headers
- `Content-Type: text/event-stream` -- confirmed in `SendMessageAsync`
- `Cache-Control: no-cache` -- confirmed
- `Connection: keep-alive` -- confirmed
- `X-Accel-Buffering: no` -- confirmed (prevents reverse proxy buffering)

### Typing Indicator
- Rendered when `isTyping && !streamedContent` -- shows between `typing_start` and first token
- Hidden automatically when first `token` event arrives (sets `isTyping=false`)
- **Result**: Correct behavior for NFR-01 visual feedback.

## Fix Applied

### 429 Rate Limit User-Friendly Message

**File**: `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useSseStream.ts`

**Issue**: The generic error handler showed raw HTTP status: `Chat request failed (429): {body}` which is not user-friendly per ADR-016 constraint.

**Fix**: Added specific 429 detection before the generic error throw:
```typescript
if (response.status === 429) {
  throw new Error('You are sending messages too quickly. Please wait a moment and try again.');
}
```

This ensures the error banner displays a clear, actionable message instead of a technical HTTP status code.

## NFR-01: First Token Latency

The architecture supports sub-500ms first-token latency:
1. `typing_start` is emitted immediately before the AI streaming loop begins
2. `X-Accel-Buffering: no` prevents proxy buffering
3. First `token` event hides typing indicator and shows content

Actual latency depends on Azure OpenAI model response time (typically 200-400ms for GPT-4o streaming). Network latency between client and BFF is the only additional factor.

## No Other Issues Found

All SSE event types, headers, cancellation, error propagation, and content accumulation are correctly implemented across both layers.
