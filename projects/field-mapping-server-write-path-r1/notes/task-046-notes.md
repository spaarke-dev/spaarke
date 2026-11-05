# Task 046 notes - feature permission matrix and role guard

Branch `task/fm-046` from `origin/work/field-mapping-server-write-path-r1` at `643aefe0d7` (contains 031's and 042c's merges).
Rigor FULL (POML). Model opus, effort high.

## Outputs

| File | What |
|---|---|
| `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` §5.1 | The feature permission matrix, the role model, the writer-only-table rule, gaps G1-G5, the three named gaps. Nothing else in the file changed. |
| `tests/Spaarke.ArchTests/FeatureRolePrivilegeGuardTests.cs` | The declared table (`Requirements`, 87 privilege rows over 21 feature entries), the role-file scan, the ratchet baseline (`KnownGaps`, 6 entries = G1 x2, G2-G5), must-fire and must-not-fire controls. |
| this file | Evidence, the dev read, the proposed role changes, the added scope. |

No `Roles/*.xml` file changed and no role, privilege or profile was written on any environment (`git diff --stat` of the branch lists only the three files above; every Dataverse call was a GET).

## Added scope (2026-11-05, from the coordinator)

1. **Delete (and Write) on the writer-only tables.** UAC-r2 and ontology asked that person-role Delete and Write on `sprk_signal`, `sprk_decisionrecord` and `sprk_budgetrevision` go into the matrix. Recorded as the rule "created, changed and removed only by their writers; person roles hold Read" (doc §5.1). The BFF's generic paths already refuse these tables (042c create; 048 update and delete, branch `task/fm-048`). The role-level proposal keeps the app users' roles separate from the person roles (dev read: Spaarke Ontology Service is held only by the app users `mi-ontology-writer-dev` and `SDAP-BFF-SPE-API`; no app user holds a person role that the proposal changes) and leaves Service Deleter untouched. The proposal itself goes to UAC-r2 by message (see "Sent to UAC-r2").
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
| G2 | Spaarke Office Add In User | `prvAppendTosprk_EventType_Ref` | Deep → Global | Event (typed route and wizard) for Basic User + Office Add In User |
| G3 | Spaarke Basic User | `prvAppendToContact` | Basic → Deep (its Read depth) | Office save to a contact; UAC-r2 decides for the guest base role |
| G4 | G3, or Spaarke Office Add In User | `prvAppendToContact` | Basic → Deep | Office quick create with an assignee, Basic User + Office Add In User |
| G5 | Spaarke Billing Administrator | `prvReadsprk_procedure` | Deep | Procedure override remove / preview (ebilling-r1 089e; already owner-approved, on dev) |
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
- Ratchet: unlisted gap fails; baseline entry with no gap fails ("remove it"); a listed gap passes (control `RatchetReportsUnlistedAndStale`).
- Load: missing, unparsable, empty or misnamed role file throws.
- Seeding proof (AC, recorded and reverted): the `Report Card wizard / prvCreatesprk_ReportCard` baseline entry deleted once → `FeatureRequirementsAreHeldByTheirRoles` failed with "Report Card wizard: prvCreatesprk_ReportCard at Basic or deeper in [Spaarke Core User] — held: none" (1 failed, 5 passed); file restored from a copy.
- Merge note: when ebilling-r1 089e merges, G5 stops reproducing and the guard asks for its baseline entry to be removed; whichever of 046 and 089e merges second removes it.

## Known limits

- K4: depths are declared from the ownership rule (team-owned rows need Deep; organization-owned any depth); a row owned by a user in another business unit is outside this static check (the live caller checks still decide).
- The guard reads source role files only; dev drift is this task's one-off read, not a recurring check.
