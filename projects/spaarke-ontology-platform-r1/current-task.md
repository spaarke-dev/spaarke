# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-11-13 ~03:40Z (by context-handoff, at 85% context). State only; history in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-11.md` (previous checkpoint archived there verbatim). Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 and §6.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..**D-221**). Deferred work: one GitHub issue per item, label `ontology-deferred`,
> plus a DI-nn row in both tables of `notes/defer-issues.md` (next free: **DI-154**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Role** | Main session `ontology-r1`: orchestrate R1; review every PR touching ontology-owned files (`Services/Signals/**`, `Api/Signals/**`, `FinanceBaseValues.cs`, `Services/Signals/Billing/**`); answer peers; hold wrap-up 090 until owner UAT done. Peer messages are not owner approvals. |
| **Working dir** | `C:\code_files\spaarke-wt-spaarke-ontology-platform-r1`, branch `docs/ontology-platform-design` (pushed). |
| **Active** | **190 + 191 are live on dev (2026-11-13):** BFF `b100effac3` (uac-r2: master `c2f366bf52` + mcp spike, 20:39Z) and SpaarkeAi Console `sprk_spaarkeai` built from `c2f366bf52` (published ~20:50Z). Waiting on the owner re-test on a fresh zz- fee/scope communication after the nightly run. ebilling: ISS-021 resolver fix #2798 signed off, auto-merge on (owner go); EB-D-232 (assign refuses statecode!=0) comes with 084d-5a for review. |
| **Next Action** | (1) Owner re-tests the Budget inquiry (Cc/Bcc, failed-send recovery) on a fresh zz- communication; triage findings. (2) Review ebilling 084d-5a (+EB-D-232) when it arrives. (3) Then 189, 180, 182, 183, 184, 188, 185 (deploy, owner go). Temp folders `C:\t191m`, `C:\t191p` for owner to delete. |

### 176/160 run findings so far (owner screenshots 2026-11-13)
- Dev data: budget revision `5e408089…` created 03:26:52Z; service request `e03b4d2e…` "Fee or scope change, no budget revision: Intellectual Asset Management System" 03:26:54Z; **no sprk_communication** created; Signal `48fddf1f…` auto-closed 03:27:27Z by the budget-revision trigger ("resolution 100000002, no Decision Record"). UI: "The service request was created but the email was not sent (decision.action.write_failed)"; retry 03:29:42Z = 500 (replay; "earlier attempt did not finish"), ref 0HNQ162B12DDA:00000015, op 6889d087.
- First-attempt decision op NOT in App Insights / Log Analytics (25% sampling). No Graph sendMail seen 03:26–03:28Z (not proof: sampled ops drop whole).
- Graph mailbox **reads** failing 502/503 + 30 s timeouts since ~16:00Z 2026-11-12 (Inbox polling `mailbox-central`, `testuser1`); predates the 02:49Z deploy. Every `POST /me/sendMail` is followed by `GET /me/mailFolders/sentitems/messages` **400** (16:52Z, 17:00Z) → .eml archive lookup likely broken (D-216) — check and file.
- Deploy diff (e9208abc5e) Message-channel code returns early for Email (`IsUnfiledMessageSend` false) — unlikely cause.
- Separate: `POST /api/finance/matters/{id}/recalculate` PATCH `sprk_pendingspend` → 400 `0x80048d19` (03:27:38Z) — finance rollup defect, file/route to ebilling.
- Separate: 03:11Z thread code queried `sprk_regardingreportcard` on `sprk_communicationthread` (column absent) — find caller, file.
- **Owner UX ask:** inquiry step shows "Cc and Bcc are not sent with this step. Add every recipient to To." Owner: if Cc is offered it must work; alternative pattern = Cc collapsed like Bcc, revealed on click, for ALL email forms. D-219 Q4 already says Cc + Bcc as in Send Email → inquiry executor must send Cc/Bcc end to end (task 182/183 scope check).
- Owner's half-done decision: budget revised + service request created, no email, no DR, Signal closed. Needs owner choice on clean-up (delete SR? re-raise?) — ask, do not act.

## Open items

**Owner**
- **Dev BFF = `9411f604bb`** (uac-r2 2026-11-13 02:49Z: master `e9208abc5e` + mcp-r1 spike `d7369fecf1`; includes #2754). Previous: `6dbe69b9e2` (uac-r2 deploy 2026-11-12: master `a654da59c4` + mcp-r1 spike, local merge; DLL read by us). Contains 177, #2689, #2691, ebilling 116, **#2695**. 177 re-check had PASSED on the previous build `b3ca99cad5`.
- **#2695 merged to master 2026-11-11 as `25ac8ea931`** (squash; outgoing email association status → Resolved when the send files it; replies Resolved only from a Resolved parent). Not deployed. Dev backfill of 76 existing rows done (owner-approved). Worktree `C:\wtoas` removed.
- **177 smoke PASSED on dev 2026-11-11** (owner, Assign Work task email): Outgoing, internal-only, `sprk_regardingmatter` set, .eml document on the same matter, shows in Messages + Documents. Sent to uac-r2.
- **176 smoke NOT RUN:** no open Revise-budget Work Item on dev to send a Budget inquiry from; verify `sprk_regardingmatter` + `sprk_regardingservicerequest`, internal-only, Private thread, .eml on the matter, Messages. Note: after #2695 deploys, inquiry replies auto-file (Resolved) instead of Suggested; `ConfirmSuggestedAssociationAsync` then no-ops (owner-approved).
- **UAT round 4 (2026-11-12, owner as testuser1):** 162, #2394, 114, 111 row 4, F-R3-1 PASS → tasks 111, 114, 162 closed (POML + index + completion; drift check clean); #1428 closed. Still open: 160 (its visual checks ride the 176 test), 176 (needs a zz- fee/scope comm, owner's go), 105 (owner wants to review and update the R1–R24 checklist). **090 held.**
- **New round 4 findings (uat-v4-log Round 4):** F-R4-1 defect: Create Project wizard outside-counsel search queries `sprk_organization.sprk_name` (does not exist; column is `sprk_organizationname`) at `CreateProjectWizard.tsx:141-152` and `projectService.ts:187-197`; fix = use `matterService.searchOrganizationsAsLookup`. F-R4-2 request: side-pane lookups in the create wizards' Assign Work step. F-R4-3 request: create wizards' Send Email step on the shared EmailComposer. **F-R4-1 fix: PR [#2737](https://github.com/spaarke-dev/spaarke/pull/2737)** (branch `fix/create-project-org-search` from master `9456083c78`, head `5843e678e2`, worktree `C:\wtorg`; owner said "Yes, fix it"): both searches now call `matterService.searchOrganizationsAsLookup`; new test fails 2/3 before, passes after; 24 suites / 287 tests, tsc clean. Auto-merge (squash) ENABLED 2026-11-12 on the owner's "merge on green". Next: GitHub merges on green → Console auto-deploys from master → owner re-checks Outside Counsel → remove `C:\wtorg` with Remove-WorktreeSafe.ps1. Owner still to say where F-R4-2/3 go.
- **Terminology:** connector renamed **Spaarke MCP** (owner 2026-11-10, recorded in mcp-bff `phase0-registration-facts.md`); `ontology-component-model.md` §1.1 rows updated on docs 2026-11-12 at mcp-r1's request; mcp-r1 task 050 (root CLAUDE.md product-names row) waits on it reaching master with the next docs→master PR.
- **161 CLOSED 2026-11-12** (owner "yes close it"; evidence DR-1167 + round 3). 160 stays open: its visual checks ride the 176 test.
- **176 test data CREATED 2026-11-12 (owner's go):** zz- inbound communication "zz- UAT 176 fee or scope change (budget inquiry) - MAT-34929-0040", triage category fee/scope, regarding MAT-34929-0040 (has an active budget, no revision). **Signal present** (checked 2026-11-13 ~02:55Z): open "Fee or scope change, no budget revision" created 2026-11-12 21:30Z, dedupe key episode 2 on MAT-34929-0040. Dev BFF live 1.0.0+9411f604bb (master e9208abc5e + mcp-r1 spike; uac-r2 deploy, 2026-11-13 02:49Z). Next: tell the owner to send a Budget inquiry from it; verify the comm (regardingmatter + regardingservicerequest + internal-only + Private thread + .eml on matter + Messages) and 160's visual checks. Delete this zz- row afterwards (owner already said test emails can be deleted).
- **3 old zz- comms DELETED 2026-11-12** (owner yes; MCP delete errored, done via Web API DELETE 204 x3; query confirms gone).
- DEF-059 (#2635) is DONE (closed 2026-11-09): option C, a one-privilege Decision Record reader role on the BFF identity only, live on dev; customer environments go to customer-provisioning-orchestration-r1. Nothing owed by the owner.
- F-R3-1 footer fix is on dev (in `a654da59c4`): owner can confirm an Assign Task notification footer shows no logical name or GUID.

**uac-r2 (`uac-r2`)**
- #2670 (198, Direct 1:1 participant access) merged to master `c71414c7a0`, not deployed. Our post-merge review found **F-198-1** (race in `ReownToDirectAsync`); their fix **#2673** (`2bb9f25ee6`) **re-checked and approved by us** — they merge on green. K1–K5 recorded in their 198 notes §7.8.

**ebilling-r1 (`ebilling-r1`)**
- EB-D-216 (inactive Draft policy versions): no draft lister on our side; their guard stays out of `Services/Ontology/**` and `Services/Signals/**`; `ReadVersionsAsync` stays unfiltered. They send file:line if their sweep finds one of ours.
- DEF-061 review F1 + rulings sent; waiting for their follow-on SHA. Owed by them: RowReads rerun, DEF-059 outcome. DEF-060 builds after 116 + 130.
- **084 (EB-D-217, owner brought into R1):** 084d adds a caller-read dry-run route over `Services/Signals/Billing/ProcedureResolver.cs` via an in-memory row-source overlay (selection logic unchanged). They send the design before building, plus any `Services/Signals/**` diff. Our review conditions sent 2026-11-11: overlay at the row-source seam only; parity test through the real resolver; caller-read filtering (D-123); per-request, uncached; no writes; route census. Part A and 084a–c don't touch Signals. **Owner moved the mark to new task 084e (EB-D-219), built under our R1–R6 (binding constraints + our must-fire/must-not-fire cases as acceptance; opus).** 084e step 1 design REVIEWED 2026-11-12 (approved with 3 binding changes: valid evaluated lazily rank-by-rank, never on a Replace-displaced version (HandlerAccepts logs/records refusals); outcomes as an input-order list, not a dict keyed by the record; ProcedureRank equality/CompareTo as today; plus parity fixtures and an episode-filter boundary test); Signals diff + parity test to us before merge. 084 itself ships four marks (excluded, changed, stale, base retired) with no `Services/Signals/**` change. Our original call: show it only as "Outranked by X" with the "unless X is refused on an invoice; a line can differ" note, under R1–R6 (top = highest gate-accepted, not-downgraded rank; Split needs a stated cap; ties never; "by" lists all top-rank displacers; ONE pure precedence function extracted from evaluator step 4 into Services/Signals/Billing with a parity test, diff to us; legacy versions grouped too); otherwise leave it out. Reason: HandlerAccepts runs on the invoice facts, so validity is not knowable per record (B2).
- **Held PRs ours:** 174 (#2580) and 175 (#2579) wait for ebilling 112 (Invoice Review) on dev; then merge both and send SHAs to uac-r2.

**provisioning-r1** — task 267 (writer into its own business unit): we answered "the writer reads no field-secured column as itself"; recorded in their notes. No ACME check needed now.

**mcp-r1** — pings us when #2587 merges; then DI-143 #2630 builds on `WorkItemPrivilege`.

**Ours, after the critical path:** DI-144 #2632, DI-145 #2639, DI-146 #2650, DI-147 #2653, **DI-148 #2671**, **DI-149 #2672**; F-R3-1 footer fix (reaches master with this docs→master); `OntologyAdminClassificationService.cs:658/685/705` waits for `sprk_publishstate`; stale `DecisionRecordWriter.cs` comments wait for ebilling 074; close #2212 when ebilling `f0884a175f` reaches master.

**Worktree cleanup (use `scripts/worktree/Remove-WorktreeSafe.ps1` only):** 176/177 agent worktrees removed 2026-11-11 (tips verified inside #2666 / master). Left: task174 `agent-a87a71b050eceafb1` under `C:/code_files/spaarke/.claude/worktrees/` (after #2580/#2579 merge).

**ebilling master PR [#2689](https://github.com/spaarke-dev/spaarke/pull/2689):** ontology scope **APPROVED 2026-11-11 at `ec11c6d0e2`** (PR comment 6799396706; one adversarial review, no F-class; DEF-061 `340b6c53ad` approved explicitly; test fixes `49a49437bc` + `14e54c9e06` accepted). Public K1 (`sprk_publishedon` before BFF), K3, K5 on the PR; K2/K4 PRIVATE, sent to ebilling by message. **MERGED 2026-11-11 as master `2fc434c912`** (head `60628550fb`; delta since `ec11c6d0e2` has no change in `Services/Signals/**`, `Api/Signals/**`, `FinanceBaseValues.cs`). Their K4 O3 guard comment fix `584f6cc34d` (work/ebilling-r1) **checked 2026-11-12: K4 closed**, rides their next master PR; non-blocking note sent (ReadingRoleFile still names Ontology Service while DEF-059's reader role supplies the BFF read); 116 is open to master as [#2724](https://github.com/spaarke-dev/spaarke/pull/2724) (checked at `2c4ef48445`: no ontology-owned files; billing drafts can't reach our readers, which filter `sprk_domain` to Ontology). They still send a notice before any live `sprk_policyversion` write.

## Dev state

- Every dev BFF deploy runs in uac-r2's session with the owner's go per SHA; before/after notices to word-add-in, R3/R4 and uac-r2. Console auto-deploys from master (CLAUDE.md §6).
- Stash: two old `lint-staged automatic backup` entries are not ours. Leave them.

## Peers (re-check with ListAgents)

Desktop: `uac-r2` (dev BFF deploy owner) · `provisioning-r1` · `ebilling-r1` · `mcp-r1` · `word-add-in-r1` · `acclerator-r1` · `spa-r4` · `field-mapping-r1` · `accelerator-claude-r2`. VS Code: R3 `spaarke-wt-spa-external-access-platform-1f` (no longer deploys).
