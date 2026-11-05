# Task 046 notes - feature permission matrix and role guard

Branch `task/fm-046` from `origin/work/field-mapping-server-write-path-r1` at `643aefe0d7` (contains 031's and 042c's merges).
Rigor FULL (POML). Model opus, effort high.

## Outputs

| File | What |
|---|---|
| `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` §5.1 | The feature permission matrix, the role model, the writer-only-table rule, gaps G1-G8, the three named gaps. Nothing else in the file changed. |
| `tests/Spaarke.ArchTests/FeatureRolePrivilegeGuardTests.cs` | The declared table (`Requirements`, 148 privilege rows over 25 feature entries after fix round 1; 87 over 21 before), the role-file scan, the ratchet baseline (`KnownGaps`, 11 entries = G1 x2, G2-G6, G7 x2, G8, each with its held depth), the evidence-path check, must-fire and must-not-fire controls. |
| this file | Evidence, the dev read, the proposed role changes, the added scope. |

No `Roles/*.xml` file changed and no role, privilege or profile was written on any environment (`git diff --stat` of the branch lists only the three files above; every Dataverse call was a GET).

## Added scope (2026-11-05, from the coordinator)

1. **Delete (and Write) on the writer-only tables.** UAC-r2 and ontology asked that person-role Delete and Write on `sprk_signal`, `sprk_decisionrecord` and `sprk_budgetrevision` go into the matrix. Recorded as the rule "created, changed and removed only by their writers; person roles hold at most Read on them" (doc §5.1; worded as a rule in fix round 1). The BFF's generic paths already refuse these tables (042c create; 048 update and delete, branch `task/fm-048`). The role-level proposal keeps the app users' roles separate from the person roles (dev read: Spaarke Ontology Service is held only by the app users `mi-ontology-writer-dev` and `SDAP-BFF-SPE-API`; no app user holds a person role that the proposal changes) and leaves Service Deleter untouched. The proposal itself goes to UAC-r2 by message (see "Sent to UAC-r2").
2. **From 031:** the event-type gap (G2; Basic User alone holds no Create on `sprk_event`, so its missing event-type AppendTo matters only together with Office Add In User, which holds it at Deep); ebilling-r1's Procedure Read for Billing Administrator (G5); Console User and AppendTo on matter and project (no failing gap with a base role; doc §5.1).

## Derivation (code, not docs)

A code inventory of the closed feature list was taken read-only (file:line per privilege) and spot-checked: `ChildRecordEndpoints.cs:70-85` (`CreateTables`), `AnalysisAuthorizationFilter.cs:152,159`, `EventEndpoints.cs:66,131,928`, `QuickCreateSourceAccessFilter.cs:91,100,106`, `TodoSourceAccessFilter.cs:99,205,259`, `FinanceAuthorizationFilter.cs:335`, `RecordCreationService.RouteAuthorized.cs:40`, `MappingSourceReadProbe.cs:119,141,171`, `DecisionCallerRights.cs:42,61,79`, `RecordTableRules.cs:420,429,438`, `OfficeService.cs:2438`, `OfficeEndpoints.cs:185,1386,1429`, `FieldMappingEndpoints.cs:65`, `SignalWriter.cs:720`, `DecisionRecordWriter.cs:511`; the Report Card wizard posts through the BFF child-record route (`reportCardService.ts:154`, `withBffChildWrites`). Billing rows: `ProcedureAssignmentEndpoints.cs:114-133` on `origin/task/ebilling-089e` (`RequireReadOnTheOverridesSubject`).

Table ownership (dev metadata, GET `EntityDefinitions`): `sprk_eventtype_ref`, `sprk_analysisplaybook`, `sprk_aisearchindex`, `sprk_organization`, `sprk_procedure`, `sprk_reportcard` are user-owned; `sprk_recordtype_ref`, `sprk_mattertype_ref`, `sprk_practicearea_ref`, `sprk_projecttype_ref` are organization-owned. So the event type's AppendTo depth matters (G2), and the record type's does not.

Role model source: `docs/guides/office-addins-admin-guide.md` §7.5 (one base role, Basic or Core; Office Add In User optional) and `docs/guides/UAC-ACCESS-MODEL-GUIDE.md` §5 (Core User and Basic User). Confirmed on dev: the team `Spaarke` holds Basic User, Console User and Office Add In User together.

**Not repeated (§11):** Core User's AppendTo on wizard bind targets is `CoreUserWizardLinkingGuardTests` (config `core-user-wizard-linking-role.json`; it never requires Create, which is how G1 went unseen); the writer's AppendTo is `OntologyWriterAppendToGuardTests`; role depths per table are 060's.

