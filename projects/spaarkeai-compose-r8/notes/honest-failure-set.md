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

---

## What landed

### Server

| Item | Change |
|---|---|
| **5(a)** | `recordSignal` is DERIVED from `promotion.DocumentRecordId`, not a hardcoded `CompletedSignal`. The very next statement already branched on `.HasValue` for the profile step — the two lines contradicted each other three lines apart. |
| **5(b)** | The outcome decision reads the record step. It previously consulted the partial-apply summary and the warning list and nothing else, so a save whose completion aggregate was Failed still reported `persisted`. |
| **5(c)** | `PromoteIfEphemeralAsync` is guarded. A throw after a successful write returns `BuildRecordFailedResult` → **`partially-recorded`** on a 200 (the same shape `BuildContainerFailedResult` uses). The two Dataverse **identity-key** faults are RETHROWN so the endpoint's existing 409/503 handler stays reachable — swallowing them would have dead-coded it, which is the defect this task exists to remove. |
| **6** | `GraphThrottledException` (sibling of `EtagPreconditionFailedException` / `DocumentLockedByWordException`, same file) carries Graph's `Retry-After`, which all four throttle sites were discarding. Endpoint → **429 + the header**, `storage-failed` + cause `throttled`. |
| **7** | The idempotent existing-row branch refreshes `sprk_filesize` + `sprk_filepath` — those two only; identity fields stay the create branch's business. A failed refresh sets `MetadataRefreshFailed` → `document-metadata-stale` on `persisted-with-warnings`. |
| **4** (server half) | The checkout 409 body advertises `status`/`title` so `authenticatedFetch` parses it as ProblemDetails. Without that the thrown `ApiError` carried no body and no caller could name the lock holder. Additive superset — existing readers untouched. |

### Client

| Item | Change |
|---|---|
| **1** | The two bare `return`s are three explicit cases. `saveFailed` no longer forces `status: 'loaded'` — a refusal must not move the state machine (it fires while the document is still LOADING). |
| **2** | Dismissing the name modal dispatches an honest refusal. |
| **3** | `canSaveNow` requires `tenantId`; a disabled Save carries `saveDisabledReason` (the `applyTemplateDisabledReason` convention already in that toolbar). |
| **4** | Status routing moved into the `catch`, where non-2xx actually arrives. All three dead blocks gone — including the necessarily-TRUE `if (probeResponse.ok)`, because a condition that cannot be false is the same defect wearing the opposite sign. |
| **6** | A 429 renders as a wait, not a rejection. |
| **8** | Per-document draft keys + bounded retention (10, by age) + legacy-slot read-through. |

## Component justification (root CLAUDE.md §11)

| New surface | Existing overlap | Why not extend | Concrete failure without it |
|---|---|---|---|
| `GraphThrottledException` | `EtagPreconditionFailedException`, `DocumentLockedByWordException` — same file, same purpose (typed facade translations of Graph errors) | This IS the extension: a third member of an established family. Reusing either would mean a throttle claiming to be a lock or a precondition | A Graph 429 renders as HTTP 500 "Save failed: InvalidOperationException…", and `Retry-After` is discarded |
| `ComposeSaveLimits`-style constants — **none added** | — | — | — |
| `saveDisabledReason` prop | `applyTemplateDisabledReason` on the same toolbar | Same convention, different button; one prop cannot serve two controls | A disabled Save with no account of itself |
| `composeDraftKey()` | `COMPOSE_DRAFT_CONTENT_KEY` | The constant is now the LEGACY read-path; the function derives the live per-document key | Two documents share one slot and destroy each other's work |
| `document-metadata-stale` code | `SAVE_DEGRADATION_COPY` | Added to that map — no new banner surface | A failed metadata refresh is silent again |
| Causes `throttled`, `record-promotion` | `ComposeSaveTelemetry` cause set | Added to the existing closed set | A throttling spike is indistinguishable from a real storage outage; a record fault indistinguishable from a partial apply |

No new DI registration, no new NuGet, no new endpoint, no new banner component.

## Verification

- **Seam** `ConcurrencySaveSeamTests` **13/13**. **3 of the 4 new ones FAIL on the unfixed code**:
  item 5 → `500 "Save failed: TimeoutException: Dataverse request timed out."` (the exact lie — the bytes
  were already in storage), item 7 → the row was never updated, item 6 → 500.
- **Client** `ComposeWorkspace.saveLifecycle.test.tsx` 13/13, **4 new, all 4 fail pre-fix**;
  `useComposeCheckoutLifecycle.honestFailure.test.tsx` 6/6, **5 of 6 fail** against the dead-code hook
  (the 6th is the healthy-path negative, which must pass both ways).
- Full Compose client suite **91 suites / 1,121 tests**; all Compose server tests **1,139**.
- Publish **43.68 MB** compressed incl. PDBs — **0.00 MB delta**. No vulnerable packages. No new NuGet.
- Typecheck: 9 errors before, the same 9 after (unbuilt `@spaarke/ai-widgets` dist + four pre-existing
  implicit-`any`s). Prettier clean. ESLint is not configured for this package — the known gap task 018
  drafted an issue for.

### One fixture gap, found the right way

`ComposeServiceImportedRenderSaveTests.SaveAsync_CleanImportedRender_ReportsNoDegradations` went red.
Per `bff-extensions.md` § F.2 (Fixture-Config-FIRST), the fixture was inspected before the code was
blamed — and the fixture was the problem: its `IGenericEntityService` mock is `MockBehavior.Strict`, the
replace path now legitimately calls `UpdateAsync`, and an unconfigured strict call throws, which the
best-effort catch correctly recorded as a failed refresh. Setup added to `ArrangeReplaceExisting`. The
production path was right; the mock had not been told about a new, intended call.

