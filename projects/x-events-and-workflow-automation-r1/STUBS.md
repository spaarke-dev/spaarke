# Stubs Index: Events and Workflow Automation R1

> **Last Updated**: 2026-02-01
> **Project**: events-and-workflow-automation-r1
> **Purpose**: Track implementation stubs that need to be resolved when Dataverse entities are deployed

---

## What Are Stubs?

Stubs are placeholder implementations that:
- Return empty/mock data
- Document the expected behavior
- Will be resolved when dependencies are available (e.g., Dataverse entities deployed)

**Stub Format**: `S{TaskId}-{Sequence}: {Description}`

---

## Active Stubs

### API Stubs

| Stub ID | Task | Description | Resolution Trigger |
|---------|------|-------------|-------------------|
| S013-01 | 013 | Query sprk_fieldmappingprofile entity for GET /profiles | sprk_fieldmappingprofile deployed |
| S014-01 | 014 | Query sprk_fieldmappingprofile by source/target entities | sprk_fieldmappingprofile deployed |
| S050-01 | 050 | Query sprk_event entity from Dataverse | sprk_event entity deployed |
| S051-01 | 051 | Create sprk_event record in Dataverse | sprk_event entity deployed |
| S051-02 | 051 | Update sprk_event record in Dataverse | sprk_event entity deployed |
| S052-01 | 052 | Update sprk_event statuscode to Canceled (soft delete) | sprk_event entity deployed |
| S053-01 | 053 | Update sprk_event statuscode to Completed | sprk_event entity deployed |
| S053-02 | 053 | Update sprk_event statuscode to Cancelled | sprk_event entity deployed |
| S054-01 | 054 | Retrieve source record field values for push endpoint | IDataverseService.RetrieveAsync implemented |
| S054-02 | 054 | Query child records and update records for push endpoint | IDataverseService query/update methods |
| S055-01 | 055 | Create/Query sprk_eventlog records in Dataverse | sprk_eventlog entity deployed |

### PCF Stubs

| Stub ID | Task | Description | Resolution Trigger |
|---------|------|-------------|-------------------|
| S021-01 | 021 | ENTITY_LOOKUP_CONFIGS hardcoded | Move to configuration |

---

## Stub Details

### S050-01: Query sprk_event entity from Dataverse

**Task**: 050 - Create Event API - GET endpoints

**Location**: `src/server/api/Sprk.Bff.Api/Api/Events/EventEndpoints.cs`

**Current Behavior**:
- `QueryEventsAsync()` returns empty array and count 0
- `GetEventByIdFromDataverseAsync()` returns null (not found)

**Expected Implementation**:
```csharp
// FetchXML for list endpoint with dynamic filters
<fetch count="{pageSize}" page="{pageNumber}" returntotalrecordcount="true">
  <entity name="sprk_event">
    <attribute name="sprk_eventid" />
    <attribute name="sprk_eventname" />
    <attribute name="sprk_description" />
    <attribute name="sprk_regardingrecordid" />
    <attribute name="sprk_regardingrecordname" />
    <attribute name="sprk_regardingrecordtype" />
    <attribute name="sprk_basedate" />
    <attribute name="sprk_duedate" />
    <attribute name="sprk_completeddate" />
    <attribute name="statecode" />
    <attribute name="statuscode" />
    <attribute name="sprk_priority" />
    <attribute name="sprk_source" />
    <attribute name="createdon" />
    <attribute name="modifiedon" />
    <filter type="and">
      <!-- Dynamic filters based on parameters -->
    </filter>
    <order attribute="sprk_duedate" />
    <order attribute="createdon" descending="true" />
    <link-entity name="sprk_eventtype" from="sprk_eventtypeid" to="sprk_eventtype_ref" link-type="outer" alias="eventtype">
      <attribute name="sprk_name" />
    </link-entity>
  </entity>
</fetch>
```

**Resolution Criteria**:
1. `sprk_event` entity deployed to Dataverse
2. `sprk_eventtype` entity deployed with relationship
3. IDataverseService supports FetchXML queries
4. Unit tests pass with real Dataverse connection

---

### S051-01: Create sprk_event record in Dataverse

**Task**: 051 - Create Event API - POST/PUT endpoints

**Location**: `src/server/api/Sprk.Bff.Api/Api/Events/EventEndpoints.cs`

**Current Behavior**:
- `CreateEventInDataverseAsync()` returns a mock GUID and timestamp
- Does not actually create a record in Dataverse

