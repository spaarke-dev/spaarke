# Pilot: issue triage on uac-r2 (2026-10-15)

> Pilot for `notes/proposal-cross-session-coordination.md` §10. **Recommendations only.** No issue was commented on, closed or relabelled. The owner decides on Board #2.

## What was done

1. **Board #2 fields added:**
   - Finding Class (F1–F4)
   - Fix Cost (Small/Medium/Large)
   - Triage Recommendation
   - Triage Decision (Do now/Next/Later/Won't do), which the owner fills
   - Code Area
   - Triage Reason
   - Owning Project
2. **Input set:** every open issue labelled `unified-access-control-r2` or mentioning `uac-r2` / `unified-access-control-r2`.
   - 235 collected.
   - 27 were already closed: a query error on my side, so they were dropped.
   - **208 triaged.**
3. **Triage:** four Sonnet agents (medium effort), about 59 issues each. They used the standard prompt (proposal §5.6.1), with at most two "is it already fixed?" checks per issue. About 8 minutes of wall-clock time and about 470k subagent tokens in all.
4. **Board:** all 208 added to Board #2 with Class, Cost, Recommendation, Code Area, Reason and Owning Project filled. The Decision field is empty.

## Results (208 open issues)

| Recommendation | Count | What the owner does |
|---|---|---|
| Other project | 64 | Confirm the owner, then they leave uac-r2's queue. 18 are ontology's; 19 have an unclear owner. |
| Close | 50 | Bulk-approve. Each needs its evidence checked before it is closed. |
| Ask owner | 33 | Decide one by one. |
| Record vs files | 20 | Bulk "Later": tie them to their files. |
| Fix now (tell) | 16 | Bulk "Do now", or "Later" given wrap-up. |
| Fix now | 15 | Check first: see the accuracy notes below. |
| Note | 10 | Bulk "Won't do" or add to a small-fixes list. |

## Accuracy: what a spot-check found

I checked the 15 "Fix now" items against uac-r2's own records:

- **#1755** (orphan container on a client disconnect) is recorded in `notes/batch5-live-gates-2026-10-08.md` as "not uac-r2-impacting" under the round 90 rule. The agent could not see that ruling.
- **#1750–#1754** (G1–G5, the external-route and secure-wall items from owner round 94) are referenced by the secure-family design in `uac-secure-project-r3`. They are not wrap-up fixes for uac-r2. The owner should confirm which project holds them.
- **#1059** is task 136's tracking issue. Task 136 is gate-only and waits on the combined CIAM session (checklist §14), so it is in flight rather than a finding.

The conclusion is that a triage pass sees the issue and the code, but not the owner rulings recorded in project notes. Recommendations were reasonable from the issue text and wrong wherever a ruling existed only in notes.

## Lessons for the formal process

| # | Lesson | Change |
|---|---|---|
| L1 | Owner rulings recorded in notes never reached the issue, so triage could not see them | When an owner ruling or a "not impacting" call concerns an issue, it is written **on the issue** as a comment and the Triage Decision field is set. This extends the two-write rule from findings to decisions (`project-defer-issue-tracking`). |
| L2 | 55 of the 208 are task tracking issues that `TASK-INDEX.md` references; 32 of those are "Close" because their task is done | `task-execute` Step 10 closes the task's tracking issue when the task completes. Triage skips task tracking issues: they are tasks, not findings. |
| L3 | 64 of 208 belong to other projects; they only mention uac-r2 | Pilot input should be "issues labelled for the project", not "issues mentioning it". The project label (now created by `/project-pipeline` Step 4.2) makes that possible. Only 54 of the 208 carried the uac-r2 label. |
| L4 | 19 issues have no identifiable owner | Every filed issue names its owning project when filed: label plus Owning Project field. |
| L5 | Closes rest on one or two checks | A recommendation is not evidence. Before closing, the session applying the decision checks the evidence: the SHA is on master, or the issue is a duplicate. |
| L6 | GitHub's API cannot create Project views | The four views (Triage inbox, By project, Ageing, Do now) are made once by hand in the UI. |
| L7 | Linking issues as sub-issues of #808 was skipped | An issue can have only one parent, and many already have one. **Owning Project** (text) replaces the parent link for triage. |

## Owner review: suggested order

1. **Close (50):** bulk-approve. I verify each one's evidence before closing.
2. **Other project (64):** confirm the owners. I hand them off with one comment each, and the 19 with an unclear owner come back to you as a list.
3. **Fix now (15) and Fix now (tell) (16):** given round 90, each is either a uac-r2-impacting fix or "Later". This is the real decision.
4. **Ask owner (33):** decide individually.
5. **Record vs files (20) and Note (10):** bulk "Later" or "Won't do".
