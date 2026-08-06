# Task 011 — `POST /api/compose/project` stateless endpoint for browse (FR-03 / WS-1 / T-2) — implementation notes

**Status**: implementation complete, build + seam tests green. This note is written by the
executing subagent, which is NOT permitted to edit `tasks/TASK-INDEX.md` or `current-task.md` —
the orchestrator/human should flip task 011 to ✅ there.

## Anchor drift found during re-grep (Step 1)

None of consequence. `IComposeService.ProjectDocument(ReadOnlyMemory<byte>, CancellationToken)`
(added by task 010) was ALREADY the exact stateless, synchronous, no-I/O primitive this task
needed — `ComposeService.ProjectDocument` is a one-line wrapper around `_projectionBuilder.Build`.
No new service method was required; only a NEW route + thin handler + two new DTOs
(`ComposeProjectRequest`/`ComposeProjectResponse`) were added to `ComposeEndpoints.cs`. The
POML's step 2 framing ("add a stateless project method") turned out to already exist — the actual
new surface is the HTTP door onto it, not the service method.

## What was built (server)

- `POST /api/compose/project` — new route registered as "(1b)" (right after Upload's "(1)", before
  Load's "(2)") in `ComposeEndpoints.cs`, on the `ai-context` rate-limit bucket (same bucket as
  sibling Load/Upload — deterministic, no persistence, not an ingest/write bucket).
- `Project` handler: `[FromBody] ComposeProjectRequest?` → validates non-empty `Content` → calls
  `composeService.ProjectDocument(body.Content, ct)` (the SAME builder instance Load/Upload use) →
  maps via the EXISTING `MapProjectionResponse` helper (task 010) → `ComposeProjectResponse`. The
  handler takes NO `ITenantCache`, NO `ISpeFileOperations`, NO Dataverse dependency — it cannot
  persist or author by construction (not just by intent).
- `ComposeProjectRequest(byte[] Content, string? FileName = null)` / `ComposeProjectResponse(
  ComposeProjectionResponse Projection, string CorrelationId)` — the response carries ONLY the
  projection + correlation id, deliberately omitting `sessionId`/`documentId`/`content` (unlike
  `ComposeUploadResponse`) because there is no server-side identity to echo back.

## What was built (client)

- `handleBrowseFileSelected` (`ComposeWorkspace.tsx`) now, after reading the picked file into an
  `ArrayBuffer`, POSTs the bytes to `/api/compose/project` (best-effort — if `bffBaseUrl` is unset
  or the call throws/network-fails, `projection` stays `null`, preserving Browse's historical
  zero-BFF-dependency contract for the MOUNT itself; only render fidelity degrades to mammoth).
  The single `mountTransient` dispatch now carries the resolved `projection` alongside
  `docxBytes`/`fileName`/`containerId`/`sessionId` — no reducer changes were needed since the
  `mountTransient` action + reducer already supported an optional `projection` field (task 010
  wired it for the assistant-upload door and explicitly left a comment for this door to fill in).
