# spaarkeai-compose-r3 — UAT feedback round 2 (2026-07-20, spaarkedev1)

> Second UAT after the P1/P2/UX-1 fix redeploy + master sync. The P1 crash / P2 born-in-editor
> save / UX-1 are not re-reported here (assumed OK unless noted). This round surfaced deeper
> **flagship-critical** issues in the redline → paraId → Word round-trip (E1/E2/E3 keystone).

## CRITICAL (blocks flagship gate G-R3) — save + redline round-trip

### C1 — Save 422 "A tracked change could not be located in the document to save"
- **Repro:** uploaded a file → made edits → Save → `Save failed: A tracked change could not be located
  in the document to save. Reload the document and reapply your changes.` Console: `422`.
- **Hypothesis:** an annotation/edit anchor (a tracked change) could not be located in the retained-original
  baseline at save time — the server (or client pre-check) returns 422 because a redline/comment anchor
  drifted from the baseline text. Related to C2 (paraId/anchor mismatch).
- **Note:** after a refresh + re-add, redlines applied and a save SUCCEEDED (4a) — so it's state-dependent,
  not a hard-every-time failure. Points at an anchor/paraId that is valid on one mount path and not another.

### C2 — Save fails AFTER accepting an AI redline: ComposeRedlineException, paraId not in retained original
- **Repro:** refresh + re-add file → redlines added → save OK (4a). Then **Accept** the change → Save →
  `Save failed: ComposeRedlineException: One or more edited paragraphs have a w14:paraId that matches no
  paragraph in the retained original: 1E5EC15C. The synthesis was aborted — no paragraph was modified.
  TraceId=0HNN6O0SUPHMC:0000000E`
- **If you UNDO the accept, the file saves again (4c).**
- **Leading hypothesis:** accepting an AI redline produces a paragraph whose `w14:paraId` (1E5EC15C) is NOT
  present in the retained-original baseline — most likely the AI redline inserted a NEW paragraph (or the
  accept re-minted/split a paragraph), giving it a fresh client-minted paraId. `collectEditedParagraphs`
  then emits that paraId in the delta; `ComposeParagraphRedlineSynthesizer` requires EVERY edited paraId to
  match a baseline paragraph (`ComposeParaIdSpliceMap.Resolve` → `IsFullyMatched`) and ABORTS otherwise.
  This is the E2 anchoring keystone: the synthesizer has no "insert a NEW paragraph" path — a
  paraId-not-in-baseline must be treated as an inserted paragraph (tracked insertion), not a fatal error.
- **Files to trace:** client accept flow (usePendingRedline.accept → strips mark, leaves text) +
  `collectEditedParagraphs` (docxBridge) + server `ComposeParagraphRedlineSynthesizer.SynthesizeRedline` /
  `ComposeParaIdSpliceMap.Resolve`.

### C3 — Redlines do NOT round-trip to Word (Open in Web AND Desktop); comments DO
- **Repro:** Open in Word for Web → the **redlines do not show** (only the ORIGINAL text that was redlined
  in the TipTap editor); the **comment shows**. Same for Open in Word Desktop.
- **Leading hypothesis:** the pending redline marks are not being emitted as native OOXML `w:ins`/`w:del`
  track-changes into the saved .docx — while comments ARE (DocxAnnotationWriter works for comments). Either
  `getRedlineAnnotations` / `redlineMarksToDocxAnnotations` yields nothing/wrong for redlines, OR the save
  that Word opened had the redlines still PENDING and they weren't written, OR the accept→editedParagraphs
  path (which failed per C2) means the accepted redlines never landed. "Only the original text shows" =
  the baseline was written WITHOUT the redline markup.
- **Files to trace:** `redlineMarksToDocxAnnotations` (useComposeWordShuttle) → save `annotations` →
  server `DocxAnnotationWriter.Annotate` (ins/del vs comment handling) + the Open-in-Word handoff.

## UX (not gate-blocking, but flagship-quality)

### U1 — Redline rationale/confidence band is hidden; needs a prominent, proper popup
- The per-redline rationale ("This alternative improves clarity and precision by simplifying language…") +
  the **High confidence** band + Accept/Reject buttons render in a cramped inline strip that's easy to miss
  and gets clipped/hidden. Ask: a prominent **note icon** (or clear indicator) on the redline that opens a
  proper **hover/popup** containing the full rationale language + the confidence band + Accept/Reject.
- **Files:** the per-change popover / redline summary surface in ComposeEditor (DEF-12 popover, FR-13/FR-14).

