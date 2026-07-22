# Task 004 — Round-Trip Byte-Diff Harness Findings

> **Created**: 2026-07-22 by task 004 (`spaarkeai-compose-r4`)
> **Purpose**: Record the NFR-01 round-trip byte-diff harness results — the acceptance evidence
> the Phase-0 gate (task 006, criterion b) consumes, and the input to the spec Unresolved Question
> ("True byte-identity within `document.xml`").

---

## 1. What was built

`tests/integration/seam/Compose/` (new directory, compiled into the existing
`tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj` via its pre-existing
`<Compile Include="..\..\integration\seam\**\*.cs">` glob — **no `.csproj` or `.sln` edit was
needed or made**):

| File | Purpose |
|---|---|
| `ComposeCorpusFixtureLocator.cs` | Enumerates every `.docx` under `tests/fixtures/compose-corpus/` at xUnit `[MemberData]` discovery time (glob, NOT a hardcoded 3-filename list — per owner feedback, an owner-supplied worst-offender dropped into that directory later is picked up automatically). Also verifies the bytes are LFS-smudged (ZIP `PK` signature check) with an actionable `git lfs pull` error if not. |
| `ComposeOoxmlPackagePartComparer.cs` | The byte-comparison helper. Recursively walks the OPC package-part graph (generic — not a fixed list of typed SDK part properties), classifies `word/document.xml` separately from every other ("untouched") part, and exposes BOTH a byte-identical check and a "structurally faithful" (SDK re-serialize equivalence) check for `document.xml`, gated by a `strictDocumentXmlByteIdentity` switch — the switchable hook the task brief and spec Unresolved Question call for. |
| `ComposeNoOpRoundTripByteDiffSeamTests.cs` | The seam slice itself. `[Theory]` over every corpus doc via `ComposeCorpusFixtureLocator`. Drives the REAL `POST /api/compose/documents/{id}/save` route through `WebApplicationFactory`, reusing the existing `ComposeFidelitySeamFixture` (from `tests/integration/seam/Ai/ComposeFidelitySeamTests.cs`) rather than duplicating ~150 lines of host config — per root CLAUDE.md §11 (reuse over new components). Captures the persisted bytes at the `ISpeFileOperations.ReplaceFileContentAsUserAsync` facade boundary. |

## 2. Scope of THIS task (explicit)

`ComposeShadowPatchEngine` does not exist yet (task 030). This harness proves the **no-op /
retained-original byte-preservation invariant**: load a corpus doc → save with an **empty
operation log** (no `editedParagraphs`, no `annotations`, no `contentModel`) → assert the
persisted bytes equal the retained original.

This is the **existing** retained-original / clean-save passthrough path already in
`Services/Compose/ComposeService.cs` — no production code changed for this task. Per that file's
own remarks (`SaveAsync`): *"An empty edit list is a structural round-trip (no revisions) → a
clean Save stays byte-identical... A no-edit, no-annotation Save persists the baseline
byte-identical (FR-06a byte-identity preserved)."* The harness is the through-the-wire proof of
that comment.

**TODO (task 034)**: once the Patch Engine lands, add a sibling seam test that saves with a
NON-EMPTY operation log and reuses `ComposeOoxmlPackagePartComparer` unchanged — untouched-part
assertions stay identical; only `document.xml` legitimately diverges from the original there
(the edit itself). That is where the strict-vs-structural switch decision actually matters.

## 3. Results — all 3 seed corpus docs, PASS

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj \
  --filter "FullyQualifiedName~ComposeNoOpRoundTripByteDiffSeamTests"

Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 70 ms
```

| Corpus doc | Untouched parts byte-identical (strict) | `document.xml` byte-identical (strict) | `document.xml` structurally faithful (loose) | Whole-file byte-identical |
|---|---|---|---|---|
| `PAT 109270W-1 - CLAIMS track changes vs US12470413 claims(206092900.1).docx` | ✅ | ✅ | ✅ | ✅ |
| `Engagement Letter.docx` | ✅ | ✅ | ✅ | ✅ |
| `01 - Test Matter Create Fields Only.docx` | ✅ | ✅ | ✅ | ✅ |

No escalation trigger fired. Every corpus doc round-trips byte-identically on both untouched
parts AND (at this no-op-scope, stronger-than-eventually-required bar) `document.xml` itself —
confirming today's Compose save path already satisfies NFR-01 for the no-edit case across all 3
seed fixtures, including the CIPO doc (which task 002's corpus-manifest.md flags as carrying real
SDT/footer content and multi-header/footer parts — exactly the "never opened" surface this harness
targets).

A sibling regression check (`ComposeFidelitySeamTests` — the existing dirty-save + born-in-editor
seam tests in `tests/integration/seam/Ai/`) was run alongside the new tests to confirm the shared
fixture reuse introduced no regression: 5/5 passed (2 existing + 3 new).

## 4. Input to the spec Unresolved Question ("True byte-identity within `document.xml`")

Today's evidence: for the no-op case, `document.xml` is byte-identical, not merely structurally
faithful — because `ComposeService.SaveAsync` never re-serializes it when there is nothing to
edit. This does NOT yet answer the Unresolved Question for the REAL edit case (task 034), where a
Patch Engine that opens and re-serializes `document.xml` to apply a surgical edit may legitimately
produce non-byte-identical-but-structurally-equivalent XML (e.g. attribute-order or whitespace
differences from re-serialization). The comparer's `strictDocumentXmlByteIdentity` switch is built
and proven functional (both modes independently pass on this no-op case) so task 034/006 can flip
it once real edits are available to test against.

## 5. Publish-size impact

None — this task added test files only under `tests/integration/seam/Compose/`; zero files under
`src/server/api/Sprk.Bff.Api/` were touched (`git status --porcelain` confirms no `src/` changes
from this task). No `dotnet publish` re-measurement was needed (harness-only, size-neutral per the
task's own constraint statement).

## 6. Consumers

- Task 006 (Phase 0 gate) — criterion (b): corpus byte-diff harness green, consumed directly.
- Task 034 (Patch Engine seam slices + corpus proof) — extends this harness with a non-empty
  operation log; reuses `ComposeOoxmlPackagePartComparer` and `ComposeCorpusFixtureLocator`
  unchanged.
- Task 061 (post-cutover corpus proof) — re-runs this same harness after the hard-replace.
