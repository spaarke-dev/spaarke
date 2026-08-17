# Task 050 — Async `ProjectForMount` PDF fork (server, FR-06) — IMPLEMENTED

> Phase 5 (PDF Import Parity / UC-7) · **opus**@high · FULL rigor · 2026-08-16 · BFF contract change
> **ADR Tensions row 2 (path A)**: `ProjectForMount` sync→async is the documented, project-scoped exception.

## What shipped

Gave `ComposeService.ProjectForMount` the SAME `IsPdfSource → ProjectPdfToDocxAsync` fork `LoadAsync`
already has (mirrors `ComposeService.cs:502`), making it **async**. A PDF opened via the **Browse-project**
(`POST /api/compose/project`) or **Assistant-upload** (`POST /api/compose/upload`) doors now becomes an
**editable** Compose document — synthesized docx, `sourceFormat:"pdf"`, counted `pdf-intake-*`
honest-lossiness warnings — exactly as it does via Load. The **docx path stays synchronous-fast**: the
single Azure Document-Intelligence `await` is reached ONLY on the PDF branch; a native docx mount does
zero added I/O and never touches the intake source.

### Files changed (all `Services/Compose/` + `Api/` — BFF §10 in-place)

- **`Services/Compose/IComposeService.cs`**:
  - `ProjectForMount` signature → `Task<ComposeMountProjection> ProjectForMount(ReadOnlyMemory<byte> content, string? fileName = null, CancellationToken = default)` (async + additive optional `fileName` for source detection/diagnostics). `<remarks>` documents the ADR-007/013 contract change (NFR-04, path A).
  - `ComposeMountProjection` record → **added `SourceFormat` field** (mirrors `LoadComposeDocumentResult.SourceFormat`).
- **`Services/Compose/ComposeService.cs`** `ProjectForMount`:
  - Bytes-first PDF detection (`IsPdfSource(fileName, content.Span)`) → forks to the existing
    `ProjectPdfToDocxAsync` (same primitive Load uses — the escalation trigger did NOT fire).
  - Folds `pdfIntakeWarnings` into `ContentModelWarnings` **unconditionally** (mirrors Load LOW-7 @563), sets `SourceFormat="pdf"`.
- **`Api/ComposeEndpoints.cs`**:
  - `Upload` + `Project` handlers → `await` the now-async method, pass their fileName (`fileName` sidecar / `body.FileName`), surface `SourceFormat` on `ComposeUploadResponse` + `ComposeProjectResponse` (both records got an additive `sourceFormat` field).
  - **New `catch (ComposePdfIntakeException)` in BOTH mount handlers** — the fork adds a throw site the mount doors didn't have; maps it to the SAME honest 503 (unavailable, retryable) / 422 (not projectable) ProblemDetails the Load door uses, never a generic 500.
  - **Correctness fix** — `Project` door's `Content` echo now fires on `mount.Minted || mount.SourceFormat is not null`. A PDF's synthesized docx is pre-minted by the renderer, so `MintAndPersist` is a no-op → `Minted=false`; without the `SourceFormat` clause a PDF browse would return a docx projection but NO docx bytes, breaking the client's save-as-docx flow (051). `Upload` already echoes `Content` unconditionally.
- **`tests/integration/seam/Compose/ComposeMountPdfProjectionSeamTests.cs`** (NEW, seam KEEP path): 3 through-the-wire tests over the real `/project` door + real projector/renderer, `IComposePdfIntakeSource` mocked at the PublicContracts boundary only.

## FR-11 rider (deferred from task 073) — ADR §6.5 **Path C (comply)**, NOT wired server-side

Task 073 shipped facade-level cause discrimination (`ParseWithDiagnosticsAsync` on the concrete
`ComposePdfIntakeSource`, returning `PdfIntakeParseResult{FailureCause, FailureMessage}`) and deferred the
**end-to-end surfacing** to 050/051. On implementing 050 the surfacing hit a **hard boundary**:

