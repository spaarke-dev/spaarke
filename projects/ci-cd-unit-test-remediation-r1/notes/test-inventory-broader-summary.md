# Test inventory — broader build-vs-maintain criteria (task CICD-082)

> **Generated**: 2026-08-28 · **Spec**: FR-B10 · **Criteria**: [ADR-038 §7](../../../docs/adr/ADR-038-testing-strategy.md#7-build-vs-maintain-criteria-scaffolding-test-bans--added-2026-06-26-per-spec-fr-b08) (17 bans)
> **Classifier**: [`scripts/Classify-BffUnitTests.ps1`](../scripts/Classify-BffUnitTests.ps1) · **Data**: [`test-inventory-broader.csv`](test-inventory-broader.csv)
> **Revised 2026-08-28** after spot-check rounds 3 + 4 (see "Accuracy control"). DELETE went 358 → **301**.

---

## Operator decision — recorded, not re-opened

FR-B10 projected **1,500–3,000** DELETE candidates. High-confidence mechanical detection finds **301**.

Per the owner decision recorded in PR #852, this is **accepted and not a deviation**: FR-B10's own MUST says *"numeric reduction is a signal, not a gate."* The 1,500–3,000 was a projection about what mechanical detection would find, and that projection was wrong. See "What the gap actually means".

| Classification | Count | % of suite |
|---|---:|---:|
| **KEEP-maintain** | 5,325 | 74.8% |
| **AMBIGUOUS** (human judgement) | 1,493 | 21.0% |
| **DELETE-scaffolding** (auto-safe) | **301** | 4.2% |
| **Total classified** | **7,119** | |

---

## Scale correction — the POML's numbers are stale

| | POML says | Actual |
|---|---:|---:|
| Test files | 425 | **592** |
| Test methods | ~6,695 | **7,119** classified (6,951 `[Fact]` + 376 `[Theory]`) |
| LOC | — | **211,523** |

The suite **grew ~10%** between the inventory estimate and today. Any "reduce to ≤3,500" target should be restated against 7,119 — and per PR #852 that target is retired as unreachable by the sanctioned path.

---

## DELETE-scaffolding — the 301

| Bucket | Count | Confidence | Owning task |
|---|---:|---|---|
| `B10-coverage-filler` | 247 | medium | **084** — needs a review pass |
| `B4-ctor-null-check` | 54 | **high — 54/54 individually verified** | **083** |

Round 3 emptied three buckets entirely: `B1-http-message-handler-mock` 1 → **0**, `B3-di-registration` 13 → **0**, `B8-private-via-reflection` 3 → **0**. Every row in all three was a false positive. Details below.

Top files by DELETE density: `PlaybookRunEndpointsTests.cs` (20), `DuplicateDetectionTests.cs` (14), `SignalEvaluationServiceTests.cs` (10), `NodeEndpointsTests.cs` (10), `OfficeJobStatusServiceTests.cs` (9).

### The 54 in task 083 are verified individually, not sampled

An independent check — separate code path from the classifier — re-parsed each method with brace balance, stripped comments, and required both that the *act* be a construction (`=> new X(…null…)`) and that no instance-method act be present. **54/54 passed.** The two whose names don't begin `Constructor`/`Ctor` were also read by hand:

- `ExternalAccessEndpointTests :: SpeContainerMembershipService_NullGraphClientFactory_ThrowsArgumentNull` — `() => new SpeContainerMembershipService(null!, …)` ✓
- `MembershipEventPublisherTests :: NullPublisher_Constructor_NullLogger_Throws` — `() => new NullMembershipEventPublisher(logger: null!)` ✓

---

## AMBIGUOUS — 1,493, and why they are NOT auto-DELETE

| Bucket | Count | ADR-038's own first remedy |
|---|---:|---|
| `B13-name-missing-scenario` | 1,124 | **"Rename per convention** or delete" |
| `B15-setup-heavy` | 326 | "Integration test with amortized setup" |
| `B7-all-mocks-trivial` | 6 | "Integration test or delete" |
| `AMBIGUOUS-b10-absence-contract` | 35 | *(not a ban — see round 4)* |
| `AMBIGUOUS-adr032-killswitch` | 2 | *(not a ban — see round 3)* |

For the three real bans, the ADR's *Acceptable replacement* column names a non-delete remedy first. Auto-deleting them would skip the cheaper fix and destroy possibly-good tests.

**`B13` is the whole story of the gap.** 1,124 tests — 15.8% of the suite — carry names that do not state scenario + expected result. That is a real, large finding, but the remedy is *rename*. Per the owner decision in PR #852, **a bad name is not grounds for deletion**; task 085 is re-scoped accordingly.

The two `AMBIGUOUS-*` buckets are **not bans at all**. They mark tests a ban would otherwise have eaten, and they can never reach DELETE.

---

## What the gap actually means

Three readings, in order of likelihood:

1. **The bans are mostly non-mechanical.** 9 of 17 (B2, B5, B6, B9, B11, B12, B14, B17, and most of B7) require reading intent. B6 "mirror tests" in particular — implementation == implementation — cannot be regex-detected at all, and is plausibly the single largest real bucket. **Mechanical detection under-counts by construction.**
2. **Task 053 already removed the easy wins.** The narrow-criteria pass deleted the most obvious scaffolding; what remains is by definition harder to detect.
3. **The suite may genuinely be better than FR-B10 assumed.** Possible, but least likely given 1,124 tests cannot state what they assert.

**Do NOT tune the classifier until it reaches 1,500.** That inverts the method — picking a number and manufacturing criteria to hit it is how the first run produced 3,411 DELETEs including `LoadSessionAsync_BothMiss_ReturnsNull`.

---

## Recommended slicing for 083 / 084 / 085

| Task | Scope | Count | Risk |
|---|---|---:|---|
| **083** | `B4-ctor-null-check` | **54** | **Low** — ADR-explicit, no judgement, 54/54 verified |
| **084** | `B10-coverage-filler`, after a review pass | **247** | Medium — see the round-4 warning below |
| **085** | `B13` naming remediation + `B15`/`B7` | **1,456** | **RE-SCOPED — mostly RENAME.** See PR #852 |

⚠️ **084 is not yet safe to execute mechanically.** Round 4 found and fixed one over-call class inside B10 (absence-of-throw contracts) but only via a name heuristic. B10 is the one medium-confidence bucket left, and it has now produced a false positive in two separate rounds. It needs its own verification pass of the kind 083 got — not a sample.

---

## Accuracy control — four spot-check rounds, four sets of bugs

Step 8 required 10 random DELETE samples. **Every round so far has found the classifier over-calling**, i.e. about to delete good tests. This is the audit trail.

**Round 1 — 3,411 DELETE.** Sample surfaced `LoadSessionAsync_BothMiss_ReturnsNull`, `DeleteDocumentAsync_NullDocumentId_ThrowsArgumentException`, `ResolveBindingAsync_MalformedChipTransitionsJson_ResolvesWithEmptyChips` — all good behavioral tests.
**Cause**: the "meaningful assertion" list omitted `Assert.Null` / `Empty` / `Single`, so a test whose entire contract is *returns null* scored as trivial.
**Fix**: expanded to ~40 xUnit + FluentAssertions + Moq forms; reclassified B13/B15/B7 as AMBIGUOUS. **3,411 → 427.**

**Round 2 — 427 DELETE.** Sample surfaced `ResolveAsync_NoClo**set**_…`, `Render_WithOutOfRangeStartOff**set**_…`, `SaveAsync_…FireAndFor**get**_…`.
**Cause**: B16's `(get|set)_?` alternation matched substrings inside ordinary words.
**Fix**: word-boundary anchors + require body corroboration. B16 went 69 → **0**. **427 → 358.**

**Round 3 — 358 DELETE. Four distinct bugs, all over-calls.**

| # | Bug | Evidence | Fix |
|---|---|---|---|
| A | **Ban detection matched prose, not code** | `AiCompletionNodeExecutorTests` carries the header comment *"ADR-038 compliance: NO `Mock<HttpMessageHandler>`"*. Both the file flag and the body match fired on it — **a file documenting its compliance was classified as violating it.** | `Remove-CsComments` applied before all ban matching; B1 now requires the actual `Mock<HttpMessageHandler>` construction |
| B | **`GetService` name collision** | `ExportServiceRegistry.GetService(ExportFormat.Docx)` is a domain strategy selector built with `new`. Matched `\bGetService\b` as if it were `IServiceProvider`. | B3 now requires `BuildServiceProvider` in the body |
| C | **`-match` is case-INSENSITIVE in PowerShell** | `[^)]*null` matched the `Null` in every ADR-032 Null-Object type name, e.g. `new NullMembershipEventPublisher(Mock.Of<ILogger<NullMembershipEventPublisher>>())`. **The entire Null-Object family was a B4 candidate.** | `-cmatch`, so only the C# keyword `null` counts |
| D | **`JsonElement.GetProperty` read as reflection** | `DailyBriefingResponseShapeTests` — a golden fixture its own header calls *"the load-bearing golden fixture; drift here is a widget-parser break"* — was flagged B8 for navigating JSON. | B8 now requires `BindingFlags.` in the method body |

Round 3 also found that **B4 was eating method tests**: `new EffortScoreInput(null!, …)` followed by `_sut.CalculateEffortScore(input)` matched a "constructor" null-check. B4 now requires the *act itself* to be the construction (`=> new X(`).

Separately, B3 was eating the **ADR-032 Null-Object kill-switch contract** — the 8 `TodoSyncModule` FlagOn/FlagOff tests and the 2 `CacheModule` Redis on/off tests. Those assert *which* implementation resolves under a flag; app start proves only that *something* resolves. Root CLAUDE.md §10 / [`bff-extensions.md` §F.1](../../../.claude/constraints/bff-extensions.md) make this a binding sub-mechanism — it is the regression cover for the RB-T028-03..06 production defect class. **358 → 301** across rounds 3–4.

**Round 4 — verification of the round-3 fixes.** All 19 rescued rows confirmed landed in KEEP or AMBIGUOUS, none in DELETE. All 54 remaining B4 rows verified individually (above). One **new** over-call class surfaced while checking the rescues:

- `FlagOff_NullTodoGraphSyncHandler_IsQuietNoOp` and `FlagOff_NullTodoSyncBackfiller_IsNoOpButLogsOnce` were sitting in DELETE as `B10-coverage-filler` — they have no assertion because *"completes without throwing"* is precisely the ADR-032 P2 quiet-semantics contract they exist to pin. The same shape covers `LogInteractionAsync_CosmosThrows_DoesNotThrowToCallerAndLogsError` (audit logging must not break its caller — a resilience contract).
- **This is round 1's failure mode recurring in a different bucket**: a *negative* expected outcome reads as "no expectation" to a counter. 35 rows moved to `AMBIGUOUS-b10-absence-contract`.

**Every round over-called; none under-called.** The failure mode has been consistently *deleting good tests*. The classifier is deliberately conservative for that reason: ADR-038's rule is *doubt = KEEP*, and an under-call costs a review pass while an over-call destroys regression cover.

> **The spot-check is load-bearing and must not be skipped.** Four rounds, four sets of real bugs. Do not act on a bucket that has not had a clean verification round.

---

## Protected-path safety (FR-B06)

Zero DELETE rows fall under the protected KEEP paths. The classifier hard-codes them (`tests/integration/{auth,regression,data-mutation,tenant,contract}/**` + `tests/Spaarke.ArchTests/**` per Amendment A1) and downgrades any ban firing there to AMBIGUOUS with a `PROTECTED::` prefix. In this run the question was moot — 082 targets `tests/unit/Sprk.Bff.Api.Tests/` only, which contains none of those paths — but the guard stays for reuse.
