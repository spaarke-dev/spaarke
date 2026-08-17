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

## FR-11 rider (deferred from task 073) — SOLVED via ADR §6.5 **Path A** (owner directive: r2 is closed)

Task 073 shipped facade-level cause discrimination (`ParseWithDiagnosticsAsync` on the concrete
`ComposePdfIntakeSource`, returning `PdfIntakeParseResult{FailureCause, FailureMessage}`) and deferred the
**end-to-end surfacing** to 050/051. On implementing 050 the surfacing appeared boundary-blocked
(consuming the cause server-side needed either widening the **then-r2-sole-owned** `IComposePdfIntakeSource`
interface OR a concrete downcast = ADR-013 breach). I surfaced this per §6.5.

**Owner directive**: `spaarke-ai-architecture-redesign-r2` **is closed** → the sole-owner coordination gate
is gone and **R7 now owns the facade**. That makes **Path A (widen the facade cleanly)** correct — no
breach, no downcast. Implemented end-to-end:

- **Moved `PdfIntakeFailureCause` + `PdfIntakeParseResult`** from `Services/Ai/ComposePdfIntakeSource.cs`
  into a new `Services/Ai/PublicContracts/PdfIntakeParseResult.cs` — the facade contract is now
  self-contained; `Services/Compose` consumes the cause through the **facade namespace only** (ADR-013 clean).
- **Added `ParseWithDiagnosticsAsync` to `IComposePdfIntakeSource`** (the facade interface). Concrete already
  had it (073) → now an interface member. `NullComposePdfIntakeSource` implements it too: gate-off returns
  `Failure(Unknown, "<AI document parsing is disabled …>")` — the ADR-032 gate-off universe stays distinct
  via the MESSAGE, keeping the enum narrow per 073's design.
- **Wired `ComposeService.ProjectPdfToDocxAsync`** to call `ParseWithDiagnosticsAsync` (via the facade — no
  downcast) and throw the **cause-specific** `FailureMessage`. Status mapping: `unavailable = FailureCause
  != Corrupt` → **Corrupt → 422** (not retryable — the document is the problem), everything else
  (circuit-open / timeout / unknown / disabled) → **503** (retryable / service-side). Mirrors the load
  endpoint's own 503-vs-422 split; applies to BOTH the Load and the new mount doors.
- **Test ripple (task-013 pattern)**: seam mocks migrated `ParseAsync` → `ParseWithDiagnosticsAsync`
  (`ComposePdfIntakeRoundTripSeamTests` ×3, `ComposeMountPdfProjectionSeamTests` ×2 + Verify). Added one
  FR-11 end-to-end seam test: a Corrupt cause → 422 with the cause-specific message (not the collapsed text).
  073's 17 direct unit tests on the concrete stay green (types resolve from PublicContracts via the
  existing `using`).

**Result**: FR-11 is DONE end-to-end — a PDF that fails intake now shows the user the SPECIFIC reason
(circuit-breaker-open / timeout / corrupt / disabled) with the correct retryable-vs-not status, on every
Compose PDF door. No ADR-013 breach; the facade is cleanly R7-owned post-r2-close.

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

## Deviations / notes

- **NFR-04 contract change**: `ProjectForMount` is now async. Documented in code (`IComposeService` `<remarks>`, `ComposeService` inline, both endpoint comments) and here. Pre-approved by spec ADR Tensions row 2 (path A).
- **FR-11 solved (Path A)** per owner directive (r2 closed) — see the FR-11 section above. Facade widened cleanly; no ADR-013 breach. Committed separately from the async fork.
- **Project scope note**: FR-11 was authored as a 073 rider originally homed at 050/051; it is now fully landed in the 050 work (server end-to-end). 051 (client) can render the cause-specific message the server already sends, but does not need to — the server surface is complete.

## Task 051 (next) — client half

051 (client PDF intake-door gates + env verify + parity) consumes `sourceFormat` (now on the mount
responses), admits `.pdf` in the Browse `accept` filter + the intake-door gate, and runs the parity UAT.
FR-11 is already surfaced server-side; 051 may optionally render the cause-specific message in the PDF
error banner (the server sends it), but the end-to-end contract is complete without further server work.
