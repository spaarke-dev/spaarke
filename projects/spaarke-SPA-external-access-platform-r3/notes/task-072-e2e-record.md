# Task 072: both-plane E2E on dev

> Environment: dev BFF `9f4e3820b2` (uac-r2 deploy 2026-10-30 19:22:50Z; SecureRoot/SecureChild write switches on since 19:45/19:53Z). Dev SPA `96f1f6b675` during the owner's run, then `b083d62183` from 2026-10-31 19:12:41Z (run 41233021759; rollback tag `external-spa-dev-rollback-96f1f6b675`).

## Owner result (2026-10-31)

- Owner: "the end to end test works", with UAT feedback (below). 002 grids: "002 is good".
- The run was the owner's own walkthrough. This record does not hold per-criterion, per-persona rows. The criteria below cite the owner's pass for the live behaviour and the earlier task evidence for the negative cases, most of which the spec verifies with server tests.

| # | Criterion (spec) | Evidence |
|---|---|---|
| 1 | Detail view for a granted record; ungranted denied | Owner E2E 2026-10-31; tasks 050/057, 038 probes; negative: server tests in 050/039 |
| 2 | Quick-start wizards create a service request; CIAM refused | Owner E2E; tasks 052/053, 035 server tests |
| 3 | Grant email + in-portal feed | Owner E2E; tasks 036/037/055. **Email copy approval open** (below) |
| 4 | Grid columns match the owner's list | Owner OK 2026-10-31 (task 002) |
| 5 | Message send; outside-set send 403 | Owner E2E; tasks 034/051 server negative tests |
| 6 | Self-registration via profile form; unlisted tenant refused | Tasks 047/058/074; unlisted directory in task 071 (pass) |
| 7 | Workforce sign-in on the A9 multitenant client | Tasks 071 (8/9 runs) and 074 probes; `notes/fr-21-teams-live-check.md` |
| 8 | One SPA build, two backends | Task 041 |
| 9 | No page leaves the external plane; no production mock | Task 042 code tests; owner E2E |
| 10 | Documents upload/download on five types; ViewOnly cannot upload | Owner E2E; tasks 031/054/066 server tests |
| 11 | Delete own record; others 403 | Task 032 server negative tests |
| 12 | Partner switch off/on | Task 011 server tests |
| FR-10 | C1–C4 in Teams for workforce | Task 071 (licensed user and workforce contact, desktop and web) |

## UAT feedback

- **The record Documents list** (record modal and project page) is a separate hand-built grid. Its columns cannot be resized and they overlap (Type runs into Name). Filed for R4 as #2490 (ISS-027). The Documents tab itself uses the shared grid.

## Still open

- **Owner approval of the grant email copy** (072 criterion "owner review of grid columns and notification copy"). The draft is in `notes/task-036-notes.md` ("Email copy" and "Updated email copy"). It has three open questions: name the granter, put the record name in the subject, and state the access level.
- 2026-10-31: the owner decided to fix #2458 (ISS-024) in R3; that is separate from this test.
