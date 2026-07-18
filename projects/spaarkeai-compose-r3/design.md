# Spaarke Compose R3 — Design (Working Document)

> **Status**: DRAFT — first design pass. Not yet a committed spec. Feeds `/design-to-spec` → `/project-pipeline`.
> **Codename**: Spaarke Compose (continuing from R1 → R2)
> **Positioning**: AI-native legal drafting workspace — **R3 makes the Word round-trip faithful.**
> **Project ID**: `spaarkeai-compose-r3`
> **R3 Theme**: **Fidelity.** R1 shipped the editor round-trip (with documented loss); R2 shipped AI actions + native-OOXML track-changes/comments *authoring*. R3 closes the gap between "the document came back mangled" and "the document came back exactly as it went in, with my edits marked."
> **Owner**: Ralph Schroeder
> **Last updated**: 2026-07-16 (first draft — code-grounded against the as-built compose-r2 save/annotation path)
> **Seed**: [`notes/seed-README.md`](notes/seed-README.md) (Scope Areas A–J) + [`ooxml-fidelity-findings.md`](ooxml-fidelity-findings.md) (E1/E2/E3 verdicts, grounded 2026-07-14)
> **Research (current best practices, July 2026)**: [`notes/tiptap-docx-fidelity-research-2026-07-16.md`](notes/tiptap-docx-fidelity-research-2026-07-16.md) — three primary-source-cited threads (TipTap ecosystem/licensing · DOCX fidelity round-trip · AI authoring/track-changes UX). Its conclusions are folded into §4–§7, §10, §14 below.
> **R2 reference (direct foundation, merged to master)**: [`../spaarkeai-compose-r2/design.md`](../spaarkeai-compose-r2/design.md) · [`../spaarkeai-compose-r2/notes/defer-issues.md`](../spaarkeai-compose-r2/notes/defer-issues.md)
> **Binding foundations**: [ADR-039 Grounded Execution & Closed Catalogs] · [ADR-040 Session Ledger] · [ADR-043 Action spine] · ADR-013 (AI facade) · ADR-007 (Graph isolation) · ADR-009 (Redis-first)
>
> ### Constraint carried forward (BINDING — owner rule, 2026-07-14)
> **NO TipTap product features — paid OR unpaid.** Allowed: the MIT base editor only (`@tiptap/core`, `@tiptap/react`, `@tiptap/starter-kit`, and the MIT extensions already in use). Forbidden: `@tiptap-pro/*` and any TipTap feature product (TrackChanges, Comments, AI, Collaboration, Import/Export converters). All three R3 enhancements are **our code on the MIT base**. Track-changes + comments already ship home-grown in R2 (native `w:ins`/`w:del`/`w:comment`) — no Pro anywhere in the tree.

This document leads with the **fidelity problem as users experience it**, grounds it in **what the running R2 build actually does** (file:line evidence), then designs the three enhancements (E1/E2/E3) and the import round-trip. Design follows from the gap, not from the feature list.

---

## 0. Locked Decisions (owner review 2026-07-16)

| # | Decision | Effect |
|---|---|---|
| **D1 — E1 approach** | **Hybrid** — retained-original baseline + the redline engine synthesizes the redline for direct typing + the existing `DocxAnnotationWriter` handles AI redlines/comments. **AMENDED 2026-07-17 (§6.5): the redline engine is `ComposeParagraphRedlineSynthesizer` (Option C, self-synthesized `w:ins`/`w:del` in place), NOT Docxodus `WmlComparer`** — the NFR-09 gate proved WmlComparer strips `w14:paraId` + drops tables on real docs (6.4.0 AND 7.1.0). See §4 amendment banner. | §4.2 fork **resolved**; engine amended. Untouched paragraphs preserved (byte-identical under Option C); maximum reuse of the R2 annotation pipeline. |
| **D2 — R3 scope** | **Everything**: E1+E2 fidelity core + E3 (grounding-tied confidence) + the credible editing toolset (find/replace, basic tables, toolbar polish) + **import round-trip** (read existing Word revisions/comments into the editor). | Broadest scope. Import moves from fast-follow **into R3 core**; toolset is in. Timeline is larger — plan/WBS will sequence fidelity core first, import + toolset as parallel tracks. |
| **D3 — E3 confidence** | **Grounding-tied qualitative band** (high/med/low tied to verifiability), rationale-first. No numeric self-report; no auto-accept of low-confidence. | §6.2 **locked**. Aligns with 2026 HCI research; guards against over-reliance. |
| **D4 — Edit fidelity** | **Text + run-level formatting** inside a changed paragraph (bold/italic/font edits), leveraging WmlComparer Format-Change Detection. | Fuller fidelity in the MVP, not paragraph-text-only. |

> ✅ **ALL SPIKES PASSED (2026-07-16)** — [`notes/spikes/`](notes/spikes/). Summary of what was proven + folded into the design:
> - **S1 / S1b** — Docxodus `WmlComparer` **preserves `w14:paraId` on unchanged paragraphs** (incl. table cells, nested tables, 3-level numbering), preserves all structural parts, handles delete/split, emits `w:ins`/`w:del` with format-change detection. **Docxodus adoption CONFIRMED.** *Refinement*: WmlComparer **re-serializes** (cosmetic) rather than byte-preserving → the fidelity claim is **structural/semantic**, not literal byte-identity (Approach B splice-back is optional hardening).
> - **S2** — `@tiptap/extension-unique-id` (3.28.0, MIT) carries paraIds: untouched ids survive edits; split keeps one id + re-mints the other. **No custom minting plugin needed** (design simplification). Load-time ids set explicitly by the server pre-parse.
> - **S3** — Docxodus managed = 2.44 MB; its SkiaSharp native (11.6 MB) is **not needed for the diff** → excluded. Net publish add ≈ 2.44 MB.
> - **S4** — baseline = **load-time SPE version by `versionId`** (authoritative, refresh-safe, zero new storage) + client fast-path + Redis fallback.
> - **S5** — the shipped redline apply path is **slice-safe** (existing formatting preserved). One R3 item: enrich `new_text` so AI *insertions* carry formatting (D4).
>
> `AnnotationReanchorService` fuzzy matcher is **retained** as the cross-Word-session anchor fallback. **No design pivots. Ready for `/design-to-spec`.**

---

## 1. Product Statement

Compose is a legal drafting workspace: a lawyer opens a Word document, edits it, gets AI-suggested redlines, accepts/rejects them, and saves back to SharePoint Embedded (SPE). The differentiator vs. Harvey/Spellbook is that Compose lives *inside* the Spaarke DMS — the document it edits **is** a `sprk_document`, and every edit round-trips through real `.docx` bytes.

