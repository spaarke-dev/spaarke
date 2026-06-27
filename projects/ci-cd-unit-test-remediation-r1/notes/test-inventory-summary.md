# Test Inventory Summary (from task CICD-020)

> **Generated**: 2026-06-26 from `notes/test-inventory.csv` (492 .cs files classified)
> **Status**: Transient Phase 1 work artifact (per spec FR-B05). Path conventions canonical at runtime, NOT this CSV.

## Totals

| Metric | Count | % |
|---|---|---|
| **Total .cs test files** | 492 | 100% |
| **KEEP** | 481 | 97.8% |
| **DELETE** | 11 | 2.2% |

**Compared to spec's working estimate**: spec.md §28 anticipated "~60% wiring tests" deletion; actual is 2.2%. **This is consistent with spec.md §218 SC-10** which explicitly frames the success criterion as "**Portfolio shape, not count** … Count reduction (~60% working estimate) is observation, not gate." The 11 DELETE files satisfy the "DELETE category gone" shape goal; the 6 KEEP categories are all represented (see below).

## By project (where the files live today)

| Project | File Count |
|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/` | 425 |
| `tests/integration/Spe.Integration.Tests/` | 29 |
| Other (RecordSyncJob.IsolatedTests + CrossPane folders) | 26 |
| `tests/unit/Spaarke.Scheduling.Tests/` | 8 |
| `tests/unit/Spaarke.Core.Tests/` | 2 |
| `tests/unit/Spaarke.Plugins.Tests/` | 2 |

## KEEP by category (all 6 represented per spec §73 + FR-B04)

| Category | Suggested Target Path | Count |
|---|---|---|
| domain-logic | `tests/unit/domain/**` | 288 |
| endpoint-contract | `tests/integration/contract/**` | 117 |
| data-mutation | `tests/integration/data-mutation/**` | 45 |
| security-auth | `tests/integration/auth/**` | 25 |
| regression | `tests/integration/regression/**` | 5 |
| tenant-isolation | `tests/integration/tenant/**` | 1 |
| **Total KEEP** | | **481** |

Tenant-isolation count (1) is low — flag for Phase 2 Stream B follow-up: spec.md §73 includes tenant-isolation as a KEEP category but the codebase has only 1 such test today. Worth a regression-test-driven backfill over the ≥6-month cultural change window (per design.md §257).

## DELETE by antipattern (all 11 live in Sprk.Bff.Api.Tests)

| Antipattern | Count | Project |
|---|---|---|
| HttpMessageHandler-mock | 9 | Sprk.Bff.Api.Tests |
| DI-registration | 2 | Sprk.Bff.Api.Tests |
| constructor-null-check | 0 | — |
| wiring-other | 0 | — |
| **Total DELETE** | **11** | |

Classification was **conservative** per spec §38 ("doubt = KEEP"). The classifier flagged DELETE only for files exhibiting clear binding-banned antipattern signatures (`Mock<HttpMessageHandler>`, explicit DI-container usage in test setup). Files that ARE wiring tests but lack these specific signatures default to KEEP and reorganize into one of the 6 path conventions.

## ⚠️ Sub-slicing recommendation: COLLAPSE tasks 053a/b/c into a single PR

The planned 3-sub-task split (053a/b/c) was based on the spec's ~60% deletion estimate. **With only 11 DELETE files, sub-slicing is unnecessary overhead.**

**Recommended revision to Phase 2 Stream B**:

| Original task | Recommended action |
|---|---|
| 053a-delete-bff-mock-httpmessagehandler (~50 files est.) | **MERGE into single 053** |
| 053b-delete-bff-di-registration-and-null-checks (~150-180 est.) | **MERGE into single 053** |
| 053c-delete-bff-remaining-by-directory (~200-235 est.) | **DROP — no remaining DELETEs** |

**Net effect on critical path**: shortens by ~2 elapsed days (one PR instead of three sequential rebases). Original critical path was `… → 053c → 070 …` ≈ 28 days; revised is `… → 053 → 070 …` ≈ 26 days.

**Decision authority for the merge**: operator chooses at Phase 2 start. The task POMLs 053a/b/c can be left in place (operator dispatches only 053a with merged scope, marks 053b/c as ⏭️ "merged into 053a"), OR consolidate into a new `053-delete-bff-wiring-tests.poml` and mark 053a/b/c as cancelled. Either approach is honest.

## Path reorganization scope for task 050

Of 492 files, **481 KEEP files need move-and-rename to canonical paths**:

| Move | Count |
|---|---|
| → `tests/integration/auth/**` | 25 |
| → `tests/integration/regression/**` | 5 |
| → `tests/integration/data-mutation/**` | 45 |
| → `tests/integration/tenant/**` | 1 |
| → `tests/integration/contract/**` | 117 |
| → `tests/unit/domain/**` | 288 |

Note: `tests/unit/domain/**` does not exist today (per spec UQ #3, confirmed in earlier exploration). Task 050 creates it from scratch.

The 11 DELETE files are NOT moved — they're removed by task 053 (revised single slice).

## Key findings flagged to operator

1. **Deletion magnitude much smaller than spec's working estimate** (11 vs ~280-300). This is consistent with SC-10 "portfolio shape, not count" framing. CI tier budget achievability (SC-01/SC-02) likely comes from: path-aware dispatch (only changed-surface tests run on PR), not from test count reduction.
2. **Sub-slicing 053a/b/c is overkill** — recommend collapsing to single 053 PR.
3. **tenant-isolation count is 1** — flag for regression-test-driven backfill over ≥6 months (per design.md §257 "Cultural change: ≥6 months").
4. **425 of 492 files live in `Sprk.Bff.Api.Tests/`** — the path reorganization (task 050) is dominated by moves out of that one project into the 6 canonical paths.
5. **Spe.Integration.Tests (29 files)** is already structurally aligned with the new path-convention model; mostly endpoint-contract category. Minor moves only.

## Files not in inventory

The `Other` bucket (26 files) covers `tests/unit/RecordSyncJob.IsolatedTests/` and `tests/integration/CrossPane/` (the latter is a folder not a .csproj per earlier exploration). Verify in task 050 whether these are KEEP/DELETE; current classification puts them in the KEEP+domain-logic bucket by default.

---

*Generated from `test-inventory.csv` (153KB, 493 rows). CSV is transient per spec FR-B05; this summary is the durable artifact.*
