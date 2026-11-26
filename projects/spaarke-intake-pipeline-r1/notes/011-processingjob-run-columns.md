# Task 011: sprk_processingjob as the intake run record (FR-06)

> Status: **script written and dry-run verified; live dev run NOT done** (owner has not approved the dev schema change). `sprk_ProcessingJob/Entity.xml` is deliberately unchanged until the live run.
> Executed 2026-11-26. Rigor STANDARD (Step 9.5 also run: the task adds a test under `tests/**`).

## FINAL NAMES (tasks 013, 014, 023 depend on these)

**Job type: `sprk_jobtype` = `7`, label "Intake run".** (Dev options today: 0 Document Save, 1 Email Save, 2 Share Links, 3 Quick Create, 4 Profile Summary, 5 Indexing, 6 Deep Analysis; 7 is the next free value and no Office writer produces it.) C# constant: `Sprk.Bff.Api.Models.ProcessingJobType.IntakeRun = 7` (added in this task). The script parameter `-IntakeRunJobTypeValue` defaults to 7.

| Purpose | Logical name | Type | Web API bind name (lookups) | Delete |
|---|---|---|---|---|
| Request this run belongs to (1 request : N runs) | `sprk_servicerequest` | Lookup → `sprk_servicerequest` | `sprk_ServiceRequest@odata.bind` (set `/sprk_servicerequests(<id>)`) | Restrict |
| Output document (redline / clause table) | `sprk_outputdocument` | Lookup → `sprk_document` | `sprk_OutputDocument@odata.bind` (set `/sprk_documents(<id>)`) | RemoveLink |
| Harness routing key | `sprk_harnesskey` | Text 100 | | |
| Engine Job execution id | `sprk_engineexecutionid` | Text 200 | | |
| Staging blob prefix (ids only) | `sprk_stagingblobprefix` | Text 500 | | |
| Operator re-drive count | `sprk_redrivecount` | Whole number 0..2147483647, **no schema default: null = 0** | | |
| Last re-drive time | `sprk_lastredriveon` | Date and time (user local) | | |

Relationship schema names: `sprk_servicerequest_processingjob_servicerequest`, `sprk_document_processingjob_outputdocument`. Entity set: `sprk_processingjobs`.

**Intake stages use the EXISTING columns, no type change** (verified read-only on dev 2026-11-26): `sprk_currentstage` = String(100), `sprk_stages` = Memo(10 000), `sprk_stagestatus` = Memo(10 000). The escalation trigger did not fire. `sprk_status` stays Pending 0 / In Progress 1 / Completed 2 / Failed 3 / Cancelled 4.

## Deviation from the goal text

- **"`sprk_redrivecount` (whole number, default 0)"**: Dataverse whole-number attributes have no schema default (the metadata API has none for `IntegerAttributeMetadata`). The column is created without one. Writers (task 023) treat **null as 0** and write the incremented value; a reader must not assume the column is populated.

## Step 3: `-WhatIf` run (dev, read-only), 2026-11-26

`pwsh scripts/Add-ProcessingJobIntakeRunColumns.ps1 -WhatIf`, exit 0. Auth worked (az CLI token), so the full dry run executed. Nothing was created, changed or published.

```
Step 1: sprk_processingjob, sprk_servicerequest, sprk_document exist.
Step 2: sprk_currentstage = String, sprk_stages = Memo: intake stages fit, no change.
Step 3: WhatIf: would create lookup sprk_ServiceRequest (Delete=Restrict)
        WhatIf: would create lookup sprk_OutputDocument (Delete=RemoveLink)
Step 4: WhatIf: would create sprk_harnesskey, sprk_engineexecutionid, sprk_stagingblobprefix,
                sprk_redrivecount, sprk_lastredriveon
Step 5: WhatIf: would add option 7 'Intake run'
Step 6: WhatIf: would publish
Step 7: all seven new columns reported MISSING (expected: dry run)
```

That all seven read as "would create" also proves that none of the names collides with an existing column on dev. The idempotency (second real run makes no change) is **not yet proven live**: it is by construction (every step checks first, and the option step refuses a clashing value or label), and is exercised for the existing-column branch only on the live re-run.

