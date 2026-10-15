# Task 065 — `sprk_finalduedate` readers (folded into task 098, PR #1359)

> **Date**: 2026-10-07 · **Decision**: D-27 (spec FR-61). The Do lane, Reschedule and the Daily Briefing use `sprk_duedate` **always**. `sprk_finalduedate` is informational.

## Changed in #1359

All changes are in `DailyBriefingCollector` (`src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs`).

| Where | Before | After |
|---|---|---|
| High Priority spec for `sprk_event` | `DueDateColumn: "sprk_finalduedate"`, falling back to `sprk_duedate` | `sprk_duedate` only, with **no** fallback. An event that has only a final due date has no due date here. |
| Upcoming Tasks filter | `NextXDays(sprk_duedate) or NextXDays(sprk_finalduedate)` | `NextXDays(sprk_duedate)` |
| Overdue Tasks filter | `OnOrBefore(sprk_duedate, cutoff) or OnOrBefore(sprk_finalduedate, cutoff)` | `OnOrBefore(sprk_duedate, cutoff)` |
| Order of task channels | `sprk_finalduedate asc, sprk_duedate asc`, then in memory: final due, then due | `sprk_duedate asc, sprk_eventid asc`, then in memory: `sprk_duedate` |
| Displayed due date (`BriefingItem.DueDate`) | final due, else due | `sprk_duedate` |
| `$select` of task events | included `sprk_finalduedate` | not read |

Task 098's user-local "today" is unchanged.

Comments that said "the collector reads `sprk_finalduedate` first" were corrected. Only comments changed in these files:
- `IActionSeam.cs` (`FinalDueDate` remarks)
- `TaskActionCore.cs:178`
- `CommunicationRiActionService.cs:196`
- `CommsPolicyOptions.cs:54`

**Tests** (`DailyBriefingCollectorTests`):
- `CollectHighPriorityAsync_ClassifiesAndOrdersTaskEventsBySprkDuedate_NotTheFinalDueDate`
- `CollectAsync_TaskChannels_SelectOrderAndShowBySprkDuedate_NotTheFinalDueDate`. Its fake evaluates the NextXDays/OnOrBefore clauses the way Dataverse does.

Both **fail on the previous collector**. The existing High Priority fixtures moved from `sprk_finalduedate` to `sprk_duedate`.

**Live** (`EventRoutesLiveTests.LiveMode_DailyBriefing_JudgesTasksBySprkDuedate_NotTheFinalDueDate`, spaarkedev1, 2026-10-07). The test writes two real Open Task events whose two dates differ, and reads them back. The production collector then reads them as the operator through the production impersonated query.
- **With this PR:** the event due 09-29 (final 10-09) is in Overdue; the rescheduled event due 10-09 (final 09-29) is in Upcoming. High Priority shows Overdue and DueSoon. Cleanup left 0 records.
- **With the previous collector:** Overdue was empty, and both events were in Upcoming. The test fails.

## Not changed: other readers and writers of `sprk_finalduedate`

| File:line | What it does | Note |
|---|---|---|
| `src/client/pcf/VisualHost/control/utils/eventDueDate.ts:46-68` (`selectActiveDueDate`) | The VisualHost due-date card shows `sprk_duedate` if it is today or later. Once that has passed, it shows `sprk_finalduedate` if that is today or later. | **Lets the final due date decide** the card's date after `sprk_duedate` passes. It is a VisualHost card, not the Briefing or the Do lane. |
| `src/client/pcf/VisualHost/control/components/CalendarVisual.tsx:64-65` | Calendar bucketing date: the configured field, else `sprk_finalduedate`, else `sprk_duedate`. | **Final due first** when no field is configured. |
| `src/client/shared/Spaarke.Communication.Components/src/components/ReconcileTabs/TaskReconcileTab.tsx:105-243, 563-566` | Edits the field on the reconcile form. | Write, not a reader. |
| `src/client/shared/Spaarke.Events.Components/src/components/CalendarFilterPane/CalendarFilterPane.tsx:158` | "Final Due Date" is one of the date-field filter options. | User's choice. |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/workAssignmentService.ts:622-623` (+ `CreateFollowOnEventStep.tsx:112-199`, `formTypes.ts:98,113`) | The wizard writes the field. | Write. |
| `src/solutions/EventDetailSidePane/src/config/eventTypeConfigs.ts:49,124`; `services/eventService.ts:262`; `types/EventRecord.ts:25,184` | The side pane shows and edits the field. | Display/edit. |
| `src/server/api/Sprk.Bff.Api/Api/Events/Dtos/EventDto.cs:74-76`; `EventEndpoints.cs:755`; `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs:394,2197`; `Models.cs:896-897` | The event API returns the field. | Read model only. |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/ActionCore/TaskActionCore.cs:180-181`; `PublicContracts/ActionSeam.cs:110`; `IActionSeam.cs:137` | Task creation writes the field. | Write. |
| `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationCreateTaskApplyService.cs:191,516,675,686` | Create-task apply writes the field. | Write. |
| `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationRiActionService.cs:221`; `CommunicationRuleGate.cs:37`; `Configuration/CommsPolicyOptions.cs:62` | The RI task's final-due-days setting, and its write. | Write/config. |
| **Live Dataverse playbook node "Query Overdue Tasks"** `348b6395-a171-f111-ab0d-7ced8ddc4a05` (repo copy: `projects/spaarke-daily-update-service/notes/playbooks/notification-tasks-overdue.json`) | FetchXML: `sprk_duedate lt {{todayUtc}} OR sprk_finalduedate lt {{todayUtc}}`, ordered by `sprk_finalduedate` first. | **Still lets the final due date decide "overdue"** for the overdue-tasks notification: a rescheduled task whose final due date has passed is notified as overdue. This is Dataverse data, outside task 098's Dataverse approval, so it is **not changed**. It needs a decision on whether D-27 covers the notification playbooks. A "Tasks Due Soon" copy (`notification-tasks-due-soon.json`) has the same OR; no live node with that FetchXML was found in spaarkedev1. |

## Deviation from the POML

- **Scope:** 065 was folded into task 098's PR #1359, on the coordinator's instruction of 2026-10-07, so the PR touches many files besides the collector. For 065 itself, the only code change is the collector. The other files listed above changed in comments only.
- **Placement and publish size:** cited in #1359. The collector change is in the BFF.
- **`/conflict-check`:** not run separately. The coordinator owns the PR sequencing.
- **TASK-INDEX.md:** left to the coordinator.
