# Spaarke Compose R3 — Word-Feature Fidelity Project (Seed)

> **Status**: **Design authored + scope locked + ALL PRE-SPEC SPIKES PASSED (2026-07-16)** — see [`design.md`](design.md) (code-grounded + refreshed with July-2026 best-practices research: [`notes/tiptap-docx-fidelity-research-2026-07-16.md`](notes/tiptap-docx-fidelity-research-2026-07-16.md); spikes: [`notes/spikes/`](notes/spikes/)). Owner review locked D1–D4 (design.md §0): hybrid retained-original delta save (Docxodus `WmlComparer`); full scope (fidelity core + E3 grounding-tied confidence + editing toolset + import round-trip); text+run-level fidelity. **Spikes S1/S1b/S2/S3/S4/S5 all passed — no design pivots.** **Next: `/design-to-spec projects/spaarkeai-compose-r3`.** (Sections below are the original 2026-07-01 seed, superseded where design.md differs.)
> **Status (original)**: Draft / seed — created 2026-07-01 during R1 UAT close-out
> **Author**: R1 close-out session (see `../spaarkeai-compose-r1/`)
> **Parent lineage**: R1 (Path A modal + TipTap editor + AI-dispatch scaffolding) shipped
> as the baseline; R2 (three-pane pivot + streaming Assistant panel + facade extension)
> ships in parallel with R1 close-out; **R3 is the fidelity project**.
> **Not yet**: spec / plan / task decomposition. This document scopes the problem
> space so a proper `/project-pipeline projects/spaarkeai-compose-r3` can be run later.

> **🔎 2026-07-14 UPDATE — read [`ooxml-fidelity-findings.md`](ooxml-fidelity-findings.md) first.**
> A grounded gap-check during R2 close-out mapped the shipped Compose save/redline path
> against the fidelity target and formalized **three enhancements (E1/E2/E3)** with verdicts:
> **E1** retained-original OOXML delta save (= Scope **A**, the keystone — GENUINELY MISSING for
> edited saves), **E2** `paraId` anchoring (= A/E — MISSING, coupled to E1), **E3** enriched redline
> contract (= G/H — PARTIALLY present; rationale already ships, confidence+offsets missing).
> The findings doc also **corrects this seed**: R2 already shipped **home-grown track changes +
> comments** (native `w:ins`/`w:del`/`w:comment`, no TipTap Pro), so Scope **B/C** authoring is DONE
> — R3's B/C work is now the **import round-trip**. Constraint reaffirmed: **no TipTap product
> features, paid or unpaid.** E1/E2/E3 are the prioritized R3 core; `design.md` is authored next.

---

## Motivation

R1's mammoth → TipTap → docx round-trip was locked in Spike #1 with a
documented scope: preserve prose text; accept that "advanced Word features"
would be lost on save. Live UAT 2026-07-01 confirmed the round-trip
does lose:

- **Paragraph & character styles** (Normal, Heading 1, custom styles, direct formatting)
- **Theme** (colors, fonts)
- **Section formatting** (headers, footers, page numbers, margins, columns)
- **Tables of contents / lists / bibliographies** (are converted to plain text on import)
- **Fields** (dates, page numbers, cross-references, mail-merge tokens)
- **Track changes / revision marks**
- **Comments** (Word `<w:comment>` elements)
- **Embedded objects** (equations, SmartArt, charts, embedded Excel, pictures with cropping)
- **Ink annotations**
- **Bookmarks**
- **Content controls / structured document tags**
- **Custom XML parts**

R1 shipped the correct architecture per Spike #1; R3 is the project that
closes the gap between "R1 as specced" and "R1 as legal users actually need it."

---

## Scope Areas

The following are candidate R3 features. Not all will make R3 — the
`/project-pipeline` step will produce a scoped spec.

### A. Preserving Formatting on Save (highest priority)

**Problem**: Every Save currently emits a brand-new DOCX that has only what
mammoth's simplified HTML could represent. Styles, theme, and structural
elements are dropped even when the user made no changes touching them.

**Approach candidates**:
1. **Original-DOCX preservation + patch**. On Load, retain the original DOCX
   bytes server-side (keyed by `sprk_documentid` + `versionId`). On Save,
   diff the current TipTap JSON against the DOCX derived at Load time; open
   the original DOCX with `DocumentFormat.OpenXml` (server-side); patch only
   the paragraphs whose text changed. All non-edited paragraphs, styles,
   and structural elements pass through unchanged.
   - Pros: preserves everything except what the user explicitly edited.
   - Cons: paragraph-diff algorithm is non-trivial when users add/remove
     paragraphs; requires TipTap → OOXML paragraph mapping that preserves
     order.
