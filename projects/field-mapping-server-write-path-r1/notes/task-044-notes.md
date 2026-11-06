# Task 044 — the Signal / Decision Record core pair is written only by the server and the ledger writers

> Branch `task/fm-044`, cut from `cb61ddf317` (work branch tip: master `20669df6a9` + checkpoint; contains 042c, 046b, 049).
> Rigor FULL. Step mode prescriptive.
> **Status (2026-11-06): stopped at POML step 3.** The design (step 1) and the Grep proof (step 2) are below, ready for
> unified-access-control-r2 and spaarke-ontology-platform-r1. No code, solution XML or test was changed. Two points in
> the POML need a decision before step 4 (§6), so the design does not go further than the review copy.

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
| Ontology writer profile, **choice open (§6 P2)** | `# mi-ontology-writer-dev` (appId 69040982-…67c5); no team | 4 | 4 | 4 |
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

## 6. Points that need a decision before step 4

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

## 8. Planned tests (step 4, after the decisions)

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
