# AI Advanced Capabilities — Agreement Analysis (agreements-r1) — Design Discussion

> **Project**: `ai-advanced-capabilities-agreements-r1` · **Round**: r1 (the "nda-r2" successor)
> **Date**: 2026-07-28 · **Owner**: ralph.schroeder
> **Status**: 🟡 Discussion / pre-design — owner enhancement list captured; raw material to formalize via `/design-to-spec`
> **Spawned from**: `ai-advanced-capabilities-nda-r1` (the shipped NDA advisory-review vertical). This project is the
> **work-type generalization + review-depth successor**: NDA is the first *knowledge sub-domain* of the
> **Agreement Analysis** work type; this round deepens the review experience and adds the Review Summary Memo.
> **Siblings**: `ai-advanced-capabilities-analysis-hub-r1` (platform: `sprk_analysis` spine + sessions + hub widget)
> · `ai-advanced-capabilities-research-r1` (Legal Research — different surface, later).

---

## 0. What this project is (and isn't)

**Is:** review-surface *depth* + output deliverables for the Agreement Analysis work type — multi-select batch AI
actions, bidirectional summary↔note↔document highlighting, cleaner Assistant confirmations, and a first-class
**Review Summary Memo** (generate-as-docx / email). All build directly on the nda-r1 Compose review surface.

**Isn't:** a from-scratch surface. 5 of the 6 enhancements are refinements to Compose review UX or the memo output;
they reuse shipped nda-r1 primitives. The one platform item (PDF ingest) is flagged for a separate Compose project.

