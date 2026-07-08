# Task 025 — R7 Tactical Branch Disposition (FR-P1-06)

> **Date**: 2026-07-05 · **Full record**: [`../../spaarke-ai-platform-unification-r7/notes/branch-closure-2026-07-05.md`](../../spaarke-ai-platform-unification-r7/notes/branch-closure-2026-07-05.md)

## Summary

The redesign branch was **forked from the r7 tip** (`75e94fe4c` = merge-base), so
all four keep-fixes were already inherited by ancestry — zero cherry-picks. The
dispatch-patch content that came along with the fork was **deleted in place**:

**Kept (verified present, no action needed)**
1. Session-id fix (`ab8ab68a8`) — `useChatSession.resumeSession` + AiSessionProvider persistence
2. ExtractedText persistence (`5ab21578b`) — `ChatSession.ExtractedText` + `SessionFileTextSource`
3. Auto-promote ready chips (`68e8b96f1`) — ConversationPane
4. field_delta synthesis (`2d4e0c8d8`) — `sseToPaneEventBridge.ts` `case 'complete'`

**Dropped (deleted by this task)**
- `TryDetectExplicitConsumerType` + `SummarizeKeywordRegex` + keyword bypass block — `ChatEndpoints.cs` (ADR-039: no second intent-detection mechanism)
- `linear_dispatch` SSE set — `LinearDispatchSseEvent.cs` (file), `ChatSseEventFactory.CreateLinearDispatchEvent`, shared-lib `ILinearDispatchPayload` / union member / `useSseStream` branch / `SprkChat` forwarding, ConversationPane handler + prop
- `executeLinearDispatch.ts` (file)
- Diagnostic logs: the "Wave 12.3 keyword-check" log (ChatEndpoints, part of bypass) AND the `[LinearDispatch]` config-dump log in `AnalysisEndpoints.cs` (diag commit `2d861eb6a`, was already on master)

## Empty-attachments guard handoff (BEHAVIOR, not code)

The dropped code carried a guard: **never fire a file-consuming dispatch with an
empty session-file list** (transient pre-hydration window → empty-fileIds POST →
empty text → visible stream error; fall back gracefully instead).

- **Task 022** owns the Event-path precondition
- **Task 023** owns the Click-path (`dispatchConsumer` helper) precondition

## Grep-zero (src/ + tests/, case-insensitive)

`linear_dispatch|LinearDispatch|TryDetectExplicitConsumerType|executeLinearDispatch|ILinearDispatchPayload|onLinearDispatch|setOnLinearDispatch` → **0 hits**.
Remaining mentions are decision-record docs only (project notes, POMLs, the
architecture doc's disposition table rows recording "DEL — never merges").

## Coordination notes for wave W-P1-A

- Agent/task **020** (SummarizeSessionEndpoint / SessionSummarizeOrchestrator dual-path rewrite): **no line overlap** — task 025 touched `ChatEndpoints.cs`, `AnalysisEndpoints.cs`, `ChatSseEventFactory.cs`, shared-lib SprkChat files, `ConversationPane.tsx`; none of 020's files. Test-assembly compile currently blocked by 020's in-flight `WorkspaceOptions.ChatSummarizePlaybookId` removal (`WorkspaceOptionsValidatorTests.cs`) — owned by 020.
- Task **023**: do not resurrect `executeLinearDispatch.ts`; the wave-end grep sweep covers both tasks' deletions.
