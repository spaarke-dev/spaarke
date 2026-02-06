# Statecode/Statuscode Migration Plan

> **Created**: 2026-02-05
> **Updated**: 2026-02-05
> **Status**: In Progress
> **Purpose**: Document all statecode/statuscode usage and migration to custom status fields

---

## Current Session Progress

### ✅ Completed Items

| Item | Files Updated | Notes |
|------|---------------|-------|
| **Ribbon Commands JS** | `sprk_event_ribbon_commands.js` | All functions use `sprk_eventstatus`, added Close/Archive/OnHold/Resume |
| **EventRecord.ts** | `EventDetailSidePane/src/types/` | Added `EventStatus` enum, `sprk_eventstatus` field, helper functions |
| **StatusFilter.tsx** | `EventsPage/src/components/` | Updated to new values 0-7, added Closed/Reassigned/Archived |
| **GridSection.tsx** | `EventsPage/src/components/` | Updated interface, queries, badge colors, mock data |
| **Dialog HTML files** | `EventCommands/dialogs/` | Created Complete, Reschedule, Reassign dialogs |
| **DueDatesWidget** | `eventFilterService.ts`, `setupTests.ts` | Updated EventStatus (0-7), ACTIONABLE_EVENT_STATUSES, IEventData/IEventItem, query builders |
| **Calendar Side Pane** | `EventsPage/src/App.tsx` | Moved calendar to toggleable right-side panel with session storage persistence |
| **Main View Ribbon** | `EventRibbonDiffXml.xml`, `sprk_event_ribbon_commands.js` | Added HomepageGrid buttons: Complete, Close, Cancel, On Hold, Archive with bulk operations |

### 🔲 Remaining Items

| Item | Priority | Description |
|------|----------|-------------|
| **BFF API** | Low | Update server-side Event queries (DataverseWebApiService.cs, EventEndpoints.cs) |

### Note
All high-priority client-side items are complete. BFF API updates are lower priority and can be done in a future session.

### Key Technical Decisions

1. **Event Status Field**: `sprk_eventstatus` (custom optionset, values 0-7)
2. **Archive Behavior**: Sets both `sprk_eventstatus=7` AND `statecode=1` (Inactive)
3. **Reassigned Status**: Yes, it's a valid terminal status (task given to someone else)
4. **Calendar UX**: Will use right-side pane (same pattern as Event form side pane)

---

## Overview

This document catalogs all uses of OOB `statecode` and `statuscode` fields across the codebase, organized by entity. Each entity should migrate to a custom `{EntityName} Status` field for better control over status transitions.

**Migration Pattern**:
- Keep `statecode` only for Archive functionality (set to Inactive when truly hiding records)
- Use custom status field for all business logic

---

## Entity: Event (sprk_event)

**Custom Status Field**: `sprk_eventstatus`

| Value | Label | Description |
|-------|-------|-------------|
| 0 | Draft | Event created but not yet active |
| 1 | Open | Active, actionable event |
| 2 | Completed | Successfully finished |
| 3 | Closed | No action taken or required |
| 4 | On Hold | Temporarily paused |
| 5 | Cancelled | Intentionally not done |
| 6 | Reassigned | Given to someone else |
| 7 | Archived | Hidden (also sets statecode=Inactive) |

### Files to Update

