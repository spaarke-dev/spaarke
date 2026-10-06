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

---

# Independent audit (2026-10-06, read-only, after the conversion)

Verdict: the conversion is sound. Almost every live item reached CLAUDE.md, this file, or the existing notes and TASK-INDEX. Line numbers refer to the ARCHIVE.

## Missing: still live, only in the archive
1. **MEDIUM — the residual security gap for saves with no related record (065) is still open** (L477–497).
   - Control 3, "table-level create right on `sprk_document`", is ❓ unverified: under 080 the row is created app-only with a team owner, so nothing checks that the caller holds the create privilege.
   - Control 4, destination container authorization via UAC-r2's #1025, is still OPEN on GitHub.
   - TASK-INDEX row 065 reads as fully closed ("ownership → 080").
   - Action: add to current-task "Waiting on others" plus one line in the 065 note.
2. **LOW — "ci-cd-unit-test-remediation-r1 is CLOSED → r1 owns CI work"** (owner 2026-09-09; L1563). Add to CLAUDE.md Owner directives.
3. **LOW — "coordinate so 080 does not become a fifth caller-identity primitive"** (UAC-r2 task 082; L638). Add to CLAUDE.md Coordination.
4. **LOW — process gotchas:**
   - Check untracked files before a mid-task commit; never a blind `git add -A` (L806–808).
   - `jq` is absent; use `gh --jq` (L1187).
   - For overlapping line ranges on another branch, check which function they fall in, not just line numbers (L1022–1026).
   - Owner fact: "Access is BU-assigned, never org-wide" (L1499).

## Wrongly carried: stale or incorrect in the new files
1. **MEDIUM — CLAUDE.md "#1011 stays OPEN" is wrong.** GitHub #1011 was **auto-CLOSED 2026-09-30** when #960 merged: commit `34beafe78`'s message ("do **not close #1011**…") was read as a closing keyword. The defect is unrepaired but its tracker is closed. Reopen #1011 (owner's or UAC-r2's call) and correct the line.
2. **MEDIUM — current-task "Critical context" says the shared composer's "Link" option still uses the Graph link and the owner was "not yet asked".** Superseded: the owner decided 2026-10-05 (CLAUDE.md Decisions) and 098 shipped the record link. Only the share-link ROUTE retirement remains, and it's already under "Waiting on others".
3. **LOW–MEDIUM — the CLAUDE.md Decisions row (2026-10-05) says the share-link route "refuses SPE files with a typed code".** That change was REVERTED (098 note L22–29) and handed to UAC-r2 as a patch.
4. **LOW — current-task "TASK-INDEX row 097 may still read ⛔" and review item 14 are stale:** row 097 reads ✅ DONE 2026-10-06.
5. **LOW — CLAUDE.md L113 is stale:** "`office-addins/CLAUDE.md:39` names a script that does not exist"; line 39 now says there is no `build:prod`.
6. **Review items already answered:**
   - Item 2: the 32-privilege drift was removed 2026-10-01 (`082` note §4.1); only the hotmail guest finding remains, with UAC-r2.
   - Item 4: 084's latency is on the UAT list (`defer-issues.md` L43).
   - Item 9: W-5 is confirmed still open (`identity-obj-proxy` mapped in `jest.config.js:70`, missing from `package.json`); lint was fixed by 072.
