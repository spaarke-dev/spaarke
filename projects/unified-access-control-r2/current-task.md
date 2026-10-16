# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint, never prepend; ≤10 KB. Standing rules: project `CLAUDE.md` (§2 Binding rules, §3 Owner directives incl. the ADR-028 A9 block, §5 Environment incl. the R3-holds-dev deploy rule, §6 Gotchas). Decisions: `notes/session27-owner-decisions-and-research.md` and `notes/decisions.md` (D-113 amended 2026-10-16). Task 185: `notes/185-invite-code-design.md`. Narrative: checkpoint commit messages.

> **Last Updated**: 2026-10-16 (checkpoint #36).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push to release, owner decisions of 2026-10-16. Do 099, 186 and 036. Fix #1620, #1363, #994 and #1010 (verify first). For #1576, investigate, scope and fix: here unless an active project overlaps it. Do not defer or hand it off. For every deferred or not-uac-r2 item, decide NOW, FUTURE (with the reason, in an issue and the defer list) or NEVER (close). Then finish wrap-up: 090, 058, the gate-only tasks, the owner checklist. Then write the **UAT checklist** for the owner, as scenarios by user type. |
| **Fix bar** | The most technically correct, best-practice, code-efficient fix. Never take the quick route. Add no complexity that brings no value. |
| **Order** | 099 → 186 (design note and the ceiling question to the owner first) → 036 (runs alone, Opus xhigh). The defect fixes run in parallel where they are parallel-safe. |

## Running (2026-10-16)
- #1620 fix: owner unshare through the owner-aware `RevokeAccessAsync`. Implementer, worktree `C:/wt1620`, branch `fix/uac-1620-unshare-owner`; result is a PR.
- #994 fix: playbook Share bit (262144). Implementer, `C:/wt994`, `fix/uac-994-playbook-share-bit`; result is a PR.
- #1576 investigation, read-only (`C:/wt1576r`): (a) resolvers and isdisabled, (b) own versus effective flag readers, (c) shared HttpClient OBO header race, (d) creator attribution. Returns a fix plan.
- Assessment of deferred and not-uac-r2 items, read-only. Returns NOW, FUTURE, NEVER, ALREADY-DONE or OTHER-PROJECT for each.

## Queued
- #1363: demo self-registration SPE writer marker keyed to the demo Dataverse; expiry leaves the marker. Task 171 code.
- #1010: Manage Access grants list for non-root users; a failed read shows "no grants". Verify against task 172 (team-owned owner column) before fixing.
- Task 099: the Expiration date picker plus fixes for expired rows and read errors. Run through task-execute.
- Task 186: design note, then the ceiling question to the owner (service-request privileges?), then the build.
- Task 036: the FR-20 flag-gated swap. Closes #1074 and #1567.
- Wrap-up 090: absorbs 058 and #1364's hand-off doc. Fix 183's index status (it shipped; only the K2 live gate is open). Close the 21 done-task issues after checking their evidence.

## Done this session (2026-10-15/16)
- **PR #2007 MERGED** as `96d5b16c2` (#2005, owner sign-off 2026-10-16): every bind consumes the invite hash; the invite mark is conditional (never `*`); "pending" requires an unconsumed hash. **Not on dev yet.** R3 merges master, folds its home bind onto #2007's store method, then the dev BFF deploys from R3's branch after the ancestry check. Then run #2007's server gate on dev.
- **PR #1983 MERGED** as `42aecf064`: GitHub registration is a MUST in /project-pipeline.
- R3 047 #2005 fixes: CLEAN at `4eacd5934`. Deployed to the dev BFF and SPA at `2c2fed38d` (12:59Z and 13:13Z). Heartbeat at 13:00:50Z healthy. #1830 noise is known.
- Ontology PR #2012 (task 126): UAC review found F1 (the anchor narrowing) and F3 (untested fault paths). **D-113 amended** (each grant applies at its own anchor BU): #1355 comment and notes/decisions.md. Ontology is fixing it; a scoped re-check follows. Their merge is held for UAC.
- Coordination project worktree: `C:/code_files/spaarke-wt-spaarke-project-coordination-r1`. The owner runs /design-to-spec there.

## Open owner items
- The live CIAM checks: §12 (185's code-gated bind; the dev SPA now carries 185) and §11; §13 is optional; §14 is the combined session (135 gate, 136, 037, 039).
- Task 186 ceiling (asked with the design note).
- Board #2 triage: the 4 views, or the grouped list.
- Task 049 (ontology): confirm the Ontology Administrator change to ontology directly.
- Older items: #1747, #1776, ontology #1876/#1877/#1879; restate the Model 1 `-Apply` rule after #1815; checklist items for 140, 153, 165 and 171.
