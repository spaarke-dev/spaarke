# Automatic Document Re-Indexing Design

> **Status**: Design Complete
> **Created**: January 23, 2026
> **Project**: AI Semantic Search UI R2

---

## Executive Summary

This document defines the architecture and implementation plan for **automatic document re-indexing** in the semantic search system. When document metadata or file content changes, the system must detect these changes and update the search index to maintain accurate semantic relationships.

---

## Requirements

### Functional Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-01 | Re-index when Parent entity (Matter, Project, Invoice) metadata changes | High |
| FR-02 | Re-index when Document entity metadata changes | High |
| FR-03 | Re-index when SPE file content is modified | High |
| FR-04 | Re-index immediately on Document check-in | High |
| FR-05 | Notify user via dialog when re-indexing is triggered by their action | Medium |
| FR-06 | Limit manual "Send to Index" to 10 records in UI | Medium |
| FR-07 | Backend indexing has no hard limits (supports admin bulk operations) | Medium |

### Non-Functional Requirements

| ID | Requirement |
|----|-------------|
| NFR-01 | ADR-001 compliant: Use BackgroundService, not plugins or Power Automate |
| NFR-02 | Polling latency: 5-10 minutes for change detection |
| NFR-03 | Check-in re-index: Near-immediate (within request lifecycle) |
| NFR-04 | Scalable: Support thousands of documents per tenant |

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              CHANGE SOURCES                                  │
├───────────────────────┬───────────────────────┬─────────────────────────────┤
│   Dataverse Records   │   SPE Files           │   User Actions              │
│   (modifiedon field)  │   (Graph Delta API)   │   (Check-in, Send to Index) │
└───────────┬───────────┴───────────┬───────────┴──────────────┬──────────────┘
            │                       │                          │
            ▼                       ▼                          ▼
┌───────────────────────────────────────────────────┐  ┌──────────────────────┐
│         DocumentReindexingBackgroundService       │  │  Explicit Triggers   │
│  ┌─────────────────────────────────────────────┐  │  │  (Immediate)         │
│  │ Poll every 5 minutes:                       │  │  ├──────────────────────┤
│  │  • Dataverse Document.modifiedon changes   │  │  │ • Check-in endpoint  │
│  │  • Dataverse Parent entity changes         │  │  │ • Manual Send to Idx │
│  │  • Graph Delta API for SPE file changes    │  │  │                      │
│  └─────────────────────────────────────────────┘  │  └──────────┬───────────┘
└───────────────────────┬───────────────────────────┘             │
                        │                                          │
                        ▼                                          ▼
              ┌─────────────────────────────────────────────────────────────┐
              │                    Re-Index Queue                           │
              │  (In-memory for immediate, Redis-backed for background)     │
              └─────────────────────────────────────────────────────────────┘
                                          │
                                          ▼
              ┌─────────────────────────────────────────────────────────────┐
              │               Existing SendToIndex Pipeline                 │
              │  RagEndpoints.cs → DocumentIntelligence → AI Search Index   │
              └─────────────────────────────────────────────────────────────┘
```

---

## Component Design

### 1. Background Service (Polling-Based Change Detection)

**File**: `src/server/api/Sprk.Bff.Api/BackgroundServices/DocumentReindexingBackgroundService.cs`

```
┌─────────────────────────────────────────────────────────────────┐
│              DocumentReindexingBackgroundService                │
├─────────────────────────────────────────────────────────────────┤
│ Responsibilities:                                               │
│  • Poll Dataverse for Document/Parent entity changes            │
│  • Poll Graph Delta API for SPE file modifications              │
│  • Queue changed documents for re-indexing                      │
│  • Track last-check timestamps per tenant in Redis              │
├─────────────────────────────────────────────────────────────────┤
│ Methods:                                                        │
│  + ExecuteAsync(CancellationToken)                              │
│  - DetectDataverseChangesAsync(tenantId, lastCheckTime)         │
│  - DetectSpeFileChangesAsync(tenantId, deltaToken)              │
│  - QueueForReindexAsync(documentIds[])                          │
│  - ProcessReindexQueueAsync()                                   │
├─────────────────────────────────────────────────────────────────┤
│ Dependencies:                                                   │
│  • IDataverseClient                                             │
│  • ISpeFileStore                                                │
│  • IReindexingStateStore (Redis)                                │
│  • IRagIndexingService                                          │
└─────────────────────────────────────────────────────────────────┘
```

**Polling Logic**:

```
Every 5 minutes per tenant:
  1. Get lastCheckTime from Redis (key: "reindex:tenant:{tenantId}:lastCheck")

  2. Query Dataverse for changed Documents:
     SELECT documentid, modifiedon FROM sprk_documents
     WHERE modifiedon > {lastCheckTime}

  3. Query Dataverse for changed Parent entities:
     For each parent type (Matter, Project, Invoice):
       SELECT {parentid}, modifiedon FROM sprk_{parenttype}
       WHERE modifiedon > {lastCheckTime}

     Then find associated Documents:
       SELECT documentid FROM sprk_documents
       WHERE sprk_{parenttype}id IN ({changedParentIds})

  4. Call Graph Delta API for SPE file changes:
     GET /drives/{driveId}/root/delta?token={deltaToken}

     Map changed file IDs to Document records

  5. Combine all changed document IDs (deduplicated)

  6. Queue for re-indexing (batch of 50 at a time)

  7. Update lastCheckTime and deltaToken in Redis