2. **Word Online / Graph API delegation**. Use Microsoft's Word for the Web
   editing API to apply edits to the source DOCX. Requires Graph API for
   files' `permanentDelete` / SPE-specific file edit endpoints. Substantial
   new integration.
3. **Read-only Compose**. Ship Compose as a "read + AI-assist + Open in
   Word for edits" tool. No Save from Compose. Users use Open-in-Word for
   any Save work. Lowest complexity, but reduces value.
4. **Import annotations only**. Compose adds AI-generated annotations
   (comments, suggestions) to the source DOCX without touching prose.
   Preserves everything by design. Doesn't support text editing at all.

### B. Track Changes / Revision Marks

> **⚠️ SUPERSEDED (2026-07-14): AUTHORING is DONE.** R2 shipped a **home-grown** revision-marking
> system — our own TipTap marks → `DocxAnnotationWriter` emitting **native OOXML `w:ins`/`w:del`**,
> with an accept/reject workflow. **No TipTap Pro** (the "License TipTap Pro" option below is
> RETIRED — constraint reaffirmed: no TipTap product features, paid or unpaid). See
> [`ooxml-fidelity-findings.md`](ooxml-fidelity-findings.md) §Current State.

The remaining R3 gap is **IMPORT round-trip**:
- **Read existing DOCX revision marks** on Load and render them in the editor (currently only
  *authored* marks are handled; a doc that already has Word revisions doesn't round-trip its
  history in). Depends on the E1/E2 OOXML-identity work (retained-original + `paraId`).

### C. Comments (Word `<w:comment>` elements)

> **⚠️ SUPERSEDED (2026-07-14): AUTHORING is DONE.** R2 writes new comments as **native
> `<w:comment>`** via `DocxAnnotationWriter` (anchored-annotation pipeline). R1's "MUST NOT store
> comments as `w:comment`" was lifted in R2.

The remaining R3 gap is **IMPORT round-trip**:
- Read source DOCX comments on Load and render them as first-class comment threads in the editor
  (author, timestamp, response threads), preserving them across the retained-original save (E1).

### D. Fields (Dates, Cross-References, Mail-Merge Tokens)

Fields render dynamically in Word based on document state. In TipTap
they'd need either:
- **Rich placeholders**: render as visible text with distinct styling;
  editable but non-computed. Round-trips as field text without the field
  code (loses computation).
- **Read-only pass-through**: freeze field values at Load, prevent editing
  the field text, restore field codes on Save. Preserves computation on
  the Word side.

### E. Advanced Structural Elements

**Tables**: TipTap has table support (loaded in R1 LOCKED_EXTENSIONS) but
Word tables can nest and have styles/borders/shading that TipTap won't
render identically. Options: enrich table extension; downgrade
formatting on Save with warning.

**Headers/Footers/Section Breaks**: no TipTap concept. Preserve via
original-DOCX-patch approach (A.1).

**Table of Contents**: currently a `<w:sdt>` structured document tag with
field codes. TipTap doesn't render this natively. Options: convert to
static text on Load; preserve field code on Save via A.1; or add a TOC
extension.

**Footnotes / Endnotes**: no TipTap concept. Similar to comments — needs
custom implementation or original-DOCX passthrough.

### F. Fonts, Colors, Theme

Word themes let styles reference theme colors/fonts. If the user edits
theme-based styled text in TipTap without a theme, the color is lost.
Options: import theme, apply as computed styles in TipTap (loses theme
binding); preserve theme via A.1; ignore.

### G. Selection-Scoped AI (Rewrite / Explain / Find Similar / Lookup References)

Currently R1 has: whole-document Summarize (via `compose-summarize`
consumer type). R3 candidate: selection-scoped consumer types:
- `compose-rewrite` — user selects text, asks AI to rewrite in a specific
  tone / register / clarity
- `compose-explain` — user selects a legal clause, asks AI to explain it
- `compose-find-similar` — user selects a passage, asks AI to find similar
  clauses in the matter's document set
- `compose-lookup-references` — user selects a citation, asks AI to look up
  the cited authority
- (extensible) other AI functions surfacing in the Assistant pane

Each is a new sprk_playbookconsumer row + a JPS scope + a UI trigger. R2
lays the streaming Assistant Pane groundwork that R3 leverages for
selection-scoped functions.

