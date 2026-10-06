# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** this file holds CURRENT state only and is REWRITTEN at each checkpoint — never prepend a new block on top of old ones. Standing directives + environment gotchas live in the project `CLAUDE.md` ("Standing directives & gotchas"). Session history lives in git (checkpoint commit messages) and the verbatim archive `notes/handoff-history/current-task-archive-2026-10-06.md` (do NOT load it on recovery; grep it only if you need a specific past detail). Why: see `.claude/skills/context-handoff/SKILL.md` "State, not history" and `notes/handoff-history/2026-10-06-conversion-review.md`.

> **Last Updated**: 2026-10-06 (by context-handoff, before /compact + new session). Rounds 5-7 + 097 all merged; round 7 #1323 merged `002ac9b39` (add-in site deploying)

## ⚡ Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **UAT rounds 5-7 + task 097 — all merged.** Merged: #1301 (098/099/100, BFF deployed), #1315 (101-103), #1316 (Word Email tab ON + Outlook footer shows package version), #1321 (097 archive option A + copies as protected as the source). Dev BFF deployed from master `11a8ba203` (36.14 MB — master shrank; 4/4 SHA-256; routes 401). Add-in site live from `b403c7713` (Email tab verified `canEmailFromPane:!0` in the deployed bundle). |
| **Next Action** | 1) Confirm the add-in site deploy for master `002ac9b39` (PR #1323, round 7: 104 expand-to-window, 105 Copied/Opened + resizable Profile Summary) succeeded: `gh run list --workflow deploy-office-addins.yml --limit 1`. 2) Owner live checks: rounds 5-7, Word Email tab (sends own doc; foreign doc → `sdap.access.deny.communication.send`), archive (Spaarke email page, archive on, secure + >4 MB attachment → separate files; secure copy in the secure container). 3) Outlook desktop pane does not open — waiting on owner: classic vs new Outlook, version/build, which buttons show (Quick Save present?), what happens on click; cache-clear steps already given (Wef + HubAppFileCache; `olk.exe --devtools`). |
| **Waiting on others** | UAC-r2: retire the unused `POST /api/documents/{id}/share-link` + its tests (`notes/098-share-link-route-refusal.patch`); settle the cross-secure archive copy (keeps `sprk_relatedcommunication`) before switching on `DocumentPointer__StrictDerivedContainer` (097 note §11.4). Indexing owner: Find's "Matter: Matter" / "Unknown" are server fallbacks (`VisualizationService.cs:836/:1258`) — pane masks them. |
| **Later / next re-upload** | Remove the 404 `CommandRuntime.code.script` (`outlook/manifest.json:84`, `word/manifest.json:78`) at the next package version bump. 098's composer record-link reaches users only when the Email page / Console / upload wizard / communication PCFs are rebuilt + deployed. 090 wrap-up (`/test-diet`) after UAT ends. |
| **Out of scope here** | Owner 2026-10-06: an Outlook "submit Service Request" add-in for license-free workforce users is a SEPARATE project, aligned with the external-access SPA; nothing to record in this project. |
| **Main checkout** | `C:\code_files\spaarke` — check its branch first; fast-forward only when it is on master and clean; otherwise `git branch -f master origin/master` (memory). |
| **Tree** | Branch `work/spaarkeai-word-add-in-r1` clean and pushed (merged with master). Temp worktrees removed. Branch `work/word-addin-097-archive-optionA` is merged (#1321) — may be left as is (never delete branches as part of merge). |

## State of every open item

| Item | State |
|---|---|
| 083, 088–096 | ✅ merged and deployed |
| 097 | ✅ #1316 (Email tab on) + #1321 (archive option A, protected copies); BFF `11a8ba203` deployed; live checks open |
| 098–100 (round 5) | ✅ #1301, deployed; 098 route refusal reverted (UAC-r2 tests) — record link shipped |
| 101–103 (round 6) | ✅ #1315, add-in site deployed |
| 104–105 (round 7) | ✅ #1323 merged `002ac9b39`; add-in site auto-deploy |
| 042 | 🔄 UAT continues (notes `042-uat-round5/6/7-*.md`); Outlook desktop issue open (owner info needed) |
| 090 | 🔲 wrap-up with `/test-diet` after 042 |
| Publish size | 097: +4,188 B vs master merge base (Compress-Archive Optimal, 192=192 files); 100: +15.6 KB |

## Critical context
- Graph refuses sharing links on every SPE file. Owner decided 2026-10-05: document links open the Spaarke record; 098 shipped that in the shared composer. Only the now-unused share-link ROUTE remains (retirement handed to UAC-r2 — "Waiting on others").
- Local add-in build needs the CI env values from `.github/workflows/deploy-office-addins.yml` (see CLAUDE.md "Standing directives & gotchas").
