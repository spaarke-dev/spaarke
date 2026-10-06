# current-task.md conversion — items to verify (2026-10-06)

`current-task.md` was converted from a 483 KB stacked journal (27 sessions) to a current-state file, per the repo procedure change in `.claude/skills/context-handoff/SKILL.md` "State, not history".

- **Nothing was deleted.** The original is at `current-task-archive-2026-10-06.md` in this folder, byte-identical.
- **Current state** was taken from checkpoint #18 (commit `657dbc9de`).
- **Standing items** were moved to the project `CLAUDE.md` "Standing directives & gotchas".

The items below could not be classified confidently as current or superseded. Line numbers refer to the ARCHIVE. Resolve them when convenient: move into `CLAUDE.md` / notes, or mark as no longer applicable.

## Stale content inside the #18 block (corrected in the new file — confirm)
- L75 "25 tasks are 🔄 [wip]" — TASK-INDEX shows 15; the new file uses the measured list.
- L79–82 integration contents / merging — integ consumed by #1312; dropped.
- L84–102 "Dev live steps — Remaining" — all shown done in lines 17–39; dropped.
- L101, L168 test user `uac.child.user@demo.spaarke.com` — superseded by testuser1 (no known password). Does the Session-A CIAM sign-in still need the owner?
- L102, L153 "tell word-add-in-r1 once 161 is deployed" — 161 is ✅; no record the note was relayed.
- L48–63 PR #1314 described as open — merged as `891cfd9a3`. The local branch `fix/uac-r2-deploy-script-fixes` still exists (housekeeping).
- L111 "114's licence proxy" owed — possibly answered by round 67.
- L106 #1293 draft — status not re-verified.

## Older blocks
- L343, L376 continuation-script rule — conflicts with memory `workflow-agent-messaging` (`resumeFromRunId`). Kept in CLAUDE.md marked *(verify)*; reconcile in one place.
- L1689, L1889, L2484 "schema work is CODE + DOCS ONLY; live creation is an operator step" (2026-09-04, BINDING) — since 2026-10-02 the main session applies live dev schema with per-round owner approval. Superseded, or still binding outside approved batches?
- L1739, L1932, L2065 "Dataverse MCP is DOWN" — MCP tools are available now; probably fixed.
- L1748, L1933, L2074 "Python is a Microsoft Store stub" — superseded in practice (excluded).
- L1685, L1941 `gh` token lacks `read:project` — possibly fixed by the 2026-10-04 portfolio work.
- L1126–1143 word-add-in coordination (`OfficeService.cs` synthetic-job deletion; `AssociationType` ordinal 3 burned) — possibly moot after #1315.
- L2043–2046 `Spaarke Demo` default team holds System Administrator; root default team must stay role-free — current state unknown.
- L1147, L1180–1183 open owner items from the D-12 era (Power BI F-SKU pool, M365 Copilot agent, Redis Standard bar, Trivy) — still this project's to track?
- L1714 / L1908 "B3: record the `section-break-flattened` acceptance in spaarkeai-compose-r8 — not yet done".
- L1680 #974 Dataverse-side expiry guard — open/closed not re-checked.
- Merge style: L604/L612/L660/L677 say "merge commit, keep the branch", but #1312/#1314 were squash-merged. Which is current?
- Housekeeping: undeletable `wf_*` folders (L395, L623, L664); ~169 agent worktrees registered under `C:/code_files/spaarke/.claude/worktrees`; consumed worktrees `C:\wt4i`, `C:\wtD`, `C:\wtO`, `C:\wv*`/`C:\wvs*`.