| File | Location | Usage | Migration Status |
|------|----------|-------|------------------|
| `sprk_event_ribbon_commands.js` | `src/solutions/EventCommands/` | Ribbon button logic | ✅ Complete |
| `EventRecord.ts` | `src/solutions/EventDetailSidePane/src/types/` | Type definitions | 🔲 Pending |
| `eventService.ts` | `src/solutions/EventDetailSidePane/src/services/` | API calls | 🔲 Pending |
| `StatusSection.tsx` | `src/solutions/EventDetailSidePane/src/components/` | Status display | 🔲 Pending |
| `App.tsx` | `src/solutions/EventDetailSidePane/src/` | Side pane app | 🔲 Pending |
| `useEventTypeConfig.ts` | `src/solutions/EventDetailSidePane/src/hooks/` | Event type config | 🔲 Pending |
| `HistorySection.tsx` | `src/solutions/EventDetailSidePane/src/components/` | History display | 🔲 Pending |
| `EventsPageContext.tsx` | `src/solutions/EventsPage/src/context/` | Page state | 🔲 Pending |
| `StatusFilter.tsx` | `src/solutions/EventsPage/src/components/` | Status filter UI | 🔲 Pending |
| `GridSection.tsx` | `src/solutions/EventsPage/src/components/` | Grid display | 🔲 Pending |
| `RecordTypeFilter.tsx` | `src/solutions/EventsPage/src/components/` | Record type filter | 🔲 Pending |
| `App.tsx` | `src/solutions/EventsPage/src/` | Events page app | 🔲 Pending |
| `eventFilterService.ts` | `src/client/pcf/DueDatesWidget/control/services/` | Event filtering | ✅ Complete |
| `setupTests.ts` | `src/client/pcf/DueDatesWidget/control/__tests__/` | Tests | ✅ Complete |
| `EventTypeService.ts` | `src/client/shared/Spaarke.UI.Components/src/services/` | Shared service | 🔲 Pending |
| `EventTypeService.test.ts` | `src/client/shared/Spaarke.UI.Components/src/services/__tests__/` | Tests | 🔲 Pending |

### BFF API Files

| File | Location | Usage | Migration Status |
|------|----------|-------|------------------|
| `DataverseWebApiService.cs` | `src/server/shared/Spaarke.Dataverse/` | Event queries | 🔲 Pending |
| `EventEndpoints.cs` | `src/server/api/Sprk.Bff.Api/Api/Events/` | Event API | 🔲 Pending |
| `EventDto.cs` | `src/server/api/Sprk.Bff.Api/Api/Events/Dtos/` | DTOs | 🔲 Pending |
| `UpdateEventRequest.cs` | `src/server/api/Sprk.Bff.Api/Api/Events/Dtos/` | Update request | 🔲 Pending |
| `EventListResponse.cs` | `src/server/api/Sprk.Bff.Api/Api/Events/Dtos/` | List response | 🔲 Pending |

---

## Entity: Container (sprk_container)

**Custom Status Field**: `sprk_containerstatus` (TBD)

### Files Using statecode

| File | Location | Usage |
|------|----------|-------|
| `Entity.xml` | `src/dataverse/solutions/spaarke_containers/Entities/sprk_Container/` | Entity definition |
| `SavedQueries/*.xml` | `src/dataverse/solutions/spaarke_containers/Entities/sprk_Container/SavedQueries/` | Views filter on statecode |
| `DocumentStorageResolver.cs` | `src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/` | Container lookup |

**Note**: Container uses statecode for active/inactive filtering in views. May not need custom status field - evaluate business need.

---

## Entity: Email/Job Entities

**Entities**: `sprk_emailjob`, `sprk_outboundemailqueue`, etc.

### Files Using statecode/statuscode

