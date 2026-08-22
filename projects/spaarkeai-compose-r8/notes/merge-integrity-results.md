# COMMENT RANGES, REVISION IDS AND DOCUMENT REPAIR — task 042 (FR-A11)

> **Verified** 2026-08-22 · Harness:
> [`tests/integration/seam/Compose/ComposeMergeIntegrityTests.cs`](../../../tests/integration/seam/Compose/ComposeMergeIntegrityTests.cs)

These are the failures that make a merge **look correct and be wrong**: content compares equal, the
preservation number reads 100%, and Word offers to repair the file on open.

---

## Headline: 042 needed almost no production code, and one real defect turned up anyway

Four of its five acceptance criteria were already satisfied **structurally** by the task-040 merge — not
mitigated, but made impossible. That is worth stating precisely, because "the tests passed immediately" is
also what a vacuous test suite looks like, and two of these very nearly were.

| Criterion | Status | Why |
|---|---|---|
| Model-side comment anchors suppressed for cloned blocks | ✅ by construction | A cloned block is **never rendered from the model**, so there is no second emission to suppress |
| Revision-id seed computed from cloned ids | ✅ already | `ScanCarrierRevisionIdSeed` reads the carrier — the source of every cloned id — and mints above it. Computed, not a fixed offset |
| Cross-boundary comment ranges stay well-formed | ✅ verified both directions | Purpose-built fixture; start-in-clone/end-in-render and the reverse |
| Duplicate paraIds cannot mis-clone | ✅ by construction | The merge **never resolves a paraId**. Alignment is a longest common subsequence over block CONTENT, so a duplicate id has nothing to act on. The POML's consume-in-document-order scheme with a dup-detection fallback is unnecessary |
| FR-G05 actual document open | ✅ **runs** | Headless LibreOffice opens four merged documents and the extracted text still contains the user's edit |

---

## The defect adding a fixture found

**The op-log patch path was re-serializing `word/comments.xml` on every save**, breaking the
"every untouched package part is byte-identical" guarantee its own corpus harness asserts.

`EnumerateExistingIds` needed comment ids for collision avoidance and reached them through
`commentsPart.Comments` — which **materializes the part's SDK DOM**, marks it dirty, and makes the SDK
rewrite it on dispose. Nothing was edited; the bytes changed anyway.

It went unnoticed for the whole project because **no corpus fixture had a comments part**. It surfaced the
moment one existed, failing 8 tests across four seam suites.

**Fixed** by reading the ids straight off the part's XML stream with `XmlReader`, never touching its DOM —
the same read-only-side-open discipline the renderer already applies via `ScanCarrierComments`. A malformed
comments part yields no ids rather than failing the save (ADR-049 invariant 1).

---

## The corpus gap this closed

The comment-integrity sweep ran green across all 18 corpus documents on its first execution. Then it printed
the number of comment ranges it had actually examined:

```
01 - Test Matter Create Fields Only.docx: 0 comment range(s)
alternate-content-duplicate-paraid.docx: 0 comment range(s)
… all 18 documents: 0 comment range(s)
```

**Zero.** FR-A11 is entirely about comment ranges and the corpus contained none, so eighteen green rows were
evidence of nothing. New fixture
[`comment-ranges-multiparagraph.docx`](../../../tests/fixtures/compose-corpus/comment-ranges-multiparagraph.docx),
authored by a checked-in generator, carries three shapes deliberately:

1. A range **spanning two paragraphs** — the clone/render boundary case.
2. A **point comment** inside one paragraph — start and end adjacent.
3. A **second multi-paragraph range**, so two ranges are open at once and an implementation tracking only
   "the current range" is caught.

Its first version was **schema-invalid** — `w14:paraId` values of 7 hex digits where `ST_LongHexNumber`
requires 8 — and the corpus-validity test added earlier in task 041 caught it before it could distort
anything. That test paying for itself within a day is the argument for keeping it.

---

## Two near-vacuous tests, corrected

Both would have passed forever while proving nothing.

**Revision ids.** The first run reported `0 revision id(s)` for the PAT document — whose *filename* says
"track changes" — and passed, because uniqueness over an empty set is trivially true. Inspecting the source
settled it: that document contains **zero** `w:ins` / `w:del` / `w:pPrChange`. Its revisions were accepted
before it was saved; the name describes its provenance, not its markup. The fixture is kept and declared
honestly (`carriesRevisions: false`), and the test now asserts the *real* property on the fixture that does
carry them — `multi-author-redline-synthetic.docx`, **source=7 → merged=7**, all unique. Cloning does not
drop a redline.

**Duplicate paraIds.** The two fixtures carry **different** collisions and only one is visible to a
body-level scan: `alternate-content-duplicate-paraid.docx` duplicates an id *within the body*
(`mc:Choice` + `mc:Fallback`), while `multipart-paraid-collision.docx` repeats one across `document.xml`,
`footnotes.xml` and `header1.xml` — and `paraId` uniqueness is **part-scoped**, so nothing is duplicated
inside the body and the flag is correctly false. The first draft asserted `true` for both and failed loudly
on the wrong property. Each fixture now declares its own shape.

The corpus comment sweep still reports its examined range count on every run, so eighteen green rows can
never again imply eighteen exercised cases.

---

## FR-G05 — an actual document open, not a schema check

The requirement is explicit that `OpenXmlValidator` passing is **not** sufficient evidence for this class.
Headless LibreOffice converts four merged documents and the extracted text is checked for the edit:

| Document | Result |
|---|---|
| `AppligentNDA_Signed.docx` | opened, 3,639 chars extracted |
| `PAT …CLAIMS….docx` | opened, 23,955 chars extracted |
| `content-controls-sdt.docx` | opened, 147 chars extracted |
| `ref-cross-references.docx` | opened, 144 chars extracted |

When LibreOffice is absent the test writes a **loud** skip message rather than passing silently — a repair
check that reads as green without running is how this class of defect ships.

---

## Verification

Full BFF suite **10,889 passed / 0 failed** · publish **43.69 MB** (−1.27 vs the 44.96 MB baseline; ceiling
60) · no new NuGet · `dotnet list package --vulnerable --include-transitive` → **no vulnerable packages**.
