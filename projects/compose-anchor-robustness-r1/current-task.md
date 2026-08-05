# Current Task State — Compose Fidelity / Anchor Robustness R1

> **Last Updated**: 2026-08-05 (context-handoff before compaction)
> **Worktree dir**: `C:/code_files/spaarke-wt-spaarkeai-compose-r5` · **Branch**: `work/compose-anchor-robustness-r1` (off master)
> **Recovery**: read **Quick Recovery** + **THE DECISION** first. A long architecture conversation produced a firm direction — do NOT re-litigate it.

## Quick Recovery

| Field | Value |
|-------|-------|
| **Project** | `compose-anchor-robustness-r1` (NEW; successor to the reactive compose-r3/r4/r5 anchor patches) |
| **Status** | Architecture **DECIDED**; **no fix code written yet**; awaiting user answers to 3 OPEN QUESTIONS |
| **Branch** | `work/compose-anchor-robustness-r1`; last commit `41d54b9bd` (spec.md + NDA fixture); ~7 behind master; working tree clean |
| **Next action** | Answer OPEN QUESTIONS → rewrite spec/design to the DECIDED architecture → verify SPE versioning + version-open UX → (maybe tactical fix) → build **render-on-save canonical model** |

## THE DECISION (2026-08-05 — the anchor for all next steps)

**Two-approach product strategy:**
1. **Spaarke Compose** — integrated analysis/review/drafting, in-browser (**THIS PROJECT**).
2. **Word add-in** — surface Spaarke analysis/review/drafting *inside* Word (SEPARATE future project = "Option B"; hard part is porting Spaarke's analysis UX into Word's task pane, à la Harvey/Legora/Wordsmith).

**This project = the Compose editor. Decided architecture:**
- **Save = RENDER a fresh docx from our canonical model → a NEW SPE version.** NOT surgical-patch of the original bytes. **This eliminates the 422 anchor bug class by construction** (nothing to anchor against on save).
- **DROP requirement #3** (no guarantee that re-opening a Word-edited version in Compose preserves Word's refinements — re-import is lossy through the same path).
- **Version history = the safety net.** Every save APPENDS an immutable SPE version; nothing is destroyed. Confirmed chain: `v1 upload(Word-perfect) → v2 Compose → v3 Word(Word-perfect) → v4 Compose(flattened)`; **switching to v3 returns the Word-perfect file** because v4 never overwrote it. (Depends on the two conditions in OPEN QUESTION (a).)
- **Fidelity target = pragmatic middle** between Model-1 (lossy re-render) and Model-2 (surgical byte-preserve): widen the canonical model + adapters to preserve as much Word structure as reasonable. Tiers:
  - **Near-term:** paragraphs, headings, **numbering/lists**, bold/italic/underline, **tables**, **headers/footers**, page breaks, hyperlinks, comments, tracked-changes (redlines).
  - **Medium:** styles/theme, images, footnotes, tab stops, section properties.
  - **Hard → accept-flatten (recover via version history):** text boxes, drawings, fields, content controls, embedded objects.
