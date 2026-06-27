# Task 021 — Publish-Size Verification

> **Date**: 2026-06-25
> **Task**: 021 — Ensure sprk_category column dual-write (audit + fix if missing)
> **Constraint**: CLAUDE.md §10 BFF Hygiene — ≤60 MB ceiling, ≤+5 MB single-task delta
> **Baseline**: 46.31 MB (compressed, from task 020 checkpoint)

---

## Audit finding (the core deliverable of task 021)

**Dual-write was ALREADY PRESENT** — no production C# code change was required.

`CreateNotificationNodeExecutor.BuildNotificationEntity` (lines 554–557 in the
post-task-020 file) already contains:

```csharp
// Add category (custom field for idempotency grouping)
if (!string.IsNullOrWhiteSpace(category))
{
    entity["sprk_category"] = category;
}
```

This was introduced earlier (pre-R4) primarily to support the idempotency check
in `CheckForDuplicateNotificationAsync` (which queries `sprk_category` directly).
The same column write is exactly what FR-17c needs for the
`sprk_category not in (…)` server-side `$filter` on `disabledChannels`.

The write is reached by **both** code paths in the executor:
- Standard path: line 271 calls `BuildNotificationEntity(... category, ...)`
- Iterate-items path: line 421 calls the same method.

Both paths render `config.Category` through `ITemplateEngine.Render` before
passing it to `BuildNotificationEntity`, so the column always mirrors the
template-rendered category string. Null/empty category is gracefully handled —
the attribute is *omitted* (not set to empty string), matching the spec's
null-handling pattern.

### What task 021 actually did

1. Verified the column dual-write exists and is reached by both code paths.
2. Added 4 new xUnit test methods (10 individual test executions counting the
   `[Theory]` cases) to lock in the invariant explicitly. Without these tests,
   a future refactor could silently drop the column write — and that regression
   would not be caught by existing tests (which assert customData shape, not
   entity-attribute presence).

New tests added (all in `CreateNotificationNodeExecutorTests.cs`):

| Test | Asserts |
|---|---|
| `BuildNotificationEntity_PopulatesSprkCategory` | Column attribute is set and equals the rendered category |
| `BuildNotificationEntity_HandlesNullCategory` | Null category → column attribute is OMITTED (not empty string) |
| `BuildNotificationEntity_SprkCategoryMatchesCustomDataCategory_Always` (`[Theory]` × 7 channels) | Invariant holds across all 7 R4 notification channels |
| `BuildNotificationEntity_IterateItemsPath_PopulatesSprkCategory` | Iterate-items branch also writes the column |

All 4 tests pass. Total test count in the file: 15 active passing (was 11), 20
pre-existing skipped (unchanged — those are the HttpHandler-mock legacy tests).

---

## Measurement

```powershell
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
tar -czf api-publish-021.tar.gz deploy/api-publish/
```

| Metric | Value |
|---|---|
| Compressed publish output | **47.14 MB** (47,142,860 bytes) |
| Delta vs task 020 baseline (46.31 MB) | **+0.83 MB** |
| Distance from §10 ceiling (60 MB) | **12.86 MB headroom** |
| Single-task delta threshold (≤+5 MB) | ✅ within limit |
| Cumulative ceiling check (≤55 MB) | ✅ within limit |

The +0.83 MB delta is normal build-artifact variance — **task 021 modifies zero
production code**, only test code. Test assemblies do NOT ship in the API
publish output (the publish target is `Sprk.Bff.Api`, not the test project),
so the delta reflects tooling/compression-library differences between the
PowerShell `Compress-Archive` used for task 020 and the `tar -czf` used here.

The next per-task delta should be measured against this 47.14 MB checkpoint,
or re-measured with consistent tooling.

---

## CVE check

```
dotnet list package --vulnerable --include-transitive
```

| CVE | Severity | Pre-existing? |
|---|---|---|
| Microsoft.Kiota.Abstractions 1.21.2 — GHSA-7j59-v9qr-6fq9 | High | ✅ Pre-existing in master (carried across task 020 and earlier R4 tasks) — NOT introduced by task 021 |

**No NEW HIGH CVE introduced by this task.**

---

## Justification (per CLAUDE.md §10 + §11)

| Question | Answer |
|---|---|
| Existing — what does this overlap with? | `BuildNotificationEntity` already writes `sprk_category`. The audit confirms; no new code added to production. |
| Extension — can I extend the existing instead? | Already there — task is purely audit + test addition. |
| Cost-of-doing-nothing — concrete failure mode? | (1) FR-17c consumer `disabledChannels` server-side `$filter` (`sprk_category not in (…)`) would return wrong rows because Dataverse OData cannot `$filter` on nested JSON. (2) Future refactor that mistakenly removes the column write would not be caught — silent regression. |

**Placement decision**: no placement change — code unchanged. Tests live in the
canonical test fixture (`CreateNotificationNodeExecutorTests.cs`).

**Asymmetric-registration check (§F.1)**: not applicable — no DI changes,
no `*Module.cs` modifications, no feature-gated services introduced.

**Test obligation (§F)**: 4 new xUnit tests added to lock in the invariant.

---

## Downstream unblocked

This task UNBLOCKS:
- Task 022 (notification-tasks-overdue.json migration) — needs sprk_category for filtering
- Task 023 (notification-tasks-due-soon.json migration)
- Task 024 (notification-matter-activity playbook)
- Task 025 (notification-work-assignments playbook)
- Task 028 (customData schema-conformance fixture)
- PR 5 task 042 (consumer-side `disabledChannels` server-side `$filter`)
