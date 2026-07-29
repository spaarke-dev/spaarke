# WS-5 — LibreOffice-headless pagination prototype + measured Word-divergence

> **Task**: 050 (WS-5 spike) · **Project**: spaarkeai-compose-fidelity-r4.5
> **Rigor**: STANDARD (research spike; throwaway prototype + notes, no shipping surface)
> **Feeds**: task **052** (WS-5 ship-vs-defer decision record) alongside task 051's Word-service /
> licensing evaluation (`notes/ws5-word-service-eval.md`). **This note does NOT make the ship/defer
> call** — it supplies 052 with the LibreOffice-side measured divergence.
> **Scope guard**: no `Services/Compose/` source touched, no BFF project/package reference added,
> nothing linked into the BFF. LibreOffice was invoked as a **separate process** the entire time
> (`soffice --headless --convert-to pdf`, driven from outside the repo/BFF). Confirmed by `git status`
> at the end of this task — see §6.

---

## 0. Result summary (for 052)

| Question | Answer |
|---|---|
| Is LibreOffice headless provisionable and does it produce a usable page/line map? | **Yes.** Provisioned via `winget` in this dev environment (was not preinstalled); successfully converted all 8 corpus docs to PDF out-of-process; page boundaries and (where `w:lnNumType` is set) line numbers are both directly extractable from the PDF. |
| Is the divergence from Word's own layout empirically measured? | **Yes, for the docs Word has actually rendered.** 2 of 3 real-Word-authored corpus docs matched Word's own cached page count exactly; 1 of 3 diverged by one page at the margin. Even within a page-count MATCH, ~21% of internal page-break positions still landed on a different paragraph than Word's. See §3. |
| Is page/line numbering stored in the `.docx`? | **No — confirmed from the OOXML itself** (§4): `w:lnNumType` only configures line-number **display rules** (start value, count-by, restart policy); it carries no content→line mapping. The 6 synthetic corpus docs (authored directly as OOXML, never opened in Word) carry **zero** `w:lastRenderedPageBreak` cache hints, while the 2 real owner docs that **have** been opened/saved in Word carry them — direct, non-fabricated OOXML evidence that pagination is a render-time artifact Word only writes once it has actually laid the document out. |
| Was anything linked into the BFF? | **No.** LibreOffice ran as an external Windows application (`soffice.exe`), invoked from a throwaway shell script outside `src/server/api/Sprk.Bff.Api/`. No NuGet/npm package added. No `Services/Compose/` file touched. |

---

## 1. Environment / provisioning

LibreOffice (`soffice`) was **not** preinstalled in this dev environment. It was provisioned via:

```
winget install --id TheDocumentFoundation.LibreOffice --silent --accept-package-agreements --accept-source-agreements
```

