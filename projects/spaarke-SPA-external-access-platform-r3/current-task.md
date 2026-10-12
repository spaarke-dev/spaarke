# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-12 (057 done; waiting on owner; by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | None active. 057 done (`39d2712ce`) |
| **Step** | 039, 055, 057 done and pushed. All agent-runnable tasks without an owner gate are complete |
| **Status** | Clean. Waiting on owner answers |
| **Next Action** | Owner answers unblock: 002 (columns), 052/053 (intake), multi-root A/B/C, dev dispatch go (070 -> 071 -> 072, 040 step 5), 060 host. Before the BFF PR: /conflict-check + actionlint |

### Critical Context
- **Parallel pattern:** BFF tasks run in isolated worktrees under `C:\code_files\spaarke\.claude\worktrees\agent-*` (`git reset --hard <R3 sha>` first), commit there, main session merges `--no-ff`. SPA tasks run in this worktree, uncommitted; the main session commits by explicit path. Agents never edit TASK-INDEX, this file, POML status or `.claude/`.
- **Review policy:** auth/security/tenant-isolation changes get two fable passes; other FULL tasks one. Fix rounds sonnet; a fix that changes approach gets a scoped re-check. Max 4 agents.
- **Bookkeeping edits:** python script files in the scratchpad (sed breaks on the status emoji). Check with `pwsh -NoProfile -File scripts/check-task-status-drift.ps1`.
- **Do not run the full BFF unit suite under load**; use filtered runs.
- **039 interim rule (main session, pending owner):** an external list row with several core roots is shown only when the caller has Read on EVERY root (`ParentReadable` / `ReadableEvents`). Fix round extends it to to-dos and audits documents, messages and the feed.

## Open owner questions

1. **Closed SR delete:** may a requester delete a resolved/closed SR with no related rows (today yes), or only open ones?
2. **Invoice upload into a secure container:** union of rights lets a Collaborate-on-project caller upload into the secure matter's container of an invoice with both parents. Keep, or require Create on every parent?
3. **Multi-root visibility (039):** A every-root (interim) / B secure-roots-only (recommended) / C union.
4. **Bell items:** staff team-membership grants; a renewed grant restored in place produces no bell item.
5. **Intake forms (052/053):** five questions in `notes/intake-form-design.md`.
6. **ISS-005:** which BU a binder-created workforce contact belongs to.
7. **002:** expand the allow-list for the 15 excluded columns? active-only? sort? seed in repo? (`notes/task-002-notes.md`).
8. **Go for dev dispatch:** Teams package + SPA (`external-spa-dev`), then task 071 live sign-in; 040 step 5 after it (never remove the Copilot `access_as_user`).

## Next actions (in order)
1. (done) 057.
2. Known limits recorded in notes/task-039-notes.md section 11 (truncated one-bit signal, thinned grid pages, invoice documents vs thread).
3. 002 after the column answers; 052/053 after the intake answers.
4. 060 (self-registration service; co-host with T240c, own MI; registry field `CustomerTenantIds`).
5. Before the BFF PR: `/conflict-check` (`AuthorizationModule.cs`, `Api/ExternalAccess/**` shared with UAC-r2) and actionlint on both deploy workflows.

## Dependencies tracked elsewhere
- ISS-001 (#1623), ISS-002 (#1633), ISS-003 (#1636), ISS-004, ISS-005 (#1673).
- Copilot agent still uses `access_as_user` on `1e40baad`.
- Operator: `mailbox-central@spaarke.com` + Exchange RBAC scoping for the stamp UAMI (H14a); BFF app user needs `prvDelete` on `sprk_servicerequest` (verify live).
