# Pattern Spec: Canvas / Document-centered

> **Status**: Draft v0.1
> **Pattern slug**: canvas-document
> **Closest analogous product:** Word + Acrobat (with reference to Adobe AI Assistant for PDF, Ironclad for contract redlining, and Notion / Google Docs for collaborative annotation)
> **Primary user intent:** *"I'm reading, annotating, or drafting this specific document."*

---

## 1. Optimizes For

The Canvas pattern optimizes for **focused work on a single artifact** — reading it, annotating it, redlining it, drafting it. The document is the center of the user's attention; everything else is supporting cast.

For legal work this pattern is heavier than the generic "document viewer" idiom suggests. A contract review is not "look at the PDF" — it's read against a playbook, mark up departures, compare to a prior version, attach the comments to specific clauses, and produce either an executed copy or a redline package. The Canvas in Spaarke must support all of this.

---

## 2. When To Use / When Not To Use

### 2.1 Use this pattern when

- The user has identified a specific document and now needs to *engage* with it (read carefully, mark up, redline, sign off).
- The work is at the level of the document's *content* — clauses, paragraphs, specific language — not at the level of its properties (date, party, type).
- The user may need to compare to another version of the same document or to a reference (a playbook, a prior contract, a standard).
- The output of the work is either annotations on the document, a redlined version, an executed version, or a derived artifact (summary, memo).

### 2.2 Do not use this pattern when

- The user is selecting *which* document to work on (Queue or a Summary list).
- The work is at the metadata level only (Entity pattern — work on the contract record, not the contract document).
- The document is so simple it doesn't warrant a focused canvas (a 1-page form letter).
- The output is structured data extracted *from* the document with no need to mark up the document itself — that may be a Form pattern with the document as a reference.

### 2.3 Pattern overlaps

- **Entity-centered**: The contract document and the contract record are different things. Entity shows the record (counterparty, value, dates). Canvas shows the document (clauses, language, redlines). They appear together — Canvas in one Workspace tab, Entity in another, or Entity in the Context pane while Canvas is in Workspace.
- **Queue / Inbox-centered**: Queue feeds Canvas. The transition (Queue row → Canvas in Workspace) is one of the highest-frequency in the system.
- **Generative / Conversational**: The user can ask questions about a document on Canvas in the Conversation pane. Answers cite back to specific clauses; clicking a citation highlights the clause on Canvas. This bidirectional binding is core to AI-augmented document work and the Generative pattern's most consequential downstream pattern.

---

## 3. Mechanics

### 3.1 Core mechanics

1. **Full-document rendering with fidelity.** Margins, formatting, headers, tables, signature blocks — these matter in legal contexts. A document that renders as "approximately Word formatting" is unacceptable; the document must look like itself.

2. **Continuous reading with section navigation.** Scroll is the primary navigation. A document outline / table of contents is available but not the primary mode. Users read sequentially and jump occasionally; design for sequential, support jumping.

3. **Selection and annotation on any text.** Select a phrase, comment, highlight, ask a question. The selection persists; the annotation is bound to the specific text, not to a line number that breaks if the document changes.

4. **Redline mode.** Track-changes-style redlining where the user can propose changes that show as additions / deletions / format changes. Acceptance / rejection per change. This is the table-stakes mechanic for contract review.

5. **Compare to another version.** Side-by-side or inline diff against a prior version, a counterparty's version, or a playbook standard. This is non-negotiable for legal contract work.

6. **Cross-pane interaction.** Selecting text on Canvas can drive the Conversation pane ("ask about this clause") or the Context pane (show related clauses, playbook standard, prior agreements). Selecting in Conversation can drive Canvas (citation click jumps to the cited clause). This bidirectional binding is what makes Spaarke's three-pane shell pay off for document work.

7. **Save and export.** The document can be exported (PDF, DOCX with redlines, clean PDF, redline package). Save state is visible. Document version history is accessible.

### 3.2 Supporting mechanics

1. **Page-and-line navigation** — for users who reference documents by "page 4, line 12." The convention is alive in legal practice and dropping it will frustrate users even if scroll-based navigation is faster.

2. **Find-in-document** with persistent highlight of matches and easy navigation among them.

3. **Multiple-document tabs within Canvas** — a user comparing three NDAs against a playbook may need the playbook open alongside. The three-tab Workspace ceiling means this has to be handled inside Canvas, not as three Workspace tabs.

4. **Read-only mode** for documents the user shouldn't edit (executed contracts, sealed filings) with the read-only state visible, not just enforced.

### 3.3 AI augmentation mechanics

