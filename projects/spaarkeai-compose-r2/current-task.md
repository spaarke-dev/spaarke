# Current Task State — spaarkeai-compose-r2

> **Last Updated**: 2026-07-15 (by context-handoff, pre-compact)
> **Recovery**: Read "Quick Recovery" first. Worktree `c:\code_files\spaarke-wt-spaarkeai-compose-r2`, branch `work/spaarkeai-compose-r2`.

---

## Quick Recovery (READ THIS FIRST)

> **DONE (2026-07-15)**: All 6 deferral issues addressed. Task **063 COMPLETE** (FR-30 durable-memory capability) — **PR #645 merged to master**. Built `IComposeMemoryCapture` PublicContracts facade (#615 pattern) → shared `IMemoryItemStore`; `ComposeService.SaveAsync` distils session defined-term canon into Record-scope MemoryItems keyed to the promoted `sprk_document`, best-effort. adr-check COMPLIANT; code-review MEDIUM fixed; build 0-err; 37 tests green. AC3 CAPTURE→RECALL ✅◐ E2E-pending (Cosmos+HTTP = dev/UAT). Untrusted-origin GATE deferred → #629 (narrowed to the gate only). **Session deferral sweep**: #601/#604/#605 closed (stale-fixed on master); #602 closed (dev webhook secrets provisioned + verified; prod→runbook); #615 closed (OBO facade canonicalized, PR #644); #629 capability shipped, gate remains for the memory-governance project. Nothing else active.



| Field | Value |
|-------|-------|
| **Project** | spaarkeai-compose-r2 (AI-native Compose drafting workspace) |
| **Status** | ✅ **COMPLETE — MERGED TO MASTER** (`origin/master` = `e7f784131`) |
| **What merged** | Rounds 6–9 UAT fixes + full `/code-review` remediation + `/test-diet` (clean) + `origin/master` integration |
| **Deployed** | All fixes deployed to spaarkedev1 through the session (BFF `spaarke-bff-dev`, client web resource `sprk_spaarkeai`) |
| **Next Action** | (1) **Owner**: sync the MAIN repo's local master (see below — BLOCKED, not forced). (2) Pick up any parked follow-up. |

### ⚠️ ONE pending item — main-repo local master sync (owner action)
`C:/code_files/spaarke` (main repo, on `master`) is **144 commits behind** origin/master (PRE-EXISTING staleness from many prior merges, NOT this project). The ff-sync **safely aborted** — 13 untracked entries there would be overwritten, nearly all the owner's OWN local files: `projects/spaarke-email-intelligence-module/`, `projects/spaarke-legal-front-door-r1/`, `projects/spaarkeai-assistant-enhancements-r1/`, `projects/visual-host-version-update/`, `docs/notes/`, `exports/`, `projects/.claude/`, `.claude/worktrees/`, untracked `.husky/*`. **Do NOT force** (would clobber local files). Owner reviews/moves those paths in `C:/code_files/spaarke`, then `git pull origin master`. Offer to walk through it; touch nothing unprompted.

### Environment gotchas hit this session (may recur post-compact)
- **Root `node_modules` was missing** → the husky pre-commit hook fails with `'prettier' is not recognized`. Fix: `npm install --no-audit --no-fund --legacy-peer-deps` at repo ROOT (adds prettier/husky/lint-staged). Do NOT `--no-verify`.
- **`npm install` in a sub-lib can add a spurious `"spaarke": "file:../../../.."` self-dep** to that lib's package.json — revert it (`git checkout -- <pkg>/package.json <pkg>/package-lock.json`) before committing; it's an install artifact, not real work.
- **Master's `@spaarke/ui-components` is now 2.3.0** and adds a `@spaarke/auth` file: dep — a stale worktree install fails UI.Components tsc on `useWizardPageBootstrap.ts`; fix with `npm --prefix src/client/shared/Spaarke.UI.Components install --legacy-peer-deps` then rebuild.

---

## Parked follow-ups (owner's call to pick up — all captured in project folders)
1. **compose-r3 `design.md`** — OOXML fidelity project. Findings captured in `projects/spaarkeai-compose-r3/ooxml-fidelity-findings.md` (E1 retained-original delta = keystone + the everything-tracked-vs-diff design fork; E2 paraId anchoring; E3 confidence/offsets). Seed README reconciled. **Next: write design.md** (spine = E1+E2).
2. **AI Execution Trace / observability** — seed at `projects/spaarke-ai-execution-trace-r1/README.md`. AWAITING owner: placement (dedicated vs fold into pane-UI), best-practices scope confirm, timing → then `/design-to-spec`. Grounded: trace surface exists (6 events live); upload/classify captured-but-not-SSE'd; model/token/cost NOT emitted (new work).
3. **sdap-file-duplication-detector-r1** — capture done (`projects/sdap-file-duplication-detector-r1/analysis.md`). Two-tier detection (quickXorHash exact + existing Find-Similar vector KNN near-dup). AWAITING: create worktree off master + `/design-to-spec` + spikes (quickXorHash timing; Tier-2 robustness).
4. **Assistant/Workspace pane-UI redesign** — separate project AFTER compose-r2 merge (now unblocked); freeze the Compose output seam as the contract.
5. Optional: delete merged remote branch `work/spaarkeai-compose-r2`.

---

## What was completed (this close-out session)
- **Round-9 active-document seam**: summarize scopes to the active tab (`SessionDispatchOrchestrator.ResolveTargetFiles` honors `ActiveDocument`); Browse-open file visible to Assistant (`ConversationPane` lazy session); tab-switch re-focuses the Assistant (`ComposeEndpoints.RegisterActiveDocument` now honors `visible:false` withdraw); manual-open ingest ceremony parity (loaded + classified messages).
- **`/code-review`** (3 subsystem agents) → no Critical. Remediation `42e1c0656`: **W1** Save/Accept/Insert bridge conduits now active-tab-scoped (multi-tab wrong-tab routing); **W2** profile-on-save is fire-and-forget (detached DI scope + captured OBO token); dead preview-modal removed; index-stamp/const/comment fixes; concurrent-upload race guard; toolbar Explain→Email gap removed.
- **`/test-diet`** `4d69c73ff` — 194 methods, 0 scaffolding, clean.
- **Master integration** `e7f784131` — re-gate green (BFF 0-err/8161 · compose 189 · SpaarkeAi 426 · UI.Components build · SpaarkeAi prod build). Fast-forward merged to origin/master.

## Commit landmarks (on merged master)
`e7f784131` merge origin/master · `4d69c73ff` test-diet · `42e1c0656` code-review remediation · `324f87110` tab-switch withdraw · `82de66f69` ceremony parity · `c08b0a6aa` active-doc seam · `a55834bbf` redline accumulation · `b0d845b5c` round-8 (#1/#2/#3).
