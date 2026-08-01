# Task 033 — FR-17 wizard→review auto-run bridge — execution notes

> Rigor: FULL · Model tier: opus @ high · Step mode: directional · Status: **COMPLETE** — the escalation
> below was RESOLVED by the coordinator (2026-07-31): **path B-i approved** (client-side Analysis-owned session
> mint per §11 reuse-first + ADR-013 no-new-surface; B-ii new endpoint REJECTED), and `ConversationPane.tsx`
> was FREED (task 041 landed in commit `9b28628bc` WITHOUT touching it — its batch loop went to ComposeEditor).
> Part 1 (unchanged below) is the escalation-phase investigation/design record. **Part 2 (appended at the
> bottom, "WIRING — Part 2") records the B-i implementation that completed legs 2/3.**

---

## Headline

The task's three legs decompose cleanly, but two of them cannot be built inside this task's allowed file set:

| Leg | Finding | Buildable here? |
|---|---|---|
| **1. BRIDGE** (durable doc → session file) | **Already solved by shipped client plumbing** — no new BFF endpoint. When Compose opens the wizard's stored doc, `ComposeWorkspace`'s DEF-10 auto-register hands the loaded bytes to `registerComposeActiveDocument`, which POSTs them to the EXISTING `/documents` endpoint → a dispatchable `ChatSessionFile` + notifies `documentSessionWaiter`. Escalation trigger 3 (new BFF endpoint) does NOT fire. | The bridge itself needs no work; the only gap is `subDomain` delivery (below), which lands in ConversationPane. |
| **2. BIND** (session → wizard's Analysis, durable FK) | The wizard already creates a **rich** `sprk_analysis` (CreateAnalysisWizardWidget.tsx:863). `fork` and `promote` **both CREATE a new Analysis** (`CreateAnalysisAsync` — AnalysisEndpoints.cs:1182 / :1356); neither binds a session to a PRE-EXISTING analysisId. Binding the wizard's analysis needs either a new "bind-to-existing" server capability OR minting the compose session with an Analysis-owned HostContext at create time (which happens in `ConversationPane.registerComposeActiveDocument`). | **No** — new BFF endpoint OR ConversationPane.tsx. Both are STOP boundaries. |
| **3. AUTO-DISPATCH** (`runExplicit`) | `runExplicit(sessionFileId, fileName, subDomainKey)` needs the gate controller instance + the minted `sessionFileId` + `subDomain`. All three live in / flow through `ConversationPane.tsx` (the gate is `useAgreementReviewGate(...)` wired there; `sessionFileId` is minted in `registerComposeActiveDocument`; `subDomain` must reach the ambient `useComposeLaunch()` provider that ConversationPane reads). | **No** — every practical firing point is in ConversationPane.tsx. |

Per the HARD BOUNDARY ("if ConversationPane.tsx itself must change, STOP and report") and CLAUDE.md §6/§6.5,
this is escalated with a §10/§11-justified minimal-delta recommendation (see "Escalation" below). The parent
pre-instructed this exact stop.

---

## Step 0 — Escalation-status verification (the two resolved triggers + the seam)

### (a) Hub promote-FK fix — LANDED and empirically verified ✅

- Commit `2f8f11123` ("fix(api): promote durable-FK — bind creates the anchor row when missing (agreements-r1 Q2)")
  is an **ancestor of HEAD** (`git merge-base --is-ancestor 2f8f11123 HEAD` → true; merged via `9d83f2a05`).
- The fix is in **`ChatDataverseRepository.BindSessionToAnalysisAsync`** (:261-287): when no `sprk_aichatsummary`
  row exists, it now **CREATEs the anchor row WITH the `sprk_analysis` FK** and returns `true` (was: warn +
  return `false`). A real Dataverse write failure throws → propagates to the caller's compensation.
- The consumer chain is correct: `promote` endpoint (AnalysisEndpoints.cs:1372) → `ChatSessionManager.PromoteSessionToAnalysisAsync`
  (:527) → `BindSessionToAnalysisAsync`. Promote's comment (:519-526) documents the throw-on-failure + Analysis-delete
  compensation.
- **Empirical proof IN-REPO** (per Step 2 instruction — NOT against the live BFF, which may be env-lagged):
  the hub's own unit test asserts create-with-FK (`ChatDataverseRepositoryTests.BindSessionToAnalysisAsync_WhenNoSummaryRow_CreatesAnchorRowWithFkAndReturnsTrue`),
  and **this task adds the missing round-trip regression** proving by-analysis visibility (see "Delivered" below) — green 3/3.
- **Env-lag note**: spaarkedev1's deployed BFF may predate `2f8f11123` (the fix requires a `bff-deploy`, per its own
  commit message; hub task 060 deploys). The gate here is the in-repo test, not a live probe. If a live probe is run
  later and fails, that is deployment lag, not a code regression.