- `ComposeService._pdfIntakeSource` is typed `PublicContracts.IComposePdfIntakeSource?` — the **r2-sole-owned** facade. `ParseWithDiagnosticsAsync` exists ONLY on the concrete class, NOT on that interface.
- Consuming the discriminated cause server-side therefore requires EITHER **(a)** widening the r2-owned `IComposePdfIntakeSource` (coordination-gated — 073's constraint was "consume, don't modify r2's surface") OR **(b)** a concrete downcast in `ComposeService` (an **ADR-013 facade-discipline breach** — and code-quality-r3 just consolidated such downcasts away).

**Decision — Path C**: neither boundary is breached. The current collapsed message (`ProjectPdfToDocxAsync`
throws `"PDF intake failed: … The file may be corrupt or the document-parsing service is unavailable."`)
is **already honest and safe** — it is byte-identical to the `PdfIntakeFailureCause.Unknown` wording. The
FR-11 improvement is a *cause-specific UX message refinement* (circuit-open vs timeout vs corrupt), **not a
correctness gap**. It is not worth an ADR-013 breach or an unilateral edit to r2's surface. FR-11's
facade-level discrimination is DONE (073, directly unit-tested); end-to-end surfacing is consciously left
UN-wired pending a cross-worktree decision (see "Open decision for the owner" below). **Not a silent drop.**

## Placement Justification (BFF §10)

All work stays in `Services/Compose/` + `Api/ComposeEndpoints.cs`; the PDF fork **reuses** R6's
`ProjectPdfToDocxAsync` / `ComposePdfModelProjector` / `ComposeDocumentRenderer` (no re-build, no new
subsystem/service/DI/package — cite `.claude/constraints/bff-extensions.md`). `SourceFormat` is an
additive field on the existing `ComposeMountProjection` record (mirrors `LoadComposeDocumentResult`), not a
new component → no §11 justification required (modify-only).

## Verification

- `dotnet build -c Release src/server/api/Sprk.Bff.Api/` — **0 errors** (7 pre-existing unrelated CS0618 warnings).
- New seam tests + adjacent PDF/mount seams: **13/13 pass**. Full Compose surface (`--filter ~Compose`): **1127 / 0**.
- **Publish size (net10 fw-dependent linux-x64, `Compress-Archive -Optimal`, same convention as the baseline)**: **44.9452 MB incl PDBs** — delta **−0.0148 MB** vs the 44.96 baseline (noise; source-only change, 215 files / 4 pdb unchanged). 15.05 MB headroom under the 60 MB ceiling; far below the +5 MB single-task escalation threshold.
- **CVE**: `dotnet list package --vulnerable --include-transitive` → "no vulnerable packages" (no package refs changed).
- **`/conflict-check`**: `ComposeService.cs` / `ComposeEndpoints.cs` have zero master-side or open-PR overlap (all open PRs `compose:[]`; the 1-behind master commit `749dd273e` touches only Dataverse). Soft note for wrap-up: a future merge must reconcile `DataverseWebApiService.cs` (task-013 `UpsertAsync` vs master `GetEntitySetNameAsync` — both additive).

## Gates (Step 9.5)

- **code-review: PASS** — 0 Critical / 0 Warnings. Async fork mirrors `LoadAsync`; new mount-door catches replicate the Load door's mapping; the `Content`-echo fix is a real correctness improvement. No AI smells; seam test is ADR-038 KEEP-path, no banned patterns.
- **adr-check: PASS** — 0 violations. ADR-007/013 sync→async = pre-approved spec Path-A exception (documented in code); ADR-013 facade discipline HONORED (FR-11 downcast NOT taken — Path C); ADR-032 gate unchanged; §10 clean (publish/CVE/placement); §11 modify-only.

## Deviations / open decision for the owner

- **NFR-04 contract change**: `ProjectForMount` is now async. Documented in code (`IComposeService` `<remarks>`, `ComposeService` inline, both endpoint comments) and here. Pre-approved by spec ADR Tensions row 2 (path A).
- 🔔 **FR-11 end-to-end surfacing** needs an owner decision: (A) file a small r2-coordinated addition to `IComposePdfIntakeSource` (widen the facade to carry the cause), or (C — current) accept the already-honest collapsed message and formally close FR-11 as "facade discrimination shipped in 073; end-to-end surfacing not worth an ADR-013/r2 breach". Recommend **C**. To be filed via `/defer` (notes/defer-issues.md + GitHub Issue atomically) once the owner confirms the path.

## Task 051 (next) — client half

051 (client PDF intake-door gates + env verify + parity) consumes `sourceFormat` (now on the mount
responses), admits `.pdf` in the Browse `accept` filter + the intake-door gate, and runs the parity UAT.
The FR-11 surfacing does NOT block 051 (server can't discriminate without the boundary change above).