**060:** not landed. §5.1 says 060's matrix goes beside it and links back here; 060 adds the link.

## Dev read (2026-11-05, read-only)

`RetrieveRolePrivilegesRole` on each Spaarke role's root-business-unit copy, privilege names from `privileges`; role holders from `roles?$expand=systemuserroles_association,teamroles_association`. Compared with the role XML by privilege name and depth:

| Role | XML | Dev | Difference |
|---|---|---|---|
| Access Administrator, AI Analysis Admin, AI Analysis User, Basic User, Console User, Office Add In User, Ontology Administrator, Ontology Service, Reporting Access Admin / Author / Viewer | equal | equal | none |
| Billing Administrator | 24 | 25 | dev holds `prvReadsprk_procedure` Deep (G5; ebilling-r1 089e applied it on dev; its file change is on that branch) |
| Core User | 781 | 782 | one privilege on dev only; passed to UAC-r2 by message (not reconciled here) |

## Proposed role changes (for UAC-r2; applying any of them is UAC-r2's, with the owner's go per change set, read-back and census)

| # | Role | Privilege | Depth | Feature it unblocks |
|---|---|---|---|---|
| G1 | Spaarke Core User | `prvCreatesprk_ReportCard`, `prvAppendsprk_ReportCard` | Deep | Report Card wizard |
| G2 | Spaarke Office Add In User | `prvAppendTosprk_EventType_Ref` | Deep → Global | Event (typed route) for Basic User + Office Add In User (the Event wizard is Core User only) |
| G3 | Spaarke Basic User | `prvAppendToContact` | Basic → Deep (its Read depth) | Office save to a contact; UAC-r2 decides for the guest base role |
| G4 | G3, or Spaarke Office Add In User | `prvAppendToContact` | Basic → Deep | Office quick create with an assignee, Basic User + Office Add In User |
| G5 | Spaarke Billing Administrator | `prvReadsprk_procedure` | Deep | Procedure override remove / preview (ebilling-r1 089e; already owner-approved, on dev) |
| G6 | Spaarke Basic User | `prvWritesprk_Document` | none → Deep | Office version save (Word FR-11) for Basic User; or UAC-r2 declares the version save Core User only (fix round 1) |
| G7 | Spaarke Basic User or Spaarke Office Add In User | `prvAppendToAccount`, `prvAppendToContact` | Basic → Deep | Event (typed route) regarding an account or a contact, Basic User + Office Add In User (G3's change covers the contact) (fix round 1) |
| G8 | Spaarke Basic User or Spaarke Office Add In User | `prvAppendTosprk_Budget` | none → Deep | Event (typed route) regarding a budget, Basic User + Office Add In User; or such events are Core User only (fix round 1) |
| WO | person roles | Create, Write and Delete on the three writer-only tables | — | D-204 (writers only); the role-by-role list is in the UAC-r2 message |

## Questions for UAC-r2 (no gap recorded until answered)

1. Is Office To Do meant for Basic User + Office Add In User? Neither role holds `prvCreatesprk_Todo`; the role guide lists To Do outside Office Add In User. Today it is a Core User feature.
2. Is the Console's Do-lane (Write on a Signal's To Do or event subject) meant for Basic User + Console User? Neither holds Write there. Today it is a Core User + Console User feature.
3. Should an add-on role be assignable without a base role? The matrix assumes not (both named Console and Office cases rest on it).

## Sent to UAC-r2

Prepared by this task and handed to the main session to send in UAC-r2's session (this agent has no channel to it): the matrix, G1-G5, the questions above, the dev/XML differences, and a session-only list of grants beyond what the features ask. **UAC-r2's answer: pending** (record it here when it arrives).

## Guard

- Must-fire: a role missing the privilege; held below the declared depth; a seeded new feature row whose role lacks it; a seeded copy of the Core User role file with `prvCreatesprk_Matter` removed.
- Must-not-fire: held at the declared depth (other casing); held deeper; held by another role of the set; a privilege no feature declares.
- Ratchet: unlisted gap fails; baseline entry with no gap fails ("remove it"); a listed gap passes (control `RatchetReportsUnlistedAndStale`). Since fix round 1 each entry records the held depth, and a listed gap held at another depth fails (`RatchetFiresOnAChangedHeldDepth`, `RatchetFiresOnASeededRoleFileWhoseGapGotWorse`).
- Evidence: every path an evidence segment names must exist in the repo (`EvidencePathCheckFires`).
- Load: missing, unparsable, empty or misnamed role file throws.
- Seeding proof (AC, recorded and reverted): the `Report Card wizard / prvCreatesprk_ReportCard` baseline entry deleted once → `FeatureRequirementsAreHeldByTheirRoles` failed with "Report Card wizard: prvCreatesprk_ReportCard at Basic or deeper in [Spaarke Core User] — held: none" (1 failed, 5 passed); file restored from a copy.
- Merge note: when ebilling-r1 089e merges, G5 stops reproducing and the guard asks for its baseline entry to be removed; whichever of 046 and 089e merges second removes it.

