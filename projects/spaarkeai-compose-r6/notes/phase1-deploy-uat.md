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

## UAT (manual, operator-driven) — Step 5

| # | Check | Result |
|---|---|---|
| 1 | **NDA end-to-end**: open `AppligentNDA_Signed.docx` in Compose → edit → save → **no 422** → new SPE version → reopen shows the edit | ⏳ pending |
| 2 | Imported-doc redlines open correctly in **real Word** (tracked ins/del with authors/dates) | ⏳ pending |
| 3 | Clean save (open → save, no edits) keeps **byte-identity** (FR-06a) | ⏳ pending |
| 4 | Comment round-trip: session/advisory comments survive save → reopen; Word shows them anchored | ⏳ pending |
| 5 | Version history: open v3 after v4 exists → exact bytes (002's live human gate) | ⏳ pending |

## Deviations

- Anti-clobber initial failure + option-A resolution recorded above (the task's escalation path, exercised as designed — no forced deploy).
- `comments-ignored` string not present in the client bundle copy-map grep — the wire-warning copy maps under a different key in `ComposeBannerStack`; verified functionally in 012's review, not a deploy issue.
