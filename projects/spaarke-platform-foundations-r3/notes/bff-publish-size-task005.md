# BFF Publish-Size Measurement — R3 Task 005

> **Task**: 005 — Unrendered-template runtime warning in `PlaybookOrchestrationService.cs`
> **Date**: 2026-06-21
> **Per**: CLAUDE.md §10 BFF Hygiene + `.claude/constraints/azure-deployment.md` NFR-01 verification rule

---

## Inputs

| Field | Value |
|---|---|
| Build | `dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` → **succeeded** (0 errors; 16 pre-existing warnings, none from task 005) |
| Publish command | `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/ --runtime linux-x64` |
| Configuration | Release / linux-x64 / framework-dependent |
| Compression method | PowerShell `Compress-Archive -CompressionLevel Optimal` (matches deploy ZIP + baseline convention) |

---

## Measurements

| Metric | Value |
|---|---|
| Compressed publish ZIP | **46.16 MB** (48,397,039 bytes) |
| Publish entry count | 264 files |
| Baseline (task 013, 2026-06-21) | 46.14 MB |
| **Delta vs baseline** | **+0.02 MB (~16 KB)** |
| NFR-01 per-task ceiling | +1 MB |
| Hard ceiling | 60 MB |
| Status | ✅ **Within budget** (~2% of per-task ceiling) |

---

## Composition of the delta

Task 005 modified two files in `Sprk.Bff.Api`:

1. **`Services/Ai/IPlaybookOrchestrationService.cs`** — added one enum member (`PlaybookEventType.UnrenderedTemplateDetected`) + one static factory method (`PlaybookStreamEvent.UnrenderedTemplateDetected(...)`) + XML doc.
2. **`Services/Ai/PlaybookOrchestrationService.cs`** — added one private method `ScanForUnrenderedTemplatesAsync(...)` (~85 lines incl. XML doc) + a single `await` call site at the post-`NodeCompleted` hook.

Zero NuGet package additions. Zero new DI registrations. Zero new endpoints. Pure in-process code addition — the ~16 KB delta is consistent with the added IL/XML doc footprint.

---

## CVE check

`dotnet list package --vulnerable --include-transitive` → **1 HIGH-severity CVE present** (`Microsoft.Kiota.Abstractions 1.21.2` / GHSA-7j59-v9qr-6fq9). This is **pre-existing** — task 005 added zero packages. CVE remediation is tracked separately and is not in scope for this task.

---

## Placement justification (CLAUDE.md §10 imperative)

Task 005 EXTENDS existing PlaybookOrchestrationService per ADR-013 (refined 2026-05-20) — does NOT create a parallel orchestrator or new service. The scan/emit logic is colocated with the post-node-execution hook because:

| Decision criterion | Answer | Why |
|---|---|---|
| Latency budget against BFF state? | YES | Per-node hook runs inline in playbook execution; observation has no separable workload |
| Writes to BFF session/audit/safety state? | YES | Emits to in-process `Channel<PlaybookStreamEvent>` consumed by the SSE response handler |
| Retroactive annotation of streaming response? | YES | The warning event interleaves with the playbook stream |
| Event-driven with no synchronous user wait? | NO | Synchronous to the playbook run lifecycle |

All four → BFF. No alternative placement evaluated.

---

## Test coverage

3 new tests in `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PlaybookOrchestrationServiceTests.cs` covering:

- `ExecuteAsync_NodeOutputContainsUnrenderedTemplate_EmitsWarningStreamEvent` — happy-path detection (TextContent leak)
- `ExecuteAsync_NodeOutputClean_NoUnrenderedTemplateEventEmitted` — false-positive guard
- `ExecuteAsync_NodeOutputMultipleFieldsUnrendered_EmitsSingleWarningWithAllFields` — multi-field coalescing (single event per node; warning log lists all offending field names)

All 35 PlaybookOrchestrationService tests pass.
