# Deep cleanup PR 1/3 — task CICD-083

> **Executed**: 2026-08-28 · **Spec**: FR-B10 · **Bucket**: `B4-ctor-null-check` (ADR-038 §7 ban B4)
> **Inventory**: [`test-inventory-broader.csv`](test-inventory-broader.csv) · **Rationale**: [`test-inventory-broader-summary.md`](test-inventory-broader-summary.md)

---

## What was removed

**54 constructor null-guard test methods across 29 files.** 506 lines deleted, 0 added.

ADR-038 §7 ban **B4** — *"constructor null-check tests"* — with the acceptable replacement being `ArgumentNullException.ThrowIfNull` in the production constructor, which is already the codebase convention. These tests assert that a guard clause exists, not that any behavior is correct.

Every removal is a **method-level** deletion. No file was deleted; no file became empty.

| Count | File |
|---:|---|
| 4 | `Services/Ai/DocumentParserRouterTests.cs` |
| 4 | `Services/Insights/Precedents/PrecedentProjectionSyncTests.cs` |
| 3 | `Services/Ai/PlaybookSchedulerJobTests.cs` |
| 3 | `Services/Finance/Tools/FinancialCalculationToolHandlerTests.cs` |
| 3 | `Services/Jobs/Insights/InsightsIngestJobHandlerTests.cs` |
| 3 | `Services/Workspace/TodoGenerationServiceTests.cs` |
| 2 | `Filters/IdempotencyFilterTests.cs`, `Services/Ai/AiAuthorizationServiceTests.cs`, `Services/Ai/Audit/AuditLogServiceTests.cs`, `Services/Ai/ComposePdfIntakeSourceTests.cs`, `Services/Ai/Membership/Events/MembershipEventPublisherTests.cs`, `Services/Ai/Memory/PromptBudgetTrackerTests.cs`, `Services/Insights/LiveFacts/{Invoice,Matter,Project}LiveFactResolverTests.cs`, `Services/NotificationServiceTests.cs`, `Services/Workspace/TodoRegardingBuilderTests.cs` |
| 1 | 13 further files (see the diff) |

---

## Why this bucket, and not the one the POML named

The POML anticipated *"mirror-tests + all-mocks-trivial, ~500–1000 tests"* and states that **"bucket assignment can be revised at task start based on 082's actual output."** It was revised, twice:

1. **B6 mirror-tests is not mechanically detectable.** *Implementation == implementation* has no regex signature. It is plausibly the largest real bucket in the suite, and none of it is in the inventory. `B7-all-mocks-trivial` yielded 6 rows, all AMBIGUOUS because ADR-038's own first remedy for B7 is *"integration test"*, not deletion.
2. **The original slice was 76 rows (B4+B3+B8+B1). Spot-check rounds 3 and 4 cut it to 54** by finding six classifier over-call bugs — every one of which would have deleted a good test. B1, B3 and B8 went to **zero**; every row in all three was a false positive. Full detail in the summary's "Accuracy control" section.

The ~500–1000 estimate does not survive contact with the data, and per the owner decision in PR #852, **the FR-B10 numeric target is a signal, not a gate.**

> The `<constraint source="spec FR-B10">` line in the POML — *"Combined 083+084+085 target: ~6,695 → ≤3,500"* — is **retired** per PR #852. It is unreachable by the sanctioned path (~640 maximum removals against a suite that has since grown to 7,119 methods), and pursuing it would mean deleting tests to hit a number.

---

## Verification

| Check | Result |
|---|---|
| Every B4 row verified before deletion | **54/54**, individually — not sampled |
| `dotnet build` | **Succeeded — 0 warnings, 0 errors** |
| `dotnet test` (full suite, not the sampled subset step 6 permits) | **Passed — 0 failed, 10,702 passed, 77 skipped, 10,779 total** |
| Test attributes removed (diff-counted) | **54** — matches the inventory exactly |
| Lines added | **0** |
| KEEP-protected-path violations (FR-B06) | **None.** Zero DELETE rows fall under `tests/integration/{auth,regression,data-mutation,tenant,contract}/**` or `tests/Spaarke.ArchTests/**` |

The pre-deletion verification used a **separate code path from the classifier**: each method was re-parsed with brace balance, comments stripped, and required to have a construction as its act (`=> new X(…null…)`) with no instance-method act present. The two methods not named `Constructor*`/`Ctor*` were additionally read by hand and confirmed as constructions.

The clean build with **zero warnings** is the evidence that no deleted method was the sole user of a field, mock, or helper.

---

## Surviving state

- BFF unit test methods: **7,119 → 7,065**
- Executable test cases: **10,779** (Theory expansion), all passing
- Remaining DELETE inventory: **247** rows, all `B10-coverage-filler`, owned by task **084**

⚠️ **084 is not cleared to run mechanically.** `B10` is the last medium-confidence bucket and has produced a false positive in two separate rounds — most recently *absence-of-throw as the contract* (`FlagOff_NullTodoGraphSyncHandler_IsQuietNoOp`, `LogInteractionAsync_CosmosThrows_DoesNotThrowToCallerAndLogsError`), where the missing assertion **is** the point. Round 4 mitigated that with a name heuristic, which is not verification. 084 needs a per-row pass of the kind these 54 got before a single deletion.
