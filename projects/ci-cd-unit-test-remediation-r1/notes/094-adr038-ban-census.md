# Task CICD-094 — ADR-038 §7: all 17 bans accounted for

> **2026-08-29** · Issue [#864](https://github.com/spaarke-dev/spaarke/issues/864). Closes the gap PR #865 left open:
> two bans armed, fifteen documented-but-unenforced.
>
> **Result: 5 of 17 armed (B1, B3, B4, B12, B16). 12 documented-unenforceable, each with a live count.**
>
> Net new enforcement this task: **B3, B12, B16**, at a migration cost of **4 test methods in 2 files**.

---

## Why the measurement and the guard are the same code

The instrument used to measure a ban has to be the instrument that enforces it. Measure with a loose
detector and arm a tight one and you arm a guard weaker than your evidence; measure tight and arm
loose and the guard ships red. Both are #839 in a new costume.

So every count below was produced by the regex that now lives in
[`Adr038TestBanGuardTests.cs`](../../../tests/Spaarke.ArchTests/Adr038TestBanGuardTests.cs), or — for
the bans that stay unarmed — by a detector written in the same dialect and reported with its
false-positive rate rather than as a bare number.

**A count is not a finding until the rows are read.** Small counts here were adjudicated row by row,
not sampled. That is the rule task 083 established (spec FR-B10) and the one that turned B10 from
"247 scaffolding tests" into "one".

---

## The 17

| Ban | What it bans | Detector hits | True after adjudication | Verdict |
|---|---|---:|---:|---|
| **B1** | `Mock<HttpMessageHandler>` | 0 | **0** | ✅ **ARMED** (#865) |
| **B2** | Typed HttpClient-wrapper mocks | 4 | **0** | ❌ judgment-gated + no such type |
| **B3** | DI-registration assertions | 5 | **1** | ✅ **ARMED** — migrated here |
| **B4** | Constructor null-argument tests | 0 | **0** | ✅ **ARMED** (#865, after 083 deleted 54) |
| **B5** | Mocking the SUT's own collaborators | — | — | ❌ judgment ("when … cheaper and more honest") |
| **B6** | Mirror tests | — | — | ❌ **no regex signature exists** |
| **B7** | All-mocks + trivial assertion | 28 | unverified | ❌ non-zero; detector FP-prone |
| **B8** | Internal/private method tests | 12 | **12 in 10 files** | ❌ blocked on a PRODUCTION refactor — see correction below |
| **B9** | Pass-through wrapper tests | — | — | ❌ undetectable from test source alone |
| **B10** | Coverage-fillers | 247 | **1** | ❌ 1/247 true-positive rate |
| **B11** | Language-feature redundancy | 37 | **~0** | ❌ detector matches behavioral assertions |
| **B12** | Snapshot of trivial output | 9 | **0** | ✅ **ARMED** |
| **B13** | Names without scenario+expected | 15 – 1,466 | n/a | ❌ owner-ruled; threshold undefined |
| **B14** | Exhaustive-switch coverage | 3 | **~0** | ❌ weak signature |
| **B15** | Setup:assertion > 10:1 | 1,220 | n/a | ❌ metric miscalibrated |
| **B16** | Pure auto-property round-trip | 4 | **3** | ✅ **ARMED** — migrated here |
| **B17** | Generated-code field-by-field | 0 | **0** | ❌ **not applicable** — no mapper library |

Corpus: **932 test files, 9,694 `[Fact]`/`[Theory]` methods**, comments stripped.

---

## The three armed here

### B3 — DI-registration assertions · 5 hits → 1 true

`PipelineHealthTests.Services_Should_Be_Registered_Correctly` resolved four services and asserted each
non-null. Textbook B3, deleted. The other three tests in that class are real HTTP assertions and stay.

The other four hits were **one row in `PhaseAVerticalSliceTests`**, where `NotBeNull` is a fluent
*unwrap* to reach `.Subject` and the real assertion follows:

```csharp
.GetService<IServiceProviderIsService>()
    .Should().NotBeNull("DI provider exposes registration introspection").And.Subject;
```

That is round 5's defect 10 — chained `.And.` continuations — reappearing in enforcement rather than
in classification. The guard requires the statement to **end** at the assertion, so it stays off this row.

**The guard deliberately ignores `BeOfType<NullFoo>()` and `BeNull()`.** For a feature-gated service the
contract is *which* implementation resolved, not that something did — root CLAUDE.md §10 bullet 6 and
ADR-032. A B3 rule that swept those in would be attacking the mechanism ADR-032 prescribes. Both shapes
are pinned as positive controls.

### B12 — trivial snapshots · 9 hits → 0 true

Every hit was a false positive, and the *shape* of the falseness set the detector:

- **7 × `.ToString()` on a value with a real contract** — an `X-RateLimit-Limit` header, a `StringBuilder`
  accumulating streamed tokens, a `Uri` proving `TargetMode=External`. None snapshot a framework-generated
  `ToString()`; they assert values the code is responsible for.
- **2 × `Serialize(a).Should().Be(Serialize(b))`** in `NdaReviewFanOutSeamTests` — structural equality
  between two **live** values (render-follows-store, ADR-040). That is the opposite of a snapshot.

B12 bans pinning output against a **hard-coded literal of the framework's default format**. So the armed
detector requires a string literal, and the `.ToString()` arm was dropped entirely. Narrower than the ADR's
prose, and deliberately: the discarded breadth was 100% false positives.

### B16 — auto-property round-trips · 4 hits → 3 true

`OpenAiClientConfigurationTests` — three `[Theory]` methods, 12 cases, each assigning one auto-property and
asserting it read back. Deleted.

The names promised more than the bodies delivered: `MaxOutputTokens_AcceptsValidRange` asserted no range
(none is enforced on the options type), `SummarizeModel_AcceptsAnyDeploymentName` asserted no
deployment-name rule. **Nothing regressed when they were removed because they constrained nothing.**

The fourth hit, `VisualizationOptions_DefaultValues_AreCorrect`, is **kept**. It asserts six real defaults
(`Threshold == 0.65f`, `Limit == 25`, …) and merely happens to read back one value it set. That forced the
rule to be **method-scoped**: fire only when *every* assertion in the method is a round-trip. That test is
now a positive control, so the guard can never grow into it.

---

## The twelve that stay unenforced

Grouped by *why*, because the reasons are not interchangeable and the remedies differ.

### No signature exists (B5, B6, B9)

**B6 — mirror tests.** The ban is "the test asserts the implementation does what it does." That is a
statement about the *relationship* between two bodies of code, and it has no lexical form. `GetName()`
returning `Name` and `CalculateTax()` returning a computed value are textually identical shapes. Detecting
B6 requires comparing each test against the production method it exercises — that is a compiler and a call
graph, not a regex. Plausibly the largest real bucket and the least tractable. Shipping a weak B6 detector
would be worse than none: it would fail honest tests while missing the category.

**B5** turns on "when an in-memory test double + real integration boundary is **cheaper and more honest**"
and **B9** requires knowing the production method is a one-line delegation — invisible from test source.

These three belong to `/test-diet` judgment. That is not a dodge; it is the correct routing, and it is why
`/test-diet` exists.

### Detector exists, but its output is mostly noise (B7, B10, B11, B14)

**B10** is the cautionary one: 247 rows, **1** true positive after per-row verification (round 5). The other
246 asserted real behavior through forms the classifier could not see — assertions delegated to helpers,
assertions inherited from base classes, expression-bodied methods, chained `.And.` continuations.

**B11** (37) matches `.Should().Be(new Foo(...))` — comparing a result to an expected value, which is
behavioral. B11's actual target is constructing *two identical* values and asserting equality, i.e. testing
the compiler. **B7** (28) and **B14** (3) fail the same way.

Arming any of these means arming a rule whose failures would mostly be wrong. A guard that cries wolf gets
suppressed, and then the real violations ride in behind it.

### Blocked on a production refactor (B8) — **corrected 2026-08-30**

> **Correction.** This section previously read *"7 call sites in 5 files … the strongest candidate for the
> next arming pass … five files is a tractable slice."* **Both halves were wrong.** A precise re-census
> found **12 call sites across 10 files**, and the migration is not tractable as described. Recorded here
> rather than quietly amended, because the original wording would send someone into a "quick" pass that
> is not quick.

`GetMethod("literal", BindingFlags.NonPublic).Invoke(...)` — **12 call sites in 10 files**:

| Private member invoked | Test file |
|---|---|
| `CommunicationCreateTaskAi.ParseResult` | `contract/Eval/CreateTaskFromEmailEvalTests.cs` |
| `CommunicationProposeAi.ParseResult` | `contract/Eval/ProposeFieldUpdatesEvalTests.cs` |
| `CommunicationTriageAi.ParseResult` | `contract/Eval/TriageEmailEvalTests.cs` |
| `WorkspaceFileEndpoints.HandleSummarize` | `Sprk.Bff.Api.IntegrationTests/Phase1StableIdMigrationSuite.cs` |
| `ChatEndpoints.ValidateAttachments` + `.ComposeMessageWithAttachments` | `Api/Ai/ChatEndpointsAttachmentsTests.cs` |
| `DailyBriefingEndpoints.HandleNarrate` | `Api/Ai/DailyBriefingEndpointsTests.cs` |
| `DailyBriefingEndpoints.HandleNarrate` | `Api/Ai/DailyBriefingResponseShapeTests.cs` |
| `AppOnlyAnalysisService.ResolvePlaybookAsync` | `Services/Ai/AppOnlyAnalysisServiceResolveTests.cs` |
| `AgentToolCatalogProjector.TryParseChatSessionId` + `.TryParseMatterId` | `Services/Ai/Chat/SprkChatAgentFactoryToolResolutionTests.cs` |
| `Sanitize` | `Services/Ai/Handlers/LegalResearchHandlerTests.cs` |

(The broader `BindingFlags.NonPublic` count is 34 across 21 files, but most read private *fields* for
fixture setup or call `GetMethod("Clear")` on a runtime type — neither is what B8 bans. Filtering those out
is why the precise count is 12, not 34.)

**Why it cannot simply be migrated.** ADR-038 B8 bans internal-method tests *"via `InternalsVisibleTo` **or**
reflection"* — so making these members `internal` is **not** a compliant fix; it is the same ban under a
different spelling. The only compliant route is testing through the public surface, which means:

- for the three `ParseResult` parsers — **extract the parser into a public type** so it has a real surface;
- for `HandleNarrate` / `HandleSummarize` / `ValidateAttachments` — **exercise the endpoint over HTTP**,
  converting fast unit tests into slower integration tests.

Both are **production refactors across multiple subsystems**, not test edits. That is well outside a CI
remediation project, and doing it badly would trade a documented ban violation for a worse test suite.

**Honest verdict: B8 stays unarmed, and it is NOT a quick win.** The right sequencing is a production
change that gives this logic a public surface — at which point the tests migrate naturally and the guard
arms for free. Until then the ban is documented with an exact, per-call-site inventory (above), which is
the most useful thing this project can leave behind for it.

### The threshold is undefined (B13, B15)

**B13** is the clearest case for not guarding, and the number makes the argument:

| Reading of "name describes behavior" | Non-conforming |
|---|---:|
| Missing `Method_Scenario_ExpectedResult` (3 parts) | **1,466** of 9,588 |
| Missing even one underscore | **15** of 9,588 |

Two orders of magnitude apart, on a threshold nobody has fixed. A guard would be enforcing the threshold,
not the ban. The owner has already ruled that **a bad name is not grounds for deletion**; rename-over-time
via `/test-diet` is the sanctioned path.

**B15** (1,220 by line ratio) has the same defect plus a known miscalibration: `tests/CLAUDE.md` exempts
`tests/Spaarke.ArchTests/**`, where a source scan over the whole server tree *is* the arrange block and a
high ratio is inherent. Counts above exclude ArchTests (106 methods) for exactly this reason.

### Not applicable (B2, B17)

**B17** targets AutoMapper field-by-field tests. **AutoMapper is not a dependency of this repo** — zero
references in `src/`. Arming a guard against a library we do not use is guard-theater; it would sit green
forever and teach nothing.

**B2** names `Mock<IServiceClient>`. `IServiceClient` **does not exist** either — its 3 grep hits are all
prose, two of them comments asserting B2 compliance. (The same comment-vs-code confusion that produced
#864's "24 files" figure, which was really 0.) The ADR's broader clause, "or other typed HttpClient
wrappers **when they hide the same antipattern**", is judgment-gated by construction.

The near-miss shapes were checked and are not violations: `Mock<IHttpClientFactory>` appears 4× in 2 files,
and in `PlaybookExecutionTests` the mock is a **dead local never passed to the executor**, while in
`RecordSyncJobTests` it is an unstubbed constructor dependency. Neither hands back an `HttpClient` over a
fake handler, which is the antipattern B2 exists to catch.

---

## Carried forward unchanged: the B1-adjacent gap

**13 files subclass `HttpMessageHandler` / `DelegatingHandler` directly.** That is B1's coupling under a
different spelling, and it remains **reported to #864, not guarded** — the same decision PR #865 made.

ADR-038 B1 names the *mock*, not the subclass, and subclassing is the conventional way to exercise an
outbound HTTP boundary in a seam test. Guarding it would fail 13 files on a rule the ADR does not state.
Widening a ban is an ADR amendment (root CLAUDE.md §6.5 path B), not a test-file edit.

---

## Verification

1. **Detector self-test before measuring** — each detector was run against seeded violations (must fire)
   and against the sanctioned shape (must stay quiet) *before* being trusted over the tree. A zero from a
   too-narrow detector manufactures confidence, which is worse than no guard.
2. **Every row read** for the small counts (B2 4, B3 5, B12 9, B16 4, B8 7). No sampling.
3. **End-to-end seeding** — real B3/B12/B16 violations were written into `tests/unit/Sprk.Bff.Api.Tests/`
   and all three guards went red; exactly those three, nothing else. Probe removed, suite green again.
   This is the step #839 skipped: regex-level controls prove the *pattern*, seeding proves the *path*.
4. **Armed with the exact filter string** — the `--filter` value was extracted from
   `ci-tier1-blocking.yml` by `grep` and run verbatim: **22 → 25 tests selected**. Verified against the
   string in the file, never against the class name.

---

## Deletion safety (FR-B06 / FR-B10)

Both edited files are **outside the 8 KEEP paths**, so no same-PR replacement is owed:

| File | Path class | Removed |
|---|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/PipelineHealthTests.cs` | not a KEEP path | 1 method |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/OpenAiClientTests.cs` | not a KEEP path | 3 methods (12 cases) |

Each deletion leaves a comment at the site recording what was removed, why, and what to test instead —
so the next reader finds the reasoning where the code used to be, not only in this note.

---

## What this leaves open

- **B8 is the next arming pass** — tight signature, 5 files, needs a per-call-site visibility decision.
- **B6 needs `/test-diet` judgment**, permanently. No mechanical path exists.
- **The B1-adjacent subclass gap** awaits an owner call on #864.
- **B13/B15 thresholds** stay undefined by choice; defining one to enable a guard would be the ratchet
  ADR-038 §3 removed, wearing a different hat.