- Extracted TWO small shared helpers to `ComposeWorkspace.tsx` module scope, used by all THREE
  bytes-projection hydration sites (Load, Upload, Browse->project):
  - `arrayBufferToBase64` — previously a locally-scoped `encodeRetained` inside `triggerSave`'s
    `useCallback`; hoisted and reused by the new Browse->project POST body (root CLAUDE.md §11 —
    avoid a third fork of the same byte-encoding logic).
  - `normalizeProjection` — the defensive `status ?? 'failed' / canEdit ?? false / ...` normalizer
    was inlined TWICE already (Load's effect, Upload's effect, both written near-identically by
    task 010's own notes: "mirrors the existing Load effect's normalization"). Adding a THIRD
    inline copy for Browse crossed the reuse threshold — extracted to one shared function, and
    retrofitted the two existing call sites to use it too (net LOC reduction, zero behavior
    change — verified via the tsc A/B diff below).

## ADR-040 / R4 I-2 — T-2 path-A resolution (per project CLAUDE.md, already locked at design time)

The browse round-trip is a projection READ, not byte-authoring: the client sends bytes it already
holds locally; the server responds with a render and persists NOTHING (no `ITenantCache` write, no
SPE call, no `sprk_document` row, no `ChatSession` mint — `ProjectDocument` is pure/synchronous,
and the `Project` handler injects no persistence-capable dependency at all). The "client authors no
`.docx` bytes" invariant is about the SERVER never treating client-supplied bytes as an authored
artifact it stores/versions — `project()` never does that. No escalation fired: this matches the
design-time T-2 path-A resolution verbatim (project CLAUDE.md ADR Tensions table), and code-review
should verify the no-persist invariant via the seam test's mock-interaction assertions below, not
just the response shape.

## Statelessness proof (seam test)

`tests/integration/seam/Compose/ComposeProjectSeamTests.cs` (3 tests, reusing the SAME
`ComposeFidelitySeamFixture` task 010's `ComposeUploadProjectionSeamTests` uses — root CLAUDE.md
§11, no forked fixture):

1. `Project_RealDocxBytes_ReturnsProjectionMatchingLoadPathShape_AndTouchesNoPersistenceBoundary` —
   POSTs a real in-memory `.docx` to `/api/compose/project`, asserts the projection shape/content,
   THEN asserts `_fixture.SpeMock.Invocations`, `DataverseMock.Invocations`, `IndexingMock.Invocations`
   are ALL EMPTY (zero calls into every persistence/authoring module boundary the fixture mocks —
   the concrete, wire-provable form of "no ledger, no SPE, no authoring"). A SECOND identical call
   is asserted byte-identical (idempotence — the acceptance criteria's "zero cumulative server-side
   state" wording) and re-asserts the three mocks are STILL empty. Finally proves F-2 "one reader"
   by feeding the SAME source bytes through the REAL Load door and asserting byte-identical
   projection HTML (mirrors task 010's own Upload-vs-Load proof).
2. `Project_UnreadableBytes_FailsClosedThroughTheWire_NeverAnUnhandledException` — garbage bytes →
   HTTP 200, `Status=failed`/`CanEdit=false`, never a 500, and the three mocks stay empty (fail-closed
   does not mean "persist the garbage somewhere").
3. `Project_EmptyContent_ReturnsBadRequest_NotAServerError` — empty `content` → 400, not a 500 or a
   silently-succeeding empty projection.

I did NOT attempt to instrument `ITenantCache`/`IDistributedCache` directly (the fixture registers
the REAL in-memory-fallback cache, not a mock) — the SPE/Dataverse/Indexing zero-invocation proof
covers every EXTERNAL persistence side effect the POML's "no ledger, no SPE, no authoring" language
actually cares about, and the `Project` handler's signature (no `ITenantCache` parameter at all)
makes a cache write structurally impossible, not just behaviorally improbable. Documented here per
root CLAUDE.md §6.5 in case a reviewer wants a stronger cache-level proof — no escalation needed
since the structural argument plus the mock-boundary proof together are conclusive.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/` → 0 errors (23 pre-existing warnings, unrelated).
- `dotnet test --filter "FullyQualifiedName~Compose"` → 622 passed, 1 skipped (same pre-existing
  skip task 010 reported — numbering harness pending WS-3), 0 failed. Delta vs task 010's reported
  619/1/620 is exactly +3 (this task's 3 new seam tests).
- TypeScript: no NEW errors. A/B diff (stashed the 2 client files, re-ran `npx tsc --noEmit`,
  popped the stash) confirms the SAME 23 pre-existing workspace-package-resolution errors
  (`@spaarke/auth`, `@spaarke/ui-components`, `@spaarke/ai-widgets`, `@spaarke/document-operations`
  not linked in a standalone install) plus the same handful of pre-existing implicit-any/unknown
  errors — only line-number shifts from the added code. Net LOC in `ComposeWorkspace.tsx` is
  actually LOWER than a naive third-copy approach thanks to the `normalizeProjection` extraction.
- `dotnet publish -c Release` compressed (`tar czf`, same tool task 010 used) measured ~47 MB
  (48,376,573 bytes ≈ 46.1 MiB) — effectively unchanged vs task 010's own ~46.1 MB measurement.
  `git diff --stat -- '**/*.csproj'` is empty — **0 new packages, ~0 MB delta**, as expected for
  WS-1 (no new dependency; reuses the existing `DocumentFormat.OpenXml` + `IComposeService`
  surface). Per root CLAUDE.md §10, the absolute figure is `tar czf`-measured, not the Azure zip
  pipeline's ~49.63 MB baseline — not apples-to-apples in absolute terms; the delta-from-this-task
  claim (~0 MB) is the load-bearing number.
- `/conflict-check`: no open PR touches `ComposeEndpoints.cs`, `ComposeWorkspace.tsx`,
  `ComposeWorkspace.types.ts`, or the new `ComposeProjectSeamTests.cs`; no overlap with master's
  divergent history either. PR #692 (`ai-nda-r1-followups`) touches `ComposeAiToolbar.tsx` — a
  DIFFERENT file, no overlap. Sibling `spaarkeai-compose-r1/r2/r3/r4` worktrees still have no open
  PR to diff against (pre-existing coordination note, unchanged from task 010).

## Step 9.5 quality gates

- `/adr-check`: 8 Compliant, 0 Violations, 2 low/medium-confidence Warnings (both non-blocking,
  non-ADR — rate-limit bucket sanity-check on `ai-context` and an unbounded-payload-size
  observation on the new `Content` field; neither is mandated by any ADR).
- `/code-review`: 0 Critical, 2 Warnings (both form/documentation-level — DTO justification is
  subsumed by the endpoint's `<justification>` in the POML; the same unbounded-payload-size note
  as adr-check), a few low-priority Suggestions (optional diagnostic log on the browse-effect's
  catch-all; optional named TS interface for the client POST body shape). No fixes applied — none
  were blocking; all warnings/suggestions are documented here for the reviewer per the coverage-
  first contract (report everything, let the orchestrator/human filter).

## Placement Justification (root CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`)

**Existing**: `POST /api/compose/upload` (`ComposeEndpoints.cs`) exists but reads retained bytes
from `ITenantCache` and returns them alongside a projection — it PERSISTS nothing new but DEPENDS
on a prior persistence step (the chat upload pipeline's retain). No stateless, no-input-persistence
route existed that takes bytes directly off the wire. **Extension**: the upload endpoint's
retained-cache dependency makes it structurally the wrong tool for Browse (which has bytes in
client memory only, never retained anywhere) — a distinct route was required, but it reuses the
EXISTING `IComposeService.ProjectDocument` reader (task 010) and the EXISTING `MapProjectionResponse`
mapping helper; only the route + two DTOs are new. **Cost-of-doing-nothing**: without it,
Browse-local `.docx` keeps falling back to the lossy client `mammoth` reader, breaking F-2 (single
auditable reader) and legal read-fidelity (wrong numbering, dropped glyphs, lost alignment) on the
one entry path that had zero server involvement. All new server surface stays inside
`Services/Compose/`-adjacent `Api/ComposeEndpoints.cs` (one new route + one new private handler +
two new DTOs on the existing static endpoint class) — no new DI registration, no new package,
`Services/Compose/` itself untouched (no code changes there at all; `ProjectDocument` already
existed).