**Relationship to the hub:** **largely independent.** These are Compose-surface + output features; they do not
require the `sprk_analysis` spine/session work of `analysis-hub-r1`. The one tie-in is memo persistence (#5), which
lands in Dataverse `sprk_analysisoutput` (exists today) — the hub formalizes that store but does not block this.

---

## 1. Enhancement list (owner, 2026-07-28) — structured into buckets

### Bucket A — Platform prerequisite (SCOPING DECISION)

**#1 — PDF support (load PDFs onto the Compose surface).**
Most agreements arrive as PDF. Owner's question: *here, or a `compose-r5` project?*

- **Ground truth (verified 2026-07-28):** Compose is **DOCX-native end to end** — client `docxBytes` + `.docx`
  picker + paraId-structured import (`ComposeWorkspace.tsx`, `docxBridge.ts`); server
  `ComposeDocumentRenderer` / `ComposeDocxProjection` / `DocxAnnotationReader` are all OpenXML. **No PDF path
  exists anywhere in Compose.**
- **What PDF ingest actually requires:** a new ingest pipeline — PDF → text/layout extraction (candidate:
  the existing `spaarke-docintel-dev` Document Intelligence / Form Recognizer) → map into Compose's paraId
  structured projection. PDFs are not round-trip-editable like DOCX, so there are real decisions (extract-to-
  editable-DOCX vs read-only overlay; fidelity; how redlines/comments map back).
- **RECOMMENDATION: `compose-r5` (a Compose *platform* project), NOT agreements-r1.** Reasons: (a) it's a
  platform capability every Compose consumer benefits from, not agreement-specific; (b) it's a substantial
  ingest/conversion effort with its own design surface; (c) bundling it here would couple a UX-depth project to
  a heavy platform effort and bloat scope (§11). It IS a hard prerequisite for the real agreement use case, so
  sequence it as a focused precursor; agreements-r1 can build/validate on DOCX and inherit PDF when compose-r5
  lands. **Owner decision needed: confirm compose-r5, or fold in as a foundational agreements-r1 task.**

### Bucket B — Review-surface UX depth (Compose client)

**#2 — Bidirectional highlight: summary row → BOTH document section AND its Review Note.**
Today, selecting a Review Summary row highlights the document section/paragraph but NOT the corresponding Review
Note. Want: selecting a summary row highlights *both* the document location and the matching gutter Review Note.
- **Reuse:** `NdaReviewSummaryPanel` (summary rows, already resolve `docPosition`) + `ComposeCommentGutter`
  (Review Notes) + the existing anchor/highlight resolution. The doc→highlight already works; add the reverse
  link to scroll+highlight the matching gutter note (join key = the finding's section/anchor id).
- **Size:** small–moderate, client-only.

**#3 — Multi-select Review Notes + batch AI action.**
Checkbox in the upper-left of each Review Note (next to title/subject); select one/many/all. When ≥1 selected, a
sub-toolbar exposes an **AI-action dropdown** (available actions); selecting 1 shows a "select all" option. Running
the action executes it **per selected note**, and the **Assistant shows the per-section outcome summary exactly as
it does today** for an individually-run note. Show a **progress bar** if the batch will take time.
- **Reuse:** the per-note AI action is SHIPPED — `ComposeCommentGutter` `noteTools` + `onRunNoteTool`; dispatch
  via `ConversationPane.dispatchComposeAction` → `makeComposeEditControlsMessage` (the per-section confirmation
  #4 refines). Batch = a selection model + sub-toolbar + loop the existing single-note dispatch; per-section
  outcomes already render. Progress = reuse the `NdaReviewProgressModal` / stepper pattern or a lighter inline bar.
- **Open Q:** concurrency cap on batch runs (ADR-016 rate limits) — run sequentially with progress, or bounded-parallel?
- **Size:** moderate–large, client-heavy (selection state, sub-toolbar, batch orchestration) reusing shipped dispatch.

**#4 — Assistant pane: clearer, separated AI-action confirmations.**
Screenshot shows multiple "What I changed" confirmations running together. Want each entry clearly identified +
separated: add the **location indicator (bold)** + more whitespace between entries.
- **Reuse:** `makeComposeEditControlsMessage` / `COMPOSE_EDIT_CONFIRMATION` rendering (`ConversationPane`,
  ~L163/L941 per nda-r1 notes). Thread the section location into the confirmation header (bold) + spacing.
- **Size:** small, client-only. (Natural to pair with #3, which produces many confirmations at once.)

### Bucket C — Review Summary Memo (output + export)

**#5 — Review Summary Memo.**
A clear list identifying each changed section/paragraph, a description of **why** changed + **what** changed, and
the **golden reference**. Saveable as a document or added to an email.
- **Reuse (this is ~90% already produced):** the shipped NDA-REVIEW output schema emits per flagged section:
  `sectionRef` (location), `quotedText` (before), `explanation` (why), `standardRef` (golden reference). The one
  net-new piece is the **after text** (the accepted Draft Alternative redline). Memo = assemble
  {location, before, after, why, golden-ref} per changed section.
- **Persistence (per the storage model locked with analysis-hub-r1):** the memo is a durable business deliverable
  → **Dataverse `sprk_analysisoutput`** (structured field-level + a JSON body for the section array). The chat
  transcript stays in Cosmos; the memo is one of the "important Dataverse artifacts." Working copy may live in
  Cosmos alongside the session; the committed memo lands in Dataverse.
- **Size:** moderate (assemble + persist + the after-text capture).

**#6 — "Create Summary Memo" toolbar control.**
A toolbar icon → dropdown: **Generate memo** (creates a `.docx` saved to downloads) + **Email memo** (opens the
email compose with the memo in the body + subject prefilled, using our Email compose form).
- **Reuse:** docx generation via `ComposeDocumentRenderer` / `ComposeShadowPatchEngine` (server OpenXML — same
  engines nda-r1's Summary-Page uses); email via the shipped **EmailComposer** form
  (`Spaarke.UI.Components/.../EmailComposer`). "Generate memo" → download; "Email memo" → EmailComposer with body
  + subject prefilled from the memo.
- **Size:** moderate; mostly wiring shipped renderers + EmailComposer to the #5 memo content.

---

## 2. Reuse inventory (§11 — build almost nothing net-new)

| Need | Reuse |
|---|---|
| Per-note AI action dispatch (#3 batch unit) | ✅ `ComposeCommentGutter` `noteTools`/`onRunNoteTool` + `ConversationPane.dispatchComposeAction` (shipped) |
| Per-section Assistant outcome (#3/#4) | ✅ `makeComposeEditControlsMessage` / `COMPOSE_EDIT_CONFIRMATION` (shipped; #4 refines its formatting) |
| Summary rows + doc highlight (#2) | ✅ `NdaReviewSummaryPanel` (+ `docPosition` resolution) |
| Review Notes gutter (#2/#3) | ✅ `ComposeCommentGutter` |
| Progress UI (#3) | ✅ `NdaReviewProgressModal` / `AiProgressStepper` pattern |
| Memo content (#5) | ✅ NDA-REVIEW output schema (location/before/why/golden-ref) — only after-text is net-new |
| Memo docx export (#6) | ✅ `ComposeDocumentRenderer` / `ComposeShadowPatchEngine` (nda-r1 Summary-Page engines) |
| Memo email (#6) | ✅ `EmailComposer` form |
| Memo persistence (#5) | ✅ Dataverse `sprk_analysisoutput` (exists) — per the analysis-hub storage model |
| Contextual tool palette (batch actions) | ✅ Contextual AI Tool Library (`workTypes`×`surfaces`, shipped nda-r1) |

**Net-new:** the multi-select selection model + sub-toolbar (#3); the summary↔note reverse-highlight link (#2);
the after-text capture + memo assembly/persistence (#5); the memo toolbar dropdown + download/email wiring (#6);
the confirmation formatting (#4). PDF ingest (#1) is out of scope → compose-r5.

---

## 3. Dependencies & sequencing

- **PDF (#1)** → recommend `compose-r5` (platform). Hard prerequisite for the real agreement use case, but agreements-r1
  can build/validate on DOCX and inherit PDF when compose-r5 lands.
- **Memo persistence (#5)** → uses `sprk_analysisoutput` (exists today); the analysis-hub storage decisions
  (Cosmos = transcript, Dataverse = business artifacts) apply but do not block — the hub need not ship first.
- Otherwise **independent of analysis-hub-r1** — Buckets B & C are Compose-surface + output work.
- Internal order: #4 pairs with #3 (many confirmations); #5 precedes #6 (memo content before export/email).

---

## 4. Design decisions — ✅ ALL RESOLVED (owner, 2026-07-28) → SPEC-READY

1. **PDF placement** — ✅ **Move to `compose-r5`** (platform project). OUT of agreements-r1 scope. This project
   builds/validates on DOCX and inherits PDF ingest when compose-r5 lands.
2. **#3 batch concurrency** — ✅ **Batch confirmed; sequential-with-progress** (respects ADR-016 rate limits; the
   progress bar covers the wait). Each note's outcome surfaces in the Assistant exactly as an individual run does.
3. **Memo storage shape** — ✅ **Reuse `sprk_analysisoutput`** (child of `sprk_analysis`) + JSON body for the
   section array. No new entity for r1.
4. **Memo before/after semantics** — ✅ **No timestamp / accept-time capture needed.** The memo derives from the
   document's final change-disposition state: **before = original text**, **after = whatever was accepted (or the
   rejected outcome)**. So each memo row = {location, before(original), after(accepted result / rejected), why,
   golden-ref}, assembled at generation time from the tracked-change dispositions — not a per-accept event capture.
5. **Work-type scope** — ✅ **General across ALL agreement types — NOT per-type specialized.** Owner: "not sure
   there will be a difference between agreement types; should support any/all agreements analyzed." So r1 is a
   **single general Agreement Analysis review capability** that works for any agreement; knowledge sub-domains
   (NDA/MSA/employment) vary GROUNDING only, not the UX/tools/memo. Do NOT build per-type branches. (This is the
   work-type model working as intended: Agreement Analysis = one surface + one tool palette; sub-domain = grounding.)

**Spec-ready.** Next: `/design-to-spec` → `/project-pipeline` (after the analysis-hub, per owner sequencing).

---

## 5. Constraints to honor

- **§10 BFF Hygiene** — memo docx generation + persistence touch `Sprk.Bff.Api` → Placement Justification +
  publish-size check; `<hot-path-declaration>` (BFF Y, SpaarkeAi Y).
- **§11 Component Justification** — default to reuse (see §2); justify any new service/entity (esp. Q3 memo entity).
- **ADR-021** — new UI (multi-select checkboxes, sub-toolbar, memo dropdown) uses Fluent v9 semantic tokens + dark mode.
- **ADR-016** — batch AI actions (#3) route through the single tier→deployment resolver + honor rate limits.
- **Contextual AI Tool Library** — batch actions draw from the shipped `getToolsForSurface` registry (work-type scoped).
