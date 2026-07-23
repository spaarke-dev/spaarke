# External Senior-Engineer Reviews — Compose Fidelity (captured 2026-07-22)

> **Why this file exists**: Two independent senior-engineer reviews were provided by the owner on 2026-07-22. Both independently prescribe the SAME architecture (OOXML source-of-truth + editor-as-projection + ID-anchored operational deltas + surgical patch). They are captured verbatim here because they existed only in conversation. They are the primary external evidence base for the R4 rip-and-replace.
>
> **Convergence**: These two + the earlier fix-review pair (`../../spaarkeai-compose-r3/notes/CLAUDEREVIEW-docx-html-converter-design.md`, `../../spaarkeai-compose-r3/notes/GPT-compose-docx-html-conversion-architecture-review.md`) all point to the Shadow Document / Patch model. Convergence across four independent reviewers is the confidence basis for D1–D5 in `design.md`.

---

## Review A — "docx → editor → docx is the root cause" (verbatim)

> The root cause of both problems is the same architectural decision: treating the conversion as docx → editor model → docx. Any pipeline that regenerates the docx from the editor's document model will lose fidelity, because Tiptap/ProseMirror's schema can only represent a small subset of WordprocessingML (no fields, content controls, section properties, numbering definitions, styles inheritance, floating objects, etc.). The fix is to invert the mental model: the OOXML package remains the single source of truth, and the editor is a lossy projection used only to capture user intent. Edits are applied surgically back to the original XML, never by re-serializing the editor state.
>
> **Architecture**
>
> 1. **Source of truth: the original .docx package, untouched.** Store it (SharePoint Embedded, in your case) and never regenerate it wholesale. All parts you don't render — styles.xml, numbering.xml, theme, settings, headers/footers, embedded objects — survive by definition because you never touch them.
>
> 2. **Ingest with provenance, not conversion.** Parse document.xml server-side and project it into a Tiptap document where every node carries anchor attributes back to the source:
>    - w14:paraId on every paragraph (Word 2013+ emits these; if missing, inject them once on ingest and save — they're stable, unique per paragraph, and survive Word round-trips). This is your primary anchor.
>    - Run index and character offset within the paragraph for sub-paragraph positioning.
>    - Constructs your editor can't render (SDTs/content controls, fields, complex tables) become opaque atom nodes in ProseMirror — visible placeholders the user can't edit inline, but which hold their paraId so document order is preserved.
>
>    This is the piece that kills your "can't find insertion point" error. Right now you're almost certainly doing text-search against a flattened string, and it fails whenever whitespace, run splits, or lost formatting cause the projected text to diverge from what the AI or the matcher expects. With provenance attributes, a Tiptap selection maps deterministically to (paraId, runIndex, charOffset) — no searching at all.
>
> 3. **Edits as operations, not documents.** User and AI edits become a patch list: {anchor: paraId + offset, op: insert|delete|replace|comment, content, author, timestamp}. AI redlines are emitted as native OOXML tracked changes (w:ins/w:del with author = "Spaarke AI") and comments as w:comment + commentRangeStart/End. This gives you a huge product win: the output opens in Word with real, accept/rejectable redlines and threaded comments — lawyers stay in their native review workflow.
>
> 4. **Apply patches with Open XML SDK.** A .NET service (fits your Azure stack) takes the original package plus the patch list, locates paragraphs by paraId, splits runs at the required offsets, injects w:ins/w:del/comment ranges, and writes the package back. DocumentFormat.OpenXml is the right tool; you're doing targeted DOM surgery on document.xml and comments.xml only.
>
> 5. **Anchor the LLM the same way.** When you send content to the model for drafting/redlining, send it as structured segments with IDs — [{paraId: "4F2A1C3B", text: "..."}] — and require the response as JSON operations referencing those IDs ({paraId, action, position, text, rationale}). Validate every returned anchor against the document before applying; if the model returns an ID that doesn't exist or an offset out of range, reject and retry. Keep a fuzzy-match fallback (diff-match-patch style) only as a last resort, with a confidence threshold below which you surface the suggestion as a comment rather than an inline edit.
>
> **Concurrency caveat**
>
> Offsets go stale the moment the user types. Two standard mitigations: use ProseMirror's position-mapping (its transform system rebases positions through concurrent steps — this is exactly what it's built for) between the time you snapshot for the LLM and the time the patch arrives; and version-stamp each AI request so a patch against a stale snapshot gets rebased or re-anchored before applying.

---

## Review B — "Shadow Document Architecture" (verbatim)

