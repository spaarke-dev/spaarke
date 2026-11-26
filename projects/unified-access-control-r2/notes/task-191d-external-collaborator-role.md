# Task 191d: the External Collaborators business unit and the Spaarke External Collaborator role

> 2026-11-11. Branch `task/uac-191d` off origin/master `ddae82fa90`. Rigor FULL, opus @ high. Nothing was applied live:
> the dev runs below are a dry run and a `-Verify`, both read-only, as the operator's az CLI identity.

## 1. What was built

| File | What it is |
|---|---|
| `config/external-collaborator-role.json` | THE definition: the unit (`External Collaborators`, parent root), the role (`Spaarke External Collaborator`, isinherited 1), 25 lineage tables at Basic, 17 never tables, 44 platform privileges, 5 organization-owned catalogs, and the default-team rules. |
| `scripts/Set-ExternalCollaboratorRole.ps1` | Dry run (default), `-Apply`, `-Verify`. `-Apply` + `-Verify` throws. `-Apply` writes only the unit, the role, `isinherited` and the role's privileges; never a user, team, share or profile. |
| `tests/Spaarke.ArchTests/ExternalCollaboratorRoleGuardTests.cs` | Pins the file to `SecureChildLineage` (roots + children): each registry table is lineage (Basic) or never; Basic only; no Delete/Share/Assign on lineage; never set exact; platform rules; must-fire controls (one per never table, twice: as lineage and as catalog) and must-not-fire (live file). |
| `tests/scripts/Set-ExternalCollaboratorRole.Tests.ps1` | Pester 5.7.1 over a mocked Dataverse; added to CI job `owner-teams-script-tests`. |