### (b) Phase-1 owner UAT — OK; wizard-finish seam re-checked ✅

- Owner confirmed OK (hub Q5 closeout, 2026-07-31; parent cite). Coordination doc Q5: Phase-1 deployed to
  spaarkedev1, seam **stable + additive-only**, now also carries `subDomain`.
- Seam re-check (my read of `CreateAnalysisWizardWidget.tsx:940-955`): the finish hook dispatches
  `widget_load{ widgetType:'compose', widgetData.compose:{ speDriveItemId, speDriveId, sprkDocumentId, fileName,
  activeWorkType(:950), subDomain(:951) } }` — additive; `subDomain` present; no field removed/renamed. Stable. Safe to wire into.

### (c) Session file-registration seam audit — the decisive investigation

**Server (READ-ONLY audit):** there is **NO pointer-based (no-re-upload) stored-doc → `ChatSessionFile` path**.
The ONLY writer of `ChatSession.UploadedFiles` for a live session is `POST /api/ai/chat/sessions/{sessionId}/documents`
(`ChatDocumentEndpoints.cs:98`, raw `IFormFile` → extract text → RAG-index → append `ChatSessionFile` with both
`ExtractedText` and `SearchDocumentIdsCsv`). Dispatch resolves target files ONLY from `session.UploadedFiles`
(`SessionDispatchOrchestrator.ResolveTargetFiles` :1170-1195) and hard-errors "No session files were available"
(:814-820) when empty; a `ChatSessionFile` is dispatchable iff it has non-empty `ExtractedText` OR a live
`SearchDocumentIdsCsv` (`SessionFileTextSource.FetchAsync`). `POST /api/compose/active-document` and the compose
seed resolvers write only an `ActiveDocumentIdentity` **pointer** — never a `ChatSessionFile`. The "compose-direct
landing" comment (`ChatSession.cs:317`) is realised by REUSING the raw-bytes upload endpoint **client-side**.

**Client (READ-ONLY audit):** the wizard's `widget_load{compose}` opens the **stored/pointer** door
(`WorkspacePane.tsx:1375` → `ComposeDirectWidget` `buildLaunchFromSeed` stored branch :130-141 →
`ComposeWorkspace` load; `state.sessionId` = server-minted **document** session). The dispatchable
`sessionFileId` (a `ChatSessionFile` FileId) is minted **later + asynchronously** by the **DEF-10 auto-register
effect** (`ComposeWorkspace.tsx:2558-2582`, once-per-`speDriveItemId`, active tab only) → host
`registerComposeActiveDocument` (`ConversationPane.tsx:1529-1677`): lazily ensures a chat session, uploads the
loaded bytes to `/documents` (:1590-1606) → `sessionFileId`, stores `activeSourceDocRef.current`, and
`documentSessionWaiterRef.current.notify(sessionFileId, documentSessionId)` (:1647).

**Conclusion:** the bridge exists and rides shipped plumbing (no server change). But the file-registration,
the session mint, and the `runExplicit` seam ALL live in `ConversationPane.tsx`.

---

## Bridge design + §11 answers

**Chosen bridge = the shipped DEF-10 register (pointer-open → client re-uploads the ALREADY-LOADED editor bytes
to the existing `/documents` endpoint).** No new component, no new BFF surface.

- **§11 Existing?** Yes — `registerComposeActiveDocument` + the `/documents` endpoint already land every compose
  document (Browse, stored, chat-upload) as a `ChatSessionFile`. The wizard's stored doc is just another stored-doc open.
