# Lessons Learned — SprkChat Platform Enhancement R2

> **Date**: 2026-03-17
> **Project**: ai-sprk-chat-platform-enhancement-r2
> **Tasks**: 50 tasks across 6 phases
> **Duration**: Single sprint (2026-03-17)

---

## 1. Factory Pattern for DI Budget Compliance (ADR-010)

**Challenge**: The AiModule already had 16 non-framework DI registrations, exceeding the ADR-010 ceiling of 15. R2 introduced 8+ new services (PlaybookDispatcher, DocumentContextService, WebSearchTools, ScopeCapabilitiesService, etc.) that would have pushed registrations far beyond the limit.

**Solution**: Used `SprkChatAgentFactory.ResolveTools()` to instantiate tool handlers directly rather than registering them in the DI container. The factory pattern creates tools on-demand with constructor injection of already-registered dependencies (IChatClient, IDistributedCache, etc.), keeping the DI registration count flat.

**Lesson for R3**: Any new AI tool handler should follow the factory instantiation pattern. Reserve DI registrations for cross-cutting infrastructure services only (caching, auth, logging). The factory pattern also improved testability since tools can be instantiated with mock dependencies in unit tests without configuring a full DI container.

---

## 2. SSE Event Type Design and Streaming Architecture

**Challenge**: R1 used a simple request-response pattern for chat. R2 needed token-by-token streaming with multiple event types (typing indicators, tokens, citations, suggestions, plan previews, errors, done signals) all flowing through a single SSE connection.

**Solution**: Defined a typed discriminated union of SSE event types (`typing_start`, `token`, `typing_end`, `suggestions`, `citations`, `plan_preview`, `done`, `error`). The BFF emits these via `WriteChatSSEAsync` with camelCase JSON serialization, and the frontend `useSseStream` hook parses them with a switch dispatcher. The `X-Accel-Buffering: no` header prevents reverse proxy buffering.

**Key findings**:
- `typing_start` must be emitted immediately before the AI call, not after, to ensure sub-500ms visual feedback (NFR-01).
- Cancellation propagation via `AbortController.abort()` (frontend) to `httpContext.RequestAborted` (BFF) works cleanly end-to-end.
- Streaming tokens must NOT be cached in Redis (ADR-014) — only the final assembled content is cached.

**Lesson for R3**: The SSE event type system is extensible. New event types (e.g., `progress`, `tool_call`, `function_result`) can be added without breaking existing clients as long as the frontend `parseSseEvent` switch has a default case that ignores unknown types.

---

## 3. Two-Stage Semantic Matching for Playbook Dispatch

**Challenge**: Natural language intent ("analyze this contract for risks") needs to be matched to the correct playbook from a catalog of potentially hundreds. Simple keyword matching is too brittle; full LLM classification on every message is too expensive.

**Solution**: Implemented a two-stage matching pipeline:
1. **Stage 1 — Vector similarity** (AI Search): Embed the user message with `text-embedding-3-large`, query the `playbook-embeddings` index for top-5 candidates by cosine similarity. This is fast (~50ms) and filters the candidate set.
2. **Stage 2 — LLM refinement**: Send the top-5 candidates with their trigger metadata to GPT-4o for final selection and parameter extraction. The LLM sees playbook descriptions, example triggers, and parameter schemas, returning a structured JSON response with the matched playbook ID and extracted parameters.

**Key findings**:
- Dedicated `playbook-embeddings` AI Search index (separate from document RAG) was the right call — different embedding strategies and update frequencies.
- Dataverse fields (`sprk_triggerphrases`, `sprk_matchingkeywords`) for discovery + JPS JSON for execution is a clean two-layer architecture.
- Fallback to keyword matching when vector similarity scores are below threshold prevents false positives.

**Lesson for R3**: The two-stage pattern generalizes well. Any intent-to-action matching (not just playbooks) can use this approach. Consider a shared "semantic dispatcher" service that playbooks, slash commands, and scope capabilities all route through.

---

## 4. BroadcastChannel Write-Back Pattern

**Challenge**: SprkChat (running in a side panel or embedded control) needs to update the Lexical editor (running in the main form) with AI-generated content. These are separate React trees with no shared state.

**Solution**: Used the browser `BroadcastChannel` API via `SprkChatBridge.ts` to establish a typed message bus between SprkChat and the editor. Write-back content streams token-by-token via SSE (`document_stream_start`, `document_stream_token`, `document_stream_end` events), and the bridge relays each token to the editor for real-time insertion.

**Security constraint**: Auth tokens must NEVER traverse the BroadcastChannel (enforced by file-header comment, TypeScript discriminated union types, and dedicated security tests).

**Key findings**:
- `postMessage` origin validation is essential — the bridge checks `event.origin` against a configured allowedOrigin.
- The `document_replaced` event (for full content replacement) and `document_stream_*` events (for incremental streaming) serve different use cases and both were needed.
- BroadcastChannel works across same-origin tabs/iframes but not cross-origin — this matches the Dataverse form hosting model.

**Lesson for R3**: The bridge pattern is reusable for any cross-component communication within Dataverse forms. Consider extending it for real-time collaboration signals if multi-user editing is added in R3.

---

## 5. Security Audit Findings — ADR-015 Logging Violations

**Challenge**: The R2-066 security audit discovered 7 ADR-015 violations where document text, user messages, or AI response content was being logged in full. These were spread across 4 different service files and had been introduced incrementally across multiple tasks.

