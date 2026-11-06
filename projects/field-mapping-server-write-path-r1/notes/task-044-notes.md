# Task 044 — the Signal / Decision Record core pair is written only by the server and the ledger writers

> Branch `task/fm-044` (design, steps 1-3), then `task/fm-044-build` (step 4 on: `task/fm-044` + the work branch). Rigor
> FULL. Step mode prescriptive.
> **Status (2026-11-06): ready to apply.** The decisions (§0) are made; the script, the solution XML and the tests are
> built (§10). Nothing was written to any environment. UAC-r2 runs `-Apply` on dev with the owner's go (§12); the live
> check (AC6) follows it.

## 0. Decisions (2026-11-06)

UAC-r2 (relayed by the coordinator):
- **P1 (a).** A script, `scripts/Set-CorePairFieldSecurity.ps1`: dry run by default, `-Apply`, `-Verify`, modelled on
  `Set-EbillingFirmReasonFieldSecurity.ps1`. Per column: secure it, grant the reader and writer profiles straight away
  (revert the column if a grant fails), then publish both tables. The solution XML mirrors it, and the agreement test
  pins script, XML and code together. There is no hand-apply path.
- **P2 (1).** A new narrow profile "Spaarke Ontology Writer Core Pair Writers" with a fixed id, holding the four cells at
  Read/Create/Update 4/4/4. Its only member is `# mi-ontology-writer-dev`. The D-136 profile "Spaarke Ontology Writer
  Field Writers" gets none of these columns, and ontology-r1's `Set-ChildRecordCreatorPersonSchema.ps1 -Verify` stays
  as it is.
- **CallerWriteAuthorization.cs: no change.** UAC-r2 and ontology-r1 confirm it: `RecordCreationService.cs:182-187`
  refuses these tables before any caller check runs. The agreement test pins "no exemption" in place of the dropped AC4.
  AC6 now reads "a chat create with a core pair is refused with D-204's existing words".
- **-Verify conditions:**
  - (a) the Readers profile is on every business-unit default team (queried, never a fixed count);
  - (b) the BFF identities are in the BFF Writers profile, and the ontology writer is in the new profile;
  - (c) no other field-security profile grants any of the four columns.

spaarke-ontology-platform-r1 (relayed the same day) agrees to the new narrow profile, and confirms that the D-136
profile stays `sprk_createdbyperson`-only. It also confirms that the only writers of the pair are `SignalWriter` and
`DecisionRecordWriter`, through the writer managed identity. It adds two conditions, both built (§10):
1. `-Verify` proves that the server identities still read the pair (step (r) of the script).
2. The new writer profile keeps Read = 4. The writer reads the pair in `SignalSuppressionService`,
   `DecisionCommitService` and the decision action executors. The agreement test pins this.

