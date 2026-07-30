# Task 022 — Archive-durability decision + impl (AIPL-054 stub)

> Decision: **Option B — durable Dataverse archive** · FULL rigor · opus/high · deps 021 · completed 2026-07-28
> spec FR-06 / PLAN §2.3 / AIPL-054. No escalation fired.

## Decision (recorded)

**Option B — durable Dataverse archive.** `ChatDataverseRepository.ArchiveSessionAsync` now durably flips
`sprk_isarchived = true` on the prior session's `sprk_aichatsummary` record. The prior logging-only stub
(the AIPL-054 gap) is removed.

### Why B over A

- **021 wired the archive call for the fork's prior session** (`AnalysisEndpoints.ForkAnalysis` → best-effort
  `IChatDataverseRepository.ArchiveSessionAsync`). Leaving it a no-op would mean the fork "archives" a prior
  session that is never marked archived — history/UX reads of `sprk_isarchived` (e.g.
  `GetSessionsByAnalysisAsync`, which already projects `IsArchived`) would silently show it as active. That is a
  concrete, nameable failure, not hypothetical.
- **The "missing cached summary GUID" gap that deferred B is closeable in-place without a broad refactor.**
  `sprk_sessionid` is already a queryable key on the `sprk_aichatsummary` row (written by `CreateSessionAsync`).
  A single tenant-scoped `QueryExpression` resolves the record GUID; a sparse `UpdateAsync` flips the flag. No
  `ChatSessionManager` summary-GUID caching, no new persistence infrastructure, no schema change — so the
  escalation trigger (scope balloon into cross-service caching) did NOT fire.
- **`sprk_isarchived` is a real BIT column** (verified via Dataverse MCP; already written `false` at create in
  `CreateSessionAsync`). Flipping it durably is the natural completion of an existing field, not new surface.

Option A (accept Cosmos/Redis-authoritative + document `sprk_isarchived` as advisory) was the lower-risk default
per the POML notes, but it would leave the flag permanently lying about durability and would make 021's archive
call cosmetic. Since B is bounded and B removes an actual correctness gap, B is chosen.

## What "archived" means afterward (unambiguous)

- **Durable marker in Dataverse**: `sprk_aichatsummary.sprk_isarchived = true` — survives Redis eviction and
  Cosmos, queryable by history/UX. This is a MARKER only.
- **Transcript store-of-record is unchanged**: Cosmos (ADR-040 Path A). Archive NEVER deletes the Cosmos
  transcript — the archived prior session stays **retrievable / switchable-back**, no transcript data loss. No
  second transcript store is created.
- **Redis**: not force-evicted by this method (ages out on its 24 h TTL); unchanged from 021.

### Data-durability implications

- Archive is idempotent (re-flipping `true` is a no-op-equivalent update).
- **Non-fatal when there is no cold-tier anchor row**: `CreateSessionAsync` tolerates a Dataverse write failure
  and continues Redis-only, so a summary row may not exist. In that case archive logs a warning and returns —
  no throw, no blind update. The Cosmos transcript remains the store-of-record and retrievable. This mirrors the
  tolerant create path and matches 021's "archive is best-effort/non-fatal" contract.
- Tenant-scoped query (ADR-014/ADR-028): a cross-tenant `sessionId` guess cannot flip another tenant's flag.

## Implementation

`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatDataverseRepository.cs` — `ArchiveSessionAsync` rewritten
(was lines 168–183, logging-only). Now: tenant-scoped `QueryExpression` on
`sprk_sessionid` + `sprk_tenantid` (TopCount 1, NoLock) → resolve `Entity.Id` → sparse
`UpdateAsync("sprk_aichatsummary", id, { sprk_isarchived = true })`. Query/update seams already on
`IGenericEntityService`; pattern mirrors the sibling `GetSessionsByAnalysisAsync`. **Zero edits to
`Services/Ai/` orchestration** — this is the persistence repo, not orchestration. Endpoint contract unchanged
(021's fork handler already calls this seam).

## Placement Justification (CLAUDE.md §10 / bff-extensions.md)

- **Modification of an existing repository method** — not a new service/endpoint/DI registration/package/background
  job. `ChatDataverseRepository` is already DI-registered and already the archive seam. No new BFF surface.
- **ADR-013 facade discipline**: the repo injects `IGenericEntityService` only (generic Dataverse CRUD) — no
  AI-internal orchestration types; `Services/Ai/` (sole-owned by `spaarke-ai-architecture-redesign-r2`)
  untouched (this file lives under `Services/Ai/Chat/` but is persistence, not orchestration; only the archive
  method body changed).
- **ADR-032 symmetry**: no feature gate involved; `IChatDataverseRepository` is registered unconditionally.

## Publish-size (§10 / NFR-01 / ADR-029)

`dotnet publish -c Release … -o deploy/api-publish/` → **47.51 MB compressed** (zip, Optimal). Baseline (021) was
47.51 MB → **delta 0.00 MB** (no package added; a repository method body changed). Well under the 60 MB HARD ceiling.

## CVE (§10)

`dotnet list package --vulnerable --include-transitive`: only pre-existing HIGH is
`System.Security.Cryptography.Xml 8.0.3` (known/unrelated, transitive). **No NEW HIGH CVE** — task 022 added no
packages.

## Quality gates (Step 9.5)

- **Build**: `dotnet build src/server/api/Sprk.Bff.Api/` green (0 errors; warnings all pre-existing, none from
  changed files).
- **Tests**: `ChatDataverseRepositoryTests` 10/10 pass (7 existing + 3 new): durable flip + tenant-scoped query
  shape; marker-only (never `DeleteAsync`, exactly one `sprk_isarchived=true` update); no-summary-row non-fatal
  (no throw, no update). Added to the existing co-located unit file that already mocks `IGenericEntityService`
  for this class (honest module-boundary double, not a banned wiring antipattern).
- **code-review / adr-check**: see task-execute Step 9.5 output.
- **/conflict-check**: BFF hot-path touched; zero `Services/Ai/` orchestration edits; no open-PR overlap on
  `ChatDataverseRepository.cs`.
