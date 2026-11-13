# Current Task State

> **Last Updated:** 2026-11-13, about 21:00Z. Session `ebilling-r1`, Claude Desktop, the only writer to this worktree.
> Older detail is in `notes/handoff-history/current-task-2026-11-13a.md`: the 116 rounds, the master-sync history, and the owed lists for word-add-in, provisioning and DEF-060.

## Quick recovery

- **Branch:** `work/ebilling-r1`, pushed, tip `270c5c6b3e` or later. Nothing uncommitted.
- **Owner mode:** "keep progressing on open tasks so we can complete the project".
- **Active task:** 084d. Its parts are in `tasks/TASK-INDEX.md`.

| Part | What | State |
|---|---|---|
| 084d-1 | Planning without writes | ✅ `d24681ed38` |
| 084d-2a / 2b | The dry-run route, plus `closes` and the affected count | ✅ `c313c6c211` |
| 084d-3 | Parity, mutation, no-write and concurrency tests | ✅ `588526e579`, plus ontology-r1's order pin `07c618c163` |
| 084d-4 | Client: Impact on the dry run | ✅ `cd869e3050`. Jest 392/392. Opus review, then a Sonnet re-check: PASS |
| 084d-5a | Server: assignable-Procedures route, and the 089 assign refusing non-Billing (EB-D-228) | 🔄 BUILT on branch `task/084d-5a` (`de52771371`, base `3a1d15a50d`, 3 commits). **NOT merged.** |
| 084d-5b | Client: Assign Procedure wizard | 🔲 after 5a |

- Design, with the binding conditions for 5a and 5b: `notes/084d-dry-run-design.md`, sections "## 084d-5 design" and "### 084d-5 design: answers and binding conditions":
  - owner: EB-D-227..230;
  - ontology-r1: conditions 1-3;
  - uac-r2: Q2/Q3 and notes 1-5.
- 084d-5a build report:
  - Filtered BFF tests 8608/0/4; ArchTests 2570/0; ledger count stays 153.
  - Publish size +278 B against master `c2f366bf52`.
  - Mutation checks 2/1/2/16. `RecordAssignableProceduresContractTests`: 56.
  - Deviation: `AssignRefusalOf` takes the row only.

## Next action (exact)

1. **Opus adversarial review of `task/084d-5a`**, comparing `3a1d15a50d..task/084d-5a`. Then fix rounds until no F-class finding remains. Then send the diff to uac-r2, which gates the merge, and the code to ontology-r1. Then merge into work, update TASK-INDEX and close out the notes.
2. **File the pre-existing finding from the 5a build.** A Procedure deactivated in Dataverse (statecode 1) but with Status Active is still applied by the resolver and accepted by 089's write, because neither reads statecode. The fix is in the resolver, which ontology-r1 owns, and in 089's write.
   - Tell ontology-r1.
   - File it with `/project-defer-issue-tracking`. That is not done yet.
3. **084d-5b, the client wizard.** Gate the entry point on `allowedSteps(...).create`. The pick list comes from the assignable route. Offer Follow latest or Pin, with versions from `GET /api/finance/procedures/{id}`. Show the Gap warning, and Impact on the dry run. Write through 089. Then jest, an Opus review, and the uac-r2 and ontology-r1 reviews.
4. **Then mark 084d ✅** and go to task 112: deploy, ui-tests, the access-invariant check from session memory, and the step 3 probe. Its live changes need the owner's typed OK.
5. **Then 095e → 095f → 095g** (owner rulings EB-D-223..226; `notes/095g-assign-firm.md`). 095g needs a design note to uac-r2 before any code.

## Owner decisions this session (all recorded in `notes/decisions-log.md`)

