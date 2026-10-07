# Task 083 — an Office-pane To Do names the person it is for (#1044, writer half)

> **Date**: 2026-10-04 · **Rigor**: FULL · sonnet @ high · **Status**: code-complete, uncommitted (main session commits/PRs). Live check + publish size OWED to the main session.

## 1. Gate and escalation triggers

- UAC-r2 task 141 is merged; its contract is `projects/unified-access-control-r2/notes/141-link-contract.md`. §6 is written for this task: resolve the caller's systemuserid with the existing task-067 resolver, then `IIdentityNormalizationService.ResolveAsync(systemUserId, ct).ContactId`. Two contacts on one oid, or an inactive contact, give `null`. `ContactIdentityBinder` is NOT called from the Office path.
- **Trigger 1 (link unavailable for most users): did not fire.** Live dev 2026-10-04 (main session): 10 of 12 enabled interactive systemusers have `sprk_primarycontact`. The 2 without are the known flagged accounts (eyal.iffergan, the hotmail guest).
- **Trigger 2 (152 matches on something other than Assigned To = the user's contact): did not fire.** UAC-r2 task 152 is merged (`0a96dd3e1`). Its people-targeting surface (`MembershipResolveOptions.PeopleTargeting`, ADR-034 A3) matches (a) a HUMAN `createdby`, (b) user-valued owner, (c) registry Contact-typed "Assigned *" columns (incl. `sprk_todo.sprk_assignedto`) against `PersonIdentity.ContactId` from the same `IIdentityNormalizationService` I read. Its note (§9) and `AssignedToDefaults.cs` header both state the Office `CreateTodoAsync` default is this task's and that this task is what makes Office To Dos visible. Because the app user is not human, Created By never matches an Office To Do, so Assigned To is the only term that finds it, as agreed.

## 2. Change

`OfficeService.CreateTodoAsync` (the code moved from the POML's `:2875`; located by name): when `request.AssignedToContactId` is absent or empty, `sprk_assignedto` = `IIdentityNormalizationService.ResolveAsync(<caller systemuserid>).ContactId`. The systemuserid is the `ownerSystemUserId` the endpoint already resolves with `ICallerSystemUserResolver` (task 067), so no second resolution. A resolver fault or no contact leaves the column absent and logs one `todo_assignee_unset` warning naming the caller (a fault logs its own warning first); the create is never refused (no new error code, ADR-019). An explicit assignee is untouched. The owner team (080) is untouched.

- New ctor parameter `IIdentityNormalizationService` (required, `ArgumentNullException`, ADR-032). It is a singleton registered unconditionally (`MembershipModule`); `RecordCreationService` (task 152 A7) injects it the same way. No new endpoint, DI registration, package, column or error code.
- Placement (CLAUDE.md §10): extends an existing BFF service; no new component (§11).
- Task 091's contact-search email field is not touched.

## 3. Tests (ADR-038 KEEP path `tests/integration/data-mutation/RecordOwnership/OfficeRecordOwnershipTests.cs`)

3 added: (1) no assignee + linked contact -> `sprk_assignedto` = caller's contact and `ownerid` still the resolved team; (2) explicit assignee wins; (3) no assignee + no linked contact -> 201, `sprk_assignedto` absent, warning logged naming the caller. Supporting change: `TodoRegardingTestWebAppFactory` (in `OfficeTodoRegardingContractTests.cs`) gained `CallerResolver`, `Identity` and `Logs` doubles with constructor defaults (unresolved caller, no contact). The defaults matter: a first draft without them made 3 pre-existing To Do tests return 500, because a loose Moq returns null for the sealed `CallerSystemUserResolution` record. No `Mock<HttpMessageHandler>`, no DI-registration or ctor-null tests.

**Seeded negative control** (criterion 4): changed the new `else {` to `else if (Guid.NewGuid() == Guid.Empty) {` (a plain `if (false)` fails the build with CS0162 under warnaserror; the non-constant form compiles).
- RED: `CreateTodo_NoAssignee_DefaultsSprkAssignedToToTheCallersLinkedContact_OwnerTeamUnchanged` -> `Failed ... [23 s]`, `Total tests: 1, Failed: 1`.
- Restored; `diff` against the pre-seed backup: IDENTICAL; rebuilt.
- GREEN afterwards: `OfficeRecordOwnershipTests` + `OfficeTodoRegardingContractTests` 18/18 (before the seed: 18/18).

## 4. Gates

| Gate | Result |
|---|---|
| Build | `Sprk.Bff.Api` and `Sprk.Bff.Api.Tests`: 0 warnings, 0 errors |
| Other `OfficeService` construction sites | none (`new OfficeService(` has no hit in `src` or `tests`; the host builds it by DI, exercised by every Office host test) |
| Added + Office/ownership classes | 18/18 |
| Full BFF suite (single process, `Sprk.Bff.Api.Tests`) | **14,226 pass / 6 fail / 54 skip (14,286)**. All 6 failures took 2m42s-2m52s and ended within one second of each other (host-startup starvation under a 27-minute full run): `OfficeSaveDocumentStampContractTests...CorruptPackageBytes`, `OfficeImmutableSaveFileSafetyTests...ByteIdentical...`, `OfficeSaveAsNewDocumentLinkGraduateTests...CopyMadeByTheOverride`, `ExternalAccessContractTests.ProjectReadRoute_SecureProject...`, `Issue1084_OfficeJobRecordDurabilityTests.RetryOfASaveWhoseRequestDied`, `InsightsSearchEndpointContractTests.PostSearch_MissingQuery`. Re-run in isolation (the six classes): **118/118 pass**. So the clean equivalent is 14,232 / 0 / 54. None touches the changed code path |
| ArchTests | **349 / 349** |
| CVE (`dotnet list package --vulnerable --include-transitive`) | no vulnerable packages; no package changes |
| Publish size (§10) | **OWED to the main session**: this worktree has been built repeatedly, so a number from here is not comparable (§10 hazard 3). Expected ~0 MB: +54 lines, no package or new type |

## 5. Step 9.5 review (code-review + adr-check, coverage-first; self-applied per the skills)

| # | Finding | Sev / conf | Action |
|---|---|---|---|
| 1 | A resolver fault logs two warnings (the fault, then `todo_assignee_unset`) | Suggestion / high | Kept: same shape as `RecordCreationService.ApplyMakerAssignedInternalAsync`; the test asserts `Contain`, not exactly one |
| 2 | `OfficeService` ctor is now 18 parameters (was 17) | Warning / high | Pre-existing, already triaged in 080 F16 as "not in scope"; the natural cut is moving the To Do leg into its own service (task 059 track). Not done here |
| 3 | `IIdentityNormalizationService` lives under `Services/Ai/Membership`; ADR-013 forbids CRUD injecting AI-internal types | Info / medium | Not an AI-capability type; `RecordCreationService`, `TaskActionCore`, `CreateTaskNodeExecutor` and `ActionSeam` already inject it directly. No path A/B/C needed |
| 4 | Test-double fix: loose Moq returns null for a sealed record -> 500 in 3 older tests | Resolved | Constructor defaults added (see §3) |
| 5 | The `LogCapture` copy duplicates `ProvisionProjectTestFixture.LogCapture` | Suggestion / medium | Kept: the file's own remarks forbid touching the shared fixtures; a shared test helper is a later consolidation |
| ADR-002/019/032/038 | No plugin; invariant stays server-side; no new error code; required singleton, no kill-switch needed; KEEP path, no banned test shapes | Compliant | none |

No Critical findings, no ADR violation, no exception or amendment needed.

## 6. Post-080 Office To Dos with no assignee (criterion 6) — read-only dev query, 2026-10-04

`sprk_todo` rows with `createdon >= 2026-09-22`: **3**, all created by the BFF app user (`8793f4b0...`), all team-owned, **all with `sprk_assignedto` already set** (explicit assignee picked in the pane):

| Created (UTC) | Name | Owner | `sprk_assignedto` |
|---|---|---|---|
| 2026-10-03 12:41 | To Do Created in Word Add-in 10-3-2026 | team `09fbf21c...` | `2e419a4f...` |
| 2026-10-02 17:20 | To Do from Word Add in | team `cf15f587...` | `8e9918a9...` |
| 2026-10-02 17:18 | Word To Do Item | team `cf15f587...` | `2e419a4f...` |

All `sprk_todo` rows with `sprk_assignedto` null (16): none is Office-created after 080. 6 are "Invoice Pending" rows from the invoice rule (July-August, before 080; TodoGenerationService, UAC-r2 152's scope), and 10 are user-owned rows created directly by `1d02f31c...` (June-August). **So no Office To Do needs stamping and no backfill script is needed.** (Proposed, only if one appears before deploy: a dry-run-first script that sets `sprk_assignedto` from `systemuser.sprk_primarycontact` of the To Do's initiator, with a write-ahead reversal manifest like `Backfill-RecordOwnership.ps1`. Not written: nothing to stamp.)

## 7. Criteria

| # | Criterion | Status |
|---|---|---|
| 1 | no assignee + linked contact -> `sprk_assignedto` = caller's contact, owner still the team | PASS (test 1) |
| 2 | explicit assignee wins | PASS (test 2) |
| 3 | no assignee + no link -> create succeeds, column absent, one warning naming the caller | PASS (test 3) |
| 4 | seeded negative control, both outputs | PASS (section 3) |
| 5 | live check: Office To Do appears in Test User 1's briefing | **OPEN**: needs this change deployed and 141's link live for Test User 1 (152 is merged). Main session: create an Office To Do with no assignee as Test User 1, record `sprk_assignedto`, then check the briefing |
| 6 | post-080 To Dos with no assignee | PASS: none exist (section 6) |
| 7 | full suite reconciled; ArchTests; publish delta | suite + ArchTests PASS (section 4); publish delta **OWED** |