```

---

### 2. State Store (Redis-Backed)

**Interface**: `src/server/api/Sprk.Bff.Api/Services/Indexing/IReindexingStateStore.cs`

```csharp
public interface IReindexingStateStore
{
    Task<DateTimeOffset?> GetLastCheckTimeAsync(string tenantId);
    Task SetLastCheckTimeAsync(string tenantId, DateTimeOffset time);

    Task<string?> GetDeltaTokenAsync(string tenantId);
    Task SetDeltaTokenAsync(string tenantId, string token);

    Task<DateTimeOffset?> GetDocumentLastIndexedAsync(string documentId);
    Task SetDocumentLastIndexedAsync(string documentId, DateTimeOffset time);
}
```

**Implementation**: `src/server/api/Sprk.Bff.Api/Services/Indexing/ReindexingStateStore.cs`

**Redis Keys**:
| Key Pattern | Purpose |
|-------------|---------|
| `reindex:tenant:{tenantId}:lastCheck` | Last polling timestamp |
| `reindex:tenant:{tenantId}:deltaToken` | Graph Delta API token |
| `reindex:doc:{documentId}:lastIndexed` | Per-document index timestamp |

---

### 3. Explicit Check-In Trigger

**File**: `src/server/api/Sprk.Bff.Api/Api/Documents/CheckinEndpoints.cs`

**Modification**: Add re-index trigger after successful check-in

```
Current Flow:
  POST /api/documents/{id}/checkin
    → Unlock document in SPE
    → Return success

New Flow:
  POST /api/documents/{id}/checkin
    → Unlock document in SPE
    → Queue document for immediate re-index
    → Return success with reindexing: true flag
```

**Endpoint Response Enhancement**:
```json
{
  "success": true,
  "documentId": "abc-123",
  "reindexing": true,
  "message": "Document checked in. Re-indexing in progress."
}
```

---

### 4. Front-End Notifications and Limits

**File**: `infrastructure/dataverse/ribbon/DocumentRibbons/WebResources/sprk_DocumentOperations.js`

#### 4.1 Manual Send to Index - 10 Record Limit

**Function**: `sendToIndex(selectedIds, tenantId)`

```javascript
// Add at start of sendToIndex function
var MAX_MANUAL_INDEX_RECORDS = 10;

if (selectedIds.length > MAX_MANUAL_INDEX_RECORDS) {
    Xrm.Navigation.openAlertDialog({
        title: "Selection Limit Exceeded",
        text: "The manual Send to Index is limited to " + MAX_MANUAL_INDEX_RECORDS +
              " records at a time.\n\nContact your administrator for large volume " +
              "bulk indexing projects.",
        confirmButtonLabel: "OK"
    });
    return;
}
```

#### 4.2 Re-Index Notification Dialog

**Function**: Show notification when re-indexing starts

```javascript
/**
 * Shows re-indexing notification dialog
 * @param {string} trigger - "checkin" | "manual" | "auto"
 * @param {number} count - Number of documents being re-indexed
 */
