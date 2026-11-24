# Re-home root-owned dev core records into BU1: dry run (2026-11-24)

Owner asked for a dry run first ("Yes, dry run first", 2026-11-24). Read-only: no row was written. Issue: #3047. Precedent: the 2026-11-02 BU1 move, step 4 (`notes/decisions.md`).

## What is in the root business unit

149 core records, all created 2026-11-02..11-18; none secure, none Restricted.

| Table | Rows | Owner today |
|---|---|---|
| Matter | 80 | root default team (created by the dev BFF identity) |
| Matter | 63 | the administrator's own user account (created directly, not through the BFF; 21 each on 11-05, 11-06, 11-08; no `sprk_createdbyperson`) |
| Project | 3 (PRJ-000043, -45, -47) | root default team |
| Work assignment | 3 | root default team; each is filed under a root-owned matter or project from this set |

- No root-owned project is linked to a matter, and no root-owned matter is linked to a project outside the root, so every family is wholly inside this set.
- The 63 user-owned matters also break team ownership (D-11); the move makes them team-owned.

## Children that follow

| Table | Rows (all in the root today) |
|---|---|
| Communication | 11 |
| Document | 9 |
| Event | 6 |
| To-do | 2 |
| Signal | 152 (of 154 root-owned) |
| Decision record | 14 (of 15 root-owned) |

No agreements, analyses, billing events, budgets, invoices, KPI assessments, memos, policies, report cards or spend rows hang off this set. (The MAT-000113 invoice was never created: it was dead-lettered.)

## Proposed writes (only on the owner's go)

1. Assign the 149 roots to the BU1 default team `cf15f587-baa0-f111-aaac-000d3a99d1d7`; read each back (owner = BU1 team, business unit = BU1).
2. Let the reconciler carry the children (about 8 minutes on 11-02). Read back the 28 children, 152 Signals and 14 Decision Records; explicitly assign any still in the root to the same team.
3. ebilling-r1 resubmits the dead-lettered MAT-000113 invoice (their action).

Rollback: the current owner of every root is captured (id and type) in the session scratch `dr1-roots.csv`; re-assign from it.

## Applied (owner "Yes, go", 2026-11-24)

- **Roots:** a fresh check matched the dry run (149, none new). All 149 assigned to the BU1 default team between 15:43:22Z and 15:44:35Z and read back (owner = BU1 team, BU = BU1). No failures.
- **Children:** the reconciler carried 193 of 194 by 16:09Z (Signals were last). The one row left was a to-do owned directly by a user in the root (the reconciler does not move user-owned rows); assigned by hand and read back. Final read-back 16:10Z: 11 communications, 9 documents, 6 events, 2 to-dos, 152 Signals and 14 Decision Records, all in BU1; none in the root.
- BFF `/healthz` 200 afterwards.
- **Already recurring:** a field-mapping-r1 live-check work assignment created at 15:51Z with no parent (FMSWP-026) is root-owned, the #3047 shape. Not in the approved set; left alone.
- Logs (session scratch): `rh1-log.csv` (roots, with previous owners), `rh2-log.csv` (the hand-assigned to-do).

## Not covered

- The administrator's account stays in the root (owner decision 2026-11-02), so records it creates unfiled still land in the root until #3047 is fixed.
- 2 Signals and 1 Decision Record in the root are not stamped to this set; they stay.
