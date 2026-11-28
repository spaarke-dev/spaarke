# Accelerator findings applied to Phase 0 (2026-11-28)

> **Why:** owner, 2026-11-28: "coordinate with Accelerator so have benefit of those findings". Source: a read-only sweep of `origin/work/spaarke-legal-ai-accelerator-r1` (`projects/spaarke-legal-ai-accelerator-r1/notes/`: `poc-e2e/results.md` = res, `poc-e2e/gap-log.md` = gap, `mvp-nda/results.md` = MR, `mvp-nda/run-log.md` = RL, `research/office-surfaces-2026-11/` = S02/SYN) and `origin/work/copilot-platform-coordination` `projects/copilot-platform-coordination/CLAUDE.md` (CO). Line numbers are as read on 2026-11-28. New findings arrive by message from the `acclerator-r1` session (standing exchange, project `CLAUDE.md` §4).
> **How to use:** every test kit links here. Part A is binding run preparation. Part B changes scoring or pass rules, so the owner decides it at task 022 (or 023/024) before the first run.

## Part A: run preparation (applies now)

| # | Rule | Applies to | Evidence |
|---|---|---|---|
| A1 | **Clean slate per tester before a run session:** empty the user's `Microsoft Copilot Chat Files/` and old `Cowork/Tasks/*/output` files; check Copilot custom instructions, Cowork Preferences and saved memories (empty for demo; for the owner, note what is set). Earlier outputs and a custom instruction were reread as grounding in accelerator runs | 022, 023, 024, 025 | MR:41, 44; res:463, 607-609, 679; gap:49 |
| A2 | **New chat or Cowork task per run**, and paste the NDA text or attach from the cloud, not from the device (a device attach makes a OneDrive copy that search later finds) | 022, 023, 025 | res:463, 679 |
| A3 | **Pin model and effort in Cowork** for every run and record both; check demo's credit limit and `/cost` (free) before the session. Same review: Auto 118, Sonnet 5.5 71, GPT 5.5 230 credits; the picker can reset effort | 022, 024, 025 | MR:12; RL:18-19; res:16 |
| A4 | **Approval cards:** our tools are all read-only (`readOnlyHint` set, checked 2026-11-28), so a Spaarke tool card is unexpected: click Cancel and tell the session, as before. A Cowork file card ("Create folder?", "Create file?") is expected for the patent spike: choose **Approve once**, never "Always allow", then Submit; log each card. A run that stalls "in progress" is usually a card waiting | 022, 025 | gap:38; res:310, 355; RL:20 |
| A5 | **Keep the word "Spaarke" out of run prompts** where the path matters: a prompt naming Spaarke was routed to the Spaarke connector even with a skill chosen. The BFF log, not the model, says which registration was called | 021, 022, 023 | res:623-626; CO:15-16 |
| A6 | **Record the Cowork step-trace line** (it names the skill or plugin that ran) for every Cowork run | 022, 024, 025 | RL:12; CO:18 |
| A7 | **Never score from what the model says it used** (it named a playbook version that does not exist). Use `profileVersion` from the tool result and the BFF telemetry line | 022, 023, 025 | res:574, 576; gap:53 |
| A8 | **Variant (c) in Cowork may fail on long reviews** (Microsoft: tool calls "should answer in under 30 seconds"; Cowork dropped a connection mid-run). Record a timeout as a result, not a run error; start with the shortest NDAs (our known limit already) | 022 | S02:113; RL:14, 38 |
| A9 | **After any re-upload, wait about 45 minutes** and run a marker prompt before a real run; open a fresh session per run (skill edits apply next session; a same-name re-upload is kept alongside the old one) | 020-026 | S02:134-136; MR:14, 49; CO:23 |
| A10 | **Patent spike output:** check the claims in the saved Word file (OneDrive or Word desktop), not Cowork's read-only preview; outputs land in `Cowork/Tasks/<task>/output/`. Cowork has no Office.js runner (our skill ships none) | 025 | res:545-547, 830-839; S02:187-190 |
| A11 | **G3 in Word:** record mode (Chat or Agent), host (desktop or web) and whether a document is open with each capture. Word web did not run plugin skills; agent mode needs an editable copy | 021 | res:468-471, 611-615, 730-737 |
| A12 | **G6:** record the Legal for Cowork toggle at the start of every run (the accelerator's notes once had it wrong). An `@`-mentioned declarative agent takes over the chat and Microsoft's Legal skills are not available in it, so "coexist in one Chat" is expected-no; test coexistence by toggles in Cowork. Use both named and unnamed prompts (routing is wording-sensitive) | 024 | res:13, 228, 302-303, 327, 508, 548 |
| A13 | **G2:** do not rely on cross-package skill hand-off (untested); test one package at a time; record whether the skill inside an agent package loads (G2-Q1 did not name its marker skill on 2026-11-28) | 023 | SYN:66; res:844-846 |

## Part B: scoring proposals (owner decides before the first run)

| # | Proposal | Task | Evidence |
|---|---|---|---|
| B1 | Ratings flip between identical runs (3 of 24 topics in two Word runs; 15, 18 and 20 changes from one baseline). Run each pilot decision twice in fresh chats and report the flip rate beside the FR-04 score, rather than one pass/fail | 022, 023 | res:160-162, 292; gap:43; MR:39 |
| B2 | A clause already at the playbook fallback: count it as **meets the fallback, no edit, on the judgment list** (accelerator PR #3263, open; their runs W4/W5/W6 handled it three ways). Do not score a variant wrong for following that rule | 022 | res:690-726 |
| B3 | Dry-run the synthetic corpus and key before the pilot, with a typo scan: 5 of 9 accelerator misses were key errors. Fix "primary topic only" and "silent clause" scoring rules in the corpus README first | 022 | gap:30, 40-41; res:341-349 |
| B4 | For variant (c), compare `documentChars` (already logged by the spike) with the source length to catch retyped or cut text; a content hash would need a spike change (owner's call) | 022 | res:897-898; cowork-access-to-spe-files:72 |

## Checked and not needed

- Spike tools prompting every time in Cowork (unannotated tools are treated as destructive): ours set `readOnlyHint = true` (`SpikeGetProfileTool.cs:82`, `SpikeReviewNdaTool.cs:72`, `SpikeRoutingStubTools.cs:49`).
- No accelerator source records request headers or caller attribution, so G3 (task 021) has no prior result to reuse.
