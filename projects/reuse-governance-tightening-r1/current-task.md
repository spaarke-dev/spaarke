# Current Task — Reuse Governance Tightening R1

> State, not history (root CLAUDE.md §5). Decisions: `design.md` §11 and `notes/decisions.md`; narrative: commit messages.

---

**Task ID**: none · **Status**: none · **Project**: Complete and archived (2026-11-17)

**Next action**: Owner decides on the final docs PR. Then the worktree can be removed with `scripts/worktree/Remove-WorktreeSafe.ps1` whenever the owner wants.

## State

- 19/19 tasks done. Merged to master: #2751 (items 1–3), #2705 (5c), #2706 (5e), #2843 (item 4), #2873 (5a). 5(f) C# LSP: not adopted. 5(b): deferred by design.
- SpaarkeAi DEV auto-deploy from the #2843 merge succeeded (2026-11-17).
- Issue #2682 closed; board Status Completed, Closed Date 2026-11-17, Tasks Completed 19, AI spend $184.30 / 1,461 calls. `.archived` marker written.
- **Not yet on master** (branch `work/reuse-governance-tightening-r1` only): task 043 record (`notes/csharp-lsp-trial.md`, design.md §10 item 6, `docs/procedures/testing-and-code-quality.md` "Agent Code-Navigation Tools"), the ISS-001 pattern fix (`.claude/patterns/ai/node-executor-authoring.md`; issue #2683 already closed), the wrap-up docs (README, plan, TASK-INDEX, test-diet report, lessons learned, `.archived`), and the `scripts/ai-cost` item-list limit fix. They need one docs PR, which the owner must approve first.

## Open follow-ups (filed, stay open after close)

- DEF-001..006 (#2801–#2806): the six remaining package actions.
- ISS-004 #2807, ISS-005 #2808, ISS-006 #2841, ISS-007 #2842.
- OQ-8 sweep tuning after four weekly runs (the alert counts touched files as "new"; a real fix needs a baseline, cut in design.md §10.7). Alert #2866 is open.
- `.claude/skills/devops-project-start/SKILL.md:57` uses `--limit 1000` for `gh project item-list` (board at 576 items).
- Three docs outside `.claude/` still say `ActionType` (listed under ISS-001 in `notes/defer-issues.md`).
