# #699 — advisory-comment anchoring: what I found and what I built

> 2026-09-01 · `work/spaarkeai-compose-r8`

## The reported defect is already fixed — verified, not assumed

[#699](https://github.com/spaarke-dev/spaarke/issues/699) reports
`ComposeEditor.placeAdvisoryComments` returning `placed=2` where `1` was expected: *"a should-be-ambiguous
target materializes a comment instead of being reported `not_found`/`ambiguous`."*

That was closed on master by **`ai-advanced-capabilities-agreements-r1`**, in two parts:

| Part | Task | What it did |
|---|---|---|
| Precision | 012 | `resolveAdvisoryAnchorSpan` REPORTS `ambiguous` instead of silently taking the first occurrence — for the exact text AND for the distinctive-prefix retry |
| Determinism | 011 | `resolveDeterministicAnchorSpan` anchors by `sectionRef` → `CitationResolver` → paraId BEFORE any text search — the issue's own recommendation |

**Verified on this tree**, not inferred from the commit log: the DEF-01 scenario is exercised by
`ComposeEditor.advisoryComments.test.tsx` ("a unique target resolves…; not_found/ambiguous targets are
reported, not dropped"), which asserts `placed=1` with `kind: 'ambiguous'` on the twice-occurring
target. It passes — 18/18 across that file and its paraId sibling.

## What was still open, and why it is the same defect

The issue's fix depends on a **client mirror**: `composeCitationResolver.ts` reimplements the server
`CitationResolver.cs` in TypeScript, because there is no way to call a pure C# function from a browser
and a `resolve-citation` endpoint would add per-finding latency for data the projection payload already
carries. That reasoning is sound and stays.

What the mirror lacked was any mechanism keeping the two in step. Parity rested on **ported test cases
and `@see` comments** — two hand-kept copies of the same expectations, which by construction cannot
detect drift between themselves: change one parser without touching the other and both suites stay
green, because each only checks its own copy.

**Why that is #699 and not a tidiness concern.** `placeAdvisoryComments` tries the deterministic leg
first. When the client cannot parse a citation the server (and therefore the review model's vocabulary)
can, that leg returns `null` and the finding **falls through to text search** — which is where a note
lands on the wrong clause. The gap does not degrade into "slightly less precise"; it reopens the defect
through a side door, and it does so precisely for the findings that were cited well enough to be safe.

## Two mechanisms, deliberately different in kind

Neither is sufficient alone, which is why both exist.

**1. Behaviour — one corpus, executed by both resolvers.**
`tests/fixtures/compose-citation-parity/cases.json` holds 45 cases over 5 reference maps, covering
every shape: single label, sub-item (letter/roman/decimal/numeric-token), contiguous range (hyphen, en
dash, em dash, descending), bullet exclusion, section-sign stacking, and the malformed-input table.
Read — never imported — by both:

- `tests/integration/seam/Compose/ComposeCitationParityCorpusTests.cs`
- `src/client/shared/…/widgets/composeCitationResolver.parity.test.ts`

Both locate it by walking up for the repo-root marker, the same resolution
`ComposeCorpusFixtureLocator` already uses, so moving either project cannot silently unhook the halves.

**Result: the two parsers agree on all 45 cases today.** No divergence exists — which is worth stating
plainly, because "we built a detector" and "we found a bug" are different claims and only the first is
true here.

**2. Surface — the drift detector.**
`tests/Spaarke.ArchTests/ComposeCitationResolverParityGuardTests.cs` pins the three things a
behavioural corpus cannot catch on its own, because nobody adding a shape to one parser is obliged to
add a case for it:

| Rule | Catches |
|---|---|
| Leading-label vocabulary set equality | `"recital"` added to one side's strip list |
| `CitationShape` set equality (casing normalized) | a shape classified by one parser only |
| Range-separator character set equality | a side that accepts `-` but not `–`/`—` |

## Verification

| Control | Result |
|---|---|
| In-test negative controls (seeded synthetic sources) | fire on all three rules |
| In-test positive controls (equivalent sources in each language's idiom) | do **not** fire — a guard that flags the code it protects gets deleted rather than obeyed |
| **D** — `"recital"` added to the SERVER vocabulary only | vocabulary rule failed ✅ |
| **E** — em dash removed from the CLIENT range parser only | separator rule failed ✅ **and** the behavioural corpus failed its em-dash case ✅ |

**The negative control caught a real defect in my own detector.** `ExtractServerShapes` was anchored to
line starts, so it found only the first member of a single-line enum — meaning a future reformat of the
real enum onto fewer lines would have silently shrunk the detected set and disarmed the rule while it
still reported green. Fixed by splitting on commas after stripping doc comments, which is what the
control was for.

Suites: client **1,381 / 1,381** (105 suites) · ArchTests **181 / 181** · BFF parity cases **45 / 45**.

## What this does NOT cover

- **Reverse resolution.** The server's `ResolveCitation` (paraId → canonical number) is deliberately not
  mirrored client-side; `clauseLocation.ts` answers that question a different way. The corpus covers
  forward resolution only, matching the mirror's declared scope.
- **The numbering ENGINE.** These cases resolve citations against in-memory reference maps. Whether the
  engine derives `4.2(b)(iii)` from a real Word `numbering.xml` is the open corpus-fixture item (§R3
  row 2) and still needs an owner-supplied letter/roman document.
