# Task 041 — Mechanical Baseline: Per-Surface Activation Status

> **Task**: 041 — analyzers-as-errors + strict lint mechanical baseline (per-surface activation)
> **Date**: 2026-08-14
> **Constraint**: PER-SURFACE only (design §4A) — never enable a gate repo-wide while another surface
> is still dirty. Each surface flips its own gate as its last step.

## C# surfaces — FULLY ACTIVATED ✅

**Finding**: the C# mechanical-baseline infrastructure already existed and was already ON for every C#
project EXCEPT the BFF. Root [`Directory.Build.props`](../../../Directory.Build.props) sets
`TreatWarningsAsErrors=true` + `Nullable=enable` + a curated `WarningsNotAsErrors` allowlist
(CS0109/CS0618/CS1998/CS8601/CS8604, each documented with removal criteria per
`docs/assessments/bff-warning-suppression-analysis-2026-06-01.md`). The BFF csproj was the **lone
opt-out** (`TreatWarningsAsErrors=false`).

**Task 041 flipped the BFF surface ON** — the last C# opt-out. The C# mechanical baseline is now active
repo-wide. To flip cleanly, the non-curated nullable warnings were **fixed, not suppressed**:

| Warning | Site | Fix (behavior-neutral) |
|---|---|---|
| CS8766 (return-type nullability) | `Infrastructure/Cache/NullObjects/NullConnectionMultiplexer.cs` | Explicit `public override string ToString()` (IConnectionMultiplexer declares non-null ToString; inherited object.ToString is `string?`) |
| CS8625 (null → non-nullable) | `Services/Ai/AnalysisActionService.cs:187` (`expandClause: null`) | Made `BuildODataQuery`'s `expandClause` param `string?` (body already guards `!IsNullOrEmpty`) |
| CS8619 (Task<object> vs Task<object?>) | `Services/Ai/Workflows/SendUploadNotificationWorkflow.cs:89` | `Task.FromResult<object?>(result)` (explicit type arg) |
| CS8602 ×2 (deref possibly null) | `Services/Compose/ComposeBaselineParaIdStamper.cs:172,284` | `doc.MainDocumentPart!.Document!.Save()` (Document is `Document?`) |

**NU1510 exemption** (documented in the BFF csproj): with WarningsAsErrors on, the NuGet pruning hint
NU1510 on two deliberate version pins (Caching/Hosting.Abstractions 10.0.3, pinned for the
Microsoft.Extensions.AI 10.3.0 chain) promoted to errors. NU1510 is a NuGet restore-hygiene warning, NOT
a Roslyn analyzer warning — outside the analyzers-as-errors scope. Scoped out via `<NoWarn>NU1510` with a
justification comment (a scoped NuGet exemption, not a code-warning suppression). Removing the pins to
satisfy the pruner would drop the documented version resolution.

**Curated exemptions retained** (root policy, unchanged): CS0618 ×14 = obsolete
`DemoProvisioningOptions.Environments/DefaultEnvironment` usage (DemoExpirationService multi-env
migration — design NG4, deferred; the obsolete-marker's planned-removal contract, MF-7). NOT
blanket-suppressed — scoped to the specific codes with documented removal criteria.

**Verification**: `dotnet build -c Release --no-incremental` → **0 errors** (15 curated warnings remain,
non-blocking by policy). Test project compiles clean. Full BFF suite green (unchanged behavior).

## TypeScript surfaces — per-surface follow-on (tracked, NOT big-banged)

The TS mechanical baseline (`--max-warnings 0` + `no-console` + `tsc --noEmit`) is per-surface by design
(§4A). Archived-R3 items #5 (ESLint ~181) and #6 (console.log ~117) are distributed across ~15
`src/client/pcf/*` + `src/solutions/*` surfaces. Per the per-surface constraint, each surface flips its own
ESLint gate as it is cleaned — this is **ongoing incremental program work**, not a single-task big-bang
(activating a shared TS gate repo-wide while surfaces are dirty is explicitly forbidden by §4A + the
escalation trigger). No repo-wide TS gate was enabled. Each PCF/solution surface activates its own gate as
its warning count reaches zero; this is tracked as standing program follow-on (not a blocker for the C#
activation above).

## Net result

- **C# side**: mechanical baseline ACTIVE repo-wide (BFF, the last opt-out, now flipped + clean).
- **TS side**: per-surface activation is incremental by design; no big-bang; tracked follow-on.