- **EB-D-223:** no invoice without a firm, in every channel.
- **EB-D-224:** assign a firm from Outlook (095g).
- **EB-D-225:** a correction note, in both places (095f).
- **EB-D-226:** the invoice name comes from the chosen firm (095e).
- **EB-D-227..230:** the Assign list.
- **EB-D-231:** a wrongly filed invoice is returned and resubmitted, never moved.
- **Pending Spend on dev:** DONE (ISS-018). Still to do: add it to SpaarkeMaster source and to 112's apply list.
- **No owner question is open.**

## Peers and reviews in flight

- **field-mapping-r1:**
  - PR #2790 to master is open. It carries 045d/e/f and 052b, which we reviewed (045f: PASS).
  - They are building **045g** (EB-D-231 / ISS-020 #2794, branch `task/fm-045g`, after #2790): no invoice move, children match their invoice's core record, and no line detach. We review before merge.
  - We sent them 7 Finance flows to keep legal. Among them, the commit undo clears the submission's invoice; it must stay allowed.
- **uac-r2:**
  - Gates 084d-5a's merge (one Opus pass on our side first).
  - Its 095g conditions are in `notes/095g-assign-firm.md`.
  - The dev BFF is `0c06f464e3`, with no 084.
- **ontology-r1:**
  - 084d-3 is OK. Send it the 5a code.
  - Still owed to it: the DEF-059 role note, the DEF-060 Signals diff, and the 176 fixture change.
- **word-add-in-r1** (its task 134) is owed:
  - the master and dev SHAs for 095e (banner `firm {id, name}`);
  - the 095f contract;
  - the 095g contract, after uac-r2 agrees.
- **provisioning-r1:** re-run the `Set-CustomerOwnerTeams` Pester tests after our next master merge (#2780 overlap). Their S26 needs SpaarkeMaster 1.3.1.0 installed on each stamp.
- **spa-r4:** ping it before task 134.

## Filed this session

- ISS-018 #2777 (Pending Spend)
- ISS-019 #2782 (NFR-01 named firm)
- ISS-020 #2794 (no invoice move; built as fm 045g)
- ISS-017 #2772 (decided, built in 045e)
- DEF-067 #2770

## Rulings in force

- **EB-D-218:** the read routes read app-only, then run ONE batched caller check. A refusal or fault gives 503. `Exempt` covers only the exact synthetic rows.
- **EB-D-220/221/222:** see the decisions log.
- **D-170:** only switching a rule Off closes findings.
- **D-96:** now extended by EB-D-231: no invoice move at all.

## Remaining tasks

- 084d-5a (review and merge), 084d-5b.
- 112.
- 095e, 095f, 095g.
- 079.
- 131–135 (134: ping spa-r4 first).
- 093, 096, 113, 117, 121, 123, 124.
- Deploys 053, 136, 153.
- 160, 162, 163, 189, 190.
- DEF-060 build.
- The `sprk_sourcedocument` delete.
- Drift on 050a/161.
- DEF-067 design.

## Standing gates

- The creator-person `-Verify` exits 0 before any BFF deploy.
- Reconcile stays OFF until DEF-044 is on master and L6 passes.
- No stamp gets a BFF carrying 116 until SpaarkeMaster 1.3.1.0 is INSTALLED on it.
- A live Dataverse write or role change needs the owner's OK typed in THIS session, plus uac-r2 notices before and after (and ontology-r1 for `sprk_policyversion`).
- The access-invariant check lives ONLY in session memory (`eb-d-218-access-invariant-check.md`). Run it after every solution import; record pass/fail only.
- **Agents:**
  - At most 4. Name the model; launch from the worktree root.
  - `isolation: worktree` starts on MASTER, so every brief names its base sha.
  - Remove a worktree only with `scripts/worktree/Remove-WorktreeSafe.ps1`, after `git worktree unlock`.
  - An empty leftover folder `C:\code_files\spaarke\.claude\worktrees\agent-a7122a26394c93f4f` could not be deleted (permission denied). It is no longer a git worktree.
- NEVER run python in any form, agents included. Never use `--no-verify` or a bare `git stash`.
- The repo is public: describe security findings by mechanism only. Private findings go to uac-r2 by message.