## Known limits

- K4: depths are declared from the ownership rule (team-owned rows need Deep; organization-owned any depth; rows owned outside the users' business units, such as event types and analysis playbooks and scopes, Global); a row owned by a user in another business unit is outside this static check (the live caller checks still decide). Account rows are taken as team-owned like contacts (G7); not read on dev.
- Not guarded (fix round 1): the chat create, update and delete tools and the push's child writes act on tables chosen at run time; the doc marks them.
- The guard reads source role files only; dev drift is this task's one-off read, not a recurring check.

## Component justification (CLAUDE.md §11), placement (§10)

One new test file, as the POML's justification: Existing - `PolicyReadRoleGuardTests`, `ProcedureRolePrivilegeGuardTests`, `CoreUserWizardLinkingGuardTests` guard fixed privilege lists for one purpose each; none ties a feature to its roles. Extension - a sibling that reuses their scan shape (`SourceScan.RepoRoot`, fail on a missing file) and defers to the Core User wizard-linking guard for bind-target AppendTo rather than repeating it. Cost of doing nothing - a role missing what its feature asks fails the feature for its users (G1 shipped unseen because the wizard-linking guard never requires Create). No `src/` change, no BFF change, no publish delta.

## Verification (tip = the commit holding this section)

| Check | Result |
|---|---|
| `dotnet build Spaarke.sln` | 0 Warning(s), 0 Error(s) |
| `FeatureRolePrivilegeGuardTests` | 7 / 7 passed (seeding proof above: 1 failed with the entry removed, restored) |
| `Spaarke.ArchTests` (whole project) | 1938 / 1938 passed |
| `verify-suites.sh` Sprk.Bff.Api.Tests filter | 6789 passed, 7 skipped (Office contract, pre-existing), 0 failed |
| Quality gates | Self-review against code-review Steps 3, 5, 6, 6.55, 6.6 and ADR-038: KEEP path (`tests/Spaarke.ArchTests/**`), must-fire and must-not-fire controls, ratchet with reasons, no file under `tests/unit/Sprk.Bff.Api.Tests/**`, no secret, no grant beyond a feature described in the repo. The adversarial pass is the main session's. |

## Fix round 1 (2026-11-05, independent review)

Branch `task/fm-046` at `51036cd193` (the work branch, task 048 included, merged in); line numbers are from that tree. No `src/` file and no role file changed.

| Finding | Outcome |
|---|---|
| F3 Decision commit never checks Read on the Signal | Fixed. Each Signal is read as the caller (`SignalCoreRecordAccess.cs:191-193`, called per Signal at `DecisionCommitService.cs:456-466`), then its core record (`:239-247`). Rows added for [Core, Console] at Deep (Signals and core records are team-owned): Read on `sprk_signal`; Read and AppendTo on the four core types (`CoreAncestorResolver.CoreRecordEntities`: matter, project, work assignment, service request; the AppendTo rows were matter and project only). All held. Doc row updated. |
| F3 analysis Read depths | Fixed. `sprk_analysisplaybook`, `sprk_analysisskill`, `sprk_analysisknowledge`, `sprk_analysistool` are UserOwned (`Entities/*/Entity.xml`), owned by their authors in any business unit; the rows are Global now (playbook: `RecordCreationService.RouteAuthorized.cs:150-165`; scopes: `AnalysisAuthorizationFilter.cs:833-835,881-892`). Core, Basic and AI Analysis User hold all four at Global: no new gap. |
| F1 Office version save Write | Fixed. `OfficeVersionSaveAuthorizationFilter` asks `write` on the document (`:53,89`, wired at `OfficeEndpoints.cs:194`). Nothing in code makes the version save Core-only, and the add-ins admin guide §7.5 says either base role saves documents from Word. Rows [Core] and [Basic] `prvWritesprk_Document` at Deep; Basic User holds none: **G6**. |
| F1 event regarding AppendTo | Fixed. No Office add-in path (and no client in the repo) posts `POST /api/v1/events`; the route accepts any of the eight regarding types (`Models.cs:1439-1449`) from any caller, with AppendTo asked through `event.attach_regarding` (`EventEndpoints.cs:131`, `OperationAccessPolicy.cs:255`). So both role sets the doc names are held to all eight at Deep. Core: all held. Basic + Office: account and contact held at Basic only (**G7**, two entries), budget not held (**G8**). |
| F4 rows missing for privileges the doc lists | Fixed where a table fixes the privilege: push source Read (7 tables, [Core]); record-keyed OBO upload AppendTo on the owning record (7 tables, [Core]); quick create source Read (6 tables beyond the assignee contact) and AppendTo on the matter or project a new project is filed under, for [Core] and [Basic, Office]; event regarding, version save and Signal Read (above). Chat create, update and delete and the push's child writes are marked "not guarded: table chosen at run time" in the doc. The doc's intro no longer says every privilege has a row. |
| F4 chat update and delete "as above" | Fixed: "any table the caller may update/delete; Signal, Decision Record and Budget Revision refused (048)". Update also states the I-20 invoice-family check on a change of an invoice-family row's core-record lookups (`OwnedChildWrite.cs:781-793`; D-96 lines follow their invoice). |
| F4 "Person roles hold Read on them" | Fixed: worded as the rule, "person roles hold at most Read on them, never Create, Write or Delete". |
| F4 Push wording | Fixed from the code: each child is updated as the caller (`FieldMappingEndpoints.cs:1221-1226`), so Dataverse asks Write on the child, Append when a lookup is written, and AppendTo on the record each written lookup names; a lookup moving the child re-files it (`:1186-1195,1257-1270`). There is no separate BFF AppendTo check on the new parent: it is the same Dataverse AppendTo on the written lookup. |
| K1 ratchet compares only feature and privilege | Fixed. `KnownGap` carries `Held` (the deepest depth the set holds, or "none"); `Gap.Held` is now that deepest depth; a listed gap held at another depth fails ("changed"), and the well-formed test refuses a baseline depth that is not a depth or "none", or that meets the row. Controls: `RatchetFiresOnAChangedHeldDepth` (worse, narrower, bogus level; must-not-fire: same depth in other casing) and `RatchetFiresOnASeededRoleFileWhoseGapGotWorse` (seeded Basic User copy). |
| K2 evidence checked only for shape | Fixed. Each `;` segment's leading path must exist under the repo root (`MissingEvidencePaths`); control `EvidencePathCheckFires` (missing path and pathless segment fire; present path does not; seeded drift: the Billing row's former path is reported against the real repo). The Billing row cited `ProcedureAssignmentEndpoints.cs`, which is not on this branch; it now cites `projects/ebilling-r1/tasks/089-assignment-override-service.poml:40,113` and says the route comes with ebilling-r1 089e. Every other path resolved. |
| K4 AI Analysis User alone | Fixed. The row is [Basic, AiUser] with the full create set: Create and Append on `sprk_analysis` (Basic), Read on the playbook and scopes (Global), Read and AppendTo on the document (Deep). All held. |
| K7 G2 wording | Fixed: "Event (typed route) for Basic User + Office Add In User (the Event wizard is Core User only)". |

