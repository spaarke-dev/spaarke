# Task 142: Decision Record writer contract (2026-10-13)

Branch `task/ontology-142-dr-writer-contract` (worktree `C:\wts-142`), branched from 141's head `c2ccfc0f4` with
`origin/docs/ontology-platform-design` merged. Draft PR into `docs/ontology-platform-design`. Rigor FULL (opus, high).

## What changed

- **Filing (C1/C2).** `DecisionRecordWriter.WriteAsync` builds the content, then `FileUnderSubjectAsync`: the shared
  `TodoRegardingBuilder.ApplySubjectLookupAndPairAsync` (subject lookup + ADR-024 pair; untyped pair valid, D-133) FIRST, then
  `CoreAncestorResolver.StampAsync` from the same subject (`sprk_regarding{matter,project,workassignment,servicerequest}`). The
  order is forced by the builder's ADR-024 guard (it refuses a row that already carries any regarding lookup, and the stamp
  columns are regarding lookups), so K2 did not force stamp-before-builder. The subject is `ItemSubject` (now always the primary
  Signal's subject) or else the core record. Typed `sprk_matter`/`sprk_project`/`sprk_workassignment` stay (task 145).
- **Owner (C3).** `OwnershipContextOf(request, derivedCores)`: core, subject, every derived core, every Signal subject;
  `RequestedBy` = the deciding user (`ConfirmedByUserId`), so `ApplyTo` stamps `sprk_createdbyperson` (#1783 / D-135).
  `ResolveOwnerAsync` (route step 1f) derives the cores with `ResolveStampsAsync` and returns a `DecisionRecordOwner` (answer +
  parents + assignee). `WriteAsync` reuses it only when `Answers(parents, assignee)`; otherwise logs a warning and asks again.
- **Two walls (C9, #1778).** `DecisionCommitService` step (1c-walls), after FR-51 and the finishing branch, before any write:
  `SecureWallsRefusalAsync` reads `DecisionCommitFollowUps.SecureFamiliesAsync` → `SignalSuppressionService.PartitionsOfSignalsAsync`
  (uac-r2's walk). Different partitions or any Undetermined → Refused with the existing `AlsoResolveNotAllowed` reason; a fault or short answer →
  Unavailable (503). Only when more than one Signal is resolved. No public contract change (existing reason code).
- **Creator person (#1783 / D-135).** `sprk_decisionrecord` in `RecordCreatorPerson.StampedChildTables`, the script's `$Tables`,
  and `$SolutionHomes` (SpaarkeMaster + OntologyPlatformSolution, as 147 did for the budget revision). Pester extended.
- **D-34.** `tests/Spaarke.ArchTests/DecisionRecordContextImmutabilityTests.cs`: the four context columns are written only in
  `DecisionRecordWriter.Content` and `SignalWriter.BuildEntity`; the writer calls its client for Create/Retrieve only; no update
  addresses a Decision Record by name. Must-fire and must-not-fire controls in the same file.
- **Census.** The 182 entry for `DecisionRecordWriter.cs` is Routed; `PersonBearingWriter("DecisionRecordWriter.cs",
  "OwnershipContextOf", …)`; the negative control asserts the table is stamped.
- **`LineageColumnByCoreEntity`** XML remarks: the `Key == Value` rule and the never-empty rule (#1780 K3).

## Fix round (adversarial passes)

- **Pass 2 F1 (fixed): no filing read after the sends.** Step 1f (`ResolveOwnerAsync`) now computes the whole filing (catalog read,
  stamp derivation and probe, inherited Access Permission) through the same `FiledAndOwnedAsync` the write uses, and the
  `DecisionRecordOwner` carries it (`DecisionRecordFiling`: subject, columns, derived cores). The write applies it with no read when
  the subject matches, and reuses the owner when it answered for the same parents. Tests: a catalog fault refuses at 1f before the
  inquiry is sent; a catalog that faults on every read after the first still records (one catalog read per commit); the writer
  applies a handed filing with zero entity reads. Mutation "handed filing ignored" is killed (2 tests).
  Accepted consequence: the record carries the filing as of 1f (seconds earlier); a re-filing in between is picked up by the
  restamp cascade, and both answers are owner parents.
- **Pass 2 F3 (fixed): the D-34 guard is case-insensitive** (PascalCase `sprk_PolicyVersion@odata.bind`, now the seeded control) and
  rule 3 accepts the entity-set name as the update call's argument.
- **Pass 2 K1 (done): rule 3 also catches `new Entity("sprk_decisionrecord", id)`** (an update target; a create has no id), and the
  class doc says rule 3 covers string literals only.
- **Pass 2 K3 (note): `-Apply` would now grant Readers 0/0/4 and the BFF Writers 4/4/4 on `sprk_decisionrecord.sprk_createdbyperson`,
  secure the column if needed, and publish** (the 147 precedent). `-Verify` passed on dev, so there is nothing for it to do there; the
  main session decides any live run. Not run by this task.

- **Pass 1 F1 (fixed): a permanent filing condition is a refusal, not an outage.** `CoreAncestorResolver` (uac-r2's file; additive)
  gains `IsPermanent` on `CoreAncestorResult` / `CoreAncestorStampOutcome`: true for two different records of one root table and
  for a target row that does not exist (no row, or `0x80040217`); `Status` stays `Error`, so every other caller is unchanged. The
  writer maps it to the new `decision_subject_unfileable` (the route's 422 "cannot be recorded", fixed text naming no record) and
  leaves faults at `decision_filing_failed` (503). Both happen at 1f, before any send. Tests: document with two different matters
  and a missing subject (writer), a missing To Do subject through the route (422, nothing written); the timeout case stays 503.
  Mutation "permanent treated as fault" killed (3).
- **Pass 1 K2 = pass 2 F1 (fixed above).** The catalog read moved to 1f with the cores (one `QueryRecordTypeRefAsync` per commit,
  pinned by `A142_TheWriteAtStep5_MakesNoFilingRead…`).
- **Pass 1 K4 (done): the wall check runs after FR-51**, pinned by `A142_TheWallCheckRunsAfterFR51…` (a two-wall set from a caller
  without AppendTo on the matter is Forbidden and the families are never read). Mutation "wall check before FR-51" killed (1).
- **Pass 1 notes (no code).** K1: the 1f owner can go stale if a parent turns secure during the sends; the window predates 142 and
  the secure-flip cascade covers it. K3: a single-Signal decision skips the wall when its core X differs from its subject's root Y;
  the owner is still resolved over X, the subject and Y (secure-if-any). K4b: `SecureFamiliesAsync` is a one-line pass-through not
  exercised directly (the route tests script it; `PartitionsOfSignalsAsync` is tested on the real walk); no test covers the
  finishing path skipping the wall (it writes nothing new: it closes Signals with an existing record's resolution); the partition
  reads the subject from the dedupe key while the owner reads the lookup, which diverge only on malformed rows.

## Component justification (CLAUDE.md §11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `DecisionRecordOwner` (+ internal `DecisionRecordFiling`) | `RecordOwnerResolution` | The answer alone cannot say which parents it answered nor carry the filing; adding them to uac-r2's type would change its contract | Step 5 would write with an answer given for a different parent list, and would re-read the filing after the sends |
| `DecisionRecordRefusalReason.SubjectUnsupported` | `CoreUnsupported` | That reason is about the core table, not the filing subject | A subject the DR cannot be filed under would throw `ArgumentException` out of the builder unclassified |
| `DecisionRecordRefusalReason.FilingFailed` | `OwnerResolutionFailed` | A filing (catalog/stamp) fault is not a resolver fault; the route maps both to 503 | A stamp-derivation fault would be swallowed or surface as a 500 |
| `SignalSuppressionService.PartitionsOfSignalsAsync` | `PartitionsAsync` / `PartitionsOfHitsAsync` | Reuses `PartitionsAsync`; only reads the rows by id | One DR could resolve Signals of two secure families |
| `DecisionCommitFollowUps.SecureFamiliesAsync` | the follow-ups class is the route's one seam to suppression | One virtual pass-through so route tests can script it | The route would take a second suppression dependency |
| `DecisionRecordRefusalReason.SubjectUnfileable`, `CoreAncestorResult/StampOutcome.IsPermanent` | `FilingFailed` / `Status = Error` | Both stay; the flag is additive so `Succeeded` is unchanged for every caller | A subject with conflicting roots or no row returns 503 "try again" forever |

## Decisions and known limits

- **Ordinary beside secure is two families** (the contract's "secure-root sets differ": the empty set differs from {X}). The F1
  mixed also-resolve test now documents that the route's family check is stubbed at one family there; a companion test shows the
  real families refuse the mix before the owner is asked.
- **K: empty `sprk_regardingrecordname`.** The writer has no display name for the subject; the builder writes an empty name (its
  documented behaviour). Views fall back to the lookup's own name.
- **K: derived core can differ from the Signal's core.** If a subject was re-filed after the Signal was raised, the typed
  `sprk_matter` (lineage from the Signal's core, D-34 context) and the live stamp (from the subject now) can name different
  matters. Both are owner parents, so ownership stays secure-if-any; the typed column is retired by task 145.
- **Test rig change.** `DecisionCommitServiceTests.Rig.TodoReadFault` now faults only the assignee read (columns naming
  `sprk_assignedto`); the new `TodoFilingReadFault` faults the filing read. Before, one fault hit both reads; with the filing now
  reading the To Do, D134's "assignee unreadable, secure answer stands" case needs the two reads apart.
- **Tests beyond the named cases** (one-line justifications): `A142_AnOwnerResolvedBeforeTheFirstWrite…` and
  `…ForOtherParents_IsAskedAgain` pin the handed-owner rule (a stale owner is the main secure-leak risk the handoff adds);
  `A142_WhenTheRecordTypeCatalogFaults…` pins fail-closed on the builder's catalog read; `Task142_PartitionsOfSignals…` pins the
  real partition truth the walls check reads (the route tests script it).

## Verification (after the fix round and the base merge, `0cbef266d`)

- Filters (`Sprk.Bff.Api.Tests`): DecisionRecord 87; DecisionCommit 165; RecordOwnership 428; RecordCreatorPerson 17; SecureChild
  501; Signal 1400; CoreAncestor 145; DiGraphValidation 1. All pass.
- ArchTests: 973/973 (the booted-BFF `Task 167` tests that once timed out under full-suite load in the first run pass now).
  D-34 + census + creator-person: 38/38.
- Pester `Set-ChildRecordCreatorPersonSchema.Tests.ps1`: 25/25.
- Mutations (all killed): two-wall check removed (5 fail); derived-core parents dropped (2); Signal-subject parents dropped (8);
  stamp before builder (6); D-34 update path added (2 ArchTests); `RequestedBy` dropped (3); handed filing ignored (2); wall check
  before FR-51 (1); permanent filing treated as a fault (3). Script: decisionrecord not in `$Tables` (4 Pester); no solution home (7).
- `-Verify` against spaarkedev1 (read-only, once, before the fix round; the fix round touches no script): VERIFY PASS;
  `sprk_decisionrecord.sprk_createdbyperson` OK in (a), (c), (c2), (d).
- Publish size vs a fresh Release build of `origin/docs/ontology-platform-design` (`7e2caa6ae`): +17,256 bytes total
  (`Sprk.Bff.Api.dll` +14,848). No package change, no new CVE surface.

## Incident (local tooling, fixed)

A mutation run inside the Pester session wrote the schema script's content into the user's
`Documents\PowerShell\Modules\Pester\6.2.0\Pester.ScriptScope.ps1` (Pester's dot-sourced wrapper shadows `$p`). Repaired by
`Save-Module Pester -RequiredVersion 6.2.0` to the scratchpad and copying the clean file back (hash identical, E81800AD…). Later
mutations ran in a separate `pwsh -NoProfile -File` process with distinct variable names.
