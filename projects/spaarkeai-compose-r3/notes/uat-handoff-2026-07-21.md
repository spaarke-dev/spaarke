# spaarkeai-compose-r3 — UAT handoff (2026-07-21, before compaction)

> Context handoff after 3 rounds of browser UAT on spaarkedev1. All fixes committed AND merged to
> master. Worktree fully synced (branch = origin/master = `6c60586ac`). Deploys done except the BFF
> Communication-engine work owned by another project (they deploy when ready — see "Open" below).

## Current deploy state (spaarkedev1)
- **`sprk_spaarkeai`** (client): LIVE with ALL compose fixes through UAT round 3 (verified via Dataverse
  content fetch — lightbulb `1f4a1`, new confidence header, U2 copy all present). `modifiedon 2026-07-21 04:29`.
- **BFF `spaarke-bff-dev`**: the round-2 build (**47.08 MB**, hash-verified) — has the C1 fold + C2 stamper.
  It does NOT have the 31+ Communication-engine commits now on master (another project deploys those).
  **Our compose fixes need no further BFF deploy** (round-3 was client-only).
- Git: branch `work/spaarkeai-compose-r3` = `origin/master` = main-repo master = `6c60586ac`. 0 uncommitted /
  unpushed / unmerged / behind.

## Fixes shipped across the 3 UAT rounds (all on master, all deployed)
- **Round 1** (`e31f99cf2`/`c1f57cb54`): P1 BubbleMenu insertBefore crash (relocated to last child); P2
  born-in-editor second-save baseline (retain driveId+versionId, adopt-only-when-null); P3 Word-open (downstream
  of P2); UX-1 Save in Word dropdown.
- **Round 2** (`ef36acfcc`): C1 save 422 typographic fold (`ComposeTextFold`, server); C2 accept→save
  ComposeRedlineException — the KEYSTONE: minted paraIds never written to baseline bytes; NEW
  `ComposeBaselineParaIdStamper` stamps them at save (text-verified/fill-gaps/count-gated/fail-open); C3 redlines
  →Word (Open-in-Word save-first + push includes redlines); U1 rationale card; U2 pane empty-states.
- **Round 3** (`50045ccc7`): **Fix #1** cross-paragraph save 422 — `redlineMarksToDocxAnnotations` now splits
  annotations PER BLOCK (a redline spanning >1 paragraph was one concatenated target the server, which searches
  one <w:p> at a time, could never find). **Fix #4** client "target not found" on upload — cross-extractor
  whitespace gap (AI authors target vs Document-Intelligence; client matches vs mammoth). `resolveTargetSpans`
  now runs a whitespace-COLLAPSED fallback (position-safe, only on precise-miss; paraphrase still refused).
  **U1 R2**: 💡 lightbulb at front of each redline; popover is a responsive card, "Suggested edit" label removed,
  confidence tag moved to a compact header w/ divider, full rationale below.

## Tests (all green)
- BFF compose 266/266; client compose 358/358 (parallel AND --runInBand). New regression tests:
  ComposeBaselineParaIdStamperTests (6), DocxAnnotationWriter fold (+1), ComposeWorkspace.saveBaseline (4),
  ComposeEditor.paneToggleCrash (3), buildBaselineParaIdMap (2), usePendingRedline whitespace fallback (+5),
  redlineMarksToDocxAnnotations per-block split (+3).

## OPEN / next-session
1. **Re-UAT round 3 on spaarkedev1** (hard-refresh first — Ctrl+Shift+R; the new bundle shows the 💡 + tighter
   card). Verify: fresh upload redlines PLACE (whitespace targets); Save incl. multi-sentence redline succeeds
   (no 422); Open-in-Word shows the redlines; rationale card reads well.
2. **Known caveat — intra-paragraph server whitespace**: Fix #1 solved the CROSS-paragraph 422. If a save still
   422s on a target that differs from the OOXML by whitespace WITHIN one paragraph, the server
   `DocxAnnotationWriter.LocateTarget`/`GetParagraphText` needs the same whitespace-tolerant match (with a
   normalized-index→original-w:t-offset map) the client got in Fix #4 — a BFF change (would need a BFF deploy).
   Not yet done (round-3 was client-only). Get the TraceId if it recurs.
3. **Genuine paraphrase** (AI reworded the target) still correctly REFUSES to place (FR-19 do-not-guess) — not a bug.
4. **Other project's BFF Communication-engine work** is on master but not deployed to dev; that team deploys it.
   If we ever deploy the BFF from here, sync-master-first is already satisfied (we're at master).
5. After UAT sign-off: task **082 flagship gate G-R3** (browser round-trip, `ui-test`) → **090 wrap-up**
   (code-review, adr-check, /test-diet, lessons-learned). See `tasks/082-flagship-gate-g-r3.poml`.

## Investigation agents used (root-causes captured in commit messages + this file)
- Round 2: BubbleMenu detached-node insertBefore repro; redline save/paraId/Word pipeline.
- Round 3: redline target-text anchor failures (3-views-of-the-document analysis: AI↔DocIntelligence,
  client↔mammoth, server↔OOXML; cross-paragraph vs whitespace).