> This is the classic "Web Editor vs. Word" problem. What you are experiencing is the fundamental mismatch between the Office Open XML (OOXML) schema and web-based rich text schemas (like Tiptap/ProseMirror's JSON or HTML). OOXML contains thousands of layout rules, custom styles, pagination instructions, and metadata fields that Tiptap simply does not have the schema to hold. When you convert OOXML to Tiptap, you strip away that metadata. When you convert back, you are effectively creating a brand-new Word document from scratch, which is why the fidelity is destroyed.
>
> To solve both the fidelity loss and the AI insertion errors, you must move away from a "convert-edit-convert" model and adopt a **Shadow Document Architecture** (often called the "Patch" or "Delta" approach).
>
> **The Best Practice Architecture**
>
> Instead of treating Tiptap as the source of truth, treat it as a view and a controller. The true state of the document remains the OOXML file (usually held on the backend).
>
> 1. **Ingest & Tag (Backend):** Do not just convert to HTML. Parse the uploaded .docx using an OOXML SDK (like OpenXML SDK in C# or docx4j in Java/Node). Inject a unique ID (e.g., a custom XML attribute or bookmark) into every structural node (w:p for paragraphs, w:r for runs, w:tbl for tables).
>
> 2. **Generate the Web View:** Convert the tagged OOXML into HTML/JSON for Tiptap. Crucially, map the injected OOXML IDs to data-word-id attributes on your Tiptap HTML elements. You must extend Tiptap's schema (Paragraph, Text, Table extensions) to retain these ID attributes.
>
> 3. **Track Edits via Deltas (Frontend):** Stop exporting HTML. When the user or AI makes a change, do not export Tiptap's getHTML(). Instead, capture the exact operation using ProseMirror's Transaction steps (e.g., "Insert text 'X' at position Y inside Node ID 1234"). Send these exact instructions (deltas) to the backend.
>
> 4. **Patch the Shadow Document (Backend):** The backend receives the delta, locates the corresponding w:p or w:r node in the original OOXML using the unique ID, and applies the text change, deletion, or format adjustment. This preserves all surrounding Word metadata, margins, and complex headers because the rest of the XML tree is never touched.
>
> **Solving the "Can't Find Insertion Point" AI Error**
>
> The AI insertion error happens because of state drift. When the user highlights text and clicks "Generate," the AI takes a few seconds to respond. By the time the AI returns the text, the Tiptap document state has changed, or the index positions have shifted, causing the insert to fail. Here is how to fix it within Tiptap/ProseMirror:
>
> - **Use ProseMirror Bookmarks/Decorations, not absolute positions:** When the user highlights text to prompt the AI, immediately insert a ProseMirror Decoration (an invisible marker) or save a Selection.getBookmark() at that exact spot.
> - **Assign a Transaction ID:** Tie the AI request to this specific bookmark.
> - **Resolve on Return:** When the AI payload returns, ask ProseMirror to resolve the bookmark to the current valid position in the document, regardless of what the user has typed elsewhere in the meantime. Insert the AI text there.
> - **Pass Context via IDs:** Send the data-word-id of the highlighted paragraph to the AI as context, so the resulting delta sent to the backend knows exactly which OOXML node to patch.
>
> **Recommended Components**
>
> Building a flawless OOXML-to-HTML parser from scratch takes years. You have two paths depending on your budget and engineering capacity:
>
> 1. **The Custom Open-Source Stack**
>    - Backend: Use OpenXML SDK (.NET) or docx4j (Java/Node). These are the only reliable libraries for safely traversing and patching document.xml without corrupting it.
>    - Frontend: Tiptap / ProseMirror. You will need to heavily customize the Tiptap schema to support data-word-id attributes on every node.
>    - Conversion: Do not use mammoth.js for this. Mammoth explicitly strips styles to create clean HTML, which guarantees fidelity loss. You will need to write a custom XML-to-HTML mapper that maps w:p to <p> while carrying over the IDs.

---

## Spaarke reconciliation (how these map to our decisions)

| Review point | R4 decision | Note |
|---|---|---|
| OOXML source of truth, editor is projection | D-invariants I-1/I-2/I-6 | Already our re-grounded framing |
| paraId provenance on ingest; inject if missing | Phase 1 (T01.2) | Phase-1 (r3) already built the custom mapper; extend it |
| Edits as operations, not re-serialize | D1 (step-level deltas) | THE spine decision |
| Anchor by paraId (+ offset); Review B says also per-run | D2 | We anchor paraId + offset, NOT run-ids (run-ids don't survive Word round-trips — see research digest) |
| Apply with Open XML SDK, native w:ins/w:del/w:comment | D5 (unified Patch Engine) | Replaces both current writers |
| Anchor the LLM by ID; JSON ops back; fuzzy-as-comment last resort | Phase 4 | `AnnotationReanchorService` becomes the fuzzy last resort |
| Bookmark/decoration for AI generate-window drift | Phase 4 (T04.1) | The one genuinely NEW insight vs. our prior plan |
| Version-stamp + ProseMirror position-mapping for concurrency | Phase 5 | Our Bug B family |
| "Do NOT use mammoth — write a custom w:p→<p> mapper" | Vindicates Phase-1 | `ComposeDocxProjectionBuilder` already did exactly this |