**The R3 problem, in one sentence**: *the moment the user edits anything, Save rebuilds the entire `.docx` from the editor's simplified view — so headers, footers, firm styles, multi-level clause numbering, hyperlinks, and embedded objects are silently dropped, even for the 99% of the document the user never touched.*

For prose-only memos this is tolerable. For a 40-page contract with a numbered clause hierarchy, a firm letterhead, and a signature block, it is **make-or-break** — and it is the root of both the R1 UAT complaint ("Word loses its formatting on save", 2026-07-01) and the R2 round-7 redline-fidelity pain. R3 exists to fix exactly this.

R3 has three enhancements, prioritized:

| # | Enhancement | Verdict (grounded 2026-07-14) | Scope-seed | Effort |
|---|---|---|---|---|
| **E1** | Retained-original OOXML + delta save (the fidelity keystone) | **GENUINELY MISSING** for edited saves | A | HIGH (diff engine now off-the-shelf MIT — §4.2) |
| **E2** | Paragraph/position identity (`w14:paraId` anchoring) | **GENUINELY MISSING** (fuzzy text+index today) | A/E | MED (coupled to E1; fuzzy match retained as fallback) |
| **E3** | Enriched redline contract (reason **+ confidence +** offsets) | **PARTIALLY present** (reason ships; confidence + offsets missing) | G/H | LOW |
| (+) | Import round-trip (read *existing* Word revisions/comments IN) | Authoring done in R2; **import missing** | B/C | MED |

E1 + E2 are **one workstream** (the OOXML-identity core). E3-confidence is a cheap rider. Import round-trip rides on the same retained-original substrate. **All four are in R3 scope (D2)**, plus the credible editing toolset (§7.5); the fidelity core sequences first, with import + toolset as parallel tracks.

---

## 2. Current State — What R2 Actually Shipped (grounded 2026-07-14/16)

R2 already built substantial fidelity machinery. R3 is *not* greenfield — it extends what exists. The table below is the ground truth (file:line verified against the merged build); everything in R3's design attaches to these seams.

| Capability | Reality today | Evidence (file:line) |
|---|---|---|
| **Track-changes authoring** | Home-grown, shipped. Our TipTap marks → `DocxAnnotationInput` → `DocxAnnotationWriter` emits **native `w:ins`/`w:del`**. No TipTap Pro. | `Services/Compose/DocxAnnotationWriter.cs`; `ComposeWorkspace.tsx:976-989` (`serializeForSave` → `redlineAnnotations`) |
| **Comment authoring** | Home-grown, shipped. Anchored annotations → **native `w:comment`**. | `DocxAnnotationWriter.cs`; `ComposeWorkspace.tsx:977` (`composeDocxAnnotations`) |
| **Main editor Save (clean)** | Byte-identical passthrough of the retained original — **faithful**. | `ComposeWorkspace.tsx:994` (`… : state.docxBytes`); `ComposeService.cs:352` (opaque baseline) |
| **Main editor Save (DIRTY)** | **Reconstruction (LOSSY).** Any dirty save rebuilds the whole `.docx` from the TipTap view via `serialize()` / `serializeForSave().baselineBytes`. This is the E1 gap. | `ComposeWorkspace.tsx:964,984-994`; export = `docx ^9.0.3` from TipTap JSON in `utils/docxBridge.ts` |
| **Annotation re-apply on Save** | The client sends a clean baseline + a structured annotation list; the BFF re-applies redlines/comments as native OOXML on top of the baseline **before** the SPE write. | `ComposeService.SaveAsync` `ComposeService.cs:353-359` (`_annotationWriter.Annotate`) |
| **True delta path (exists, unused by Save)** | `PushAnnotationsAsync` re-fetches the drive-item bytes from SPE and annotates **those** bytes (a real delta) with an atomic `If-Match` write. **The main Save does NOT use this.** | `ComposeService.cs:1107-1166` (download → `Annotate` → replace) |
| **Import (DOCX → editor)** | `mammoth ^1.8.0` flattens the `.docx` to simplified HTML. **Discards `paraId`, styles, headers/footers, multi-level numbering, and existing revision/comment marks.** Warnings captured, not surfaced (R2 partial). | `utils/docxBridge.ts:8,17-22,56-60,77-86` |
| **Existing-mark READER (server, exists!)** | `DocxAnnotationReader` reads **all** native `w:comment`/`w:ins`/`w:del` regardless of authorship (built to recover human Word edits) → `RecoveredComment` / `RecoveredRevision` with author/date/anchorText/paragraphHint. **The reader is not the gap — the editor mount is.** | `DocxAnnotationReader.cs:14-16,327-348` |
| **Redline suggestion payload** | `ComposeDraftPayload` carries `target_text` / `new_text` / `match_mode` / **`rationale`** / `sources` / `edits[]` / `comments[]`. Rationale **is** surfaced in the accept/reject UI. **No `confidence`, no explicit offsets/paraId.** | `ComposeEditor.tsx:271-294` |
| **Anchoring model** | `AnchoredAnnotationAnchor` = `textPattern` (content match) + `paragraphHint` (best-effort index) + `spanId` (editor-session-only). **No stable OOXML `w14:paraId`.** Levenshtein re-anchor scorer. | `types/compose-contracts.ts:108-115`; `Services/Compose/AnnotationReanchorService.cs` |

**Net**: the R3 seed's "build track-changes / consider TipTap Pro" framing (Scope B/C) is **superseded** — those are done home-grown. The live gaps are exactly (1) **round-trip fidelity on a dirty Save** and (2) **importing** pre-existing Word revisions/comments into the editor.

> **Key asset for E1**: the pristine load-time original is *already retained client-side* as `state.docxBytes` (`ComposeWorkspace.tsx:957-960`, set once on mount, never mutated) **and** is re-fetchable server-side via the SPE facade (`ComposeService.cs:1131`). E1 is about *using* that baseline for dirty saves — the bytes are not the missing piece; the **delta algorithm** is.

---

## 3. R3 Features — What Users Actually Do

### F1 — Edit a formatted contract, save, and get it back intact (E1)
A lawyer opens a 40-page contract (letterhead, numbered clauses, footnotes, a signature table). She edits three clauses and accepts two AI redlines. She saves. **The returned `.docx` is byte-for-byte the original everywhere she didn't touch** — header, footer, numbering, styles, embedded logo all intact — with her three edits and two redlines applied as tracked changes. Today this document comes back stripped to prose.