- **Word template-merge FR (user idea 2026-08-05, ACCEPTED):** export TipTap content referencing style *names*, **merged into a firm/matter Word template** (`.dotx`) supplying `styles.xml`/`numbering.xml`/`theme`/headers/footers/`sectPr`. Direct OOXML **part-merge** (not `altChunk`). Best for born-in-Spaarke + firm docs (applies house style); restyles third-party inbound (OK per req #2). Big fidelity + competitive win for headers/footers/styles/numbering.

## The mapping engine — still needed, but SIMPLER (renamed "canonical document model", not "IR")

NOT the fragile surgical patcher. It is:
```
docx ─► canonical model      pdf ─► (Azure Document Intelligence) ─► canonical model
                     └────────► canonical model ◄────────┘
                                   │  ▲  (project / capture edits)
                                   ▼  │
                                TipTap editor (UNCHANGED — keep it)
                                   │
                                   ▼  render (NEW version; NO anchoring)
                        docx (via template-merge) · pdf (headless LibreOffice sidecar)
```
LibreOffice = **render-only sidecar** (docx/model→PDF), NOT an editor (that would be Collabora Online = replaces TipTap = rejected; documented as road-not-taken). TipTap STAYS the editor.

## OPEN QUESTIONS — awaiting user before coding

- **(a)** Verify **SPE versioning is non-destructive (append)** AND that Spaarke has **version-history UX to open/restore a specific prior version** (v3). The safety net depends on BOTH. (Downloading/opening v3 in Word = exact file; opening v3 in Compose + save = new flattened version, v3 stays intact.)
- **(b)** Ship the **tactical NDA anchor fix now** for immediate prod relief, OR **skip straight to the render-on-save pivot** (the tactical fix becomes moot under render-on-save)? Agent lean: ship the small fix for relief, then build the pivot.
- **(c)** Confirm **template-merge FR** priority within the near-term fidelity work.

## NDA 422 root cause (diagnosed via App Insights + file inspection — do NOT re-derive)

`AppligentNDA_Signed.docx` (fixture: `projects/compose-anchor-robustness-r1/notes/`). Signature/graphic **text box in `mc:AlternateContent`** → Word emits it **twice** (DrawingML Choice + VML Fallback) with the **SAME `w14:paraId`s** → 55 `<w:p>` / **52 unique / 3 duplicates**; 49 body + 6 textbox. Server walks `Descendants<Paragraph>()` everywhere → counts 55; editor sees 49. `ComposeBaselineParaIdStamper` **all-or-nothing count gate** (55 ≠ client map count) → **stamps nothing** → all 9 ops + 6 comments `ParagraphNotFound` → prong 1 correctly refuses (0 applied → re-throw → 422, "nothing overwritten"). General to ANY doc with textboxes/drawings/AlternateContent. **Under render-on-save this disappears** (no stamp/anchor).

## Competitor evidence (why they don't hit this — researched 2026-08-05)

Harvey: primary editing = **Word add-in via Office.js** (Microsoft owns fidelity); AI edits use **"reversible OOXML↔natural-language mapping + deterministic reverse-translation + RELATIVE anchoring"**; calls docx editing "one of the hardest surfaces." Legora/Wordsmith: **Word add-in-centric** for existing docs; browser editors mostly for drafting. Lessons: (1) they **offload fidelity editing to Word**; (2) where they map, they use **relative/tolerant object-model anchoring**, not absolute-index raw-XML surgery. Validates the canonical-model + render-on-save decision. Sources in chat (harvey.ai/blog, legora.com/product, wordsmith.ai).

## Deploy / branch state (2026-08-05)

- **Assistant follow-on cards fix**: DONE — merged to master (`0af7f1e0a`), `sprk_spaarkeai` deployed + verified superset. (Root cause: a Compose-registered file wasn't counted by the chip attachment gate; fixed in `ConversationPane` `sessionAttachmentCount` via `composeSourceDocCount`, keyed on `sourceDocReadyToken`.)
- **BFF (`spaarke-bff-dev`)**: live binary has prong1/2 (`ApplyBestEffortByParagraph`, `ResolveAbsoluteFromParaOffset`, `PartialApplySummary`); last deploy 2026-08-04 (by another project, from master, verified contains our code).
- **`sprk_spaarkeai`**: live, superset (compose-r5 + agreements-r1 + messaging-r3 + assistant fix).
- **compose-r5**: merged to master, closed.
- **NDA 422**: STILL LIVE in prod (not fixed) — resolved by this project's render-on-save pivot (or tactical fix per OPEN Q (b)).
- Consider a dedicated worktree `spaarke-wt-compose-anchor-robustness-r1` + `projects/INDEX.md` row when this goes multi-task (hot Compose files overlap ~5 active projects → conflict-check before every BFF PR).

## Fault-line / key files

`src/server/api/Sprk.Bff.Api/Services/Compose/`: `ComposeDocxProjectionBuilder.cs` (projection = canonical-model source, ~1897L), `ComposeShadowPatchEngine.cs` (surgical patcher, ~2999L — largely bypassed by render-on-save), `ComposeBaselineParaIdStamper.cs` (the count-gate, ~347L), `ParaIdPreParser.cs`, `AnnotationReanchorService.cs` (the tolerant scorer — reuse for any residual fallback). Save path: `ComposeService.cs` `SaveAsync`. **Render-from-model precedent to generalize**: the existing born-in-editor / authored / clean-apply path via `ComposeDocumentRenderer.SynthesizeDocument` — the pivot routes IMPORTED docs through this instead of surgical patch. Client: `src/client/shared/Spaarke.Compose.Components` (TipTap editor; `stepOperationInterceptor`).

## Next steps (post-compaction, in order)

1. Get user answers to OPEN QUESTIONS (a)(b)(c).
2. **Rewrite `spec.md` + add `design.md`** to the DECIDED architecture (render-on-save canonical model · Word-parity tiers · template-merge FR · version-history safety net · PDF via Azure DI · LibreOffice render sidecar · TipTap kept · Word-add-in as separate Option B). The old surgical-anchor FR-1/2/3 framing in spec.md is SUPERSEDED — mark it so.
3. Verify SPE versioning (non-destructive/append) + version-open/restore UX (OPEN Q (a)).
4. (If user chooses relief) minimal tactical anchor fix on the current path (align walks + dedup ids + positional fallback).
5. Build **render-on-save**: route imported docx through render-from-canonical-model; widen the canonical model for near-term Word features; docx export via **template-merge**.
6. Add **PDF intake (Azure Document Intelligence) + PDF export (LibreOffice sidecar)** as follow-on tasks.
7. **Corpus + round-trip fidelity harness** (CI gate); dedicated worktree + INDEX row.
8. Anti-clobber deploy (BFF + `sprk_spaarkeai` together); `/conflict-check` before every BFF PR.
