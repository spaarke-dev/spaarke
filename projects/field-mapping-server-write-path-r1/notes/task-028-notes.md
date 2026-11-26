# Task 028 — Analysis wizard + analysis-to-email mapping at send (FR-16, FR-17)

FULL rigor, sonnet @ xhigh, directional. Started 2026-11-25.

## Coordination (step 1)

- PR #1501: CLOSED, not merged. The project CLAUDE.md records it as superseded, so there is nothing to rebase onto.
- PR #2110: MERGED. `CommunicationService.cs` on master already carries it; the conflict check runs against master at edit time.

## Inventory (step 2, 2026-11-25)

There are two analysis creators in the client.

| Creator | Route | Client mapping |
|---|---|---|
| `Spaarke.AI.Widgets/.../CreateAnalysisWizardWidget.tsx` (the spec's "Analysis wizard"; hosted by the Console `WorkspacePane` and `InAppWizardHost`) | `POST /api/v1/child-records/sprk_analysis` via `withBffChildWrites(...).createRecord` (:912-913) | `applyFieldMappings(association parent -> sprk_analysis)` :894; `applyFieldMappings(sprk_analysis -> sprk_communication)` for the Send Email subject/body :958 |
| `Spaarke.UI.Components/.../Playbook/analysisService.ts` `createAndAssociate` (used by `PlaybookLibraryShell`) | `POST /api/ai/analysis/create` | none; the server maps Document -> Analysis (task 043) |

Widget payload (:807-907):
- user-entered: `sprk_name`, `sprk_description` (or null), the Attorney 1 / Paralegal 1 binds when picked, the agreement type bind when picked;
- the document bind (the uploaded or selected document; required);
- the regarding keys from `applyResolverFields` when an association is chosen;
- client-set: `sprk_worktype` (the user's work-type choice) and `sprk_analysisstatus = InProgress` (the wizard's own run state, not a mapped default).

`/api/ai/analysis/create` (`CreateAnalysisRequest`) takes only Name, DocumentId, skill/knowledge/tool ids, PlaybookId and ActionId. It has no description, work type, status, assignees, agreement type or regarding.

## Criteria conflict (escalated to the owner 2026-11-25)

The two criteria added from the task 043 review name `analysisService.ts` as "the Analysis wizard", and say "the wizard keeps creating the analysis through /api/ai/analysis/create". The wizard this task migrates (the widget) has never used that route; it creates through the child-records route. `analysisService.ts` belongs to the Playbook Library.

Recommendation:
- The widget stays on the child-records route and names the association parent as `mappingSource`, like the other wizards. With no association, no source is named.
- The 043 criteria apply to `analysisService.ts` / `PlaybookLibraryShell`: show the 201 `warnings`, and keep that path on `/api/ai/analysis/create`.
- Known limit: a Document -> Analysis profile is not applied to an analysis the widget creates. This is the same as today, since the widget maps only from the association parent.
- Moving the widget onto `/api/ai/analysis/create` would need a BFF contract change for six fields. That is out of scope.

**Owner decision (2026-11-25): as recommended.**

## Execution

- Master was merged into the work branch first: 83543888fe, `--no-verify`, a master merge. The BFF builds with 0 errors.
- Open PRs: none touch `CommunicationService.cs`, the widget, `createXrmEmailComposeHandlers.ts`, `analysisService.ts`, `PlaybookLibraryShell.tsx` or `composeOnlySend.ts` (checked 2026-11-25).
- The send path refuses an empty Subject or Body in `ValidateRequest`, in both the send-as-user and shared-mailbox branches. The fill step therefore runs in `SendAsync` before either branch.
- Dev has no `* -> Analysis` and no `Analysis -> Communication` profile. The live check needs temporary profiles, as task 024 did; each needs the owner's go.
- One implementer agent (Sonnet) works on `task/fm-028` from 83543888fe.

## Implementation (agent on `task/fm-028`, merged `--no-ff` as d1fef67b64)

Commits 125ac691ee (client), 2a7cf1d6bd (an analysis can be a mapping source read as the caller), 9b1d878e01 (server send step).

- **Widget** (`CreateAnalysisWizardWidget.tsx`):
  - No `applyFieldMappings` (grep empty).
  - Creates through `createRecordViaBffWithWarnings` on `POST /api/v1/child-records/sprk_analysis`, naming the association parent as `mappingSource` when there is one; the server's warnings are shown.
  - The Send Email follow-on sends the typed subject and body unchanged, filed under the new analysis.
- **Server**:
  - `Services/Dataverse/FieldMapping/SendTimeFieldMapping.cs`, called once from `CommunicationService.SendAsync` after the Message branch and before both email branches.
  - For a caller email with exactly one `sprk_analysis` association, it fills an EMPTY subject and/or body from the active `sprk_analysis -> sprk_communication` profile, fill-only. The source is read as the caller, put to the narrowing and the copy gate, and masked to the columns the caller can read.
  - No new endpoint, DI registration, package or route filter (ADR-008 path A, ADR-010).
- **FR-40**: `createXrmEmailComposeHandlers.ts` no longer sends `sprk_searchindexname` or the `sprk_AI_Search_Index` bind on the attachment's `sprk_document` create. `resolveUserBuDefaults` is gone from that handler: nothing else on the path read it.
- **043 criteria**: `analysisService.createAndAssociateWithWarnings` reads the optional `warnings` of the 201 body; `PlaybookLibraryShell` shows them (Fluent v9 MessageBar, tokens only) and shows nothing for a body with no `warnings`. That path stays on `/api/ai/analysis/create`.

## Review and fixes

The main session's review of the email step found that its gate (`isComposedEmailReady`) and the client send (`sendCommunication`) both refuse an empty subject, so the server fill could never be reached from the wizard.
- **Owner decision (2026-11-25): allow empty.** The Analysis wizard's email step lets the subject and body be empty. The server fills them, or refuses the send, which the user sees as a Finish warning; the analysis is kept.
- Built as explicit opt-ins, so every other wizard is unchanged:
  - `isComposedEmailReady(email, { allowEmptySubjectAndBody })`;
  - `SendEmailFollowOnStep allowEmptySubjectAndBody` (the body is not marked required);
  - `sendComposedEmail({ allowEmptySubjectAndBody })`;
  - `SendCommunicationOptions.serverFillsEmptySubject`, client-only and never sent.
- Commit fa92efe766. The widget test for an empty subject failed until the client send's check was opted out; that is how the second refusal was found.

One adversarial review (Opus) of d1fef67b64. Its findings, with the fixes (agent on `task/fm-028b`, commit b188946b6c, merged as b9e426ff78):

| Finding | Class | Fix |
|---|---|---|
| An analysis mapping source was not put to the external document narrowing that the analysis routes apply | F1 | `MappingSourceReader.AnalysisShown`, mirroring `DocumentShown`. Only the send fill gives it, using `AnalysisAuthorizationFilter.IsAnalysisDocumentHiddenFromCallerAsync`; a hide or a fault maps nothing. `TryResolveSourceEntitySet` / `CheckCallerReadOfSourceAsync` resolve an analysis only with `includeAnalysis`, so create and push still map nothing from an analysis, as before the task. |
| A mapped subject longer than `sprk_subject` (2000) would be sent and then leave no communication row | F1 | A mapped subject over 2000, or a body over 100000, is not used (docs/data-model/sprk_communication.md). |
| No test for a secure-owned analysis | F4 | Added, with an ordinary-team control. |
| The send test used mocks and DI under `unit/domain` | nit | Moved to `tests/integration/data-mutation/Communication/`. Added a test where the caller holds Read but the analysis is missing. |
| An empty `bffBaseUrl` is now refused | K | The only host (`WorkspacePane`) passes the session URL and already treats an empty one as not ready. |
| A bulk send refuses an empty subject before `SendAsync` | K | FR-17 covers the wizard's single send. |

Fail-before: 13 of the 63 affected server tests fail with the three fixes disabled. The secure-team, at-limit and shown controls pass both ways, as controls should.

Filed: DI-040 / #3126. A TYPED subject over 2000 characters is sent and then has no communication row. This was there before the task; the composer has no limit and the send's validation checks only for an empty subject.

## Gates

- Client: UI.Components jest, 83 suites / 1146 tests passed. `tsc --noEmit` clean; eslint 0 errors (1 old warning). AI.Widgets workspace jest, 20 suites, all passed. The widget test has 16 tests, including empty subject → sent empty, and no recipient → Finish disabled.
- First server run (on d1fef67b64, while an agent was also building): build 0/0, ArchTests 2793/2793. The filtered BFF tests had 5 failures, all Office/Document `WebApplicationFactory` contract tests that each took over 3 minutes. They were rerun on b9e426ff78 with nothing else running (below).
- Rerun on b9e426ff78 (the merge with the fixes):
  - build 0 warnings / 0 errors; ArchTests (built fresh) 2793/2793;
  - filtered BFF tests: 30 failures, every one an HTTP request in a `WebApplicationFactory` host that hung for about 3 minutes and was cancelled. They include `GetLinkedTodos_WithoutAuth_Returns401`, which never reaches the changed code: machine load.
  - The 25 failed classes rerun alone with `--no-build`: 398 passed, 0 failed.
  - The fix agent's own filtered run (Communication|FieldMapping|MappingSource|RecordCreation|ChildRecord|AnalysisAuthorization): 5413 passed, 0 failed, 2 skipped (live-env).
- Publish size (`publish-delta.ps1`, a copy using the paths `C:\tmp\f28m` / `C:\tmp\f28b`; Compress-Archive): master base e9393ace47 43,574,589 bytes against b9e426ff78 43,578,749 bytes. Delta **+4,160 bytes**, 194 files on both sides, 41.56 MB total.
- CVE (`dotnet list package --vulnerable --include-transitive`): no vulnerable packages.
- Placement: in the BFF, extending the existing send path (`CommunicationService.SendAsync`) with one static step beside the create-time mapping engine it reuses. No endpoint, DI registration, package or background work. ADR-008 path A: no route filter changed.

## Coordination during the task

- word-add-in-r1 task 138 (#2209, an email re-save adds the attachments it missed) touches `OfficeService.SaveAsync` and `UploadFinalizationWorker`, our W3 paths.
  - Conditions given 2026-11-25: children only through `ProcessSingleAttachmentAsync`; guards G1/G3/G5/G6 hold; full ArchTests; current rights re-checked on the duplicate path, failing closed.
  - Their diff was read against the guards: OK to merge, with two observations (the job is marked Completed before the worker runs; children follow the current record-first owner).

## Merge and code-page deploys (2026-11-25)

- PR #3133 merged to master at 13:39Z as 7f13cf2e26, on the owner's "merge when green". A Client lint fix went in first (2034bc91e2): the new fetch guard made four later checks always true, and two suppression counts were lowered by hand. A full local `prune` also rewrote unrelated entries because some local installs were stale, so it was not used. Every required check passed. The office-scope api-office leg was cancelled at its 20-minute cap again; it is not required, and the Tier 2 api-office leg passed.
- `sprk_spaarkeai` was deployed to dev by the `Deploy SpaarkeAi` workflow from 7f13cf2e26 (13:43Z). The live bundle has the email-step subtitle, the create guard text and `serverFillsEmptySubject`. A later master run (423cfc67f7, which contains 7f13cf2e26) redeploys it.
- `sprk_playbooklibrary` was deployed to dev at 13:49Z on the owner's go. It was built in a temporary worktree at 7f13cf2e26.
  - Bundle: SHA256 B2AA71FB…1387B40, 999,732 bytes. It has the create-warnings bar; the old dev bundle did not.
  - Read-back: the same SHA.
  - Rollback: the previous dev bundle (2026-11-24 22:06Z, SHA 6C0C6DBA…46E3866, 1,000,446 bytes), kept outside the repo.
- `Deploy-WizardCodePages.ps1` always republishes `sprk_wizard_commands` from source. That file is unchanged on master since 2026-10-31, and the earlier wizard deploys used the same script. Read-back SHA 22671FA9…1739D435 equals master's file.
- BFF: candidates come only from master and are deployed by UAC-r2. The go is given in UAC-r2's session ("BFF deploy <sha>"). The dev checks run after UAC-r2's LIVE notice.

## Live-check setup on dev (2026-11-25, owner's go)

- Created "FMSWP-028 live check: Matter to Analysis (temporary)" `62bac40d-dbe8-f111-84ea-00224829aef9`, active, with three One-time Copy rules:
  - `sprk_matterdescription` → `sprk_description` (`9eeea60e-dbe8-f111-84e8-7ced8d1d8f6b`);
  - `sprk_assignedtointernal` → `sprk_assignedtointernal` (`8abac40d-dbe8-f111-84ea-00224829aef9`);
  - `sprk_assignedattorney1` → `sprk_assignedattorney1` (`71d17711-dbe8-f111-84ea-002248359bb2`).
  - Deactivate it after the check.
- Test Matter: PAT-176903 "Targeted Protein Degradation Patent Issuance" (`232852f8-a263-f111-ab0c-000d3a582930`), team-owned (Spaarke Business Unit 1). Attorney 1 is Ralph Schroeder, Internal is Jake Schroeder, and it has a description. The 027 Matter `3fa0d616…` no longer exists on dev.
- The BFF on dev (ef0c566d2c) already serves `POST /api/v1/child-records/sprk_analysis` with a mapping source; 028 did not change that route. So the create half of the check can run before the BFF deploy.
- **Blocked: the Analysis → Communication profile.** `sprk_recordtype_ref` has no `sprk_communication` row on dev. The provisioning `DataverseWebApiRecordTypeSeeder` does not seed one either, so no environment has it. A profile's target is a lookup to that catalog, so FR-17's profile cannot be configured anywhere. The catalog is also the regarding-parent list that `PolymorphicResolverService`, `OwnedChildWrite` and the regarding pickers read, so adding a row is a product change, not test setup. Waiting on the owner's decision. Not created.

## Communication record type (fix, 2026-11-25, owner: "check and fix")

**Impact check (read-only).** No reader lists the catalog into a picker; the picker catalogs are written in code. Two readers list every row:
- the core-ancestor reconciliation job's id-to-table map, which skips Communication because it is not in `CoreAncestorResolver.CoreRecordEntities`;
- the identifier reverse-lookup roster (Rung 0), which takes only the seven core types that have a number column, so Communication (no number column) never joins it.

Every other reader looks up a row by name or id. `OwnedChildWrite.RegardingPairTables` already lists `sprk_communication`, and the client `PolymorphicResolverService` looks up by name. The one behaviour change is that a record filed under a communication (`TodoRegardingBuilder`, `OfficeSearchService`, `ExternalDataService`, `EventEndpoints`) now gets its pair type; until now that was skipped silently. The `sprk_billingevent` row is seeded by the eBilling schema scripts, not by this seeder, so it is not a gap.

**Fix.**
- `DataverseWebApiRecordTypeSeeder` seeds `sprk_communication` (Communication, `COMM`, `sprk_name`, `sprk_regardingcommunication`, no number field).
- Test `Task028_EmptyTable_CreatesTheCommunicationRecordType_…` fails without the seeder line; the 15 seeder tests pass.
- The admin guide gains "Email subject and body from an analysis" and a troubleshooting row for environments provisioned before this change.

**Dev (owner's go):**
- catalog row `6b59ba77-dee8-f111-84e8-7ced8d1d8f6b`. It is permanent: it is product reference data, not test data.
- "FMSWP-028 live check: Analysis to Communication (temporary)" `4e88e776-dee8-f111-84ea-00224820fd20`, with rules:
  - `sprk_name` → `sprk_subject` (`7059ba77-dee8-f111-84e8-7ced8d1d8f6b`);
  - `sprk_description` → `sprk_body` (`b14beb75-dee8-f111-84ea-7ced8ddb6f85`).
  - Deactivate the profile after the check.

**UAC-r2 review of #3148: no objection.**
- `SecureRootInheritance` treats a Communication pair as `NotAFilingParent`, and the typed lookup still carries lineage. Before, the type was `Unknown`.
- `NoAccessShareEnforcer` sees `NotASecureRoot`, as before.

Their one ask is the pair-agreement rule: a pair typed to Communication must come with `sprk_regardingcommunication` holding the same id. Checked in code:
- `TodoRegardingBuilder` sets the typed lookup before `ApplyResolverPairAsync`.
- `TaskActionCore` (event tasks) sets `entity[regardingField]` before the same call.
- The client `applyResolverFields` binds the typed lookup when its navigation property exists. The wizard target lists (`TODO_REGARDING_TARGETS`, `EVENT_REGARDING_TARGETS`, `COMMUNICATION_REGARDING_TARGETS`) are matched to each table's live schema, so Communication is offered only where the lookup exists.

Still to do: one live create on dev each of a To Do and an Event under a communication, with the live check, on the owner's go.

## Live check on dev (2026-11-25 16:35–16:46Z, owner: "Yes, run it")

Run in the owner's Chrome from the Console (`sprk_spaarkeai` from master 7f13cf2e26): Quick Start → Analysis → NDA Analysis, an existing document (Brightwater Mutual NDA - Test 3.docx), the description left empty, Assigned Attorney picked as Chelsea Friez, follow-on Send Notification Email to the owner's work address. Dev BFF a1b7db1def (UAC-r2's deploy, 30-minute gate passed 16:29Z).

| Run | Matter | Analysis | Result |
|---|---|---|---|
| A | PAT-176903 (Access Permission **Restricted** since 2026-11-12) | `672f06d4-989e-4178-b175-b2cc9cefb5ba` | **Correct refusal to copy.** Wizard warning: fields not copied because the source is Restricted (022's `MappingSourceCopyGate`: a Restricted record's values go only into a record that is Restricted when created). Analysis: attorney Chelsea kept, no description, no internal, owner = the Matter's team. The email had an empty subject and body; with no description the body stayed empty, so the send was refused ("Body is required") and no communication row was written. This matches the wizard's stated rule: if nothing fills it, the email is not sent. |
| B | MAT-000113 (Standard; attorney 1 and internal Ralph Schroeder; has a description) | `d9eb3a64-4e69-44d2-b1a3-9a684fe059db` | **PASS.** Description = the Matter's description; internal = Ralph Schroeder (filled); attorney 1 = Chelsea Friez (picked value kept); owner = Spaarke Business Unit 1 (the Matter's team). No warnings. Email (empty subject and body) → communication `a66483b3-eee8-f111-84ea-00224829aef9`, status Send: subject = "FM-028 live check B" (the analysis name), body = the analysis description; `sprk_regardinganalysis` = the analysis; core Matter MAT-000113 stamped. |
| C | MAT-000113 | `641664b0-bee7-4cdb-99e4-8a63cd11eef7` | **PASS.** Email with typed subject "FM-028 typed subject C" and empty body → communication `a81a180b-efe8-f111-84ea-00224829aef9`, status Send: the typed subject was kept and the body was filled from the analysis description. |

**UAC-r2's ask (pair agreement): PASS.** Both were created from communication B's form, through its Related To Dos and Events lists, which open the To Do and Event wizards. Neither was refused.
- To Do `3de575e9-5fa3-4b26-b689-5dd59c6d3df6`;
- Event `b63bdc13-dba8-4533-98e1-bc719ddfb385`.

Both rows have:
- `sprk_regardingrecordtype` = Communication (`6b59ba77…`);
- `sprk_regardingrecordid` = `sprk_regardingcommunication` = `a66483b3…`;
- core Matter MAT-000113;
- owner = the Matter's team.

**Observation, not a defect:** the outbound communication rows carry `sprk_regardingrecordid` and the typed `sprk_regardinganalysis`, but no `sprk_regardingrecordtype`. The outbound sender never writes that column, by design (`CommunicationContainerResolver` remarks; `CommunicationService.MapAssociationFieldsAsync`). The pair id still matches the typed column.

**Cleanup:** both temporary profiles were deactivated at 16:47Z (`62bac40d…`, `4e88e776…`: state Inactive). The catalog row `6b59ba77…` stays as product reference data. Test rows left on dev: analyses A, B and C; communications B and C; the To Do; the Event. The Compose tabs the wizard opened in the Console were not saved.

The PAT-176903 note under "Live-check setup" above was written before the Restricted flag was noticed. For a mapping check, use a Standard Matter such as MAT-000113.

Task 028 is done except for the owner's dark-mode look.
- 2026-11-25: the final 138 diff (3bb88235bc, PR #3157) was checked against the four conditions.
  - All four pass; ArchTests 2823/2823 at its head.
  - Known limits sent to word-add-in-r1:
    - no guard scans the attachment-add planner for creates;
    - the fault test is weak;
    - add-job children take the current record-first owner, not the parent .eml's;
    - an untested null access decision fails closed.
