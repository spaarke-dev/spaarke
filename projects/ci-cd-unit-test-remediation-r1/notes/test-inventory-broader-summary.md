# Test inventory — broader build-vs-maintain criteria (task CICD-082)

> **Generated**: 2026-08-28 · **Spec**: FR-B10 · **Criteria**: [ADR-038 §7](../../../docs/adr/ADR-038-testing-strategy.md#7-build-vs-maintain-criteria-scaffolding-test-bans--added-2026-06-26-per-spec-fr-b08) (17 bans)
> **Classifier**: [`scripts/Classify-BffUnitTests.ps1`](../scripts/Classify-BffUnitTests.ps1) · **Data**: [`test-inventory-broader.csv`](test-inventory-broader.csv)

---

## 🔔 Operator decision required — DELETE count is below the FR-B10 target

FR-B10 projected **1,500–3,000** DELETE candidates. High-confidence mechanical detection found **358**.

The POML's step 5 anticipates exactly this (*"if total < 1,500: criteria may be too conservative — surface to operator"*). This is that surfacing. **It is not a failure of the cleanup thesis** — see "What the gap actually means" below before concluding the suite is fine.

| Classification | Count | % of suite |
|---|---:|---:|
| **KEEP-maintain** | 5,306 | 74.5% |
| **AMBIGUOUS** (human judgement) | 1,455 | 20.4% |
| **DELETE-scaffolding** (auto-safe) | **358** | 5.0% |
| **Total classified** | **7,119** | |

**358 + 1,455 = 1,813 flagged**, which *is* inside FR-B10's 1,500–3,000 band. The band is only met if AMBIGUOUS largely resolves to DELETE — and that resolution is a human call, not a script's.

---

## Scale correction — the POML's numbers are stale

| | POML says | Actual |
|---|---:|---:|
| Test files | 425 | **592** |
| Test methods | ~6,695 | **7,119** classified (6,951 `[Fact]` + 376 `[Theory]`) |
| LOC | — | **211,523** |

The suite **grew ~10%** between the inventory estimate and today. Any "reduce to ≤3,500" target should be restated against 7,119, not 6,695.

---

## DELETE-scaffolding — the 358 that are safe to act on

| Bucket | Count | Confidence |
|---|---:|---|
| `B10-coverage-filler` | 282 | medium-high |
| `B4-ctor-null-check` | 59 | **high** |
| `B3-di-registration` | 13 | **high** |
| `B8-private-via-reflection` | 3 | **high** |
| `B1-http-message-handler-mock` | 1 | **high** |

Top files by DELETE density: `PlaybookRunEndpointsTests.cs` (20), `DuplicateDetectionTests.cs` (14), `SignalEvaluationServiceTests.cs` (10), `TodoSyncModuleTests.cs` (10), `NodeEndpointsTests.cs` (10).

**B10 still warrants a review pass** before deletion — it is the only medium-confidence bucket here and it survived two rounds of false-positive correction (below).

---

## AMBIGUOUS — 1,455, and why they are NOT auto-DELETE

| Bucket | Count | ADR-038's own first remedy |
|---|---:|---|
| `B13-name-missing-scenario` | 1,123 | **"Rename per convention** or delete" |
| `B15-setup-heavy` | 326 | "Integration test with amortized setup" |
| `B7-all-mocks-trivial` | 6 | "Integration test or delete" |

For all three, the ADR's *Acceptable replacement* column names a non-delete remedy first. Auto-deleting them would skip the cheaper fix and destroy possibly-good tests.

**`B13` is the whole story of the gap.** 1,123 tests — 15.8% of the suite — carry names that do not state scenario + expected result. That is a real, large finding, but the remedy is *rename*, and a rename is not a deletion. Treating B13 as DELETE is what produced the first run's inflated 3,411.

---

## What the gap actually means

Three readings, in order of likelihood:

1. **The bans are mostly non-mechanical.** 9 of 17 (B2, B5, B6, B9, B11, B12, B14, B17, and most of B7) require reading intent. B6 "mirror tests" in particular — implementation == implementation — cannot be regex-detected at all, and is plausibly the single largest real bucket. **Mechanical detection under-counts by construction.**
2. **Task 053 already removed the easy wins.** The narrow-criteria pass deleted the most obvious scaffolding; what remains is by definition harder to detect.
3. **The suite may genuinely be better than FR-B10 assumed.** Possible, but least likely given 1,123 tests cannot state what they assert.

**Recommendation**: do NOT tune the classifier until it reaches 1,500. That inverts the method — picking a number and manufacturing criteria to hit it is how the first run produced 3,411 DELETEs including `LoadSessionAsync_BothMiss_ReturnsNull`.

---

## Recommended slicing for 083 / 084 / 085

| Task | Scope | Count | Risk |
|---|---|---:|---|
| **083** | The 4 high-confidence buckets: B4 + B3 + B8 + B1 | **76** | **Low** — mechanically identifiable, ADR-explicit, no judgement |
| **084** | `B10-coverage-filler`, after a review pass | **282** | Medium — verify each asserts nothing meaningful |
| **085** | `B13` rename-vs-delete adjudication + `B15`/`B7` | **1,455** | **High — mostly RENAME, not delete.** Should likely be re-scoped |

**085 as written does not fit its bucket.** It is scoped as "final sweep + deletion", but its actual content is 1,123 rename decisions. Recommend re-scoping to *"B13 naming remediation"* with deletion as the exception, or splitting the rename work into its own task.

---

## Accuracy control — two spot-check rounds, two bugs caught

Step 8 required 10 random DELETE samples. Both rounds failed and drove classifier fixes; this is the audit trail.

**Round 1 — 3,411 DELETE.** Sample surfaced `LoadSessionAsync_BothMiss_ReturnsNull`, `DeleteDocumentAsync_NullDocumentId_ThrowsArgumentException`, `ResolveBindingAsync_MalformedChipTransitionsJson_ResolvesWithEmptyChips` — all good behavioral tests.
**Cause**: the "meaningful assertion" list omitted `Assert.Null` / `Empty` / `Single`, so a test whose entire contract is *returns null* scored as trivial.
**Fix**: expanded to ~40 xUnit + FluentAssertions + Moq forms; reclassified B13/B15/B7 as AMBIGUOUS. **3,411 → 427.**

**Round 2 — 427 DELETE.** Sample surfaced `ResolveAsync_NoClo**set**_…`, `Render_WithOutOfRangeStartOff**set**_…`, `SaveAsync_…FireAndFor**get**_…`.
**Cause**: B16's `(get|set)_?` alternation matched substrings inside ordinary words.
**Fix**: word-boundary anchors + require body corroboration. B16 went 69 → **0**, i.e. all 69 were false positives. **427 → 358.**

**Both rounds over-called, never under-called** — the failure mode was deleting good tests. The classifier is deliberately conservative for that reason: ADR-038's rule is *doubt = KEEP*, and an under-call costs a review pass while an over-call destroys regression cover.

⚠️ **A third spot-check has not been run against the current 358.** Recommended before 083 acts on them.

---

## Protected-path safety (FR-B06)

Zero DELETE rows fall under the protected KEEP paths. The classifier hard-codes them (`tests/integration/{auth,regression,data-mutation,tenant,contract}/**` + `tests/Spaarke.ArchTests/**` per Amendment A1) and downgrades any ban firing there to AMBIGUOUS with a `PROTECTED::` prefix. In this run the question was moot — 082 targets `tests/unit/Sprk.Bff.Api.Tests/` only, which contains none of those paths — but the guard stays for reuse.
