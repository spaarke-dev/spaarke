# Patch-Engine A/B Decision — Docxodus vs build-on-OpenXML-SDK (FR-04 / FR-11)

> **Task**: `spaarkeai-compose-r4` task 005 (Phase-0 applier spike + patch-engine A/B)
> **Date**: 2026-07-22
> **Status**: DECIDED — **build on the Open XML SDK** (Candidate B). Docxodus is rejected on *fit first*, size second.
> **Gate impact**: Phase-0 gate (task 006) can pass on this evidence — at least one engine (B) lands interior
> edits at the correct `paraId + offset` with zero write-path text-search, on the CIPO worst-offender doc, at
> 0 `OpenXmlValidator` errors. **No escalation of the "neither engine works" trigger.**

---

## TL;DR recommendation

**Build the FR-04/FR-11 `ComposeShadowPatchEngine` (task 030) directly on `DocumentFormat.OpenXml` 3.5.1 (already a
BFF dependency — zero new runtime package).** Do **not** adopt Docxodus.

The decision is not close, and it is driven by **architectural fit**, not just size:

- **Docxodus is a whole-document *comparison* engine (`WmlComparer.Compare/Consolidate/GetRevisions`), not an
  offset-addressed *applier*.** It infers tracked changes by diffing two full documents. Our model applies an
  ordered operation log at `(paraId, runIndex, run-local-offset)` with **zero text-search** (invariants I-3, I-7;
  D2). To use Docxodus we would have to first materialize a whole "modified" document (which requires an applier —
  the very thing we are choosing) and then let WmlComparer *re-derive* the diff by fuzzy text alignment. That
  re-derivation **is** the text-search anchoring the architecture forbids.
- **Docxodus/WmlComparer was already empirically disqualified by R3.** Per the BFF csproj comment
  (`Sprk.Bff.Api.csproj` lines 153–159), the R3 NFR-09 hardening gate proved WmlComparer **strips `w14:paraId`
  and drops tables** on real firm templates in both the 6.4.0 (net8) and 7.1.0 (net10) lines. Stripping
  `w14:paraId` destroys our stable-addressing invariant (I-3) outright — the anchor the whole architecture rests on.
- **Candidate B works today.** The spike lands an interior `insertText` (native `w:ins`), an interior
  `deleteRange` (native `w:del` / `w:delText`), and a para-mark-deletion probe (`w:pPr/w:rPr/w:del`) on the CIPO
  doc, resolving every anchor by `w14:paraId` dictionary lookup + run-index walk — **no text-search** — at
  **0 validator errors**.

---

## What was actually run (evidence)

**Spike location** (throwaway, NOT in `Spaarke.sln`, NOT wired to any save path):
`projects/spaarkeai-compose-r4/spike/ComposeApplierSpike/` — a standalone net8 console that **links the real
task-003 schema** (`Services/Compose/Operations/ComposeOperation.cs`, not a copy) so the op set is a genuine
fit check.

**Corpus doc**: `tests/fixtures/compose-corpus/PAT 109270W-1 - CLAIMS track changes vs US12470413
claims(206092900.1).docx` (CIPO — the Phase-0 worst-offender; 108 paragraphs, footer page-number SDT, empty-para
history, track-changes-clean as saved per corpus manifest note 1a).

**Op set applied** (built against the real `ComposeOperationLog`; hand-built, no AI — ADR-013/NFR-05):

| # | Op | Anchor (paraId, runIndex, run-local offset) | Verified landing |
|---|---|---|---|
| 1 | `insertText " AMENDED"` | `712269E5`, run[0] @ off 16 (after "5. The method of") | native `w:ins`; preceding run split exactly at "…of" |
| 2 | `deleteRange` "asset information " | `410C9E5F`, run[2] @ [1,19) | native `w:del` w/ `w:delText`; exact span |
| 3 | `deleteParagraph` (para-mark probe) | `5D98777E` | content `w:del` + `w:pPr/w:rPr/w:del` on the para mark |

**Candidate B result** (`dotnet run -c Release`): all placement checks PASS, `OpenXmlValidator`
(Office2019) = **0 errors**, output 27,827 bytes (input 27,417). Verification resolves the paragraphs by
`w14:paraId` and inspects the DOM — **zero text-search on the verify side too**.

