# Task 046 notes: acme fault, closed with no fix needed (FR-31 a, b; #2578)

**Status:** completed 2026-11-28, no live read and no data change.

## Owner decision

2026-11-28: "If they aren't needed then close them; if they are needed then leave open and fix."

The evidence says they are not needed, so task 046 and #2578 close.

## Why no fix is needed

- The fault was the dev BFF reconciling acme's registry row. That row names acme's own BFF (`sprk_appservicename` is set and differs from the dev BFF's site name).
- uac-r2's #2791 (`f23dfed281`, fixing #2761) makes the reconciliation skip rows served by another BFF (`IsServedByThisBff` in `IdentityLinkReconciliationJob.cs`). It reached dev on 2026-11-13.
- provisioning-r1's read-only App Insights query over 90 days (`--offset 90d`):
  - 0x80072560 faults ran from 2026-10-14 21:10Z to 2026-11-13 20:35Z (40,910 rows);
  - the first "served by another BFF" skip came at 20:40Z that day;
  - no acme fault since.
- The registry row's App Service name predates the 2026-11-06 audit, so this was not a stale row that later got fixed.
- So the checks A–C and the fix options (application user, or deactivating the row) answer a question that no longer arises. acme has its own BFF, which reconciles its own environment.

## Deviations from the prescriptive steps

- Steps 2–6 (owner-approved live reads, fix options, the data change, the 30-minute verification) did not run. The owner chose to close the task because the fault is already gone. The prescriptive mode makes a deviation an escalation; the owner's decision above is that escalation's answer.
- Acceptance criteria 1–3 and 6 (the record of checks, the fix, telling uac-r2, UAC's review of the fix) are void, as there was no check or fix. Criterion 4 is met by the evidence above: 0 acme faults since 2026-11-13. Criterion 5 holds: no read or change ran in this task.
- `notes/task-046-acme-record.md` was not created, since nothing was read or changed.
- Step 8 closes #2578 "only when 047's acceptance is also met". 047 is merged (`4f9e371445`, PR #3311) and goes to dev with uac-r2's next deploy. With acme skipped there is no foreign-environment fault left for its Warning branch to log on dev, so #2578 closes now.
- #2163 is closed as #2578's duplicate (its last comment says so).

## Not changed

- acme's `sprk_setupstatus` stays as it is; provisioning-r1's L2 and provisioning skill read it.
- No cooldown or skip-list was added.

## Out of scope, FYI

- provisioning-r1 saw a dev path meet an acme SPE container at 03:45Z on 2026-11-28; the ownership guard refused it, as intended. Not this project's; provisioning-r1 holds it.

rework: none task 046