- **§11 Extend vs new?** Extend (reuse) — zero new registration surface. The only missing datum is `subDomain` on
  the DIRECT compose door (dropped by `ComposeWidgetSeed`/`buildLaunchFromSeed`; the ambient `ThreePaneShell`
  `composeLaunch` also omits it, so ConversationPane's `useComposeLaunch()?.subDomain` is inert for compose launches today).
- **§11 Cost-of-doing-nothing?** Without wiring the FIRING, wizard-finish opens Compose but never registers-for-review
  auto (the review only runs if the user types "review this document"). FR-17's "no manual re-upload, review auto-runs"
  is unmet.
- **ADR-007 tension (documented, NOT a violation):** the shipped DEF-10 bridge re-uploads the loaded docx bytes to
  `/documents` rather than registering the SPE pointer by reference. This is the platform's established compose-direct
  landing (the bytes are already in the editor; no second SPE fetch), and there is no by-reference registration path
  server-side (audited). A future pointer-registration endpoint would be the ADR-007-preferred shape but is a NEW BFF
  surface — out of scope and unnecessary for FR-17 (the bytes are in hand). Noted for the owner.

---

## Bind analysis — why fork/promote don't fit as-is

- The durable-bind acceptance requires the session bound to **the wizard's** analysis (so comments/memo land where the
  user is looking; the sentinel `HostContext.EntityId = analysisId` flows into dispatch envelopes as `HostEntityId`
  and routes the memo to THAT analysis's `sprk_analysisoutput`).
- `fork` (`{priorSessionId!, documentId!, name!}` → 201 `{analysisId, newSessionId}`) and `promote`
  (`{sessionId!, name!, documentId?}` → 201 `{analysisId, sessionId}`) **both call `CreateAnalysisAsync`** → a NEW,
  MINIMAL analysis (documentId + name + playbookId). Using either would (a) duplicate the wizard's analysis and (b)
  lose the wizard's rich payload (worktype, agreementtype lookup, assignees, associations, field mappings, todo, email).
- **The durable FK write itself is fine** — `BindSessionToAnalysisAsync(tenant, session, analysisId)` (with the hub's
  fix) writes/creates the FK for ANY analysisId, and `CreateSessionAsync` writes the FK at create time for an
  Analysis-owned HostContext. What's missing is a way to point EITHER at the wizard's PRE-EXISTING analysisId without
  minting a second Analysis.

**Two ADR-compliant ways to close it (owner decision — see Escalation):**
- **(B-i) client-side, no server change:** mint the compose session with `HostContext{ EntityType:"sprk_analysisoutput",
  EntityId: wizardAnalysisId, EntityName: name }` at create time → `CreateSessionAsync`'s create-time FK write binds
  the wizard's analysis durably. **Requires** `ConversationPane.registerComposeActiveDocument`'s `ensureChatSession`
  to accept/inject that HostContext for the wizard-launched compose → **ConversationPane.tsx change**. Sentinel MUST
  reference an existing constant site (`ChatSessionManager.AnalysisHostContextEntityType` :473, or the client's
  existing sentinel), never re-typed.
- **(B-ii) server-side, minimal endpoint:** a `bind-to-existing` variant (`{sessionId!, analysisId!, name!}` → binds
  via `PromoteSessionToAnalysisAsync`, SKIPPING `CreateAnalysisAsync`). This is a NEW BFF surface → §10 Placement
  Justification + publish-size check. Small (reuses the existing bind + HostContext-update seam), but it IS a new endpoint.

---

## Auto-run (dispatch) analysis — converges on ConversationPane

`runExplicit` (useAgreementReviewGate.ts:440-482 — **mine to extend**) is the correct dispatcher (023 left it clean;
inherits 031's DEF-09 routing). But arming/firing it needs, together: the gate controller instance (created in
ConversationPane), the minted `sessionFileId` (`activeSourceDocRef.current`, ConversationPane), and `subDomain`
(must reach the ambient compose-launch context ConversationPane reads). The existing buffered explicit-door effect
(`ConversationPane.tsx:1975-1983`) is the natural firing point but is armed only by a text message and reads a
`subDomain` that is inert for compose launches. There is **no ConversationPane-free firing path** — the impedance is
centralised there by the shipped architecture. Extending `useAgreementReviewGate` alone cannot self-fire without
ConversationPane passing it a compose-open signal (a ConversationPane change).

---

## Failure-mode design (ADR-019) — how the three legs would fail DISTINCTLY

Design carried for whoever wires legs 2/3 (so the wiring is correct, not improvised):

1. **Ordering (recoverable at every step):** (1) wizard creates the rich Analysis + document (already durable, Phase-1
   shipped) → (2) open Compose (shipped) → (3) DEF-10 register lands the `ChatSessionFile` + document session → (4) BIND
   the session to the wizard's analysis → (5) auto-dispatch `runExplicit`. Each later step failing leaves the earlier
   durable state intact and explainable — **no orphan half-flow** (the Analysis record always exists and is consistent;
   worst case the review didn't auto-run and the user can trigger it manually, exactly today's behavior).
2. **Bridge failure** (register/`/documents` upload fails): `registerComposeActiveDocument` already degrades non-fatally
   (:1672 "the direct upload just won't be chat-visible"); the auto-run must NOT fire on a null `sessionFileId` (the
   `documentSessionWaiter` 8s timeout → `null` already models this) → surface a distinct "couldn't prepare the document
   for review" affordance in the Assistant; the Analysis + open Compose stay valid.
3. **Bind failure** (B-i: session mint with Analysis HostContext fails; B-ii: bind-to-existing non-2xx): distinct from
   bridge failure — the file is registered but the durable FK isn't written → the review can still run in-session, but
   surface "review results won't be saved to this Analysis yet" (durable recall degraded) and do NOT claim success. The
   hub's fix guarantees the SERVER never returns a silent 201-without-FK (regression-tested here); a transport/500 is a
   real, surfaced error (ProblemDetails).
4. **Dispatch failure** (`runExplicit`/binding unavailable): 023's `runExplicit` already never blocks and degrades on a
   missing bindingId; surface a distinct "review couldn't start" message. Comments never half-render (030/031/032's
   durable path only renders on a real completed output).

Each surface is DISTINCT (wizard error vs Assistant affordance) and no step silently swallows another's failure.

---

## ESCALATION (CLAUDE.md §6.5 format)

🔔 **Cross-boundary / new-surface — Resolution Required**

- **Boundary in question:** HARD BOUNDARY "`ConversationPane.tsx` is task 041's this wave — STOP if it must change";
  and BFF §10 (a new endpoint needs Placement Justification).
- **Conflict:** FR-17's BIND (session→wizard's Analysis) and AUTO-DISPATCH (`runExplicit`) legs both require code where
  the gate controller + `sessionFileId` + session-mint live = `ConversationPane.tsx`, OR a new `bind-to-existing` BFF
  endpoint. Neither is buildable in this task's allowed files. The BRIDGE leg needs neither (shipped DEF-10 + existing
  `/documents`).
