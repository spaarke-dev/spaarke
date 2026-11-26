# Current Task State

> **Last Updated**: 2026-11-26 ~18:40Z (task 043 near close; checkpoint at 85% context)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active** | **Task 043** (`tasks/043-binder-fault-503-retry.poml`, FR-28 #2269), FULL, opus @ high, directional. Owner go "Yes, start 043" (2026-11-26). Notes: `notes/task-043-notes.md` (full record: BFF + SPA changes, caller audit, seeding proofs, both adversarial passes, code-review, adr-check, gates, publish size +224 B). |
| **Committed (local, NOT pushed)** | `675457fbb4` (feature) + `c49088800b` (review fixes: registration 503, ProjectPage retry + fixed error text, WorkspaceHomePage MessageBar, test fixes, docs; D-43 #3264 + D-44 #3265 filed, defer-issues.md updated). |
| **Uncommitted** | `src/client/external-spa/tests/me-client.binderFaultRetry.test.tsx` (source pin: SPA `BINDER_FAULT_CODES` == server `IsBinderFault` set, with must-fire control; 23/23 pass) and `notes/task-043-notes.md` (code-review + adr-check + UAC sections). |
| **UAC review** | **APPROVED** by uac-r2 (2026-11-26 ~18:35Z) for items 1-2 (resolver 503 + registration 503). Quote in notes + PR. Two non-blocking notes: (1) privilege-refused (`StoreWriteStatus.Forbidden`) bind on the sign-in path maps to `contact_bind_failed` → now retries; fix in 043 if cheap (registration bind already maps Forbidden → `DenyContactCreateUnavailable`, ContactIdentityBinder.cs ~:1139); else under #3264. (2) `ContactBindingDecision.DecideIntent` (~:1202) answers lookup_failed on `intent.ContactId != contactId` (inconsistent read) → make the log line name the mismatch; no status change. #3264/#3265 stay labelled R4 (we own them, uac reviews diffs). |
| **In progress** | Applying UAC note 1 cheaply: `ContactIdentityBinder.cs` ~:1769 (sign-in email bind: if `write.Status == Forbidden` → LogError + `Denied(DenyContactCreateUnavailable)`) and ~:1834 create default (add `case Forbidden` → create_unavailable, like :1518). Test via `InMemoryContactIdentityStore.BindStatusOverride` / `CreateStatusOverride = Forbidden` in `BinderFaultStatusTests.cs` (must-not-fire: Forbidden → 403 create_unavailable, not 503). Then note 2 (log wording at ContactBindingDecision ~:1202). Then update #3264 (narrowed to the resolver catch arms). |
| **Next action** | Finish note 1 + 2 + tests; re-run BFF filter tests (BinderFault, CallerPrincipalResolver, ExternalRegistration, ContactIdentityBinder*); commit; TASK-INDEX 043 ✅ (row line 47), POML `<status>completed</status>`, `pwsh scripts/check-task-status-drift.ps1`, /devops-project-sync; push; PR (Placement Justification, UAC approval quote, publish-size, seeding proofs, D-43/D-44); merge only on owner's go. |

## Live on dev
- **BFF:** `d307d67e36` LIVE since ~18:00Z (uac-r2, owner's go); 30-min gate 18:31Z. Rollback `140c01219a`. Task 042's BFF part live since `6a2a569ac9`. Task 043 NOT deployed (not merged).
- **SPA:** master `24d53fb333` LIVE since 2026-11-26 ~15:50Z. Rollback tag `external-spa-dev-rollback-0267ee64a2`. Dispatch SPA deploys from tag `external-spa-dev-<sha>` to build exactly the approved SHA; check the run's headSha.

## Owner questions open
- Track the raw filing-reference footer seen in an older PRJ-000046 system email?
- Track the residual export-hardening gaps privately?
- Docx preview as an Idea?
- "The client is reviewing this invoice." beyond the Information field?
- `useHostRowSource` row in `.claude/patterns/CAPABILITY-INDEX.md` row 31 (`.claude` edit, owner approval)?

## Filed
- D-24 #2939 … D-42 #3201; D-43 #3264; D-44 #3265.

## Housekeeping
- Short-path publish worktrees `C:\wt043m` (master 51ce01a116) and `C:\wt043b` exist; remove with `scripts/worktree/Remove-WorktreeSafe.ps1` after the PR.

## Merge trap (seen 2026-11-22, again 2026-11-25)
Master holds older squashes of this project's notes. On a master merge, keep the branch's copies of `projects/spaarke-SPA-external-access-platform-r4/**` (`git checkout --ours`). After a squash merge of this branch's PR, the next master merge conflicts on the same source lines; take ours only after checking that master's only commit to those files is that squash.