```
[PASS] insertText -> w:ins " AMENDED" inside paraId 712269E5
[PASS]   split boundary: preceding run ends at offset 16 ("...of")
[PASS] deleteRange -> w:del "asset information " (w:delText) inside paraId 410C9E5F
[PASS] para-mark deletion -> w:pPr/w:rPr/w:del on paraId 5D98777E
[PASS] OpenXmlValidator: 0 error(s)
```

**Candidate A (Docxodus) evaluation**: API + footprint analysis in isolation (reflection over the cached
`Docxodus.dll`, and an isolated `dotnet publish`). Docxodus was **not** driven against the op set because it has
**no offset-addressed surface to drive** — its public redline entry point is `WmlComparer.Compare(orig, mod,
settings)` / `Consolidate` / `GetRevisions` over two whole documents.

---

## Comparison

### 1. Fit with our operation schema — DECISIVE

| Dimension | Docxodus (`WmlComparer`) | Build-on-OpenXML-SDK (Candidate B) |
|---|---|---|
| Input model | Two **whole documents**; infers changes by diff | An **ordered op log** `{type, paraId, runIndex, offset}` |
| Anchoring | Fuzzy text/atom alignment (`ComparisonUnitAtom`) — a text-search by another name | `w14:paraId` O(1) dict + run-index walk — **no text-search** |
| Honors `w14:paraId` | **No** — strips it (R3 NFR-09 gate) | **Yes** — it is the primary key |
| Split-run at an offset | Not exposed; internal to the differ | **Yes** — `Run.Clone()` split preserving `RunProperties` |
| Emits `w:ins`/`w:del` at a point | Only as a byproduct of a full diff | **Yes** — `InsertedRun` / `DeletedRun` exactly at the anchor |
| Para-mark deletion edge | Opaque | **Probed** — `w:pPr/w:rPr/w:del` emitted + validates |
| Tables on real templates | **Dropped** (R3 gate) | Untouched subtrees preserved (byte-surgical) |

