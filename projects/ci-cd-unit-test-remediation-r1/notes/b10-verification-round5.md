# Task 084 verification pass — B10 bucket

> **2026-08-28** · The per-row verification the standing rule (spec FR-B10) requires before 084 may act.
> **Result: B10 went 247 → 18. 93% of the bucket was false positive.** Four more classifier defects found.

---

## Verdict

**Do not execute 084 as a mechanical deletion.** Even the surviving 18 need human adjudication — roughly half still look wrong to me on inspection.

**Recommendation: close 084 as not-worth-executing.** Deleting at most ~9 genuinely contentless tests out of 7,076 does not justify the review time or the risk, and it buys nothing the project's own decisions still call for. Numeric reduction is already recorded as *a signal, not a gate* (FR-B10 owner decision 1), and `/test-diet` at project close is already the sanctioned mechanism for residual cleanup. The value of this pass was the classifier, which now feeds `/test-diet` going forward — not the 18 deletions.

---

## The four defects found

| # | Defect | Evidence | Rows |
|---|---|---|---:|
| **7** | **Allow-list of exact assertion names, terminated by `\b`.** Any *longer* assertion that extends a listed name silently failed to match. | `.Should().NotBe(x)` (93), `.BeOneOf` (34), `.NotThrowAsync` (19), `.ContainKey` (17), `.MatchRegex` (16), `.BeApproximately`, `.BeCloseTo`, `.OnlyHaveUniqueItems`, `.ContainInOrder`, `.AllSatisfy`, `Assert.ThrowsAnyAsync`. Of six sampled forms the **only** one the list recognised was `NotBeNull` — the weakest of them. | ~195 |
| **8** | **Assertion delegated to a helper** is invisible to a body-local counter. | `Engine_ThreadMatch_IsDirectionSymmetric` and 3 siblings: the entire body is `await AssertSymmetric(…)`, which asserts `inWrites.Should().ContainSingle()`. | ~30 |
| **9** | **Expression-bodied methods produce an EMPTY body.** No `{`, so the brace-balance loop never starts. Empty body → assertCount 0 → "no assertion". | `NoisyOr_CombinesIndependentConfidences_Bounded` (`=> …Should().BeApproximately(expected, 0.001)`) and `Union_FirstValue_ReturnsIt` (`=> …Should().Be("a@x.com")`). **679 methods in this suite are expression-bodied.** | 2 in residue, 679 at risk |
| **10** | **Chained `.And.` continuations not read.** Only the form directly after `.Should()` was captured. | `AnalysisAction_DefaultGroundedToolAllowList_IsEmptyNotNull` asserts `.Should().NotBeNull().And.BeEmpty()`. Reading only `NotBeNull` deletes a test whose real contract is the `.And.BeEmpty()` half — the **deny-by-default state of a grounded-tool ALLOW-LIST**. | 1 in residue |

### Defect 7 is the important one

It is round 1's bug recurring, and round 1's fix — *add more names to the list* — is what let it recur. An allow-list of exact names can never be complete against a fluent assertion API.

**Fixed by inverting to a deny-list**: any assertion is meaningful *unless* every assertion in the method is one of `{NotBeNull, NotNull}`. That is ADR-038's actual B10 shape — "it returned something" is coverage, not behavior. `BeNull`/`Null` are deliberately **not** trivial: a null outcome is a real contract, which is round 1's original lesson.

---

## Trajectory

| Round | DELETE | What it found |
|---|---:|---|
| 1 | 3,411 | meaningful-assertion list omitted `Assert.Null`/`Empty`/`Single` |
| 2 | 427 | B16 matched substrings inside ordinary words |
| 3 | 358 → 301 | six bugs; B1/B3/B8 all → 0, every row false positive |
| 4 | 301 | absence-of-throw as the contract |
| **5** | **301 → 18** | **the four above** |

Six rounds. **Every one found the classifier over-calling; none ever found it under-calling.** The failure mode has been consistently *deleting good tests*.

---

## The 18 that survive — and why they are still not clean

**Plausibly genuine B10 (~9)**: `Services_Should_Be_Registered_Correctly`; `ReportingEndpointsTests` ×3 `*_HasExpectedProperties`; `DataverseEntitySchemaTests` ×4 `*_HasAll*Properties` + `EmailMetadata_HasAllRequiredProperties`; `OpenAiClient_InitializesWithCircuitBreaker`. These assert that a DTO has properties — mirror-shaped coverage.

**Still questionable (~9)**, each stating a specific behavioral outcome its name commits to:

- `NullMembershipCacheInvalidator_LogsAndReturns`, `NullHost_ExecuteAsync_LogsAndReturnsImmediately` — **ADR-032 Null-Object quiet semantics again.** The absence-contract name heuristic added in round 4 does not match "LogsAndReturns"/"ReturnsImmediately". This class has now been rescued three separate times under three different names, which is itself evidence the heuristic approach is at its limit.
- `UpdateDocumentRequest_LegacySearchIndexFieldsPreservedForDualWrite` — a dual-write **migration invariant**.
- `PublishStatusUpdate_MultipleUpdates_MaintainCorrectOrder`, `SequenceNumbers_AllowClientReconnection` — SSE ordering and reconnection contracts.
- `Apply_InsertTextWithMarks_AppliesMarksToInsertedRun` — Compose shadow-patch engine behavior.
- `Inject_WhenNoOrderElement_AppendsFilterToEntity`, `ExecuteChatAsync_Matrix_AllPurposesXScopes_ProduceStructuredToolResult`.

A bucket where half the residue is arguable is not a bucket to delete mechanically.

---

## What carries forward

The classifier is materially better and is the durable artifact: comment-stripped matching, deny-list assertions, helper-assertion awareness, expression-bodied capture, chained-continuation parsing, and five `AMBIGUOUS-*` routes that can never reach DELETE. `/test-diet` inherits all of it, and applies it to the small set of tests each project actually touched — which is where this kind of judgment is affordable.
