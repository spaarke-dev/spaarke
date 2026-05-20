# Spike 001: FR-07 Chat-Message Attachments Payload — Decision Memo

> **Task**: [`001-spike-fr07-attachments-payload.poml`](../../tasks/001-spike-fr07-attachments-payload.poml)
> **Date**: 2026-05-20
> **Investigator**: Claude Code (task-execute, STANDARD rigor)
> **Time spent**: ~10 min focused source-code investigation
> **Status**: ✅ Complete

---

## Decision

**NO** — the current `POST /api/ai/chat/sessions/{sessionId}/messages` endpoint does **NOT** accept an `attachments[]` field on its request body.

**→ Phase E status: REQUIRED.** Tasks 050 and 051 must execute.

---

## Evidence

### Route handler

[`src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs:72`](../../../../src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs#L72):

```csharp
// POST /api/ai/chat/sessions/{sessionId}/messages — send message, receive SSE stream
group.MapPost("/sessions/{sessionId}/messages", SendMessageAsync)
    .AddAiAuthorizationFilter()
    .RequireRateLimiting("ai-stream")
    .WithName("SendChatMessage")
    ...
```

Handler signature at [`ChatEndpoints.cs:278`](../../../../src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs#L278):

```csharp
private static async Task SendMessageAsync(
    string sessionId,
    ChatSendMessageRequest request,
    ChatSessionManager sessionManager,
    ChatHistoryManager historyManager,
    SprkChatAgentFactory agentFactory,
    PendingPlanManager pendingPlanManager,
    IChatClient chatClient,
    [FromServices] IWorkingDocumentService workingDocumentService,
    [FromServices] IMatterContextDetector matterContextDetector,
    [FromServices] IConversationHistorySanitizer conversationHistorySanitizer,
    [FromServices] CrossMatterSafetyTelemetry crossMatterTelemetry,
    HttpContext httpContext,
    ILogger<SprkChatAgentFactory> logger)
```

### Request DTO

[`src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs:2093-2096`](../../../../src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs#L2093-L2096):

```csharp
/// <summary>Request body for POST /sessions/{id}/messages.</summary>
/// <param name="Message">The user's message text.</param>
/// <param name="DocumentId">Optional document ID override (uses session's document if omitted).</param>
public record ChatSendMessageRequest(string Message, string? DocumentId = null);
```

**Two properties only**: `Message: string` and `DocumentId: string?`. **No `Attachments` property exists.**

### Agent invocation

[`ChatEndpoints.cs:549`](../../../../src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs#L549) confirms only `request.Message` is forwarded to the agent — no attachments are read from the request even if a JSON payload were to include them:

```csharp
await foreach (var update in agent.SendMessageAsync(request.Message, history, cancellationToken))
```

### Negative evidence

Searched all of [`src/server/api/Sprk.Bff.Api/Api/Ai/`](../../../../src/server/api/Sprk.Bff.Api/Api/Ai/) for `attachment`, `Attachment`, `IAttachment`, `ChatAttachment`:

- `ChatEndpoints.cs`: **0 matches** (case-insensitive)
- AI endpoints folder overall: **0 matches**

There is no partial implementation, no commented-out scaffold, no branch where this lives.

---

## Sample payload (target schema per FR-07)

The frontend (per FR-07, OC-07) intends to send:

```json
POST /api/ai/chat/sessions/{sessionId}/messages
Content-Type: application/json

{
  "message": "Compare the two attached contracts and highlight differences.",
  "documentId": "...optional...",
  "attachments": [
    {
      "filename": "contract-a.pdf",
      "contentType": "application/pdf",
      "textContent": "...extracted text from PDF.js..."
    },
    {
      "filename": "contract-b.docx",
      "contentType": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
      "textContent": "...extracted text from mammoth..."
    }
  ]
}
```

**Under the current DTO**, the `attachments` field would be silently dropped by System.Text.Json (which by default ignores unknown properties). The frontend would not get an error, but the agent would never see the attachments. This is a silent-failure mode and confirms Phase E is required.

---

## Phase E impact

**Phase E status: REQUIRED**

| Task | Status (post-spike) | Why |
|---|---|---|
| `050` Extend ChatEndpoints with `attachments[]` payload | 🔲 **active** | Add `Attachments` property to `ChatSendMessageRequest` record; enforce validation (max 5, max 10 MB per file, allowed MIME types per NFR-04); pass into agent context |
| `051` BFF unit tests for attachments[] payload | 🔲 **active** | Cover happy path (1, 3, 5 attachments) + each validation rejection (6+ rejected, oversize rejected, disallowed MIME rejected) |

Per **ADR-013**: extension MUST stay within `Sprk.Bff.Api` (in-process, no new service). ✅ Confirmed feasible — the existing record can be extended in place.

Per **ADR-008**: any modified endpoint inherits the existing endpoint-filter pipeline. The MapPost chain already applies `AddAiAuthorizationFilter()` + `RequireRateLimiting("ai-stream")` — these will continue to apply with the extended DTO. ✅ No bypass introduced.

---

## Recommended schema for task 050

Add a third positional record parameter:

```csharp
/// <summary>Request body for POST /sessions/{id}/messages.</summary>
/// <param name="Message">The user's message text.</param>
/// <param name="DocumentId">Optional document ID override (uses session's document if omitted).</param>
/// <param name="Attachments">Optional in-memory file attachments (text content extracted client-side). Max 5 entries.</param>
public record ChatSendMessageRequest(
    string Message,
    string? DocumentId = null,
    IReadOnlyList<ChatMessageAttachment>? Attachments = null);

/// <summary>In-memory attachment with client-extracted text content.</summary>
/// <param name="Filename">Original filename (display only).</param>
/// <param name="ContentType">Original MIME type.</param>
/// <param name="TextContent">Extracted text content (client-side via PDF.js, mammoth, or raw read).</param>
public record ChatMessageAttachment(
    string Filename,
    string ContentType,
    string TextContent);
```

Default of `null` preserves backwards-compatibility — existing clients (no `attachments` field) continue to work unchanged.

**Validation (task 050 + 051)**:
- `Attachments?.Count <= 5` → return 400 if violated (NFR-04, FR-07)
- Each `Attachment.ContentType` ∈ `{text/plain, text/markdown, application/pdf, application/vnd.openxmlformats-officedocument.wordprocessingml.document}` → return 400 if violated
- Each `Attachment.TextContent.Length` should have a sensible cap (e.g., ≤ 10 MB equivalent in characters — ~10M chars assuming 1 char ≈ 1 byte for ASCII; for safety, cap at ~2.5M chars to account for Unicode + LLM context budget)
- Sum of all `TextContent.Length` should also be capped (avoid 5 × 2.5M = 12.5M ballooning the LLM prompt)

**Agent integration**: pass attachments through to `agent.SendMessageAsync` — likely via a new overload or via prepending attachment text to `request.Message` with a structured prefix. **Task 050 decides** the exact integration approach; this spike does not.

---

## Follow-up notes for task 026 (frontend payload wiring)

- **Field casing**: System.Text.Json default is camelCase via `JsonSerializerOptions.PropertyNamingPolicy = CamelCase`. Confirm by checking `Program.cs` JSON options if uncertain. Use `attachments`, `filename`, `contentType`, `textContent` from the client.
- **Sequencing**: Task 026 should not merge until task 050 lands and is verified. Until then, the frontend `useChatFileAttachment` hook (task 024) builds attachments correctly and stores them in state, but `SprkChat.tsx` outbound payload should branch on a feature flag (or simply ship the field — it'll be silently ignored until 050 lands; the chips will appear but the AI reply won't reference attachment content). The conservative path: gate the wiring on task 050 completion.
- **Order of execution**: 001 → 024 (hook) → 050 → 051 → 026 (wire). The TASK-INDEX dependency graph already encodes this.

---

## Acceptance criteria check

| Criterion | Status |
|---|---|
| Memo exists at `notes/spikes/001-fr07-attachments-payload.md` | ✅ |
| Definitive YES/NO decision (not "inconclusive") | ✅ NO — endpoint does NOT accept attachments[] today |
| Cites DTO type name + file:line | ✅ `ChatSendMessageRequest` at `ChatEndpoints.cs:2096` |
| States Phase E impact (REQUIRED / SKIPPED) | ✅ REQUIRED |
| No files under `src/` modified | ✅ Read-only investigation |

---

*Decision: Phase E is REQUIRED. Tasks 050 and 051 remain active in the execution plan.*
