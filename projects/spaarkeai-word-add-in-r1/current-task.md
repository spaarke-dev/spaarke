# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas". History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-11-26 ~15:30Z. 139 DONE. 140 merged and live everywhere (waiting on the owner's live check). Task 141 written, implementer next. This project runs in the **Claude Desktop session `word-add-in-r1`**.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task 139** | **MERGED** #3204 → master **03213a90fd** (owner go "Yes, merge when green"). Add-in deploy run 45750570047 success; the live bundle carries the new strings (checked by fetching). Review fixes f738a841e8; CI-only test fixes 57ad4d8a1a (`AuthService.naaRefusedToken` msal resolve; a `BrowserMsalStrategy.dataverseUser` exp clock flake); gate list now 149/149 suites. Owner live check in Outlook on the web PASS (2026-11-26). **CLOSED** (POML + TASK-INDEX). |
| **Task 140 = ISS-043 (#3194)** | **MERGED** #3214 → master **152397ab11** (05:35Z). Dev BFF: LIVE in 6a2a569ac9 (deployed 12:58Z by another session on the owner's go there; uac-r2 gate PASS 13:28Z; verified ancestor). Code pages: Console auto-deployed at 152397ab11 (deploy-spaarke-ai run 45762655905); `sprk_communicationreconciliation` on dev byte-identical to a master build (already deployed 13:02); `sprk_emailpage` deployed by this session 15:20:43 from 152397ab11 (byte-identical verified). Server rule: confirm only the SOLE regarding, pair type required (`RequirePairType`), else `FiledUnconfirmed`. **WAITING: the owner's live check** (save the ISS-043 email to MAT-000113 from Outlook → Resolved, out of Needs Review; the panel shows the matter). Then close 140 (POML + TASK-INDEX), update ISS-043 notes + #3194. Gap noticed: `deploy-spaarke-ai.yml` paths omit `src/client/shared/Spaarke.Communication.Components/**` (not yet filed or fixed). |
| **Next Action** | **Task 141** (owner Outlook feedback 2026-11-26: saved-email view redesign + attachments-only re-save saved nothing). POML `tasks/141-saved-email-view-and-unsaved-attachments.poml` + TASK-INDEX row written. Diagnosis: task 138's add path needs a Completed job under the same idempotency key (key includes target); un-ticking the matter dropped the target → fresh save, deduped, no children; pane has no saved-attachment names (identity response lacks them). Launch the implementer (opus) in a new worktree `C:\wt141` from origin/master, branch `fix/word-addin-141`; then verify, adversarial review (2 passes: authorization), uac-r2 review of the new add path/child read, PR, owner merge go, BFF deploy via uac-r2 (owner go in uac-r2's session), add-in deploy, owner check. Conflict check 2026-11-26: no open PR touches the files. In parallel: the owner's task 140 live check. Then 090. |
| **Deploy rule (uac-r2, 2026-11-26)** | A dev BFF deploy go must be given by the owner IN uac-r2's session, naming the exact SHA; a relayed go does not count. Code-page / add-in deploys stay with this session under the owner's go here. |
| **Owner questions open** | (1) The pane message for ebilling's 413 `too_large` (now live on dev, unreachable at the default 25 MB)? A small separate PR. (2) Confirm ISS-043 was meant (not ISS-034). |
| **Done** | 2026-11-25: #3134 (137), #3139 (136), #3157 (138) merged; dev BFF aa933c09e6; 136–138 CLOSED. 2026-11-26: uac-r2 deployed dev BFF 1e64e94168 (no Office changes; gate PASS 02:27Z). #3204 (139) merged 03213a90fd and deployed. ISS-044 #3202 filed (upload capture on an auto-file-off tenant is created Suggested; communication owner). |
| **Worktrees** | `C:\wt140` (active, #3214). After use, remove with Remove-WorktreeSafe.ps1: `C:\wt136`, `C:\wt137`, `C:\wt138`, `C:\wt139`, `C:\wt140`. `C:\wt137pub` needs manual deletion (tell the owner). |

## Still with the owner
- Test-diet deletions (`notes/test-diet-report.md`); #996 blocking gate (ruleset choice); 125/135 checks; ISS-001 has no GitHub issue; F-g leftover `event` entry in EntityAccessFilter; task 065 leftover checks.
- Mention: three stray python processes spinning since 11/23–24 (PIDs 41460, 84288, 43772); quality alerts #1821 and #1906 (do not fix).
- Prod Entra: the add-in's `brk-…://addins.spaarke.com` redirect on prod `1958aec2`. Teams admin: reject the duplicate "Spaarke portal" submission `5d88b948…` (keep `60055573…`). Excel/PowerPoint add-in (no go yet).

## Word Email tab: templates + AI draft (ontology D-220, owner ruling 2026-11-12)
- Ontology task 189 builds `GET /api/communications/templates`. Then this project adds the pane side as a new task. Trigger: ontology sends the master SHA + contract.

## Submit Invoice
- **Slice C (needs the owner's explicit go):** remove Invoice from Save and "+ New" in both hosts; tell the owner Word loses Invoice save; then tell ebilling for 096.
- ebilling task 123 upload contract agreed (`projects/ebilling-r1/notes/123-upload-contract.md` on `task/ebilling-123`); the pane's supporting-files choice stays unwired until 123 merges.

## Peers (SendMessage by session name)
- `uac-r2`: dev BFF deploys (owner go per SHA); review of task 140's new confirm write.
- `field-mapping-r1`: G7 guard in their task 035 PR. `ebilling-r1`: #3155 live. `ontology-r1`: task 189 templates route.
