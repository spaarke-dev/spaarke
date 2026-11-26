# B-1 commit invariants (task 014)

The commit moves into the decision capability as a **direct delegation**: `DecisionCapability.CommitAsync(http, signalId, review, ct)` → `DecisionCommitService.CommitAsync(signalId, review, http, ct)`. `DecisionCommitService.cs` is **not edited** (FR-10 as amended 2026-11-07, option C), so every invariant below keeps its exact statement and position. "Before" is branch `work/spaarkeai-mcp-bff-api-r1` at `8cc38fff6f` (master `23567b2f93` merged in). "After" is the task 014 commit; the after line equals the before line because `git diff 8cc38fff6f -- src/server/api/Sprk.Bff.Api/Services/Signals/DecisionCommitService.cs` is empty.

All lines are in `src/server/api/Sprk.Bff.Api/Services/Signals/DecisionCommitService.cs` unless another file is named.

## The commit order (spec MUST rule, ontology's list)

| # | Invariant | Before (file:line) | After (file:line) |
|---|---|---|---|
| 0 | Entry: `CommitAsync(Guid signalId, DecisionReview review, HttpContext http, CancellationToken ct)` keeps its `HttpContext` parameter | `:439` | `:439` |
| 0′ | Who is asking, no I/O: `tid` + `oid` from the request principal, else `CallerUnresolved` (single 403) | `:444-448` | `:444-448` |
| 1a | Every Signal (primary + Also resolve) authorized AS THE CALLER (`SignalCoreRecordAccess.AuthorizeAsync`, readability only) before the review id is looked up; not readable → uniform 404, unresolved → 403, fault → 503 | `:453-475` (call `:467`, 404 `:472-473`) | `:453-475` (`:467`, `:472-473`) |
| 1b (D-141) | Do item is the caller's own work, or the uniform 404, before any lock, ledger answer or write; skipped only for the same caller's same-content review in the ledger | `:480-489` | `:480-489` |
| 0a | Locks: review lease + one per covered Signal (fixed order) + budget lease when Create budget is taken, acquired without the request token | `:491-529` | `:491-529` |
| finally | Locks released in reverse order in `finally`, uncancellable | `:533-547` | `:533-547` |
| 0 (ledger) | Ledger replay under the lock: in progress → 409; same id, other content → `review_changed`; Recorded → `ReplayRecordedAsync`; not a retry or not Failed → first outcome `Replayed=true`; not resumable → `not_resumable`; resume → "mine" re-check | `CommitLeasedAsync :621`; `:626-667` | `:621`; `:626-667` |
| replay (server-side) | Replay decided only from server state: ledger key `{callerOid}:{reviewId}` from the token (`:478`), content hash (`:477`), stored ledger entry (`:630`), stored Decision Record by (caller, review id) (`:947-962`); never from the request body | `:477-478`, `:630`, `:947-962` | same |
| review start | Plan-read stamp (`DecisionPlanReadStamp.ReadAsync`, server time) is the review start; no stamp → now | `:672-674` | `:672-674` |
| confirm | `review.Confirmed` false → `not_confirmed`, nothing written | `:676-677` | `:676-677` |
| marker | In-progress ledger marker set before any slow read | `:683-685` | `:683-685` |
| 1c | The Signals read APP-ONLY (server-written columns) | `:964-972` | `:964-972` |
| 1c-billing (D-207) | Any invoice / invoice-line subject → 409 `ontology.decision.decided_in_invoice_review`, before `PrepareAsync`, the rights probe, any executor and every write; an already-recorded review is not refused | `:974-993` (predicate `IsBillingSubject :1684`) | `:974-993` (`:1684`) |
| PrepareAsync | Plan re-derivation, review validation, open-state / core-changed checks | call `:997`; def `:1283` | `:997`; `:1283` |
| 1c′ (FR-51) | `CallerMayDecideAsync`: as the caller, Write on each subject and AppendTo on the core (rule in `DecisionCallerRights.CheckAsync`) | call `:1005`; def `:1577` (rule call `:1580`) | `:1005`; `:1577` (`:1580`) |
| 1c-walls | One Decision Record never spans two secure walls | `:1028-1033` | `:1028-1033` |
| 1c″ | No-core item with no subject → `item_owner_unsupported` | `:1035-1039` | `:1035-1039` |
| 1d | Every executor's preflight as the caller | `:1050-1079` | `:1050-1079` |
| 1e | Record writer's no-I/O preflight | `:1081-1089` | `:1081-1089` |
| 1f | Record owner resolved before the first write | `:1091-1126` | `:1091-1126` |
| 2-4 | The writes, D-17 order | `:1128` | `:1128` |
| 5 | The ONE Decision Record | `:1170` | `:1170` |
| 6 | Close (or link) every covered Signal | `:1203` | `:1203` |
| 7 | Assign Task notification after the record | `:1217` | `:1217` |

## Other invariants named in the spec

| Invariant | Before | After |
|---|---|---|
| HttpContext helpers unchanged (option C) | `CommitLeasedAsync :622`, `ReplayRecordedAsync :770`, `CommitCoreAsync :927`, `CallerMayDecideAsync :1578`, `Request :1966` | same lines |
| `sprk_privilegeflagged` read only by the commit's Signal select | `:313` | `:313` |
| `IsBillingSubject` called only from `DecisionCommitService` (no second billing check) | `:984` (call), `:1684` (def) | same; grep of `src/**` and the capability shows no other caller |
| D-197 `CheckVersionOwnerAsync` stays put | `Services/Ontology/OntologyAdminAuthoringService.cs:1178` (call), `:1266` (def) | same (file not touched) |
| `SignalWriter.cs`, `DecisionRecordWriter.cs`, `DecisionCallerRights.cs` untouched | — | empty diff |
| Single owner of completion: the only commit path is `DecisionCommitService.CommitAsync` | REST handler `Api/Signals/DecisionCommitEndpoints.cs:56` called the service | REST handler calls `IDecisionCapability.CommitAsync`, which delegates (`Services/Signals/DecisionCapability.Commit.cs`); MCP (024) will call the same member |

## What B-1 adds

Only the caller guard in `DecisionCapability.CommitAsync`: a null `HttpContext` or a principal with no authenticated identity returns `DecisionCapability.UserContextRequiredCommitResult` (mapped to `DATAVERSE_USER_CONTEXT_REQUIRED`, 401) before the service is called, so nothing is read, locked or written and the ledger is not touched. A REST request never reaches it (`RequireAuthorization` answers 401 first), so the REST bytes do not move.

## Known gap between the spec text and the service (not changed here)

The spec (NFR-06, FR-08 negative) and design §11 say a commit "requires a prior plan read". The service does not refuse a commit that has no plan-read stamp: it takes the review start as now (`:672-674`). The only refusal that a missing plan read causes today is `ontology.decision.signal_not_open` (409) for a Signal the system closed before the commit. A Signal the system closed after an earlier plan read is re-closed instead. Making the plan read mandatory would change `DecisionCommitService` validation, which the spec forbids (MUST NOT), so this needs an owner decision. Filed as ISS-004 (#2628) in `notes/defer-issues.md`. `DecisionCommitServiceTests.Capability.cs` pins today's behaviour (no stamp on a system-closed Signal → `signal_not_open`; a stamp from before the closure → recorded).
