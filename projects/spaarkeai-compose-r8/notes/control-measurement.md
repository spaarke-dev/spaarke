# The control — what current master actually preserves (task 023)

> **Taken at** `bf77cdcb023315f9edae9c615613abd0b411f8ef` · 2026-08-21
> **Corpus** 18 documents · 271 comparable blocks · 210 near-tier-relevant blocks
> **Instrument** [`ComposeBlockPreservationOracle`](../../../tests/integration/seam/Compose/ComposeBlockPreservationOracle.cs) via
> [`ComposeFidelityGateHarnessTests`](../../../tests/integration/seam/Compose/ComposeFidelityGateHarnessTests.cs) (task 020) ·
> contract: [`gate-contract.md`](gate-contract.md)

---

## The number

| | Lenient (content loss) | Strict (+ identity drift) |
|---|---:|---:|
| **Overall block preservation** | **18.08%** (49/271) | **12.18%** (33/271) |
| **Near-tier preservation** | **6.67%** (14/210) | — |

**Save one paragraph of a legal document and roughly four in five of the paragraphs you did not touch come
back different. Nineteen in twenty lose their formatting.**

Terminal outcome distribution across all 18 documents: **`persisted` × 18**, with **zero** outcome-honesty
violations. Track S's contract holds corpus-wide — every save tells the truth about *whether* it wrote. This
document is about *what* it wrote.

---

## Is this a valid control?

The oracle measures what the **render path** emits. Those files are untouched on this branch:

```
$ git diff --stat origin/master...HEAD -- \
    src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocumentRenderer.cs \
    src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs \
    src/server/api/Sprk.Bff.Api/Services/Compose/ComposeContentModel.cs
(no output)
```

Track S changed the save **lifecycle** — `ComposeService`, `ComposeEndpoints`, `UploadSessionManager`,
telemetry, and the client. It never changed what the renderer writes. **No Track A change is applied.** The
control is clean.

---

## Per-document

| Document | lenient overall | near tier | near-tier blocks | strict overall | paraId drift |
|---|---:|---:|---:|---:|---:|
| `PAT 109270W-1 - CLAIMS…docx` | **0.93%** | 0.00% | 107 | 0.93% | 0 |
| `AppligentNDA_Signed.docx` | **2.04%** | 0.00% | 48 | 2.04% | 0 |
| `multilevel-1-1-1.docx` | 12.50% | 0.00% | 7 | 12.50% | 4 |
| `Engagement Letter.docx` | 16.67% | n/a | 0 | 8.33% | 0 |
| `court-filing-spacing.docx` | 20.00% | 0.00% | 4 | 20.00% | 0 |
| `01 - Test Matter Create Fields Only.docx` | 25.00% | n/a | 0 | 12.50% | 0 |
| `multi-author-redline-synthetic.docx` | 25.00% | 0.00% | 3 | 25.00% | 0 |
| `char-formatting-mixed-runs.docx` | 33.33% | 0.00% | 2 | 33.33% | 0 |
| `footnote-references.docx` | 33.33% | 0.00% | 2 | 33.33% | 0 |
| `ref-cross-references.docx` | 33.33% | 0.00% | 2 | 33.33% | 0 |
| `symbol-section-mark.docx` | 33.33% | 0.00% | 2 | 33.33% | 3 |
| `nda-interrupted-clauses.docx` | 41.67% | 14.29% | 7 | 41.67% | 0 |
| `line-numbered-pleading.docx` | 47.83% | 36.84% | 19 | 26.09% | 8 |
| `interior-text-boxes.docx` | 50.00% | n/a | 0 | 25.00% | 0 |
| `multipart-paraid-collision.docx` | 50.00% | 0.00% | 1 | 50.00% | 0 |
| `alternate-content-duplicate-paraid.docx` | 66.67% | n/a | 0 | 33.33% | 0 |
| `content-controls-sdt.docx` | 66.67% | 100.00% | 1 | 33.33% | 0 |
| `heading-style-numbering.docx` | 100.00% | 100.00% | 5 | 45.45% | 6 |

**The two real client documents are the worst two rows.** The 109-block patent claims document preserves
**one block**. The 50-block signed NDA preserves **one block**. `n/a` means the near tier was not in play in
that document at all — deliberately not reported as 100% (see `gate-contract.md`, "null is not 100").

