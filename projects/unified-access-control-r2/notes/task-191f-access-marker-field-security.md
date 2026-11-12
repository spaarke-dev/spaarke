# Task 191f: field-lock the access markers (F1, #2179)

> Owner decisions 2026-11-11: Q1 = (a) field-lock the three columns; Q4 = (a) Restricted is internal-only, and Secure is how
> a record is shared externally; Q5 = (a) the whole environment. Design: `2179-flagged-external-dataverse-design.md` §3.6,
> §3.19 step 4, §4.1. Nothing was applied live: the dry run and -Verify in §5 are read-only.

rework: fix-round-1 task 191f (adversarial review R1-R7, §6)

## 1. What was built

- **Engine target** `AccessMarkerColumns` in `scripts/Set-DocumentPointerFieldSecurity.ps1`, the one field-security
  engine (tasks 133, 166 and 188). It takes (table, column) pairs, because the markers live on nine tables.
- **Entry point** `scripts/Set-AccessMarkerFieldSecurity.ps1`. It runs a dry run by default and takes `-Apply` and
  `-Verify`. It is a copy of `Set-DocumentNarrowingFieldSecurity.ps1`.
- **Profiles:**
  - Reader: the existing "Spaarke BFF-Managed Field Readers" (Read). It is on every business unit's default team,
    including the External Collaborators unit's. `Set-RecordCreatorPersonSchema.ps1` maintains it.
  - Writer: a new profile, "Spaarke Access Marker Writers" (Read, Create, Update), with the fixed id
    `60b9344e-f253-49cd-af1a-4be2cf52c3a1`.
    - Its members are every internal business unit's default team and the BFF application users. "Internal" means
      every default team except the External Collaborators unit's and the ontology writer unit's.
    - `-Apply` step (a0) creates the profile and adds the missing members before any column is secured. It then reads
      the members back, and stops with nothing secured if one did not land.
    - The script never removes a member. A wrong member is a FAIL: the External team, any other team, or any other user.
- **Comment change** in `scripts/Set-SecureFlagFieldSecurity.ps1` at line 47: it now points to the new target.
- **Tests** in `tests/scripts/Set-AccessMarkerFieldSecurity.Tests.ps1`: 19 tests on Pester 5.7.1, against a mocked
  Dataverse.

### 1.1 Columns (spaarkedev1, read-only, 2026-11-11)

- `sprk_accesspermission` (Picklist, global option set) is on 8 tables, all unsecured:
  - matter, project, work assignment and document;
  - communication, event and to-do;
  - agreement.
- `sprk_communication.sprk_isinternalonly` (Boolean) is unsecured.
- `sprk_communicationthread.sprk_privacystate` (Picklist) is unsecured.
- `systemuser.sprk_isexternal` is unsecured, and it stays that way.

## 2. Deviations from the POML

| # | Deviation | Why |
|---|---|---|
| 1 | `sprk_agreement.sprk_accesspermission` is in the target | The POML says "every table that carries it; confirm the live list". The live list includes agreement, which the POML's own list missed. (p9) now fails if a ninth table appears. |
| 2 | The target has its own writer profile instead of reusing "Spaarke BFF-Managed Field Writers" | That profile's members must be exactly the BFF users (its p3), and it also locks the document pointers and the secure flag. Internal teams on it could re-point documents. Precedent for a separate profile with a fixed id: `scripts/ebilling/Set-EbillingFirmReasonFieldSecurity.ps1`. |
| 3 | New preconditions p8 to p11 | **(p8)** Exactly one External Collaborators unit exists. Without it, flagged users still sit in an internal unit whose team writes the markers.<br>**(p9)** Every table that carries `sprk_accesspermission` is in the target.<br>**(p10)** Under `-Apply` only, each BFF application user holds System Administrator. Without that, the BFF reads a marker EMPTY in the seconds between securing a column and the grants (§3.3). The dry run and -Verify report it as INFO.<br>**(p11)** No other profile, the shared reader included, already grants create or update on a marker. Such a grant would survive the lock. |
| 4 | The entry point's solution default is `SpaarkeMaster`; the engine's other targets keep SpaarkeCore | On dev, SpaarkeCore lacks `sprk_agreement`. It carries `sprk_todo` and `sprk_communicationthread` without all subcomponents, so (p6) fails there. SpaarkeMaster carries all nine tables with `rootcomponentbehavior` 0, and the reader profile. |
| 5 | (p4) for this target is a count plus an operator confirmation | Internal users are writers by design, and §3 is the evidence for the writers. (p4) reports how many enabled flagged users are outside the External unit (INFO). It also requires `-BffOmitsMarkersFromCallerCreates` for `-Apply` (R1, §6). |
| 6 | `TASK-INDEX.md` (POML step 6) is not updated | The operator brief forbids editing `TASK-INDEX.md`. The main session updates it on merge. |

