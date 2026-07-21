# spaarkeai-compose-r3 — UAT round-4 CONTEXT HANDOFF (2026-07-21)

> Deep round-4 UAT session. Many fixes shipped; the SAVE root cause was finally found + proven.
> This file is the self-contained recovery record. Branch: `work/spaarkeai-compose-r3`.

## TL;DR — the recurring save failure is SOLVED (root cause proven)

The recurring `"a tracked change could not be located" / "w14:paraId matches no paragraph in the
retained original"` save failures were **mammoth's default `ignoreEmptyParagraphs: true`** silently
dropping empty paragraphs when it flattens a real `.docx`. That shifts the editor's paragraph set out
of alignment with the server's document-order `w14:paraId` pre-parse → the client's **position-based**
paraId stamping drifts → edited paragraphs carry ids the retained original lacks → save aborts.

**Proven** on the real CIPO patent letter (`notes/sample-docs/PAT 109270W-1 - Letter to CIPO …docx`,
49 body `<w:p>` but 57 paraIds + 12 tabs): mammoth default emits **39** `<p>`; with
`ignoreEmptyParagraphs:false` it emits **48** (recovers the 9 dropped empties). Fix = one line in
`docxBridge.ts` `docxToTipTapHtml`. NOT a TipTap or Spaarke-unique problem — a known mammoth option.

## Commits this session (on branch, NOT yet pushed/merged past 8d2efdd3b)

| Commit | What |
|---|---|
| `8d2efdd3b` | (already on master) round-4a: items 1-8 + BFF whitespace matcher + merge of 9 master commits |
| `a9c4d3bc4`/`ce30f785a`/`5da887f06`/`6ed133c36` | round-4a pieces (in 8d2efdd3b) |
| `8b5e348e7` | round-4b: save fix v1 (position-based accept-state delta), noisy-diff cleanup, Track Changes default-on, remove Show Styles |
| `a0ba8938e` | Track Changes overlay no longer double-draws AI-suggestion blocks (lightbulb/explanations restored) |
| `3fd00afad` | **BFF** graceful degradation — save no longer aborts on one unmapped paraId (applies matched, reports unmatched via out param + logs) |
| `9cbea5d77` | **ROOT-CAUSE FIX** — mammoth `ignoreEmptyParagraphs:false` (stops the paraId drift) |

**HEAD = `9cbea5d77`.** origin/master = `8d2efdd3b`. So 4b + lightbulb + graceful-degradation + mammoth-fix
are committed on the branch but **not pushed/merged** (user gates merges). The BFF graceful-degradation +
mammoth fix need a **coordinated BFF+client deploy** (BFF change → sync-first process).

## Deploy state (spaarkedev1)
- **`sprk_spaarkeai`** (client): LIVE with round-4a + 4b + lightbulb fix. Does NOT yet have the mammoth
  `ignoreEmptyParagraphs` fix (committed `9cbea5d77`, needs rebuild+deploy).
- **BFF `spaarke-bff-dev`**: LIVE with round-4a (whitespace matcher). Does NOT have the graceful-degradation
  change (`3fd00afad`). Needs a coordinated deploy.

## What's fixed + verified this session
Client suite 403 green through 4b. BFF compose 467 + synthesizer 22 green (incl. graceful-degradation).
Shipped/live: save text-search 422 (4b), manual-edit redline+save, noisy diff→clean strike+insert,
Track Changes default-on, Show Styles removed, AI 💡/explanations restored.

## The complete save fix = TWO commits, needs ONE coordinated deploy
1. **`9cbea5d77` (client)** — mammoth `ignoreEmptyParagraphs:false` → paraIds stop drifting → edits map →
   saves succeed correctly. THE primary fix.
2. **`3fd00afad` (BFF)** — graceful degradation → any RESIDUAL mismatch (e.g. table-cell ordering mammoth
   still reflows) no longer aborts the whole save; applies what maps, logs the rest.

## NEXT STEPS (in order)
1. **Coordinated deploy** (user process): commit-all ✓ → `/push-to-github` → `/worktree-sync` (pull the ~N
   new master commits in, rebuild, re-verify) → deploy BFF (`Deploy-BffApi.ps1`, sync-first, hash-verify) +
   rebuild/deploy SpaarkeAi. Ships other projects' merged work too — coordinate.
2. **Re-UAT the CIPO doc** (hard-refresh): open it, make AI + manual + paste edits, Save → should succeed
   with correct tracked changes now (empty paragraphs preserved → paraIds align).
3. If a residual table-cell mismatch remains: the graceful degradation makes the save succeed; add a CLIENT
   WARNING surfacing the BFF's `unresolvedParaIds` (currently only server-logged) so partial saves aren't silent.
4. **Comments in a Word-style left pane** (user request, deferred — editor-layout change).
5. **push/merge to master** (user's call) → task **082** flagship gate → **090** wrap-up.

## Research finding (user challenged "1000s use TipTap — how do they do it?")
Correct challenge. The robust industry approach = model-as-source-of-truth with LOSSLESS docx import/export:
- **Tiptap Conversion** (`/import-docx` `/export-docx`) — TipTap Pro, **banned by NFR-03**.
- **SuperDoc** (ProseMirror-based, native docx fidelity + tracked changes) — **AGPLv3**, banned by NFR-03.
- **Apryse / CKEditor / TinyMCE** — commercial.
So Spaarke rolled its own (mammoth import + retained-original paraId patch) BECAUSE of NFR-03. That's a
reasonable compromise; the bug was simply mammoth's empty-paragraph default, now fixed. If fidelity issues
persist beyond this, the strategic question is whether NFR-03 should be amended to allow a licensed
high-fidelity converter (§6.5 ADR-tension candidate) — but the mammoth fix likely makes that unnecessary.

## Sample docs (user-provided repro fixtures)
`projects/spaarkeai-compose-r3/notes/sample-docs/` — CIPO letter is the key repro (tables+tabs+empties).
Clean docs (Engagement Letter, etc.) have 1:1 paraIds and always saved.
