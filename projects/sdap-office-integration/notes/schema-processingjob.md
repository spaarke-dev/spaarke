# Task 012: ProcessingJob Table Schema

> **Status**: Ready for manual creation
> **Requires**: Power Platform Maker Portal access
> **Based on**: ADR-004 Async Job Contract

## Overview

The `sprk_processingjob` table implements the ADR-004 job contract pattern. It tracks async document processing through multiple stages (upload finalization, profile summary, indexing, deep analysis), enabling real-time status updates via SSE and proper error handling/retry logic.

## Table Configuration

| Setting | Value |
|---------|-------|
| **Display Name** | Processing Job |
| **Schema Name** | sprk_processingjob |
| **Plural Name** | Processing Jobs |
| **Primary Column** | sprk_name (job ID) |
| **Ownership** | User |
| **Enable Audit** | Yes |

## Fields to Create

### Primary Name Field

| Property | Value |
|----------|-------|
| Display Name | Name |
| Schema Name | sprk_name |
| Type | Single Line of Text |
| Max Length | 100 |
| Required | Yes |
| Description | Auto-generated job ID (GUID or sequential) |

### Job Type and Status

#### sprk_jobtype
| Property | Value |
|----------|-------|
| Display Name | Job Type |
| Schema Name | sprk_jobtype |
| Type | Option Set (Local) |
| Options | DocumentSave (0), EmailSave (1), ShareLinks (2), ProfileSummary (3), DeepAnalysis (4), BatchProcess (5) |
| Default | DocumentSave (0) |
| Required | Yes |
| Description | Type of async operation |

#### sprk_status
| Property | Value |
|----------|-------|
| Display Name | Status |
| Schema Name | sprk_status |
| Type | Option Set (Local) |
| Options | Pending (0), InProgress (1), Completed (2), Failed (3), Cancelled (4) |
| Default | Pending (0) |
| Required | Yes |
| **Searchable** | Yes |
| Description | Current job status |

### Stage Tracking

#### sprk_stages
| Property | Value |
|----------|-------|
| Display Name | Stages |
| Schema Name | sprk_stages |
| Type | Multiline Text |
| Max Length | 10000 |
| Required | No |
| Description | JSON array of stage definitions (ordered list of stages for this job type) |

**Example JSON:**
```json
["Upload", "Validate", "ProfileSummary", "Index", "Complete"]
```

#### sprk_currentstage
| Property | Value |
|----------|-------|
| Display Name | Current Stage |
| Schema Name | sprk_currentstage |
| Type | Single Line of Text |
| Max Length | 100 |
| Required | No |
| Description | Name of the current executing stage |

#### sprk_stagestatus
| Property | Value |
|----------|-------|
| Display Name | Stage Status |
| Schema Name | sprk_stagestatus |
| Type | Multiline Text |
| Max Length | 50000 |
| Required | No |
| Description | JSON object tracking individual stage completion status |

**Example JSON:**
```json
{
  "Upload": { "status": "Completed", "completedAt": "2025-01-20T13:00:00Z" },
  "Validate": { "status": "Completed", "completedAt": "2025-01-20T13:00:05Z" },
  "ProfileSummary": { "status": "InProgress", "startedAt": "2025-01-20T13:00:06Z" },
  "Index": { "status": "Pending" },
  "Complete": { "status": "Pending" }
}
```

### Progress Tracking

#### sprk_progress
| Property | Value |
|----------|-------|
| Display Name | Progress |
| Schema Name | sprk_progress |
| Type | Whole Number |
| Min Value | 0 |
| Max Value | 100 |
| Default | 0 |
| Required | No |
| Description | Overall percentage (0-100) |

### Timing Fields

#### sprk_starteddate
| Property | Value |
|----------|-------|
| Display Name | Started Date |
| Schema Name | sprk_starteddate |
| Type | Date and Time |
| Behavior | User Local |
| Required | No |
| Description | When job processing started |

#### sprk_completeddate
| Property | Value |
|----------|-------|
| Display Name | Completed Date |
| Schema Name | sprk_completeddate |
| Type | Date and Time |
| Behavior | User Local |
| Required | No |
| Description | When job finished (success or failure) |

### Error Tracking

#### sprk_errorcode
| Property | Value |
|----------|-------|
| Display Name | Error Code |
| Schema Name | sprk_errorcode |
| Type | Single Line of Text |
| Max Length | 100 |
| Required | No |
| Description | Machine-readable error code |

