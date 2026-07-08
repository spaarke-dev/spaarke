# Agent findings — r7 branch delta vs master (auditor 1/7, 2026-07-05)

Merge-base `12c62b711`. Delta = single work-package (Wave 12.3 Phase 12.3a) + architecture/close-plan docs.
Theme: converge NL + slash + button summarize onto one server-side deterministic dispatch path
(`linear_dispatch`), fix session-id desync, close AI Search index-catchup race, render summarize output.

## Verified keep/retire classification

### Session — sound, KEEP
| File | Delta |
|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx` | `clearChatSession()` + `removeSession()` (clears localStorage AND sessionStorage) |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatSession.ts` | `resumeSession(id)`; `loadHistory()` → `{ok, staleSession?}` (**breaking change to hook result type** — only SprkChat consumes today) |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx` | mount = resume-not-create; stale → recreate + `onSessionCreated`. Fixes upload-in-A/message-to-B double-session bug |
| `src/server/api/Sprk.Bff.Api/Api/Ai/ChatDocumentEndpoints.cs` | persist `ExtractedText` at upload (write side of index-race fix) |
| `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatSession.cs` | nullable `ChatSessionFile.ExtractedText` |
| `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/SessionFileTextSource.cs` | inline-text-first read, RAG fallback; `BuildMultiFileInlineText()` keeps SUM-CHAT@v1 prompt stable |

### Consumer / Widget-routing — sound, KEEP
| File | Delta |
|---|---|
| `src/solutions/SpaarkeAi/src/components/conversation/sseToPaneEventBridge.ts` | synthesizes `streaming_started` + per-property `field_delta` from terminal `complete` chunk (ADR-030-compliant) |
| `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` | mixed: session wiring + auto-promote effect (retry-on-5xx, max 2) = KEEP; `handleLinearDispatch` wiring + retired `matchIntent`/`executeSummarizeIntent` NL branch = dispatcher-at-risk |

### Dispatcher — CONFIRMED flagged for retirement
| File | Delta |
|---|---|
| `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` | `TryDetectExplicitConsumerType` + `SummarizeKeywordRegex` (typo-tolerant, single-consumer hardcoded; self-documented as "Phase 12.4: replace with `sprk_analysisplaybook.sprk_intenttriggers` lookup") + inline `linear_dispatch` emission block |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/LinearDispatchSseEvent.cs` (NEW) | wire contract; semantic opposite of `playbook_options` (auto-execute vs must-click) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/ChatSseEventFactory.cs` | `CreateLinearDispatchEvent` factory — retire with event |
| `src/solutions/SpaarkeAi/src/components/conversation/executeLinearDispatch.ts` (NEW) | client executor (widget_load + POST + SSE bridge); duplicates `executeSummarizeIntent.ts`; `resolveWidgetConfig` hardcodes `chat-summarize` only |
| `src/client/shared/Spaarke.UI.Components/src/hooks/useSseStream.ts` | `linear_dispatch` parse case + callback ref — retire the case with the event |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/types.ts` | mixed: session types KEEP; `ILinearDispatchPayload` + `linear_dispatch` variant retire with event |

### Non-AI
`Spaarke.Compose.Components/src/index.ts` + `ComposeToolbar.tsx` — Prettier churn only.

## Four things the handoff under-emphasized

1. **Debug scaffolding in ChatEndpoints**: unconditional `LogInformation` ("Wave 12.3 keyword-check…")
   explicitly annotated "Remove after root cause identified" — must not reach master as-is.
2. **No revert path**: the retired client NL branch fixed a real double-dispatch race (two parallel
   /summarize streams). The redesign must REPLACE the dispatch mechanism, not revert.
3. **ADR-028 divergence**: `executeLinearDispatch.ts` uses bare `fetch` + manual Bearer header instead
   of `authenticatedFetch` — flag regardless of retirement decision.
4. **Edge case to preserve**: the empty-`sessionAttachmentIds` guard (server falls through to Phase B
   `playbook_options`; client skips) encodes a real empty-file race the redesign must still handle.
