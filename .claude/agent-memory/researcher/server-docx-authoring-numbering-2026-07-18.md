---
name: server-docx-authoring-numbering-2026-07-18
description: Compose R3 re-architecture validation — server-owns-all-.docx-authoring pattern, OOXML multi-level numbering (NumberingDefinitionsPart) techniques + pitfalls, and how Harvey/Spellbook handle document CREATION. Complements the R2 editing/tracked-changes memos.
metadata:
  type: project
---

# Server-side .docx authoring + multi-level numbering (2026-07-18)

**Question**: Validate Compose R3's move to server-owns-ALL-.docx-authoring (thin TipTap client sends a paraId-keyed content model, never authors bytes; server DocumentFormat.OpenXml renders NEW docs from scratch + deltas onto retained originals for edits). Concrete OOXML numbering techniques + how Harvey/Spellbook do document creation.

**Findings**:

1. **Harvey VALIDATES the architecture, precisely.** Harvey builds documents **server-side** in a mutable in-memory model; parses uploaded .docx into it; the LLM sees a **reduced abstraction (natural-language/structured tools), NOT raw OOXML** — "asking models to simultaneously perform legal reasoning and XML parsing leads to regression in both." Edits are round-tripped: LLM proposes text-level edits → **deterministic backend code** translates to precise OOXML mutations preserving styles/structure. Critically for us: **"List numbering isn't stored with the text… lives in a separate numbering.xml file. Advanced list editing, styles, tables are handled through deterministic backend code rather than model-generated XML."** Two output surfaces: Word add-in → OfficeJS → native track changes; web app → docx service → direct export. This is exactly Spaarke's split (SPE/Word add-in path vs web TipTap path) and exactly the "deterministic code authors OOXML, LLM only touches text" principle.

2. **Numbering (the hard part) — concrete recipe.** One `NumberingDefinitionsPart` → one `Numbering` root. Structure = **`w:abstractNum` (the format template) + `w:num`/NumberingInstance (per-list instance pointing at an abstractNumId)**. Multi-level: each `abstractNum` holds `w:lvl` (ilvl 0-8) with `w:start`, `w:numFmt` (decimal/upperRoman/…), **`w:lvlText`** (the label pattern), `w:lvlJc`, `w:pPr` (indent), `w:rPr`. Compound labels (1 / 1.1 / 1.1.1) come from `w:lvlText val="%1.%2.%3"` — **`%N` = the running counter of level N (1-indexed)**; level 2's text `%1.%2.` yields "1.1". `w:isLgl` forces all displayed levels to Arabic (legal scheme). Paragraph opts in via `w:pPr/w:numPr` with `w:numId` (→ instance) + `w:ilvl`.

3. **Numbering PITFALLS (each cost real people real time):**
   - **Restart:** do NOT clone a new abstractNum per list. Reuse ONE abstractNum; create a **new NumberingInstance (unique numId)** per list that must restart. BUT that alone isn't enough — **add `w:lvlRestart` to the level** or numbering continues across instances (confirmed fix on MS Q&A). For overriding start mid-document, use `lvlOverride/startOverride` on the instance.
   - **Style-linked vs direct numbering:** two ways to attach numbering to a paragraph style. (a) `w:pStyle` **inside a `w:lvl`** = "this level is associated with paragraph style X" (applying the style triggers the level). (b) `numPr` **inside the style's `pPr`**. Plus `w:styleLink`/`w:numStyleLink` pair a numbering definition to a style for the numbering-gallery. Mixing direct `numId` on a paragraph AND a style-supplied numId is the classic "double/ghost numbering" bug. For legal clause schemes, style-linked (Heading1-N → linked multi-level abstractNum) is the robust choice and matches how real firm templates work.
   - Forgetting to Save the part / applying numId that has no instance = silently unnumbered.

4. **Client-side JS export (docx.js) is the wrong tool for fidelity-critical docs.** docx.js *can* emit multilevel numbering + ToC, but it is a **generator, not a fidelity-preserving round-tripper** — no retained-original bytes, re-synthesizes everything, and cannot preserve arbitrary firm styles/headers/footers/embedded objects it wasn't told about. Server OpenXML with the retained original is the recognized correct approach; R3 already dropped docx.js from the export path (design decision, FR-01). For the *from-scratch NEW-doc* case there's no retained original, so the risk is purely "does our generator emit clean OOXML" — that's where the numbering recipe above matters.

5. **Tracked-changes × numbering interplay gotchas:** (a) A `w:del` on the runs of a numbered paragraph leaves an **empty numbered bullet** still consuming a count — to actually remove a list item you must mark the **paragraph mark deleted** (`w:del` in the paragraph-mark `rPr`), which is the same hardest-edge-case R2 flagged (paragraph-boundary deletes). (b) OOXML has a dedicated revision element **`w:numberingChange`** for tracked numbering-property changes — most libraries (incl. our stack) don't emit it; renumbering caused by ins/del is recomputed by Word at open, generally fine, but comparing tools can surface spurious numbering revisions. (c) A from-scratch-rendered doc that later receives tracked edits is fine **provided** numbering is style-linked / instance-clean at birth — malformed numId references become visible only once revisions are layered on. Get numbering right at generation time.

**Sources**:
- Harvey eng blog: harvey.ai/blog/building-an-agent-for-complex-document-drafting-and-editing (server-side in-memory model, deterministic OOXML, numbering.xml quote, OfficeJS vs docx service) — MOST authoritative on legal-AI doc creation
- ZenML LLMOps writeup of same (reversible NL↔OOXML mapping; "legal reasoning + XML parsing → regression in both"; OfficeJS)
- MS Learn API: Numbering, AbstractNum, NumberingInstance, NumberingId, NumberingProperties, NumberingLevelReference, StartNumberingValue, NumberingStyleLink classes (DocumentFormat.OpenXml.Wordprocessing)
- MS Q&A "do I need a new abstract numbering to restart" — lvlRestart + new-instance-per-list confirmed fix
- officeopenxml.com/WPnumbering*, ooxml.info/docs/17/17.9, datypic w:lvl (child-element inventory: start/numFmt/lvlText/lvlJc/pStyle/isLgl/lvlRestart/suff/pPr/rPr)
- Prior Spaarke memos: [[openxml-docx-compose-r2-2026-06-29]] (SDK status, comments/ins/del), [[adeu-architecture-study-2026-06-29]] (deterministic-code-authors-OOXML, LLM-only-text asymmetry)

**Open questions**:
- Spellbook internals unpublished (no eng blog) — only "uses Word native Track Changes" confirmed; assume Word-add-in-native, not server render.
- Does Spaarke's chosen NuGet (Docxodus) expose clean numbering authoring, or only WmlComparer? R3 design uses it for delta; from-scratch numbering may need raw DocumentFormat.OpenXml.
- Whether R3 will actually take on complex multi-level numbering for the from-scratch path — design.md line 219 currently lists it OUT of scope. This research implies it should be IN scope if NEW-doc authoring is first-class.

**Related to**: [[openxml-docx-compose-r2-2026-06-29]], [[adeu-architecture-study-2026-06-29]]
