# R4 Research Digest — Shadow Document Architecture

> **Created**: 2026-07-22
> **Purpose**: The consolidated research basis for R4. Combines (a) the July-2026 fidelity research carried from R3, (b) the four converging external reviews, and (c) the correction of framing drift. Feeds `design.md`.
> **Primary sources (do not re-derive — read these)**:
> - `senior-reviews-2026-07-22.md` (this folder) — two external reviews verbatim.
> - `../../spaarkeai-compose-r3/notes/tiptap-docx-fidelity-research-2026-07-16.md` — primary-source-cited: TipTap licensing, OOXML round-trip libraries, AI-authoring UX. **Still current.**
> - `../../spaarkeai-compose-r3/notes/compose-clean-slate-architecture.md` — the from-scratch derivation (invariants I-1…I-7).
> - `../../spaarkeai-compose-r3/notes/compose-shadow-document-RIP-AND-REPLACE-PLAN.md` — the phased plan this project formalizes.
> - `../../spaarkeai-compose-r3/notes/CLAUDEREVIEW-*.md` + `GPT-*.md` — the earlier fix-review pair (same direction).

---

## 1. The one-paragraph thesis

The current Compose save is a `docx → editor-model → docx` pipeline. Any pipeline that regenerates the `.docx` from the editor's document model loses fidelity, because TipTap/ProseMirror's schema can represent only a small subset of WordprocessingML (no fields, content controls, section properties, numbering definitions, style inheritance, floating objects). **Invert the model**: the OOXML package is the single source of truth; the editor is a lossy *projection* used only to capture intent; edits are applied **surgically** back to the original XML as ID-anchored **operations**, never by re-serializing editor state. This is the "Shadow Document" (a.k.a. Patch/Delta) architecture. Four independent reviewers converged on it.

## 2. What is settled (do not re-litigate)

| Settled fact | Evidence | Consequence for R4 |
|---|---|---|
| MIT TipTap base + our own ProseMirror plugins is the correct, current stack | R3 research Thread A (TipTap v3.28.0 MIT) | Keep TipTap. It provides editing. Not at fault. |
| No permissive JS library does high-fidelity DOCX round-trip | R3 research Thread B | All fidelity work is **server-side .NET** (Open XML SDK). `docx.js` stays dropped. |
| Home-grown track-changes/comments marks → native `w:ins`/`w:del`/`w:comment` is right | R3 research Thread C; R2 shipped it | Keep the native-OOXML authoring; unify it under one Patch Engine. |
| "Do NOT use mammoth for round-trip — write a custom `w:p`→`<p>` mapper carrying IDs" | Review B; R3 research Thread B | **Already done** in Phase 1 (`ComposeDocxProjectionBuilder`). Extend it. |
| `w14:paraId` is the correct primary anchor | Review A/B; R3 research Thread B; MS-DOCX spec | Anchor everything by paraId. |
| Harvey/Spellbook ride *native* Word track-changes; nobody builds a browser Word engine | R3 research Thread C | "Open in Word/Word-for-web" is a validated *launch* surface (SPE, already wired), not the fix. |
| No commercial / AGPL / per-seat component | Owner rule (binding); NFR-03 | Open XML SDK (MIT), TipTap (MIT), PDF.js (Apache-2.0, later). No Syncfusion, no SuperDoc code, no TipTap Pro. |

## 3. The critical caveats (design must honor)

1. **paraId is stable within our round-trip but Word REGENERATES all paraIds when tracked changes/comments are added in an external Word session** (Open-XML-SDK #925). → paraId is the primary anchor; a **fuzzy content-match re-anchor is retained as the cross-Word-session last resort** (our `AnnotationReanchorService`, with AUTO/REVIEW/ORPHAN bands + ambiguity guard — already built). Below-threshold matches surface as a comment, never a silent inline edit.
2. **Open XML SDK has no unique-paraId generator** (#962). → We mint + collision-check our own (random 32-bit `< 0x80000000`) and **persist** it into the shadow package on ingest, so id-less paragraphs get durable ids.
3. **Concurrency / state drift**: offsets go stale the instant the user types. → (a) ProseMirror **position-mapping** rebases positions between LLM-snapshot and patch-apply; (b) **version-stamp** every AI request + every save; a patch against a stale base is re-anchored before applying, not failed.
4. **Anchor granularity — paraId + offset, NOT per-run ids.** Review B suggested tagging every `w:r`. We deliberately do NOT: Word re-splits runs constantly, so run-ids are volatile and don't survive round-trips; paraId (paragraph) + intra-paragraph character offset is the stable addressing unit. Run boundaries are resolved *at patch time* by the Open XML SDK (split-run-at-offset), not carried as ids. Finer granularity would only be needed for structural edits, which we handle as explicit structural operations instead.

## 4. Why NOW is different from R3 (why rip-and-replace, not patch)

R3 built genuine, valuable machinery (custom projection mapper, paraId substrate, native-OOXML annotation writers, re-anchor engine) — but kept **two save paths** that both proved fragile in UAT:
- `ComposeParagraphRedlineSynthesizer` — paragraph-granularity diff (`{paraId, text}` per changed paragraph). Byte-preserving on untouched paragraphs, but re-diffs run structure on edited ones and **cannot represent structural edits** (paragraph insert/split/merge/delete are explicitly out of its "E1 delta scope").
- `DocxAnnotationWriter.LocateTarget` — **whole-document text-search** anchoring for comments/anchored annotations. This is the direct cause of the interior-location HTTP 422 "a tracked change could not be located." Multiple fold/whitespace-collapse fallback layers are band-aids over a fundamentally fragile approach.

The two-path split is itself a defect source (an edit routed to the wrong path drifts). R4 collapses both into **one** operational Patch Engine and eliminates text-search from the write path entirely. That is a replacement, not a patch — hence a new project.

## 5. Framing corrections locked (anti-drift — from R3 re-grounding, 2026-07-22)

- We are **NOT building Word.** "Full Word fidelity" was never a goal — it is a red herring. The goal is **preservation fidelity** (don't corrupt/lose untouched content) + **placement determinism** (edits land exactly right).
- The **feature set is right**; this is not a feature problem. TipTap provides the editing and is not at fault.
- **WOPI / Office-embed is NOT the fix.** It gives an Office shell we cannot programmatically control content inside. The open-to-web/desktop launch (SPE `SpeDocumentViewer`) is an existing convenience, out of scope as a fix.