### F2 — Redlines and comments land in the exact right place, and survive edits elsewhere (E2)
An AI redline anchored to clause 12.3(b) stays on clause 12.3(b) after the user edits clause 4 — because the anchor is a stable OOXML paragraph identity, not a fuzzy text match that drifts. This also hardens the R2 stale-selection bug class (DEF-09 round-8 #3).

### F3 — See *why* and *how sure* for each suggested change (E3)
Each AI redline shows its rationale (ships today) **and a confidence signal** ("high / medium / low" or a band), so the lawyer accepts/rejects with informed judgment — the "offer a suggestion the user knowingly accepts with track changes" model.

### F4 — Open a document that *already has* Word revisions/comments and see them in Compose (import round-trip)
A document redlined in Word by opposing counsel opens in Compose with those `w:ins`/`w:del`/`w:comment` rendered as first-class tracked changes and comment threads — not flattened to prose. Authoring these is done (R2); **reading existing ones in** is the R3 gap.

---

## 4. E1 — Retained-Original Delta Save (THE keystone)

> ### 🔁 DESIGN AMENDMENT — 2026-07-17 (CLAUDE.md §6.5, owner-approved): Option C supersedes the WmlComparer path
>
> **The redline engine is no longer Docxodus `WmlComparer`.** The NFR-09 hardening gate (task 003) ran the WmlComparer path on real firm templates (Common Paper CSA + Mutual NDA) and proved it **strips `w14:paraId`** (→ internal `pt14:Unid`) and **drops unchanged tables** — a hard FR-07/NFR-07 + E2-anchor violation. An empirical net10 probe confirmed **Docxodus 7.1.0 (net10) reproduces both defects identically to 6.4.0 (net8)** — so it is inherent to the PowerTools `WmlComparer` algorithm on real docs, NOT a version/net-target gap. S1's "paraId preserved" was a synthetic-fixture artifact.
>
> **Adopted: Option C — self-synthesized paragraph redline (`ComposeParagraphRedlineSynthesizer`).** Since the client already tells us exactly which paragraphs changed (paraId-keyed, E2), we don't need a general document differ: for each edited paragraph we locate it in the retained original by `w14:paraId`, run a **word-level LCS diff** (old→new text), and emit native `w:ins`/`w:del` **in place**. Every other paragraph + all structure is byte-untouched, so `w14:paraId` + tables are preserved **by construction**. Validated on the real CSA (345/345 paraIds + 6/6 tables preserved). **This removes the Docxodus dependency entirely** (and its SkiaSharp-exclusion complexity) and retires tasks 001/020/021's comparer path.
>
> Where §4.2 below says "Docxodus `WmlComparer` synthesizes the redline" / "Approach A save the comparer output", read **"the synthesizer emits `w:ins`/`w:del` in place on the retained original"**. The **FR-05 format-change** representation (bold-a-word → `rPr`/`pPrChange`) is now handled explicitly by the synthesizer (compare run properties) rather than by the comparer. Evidence: `notes/spikes/S1-nfr09-real-template-hardening-2026-07-17.md`. The §4.2 text is retained below as the decision record.

### 4.1 The gap, precisely
On a **dirty** save, `ComposeWorkspace.triggerSave` sends bytes produced by the editor's `serialize()` (or `serializeForSave().baselineBytes`) — a full `.docx` reconstructed from TipTap JSON by the `docx` library (`docxBridge.ts`). That reconstruction can only represent what mammoth's simplified-HTML import preserved, so **headers/footers, section properties, multi-level numbering, custom/firm styles, hyperlinks, fields, footnotes, and embedded objects are gone** — regardless of whether the user touched them. `SaveAsync` then treats those bytes as an opaque baseline and re-applies redlines/comments on top (`ComposeService.cs:352-359`) — but the baseline is *already* lossy, so the annotations decorate a stripped document.

The clean-save path is faithful (`state.docxBytes` passthrough) — proving the retained original is available and correct. **E1 = extend the faithful-baseline model from the clean path to the dirty path.**

### 4.2 The central design decision — RESOLVED (D1, hybrid) — record of alternatives

To apply a dirty edit onto the *retained original* OOXML instead of a reconstruction, we must represent the user's edits as a **delta against the original bytes**. Two candidate models:

| | **(a) Everything-is-a-tracked-change** | **(b) Text-diff → OOXML paragraph patch** |
|---|---|---|
| **Idea** | Capture *all* editing (direct typing too, not just AI redlines) as `w:ins`/`w:del` deltas onto the retained original, via the existing `DocxAnnotationWriter` pipeline. | Diff each edited paragraph's text against the original, synthesize the minimal OOXML edits, and splice them into the retained original at paraId-anchored positions; untouched paragraphs pass through byte-identical. |
| **Reuses** | The whole R2 annotation pipeline (writer, accept/reject, native OOXML). Very high reuse. | The retained-original baseline + `paraId` map (E2). New diff/patch component. |
| **Fidelity** | High for the edited spans; the rest of the doc is the untouched original. | Highest — non-edited paragraphs are literally the original XML. |
| **Hard part** | Representing **free typing** as tracked-change marks (every keystroke → ins/del) without a heavy editor rewrite; the TipTap surface today only marks *AI* redlines. | The **paragraph add/remove/reorder** diff algorithm — historically hard, but **now largely solved by an off-the-shelf MIT engine** (Docxodus `WmlComparer`, see below). |
| **Legal fit** | Often *desirable* — "every edit is tracked" is the default expectation in contract markup. | Neutral — edits appear as clean text unless combined with track-changes. |

**The July-2026 research changes the risk calculus for model (b).** The "paragraph-diff → OOXML patch" that made (b) daunting is **prior art we adopt, not code we write**: **Docxodus 7.1.0** (MIT, .NET 10, released 2026-07-12 — a maintained fork of Microsoft's archived Open-Xml-PowerTools) ships `WmlComparer`, which compares two DOCX and emits the **minimal `w:ins`/`w:del` revision markup**, with **Move Detection** (relocations link via `MoveGroupId` instead of delete+re-insert) and **Format-Change Detection** (bold/italic/font-size-only). We never ask the model or a diff layer to emit raw revision XML — we rebuild the edited paragraph's OOXML and let the comparer synthesize the redline (the **adeu asymmetry**: editor produces *typed text edits*, the engine produces valid OOXML). This supersedes the Codeuctivity PowerTools fork R2 evaluated.

**Recommendation (for owner ratification)**: pursue a **hybrid anchored on the retained original**:

1. **Baseline = retained original OOXML** (client `state.docxBytes`, or server SPE re-fetch of the *load-time* version) for **every** save, dirty or clean. **Never reconstruct the whole document from TipTap again — drop `docx.js` from the export path entirely** (the research confirms no MIT JS library does high-fidelity round-trip; all fidelity work is server-side .NET).
2. **AI redlines + comments** → the existing `DocxAnnotationWriter` delta (already native OOXML, already accept/reject-able) — **no change needed**, it already writes onto a baseline.
3. **Direct user typing** → **model (b) via Docxodus** (S1-validated): rebuild only the edited paragraphs' OOXML (keyed by `w14:paraId`, E2), splice them into a copy of the retained original, and run `WmlComparer` to synthesize the minimal `w:ins`/`w:del`. Paragraphs the user never touched are **structurally/semantically preserved** — same content, styles, numbering, headers/footers, and **same `w14:paraId`** (S1 confirmed). *Fidelity nuance (S1):* WmlComparer's output **re-serializes** the XML (cosmetic normalization — BOM/whitespace), so it is faithful but **not byte-identical**. If a hard byte-identity requirement ever appears (OOXML-level digital signatures, byte-diffing), **Approach B** — splice WmlComparer's `w:ins`/`w:del` back into the retained-original bytes for the changed paragraphs only — restores literal byte-preservation. **MVP uses the simpler direct-save (Approach A)**; the re-serialization is cosmetic-lossless.
4. Offer **model (a)** as a per-document *"track all my edits"* toggle later (reuses the same writer), but do **not** gate R3 on capturing free typing as tracked changes.

This makes **E1 depend on E2** (paraId identity is the splice key) — confirming the findings' "treat them as ONE workstream." It keeps the blast radius on the *save* path, reusing the proven `PushAnnotationsAsync` download-annotate-replace shape (`ComposeService.cs:1107`) as the reference implementation, plus one new MIT NuGet (Docxodus — measure publish-size, §12).

> ✅ **RESOLVED (D1, owner review 2026-07-16): the hybrid is ratified.** The alternatives (pure-(a), pure-(b), read-only Compose) are retained above as the decision record. **The single highest-priority Phase-0 spike (§14, S1): does Docxodus `WmlComparer` preserve `w14:paraId` on *unchanged* paragraphs, or regenerate them like Word?** If it regenerates, we run the comparer on a copy and map results back by content — a known, bounded workaround, but it must be proven before the build commits.

### 4.3 Where the baseline lives
- **Client**: `state.docxBytes` is the pristine mount payload, already retained for the life of the mount (`ComposeWorkspace.tsx:957-960`). Preferred source — no extra fetch.
- **Server (S4 decision — primary)**: the baseline is the **load-time SPE version, fetched by `versionId`** captured at Load. SPE versions files, so the load-time original is addressable even *after* prior dirty saves — this is authoritative, survives a page refresh, and needs **zero new storage**. It requires a small `SpeFileStore` addition to fetch a specific version's content (`/versions/{id}/content`, a known Graph capability) — a spec task. The client-retained `state.docxBytes` is a **same-session fast-path** (skip the re-fetch when present); a size-capped Redis cache (ADR-009, ADR-015 Tier-3) is the fallback if version-by-id fetch proves impractical. This supersedes the earlier "client-retained default" — client bytes don't survive a refresh, and the SPE *current* version is degraded after the first dirty save, so neither alone is a robust baseline.

---

## 5. E2 — Paragraph/Position Identity (`w14:paraId` anchoring)

### 5.1 The gap
Anchors are fuzzy today: `textPattern` + best-effort `paragraphHint` index + a session-only `spanId` (`compose-contracts.ts:108-115`), re-scored with Levenshtein on return (`AnnotationReanchorService.cs`). mammoth **discards** `w14:paraId` on import, so there is no stable identity carried from the original OOXML into the editor and back.

### 5.2 The design (grounded in the MS-DOCX spec + 2026 research)
- **`w14:paraId` is a first-class OOXML concept** on `w:p` (`ST_LongHexNumber`, unique within the part, `0 < x < 0x80000000`; MS-DOCX spec). It is **assigned on creation and preserved across edits**; its sibling `w14:textId` **refreshes when the paragraph's content changes** — so `paraId` = stable anchor, `textId` = content-dirty flag we can exploit.
- **Preserve `paraId` on import.** mammoth can't (it flattens to HTML). Server-side, run a lightweight Open XML pre-parse alongside the mammoth convert that extracts an ordered `paraId → paragraph` map and **sets each `paraId` explicitly as a hidden ProseMirror node attribute at load time** (a transaction stamping `paraId` per paragraph — S2 confirmed `UniqueID` does NOT auto-assign to `setContent`-loaded nodes, so the server pre-parse owns load-time ids).
- **Split/merge minting is handled by `@tiptap/extension-unique-id` (3.28.0, MIT) — no custom plugin (S2 simplification).** Configure `UniqueID.configure({ types: ['paragraph'], attributeName: 'paraId', generateID })`. S2 proved: on a paragraph split, UniqueID's built-in dedup keeps the original id on one half and mints a fresh id on the other; untouched paragraphs keep their ids. Point `generateID` at an **8-hex `< 0x80000000` generator** so new-paragraph ids are valid `w14:paraId`s directly (the Open XML SDK exposes `Paragraph.ParagraphId` but has no generator — issue #962 — so we own the generator regardless).
- **Anchor annotations/redlines to `paraId`**, not text-pattern + index. `AnchoredAnnotationAnchor` gains a `paraId` field (additive; `textPattern`/`paragraphHint` remain).
- **⚠️ The Word-regeneration caveat (critical, from research).** **Word regenerates *all* `paraId`s on save when tracked changes or comments are added** (Open-XML-SDK #925). So `paraId` is stable *within our own load→edit→save round-trip* but **NOT across an external Word-for-Web / desktop edit session**. This means E2 does **not** retire the existing `AnnotationReanchorService` fuzzy matcher (`textPattern` + Levenshtein + `paragraphHint`) — it **promotes `paraId` to the primary anchor for our own round-trip and keeps the fuzzy matcher as the cross-Word-session re-anchor fallback**. The two compose cleanly: try `paraId` first, fall back to content match when the paraId is gone.
- **On save**, the paraId map is the splice key for E1's Docxodus patch — the same substrate serves both.

E2 is the **identity substrate E1 needs**; building them together avoids a throwaway fuzzy-anchor iteration and reconciles the new anchor with the shipped re-anchor service rather than replacing it.

---

## 6. E3 — Enriched Redline Contract (reason + confidence + offsets)

### 6.1 The gap
`ComposeDraftPayload` already carries `rationale` + `sources`, and the accept/reject popover already renders the rationale (`ComposeEditor.tsx:271-294`; `redlineLabelText`). Missing: a **confidence** signal and **explicit character offsets / paraId**.

### 6.2 The design — rationale-first, confidence-as-a-coarse-band (revised per 2026 HCI research)

The July-2026 research adds an important caution: **a numeric confidence score is the weakest and riskiest signal.** 2025–26 HCI studies (arXiv 2402.07632; tandfonline 2025 appropriate-reliance) show high confidence scores drive *over-reliance* and false precision, measurably degrading professional decisions, and a verification-bottleneck effect where users rubber-stamp more as difficulty rises. So E3 is designed **rationale-first**:

- **Lead with the cited rationale** as the primary trust cue — for lawyers, *why* + *grounded in what* beats a number. This already ships; keep it prominent in the accept/reject surface.
- **Confidence, if shown, is a coarse qualitative band tied to *verifiability*, not a model self-report.** Prefer `confidence_band?: 'high' | 'medium' | 'low'` derived from **grounding** (is the change supported by a cited source / the retained original?) over a raw `0–1` model-emitted number. Avoid false-precision scores.
- **Engineer against rubber-stamping**: never pre-select or auto-accept low-confidence edits; low-confidence items get explicit-review affordances, not a one-click accept-all.
- Add explicit `paraId` + `start_offset` / `end_offset` to the anchor — these **ride with E2** (offsets are only meaningful against a stable paragraph identity). Do not build offsets before E2.
- Contract change is still additive: `confidence_band` (+ optional numeric with the caveats above) on `ComposeDraftPayload` (client mirror) and `ComposeDraftPayload.cs` (server). snake_case, Tier-3-safe (enum/number, not user content).
- **Formatted AI insertions (S5 finding, supports D4)**: the shipped apply path is slice-safe (existing formatting preserved), but `ComposeDraftPayload.new_text` is a plain **string**, so an *AI-inserted* replacement carries no bold/italic. For D4 (run-level fidelity), enrich `new_text` into a **formatting-bearing fragment** (or add a structured runs field) and update `buildInsertionHtml` to emit the marks — a bounded change at the payload + insertion-HTML boundary, **not** the mark/range layer (which is already correct, `usePendingRedline.ts:461-465`).
- **Eval-case obligation** (ADR-039/ADR-038): if the band is model-supplied, the Action's JPS output schema declares it and eval cases assert it renders; if computed (e.g. from grounding / re-anchor score), document the derivation. **Property-level boolean `required` remains BANNED** (owner hygiene rule).

E3-rationale is already done; R3 adds the **grounding-tied confidence band** (not a raw score) + offsets-with-E2. This is the item to design deliberately *before* building — the cheap-to-add field is the easy part; getting the trust semantics right is the point.

---

## 7. Import Round-Trip (Scope B/C — authoring done, reading in missing)

**The reader already exists.** `DocxAnnotationReader.cs` reads **all** native `w:ins`/`w:del`/`w:comment` regardless of authorship — it was explicitly built to recover human Word-for-Web edits (`DocxAnnotationReader.cs:14-16`), returning `RecoveredComment`/`RecoveredRevision` with author/date/anchorText/paragraphHint. The gap is **not a missing reader** — it's that the **editor mount path uses mammoth**, which flattens those marks to prose before the editor ever sees them (`docxBridge.ts:77-86`). So R3's import round-trip is *wiring*, not *building*:

- **On Load, run the existing reader** (server-side, alongside the mammoth convert) and project `RecoveredRevision`/`RecoveredComment` onto the Load response; **render them as first-class TipTap tracked changes / comment threads** in the editor mount (author, timestamp, replies) instead of letting mammoth flatten them.
- **Attach them by `paraId`** (E2) so they survive the retained-original save (E1) — the same substrate the fidelity core builds.

**In R3 core (D2)** — it runs as a parallel track behind the E1/E2 fidelity core (which it depends on for the paraId substrate). Because the reader is done, the remaining cost is the Load-response projection + the in-editor render, so it is a bounded add rather than a greenfield build.

---

## 7.5 The Editing Surface — Credible Tools Without Recreating Word

The owner's core goal is *a credible AI authoring surface with expected core editing tools, without recreating Word.* The July-2026 research draws that line clearly, and it is more favorable than it looks: **we already own the hard part** (native `w:ins`/`w:del`/`w:comment` marks + OOXML map + accept/reject). The gap to "credible" is the *everyday* toolset, not Word parity.

**In R3 scope — table-stakes for a credible 2026 editor** (all achievable on the MIT TipTap base + MIT extensions):

| Capability | Source |
|---|---|
| Bold / italic / underline / strikethrough | StarterKit (MIT, shipped) |
| Headings + paragraph styles | StarterKit + style mapping (shipped/extend) |
| Ordered / unordered / **nested** lists | StarterKit + list extensions (shipped) |
| Tables (basic) | `@tiptap/extension-table` (MIT, shipped) |
| Links | MIT link extension (shipped) |
| **Find / replace** | Build (small; no MIT-Pro dependency) — a common credibility gap today |
| Comments | Home-grown marks → `w:comment` (shipped, R2) |
| Track changes + accept/reject | Home-grown marks → `w:ins`/`w:del` (shipped, R2) |
| Undo / redo, clean paste | StarterKit (shipped) |
| Pinned/sticky toolbar, one-line bubble menu | R2 UAT round-3 items (DEF-16/DEF-17) — carry in |

**Newly-MIT extensions worth considering** (TipTap open-sourced 10 formerly-Pro extensions under MIT in June 2025 — *not* on our forbidden list): `TableOfContents`, `DragHandle`, `Mathematics`, and **`UniqueID`** (the E2 paraId carrier). Verify each resolves to `@tiptap/extension-*` (MIT), never `@tiptap-pro/*`, before adding.

**Deferred to "Open in Word"** (round-trip, don't rebuild) — these are where "credible editor" would tip into "trying to be Word":
pagination / page layout, footnotes & endnotes numbering, complex multi-level numbering schemes, cross-references / TOC field computation, full styles-pane management, print fidelity.

**This is a validated architecture, not a compromise.** The 2026 legal leaders — **Harvey and Spellbook — ride *native* Word track changes** via a Word add-in rather than build a bespoke web redline surface. Deferring heavy features to Open-in-Word (already shipped as `useDocumentActions`) is the accepted pattern, not a shortcoming. Compose's differentiator is the **AI-native drafting loop with high round-trip fidelity**, not a browser reimplementation of Word.

> **Scope guard**: the toolbar scope must be pinned in `spec.md` so it does not creep toward Word parity. "Find/replace + basic tables + the formatting toolbar" is the R3 credibility target; anything on the Open-in-Word list is out.

---

## 8. Architecture — the retained-original save pipeline

```
LOAD:
  SPE .docx ──► (server) Open XML pre-parse ──► stamp/collect w14:paraId map (E2)
            └─► mammoth ──► simplified HTML ──► TipTap  (+ hidden paraId node attr)
            └─► DocxAnnotationReader ──► existing w:ins/w:del/w:comment ──► editor marks (import round-trip)
  client retains pristine bytes = state.docxBytes  (baseline for save)

EDIT:
  AI redline  ──► TipTap ins/del marks ──► DocxAnnotationInput          (R2, unchanged)
  direct type ──► TipTap text change (paraId-tagged block)             (E1/E2)

SAVE (dirty):
  baseline = retained ORIGINAL bytes (NOT a TipTap reconstruction)      ◄── E1: drop docx.js export
    ├─ rebuild ONLY edited paragraphs' OOXML, spliced by paraId          ◄── E1 + E2
    │     └─ Docxodus WmlComparer ──► minimal w:ins/w:del (+move/format)  ◄── MIT engine, not hand-written
    ├─ apply AI redlines + comments via DocxAnnotationWriter             ◄── R2, reused verbatim
    └─ untouched paragraphs preserved (same content/styles/numbering     ◄── the fidelity guarantee
       /paraId); WmlComparer re-serializes cosmetically (Approach A).
       Optional Approach B splices ins/del back into original bytes
       for literal byte-identity if ever required.
  ──► SPE ReplaceFileContentAsUserAsync (If-Match, atomic)              ◄── ComposeService.cs:1107 pattern
```

The load-bearing change is a single inversion: **the save baseline becomes the retained original, and edits are deltas onto it** — instead of the baseline being a reconstruction and the original being discarded. Everything else is reused or off-the-shelf: the annotation writer, the accept/reject workflow, and the atomic If-Match write already exist (R2); the paragraph-redline synthesis is an MIT engine (Docxodus `WmlComparer`), not a bespoke algorithm. The only genuinely new code is the paraId carry-through (E2) and the save-path orchestration that rebuilds edited paragraphs and drives the comparer.

---

## 9. BFF Surface (R3)

R3 adds **no new AI dispatch endpoint** (ADR-039 — unchanged from R2). The changes are to the *save/load* orchestration, not the AI seam.

| Endpoint | Change |
|---|---|
| `POST /api/compose/documents/{speId}/save` + `/create-on-save` | **Extend** `ComposeService.SaveAsync` (`ComposeService.cs:315`) to apply direct-typing edits as a **delta onto the retained original baseline** (E1) instead of persisting a client reconstruction. Contract shape unchanged; the baseline semantics change (client sends original bytes + a paragraph-edit list, or the server re-derives from the load-time cache). **This is the core R3 BFF task.** |
| `GET /api/compose/documents/{speId}` (Load) | **Extend** `LoadAsync` (`ComposeService.cs:168`) to emit the **`paraId` map** (E2) and, for import round-trip, structured **existing** revisions/comments (extends `DocxAnnotationReader`). |
| `ComposeDraftPayload` / anchor contracts | **Additive** `confidence` + `paraId`/offsets (E3). Mirror-first under `Spaarke.Compose.Components/src/types` ↔ `Services/Compose/*.cs`. |

No new microservice, no Dataverse plugin. Open XML SDK is already a BFF dependency (`Sprk.Bff.Api.csproj`) → **0 MB publish add** for the pre-parse/splice; measure per task (baseline ~49.63 MB incl. PDBs; ceiling 60 MB).

---

## 10. Component Reuse Map (per CLAUDE.md §11)

| Need | Reuse from | Net-new in R3 |
|---|---|---|
| Native-OOXML track-change/comment writer | `DocxAnnotationWriter.cs` (R2) | — (reused verbatim for redlines/comments) |
| Retained-original delta shape | `PushAnnotationsAsync` download→annotate→replace (`ComposeService.cs:1107`) | Generalized into the main Save path |
| SPE read/write facade | `SpeFileStore` / `ISpeFileOperations` (`DownloadFileAsUserAsync`, `ReplaceFileContentAsUserAsync`) | — |
| Original bytes baseline | client `state.docxBytes` (retained mount payload) | Save uses it for dirty saves (E1) |
| Editor framework | TipTap **MIT base** + R2 custom marks (`insertion`/`deletion`/`commentAnchor`) | + `paraId` block attribute (E2, via MIT `@tiptap/extension-unique-id` 3.28.0 — split-minting built-in, S2; `generateID` → OOXML-shaped); **NO TipTap Pro** |
| Import parser | mammoth 1.12.0 (R1, MIT) | + server-side Open XML pre-parse for paraId map (E2) |
| Existing-mark reader (import round-trip) | `DocxAnnotationReader.cs` (R2 — reads all `w:ins`/`w:del`/`w:comment` regardless of authorship) | + Load-response projection + in-editor render of `RecoveredRevision`/`RecoveredComment` (was flattened by mammoth) |
| **DOCX export** | ~~`docx.js` (R1 reconstruction)~~ **REMOVED from export path** | Export = retained-original splice server-side; docx.js dropped (lossy) |
| **Paragraph redline synthesis** | — | **ADOPT `Docxodus` 7.1.0 (MIT, .NET 10)** `WmlComparer` — off-the-shelf, not hand-written. Supersedes the Codeuctivity PowerTools fork. §12 justifies the NuGet add. |
| AI apply-path reference | — | Mine `davefowler/prosemirror-suggestion-mode` `applySuggestion` text-match helper (MIT) as a *reference* for LLM `target_text`/`new_text` → suggestion marks (pattern, not dependency) |
| Redline payload | `ComposeDraftPayload` (R2) | + grounding-tied `confidence_band` + offsets/`paraId` (E3, additive) |
| Re-anchor service | `AnnotationReanchorService.cs` (R2) | + `paraId` as primary anchor for our round-trip; **text/index fuzzy match retained as cross-Word-session fallback** (Word regenerates paraIds) |
| AI dispatch / routing | Session-dispatch seam + Binding table (ADR-039) | — (no new endpoint) |

---

## 11. ADR Tensions (per CLAUDE.md §6.5)

| Topic | ADR / rule | Path | Rationale |
|---|---|---|---|
| **Save baseline semantics change** (reconstruction → retained-original delta) | R2 decision "Save regenerates `.docx` from editor" (project-level, 2026-07-08) | **Path B — supersede the R2 decision** | The R2 "regenerate from editor" decision is exactly the fidelity defect; R3 amends it to "delta onto retained original." Documented here + carried into spec. |
| **E1 delta algorithm** (adopt Docxodus vs build) | CLAUDE.md §11 (default to reuse) | **Path C — comply via off-the-shelf reuse** | The 2026 research found a maintained MIT engine (`Docxodus` `WmlComparer`) that synthesizes the paragraph redline — so this is a **dependency adoption, not a hand-written component**. Passes the §11 three-question test: *existing* = none in-repo; *extension* = the closest (`DocxAnnotationWriter`) covers only the AI-redline subset; *cost-of-doing-nothing* = dirty saves stay lossy. New NuGet → publish-size measured (§12). |
| **New NuGet dependency (Docxodus)** | §10 BFF hygiene (publish-size, CVE) | **Path C — comply, measured** | Docxodus adds to the publish output (PowerTools-lineage, non-trivial). Measure the delta against the +5 MB single-task escalation trigger; verify no new HIGH CVE. If it exceeds budget, fall back to the Codeuctivity fork or scope the splice to a WASM/out-of-process boundary. |
| **`paraId` on import** requires an Open XML pre-parse alongside mammoth | ADR-007 (Graph isolation) / ADR-013 (AI facade) | **Path C — comply** | Pre-parse runs server-side behind the `Services/Compose` boundary, no Graph types leak, no AI internals injected. Open XML SDK already a BFF dep. |
| **No TipTap product features** | Owner rule 2026-07-14 | **Path C — comply** | Everything on the MIT base; paraId is our block attribute; diff/patch is our code. Zero `@tiptap-pro`. |
| **No new AI dispatch endpoint** | ADR-039 | **Path C — comply** | R3 touches save/load orchestration, not the AI seam. E3 confidence is an additive field on the existing `ComposeDraftPayload`. |
| **`AnchoredAnnotation` stays Compose-domain (not a MemoryItem)** | Core charter §3.4 (inherited from R2) | **Path A — inherited deviation** | Adding `paraId`/offsets keeps it positional document-adjacent UI state; not written via `memory.*`. Same argument R2 made. |

**Actions**: file the R2 "regenerate from editor" decision amendment (Path B) at R3 spec authoring; carry the paragraph-diff new-component justification (Path A) into spec §11; resolve the §4.2 E1 fork with the owner before the build.

---

## 12. Placement Justification (per CLAUDE.md §10) + Hot-Path Declaration

All R3 server work belongs in `Sprk.Bff.Api` (`Services/Compose/`). No new microservice, no Dataverse plugin.

1. **DOCX manipulation is server-side** — the retained-original splice, paraId pre-parse, and OOXML patch run in the BFF where Open XML SDK + SPE OBO auth already live. Browser-side OOXML surgery is infeasible at legal-document scale.
2. **Reuses existing BFF seams** — `ComposeService.SaveAsync`/`LoadAsync`, `DocxAnnotationWriter`, `SpeFileStore` facade. Extension, not new surface.
3. **Publish-size (S1/S3 measured)**: the Open XML SDK is already referenced (a minor 3.4.1 → 3.5.1 bump). **`Docxodus` managed = 2.44 MB** (~1 MB compressed). Its `SkiaSharp` transitive dependency ships an **11.6 MB native** (`libSkiaSharp.dll`, win-x64) — but S1 proved **`WmlComparer` runs with SkiaSharp fully removed**, so **exclude SkiaSharp assets** (`<ExcludeAssets>runtime;native</ExcludeAssets>` on the transitive ref, or a targeted `runtimes/` exclusion) in `Sprk.Bff.Api.csproj`. **Binding packaging note**: any Docxodus code path that touches `HtmlToWml`/`FormattingAssembler` would re-pull SkiaSharp — the R3 splice must NOT use those. Net add ≈ **2.44 MB uncompressed (~1 MB compressed)**, well under the 60 MB ceiling / +5 MB trigger. Re-verify the exclusion holds when the BFF actually publishes (task-time check).
4. **AI facade intact (ADR-013)** — `Services/Compose/` injects no `IOpenAiClient`/executor/routing types; E3 confidence rides the existing catalog Action output, not a new AI call.
5. **Off-the-shelf, not net-new (§11)** — the paragraph-redline synthesis is adopted (`Docxodus WmlComparer`, MIT), not hand-written; the only genuinely new *code* is the paraId carry-through + save-path orchestration. *Cost-of-doing-nothing*: dirty saves remain lossy — the exact defect R3 exists to fix.
6. **Test obligation** — every new/changed `Services/Compose/` service gets matching tests in `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/`, **plus** a through-the-wire slice test per the compose-r2 E2E Definition-of-Done (a `WebApplicationFactory` save-a-formatted-doc → assert non-edited OOXML survives byte-identical + edits applied). Unit-green ≠ done.

**Hot-Path Declaration**: BFF=Y · SpaarkeAi=Y · ci-workflows=N · skill-directives=N · root-CLAUDE.md=N.

---

## 13. Constraints (binding)

- **NO TipTap product features — paid OR unpaid.** MIT base only. (Owner rule 2026-07-14.) The newly-MIT ex-Pro extensions (`UniqueID`, `TableOfContents`, `DragHandle`, `Mathematics`) are allowed — but verify each resolves to `@tiptap/extension-*` (MIT), never `@tiptap-pro/*`, before adding to `package.json`.
- **Permissive licenses only — AVOID AGPL/copyleft.** `Docxodus` (MIT), Open XML SDK (MIT), TipTap MIT base, mammoth/docx-preview (MIT) are fine. **Do NOT vendor `SuperDoc` (AGPL-3.0), ONLYOFFICE, or LibreOffice code** — borrow architecture patterns only. Do not adopt the archived Eigenpal `@eigenpal/docx-editor`.
- **No new AI dispatch endpoint** (ADR-039). E3 is additive fields on `ComposeDraftPayload`.
- **Ledger-first** for any AI edit payload (ADR-040) — unchanged from R2.
- **AI facade** — `Services/Compose/` never injects AI internals (ADR-013, Tier-1 NetArchTest).
- **Graph isolation** — no `Microsoft.Graph` types above `SpeFileStore` (ADR-007).
- **Redis-first** for any load-time-baseline cache (ADR-009).
- **NO version suffix** in action codes / Binding names / mirror filenames / data-row values (owner hygiene).
- **Publish size ≤ 60 MB** compressed; measure per BFF task.
- **Engine frozen** — no new `sprk_analysisplaybook` records.
- **E2E Definition-of-Done** — through-the-wire slice test required for any save/load/dispatch change (inherited from R2 CLAUDE.md).

---

## 14. Spike Plan (Phase 0 — before the build commits)

| # | Spike | Decision unlocked | Status |
|---|---|---|---|
| **S1** | **Docxodus `WmlComparer` on a firm-styled legal `.docx`** (custom style, 2-level clause numbering, header/footer, footnote, paraIds). (a) paraId preservation on unchanged paragraphs? (b) untouched parts preserved? (c) minimal `w:ins`/`w:del` incl. run-format change? | Confirms the E1 engine + paraId-anchor validity. | ✅ **DONE 2026-07-16 — PASS.** [`notes/spikes/S1-docxodus-wmlcomparer-2026-07-16.md`](notes/spikes/S1-docxodus-wmlcomparer-2026-07-16.md). paraIds preserved on all unchanged paras; all structural parts preserved (but **re-serialized**, not byte-identical — cosmetic only); 3 ins/2 del + format-change detected; author attribution works. |
| **S3** | **Publish-size of Docxodus** vs the 49.63 MB baseline; CVEs. | §12 budget headroom (or fallback). | ✅ **DONE 2026-07-16 — MITIGATED.** Docxodus managed = 2.44 MB; the risk was **SkiaSharp native (11.6 MB win-x64)** — but **WmlComparer runs with SkiaSharp fully removed**, so exclude it. Net add ≈ 2.44 MB uncompressed (~1 MB compressed). |
| **S1b** | **Harder fixture** — nested table, 3-level numbering; edits = table-cell text, whole-paragraph **delete**, paragraph **split**. | Hardens S1 on the flagged edge cases. | ✅ **DONE — PASS.** All unchanged paraIds preserved incl. table-cell + **nested-table-cell** + 3-level; both tables intact; delete → `w:del`-marked para; split → both paras present. [`notes/spikes/S1b-S2-S4-S5-2026-07-16.md`](notes/spikes/S1b-S2-S4-S5-2026-07-16.md). *Re-run on real firm templates before build-freeze.* |
| **S2** | **`w14:paraId` carry-through in TipTap** — pre-set paraIds (server pre-parse) → survive edits elsewhere; split re-mints one half. | Validates E2 substrate + minting. | ✅ **DONE — PASS.** Headless TipTap v3 + `@tiptap/extension-unique-id` 3.28.0 (MIT). Untouched paraIds preserved; split keeps one id, re-mints the other via UniqueID's built-in dedup — **no custom plugin needed**. Load-time ids set explicitly; `generateID` → OOXML-shaped. |
| **S4** | **Baseline source** — client bytes vs re-fetch vs cache; correctness after prior dirty saves. | Picks the E1 baseline. | ✅ **DECIDED.** Primary = **load-time SPE version by `versionId`** (authoritative, survives refresh + prior saves, zero new storage); client-retained `state.docxBytes` = same-session fast-path; Redis cache = fallback. Adds a `SpeFileStore` version-content fetch (spec task). |
| **S5** | **Track-changes slice-safety** — do shipped ins/del marks preserve inline formatting (avoid the davefowler flatten bug)? | De-risks "credible editor without TipTap Pro". | ✅ **DONE — PASS.** Apply path is **mark-over-range / slice-safe**; existing bold/italic preserved. One R3 item: enrich `ComposeDraftPayload.new_text` → formatting-bearing so AI *insertions* carry runs (supports D4). |
| **S6** (opt.) | **Import round-trip** — run the existing `DocxAnnotationReader` on Load and render `RecoveredRevision`/`RecoveredComment` in-editor without mammoth flattening. | Scopes the B/C import work. | 🟢 low — deferred to a build-time task (reader already exists, §7). |

**ALL gating spikes have passed.** S1/S1b validated the OOXML fidelity + paraId core; S2 validated the editor substrate (and simplified it); S3 resolved publish-size; S4 settled the baseline; S5 confirmed the apply layer is slice-safe. **No design pivots.** The only pre-build residual is S1b-on-real-templates (a hardening re-run, not a gate). The design is ready for `/design-to-spec`.

---

## 15. What NOT to build in R3

- **Multi-user co-editing / CRDT** — R5+ (unchanged from R1/R2 non-goals).
- **TipTap Pro anything** — banned (owner rule).
- **New AI dispatch endpoint** — banned (ADR-039).
- **Word's full authoring UX at parity** (complex tables, SmartArt authoring, equation editing) — Open-in-Word remains the escape hatch for features beyond the retained-original passthrough.
- **Capturing every keystroke as a tracked change** by default — offered as a *later* opt-in toggle (E1 model (a)), not an R3 gate.
- **SharePoint version-history rewrites** — consume existing version endpoints only.

---

## 16. Decisions & Remaining Spike-Gated Items

**Resolved at owner review 2026-07-16 (see §0):**
1. ✅ **E1 approach (D1)** — hybrid ratified.
2. ✅ **R3 scope (D2)** — everything: fidelity core + E3 + toolset + import round-trip.
3. ✅ **E3 confidence (D3)** — grounding-tied qualitative band, rationale-first; derived from grounding/re-anchor evidence (no forced catalog-Action change; if a band is model-supplied it's declared in the JPS output schema + eval cases).
4. ✅ **Direct-typing fidelity (D4)** — text + run-level formatting in the MVP (WmlComparer Format-Change Detection).
5. ✅ **Toolbar scope** — the §7.5 "credible toolset" line is accepted (find/replace + basic tables + toolbar polish IN; Open-in-Word deferral list accepted). To be pinned in `spec.md`.

**All spike-gated items are now RESOLVED (2026-07-16) — nothing open before `/design-to-spec`:**
- ✅ **Docxodus adoption** — ADOPT (S1/S1b/S3), SkiaSharp excluded; Codeuctivity fork = documented fallback.
- ✅ **E1 baseline source** — load-time SPE version by `versionId` (primary) + client fast-path + Redis fallback (S4).
- ✅ **paraId split/merge minting** — `UniqueID` built-in, no custom plugin (S2).
- ✅ **Track-changes slice-safety** — confirmed slice-safe (S5); enrich `new_text` for formatted AI insertions.
- ✅ **Byte-identity level** — MVP = Approach A (WmlComparer re-serialized output); Approach B splice-back deferred unless a hard requirement appears.

**Carried into `spec.md` as build-time tasks/gates** (not open decisions): re-run S1b on real firm templates before build-freeze; add `SpeFileStore` version-content fetch; configure `UniqueID.generateID` for OOXML-shaped ids; enrich `ComposeDraftPayload.new_text` to formatting-bearing.

---

## Footer

**Next step**: resolve the §4.2 fork + Open Items with the owner, run **S1 (the gating Docxodus spike)** + S2, then `/design-to-spec projects/spaarkeai-compose-r3` → `/project-pipeline projects/spaarkeai-compose-r3`. All work on the MIT TipTap base — no TipTap product features; permissive licenses only (no AGPL). Sources of truth: this design + [`ooxml-fidelity-findings.md`](ooxml-fidelity-findings.md) + [`notes/tiptap-docx-fidelity-research-2026-07-16.md`](notes/tiptap-docx-fidelity-research-2026-07-16.md) + the file:line-grounded as-built (§2).

*Generated 2026-07-16 as the first R3 design pass, code-grounded against the merged compose-r2 build and refreshed with July-2026 best-practices research.*
