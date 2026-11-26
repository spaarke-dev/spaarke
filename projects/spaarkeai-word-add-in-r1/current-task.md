# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas". History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-11-26 ~04:30Z. Task 139 MERGED and deployed (waiting on the owner's live check). Task 140 = ISS-043 in PR #3214 with auto-merge on (reviews and uac-r2 cleared). This project runs in the **Claude Desktop session `word-add-in-r1`**.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task 139** | **MERGED** #3204 → master **03213a90fd** (owner go "Yes, merge when green"). Add-in deploy run 45750570047 success; the live bundle carries the new strings (checked by fetching). Review fixes f738a841e8; CI-only test fixes 57ad4d8a1a (`AuthService.naaRefusedToken` msal resolve; a `BrowserMsalStrategy.dataverseUser` exp clock flake); gate list now 149/149 suites. **WAITING: the owner's live check in Outlook on the web** (start-up Retry, pinned pane follows the selected email, select-an-email, optional ITEM_CHANGED mid-save). Then close 139 (POML + TASK-INDEX). |
| **Task 140 = ISS-043 (#3194)** | **PR #3214, auto-merge (merge commit) ON**, head **b68bbe9881**, CI running. Adversarial passes 1 and 2 (opus) are done and their fixes are in. uac-r2 cleared the final diff (d91916cc2c..b68bbe9881). Server: the save confirms (Resolved) only when the save's record is the SOLE regarding: typed lookups == {target} in its own column/table, the pair names it, the pair type is written and is the target table (`RequirePairType = true`, uac-r2 option b), and the status is not Resolved/Ambiguous. Otherwise the result is `NotConfirmable` → wire `FiledUnconfirmed` (the pane says it still needs confirming) when the save's record is on the row, else `AlreadyFiled`. Unfiled path: `ConfirmedByTheSave` lifts Suggested only when the decision names no other written record. Empty-candidates root cause: `SerializeProvenance` over the 10,000-char cap (fixed). Panel: `derivePrimaryReview` treats a pair-named written regarding as needs-confirmation. Dev data: all untyped pair rows are Resolved, so (b) blocks nothing today; the owner's test row is typed (Matter). |
| **Next Action** | When #3214 merges: (1) send uac-r2 the master SHA. **The deploy go must come from the owner IN uac-r2's session, naming the exact SHA** (uac-r2 2026-11-26: a relayed go does not count); uac-r2 asks the owner after its candidate test pass. (2) Code-page redeploy of the review page (communication reconciliation web resource) via the `code-page-deploy` skill, under the owner's go 2026-11-25. (3) After both are live: the owner's live check (save the ISS-043 email from Outlook to MAT-000113 → Resolved, out of Needs Review; the panel shows the record). Then close 140, update ISS-043 and #3194. Then task 090. |
| **Owner go for 140 (2026-11-25, verbatim)** | *"140 yes proceed with review page redeploy; and uac-r2 is approved to deploy"*. Merge and the review-page redeploy are covered here. The dev BFF deploy needs the owner's go in uac-r2's own session (above). |
| **Owner questions open** | (1) The pane message for ebilling's 413 `too_large` (now live on dev, unreachable at the default 25 MB)? A small separate PR. (2) Confirm ISS-043 was meant (not ISS-034). (3) Task 139 live check result. |
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