#### sprk_errormessage
| Property | Value |
|----------|-------|
| Display Name | Error Message |
| Schema Name | sprk_errormessage |
| Type | Multiline Text |
| Max Length | 10000 |
| Required | No |
| Description | Human-readable error description and stack trace |

#### sprk_retrycount
| Property | Value |
|----------|-------|
| Display Name | Retry Count |
| Schema Name | sprk_retrycount |
| Type | Whole Number |
| Min Value | 0 |
| Max Value | 10 |
| Default | 0 |
| Required | No |
| Description | Number of retry attempts |

### Idempotency and Correlation

#### sprk_idempotencykey
| Property | Value |
|----------|-------|
| Display Name | Idempotency Key |
| Schema Name | sprk_idempotencykey |
| Type | Single Line of Text |
| Max Length | 64 |
| Required | No |
| **Searchable** | Yes |
| Description | SHA256 hash of userId + requestBody for duplicate detection |

#### sprk_correlationid
| Property | Value |
|----------|-------|
| Display Name | Correlation ID |
| Schema Name | sprk_correlationid |
| Type | Single Line of Text |
| Max Length | 100 |
| Required | No |
| Description | For distributed tracing across services |

### Relationships

#### sprk_initiatedby (Lookup)
| Property | Value |
|----------|-------|
| Display Name | Initiated By |
| Schema Name | sprk_initiatedby |
| Type | Lookup |
| Target Entity | systemuser |
| Required | No |
| Description | User who initiated the job |

#### sprk_document (Lookup)
| Property | Value |
|----------|-------|
| Display Name | Document |
| Schema Name | sprk_document |
| Type | Lookup |
| Target Entity | sprk_document |
| Required | No |
| Description | Target document being processed |

### Input/Output Data

#### sprk_payload
| Property | Value |
|----------|-------|
| Display Name | Payload |
| Schema Name | sprk_payload |
| Type | Multiline Text |
| Max Length | 100000 |
| Required | No |
| Description | JSON input data for the job |

#### sprk_result
| Property | Value |
|----------|-------|
| Display Name | Result |
| Schema Name | sprk_result |
| Type | Multiline Text |
| Max Length | 100000 |
| Required | No |
| Description | JSON output data from the job |

## Indexes Required

1. **Index on sprk_idempotencykey**
   - Purpose: Fast idempotency checks (prevent duplicate processing)
   - Column: sprk_idempotencykey (ascending)

2. **Index on sprk_status**
   - Purpose: Query for jobs by status (find pending jobs, failed jobs)
   - Column: sprk_status (ascending)

3. **Composite Index on sprk_status + sprk_starteddate**
   - Purpose: Efficiently find stale in-progress jobs
   - Columns: sprk_status, sprk_starteddate (descending)

## ADR-004 Job Contract Requirements

This table implements the following ADR-004 requirements:

1. **Idempotent Job Handlers**: The `sprk_idempotencykey` field enables idempotent request handling
2. **Stage Tracking**: `sprk_stages` and `sprk_stagestatus` enable granular progress updates
3. **SSE Updates**: The `sprk_progress` and `sprk_stagestatus` fields power real-time SSE notifications
4. **Retry Logic**: `sprk_retrycount` and `sprk_errorcode` support retry strategies
5. **Correlation**: `sprk_correlationid` enables distributed tracing

## Job Type Stage Definitions

Different job types have different stage sequences:

### DocumentSave Stages
```json
["Upload", "Validate", "Store", "ProfileSummary", "Index", "Complete"]
```

### EmailSave Stages
```json
["Extract", "Validate", "Store", "ParseHeaders", "ProfileSummary", "Index", "Complete"]
```

### ShareLinks Stages
```json
["Validate", "CreatePermission", "GenerateLink", "Complete"]
```

## Verification Checklist

- [ ] Table `sprk_processingjob` exists
- [ ] All fields created with correct types
- [ ] sprk_jobtype option set has all values (DocumentSave, EmailSave, ShareLinks, ProfileSummary, DeepAnalysis, BatchProcess)
- [ ] sprk_status option set has all values (Pending, InProgress, Completed, Failed, Cancelled)
- [ ] sprk_idempotencykey field is searchable/indexed
- [ ] sprk_status field is searchable/indexed
- [ ] Lookup to systemuser for sprk_initiatedby
- [ ] Lookup to Document for sprk_document
- [ ] Table is solution-aware

## References

- [Task 012 POML](../tasks/012-create-processingjob-table.poml)
- [ADR-004 Async Job Contract](../../../.claude/adr/ADR-004-async-job-contract.md)
- [Dataverse Schema Guide](../../../docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md)