| File | Location | Usage |
|------|----------|-------|
| `JobStatusService.cs` | `src/server/api/Sprk.Bff.Api/Services/Office/` | Job status tracking |
| `EmailPollingBackupService.cs` | `src/server/api/Sprk.Bff.Api/Services/Jobs/` | Email polling |
| `EmailToDocumentJobHandler.cs` | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/` | Job handling |
| `BatchProcessEmailsJobHandler.cs` | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/` | Batch processing |
| `BulkRagIndexingJobHandler.cs` | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/` | RAG indexing |
| `EmailRuleSeedService.cs` | `src/server/api/Sprk.Bff.Api/Services/Email/` | Email rules |
| `EmailAssociationService.cs` | `src/server/api/Sprk.Bff.Api/Services/Email/` | Email association |
| `EmailFilterService.cs` | `src/server/api/Sprk.Bff.Api/Services/Email/` | Email filtering |

**Recommendation**: Jobs/emails typically have well-defined lifecycle states. Evaluate if custom status fields improve workflow visibility.

---

## Entity: Playbook (sprk_playbook)

### Files Using statecode/statuscode

| File | Location | Usage |
|------|----------|-------|
| `PlaybookService.cs` | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Playbook CRUD |
| `PlaybookSharingService.cs` | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Sharing logic |
| `PlaybookOrchestrationService.cs` | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Orchestration |
| `PlaybookEndpoints.cs` | `src/server/api/Sprk.Bff.Api/Api/Ai/` | API endpoints |
| `PlaybookRunEndpoints.cs` | `src/server/api/Sprk.Bff.Api/Api/Ai/` | Run endpoints |
| `AiBuilderErrors.cs` | `src/server/api/Sprk.Bff.Api/Services/Ai/Builder/` | Error handling |

---

## Entity: Node (AI Nodes)

### Files Using statecode/statuscode

| File | Location | Usage |
|------|----------|-------|
| `NodeService.cs` | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Node operations |
| `NodeEndpoints.cs` | `src/server/api/Sprk.Bff.Api/Api/Ai/` | Node API |
| `SendEmailNodeExecutor.cs` | `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/` | Email node execution |

---

## Shared Infrastructure (Generic Usage)

These files use statecode/statuscode generically for any entity:

| File | Location | Usage |
|------|----------|-------|
| `DataverseWebApiService.cs` | `src/server/shared/Spaarke.Dataverse/` | Generic query building |
| `DataverseWebApiClient.cs` | `src/server/shared/Spaarke.Dataverse/` | API client |
| `DataverseServiceClientImpl.cs` | `src/server/shared/Spaarke.Dataverse/` | Service client |
| `DataverseAccessDataSource.cs` | `src/server/shared/Spaarke.Dataverse/` | Data source |
| `IDataverseService.cs` | `src/server/shared/Spaarke.Dataverse/` | Interface |
| `Models.cs` | `src/server/shared/Spaarke.Dataverse/` | Model classes |
| `OwnershipValidator.cs` | `src/server/api/Sprk.Bff.Api/Services/Scopes/` | Ownership validation |
| `ScopeResolverService.cs` | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Scope resolution |

**Note**: Generic infrastructure should continue to support both statecode and custom status fields.

---

## PCF Controls (Generic Usage)

| File | Location | Usage |
|------|----------|-------|
| `DatasetGrid.test.tsx` | `src/client/pcf/UniversalDatasetGrid/control/components/__tests__/` | Test data |
| `rowUpdateHandler.test.ts` | `src/client/pcf/UniversalDatasetGrid/control/utils/__tests__/` | Test data |
| `types/index.ts` | `src/client/pcf/UniversalDatasetGrid/control/types/` | Type definitions |
| `DataverseMetadataService.ts` | `src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/` | Metadata |
| `FieldMappingHandler.ts` | `src/client/pcf/AssociationResolver/handlers/` | Field mapping |
| `RecordSelectionHandler.ts` | `src/client/pcf/AssociationResolver/handlers/` | Record selection |
| `AnalysisWorkspaceApp.tsx` | `src/client/pcf/AnalysisWorkspace/control/components/` | Analysis workspace |
| `AnalysisBuilderApp.tsx` | `src/client/pcf/AnalysisBuilder/control/components/` | Analysis builder |

---

## Legacy Web Resources

| File | Location | Usage |
|------|----------|-------|
| `sprk_emailactions.js` | `src/client/webresources/js/` | Email actions |
| `sprk_DocumentOperations.js` | `src/client/webresources/js/` | Document operations |

---

## Migration Priority

### Phase 1: Event Entity (Current Focus)
1. ✅ `sprk_event_ribbon_commands.js` - Complete
2. 🔲 `EventDetailSidePane` - All files
3. 🔲 `EventsPage` - All files
4. 🔲 `DueDatesWidget` - Filter service
5. 🔲 `EventTypeService` - Shared service
6. 🔲 BFF API Event endpoints

### Phase 2: Evaluate Other Entities
- Container: Likely keep statecode only
- Email/Job: Consider job status field
- Playbook: Consider playbook status field
- Node: Consider node status field

### Phase 3: Update Generic Infrastructure
- Ensure DataverseWebApiService supports both statecode and custom status
- Update filters and query builders

---

*Migration plan created: 2026-02-05*