function showReindexNotification(trigger, count) {
    var messages = {
        checkin: "Your document is being re-indexed to update semantic relationships.",
        manual: count + " document(s) are being sent to the search index.",
        auto: "Document metadata has changed. Re-indexing to update search results."
    };

    Xrm.Navigation.openAlertDialog({
        title: "Indexing in Progress",
        text: messages[trigger] + "\n\nThis process runs in the background and " +
              "may take a few moments to complete.",
        confirmButtonLabel: "OK"
    });
}
```

#### 4.3 Check-In with Re-Index Notification

**Function**: `checkinDocument(primaryControl)`

Add notification after successful check-in:

```javascript
// After successful check-in API call
if (response.reindexing) {
    showReindexNotification("checkin", 1);
}
```

---

## Data Flow Diagrams

### Flow 1: Background Polling (Automatic Re-Index)

```
┌──────────────┐     ┌─────────────────┐     ┌──────────────────┐
│ Background   │     │   Dataverse     │     │   Graph API      │
│ Service      │     │   (OData)       │     │   (Delta)        │
└──────┬───────┘     └────────┬────────┘     └────────┬─────────┘
       │                      │                       │
       │ Query modifiedon     │                       │
       │─────────────────────>│                       │
       │                      │                       │
       │ Changed Document IDs │                       │
       │<─────────────────────│                       │
       │                      │                       │
       │ Query parent changes │                       │
       │─────────────────────>│                       │
       │                      │                       │
       │ Changed Parent IDs   │                       │
       │<─────────────────────│                       │
       │                      │                       │
       │ GET /delta           │                       │
       │──────────────────────┼──────────────────────>│
       │                      │                       │
       │ Changed file items   │                       │
       │<─────────────────────┼───────────────────────│
       │                      │                       │
       │ Map files to docs    │                       │
       │─────────────────────>│                       │
       │                      │                       │
       │ Document IDs         │                       │
       │<─────────────────────│                       │
       │                      │                       │
       ▼                      │                       │
┌──────────────┐              │                       │
│ Deduplicate  │              │                       │
│ & Queue      │              │                       │
└──────┬───────┘              │                       │
       │                      │                       │
       ▼                      │                       │
┌──────────────┐              │                       │
│ SendToIndex  │              │                       │
│ (Batched)    │              │                       │
└──────────────┘              │                       │
```

### Flow 2: Check-In Triggered Re-Index

```
┌──────────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│  User    │     │  Ribbon JS   │     │  BFF API     │     │  AI Search   │
└────┬─────┘     └──────┬───────┘     └──────┬───────┘     └──────┬───────┘
     │                  │                    │                    │
     │ Click Check In   │                    │                    │
     │─────────────────>│                    │                    │
     │                  │                    │                    │
     │                  │ POST /checkin      │                    │
     │                  │───────────────────>│                    │
     │                  │                    │                    │
     │                  │                    │ Unlock in SPE      │
     │                  │                    │───────────────────>│
     │                  │                    │                    │
     │                  │                    │ Queue re-index     │
     │                  │                    │────────┐           │
     │                  │                    │        │           │
     │                  │                    │<───────┘           │
     │                  │                    │                    │
     │                  │ {reindexing: true} │                    │
     │                  │<───────────────────│                    │
     │                  │                    │                    │
     │ Show Dialog      │                    │                    │
     │<─────────────────│                    │                    │
     │ "Re-indexing..." │                    │                    │
     │                  │                    │                    │
     │                  │                    │ Process index      │
     │                  │                    │───────────────────>│
     │                  │                    │                    │
```

### Flow 3: Manual Send to Index (with Limit)

```
┌──────────┐     ┌──────────────┐     ┌──────────────┐
│  User    │     │  Ribbon JS   │     │  BFF API     │
└────┬─────┘     └──────┬───────┘     └──────┬───────┘
     │                  │                    │
     │ Select 15 docs   │                    │
     │ Click Send       │                    │
     │─────────────────>│                    │
     │                  │                    │
     │                  │ Count > 10?        │
     │                  │─────┐              │
     │                  │     │ YES          │
     │                  │<────┘              │
     │                  │                    │
     │ Show Error       │                    │
     │<─────────────────│                    │
     │ "Limit: 10 max"  │                    │
     │                  │                    │
     │                  │                    │
     │ Select 5 docs    │                    │
     │ Click Send       │                    │
     │─────────────────>│                    │
     │                  │                    │
     │                  │ Count <= 10?       │
     │                  │─────┐              │
     │                  │     │ YES          │
     │                  │<────┘              │
     │                  │                    │
     │ Show Progress    │                    │
     │<─────────────────│                    │
     │ "5 docs..."      │                    │
     │                  │                    │
     │                  │ POST /send-to-index│
     │                  │───────────────────>│
     │                  │                    │
     │                  │ {queued: 5}        │
     │                  │<───────────────────│
     │                  │                    │
     │ Show Success     │                    │
     │<─────────────────│                    │
     │                  │                    │
```

---

## File Inventory

### New Files

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/BackgroundServices/DocumentReindexingBackgroundService.cs` | Polling-based change detection service |
| `src/server/api/Sprk.Bff.Api/Services/Indexing/IReindexingStateStore.cs` | Interface for state tracking |
| `src/server/api/Sprk.Bff.Api/Services/Indexing/ReindexingStateStore.cs` | Redis-backed state implementation |