### U2 — Floating edit (Styles "A") + comment FABs open an empty/unclear pane
- "I'm not clear what the floating edit icon or the floating comment icon are supposed to do — open a pane
  but nothing in it?" The P1 fix stopped the crash, but the Styles pane and Comments pane now open with no
  obvious content/purpose (empty state unclear). Needs: clearer empty-state copy / affordance, or a rethink
  of whether the FABs belong.
- **Files:** ComposeStylesPane.tsx (empty state), ComposeCommentThread.tsx (empty state), the FAB tooltips.

## Priority
C2 + C1 (save fails — hard blockers, data-loss risk) → C3 (redline→Word round-trip — core G-R3.4) →
U1 (rationale prominence) → U2 (pane empty-state clarity). C1–C3 likely touch the SERVER redline/annotation
pipeline (BFF change → worktree already synced to master, so a BFF deploy is clean). U1/U2 are client-only.

---

## RESOLUTION (2026-07-20 — all fixed; needs a BFF deploy + sprk_spaarkeai redeploy)

Deep root-cause via investigation agent, then §6.5-surfaced the C2 keystone approach (path B — finish the
step the design intended) and got explicit operator approval before touching the E1/E2 synthesizer path.

**C2 — accept→save `ComposeRedlineException` (paraId not in retained original) — FIXED (server + client).**
Root cause was NOT a new/split paragraph. `ParaIdPreParser` MINTS a `w14:paraId` for every id-less
paragraph on Load but returns it ONLY in the map (opens read-only) — its own remark flags "task 020/022
apply them physically" as a step that was **never wired**. So the editor/snapshot hold ids the baseline
bytes lack; editing/accepting on such a paragraph emits a paraId the synthesizer can't resolve → abort.
Uploaded docs are worse (no server pre-parse → all ids client-minted). **Fix:** NEW
`ComposeBaselineParaIdStamper` stamps the client's minted ids physically onto the baseline's id-less
paragraphs at save, BEFORE synthesis — text-verified (via the shared `ComposeTextFold`) + fill-gaps-only +
count-gated + fail-open, so it can never stamp the wrong paragraph. Client sends the load-time paraId map
(`buildBaselineParaIdMap` from the snapshot → `getBaselineParaIdMap()` handle → `triggerSave` both bodies).
Gated on an editedParagraphs delta (preserves FR-06a byte-identical clean saves). Tests: 6 stamper +
1 client map-builder.

**C1 — save 422 "a tracked change could not be located" — FIXED (server).**
`DocxAnnotationWriter.LocateTarget` matched annotation targets by EXACT ordinal search while the client had
already folded typographic chars (curly quotes / NBSP / dashes) — a single-char drift → TargetNotFound →
422 (state-dependent, matching the repro). **Fix:** new `ComposeTextFold` (server twin of the client's
`MATCH_FOLD`, byte-verified) folds BOTH sides before the search; the fold is 1:1 so the match offset stays
valid for run isolation. Test: a curly-apostrophe/em-dash target now anchors.

**C3 — redlines don't round-trip to Word (comments do) — FIXED (client).**
Redlines only reach SPE via a SAVE; Open-in-Web/Desktop opened the LAST-PERSISTED bytes without saving, so
pending redlines never showed. **Fix:** Open-in-Word now flushes a save first (gated on unsaved work OR
pending redlines; the id is stable because Word-open is only enabled once persisted) — with C1/C2 fixed
that redline-inclusive save now succeeds. Also fixed the secondary gap: `handlePushToWord` now includes
`getRedlineAnnotations()` (it previously pushed only comments).

**U1 — redline rationale hidden/cramped — FIXED (client).**
The per-change popover is now a proper CARD: a "Suggested edit" header with a lightbulb icon, the FULL
cited rationale (wraps + scrolls, no more single-line ellipsis), the confidence band, and a clear
Accept/Reject footer. Removed the internal ledger id (`…@t8`) that cluttered the rationale line.

**U2 — Styles/Comments FAB panes read as empty — FIXED (client).**
Both panes now explain their purpose in the empty state (Styles: what named styles are + how they apply;
Comments: select a passage → add → reply to thread) instead of a bare one-liner.

**Verify:** BFF `dotnet build` 0 errors; BFF compose tests 266/266; client tsc + jest 350/350 (parallel AND
--runInBand). **Deploy:** BFF (`Deploy-BffApi.ps1`, hash-verified — worktree is synced to master) + rebuild
`sprk_spaarkeai`. Then re-UAT: upload → edit → save; accept a redline → save (no ComposeRedlineException);
Open in Word Web/Desktop → redlines show as tracked changes; click a redline → readable rationale card.