## 3. Writer census: every writer of the three columns, and its identity

After the lock:
- **App-only writes** by a BFF application user are allowed, because those users are on the writer profile.
- **Caller-identity writes** by an internal user are allowed, because that user's unit default team holds the writer.
- **Caller-identity writes** by a flagged user placed in the External unit are refused by the platform. AC-3 intends
  this.

### 3.1 BFF, app-only (writer profile through the BFF application users)

| Writer | Column(s) | Note |
|---|---|---|
| `CoreAncestorResolver` stamp, step 10, after the caller check at step 5 | accesspermission | Reads the ancestors app-only and stamps the inherited value on create |
| `CoreAncestorResolver.RefreshInheritedAccessPermissionAsync` | accesspermission | Runs after a document create |
| `ChildAccessPermissionReconciler` | accesspermission | The scheduled reconciler (task 189's own-narrowing record) |
| `SecureRootInheritance.Cascade.WritePermissionAsync` | accesspermission | The Secure root cascade |
| Office document create (`OfficeDocumentPersistence`) | accesspermission | |
| `CommunicationService` send, reply, forward and post creates | isinternalonly | New rows: the sender chooses on a new row; it never lowers an existing one |
| `CommunicationService`, the sent-.eml archive document | accesspermission | |
| `EmailUploadCaptureService`, `IncomingCommunicationProcessor` | isinternalonly, accesspermission | Email pipelines |
| `MessagingIngestor` (`Services/Communication/Channels/MessagingIngestor.cs`) | isinternalonly | Sets it on create (`:244`); the floor lowers it (`:153`) |
| `CommunicationInternalOnlyFloor.LowerWhenSafeAsync` | isinternalonly | The server decides to lower it, never the caller |
| `ThreadResolver`, `DirectThreadAccessService` | privacystate | |
| `RecordCreationService.Repair`, `DocumentProfileOutputMapper` | accesspermission | |

For the AppOnly and Playbook identities, `InheritedAccessPermission.RefuseAppOnlyWrite` (`ParentLineage.cs`) refuses
these values when a caller's payload names them:
- a root's `accesspermission`;
- a document's `accesspermission`;
- the `AccessColumns`, which include isinternalonly and privacystate.

It does not refuse `accesspermission` on to-do, event or communication. Those values are display copies that are never
trusted (I-16), and the reconciler and the stamp overwrite them.

**Escalation trigger 2 does not fire.** No BFF path writes these columns app-only for an existing row with a value the
caller chose. Every app-only write is one of:
- a new-row create, where the sender chooses at birth (for example the send-time `isinternalonly`);
- a value the server decides: the inherited stamp, the reconciliation, or the floor's lowering when it is safe.

### 3.2 Caller identity (writer profile through the internal unit's default team)

| Writer | Identity | After the lock |
|---|---|---|
| Field-mapping push `POST /api/v1/field-mappings/push` | Impersonated (`MSCRMCallerID`). `RefuseAppOnlyWrite` with the Caller identity skips AccessColumns | Internal caller: unchanged. Flagged caller: refused by the platform |
| Child-records `PATCH /api/v1/child-records/{table}/{id}` and the `/filing` routes | Caller OBO token (`OwnedChildWrite.cs`) | Internal caller: unchanged. Flagged caller: refused by the platform |
| Chat tool `dataverse.update_record` | Caller token | Internal caller: unchanged. Flagged caller: refused by the platform |
| A person-started playbook's Update Record step and output mapping | Impersonated | Internal caller: unchanged. Flagged caller: refused by the platform |
| Job B `CommunicationProposalApplyService` | Impersonated | Internal caller: unchanged. Flagged caller: refused by the platform |
| Chat tool `email.draft`, filed path (`EmailDraftToolHandler.cs:426-459`) | Checked as the caller, created app-only through `RecordCreationService` | **Breaks for internal users too until the BFF fix lands (R1, §6)** |
| Chat tool `email.draft`, unfiled path (`:478`) | Run as the user | Internal caller: unchanged. A flagged caller's draft that names an inherited level is refused (K6) |
| Model-driven forms: TrackingFieldTrio, `sprk_accesspermission_inherited.js` | The signed-in user | The `setValue` calls only revert a user's own change. Internal users: unchanged. A flagged user's form shows the field read-only |
| The native communication and thread forms | The signed-in user | Same as the row above |

### 3.3 Readers (a masked read must not change a decision)

- **BFF app-only readers map an empty Access Permission to "not Restricted":**
  - `SecureChildShareSynchronizer`, `RestrictedFilingShareBar`, `SpeContainerMembershipSync`, `OfficeEditAccessService`.
  - In the steady state they read through the writer profile.
  - During `-Apply`, they read through System Administrator, which (p10) requires.
- **Caller-identity readers map empty the same way:**
  - `OntologyAdminAuthoringService.ReadMatterAccessAsync` (through `ExternalParticipationService.FlagsFrom`).
  - The browser `sprk_access_ribbon.js` maps null to "show Share".
  - These readers read through the reader on every default team, which (p2) requires.
  - They are exposed only in the seconds between securing a column and the reader grant during `-Apply` (K5).
- **`InquiryRecordFacts`** reads `sprk_accesspermission` through the BFF app client. A null fails closed.
- **`CommunicationAccessFilter` and `CommunicationOpenThreadGate`** fail closed on a blank value.
- **The provisioning control plane and the ontology writer** neither read nor write the three columns.
  - The ontology writer unit's default team stays in no profile (H7b S24). (p2) and (p3) check this.
- **`MappingWriteGates.CopyableSourceColumns`** will not copy a secured source column into a system-principal mapping.
  - (p5) checks only targets: no field-mapping rule, AI topic-registry row or email update field targets the three
    columns on dev. Sources were not checked by the script.

### 3.4 Task 189 (escalation trigger 3 does not fire)

- Task 189's own/inherited classification compares the stored `sprk_accesspermission` with `sprk_accessinheritance`. It
  reads both app-only.
- Internal editors stay writers, so an internal edit is still recorded as an own narrowing exactly as today.
- Task 189 §2.4 kept `sprk_accessinheritance` unsecured on documents. This task does not touch that column.
- The only change is that a flagged user can no longer narrow or widen a document, which owner Q4 = (a) intends.

## 4. Known limits

| # | Limit | Disposition |
|---|---|---|
| K1 | **A caller create payload that names a marker is refused, internal callers included.** `CallerWriteAuthorization.NoFieldSecuredColumnAsync` (`CallerWriteAuthorization.cs:129-157`) refuses any caller create payload on the `RecordCreationService` routes that names an IsSecured column. Those routes are the child-records POST, chat `create_record` and `email.draft`. | `email.draft` is R1 (§6). No shipped client sends these columns in a create payload: `src/client` and `src/solutions` were grepped. Mapped and default values are dropped with a warning, not refused (`CheckMergedPayloadAsync`). If a chat model names the column in `create_record`, it gets a refusal and retries without it. All of this fails closed. |
| K2 | **The solution source.** `src/dataverse/solutions/SpaarkeMaster` carries IsSecured = 0 for these columns, so an unmanaged SpaarkeMaster import after `-Apply` may clear the lock. | Run `-Verify` after every import, and run `-Apply` again if a column reads unsecured. **Do not export the lock into the source** until provisioning runs this script and `Set-RecordCreatorPersonSchema.ps1`. A profile's team members never travel with a solution, and `Provision-Customer.ps1` runs neither script today. A new environment would then receive the markers secured with nobody on the reader: every non-administrator would read them EMPTY, and the readers in §3.3 that map empty to "not Restricted" would fail open. Wiring both scripts into provisioning is a follow-up (R3). |
| K3 | **The PUT shapes for IsSecured on a Picklist and a Boolean column are not proven live.** | The plain GET and PUT is the engine's existing pattern. The Boolean column retries once with `$expand=OptionSet`, as `Set-SecureFlagFieldSecurity.ps1` does, and a test covers it. A refused PUT leaves IsSecured unchanged, so the run stops and fails closed. |
| K4 | **Not proven live in this task:** a flagged user's form edit is refused; an internal user's edit is accepted; a flagged user's create of a communication. | This is the 191i AC-3 gate (POML notes). |
| K5 | **Masked windows.** During `-Apply`, each column reads EMPTY for non-administrators between its secure and its reader grant, a few seconds per column. A business unit created later reads the markers EMPTY until `Set-RecordCreatorPersonSchema.ps1 -Apply` gives its team the reader, and it cannot write them until this script's `-Apply` adds its team to the writer. | Run `-Apply` in a quiet window. After creating a unit, run both scripts, then `-Verify`, which names the missing team. |
| K7 | **Managed SpaarkeMaster (ADR-027 decisions 3 and 4, 2026-10-07).** A customer environment gets SpaarkeMaster managed by default. In that environment, step (a0) cannot create the writer profile in it, because components cannot be added to a managed solution. The create is refused before any column is secured, so the run fails closed. Changing IsSecured on a managed column may also be blocked by its managed properties. This is not proven. | Dev (unmanaged) is the target of 191i. For managed customer environments, the lock and the writer profile have to ship IN SpaarkeMaster, and the team links still have to come from the scripts (K2/R3). Both are part of the provisioning follow-up. The engine's other targets share this limit, since their profiles are in SpaarkeCore. |
| K6 | **A flagged user's unfiled `email.draft`.** On the run-as-user path, a flagged user's draft that names an inherited level is refused by the platform. | Flagged users only, and the refusal is fail-closed. 191i AC-3 should observe it. |

## 5. Dev run (read-only, 2026-11-11)

Dry run (the default mode) against spaarkedev1, exit code 0, nothing written:

- **(p1)** The reader exists and is in SpaarkeMaster. The writer would be created by step (a0).
- **(p2)** The reader is on every business unit default team (6).
- **(p3)** Those 6 internal default teams and the 2 BFF application users would be added to the writer.
- **(p4)** INFO only.
- **(p5), (p6), (p7), (p9), (p11)** OK. (p6) is OK for all 9 tables in SpaarkeMaster. With
  `-SolutionUniqueName SpaarkeCore`, it fails for agreement, to-do and thread (deviation 4).
- **(p8)** FAIL: no External Collaborators unit exists yet, because 191d is not applied. `-Apply` is refused until it
  is.
- **(p10)** Reported, not reproduced here.
- **Steps (a)+(b):** WOULD secure the 10 pairs and grant the reader and the writer on each.

`-Verify` against spaarkedev1 exits 1 with 30 named gaps:
- the writer profile is missing (p1);
- (p8);
- 6 teams and 2 BFF users are not on the writer (p3);
- 10 columns are not field-secured;
- 10 reader grants are missing.

The raw output stays outside the repo, because it lists System Administrator holders.

## 6. Adversarial review (one Opus pass) and disposition

| # | Finding | Class | Disposition |
|---|---|---|---|
| R1 | `email.draft` puts the inherited `sprk_accesspermission` in the item (`EmailDraftToolHandler.cs:1069-1071`). A draft filed under a matter, project or work assignment goes through `RecordCreationService` as the caller (`:450-459`), and step 5 (`NoFieldSecuredColumnAsync`) refuses a secured column for any caller. After the lock, every filed draft fails, for internal users too. | F1 (BFF, latent until 191i) | **Not fixed here.** It is BFF code, outside this scripts-only task's files, and the hygiene obligations of root CLAUDE.md §10 apply to it. The fix is to drop the column from the item on the `OwnedChildWrite.AppliesTo` path, because step 10 already stamps I-16 there (`RecordCreationService.cs:1101-1103`), and to add a test with IsSecured metadata. The lock now enforces the order: `-Apply` refuses without `-BffOmitsMarkersFromCallerCreates` (p4), and a test covers it. Reported to the operator for filing; it is a 191i prerequisite. |
| R2 | No must-fire test covered the (a0) read-back, the Boolean retry, or a revert on a non-document pair. | F3 | Fixed: three tests added. |
| R3 | Team links in a profile do not travel with a solution, so an exported lock would mask the markers in a new environment. | K | Recorded in K2 and K5. "Export the source" was removed from the live steps. Wiring provisioning is a follow-up. |
| R4 | §3.3 missed the caller-identity readers that map empty to "not Restricted". | F4 | Fixed (§3.3, K5). |
| R5 | `MessagingIngestor` was missing from the census, and the (p5) "sources" claim was too broad. | F4 | Fixed (§3.1, §3.3). |
| R6 | §5 named the dev business units and gave the flagged user's placement. | F4 | Fixed: counts only. |
| R7 | A create or update grant that another profile already held on a marker was caught only after securing. `$expectedTeamIds` was unused. | K / F4 | Fixed: (p11) refuses `-Apply` before anything is secured, and a test covers it. The unused variable was removed. |

## 7. Live steps for 191i (owner go; agents never run -Apply)

1. Deploy a BFF that no longer puts a marker in a caller create (R1).
2. Run `scripts/Set-ExternalCollaboratorRole.ps1 -Apply` (191d): it creates the External Collaborators unit and role.
3. Run `scripts/Set-RecordCreatorPersonSchema.ps1 -Apply`: it puts the reader on the new unit's default team.
4. Dry run `scripts/Set-AccessMarkerFieldSecurity.ps1 -EnvironmentUrl <env> -BffApplicationIds <ids>`. Every
   precondition must be OK, WOULD or INFO, with no FAIL.
5. Run the same command with `-BffOmitsMarkersFromCallerCreates -Apply` in a quiet window (K5).
6. Run it again with `-Verify`. It must exit 0 and print PASS.
7. Run the placement job (191e), then the live AC-3 gate: a flagged user's PATCH of a marker is refused, and an internal
   user's PATCH is accepted. Also check K6.

**Order relative to imports:** run steps 4 to 6 after the release's last SpaarkeMaster import. After any later import,
run `-Verify` again, and run `-Apply` if it reports a column unsecured (K2).
