# Task 236 — one-off finance roll-up recalculation (D-235)

**Run:** 2026-11-27, main session, dev BFF 5cd40984b0 (carries task 232, fae8f453d6; uac-r2 30-minute gate passed 13:45Z).

**Read-only listing (Dataverse Web API, read as the operator):**
- 676 open Signals across 7 policies: Submission integrity (system), Open task past its due date, Open task coming due, Work assignment past its response date, To Do past its due date, Matter created more than five days ago with no active budget, Classified fee or scope change with no budget revision since.
- 10 policy versions scanned; none names a derived finance column (`FinanceBaseValues.IsDerived`: total spend to date, budget utilization, remaining budget, monthly spend, total budget, average invoice, pending spend). No spend, budget or utilization Threshold policy exists on dev, so `SignalReevaluationJob.RollupSubjectOf` is null for every open Signal.

**Result:** subjects listed 0; recalculated 0; unchanged 0; failed 0. No recalculate call was needed and none was made. Nothing was written.

**Follow-on:** when a spend, budget or utilization Threshold policy is authored, task 232 recalculates each subject on its first raise; there are no pre-232 open Signals of that kind to repair.