- **Proposed path:** **A (project-scoped coordination) + minimal delta.** Recommend the owner pick the BIND mechanism,
  then wire legs 2/3 in ConversationPane in coordination with task 041 (ConversationPane's current owner), sequenced
  with a commit between (per project CLAUDE.md hot-file rule: 021→031→**041**→042; 033's ConversationPane wiring slots
  alongside/after 041). This task delivered the escalation-independent pieces (FK verification + regression) so the
  remainder is a focused, well-specified wiring task.
- **Recommended minimal delta (smallest correct footprint):**
  1. **subDomain delivery (client, ~3 files, mine + shared, non-ConversationPane):** add `subDomain` to `ComposeWidgetSeed`
     (`composeWidgetData.ts`) + read it in `buildLaunchFromSeed` (`ComposeDirectWidget.tsx`) + thread it into the
     ambient compose-launch context ConversationPane reads (`ThreePaneShell` composeLaunch, which today omits it). These
     are additive and unblock BOTH the auto-run AND 023's already-shipped explicit-door read.
  2. **BIND = prefer B-i (no server change):** mint the wizard-launched compose session Analysis-owned so the create-time
     FK binds the wizard's analysis (one focused change in `registerComposeActiveDocument`/`ensureChatSession`; sentinel
     via existing constant). Fall back to B-ii (`bind-to-existing` endpoint) only if B-i proves infeasible.
  3. **AUTO-DISPATCH:** arm the existing buffered explicit-door effect (`ConversationPane.tsx:1975-1983`) on compose-open-
     with-`subDomain`+`activeWorkType` (once-per-doc), firing the already-clean `runExplicit(sessionFileId, fileName,
     subDomainKey)` when `sessionFileId` lands (add a readiness bump in `registerComposeActiveDocument`, which today
     bumps `sourceDocReadyToken` only on chat upload).
