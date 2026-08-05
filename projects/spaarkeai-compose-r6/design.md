# Spaarke Compose R6 — Render-on-Save Canonical Model & Word-Parity Fidelity

> **Project**: `spaarkeai-compose-r6`
> **Created**: 2026-08-05 · **Author**: Ralph Schroeder + Claude (Opus 4.8)
> **Status**: DESIGN (hand-authored input to `/design-to-spec` → `/project-pipeline`)
> **Governing ADR**: [ADR-049 Compose Shadow Document](../../.claude/adr/ADR-049-compose-shadow-document.md) — **amendment proposed** (see ADR Tensions)
> **Supersedes**: the reactive per-UAT anchor-patch line of `spaarkeai-compose-r3/r4/r5` AND the interim
> `compose-anchor-robustness-r1` surgical-anchor framing (that project's FR-1/2/3 "make the surgical
> patcher tolerant" approach is **abandoned** in favor of the render-on-save pivot decided below).
> This design folds in the full architecture conversation of 2026-08-05 and the owner's three decisions.

---

## 1. Why this project exists

Every Compose save failure across R3 → R5 has been the **same bug class**: reconciling anchors between
the TipTap editor model and the server-authoritative OOXML, discovered reactively in UAT and patched one
divergence at a time (text-search → paraId; runIndex/offset → paraOffset; then paraId uniqueness/count).
We have been on a treadmill: each release "completes" and the next real-world document exposes a new
anchor divergence. The most recent (`AppligentNDA_Signed.docx`, 2026-08-04) hard-fails with a 422
("A change could not be anchored — reload and reapply"), which is an unacceptable user experience and,
per the owner, **not** something to be resolved with a per-document band-aid or a "reload and reapply"
instruction.

The owner's framing after a long architecture review (2026-08-05): *we keep chasing new issues — is the
architecture correct?* Competitor products (Harvey, Legora, Wordsmith) do **not** appear to hit this
class of problem. Investigation showed why (see §7): they offload high-fidelity editing to a **Word
add-in** (Office.js — Microsoft owns fidelity), and where they map between representations they use
**relative/tolerant object-model anchoring**, not absolute-index raw-XML surgery. Our surgical
byte-patch approach is the source of the treadmill.

**A new, critical requirement also lands here:** **PDF support.** Agreements — especially NDAs — very
often arrive as PDF, not docx. Any architecture we commit to must ingest PDF as a first-class source,
not just docx.

---

## 2. THE DECISION (the anchor for this whole project — do not re-litigate)

### 2.1 Two-approach product strategy

1. **Spaarke Compose** — an integrated analysis / review / drafting experience **in the browser**
   (**THIS PROJECT**). TipTap stays the editor.
2. **Word add-in** — surface Spaarke's analysis/review/drafting *inside* Word via Office.js (a
   **SEPARATE future project = "Option B"**). Out of scope here; noted so this project doesn't try to
   solve Word-grade round-trip fidelity that Option B is the right home for.

### 2.2 This project = the Compose editor, re-architected around **render-on-save**

- **Save = RENDER a fresh document from our canonical model → a NEW immutable version.** NOT a surgical
  byte-patch of the original bytes. **This eliminates the 422 anchor bug class by construction** — there
  is nothing to anchor against on save, because we author the output document from the model rather than
  splicing ops into inherited XML.
