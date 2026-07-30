# Task 023 — Two-tier session model (loose vs Analysis-owned + explicit promotion)

> spec FR-07 / PLAN §2.4. Completed 2026-07-29.

## What shipped

**Server (BFF)**

- `IChatDataverseRepository.BindSessionToAnalysisAsync(tenantId, sessionId, analysisId, ct)` — new
  interface member. Binds the `sprk_analysis` FK onto an **existing** `sprk_aichatsummary` row
  (query-then-update, same tenant-scoped shape as `ArchiveSessionAsync`). Implemented in
  `ChatDataverseRepository.cs`.
- `ChatSessionManager.PromoteSessionToAnalysisAsync(tenantId, sessionId, analysisId, analysisName, ct)`
  — re-fetches the session, calls the FK bind above, then updates the session's `HostContext` in
  place to the Analysis-owned convention (`EntityType="sprk_analysisoutput"`, `EntityId=analysisId`)
  and writes through Redis + Cosmos via the existing `UpdateSessionCacheAsync`. **No new session is
  minted; no archive occurs.** The FK-bind failure is NOT swallowed (unlike `CreateSessionAsync`'s
  tolerant posture) — it propagates so the caller can compensate; see design note below.
- `POST /api/ai/analysis/promote` (`AnalysisEndpoints.PromoteSession`) — the lighter sibling of the
  task-021 `/fork` endpoint. Validates `sessionId` + `name`, rejects a session that is already
  Analysis-owned (400, one-time-bind guard), resolves a `documentId` (request override or the
  session's own), creates the Analysis via `IAnalysisDataverseService.CreateAnalysisAsync`, then
  binds via `ChatSessionManager.PromoteSessionToAnalysisAsync`. On any failure after the Analysis is
  created (bind throws, or the session vanished), the Analysis is compensated (deleted) — same
  no-orphan discipline as `ForkAnalysis`'s mint-failure rollback.
- New contracts: `AnalysisPromoteRequest` / `AnalysisPromoteResponse`
  (`AnalysisPromoteContracts.cs`).

**Client (SpaarkeAi)**

- `HistoryOverlay.tsx` (the `HistoryMenu` "History ▾" dropdown) gained a small "Promote to
  Analysis…" icon-button per session row + an inline Fluent v9 `Dialog` (name input → `POST
  /api/ai/analysis/promote`). **Zero changes to `ConversationPane.tsx`** — `bffBaseUrl` +
  `authenticatedFetch` were already passed into `HistoryMenuProps`, so the whole feature is
  self-contained in the sibling file. This was a deliberate scope choice per the task's merge-order
  coordination note (`spaarke-ai-architecture-redesign-r1` was decomposing `ConversationPane` in
  parallel — see design note below).

**Tests** (all green; see Build/test summary below)

- `ChatDataverseRepositoryTests.cs` — 2 new tests for `BindSessionToAnalysisAsync` (binds the
  existing row tenant-scoped; tolerant when no row exists, mirrors `ArchiveSessionAsync`).
- `ChatSessionManagerTests.cs` — 3 new tests for `PromoteSessionToAnalysisAsync` (binds in place +
  updates HostContext + no new session minted; returns null when session not found; FK-bind failure
  propagates and does NOT write through the cache — see design note).
- `AnalysisPromoteEndpointContractTests.cs` (new file, mirrors `AnalysisForkEndpointContractTests.cs`)
  — 7 contract tests: happy-path bind-in-place, 401 unauthenticated, 404 session-not-found, 400
  double-promote guard, 400 no-document, 500 + Analysis-rollback on bind failure, and the **negative
  acceptance criterion**: creating + using a loose session (no promote call) never invokes
  `CreateAnalysisAsync`.
- `AnalysisForkEndpointContractTests.cs`'s `CapturingChatDataverseRepository` test double extended
  with `Bound`/`FailOnBind` to satisfy the new interface member (reused rather than duplicated per
  CLAUDE.md §11).

## Design note — FK-bind failure must propagate (course-correction during implementation)

The FIRST draft of `PromoteSessionToAnalysisAsync` swallowed a `BindSessionToAnalysisAsync` failure
(mirroring `CreateSessionAsync`'s tolerant "Redis is enough, Dataverse audit trail is best-effort"
posture) — this was **wrong** for promotion specifically, because the durable FK *is* promotion's
entire deliverable (it is what makes the Analysis queryable via `GetSessionsByAnalysisAsync` / visible
in the Analysis hub grid, task 030). A caught-and-swallowed bind failure would have left the caller
believing promotion succeeded while the Analysis sat orphaned with zero bound sessions. Caught by the
endpoint contract test `Promote_BindFailsAfterAnalysisCreate_RollsBackAnalysisAndReturns500` failing
(got 201 instead of 500) during Step 9.5 verification. Fixed by removing the try/catch — the
exception now propagates to `AnalysisEndpoints.PromoteSession`, which compensates (deletes the
Analysis) exactly like `ForkAnalysis`'s existing mint-failure rollback. Both the manager unit test and
the contract test were updated to assert the corrected (propagate + compensate) behavior.

## Scope decision — History-list tier visualization deferred

The task's acceptance criteria require both tiers to **appear** in session history (already true —
`ChatEndpoints.ListRecentSessionsAsync` / `SessionPersistenceService.ListRecentSessionsAsync` lists
every Cosmos-stored session for the tenant with no analysis-FK filter) but do NOT require the History
list to visually *distinguish* loose vs Analysis-owned rows. Investigating this surfaced a **pre-existing,
unrelated gap**: `StoredSession.EntityRefs` (the field `ListRecentSessionsAsync` reads for its
`EntityType`/`EntityName` projection) is never written by any code path today — it is always the
default empty list. Wiring `ChatHostContext` → `StoredSession.EntityRefs` to make the History list
tier-aware would be a legitimate follow-up, but was explicitly out of scope for task 023 (not named in
the acceptance criteria, and touches a different, currently-dead subsystem). Because of this, the
"Promote to Analysis…" affordance is offered on **every** row (not gated to visually-loose rows) —
the server's one-time-bind guard (400 on an already-Analysis-owned session) is the actual enforcement
point, not client-side hiding. Flagging this gap for a future task rather than silently expanding
scope (CLAUDE.md §11).

## Merge-order coordination (ConversationPane)

`/conflict-check` (run before finalizing) found zero open PRs touching `HistoryOverlay.tsx`,
`AnalysisEndpoints.cs`, `ChatSessionManager.cs`, `ChatDataverseRepository.cs`, or
`IChatDataverseRepository.cs` other than this project's own PR #694. No active worktree matching
`spaarke-ai-architecture-redesign-r1`/`r2` was found in `git worktree list` at task time (that
project's ConversationPane decomposition work — ADR-040 gate G-P0 etc. — appears to have already
landed on master per root CLAUDE.md's references to it as shipped). The client change here touches
ONLY `HistoryOverlay.tsx`, never `ConversationPane.tsx`, so the decomposition risk this note was
meant to guard against does not apply to this diff.

## Build/test verification

- `dotnet build src/server/api/Sprk.Bff.Api/` — succeeded (0 errors, pre-existing warnings only).
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/` (full suite) — 9,027 passed / 8 failed / 101 skipped.
  All 8 failures confirmed **pre-existing** (reproduced identically on a clean `git stash -u` baseline
  before this task's changes): 3 in `AnalysisEndpointsExecuteDispatchContractTests` (DI-registration
  gap in that fixture, unrelated to session/analysis code) + 5 in `Services/Communication/*` (a
  different subsystem, untouched by this task).
- `npm run typecheck` (SpaarkeAi) — `tsc-surface-gate`: 291 pre-existing errors in shared libs
  (deferred to Phase B, per the gate's own baseline), **0 surface-owned errors** — this task's
  `HistoryOverlay.tsx` edit introduced no new type errors.
- Publish-size: **47.52 MB compressed** (`deploy/api-publish.zip`, `Compress-Archive -CompressionLevel
  Optimal`) vs the ~47.51 MB baseline stated in the task brief — **+0.01 MB delta**, far under the
  +5 MB single-task justification threshold and the 55/60 MB review/hard-stop ceilings (ADR-029).
- CVE scan: `dotnet list package --vulnerable --include-transitive` shows the same
  `System.Security.Cryptography.Xml 8.0.3` HIGH advisories as before this task — **pre-existing**
  (this task added zero NuGet package references), confirmed by an unchanged package graph.

## Placement Justification (CLAUDE.md §10)

- **Existing**: `ChatSessionManager` / `ChatDataverseRepository` already own session lifecycle +
  the task-020 Analysis FK write; `AnalysisEndpoints.cs` already owns `/fork` (the sibling
  mint-and-archive operation).
- **Extension**: promotion is a lighter sibling of `/fork` — same file, same DI dependency shape
  (`IAnalysisDataverseService` + `ChatSessionManager` + `IGenericEntityService`), reusing the
  existing FK-bind convention. No new service, no new DI registration, no new BFF module.
- **Cost of doing nothing**: without an explicit promotion path, a loose (casual) session has no way
  to become a first-class, named `sprk_analysis` after the fact — the ONLY way to associate a
  session with an Analysis today would be the heavier `/fork` (which mints a second session and
  archives the first), which is the wrong shape for "I want to keep chatting AND name this as an
  Analysis."
