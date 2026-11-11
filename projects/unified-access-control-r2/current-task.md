# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.

> **Last Updated**: 2026-11-11 (checkpoint #123, desktop session `uac-r2`). Private details: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-handoff-2026-11-09-198.md` and `private-handoff-2026-11-08.md` (outside the repo; never copy them into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | **WRAP-UP.** The owner said "please proceed so we can wrap this up" (2026-11-11), then answered **"Accept all"** to D-1..D-12 in `notes/wrap-up-uat-and-decisions-2026-11-11.md`. Round-90 rules apply. This session (`uac-r2`) is authoritative. Merge: run `git merge-tree`, then squash with `--match-head-commit`. Live actions need the owner's go IN THIS SESSION. Bash `grep`/`sed` loops HANG: use Grep, Read or PowerShell. |
| **Next action** | (1) Wait for mcp-r1's answer on the spike, and for ebilling's sync, boundary-test cause and `--remerge-diff` list. (2) Start the D-3 and D-5 fixes as two Opus implementer agents, each in a fresh short-path worktree from origin/master. (3) Run 090 through the `task-execute` skill (see "090 scope" below). (4) P-1: a read-only check, then report to the owner. |
| **Merged (not deployed)** | 198 `c71414c7a0` (#2670), F-198-1 `0a0e6a9aa7` (#2673), 198b `c398d2cecd` (#2677), #2669 `a7d7aad31f` (#2679). Also on master: fm 2b `56d3463c1a` (#2675) and ontology 177 `c152d7d0b1` (#2674). Filed: #2678 (the chain-bound limit on the Assistant tools and internal routes, low priority). |
| **Dev state** | The BFF is `755987b385` (mcp-r1's spike plus master through #2660). The spike is NOT on master, so a deploy from master needs mcp-r1's answer first (asked 2026-11-11). |

## Owner decisions (2026-11-11, "Accept all")
- **D-1:** close 036, 099, 135, 136, 037, 039, 013, 141, 140, 153, 165 and 171. Their UAT is carried in the wrap-up table (U-1..U-11).
- **D-2, deploy order:**
  1. dev BFF from master, once the ebilling batch lands and mcp-r1 is coordinated;
  2. fm 054b -DryRun / -Apply / -Verify (owner's go in fm's session);
  3. Direct setup plus `DirectMessage:BusinessUnitName` / `OwnerTeamName` (owner's go HERE, only after 054b verifies);
  4. 054c Stamp;
  5. piece 3;
  6. 054c Move.

  **BINDING:** the `DirectMessage:*` settings stay unset on an environment until 054b verifies there. While they are unset, 198 is inert: NotConfigured makes `IsDirectOwnerTeamAsync` return false. fm agreed and is recording the rule in the 054b/054c notes.
- **D-3:** #2033 refuse the send for a user disabled in Dataverse. A small fix to do now.
- **D-4:** #2245 carried to a follow-on project; the design is to bind readers to the locked copies.
- **D-5:** #2250 refuse `sprk_assigned*` on the app-only playbook writers. A small fix to do now.
- **D-6:** #2270/#2278 carried to the follow-on secure-family project.
- **D-7:** #2203 goes to the roadmap.
- **D-8:** 036 (iii): accept the unit proof.
- **D-9:** 184 G3 via provisioning T240d.
- **D-10:** turn on 142's job revoke-on-change setting after U-3 passes. It is an app setting, so it needs the owner's go then.
- **D-11:** 166 (f): tests accepted as proof.
- **D-12:** ebilling 116 condition 1 revised (reject-equivalent rights checked as the user, then an app write). Relayed to ebilling.
- **P-1 (private, chat only):** a dev app user named "...-prod" holds System Administrator in the root BU. Recommended: compare its application id with the prod BFF registration (read-only), and disable it on dev if they match. **The disable needs an explicit owner go**; "Accept all" is not taken as that go.

## 090 scope (from the audit, 2026-11-11)
- Reconcile TASK-INDEX: close the D-1 tasks, plus 058 (absorbed). Stale rows: 036, 099, 153, 165, 171. Add the 198 row(s). The index shows 5 deferred tasks, not 7.
- Defer/issue disposition: reconcile `defer-issues.md` with `gh issue list --label unified-access-control-r2`. #1304 (task 089) is CLOSED while the task is `[deferred]`, so fix that. Close #1059, #1063, #1077 and #1104 with the D-1 closures. The owner confirms the hand-off list in one batch.
- `/test-diet` → `notes/test-diet-report.md`.
- H-8a stale comments: `ExternalCallerContext.cs:7`, `ExternalDataService.cs:18`, `ExternalAccessEndpoints.cs:75` (verify against master).
- Docs: 058 (parents vs children, stamp inheritance, matter ≠ project, correct the No Access doc), the 082 paragraph, the 087 correction, the 089 known-gap line.
- `notes/lessons-learned.md`. README → Complete. `CLAUDE.md` §2: the 036 ADR-034 amendment is MERGED (the text there is stale).
- A wrap-up PR that cites the test-diet report and the register table. Then tell the owner the full code review can start.

## Peer state
- **ebilling-r1:**
  - Master sync is on `merge/ebilling-master-sync` at `f7b6d75061`. The conflict with #2679 in `ExternalDataService.cs` is resolved as the union. My test `DocumentsModuleFetch_OrdinaryHiding_AndAChainOfExactlyMaxHops_NotTruncated…` fails under the union; their agent is checking whether that is the test world, under my 3 conditions (the boundary stays; fail closed; both bounds pinned). Then I do the `--remerge-diff` read, they run `/merge-to-master`, and I deploy.
  - 116: round 5 comes after the owner accepted D-12. Review it after their re-check.
  - The owner's go in their session for 164 `-Apply` and the `sprk_sourcedocument` delete (after 079 and a BFF that contains 116) is theirs to run.
- **field-mapping-r1:** 054b is being built (Opus, `task/fm-054b`): a code scan, BFF refusals, and `Set-ThreadFlagFieldSecurity.ps1`. 054c is being built (`task/fm-054c`; the Stamp and Move phases both refuse to run unless 054b verifies). They send me the 054b scan, then the diff.
- **ontology-r1:** 177 is on master. After the next dev deploy (`sprk_document` needs `sprk_accesspermission` and `sprk_accessinheritance`, both on dev), they run the 176+177 smoke, which needs one Budget inquiry from the owner.
- **mcp-r1:** asked about the spike on dev; waiting for the answer.
- **Still to review:** ebilling's DEF-060 build diff; R4's #2269 and FR reviews; provisioning #2618's other areas.

## Peers (cross-session names)
ebilling `ebilling-r1` · field-mapping `field-mapping-r1` · ontology `ontology-r1` · provisioning `provisioning-r1` · mcp `mcp-r1` · R4 `spa-r4`.

## Scratch
- Untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs` in this worktree was left by an agent; leave it alone.
- No task worktrees are open. `C:\wt198` still exists from 198 (`Remove-WorktreeSafe.ps1` when it's no longer needed).

## Private items (never in issues/PRs/commits)
See the private handoffs. Identifiers are fine in public. "X can still read/write Y today" about an open gap, or an identity's role holdings, is not.
