# Follow-up: `RouteAuthorizationGuardTests.Ledger.cs` after ontology #1905 (D-141) merges

UAC review of #1905 at `e5fb90b09`: CLEAN (PR comment 6194460481). The guard passes, so this is a ledger-accuracy update, not a fix. Do it after #1905 merges, against the merged master. Re-verify every line number there; these were taken at `e5fb90b09`.

## Entries that need a "mine" step note
- `:267-279`: DecisionPlanEndpoints file entry and commit file entry. "Before anything else (even the review-id lookup)" is now true only of the readability half. "Mine" runs after the ledger peek (DecisionCommitService `:477`), is skipped when the same caller resends the same content, and runs again before a resume (`:641`) or repair (`:755`) unless that review's own reassign landed.
- `:1780-1786` plan handler; `:1787-1798` commit handler; `:1799-1806` suppression handler (its 503 now also covers a "mine" fault).

## Drifted line references (cited → actual at e5fb90b09)
- **DecisionPlanEndpoints.cs:**
  - plan: :147→167, :148-150→169-171, :153-155→174-176, :160→179-181
  - suppression: :70→74, :71-73→76-78, :76-78→81-83, :83→86-88
- **SignalCoreRecordAccess.cs:** :114→143, :166→195, :224/:229→260/265. New: `ConfirmDoLaneMineAsync` :293, caller :310, "mine" read :319.
- **DecisionCommitService.cs:** :434→457, :435-436→458-459, :437-438→460-461, :439-440→462-463, :856/:1275→1429/1452, :950→1065. :671/:697 moved into `ReplayRecordedAsync` (:745 on). New: :477, :564, :617, :641, :755.
- **SignalReadService.cs** (worklist entry `:1807-1813`): :240 is now `ResolveMineCallerAsync` (WhoAmI moved to :777); :510→484, :671→645, :726→698-699.

Related: #1921 (Do-lane suppression keyed (policy, core); owner decision through ontology).

## Also after ontology #1946 (D-145a) merges (UAC review at 06c5dd473)
- **Suppression handler entry (`:1799-1806`).** For a Do item, the count is the item's person's own (D-145a). `DecisionPlanEndpoints.cs:98` passes `decision.CallerContactId`, set at `SignalCoreRecordAccess.cs:356`. A "mine" caller who isn't the person gets a 200 empty status, not a 404.
- **Line moves at 06c5dd473 vs the e5fb90b09 values above:**
  - `DecisionPlanEndpoints.cs` +1. Suppression: 75, 78, 83, 88. Plan: 169, 172, 177, 182.
  - `SignalCoreRecordAccess.cs` +3. Reads 146, 198, 263/268; `ConfirmDoLaneMineAsync` 296, its caller 313, the "mine" read 322-323; new line 356.
- **Take the final numbers from merged master, not from these notes.**
