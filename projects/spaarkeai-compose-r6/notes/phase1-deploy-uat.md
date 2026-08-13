# Phase-1 Deploy + UAT Record — Task 014

> **Deployed**: 2026-08-06 (operator go: option A — r2-session deploys frozen during window)
> **Deployed SHA**: `d01007a38` (branch `work/spaarkeai-compose-r6`; includes origin/master merge `11fe9cfd8`, 0 behind master at deploy time)

## Pre-deploy gates

| Gate | Result |
|---|---|
| Deps 013 + 027 | ✅ both closed; suite 1024/1024 green on the merged baseline (one first-run flake in master-side `ComposeServiceCreateOnSaveTests.SaveAsync_WhenBackgroundProfileThrows…` — passes isolated + on re-run) |
| Master merge (obligation #1) | ✅ merge `11fe9cfd8` — keep-both conflicts in `.claude/CHANGELOG.md` + `projects/INDEX.md` only; zero code conflicts |
| /conflict-check | ✅ re-run 2026-08-06: one overlap — PR #743 (`spaarkeai-assistant-enhancements-r2`) on `ComposeWorkspace.tsx` (semantically compatible flush-on-unmount; textual conflict for second merger) + trivial `projects/INDEX.md`. Does not affect this deploy (deployed from branch). |
| CVE scan | ✅ clean — `dotnet list package --vulnerable --include-transitive`: "no vulnerable packages". Crypto.Xml HIGHs resolved by the master merge. |
| **Anti-clobber** | 🛑 **FAILED initially → resolved via operator option A.** Live env was NOT a strict superset: BFF deployed 5× on 2026-08-06 (latest 12:57Z) and `sprk_spaarkeai` at 11:15Z by the `assistant-enhancements-r2` session (PR #743: 24 unmerged BFF files, 18 SpaarkeAi files). Live-bundle probe: #743's distinctive "Set related record" string ABSENT → live code page was master-lineage. **Operator decision (2026-08-06): option A — freeze r2 deploys, deploy R6 now; r2 merges/rebases over R6 before it deploys again ("when it is ready to merge and deploy we'll coordinate").** Consequence: #743's unmerged BFF endpoints are no longer live on dev until that coordination. |

## Publish size (ADR-029 / NFR-01)

- **46.94 MB compressed incl. PDBs** (clean worktree, in-place = HEAD). Delta **+0.03 MB** vs the 46.91 MB task-baseline (from the 67-commit master merge). Ceiling 60 MB — ample headroom. Deploy script's zip: 48.31 MB (different compression settings; same content).

## Deploy execution (atomic window)

| Step | Result |
|---|---|
| Client pre-build | Shared libs built in dependency order (`Build-AllClientComponents.ps1 -Component SharedLibs`; Compose.Components dist fresh with R6 markers). Orchestrator's halt on `Spaarke.Communication.Components` type errors is benign — that lib (and Events/SmartTodo/DailyBriefing/LegalWorkspace) is source-aliased in SpaarkeAi's vite.config (`main: ./src/index.ts`), no dist needed. |
| SpaarkeAi bundle | `dist/spaarkeai.html` 5.30 MB; R6 markers verified in bundle: `imported-thread:` ×2 + comment-collision banner copy ×1. Fresh worktree → no stale Vite cache; cache-clear run anyway. |
| BFF deploy | `Deploy-BffApi.ps1` → **SHA-256 hash-verify: all 4 critical files match** (silent-file-lock guard); `/healthz` green. |
| `sprk_spaarkeai` deploy | `Deploy-SpaarkeAi.ps1` → updated web resource `5206a442-3451-f111-bec7-7ced8d1dc988` (5176 KB), published. Deployed **immediately after** the BFF — atomic window ~1 minute. |
| Route verification | `POST /api/compose/project` → **401** (route registered, auth-gated — not 404); `GET /api/documents/test/preview-url` → 401. |

## Ops notes (from 020-canonical-hub-design.md §20 — for dashboards)

1. **Chart the `TRANSITIONAL op-log save shape` Warning** (ComposeService.SaveAsync, ContentModel-null path). Decay to zero = the signal to delete the transitional op-log path + `ComposeShadowPatchEngine` + count-gate.
2. **Watch save latency on very large documents** — the post-save re-projection (`BuildContentModel` on persisted bytes) runs inside the save request.
3. Old-client-on-new-server (within any future window): separate-comments drop LOUDLY via `comments-ignored` — expected, bounded by the atomic window.

## UAT (manual, operator-driven, 2026-08-06 evening) — Step 5 RESULTS

Operator ran UAT on a REAL owner document — `Corteva -NDA- August 2022_Signed.docx` (a signed NDA,
harder than the Appligent fixture; original dropped at `notes/` untracked) — via the Compose upload
door: upload → save → NDA analysis → save → AI "draft compliant alternative" → manual edits → save.

| # | Check | Result |
|---|---|---|
| 1 | **NDA end-to-end** — edit → save → **no 422** → new SPE version → reopen shows edits | ✅ **PASS** — repeated saves all succeeded (incl. post-analysis + post-edit saves); redlines landed; SPE versions accumulated (7+ visible in Word Version History) |
| 2 | Imported-doc redlines in **real Word** (tracked ins/del, authors/dates) | ✅ **PASS** — Word shows proper tracked strikethrough/insert per save version (screenshots on file). Caveat: D2 char-mangling *within AI-suggested text* (upstream of save — see defects) |
| 3 | Clean save (no edits) byte-identity (FR-06a) | ✅ PASS (initial pre-edit save succeeded with no degradation warnings; byte-level identity not independently diffed in this UAT) |
| 4 | Comment round-trip | ⚠️ **NOT EXERCISED** — operator found no "Add Comment" toolbar affordance (D7). Round-trip itself is seam-proven (024/026); the missing UI entry point blocks live verification |
| 5 | Version history: open prior version after later exists → exact bytes (002's live gate) | ✅ **PASS at the storage layer** — Word Version History lists every save; operator opened the 11-min-ago version and saw the exact prior state. In-app (Compose/Documents) surface = Phase 5 (050-052), not built yet — expected gap |

**Loud-degradation check (design principle)**: the fidelity banner fired exactly as designed on this
real NDA — `indentation-dropped ×84`, `paragraph-style-flattened ×85`, `section-break-flattened ×6`,
`tab-flattened ×5`, `line-break` ×5, drawing/embedded ×5, `heading-direct-numbering-dropped ×4`.
Nothing failed silently. The VOLUME on a common real-world NDA is a priority signal for the
fidelity-widener backlog (notes §16): indentation + paragraph-style survival should move to the front.

## Defect register (triaged — none blocks Phase-1 sign-off; per gate constraint NONE hot-patched)

| ID | Finding | Root cause / evidence | Triage |
|---|---|---|---|
| **D1** | A new Documents row appears per save (operator saw 4; actually 6 total) | **PRE-EXISTING, NOT an R6 regression.** All 6 `sprk_document` records point to the SAME `sprk_graphitemid` (`01MJSXLZEANWG5S3VB4NC2KZ6LMRGZJCXG`) — one file, correct versioning; the duplication is Dataverse-record-layer only. Two records date from 2026-08-05 17:33/17:35Z — BEFORE this deploy → pre-existing. Mechanism: repeat create-on-save sessions hit Graph PUT-by-filename (returns the EXISTING driveItem = new version, same id) while the record write INSERTS instead of upserting on `sprk_graphitemid`; client create-on-save adoption (`ComposeWorkspace.tsx:1664/:1711`) + transientKey dedup only covers a single mount session | **NEW FIX TASK recommended.** ROOT CAUSE CONFIRMED (operator insight 2026-08-06 + code): the Save split-button — primary "Save Version", dropdown "Save New Document" — invites clicking the fork by mistake; `forkNew` DELIBERATELY skips transientKey dedup (`ComposeService.cs:1030-1033`) and the fork reuses the UNCHANGED filename (`ComposeWorkspace.tsx:1517` `displayName: state.documentRef.fileName`), so Graph PUT-by-path coalesces onto the EXISTING driveItem → new record, same file. Fix scope per operator: (a) UX — clearly distinct icons/labels for "Save Version" vs "Save New Document"; (b) fork must UNIQUIFY the filename (else it is not a fork at all — it silently versions the original); (c) optional upsert guard on sprk_graphitemid. Data hygiene: 5 duplicate records can be deleted (keep newest) |
| **D2** | Curly quotes render as digit `2` in AI-suggested draft text (`2Affiliate2`, `2control2`, `(2Pioneer2)`) | Mangling is visible IN THE EDITOR before save and round-trips faithfully to Word — the renderer/save path is exonerated; the corruption happens in the AI-suggestion insertion pipeline (or LLM echo of a mangled extraction). NOT `Services/Compose` | **NEW FIX TASK** in the suggestion pipeline |
| **D3** | "A suggested edit couldn't be placed automatically — wording differs slightly" | Almost certainly D2's consequence: the suggestion text (with `2`s) no longer matches the document text (with `“”`), so the placement matcher correctly falls back LOUDLY | Expected to resolve with D2; re-test after |
| **D4** | "Restore from Source" blanks the page and asks for a new upload | Not yet root-caused; likely mount-state reset regression on the transient/upload path | **NEW FIX TASK** (repro + fix) |
| **D5** | Page borders overrun / page-break flow oddities in Word output | Known consequence of `section-break-flattened ×6` (hard-tier accept-flatten, warned loudly). This doc has per-section page borders | Fidelity-widener backlog §16 — section-break survival, elevated by this evidence |
| **D6** | No saving-progress / saved indicator | UX gap | Backlog UX task (small) |
| **D7** | No "Add Comment" toolbar affordance | UI entry-point gap (comment machinery itself shipped + seam-proven) | Backlog UX task; unblocks UAT check #4 |
| **D8** *(2026-08-10, post-full-surface UAT)* | Compose tab → "Blank page" mounts a NON-editable page; "Open template" IS editable | **NOT an R6 regression** — the Blank/Template affordances shipped 2026-07-21 (`a9c4d3bc4`, pre-R6 UAT round-4 fixes). Both buttons call the SAME `mountBornInEditor` → `mountDraftHtml` → editor `initialHtml` seed branch (`ComposeWorkspace.tsx:3017`, `ComposeEditor.tsx:2252`); the ONLY difference is the seed HTML — blank = `'<p></p>'`, template = heading+paragraph. Defect is therefore empty-seed-specific (suspects: `setContent('<p></p>')` no-oping against the editor's identical creation content `ComposeEditor.tsx:2155` leaving a stale/unfocused surface, or empty-doc focus/click-target behavior). Not yet root-caused; needs live repro | **R7 batch** (with D1 save-fork UX, D6, D7) — small contained fix once reproduced |
| **D9** *(2026-08-13, post-full-surface UAT)* | "Open in Compose" (document ribbon → SpaarkeAi modal): the Assistant pane's transcript viewport does not fill its pane — content clips mid-row with dead space above the composer input. Same Assistant in the standard workspace surface fills correctly | **NOT an R6 defect** — R6 never touched the Assistant surface (`ThreePaneShell` / `ConversationPane` / `SprkChat`). Since compose-r1 task 092 the modal mounts the SAME `ThreePaneShell` as the full page, so the layout is not forked — the break is host-dependent: the Xrm-dialog iframe's shorter/late-settling height exposes a broken flex height chain somewhere between `App.tsx` `appRoot` (100vh) and SprkChat's transcript scroll viewport (`ConversationPane` → SprkChat slots). App-level chain reads correct (`layoutShell` flex:1/minHeight:0 → shell height:100%); suspect is inside the ConversationPane/SprkChat slot chain (an element missing `flex:1; minHeight:0` falls back to content-height, or a measured height captured before the dialog settles). Needs live-DOM inspection to name the exact element | **Assistant-surface owner** (spaarkeai-assistant-enhancements-r3 — it owns ConversationPane/SprkChat context) or the R7 UX batch; fix = restore the flex chain (`flex:1; minHeight:0; overflow:auto` on the transcript region), never a fixed/measured height |
| — | Create Summary Memo message about promoting to Analysis | **Working as designed** (instructive, loud, nothing lost) | No action |

**Corpus candidate**: the Corteva NDA is a perfect owner worst-offender for the OPEN corpus-manifest
row 4 (real signed NDA; triggers 180+ flatten warnings; exercises section breaks, page borders,
tabs, headers/footers, embedded objects). Recommend adding as an LFS fixture at task 060 — needs
operator confidentiality sign-off before committing a real signed agreement to the repo.

## Gate verdict

**Task 014 PASSES.** Phase-1 acceptance criteria met: /conflict-check recorded; co-deploy after
anti-clobber (option-A resolution); NDA UAT — save succeeds, no 422, edits land, new versions
produced; publish 46.94 MB ≤60 MB recorded; no hot-patching occurred (all defects triaged to
follow-up tasks instead — the negative criterion honored).

## Deviations

- Anti-clobber initial failure + option-A resolution recorded above (the task's escalation path, exercised as designed — no forced deploy).
- UAT used the operator's real Corteva NDA instead of (in addition to the spirit of) the Appligent fixture — a STRICTER test; the Appligent doc remains CI-proven via 013's regression suite.
- `comments-ignored` string not present in the client bundle copy-map grep — the wire-warning copy maps under a different key in `ComposeBannerStack`; verified functionally in 012's review, not a deploy issue.
