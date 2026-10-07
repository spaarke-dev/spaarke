# Task 033 — FR-16b Find view three-state gating and Run Index

> **Rigor**: FULL · **Model tier**: sonnet @ high · **Step mode**: DIRECTIONAL
> **Gate**: 032 complete (per-row authorization), 015 complete (tab shell), 013 complete (client document identity)

---

## 1. Identity-outcome → Find-state mapping

Task 013's `documentIdentityService.resolveDocumentIdentity` returns one of SIX outcomes
(`resolved` / `new` / `conflict` / `indeterminate` / `denied` / `error`), richer than the POML's
"`sprk_document` exists or not". Only `new` maps to spec FR-16's state 1 (the save prompt).
`conflict`, `indeterminate`, `denied` and `error` each get their OWN honest Find state — none is
ever treated as "new", mirroring task 024's `SaveModeSection.resolveSaveMode` precedent for the
exact same `DocumentIdentityState` union.

| `documentIdentity` | `FindState.kind` | Copy shown | Retry offered? |
|---|---|---|---|
| `'checking'` | `checking` | "Checking whether this document is in Spaarke…" | n/a |
| `undefined` (Outlook — identity does not apply) | `no-document` | Save prompt (state 1) | n/a |
| `{ kind: 'new' }` | `no-document` | Save prompt (state 1) | n/a |
| `{ kind: 'conflict' }` | `identity-conflict` | "Can't check this document for indexing" — conflicting record | Yes, `onRetryDocumentIdentity` ("Check again") |
| `{ kind: 'indeterminate', reason }` | `identity-indeterminate` | "Couldn't check this document" — service unavailable vs. system failure copy | Yes ("Try again") |
| `{ kind: 'denied' }` | `identity-denied` | "You can't check this document" — no access to the record | No (a definite, terminal answer) |
| `{ kind: 'error', message }` | `identity-error` | The identity service's own error message | Yes ("Try again") |
| `{ kind: 'resolved' }` + `sprk_searchindexed` still loading | `loading-index-status` | Spinner | n/a |
| `{ kind: 'resolved' }` + the index-status READ itself failed | `index-status-error` | Error + "Try again" that calls `useDocumentProfile`'s own `refetch()` | Yes |
| `{ kind: 'resolved' }` + `searchIndexed` is `false` or `null` | `not-indexed` | State 2 — "This document isn't indexed yet." + Run Index | n/a |
| `{ kind: 'resolved' }` + `searchIndexed` is `true` | `indexed` | State 3 — minimal results container | n/a |

