# Compose — Focused Narrow Fix Plan (ID-anchored save + concurrency)

> **Created**: 2026-07-22
> **Scope guard**: This plan touches ONLY our OOXML↔TipTap translation + save layer. NOT WOPI, NOT a commercial component, NOT an editor rebuild, NOT a feature change, NOT a Word replacement. Feature set is right; TipTap is not at fault; the defect is preservation-fidelity in *our* mapping/save.
> **Origin**: Independent senior-dev review (2026-07-22) that landed on the exact architecture we re-grounded to. This plan reconciles that review against the *actual code* and scopes the two remaining gaps.

---

## 1. Headline finding

The senior-dev architecture (OOXML = source of truth; editor = lossy projection; edits as ID-anchored operations; apply surgically with Open XML SDK; anchor the LLM by ID; concurrency via version-stamp + re-anchor) is **~80% already built** in this codebase. We are on the right path, not a wrong one. The remaining defect surface is narrow and specific.

### Done-vs-gap map (verified against code, 2026-07-22)

| Architecture principle | Status | Where |
|---|---|---|
| Source `.docx` untouched; delta-onto-original, never re-serialize editor→docx (loaded docs) | ✅ Done | `ComposeParagraphRedlineSynthesizer`; client never authors bytes (`docxBridge.ts` §Export path) |
| Provenance ingest — `w14:paraId` on every paragraph, single walk | ✅ Done (Phase 1) | `ComposeDocxProjectionBuilder`, `stampParaIds`, `captureParaIdSnapshot` |
| Edits as ID-anchored operations | ⚠️ **Split** — synthesizer path is ID-anchored; `DocxAnnotationWriter.LocateTarget` still whole-doc text-searches | `collectEditedParagraphs` (good) vs `DocxAnnotationWriter` (gap) |
| Apply with Open XML SDK, native `w:ins`/`w:del`/`w:comment` | ✅ Done | `DocxAnnotationWriter`, `ComposeParagraphRedlineSynthesizer` |
| Anchor the LLM by ID | ⚠️ **Partial** — re-anchor uses paraId as primary (FR-11) but LLM *initial* emission ships `target_text` | `AnnotationReanchorService.ResolveByParaId` (reload) vs draft/redline payload (emission) |
| Concurrency: version-stamp snapshot + re-anchor on stale + fuzzy-as-last-resort-comment | ⚠️ **Machinery exists, not wired into save** (= Bug B) | `AnnotationReanchorService` (bands/ambiguity/ORPHAN) exists; save-write eTag sequencing open |

**Conclusion**: no re-architecture. Two narrow changes (plus one client-side hardening, §3.5) close the defect classes we saw in UAT.

### Deliberate design choice: paragraph-granularity, NOT run/step-level (do not adopt)
Three reviewers converged on the shadow-document/patch model; one (2026-07-22) additionally recommended tagging every **run** (`w:r`) and capturing raw **ProseMirror step-level** deltas. We deliberately use **paragraph-level (`w14:paraId`) tagging + paragraph-granularity diff**, and keep it:
- **paraId is Word-native and survives round-trips**; injected run-ids do NOT (Word re-splits runs constantly — run anchoring is *less* stable than paragraph+offset).
- Paragraph-granularity delta is coarser but **sufficient** for the byte-preserving synthesizer: untouched paragraphs are literally untouched; edited ones are reinterpreted whole.
- The only capability finer granularity buys is **structural edits** (paragraph insert/split/delete), currently out of the E1 delta scope. Revisit ONLY if that becomes a requirement — adopting run/step-level now is a rebuild for a problem we don't have (against the re-grounding).

### Third-reviewer vindication of Phase 1
The 2026-07-22 reviewer independently prescribed "Do NOT use mammoth.js — write a custom XML-to-HTML mapper mapping `w:p`→`<p>` carrying the IDs." That is exactly what Phase 1 shipped (`ComposeDocxProjectionBuilder` replaced mammoth). Confirmation, not new work.

---

## 2. Gap 1 — Collapse the text-search seam to ID-anchored (kills the interior-location 422 class)

