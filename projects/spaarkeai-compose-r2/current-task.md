# Current Task State — spaarkeai-compose-r2

> **Last Updated**: 2026-07-14 (by context-handoff, pre-compact)
> **Recovery**: Read "Quick Recovery" first. Worktree `c:\code_files\spaarke-wt-spaarkeai-compose-r2`, branch `work/spaarkeai-compose-r2`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Round** | Round-7 = Compose-widget refinement (9 UAT items) + 2 follow-up bugs |
| **Status** | in-progress |
| **Deployed** | spaarkedev1: round-6 + round-7 Waves A/B/C(#6/#7) **+ bug A + bug B (2026-07-14)**. BFF `spaarke-bff-dev` (SHA-256 4/4 verified, health OK, 46.62 MB). Client web resource `sprk_spaarkeai` (5206a442…, published). |
| **Master merge** | HELD (branch 66 ahead / 34 behind origin/master; ~47 unpushed) |
| **Next Action** | **AWAITING OWNER RE-UAT (round-8)** — #1 multiple Compose tabs open simultaneously; #2 no 429 during drafting; #3 Draft-alternative targets the CURRENT selection. PLUS decide: supersession keeps ONE pending draft-alternative (drafting B discards kept A) — accumulate independent redlines? (#8 dedup spun out to projects/sdap-file-duplication-detector-r1.) |

### Round-8 UAT fixes DEPLOYED (2026-07-14), tree clean
- **#1** multi-instance Compose tabs — `b0d845b5c` (WorkspacePane instance-key reuse + per-tab keep-alive + tab-scoped active-doc). Was a regression from Bug B unify singleton.
- **#2** compose 429 — `b0d845b5c` (5 endpoints re-bucketed off 5/min ai-upload: reads→ai-context 60/min, SPE-writes→ai-persist 20/min). BFF deployed, SHA-256 4/4 verified.
- **#3** draft-alternative stale selection — `b0d845b5c` (snapshot+remap intended selection before supersession strip) + **accumulation** `a55834bbf` (range-scope supersession to the drafted section: keep A when drafting a DIFFERENT B; only re-draft of same/overlapping section supersedes). Owner-confirmed accumulate. 32 redline / 184 compose tests.
- Gates: BFF unit 8230/0 · compose jest 183 · SpaarkeAi jest 419. Client `sprk_spaarkeai` published.
- Bug A (draft-alt 404) confirmed FIXED by owner UAT step 7.

### Both bug fixes COMMITTED + DEPLOYED (2026-07-14), tree clean
- **Bug A** = `bd3d1eb90` (draft-alternative /dispatch 404 — RegisterActiveDocument creates resolvable doc session; BFF).
- **Bug B** = `e14b9dc32` (unify all Compose entries on Direct `'compose'` widget — fixes Email-tab-loses-file; SpaarkeAi client).

### Critical Context
Deploy — BFF: `pwsh scripts/Deploy-BffApi.ps1` (hash-verify). Client: `rm -rf src/solutions/SpaarkeAi/dist src/solutions/SpaarkeAi/node_modules/.vite .vite && npm --prefix src/solutions/SpaarkeAi run build && pwsh scripts/Deploy-SpaarkeAi.ps1`. **App Insights for runtime diagnosis** (use it FIRST — it works): appId `6a76b012-46d9-412f-b4ab-4905658a9559` (spe-insights-dev-67e2xz), `az monitor app-insights query --app <id> --analytics-query "..."`. **Playbook engine is being decommissioned** → run Actions via `IActionResolver`→`IActionRunner` (ADR-043), mirror `AnalysisEndpoints.ExecuteDocumentProfilePipelineAsync`. Bug-A/B fixes both carry E2E-DoD (bug A has a real through-the-wire seam test).

---

## Round-7 item status (9 items)
| # | Item | Status |
|---|---|---|
| 1 | Second file unloads on tab switch | Deployed (keep-mounted-hidden) — exposed **Bug B** (keep-alive only covered Direct widget, not layout Compose tab) → unify fix building |
| 2/3/4 | Redlines→Word tracked-changes + accept-persist + comments | Deployed (Save routes through server `DocxAnnotationWriter`; w:ins/w:del/w:comment) |
| 5 | One-row toolbar (Body/Paragraph/Font/Word dropdowns + right-aligned Save/undo/redo) | Deployed |
| 6 | Dynamic active-doc (active tab drives Assistant; toggle+gear removed) | Deployed |
| 7 | Auto-load Assistant uploads into Compose | Deployed |
| 8 | File already in SharePoint → notify/open latest | **PENDING owner shape**: content-hash vs filename+matter; notify+"open latest" (my lean) vs auto-open |
| 9 | Selection-bubble restyle (full-width light-grey elevated, icons-only, vertical overflow, scroll affordance) | Deployed |

### Follow-up bugs (this UAT)
- **Bug A** (draft-alternative → /dispatch 404): FIXED `bd3d1eb90`. Root: client-minted document session never created server-side; `RegisterActiveDocument` now creates it idempotently (preserves Outputs). Not yet deployed.
- **Bug B** (Email-tab-open loses Compose file): unify fix building — route all Compose entries through Direct `'compose'` widget.
- Compare-to-playbook "no playbook available" = **expected** (no playbook configured), confirmed to owner.

## Round-7 commit chain
`41f51a290` Wave A (#1 + #2/#3/#4) · `df2393d5c` Wave B (#5,#9) · `873a0f34f` #6/#7 · `bd3d1eb90` Bug A.

## After round-7 (to lift the HELD master merge)
1. Clean round-7 re-UAT (owner) of bug A + bug B + #8.
2. Sync branch with `origin/master` (34 behind) + re-gate.
3. Reconcile the **golden-utterance eval data** (core's fixture — our reseed changed routing; stale but not failing tests). Coordinate with redesign-r2 (paused).
4. Flag pre-existing branch debt (UI.Components-16 tests, ArchTest-2 — fail at clean HEAD, not ours).
5. Push + open PR → review → merge to master (HELD).
