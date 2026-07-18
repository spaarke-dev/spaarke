# Current Task State — messaging-communication-app-r1

> **Last Updated**: 2026-07-18 (090 wrap-up COMPLETE — project closed)
> **Project status**: ✅ **COMPLETE**. All 29 tasks done (28 work + 090 wrap-up); BFF + PCFs deployed to dev; merged to master; portfolio archived.

## Active task

**None — project complete.**

## What remains (owner config, not code)

- Set `Communication__Acs__Endpoint` on staging/prod (dev is set).
- App-user **Delegate role + both messaging tables Read=User-level** (enables impersonated reads — crit. 5).
- App-user **Share privilege on both messaging tables** (enables Direct/Open message-access grants — crit. 6).

## Open findings (follow-up, tracked in README "Post-completion")

- Messaging-archival gap (MED) — send path never invokes the archiver (no SPE transcript per chat message).
- Send-into-existing-ACS-thread reuse → R2.
- DI-cycle refactor (LOW) → optional future cleanup.

## Follow-on project

`messaging-communication-app-r2` (Communication Workspace) — draft design; 4/5 owner decisions locked 2026-07-18; only Q3 (coordination confirm) gates `/design-to-spec`.

See [`README.md`](README.md) · [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) · [`notes/lessons-learned.md`](notes/lessons-learned.md) · [`notes/test-diet-report.md`](notes/test-diet-report.md).
