# FR-11 — Full Test Suite on net10 (task 030)

> **Date**: 2026-08-12 · **Rigor**: FULL (TEST-MODIFYING) · **Status**: net10-caused failures FIXED + verified; **2 pre-existing clusters awaiting owner decision** (see §Escalation).
> **Headline**: the .NET 8→10 retarget introduced exactly **2 test-infrastructure regressions** (both fixed, clearing **521+** failures). The remaining red is **pre-existing on the branch baseline** (unrelated to net10) — and the branch is **44 commits behind master**, so some may already be resolved by the planned master-sync.

---

## Per-project results (net10, `dotnet test -c Release`)

| Project | Before | After my fixes | Notes |
|---|---|---|---|
| `tests/unit/Spaarke.Core.Tests` | 35/45 (10 fail) | **45/45 ✅** | Stale `DesktopUrlBuilder` tests fixed (pre-existing) |
| `tests/unit/Spaarke.Scheduling.Tests` | 47 pass/10 skip ✅ | ✅ | unchanged |
| `tests/unit/RecordSyncJob.IsolatedTests` | 12/12 ✅ | ✅ | unchanged |
| `tests/unit/Sprk.Bff.Api.Tests` | **522 fail** / 9826 pass | **0 fail / 10348 pass / 101 skip ✅** | test-host bump (−521) + eval-string fix (−1) |
| `tests/Spaarke.ArchTests` | restore ERROR (NU1510) | builds; **4 fail** (pre-existing ADR-007/010) | pin fixed; 4 arch-drift fails remain — see §Escalation |
| `tests/unit/Spaarke.Plugins.Tests` | restore ERROR (NU1015) | **dead orphan** | references a deleted project — see §Escalation |
| `tests/integration/*` (Spe, Sprk.Bff.Api.IntegrationTests) | (test-host bug) | test-host fixed; `[Trait("Category","Live")]`+env-gated | Live tests skip w/o infra; CI runs filtered-by-surface |

---

## net10-CAUSED failures — FIXED (in scope, verified)

### 1. `Microsoft.AspNetCore.Mvc.Testing` 8.0.23 → 10.0.1 (the big one: −521)
- **Symptom**: 1928× `InvalidOperationException: The PipeWriter 'ResponseBodyPipeWriter' does not implement PipeWriter.UnflushedBytes` in `System.Text.Json...SerializeAsync`, cascading to 200→500 / 400→500 across every WebApplicationFactory endpoint/contract test (522 failed).
- **Root cause**: net10's STJ async path requires `PipeWriter.UnflushedBytes`; the **8.0.23 TestHost's** in-memory response pipe writer doesn't implement it. Pure **test-harness** issue — production runs on real Kestrel (implements it). Task 005 bumped the TFM but missed this package alignment.
- **Fix**: bumped to `10.0.1` (= installed `Microsoft.AspNetCore.App` runtime) in all 3 test projects (`Sprk.Bff.Api.Tests`, `Sprk.Bff.Api.IntegrationTests`, `Spe.Integration.Tests`). Re-run: **522 → 1**.

