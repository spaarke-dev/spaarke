# Test diet report — email-communication-solution-r4

**Run date**: 2026-07-28
**Branch**: work/email-communication-solution-r4
**Classifier**: ADR-038 §7 build-vs-maintain (17 bans B1–B17)

## Scope note (read first)

This project's branch and `master` are heavily intertwined (multiple merges in both
directions across the R4 UAT arc), so `merge-base(master, HEAD)` does **not** cleanly
mark the project start — a naive `{start}..HEAD` range would either be empty or capture
unrelated projects' tests. The diet was therefore scoped two ways:

1. **Direct session delta** — the C# test files this closing work-arc actually touched
   (the deterministic name/number-match rung tests). Classified per-method.
2. **Evidence-based ban sweep** — a signature scan of the project's signature C# test
   area (`tests/unit/Sprk.Bff.Api.Tests/Services/Communication/**`, 66 files) for the
   mechanical bans (B1/B3/B4/B7/B8/B9/B13), to catch scaffolding accreted across earlier
   waves without hand-reading 66 files.

Client-side jest tests (`src/client/**/__tests__/*.tsx|ts`) are **outside** this skill's
C#-scoped enumeration (`tests/**/*.cs`). The composer test deltas were validated in the
same-session `/code-review` and by the 154/154 passing EmailComposer suite.

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (KEEP, confirmed) | 25 methods (2 files) direct + 64 files swept clean | confirmed |
| SCAFFOLDING (DELETE candidate) | 0 | — |
| AMBIGUOUS (reviewer judgment) | 0 delete-worthy (see notes) | — |
| PATH-VIOLATION-PROTECTED (KEEP-path migration) | whole Communication tree (66 files) | future `git mv`, NOT delete |

## Ban-signature sweep (66 Communication test files)

| Ban | Signature searched | Hits | Verdict |
|---|---|---|---|
| B1 | `Mock<HttpMessageHandler>` | 0 | clean |
| B3 | `GetRequiredService<…>().Should().NotBeNull` / `Assert.NotNull(services…)` | 0 | clean |
| B4 | `Throws<ArgumentNullException>(() => new …)` | 0 | clean |
| B13 | `void Test\d` / `_Works()` naming | 0 | clean |
| B8 | `BindingFlags.NonPublic` / `InternalsVisibleTo` / `GetMethod(` / `.Invoke(` | 1 | `.Invoke(` in `InboundPipelineTests.cs` — inspected as a delegate/handler call, not reflection into a private member. Not a B8 violation. |
| B7/B9/B15 | `Verify(… Times.Once/Never/Exactly)` | 51 across 19 files | Density ~2.7/file, paired with state/behavior assertions (not the "all-mocks + Verify-only + ≤2 asserts" shape). Normal for a service-heavy suite. No delete recommendation; not flagged AMBIGUOUS. |

Interpretation: the Communication C# suite is **clean of the mechanical scaffolding bans**.
No `git rm` recommended.

## Direct session delta — classified MAINTAIN

| File:scope | KEEP rationale | Ban check |
|---|---|---|
| `RecordNameMatchRungTests.cs` (16 `[Fact]`) | Behavioral: runs the REAL deterministic name/number verification logic over a mocked `IRecordMatchingAi` **boundary** (ADR-038-sanctioned boundary mock). Protects the email-r4 UAT contract (exact-name match, ref-number recall #7, surface-all, quote neutralization). `{Method}_{Scenario}_{ExpectedResult}` naming throughout. | passes B1–B17 |
| `ContactNameMatchRungTests.cs` (9 `[Fact]`) | Behavioral over a mocked `ICommunicationDataverseService` boundary; precision-guard + Suggested-band + location-tiering + (session add) provenance-parseability. Proper naming. | passes B1–B17 |

## Delete commands

None. No scaffolding-class tests identified.

## Path-move (PATH-VIOLATION-PROTECTED) — repo-wide, not email-r4-specific

Per the classifier's path-check (heuristic 1), the entire
`tests/unit/Sprk.Bff.Api.Tests/Services/Communication/**` tree sits **outside** the 7
ADR-038 KEEP paths (`tests/unit/domain/**`, `tests/integration/{auth,regression,
data-mutation,tenant,contract,seam}/**`). These are behavioral tests with **no same-PR
replacement**, so they are **PATH-VIOLATION-PROTECTED** (keep + migrate), never delete.

This is **pre-existing, repo-wide debt** from before the task-050 KEEP-path
reorganization — it is not introduced by email-r4 and should not be resolved piecemeal
inside this project's close. Recommended handling: a dedicated `git mv` sweep of the BFF
unit-test tree into the KEEP taxonomy (most rung/service tests → `tests/integration/…`
or a domain path), tracked as its own follow-up rather than an email-r4 wrap-up action.
(Same observation was raised as W2 in this session's `/adr-check`.)

## Count delta

- Scaffolding deletions recommended: **0**
- Path migrations (protected, future sweep): whole Communication tree (out-of-scope for this close)
- Net post-diet expected count: **unchanged** (no deletions)

## Verdict

email-r4's test deltas are **maintain-class and ban-clean**. No diet deletions. The only
open item is the repo-wide KEEP-path taxonomy migration, which is pre-existing and should
be tracked separately.

## Industry citation

Build-vs-maintain per ADR-038 §7 (Beck "delete the scaffolding"; Feathers
characterization-vs-behavior; Google test-sizes; DHH less-tests). Classifier B1–B17.
