# Task 021 — Fork-on-analysis BFF endpoint `POST /api/ai/analysis/fork`

> UQ-1 Option B · §6.5 Path A (owner-approved 2026-07-28) · FULL rigor · opus/xhigh · completed 2026-07-28

## What shipped

A single authenticated minimal-API handler **`POST /api/ai/analysis/fork`** that atomically composes
existing server-owned seams (NO `Services/Ai/` fork):

1. **Snapshot + verify prior** — `ChatSessionManager.GetSessionAsync` materialises the prior transcript
   into the Cosmos store-of-record (ADR-040 Path A) and confirms existence **before any write** →
   a missing/expired prior returns 404 and orphans nothing.
2. **Create Analysis** — `IAnalysisDataverseService.CreateAnalysisAsync(documentId, name, playbookId)`.
3. **Mint bound session** — `ChatSessionManager.CreateSessionAsync` with a
   `ChatHostContext(EntityType="sprk_analysisoutput", EntityId=analysisId)` — the exact convention
   `ChatDataverseRepository.CreateSessionAsync` keys the **task-020 `sprk_aichatsummary.sprk_analysis` FK**
   write on, so the FK fires on the NEW analysis.
4. **Archive prior** — `IChatDataverseRepository.ArchiveSessionAsync` (durable `sprk_isarchived` flip is
   task 022's scope). Best-effort/non-fatal.

Returns `{ analysisId, newSessionId, archivedSessionId }` (201 Created). The client's only job is to store
`newSessionId` + show the warning (out of scope here — a later SpaarkeAi task).

### Files
- `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` (endpoint registration + `ForkAnalysis` handler + `ExtractTenantId`/`AnalysisProblem` helpers)
- `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisForkContracts.cs` (new — `AnalysisForkRequest` / `AnalysisForkResponse`)
- `tests/integration/contract/Api/Ai/AnalysisForkEndpointContractTests.cs` (new — 4 tests, all pass)

## Placement Justification (CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`)

- **Placement = BFF (in `Api/Ai/`), and it MUST be server-side.** Session GUIDs are minted 100% server-side
  (`ChatSessionManager.cs:108`); the client only reads back the id. Atomic fork therefore cannot live on the
  client (UQ-1 Option B). This is the §6.5 **Path A** project-scoped exception to §10/ADR-013, owner-approved
  2026-07-28 (spec FR-06 + ADR Tensions row 2).
- **One minimal-API handler composing already-DI'd services.** NO new service class, NO new DI registration,
  NO new NuGet package, NO new background work. All four BFF decision criteria (latency-coupled, transactionally
  coupled, no new external dep, AI-surface code in `Api/Ai/`) answer "BFF".
- **ADR-013 facade discipline honored:** injects `IAnalysisDataverseService` + `ChatSessionManager` +
  `IChatDataverseRepository` (analysis-CRUD + session-lifecycle seams) — NOT AI-internal orchestration types
  (`IOpenAiClient`, `IPlaybookService`, `AnalysisOrchestrationService`). **Zero edits to `Services/Ai/`**
  (sole-owned by `spaarke-ai-architecture-redesign-r2`) — verified by `git status` + `/conflict-check`.
- **Endpoint/DI symmetry (ADR-032):** the endpoint is unconditionally mapped in `MapAnalysisEndpoints`; every
  dependency (`IAnalysisDataverseService`, `ChatSessionManager`, `IChatDataverseRepository`,
  `IGenericEntityService`) is registered **unconditionally** (AnalysisServicesModule) → no Null-Object
  kill-switch required.

## Publish-size (§10 / NFR-01 / ADR-029)

`dotnet publish -c Release … -o deploy/api-publish/` → **47.51 MB compressed** (zip, Optimal).
Baseline was 47.51 MB → **delta 0.00 MB** (no package added). Well under the 60 MB HARD ceiling.
(Uncompressed 143.05 MB incl. PDBs / 141.00 MB excl. PDBs.)

## CVE (§10)

`dotnet list package --vulnerable --include-transitive`: the ONLY HIGH is the pre-existing
`System.Security.Cryptography.Xml 8.0.3` (known/unrelated, transitive). **No NEW HIGH CVE** — task 021 added
no packages.

## Design decision (surfaced per parent instruction) — transcript-preserving archive

The parent's shorthand for step 4 was "archive = Redis evict + Cosmos delete + snapshot." Following that
literally would call `ChatSessionManager.DeleteSessionAsync`, which **hard-deletes the Cosmos document**
(its GDPR-erasure path). That would:
- **lose the prior transcript** → the exact "archived-but-transcript-lost" orphan the task forbids
  (acceptance criterion 4 / goal), and
- **violate ADR-040 Path A** (Cosmos = transcript store-of-record; do NOT introduce/replace the store).

**Resolution (directional-mode adaptation):** archive = `IChatDataverseRepository.ArchiveSessionAsync`
(the Dataverse archive-marker seam) **while preserving the Cosmos transcript**. The GetSessionAsync read
IS the "snapshot" — it materialises the transcript into the store-of-record; we never copy it to a second
store and never hard-delete it. The prior session stays **retrievable / switchable-back** (criterion 2,
asserted by test 1). Redis is NOT force-evicted (it ages out on its 24 h TTL) — avoiding both a
cache-key-internals duplication in the endpoint and a needless retrievability gap. The **durable
`sprk_isarchived` flip is explicitly task 022's scope (AIPL-054)**; 021 wires the archive call + guarantees
transcript preservation.

No escalation fired: atomicity was achievable by **composing** public seams (no `Services/Ai/` modification),
and the partial-failure story has no residual-inconsistency window.

## Partial-failure / compensation story (mirrors `CreateSessionAsync` Redis-authoritative tolerance)

- **Ordering** (adapted from the POML's literal order under `mode="directional"`): existence-check → create
  Analysis → mint bound session → archive. The read-only existence check runs FIRST so a missing prior can
  never orphan a freshly-created Analysis.
- **Analysis created, mint throws** (e.g. Redis hot-cache down — `CreateSessionAsync` tolerates a Dataverse
  write failure but not a cache-write failure) → **compensate**: `IGenericEntityService.DeleteAsync("sprk_analysis", analysisId)`
  (with `CancellationToken.None` so client-abort can't skip cleanup) → 500. No dangling Analysis. (test 4)
- **FK bind fails inside the mint** → swallowed by `CreateSessionAsync` (Dataverse-tolerant); the new session
  stays live in Redis (bound in-memory), NOT orphaned — the accepted degraded state.
- **Archive-marker write fails** → non-fatal warning; the fork is already durable and the transcript is
  preserved. Nothing to roll back.
- **Unauthenticated** → 401 at the auth boundary, before any write (test 2).

## Quality gates (Step 9.5) + verification

- **Build:** `dotnet build src/server/api/Sprk.Bff.Api/` green; test project green; no new warnings from fork files.
- **Tests:** 4/4 pass — happy-path 201 + triple + FK-bound new session + prior-still-retrievable; 401
  unauthenticated (no write); 404 prior-not-found (no Analysis created); compensation (mint fails → Analysis
  deleted, 500, prior NOT archived). Placed under the compiled `tests/integration/contract/**` KEEP path
  (ADR-038 / tests/CLAUDE.md) — the parent's "tests/unit" hint was superseded by the binding KEEP-path rule
  (endpoint contract tests belong in `integration/contract/`; `integration/data-mutation/**` is not compiled
  by any csproj in this repo state, and the fixtures live in the contract assembly).
- **code-review:** PASS (0 Critical, 0 blocking). Acknowledged trade-off: `ForkAnalysis` composes 4 steps
  inline in `Api/Ai/` to avoid touching the `Services/Ai/` sole-owned zone.
- **adr-check:** CLEAN (ADR-001/008/013/028/040/032/007/009/010 compliant).
- **/conflict-check:** soft pass — hot-path BFF touched, zero `Services/Ai/` edits, no open-PR overlap on fork files.

## Hand-off to task 022

Task 022 hardens **archive durability** — flip `sprk_aichatsummary.sprk_isarchived = true` durably (needs the
summary-record GUID; currently `ChatDataverseRepository.ArchiveSessionAsync` is an AIPL-054 stub). 021's fork
handler already invokes that seam; 022 makes it durable without changing the endpoint contract.
