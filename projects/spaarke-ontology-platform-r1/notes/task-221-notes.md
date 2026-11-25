# Task 221 notes — a record's Messages list says when messages are not shown (#2671, DI-148)

Branch `work/ontology-221-messages-truncation` from origin/master `2ac250cae4`. PR closes #2671.

## Findings (step 1)

Route `GET /api/communications/by-regarding/{entityType}/{id}` (`CommunicationEndpoints.GetCommunicationsByRegardingAsync`)
returns `CommunicationThreadReadService.ReadByRegardingAsync` as is. On `2ac250cae4` (line numbers moved since the POML):

| Point | Where | What |
|---|---|---|
| Anchored threads | `ReadByRegardingAsync` ~420-423 | `$top=MaxThreads` (200); a cut was silent |
| Anchored messages | ~438 | `QueryVisibleMessagesAsync(…, MaxRegardingScan)` (500, `createdon asc`); a cut was silent |
| Filed read | `ReadThreadsOfFiledMessagesAsync` ~491-550 | query `_{field}_value eq {record}` only (anchored-thread messages and thread-less messages used the scan, then were dropped); scan `createdon asc` (the newest filed messages were the ones cut); added threads `OrderBy(CreatedOn).Take(threadBudget)` (oldest kept); every bound only logged |
| External plane pattern | `ReadRegardingAppOnlyAsync` ~617-621 | reads `MaxRegardingScan + 1`, `Truncated = rows > cap` |

Client surfaces that read this route (grep of the route path in `src/client` and `src/solutions`):

| Surface | File | Uses |
|---|---|---|
| **The record's Messages list** (the "Messages" modal of the `CommunicationConversationPanel` PCF and the conversation code page, record mode) | `Spaarke.UI.Components/src/components/ConversationWorkspace/ConversationWorkspace.tsx` via `services/communicationThreadListApi.ts` `listThreadsByRegarding` | thread rows; **changed here** |
| Conversation panel preview (PCF) | `pcf/CommunicationConversationPanel/.../previewModel.ts` | last few messages; not changed |
| Regarding-mode timeline (legacy PCF `CommunicationTimelineRegarding`) | `Spaarke.UI.Components/src/services/communicationTimelineApi.ts` `readByRegarding` | not changed |

No contract baseline covers this route (no `tests/**/*.json` names `messageCount`/`threadCount`).

## Change (step 3)

Server (`CommunicationThreadReadService.cs`, `CommunicationThreadReadModels.cs`):

- `RegardingReadResult` gains `bool Truncated = false` (additive; JSON `truncated`). A plain boolean: no count, no reason.
- Anchored threads are read with `$top=MaxThreads + 1`; the extra row is never shown and only says the thread bound cut the list.
- The shared message pipeline gets a bounded variant (`QueryVisibleMessagesBoundedAsync`): it reads one row past the bound (the
  probe), runs it through the SAME access filter and D7 open-thread gate as the rest, and never returns it. The messages are
  exactly those of the first `top` rows, as before. `QueryVisibleMessagesAsync` (the filtered query, R2 task 011) is unchanged:
  same query string, no probe.
- Filed read (`ReadThreadsOfFiledMessagesAsync`): the query itself excludes thread-less messages (`ne null`) and messages on
  anchored threads (one `ne {id}` per anchored thread, at most 199; same scale as the anchored read's OR filter), so the 500-row
  scan is spent only on messages it can add (the post-read exclusion stays as a second check). The scan is newest first
  (`createdon desc, id desc`), so a cut drops the oldest filed messages; each thread's messages are re-ordered oldest first for
  display. Threads are selected by their newest filed message (newest first) for the budget, then shown oldest thread first
  after the anchored ones (unchanged display order). Returns `(threads, truncated)`; the existing Warning log stays.

When the flag is set:

| Bound | Flag | Why it cannot reveal hidden content |
|---|---|---|
| Anchored threads > 200 | yes | every thread row is one the caller may read (impersonated; Open-only for an external caller) and is shown |
| Anchored budget spent (exactly 200 anchored threads; filed read skipped) | yes | depends only on the shown anchored threads |
| Filed threads > budget | yes | the left-out threads are readable threads holding visible messages |
| A message scan (anchored or filed) cut | only when EVERY row of the scan is shown (or left out for the budget) AND the row past the scan is visible to the caller (filed: on a thread the caller can read) | given what is shown, the flag is a function of visible rows only: with fewer than 500 shown it is never set by a scan, so a caller can never infer that hidden rows exist or how many |

The rule errs toward silence: when hidden rows share a cut scan, a readable message beyond them is not signalled (known limit
K-221-1, K2 class: fails closed toward "no flag", never toward disclosure).

Client: the record's Messages list (`ConversationWorkspace` record mode) shows one muted line under the thread list,
"Some messages may not be shown.", only when the by-regarding response has `truncated: true` (`listThreadsByRegarding` maps it;
`ThreadList` gains an optional `footnote` prop, Fluent tokens, `role="note"`). All mode never shows it. An older BFF (no field)
shows nothing. The PCF preview and the legacy regarding-mode timeline are not changed.

## §11 answers (the additive response field)

1. **Existing**: `RegardingReadResult` (this route's DTO); the external plane's `AppOnlyRegardingReadResult.Truncated` and
   `AppOnlyRecentReadResult.Truncated` are the same idea on other DTOs (the pattern copied). No new service, interface, endpoint,
   DI registration, package or column.
2. **Extension**: it is an extension: one optional positional field on the existing record, defaulting to `false`.
3. **Cost of doing nothing**: a record with more than 500 filed messages, more than 200 threads, or more filed threads than the
   budget silently shows an incomplete Messages list with nothing telling the user (#2671); the bound reaches only a log.

## Known limits

- K-221-1 (K2): a readable message cut behind hidden rows in the same scan is not signalled (see table).
- K-221-2 (K2): when the anchored threads fill the 200 budget exactly, the filed read is skipped and the flag is set though no
  filed thread may exist ("may not be shown" wording covers it).
- K-221-3 (pre-existing, unchanged): the anchored message scan is oldest first, so a cut there drops the newest anchored
  messages; now flagged, not reordered (the issue scopes the reorder to the filed union).
- The filed query's `ne` filter is up to ~16 KB of URL at 199 anchored threads, the same scale as the existing anchored OR filter.