`heading-style-numbering.docx` reading 100% lenient / 45.45% strict is the two levels doing exactly their
job: nothing was lost, but six paragraph identities were regenerated.

---

## Every loss, classified

The task's rule: *an unclassified loss may not be counted in either direction.* Every difference the oracle
reported was traced to the actual XML on both sides. Two classes came back **ARTIFACT** and were fixed in the
oracle (below); everything else is **GENUINE**.

### GENUINE — silent (no warning; the user is never told)

These are the merge model's job. The renderer rebuilds each paragraph from a content model that carries
**justification, bold and italic — and essentially nothing else**.

| # | Loss | Evidence (original → saved) | Occurrences |
|---|---|---|---:|
| G1 | **Indentation** dropped | `<w:ind w:left="720" w:hanging="720"/>` → *absent* | 37 |
| G2 | **Line/paragraph spacing** dropped | `<w:spacing w:line="480" w:lineRule="auto" w:after="240"/>` → *absent* (double-spaced court filing renders single) | 37 |
| G3 | **Paragraph-mark run properties** dropped | `<w:pPr><w:rPr>…</w:rPr></w:pPr>` → *absent* | 36 |
| G4 | **Paragraph style** dropped | `<w:pStyle w:val="Heading1"/>` → *absent* | 29 |
| G5 | **Numbering association** dropped | `<w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr>` → *absent* | 27 |
| G6 | **Fonts** dropped | `<w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman"/>` → *absent* | 25 |
| G7 | **Character formatting reduced to bold+italic** | `<w:rPr><w:rFonts …/><w:b/><w:i/><w:caps/><w:color w:val="1F3864"/><w:spacing w:val="20"/><w:sz w:val="22"/><w:szCs w:val="22"/></w:rPr>` → `<w:rPr><w:b/><w:i/></w:rPr>` — caps, colour, letter-spacing, size and font all gone | 38 |
| G8 | **Tab stops** dropped | `<w:tabs><w:tab w:val="left" w:pos="1440"/></w:tabs>` → *absent* | 8 |
| G9 | **Outline level** dropped | `<w:outlineLvl w:val="0"/>` → *absent* | — |
| G10 | **Footnote reference** dropped from the body | `<w:r><w:rPr><w:rStyle w:val="FootnoteReference"/><w:vertAlign w:val="superscript"/></w:rPr><w:footnoteReference w:id="1"/></w:r>` → *the whole run is absent* — the footnote is orphaned in `footnotes.xml` | — |
| G11 | **`w14:textId` dropped** on every rendered paragraph | present → absent | all |
| G12 | **`w14:paraId` regenerated** on some paragraphs | `FC519803` → `5ED97E66` | 21 |

**G7 is the one to look at twice.** `char-formatting-mixed-runs.docx` loses 100% of its near tier and emits
**no degradation warning at all**. The renderer does not know it dropped anything, so nothing is reported to
the user, nothing appears in telemetry, and nothing would have appeared in a UAT unless somebody compared
fonts by eye. That is the silent-loss mode this project exists to close.

### GENUINE — warned (sanctioned by ADR-049; the user IS told)

Real loss, but policy rather than defect. The merge model is **not** required to eliminate these; it is
required not to make them worse.

| # | Loss | Evidence | Warning code |
|---|---|---|---|
| W1 | **Text boxes flattened** into the host paragraph | `<w:pict><v:textbox><w:txbxContent><w:p>Interior text-box line one.</w:p><w:p>Interior text-box line two.</w:p></w:txbxContent></v:textbox></w:pict>` → `<w:t>Interior text-box line one. Interior text-box line two.</w:t>` | `text-box-flattened`, `unrendered-paragraphs` |
| W2 | **Fields flattened to their cached result** | `<w:fldSimple w:instr=" REF _Ref_Confidentiality \r \h "><w:r><w:t>4</w:t></w:r></w:fldSimple>` → `<w:r><w:t>4</w:t></w:r>` — the number is right and will never update again | `field-flattened-to-text` |
| W3 | **Content controls flattened** to plain runs | `<w:sdt><w:sdtPr><w:alias w:val="Counterparty"/>…</w:sdtPr><w:sdtContent><w:r><w:rPr><w:b/></w:rPr><w:t>Beta Industries, Inc.</w:t></w:r></w:sdtContent></w:sdt>` → `<w:r><w:rPr><w:b/></w:rPr><w:t>Beta Industries, Inc.</w:t></w:r>` | `content-control` |
| W4 | **`w:sym` converted to Unicode text** | `<w:sym w:font="Symbol" w:char="F0A7"/>` → `<w:t>§</w:t>` | — (intended per R4.5 FR-06) |
| W5 | **Footnote reference unrepresentable** | see G10 — warned, but the loss is content, not formatting | `unrepresented-footnote-reference` |