## FR-06b verdict: **filter ADDED**

An intake run could be returned by the Office job-status read:
- `GET /api/office/jobs/{jobId}` and `/stream` call `OfficeJobStatusService.GetAsync`, which loads **any** `sprk_processingjob` row by id (`GetProcessingJobAsync`, `DataverseServiceClientImpl.cs:2239`, no job-type condition). `JobOwnershipFilter` only compares the row's creator OID with the caller.
- The orchestrator is expected to record the submitting user as `sprk_initiatedby` on the run (it is the table's creator column, and task 013 is not written yet, so this is an inference, not a read of its code); a run that does so passes the ownership check for that user; `MapDataverseJobTypeToJobType` maps the unknown value to `DocumentSave`. Failure scenario: the submitter calls `GET /api/office/jobs/{intakeRunId}` and gets the intake run served as an Office save (stage name as `currentPhase`, intake error text, progress).
- `FindExistingAsync` (the save idempotency check) had the same gap for a shared idempotency key.

Change (only BFF change in the task): `OfficeJobStatusService.IsIntakeRun(row)` (`JobType == (int)ProcessingJobType.IntakeRun`), applied in `GetAsync` (returns null, so 404 via `JobOwnershipFilter`, the status read and the SSE stream) and in `FindExistingAsync` (treated as no duplicate). Class search: `OfficeJobStatusService` is the only caller of the three `IProcessingJobService` job readers (`GetProcessingJobByIdempotencyKeyAsync` has no BFF caller), so no other reader needs the filter.

Test: `tests/integration/contract/Api/Office/OfficeJobStatusIntakeRunContractTests.cs` (KEEP path `integration/contract`, 1 test). It saves a real Office document, clones its row twice with the caller as creator, sets one clone to job type 7, and asserts Office clone 200 (control) / intake clone 404. **Before the fix: failed (200 for the intake row). After: passes.**

## BFF hygiene (§10, task is BFF-touching)

**Publish size, delta against a fresh master build** (`origin/master` @ `9d2bf853d7`; branch side = the same fresh master worktree with only this task's two BFF files applied as a patch, so other tasks' commits on this branch do not count):
- Command `dotnet publish -c Release -o deploy/api-publish --no-restore` (widget `npm` step skipped on both sides, which the csproj allows; identical on both), framework-dependent linux-x64, Release, zip = PowerShell `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included.
- Fresh worktrees at short paths (`C:\wt011m`, `C:\wt011b`); **194 files on each side**.
- master **43,633,386 bytes (41.61 MB)**; branch **43,633,344 bytes (41.61 MB)**; **delta -42 bytes (0.00 MB)**. Well under the +5 MB trigger, the 55 MB review line and the 60 MB stop.
- CVE: no `PackageReference` changed, so no new HIGH CVE is possible from this task (`dotnet list package --vulnerable` not re-run).
- Both worktrees removed with `scripts/worktree/Remove-WorktreeSafe.ps1`.

**Tests** (`tests/unit/Sprk.Bff.Api.Tests`, run on 2026-11-26):
- New test: before the fix **1 failed** (intake row served 200), after **1 passed**.
- Job-status and Office save suites unchanged: `OfficeJobEffectiveStateTests`, `Issue1084`, `Issue1086`, `Issue975`, `OfficeAuthFilterTests`, `OfficeEndpointAuthorizationContractTests`, `OfficeEndpointsContractTests`, `OfficeVersionSaveContractTests`, `OfficeSaveSpineIdempotencyContractTests`, `OfficeSaveAddInWireContractTests`, `DuplicateDetectionTests`, `OfficeEmailReSaveFinalizationPendingTests` plus the new test: **124 passed, 0 failed, 7 skipped, 131 total**. The 7 skips are existing `[Fact(Skip = ...)]` attributes in `OfficeEndpointsContractTests.cs` (a file this task does not touch).
- `tests/Spaarke.ArchTests` filtered to `Adr038*` (test-ban guard) and `RouteAuthorizationGuard*`: **114 passed, 0 failed** (no route added or changed; the new test file passes the ban guard).
- A first attempt with a broad `Office` name filter was stopped after ~40 minutes of test-host CPU (it matches far more than job status); the focused set above replaced it.

## Code review / ADR check (Step 9.5)

Reviewed against the code-review steps; ADR checks done inline against the concise ADRs (the adr-check skill itself was not launched).
- No Critical, no F1, no unmet acceptance criterion in scope.
- F2 found and fixed: the data-model doc stated the intake columns as live; it now carries a status line saying they are scripted and dry-run verified only.
- Lint: the script's only PSScriptAnalyzer findings are `PSAvoidUsingWriteHost`, the same idiom the canonical script uses (K-class, no real effect).
- ADR-002 (BFF-only writers, no plugin): holds. ADR-015: the new strings are ids, keys and prefixes. ADR-017: the Office read now fails closed on an intake row. ADR-036: description text only. ADR-010: no new interface or DI registration.
- Placement Justification (for the PR): in the BFF, in the existing Office job-record class that owns this table's Office reads; no new endpoint, service, DI registration, package or background work. Component justification: existing = `OfficeJobStatusService` readers; extension = yes (a 3-line guard in them); cost of nothing = the Office endpoints serve intake runs to their submitter.

## sprk_backgroundjob description (FR-06c)

"Office-scoped" removed in the three named places plus the script header comment that said the same ("DO NOT extend that entity"), and two rows of the comparison table in the .md:
- `scripts/Create-BackgroundJobEntity.ps1` (description string, and the header NAMING-COLLISION NOTE)
- `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_backgroundjob/Entity.xml:13`
- `docs/data-model/sprk_backgroundjob.md` (the "Why not extend" paragraph and two table rows)

The live dev entity still carries the old description text (it was created before this change); it is updated only if someone re-imports the solution or edits it. Not a functional issue.

## Findings for other tasks (not fixed here: outside this task's scope)

1. **The write path does not handle the new lookups (tasks 013 / 023).** `DataverseServiceClientImpl.CreateProcessingJobAsync` assigns a bare `Guid` to the column, except for `sprk_initiatedby`; `UpdateProcessingJobAsync` does so for every Guid. A `Guid` written to `sprk_servicerequest` or `sprk_outputdocument` is rejected by Dataverse (it needs an `EntityReference`), and the failure would surface in the orchestrator's run create. Whoever writes the run (013) must add the two `EntityReference` cases (`sprk_servicerequest` → `sprk_servicerequest`, `sprk_outputdocument` → `sprk_document`) to both mappers.
2. **The typed read does not return the new columns (tasks 023 / 024).** `ProcessingJobColumns` and `ProcessingJobRecord` stop at the Office columns, so run status, the admin list and resume cannot read `sprk_servicerequest`, the harness key, the execution id, the staging prefix or the re-drive marker until both are extended, and the intake reads must select `sprk_jobtype = 7`. (`ProcessingJobRecord` and the mappers are shared by the Office path, so tests for the Office suite must still pass.)
3. **Environment, not this project:** four long-running `python` processes (started 2026-11-23, 24, 24, 25, with CPU times of 11 000 to 163 000 seconds) are alive on this machine. They are not from this task. Consistent with the known python3 Store-stub spin; they should be killed by the owner.

## Remaining for the live run (owner approval required)

1. Owner OK for the dev schema change (project CLAUDE.md section 5).
2. `pwsh scripts/Add-ProcessingJobIntakeRunColumns.ps1` against dev; then run it again, and `-WhatIf` once more, to prove no change (acceptance criterion 2).
3. Verify on dev: seven attributes + option 7; a second `sprk_processingjob` row can reference the same `sprk_servicerequest` (criterion 1).
4. Export the entity XML (`pac solution export` of SpaarkeMaster, unpack) and update `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_ProcessingJob/Entity.xml` (job type options near :808-860, new attributes, 2 relationships) and the two touched entities' relationship metadata if the export includes them.
5. Remove the "not yet created on dev" status line from `docs/data-model/sprk_processingjob.md`.
6. Main session: `TASK-INDEX.md` status cell, POML `<status>`, `current-task.md`.
