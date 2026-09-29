# Task 084 verification pass — B10 bucket

> **2026-08-28** · The per-row verification the standing rule (spec FR-B10, established by task 083) requires before a bucket may be deleted.
> **Result: B10 went 247 → 1. Six more classifier defects found, all over-calls.**

---

## Verdict: close 084 without executing it

Of the 247 rows task 084 was scoped to delete, **exactly one is a genuine scaffolding test**:

```csharp
// OpenAiClientCircuitBreakerTests.OpenAiClient_InitializesWithCircuitBreaker
var client = new OpenAiClient(Options.Create(_options), _loggerMock.Object);
Assert.NotNull(client);
```

The other **246 were false positives** — they assert real behavior through forms the classifier could not see.

One test is not worth a task, a PR, or a review cycle. **084 should be closed.** This is not a retreat from the cleanup thesis; it is the thesis meeting the evidence, and it is consistent with the owner decisions already recorded in #852: numeric reduction is *a signal, not a gate*, and `/test-diet` is the sanctioned mechanism for residual cleanup.

---

## The six defects

| # | Defect | Evidence | Impact |
|---|---|---|---|
| **7** | **Allow-list of exact assertion names terminated by `\b`** — any *longer* assertion extending a listed name silently failed to match | `.Should().NotBe(x)` (93 rows), `.BeOneOf` (34), `.NotThrowAsync` (19), `.ContainKey` (17), `.MatchRegex` (16), `.BeApproximately`, `.BeCloseTo`, `.OnlyHaveUniqueItems`, `.AllSatisfy`, `Assert.ThrowsAnyAsync`. **Of six forms sampled, the only one recognised was `NotBeNull` — the weakest of the six.** | ~195 rows |
| **8** | **Assertion delegated to a helper** in the same file | `Engine_ThreadMatch_IsDirectionSymmetric` + 3 siblings are entirely `await AssertSymmetric(…)` | ~30 rows |
| **9** | **Expression-bodied methods produce an EMPTY body** — no `{`, so brace-balance never starts; empty body reads as "no assertion" | `NoisyOr_…_Bounded` (asserts `BeApproximately`), `Union_FirstValue_ReturnsIt` (asserts `Be`). **679 methods in this suite are expression-bodied.** | 2 in residue, 679 at risk |
| **10** | **Chained `.And.` continuations not read** | `AnalysisAction_DefaultGroundedToolAllowList_IsEmptyNotNull` asserts `.Should().NotBeNull().And.BeEmpty()` — reading only `NotBeNull` would delete the **deny-by-default guard on a grounded-tool allow-list** | 1 |
| **11** | **Braces inside string/char literals corrupt body capture** | `json.IndexOf('}', sequenceStart)` inside a Moq callback closed the lambda early: capture ended at line 170, and the real assertions (`BeEquivalentTo`, `BeInAscendingOrder`) on lines 185–186 were never seen. **Fixing this raised the total method count 7,076 → 7,263 — the parser had been mis-reading the suite generally, not just these rows.** | 2 in residue, suite-wide |
| **12** | **Assertion helper declared in a BASE CLASS**, invisible to a same-file scan | `Telemetry_DoesNotLogInputValues` and `Telemetry_DoesNotLogInvoiceContent_OrMonetaryValues` — **ADR-015 PII-leak regression tests** — assert entirely through the inherited `AssertTelemetryRespectsAdr015(…)` | 2 |

### Defect 7 is the structural one

It is round 1's bug recurring, and **round 1's fix — *add more names to the list* — is precisely what let it recur.** An allow-list of exact names cannot be complete against a fluent assertion API.

Fixed by **inverting to a deny-list**: any assertion is meaningful *unless* every assertion in the method is `NotBeNull`/`NotNull`. `BeNull`/`Null` stay meaningful — a null outcome is a real contract, which was round 1's original lesson.

A refinement followed from adjudicating the residue: **`NotBeNull` on a *navigated* expression is a real assertion**, and only `NotBeNull` on the bare result is B10's shape.

```csharp
result.Should().NotBeNull()                          // weak: "it returned something"
insRun.RunProperties!.Bold.Should().NotBeNull()      // "the Bold mark WAS applied"
…Element("entity")!.Element("filter").Should().NotBeNull()   // "a filter WAS appended"
```

### The ADR-032 Null-Object class, rescued a third time

`NullMembershipCacheInvalidator_LogsAndReturns` and `NullHost_ExecuteAsync_LogsAndReturnsImmediately` reached the residue because round 4's *name* heuristic does not match "LogsAndReturns"/"ReturnsImmediately". This class has now been rescued three times under three different namings — which is the signal that a name list was the wrong instrument. It is now detected **structurally**: constructing a `Null*` peer and exercising it without asserting *is* the ADR-032 P2 quiet-semantics contract.

---

## Trajectory

| Round | DELETE | Found |
|---|---:|---|
| 1 | 3,411 | meaningful-assertion list omitted `Assert.Null`/`Empty`/`Single` |
| 2 | 427 | B16 matched substrings inside ordinary words |
| 3 | 358 → 301 | six bugs; B1/B3/B8 → 0, every row false positive |
| 4 | 301 | absence-of-throw as the contract |
| **5** | **301 → 1** | **the six above** |

**Six rounds. Every one found the classifier over-calling. Not once did it under-call.** The failure mode has been consistently *deleting good tests* — which is why ADR-038's *doubt = KEEP* rule is the right default and why the standing rule (verify every row, independent code path, before acting) earns its cost.

---

## What actually came out of this

The deletions were never the value. The **classifier** is, and it now carries:

- comment-stripped ban matching (never match prose)
- deny-list assertions with navigated-subject discrimination
- helper-assertion awareness, same-file and inherited
- expression-bodied method capture
- chained `.And.`/`.Which.` continuations
- literal-safe brace counting
- five `AMBIGUOUS-*` routes that can never reach DELETE

`/test-diet` inherits all of it and applies it to the small set of tests each project actually touched — which is where judgment of this kind is affordable, and is exactly the mechanism the owner decisions in #852 already designated.