W2 deserves its own note. A field flattened to its cached result **reads as correct**: the cross-reference
shows the right number. It is only wrong later, once the document is edited and the reference no longer
tracks. No text-level comparison can catch it, which is why `ref-cross-references.docx` exists.

### ARTIFACT — fixed in the oracle, control re-run

Per the task's constraint, artifacts are **fixed here**, while nothing depends on them — not annotated as
known noise. Noise in the oracle becomes noise in the gate, and at gate time the pressure runs toward
lowering the bar rather than fixing the instrument.

| # | Artifact | Why it is not loss | Direction | Fix |
|---|---|---|---|---|
| **A1** | Renderer emits an **empty `<w:pPr/>`** where the source had none | An empty property container expresses no formatting. Word renders `<w:p><w:pPr/><w:r>…` and `<w:p><w:r>…` identically; the schema makes the element optional precisely because absence and empty presence mean the same thing. | **Over-reported** — the path contains `pPr`, so it counted as a near-tier loss on nearly every block | Normalize away a `w:pPr`/`w:rPr` with no attributes, no children and no value. **Only the empty side is removed**, so a `w:pPr` that HAD content and came back empty still differs. |
| **A2** | A dropped **repeated same-named child** collapsed to a bare parent path | Not "not loss" — the loss was real, but the oracle *could not name it*. Both sides had only `w:r` children, so the one-side-only name set was empty and a dropped footnote-reference run reported as `"p"` — which contains no near-tier element. | **Under-reported** — `multipart-paraid-collision.docx` read **100%** near tier while dropping a footnote reference | Descend into the unmatched tail and record every distinct element name it contains, so the difference names the construct that went missing. |

Both fixes carry a paired test asserting **both halves** — that the artifact is gone *and* that the real loss
it was masking is still caught (`Oracle_IgnoresAnEmptyPropertyContainer_ButStillSeesOneThatLostItsContent`,
`Oracle_NamesTheConstructInsideADroppedRepeatedChild_NotJustTheParent`). The first half alone would be a
plausible way to make the numbers look better; the second is what proves it is not.

### Pre- and post-correction numbers

| Stage | Lenient overall | Near tier |
|---|---:|---:|
| As first measured (both artifacts present) | 8.86% | 2.37% |
| After **A1** (empty-container over-reporting removed) | 18.08% | 7.14% |
| After **A2** (dropped-child under-reporting removed) | **18.08%** | **6.67%** |

**A1 moved the headline by nine points.** Publishing the uncorrected 8.86% would have made the gate look
further away than it is and manufactured a case for lowering the bar. **A2 moved it the other way** — the
corrected number is *worse*, because a document that was falsely reading 100% near-tier now reads 0%. Both
directions matter; an oracle is only trustworthy if its errors are corrected regardless of which way they
flatter the result.

---

## Threshold recommendation

The spec assumed **100% near-tier / ≥95% overall** pending this measurement. With the data in hand:

### Keep 100% near-tier — and measure it at the LENIENT level

**Reachable by construction.** R8's merge model clones untouched blocks **verbatim** from the retained
baseline. A byte-cloned block is identical to its original by definition, so every near-tier property on it
survives — not by careful re-serialization, but because nothing re-serializes it. If the merge model works at
all, near-tier preservation is 100%; if it is below 100%, a block that should have been cloned was not. That
makes the bar a *binary correctness check on the mechanism*, which is exactly what a gate should be, rather
than a quality percentage to negotiate.

The 6.67% control confirms there is no ambiguity to resolve: nothing about today's behavior is near the bar,
so a pass cannot be an accident of measurement.

### Keep ≥95% overall, at the LENIENT level

The residual 5% covers blocks legitimately re-authored (the edited block and its neighbours) and the
ADR-049-sanctioned hard-tier flattening in W1–W5. Block counts are **stable across every corpus document**
(109→109, 50→50, 9→9), so the merge model faces no re-alignment problem — 95% is comfortable, not heroic.

### Do NOT gate Phase 3 on the STRICT level — report it, and forbid regression

