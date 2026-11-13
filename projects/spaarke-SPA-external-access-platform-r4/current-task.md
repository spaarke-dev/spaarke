# Current Task State

> **Last Updated**: 2026-11-13
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Post-E2E rework (new tasks 008–010, not yet written) → then 018 redeploy + owner retest |
| **Step** | Planning: code map done; writing POMLs 008/009/010 next |
| **Status** | 001–007 ✅, PR #2681 merged (`c53452bb0c`). Dev SPA deployed (run 43324493710). E2E parts 1+2 partly done; **owner stopped the E2E 2026-11-13: "proceed with whatever work items and we will test after deployed"** |
| **Next Action** | Write `tasks/008-*.poml`, `009-*.poml`, `010-*.poml` + TASK-INDEX rows; send uac-r2 the 008 BFF proposal (UAC review BEFORE BFF code) and ebilling-r1 the 010 wizard contract (review BEFORE code); then execute 008 → 009 → 010 via task-execute; new PR; uac-r2 BFF deploy; SPA deploy; owner retest |

## Rework scope (owner directives 2026-11-12/13, project CLAUDE.md §3; evidence `notes/task-018-notes.md`)

**008 BFF (opus/high, UAC review of proposal first):**
- External event create accepts a type (closed set) and binds `sprk_eventtype_ref@odata.bind` → `/sprk_eventtype_refs({id})` (nav prop `sprk_EventType_Ref`; see `Spaarke.Dataverse/DataverseWebApiService.cs:615`). Task type id already in code: `DecisionExec.TaskEventTypeId = "124f5fc9-98ff-f011-8406-7c1e525abd8b"` (`Services/Signals/Actions/DecisionActionExecutors.cs:204`) — reuse, don't hard-code a second copy. Today `CreateExternalEventRequest` (`ExternalProjectDtos.cs:468`) has name/duedate/status only; `BuildEventCreatePayload` `ExternalDataService.cs:3383`. Event list DTO (`ExternalProjectDtos.cs:415`) needs the type (allow-list column `_sprk_eventtype_ref_value` → UAC review, FR-18). Resolves D-6 #2703.
- `/me` (`ExternalUserContextEndpoint.cs:75`) carries levels for projects only; add matter/WA levels (source: `AccessibleRecordSetService.cs` composed grant map, ~:718) so the SPA shows "View Only/Collaborate/Full Access" everywhere.

**009 SPA record view (opus/high, after 008):**
- Create actions move into their sections via `RecordSectionCard.actions` (`HeaderToolbar` iconSlots, unused today): Upload → Documents, New message → Messages, Add event → Tasks & events, Submit invoice → Invoices. Header keeps Refresh, Copy link. Config: `recordTemplates.ts:8` toolbar arrays; build `RecordTemplateModal.tsx:439-507`.
- Remove To Do (`sprk_todo`) from the SPA: `SmartTodo.tsx`, `TasksEventsSection.tsx:25,94-101`, `ProjectPage.tsx:259-275`, `addTodo` in recordAccess/types/createAction/templates, mocks, ~13 tests. Tasks list = events of type Task. To-do BFF routes stay (file a retirement issue).
- EventsCalendar type select (`:386,:506-518`) is sent (D-6).
- Labels: drop "Can contribute" (`recordAccess.ts:96,:338`); use the level from `/me` for matter/WA.
- Empty-state texts (no "in the toolbar", no "Add to-do").
- D-12 #2769 Documents columns overlap: `DocumentLibrary.tsx:897-1037,1165` has no column sizing.

**010 Submit invoice modal + grid toolbar (sonnet/high, ebilling review first):**
- Wizard `InvoiceSubmissionWizard.tsx:123-152` has `open`, `embedded`, `initialRecord`; needs a locked-record / skip-record-step prop. R4 hosts it in a child SprkModal over the record (same size/position) instead of navigating (`SubmitInvoiceAction.tsx:88-123`); removes #2658 risk.
- Invoices grid: `InvoicesWidget.tsx:55-72` own button → grid toolbar; shared `DataGrid` (`DataGridCommandBar`, `DataGrid.tsx:1484`) has no custom-action prop → dataset-grid framework owners agree.

## E2E record so far (task 018)
- Part 1 write partner: all PASS (project/matter/WA, upload, messages both ways, add event, Submit invoice end to end, Copy link, Refresh).
- Part 2: org inactivation PASS; View Only PASS; dark mode PASS; re-grant "no download" = legacy rows without SPE pointers (#2786 → uac-r2 backfill question; inline level change = enhancement there).
- Not done: Invoice/Document/Service Request open, bell, no-firm, Teams, FR-33(1) invite 409. Open check: Submit invoice icon missing on matter CMRCL-788888 (firm-records answer; not diagnosed — retest after rework).
- Issues: #2769 (D-12), #2770 (ebilling DEF-067), #2778 (internal email timeout text), #2786.

## Live state
- Dev BFF `b100effac3` (uac-r2, master `c2f366bf52` + spike), contains R4. Dev SPA = master `2ef4a17915` build. SPA rollback `b083d62183` (temp tag).
- Tell uac-r2 when the E2E is done (they re-read external routes). Never deploy the BFF directly.

## Open owner questions
- Backfill of the 5 legacy document rows (#2786) — uac-r2 asks the owner.
- D-1 #2686, D-2 #2687, D-4 #2693 (Copy link `?customer=`), post-merge look at ebilling #2689 SPA changes.

## Uncommitted
- none (last push `dec8adaa33`).
