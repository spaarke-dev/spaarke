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
   `owningbusinessunit` the way `SignalWriter` does (FR-14). The consequence is recorded as a known limit in section 6 (visibility of a non-secure record is a 045/038 question).
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

## 6. Review round 1 (independent review of f3362fb8d): fixes and known limits

Fixed on this branch:
- **F4 contract**: a failed create is now wrapped as `DecisionRecordRefusedException` (`decision_create_failed`, or `decision_dataverse_access_denied` for 0x80040220/0x80040299, inner exception kept, logged at 50310 and metered). A resolver fault is wrapped too (`decision_owner_resolution_failed`). The writer's one exception type (plus `OperationCanceledException`, never swallowed) is in the `<exception>` doc. The access-denied classifier is a small copy of `SignalWriter`'s private one because lane 037 is editing that file; consolidate afterwards (a candidate for one shared helper).
- **F2**: memos are serialized and length-checked (fact snapshot 100,000; `sprk_steps` / `sprk_followons` 1,048,576) before ANY I/O, refused as `decision_review_invalid`. `Steps` must be non-empty (`sprk_proposedaction` is required). A gate tier on a review that took nothing, and an outcome or written rows on a skipped step, are refused (section 0.3). A secure resolver answer with no team is refused instead of falling through.
- **F3 ADR-038 pairing**: `tests/integration/seam/Signals/DecisionRecordWriterSeamTests.cs` (Category Live, opt-in like the Signal seam). Run once on 2026-10-10 against spaarkedev1 as the writer (`SIGNALS_LIVE_*`, `AzureCliCredential`): 3 of 3 pass. It asserts every column, the class and outcome values, the lineage and core columns, the no-core owner (`owninguser` = Test User 1), the dismissal values, and that an update by the writer principal is REFUSED. Rows are swept with the operator client by the `zz-040-` marker in `sprk_reason`; afterwards `COUNT ... WHERE sprk_reason LIKE 'zz-040-%'` = 0 and the table holds 0 rows.
- **F3 observability**: the mismatch, read-back-throws, refusal and create-failure paths assert the EventId, level, reason and the `ontology.writer.failures` metric (capturing logger plus a scoped `MeterListener`). Mutation proven: changing the mismatch log call's EventId made the mismatch test fail; restored.

Known limits, not fixed:
- **A secure-owner read-back mismatch returns success** (the id is returned, an Error is logged and metered). The row is append-only, so the writer cannot correct it, and failing would make 043 retry into a duplicate. The claim that uac-r2's reconcile re-owns it is NOT verified for this table (only the 039 live gate on Signals and Decision Records, section 5.2 of the 039 notes, saw re-owning within about 60 s).
- **Criterion 11** (an update by a user holding a MIRRORED SHARE is refused) is deferred to task 041. The live seam proves the writer principal's update is refused; the mirrored-share user is 041's demonstration.
- A non-secure Decision Record is owned by the writer's BU/identity, per the POML. A reader can reach it only if a role or share allows; that is for 038/045, not 040.
- If the create times out the row may exist; the route must check before it retries.