### Root cause
`DocxAnnotationWriter.AnnotationSession.LocateTarget(target)` walks **every** `body.Descendants<Paragraph>()`, concatenates run text, and `IndexOf`s the target string across the whole document. Two fallback layers (typographic fold `ComposeTextFold`, whitespace-collapse) are band-aids over the fundamental fragility: whitespace / `<w:tab/>` / `<w:br/>` / run-split / typographic drift make the projected text diverge from what the annotation carries → `TargetNotFound` → HTTP 422 ("a tracked change could not be located"). Interior locations (tab-laid-out list items) are the worst offenders.

Bug A already routed **AI redlines** off this path onto the paraId synthesizer (comment-kind only remains on the writer). But **comments** — and any residual anchored annotation — still text-search the whole body.

### The fix (populate-existing, not new plumbing)
The client already carries `paraId` on every block (`paraIdOf` in `docxBridge.ts`). Thread it to the writer and resolve by id first.

1. **`DocxAnnotation` (server)** — add `TargetParaId` (string, optional) and optional intra-paragraph `TargetOffset`/`TargetLength` (or keep `TargetText` as the *within-paragraph* needle). Backward-compatible: null `TargetParaId` → today's whole-doc behavior (legacy docs with no ids).
2. **`LocateTarget`** — new resolution order:
   - **(a) paraId hit** → resolve the single paragraph by `w14:paraId` (O(1) dictionary, same extraction as `ParaIdPreParser` / `AnnotationReanchorService.ExtractParaIds`). Then match the offset/needle **within that one paragraph only**. Whole-doc scan eliminated; the fold/collapse passes still exist but operate on one known paragraph, so a stray match elsewhere is impossible.
   - **(b) no paraId, or paraId absent in current doc** (external Word edit regenerated ids) → fall through to today's fold→collapse whole-doc search (preserves legacy behavior; the FR-19 "do not guess wording" refusal is unchanged).
3. **Client** — populate `targetParaId` on the `DocxAnnotation`s it assembles for comments (and any anchored annotation) from the block's existing `paraId`. This is reading a value already in the editor state.
4. **Contract** — add `targetParaId` (+ offset fields) to the wire `DocxAnnotation` record in `ComposeEndpoints.cs` and the TS contract; camelCase per convention.

### Why this is the whole fix for the 422 class
The interior-location failures are *whole-document text-search misses*. Anchoring the paragraph by stable id removes the search from the equation for every annotation that carries an id — which, post-Phase-1, is every paragraph loaded from a projection. The residual text-search only runs for genuinely id-less legacy content, scoped to a paragraph when an id *is* present.

### Tests
- Writer: comment + insertion + deletion each resolve by `TargetParaId` at an interior location where a whole-doc `IndexOf` would mis-hit (duplicate text in an earlier paragraph) → lands in the correct paragraph.
- Writer: null `TargetParaId` → unchanged legacy whole-doc path (regression guard).
- Writer: `TargetParaId` present but absent in doc (simulated external-Word id regen) → falls through to text-search, does not throw prematurely.
- Client: comment annotations carry `targetParaId` from block state.

---

## 3. Gap 2 — Wire the concurrency protocol into the save sequence (Bug B)

### Root cause (Bug B, observed UAT)
`ReplaceFileContentAsUserAsync` sends an eTag precondition that goes stale when a create-on-save follow-up write advances the item's eTag between read and write → `InvalidOperationException: ... eTag mismatch`. Same family as the Office-lock / co-authoring vs. programmatic-OOXML-write (HTTP 423) question. The senior-dev caveat: "offsets go stale the moment the user types" → version-stamp + re-anchor.

