# Task 073 — PDF-intake cause discrimination (FR-11 / LOW-10) — notes

## What changed

`src/server/api/Sprk.Bff.Api/Services/Ai/ComposePdfIntakeSource.cs` — replaced the collapsed
single-message null boundary in `ParseAsync` with a discriminated result:

- New `PdfIntakeFailureCause` enum: `CircuitOpen`, `Timeout`, `Corrupt`, `Unknown`.
- New `PdfIntakeParseResult` record: `Layout` on success; `FailureCause` + `FailureMessage` on failure.
- New `ParseWithDiagnosticsAsync(...)` method on the concrete `ComposePdfIntakeSource` class: does the
  same parse attempt as before, but on failure classifies the caught exception's message text against
  known markers sourced from `TextExtractorService.ExtractLayoutAsync`'s own distinct `Failed()`
  wordings (circuit-breaker-open / timeout / bad-format) — read-only text matching, no fork of
  `Services/Ai` internals — and returns a cause-specific message instead of one generic log line.
- `ParseAsync` (the `IComposePdfIntakeSource` interface member, defined in `PublicContracts/` and owned
  by `spaarke-ai-architecture-redesign-r2`) is **unchanged in signature and behavior**: it now delegates
  to `ParseWithDiagnosticsAsync` and returns `result.Layout`, so it still never throws (beyond
  caller-cancellation) and still collapses any failure to `null` for existing callers. No PublicContracts
  file was edited; the new enum/record live in `Services/Ai` (same file), not PublicContracts — they did
  not need to (nothing about them required living behind the facade boundary).

## Scope decision — why `ComposeService.cs` was not wired to the new discriminated result

Task 073's bookkeeping instructions and POML `<outputs>` scope this task to
`ComposePdfIntakeSource.cs` + its test file ONLY (no edits to `NullComposePdfIntakeSource.cs`,
the `IComposePdfIntakeSource` interface file, or `Services/Compose/ComposeService.cs`). Consuming the
new `ParseWithDiagnosticsAsync`/`PdfIntakeParseResult` from `ComposeService.ProjectPdfToDocxAsync` (the
place that currently throws the single collapsed `ComposePdfIntakeException` message at
`ComposeService.cs:838-846`) would require touching that file, which is out of this task's stated file
boundary. The discriminated result is therefore surfaced at the facade layer (this file) — a real,
directly-testable typed result any future caller can consume — but the end-to-end HTTP-response message
a Compose user sees today is unchanged until a follow-on task wires `ComposeService.cs` to branch on
`FailureCause`. This is a deliberate, documented scope precision, not a silent gap: flagging it here per
CLAUDE.md's decision-documentation expectation.

Acceptance criteria are met as literally scoped by the POML:
- Cause is discriminated (circuit-open / timeout / corrupt) with a cause-specific message — verified by
  unit tests directly on `ParseWithDiagnosticsAsync`.
- The ADR-032 gate-off case (`NullComposePdfIntakeSource`, untouched, separate class, separate "AI
  document parsing is disabled" log line) stays distinct — trivially preserved since it's a different
  class this task never edits, and the new `PdfIntakeFailureCause` enum has no "gate-off"/"unavailable"
  member, so the two failure universes stay conceptually and code-wise disjoint.
- Only `PublicContracts` types are consumed (`DocumentLayout`, `IComposePdfIntakeSource`); no
  `Services/Ai` internals were forked — the classifier reads exception message TEXT that
  `DocumentIntelligenceService`/`TextExtractorService` already produce, it does not duplicate their logic
  or introduce a new dependency on their internal types.

## Placement Justification

BFF change stays in-place in `Sprk.Bff.Api/Services/Ai/` — this is a same-file behavior refinement of an
existing facade (`ComposePdfIntakeSource`, task 040/spaarkeai-compose-r6), not a new component; per
`.claude/constraints/bff-extensions.md`, no new endpoint/service/DI registration/package was introduced
(existing `IComposePdfIntakeSource` DI registration in `AnalysisServicesModule.cs` is untouched), so no
new placement decision is required beyond confirming the change belongs where the facade already lives.

## Verification

- `dotnet build -c Release src/server/api/Sprk.Bff.Api/` — Build succeeded, 0 errors (7 pre-existing
  unrelated `CS0618` obsolete-API warnings in `RegistrationEndpoints.cs`/`DemoExpirationService.cs`).
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj --filter "FullyQualifiedName~ComposePdfIntakeSourceTests"`
  — **17/17 passed**, 0 failed.
- Publish-size (net10 framework-dependent linux-x64, `dotnet publish -c Release
  src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, PowerShell `Compress-Archive
  -CompressionLevel Optimal` — same convention as the `dotnet-10-upgrade-r1` task 031 baseline):
  - **Incl. PDBs: 44.9617 MB** (baseline 44.96 MB → delta **+0.0017 MB**, noise-level; well under the
    ≥+5 MB single-task escalation threshold and the ≤60 MB hard ceiling — 15.04 MB headroom retained).
  - Excl. PDBs: 44.0634 MB (baseline 44.05 MB → delta +0.0134 MB, same noise-level).
  - Entry count unchanged: 215 files (4 `.pdb`) — expected, since this change adds C# source compiled
    into the existing `Sprk.Bff.Api.dll`, no new package/asset.
- `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api.csproj` — **"has no
  vulnerable packages given the current sources"** — no new HIGH (or any) CVE; expected, since no
  package references changed.

## Deviations

None. No escalation trigger fired (the needed cause distinction — circuit-open / timeout / corrupt — was
already recoverable from `PublicContracts`-adjacent, non-forked exception text produced by the existing
stack; no internals needed to be forked).

---

## Main-session integration decision (2026-08-15 — cherry-picked → work/spaarkeai-compose-r7)

Cherry-picked verbatim (files disjoint from task 010). BFF `dotnet build -c Release` clean in the
integrated tree (0 errors). Accepted the scoped delivery: discriminated result + classifier + 17 tests land now.

**FR-11 end-to-end surfacing → deferred to tasks 050/051 (PDF parity), by decision:**
- `ParseWithDiagnosticsAsync` is ADDITIVE on the concrete `ComposePdfIntakeSource`; `ParseAsync` (the
  `IComposePdfIntakeSource` PublicContracts member) delegates to it and still returns null on failure, so
  `ComposeService.ProjectPdfToDocxAsync` currently still collapses the cause at the user boundary.
- Surfacing it needs EITHER modifying `IComposePdfIntakeSource` in `Services/Ai/PublicContracts/` (r2
  sole-owned → coordination-gated; 073 constraint = consume, don't modify r2's surface) OR a concrete
  downcast in `ComposeService` (which `code-quality-and-assurance-r3` just consolidated away).
- **Chosen**: wire the cause-specific message into the PDF error UX during **tasks 050/051**, which already
  rework the PDF mount/error path in the Compose spine (main-session). Lands the surfacing where the PDF
  error surface is built; no premature spine edit, no r2-surface breach. Tracked as a 050/051 acceptance
  rider — NOT a silent drop.
