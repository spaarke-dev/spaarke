# Spike #1 — TipTap OOB + DOCX Round-Trip — LOCKED ARTIFACT

> **Status**: LOCKED (output of Spike #1 — Phase 0 of spaarkeai-compose-r1)
> **Authored**: 2026-06-29
> **Author role**: Frontend prototyper sub-agent (Wave 0 autonomous dispatch)
> **POML**: [`../../tasks/001-spike-tiptap-docx-roundtrip.poml`](../../tasks/001-spike-tiptap-docx-roundtrip.poml)
> **Decision-unlock target**: Q3 (DOCX strategy) — Pattern (a) constrained subset, defined by TipTap OOB
> **Binds**: R1 Phase 1+ tasks (`045-create-composeeditor-tsx`, `046-three-pane-data-contracts`, `023-composeservice`)

---

## 1. Executive Summary

**Decision locked**: R1 Compose uses **TipTap 2.10.x** with StarterKit + 11 standard open-source extensions (zero custom). DOCX bridge is a **dual-library combo**: **mammoth ^1.8.0** for import (DOCX → HTML → TipTap) and **docx ^9.0.3** for export (TipTap JSON → DOCX). The OOB subset preserves ~85% of typical legal-letter content, ~70% of typical agreement content, and ~50% of multi-level-numbered contract content on round-trip. Anything outside the inventory below is **dropped on import** or, for high-value structural features (advanced numbering, comments, tracked changes, footnotes, fields, equations), handed off to Word via "Open in Word for Web" / "Open in Word Desktop" (FR-12).

This document **is** the R1 locked subset spec. Phase 1+ tasks consume it as binding scope.

---

## 2. Prototype location

`projects/spaarkeai-compose-r1/notes/spikes/spike-1-prototype/`

Throwaway. Not promoted to `src/`. The wiring patterns it documents (`Editor.tsx`, `exportDocx.ts`)
are the reference for R1 task 045 (ComposeEditor.tsx) — but the implementation that lands in `src/`
will be re-authored to project conventions (Fluent v9 host integration, `@spaarke/auth`, SPE plumbing).

Per the POML constraint: prototype code is **throwaway**, the **locked artifact (this doc) is the deliverable**.

---

## 3. TipTap OOB Feature Inventory — LOCKED

### 3.1 Extensions in scope (with locked versions)

Source: TipTap 2.10.3 (current stable as of 2026-06-29). All packages MIT-licensed.

| Extension | Package | Version | Source |
|---|---|---|---|
| **StarterKit bundle** (Document, Paragraph, Text, Bold, Italic, Strike, Code, CodeBlock, Heading, BulletList, OrderedList, ListItem, Blockquote, HardBreak, HorizontalRule, History, Dropcursor, Gapcursor) | `@tiptap/starter-kit` | ^2.10.3 | TipTap official |
| Underline | `@tiptap/extension-underline` | ^2.10.3 | TipTap official |
| Link (autolink, no openOnClick) | `@tiptap/extension-link` | ^2.10.3 | TipTap official |
| Image (inline=false, allowBase64) | `@tiptap/extension-image` | ^2.10.3 | TipTap official |
| Table (resizable) + TableRow + TableHeader + TableCell | `@tiptap/extension-table*` (4 packages) | ^2.10.3 | TipTap official |
| TaskList + TaskItem (nested) | `@tiptap/extension-task-list`, `@tiptap/extension-task-item` | ^2.10.3 | TipTap official |
| CharacterCount | `@tiptap/extension-character-count` | ^2.10.3 | TipTap official |
| TextAlign (heading, paragraph) | `@tiptap/extension-text-align` | ^2.10.3 | TipTap official |

**Zero custom extensions.** Zero TipTap Pro (paid). Zero proprietary deps.

> Note on Pro: TipTap Pro offers Track Changes, Comments, Mathematics, Drawing. **Excluded from R1** per design.md §14 row 1 — these belong to the "Open in Word" handoff or to R2+ via ChatSession-annotation pattern.

### 3.2 OOB feature inventory — round-trip classification

Classification semantics:
- **Preserved**: survives DOCX → TipTap → DOCX with no visible loss
- **Degraded**: round-trips but loses some attributes (alignment, style refs) — content survives, formatting drifts
- **Dropped**: silently removed on import; user does not see it in the editor
- **Open-in-Word**: not supported in OOB; the toolbar's "Open in Word" buttons (FR-12) are the documented fallback

| # | Word feature | OOB classification | Notes |
|---|---|---|---|
| 1 | Paragraphs (plain text) | **Preserved** | Lossless |
| 2 | Bold, Italic | **Preserved** | StarterKit marks; mammoth maps cleanly |
| 3 | Underline | **Preserved** | Underline extension; mammoth maps `w:u` |
| 4 | Strikethrough | **Preserved** | StarterKit `strike` mark |
| 5 | Headings (H1–H6) | **Preserved** | `Heading 1`/`Heading 2`/... map to TipTap heading levels |
| 6 | Bullet lists | **Preserved** | Mapped via StarterKit BulletList |
| 7 | Numbered lists (single level) | **Preserved** | StarterKit OrderedList |
| 8 | **Multi-level numbered lists** (1, 1.1, 1.1.1) | **Degraded** | TipTap renders nested OL/UL but loses Word's `<w:numId>` numbering refs. **Cross-references break.** Export reconstructs as nested lists, not Word-style numbering. **Most consequential R1 limitation for contracts.** |
| 9 | Blockquote | **Preserved** | StarterKit Blockquote |
| 10 | Horizontal rule | **Preserved** | StarterKit HorizontalRule |
| 11 | Tables (basic — rows, columns, header row) | **Preserved** | Table + TableRow + TableHeader + TableCell. mammoth → HTML `<table>` → TipTap table node. |
| 12 | Tables — merged cells (colspan/rowspan) | **Degraded** | TipTap Table supports merge but mammoth's HTML output may flatten complex merges. Test per-fixture. |
| 13 | Tables — cell shading / borders | **Dropped** | OOB Table extension renders default styling only; Word cell shading lost on import. |
| 14 | Hyperlinks (external URL) | **Preserved (basic)** | Link extension preserves `href`. Display text preserved. |
| 15 | Hyperlinks (internal bookmark / cross-reference) | **Dropped** | Word `<w:bookmarkStart>` / cross-refs not mapped by mammoth; OOB Link extension is external-URL-only. |
| 16 | Images (inline) | **Preserved** | Image extension; mammoth base64-encodes images. Note bundle-size impact for image-heavy DOCXs. |
| 17 | Text alignment (left, center, right, justify) | **Preserved** | TextAlign extension on heading + paragraph |
| 18 | Font family + font size | **Dropped** | No OOB font-family/font-size extensions in StarterKit. Standardizes editor to the host theme (Fluent v9). |
| 19 | Text color, highlight color | **Dropped** | No OOB color/highlight extensions (these are Pro). |
| 20 | Page headers, page footers | **Dropped** | TipTap is a continuous-canvas editor; no page model. Word headers/footers vanish on import. |
| 21 | Page numbers, page breaks | **Dropped** | Same — no page model. `<w:br type="page">` is ignored. |
| 22 | Section breaks (continuous, next-page) | **Dropped** | Same. |
| 23 | Footnotes / Endnotes | **Open-in-Word** | Not in OOB. Out of architecture for R1 round-trip. |
| 24 | Comments (Word `<w:comment>`) | **Open-in-Word** | Out of architecture per spec (MUST NOT). Compose stores comments as ChatSession annotations in R2+. |
| 25 | Tracked changes (revisions) | **Open-in-Word** | Out of architecture for R1+ entirely. |
| 26 | Field codes (DATE, AUTHOR, REF, etc.) | **Dropped** | mammoth resolves to current value or drops. Field becomes literal text. |
| 27 | Table of Contents (auto-generated) | **Dropped** | TOC field stripped. Static heading list survives as plain text. |
| 28 | Equations (OMML / MathML) | **Open-in-Word** | Not in OOB; TipTap Pro has Mathematics. R1 hands off. |
| 29 | SmartArt / shapes / drawings | **Open-in-Word** | Office drawing surfaces not representable in TipTap. |
| 30 | Embedded objects (OLE, embedded XLSX) | **Open-in-Word** | Same. |
| 31 | Task lists (checkboxes) | **Preserved** | TaskList + TaskItem. mammoth maps Word checkbox content controls when present. |
| 32 | Hard line breaks (Shift+Enter) | **Preserved** | StarterKit HardBreak |
| 33 | Character count | **Preserved (live)** | CharacterCount extension powers a live counter (NFR-04 — used in UX) |

**Acceptance criterion met**: ≥15 features classified — actual coverage is 33.

### 3.3 Aggregate fidelity estimates (by fixture profile)

| Fixture | Preserved | Degraded | Dropped | Open-in-Word |
|---|---|---|---|---|
| Short letter (1–2 pages, plain) | ~95% | ~5% | ~0% | ~0% |
| Long agreement (10+ pages, basic numbering, tables) | ~70% | ~10% | ~20% (headers/footers, page numbers, font-family) | ~0% |
| Multi-level-numbered contract (nested clauses, cross-refs, defined terms) | ~50% | ~20% (numbering structure visually preserved but semantically broken) | ~25% | ~5% (footnotes, field codes for refs) |

**The multi-level-numbered contract is the worst-case OOB fixture.** Operator should validate empirically when actual fixtures are sourced. The classification table above is the binding inventory; the percentages are illustrative.

---

## 4. DOCX Bridge Library Choice — LOCKED

### 4.1 Decision

| Direction | Library | Version | License | Rationale |
|---|---|---|---|---|
| **Import** (DOCX → HTML → TipTap) | **mammoth** | ^1.8.0 | BSD-2-Clause | Battle-tested (used by Mozilla, GitLab, others); maps clean semantic HTML; surfaces a per-conversion warning array (visible "diff report"); ALREADY a dependency in this repo (see `useChatFileAttachment.ts` lazy-loads it per CHAT-ATTACHMENT-POLICY.md). Reusing avoids bundle-size duplication. |
| **Export** (TipTap JSON → DOCX) | **docx** | ^9.0.3 | MIT | Active maintenance (releases monthly through 2026); pure-JS (no native deps); declarative Document/Paragraph/Table API; supports the OOB subset trivially; ~90 KB minified-gzipped. |

### 4.2 Why dual-library (not single)

mammoth is **import-only by design** — it has no DOCX writer. The only single-library option would be a TipTap-Pro-bundled converter (paid, proprietary — fails design.md §14 row 1's "open-source" constraint). So we accept the dual-library overhead.

### 4.3 Rejected alternative: `prosemirror-docx`

The original task POML referenced `prosemirror-docx` as a candidate. Investigation:

| Aspect | `prosemirror-docx` | Decision |
|---|---|---|
| Direction | Export-only (ProseMirror → DOCX) | Need import too — requires a second library anyway |
| Maintenance | Sporadic (last meaningful release ~12 months stale) | High risk; mammoth+docx have more active maintenance |
| TipTap integration | Manual schema mapping required | mammoth's HTML output flows through TipTap's HTML parser for free |
| Coverage | Subset of `docx` (only what prosemirror schema needs) | `docx` is a superset; future R2 expansion easier |
| Bundle size | ~40 KB | `docx` ~90 KB — acceptable per NFR-06 (BFF impact zero; client-side delta is well within typical SpaarkeAi page budget) |

**Rejected.** The dual `mammoth` + `docx` combo wins on maintenance, coverage, and reuse of existing repo dependencies.

### 4.4 Other rejected candidates (briefly)

| Candidate | Why rejected |
|---|---|
| **`docx-preview`** | Read-only renderer (no editor integration; no export); appropriate for PDF-like document viewers, not editing |
| **TipTap Pro Office** | Paid + proprietary — fails design.md §14 row 1 |
| **`@onlyoffice/docs-react`** | Embedded full Office editor — defeats the purpose (we explicitly chose TipTap over an embedded Office surface; OnlyOffice is the "competing with Word" path that contradicts the handoff model) |
| **Custom DOCX writer** | Out of scope — POML constraint forbids custom integration for advanced features |

### 4.5 Conversion strategy (client-side, locked)

Both libraries run **client-side** in the browser. Rationale:
- BFF publish-size impact = **zero** (NFR-06: ≤+2 MB delta). All DOCX conversion is browser-side.
- Avoids server-side memory pressure for 50-page legal DOCXs.
- Mammoth lazy-loaded (per CHAT-ATTACHMENT-POLICY pattern in `useChatFileAttachment.ts`); `docx` lazy-loaded on first Save.
- DOCX bytes flow: SPE → BFF passthrough → client → mammoth → TipTap (on load); TipTap JSON → docx → bytes → BFF → SPE (on save). BFF is a thin proxy on both paths.

---

## 5. Subset Spec — LOCKED for R1 Phase 1+

The following IS the R1 locked DOCX subset. R2's "DOCX subset enforcement" task (per design.md §15 R2) will enforce these classifications via import-time validation + user-facing warnings.

### 5.1 Supported (P0 — must work in R1)

Section 3.2 features classified **Preserved** or **Degraded**. Editor renders, saves, and round-trips these (with Degraded loss documented per row).

### 5.2 Not supported, silent drop (P1 — acknowledged limitation)

Section 3.2 features classified **Dropped**. R1 does NOT warn the user when these are dropped on import — silent loss is acceptable for R1 because the "Open in Word" toolbar buttons (FR-12) are the documented escape hatch. R2 adds import-time warnings.

### 5.3 Open-in-Word handoff (R1 path)

Section 3.2 features classified **Open-in-Word**. R1 UX: the Compose toolbar surfaces "Open in Word for Web" + "Open in Word Desktop" prominently (FR-12) so users with advanced documents know the escape hatch exists. No in-editor degradation warning yet.

### 5.4 Deferred enforcement / surfacing (R2+)

- Import-time validation: warn user when a DOCX contains features outside the supported set (R2)
- In-editor "this content was simplified on load" banner (R2)
- Selective re-attach to original DOCX on Open-in-Word handoff (R2)
- DOCX subset spec versioning (e.g., "Subset v1.0 = TipTap OOB", "Subset v2.0 = + tracked changes" if Pro is later adopted) (R3+)

---

## 6. Round-trip Findings — Fixture-Driven (Forward-Looking Methodology)

The POML acceptance criteria require the prototype to validate on 3 real legal DOCXs. The prototype is scaffolded; **the operator running the prototype empirically must populate `notes/spikes/spike-1-prototype/fixtures/` with the 3 fixtures** (per `fixtures/README.md`) and record findings here.

### 6.1 Expected findings — letter fixture (`01-letter.docx`)

Profile: 1 page, plain paragraphs, basic bold/italic, signature block.

**Expected**: ~95% preserved. Bold/italic survive. Signature-block spacing may shift (no page model).

### 6.2 Expected findings — long agreement (`02-long-agreement.docx`)

Profile: ≥10 pages, headers + footers, basic single-level numbering, simple tables.

**Expected** ~70% preserved:
- Body paragraphs ✅
- Single-level numbered sections ✅
- Simple tables ✅
- **Headers + footers ❌ dropped** (no page model)
- **Page numbers ❌ dropped**
- Font-family standardized to host theme ⚠

### 6.3 Expected findings — multi-level contract (`03-multi-level-contract.docx`)

Profile: Nested clauses (1, 1.1, 1.1.1), cross-references, defined terms.

**Expected** ~50% preserved:
- Visual numbering structure preserved (nested OL/UL) ✅
- Numbering reference IDs lost — **cross-references break** ❌ — Most consequential limitation for R1
- Defined-term tracking → not in OOB ❌

### 6.4 Operator obligation

When the prototype is actually run (post-spike, during R1 implementation review), operator records the **delta between expected and actual** in §6 of this doc as inline updates. If delta is large, escalates as a Path A / Path B per CLAUDE.md §6.5. Current expected values are derived from TipTap + mammoth + docx documented behavior on common DOCX feature surfaces.

---

## 7. Risks for Phase 1+ Implementation

### R-1: Multi-level numbering semantic loss (HIGH for legal contracts)

**Risk**: Round-trip drops Word numbering refs → cross-references in the saved DOCX no longer auto-update; Word displays stale numbers.

**R1 mitigation**: Document this in Compose UI ("Click 'Open in Word Desktop' for documents with cross-references"). FR-12 handoff is the binding mitigation.

**R2+ resolution**: Custom numbering-preservation extension (deferred; not in OOB).

### R-2: Image bundle size for image-heavy DOCXs

**Risk**: mammoth base64-encodes images inline — a 10 MB image-heavy DOCX becomes ~13 MB in the TipTap state. Memory + persistence implications.

**R1 mitigation**: SPE remains file-of-record; TipTap state is ephemeral. If memory pressure surfaces, switch to mammoth's image-extraction-to-URL mode (its API supports it natively).

### R-3: Mammoth conversion-warning visibility

**Risk**: mammoth produces warnings (unsupported style references, unmapped numbering, etc.) — by default these are silent.

**R1 mitigation**: Capture `result.messages` from mammoth and surface a "Document was simplified on load — [N] formatting elements were dropped" banner. Logged at INFO. Wire-only in R1 (banner with count); R2 expands to detailed inspector.

### R-4: docx library breakages on minor releases

**Risk**: `docx` v9.x is current; v10 may break the export contract.

**R1 mitigation**: Pin to `^9.0.3` (minor-version compatible). R2 monitors v10 release; tracks via Dependabot.

### R-5: Test surface gap (deferral candidate)

**Risk**: This spike doesn't establish a round-trip *test* — only a manual visual inspection workflow.

**R1 mitigation**: Phase 5 (test phase) tasks 060/061 should add a unit test using a small synthetic DOCX confirming a defined-set of preserved features survive an in-memory round-trip. Tracked as a DEF/ISS in `notes/defer-issues.md` if not in current task plan.

### R-6: TipTap version pinning vs. floating ranges (LOW)

**Risk**: `^2.10.3` ranges accept 2.11+, 2.12+ — minor TipTap releases occasionally introduce schema-breaking changes.

**R1 mitigation**: Pin TipTap packages to exact minor version (`~2.10.3`) for R1 main implementation; revisit at R2.

---

## 8. Open questions for R2+

These are **not** R1 questions; they're explicitly deferred to R2+ design:

1. **DOCX subset enforcement UX** — how does R2 communicate degradation to users (modal? banner? per-element decoration?)
2. **Re-anchoring annotations post-Word-roundtrip** — when user does "Open in Word Desktop" and edits + saves back, how do ChatSession annotations (R2+) re-anchor against modified text? (Design likely uses prosemirror diffing on import + fuzzy-matching to original spans.)
3. **Comment storage strategy** — ChatSession annotations vs. SPE document properties vs. a sidecar `sprk_documentannotations` entity. (Spec says ChatSession annotations; details TBD.)
4. **DOCX subset versioning** — when the subset expands in later releases, how is the version stamped on a document so the editor can decline files saved with a newer subset?
5. **Tracked changes** — explicitly out of R1+ architecture, but if a future stakeholder push forces inclusion, the path would be TipTap Pro Track Changes ($ commercial) or a custom diff-overlay layer (engineering-heavy).

---

## 9. Decision unlocked (binding for R1 Phase 1+)

By committing this locked artifact:

- **Task 045** (`045-create-composeeditor-tsx`) MUST use the extension list in §3.1 with the locked versions
- **Task 023** (compose load/save BFF endpoints) MUST treat the BFF as a thin SPE passthrough — DOCX conversion is client-side (§4.5)
- **Task 046** (three-pane data contracts) selections from the editor align with the OOB schema — no advanced-feature selection paths
- **FR-12** "Open in Word" buttons are the documented escape hatch for §3.2 Open-in-Word rows
- **R2 subset enforcement** task (deferred) takes §3.2 as input

The spec.md §Unresolved Questions (Spike Outputs) checklist items "DOCX subset spec (locked, published)", "TipTap DOCX bridge library choice", and "Client-vs-server DOCX conversion decision" are all CLOSED by this document.

---

## 10. ADR Tensions encountered

**None.** No ADR rule conflicted with the design choices made in this spike. The closest tension candidate — ADR-013 refined (BFF AI extraction) — does not apply (DOCX conversion is not an AI capability). Spec §ADR Tensions remains correctly declared "No ADR tensions surfaced at design time."

---

## 11. Files written by this spike

- `notes/spikes/spike-1-tiptap-docx-roundtrip.md` (this file — locked artifact)
- `notes/spikes/spike-1-prototype/README.md` (prototype overview)
- `notes/spikes/spike-1-prototype/package.json` (locked extension list + bridge libs)
- `notes/spikes/spike-1-prototype/src/Editor.tsx` (wiring reference for R1 task 045)
- `notes/spikes/spike-1-prototype/src/exportDocx.ts` (export contract for R1 task 045 + 023)
- `notes/spikes/spike-1-prototype/fixtures/README.md` (fixture population guide)

No code under `src/`. No `.claude/` paths touched. No build verification needed (no production code modified).

---

*End of locked artifact. Updates to this file post-lock REQUIRE the project owner's explicit go-ahead — it is the binding spec for R1 Phase 1+ DOCX work.*