### God-class ratchet

`ComposeService.cs` 3,785 → **3,979** — waiver re-baselined with a reason, per the pattern (never
silently). `ComposeEndpoints.cs` 2,930 is inside its grace. `DataverseServiceClientImpl.cs` remains red;
it is another project's file and was red before this project touched anything.

**Note for the merge**: `origin/master` **retired this gate entirely** on 2026-08-20 (`866f9c101` —
LOC ratchet → `docs/standards/COMPONENT-COMPLEXITY.md` + a non-blocking report). `GodClassGuardTests.cs`
is expected to disappear on merge, and **owner decision C is resolved by that commit**, not by us. The
re-baseline is recorded anyway: "the gate is going away" is not a reason to leave it red while it exists.

---

# The sweep (owner directive, 2026-08-21)

> *"we need Compose to 100% work without errors. so whether there are 8 or 10 or 100 issues that need
> to be fixed we need to fix them all"* — which retires the POML's "closed set of eight" boundary.

## What a mechanical scan found

A detector was written for the precise defect — **any `.ok` / `.status` read on a value returned by
`authenticatedFetch`** — and run over all of `src/client`. Because that function returns only on 2xx and
throws otherwise, every such read is either a branch that cannot execute or a condition that cannot be
false. Both are the same defect.

**Result: 176 reads across 48 files.** Not eight. Not ten. The pattern is repo-wide.

(The first version of the detector under-reported at 106 because it collapsed repeated
`const response = await authenticatedFetch(...)` bindings within a file and skipped everything before
the last one. Fixed; the number above is the corrected one.)

## Compose: all 9 fixed

| Site | Was | Now |
|---|---|---|
| `ComposeWorkspace.tsx` load path | "Document not found. It may have been deleted or moved." and "You do not have permission to open this document." **unreachable** — every failed load said `Failed to load document: HTTP 404` | routed on the thrown `ApiError.status`, plus an honest transport-failure branch |
| `ComposeWorkspace.tsx` annotation sync | `if (response.ok && !aborted)` — unfalsifiable | abort check only |
| `ComposeWorkspace.tsx` `fetchReviewMemo` | FR-14's two negatives ("generate the memo first" / "promote to an Analysis first") **both dead** | split on the thrown error via `memoNegativeFromError` |
| `ComposeWorkspace.tsx` `handleGenerateMemo` | same | same |
| `ComposeWorkspace.tsx` compose-outputs materialize | a guard whose own comment admitted it could not execute | deleted; the catch already routes the 404 |
| `ComposeWorkspace.tsx` browse projection | `if (response.ok)` — unfalsifiable | unconditional block |
| `ComposeWorkspace.tsx` uploaded-file open | "the session may have expired — re-upload it in the Assistant" **unreachable** | routed on the thrown status |
| `ComposeWorkspace.tsx` drafted-document mount | same, and also self-described as unreachable | deleted; the catch carries the copy |
| `useComposeHeartbeatGate.ts` | a swallow-and-log branch that could not run, beside a catch that did the same thing | one description instead of two, one of them fictional |

`readMemoProblemCode` was deleted outright — it read a ProblemDetails body off a non-OK `Response`, a
shape the transport never produces, so it could only ever have been called from dead code.

**A test went red, and that is the point.** `ComposeWorkspace.upload.test.tsx` mocked
`{ ok: false, status: 404 }` — the impossible shape — and passed *only because the dead code existed*.
Deleting the dead branch exposed it. Both it and the file's default mock now model a thrown `ApiError`.

## Beyond Compose — 46 files still carry it

Not fixed here, and deliberately: they are other features' correctness (SprkChat, the wizards, several
PCFs, the AI widgets, the code pages), and folding an unbounded cross-feature change into a P0
save-reliability deploy that "ships alone" is how a focused fix becomes an unbounded one.

**The dangerous subset — 11 sites where a designed SOFT-FAIL was inverted into a hard throw**, which is
a behaviour change, not just a worse message:

| File | Sites |
|---|---|
| `code-pages/DocumentRelationshipViewer/src/App.tsx` | 334, 354, 373 |
| `code-pages/DocumentRelationshipViewer/src/services/FilePreviewServiceAdapter.ts` | 24, 36 |
| `Spaarke.AI.Widgets/.../DocumentViewerWidget.tsx` | 345 |
| `Spaarke.AI.Widgets/.../ReconciliationWorkspaceWidget.tsx` | 157, 170 |
| `Spaarke.UI.Components/.../CreateRecordWizard/useHandoffFileLeg.ts` | 98 |
| `Spaarke.UI.Components/.../EmailComposer/createXrmEmailComposeHandlers.ts` | 362 |
| `Spaarke.UI.Components/src/services/analysisFileResolution.ts` | 118 |

Each wrote `if (!res.ok) return null / return undefined / continue` — "on failure, skip this quietly and
carry on". Because the call throws instead, the error propagates and takes the whole operation with it.
The remaining ~142 reads are the milder class: a specific message replaced by the generic `ApiError`
one.

**Recommendation**: one dedicated task, with the detector kept as a CI check so this cannot re-accrete.
The detector lives at `scripts/ci/` if it is promoted; today it is a scratch script whose logic is
reproduced above.