**Violations fixed**:
- `IntentClassificationService.cs` — logged full user message
- `AiPlaybookBuilderService.cs` — logged full builder message, user message, and AI response JSON (3 violations)
- `AnalysisOrchestrationService.cs` — logged 200-char document text preview and fallback text (2 violations)
- `DocumentClassifierHandler.cs` — logged full classification response

**Pattern applied**: Replace content logging with length-only metadata: `{Message}` becomes `(length={MessageLength})`, `{Response}` becomes `(length={ResponseLength})`.

**Lesson for R3**: ADR-015 violations tend to accumulate during rapid development when developers add debug logging. The security audit (R2-066) caught all 7 violations, but earlier detection would have been better. Consider adding a pre-commit hook or Roslyn analyzer that flags `ILogger` calls containing `{Message}`, `{Content}`, `{Text}`, `{Body}`, or `{Response}` template parameters.

---

## 6. Parallel Execution Groups — What Worked Well

**Achievement**: R2 organized 50 tasks into 5 parallel execution groups (A through E) plus sequential phases. Groups A+B (BFF API) and C+D+E (Frontend UI) could run concurrently after Phase 1 completed, with Phase 4 (Integration) joining the two halves.

**What worked**:
- **File ownership boundaries** prevented merge conflicts. Each group owned specific directories: Group A owned `Services/Ai/Chat/`, Group B owned `PlaybookDispatcher.cs` and related files, Group C owned `SprkChatMessage*.tsx`, etc.
- **Dependency-driven scheduling** via TASK-INDEX.md parallel columns and dependency lists made it clear which tasks could run simultaneously.
- **Phase gates** (Deploy Phase 1 before Phase 2/3, Deploy Phase 2+3 before Phase 4) provided natural integration checkpoints.

**What could improve**:
- Phase 5 testing tasks (060-066) were all independent and could have run as a single parallel group rather than sequentially.
- The TASK-INDEX.md format worked for tracking but required manual updates — an automated status tracker would reduce overhead.

**Lesson for R3**: The parallel group pattern with file ownership boundaries should be the default for large projects. Plan task decomposition around file/directory ownership first, then optimize for dependency minimization.

---

## 7. Tenant-Scoped Caching and Redis Patterns (ADR-009, ADR-014)

**Challenge**: Multiple R2 services needed caching (session state, document context chunks, playbook embeddings, command catalogs) with tenant isolation and appropriate TTLs.

**Solution**: Centralized cache key construction using the pattern `chat:{service}:{tenantId}:{resourceId}` with `IDistributedCache` (Redis). Key patterns:
- Session state: `chat:session:{tenantId}:{sessionId}` — 24h sliding TTL
- Document chunks: `chat:chunks:{tenantId}:{documentId}:{turnHash}` — 1h TTL (conversation-aware)
- Command catalog: `chat:commands:{tenantId}:{contextHash}` — 15min TTL (dynamic, context-dependent)

**Key findings**:
- Tenant scoping in cache keys is non-negotiable for multi-tenant security.
- Streaming tokens (individual SSE events) must NOT be cached — only the final assembled response.
- Command catalog caching with a short TTL (15min) balances responsiveness with the cost of re-resolving dynamic commands.

**Lesson for R3**: The centralized `BuildCacheKey()` pattern prevented cache key collisions and ensured consistent tenant scoping. Standardize this as a shared utility if not already done.

---

## 8. Test Coverage Summary

R2 produced approximately 80+ new tests across 6 test files:

| Test File | Framework | Test Count | Coverage Area |
|-----------|-----------|------------|---------------|
| `SseStreamingIntegrationTests.cs` | xUnit + NSubstitute | 18 | SSE latency, cancellation, errors, rate limits, cache |
| `PlaybookDispatcherTests.cs` | xUnit + NSubstitute | ~15 | Semantic matching, parameter extraction, fallback |
| `DocumentContextIntegrationTests.cs` | xUnit + NSubstitute | ~12 | Multi-doc aggregation, token budgets, chunk selection |
| `renderMarkdown.security.test.ts` | Jest | 15 | XSS prevention, DOMPurify sanitization |
| `SprkChatBridge.security.test.ts` | Jest | 9 | ADR-015 no-auth-token, event type completeness |
| `SessionCleanupSecurityTests.cs` | xUnit + FluentAssertions | 4 | Blob cleanup, cache keys, file size limits |

**Lesson for R3**: Security-focused test files (renderMarkdown.security.test.ts, SprkChatBridge.security.test.ts) proved their value by catching regressions and documenting security invariants. Every new surface that handles user content or cross-component communication should have a dedicated security test file.

---

## Summary — Top Takeaways for R3 Planning

1. **Factory pattern over DI registration** — keeps ADR-010 compliance sustainable as the tool catalog grows.
2. **SSE event type system is extensible** — design new features (tool calls, progress tracking) as new event types.
3. **Two-stage semantic matching generalizes** — consider a shared dispatcher for all intent-to-action routing.
4. **Security audit should run earlier** — ADR-015 logging violations accumulated silently across tasks.
5. **Parallel execution with file ownership** — decompose tasks by directory ownership for maximum concurrency.
6. **Dedicated security test files** — document and enforce security invariants in test code, not just comments.
7. **Tenant-scoped caching with centralized key construction** — prevents key collisions and ensures isolation.
8. **BroadcastChannel bridge is reusable** — extend for future cross-component communication needs.

---

*Generated as part of R2-090 project wrap-up.*