**Expected Implementation**:
```csharp
// OData POST to create event record
// POST /api/data/v9.2/sprk_events
// Content-Type: application/json
// {
//     "sprk_eventname": "Subject value",
//     "sprk_description": "Description value",
//     "sprk_eventtype_ref@odata.bind": "/sprk_eventtypes(guid)",
//     "sprk_regardingrecordid": "guid-as-string",
//     "sprk_regardingrecordname": "Record display name",
//     "sprk_regardingrecordtype": 0,
//     "sprk_duedate": "2026-02-15T00:00:00Z",
//     "sprk_priority": 1,
//     "sprk_source": 0,
//     "statuscode": 3
// }

// Create steps:
// 1. Build entity object with all provided fields
// 2. Set sprk_source to User (0) for API-created events
// 3. Set initial statuscode to Open (3)
// 4. Create record via IDataverseService
// 5. Create Event Log entry: action="created", previousStatus=null, newStatus="open"
// 6. Return created record ID and timestamp
```

**Resolution Criteria**:
1. `sprk_event` entity deployed to Dataverse
2. IDataverseService supports create operations
3. Event Log creation integrated (task 055)
4. Unit tests pass with real Dataverse connection

---

### S051-02: Update sprk_event record in Dataverse

**Task**: 051 - Create Event API - POST/PUT endpoints

**Location**: `src/server/api/Sprk.Bff.Api/Api/Events/EventEndpoints.cs`

**Current Behavior**:
- `UpdateEventInDataverseAsync()` is a no-op (returns `Task.CompletedTask`)
- Endpoint checks if event exists first (reuses S050-01 stub)

**Expected Implementation**:
```csharp
// OData PATCH to update event record
// PATCH /api/data/v9.2/sprk_events({id})
// Content-Type: application/json
// {
//     "sprk_eventname": "Updated subject",
//     "sprk_priority": 2
// }

// Update steps:
// 1. Build update payload with only non-null fields from request
// 2. Update record via IDataverseService
// 3. If statuscode changed, create Event Log entry for state transition
// 4. Return updated record
```

**Resolution Criteria**:
1. `sprk_event` entity deployed to Dataverse
2. IDataverseService supports update operations
3. Event Log creation integrated for status changes (task 055)
4. Unit tests pass with real Dataverse connection

---

### S052-01: Update sprk_event statuscode to Canceled (soft delete)

**Task**: 052 - Create Event API - DELETE endpoint

**Location**: `src/server/api/Sprk.Bff.Api/Api/Events/EventEndpoints.cs`

**Current Behavior**:
- `SoftDeleteEventAsync()` is a no-op (returns `Task.CompletedTask`)
- Endpoint checks if event exists first (reuses S050-01 stub)

**Expected Implementation**:
```csharp
// OData PATCH to update statuscode
// PATCH /api/data/v9.2/sprk_events({id})
// Content-Type: application/json
// {
//     "statuscode": 3  // Canceled
// }

// Soft delete steps:
// 1. Update statuscode to 3 (Canceled) - soft delete marker
// 2. Optionally set statecode to 1 (Inactive) if required by Dataverse status transitions
// 3. Create Event Log entry for "deleted" transition (implemented in task 055)
```

**Resolution Criteria**:
1. `sprk_event` entity deployed to Dataverse
2. IDataverseService supports update operations
3. Event Log creation integrated (task 055)
4. Unit tests pass with real Dataverse connection

---

### S053-01: Update sprk_event statuscode to Completed

**Task**: 053 - Create Event API - complete/cancel actions

**Location**: `src/server/api/Sprk.Bff.Api/Api/Events/EventEndpoints.cs`

**Current Behavior**:
- `UpdateEventStatusAsync()` is a no-op (returns `Task.CompletedTask`)
- Endpoint checks if event exists and validates status transition first

**Expected Implementation**:
```csharp
// OData PATCH to update statuscode to Completed
// PATCH /api/data/v9.2/sprk_events({id})
// Content-Type: application/json
// {
//     "statuscode": 5,
//     "sprk_completeddate": "2026-02-01T12:00:00Z"
// }

// Complete steps:
// 1. Validate event exists and is in valid status (Draft, Planned, Open, On Hold)
// 2. Set statuscode to 5 (Completed)
// 3. Set sprk_completeddate to current timestamp
// 4. Create Event Log entry: action="completed", previousStatus, newStatus="Completed" (task 055)
```

**Resolution Criteria**:
1. `sprk_event` entity deployed to Dataverse
2. IDataverseService supports update operations
3. Event Log creation integrated (task 055)
4. Unit tests pass with real Dataverse connection

---

### S053-02: Update sprk_event statuscode to Cancelled

**Task**: 053 - Create Event API - complete/cancel actions

**Location**: `src/server/api/Sprk.Bff.Api/Api/Events/EventEndpoints.cs`

**Current Behavior**:
- `UpdateEventStatusAsync()` is a no-op (returns `Task.CompletedTask`)
- Endpoint checks if event exists and validates status transition first

**Expected Implementation**:
```csharp
// OData PATCH to update statuscode to Cancelled
// PATCH /api/data/v9.2/sprk_events({id})
// Content-Type: application/json
// {
//     "statuscode": 6
// }

// Cancel steps:
// 1. Validate event exists and is in valid status (Draft, Planned, Open, On Hold)
// 2. Set statuscode to 6 (Cancelled)
// 3. Create Event Log entry: action="cancelled", previousStatus, newStatus="Cancelled" (task 055)
```