- **Alternatives considered + rejected:**
  - *Use promote/fork as-is* → duplicates the wizard's rich Analysis + loses its payload. Rejected.
  - *Reorder wizard to let promote create the Analysis, then PATCH rich fields* → invasive, changes shipped Phase-1
    ordering, loses atomicity. Rejected.
  - *Build the auto-run in a new host component (avoid ConversationPane)* → would duplicate the gate + waiter + registrar
    machinery ConversationPane already owns (§11 violation). Rejected.
  - *Silently edit ConversationPane.tsx anyway* → boundary violation + real conflict risk with task 041's uncommitted
    edits in the shared worktree (no commit-between available to me). Rejected.

---

## Delivered (escalation-independent, in-bounds) ✅

**FK-regression bind-visibility round-trip test** — `tests/integration/regression/Analysis/PromoteDurableFkVisibilityTests.cs`
(KEEP path #2 regression; namespace `Sprk.Bff.Api.Tests.Integration.Regression.Analysis`; globbed via csproj:88).

- Proves **FR-17 acceptance criterion 2** at the exact by-analysis-visibility level the silent-FK gap broke — the
  ROUND-TRIP the hub's stateless unit test omits (it asserts `CreateAsync` carries the FK, never queries by-analysis).
- Wires the **REAL `ChatDataverseRepository`** over a stateful in-memory `IGenericEntityService` double (module-boundary
  double, ADR-038 §7 / B5 preferred — not a mock of the class under test). 3 tests, all green:
  1. `BindSessionToAnalysis_WhenNoPreExistingSummaryRow_ByAnalysisReturnsBoundSession` — the silent-FK-gap regression
     (empty table → bind → by-analysis returns the session).
  2. `BindSessionToAnalysis_WhenExistingLooseSummaryRow_ByAnalysisReturnsBoundSession` — the update branch round-trip.
  3. `GetSessionsByAnalysis_ForADifferentTenant_ReturnsEmptyEvenWhenFkMatches` — tenant isolation on the durable-bind
     round-trip (ADR-014/ADR-028).
- **Result:** `dotnet test --filter PromoteDurableFkVisibilityTests` → **Passed! Failed: 0, Passed: 3, Skipped: 0**.

## Deferred / NOT built (blocked on the escalation)

- subDomain-seed threading (would be partial dead code until the ConversationPane arming lands — the ambient provider
  ConversationPane reads still omits subDomain; building it now = scope creep per §11).
- BIND wiring (needs the owner's B-i vs B-ii decision).
- AUTO-DISPATCH arming (ConversationPane.tsx — HARD BOUNDARY).
- Live UAT / browser e2e (deferred to 060/061 per the project convention; also blocked on the above).

## §10 / §11 for the delivered change

- **§10 BFF Hygiene:** N/A — **zero `src/server/**` files modified** (all server reads were verification-only). No new
  endpoint, package, or DI. Publish-size unaffected (test-only change).
- **§11:** the test's in-memory double is justified (models Dataverse's round-trip; no simpler existing double proves
  by-analysis visibility of a bind-created row — the existing `CapturingChatDataverseRepository` and the stateless Moq
  unit suite both stop before the by-analysis read).

## Files

- **Added:** `tests/integration/regression/Analysis/PromoteDurableFkVisibilityTests.cs` (new regression test + in-memory double).
- **Not modified (HARD BOUNDARIES honored):** `src/server/**` (READ-ONLY verification), `ConversationPane.tsx`,
  `ComposeCommentGutter.tsx`, `.claude/**`, `current-task.md`, `TASK-INDEX.md`. No git commit/push.

---
---

# WIRING — Part 2 (escalation resolved: B-i approved, ConversationPane freed at `9b28628bc`)

## B-i rationale citation (the coordinator's decision, 2026-07-31)

**Approved:** mint the compose session ANALYSIS-OWNED via HostContext (`EntityType` = the existing
`"sprk_analysisoutput"` sentinel, `EntityId` = the wizard's pre-existing analysisId) so
`CreateSessionAsync`'s create-time FK write binds it durably — the exact path
`PromoteDurableFkVisibilityTests` already proves at the repository level, hardened by hub fix `2f8f11123`.
**Rejected:** B-ii (new bind-to-existing endpoint) per §11 reuse-first + ADR-013 no-new-surface. Zero
`src/server/**` changes (verified: none made).

## The design refinement that made B-i COMPLETE (found during wiring, not in the escalation doc)

The escalation doc's Part-1 design had legs 2/3 landing in ConversationPane only. Wiring surfaced a
**deeper impedance** the coordinator's decision then resolves even more cleanly: the review dispatch
(`runExplicit` → `sessionIdOverride` = the waiter-resolved **document session**) targets the session whose
`UploadedFiles` must contain the file — but for a stored-doc open, ComposeWorkspace's document session was a
**separately minted** compose session (`ComposeService.LoadAsync` :432-438), while the register uploads into
the **chat** session. Dispatching on the document session would have hit `ResolveTargetFiles` → "No session
files were available" (verified server-side: dispatch fetches the override session and resolves ONLY its own
manifest — `SessionDispatchOrchestrator.cs:214-221` 404-on-missing, `:1170-1195` own-manifest-only).

**Resolution — SESSION COINCIDENCE (the upload-mount door's invariant, extended to the wizard door):** the
WIZARD mints the Analysis-owned session at finish (it owns the moment when analysisId + documentId + name
are all in hand and "no session exists"), and that ONE session becomes:
1. the **document session** — the seed's `composeSessionId` threads `ComposeDirectWidget` →
   `<ComposeWorkspace initialSessionId>` → the BFF Load's `?sessionId=` (ComposeWorkspace.tsx:910, already
   shipped) → `ComposeService.LoadAsync` RESUME (`IsSameCrossVersionBinding` :612-622 — DocumentId
   ordinal-match, matter-null pass). The session is minted with `documentId` = the bare-lowercase
   `sprk_document` GUID precisely so it matches the server's resume bindingId
   (`DocumentRecordId.Value.ToString()`, "D" lowercase — ADR-044 alignment made load-bearing).
2. the **chat session** — ConversationPane's new workspace-channel listener ADOPTS it
   (`handleSelectHistorySession`, the identical hub-grid/History mechanism + `ensureChatSession`'s
   documented synchronous-ref pattern), so the DEF-10 register's lazy-create guard finds it and uploads the
   file INTO it (never a second session).

Chat ≡ document session ⇒ the register's `/documents` upload, `documentSessionWaiter.notify`, the review's
`sessionIdOverride` dispatch, the compose-outputs ledger read, AND the durable `sprk_analysis` FK all
converge on ONE session. DEF-09 holds; the file-id impedance is gone; by-analysis returns this session.

## What was wired (all client; every server read remained read-only)

1. **`CreateAnalysisWizardWidget.tsx`** (Spaarke.AI.Widgets):
   - `ANALYSIS_SESSION_HOST_ENTITY_TYPE` — the ONE client sentinel constant site (doc comment carries the
     footgun + the server's triplicated sites incl. `ChatSessionManager.cs:473`); call sites import it,
     never re-type the literal.
   - `onFinish`: for agreement work-type + full SPE pointer, POST `/api/ai/chat/sessions`
     `{ documentId: <bare-lc sprk_document GUID>, hostContext: { entityType: SENTINEL, entityId: <bare-lc
     analysisId>, entityName: finishName } }` (the endpoint passes HostContext verbatim —
     `ChatEndpoints.cs:361-393`, no validation barrier; `ChatCreateSessionRequest` :3173). Failure =
     NON-FATAL + the DISTINCT bind-failure warning on the wizard success panel; hand-off fields are then
     OMITTED (no half-armed auto-run pointing at a nonexistent session).
   - Compose seed: additive `{ composeSessionId, analysisId, autoRunReview: true-only-when-subDomain-picked }`
     alongside the already-shipped `subDomain`/`activeWorkType`.
2. **`composeWidgetData.ts`** (SpaarkeAi): `ComposeWidgetSeed` + typed `subDomain` (declaring the field the
   wizard has sent since A3) + `composeSessionId`/`analysisId`/`autoRunReview` (documented).
3. **`ComposeDirectWidget.tsx`** (SpaarkeAi): threads `data.compose.composeSessionId` →
   `<ComposeWorkspace initialSessionId>` (was hardcoded `""`); absent seed → `""` = exact pre-033 wire shape.
   ZERO `Spaarke.Compose.Components` changes (ComposeWorkspace already forwards `initialSessionId` to Load).
4. **`ConversationPane.tsx`** (freed):
   - Workspace-channel `usePaneEvent` listener (rides the wizard's EXISTING `widget_load{compose}` event —
     no new channel/event type, ADR-030): narrows the seed to `ComposeWidgetSeed`; on
     `composeSessionId+analysisId` → once-per-analysisId (ref Set) ADOPT via `handleSelectHistorySession` +
     synchronous `chatSessionIdRef` update; on `autoRunReview===true && subDomain` → arm
     `setAgreementReviewGateNeeded(true)` + `setPendingExplicitAgreementReview({subDomainKey})` (023's
     SHIPPED buffer — `runExplicit` fires deterministically, classifier sanity-check non-blocking) + the
     watchdog. Non-wizard seeds return immediately (zero behavior change).
   - `registerComposeActiveDocument`: `setSourceDocReadyToken(t=>t+1)` after the waiter notify — the
     GENERIC readiness bump (mirrors `handleSessionFileUploaded`'s, per the token's own documented
     "generic, not revise-specific" contract) so the machine-armed buffer fires when the register lands.
   - Bridge-failure watchdog (`WIZARD_AUTO_RUN_WATCHDOG_MS` = 30s + `WIZARD_AUTO_RUN_BRIDGE_FAILURE_MESSAGE`):
     armed only for WIZARD-armed buffers; stands down silently when the buffer is consumed (dispatch fired)
     or the session resets; on expiry clears the buffer + injects the DISTINCT recovery message. The TEXT
     door's indefinite-buffer semantics are untouched.

## ADR-019 failure legs as BUILT (three legs, three DISTINCT surfaces, no orphans)

| Leg | Failure | Surface | State left |
|---|---|---|---|
| BIND | wizard session-mint non-2xx/throw | Wizard success panel warning ("review could not be started automatically…") | Analysis fully durable + consistent; Compose opens; NO hand-off fields (auto-run never half-arms) |
| BRIDGE | DEF-10 register upload fails / never lands | Assistant watchdog message after 30s (`WIZARD_AUTO_RUN_BRIDGE_FAILURE_MESSAGE`) — names the recovery ("ask me to review this document") | Session adopted + Analysis-bound; buffer cleared (a late register can't fire a stale run) |
| DISPATCH | review dispatch fails | The SHIPPED chips/dispatch error surface (`runBindingDispatch`) — untouched | File registered + session bound; user retriggers conversationally |

## agreementTypeLookupWrite (coordinator item 3a) — SKIPPED, per instruction

The wizard flow already persists `_sprk_agreementtype_value` at finish via the A1 picker write
(`CreateAnalysisWizardWidget.tsx` — the `sprk_agreementtype` discoverNavProps + `sprk_AgreementType`
fallback block, hub `1e1a6579b` + naming fix). 023's `applyAgreementTypeToAnalysis` seam remains the
CLASSIFIER-path (promote) writer; invoking it here would be a redundant second write of the same lookup.

## Tests (Part 2)

**Spaarke.AI.Widgets — `CreateAnalysisWizardWidget.test.tsx` (+3, new describe "task 033"):**
1. Mints the ANALYSIS-OWNED session (wire-level body assert: `documentId` + `hostContext{entityType:
   'sprk_analysisoutput', entityId: <analysisId>, entityName}`) + hands off
   `composeSessionId/analysisId/subDomain/autoRunReview` on the compose seed.
2. NEGATIVE: non-agreement work type → ZERO session mint; seed carries NONE of the hand-off fields.
3. Bind-failure: mint attempted once, wizard still succeeds, Compose still opens WITHOUT hand-off, the
   distinct warning renders on the success panel.

**SpaarkeAi — `ConversationPane.wizard-auto-run.e2e.test.tsx` (new, 6 tests; 031's forcing harness:
real ConversationPane + real PaneEventBus + unmocked `createConsumerDispatcher` + session-keyed in-memory
ledger; STATEFUL cold-pane session mock — starts null, adoption observable):**
1. Happy path: adopt → register lands → EXACTLY ONE review dispatch on the WIZARD session
   (`/sessions/{WIZARD_SESSION}/dispatch`), `args.subDomain='employment'` (classifier sanity-check
   disagreeing at 0.95 toward 'nda' never re-routes), `args.fileIds=[sessionFileId]`; upload went to the
   SAME session; ledger entry (`disposition:'compose'`) in the SAME session; no failure message.
2. Duplicate hand-off for the SAME analysisId → still exactly one dispatch (once-per-Analysis dedupe).
3. NEGATIVE: plain compose open (no hand-off fields) → no adoption, no arming, no dispatch, no messages.
4. NEGATIVE: hand-off WITHOUT autoRunReview/subDomain → adoption + file lands (durable bind leg alone),
   NO review dispatch.
5. Bridge failure (upload 500, fake timers): after `WIZARD_AUTO_RUN_WATCHDOG_MS` the DISTINCT message is
   injected, no dispatch ever, and a LATE successful register cannot fire a stale run.
6. Watchdog never false-alarms on the happy path (timer advance after success → no message).

### Results (exact)

```
CreateAnalysisWizardWidget.test.tsx                      10/10  PASS (7 pre-existing + 3 new)
ConversationPane.wizard-auto-run.e2e.test.tsx (new)       6/6   PASS
Full SpaarkeAi package suite (npx jest, stable tree)     90 suites / 832 tests  ALL PASS
                                                          (baseline 89/826 per 023 notes + this task's 1/6;
                                                          one earlier run flaked CreateOnSaveAssociation —
                                                          14/14 in isolation + two full-suite green re-runs)
SpaarkeAi npm run typecheck (tsc-surface-gate)           0 surface-owned errors (73 pre-existing shared-lib
                                                          errors deferred — unchanged baseline)
SpaarkeAi npm run build (vite)                           GREEN (exit 0; ribbon launch scripts built)
Full Spaarke.AI.Widgets suite (npx jest)                 38 suites: 37 PASS + 1 PRE-EXISTING FAIL
                                                          (register-workspace-widgets.test.ts:379 expects
                                                          displayName 'Communications', code at HEAD says
                                                          'Messages' — a sibling messaging-project rename;
                                                          PROVEN pre-existing at clean HEAD via scoped
                                                          git-stash re-run; zero relation to task 033)
Spaarke.AI.Widgets npm run typecheck                     exit 0 (pre-existing Communication.Components
                                                          errors reported, none in this task's files)
Spaarke.AI.Widgets npm run build                         FAILS AT CLEAN HEAD identically (the SAME
                                                          pre-existing Spaarke.Communication.Components
                                                          errors — proven via scoped git-stash; this task's
                                                          files contribute ZERO build errors)
tests/integration/regression/Analysis (Part 1, dotnet)   3/3 PASS (C# untouched in Part 2)
```

## §10 / §11 (Part 2)

- **§10:** still N/A — zero `src/server/**` modifications (B-ii rejected; B-i is entirely client-side over
  the EXISTING `POST /api/ai/chat/sessions` + Load-resume + `/documents` + dispatch contracts). No publish-
  size impact.
- **§11 (per new surface):**
  - `ANALYSIS_SESSION_HOST_ENTITY_TYPE`: Existing = server-side triplicated literal only (no client
    constant existed — grep-verified); Extension = a constant, not a module; Cost-of-doing-nothing =
    re-typed sentinel literals (the exact footgun the project CLAUDE.md bans).
  - The workspace listener/watchdog: Existing = 023's buffer + `handleSelectHistorySession` +
    `sourceDocReadyToken` — ALL reused, zero new dispatch/session/registration machinery; the only new
    mechanism is the ~30-line listener + a bounded timer. Cost-of-doing-nothing = FR-17 unmet (no auto-run)
    and machine-armed intents silently stuck forever (the watchdog's concrete failure mode).
  - Seed fields: additive on the ONE existing seed shape (no parallel envelope).
