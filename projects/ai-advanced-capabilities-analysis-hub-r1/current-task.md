# Current Task State — ai-advanced-capabilities-analysis-hub-r1

> **Last Updated**: 2026-08-03 (by context-handoff)
> **Recovery**: Read the Quick Recovery table first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Status** | All hub deliverables + this session's UX/contract work **SHIPPED, MERGED to master, and DEPLOYED** (client + BFF, both hash-verified live on spaarkedev1). Working tree clean. Branch 34 behind / 0 ahead (only other projects' later merges). |
| **Next Action** | **Owner UAT** of the deployed features (list below). Then the only remaining project work is **071 env gate** (owner: ribbon buttons + retired-web-resource delete) → I run **072** (e2e) → **090** (wrap-up + `/test-diet`). |
| **Branch** | `work/ai-advanced-capabilities-analysis-hub-r1` · clean · everything merged |

### What was shipped THIS session (all on master, all deployed)
| Commit | What |
|---|---|
| `4a16906c0` | **Reopen fix** — cross-browser reopen mounts Compose + full history together (was either/or) |
| `1e1a6579b` | **A1** — wizard Agreement Type picker reads `sprk_agreementtype`, persists the lookup |
| `bd64a69d4` | **A3 core** — `subDomain` in the compose launch envelope (agreements-r1 orientation contract) |
| `7e022e7dd` | **A1 naming fix** — lookup attr is `sprk_agreementtype` (`_sprk_agreementtype_value`), NOT `sprk_agreementtypeid` (that's the table PK). Caught by agreements-r1 MCP review. |
| `22f86f781` | **Headless analysis-open** — grid row-click opens SpaarkeAi as a headless modal (`openSpaarkeAi` target:2, no OOB form chrome) via new `open_analysis_headless` intent |
| `2a1615304` | **Focused open** — a `mode==='existing'` open loads ONLY that analysis (Compose + its history), NOT the accumulated workspace tabs (skips tab-restore) |
| `2f8f11123` | **Q2 promote durable-FK fix** — `BindSessionToAnalysisAsync` now CREATES the `sprk_aichatsummary` anchor row WITH the FK when missing (was a silent no-op → orphaned Analysis) |

### Owner UAT checklist (hard-refresh Ctrl+Shift+R)
1. **Headless + focused open** — click an analysis row → opens a clean modal (no form chrome) showing ONLY that analysis (Compose + its conversation), not the full workspace tab set.
2. **Cross-browser reopen** — different browser / cleared storage → Compose + full history together.
3. **A1 picker** — Agreement Type dropdown shows `sprk_agreementtype` rows; created analysis persists the type.
4. **Q2 promote** — if a promote/classifier-start path is exercised, the bound session is visible via `GET /sessions/by-analysis` (durable FK).

### Deployment facts (both hash-verified from latest master)
- **Client** (`sprk_spaarkeai`): deployed via `pwsh scripts/Deploy-SpaarkeAi.ps1` (rebuild: `rm -rf dist/ node_modules/.vite/ .vite/ && npm run build` in `src/solutions/SpaarkeAi`; vite aliases shared libs to `/src` so no dist rebuild needed).
- **BFF** (`spaarke-bff-dev`): deployed via `pwsh scripts/Deploy-BffApi.ps1` — 48.25 MB, hash-verified, health OK. This deploy caught the App Service up to master (26+ merged-but-undeployed BFF commits from agreements-r1/compose-r5/email-r5/email-intelligence/messaging-r3 + my Q2).

### CRITICAL lesson learned (shared-env deploy)
**Do NOT deploy un-merged work to a shared env.** I deployed the focused-open fix un-merged; another project's master-build deploy then OVERWROTE it (silent drop). Fix: merge to master FIRST, then deploy from master. All my work is now merged → durable (any future master-build includes it). spaarkedev1 is shared and VERY actively deployed (esp. agreements-r1), so always: update-from-master → build-from-master → deploy, and hash-verify the live web resource/DLLs after.

### agreements-r1 coordination (platform ↔ review-machine split)
- This project = the **platform** (hub/wizard/spine/sessions/cross-surface comms). agreements-r1 = the **review machine** (classifier, general agreement-review Action, review-depth UX, memo/export). They are ACTIVELY landing (Waves 1–8, FR-16/FR-17 auto-run bridge, DEF-01, memo) and DEPLOYING to dev.
- I answered their Q1–Q5: [`notes/COORDINATION-hub-r1-ANSWERS-to-agreements-r1-Q1-Q5.md`]. Key: hub built A1+A3-core (don't rebuild); hub owns/fixed Q2; agreements-r1 loads the 7 remaining `sprk_agreementtype` seed rows + owns behavior cols; owner confirmed `sprk_key` alt-key exists.
- **Coexistence verified**: their `WorkspacePane.subdomain-envelope.test.tsx` (consumer of my A3) + `PromoteDurableFkVisibilityTests.cs` (regression for my Q2) both PASS against my code. My substrate + their machine cross-validate.
- Reverse coordination doc: [`notes/COORDINATION-hub-r1-TO-agreements-r1.md`]. Their inbound: [`notes/COORDINATION-agreements-r1-ANSWERS-and-QUESTIONS-to-hub-r1.md`].

### Schema (owner-created + seeded)
- `sprk_agreementtype` reference table (data-driven registry). Lookup on `sprk_analysis` = **`sprk_agreementtype`** attr (OData `_sprk_agreementtype_value`); `sprk_agreementtypeid` = table PK. Alt-key on `sprk_key` confirmed. Reused by new `sprk_agreement` entity. Rows: hub owns identity (`sprk_key`/`sprk_name`/`sprk_isfallback`/`sprk_isselectable`/`sprk_sortorder`); agreements-r1 owns behavior (`sprk_knowledgepackref`/`sprk_classificationcue`/`sprk_confidencethreshold`).

### Remaining project tasks (TASK-INDEX)
- **071** 🔄 deploy — code page ✅ + ribbon scripts ✅; **BLOCKED on owner env work**: add the 4 ribbon buttons (`AnalysisRecordLaunch` on sprk_matter/sprk_project = the "Analysis" launcher via `openSpaarkeAi`; `DocumentComposeLaunch`; `EntityFormLaunch`; `WorkspaceLaunch`) + Analysis subgrid/tab on Matter/Project forms + delete retired `sprk_analysisworkspace` web resources.
- **072** 🔲 e2e UI tests (add the cross-browser reopen assertion here; grep-clean retirement) — depends on 071.
- **090** 🔲 wrap-up + `/test-diet` — depends on 072.

### Memory written (~/.claude project memory)
- `handoff-requires-earned-context` (feedback) · `analysis-hub-vs-agreements-split` (project). MEMORY.md indexes both.

---

## Full State (Detailed)

Project mission: generalize the NDA vertical into a first-class **Analysis platform** (durable `sprk_analysis` spine + hub widget + creation wizard + session binding). ≈80% reuse. Tasks 001–070 shipped in prior sessions; 071 in progress (env-gated); 072/090 pending.

This session was post-shipping polish + coordination + a UX enhancement (headless/focused open) + a correctness fix (Q2). Nothing is blocked on me. Everything is merged + deployed. The ball is with the owner for UAT and the 071 environment work.
