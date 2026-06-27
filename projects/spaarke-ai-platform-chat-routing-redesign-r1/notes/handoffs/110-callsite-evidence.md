# Task 110 — PlaybookDispatcher.DispatchAsync Call-Site Evidence

> **Date**: 2026-06-24
> **Task**: 110 — Extend `PlaybookDispatcher.DispatchAsync` to accept optional `IReadOnlyList<ChatMessageAttachment>? attachments`
> **Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1`
> **Rigor**: FULL (per task POML — tags include `bff-api` + `services` + `ai`; backward-compat invariant)

## Pre-change call-site enumeration

Search command:

```
Grep "DispatchAsync" --type=cs
```

Filtered to `PlaybookDispatcher.DispatchAsync` (not the unrelated `DispatchAsync` private methods inside the handler classes):

### Production call sites (1)

| File | Line | Pre-change call | Post-change call |
|---|---|---|---|
| `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` | 572 | `dispatcher.DispatchAsync(request.Message, session.HostContext, cancellationToken)` | `dispatcher.DispatchAsync(request.Message, session.HostContext, cancellationToken, attachments: request.Attachments)` |

### Test call sites (10 across 2 files)

| File | Lines | Style | Action |
|---|---|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookDispatcherIntegrationTests.cs` | 130, 161, 184, 210, 237, 264 | Positional or `hostContext: null` named-arg style | **No change required** — optional parameter at the end of the signature defaults to `null`, preserving existing behavior. |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookDispatcherDestinationTests.cs` | 141, 179, 220, 289 | Positional + `hostContext` named-arg | **No change required** — same default-parameter mechanic preserves behavior. |

### Other "`DispatchAsync`" hits (NOT in scope — different methods)

Grep returns `DispatchAsync` matches in several other contexts that are **distinct private methods** with no signature relationship to `PlaybookDispatcher.DispatchAsync`:

| File | Method | Note |
|---|---|---|
| `Services/Ai/Handlers/AnalysisQueryHandler.cs` | `private async Task<ToolResult> DispatchAsync(...)` | Tool handler dispatch, no relation |
| `Services/Ai/Handlers/DocumentSearchHandler.cs` | `private async Task<ToolResult> DispatchAsync(...)` | Tool handler dispatch, no relation |
| `Services/Ai/Handlers/CodeInterpreterHandler.cs` | `private async Task<ToolResult> DispatchAsync(...)` | Tool handler dispatch, no relation |
| `Services/Ai/Handlers/KnowledgeRetrievalHandler.cs` | `private async Task<ToolResult> DispatchAsync(...)` | Tool handler dispatch, no relation |
| `Services/Ai/Handlers/VerifyCitationsHandler.cs` | `private async Task<ToolResult> DispatchAsync(...)` | Tool handler dispatch, no relation |
| `Services/Ai/Membership/MembershipReconciliationJob.cs` | `ScanParentsAndDispatchAsync`, `ScanOrphansAndDispatchAsync` | Job-scan reconciliation, no relation |
| `tests/integration/Sprk.Bff.Api.IntegrationTests/Membership/Phase2EndToEndFixture.cs` | Comments referencing membership job dispatch | No relation |

## Backward-compatibility verification

| Acceptance criterion | Verified by |
|---|---|
| Existing tests pass without modification | All 10 pre-task-110 test call sites compile and execute unchanged because the new parameter is optional and defaults to `null`. The post-build dotnet-test filter `~PlaybookDispatcher` reports `17 passed, 0 failed` (12 pre-existing + 5 new from `PlaybookDispatcherAttachmentsTests`). |
| Production call site accepts new signature | `ChatEndpoints.cs:572` updated to pass `request.Attachments` via named argument; `dotnet build` reports 0 errors. |
| Call-site grep shows no broken callers | Grep performed before and after the edit; both pass. |

## Why the wiring point is `ChatEndpoints.cs`, NOT `SprkChatAgentFactory.cs`

The task POML lists `SprkChatAgentFactory.cs` under "files to inspect" and asks for "attachment plumbing… where attachments per-turn live". The factory has no per-turn knowledge of `request.Attachments`:

- `SprkChatAgentFactory.CreatePlaybookDispatcherAsync(string tenantId, CancellationToken)` (line 770) — takes `tenantId` only; lifecycle/session concern; does not know about the current turn's `ChatSendMessageRequest`.
- `request.Attachments` is in scope at `ChatEndpoints.cs:572`, the endpoint handler — that IS the per-turn boundary.

The factory continues to instantiate the dispatcher with its pre-existing 6-argument constructor (no constructor change in task 110); the per-turn attachments are passed at the call site, NOT injected into the dispatcher. This matches the dispatcher's stateless design (one instance per request scope, attachments vary per turn).

## Files modified in task 110

| File | Lines changed | Nature |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs` | +30 (signature doc-comments + early guard + parameter) | Signature extension + FR-15 backward-compat guard |
| `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` | +9 / -1 (named-arg call site + 5-line FR-15 comment) | Single production call-site update |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookDispatcherAttachmentsTests.cs` | +302 (new file, 5 `[Fact]` tests) | New test class for FR-15 invariant coverage |