### H. Insert AI Response into Document

Currently AI responses render in the Assistant pane (R2) or a banner (R1).
R3 candidate: insert AI response into the editor at cursor, or replace
selection.

Approach: Assistant Pane exposes an "Insert" action per response;
ComposeEditor exposes `.replaceSelection(html)` / `.insertAtCursor(html)`
imperatives; dispatch bridges the two via PaneEventBus.

### I. Version History Integration

Word has version history in SharePoint. Compose Save creates new SPE
versions (via SaveDocxAsync). R3 candidate: expose the version history in
Compose UI so users can browse / revert.

### J. Collaborative Editing (multi-user)

**Explicitly out of R1** per design.md ("NO multi-user co-editing in R1;
CRDT deferred to R5+"). Named here for completeness — not R3 scope, but
worth noting as it interacts with Track Changes, Comments, and Save.

---

## Related Work / Dependencies

- **R1** (`projects/spaarkeai-compose-r1/`): baseline; landed the mammoth
  + TipTap + docx architecture, Path A modal, `/api/compose/action/`
  endpoint scaffold.
- **R2** (working title — separate project to be created): three-pane
  pivot, ConversationPane integration, streaming Assistant panel, facade
  extension for document-context invocations. Currently WIP in-session
  as R1 close-out extension.
- **Spike #1** in R1: locked the mammoth-based scope. R3 revisits that
  scope decision with the fidelity requirement in mind.
- **`docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md`**: contains
  the shared-library boundary rules Compose consumers must follow. R3
  work MUST keep Compose Components in `@spaarke/compose-components` and
  keep any Word-integration primitives factored so future Word-adjacent
  work (e.g., Excel via ExcelJS) can reuse patterns.
- **ADR-013 (refined)**: R2's facade extension for document-context
  invocations paves the way for R3's selection-scoped consumer types.

---

## What NOT to build in R3

- Track-changes UI in Compose editor (defer to a dedicated R4+ project if
  we decide to take it on)
- Multi-user co-editing (R5+ per design.md)
- New TipTap Pro dependencies (per R1 licensing constraint — carry
  forward through R3 unless explicitly reversed via ADR)
- SharePoint version-history rewrites (out of scope; consume existing
  version endpoints)

---

## Next Steps

1. When R1 + R2 close, run:
   `/design-to-spec projects/spaarkeai-compose-r3` (or a manually authored
   spec.md if scope is small enough)
2. `/project-pipeline projects/spaarkeai-compose-r3` to generate plan, task
   decomposition, portfolio registration.
3. Kick off with prioritized subset of A–I above based on live-user
   feedback from R1 + R2.

---

## Feedback Log (append here as it comes in)

| Date | Source | Feedback |
|---|---|---|
| 2026-07-01 | R1 UAT | "when it saves the WORD document loses its original formatting" — motivating factor for R3 A (preserving formatting on Save) |
| 2026-07-14 | R2 gap-check | Grounded verdicts: **E1** retained-original delta save GENUINELY MISSING for edited saves (any dirty Save rebuilds lossily from TipTap via `tipTapJsonToDocxBytes`); only unedited saves round-trip. Root of R1+R2 fidelity pain. → Scope A. See [`ooxml-fidelity-findings.md`](ooxml-fidelity-findings.md). |
| 2026-07-14 | R2 gap-check | **E2** `paraId` anchoring MISSING (fuzzy text+index today; mammoth import discards paraIds) — coupled to E1 as one OOXML-identity workstream. → Scope A/E. |
| 2026-07-14 | R2 gap-check | **E3** enriched redline contract PARTIALLY present — rationale + sources carried and rationale already surfaced in accept/reject UI; **confidence** signal + explicit offsets missing (offsets ride with E2). → Scope G/H. |
| 2026-07-14 | R2 status | Track changes + comments AUTHORING shipped home-grown in R2 (native `w:ins`/`w:del`/`w:comment`, no TipTap Pro) → Scope B/C authoring DONE; **import round-trip** is the remaining R3 gap. |
| 2026-07-14 | Owner (compose-r2 UAT) | Assistant/Workspace **pane UI redesign** is a SEPARATE project, sequenced AFTER compose-r2 merges (freeze the Compose output seam first) — NOT R3 scope; noted here for lineage. |

---

*This is a project seed, not a spec. Update this file as R1 close-out
UAT + R2 shipping produce feature requests + defect reports that belong
in R3 scope.*