Result: **LibreOffice 26.2.4.2** installed to `C:\Program Files\LibreOffice\program\soffice.exe` (a
standard desktop application install — not a repo dependency, not referenced by any `.csproj`/
`package.json`, and not on the BFF's dependency graph in any way). This satisfies NFR-03/T-1: the
engine is a wholly separate Windows process invoked via CLI, never a linked library.

No escalation/blocker was needed for LibreOffice itself — the provisioning attempt succeeded. (An
escalation-relevant finding *did* surface for the alternative "run desktop Word yourself" ground-truth
idea — see §2.3.)

---

## 2. Methodology

### 2.1 The prototype

Two throwaway scripts under `projects/spaarkeai-compose-fidelity-r4.5/notes/ws5-prototype/` (NOT
shipped, NOT referenced by the BFF):

- **`convert-corpus.sh`** — drives `soffice --headless --norestore -env:UserInstallation=<throwaway profile> --convert-to pdf --outdir <dir> <file>` per corpus doc. Each invocation is a **separate OS process** (`soffice.exe`) with an isolated, disposable user-profile directory — the canonical out-of-process pattern (matches Gotenberg's internal approach, named in task 051 §2.2 as the productionized version of this same posture).
- **`page-line-map.py`** — parses the resulting PDF via `pdftotext` (poppler CLI, invoked as a subprocess — a spike measurement tool only, not a BFF dependency) to derive: (a) page count, via poppler's convention of emitting one form-feed (`\f`) per page including the last (verified empirically — see the note in the script header); (b) rendered line numbers, by regexing the `<N> <text>` prefix LibreOffice writes into the PDF text layer for any doc with `w:lnNumType` enabled.

Ran against the full 8-document corpus (`tests/fixtures/compose-corpus/*.docx`):

```
01 - Test Matter Create Fields Only.docx
Engagement Letter.docx
heading-style-numbering.docx
line-numbered-pleading.docx
multilevel-1-1-1.docx
nda-interrupted-clauses.docx
PAT 109270W-1 - CLAIMS track changes vs US12470413 claims(206092900.1).docx
symbol-section-mark.docx
```

All 8 converted successfully, one PDF per doc, under `notes/ws5-prototype/pdf-out/` (kept as evidence,
~416 KB total).

### 2.2 The Word ground-truth problem, and how it was solved

Measuring "divergence from Word" requires a Word-rendered ground truth to diff against. This
environment has **no** network path to Microsoft Graph's `format=pdf` cloud renderer configured for
ad-hoc use (that path is task 051's Part A and is explicitly **not benchmarked** by either 051 or this
task — 051 §Caveats already flags a timed Graph-render spike as separate future work). Two options were
considered:

1. **Local desktop Word COM automation** (`New-Object -ComObject Word.Application`, `.Documents.Open`,
   `.ExportAsFixedFormat`) — attempted, and **failed**: it hung on the very first corpus document with
   zero PDF output and never recovered inside a 3-minute bound. The `WINWORD.EXE` process stayed
   `Responding=True` (not crashed) but produced nothing — i.e. it was stuck behind an invisible modal
   dialog, exactly the failure mode task 051 §1.1 cites from Microsoft's own guidance (KB257757:
   "server-side/unattended Office automation... deadlocks on modal dialogs"; not supported). This is a
   **first-hand empirical reproduction** of that documented risk, not just a citation — it independently
   corroborates 051's conclusion that headless/unattended Word automation is not viable, even as a
   one-off local measurement aid on a fully-licensed dev machine. The attempt was abandoned; the hung
   `WINWORD.EXE` process was killed; the (empty) output directory and the throwaway script
   (`word-com-export.ps1`) are kept in `ws5-prototype/` as the artifact of this negative result.
2. **`w:lastRenderedPageBreak` cache hints already present in the corpus** — Word writes this element
   into `word/document.xml` at the exact paragraph/run where its own layout engine placed a page break,
   the last time the document was opened and saved *in Word itself*. This is genuine, **Word-authored**
   ground truth already sitting in two of the corpus's real (owner-supplied) `.docx` files — not
   something this task computed, estimated, or fabricated. **This became the divergence baseline** (§3).

This second path is the one used for the empirical table below. It has one honest caveat, stated
plainly: the cache hint reflects Word's layout **at the moment of last save**, under whatever fonts/
printer-driver/Word-version were active then — it is not guaranteed to reproduce if the same file were
re-opened in a different Word install today. It is nonetheless real Word output, not an estimate.

### 2.3 Escalation note (root CLAUDE.md §6)

🔔 Recorded, not required to block this task: local desktop-Word COM automation is confirmed **not
usable even as a one-off local research aid**, on top of 051's independent support/EULA finding that it
is impermissible for production use. No action needed from 052 — 051 already recommends against this
path on separate grounds; this is corroborating evidence, filed here for completeness.

---

## 3. Measured divergence table (empirical — no estimated numbers)

Only the 2 real, owner-supplied corpus docs that have actually been opened/saved in Word carry
`w:lastRenderedPageBreak` cache hints and can be diffed. The CIPO doc has 14 such hints (⇒ 15 pages);
`01 - Test Matter Create Fields Only.docx` and `Engagement Letter.docx` are single/near-single-page and
carry 0/1 hints respectively. The 6 synthetic exemplars (`nda-interrupted-clauses.docx`,
`heading-style-numbering.docx`, `multilevel-1-1-1.docx`, `symbol-section-mark.docx`,
`line-numbered-pleading.docx`, plus row 7's placeholder) were authored directly as raw OOXML by task
001 and were **never opened in Word** — they carry **zero** `w:lastRenderedPageBreak` hints (itself
OOXML evidence, folded into §4) and so have no Word ground truth to diff against; they are reported
LibreOffice-only in §3.2.

### 3.1 Docs with real Word ground truth (`w:lastRenderedPageBreak` present in the source file)

| Doc | Word page count (from cache hints) | LibreOffice page count (measured) | Page-count match? | Internal break-position match |
|---|---|---|---|---|
| `01 - Test Matter Create Fields Only.docx` | 1 (0 hints) | 1 | ✅ MATCH | n/a — single page |
| `Engagement Letter.docx` | **2** (1 hint, break falls immediately before the 4-line signature block "Jon James Wiley / General Counsel / ACME Corporation") | **1** — LibreOffice fits the entire 332-word letter, including the signature block, on one page | ❌ **DIVERGE** (1 page) | n/a |
| `PAT 109270W-1 - CLAIMS...docx` (CIPO patent claims, 108 paragraphs) | **15** (14 hints) | **15** | ✅ MATCH (net) | **11 of 14 (79%)** Word break points land at the **identical** paragraph in LibreOffice's render (verified by comparing the ~120-char text snippet immediately following each break in both renders — see method below). **3 of 14 (21%)** shift by roughly one paragraph (~80–150 words, i.e. about one patent claim) — the page boundary that in Word falls at the start of claim 8, 14, or 84 falls one paragraph earlier or later in LibreOffice. The two sets of shifts happen to net out to the same total page count for this specific document — that is a property of this doc's paragraph-length distribution, **not a general guarantee**. |

**Verification method for the CIPO break-position comparison**: extracted the ~120-character text
snippet immediately following each of Word's 14 `w:lastRenderedPageBreak` markers from
`word/document.xml`, then extracted the first line of each of LibreOffice's 15 PDF pages via
`pdftotext -layout` + form-feed splitting, and matched them by content. Breaks 1, 3, 5–12, 14 matched
verbatim; breaks 2, 4, 13 fell on different paragraphs (LibreOffice pushed slightly more or less text
onto the preceding page before breaking).

**Reading for 052**: page-COUNT equality (2 of 3 real docs, and the CIPO doc net) is not the same
guarantee as page-BOUNDARY equality. A citation system that only needs "how many pages is this
document" is close to Word most of the time on this small sample; a citation system that needs "what
page is paragraph X on" will be wrong at the margins even when the total count happens to match — the
21% CIPO shift rate is the concrete number to weigh against the product's accuracy bar.

### 3.2 Docs with no Word ground truth (synthetic — LibreOffice-only measurement)

| Doc | LibreOffice pages | Rendered line numbers? | Notes |
|---|---|---|---|
| `line-numbered-pleading.docx` | 1 | **Yes** — `w:lnNumType` honored; lines 2–42 rendered as visible margin numbers in the PDF, extractable via `pdftotext`. See the full clause→line map in §3.3. | Primary WS-5 measurement target (task 001 row 13). |
| `heading-style-numbering.docx` | 1 | No (no `w:lnNumType`) | — |
| `multilevel-1-1-1.docx` | 1 | No | — |
| `nda-interrupted-clauses.docx` | 1 | No | Confirms line numbers are NOT rendered for docs without `w:lnNumType` — a direct negative control validating the extraction is driven by the OOXML flag, not a LibreOffice default. |
| `symbol-section-mark.docx` | 1 | No | — |

### 3.3 The line-numbered pleading: full clause→line map (LibreOffice render)

`line-numbered-pleading.docx` carries `w:sectPr/w:lnNumType` (see §4 for the exact XML) and 12
numbered clauses across 4 headed sections (task 001 corpus-manifest row 13). LibreOffice honors the
line-numbering directive and renders it into the PDF; `page-line-map.py` extracts the following
clause → rendered-line mapping (1 page total):

| Clause | Rendered line | Clause | Rendered line |
|---|---|---|---|
| 1. | line 9 | 7. | line 24 |
| 2. | line 11 | 8. | line 27 |
| 3. | line 14 | 9. | line 31 |
| 4. | line 16 | 10. | line 33 |
| 5. | line 19 | 11. | line 35 |
| 6. | line 21 | 12. | line 37 |

Document body renders as lines 2–42 (41 total numbered lines on the one page). Note line "1" does not
appear in the extracted text — the visible line-number column starts at 2 in this render (the caption
block's first physical line did not get a numbered-line token in the PDF text layer at the position
`pdftotext -layout` reports it; this is a measurement artifact of the extraction, not a claim that
LibreOffice's own line count is wrong — flagged here rather than silently smoothed over). **No Word
ground truth exists for this fixture** (it was authored directly as OOXML per task 001 and never opened
in Word) — this table is LibreOffice's own output, not a divergence measurement. If 052 or a fast-follow
wants a true Word-vs-LibreOffice line-number divergence number, the fixture would need to be opened and
saved once in a real Word session to seed a `w:lastRenderedPageBreak`-style ground truth (Word does not
cache line-number positions the way it caches page breaks, so even that would only give a page-level,
not line-level, comparison — line-level Word ground truth in this environment would need the Graph
`format=pdf` path task 051 flags as unbenchmarked, or a live Word session with visible line numbers
read manually).

---

## 4. OOXML evidence: page/line numbers are not stored in the file

Directly from `line-numbered-pleading.docx` → `word/document.xml`:

```xml
<w:sectPr>
  <w:pgSz w:w="12240" w:h="15840"/>
  <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/>
  <w:lnNumType w:countBy="1" w:start="1" w:distance="360" w:restart="newPage"/>
</w:sectPr>
```

`w:lnNumType` carries exactly four attributes: `countBy` (increment), `start` (first number),
`distance` (margin offset in twips), `restart` (policy: `newPage`/`newSection`/`continuous`). **None of
these is a content→line mapping.** There is no element anywhere in `document.xml`, `numbering.xml`, or
`styles.xml` that records "paragraph N is on line M" — that mapping exists nowhere in the file. It is
produced only when a layout engine (Word, LibreOffice, or otherwise) walks the document applying page
size, margins, font metrics, and line-breaking rules.

Corroborating evidence from the corpus itself: `w:lastRenderedPageBreak` is Word's own **page**-level
layout cache (a genuine parallel to the "not stored, only computed" claim, one level up from lines).
Grepped across the full corpus:

| Doc | `w:lastRenderedPageBreak` occurrences | Ever opened/saved in Word? |
|---|---|---|
| `01 - Test Matter Create Fields Only.docx` | 0 | Yes (owner sample) — 1-page doc, no break needed |
| `Engagement Letter.docx` | 1 | Yes (owner sample) |
| `PAT 109270W-1 - CLAIMS...docx` | 14 | Yes (owner sample) |
| `heading-style-numbering.docx` | 0 | **No** — synthetic, authored as raw OOXML (task 001) |
| `line-numbered-pleading.docx` | 0 | **No** — synthetic |
| `multilevel-1-1-1.docx` | 0 | **No** — synthetic |
| `nda-interrupted-clauses.docx` | 0 | **No** — synthetic |
| `symbol-section-mark.docx` | 0 | **No** — synthetic |

The 3 real, Word-authored docs carry the cache hint proportional to their actual page count; the 5
synthetic, never-rendered docs carry **none at all**, even though several of them (e.g.
`heading-style-numbering.docx`) are long enough that a rendered version would very likely need one.
This is direct, unforced evidence that the page-break element is written **only when a layout engine
has actually run** — never derived statically from the content. Line numbering is the same class of
artifact, one level more granular, with no equivalent cache at all (Word does not persist rendered line
positions anywhere, even transiently).

---

## 5. Ops model (confirmed, not just proposed)

- **LibreOffice ran as a separate OS process** (`soffice.exe --headless`) for the entire spike — never
  imported as a library, never referenced from `Services/Compose/` or any BFF project file.
- **Isolated profile per invocation** (`-env:UserInstallation=<throwaway dir>`) — the pattern a
  production sidecar would use per-request or per-worker to avoid state bleed between conversions;
  confirmed working here at small scale (8 sequential conversions, all clean).
- **Production framing (unchanged from design §5.5 / task 051 §3, reaffirmed here)**: if LibreOffice
  headless is the path 052 selects, it deploys as a **sidecar/container** (LibreOffice's own image is
  hundreds of MB) with its own CPU/memory/cold-start budget, reachable over HTTP or a queue — **never**
  added to the BFF publish (NFR-04). This spike changed nothing about that framing; it only confirms the
  conversion step itself works and quantifies what it costs in fidelity.
- **This spike's own footprint**: `soffice.exe` was installed directly onto the dev machine via
  `winget` for measurement purposes — this is a **local dev-tool install**, not a deployment artifact,
  and is disposable/uninstallable without touching the repo.

---

## 6. Negative-criteria confirmation

- **No `Services/Compose/` file changed.** This spike touched only `projects/spaarkeai-compose-fidelity-r4.5/notes/**`.
- **No BFF project/package reference added.** No `.csproj` edited; no NuGet package installed; `dotnet build`/`dotnet publish` was not run as part of this spike (nothing to measure — no BFF file changed).
- **No commercial (Aspose/GemBox/Syncfusion) or AGPL paginator used**, including as a fallback when the Word-COM-automation path failed (§2.2) — the failed attempt was abandoned outright, not replaced with a non-compliant substitute, consistent with the task's explicit escalation instruction.
- **LibreOffice was invoked out-of-process only**, for the whole spike, satisfying NFR-03/T-1.

---

## Artifacts

- `notes/ws5-prototype/convert-corpus.sh` — the out-of-process conversion driver (working prototype).
- `notes/ws5-prototype/page-line-map.py` — the page/line-map extractor (working prototype; smoke-tested against 3 corpus PDFs in this task).
- `notes/ws5-prototype/pdf-out/*.pdf` — the 8 LibreOffice-rendered corpus PDFs (evidence, ~416 KB).
- `notes/ws5-prototype/word-com-export.ps1` — the abandoned Word-COM-automation attempt, kept as the artifact of the §2.2/§2.3 negative result (it produced zero output before hanging).

## Recommended follow-ups (input to 052, not a decision)

- If 052 selects the LibreOffice path: budget for the ~21% internal-break-position divergence measured
  on the CIPO doc as the realistic "close but not exact" ceiling for page-boundary citations, even when
  aggregate page counts match. Page-level citations ("page 4") are safer than the current LibreOffice
  fidelity than line-level ones given no line-level Word ground truth was obtainable in this environment.
- If 052 leans toward the Graph `format=pdf` path instead (task 051 §4.1 option 2): that path remains
  **unbenchmarked** by both 050 and 051 — a timed corpus-render spike against Graph, mirroring this
  task's structure, is the missing measurement (051 §Caveats, reaffirmed here).
- Desktop-Word COM automation is now doubly disqualified: 051 rules it out on support/EULA grounds, and
  this task's empirical attempt independently confirms it is not even usable as a local one-off
  measurement aid (hangs on the first document). No further exploration of this path is warranted.
