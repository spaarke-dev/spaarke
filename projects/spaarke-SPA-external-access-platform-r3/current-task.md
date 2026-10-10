# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-10 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Project setup: spec done → run `/project-pipeline` (owner-directed sequence) |
| **Step** | Owner sequence: 1 `/merge-to-master` (done — PR #1604, auto-merge on) · 2 commit + push (done) · 3 `/compact` · 4 `/project-pipeline` |
| **Status** | waiting for owner `/compact`, then run `/project-pipeline` |
| **Next Action** | After compaction: confirm PR #1604 merged (`gh pr view 1604 --json state`); if a check failed, fix it on this branch first. Then invoke `/project-pipeline projects/spaarke-SPA-external-access-platform-r3` |

### Files Modified This Session
All committed and pushed on `work/spaarke-SPA-external-access-platform-r3`. Master was merged in at `bf79e279f`, cleanly; the BFF build is green (SDK 10.0.101).
- `spec.md` — C1–C9, FR-01–FR-26, NFR-01–07, ADR tensions, owner clarifications (source for the pipeline).
- `design.md` — auth hold lifted (§4.6 items 6–8); C7 self-registration and C8 auth alignment added.
- `notes/r3-auth-path.md` — the definitive SPA/Teams auth path and owner decisions.
- `notes/coordination/2026-10-09-to-provisioning-t240d-review.md` — R3's review of T240d (provisioning accepted R1–R5).

### Critical Context
- The auth path is decided and grounded in `spaarke-auth-system-of-record-r1`.
- Spec decisions the pipeline must keep:
  - `user_impersonation` everywhere;
  - single-tenant Spaarke workforce client, Spaarke-tenant authority, NAA + popup, no `webApplicationInfo`;
  - run-time backend selection on both planes;
  - workforce contacts self-register via a join link; contact created on first sign-in via the member-test branch;
  - modules by user type;
  - creator stamps `sprk_createdbyperson` / `sprk_createdbycontact`, with delete-own in the SPA;
  - partner service requests behind a switch (default off);
  - R3 owns FR-22 (Ciam audience forms) and FR-23 (default-scheme CIAM guard).

---

## Active Task

`/project-pipeline` for R3 (not started). The pipeline creates README / PLAN / CLAUDE.md / tasks from `spec.md`.

## Next Actions

1. Verify PR #1604 is MERGED (docs only). If CI failed, fix it and push.
2. `/project-pipeline projects/spaarke-SPA-external-access-platform-r3`.
3. Then, in the auth worktree (`C:\code_files\spaarke-wt-spaarke-auth-system-of-record-r1`), start the **remediation phase** for the unowned issues (owner: "whatever is most efficient"):
   - broken buttons and orphans: #1556–#1559;
   - env-var defaults: #1573;
   - BFF hardening: #1574, #1577;
   - dev cleanup: #1578;
   - hygiene: #1579.

## Open Owner Questions / Blockers

- **Registration service host (spec Unresolved Q1).** It co-hosts with the T240c directory, but who builds the directory is undecided: the owner says provisioning builds no new components. If unresolved, FR-14 goes to R4 with a written path.
- **Teams broker pre-authorization:** decided by the FR-21 live check.

## Dependencies tracked elsewhere

- **Provisioning:**
  - T240c directory;
  - T240d (spike S1 running; production CIAM SPA client created after S1);
  - H3 pre-authorization of the new workforce client;
  - PRQ-C-14 (landed, PR #1589);
  - a per-customer shared mailbox (#1562, owner-approved).
- **UAC-r2:** A5 (#1567).
- **Issues R3 carries:** #1563, #1566, #1568.