`false` and `null` are deliberately NOT distinguished in the UI (spec FR-16's table), and both are
covered by a unit test (`resolveFindState` "resolved + searchIndexed false/null -> not-indexed").

### The `documentIdentity === undefined` decision (Outlook)

Task 013's client identity resolution is gated on `hostAdapter.getCapabilities().canGetDocumentUrl`,
which is `false` on Outlook — so `documentIdentity` is permanently `undefined` there; there is no
Outlook-side equivalent of the Word URL-based resolver. `resolveFindState` treats `undefined` the
SAME as `{ kind: 'new' }` (state 1), mirroring `SaveModeSection.resolveSaveMode`'s own precedent for
`identity === undefined` (→ `view: 'none'`, defaulting to "we don't know of an existing record").

**Known, pre-existing gap this inherits (not introduced by this task):** no client flow threads a
just-completed save's `documentId` back into `App.savedContext` for EITHER host today.
`SaveFlow.tsx`'s `onComplete(docId, docUrl)` callback reaches `App.tsx`'s `onComplete` prop, which
only `console.log`s it — it never calls `setSavedContext`. So even on Word, a document saved for the
FIRST time in this pane session (before task 013's URL-based resolver has a reason to re-run) will
show the Find tab's save prompt until the user switches away and back, or `retryDocumentIdentity`
fires some other way. On Outlook this is the ONLY signal Find has, so it is more visible there. This
is flagged here rather than "fixed" under this task — wiring `App.onComplete` to set
`savedContext.documentId` is Save-flow surface, out of this task's declared scope
(`FindView.tsx`, `RagEndpoints.cs`, and the `views/index.ts`/`App.tsx` mount wiring only), and no
escalation trigger in the POML covers it. Recorded here rather than silently accepted; the operator
should decide whether this becomes a `notes/defer-issues.md` + GitHub issue entry.

---

## 2. Where `sprk_searchindexed` is read from (§11 decision)

**Extended the EXISTING read — no new BFF route.**

- `DataverseServiceClientImpl.GetDocumentAsync`'s `ColumnSet` (server) ALREADY selected
  `sprk_searchindexed` and `sprk_searchindexname` (added by `multi-container-multi-index-r1` for
  `VisualizationService`'s own index binding — confirmed by reading the ColumnSet directly, not by
  assumption).
- `GET /api/v1/documents/{id}` (`DataverseDocumentsEndpoints.cs`) already serializes the FULL
  `DocumentEntity` returned by `GetDocumentAsync` (`TypedResults.Ok(new { data = document, ... })`)
  — not a profile-only projection. So both fields were ALREADY present on every response this route
  ever returned; only the CLIENT's TypeScript envelope and the hook's public return value were
  missing them.
- The client-side extension is entirely inside `useDocumentProfile.ts` (already the pane's one
  consumer of this route, per task 021): the internal `BffDocumentProfileEnvelope.data` type gained
  `searchIndexed?: boolean | null` and `searchIndexName?: string | null`; `UseDocumentProfileResult`
  gained `searchIndexed`, `searchIndexName`, and a new public `refetch()` (the SAME mechanism
  `generateProfile()` already used internally — `setRefreshToken(t => t + 1)` — exposed so `FindView`
  can re-poll after Run Index without `documentId` changing).
- **No new BFF route was written.** The escalation trigger for "if a new route truly seems
  required, STOP before writing it" never fired — it wasn't required.

`FindView` calls `useDocumentProfile(resolvedDocumentId)` (the SAME hook `DocumentProfileSection`
already uses) rather than adding a second call to the same route.

---

## 3. Where `TenantId` comes from (Run Index)

`authService.getAccount()?.tenantId` — MSAL's `AccountInfo.tenantId`, already populated from the
signed-in token's `tid` claim by `@spaarke/auth` (`SpaarkeAuthProvider`/`OfficeNaaStrategy`). Nothing
is hand-parsed from a JWT and nothing is hard-coded. If `getAccount()` returns `null` or
`tenantId` is absent, `handleRunIndex` throws before issuing the POST and the catch block renders it
as a failure state ("Could not determine your tenant. Try signing in again.") rather than sending an
incomplete request.

---

## 4. The index-name fix (`RagEndpoints.SendToIndex`)

**Re-located by symbol** (the POML's cited line numbers, `:119/599/632/702/710-722/1297/1313/1339`,
were close but not exact after 032's edits):

- Route registration: `RagEndpoints.cs` — `group.MapPost("/send-to-index", SendToIndex)`
- Handler: `private static async Task<IResult> SendToIndex(...)`
- The defect: `var indexName = searchIndexNameResolver.GetDefaultIndexName();` was computed ONCE,
  before the per-document loop — the tenant default, unconditionally, for every document in the
  batch. Worse than the POML's framing ("ignores `sprk_searchindexname`"): the ACTUAL indexing
  write was also affected, not just the Dataverse tracking stamp — `FileIndexRequest.SearchIndexName`
  (the field `multi-container-multi-index-r1` plumbed end-to-end specifically for this) was never
  set on the request built at Step 4, so the real write always fell through to `IRagService`'s own
  tenant-default resolution regardless of what any per-record value said.

**The fix (in-handler only — no resolver, no signature change, no escalation):**
`ISearchIndexNameResolver.ResolveAsync(documentId, parentEntity?.EntityType, parentEntity?.EntityId, ct)`
was ALREADY an injected dependency of this exact handler and ALREADY does the full
document → parent → parent's-BU chain — including, as its own migration-safety fallback, reading the
LEGACY `sprk_searchindexname` text column the POML specifically names. `RagIndexingJobHandler`
already calls it this exact way (`resolvedSearchIndexName = payload.SearchIndexName ?? await
_searchIndexNameResolver.ResolveAsync(...)`); `SendToIndex` now mirrors that precedent — the ONLY
in-handler change. `GetDefaultIndexName()` is now the FALLBACK when the chain resolves nothing, not
the only path, for both the actual write (`FileIndexRequest.SearchIndexName`) and the Dataverse
stamp (`UpdateDocumentRequest.SearchIndexName`) and the response (`SendToIndexDocumentResult.IndexName`).

**`IndexFile` (`/api/ai/rag/index-file`) has the identical bug and was NOT touched** — out of this
task's declared scope (only `/send-to-index` is named in the POML/spec). Flagged, not silently fixed
alongside it, per CLAUDE.md §11 (don't widen scope silently).

**Contract-tested** in a NEW file, `tests/integration/contract/Api/Ai/RagSendToIndexIndexNameContractTests.cs`
(3 tests): per-record value used for BOTH the write and the stamp; the tenant-default fallback when
nothing resolves; the unauthenticated-caller 401. **Verified to fail without the fix** — the primary
test was re-run against the code with `resolvedIndexName` hard-set to `null` (simulating the pre-fix
state) and failed exactly as expected (`Failed: 1, Passed: 2`), then the fix was restored and the
suite went green again (3/3). The D-032-2 control tests (below) were verified the same way.

---

## 5. D-032-2 — the silent authorization-budget drop

`VisualizationEndpoints.AuthorizeRowsAsync` drops result rows past
`MaxDocumentAuthorizationChecks` (100) UNEVALUATED — correct, unchanged fail-closed behavior
(task 032 §5/§6 item 5). What was missing, and is now fixed, is that the drop said nothing:
`GraphMetadata` gained a `Warnings` field (`IReadOnlyList<SearchWarning>?`, reusing the SAME
`SearchWarning`/`SearchWarningCode` types cross-record document search already emits — extended, not
duplicated, per CLAUDE.md §11), and `AuthorizeRowsAsync` now adds a `PARTIAL_RESULTS` entry when
`budgetExhausted` is true (mirrors `SemanticSearchEndpoints.AuthorizeRowsByParentAsync`'s own
`PARTIAL_RESULTS` exactly — same code, same shape). **The fail-closed drop itself is unchanged** —
only the silence.

Reachable per `notes/032-authorization-hardening.md` §5: five hardcoded relationship queries at
`TopCount = 50` each can present up to 250 candidate rows against the 100-check budget.

Contract-tested in a NEW file, `tests/integration/contract/Api/Ai/VisualizationBudgetWarningContractTests.cs`
(3 tests): the warning fires when candidates exceed the budget (101 candidates, all granted Read —
the drop is proven by fewer than 101 surviving, and the drop happens even though every candidate was
individually permitted); no warning when comfortably under budget (control, proves the warning isn't
noise); no warning when rows are DENIED but the budget is not exhausted (control, proves the warning
is keyed to "never evaluated", not to "any row withheld at all" — a denial is a definite answer, not
an unknown). Verified to fail without the fix (inverted the `if (budgetExhausted)` guard; all 3 tests
failed as expected; restored, all 3 pass).

**Client-side surfacing:** `FindView`'s state-3 (`indexed`) branch calls
`GET /api/ai/visualization/related/{documentId}` and renders a `MessageBar` from
`metadata.warnings` when a `PARTIAL_RESULTS` entry is present — covered by
`FindView.test.tsx`'s "surfaces the D-032-2 PARTIAL_RESULTS warning" test.

---

## 6. Client architecture

- `FindView.tsx` REPLACES task 015's static placeholder body at the SAME mount point
  (`currentTab === 'find'` in `App.tsx`). No second view, no new tab wiring — per
  `notes/015-tab-shell-decisions.md` §4's explicit hand-off.
- `resolveFindState` is a PURE function (no network, no React), independently unit-tested — mirrors
  `SaveModeSection.resolveSaveMode`'s precedent for the same `DocumentIdentityState` union.
- State 3 (`indexed`)'s results container is intentionally MINIMAL (a count + the D-032-2 warning) —
  task 034 owns lazy-scroll and the records bridge, per this task's own notes section in the POML.
- `App.tsx` now passes `FindView` the SAME `documentIdentity` it already threads to `SaveView`, plus
  `onRetryDocumentIdentity` (same callback SaveView uses) and a new `onGoToSave` (mirrors
  `CreateTodoView`'s existing `onGoToSave={() => setCurrentTab('save')}` pattern).

---

## 7. Deviations from the literal POML

| POML said | What was done | Why |
|---|---|---|
| Cited line numbers `:119/599/632/702/710-722/1297/1313/1339` | Re-located every symbol before editing | Task 032 shifted line numbers; the corrections explicitly called this out |
| "the pane's own client document identity ... whether a `sprk_document` exists" (implying a two-outcome model) | Modeled all SIX `documentIdentityService` outcomes, per the operator's corrections | `conflict`/`indeterminate`/`denied`/`error` must never be treated as "new" (task 013's own rule) |
| Step 8: "Add unit tests for the three-state selector logic (true / false / null / no-document)" | Added unit tests for ALL TEN `FindState` variants, not just four | The richer identity model needs test coverage or the mapping is unverified |
| D-032-2 acceptance criterion (added 2026-09-09) | Implemented in THIS task, in `VisualizationEndpoints.cs` + `IVisualizationService.cs`, contract-tested in a NEW file | Explicitly folded into 033 by the operator's 2026-09-09 note in the POML — not deferred |

---

## 8. Placement Justification (CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`)

**All server-side changes are in the BFF, and belong there** — same reasoning task 032 already
recorded for this exact surface (§9 of `032-authorization-hardening.md`), extended for this task's
narrower edits:

- **No new service, no new DI registration, no new package, no new interface.**
  `ISearchIndexNameResolver` was already registered and already an injected parameter of
  `RagEndpoints.SendToIndex`; the fix only changes WHEN/HOW it is called (moved from
  "not called at all, `GetDefaultIndexName()` unconditionally" to "call `ResolveAsync` first, fall
  back to `GetDefaultIndexName()`"). `GraphMetadata.Warnings` is a new PROPERTY on an existing
  record, reusing the existing `SearchWarning`/`SearchWarningCode` types — not a new type, not a new
  channel mechanism.
- **The fix is in-handler**, exactly where `GetDefaultIndexName()` was already being called — no new
  layer, no new abstraction.
- **No new endpoint.** `POST /api/ai/rag/send-to-index` and `GET /api/ai/visualization/related/{id}`
  both already existed; this task only fixed one's index-name resolution and added a warning field to
  the other's response metadata.
- **No AI-internal type crosses into CRUD code.** Both files are already AI-surface endpoints
  (`Api/Ai/*`), consuming AI-surface services (`ISearchIndexNameResolver`, `IVisualizationService`) —
  nothing here bypasses `Services/Ai/PublicContracts/` because nothing here is CRUD code in the first
  place.
- **Test obligation met**: 2 new contract-test files (6 tests total) under
  `tests/integration/contract/Api/Ai/` — the established KEEP path for this test class per ADR-038 —
  substituting only the module boundaries (`IDocumentDataverseService`, `IFileIndexingService`,
  `IGenericEntityService`, `IVisualizationService`, `IAccessDataSource`), never
  `Mock<HttpMessageHandler>`, never a DI-registration or ctor-null-check assertion.
- **Publish size**: measured against a FRESH build of `origin/master` (not the recorded baseline
  number, per ADR-029) — see §9 below. Delta +0.05 MB, far under the +5 MB escalation threshold and
  the 60 MB ceiling.

**Client-side** (`FindView.tsx`, `useDocumentProfile.ts`, `App.tsx`) is in `shared/taskpane/` per
ADR-012's Path A exception (this project's documented deviation, unchanged by this task) — no
`@spaarke/ui-components` import added, no `Xrm`-bound component imported (NFR-03).

---

## 9. Publish size (ADR-029)

Measured against a **fresh** `origin/master` build (commit `e0a6f87c4`, confirmed current tip via
`git fetch origin master` at measurement time), not the recorded baseline figure — per the binding
"don't trust the recorded number, the baseline ages" rule. Both zips built with the same tool
(PowerShell `Compress-Archive -CompressionLevel Optimal`, matching `scripts/Deploy-BffApi.ps1`), both
published from the project directory into a short path (`C:\t\bffpub`, `%TEMP%\wt-master-033\pub`),
neither committed to git.

| Build | Size (bytes) | Size (MB) |
|---|---|---|
| `origin/master` @ `e0a6f87c4` (fresh build, this measurement) | 47,556,583 | 45.35 |
| `origin/master` @ `e0a6f87c4` (figure supplied in the task's corrections, for comparison) | 47,556,260 | 45.35 |
| This branch (`work/task-033-find-view-index-gating`) | 47,608,613 | 45.40 |
| **Delta (branch − fresh master)** | **+52,030** | **+0.05** |

The two master measurements differ by 323 bytes (0.0007%) — consistent with ordinary build
nondeterminism (timestamps/PDB metadata), not baseline drift; the supplied figure is corroborated
rather than contradicted. **Delta is far under the +5 MB escalation threshold and the 60 MB ceiling** —
no escalation warranted.

---

## 10. Unverified

The POML's six `<ui-tests>` all require a live Office host (Word/Outlook desktop or web) this
worktree does not have. **Listed as UNVERIFIED, not claimed as passing**:

1. State 1 — unsaved document shows the save prompt
2. State 2 — saved but unindexed shows Run Index
3. Run Index transitions to results
4. Dark mode (ADR-021)
5. Keyboard navigation and announcements (NFR-11)
6. Outlook parity

What WAS verified for these, short of a live host: the underlying state logic and Run Index request
shape via `FindView.test.tsx` (26 tests, jsdom); Fluent v9 semantic tokens only (no hard-coded hex —
`grep`-verified); standard Fluent `Button`/`MessageBar` controls (native keyboard operability, no
custom key handling); `useAnnounce` wired for every state transition and the Run Index
in-progress window specifically (task 018's pattern, same as every other view in this package).