**Failing first (scratch copies of role files, restored from copies after each run; `git status` clean after).** Mutations, one per finding: Console User minus `prvReadsprk_Signal` (F1); Core User `prvReadsprk_analysisplaybook` Global → Deep (F2); Core User minus `prvWritesprk_Document` (F3); Core User minus `prvAppendToAccount` (F4); Basic User and Office Add In User minus `prvReadsprk_Event` (F5, quick create source); Basic User and AI Analysis User minus `prvAppendsprk_analysis` (K4); Basic User minus `prvAppendToContact` (K1).
- Old guard (tip `51036cd193`) with all mutations: 7 / 7 passed (none seen).
- New guard with all mutations: `FeatureRequirementsAreHeldByTheirRoles` failed, listing six gaps: the playbook Read (held Deep), the Basic + AI Analysis Append (none), the event regarding account AppendTo (none), the version-save Write (none), the quick-create source Read on the event (none), the Signal Read (none); 9 others passed.
- New guard with the K1 mutation alone: failed with "Office save (Basic User): prvAppendToContact — held: none, baseline: Basic" (the old guard passed it: G3 was still listed).
- K2: the old guard passed with the Billing row naming a file not on this branch; the new guard with that evidence put back failed with "Billing: Procedure override remove / preview / prvReadsprk_procedure: src/server/api/Sprk.Bff.Api/Api/Finance/ProcedureAssignmentEndpoints.cs" (reverted from a copy).

**Verification (fix round 1).** `dotnet build tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj`: 0 Warning(s), 0 Error(s). `FeatureRolePrivilege` filter: 10 / 10 passed. Whole `Spaarke.ArchTests`: 1949 / 1949 passed. `Sprk.Bff.Api.Tests` not run (no `src/` change).

**For UAC-r2 (with the earlier message):** G6, G7, G8 above.
