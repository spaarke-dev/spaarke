# Chat Document Upload 500 — Diagnosis (UAT 2026-06-26)

## Failure summary

- **Endpoint**: `POST /api/ai/chat/sessions/c7702b5d087c4824ab469f9981c31078/documents`
- **Status**: 500 Internal Server Error (duration: 51.7 ms)
- **Time**: 2026-06-26T21:20:45.345Z
- **operation_Id**: `47665127d230f4966d179818c7010504`
- **CorrelationId**: `0HNMJNQFOEMCE:0000000E`
- **App Insights**: `spe-insights-dev-67e2xz` (RG `spe-infrastructure-westus2`, appId `6a76b012-46d9-412f-b4ab-4905658a9559`)

## Root cause — NOT one of the 5 hypotheses

**Exception type**: `System.NotImplementedException`

**Message**: `QueryChildRecordIdsAsync is implemented in DataverseWebApiService. Configure DI to use Web API implementation.`

**Stack trace (top frames)**:
1. `Spaarke.Dataverse.DataverseServiceClientImpl.QueryChildRecordIdsAsync` (`DataverseServiceClientImpl.cs:1623`)
2. `Sprk.Bff.Api.Services.Ai.Chat.ChatDataverseRepository.GetMessagesAsync` (`ChatDataverseRepository.cs:161`)
3. `Sprk.Bff.Api.Services.Ai.Chat.ChatDataverseRepository.GetSessionAsync` (`ChatDataverseRepository.cs:81`)
4. `Sprk.Bff.Api.Services.Ai.Chat.ChatSessionManager.GetSessionAsync` (`ChatSessionManager.cs:204`)
5. `Sprk.Bff.Api.Api.Ai.ChatDocumentEndpoints.UploadDocumentAsync` (`ChatDocumentEndpoints.cs:187`)

## Why this throws

This is a **DI misconfiguration**, not a data deploy gap. Code path:

- `ChatDataverseRepository` ctor injects `IFieldMappingDataverseService _fieldMappingService`
  (`ChatDataverseRepository.cs:24, 29`)
- `Infrastructure/DI/GraphModule.cs:74` registers:
  ```csharp
  services.AddSingleton<IFieldMappingDataverseService>(sp => sp.GetRequiredService<IDataverseService>());
  ```
- `IDataverseService` is bound to `DataverseServiceClientImpl` (stub) at `GraphModule.cs:46-51`
- `DataverseServiceClientImpl.QueryChildRecordIdsAsync` (`DataverseServiceClientImpl.cs:1616-1624`) explicitly throws `NotImplementedException` and tells the caller to "use `DataverseWebApiService`"
- A `DataverseWebApiService` is registered as a separate concrete (`GraphModule.cs:56-62`), but `IFieldMappingDataverseService` is wired to the stub, not the Web API impl

When `ChatDocumentEndpoints.UploadDocumentAsync` calls `chatSessionManager.GetSessionAsync(...)` (the existence check at line 187 in the endpoint), the manager → repository → stub chain reaches the NotImplementedException **before** any document upload begins. The browser's pre-failure `candidates:0` log is unrelated; the 500 is purely a session-lookup DI bug.

## Cold path is broken by design

`ChatDataverseRepository.GetMessagesAsync` even comments (line 167-169) that this query is a known workaround using `QueryChildRecordIdsAsync` against a text column (`sprk_sessionid`) — but the binding to the stub means it cannot execute at all. The "Phase D" comment notes it should be replaced with a proper FetchXML/OData query.

The Redis hot path works fine (the 204 NoContent at line 41 of the request log is `OPTIONS` preflight, NOT a successful POST — confirmed by `success:True duration:0.2056`). The 500 is the actual POST.

## Hypotheses verdict

| # | Hypothesis | Verdict |
|---|---|---|
| 1 | chat-summarize playbook has no `sprk_playbooknode` rows | NOT THE CAUSE — error fires before playbook lookup |
| 2 | `SUM-CHAT@v1` Action has null `sprk_outputschemajson` | NOT THE CAUSE — error fires before action lookup |
| 3 | No `sprk_playbookconsumer` row for `ChatSummarize` | UNRELATED (frontend `candidates:0` is a separate issue) |
| 4 | SPE document fetch failed | NOT THE CAUSE — error fires before SPE call |
| 5 | Other exception | **YES** — DI binding of `IFieldMappingDataverseService` to the stub impl |

## Proposed fix scope

**This is NOT R4 scope.** This is a chat-routing / chat-document-upload defect — likely introduced or surfaced by recent refactor of `ChatDataverseRepository` to call `QueryChildRecordIdsAsync` against a text column. R4 is the Daily Briefing/`/narrate` path which uses `PlaybookOrchestrationService`, not `PlaybookExecutionEngine` or `ChatDataverseRepository`.

**Fix options** (recommend the user pick one):

A. **Quickest unblock — bypass cold-path lookup for new sessions**: In `ChatDocumentEndpoints.UploadDocumentAsync`, check Redis-only via `IChatSessionStore` instead of `ChatSessionManager.GetSessionAsync`. The Dataverse cold path was always documented as Phase D incomplete (see `ChatDataverseRepository.cs:100-104` comments). ETA: ~30 min code + deploy.

B. **Proper fix — rebind `IFieldMappingDataverseService`**: Change `GraphModule.cs:74` to:
   ```csharp
   services.AddSingleton<IFieldMappingDataverseService>(sp => sp.GetRequiredService<DataverseWebApiService>());
   ```
   This requires `DataverseWebApiService` to implement `IFieldMappingDataverseService` (verify before applying). Also needs the text-column workaround in `GetMessagesAsync` (line 161-165) to be replaced with a real OData filter on `sprk_sessionid eq '...'`. ETA: ~2 hr code + tests + deploy.

C. **Workaround in repo**: Make `GetSessionAsync` return null gracefully when `QueryChildRecordIdsAsync` throws `NotImplementedException`, since the repository already returns null when messages collection is empty (lines 87-94). One-line `try/catch`. ETA: ~15 min.

**Recommended**: Option A or C for UAT-day unblock; Option B as the proper follow-on owned by `chat-routing-redesign-r1` or a new follow-on issue.

## Where the findings doc was written

`c:\code_files\spaarke-wt-spaarke-daily-update-service-r4\projects\spaarke-daily-update-service-r4\notes\uat\chat-summarize-500-diagnosis.md` (this file)

## Blocked / requires user decision

- Pick fix option A / B / C above
- Confirm this should be filed against `chat-routing-redesign-r1` (or a new project), NOT this R4 worktree
- Confirm whether the `candidates:0` playbook-options issue is in scope here or a separate ticket
