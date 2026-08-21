# Task 016 — The honest-failure set (FR-S09)

> **Step 1 deliverable**: the complete enumeration of save-path early-returns and swallowed catches,
> classified. Everything the POML named is here; so is everything it did not. Findings BEYOND the eight
> are listed in "Beyond the eight" and are **flagged, not fixed** (per the POML's escalation trigger).

---

## The reading method

The POML says *read for absence*: for each branch, "what did the user see?". A branch is only a defect
when the answer is **nothing** — a `return` with no dispatch, a `catch` with no surface, or a block that
**cannot execute** so its careful message never renders. That last class is the one this project keeps
finding: code that looks like handling and is not.

---

## A. Client — `triggerSave` (`ComposeWorkspace.tsx`)

Every exit, in source order. "Signal" = what the user perceives.

| # | Line | Exit | Signal today | Verdict |
|---|---|---|---|---|
| A1 | 1673 | `state.status !== 'loaded'` | **none** | ❌ **silent — item 1** |
| A2 | 1674 | `!state.documentRef \|\| !editorRef.current` | **none** | ❌ **silent — item 1** |
| A3 | 1727 | `!bffBaseUrl \|\| !tenantId` | `saveFailed` message | ✅ honest — but the Save button was **enabled** (item 3) |
| A4 | 1757 | size pre-flight over the advertised limit | `saveFailed` naming both numbers | ✅ honest (task 015) |
| A5 | 1770 | `saveInFlightRef.current` — a save is already running | "Saving…" indicator + spinner | ✅ **visible by other means** — see note below |
| A6 | 1804 | transient create, container unresolved | `failEarly` — two distinct messages | ✅ honest (UAT-11) |
| A7 | 1817 | replace path, no drive id | `failEarly` | ✅ honest |
| A8 | 2201 | `!isSuccessfulSaveOutcome(payload.outcome)` | `saveFailed`, outcome-specific | ✅ honest (task 013) |
| A9 | 2415 | catch, `savePersisted` — threw after a confirmed write | "saved, but something went wrong after" | ✅ honest (task 012) |
| A10 | 2429 | catch, `saveReachedServer` — 2xx, unreadable body | "could not confirm" | ✅ honest (task 013) |
| A11 | 2445 | catch, HTTP 423 | lock banner + Retry | ✅ honest (task 010/052) |
| A12 | 2464 | catch, default | `saveFailureMessage(failure)` | ✅ honest (task 010/012) |

**On A5** — deliberately left without its own message. A save IS in flight; the workspace is already
showing "Saving…" and the toolbar Save is disabled. Adding a second signal would contradict the first.
This one is *documented* as non-silent rather than *assumed* to be.

### Swallowed catches on the same path

| Line | Catch | Surface | Verdict |
|---|---|---|---|
| 2384 | parent-association write failed after create-on-save | `associationWarning` → banner + retry | ✅ honest (UAT-13) |
| 2403 | reviewed-document Analysis create failed | `console.warn` only | ✅ acceptable — no document impact, no user action exists |

## B. Client — the name-modal gate

`requestSave` (2683) opens `ComposeSaveNameDialog` and returns. The modal is visible, so the *gate*
is not silent — but its **dismissal** is: Cancel / Esc / backdrop closes the dialog, the save the user
asked for never happens, and nothing says so. The document stays dirty (correct) with no statement
that the Save they pressed did not occur. ❌ **item 2.**

## C. Client — checkout lifecycle (`hooks/useComposeCheckoutLifecycle.ts`) — item 4

The same dead-`!response.ok` defect FR-S01 removed from the save path, in three places:

| Line | Block | Why dead | Consequence |
|---|---|---|---|
| 127 | `if (response.ok)` → `checkoutAcquired` | `authenticatedFetch` returns **only** when ok | works by accident (the true branch is the only reachable one) |
| 137 | `if (response.status === 409)` → `checkoutConflict` + lock owner | unreachable | **the cross-user conflict banner never renders** — a locked document reports a generic "Could not acquire document lock: …" |
| 170 | 404 / 403 message mapping | unreachable | "not yet recorded in Spaarke" and "you do not have permission" never render |
| 263 | probe `else` non-OK branch | unreachable | harmless — the catch already leaves `probeSucceeded` false |
| 327 | `if (!discardResponse.ok)` incl. the **400 "lock already released — race-but-OK"** path | unreachable | **the expected race reports failure**: the user clicks "Force-close other session", the lock *was* already released, and they are told it failed |

The 400 case is the one the POML names. It is not a cosmetic mis-message: it is the *success* path of
force-close being reported as a failure, leaving the user stuck in a non-dismissible conflict dialog.

## D. Server — `ComposeService.SaveAsync`

| Line | Exit | Signal today | Verdict |
|---|---|---|---|
| 1491 / 1510 | `BuildContainerFailedResult` | 200 + `storage-failed` | ✅ honest (task 013) |
| 1592 | `PromoteIfEphemeralAsync` — **not guarded** | a throw propagates → generic 500 "Save failed: …" + `storage-failed` | ❌ **item 5(c)** — the SPE write already landed; "not saved" is false |
| 1664 | `recordSignal: CompletedSignal(StepRecord)` — **hardcoded** | claims the record step succeeded unconditionally | ❌ **item 5(a)** |
| 1672 | the outcome decision reads `partialApplySummary` + warnings only — **never `completion`** | a Failed/Partial aggregate still reports `persisted` | ❌ **item 5(b)** |
| 2740 | promote's idempotent existing-row branch | returns without touching `sprk_filesize` / `sprk_filepath` | ❌ **item 7** — every replace save leaves stale size/path |

### Swallowed catches, judged acceptable (documented, not silent)

- `_indexing.EnqueueIfApplicableAsync` swallows internally **by contract** and returns a result — the
  result IS read into the completion projection. The defect was never the swallow; it was that the
  projection never reached the outcome (item 5(b)).
- `CaptureDocumentMemoryAsync` (1651) — best-effort durable-memory distillation. No document impact.
- Link inheritance (2866) — logs **loudly** and creates the row unassociated; documented in-code.
- Content-dedup stamp (2899) — non-fatal warn; the row is created without the stamp.

## E. Server — Graph throttling — item 6

`UploadSessionManager` translates a Graph **429** into
`InvalidOperationException("Service temporarily unavailable due to Graph rate limiting")` at four sites
(303, 335, 360, 474, 513 — two ODataError, two legacy ServiceException). That type reaches
`ExecuteSaveAsync`'s final `catch (Exception)` → **HTTP 500**, `storage-failed` + cause `unhandled`, and
a body reading `Save failed: InvalidOperationException: …`. The **`Retry-After` header Graph sent is
discarded entirely** — the one piece of information that makes a throttle actionable.

## F. Client — the draft slot — item 8

`composeDraftStore.ts` writes **one** localStorage key, `spaarke.compose.draftContent`. Its own comment
records the design: *"the newest dirty draft overwrites the slot"*. So with two documents open, the
second one's autosave **destroys the first one's unsaved work**, and `getComposeDraft(A)` then returns
null because the slot's `logicalId` no longer matches — recovery reports "no draft" for work that
existed thirty seconds earlier.

**Escalation trigger checked**: the POML requires surfacing consumers outside Compose before changing
the key. There are none — `COMPOSE_DRAFT_CONTENT_KEY` / `saveComposeDraft` / `getComposeDraft` /
`clearComposeDraft` appear only in `composeDraftStore.ts`, `ComposeWorkspace.tsx`, their two test files,
and prose (r7 notes + `docs/architecture/COMPOSE-EDITOR-UX.md`). **The trigger does not fire.**

---

## Beyond the eight — flagged, NOT fixed

The POML calls FR-S09 a *closed* set and its escalation trigger says to report anything further rather
than fix it, because expanding the set changes what Track S's ship gate means. Two found:

### N-1. The LOAD path carries the identical dead-`!response.ok` block

`ComposeWorkspace.tsx:1286`. A 404 or 403 on document load has careful copy — *"Document not found. It
may have been deleted or moved."* / *"You do not have permission to open this document."* — inside a
branch that cannot execute. The reachable catch (1425) renders `Failed to load document: {message}`
instead. Same defect, same file, different path. **Not fixed**: FR-S09 is scoped to the save path, and
the load path has no paired client-recovery test obligation in this task.

### N-2. The review-memo path carries it twice more

`fetchReviewMemo` (~2866) and `handleGenerateMemo` (~2890) — `if (response.ok)` / `if (!response.ok)`
around `selectMemoNegativeMessage`. The FR-14 "generate the memo first" and "promote to an Analysis
first" messages are unreachable for the same reason. **Not fixed**: a different feature's surface
(agreements-r1), not Compose save.

**Recommendation**: N-1 and N-2 are one small task, not three. The pattern — not the instance — is the
defect, which is exactly what the POML says about item 4. A grep for `response.ok` inside a body that
came from `authenticatedFetch` is a mechanical, complete check; it is worth running package-wide once
rather than finding these one release at a time.

---

## What each of the eight maps to (task 013's closed enum)

No local outcome shape is introduced. Client-side refusals never reach the server, so they carry no
wire outcome — they map to the enum **member whose meaning they share**, and say so in copy:

| Item | Enum member | Where it is decided |
|---|---|---|
| 1 — silent guard drops | `refused-invalid` (nothing sent; the request as-is cannot proceed) | client |
| 2 — name-modal dismissal | `refused-invalid` | client |
| 3 — tenant precondition | `refused-invalid` | client (button gate + the existing A3 refusal) |
| 4 — checkout force-close | n/a — not a save; routes on `ApiError.status` | client |
| 5 — promote-after-write | **`partially-recorded`** | server |
| 6 — Graph 429 | `storage-failed`, cause `throttled` | server |
| 7 — metadata refresh | `persisted-with-warnings` when the refresh fails | server |
| 8 — draft slot | n/a — local draft store, never a save | client |
