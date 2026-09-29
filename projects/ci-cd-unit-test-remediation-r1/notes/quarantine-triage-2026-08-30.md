# The skipped-test quarantine — census, and why "drain it" is the wrong frame

> **2026-08-30.** The project has repeatedly noted "143 `Skip=` + 137 `[Trait("status","repaired")]`
> with nothing prompting review." This is the first census of **what is actually in there**.
>
> **Headline: only 4% is flakiness.** The quarantine is not a parking lot for unstable tests. It is
> ~116 tests that were authored and have never run.

---

## 1. Census

**121 `Skip = "…"` attributes** across the tree (the raw `grep -c 'Skip ='` figure of 133 counts
occurrences in prose and helper strings; 121 is the parsed count of real skip attributes).

| Category | Count | Share | What it means |
|---|---:|---:|---|
| **fixture-gap** | 40 | 33% | "Requires fully mocked X"; the test host was never completed for this path |
| **live-service** | 39 | 32% | "Requires live Dataverse / Azure AI Search / OpenAI / Redis" |
| **uncategorised** | 21 | 17% | On inspection mostly the two above (`IToolHandlerRegistry depends on Dataverse`, `Requires Redis`, `Requires complete mock setup`), plus a few "endpoint is not implemented" |
| **sealed-sdk** | 8 | 7% | "Graph SDK sealed classes cannot be mocked with Moq" |
| **superseded** | 8 | 7% | Explicitly obsolete |
| **flake-timing** | **5** | **4%** | Actual flakiness |

Concentration is high — **44 of 121 sit in three files**:

| Count | File |
|---:|---|
| 19 | `integration/Spe.Integration.Tests/PlaybookExecutionIntegrationTests.cs` |
| 13 | `integration/Spe.Integration.Tests/RagDedicatedDeploymentTests.cs` |
| 12 | `integration/Spe.Integration.Tests/RagSharedDeploymentTests.cs` |

---

## 2. The reframe

"Quarantine with no drain" implies tests that used to pass, went unstable, and are parked pending
repair. **The data says otherwise: 5 of 121.**

The other ~116 were written against an environment that CI does not have (live Dataverse, AI Search,
OpenAI, Redis) or against fixtures that were never finished. Most have been skipped since they were
written. **They have never protected anything.**

That is the same finding this project keeps hitting, in a third costume:

| Where | Tests that existed but never ran |
|---|---|
| Task 095 | **730** client test files, no CI job at all |
| PR #894 | **64** in `Spaarke.Core.Tests`, absent from the solution |
| Here | **~116** skipped since authoring |

The recurring defect is not flakiness. It is **authoring tests without confirming they run.**

ADR-038's own question settles the status of a permanently-skipped test.
`tests/CLAUDE.md` asks: *"What production behavior would break if this test were deleted?"*
For a test that never executes the answer is **nothing** — so by the project's own criterion these
are build-class, not maintain-class, and the honest default is deletion rather than indefinite storage.

---

## 3. What was actioned here

**Deleted `tests/unit/Sprk.Bff.Api.Tests/Integration/GraphApiWireMockTests.cs`** — 6 facts, **all 6
skipped**, so the entire file was dark. Its own class remark already contains the correct diagnosis:

> *"Each one points a bare `HttpClient` at WireMock and asserts WireMock returned the body WireMock was
> just told to return. No production code is on the path, so they cannot fail for any reason that
> matters — ADR-038 B7/B10 scaffolding."*

Someone diagnosed these correctly and then left them skipped instead of deleting them. That is the
quarantine's actual failure mode: **a correct diagnosis with no disposal step.** Not on a KEEP path,
so no replacement is owed. No coverage lost — there was none to lose.

---

## 4. Two tests that are NOT superseded and should be repaired, not deleted

Filed under "superseded" but genuinely **abandoned when production changed**:

| Test | Skip reason |
|---|---|
| `AgentMiddlewareTests` (cost control) | *"AgentCostControlMiddleware constructor signature changed — budget parameter no longer accepted"* |
| `AgentMiddlewareTests` (content safety) | *"AgentContentSafetyMiddleware PII pattern matching changed — test SSN pattern no longer triggers warning"* |

Production changed, the test broke, and the test was skipped rather than updated. That is the exact
move ADR-038 exists to prevent, and the second one touches PII redaction.

**Scoped honestly:** `AgentContentSafetyMiddleware` **does** retain live coverage (other facts in the
same file construct it and assert `FilteredPlaceholder` substitution). So this is a **narrowed**
assertion, not an untested safety control. An earlier draft of this note claimed PII was untested —
that was wrong, and the grep behind it had matched "SSN" inside the word **STATELESS·NESS**. Substring
matching has now produced a false finding in this project four times.

---

## 5. Recommendation

Ordered by value-per-effort. **None of this is a ratchet** — a count gate is what ADR-038 §3 and the
retired God-class guard already rejected.

| # | Action | Size | Notes |
|---|---|---|---|
| 1 | **Delete the remaining superseded skips** | small | Same shape as §3. Verify each per FR-B10 first. |
| 2 | **Repair the 2 abandoned middleware tests** | small | §4. Real lost assertions, cheap to restore. |
| 3 | **Fix the 5 flake-timing skips** | small | Pattern proven — PR #898 un-skipped 6 scheduling tests this way, suite now 56/56 in 7s. |
| 4 | **Decide the live-service question (≈50 tests)** | **the big one** | Is there an environment with real Dataverse/Search/OpenAI/Redis? **If yes** → move them to a nightly job and they finally run. **If no** → they can never run and should be deleted. Deciding is cheap; leaving it undecided is what preserves 50 dead tests. |
| 5 | **Fix or delete the fixture-gap cluster (≈45)** | medium | 44 of these sit in three files, so it is three decisions, not forty-five. Same root cause as issue #897. |

### Forcing function — require a reference, never a count

Every `Skip = "…"` should carry a tracking reference (issue number or task ID), enforced as a
structural fitness function in `tests/Spaarke.ArchTests/**`.

This mirrors a rule the repo already has and trusts — `tests/CLAUDE.md`: *"Every allowlist / census
entry carries a written reason and an ADR citation."* A skip is an exemption; exemptions already have
to justify themselves here.

**Deliberately not proposed:** a cap on skip count. That is the ratchet this codebase removed on
2026-08-20 for gating on the wrong instrument, and it would be gamed by inlining or deleting good
tests.

**Not armable today** — the rule would fail on 121 existing skips. It arms after item 4 resolves the
bulk. Sequencing matters: **decide the live-service question first**, because it is the single
decision that disposes of ~40% of the quarantine.