### 2. `Spaarke.ArchTests` — `System.Net.Http 4.3.4` pin (restore blocker)
- **Symptom**: `error NU1510` (System.Net.Http will not be pruned) — promoted to error by root `TreatWarningsAsErrors=true` (ArchTests doesn't override it); ArchTests couldn't even restore.
- **Root cause**: net10 framework-superseded CVE-pin (H4/NU1510 pattern). CVE-2018-8292 fix is inbox on net10. Task 005 removed the sibling `System.Text.RegularExpressions` pin but missed this (ArchTests isn't in `Spaarke.sln`).
- **Fix**: removed the pin (mirrors task 005). ArchTests now restores/builds.

### 3. `AttachmentActionEvalTests` source-string (H2 R3 rename: −1)
- **Symptom**: eval test asserts `CommunicationEnrichmentService.cs` source contains `_createTaskAi.ExtractAsync`; found `createTaskAi.ExtractAsync`.
- **Root cause**: task 020 **H2 R3** resolved `ICommunicationCreateTaskAi` from a per-op scope (local `createTaskAi`) instead of a ctor field (`_createTaskAi`) to fix a captive dependency — **behavior-preserving** (verified by task 021). Master's source still has the field form (2×) → the test passes on master. The brittle source-string match broke on the branch's H2 refactor.
- **Fix**: updated the assertion field→local (`createTaskAi.ExtractAsync`) + comment. Intent (facade reuse) unchanged. Verified: 7/7 pass.

---

## PRE-EXISTING failures — NOT net10 (logged per ADR-038 "no exclusion without rationale")

### A. `Spaarke.Core.Tests.DesktopUrlBuilderTests` ×10 — FIXED (stale test)
- **Proven pre-existing**: `git show origin/master:` — master's production `DesktopUrlBuilder.FromMime` already emits the abbreviated `ms-{app}:{webUrl}` format (raw, no `ofe|u|`, no encoding) with a detailed XML doc explaining the intentional Security-Zone-bypass rationale; master's **test already expects the legacy `ofe|u|{encoded}` form**. Both files last changed in the SAME master commit `bb63d9818` (FileViewer Enhancements) — the production change shipped without updating the test → **red on master/net8 before the retarget**.
- **Disposition**: updated the 10 assertions to the documented abbreviated behavior ("Code wins; the test lagged"), + a class note so it isn't re-broken. NOT a net10 behavior change; NOT weakening a zero-behavior-change guard. Verified: 45/45.

### B. `Spaarke.ArchTests` ×4 — PRE-EXISTING arch drift (AWAITING DECISION — see §Escalation)
- `ADR-010: 1:1 interface mapping count increased from 76 to 153` (whole-codebase ceiling accumulated across many prior projects — retarget added ~0 interfaces).
- `ADR-010`: `AutoFileSettings` / `TrackingFooterSettings` have constructor dependencies (options-pattern rule).
- `ADR-010`/`ADR-007`: type list (Graph* types, ProblemDetails handlers).
- `ADR-007`: `FileAccessEndpoints` references `Microsoft.Graph` directly (should use `SpeFileStore` facade).
- **Not net10**: these check whole-codebase production structure the retarget never touched. **ADR-002** arch tests (plugin-size / thin-plugin — the acceptance-criterion-named ones) **PASS**; the 4 fails are ADR-007/010.

### C. `Spaarke.Plugins.Tests` — PRE-EXISTING dead orphan (AWAITING DECISION)
- **NU1015** (version-less PackageReferences; root `Directory.Packages.props` has `ManagePackageVersionsCentrally=false`) — fails identically on master (diff empty).
- References a **non-existent project**: `..\..\..\src\server\plugins\Spaarke.Plugins\Spaarke.Plugins.csproj` does not exist (the only plugin is `src/dataverse/plugins/.../Spaarke.Dataverse.CustomApiProxy.csproj`, net462). Not in `Spaarke.sln`. Cannot build regardless of net10.

---

## Escalation (owner decision — CLAUDE.md §6.5 / §6)

The net10 retarget's own test impact is **fully green** (unit + the acceptance-named ADR-002 arch tests + the FR-03-adjacent seam suite run under the fixed test-host). Two **pre-existing, non-net10** clusters block a literal "100% green" claim:

1. **ArchTests ADR-007/010 ×4** — options:
   - (a) **Document as pre-existing + defer to the planned master-sync** (recommended — branch is 44 behind; master may already resolve some; fixing production arch under a retarget is out of scope + risks behavior change);
   - (b) update the ADR-010 interface ceiling with documentation (the test itself invites this) — but that masks real drift;
   - (c) fix the production arch now (real refactor — not a retarget task).
2. **Plugins.Tests dead orphan** — recommend **flag for `/test-diet` deletion at wrap-up (task 090)** (it references a deleted project; cannot build on net8 or net10).

**Recommendation**: path (a) + document Plugins.Tests for deletion. Task 030 marks the net10 retarget's test impact green; the pre-existing clusters are tracked as follow-ups (or resolved by the master-sync in the sequencing plan), NOT force-fixed under the retarget.

## Files changed (this task)
- `tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj` (remove System.Net.Http pin)
- `tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj`, `tests/integration/Sprk.Bff.Api.IntegrationTests/*.csproj`, `tests/integration/Spe.Integration.Tests/*.csproj` (Mvc.Testing → 10.0.1)
- `tests/unit/Spaarke.Core.Tests/DesktopUrlBuilderTests.cs` (stale abbreviated-format fix)
- `tests/integration/contract/Eval/AttachmentActionEvalTests.cs` (H2 R3 field→local source-string)
