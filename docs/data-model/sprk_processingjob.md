# sprk_processingjob — Processing Job (run record)

> **Last updated**: 2026-11-26
> **Projects**: SDAP Office integration (the Office save job); spaarke-intake-pipeline-r1 task 011 (intake pipeline runs, FR-06)
> **Schema scripts**: the Office columns were created in the maker portal and are in the solution XML (`src/dataverse/solutions/SpaarkeMaster/Entities/sprk_ProcessingJob/Entity.xml`). The intake-run extension is [`scripts/Add-ProcessingJobIntakeRunColumns.ps1`](../../scripts/Add-ProcessingJobIntakeRunColumns.ps1) (idempotent, `-WhatIf`).
> **Ownership**: user-owned. Primary name `sprk_name`.
> **Status of the intake columns**: scripted and dry-run verified against dev on 2026-11-26; **not yet created on dev** (the live run needs the owner's approval, project CLAUDE.md section 5). Until that run, the columns and the "Intake run" option below describe the target schema. The FR-06b read exclusion is already in the BFF code.
> **Not to be confused with** [`sprk_backgroundjob`](sprk_backgroundjob.md) (the scheduled-job *definition* catalog) and `sprk_backgroundjobrun`.

## Purpose

One row per job **run**. Two kinds of run share the table, told apart by `sprk_jobtype`:

| Kind | `sprk_jobtype` | Written by | Read by |
|---|---|---|---|
| Office save and its follow-on work | 0 to 6 (see below) | `OfficeJobStatusService`, the Office workers | `OfficeJobStatusService` (`GET /api/office/jobs/{id}`, `/stream`, the save idempotency check) |
| Intake pipeline run (one pass of one request) | 7, **Intake run** | the intake orchestrator and the admin re-drive (BFF only) | the intake run status and admin surfaces (spaarke-intake-pipeline-r1 tasks 023, 024) |

**The two readers never see each other's rows (FR-06b).** `OfficeJobStatusService.GetAsync` and `FindExistingAsync` treat an Intake run row as not found, even for the user who started the request, and the intake reads select job type 7 only. Test: `tests/integration/contract/Api/Office/OfficeJobStatusIntakeRunContractTests.cs`.

## One request, many runs

`sprk_servicerequest` is the request; each processing of it is a **run**. A request has many runs: a re-drive, or a re-run after a harness fix, creates a **new** run row and the request's current-run pointer moves (spaarke-intake-pipeline-r1 task 010). The relationship is 1:N through the `sprk_servicerequest` lookup below. Deleting a request that has runs is refused (Delete = Restrict): runs are audit records.

## Columns

Standard audit fields (`createdon`, `createdby`, `modifiedon`, `modifiedby`, `ownerid`, `statecode`, `statuscode`, `versionnumber`) are added by Dataverse and not listed.

### Columns shared by both kinds of run

| Column | Type | Required | Length / values | Purpose |
|---|---|---|---|---|
| `sprk_processingjobid` | Uniqueidentifier | system | — | Primary key. The Office job id the pane polls; the intake run id. |
| `sprk_name` | Text | **Required** | 850 | Primary name. Office: a `"Document Save - ..."`-style label. Intake: a label the orchestrator sets from ids (never from a document's file name). |
| `sprk_jobtype` | Choice | Optional | local option set `new_sprk_processingjob_sprk_jobtype` | See [Job types](#job-types). |
| `sprk_status` | Choice | **Required** | 0 Pending, 1 In Progress, 2 Completed, 3 Failed, 4 Cancelled | The pipeline status. Terminal state is written **last** (FR-07). |
| `sprk_progress` | Whole number | Optional | — | Percent, 0 to 100. |
| `sprk_currentstage` | Text | Optional | 100 | The stage now running. **Intake runs store the stage name here** (Receive, Store, Classify, Route, Analyse, Build, Persist, Approve, Return, Record; spec FR-07). |
| `sprk_stages` | Multiline text | Optional | 10 000 | JSON array of the run's stage definitions. Intake runs store the ten stages here. |
| `sprk_stagestatus` | Multiline text | Optional | 10 000 | JSON object of per-stage completion. Resume reads it to continue at the failed stage without re-running a persisted AI stage. |
| `sprk_starteddate` | Date and time | Optional | — | When processing began. |
| `sprk_completeddate` | Date and time | Optional | — | When the run reached a terminal state. |
| `sprk_errorcode` | Text | Optional | 50 | Error code (Office: `OFFICE_xxx`). |
| `sprk_errormessage` | Multiline text | Optional | 2 000 | Error message. No document content. |
| `sprk_retrycount` | Whole number | Optional | — | Automatic retry attempts. Not the operator re-drive count (see `sprk_redrivecount`). |
| `sprk_idempotencykey` | Text | Optional | 64 | Duplicate-prevention key. Office: the save key. Intake: a deterministic key per request and pass. |
| `sprk_correlationid` | Text | Optional | 36 | Distributed-trace id, carried end to end (NFR-06). |
| `sprk_initiatedby` | Lookup → `systemuser` | Optional | — | Who asked. Office ownership checks resolve it to an Entra object id. |
| `sprk_document` | Lookup → `sprk_document` | Optional | — | The Office save's target document. Not used by intake runs (see `sprk_outputdocument`). |
| `sprk_payload` | Multiline text | Optional | 50 000 | Request **metadata** for diagnostics. Never content (see [No content](#no-content-adr-015)). |
| `sprk_result` | Multiline text | Optional | 50 000 | The run's outcome as JSON; the schema belongs to the writer. Office: the save's `JobStatusResponse` view. |

### Columns added for intake runs (task 011)

Created by [`Add-ProcessingJobIntakeRunColumns.ps1`](../../scripts/Add-ProcessingJobIntakeRunColumns.ps1). Written **only by the BFF**.

| Column | Type | Required | Length / values | Purpose |
|---|---|---|---|---|
| `sprk_servicerequest` | Lookup → `sprk_servicerequest` | Optional | Delete = Restrict | The request this run belongs to. One request, many runs. Web API bind name `sprk_ServiceRequest@odata.bind`. |
| `sprk_outputdocument` | Lookup → `sprk_document` | Optional | Delete = RemoveLink | The document the run produced (the redline or the clause table), set by the Record stage. Bind name `sprk_OutputDocument@odata.bind`. |
| `sprk_harnesskey` | Text | Optional | 100 | The harness the run was routed to, as its routing key (for example `nda-review`). A key only. |
| `sprk_engineexecutionid` | Text | Optional | 200 | The Container Apps Job execution id, so a resumed run re-attaches to its engine execution instead of starting a second one. An id only. |
| `sprk_stagingblobprefix` | Text | Optional | 500 | The run's claim-check prefix in the stamp-private staging container. Built from ids, never from document file names. |
| `sprk_redrivecount` | Whole number | Optional | 0 to 2 147 483 647 | How many times an operator re-drove the run. Dataverse whole-number columns take no schema default, so **null means 0**; the admin re-drive writes the incremented value. |
| `sprk_lastredriveon` | Date and time (user local) | Optional | — | When the run was last re-driven. |

## Job types

`sprk_jobtype` is a local choice (`new_sprk_processingjob_sprk_jobtype`).

| Value | Label | Kind |
|---|---|---|
| 0 | Document Save | Office |
| 1 | Email Save | Office |
| 2 | Share Links | Office |
| 3 | Quick Create | Office |
| 4 | Profile Summary | Office |
| 5 | Indexing | Office |
| 6 | Deep Analysis | Office |
| **7** | **Intake run** | Intake pipeline (spaarke-intake-pipeline-r1) |

C# mirror: `Sprk.Bff.Api.Models.ProcessingJobType` (`IntakeRun = 7`). Office writes `(int)Models.Office.JobType` (0 to 4), which does not match the labels above; that mismatch pre-dates this project and is why the Office read takes the job type from the stored view (see the remark on `OfficeJobStatusService.MapDataverseJobTypeToJobType`). It never writes 7.

## Writers and the BFF-only rule (ADR-002)

- **Every column above is written only by the BFF.** The Office save and its workers write the shared columns; the intake orchestrator (task 013), engine dispatch and completion (task 014) and the admin re-drive (task 023) write the intake columns. No Dataverse plugin and no client write path (a form, the Office add-in or the Console never writes a run row).
- The run's invariants (terminal status last, one current run per request, re-drive counters) have one owner: the BFF write path. A wizard or form may preview them but never enforces them.
- `CreateProcessingJobAsync` / `UpdateProcessingJobAsync` (`DataverseServiceClientImpl`) map properties to `sprk_{name}` columns and wrap `sprk_status`, `sprk_jobtype` and (create only) `sprk_initiatedby`. **They do not yet wrap the new lookups**: a bare `Guid` assigned to `sprk_servicerequest` or `sprk_outputdocument` is rejected by Dataverse. The intake write path must send an `EntityReference` (`sprk_servicerequest`, `sprk_document`), and the typed read (`ProcessingJobRecord`, `ProcessingJobColumns`) does not yet return the new columns.

## No content (ADR-015)

`sprk_payload`, `sprk_result`, `sprk_harnesskey`, `sprk_engineexecutionid`, `sprk_stagingblobprefix`, `sprk_errormessage` and `sprk_name` carry **ids, keys, prefixes, stage names and metadata only**: never document text, file names taken from a document, redline or clause content, or playbook/prompt text. Document bytes travel by blob staging; queue messages carry ids (NFR-03).

## Related

- [`sprk_servicerequest.md`](sprk_servicerequest.md): the request a run belongs to.
- [`sprk_backgroundjob.md`](sprk_backgroundjob.md): why the job-definition catalog is a separate entity.
- Spec FR-06, FR-06b, FR-07: `projects/spaarke-intake-pipeline-r1/spec.md`. ADR-017 (job status persisted and authorized), ADR-015 (no content), ADR-002 (BFF write path).
