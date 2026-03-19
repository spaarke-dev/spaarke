# R2-054: Document Context Integration Findings

> **Date**: 2026-03-17
> **Task**: Document Context Injection Integration - End-to-End Verification

## Summary

Verified document context injection (R2-011) and multi-document aggregation (R2-012) end-to-end. Found one critical integration gap: conversation-aware chunk re-selection (FR-03) was not wired through to the `CreateAgentAsync` call. Fixed by adding `latestUserMessage` parameter flow.

## Issues Found and Fixed

### 1. Conversation-Aware Re-Selection Not Wired (CRITICAL)

**Problem**: `SprkChatAgentFactory.CreateAgentAsync` always passed `latestUserMessage: null` to `DocumentContextService.InjectDocumentContextAsync`, meaning document chunks were always selected by position (beginning-of-document) regardless of what the user asked about.

**Impact**: For documents exceeding the 30K token budget, asking about content in section 90 of a 200-section document would NOT surface section 90 content. The AI would only see the beginning sections.

**Fix**:
- Added `latestUserMessage` parameter to `CreateAgentAsync` (default: null for backwards compatibility)
- Threaded it through `EnrichWithDocumentContextAsync` and `EnrichWithMultiDocumentContextAsync`
- `ChatEndpoints.SendMessageAsync` now passes `request.Message` as `latestUserMessage`
- `ChatEndpoints.ApprovePlanAsync` extracts the last user message from session history

**Files Modified**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` — new parameter + threading
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` — both `SendMessageAsync` and `ApprovePlanAsync` updated

## Verified Working (Code Review)

### 2. Single-Document Context Injection (R2-011)
- `DocumentContextService.InjectDocumentContextAsync` correctly: downloads via SPE facade (ADR-007), extracts text, chunks at ~500 tokens, selects within 30K budget
- Position-based selection works for initial session creation (latestUserMessage=null)
- Embedding-based selection activates when latestUserMessage is provided and doc exceeds budget
- Soft failure: returns `DocumentContextResult.Empty` on any error (no crash)

### 3. Multi-Document Context Injection (R2-012)
- `InjectMultiDocumentContextAsync` correctly: proportional budget allocation (30K/N), bounded concurrency (SemaphoreSlim, max 5), leftover reallocation, cross-document interleaving by relevance score
- Maximum 20 documents supported; validated at endpoint level (max 5 additional = 6 total)
- Empty/failed documents gracefully produce empty groups without blocking others

### 4. Token Budget Enforcement (NFR-05)
- 30K document context budget verified (`DocumentContextService.MaxTokenBudget = 30_000`)
- `ComputeBudgetAllocations` enforces hard cap: total allocations never exceed 30K (scales down proportionally if needed)
- `InterleaveCrossDocument` re-checks total budget after per-document selection
- `AgentCostControlMiddleware` handles overall 128K session budget exceeded: returns polite limit message, not 500 error

### 5. Frontend Integration
- `SprkChat.tsx` state manages `additionalDocumentIds` with debounced context switch (300ms)
- `useChatSession.switchContext` passes `additionalDocumentIds` via PATCH to BFF
- `ChatEndpoints.SwitchContextAsync` stores in session; `SendMessageAsync` reads from session
- `SprkChatContextSelector` enforces max 5 additional documents in UI

### 6. ADR-015 Compliance (Data Governance)
- `DocumentContextService` logs only metadata: documentId, chunkCount, tokenCount, truncated flag
- `LogDebug` for character count of extracted text (acceptable — count only, no content)
- `LogWarning` for failures references documentId only
- `TruncationReason` strings contain only counts and budget numbers
- `FormatForSystemPrompt` includes document content in the prompt (required) but no logging of content

## Test Coverage Added

- `DocumentContextIntegrationTests.cs`: 14 unit tests covering chunking, formatting, budget enforcement, record behavior, ADR-015 compliance, graceful degradation

## Architecture Observations

- Agent is re-created on every `SendMessageAsync` call (by design — factory pattern). This means conversation-aware re-selection naturally works per-turn since the user's latest message flows to `DocumentContextService` on each call.
- The full document is re-downloaded and re-chunked on each message (no caching of chunks between turns). This is acceptable for now but could be optimized with a chunk cache (keyed by documentId + version) in a future task.
- Multi-document mode shares the same 30K budget, which at 5 documents means 6K per doc initially. For large documents, this limits context per doc to roughly 6K tokens (about 6 pages of text). Cross-document interleaving partially compensates by selecting the most relevant chunks globally.
