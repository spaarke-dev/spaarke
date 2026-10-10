# 040: Decision Record writer: progress and completion record

> **Date**: 2026-10-10 · **Rigor**: FULL · **Branch**: `task/ontology-040-decision-record-writer` (from `origin/docs/ontology-platform-design` @ `48742f4d2`)

## 1. What was built

- `Services/Signals/DecisionRecordWriter.cs`: one `sprk_decisionrecord` per REVIEW (D-17), create-only (no update or delete path).
  Registered in `SignalsModule` (one line, singleton). `SignalWriter.cs` is NOT touched (lane 037 is editing it).
- Input is `DecisionRecordRequest`: review id, policy version, optional core record (entity, id, `sprk_recordtype_ref` id), the item owner
  (no-core only), confirming user, every offered step (taken or skipped, values, outcome, rows written), the Next steps created, the gate
  tier, every resolved Signal id, the fact values, decider role, because text, reason, deny flag, privilege flag.
- Class (FR-50): derived in the writer from `DecisionActionCatalog`: Dismissal when nothing was taken and no Next step was created;
  Judgement when any taken action is Judgement or any Next step exists; Routine otherwise. Unknown or non-plan action codes, a Next-step
  code in `Steps`, a non-Next-step code in `FollowOns`, and mutually exclusive taken pairs are refused (`record_class_underivable` /
  `decision_review_invalid`), never defaulted. The catalog has no unresolved-class member, so the 036 "open class" escalation did not fire.
- `sprk_decisionoutcome`: Denied when the deny flag is set (only with nothing taken), Dismissed when nothing was taken, else Authorized.
  Not `sprk_disposition`.
- `sprk_factsnapshot`: `{facts, decidedBy:{role}, because}`; a review with no fact values is refused before any other work.
- `sprk_steps`: `{reviewId, resolvedSignalIds, steps:[{code,label,taken,values,outcome,written}]}`. Labels come from the catalog, not the
  caller. Skipped steps have `values: null`, `outcome: "Skipped"`. `sprk_followons`: `[{actionCode, entity, id}]`.
  `sprk_proposedaction` = offered labels joined with "; "; `sprk_actioncode` = first taken action code; `sprk_action` is never set.
- `sprk_privilegeflagged` is copied from the request; nothing reads it.
- Ownership:
  - Core record present: `IRecordOwnershipResolver.ResolveOwnerAsync` with the core as the target (secure-if-any). Secure answer:
    `RecordOwnerResolution.ApplyTo` puts `ownerid` = the Secure Record Owners team in the create. Not secure: ownership left as the writer's.
    Refused: nothing written, `DecisionRecordRefusedException` (reason `decision_owner_refused`, resolver code attached).
  - No core: `ownerid` = the item owner (systemuser) in the create (D-39); the resolver is not called; both core columns absent.
  - After a Secure create, `owningteam` is read back. A mismatch is logged at Error (EventId 50311) and metered, and the id is STILL
    returned: the row is append-only so the writer cannot correct it, a throw would make the route retry into a duplicate, and uac-r2's
    secure-child reconcile re-owns it (the table is registered in `SecureChildLineage`).
- Core columns (D-34, D-36): copies `sprk_corerecordtype` and `sprk_corerecordid`; the typed lineage lookup comes from ONE table, built from
  uac-r2's `SecureChildLineage.Children["sprk_decisionrecord"]` (its lookups that target a secure root), so no per-type branch and no
  second list. A service request has no typed column and is recorded in the generic pair only.

## 2. Decisions and deviations (none silent)

1. **The Signal link is not made in the writer.** POML step 6 allows either place "but in one place only"; task 043's POML (step 5-6,
   acceptance 1) has the commit route close each resolved Signal with `sprk_decisionrecord` set in the same update. The writer returns the
   record id and echoes `ResolvedSignalIds` (also stored in `sprk_steps`). Acceptance 9 ("both Signals reference the one record") is
   therefore proven at the writer as: one record, both ids on it, one id returned. The link itself is 043's acceptance.
2. **`sprk_steps` is an envelope object**, not a bare array, to carry the review id (043: "store the review id in sprk_steps") and the
   resolved Signal ids. A reader (045) reads `.steps`.
3. **Owner ledger entry (needs uac-r2 review).** `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs` is uac-r2's census of
   every owner write. The no-core `ownerid` = user (D-39) is a new owner write, so I listed it as `PerUser` for
   `DecisionRecordWriter.cs / WriteAsync`. This is the one uac-r2-owned file touched; it needs uac-r2's review before merge.
4. **Resolver call shape.** The core is passed as the resolver TARGET with no `Parents` (the POML says "as parent"): the resolver treats
   target and parents together as the secure-if-any set, so the answer is the same; the target form also carries the BU for a
   not-secure answer, which is ignored here.
5. **Non-secure ownership**: "unchanged from the non-secure path" means the writer's own owner and BU; this writer does NOT set
   `owningbusinessunit` the way `SignalWriter` does (FR-14). See document-only item 1.
6. **Telemetry**: its own reason constants (`DecisionRecordRefusalReason`) and EventIds 50310/50311 live in the new file instead of
   editing the shared `OntologyWriterFailureReason` / `OntologyWriterEvents` (avoids a conflict with lane 037). They feed the existing
   `OntologyWriterTelemetry.RecordFailure`.

## 3. uac-r2 files read (origin/master @ `83266093a`, fetched 2026-10-10)

| File | Last commit on master |
|---|---|
| `Services/Access/SecureChildLineage.cs` | `0615f9781` (#1390: carries the 039 `sprk_decisionrecord` entry) |
| `config/secure-record-owner-role.json` | `0615f9781` |
| `Services/Dataverse/RecordOwnershipResolver.cs` (I-6) | `d254d7166` |
| `Services/Dataverse/CoreAncestorResolver.cs` | `d7fdcafc3` |
| `Api/Filters/RecordRouteAccessAuthorizationFilter.cs` | `c5f71487f` (no route added here) |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` | `d5e1dffe7` (no route added here, unchanged) |
| `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs` | `7b89a63ab` (one entry added, see 2.3) |

Open PRs checked: #1598, #1586, #1583 (uac-r2 access and scripts), none touches the lineage, the owner config, the resolver or the
writer files. Nothing changed that invalidates the plan.

## 4. Verification

- `DecisionRecordWriterTests`: 32 pass. ArchTests: 958 pass (after the ledger entry; 957 + 1 census failure before it).
- Publish size (fresh `Release` publish of the project branch at `C:/wb40` vs this branch at `C:/wts-040`, 192 files each side):
  zip 38,340,698 B -> 38,355,605 B (+0.014 MB, 36.58 MB total); unzipped 122.280 -> 122.367 MB.
- Not run live: criterion 11's negative (an update by a user holding a mirrored share is refused) needs a real non-admin and a row; it is
  task 041's recorded demonstration. The facts it rests on were read in `security-roles.md`: no Spaarke role holds Write or Delete on
  `sprk_decisionrecord`, and this writer has no update path (test `TheWriter_NeverUpdatesOrDeletes`).

## 5. Placement Justification (bff-extensions.md)

New file in `Services/Signals/` beside `SignalWriter`; a concrete singleton (ADR-010); no endpoint, no package, no background work. It
calls only the dedicated writer client and the existing resolver. Existing: `SignalWriter` writes a different table with a different
lifecycle. Extension: no, this is create-only with a derived class. Cost of doing nothing: no durable answer to why a decision was made
(suppression and the Report Card have no input).