The role (105 privileges on dev's metadata):

- **Lineage, Basic.** Read, Write, Append, AppendTo, Create on matter, project, document, communication, event, to-do (the
  spike S1 set, design A.4). Read, Write, AppendTo on work assignment (D-1). Read only on the other 18 visible registry tables
  (agreement, analysis, analysis output, budget, budget revision, budget bucket, communication attachment / participant /
  thread, decision record, event log, file version, KPI assessment, memo, report card, signal, spend signal, spend snapshot).
- **Never: no privilege at all.** procedure, procedure version, procedure assignment, stored file, rule set, policy, policy
  version, email artifact, attachment artifact, email review log, affinity, and the invoice family (invoice, invoice
  submission, invoice approval, invoice correction, external identifier, billing event). All 17 exist on dev.
- **Platform (model-driven-app entry).** Taken from the platform `App Opener` role (Microsoft's minimum to run an app, read
  on dev), each at the LOWEST depth the privilege supports (the script checks this against metadata and refuses any other
  depth): prvReadUser Local, prvRead/WriteUserSettings Basic (design 3.2); team, business unit and role reads at Local (D-3);
  personal views, app metadata and UI settings at Basic; 28 metadata reads that exist only at Global (entity, attribute, form,
  view, web resource, app module, organization, and so on).
- **Catalogs.** Read + AppendTo at Global on the five organization-owned reference tables a matter or project form looks up
  (matter type, matter subtype, practice area, project type, practice-area document type), from the Office Add In User role.
  The script refuses a catalog that is not organization-owned.
- **Taken off on -Apply:** everything else, including the platform's injected SharePoint Create/Write (setup guide 5.4).

## 2. Decisions and deviations

- **D-1 Work assignment gets Read, Write, AppendTo.** Design 3.2 says "Basic Read on the other visible lineage tables", and
  S1 tested matters only. But Q3 = a (team share Read + AppendTo) is inert on a work assignment without the AppendTo
  privilege, and design 3.4 says Write comes from per-user shares on a ROOT (Assigned-To, Manage Access), which a work
  assignment is. No Append (a partner never re-files a work assignment) and no Create.
- **D-2 Excluded from App Opener:** workflow and process authoring and execution (Create/Write/Delete/Share/Assign workflow
  and process, prvFlow, prvWorkflowExecution, prvActivateSynchronousWorkflow), async operations, reports, workflow sessions,
  and the plugin trace log and trace log reads (diagnostic payloads can carry record data).
- **D-3 Team, business unit and role reads at Local, not App Opener's Global**, as the design already does for users
  (A.3): a flagged user sees its own unit only. Verified only when the 191i browser checklist runs.
- **D-4 The role is created IN the External Collaborators unit**, not the root (POML goal: "creates or updates the role in
  it"). Only that unit (and any unit below it) gets a copy; `-Verify` names any role of the same name elsewhere (ROLE PLACE)
  and `-Apply` refuses it.
- **D-5 POML step 6 (TASK-INDEX) not done here**: the operator reserved `current-task.md` and `TASK-INDEX.md` for the uac-r2
  session. The POML status is left for that session too, so the drift check stays consistent.

## 3. Owner questions (escalation triggers fired; nothing is applied until answered, 191i)

- **OQ-1 The default team's reader profile (POML constraint vs the profiles that exist).** Design 3.2 says the unit's
  default team receives "the document-link and secure-flag reader profiles only, never ebilling's Stored File reader
  profile". On dev those are ONE profile: `Spaarke BFF-Managed Field Readers` carries the secure flag, the document-link
  columns AND 15 `sprk_storedfile` columns (read-only probe, 2026-11-11). `Deploy-EbillingStoredFileSchema.ps1` (its
  `-ReaderBusinessUnits` check) already says this unit's default team must never hold it, and design A.5 says the unit stays
  out of that set. Meanwhile `Set-RecordCreatorPersonSchema.ps1 -Apply` adds EVERY default team to that profile (except the
  ontology writer unit's) and `Set-SecureFlagFieldSecurity.ps1 -Verify` fails a default team without it.
  - (a) **Recommended.** The default team holds no field-security profile. Flagged users read the secured columns blank in
    forms (secure flag, created-by person, document links); BFF surfaces are unaffected. The "every default team" scripts
    exclude this unit as they exclude the ontology writer unit: extend `scripts/common/OntologyWriterUnit.ps1` to a reserved
    set (one helper, all its callers). Without that, a routine re-run adds the team and `-Verify` reports it. The config
    ships with `defaultTeam.readerProfiles: []` (this option).
  - (b) Split the profile: a new reader profile with only the document-link and secure-flag columns for this team.
  - (c) Give the team the shared profile (column read only; the role has no row privilege on Stored File or invoices, so no
    row is reachable), and amend ebilling's rule.
- **OQ-2 Global privileges in the entry set (POML trigger: "a privilege above Basic").** A model-driven app does not open
  without metadata reads that exist only at Global (28 in the file; App Opener holds them) and the five reference tables are
  organization-owned (Global only). None of them holds customer rows. Default: accept them as declared (each at the lowest
  depth it supports, checked by the script). Alternative: a Web-API-only role (drop the 28 and the catalogs); flagged users
  then cannot use the model-driven app at all.
- **OQ-3 `sprk_eventtype_ref` is user-owned** (dev metadata), unlike the other reference tables. Basic would show a partner
  only event types it owns (none); Office Add In User holds Read at Deep. Not granted. Options: (a) leave it out
  (recommended: partners create events through the SPA/BFF, or pick no type); (b) Read + AppendTo at Global on it; (c)
  team-share the event-type rows (191c scope).

## 4. Known limits (one line each)

- K-1 The ArchTest pins the visible set as "registry minus never"; once 191b adds `ExternalMarker`, the guard should also
  assert lineage == registry tables whose marker is not `Never` (191b/191c follow-up, not done here to avoid touching 191b's file).
- K-2 `prvReadWorkflow` is at Basic: a business process flow added to a lineage form later would not load for flagged users.
- K-3 AppendTo is granted on the seven partner tables only; filing a new row under, say, a memo or an analysis is refused
  until a table is added (one line in the config).
- K-4 `-Verify` checks the unit, its default team and the role. Per-user drift (a flagged user outside the unit, a matrix
  role from another unit, a writer profile on a user) is the 191e census.
- K-5 A row a flagged user creates in the model-driven app is owned by that user, so `BU OWNS ROWS` fires until 191c's
  re-own runs. This is the intended alarm, not a false positive.

## 5. Dev runs (read-only, 2026-11-11; ids removed)

Dry run, exit 0 (the 105 `add <privilege> at <depth>` plan lines are omitted):

```
Mode        : DRY RUN (no writes)
Unit        : (none named 'External Collaborators')
Role        : (none named 'Spaarke External Collaborator' in 'External Collaborators')
Declared    : 105 privileges (51 lineage at Basic, 10 catalog at Global, 44 platform); never tables: 17
PLAN        : create the business unit 'External Collaborators' as a direct child of the root (its default team is created with it)
PLAN        : create the role 'Spaarke External Collaborator' in 'External Collaborators' (isinherited 1)
PLAN        : take off every privilege the platform injects on create and on AddPrivilegesRole that is outside the file (setup guide 5.4)
PLAN        : read everything back and check it as -Verify does; no user, team, share or profile is touched
GAP NOW     : BU MISSING: no business unit named 'External Collaborators' under the root.
GAP NOW     : ROLE MISSING: no role named 'Spaarke External Collaborator' in 'External Collaborators'.
DRY RUN: nothing was written. -Apply is an owner-go step (task 191i).
```

`-Verify`, exit 1: the same two gaps. A read-path probe (`-Verify` with a scratch copy of the config naming the existing
Secure Record unit, never committed) exercised the default-team, profile, field-permission and owned-row reads: they parse,
and they surfaced OQ-1 (`STORED FILE PROFILE` on that team's reader profile).

## 6. What later tasks must know

- **191c (team share):** the share target is this unit's default team; the role makes Read + AppendTo effective on every
  visible lineage table and AppendTo only on the seven partner tables (K-3). `BU OWNS ROWS` is the alarm for its re-own.
  When 191b's `ExternalMarker` lands, tighten this guard (K-1).
- **191e (placement and census):** assign the role from THIS unit (the role is created in it, D-4); move only users whose
  flag is definitely true. The census covers what `-Verify` does not (K-4).
- **191i (migration):** see the owner-go list in the PR. Answer OQ-1 to OQ-3 first.

## 7. Quality gates

- ArchTests: `ExternalCollaboratorRoleGuardTests` 46/46; full suite in section 8.
- Pester 5.7.1: 18/18.
- Review: the one Opus adversarial pass could not be launched (the session's concurrent-agent limit was full and the harness
  said not to retry); the review was done inline against the same checklist. Findings in section 8.

## 8. Results and review findings

- ArchTests, full suite: 2476/2476 passed (includes the 46 new ones).
- Pester 5.7.1, the whole `owner-teams-script-tests` job set (`Set-CustomerOwnerTeams` + this script): 86/86.
- BFF unit tests: CI's Full Unit Tests (all four shards) pass on the PR. Two local runs on a loaded machine could not
  finish (tests timed out at 2 to 4 minutes each from contention; the second run was stopped after 75 minutes). This
  branch changes no BFF or unit-tested code. One local failure is deterministic and comes from master, not this branch:
  `SecureChildLineageTests.ChildMirrorMask_OnEveryHardDenyTable_CarriesReadOnly` expects the hard-deny lineage children to
  be policy and policy version only, but master's registry also has rule set, procedure assignment and stored file (after
  the ebilling merge). It is not fixed here because that file is in 191b's change set; reported to the coordinator.
- CI Tier 1 Format failed on the first push (whitespace in one object initializer in the ArchTest); fixed with
  `dotnet format whitespace` in a follow-up commit.
- Inline review (in place of the Opus pass, see section 7). F2, fixed: the script accepted a platform depth of Deep
  that the ArchTest forbids; both now allow Basic, Local or Global only. Everything else checked
  (privilege reach, silent `-Verify` passes, `-Apply` write scope and order, test strength, Web API assumptions, each
  read path exercised on dev) gave no F-class finding; the known limits are section 4.
- Class grep (rule 6): no other codified role config grants Deep or Global on a lineage table to a role meant for flagged
  users (no such role exists before this one).

rework: fix-round-1 task 191d
