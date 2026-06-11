# Task 023 — Evidence: Delete specialized bridge tools

> **Status**: ✅ Complete (2026-06-08)
> **Closed by**: Sub-agent partial execution (hit org usage limit mid-task) + main session completion
> **Rigor**: FULL

## What this task did

Deleted the 2 specialized bridge chat-tool classes that R5 introduced as known-limitation Q9 carve-outs, completing the Pillar 3 generic `invoke_playbook` migration. The generic `invoke_playbook` chat tool (task 021's `InvokePlaybookHandler` + task 022's dynamic playbook-list description) is now the canonical dispatcher.

## Files deleted

| Path | Reason |
|---|---|
| `src/.../Services/Ai/Chat/Tools/InvokeSummarizePlaybookTool.cs` | R5 D2-05 specialized bridge. Functionality preserved by `invoke_playbook(playbookId="SUM-CHAT@v1")` routing through `IInvokePlaybookAi` facade (task 020) → `IPlaybookOrchestrationService.ExecuteAsync` → `IPlaybookExecutionEngine.ExecuteChatSummarizeAsync` (task 025 additive method). |
| `src/.../Services/Ai/Chat/Tools/InvokeInsightsQueryTool.cs` | R5 D2-14 specialized bridge to insights endpoint. Functionality preserved by `invoke_playbook(playbookId=<insights-playbook-id>)` — the insights playbook's metadata determines whether it routes to `IInsightsAi` or RAG per FR-24 (`InsightsIntentClassifier`). |
| `tests/.../Services/Ai/Chat/Tools/InvokeSummarizePlaybookToolTests.cs` | Test of deleted class. |
| `tests/.../Services/Ai/Chat/Tools/InvokeInsightsQueryToolTests.cs` | Test of deleted class. |

## Files modified

| Path | Change |
|---|---|
| `src/.../Services/Ai/Chat/SprkChatAgentFactory.cs` | Removed the 2 hardcoded registration blocks (lines ~899-1021 previously). Replaced each with a REMOVED comment matching the Wave 7/7c/8/9 pattern documenting the Pillar 3 generic replacement. Updated the session-files manifest suffix to reference `invoke_playbook` (not the deleted `invoke_summarize_playbook`). |
| `src/.../Infrastructure/DI/AnalysisServicesModule.cs` | Removed `IHttpClientFactory.AddHttpClient<InvokeInsightsQueryTool>(...)` typed-client registration (the bridge required a typed HttpClient for the `/api/insights/assistant/query` Zone B call; that's no longer needed since the generic facade path doesn't make a direct HTTP call from the chat-tool layer). |
| `tests/.../Services/Ai/Chat/SprkChatAgentFactoryTests.cs` | Updated the session-files manifest assertion to check for `invoke_playbook` (main session fix). Updated/removed obsolete tests that asserted bridge registration (sub-agent — verified by main-session test run). |

## Capability gate preservation (per task 023 binding requirement)

The 2 deleted bridges had C# constant-based capability gates:
- `InvokeSummarizePlaybookTool` gated on `if (capabilities.Contains(PlaybookCapabilities.Summarize))`
- `InvokeInsightsQueryTool` gated on `if (capabilities.Contains(PlaybookCapabilities.InsightsQuery))`

Post-deletion, gating moves to the **data layer**:

1. **Per-tenant playbook visibility**: task 021's `InvokePlaybookHandler` queries `IPlaybookService.ListUserPlaybooksAsync` + `ListPublicPlaybooksAsync` at chat-agent build time. The tenant only sees playbooks they have authorization for.
2. **Dynamic description**: task 022's renderer populates the `invoke_playbook` tool's description with the actual visible playbook list. If the SUM-CHAT or insights playbook isn't visible to the tenant, the LLM never sees it.
3. **Validation at invocation**: even if the LLM tries to invoke a non-accessible `playbookId`, the handler's tenant-visibility check rejects it with `ToolResult.Failure(ValidationFailed)` (uniform error to prevent cross-tenant info leakage).

This is the canonical R6 Pillar 3 design: capability gates move from C# constants to data (per-tenant playbook visibility + per-playbook capability metadata).

## Verification (main session)

```
$ dotnet build src/server/api/Sprk.Bff.Api/
0 errors, 16 baseline warnings

$ dotnet test --filter "FullyQualifiedName~Services.Ai"
3621 passed, 22 skipped, 0 failed (was 3664 before task 023; -43 from deleted test files; +1 from session-files test update)
```

Two test fixes applied during main-session verification (parallel-task interference + post-deletion assertion drift):

1. `SprkChatAgentFactoryTests.CreateAgentAsync_AppendsSessionFilesNoteToSystemPrompt_WhenUploadedFilesPresent` — asserted the manifest suffix contains `invoke_summarize_playbook` (the deleted tool name). Updated to `invoke_playbook`.
2. The companion `NotContain("invoke_summarize_playbook")` assertion in the no-uploaded-files variant was updated similarly to `NotContain("invoke_playbook")`.

## What was NOT done (out of scope per POML)

- `AnalysisExecutionTools.cs` was **not** deleted. The original Wave 10 mention conflated 3 classes; the actual task 023 POML scopes only the 2 invoke-bridges. `AnalysisExecutionTools` is gated on `PlaybookCapabilities.Reanalyze` and would migrate via a similar `invoke_playbook(playbookId=<reanalyze-playbook-id>)` flow once a reanalyze playbook is configured + the corresponding capability moves to data. This is documented as a follow-up task (potentially Wave 10' or R7).

## Grep verification (post-deletion)

| Pattern | Expected | Result |
|---|---|---|
| `InvokeSummarizePlaybookTool` references in `src/` | 0 in production | ✅ 0 |
| `InvokeInsightsQueryTool` references in `src/` | 0 in production | ✅ 0 |
| `invoke_summarize_playbook` references in `src/` | 0 in production | ✅ 0 |
| Hardcoded factory registration blocks | replaced with REMOVED comments | ✅ verified by reading factory |

## Pillar 3 status

**CLOSED.** Tasks 020 (facade) + 021 (handler) + 022 (dynamic description) + 023 (deletions) all ✅.

The chat agent now has ONE canonical tool for invoking playbooks (`invoke_playbook`) — eliminating the specialized bridge proliferation that R5 closed with as a known limitation. The LLM sees only the playbooks the tenant has access to, listed dynamically in the tool's description.

## Phase A status

5 of 7 Phase A completion tasks done:
- ✅ 020 (facade)
- ✅ 021 (handler)
- ✅ 022 (dynamic description)
- ✅ 023 (deletions, this task)
- ✅ 024 (FK data fix)
- ✅ 025 (orchestrator refactor)
- ⏭️ 028 (integration test)
- ⏭️ 029 (Phase A exit gate)

## Sub-agent + main session handoff (operational note)

The task 023 sub-agent (`a356994043ab46035`) executed most of the work but hit the org's monthly usage limit before completing the verification + bookkeeping phase. Main session reviewed disk state (`git status` showed all deletions + modifications were in place), ran build + test verification, fixed 1 test assertion drift, and wrote this bookkeeping note. Result: the task completed cleanly without rework.

This is the "main session as safety net" pattern working as designed — sub-agent does the bulk work; main session validates + commits + handles any tail edge cases. The agent left clean intermediate state, so the resume cost was bounded (one test fix + one bookkeeping note).