**Resolution Criteria**:
1. `sprk_event` entity deployed to Dataverse
2. IDataverseService supports update operations
3. Event Log creation integrated (task 055)
4. Unit tests pass with real Dataverse connection

---

### S013-01 & S014-01: Query sprk_fieldmappingprofile

**Tasks**: 013, 014 - Field Mapping API endpoints

**Location**: `src/server/api/Sprk.Bff.Api/Api/FieldMappings/FieldMappingEndpoints.cs`

**Current Behavior**:
- Returns empty arrays
- Returns null for profile by entity pair

**Resolution Criteria**:
1. `sprk_fieldmappingprofile` entity deployed
2. `sprk_fieldmappingrule` entity deployed with relationship

---

### S054-01 & S054-02: Push Field Mappings Dataverse Operations

**Task**: 054 - Create Field Mapping API - POST push

**Location**: `src/server/api/Sprk.Bff.Api/Api/FieldMappings/FieldMappingEndpoints.cs`

**Current Behavior**:
- `RetrieveSourceRecordValuesAsync()` returns empty dictionary
- `QueryChildRecordsAsync()` returns empty array
- `ApplyMappingsToChildRecordsAsync()` builds update payload but skips actual update

**S054-01: Retrieve Source Record Field Values**

Expected implementation:
```csharp
// WebAPI call: GET /{entityLogicalName}s({recordId})?$select={fields}
// Example: GET /sprk_matters(guid)?$select=sprk_client,sprk_mattername
// Returns: Dictionary of field name -> value
```

**S054-02: Query Child Records and Update**

Expected implementation:
```csharp
// 1. Determine relationship field based on entity pair (e.g., sprk_regardingmatter)
// 2. WebAPI query: GET /{targetEntity}s?$select={id}&$filter=_sprk_regarding{source}_value eq {sourceRecordId}
// 3. For each child: PATCH /{targetEntity}s({childId}) with mapped field values
```

**Resolution Criteria**:
1. IDataverseService supports WebAPI CRUD operations
2. Entity relationship metadata available to determine regarding field names
3. Unit tests pass with real Dataverse connection

---

### S055-01: Create/Query sprk_eventlog records in Dataverse

**Task**: 055 - Implement Event Log creation on state changes

**Location**: `src/server/api/Sprk.Bff.Api/Api/Events/EventEndpoints.cs`

**Current Behavior**:
- `CreateEventLogAsync()` logs state transitions to console/logger only
- `QueryEventLogsAsync()` returns empty array
- `GetEventLogsAsync()` endpoint returns empty response

**Expected Implementation**:

**Create Event Log (CreateEventLogAsync)**:
```csharp
// OData POST to create event log record
// POST /api/data/v9.2/sprk_eventlogs
// Content-Type: application/json
// {
//     "sprk_event@odata.bind": "/sprk_events({eventId})",
//     "sprk_action": 2,  // Created=0, Updated=1, Completed=2, Cancelled=3, Deleted=4
//     "sprk_description": "Status changed from Open to Completed"
// }

// Create steps:
// 1. Build sprk_eventlog entity with event reference, action, description
// 2. Get current user from auth context for audit trail
// 3. Create record via IDataverseService
// 4. Log success
```

**Query Event Logs (QueryEventLogsAsync)**:
```csharp
// OData GET to query event log records
// GET /api/data/v9.2/sprk_eventlogs?$filter=_sprk_event_value eq {eventId}&$orderby=createdon desc
// $select=sprk_eventlogid,sprk_action,sprk_description,createdon,createdby

// Query steps:
// 1. Filter by _sprk_event_value (lookup)
// 2. Order by createdon descending
// 3. Map results to EventLogDto array
```

**Resolution Criteria**:
1. `sprk_eventlog` entity deployed to Dataverse
2. IDataverseService supports create and query operations
3. Event status transitions create log entries
4. GET /api/v1/events/{id}/logs returns actual log entries
5. Unit tests pass with real Dataverse connection

---

### S021-01: ENTITY_LOOKUP_CONFIGS hardcoded

**Task**: 021 - AssociationResolver regarding field population

**Location**: `src/client/pcf/AssociationResolver/handlers/RecordSelectionHandler.ts`

**Current Behavior**:
- Entity lookup configurations hardcoded in TypeScript

**Future State**:
- Configuration loaded from Dataverse or environment variable
- Allows adding new entity types without code change

---

## Resolved Stubs

*None yet - stubs will be moved here when resolved*

---

## How to Resolve Stubs

1. **Find stub in code**: Search for `STUB: [API] - SXXX-XX` or `STUB: [PCF] - SXXX-XX`
2. **Implement real logic**: Replace stub with actual Dataverse query/logic
3. **Update tests**: Ensure integration tests pass
4. **Move to Resolved**: Update this file to move stub to Resolved section with date

---

*This file is maintained by task-execute skill when stubs are created.*