This is where Canvas changes most under AI. The pattern's current literature (Adobe AI Assistant, Ironclad's AI redlining, contract review AI more generally) has converged on several behaviors.

1. **Cite-back from chat to Canvas.** When the user asks a question about a document in the Conversation pane, the answer includes citations to specific clauses. Clicking a citation jumps to and highlights the clause on Canvas. This is the foundational AI-Canvas binding.

2. **AI-proposed redlines.** "Mark this NDA against our playbook" produces redlines that show as track-changes-style proposals, distinct from user-entered redlines. The user accepts, rejects, or modifies each.

3. **AI-surfaced concerns on specific clauses.** Inline flags: "this indemnification cap is unusual," "this jurisdiction clause conflicts with the playbook." These appear bound to specific text, not as a separate panel.

4. **AI summary of the document.** Short summary at the top of Canvas: "12-page MSA from Counterparty X. Notable departures from playbook: indemnification cap, governing law. Standard in other respects." This is a fast-read for users opening a document they haven't seen.

5. **AI compare-to-playbook.** When opening a document, the system can pre-render a comparison view against the relevant playbook standard. This is where the document's *purpose* (a contract review) and the AI overlay align.

6. **Distinct visual treatment for AI-generated vs user-entered content.** Critical for legal use: a user must always be able to tell which words on the page came from them, from the counterparty, and from AI. The convention belongs in the AI Augmentation cross-cutting spec but Canvas is where it matters most.

---

## 4. Expectations to Honor (Closest Analogous Product: Word + Acrobat)

Canvas is the trickiest "analogous product" call because legal users straddle Word (for DOCX work) and Acrobat (for PDF review). The conventions overlap but aren't identical. Spaarke should honor whichever is dominant for the document type and not invent third conventions.

### 4.1 What Word and Acrobat do that Spaarke must match

**Shared by both:**
- Continuous scroll with page breaks visible.
- Selection by drag, double-click for word, triple-click for line/paragraph.
- Find with Ctrl+F, navigate matches with arrow keys / Enter.
- Comment / annotation bound to selected text.
- Track changes / markup that distinguishes proposed from accepted edits.
- Right-click context menu on selection (copy, comment, search, etc.).

**Word-specific (DOCX work):**
- Track-changes-style redlining with accept / reject per change.
- Outline / heading navigation pane.
- Compare two versions with merged-redline view.

**Acrobat-specific (PDF work):**
- Bookmarks pane.
- Page thumbnails for navigation.
- Form fields if the document has them.
- Sticky-note style comments distinct from text annotations.

### 4.2 Where Spaarke legitimately diverges

| Spaarke divergence | Why |
|---|---|
| Chat-on-the-side as a first-class interaction with the document | This is the three-pane shell's value proposition for documents; neither Word nor Acrobat has this natively at the depth Spaarke proposes |
| AI redlines visually distinct from user redlines | Word's track-changes doesn't natively distinguish source — Spaarke must, for legal accountability |
| Playbook overlay as a first-class compare-target | Word's compare-to-prior-version is the closest analog, but a playbook isn't a prior version — it's a standard. The mechanic is similar but conceptually distinct, and the UX should signal that. |
| Citation surfaces from chat that jump to and highlight document text | A meaningful departure from how Adobe AI Assistant handles citations (which open a side panel rather than jumping). Spaarke's panel layout makes the jump-and-highlight approach feasible and superior. |

---

## 5. Common Failure Modes

| Failure mode | Symptom | Fix |
|---|---|---|
| **AI redlines indistinguishable from user redlines.** | The user can't tell, three days later, what they actually reviewed vs what the AI suggested. | Distinct visual treatment, mandatory and consistent. The convention belongs in AI Augmentation cross-cutting; Canvas is where violation is most costly. |
| **Comparison view that doesn't say what changed.** | Side-by-side that just shows two documents without highlighting differences is barely better than two windows. | Compare mode highlights additions / deletions / format changes; navigation jumps to the next change. |
| **Citations that don't jump.** | Chat says "as noted in section 4.2" with no link, or a link that opens a generic search. | Citations are first-class binding to specific text. If they can't jump-and-highlight, they shouldn't render as citations. |
| **Annotations that come loose.** | User commented on "section 3.2" but the document was edited and now the comment is attached to a different paragraph. | Anchor annotations to text content, not to line numbers. When the anchor text is gone, the annotation is shown as orphaned, not silently relocated. |
| **Redline export missing the redlines.** | User exports for the counterparty and the file is clean — no track changes. | Export options must be explicit (clean, with-redlines, with-comments) and the default should match user expectation by context. |
| **Document fidelity drops in rendering.** | Margins shift, tables break, signature blocks misalign. | Render high-fidelity. If rendering can't match the source, surface a "rendering may differ from source" warning, not a quiet downgrade. |