Pre-apply state on dev (read 2026-11-06, and again by the script's dry run): 313 Signals and 80 Decision Records hold
both halves of the pair. None holds only one half.

## 1. Targets re-confirmed on this branch

| POML target | State on `cb61ddf317` |
|---|---|
| `sprk_Signal/Entity.xml` `sprk_CoreRecordId` :491, `sprk_CoreRecordType` :531 | Present at those lines, `IsSecured` 0 |
| `sprk_DecisionRecord/Entity.xml` `sprk_CoreRecordId` :717, `sprk_CoreRecordType` :757 | Present at those lines, `IsSecured` 0 |
| `Other/FieldSecurityProfiles.xml` readers ~:3, BFF writers ~:471, ontology writers ~:1163 | Present; lines moved: readers :3, "Spaarke BFF-Managed Field Writers" :799, "Spaarke Ontology Writer Field Writers" :1827 |
| `CallerWriteAuthorization.cs` step 3 ~:92-113 | Now `NoFieldSecuredColumnAsync` :128-158 (049 split the checks); the #1616 exemption is `BffManagedPairColumnsOf` at :136 |
| 052's 044(d) line | Present in 052's `<background>`; see §7 |
| 042c server-writer ArchTest | `tests/Spaarke.ArchTests/CorePairTypeImpliesIdGuardTests.cs` |

### CallerWriteAuthorization needs no change (for UAC-r2 to confirm)

`CheckCallerMayCreateAsync` has one production caller, `RecordCreationService` (`RecordCreationService.cs:662` and the
`.Mapping.cs` calls). That service refuses `sprk_signal` and `sprk_decisionrecord` at the start of the pipeline
(`RecordCreationService.cs:183`, `RecordTableRule.CreatedOnlyByItsWriter`, D-204), before any caller check runs. Step 3
is therefore never asked about these two tables. Without an exemption, step 3 would refuse a caller-sent core pair once
the columns are secured. That is the right answer if a future path ever reaches it, so the column set of
`BffManagedPairColumnsOf` stays unchanged. The task leaves `CallerWriteAuthorization.cs` alone, which also keeps it out
of task 045's way. The agreement test (§8) pins "no exemption" in place of the dropped AC4.

## 2. Profile design (AC1) — review copy

The four cells are `sprk_signal` and `sprk_decisionrecord` × `sprk_corerecordtype` and `sprk_corerecordid`.

| Profile | Members on dev (read 2026-11-06) | Read | Create | Update |
|---|---|---|---|---|
| Spaarke BFF-Managed Field Readers (`42e542c8-…13fc`) | the 6 business-unit default teams (all 6 in the environment) | 4 | 0 | 0 |
| Spaarke BFF-Managed Field Writers (`917b64c5-…614d`) | `# mi-bff-api-dev` (appId 5967251e-…309d), `SDAP-BFF-SPE-API` (appId 1e40baad-…458c); no team | 4 | 4 | 4 |
| Spaarke Ontology Writer Core Pair Writers (`169feee6-7a77-4d00-9e27-ffc27bee6262`, new; P2 (1)) | `# mi-ontology-writer-dev` (appId 69040982-…67c5); no team | 4 | 4 | 4 |
| System Administrator (platform) | System Administrator role holders | full, by platform rule | | |

No other principal gains or loses a column grant. These rows are the ones `sprk_createdbyperson` already has on both
tables in all three profiles (task 133 / D-136), so the four cells copy an existing shape.

Dev facts (read-only, operator `az` identity):
- All four columns: `IsSecured` false, and `CanBeSecuredFor{Create,Read,Update}` true. No alternate key, form, view or
  calculated column in the solution source names them.
- Both BFF application users and the ontology writer application user are enabled. The BFF users hold System
  Administrator. The ontology writer holds only "Spaarke Ontology Service" and is a member of the root default team, so
  it also reads through the reader profile.
- Rows today: 313 Signals and 80 Decision Records carry both halves; none carries one half only.

## 3. Writers of the pair and their identity (044(c))

| Writer | What it writes | Identity |
|---|---|---|
| `SignalWriter` (`SignalWriter.cs:1794-1795`, create at :653) | both halves at create; the reconcile update (:720) writes neither | ontology writer, through `OntologyWriterDataverseClient` (one credential path, no fallback identity) |
| `DecisionRecordWriter` (`DecisionRecordWriter.cs:3017-3018`, create at :2163) | both halves at create, copied from the Signal | ontology writer, same client |
| `SignalSuppressionService`, `DecisionCommitService` | read the pair and filter on it; updates carry other columns | ontology writer |
| BFF generic update paths | refuse the pair after create (`RecordTableRule.FixedAfterCreateColumns`, `CoreRecordPairUpdateWriterTests`) and refuse the tables whole (D-204, task 048) | — |
| `RecordCreationService` / chat `create_record` | refuse the tables whole (D-204, 042c fix round 4) | — |

Every server writer of the pair runs as the ontology writer identity, which is in a writer profile under either P2
option. Escalation trigger 3 does not fire.

## 4. Readers of the pair and how they still read it

| Reader | Identity | Read after the change |
|---|---|---|
| `RecordOwnershipResolver`, `SecureChildLineage`, `SecureChildShareSynchronizer`, `ParentLineage`, `SignalReevaluationJob`, `SignalClosureService`, `InvoiceIndexDocument`, `ApExportMapping`, `SignalReadService` app-only queries | BFF application user (System Administrator, and a writer-profile member) | unchanged |
| `SignalSuppressionService`, `DecisionCommitService`, `DecisionRecordWriter` lookups | ontology writer | writer profile (Read 4), and the reader profile through the root default team |
| `OntologyAdminAuthoringService.ReadPolicySignalsAsync`, `SignalReadService` and `SignalCoreRecordAccess` caller reads, `RecordFamilyReader` (impersonated) | the signed-in user | reader profile on every business-unit default team |
| Worklist, Decision Records widget, Family view, external SPA | none of them read the rows directly; all go through the BFF readers above | unchanged |
| Model-driven app (Advanced Find, grids) | the signed-in user | reader profile |

No reader loses Read, so escalation trigger 2 does not fire. The reader profile has to stay on every business-unit
default team. The apply step checks that first and refuses to write if a team is missing (§6 P1).

## 5. Grep proof (AC2) — no client writes either column

Run from the repo root on `cb61ddf317`:

```
grep -rn -i "corerecordtype\|corerecordid" src/client src/solutions \
  --include=*.ts --include=*.tsx --include=*.js --include=*.jsx | grep -v -E "node_modules|/dist/|/out/|/bin/|/obj/"
```
Result: 5 files. All are types, comments or labels: `office-addins/.../InvoiceView.tsx`, `invoiceSubmissionSeam.ts`
and `invoiceSubmissionService.ts` (the `InvoiceCoreRecordType` type name), and `Spaarke.UI.Components/.../Worklist/types.ts`
and `__fixtures__/worklistFixtures.ts` (doc comments). None is a write.

```
grep -rn -E "(createRecord|updateRecord)\s*\(\s*['\"](sprk_signal|sprk_decisionrecord)['\"]|sprk_signals\(|sprk_decisionrecords\(|/sprk_signals['\"\`]|/sprk_decisionrecords['\"\`]" \
  src/client src/solutions --include=*.ts --include=*.tsx --include=*.js --include=*.jsx | grep -v -E "node_modules|/dist/|/out/|/bin/|/obj/|__tests__|\.test\."
grep -rn -E "['\"](sprk_signal|sprk_decisionrecord)['\"]" src/client src/solutions --include=*.ts --include=*.tsx --include=*.js --include=*.jsx \
  | grep -v -E "node_modules|/dist/|/out/|/bin/|/obj/|__tests__|\.test\.|__fixtures__"
```
Result: no matches. The client surfaces that show Signals or Decision Records (Worklist, DecisionRecords,
RecordFamilyModal, the LegalWorkspace section registrations) read them through the BFF with `authenticatedFetch`.

Solution source: no form (`FormXml`) or view (`SavedQueries`) of either table names either column.

## 6. Points that needed a decision before step 4 (decided 2026-11-06: P1 (a), P2 (1); see §0)

### P1 — dev is applied by script; SpaarkeMaster is not imported there

Steps 6 and 7 of the POML say to import SpaarkeMaster to dev. `config/environments.json` sets dev to
`solutionPackageType: none` ("Authoring environment: SpaarkeMaster is authored here and never imported", T218f), and
`Deploy-Release.ps1` Phase 3 skips it. Every earlier secured-column change reached dev through an operator script with a
dry run, an apply mode and a verify mode. The SpaarkeMaster XML carries the same state for other environments, and an
agreement test pins the script and the XML together: `Set-RegardingPairFieldSecurity.ps1` (#1616),
`Set-DocumentPointerFieldSecurity.ps1`, `ebilling/Set-EbillingFirmReasonFieldSecurity.ps1` and
`ebilling/Set-EbillingResubmissionOfFieldSecurity.ps1`.

Options:
- **(a) Recommended.** Add `scripts/Set-CorePairFieldSecurity.ps1`, modelled on the firm-reason script, which already
  pairs the reader profile with an ontology-writer profile on `sprk_signal`:
  - preconditions: the reader profile is on every default team; the writer profiles' members are exactly the named
    application users; the BFF and ontology writer application ids are passed in;
  - secure each column, then grant the profiles immediately (revert if a grant fails);
  - report any other writing profile;
  - publish both tables.

  The agreement test also pins the script. UAC-r2 runs it on dev after the owner's go.
- (b) UAC-r2 applies the four `IsSecured` flags and the field permissions by hand from the XML. This leaves no verify
  step in the repo.

### P2 — which ontology-writer profile carries the Create and Update

The POML (UAC-r2, 2026-11-04) names "Spaarke Ontology Writer Field Writers", the D-136 profile. That profile belongs to
ontology-r1. Its maintenance script, `scripts/Set-ChildRecordCreatorPersonSchema.ps1` (:524-528, step (c2)), fails
`-Verify` on any row outside `sprk_createdbyperson` of its three tables, and `tests/scripts/Set-ChildRecordCreatorPersonSchema.Tests.ps1`
pins that check. Adding the four cells there therefore turns ontology-r1's own verify red. ebilling-r1 task 026 hit the
same rule and created a separate narrow profile ("Spaarke Ontology Writer Firm Reason Writers"; its script header records
why).

Options:
- **(1) Recommended.** A new narrow profile, "Spaarke Ontology Writer Core Pair Writers":
  - a fixed id in SpaarkeMaster;
  - its only member is the ontology writer application user;
  - it holds the four cells at 4/4/4 and nothing else;
  - it follows the firm-reason precedent and leaves ontology-r1's script and test untouched.

  This is a new profile with the same membership, so no other principal gains or loses a grant. It needs UAC-r2's
  agreement, because UAC-r2 owns profiles and named D-136.
- (2) Use the D-136 profile as the POML says, and widen ontology-r1's step (c2) and its Pester test to allow the four
  cells. This changes ontology-r1's files and needs its agreement.
- (3) Use the firm-reason profile. Rejected: its own script fails on any row other than `sprk_firmreason`.

Escalation trigger 1 ("wants a different profile") applies to either choice: (1) differs from the profile UAC-r2 named,
and (2) changes ontology-r1's guard. The owner or UAC-r2 picks.

## 7. Task 052's 044(d) line

The current text says the pass completes "a type with no id from the derived root or clearing the type with a warning
(042c's rule)". D-204 replaced that rule for these tables: the general create refuses them whole, and the writers write
both halves or neither (`CorePairTypeImpliesIdGuardTests`). Proposed replacement, to be confirmed by ontology-r1 before
052 is written:

> Task 044(d), revised 2026-11-06: after D-204 and 044, only the writers set the core pair, and they write both halves
> or neither. For rows created after go-live, the pass reports any Signal or Decision Record that holds one half only,
> and writes nothing (Decision Record is append-only). Whether it should ever complete or clear one needs ontology-r1's
> agreement.

## 8. Planned tests (step 4, after the decisions; as built: §10)

`tests/integration/data-mutation/Signals/CorePairFieldSecurityAgreementTests.cs` (KEEP path), shaped like
`FirmReasonFieldSecurityAgreementTests`:
- Solution: the four cells are `IsSecured` 1; the reader profile has 4/0/0 on each; the BFF writers and the chosen
  ontology writer profile have 4/4/4 on each; no other profile in the XML creates or updates them; a new profile is a
  root component in `Other/Solution.xml`.
- Code: the cells equal `RecordOwnershipContext.CorePairTables` × {`CoreRecordTypeColumn`, `CoreRecordIdColumn`}; each
  table is `CreatedOnlyByItsWriter` with both columns in `FixedAfterCreateColumns`; `BffManagedPairColumnsOf` is empty
  for both tables (no step-3 exemption).
- Script (P1 (a)): its constants agree with the above; must-fire seeded drifts (each column unsecured, a writer
  profile's Create or Update dropped, the reader's Read dropped, a table or column renamed, an exemption added) and the
  must-not-fire faithful seed.
- Seeding proof: set one `IsSecured` back to 0 once, record the failure, revert.

AC4 is dropped (D-204). AC6's "a chat create with a core pair succeeds" becomes "a chat create of either table is
refused with D-204's existing words".

## 9. Placement (CLAUDE.md §10)

No BFF code change is planned (§1), so the publish-size delta is 0. If P1 (a) is chosen, the new files are a script and
an integration test. Both are outside the publish output.

As built: no file under `src/server/**` changed, so the publish output is unchanged (delta 0, no package added, no new
CVE surface). The changed files are the solution XML, a script, its Pester test and harness, an integration test, the
CI workflow list and these notes.

## 10. Step 4 — what was built

| File | Change |
|---|---|
| `scripts/Set-CorePairFieldSecurity.ps1` | new: dry run (default) / `-Apply` / `-Verify`; preconditions (p0)-(p5), steps (a)-(e), readers check (r) |
| `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Signal/Entity.xml` | `sprk_corerecordid`, `sprk_corerecordtype`: `IsSecured` 0 → 1 |
| `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_DecisionRecord/Entity.xml` | the same two columns: `IsSecured` 0 → 1 |
| `src/dataverse/solutions/SpaarkeMaster/Other/FieldSecurityProfiles.xml` | Readers: four rows 4/0/0. BFF Writers: four rows 4/4/4. New profile "Spaarke Ontology Writer Core Pair Writers" (`{169feee6-7a77-4d00-9e27-ffc27bee6262}`): the four rows 4/4/4 and nothing else |
| `src/dataverse/solutions/SpaarkeMaster/Other/Solution.xml` | the new profile as a root component (type 70) |
| `tests/integration/data-mutation/Signals/CorePairFieldSecurityAgreementTests.cs` | new, KEEP path (36 cases) |
| `tests/scripts/Set-CorePairFieldSecurity.Tests.ps1`, `.Harness.ps1` | new: the script runs whole against a test double (29 cases) |
| `.github/workflows/ci-standalone-checks.yml` | the Pester file joins the `ontology-script-tests` job list (as 046b's did) |

`CallerWriteAuthorization.cs`, the writers and ontology-r1's script and test are unchanged (§0).

### The script

- **Preconditions** (every mode; `-Apply` refuses before any write unless all pass):
  - (p0) the four columns exist and can be secured;
  - (p1) the Readers and BFF Writers profiles exist exactly once;
  - (p2) the Readers profile is on every business-unit default team (the teams are queried);
  - (p3) every `-BffApplicationIds` user is on BFF Writers, which has no other member and no team;
  - (p4) the ontology writer's application user exists;
  - (p5) the row counts per table (both halves / type only / id only).
- **Steps:**
  - (a) create the new profile with its fixed id in SpaarkeMaster, and add the ontology writer to it. Another id, any
    other member, team or column on that profile is a FAIL, and `-Apply` stops there, before any column is secured;
  - (b) per cell, secure the column, then grant Readers (4/0/0) first, then BFF Writers and the new profile (4/4/4). A
    failed grant reverts that column, publishes both tables and stops. A failed revert prints "RECOVERY REQUIRED NOW";
  - (c) any other profile with any grant (read, create or update) on a cell is a FAIL. The platform's own System
    Administrator profile is excluded;
  - (d) the new profile is in SpaarkeMaster;
  - (e) publish both tables, once, and only when the run wrote something.
- **Readers check (r)**, ontology-r1 condition 1, run in every mode:
  - (r1) each BFF application user and the ontology writer holds System Administrator, or is a member of a profile
    (directly, or through a team) with read=4 on each cell;
  - (r2) live: one Signal and one Decision Record holding both halves are read again AS each identity
    (`MSCRMCallerID`; the operator holds System Administrator), and both halves must come back. First, a control read as
    a made-up caller id must be refused, so a server that ignored the header cannot pass. With no such row, (r2) is
    informational and (r1) stands.
  - Approach: the operator's `az` identity cannot sign in as the BFF identities, so (r2) reads as them by impersonation.
    A read-only probe on dev (2026-11-06) showed that the header is honoured: the ontology writer saw 0 workflow rows
    where the operator saw 1, and a made-up caller id was refused. Before the apply, (r2) passes trivially because the
    columns are not yet secured. The run that proves it is `-Verify` after `-Apply`.

### Tests (failed first, then pass)

- `CorePairFieldSecurityAgreementTests` (Sprk.Bff.Api.Tests, filter `~CorePairFieldSecurityAgreementTests`): 36 cases.
  - Before the XML change: 13 failed (the solution test, plus every seeded drift that needs the new rows), 23 passed.
  - After the change: 36/36 pass.
  - What it covers:
    - the script constants;
    - the solution: four cells `IsSecured` 1; Readers 4/0/0; BFF Writers 4/4/4; the new profile at its fixed id with
      exactly the four cells at 4/4/4 (Read = 4 is pinned, ontology-r1 condition 2); no other profile grants a cell;
      the root component;
    - the code: `CorePairTables` × the two columns equals the cells; both tables are `CreatedOnlyByItsWriter` with the
      pair in `FixedAfterCreateColumns`; `BffManagedPairColumnsOf` is empty for both (no exemption).
  - Must fire: 9 script drifts, 19 solution drifts (each cell unsecured, each profile's read, create or update dropped or
    missing, the reader given update, a stray column, the wrong id, the D-136 profile or another profile granting a
    cell, the root component missing) and 5 code drifts (an exemption added, a third table, one table, the general
    create allowed, the pair not fixed).
  - One-time file seeding proof: `sprk_Signal` `sprk_corerecordtype` was set back to `IsSecured` 0. The test failed with
    "sprk_signal.sprk_corerecordtype is not IsSecured 1" (1 failed / 35 passed). The file was reverted.
- `Set-CorePairFieldSecurity.Tests.ps1` (Pester 5.7.1): 29/29.
  - The first run failed 13: the script counted the keys of a single unrolled profile row as profiles. Fixed with
    `@(Get-Profile …)`.
  - Must fire: 17 `-Verify` drifts, including a seventh business unit, a masked live read, and a server that accepts any
    caller.
  - Also covered: a refused `-Apply` (a failed precondition; the new profile already holding a stranger), the revert on a
    failed grant, "RECOVERY REQUIRED NOW", and `-Apply` combined with `-Verify`.
  - Must not fire: the System Administrator profile's rows on the cells; a non-administrator BFF user reading through
    its profile; no rows to read back.
- Neighbours, unchanged and green:
  - the related agreement and record-table suites (filter: `~FieldSecurityAgreementTests`,
    `~FieldSecurityScriptAgreementTests`, `~ExternalAccessContactSecuritySchemaAgreementTests`, `~CorePair`,
    `~CallerWriteAuthorization`, `~RecordTableRule`): 380/380;
  - `Set-ChildRecordCreatorPersonSchema.Tests.ps1`: 43/43 (the D-136 profile is untouched).
- `tests/Spaarke.ArchTests` (rebuilt, 0 warnings):
  - the first full run failed one guard, `SchemaScriptSolutionMembershipGuardTests`, because the script dot-sourced the
    helper with the three-argument `Join-Path` form the guard does not recognise. Fixed;
  - then 1973/1973 pass.

## 11. Dry run against dev (2026-11-06, read-only; operator `az` identity)

The System Administrator application-user INFO lines are omitted below.

```
Mode        : DRY RUN (no writes)
Preconditions
  OK       (p0) each of the four columns exists and can be secured            (4 lines)
  OK       (p1) profile 'Spaarke BFF-Managed Field Readers' exists
  OK       (p1) profile 'Spaarke BFF-Managed Field Writers' exists
  OK       (p2) reader profile on default team ... (6 of 6 business units)
  OK       (p3) BFF app user '# mi-bff-api-dev' is on 'Spaarke BFF-Managed Field Writers'
  OK       (p3) BFF app user 'SDAP-BFF-SPE-API' is on 'Spaarke BFF-Managed Field Writers'
  OK       (p4) ontology writer application user '# mi-ontology-writer-dev'
  INFO     (p5) sprk_signal : 313 row(s) hold both halves, 0 the type only, 0 the id only
  INFO     (p5) sprk_decisionrecord : 80 row(s) hold both halves, 0 the type only, 0 the id only
(a) WOULD create profile 'Spaarke Ontology Writer Core Pair Writers' (169feee6-...) in SpaarkeMaster; WOULD add the ontology writer
(b) per cell: WOULD secure, WOULD grant Readers, BFF Writers, Core Pair Writers   (4 cells)
(c) OK no profile besides the reader, the two writer profiles and System Administrator grants any core pair cell
(d) WOULD 'Spaarke Ontology Writer Core Pair Writers' is created in SpaarkeMaster
(r) OK (r1) both BFF app users hold System Administrator; WOULD (r1) the ontology writer reads every cell once granted
    OK (r2) all three identities read both halves of one Signal and one Decision Record
DRY RUN complete - nothing was written.   (exit 0)
```

## 12. Apply, verify and rollback (UAC-r2, after the owner's go)

```
pwsh scripts/Set-CorePairFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
  -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c `
  -OntologyWriterApplicationId 69040982-612e-469e-a85f-26d5172367c5            # dry run
#   ... the same arguments + -Apply      (exit 0 and "PASS")
#   ... the same arguments + -Verify     (exit 0 and "PASS"; (r2) is the live read as each identity)
```

The script has no rollback mode. Rollback un-secures the four columns first, so that no reader is left masked, then
publishes both tables:

```
$u = 'https://spaarkedev1.crm.dynamics.com'; $api = "$u/api/data/v9.2"
$h = @{ Authorization = "Bearer $(az account get-access-token --resource $u --query accessToken -o tsv)"; Accept = 'application/json'; 'Content-Type' = 'application/json; charset=utf-8'; 'MSCRM.MergeLabels' = 'true' }
foreach ($t in 'sprk_signal', 'sprk_decisionrecord') { foreach ($c in 'sprk_corerecordtype', 'sprk_corerecordid') {
  $p = "$api/EntityDefinitions(LogicalName='$t')/Attributes(LogicalName='$c')"; $a = Invoke-RestMethod $p -Headers $h
  $a.IsSecured = $false; Invoke-RestMethod $p -Method Put -Headers $h -Body ($a | ConvertTo-Json -Depth 20 -Compress) | Out-Null } }
Invoke-RestMethod "$api/PublishXml" -Method Post -Headers $h -Body (@{ ParameterXml = '<importexportxml><entities><entity>sprk_signal</entity><entity>sprk_decisionrecord</entity></entities></importexportxml>' } | ConvertTo-Json) | Out-Null
```

- The profile rows can stay: they grant nothing on an unsecured column. To remove the new profile, delete it in
  make.powerapps.com.
- After a rollback, revert this task's solution XML too. Otherwise SpaarkeMaster ships the secured state to the next
  environment.

## 13. Provisioning step (for customer-provisioning, H7b; relayed by the coordinator)

> H7b adds every business unit's default team to "Spaarke BFF-Managed Field Readers" and H10's BFF application users
> to "Spaarke BFF-Managed Field Writers". Task 044 adds a third profile that ships in SpaarkeMaster,
> "Spaarke Ontology Writer Core Pair Writers" (`{169feee6-7a77-4d00-9e27-ffc27bee6262}`). It holds read, create and
> update on `sprk_corerecordtype` and `sprk_corerecordid` of `sprk_signal` and `sprk_decisionrecord`, and nothing else.
> Its one member is the environment's ontology writer application user, which no package can carry. Proposed H7b
> step:
> - add that application user to the profile, and never remove a member;
> - any other member or team is QuarantineRequired;
> - verify, never repair, that the profile holds exactly those four cells at 4/4/4, and that no other profile grants
>   them (the Readers profile excepted, at read only).
>
> The four columns are field-secured, so a business unit created after H7b has run gets no Readers row for its default
> team until H7b's membership step (or `scripts/Set-RecordCreatorPersonSchema.ps1 -Apply`) runs again. Its users read
> the pair as empty until then. Creating a business unit should therefore be followed by that step, and
> `scripts/Set-CorePairFieldSecurity.ps1 -Verify` names any default team that lacks the profile.

## 14. Known limits

- **Business units created later.** These get no Readers row until provisioning's H7b step runs again (§13). Until
  then, their users read the pair as empty. `-Verify` (p2) names such a team.
- **The server identities read the pair through two routes.** Today they hold System Administrator, and they are also
  members of a profile with Read = 4 on each cell: BFF Writers for the BFF users, Core Pair Writers (and Readers,
  through the root default team) for the ontology writer. The app-only readers in the BFF depend on this read (§4, and
  ontology-r1's list: `SignalReadService`, `SignalReevaluationJob`, `SignalSuppressionService`, `SignalClosureService`,
  `SignalCoreRecordAccess`, `DecisionCommitService`, the decision action executors,
  `TodoAssigneeOwnerReconciliationJob`, `CoreAncestorResolver`, `SecureChildShareSynchronizer`). A read without
  permission returns the pair as null rather than raising an error, so `-Verify` checks both routes in (r1) and reads
  as each identity in (r2).
- **Operator tools.** `Invoke-OntologyStampBackfill.ps1` and Dataverse-MCP verification queries read the pair as the
  operator. An operator who is neither an administrator nor on a business-unit default team sees null values. Both are
  report-only, so nothing is written from a null.
- **(r2) relies on impersonation.** The operator must hold System Administrator. The control read stops a server that
  ignores the header from passing.
- **The dry run exits 0 even when it prints FAIL lines.** This is the precedent in both model scripts. `-Apply` refuses
  on any precondition FAIL, and `-Verify` exits 1.
- **Task 052's 044(d) line (§7)** still waits for ontology-r1 to confirm the wording. This build did not edit 052's POML.
- **The AC6 live check** runs after UAC-r2's apply, with test rows prefixed FMSWP-044. It is not part of this build.