### The fix (compose existing machinery; define the sequence)
1. **Version-stamp the LLM/save snapshot.** Carry the document version (SPE eTag or the load-time projection `SchemaVersion` + item eTag) with the edit batch. The save asserts it against the current item before writing.
2. **On stale version, re-anchor before applying — do not fail.** Re-download current bytes, run `AnnotationReanchorService.Reanchor(priorAnchors, currentParagraphs)`:
   - AUTO band → apply silently against the re-anchored paragraphs.
   - REVIEW / ORPHAN → surface to the user (already the service's contract: "NEVER silently dropped"), rather than 422/500.
3. **Sequence the eTag precondition** so the create-on-save follow-up write doesn't invalidate the precondition of the content write — order the create → capture fresh eTag → content write with the *fresh* precondition (or single conditional write). Define this explicitly in `SpeSyncOrchestrator` / the save endpoint.
4. **Office-lock (423) protocol.** Define behavior when the item is checked out / locked by Word co-authoring: detect 423, surface "open in Word — save from there or close the Word session," rather than a raw error. (This is the launch-surface boundary, not an editor change.)

### Tests
- Save with a version older than current → re-anchor path runs; AUTO matches apply; below-threshold surfaces (no exception).
- Create-on-save then content write → precondition uses the post-create eTag (no stale-eTag `InvalidOperationException`).
- 423 lock → mapped to a user-actionable ProblemDetails, not 500.

---

## 3.5. Gap 1.5 — Client-side AI-generation drift (bookmark/decoration on the in-flight window)

### Root cause (distinct from Gap 2's *save-time* drift)
A THIRD reviewer (2026-07-22) pinpointed an EARLIER drift than the save-time staleness Gap 2 covers: the window between *user highlights text → clicks Generate* and *the AI response returning seconds later*. During that window the user (or another edit) can shift positions, so an AI insertion anchored to an absolute/index position lands wrong or "can't find the insertion point." This drift is CLIENT-SIDE and, if unaddressed, produces a bad anchor that then propagates to the save.

### The fix (client, ProseMirror-native — resolve-on-return)
1. At prompt time, capture the selection as a stable marker — a ProseMirror `Decoration` (invisible) or `Selection.getBookmark()` — NOT an absolute position. Tie it to the AI request id.
2. Send the target paragraph's `paraId` to the AI as context so the returned delta already knows which OOXML node it patches (feeds Gap 1's `TargetParaId`).
3. On AI return, resolve the bookmark to its CURRENT valid position (ProseMirror rebases it through any intervening transactions) and insert there — regardless of what the user typed elsewhere meanwhile.

### Present ingredients / gap to close
We already have decoration-based marks (`QaHighlightExtension`, `CommentAnchorMark`) and persisted paraId+textPattern anchors (`AnchoredAnnotation` / `PriorAnchor`). NOT yet confirmed: a live ProseMirror bookmark on the specific *generate-in-flight* window. Closing that is the change — reuse the existing decoration infrastructure; do not add a store.

### Tests
- Highlight → dispatch AI request → user types in an earlier paragraph → AI returns → insertion lands at the (rebased) original selection, not the shifted absolute position.
- AI response carries the source `paraId`; the resulting `DocxAnnotation` populates `TargetParaId` (feeds Gap 1).

---

## 4. Sequencing & proof

1. **Gap 1 first** (kills the class the user hit repeatedly; smallest change; client value already present).
2. **Prove on the CIPO patent letter** — the doc that surfaced the interior-location 422 and the empty-paragraph drift (48 vs 39 paragraphs). Acceptance: AI edit at an interior location saves; comment at an interior location saves; untouched content byte-identical on round-trip.
3. **Gap 2 second** — concurrency protocol; prove with the create-on-save + stale-version sequence that produced the eTag mismatch.

## 5. Explicitly out of scope (anti-drift)
- WOPI / Office-for-web embedding as *the fix* (it's an optional launch convenience Spaarke already has via `SpeDocumentViewer`).
- Any commercial / per-seat / AGPL component (NFR-03).
- Rebuilding or replacing TipTap; changing the Compose feature set.
- "Full Word fidelity" — never a goal.
- Multi-format (pdf/xlsx/pptx) — a *later* capability track, not part of this fix.

## 6. ADR / governance notes
- BFF-touching (Gap 1 writer + Gap 2 orchestrator). Publish-size: additive fields only, zero new package — no delta expected (`DocumentFormat.OpenXml` already a dep). Verify ≤60 MB on the BFF task per §10.
- ADR-013 facade boundary preserved: `DocxAnnotationWriter` / `AnnotationReanchorService` stay pure (`byte[]`/strings in, no `IOpenAiClient`, no Graph type). Tier-1 NetArchTest already enforces.
- ADR-038: writer + orchestrator changes get MAINTAIN-class unit tests (paraId resolution, re-anchor bands, eTag sequencing).
