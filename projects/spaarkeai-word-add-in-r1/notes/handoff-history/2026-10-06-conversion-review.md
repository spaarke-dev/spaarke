# current-task.md conversion — items to verify (2026-10-06)

`current-task.md` was converted from a 150 KB stacked journal to a current-state file, per the repo procedure change in `.claude/skills/context-handoff/SKILL.md` "State, not history".

- **Nothing was deleted.** The original is at `current-task-archive-2026-10-06.md` in this folder, byte-identical (it includes the pre-/compact checkpoint `2318afbb2`).
- **Current state** was taken from that checkpoint's Quick Recovery block.
- **Standing items** were moved to the project `CLAUDE.md` "Standing directives & gotchas".

The items below could not be classified confidently. Line numbers refer to the ARCHIVE (approximate). Resolve them when convenient.

1. ~L23: "run `code-review` + `adr-check` on the round-4 diff before the PR" — #1292 has merged; no evidence it was done.
2. ~L196–197: the 32 extra privileges on Secure Record Owner (recommendation: remove) and the hotmail `#EXT#` guest NFR-05 finding — no recorded resolution (see `notes/082-secure-owner-role.md` §4).
3. ~L241 (080 backfill `-Apply` + Test User 1 live checks) and ~L273 (078 observed install of the `-TEST` zip) — owner-side, no completion recorded. "Package 1.1.1 — no re-upload" suggests 078 was installed.
4. ~L157: 084 latency on the real BFF (≈0.71 s p50 modelled; trigger 1 s) — still open for the owner?
5. Can a customer span more than one BU, and where does the Secure Project BU sit in a dedicated environment? Open on 2026-09-28; no later answer found.
6. Whether drift-checker fix `233ff9341` has reached master (CLAUDE.md assumes not).
7. Tier 2 30-min cap "repo finding worth filing"; idempotency filter no-header no-op (Tier 2 owner) — filed or unowned?
8. ISS-002 shadow-window latch — needs a cutover-owner decision; status unknown.
9. W-5 `identity-obj-proxy`, W-2, W-7 (`npm ci` ban), F-6 — partly fixed by tasks 071/072; the rest unknown.
10. ~L285 "Production runs XML for BOTH add-ins" — the unified package 1.1.1 may now be installed instead; the module CLAUDE.md still says XML.
11. "Expect ~45 MB" publish (~L1341/L1364) vs the measured 36.13 MB on master — CLAUDE.md keeps only "< 30 MB = incomplete".
12. ~L1445 "Never `--no-verify`" conflicts with the global memory exception for master-merge commits.
13. Project CLAUDE.md contradictions: ~line 118 "Deploy is CI-only — never run the workflow as an agent" vs line ~194 / archive ~L1566 (owner authorized agent-triggered `deploy-office-addins.yml`) and master pushes now auto-deploy. Line ~116 also stale.
14. Other stale records: CLAUDE.md "Project Status" block (lines 10–13: "28 of 47 tasks ✅ as of 2026-09-15"); TASK-INDEX row 097 still ⛔ blocked; rows 011/077/080 ⚠️ and 057/061/081 ➡️ — final?
