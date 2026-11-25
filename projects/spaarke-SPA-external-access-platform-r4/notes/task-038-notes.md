# Task 038 — P3 dev deploy + E2E: notes

> Completed 2026-11-25. Full step-by-step record with evidence: `task-038-deploy-record.md`.

## Outcome

- **Live on dev at close:** SPA `0267ee64a2` (run 45552869227, owner go "Yes, deploy 0267ee64a2"; rollback SPA `3067fb3bdc`). BFF `ef0c566d2c` (deployed by uac-r2 under the owner's go in its session).
- **E2E:** uac-r2's checks (a)–(c) pass. FR-18, FR-19 (widths, sort persisted, Clear all), FR-20 (Money sort, export text), FR-22 threads, dark theme, View Only, and FR-21 "Show new messages" pass in the browser.
- **FR-23:** not applicable. Task 036 was cancelled and superseded by tasks 014/015 (owner, 2026-11-15).

## Deviations from the POML

1. **A defect fix was added mid-task.** The E2E found that the column menu's "A to Z" / "Z to A" did nothing (pre-existing since `c1c428e92b`, 2026-06-05). The owner said "Yes fix here". Fixed in the shared DataGrid with a regression test file (5 tests), PR #3113 merged on the owner's go, and the SPA was redeployed (`0267ee64a2`). Menu sort re-tested in the browser: pass.
2. **View Only and FR-21 ran with owner-approved live changes.** A temporary View Only grant on PRJ-000047 (added about 12:47Z, revoked about 12:48Z) and one test message posted to the PRJ-000046 thread (left in place, owner-approved).
3. **Two accidental clicks during the View Only setup**, both with no effect. Access ▸ Update Access ran a recompute that reported "Access is up to date". The native Dataverse Share panel was opened and closed without changes.
4. **Three checks not run** (they need owner-side changes): FR-19 with browser storage blocked, FR-22 remote image not loaded, and the high-contrast theme. The owner rated them low priority and closed the task with them filed as D-38 #3149 (urgency someday).

## Found and filed

- D-37 #3129: Export to Excel ignores the on-screen sort (shared DataGrid).
- D-38 #3149: the three un-run checks above.

## Told to the owner, not filed

- An older system email in the PRJ-000046 thread shows a raw filing-reference footer. The owner decides whether and how to track it.

## For the next SPA deploy proposal

Master has moved past `0267ee64a2`. The proposal must list what it adds, including ebilling ISS-052 #3082 (merged in #3084 at `2ac250cae4`).