### Modified Files

| File | Changes |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Api/Documents/CheckinEndpoints.cs` | Add re-index trigger on check-in |
| `src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs` | Add internal queue-based re-index method |
| `src/server/api/Sprk.Bff.Api/Program.cs` | Register BackgroundService and state store |
| `infrastructure/dataverse/ribbon/DocumentRibbons/WebResources/sprk_DocumentOperations.js` | Add 10-record limit, notifications |

---

## Method Inventory

### DocumentReindexingBackgroundService

| Method | Purpose |
|--------|---------|
| `ExecuteAsync(CancellationToken)` | Main loop - runs every 5 minutes |
| `DetectDataverseDocumentChangesAsync(tenantId, lastCheck)` | Query Dataverse for modified Documents |
| `DetectDataverseParentChangesAsync(tenantId, lastCheck)` | Query parent entities, find related docs |
| `DetectSpeFileChangesAsync(tenantId, deltaToken)` | Use Graph Delta API for file changes |
| `MapSpeFilesToDocumentsAsync(tenantId, fileIds)` | Convert SPE file IDs to Document IDs |
| `QueueForReindexAsync(documentIds)` | Add to re-index queue |
| `ProcessReindexQueueAsync()` | Process queued items in batches |

### ReindexingStateStore

| Method | Purpose |
|--------|---------|
| `GetLastCheckTimeAsync(tenantId)` | Get last polling time |
| `SetLastCheckTimeAsync(tenantId, time)` | Update last polling time |
| `GetDeltaTokenAsync(tenantId)` | Get Graph Delta token |
| `SetDeltaTokenAsync(tenantId, token)` | Update Delta token |
| `GetDocumentLastIndexedAsync(documentId)` | Get per-doc index time |
| `SetDocumentLastIndexedAsync(documentId, time)` | Update per-doc index time |

### CheckinEndpoints (Modifications)

| Method | Change |
|--------|--------|
| `CheckinDocument` | After SPE unlock, queue document for re-index, return `reindexing: true` |

### sprk_DocumentOperations.js (Modifications)

| Function | Change |
|----------|--------|
| `sendToIndex` | Add 10-record limit check at start |
| `showReindexNotification` | New - show dialog for re-index events |
| `checkinDocument` | Call `showReindexNotification` if response.reindexing |

---

## Configuration

### appsettings.json

```json
{
  "Reindexing": {
    "PollingIntervalMinutes": 5,
    "BatchSize": 50,
    "MaxConcurrentIndexing": 5,
    "EnableDataversePolling": true,
    "EnableSpePolling": true,
    "ParentEntityTypes": ["sprk_matter", "sprk_project", "sprk_invoice"]
  }
}
```

### Front-End Constants

```javascript
// sprk_DocumentOperations.js
var Config = {
    // ... existing config
    MAX_MANUAL_INDEX_RECORDS: 10
};
```

---

## Out of Scope (Noted for Future)

### Admin Bulk Indexing Utility

For large-volume indexing operations (100+ documents), an administrative utility is needed:

- **Purpose**: Allow admins to bulk index/re-index documents
- **Interface**: Separate admin page or Power App
- **No UI Limits**: Backend already supports unlimited indexing
- **Queue-Based**: Uses same `QueueForReindexAsync` method
- **Progress Tracking**: Long-running operation with status updates

This utility will be implemented in a separate project phase.

---

## ADR Compliance

| ADR | Requirement | Compliance |
|-----|-------------|------------|
| ADR-001 | Use BackgroundService, not Functions | ✅ Using `DocumentReindexingBackgroundService` |
| ADR-001 | No plugins for orchestration | ✅ No Dataverse plugins |
| ADR-002 | Thin plugins (<50ms) | ✅ N/A - no plugins used |
| ADR-009 | Redis for caching | ✅ State stored in Redis |

---

## Implementation Order

| Phase | Components | Priority |
|-------|------------|----------|
| 1 | Front-end: 10-record limit, notification dialogs | High |
| 2 | Check-in trigger: Add re-index on check-in | High |
| 3 | State store: IReindexingStateStore + Redis impl | Medium |
| 4 | Background service: Dataverse polling | Medium |
| 5 | Background service: SPE file polling (Delta API) | Medium |

---

## Testing Strategy

| Test Type | Scope |
|-----------|-------|
| Unit | ReindexingStateStore methods |
| Unit | Change detection logic |
| Integration | Check-in → re-index flow |
| Integration | Background service polling |
| E2E | User modifies document → sees updated relationships |
| Load | 1000 documents changed in 5-minute window |

---

*Document created for ai-semantic-search-ui-r2 project*