- **DROP the lossless-round-trip requirement (old requirement #3).** We do **not** promise that
  re-opening a Word-refined version back in Compose preserves every Word refinement — re-import is lossy
  through the same adapters. Instead:
- **Version history is the safety net.** Every save **APPENDS** an immutable version; nothing is
  overwritten. The canonical chain the owner validated:
  `v1 upload (Word-perfect) → v2 Compose edit → v3 Word edit (Word-perfect) → v4 Compose edit (flattened)`
  — **opening v3 returns the exact Word-perfect file**, because v4 never overwrote it. (This depends on
  the two conditions now CONFIRMED as requirements — see §3 Q1.)
- **Fidelity target = a pragmatic middle** between "Model 1 = lossy re-render" and "Model 2 = surgical
  byte-preserve": widen the canonical model + adapters to preserve as much Word structure as is
  reasonable, in tiers (§4).
- **TipTap stays.** LibreOffice is a **render-only sidecar** for PDF output, NOT an editor. (Replacing
  TipTap with Collabora/LibreOffice Online was considered and **rejected** — documented as a
  road-not-taken; it would replace our editor and our whole analysis UX surface.)

### 2.3 The "mapping engine" — still needed, but simpler, and renamed

The owner's question was whether we need "an intermediary document mapping engine." **Yes — but not the
fragile surgical patcher.** It is a **canonical document model** (we deliberately avoid the jargon "IR")
with format adapters:

```
docx ─►(docx→model adapter)─┐
pdf  ─►(Azure Document Intelligence → model adapter)─┐
                                                     ▼
                                        canonical document model
                                             │        ▲
                                (project to  │        │  (capture edits back)
                                 TipTap)     ▼        │
                                        TipTap editor (UNCHANGED — kept)
                                             │
                                             ▼  render a NEW version (NO anchoring)
                        docx (via template-merge, §5) · pdf (headless LibreOffice sidecar)
```

The canonical model is the single hub; every source becomes the model, the editor edits the model, and
save renders the model out. No op is ever anchored back into inherited bytes.

---

## 3. Owner decisions (2026-08-05 Q&A) — now firm requirements

### Q1 — Versioning safety net → **CONFIRMED + new UX requirement**
SPE (SharePoint Embedded) versioning is non-destructive/append-only (to be verified against the live
Documents surface as the first hardening task). **AND** the owner requires: *"a way in our Documents to
open versions."* → **Firm requirement:** the Spaarke **Documents** experience must expose **version
history** with the ability to **open (and restore/branch-from) a specific prior version** (e.g. open v3
after v4 exists). The render-on-save safety net is only real if the user can actually reach a prior
version through the product. This is now in-scope for R6 (not merely an assumption).

### Q2 — Tactical NDA fix → **NO tactical fix**
The owner: *"we do not need to fix the NDA if it will be addressed through this project; we don't need a
quick fix."* → **Decision:** do NOT ship the interim surgical-anchor tolerance fix. The `AppligentNDA_
Signed.docx` 422 is resolved by the render-on-save pivot itself. The NDA stays as a **regression
fixture** proving the pivot handles text-boxes / `mc:AlternateContent` / duplicate paraIds by
construction. (Accepted consequence: the NDA 422 remains live in prod until R6 ships; the owner has
explicitly accepted this rather than invest in a throwaway fix.)

### Q3 — Word template-merge → **IMPORTANT; reuse/extend existing template capability**
The owner: *"template merge seems very important; one area to investigate is that Power Apps / Dataverse
already provides WORD template capability — is it possible we hook into it? We have already created Email
templates and can potentially reuse / extend that work."* → **Decision + investigation item (§5).**
We already have real code to build on:

- [`Services/Ai/Delivery/EmailTemplateService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Delivery/EmailTemplateService.cs)
  — fetches **Dataverse email templates** from the native `template` entity and renders them through a
  shared `ITemplateEngine`.
