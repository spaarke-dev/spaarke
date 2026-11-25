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
| Anchored budget spent (exactly 200 anchored threads; no filed thread can be added) | only when one row of the same filed-candidate read (`$top=1`, newest first, same filter, access filter and D7 gate) is visible and its thread is one the caller can read (fix round 1) | the flag depends on a visible message on a readable thread only; a hidden newest row leaves it unset |
| Filed threads > budget | yes | the left-out threads are readable threads holding visible messages |
| A message scan (anchored or filed) cut | only when EVERY row of the scan is shown (or left out for the budget) AND the row past the scan is visible to the caller (filed: on a thread the caller can read) | judged from this response alone, the flag is a function of visible rows only: with fewer than 500 shown it is never set by a scan, and it never carries a count. Combined with paging the thread itself (`GET /threads/{id}`), one bit can be inferred (K-221-4) |

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
- K-221-2 (K2, narrowed in fix round 1): when the anchored threads fill the 200 budget exactly, the flag comes from the newest
  filed candidate only. If that row is hidden from the caller, an older visible filed message is not signalled (the same
  conservative rule as K-221-1). The earlier "flag set though no filed thread may exist" behaviour is gone.
- K-221-4 (K2, access): judged from one response the flag says nothing about hidden rows. Combined with paging the thread
  itself (`GET /threads/{id}`), a caller who sees a full 500-message window and no flag can infer one bit: a hidden row sits
  immediately after the window. This is the same kind of channel that already exists when a hidden row inside the window
  lowers the shown count below 500. No count is revealed.
- K-221-3 (pre-existing, unchanged): the anchored message scan is oldest first, so a cut there drops the newest anchored
  messages; now flagged, not reordered (the issue scopes the reorder to the filed union).
- The filed query's `ne` filter is up to ~16 KB of URL at 199 anchored threads, the same scale as the existing anchored OR filter.

## Verification (step 4)

- Before-state: with the new DTO and the origin/master service, `CommunicationByRegardingFiledMessagesTests` 10 failed / 20 passed
  (the failures are the flag, newest-kept and scan-exclusion tests; the must-not-fire controls passed). Against pure origin/master
  the new tests do not compile (`Truncated` does not exist). Client: with the origin client sources, 1 jest test failed / 45 passed
  (the "shows the line" test; the three controls passed).
- After (committed code): BFF build 0 warnings; `~Communication` 3545 passed / 0 failed / 2 skipped; `~Signals` 5509 / 0 / 6;
  ArchTests (built explicitly) 2816 / 0 / 0; jest (`src/services`, `ConversationWorkspace`, `CommunicationTimeline`) 54 suites,
  870 tests passed; `tsc --noEmit` no errors in changed files (6 errors elsewhere, `@spaarke/auth` not resolved locally,
  untouched files); client-lint check exit 0, all 14 packages ok.
- Publish size (fresh short-path worktrees, `dotnet publish -c Release … -o deploy/api-publish/`, framework-dependent
  linux-x64, `Compress-Archive -CompressionLevel Optimal`, PDBs included): master `2ac250cae4` 43,591,994 bytes / 194 files;
  branch 43,595,274 bytes / 194 files; delta +3,280 bytes. No package change.
- Review (Step 9.5, inline): no F-class findings. ADR check: ADR-002 (read path, no writes), ADR-010 (no new interface or DI),
  ADR-012 (shared component extended in place), ADR-021 (tokens only), ADR-022 (React 16-safe: `useState` only), ADR-028
  (injected `authenticatedFetch`, unchanged), ADR-034 (impersonation and the shared filter unchanged on every read), ADR-038
  (real filter, in-memory Dataverse world, must-not-fire controls). Clean.
- Not verified visually in a browser.

## Fix round 1 (review of PR #3144: independent Opus review and uac-r2, both PASS on access)

- F2 test gap: the guard that counts the row beyond the filed scan only on a thread the caller can read was never exercised.
  New test `TheRowBeyondTheScan_OnAThreadTheCallerCannotRead_DoesNotSetTheFlag` (internal caller, 500 visible filed messages on
  readable thread A, one older visible filed message on unreadable thread B: flag false, B absent). Mutation: with the guard
  removed it fails (flag true); guard restored. New theory `TheRowBeyondTheScan_SetsTheFlag_OnlyWhenTheOpenThreadGatePassesIt`:
  external caller, row beyond the scan on a Private thread: false; external with an Open thread: true; internal with Private: true.
- Wording: the `CutVisibly` doc, the `Truncated` parameter doc and the table above now say "judged from this response alone",
  and K-221-4 records the one-bit channel with thread paging.
- uac-r2 nit: a spent budget no longer sets the flag unconditionally. One caller-impersonated `$top=1` read with the same
  filed-candidate filter (through the same access filter and D7 gate) and, when that row is visible, one impersonated read of its
  thread row (same active and Open-only clauses; the batched thread-row read is now a helper shared with the full filed read).
  The Warning log fires only when the flag is set. Tests: 200 anchored and nothing filed elsewhere: false, no log; 200 anchored and
  one hidden-only filed message (unreadable message; unreadable thread; internal-only for an external caller): false, no log;
  200 anchored and one visible filed message on a readable thread: true, logged (the existing test); the check is one `$top=1`
  newest-first row of the same candidate filter.
- Before-state: with the PR-head service (`2e1534c797`), 5 of the 9 new tests fail (the three hidden-only cases, nothing
  filed elsewhere, the `$top=1` shape); the guard and gate tests pass there, since they cover existing behaviour.
- After (`ef6f7857eb`): BFF build 0 warnings; filed-messages class 39 / 0; `~Communication` 3554 passed / 0 failed / 2 skipped;
  `~Signals` 5509 / 0 / 6; ArchTests (built explicitly) 2816 / 0 / 0; jest `ConversationWorkspace` 3 suites, 46 tests (no
  client change this round); client-lint check exit 0, all 14 ok. Publish size, measured the same way on a fresh worktree:
  43,596,027 bytes / 194 files, +4,033 bytes against master `2ac250cae4` (was +3,280).

rework: fix-round-1 task 221
