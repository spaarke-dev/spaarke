# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-12 (031/032/035 done; 037 + 054 remainder running; by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 037 (notification feed, BFF, isolated worktree agent) and the 054 remainder (SPA: SR delete + recall, invoice documents; this worktree, uncommitted) are running |
| **Step** | R3 head `0cd675b77` pushed. 031, 032, 035 merged, verified (build 0/0, filtered unit 4641, arch 941) and marked done |
| **Status** | Clean apart from agent work. 037 POML/TASK-INDEX set to in-progress (uncommitted until 037 lands) |
| **Next Action** | 037 done → two fable reviews (auth), fix, merge `--no-ff`, verify, mark done. 054 done → one fable review, fix, commit by path, verify (SPA suite), mark done. Then 055 (bell; needs 037 + 054 + 041) |

### Critical Context
- **Parallel pattern:** BFF tasks run in isolated worktrees under `C:\code_files\spaarke\.claude\worktrees\agent-*` (`git reset --hard <R3 sha>` first), commit there, main session merges `--no-ff`. SPA tasks run in this worktree, uncommitted; the main session commits by explicit path. Agents never edit TASK-INDEX, this file, POML status or `.claude/`.
- **Review policy:** auth/security/tenant-isolation changes get two fable passes; other FULL tasks one. Fix rounds sonnet; a fix that changes approach gets a scoped re-check. Max 4 agents.
- **Bookkeeping edits:** python script files in the scratchpad (sed breaks on the status emoji). Check with `pwsh -NoProfile -File scripts/check-task-status-drift.ps1`.
- **Do not run the full BFF unit suite under load** (host contract tests time out); use filtered runs.

## Done this session (2026-10-11/12)

- 035 intake: owner = requester contact's BU default team; refuses unresolved (merged `44f14ba11`).
- 031: invoice document routes (option A: union of matter/project rights); SR documents land in a secure record's container; back-reference fix (merged `8cdfa7659`).
- Owner-decision BFF set (merged `1cfa1c5a4`): `DELETE /servicerequests/{id}` (requester only; 409 `has_related_records`; recall = 034 message post), privileged + potentially privileged messages hidden externally (`CommunicationThreadReadService.ExternallyHiddenPrivilege`), sender from `Communication:DefaultMailbox` (must be in `ApprovedSenders` outside Dev; scripts emit `ApprovedSenders__0`).
- Live (dev): 040 steps 0-3 (Spaarke Workspace client `9f1da340-c647-4266-9d89-e09f10700593`, pre-auths), 041 CIAM `user_impersonation` `2017a8fe-…` + pre-auth `bd57e54e` + `/auth-callback.html`, 043 environments `external-spa-dev` / `external-spa-prod`. Records in task 040/041/043 notes.
- Docs: Model 1 home-tenant explanation (deployment guide §6.5.2 + `.claude/constraints/auth.md`, owner-requested).
- ISS-005 (#1673): binder-created contacts have no BU-team owner.

## Open owner questions

1. **Closed SR delete:** may a requester delete a resolved/closed SR with no related rows (today yes), or only open ones?
2. **Invoice upload into a secure container:** union of rights lets a Collaborate-on-project caller upload into the secure matter's container of an invoice with both parents. Keep, or require Create on every parent?
3. **Intake forms (052/053):** five questions in `notes/intake-form-design.md` (department from sign-in; Invention staff-only; incident channel; fixed vs per-customer option lists; NDA review-only vs issue standard NDA).
4. **ISS-005:** which BU a binder-created workforce contact belongs to.
5. **Go for dev dispatch:** Teams package + SPA (`external-spa-dev`), then FR-21 live sign-in check (task 071); 040 step 5 after it.
6. **003 Q4 follow-up answered** (CustomerTenantIds = customer's own Entra tenant ids); R3 060 adds the registry field.
7. **002:** grid columns to match the main entity views (owner 2026-10-11) — not started.

## Next actions (in order)
1. Land 037 and the 054 remainder (reviews, merge/commit, verify).
2. 055 (bell + deep link), then 052/053 after the intake-form answers.
3. 060 (self-registration service; co-host with the T240c directory, own MI; registry field `CustomerTenantIds`).
4. 002 (columns from savedqueries), 039.
5. Before the BFF PR: `/conflict-check` (`AuthorizationModule.cs`, `Api/ExternalAccess/**` shared with UAC-r2) and actionlint on both deploy workflows.

## Dependencies tracked elsewhere
- ISS-001 (#1623) AuthorizationModule once-per-process flag; ISS-002 (#1633) streaming contract test flake; ISS-003 (#1636) shared `sanitizeEmailHtml`; ISS-004 internal document delete orphan; ISS-005 (#1673).
- Copilot agent still uses `access_as_user` on `1e40baad`: 040 step 5 must not remove it.
- Operator: `mailbox-central@spaarke.com` + Exchange RBAC scoping for the stamp UAMI (H14a); BFF app user needs `prvDelete` on `sprk_servicerequest` (verify live).