- [`Services/Ai/Delivery/WordTemplateService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Delivery/WordTemplateService.cs)
  — generates Word docs from a template via **OpenXML SDK**, `{{placeholder}}` replacement, **already
  processes headers/footers**, and notes "templates can be stored in Dataverse as attachments." Uses the
  same `ITemplateEngine`.

So the template-merge FR must **extend the existing `WordTemplateService` + `ITemplateEngine` seam and
the Dataverse template-storage pattern**, NOT introduce a parallel template subsystem (root CLAUDE.md
§11 Component Justification — default to reuse). The **investigation** (spec task) must decide between:
(a) hooking into **native Dataverse Word document templates** (`documenttemplate` / the Power Platform
Word-template mail-merge feature); (b) extending our custom `WordTemplateService` from placeholder-
replacement to full **part-merge** (import the TipTap-rendered body into a firm/matter `.dotx` that
supplies `styles.xml` / `numbering.xml` / theme / headers / footers / `sectPr`); (c) reusing the
`template` entity + `ITemplateEngine` as the storage/rendering backbone under (b). Recommendation to
validate: **(b)+(c)** — part-merge is the right mechanism for document assembly (placeholder replacement
alone cannot inject a full authored body), reusing the existing engine + Dataverse storage.

---

## 4. Fidelity tiers (what "Word parity, as much as reasonable" means)

Widen the canonical model + adapters incrementally. Everything below the line still round-trips
*safely* (never corrupts, never hard-fails); it just flattens rather than perfectly preserving, and the
prior version remains retrievable via §3-Q1 version history.

- **Near-term (must):** paragraphs, headings, **numbering/lists** (reuse R4.5 `NumberingComputation
  Engine`), bold/italic/underline, **tables** (reuse R5 tracked-table work), **headers/footers**, page
  breaks, hyperlinks, comments, tracked-changes (redlines).
- **Medium:** paragraph/character **styles + theme**, images, footnotes, tab stops, section properties.
- **Hard → accept-flatten (recover via version history):** text boxes, drawings, fields, content
  controls, embedded objects. (These are exactly what broke the surgical patcher on the NDA — under
  render-on-save they degrade gracefully instead of hard-failing.)

**Template-merge (§5) is the highest-leverage fidelity lever** for headers/footers/styles/numbering: a
firm/matter template supplies the professional chrome regardless of how messy the inbound document was.

---

## 5. Word template-merge (document assembly) — FR

**Intent:** take the editor's rendered content (a docx body) and merge it into a Word **template**
(`.dotx`) that supplies the organization's `styles.xml`, `numbering.xml`, theme, headers, footers, and
`sectPr`. Direct **OOXML part-merge** (NOT `altChunk`, which just embeds foreign content). Best for
born-in-Spaarke and firm-standard documents (applies house style); acceptably **restyles** third-party
inbound documents (fine — we dropped the lossless round-trip requirement).

**Reuse mandate (root CLAUDE.md §11):** extend `WordTemplateService` + `ITemplateEngine`; store
templates via the existing Dataverse pattern (`template` entity and/or attachment storage that
`EmailTemplateService`/`WordTemplateService` already use). The spec must include the **investigation
task** from §3-Q3 to choose native-Dataverse-Word-templates vs. extend-our-service, with a written
Placement Justification (BFF §10) and Component Justification (§11).

---

## 6. PDF support (new critical requirement)

- **Intake (PDF → canonical model):** use **Azure Document Intelligence** (Layout/Read model) to extract
  structure (paragraphs, headings, tables, reading order) → map to the canonical model. NDAs and
  agreements frequently arrive as PDF; this makes them first-class Compose inputs.
- **Export (model → PDF):** render via a **headless LibreOffice sidecar** (docx → PDF), invoked as a
  render-only service. NOT an editor, NOT on the hot request path if it threatens publish size / cold
  start — evaluate as a sidecar/container per BFF §10 placement rules.
- Fidelity note: PDF intake is inherently lossier than docx (PDF is a fixed-layout format); set honest
  expectations and lean on version history + the Documents version-open UX (§3-Q1).

---

## 7. Competitor evidence (why they don't hit this — researched 2026-08-05, do not re-derive)

- **Harvey:** primary editing = **Word add-in via Office.js** (Microsoft owns fidelity). Where it maps,
  it uses a **"reversible OOXML ↔ natural-language mapping + deterministic reverse-translation + relative
  anchoring."** Publicly calls docx editing "one of the hardest surfaces."
- **Legora / Wordsmith:** **Word-add-in-centric** for existing documents; browser editors used mostly for
  drafting-from-scratch.
- **Lessons that validate this design:** (1) offload Word-grade fidelity editing to Word itself (our
  Option B, separate project); (2) where you map between representations, use **relative/tolerant
  object-model anchoring**, never absolute-index raw-XML surgery. Render-on-save is the browser-side
  expression of lesson (2): we never splice into inherited XML at all.

---

## 8. Goals / Non-goals

**Goals**
- Eliminate the anchor-reconciliation 422 bug class by construction (render-on-save).
- Ingest **both docx and PDF** as first-class Compose sources.
- Word parity "as much as reasonable" via the tiered canonical model + template-merge.
- Version-history **open/restore UX in Documents** (the safety net made real).
- PDF export via a render sidecar.
- A representative-document **round-trip fidelity harness** as a release gate (moves discovery from UAT
  to CI) — seeded with `AppligentNDA_Signed.docx`.

**Non-goals**
- Word-grade lossless round-trip of Word-refined versions back into Compose (old req #3 — dropped;
  Option B territory).
- The Word add-in (Option B) itself.
- Replacing TipTap.
- A tactical/surgical NDA anchor fix (explicitly declined — §3-Q2).

---

## 9. Constraints (inherited + new)

- **ADR-049 invariants** apply *except where the render-on-save pivot supersedes them* — see ADR
  Tensions (§11). Notably I-4 (byte-surgical untouched subtrees) and I-7 (no write-path text-search) are
  about the surgical write path; render-on-save removes that path. This requires an ADR-049 amendment,
  not a silent deviation.
- **BFF Hygiene (root CLAUDE.md §10):** any new endpoints/services (PDF intake, template-merge, render
  sidecar) require a **Placement Justification** section + publish-size verification (**≤60 MB
  compressed**, escalate at ≥+5 MB single-task delta; baseline to re-measure). LibreOffice/Azure DI must
  NOT bloat the BFF binary — prefer sidecar/managed-service placement.
- **Component Justification (root CLAUDE.md §11):** template-merge extends existing `WordTemplateService`/
  `ITemplateEngine`; PDF intake uses managed Azure DI; no parallel subsystems.
- **NEVER delete `docxBridge.ts`.**
- **Deploy discipline:** BFF + `sprk_spaarkeai` deployed together; anti-clobber verify (live artifact is
  a strict superset before deploy); `/conflict-check` before **every** BFF PR (HARD overlap with
  `spaarkeai-compose-r1/r2/r3/r4/r5` + `spaarke-ai-architecture-redesign-r2` + `analysis-hub-r1`/
  `agreements-r1` on `Services/Compose/` and `ComposeService.cs`/`ComposeWorkspace.tsx`).
- Commit with `--no-verify`; co-author trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

## 10. Key files / fault lines (starting map)

Server — `src/server/api/Sprk.Bff.Api/Services/Compose/`:
- `ComposeDocxProjectionBuilder.cs` (docx→projection; the seed of the canonical-model adapter; ~1897 L;
  contains the R4.5 `NumberingComputationEngine` to reuse).
- `ComposeDocumentRenderer.cs` — `SynthesizeDocument` is the **render-from-model precedent to
  generalize**: the pivot routes IMPORTED docs through render-from-model rather than surgical patch.
- `ComposeShadowPatchEngine.cs` (surgical patcher, ~2999 L — **largely retired** by render-on-save;
  keep only for any residual clean-apply path during transition).
- `ComposeBaselineParaIdStamper.cs` (the all-or-nothing count gate — root of the NDA failure; obviated by
  render-on-save), `ParaIdPreParser.cs`, `AnnotationReanchorService.cs` (tolerant scorer — reference for
  any residual fallback), `ComposeService.cs` `SaveAsync` (save path — becomes render+new-version).
- `Services/Ai/Delivery/WordTemplateService.cs`, `EmailTemplateService.cs`, `ITemplateEngine` (§5 reuse).

Client — `src/client/shared/Spaarke.Compose.Components` (TipTap editor; `stepOperationInterceptor`;
partial-apply banner + Open-Document modal shipped in R5).

Documents version-history UX (§3-Q1): locate the Documents surface (SPE-backed) and its version APIs.

---

## 11. ADR Tensions (surface now per root CLAUDE.md §6.5)

- **ADR-049 (Compose Shadow Document) — Path B (amendment).** Render-on-save supersedes the
  "OOXML server-authoritative + surgical step-ops anchored `(paraId,runIndex,offset)`, no write-path
  text-search" model for the *save* path. The invariants were guardrails against a failure mode; the
  render-on-save pivot removes that failure mode entirely (nothing to anchor). Propose an ADR-049
  amendment codifying: (1) save renders a new version from the canonical model — no surgical anchoring;
  (2) version history is the fidelity safety net; (3) representative-corpus round-trip is a release gate;
  (4) the surgical engine is retained only for any transitional clean-apply path. This must merge with or
  before the dependent code. **Not a silent deviation** — full §6.5 treatment in the spec.
- **BFF §10 (Placement) — Path A (documented exceptions)** for PDF intake (Azure DI), template-merge
  (extend existing service), and the LibreOffice render sidecar; each gets a Placement Justification.

---

## 12. Phasing sketch (to be refined by `/project-pipeline`)

0. **Human/verify gates:** confirm SPE versioning append-only + inventory the Documents version APIs
   (Q1); measure current publish-size baseline; ADR-049 amendment drafted.
1. **Canonical model + render-on-save core:** generalize `ComposeDocumentRenderer` render-from-model;
   route imported docx through render (not surgical patch); NDA fixture passes by construction.
2. **Fidelity widening (near-term tier):** numbering/lists, tables, headers/footers, hyperlinks,
   comments, redlines through the model.
3. **Template-merge:** §3-Q3 investigation → extend `WordTemplateService`/`ITemplateEngine` to part-merge
   into a firm/matter `.dotx`; Dataverse template storage.
4. **PDF intake (Azure Document Intelligence) + PDF export (LibreOffice sidecar).**
5. **Documents version-history open/restore UX** (Q1 safety net made real).
6. **Round-trip fidelity harness + representative corpus** (CI release gate), seeded with the NDA.
7. **Wrap-up:** anti-clobber deploy (BFF + `sprk_spaarkeai`), ADR-049 amendment merged, test-diet.

---

## 13. Success criteria (closed set for spec authoring)

- Saving `AppligentNDA_Signed.docx` after edits **succeeds** (no 422), produces a new version, and edits
  land correctly — with **no** surgical-anchor code on the save path.
- A PDF NDA can be opened in Compose, edited, and saved as a docx version.
- A saved document merged through a firm template carries that template's headers/footers/styles.
- A user can open a prior version (e.g. v3 after v4) from the Documents surface and get the exact bytes.
- The round-trip fidelity harness runs in CI and gates the release.
- Publish size stays ≤60 MB; no new HIGH CVE; BFF placement justified for every new component.