Strict adds `paraId`/`textId` identity drift, which is **task 042's** concern (revision-id and anchor seeding
under cloning), not the merge model's. Gating Phase 3 on strict would hold the merge mechanism responsible
for a defect it is not designed to fix, and a failure would be unattributable — the exact confusion Track S
was shipped alone to avoid.

**Recommendation**: strict overall is reported at every gate run and **must not fall below the control's
12.18%**. It is a ratchet, not a bar.

### The MISS condition, stated in advance

The gate is **MISSED** if any of the following is true on the full corpus:

1. Near-tier preservation < **100%** at lenient, on **any single document**.
2. Overall preservation < **95%** at lenient, corpus-wide.
3. Any document classifies **`fail`** (hard-fail HTTP, failed projection, or the edit missing on reopen).
4. Any **outcome-honesty violation** — an outcome outside task 013's closed set, or a claim that contradicts
   what reached storage.
5. Strict overall **below 12.18%** — a regression against this control.

A miss is an **owner escalation** under root CLAUDE.md §6/§6.5. It is explicitly **not** an occasion to
re-tune the thresholds: they are recorded here, before the merge model exists, precisely so that they cannot
be fitted to whatever it turns out to produce.

---

## What the merge model must achieve, per construct family

| Family | Control | Required at the gate | How the merge model gets there |
|---|---:|---|---|
| Indentation (`w:ind`) | 0% | **100%** | Untouched block cloned verbatim |
| Spacing (`w:spacing`) | 0% | **100%** | Cloned verbatim |
| Paragraph style (`w:pStyle`) | 0% | **100%** | Cloned verbatim |
| Numbering (`w:numPr`) | 0% | **100%** | Cloned verbatim; `numId` may be remapped (the oracle canonicalizes remapping, not removal) |
| Character formatting (`w:rPr`) | ~0% | **100%** on untouched blocks; **inherited** on an edited run (FR-A04) | Cloned verbatim; edited runs inherit the source run's `w:rPr` |
| Fonts (`w:rFonts`) | 0% | **100%** | Cloned verbatim |
| Tab stops (`w:tabs`) | 0% | **100%** | Cloned verbatim |
| Footnote references | 0% | **100%** — the reference must survive in the body | Cloned verbatim; FR-A11 covers the anchor |
| Fields (`w:fldSimple` / `w:fldChar`) | flattened | Survive **as fields**, not as their cached result | Opaque-atom payload carry (FR-A05, task 041) |
| Content controls (`w:sdt`) | flattened | Survive **as SDTs** | Opaque-atom payload carry (task 041) |
| Text boxes (`mc:AlternateContent`) | flattened + warned | **May remain flattened** (ADR-049 policy) — must still warn, must not regress | Unchanged |
| `w14:paraId` / `textId` | 21 drifted | Not gated at Phase 3; must not regress | Task 042 |

---

## Findings recorded against other tasks

Per the task's constraint that no `src/` file be modified here, product defects are recorded, not fixed:

| Finding | Owner |
|---|---|
| **The content model carries only justification, bold and italic.** Every other paragraph and run property is lost at projection time, before the renderer runs. The merge model's block-clone path avoids it for untouched blocks, but an EDITED block is still rebuilt from this model — so FR-A04 property inheritance is not optional, it is the only thing standing between an edited paragraph and total formatting loss. | **040 / 041** |
| **A dropped footnote reference orphans its footnote.** The body loses the `w:footnoteReference` run while `footnotes.xml` keeps the target, producing a document with an unreachable footnote. Warned (`unrepresented-footnote-reference`) but not repaired. | **041** |
| **`char-formatting-mixed-runs.docx` loses its entire near tier with NO warning.** The degradation-warning set does not cover run-property loss, so the most common loss class in the corpus is invisible to the user and to telemetry. | **044** (warning taxonomy) |
| **`w14:textId` is dropped on every rendered paragraph** — unconditional, unwarned. | **042** |

---

## Escalation check

Neither of the task's escalation triggers fired:

- **"near-100% preservation on master"** — 18.08% overall / 6.67% near tier. The oracle plainly sees the R6
  loss; it is not blind.
- **"assumed thresholds unreachable for a legitimate reason"** — 100% near-tier is reachable *by
  construction* under a verbatim block-clone. No construct family requires a scope decision from the owner.

**Phase 2 is complete. Task 030 (merge prototype) may proceed against these numbers.**