**Verdict**: Docxodus fails the fit test outright. It answers a *different question* ("what changed between two
docs?") than the one R4 asks ("apply THIS op at THIS anchor, touching nothing else"). Adopting it would reintroduce
the exact text-search/whole-doc-regeneration failure mode the Shadow Architecture exists to kill (I-1, I-4, I-7).

### 2. Fidelity on the CIPO doc

- **Candidate B**: interior edits land at the intended `paraId+offset`; untouched paragraphs are left structurally
  intact (surgical DOM mutation, never a string-edit of `document.xml`); footer SDT / numbering parts untouched;
  0 validator errors → opens clean in Word with real Accept/Reject revisions.
- **Docxodus**: not fidelity-testable in our workflow because it can't be pointed at an anchor. Independently, the
  R3 gate already recorded paraId-stripping + table-drops on firm templates — a fidelity failure against I-3.

### 3. Publish-size delta (NFR-04 / root §10) — measured in isolation

Isolated `dotnet publish -c Release -r linux-x64` (the BFF App Service RID), framework-dependent, bare console:

| Variant | Publish size (uncompressed) |
|---|---|
| Baseline (`DocumentFormat.OpenXml` 3.5.1 only) | 8.40 MB (8,696,218 B) |
| + `Docxodus` 6.4.0 | 22.0 MB (22,227,792 B) |
| **Delta** | **+12.9 MB (13,531,574 B) uncompressed** |

The delta is dominated by **SkiaSharp native assets** (`libSkiaSharp.so`: 11.15 MB for `linux-x64`, 18.4 MB for
`linux-musl-x64`) dragged transitively (`SkiaSharp` + `SkiaSharp.NativeAssets.Linux.NoDependencies` 3.119.4) plus
`Docxodus.dll` itself (1.87 MB). These are the **same SkiaSharp native packages R3 deliberately removed** when it
dropped WmlComparer.

**Impact on the BFF ceiling**: baseline is **~49.63 MB compressed** (incl. PDBs). Even assuming native code
compresses to ~50%, the added `libSkiaSharp.so` alone contributes roughly **+6–8 MB compressed**, pushing the BFF
to **~56–58 MB** and, on a musl/Alpine container, **over the 60 MB HARD STOP**. This is a root §10 escalation on
its own (≥+5 MB single-task delta; ≥55 MB cumulative → architecture review). **Hypothetical for the main BFF: do
not add it.** (Candidate B adds **0 MB** — `DocumentFormat.OpenXml` is already referenced.)

### 4. Maintenance & licensing

| | Docxodus | Build-on-OpenXML-SDK |
|---|---|---|
| License | MIT (fork of OpenXmlPowerTools) — **compliant** | MIT (`DocumentFormat.OpenXml`) — compliant |
| .NET target | **6.4.0 = net8 (only compatible line); 7.1.0 = net10-ONLY** (incompatible with our net8 BFF) | net8-native, first-party Microsoft |
| Surface area | **390 public types** (charts, xlsx/pptx, HTML conversion) — huge, mostly irrelevant | We own ~1 small class; no vendor surface |
| Control over edge cases | Low — behavior is inside the differ | Full — we encode each edge (para-mark, delText, id seeding) |
| Coupling risk | Ties FR-04 to a single-maintainer fork's net-version cadence | None beyond the SDK we already ship |

Licensing is a wash (both MIT). Everything else favours building on the SDK.

---

## Reusable spike output → seeds task 030

The spike's `SpikeOpenXmlApplier` is the working nucleus of the production `ComposeShadowPatchEngine`:

- `BuildParaIdIndex` — O(1) `w14:paraId` → `Paragraph` resolve (no text-search).
- `SplitRunAtOffset` — `Run.Clone()` split preserving `RunProperties` on both halves.
- `ApplyInsertText` / `ApplyDeleteRange` — native `w:ins` / `w:del` (`w:delText`, EDGE-4) at the anchor.
- `ApplyDeleteParagraph` — para-mark deletion via `w:pPr/w:rPr/w:del` (bridge-prior-art #6 hardest edge).
- Monotonic revision-id seeding past existing ids.

The retired `DocxAnnotationWriter` (`Services/Compose/DocxAnnotationWriter.cs`) is a rich reference for the SAME
run-surgery mechanics — but it **locates by whole-doc text-search** (`LocateTarget`), which task 030 must NOT keep.
The spike proves the identical surgery driven purely by the durable anchor instead.

### Findings to carry into task 030 (not blockers)

1. **Intra-paragraph op drift is real.** An early spike run applied two ops to the *same* paragraph; op 1's run
   split shifted the run indices op 2 relied on (op 2 landed on the wrong run). The op log must be **rebased**
   (ProseMirror-`Mapping`-style, per bridge-prior-art #1) so anchors in later ops account for earlier ops in the
   same paragraph — OR the engine applies within a paragraph in a drift-safe order (e.g. right-to-left by offset,
   re-resolving run boundaries after each op). The spike sidesteps this by targeting distinct paragraphs; task 030
   must handle same-paragraph batches. This is the client-capture/rebasing concern (task 020), surfaced early.
2. **`w:ins` is not a `w:r`.** `InsertedRun` is a sibling element, not a `Run`, so it does not shift
   `Elements<Run>()` indices — but the *split halves* of the anchored run do. Re-derive run boundaries per op.
3. **Multi-run `deleteRange`** (a range spanning >1 run) was scoped out of the spike (the CIPO acceptance range is
   intra-run). Task 030 implements the per-run `w:del` sweep across the range.
4. **`w:delText` (EDGE-4)** is mandatory inside `w:del`; using `w:t` yields a file Word treats as corrupt.

---

## Decision record

- **Chosen**: build the FR-04/FR-11 patch engine on `DocumentFormat.OpenXml` 3.5.1 (Candidate B). **Zero new
  runtime package.**
- **Rejected**: Docxodus — architectural mismatch (whole-doc comparer, not an offset applier; strips `w14:paraId`;
  net10-only latest) compounded by a ~+13 MB uncompressed / ~+6–8 MB compressed footprint that threatens the 60 MB
  ceiling.
- **ADR posture**: consistent with I-1/I-3/I-4/I-7, D2, D5; ADR-007 (`byte[]`-in/out; no `Microsoft.Graph`);
  ADR-013 (no AI internals); ADR-029/NFR-01 (no package bloat). No ADR tension to escalate.
- **Escalation triggers**: neither POML trigger fires as a blocker — Candidate B passes, so the "neither engine
  works" gate is clear; the size trigger is moot because Docxodus is rejected on fit regardless of size.