---

## 6. Spaarke Component Mapping

### 6.1 Current design positions

| Pattern element | Current Spaarke component |
|---|---|
| Canvas widget | **DocumentViewer** (spec.md FR-205); **RedlineViewerWidget** is a related new widget for R2. |
| Pane assignment | Workspace pane (Canvas is primary work surface for document engagement). |
| Cross-pane to Conversation | spec.md FR-207 names a cross-pane interaction protocol; design.md §2.2 describes text-selection cross-pane interaction in general terms. |
| AI augmentation | AI safety / streaming / groundedness covered in design.md §9 at the Conversational pattern level. Canvas-specific AI mechanics (cite-back, inline flags, redline proposals) aren't fully specified. |

### 6.2 Design-challenge findings

| Finding | Current design | Pattern requires |
|---|---|---|
| **Cite-back mechanic from Conversation to Canvas needs concrete specification.** | spec.md FR-207 and design.md §2.2 reference cross-pane interaction but the specific click-citation-jump-and-highlight behavior isn't specified end-to-end. | A concrete behavior spec: how citations are formatted in chat output, what happens on click, how the highlight on Canvas decays, whether multiple citations from one chat message render as a navigable set. Goes into AI Augmentation cross-cutting and the Composition Guide for the Generative → Canvas transition. |
| **AI-vs-user visual distinction is not a defined convention.** | Mentioned implicitly in design.md §9 (groundedness annotation) but not as a uniform "AI content looks different" rule. | A consistent visual language for AI-generated content across patterns. Belongs in AI Augmentation cross-cutting. Canvas is where the cost of getting it wrong is highest. |
| **Playbook overlay as a Canvas mode isn't specified.** | Documents are documents; playbooks are referenced elsewhere in the architecture but not as a first-class compare-target for Canvas. | A "compare against playbook" mode on Canvas, structurally similar to compare-against-prior-version but with playbook-specific UI. Engineering call on whether this is a Canvas mode, a separate widget, or both. |
| **Multi-document-within-Canvas isn't specified.** | DocumentViewer is one document per widget. Comparison mode is the closest existing concept. | When a user compares three NDAs against a playbook (four documents total), the three-tab Workspace ceiling forces this to happen *inside* Canvas, not across tabs. Multi-document Canvas needs specification. |
| **Retroactive groundedness annotation in legal contexts.** | design.md §9.2.2 specifies streaming output with retroactive groundedness annotations on the chat side. | Open question: when chat output cites Canvas content, and the citation appears retroactively (after streaming completes), what does the user see on Canvas during the gap? Highlight pre-citation? After? This is a meaningful UX question for legal accuracy and timing. |

### 6.3 New components / events / widgets proposed

- **Cite-back binding protocol** — how chat citations link to and highlight Canvas content. Belongs in Composition Guide (Generative → Canvas) and AI Augmentation cross-cutting.
- **AI-vs-user visual distinction convention** — belongs in AI Augmentation cross-cutting, applied to Canvas with highest stakes.
- **Playbook-overlay Canvas mode** — engineering call on form factor.
- **Multi-document Canvas mode** — within-widget version of comparing more than two documents.

---

## 7. Open Questions

| Question | Why it matters | Validation |
|---|---|---|
| For PDF review, do we render the PDF natively (preserving original layout) or extract text and re-render? | Layout fidelity vs. searchability and AI integration. Native rendering is faithful but harder to bind AI annotations to. Re-rendering is more flexible but breaks fidelity. | Engineering call + prototype testing |
| When the AI proposes redlines, does it propose them as a *batch* (all at once) or *progressively* (as it reads through)? | UX feel and the user's ability to disagree mid-stream. Progressive is more "alive" but harder to review systematically. | Prototype testing |
| When the user is on Canvas and chats about the document, does the chat have implicit context of the document, or must the user reference it? | Most likely implicit, but the binding mechanics affect how unambiguous the chat output's groundedness is. | Engineering call + designer judgment |
| For comparison views, is side-by-side or inline-diff the default? | Both have legal-practice precedent; user preference varies. | Prototype testing; possibly a per-user preference |
| For long documents (100+ pages), how does the AI summary remain useful and accurate? | Quality and trust at scale. A summary that's wrong on a 100-page MSA is more harmful than no summary. | Pilot instrumentation + engineering on chunking and quality bounds |

---

*Draft v0.1 — 2026-05-18.*
