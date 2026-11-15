# 084h: a Decision Record for each per-record Procedure change (EB-D-238): design for review

> **Status:** DESIGN DRAFT, 2026-11-15. No code has been written. It goes to ontology-r1 before the build, as EB-D-238 asks (`notes/decisions-log.md:247`).
> **Base:** `work/ebilling-r1` at f9f74de82e.
> **Reviewers:** ontology-r1 (the writer, the catalog, and the task 142 contract clauses C1–C3 and C9) and the owner (the open questions in §13).
> **Path legend:** `BFF/` = `src/server/api/Sprk.Bff.Api/`; `Writer` = `BFF/Services/Signals/DecisionRecordWriter.cs`; `Catalog` = `BFF/Services/Signals/Actions/DecisionActionCatalog.cs`; `PAS` = `BFF/Services/Finance/Procedures/ProcedureAssignmentService.cs`; `PAE` = `BFF/Api/Finance/ProcedureAssignmentEndpoints.cs`; `Recorder` = `BFF/Services/Finance/BillingDecisionRecorder.cs`.

## 1. What is asked, and the short answer

Every per-record write that 089 makes files one Decision Record on the **core record** (matter, project or work assignment). There are seven of these writes: assign, pin, unpin, Procedure-level exclude, rule exclude, "Change for this record" and remove. Each record names:

- the act;
- the Procedure and its version, or the rule;
- the person;
- the optional note.

Short answer:

- **A new additive writer entry is proposed under root CLAUDE.md §6.5 path B**, the same shape as D-100 (`notes/decisions-log.md:106`): `WriteRecordProcedureDecisionAsync`. No existing writer method fits (§3). Every existing method, record type, constant and test stays byte-identical. The diff to pre-existing writer and catalog files only adds lines.
- **The Decision Record is written by `PAS` after its transaction commits**, never before. A refused or failed change files none.
- **A record that fails after the change landed keeps the 2xx answer** (089's house rule, `PAS:686`, `PAS:1513`). The response carries a new additive `decisionRecord` member that says the record was not written, plus a log and the writer's failure telemetry. Whether an ADR-004 repair job also re-files it is an open question (§13 Q3).

## 2. The seven acts, as the server sees them

`RecordProcedures` builds every plan as remove-then-create (`src/solutions/RecordProcedures/src/components/wizards/plan.ts:5-6`). "Pin to Follow" is a removal of the record's own Pinned row followed by an assign of FollowLatest (`plan.ts:113-136`). So the server only ever sees the five routes below (`PAE:86-242`), and it maps them to codes like this:

| Act (EB-D-238) | Route (`PAE`) | Service | Commit point | Proposed code |
|---|---|---|---|---|
| Assign (FollowLatest) | POST `.../procedure-assignments`, mode FollowLatest | `AssignProcedureAsync` `PAS:356-369` → `CreateRowAsync` `PAS:2379-2391` | `SendAsync` = Done `PAS:2389` | `assign-procedure` |
| Pin | same POST, mode Pinned + `pinnedVersionId` | same | same | `pin-procedure` |
| Procedure-level exclude | same POST, mode Excluded | same | same | `exclude-procedure` |
| Rule exclude | POST `.../rule-overrides/exclude` | `ExcludeRuleAsync` `PAS:504` | same | `exclude-rule` |
| Change for this record | POST `.../rule-overrides/change` | `ChangeRuleCoreAsync` `PAS:620-704` | stage 3 `SendAsync` `PAS:672`, or the no-answer read-back `PAS:673-676` | `change-rule` |
| Unpin | DELETE `.../overrides/{overrideId}` of a Pinned row | `RemoveAsync` `PAS:1463-1546` | `SendAsync` = Done `PAS:1495-1504` | `unpin-procedure` |
| Remove (any other row) | same DELETE | same | same | `remove-override` |

- The code for a DELETE is chosen from the row that `LoadOverrideAsync` read (`PAS:1549-1567`) before the delete: a Procedure row whose mode is Pinned becomes `unpin-procedure`, and every other row becomes `remove-override`. `LoadOverrideAsync` does not read `sprk_pinnedversion` or `sprk_reason` today (`PAS:1559-1561`). The design adds `sprk_pinnedversion` to its column set, so an unpin can name the version it unpinned. The change is in `PAS`, which eBilling owns.
- A Pin-to-Follow plan therefore files two records: `unpin-procedure`, then `assign-procedure`. That is a faithful account of two committed writes. Grouping them into one record is §13 Q6.

## 3. (a) The subject is the core record: how the writer is called

### 3.1 Why no existing entry fits

| Entry | Why not |
|---|---|
| `WriteAsync` `Writer:608-627` | It is the Signals review path. It needs a policy version and resolved Signals, and it resolves codes only through the Signals catalog (`Catalog.TryGet`). A Procedure change resolves no Signal. Reusing it would mean a fake review id and a fake Signal set. |
| `WriteGateDecisionAsync` `Writer:655-704` | It is the communication-gate path, keyed `gate-dr-` (`Writer:716-744`). Its content is gate-specific. |
| `WriteConditionClearedAsync` | It is the nightly closure path, keyed `cleared-dr-`. It is system-decided, with no human confirmer. |
| `WriteBillingDecisionAsync` `Writer:1313`, `:1324-1393` | `ValidateBilling` refuses any subject other than the code's table (`Writer:1563`). That table comes from the literal at `Writer:1266`, which maps every code except the four line codes to `sprk_invoice`. Adding procedure codes to `DeclaredBilling` (`Catalog:388-469`) would map them to the invoice. It would also change `BillingLookupCodes` (`Writer:1271`) and fail the exact-set test (`tests/unit/Sprk.Bff.Api.Tests/Services/Signals/DecisionRecordWriterTests.Billing.cs:191`). That is a change to an existing contract, not an addition. |

### 3.2 The proposed additive entry (§6.5 path B)

🔔 **ADR Conflict — Resolution Required** (the "writer is ontology-owned" rule; the precedent is D-100)

- **Rule:** the Decision Record writer is ontology-r1's. The existing entries and their contracts (task 142 C1–C3, C9) must not change.
- **Conflict:** none of the existing entries files a human, Signal-less record under a core record with its own code set (§3.1).
- **Proposed path:** B, an additive entry under the same seven conditions as D-100 (§3.4).
- **Rejected:**
  - path C (comply by reusing `WriteAsync` with a fake review);
  - extending the billing entry, which would change the existing billing contract (§3.1).
- **Impact:** one new public method, one request record, one key class, one catalog set and one lookup. No change to any existing writer behaviour.

The shape mirrors the billing entry (`Writer:233-282`, `:1313-1393`). Names are proposals:

```text
public sealed record RecordProcedureDecisionRecordRequest(
    Guid DecisionId,                      // server-minted per landed change (§6)
    string ActionCode,                    // one of RecordProcedureActions (§8)
    DecisionRecordRef Subject,            // the core record: sprk_matter | sprk_project | sprk_workassignment
    Guid ConfirmedByUserId,               // the deciding user (systemuserid); never empty: no automated decider on this path
    string DeciderRole,                   // e.g. "procedure-editor"
    IReadOnlyDictionary<string, object?> Values,   // the ids of what was changed (§4.1); note under "note"
    string Outcome,                       // "Assigned" | "Pinned" | "Excluded" | "Changed" | "Unpinned" | "Removed"
    IReadOnlyDictionary<string, object?> Facts,    // non-decided context (§4.2)
    string? Reason,                       // the change's reason, as stored on the row (§9)
    IReadOnlyList<DecisionRecordRef>? Written = null,
    Guid? DeciderContactId = null,
    DateTimeOffset? DecidedOn = null);
// There is deliberately NO ResolvedSignalIds, NO PolicyVersionId, NO PrivilegeFlagged.

public static class RecordProcedureDecisionKey { /* IdOf(subjectEntity, subjectId, decisionId) =>
    SHA-256-derived Guid of "procedure-dr-{subjectEntity}-{subjectId:D}-{decisionId:D}", as BillingDecisionKey Writer:233-242 */ }

public Task<DecisionRecordWriteResult> WriteRecordProcedureDecisionAsync(RecordProcedureDecisionRecordRequest request, CancellationToken ct);
internal Task<DecisionRecordWriteResult> WriteRecordProcedureDecisionAsync(request, Func<CancellationToken, Task<Guid?>>? deciderContact, CancellationToken ct);
internal static void PreflightRecordProcedureDecision(RecordProcedureDecisionRecordRequest request);  // as PreflightBillingDecision Writer:1401
```

The body runs the same steps as `Writer:1324-1393`, reusing every shared builder:

1. `ValidateRecordProcedure`, a new private method modelled on `ValidateBilling` `Writer:1540-1603`. It refuses each of the following before any I/O, with an existing `DecisionRecordRefusalReason` (`Writer:285-355`):
   - a code that is not in `RecordProcedureActions`: `RecordClassUnderivable`;
   - a subject that is not a filing core table, using `InvoiceFamilyCoreRecordGuard.FilingCoreTables` (`BFF/Services/Dataverse/InvoiceFamilyCoreRecordGuard.cs:199`): `SubjectUnsupported` (`Writer:332`);
   - an empty confirmer (there is no `automated:` decider on this path): `ReviewInvalid`;
   - empty facts: `FactSnapshotMissing`;
   - a reason over `ReasonMaxLength` (`Writer:516`): `ReviewInvalid`.
2. `FindRecordProcedureRecordsAsync`, the "already recorded" lookup (§6). It is the billing lookup's twin (`Writer:1619-1677`) with its own `sprk_actioncode In` set.
3. The lazy decider contact (`Writer:1371-1374` pattern).
4. `CreateAsync` (`Writer:2156-2216`) → `FiledAndOwnedAsync` (`Writer:2349-2446`) → `FileUnderSubjectAsync` (`Writer:2457-2494`). These are the one shared create path, with a `DecisionRecordRequest` built by a `RecordProcedureReview` mapper (as `BillingReview` `Writer:1521-1537`):
   - `Core` = the subject itself;
   - `ItemSubject` = the subject;
   - `ResolvedSignalIds` = [];
   - `PolicyVersionId` = Empty;
   - `GateTier` from the catalog gate.

### 3.3 Filing under a core record is an existing, tested path

- `FileUnderSubjectAsync` first runs the shared regarding builder, whose decision-record lookup map already includes `sprk_matter` → `sprk_regardingmatter`, `sprk_project` → `sprk_regardingproject` and `sprk_workassignment` → `sprk_regardingworkassignment` (`BFF/Services/Workspace/TodoRegardingBuilder.cs:103-116`). It then runs the core-ancestor stamp.
- For a CORE target, `CoreAncestorResolver.ResolveStampsAsync` returns the target itself with no read (`BFF/Services/Dataverse/CoreAncestorResolver.cs:605`, `:637-645`).
- `RequireCoreIsTheInvoiceFamilySubjectsOwn` returns early for a subject outside the invoice family (`Writer:2504-2507`), so the 026 checks do not apply and are not bypassed.
- A matter-filed record is already covered: `DecisionRecordWriterTests.cs:1178` (`A142_AnOrdinaryMatter_...FiledUnderTheMatterWithATypedPair`).

No change to the builder, the resolver or the guard is needed.

### 3.4 The binding conditions (P1–P7, mirroring D-100's C1–C7, `notes/074-billing-actions.md` §8.2)

| # | Condition | How it holds |
|---|---|---|
| P1 | The code sets are disjoint. A procedure code through `WriteAsync` or the billing entry is refused, and a Signals or billing code through the new entry is refused. | `RecordProcedureActions` is built with the same `Separate` check as `Catalog:476`, extended to fail on a clash with BOTH `Definitions` and `BillingDefinitions`. `WriteAsync` and `ValidateBilling` already refuse unknown codes (`Writer:1555-1560`). |
| P2 | A subject outside matter, project or work assignment is refused before any I/O: zero reader calls and zero creates. | Step 1 of §3.2. |
| P3 | Idempotent by key. The lookup's `createdby` is the writer identity, read through the sysadmin reader. Another creator's row is not "already recorded". | Reuses `AddBillingWriterFilterAsync`'s pattern (`Writer:1671-1677`) and `WriterUserIdAsync` (`Writer:758`). |
| P4 | A lookup fault writes nothing. | `BillingRecordLookupFailed`'s pattern (`Writer:346`), with a new reason only if ontology-r1 wants one (§13 Q1). |
| P5 | There is no null confirmer: this path is human-only. | Step 1. |
| P6 | The shared filing and owner checks run by reuse, never copied. | The one `CreateAsync` path. |
| P7 | Additive only. | `git diff <base>..HEAD` removes 0 lines from `Writer`, `Catalog` and every existing writer test file. |

## 4. What the record says

### 4.1 Values (decided content: hashed, §6)

The record holds ids only, never names, counts or text other than the note:

| Code | Values |
|---|---|
| `assign-procedure` | `procedureId` |
| `pin-procedure` | `procedureId`, `pinnedVersionId` |
| `exclude-procedure` | `procedureId` |
| `exclude-rule` | `policyId`, `firmId` (when set) |
| `change-rule` | `policyId`, `firmId` (when set), `basedOnVersionId`, `recordRuleVersionId` (`PAS:647`) |
| `unpin-procedure` | `procedureId`, `pinnedVersionId` (after `LoadOverrideAsync` reads it, §2) |
| `remove-override` | `kind` (`KindOf` `PAS:1591`), `procedureId` or `policyId`, `firmId` (when set) |

Every code also carries `overrideId` (the assignment row id) and `note` when one is given.

`assign-procedure` records the Procedure but not the version in force at that moment: FollowLatest has no fixed version by design. Reading the latest version so it can be recorded adds a read to the write path. That is §13 Q5.

### 4.2 Facts (context, not hashed, as `Writer:1410-1418`)

- `coreTable`, `recordId`.
- For `change-rule` and the removal of a changed rule, `closeComplete`.

There are **no counts of closed findings**. The close covers invoices of this record and of records directly under it (`SupersedeBaseFindingsAsync` `PAS:953`), and those may be in other secure families. A count would expose them through a record filed under this one (§10).

### 4.3 Columns

`Content` (`Writer:2944` onward) is reused unchanged:

- `sprk_name` is "Judgement: Exclude rule" (and so on);
- `sprk_actioncode`;
- `sprk_reason`;
- `sprk_confirmedby`;
- `sprk_assignedtointernal`;
- `sprk_gatetier`;
- `sprk_factsnapshot`;
- `sprk_steps` (values, outcome, written rows, the key and the hash);
- the regarding pair and the stamp.

`sprk_policyversion` stays null. Setting it for `change-rule` (to the record rule's new version) is §13 Q4.

## 5. (b) The two-wall refusal holds

- **Where C9 lives.** C9 is `DecisionCommitService.SecureWallsRefusalAsync` (`BFF/Services/Signals/DecisionCommitService.cs:1522`). It runs only when a review resolves more than one Signal (`:1275`), and it refuses Signals that sit under different secure families (`projects/spaarke-ontology-platform-r1/notes/task-142-decision-record-writer-contract.md:18-21`; test `tests/integration/data-mutation/Signals/DecisionCommitServiceTests.cs:1059`).
- **Why this path cannot reach it.** The new request type has **no `ResolvedSignalIds`** (§3.2). The parents the owner resolver sees (`OwnershipContextOf` `Writer:2281-2309`) are therefore exactly {the core record}: one family by construction. A two-family record cannot be expressed.
- **The writer-level wall still applies.** It is the owner resolved over every parent, secure-if-any (`ResolveOwnerAsync` `Writer:2229`). The record takes the core record's own secure-family answer.
- **The `change-rule` close does not cross the wall.** It touches findings of records under this one (`PAS:953`), but the record names none of them (§4.2). It links no Signal and lists no closed id. A must-not-fire test proves the request carries no Signal and the facts carry no count (§11).
- **Leaving `ResolvedSignalIds` out is deliberate.** Adding it later would be a contract change that ontology-r1 reviews (§13 Q2).

## 6. (c) Written only after the change lands, and (d) a retry files no duplicate

### 6.1 Order

1. **Endpoint.** It resolves the decider with `PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync`, as reject and return do (`BFF/Api/Finance/InvoiceRejectReturnEndpoints.cs:89-93`). An unresolved user gets **403 `assignment.decider_unresolved`** before the service runs, so nothing is written. Today the service receives only the caller's oid, which it uses as `RequestedBy` (`PAE:638-652`, `PAS:2406-2460`). It now also receives the systemuserid.
2. **Service, before the transaction.** It runs `PreflightRecordProcedureDecision`, the pure validation of §3.2 step 1, with no I/O. A request that the writer would refuse is refused as **500 `assignment.decision_unrecordable`** before `SendAsync`, so nothing lands. This only happens if the service builds a bad request, which is a bug. It is the same guard the billing path uses (`Recorder:199`).
3. **The transaction** (`SendAsync` `PAS:2662-2699`), unchanged.
4. **Only on `SendResult.Done`** (or on change's proven commit, `PAS:673-676`), the service:
   - finishes the existing close and count, best effort as today;
   - then calls `Recorder.RecordProcedureAsync(...)` **as the last write**.

   Every non-Done result writes nothing and files no record. These are Conflict, Duplicate, Gone, Failed and Unknown (`WriteRefused` `PAS:2626-2636`), plus every refusal before the send (owner refused, reason missing, and so on).
5. **Dry run.** The image-only path (`AssignmentCreateAsync(..., imageOnly: true)`, `PAS:2406`) never calls the recorder. That is a must-not-fire test, plus a `tests/Spaarke.ArchTests/DryRunNoWriteGuardTests.cs` entry if the guard enumerates writers.

**Unknown (no answer from Dataverse).**

- `change-rule` already proves its commit by read-back (`PAS:673-676`) and records only then.
- For the single-row create (assign, pin, the excludes) the design adds the same kind of read-back. It reads the row by its derived id (`AssignmentIdFor` `PAS:2739`) and compares mode, procedure, version and firm. On a match the result is Done and the change is recorded. On no match or a fault, the result stays Unknown and no record is filed.
- For a DELETE that gets no answer, nothing is filed. A missing row cannot prove that THIS call removed it, because another remover may have. That is known limit K-1 (§12).

### 6.2 The idempotency key

- **The key** is `procedure-dr-{coreTable}-{recordId}-{decisionId}`, where `decisionId` is **minted by the server once per landed change** (`Guid.NewGuid()` at step 4).
- **What it covers.** The key protects every retry of the record write: the writer's own repeated lookup, an in-process retry, and the optional repair job of §7.3, which carries the same `decisionId`.
- **A client retry of the change itself never reaches the writer:**
  - a re-sent POST after a landed create gets 409 `assignment.duplicate` (`WriteRefused` `PAS:2628`);
  - a re-sent DELETE gets 404 (`PAE:605-611`).
- **Why not a natural key on the row id.** I-A1 row ids are derived from (core, subject column, subject id) (`PAS:2739`), so a remove followed by a re-add reuses the same row id. A row-id key would make the second episode look "already recorded". A natural key would need (row id, row version), and the version is not returned by `SendAsync`, so it is rejected.
- **Same key, different content.** As in billing (`Writer:1334-1357`), the hash covers code, subject, confirmer, role, Values (note included), outcome and reason. The same key with other content is refused as `ReviewInvalid`, never written twice.

## 7. (e) A record that fails after the change landed is surfaced, not swallowed

### 7.1 What the caller gets

The change stands, so the status stays **201/200** (089's rule: "nothing from here on may turn that into an error", `PAS:686`, `PAS:1513`). A repeat cannot complete the record the way reject/return's 503 does (`BFF/Services/Finance/InvoiceRejectReturnService.cs:129`, `:989-998`): a repeat of an 089 write gets 409 or 404 (§6.2).

The result views therefore gain one additive member, on `OverrideView` (`PAS:44-45`), `RuleChangedView` (`PAS:67-69`) and `OverrideRemoved` (`PAS:80-81`):

```text
"decisionRecord": { "status": "Recorded" | "Queued" | "NotRecorded", "recordId": "<guid>|null", "errorCode": "assignment.decision_record_failed|null" }
```

- **Recorded:** the record id.
- **Queued:** only if §7.3 is adopted.
- **NotRecorded:** with a stable error code (the `assignment.` prefix of `PAS:268-281`). `RecordProcedures` (`src/solutions/RecordProcedures/src/services/overridesApi.ts`) shows a warning bar: "The change was saved, but its Decision Record was not written. Support has been notified." The change itself is not rolled back.

### 7.2 Log and telemetry

- **The writer's own refusal path** (`LogRefusal` `Writer:2119-2147`) already logs at Error and calls `OntologyWriterTelemetry.RecordFailure` (`BFF/Telemetry/OntologyWriterTelemetry.cs:105`), so ontology-r1's failure metric counts it.
- **`PAS` adds one Error log** with a new EventId in the Procedures range, chosen at build. It carries ids only: override id, core table, record id, decision id, code and refusal reason. It never includes the note or the reason text.
- **A non-refusal exception** (transport) is caught in `PAS`, logged the same way, and mapped to NotRecorded. It is never rethrown.

### 7.3 Repair (recommended; §13 Q3)

- **The mechanism.** An ADR-004 job, modelled on `GateDecisionRecordJob` (`BFF/Services/Signals/GateDecisionRecordJob.cs`; EventIds 50380-50383; ids-only payload). On NotRecorded, `PAS` queues `RecordProcedureDecisionJob(decisionId, code, coreTable, recordId, overrideId, deciderUserId, decidedOn)`, and the result says Queued.
- **What the job re-reads.** The row's current values, and also the row's `sprk_reason`. It refiles under the same key, so it is idempotent (§6.2).
- **Limits:**
  - a row that has since been removed cannot be re-read, so the job files the record with `Facts.reconstructed = true` and only the ids carried in the payload;
  - the job payload carries no text, so a removal's note is lost on repair (K-2).
- **Residual window.** A process crash between the commit and the queue files nothing and surfaces nothing (K-3), the same window as `GateDecisionRecordJob`'s.

## 8. The new DecisionActionCatalog entries

A **new set, `RecordProcedureActions`**, separate from `All` (Signals) and from `BillingActions`. It is resolved by a new `TryGetRecordProcedure`, as `Catalog:490-500`. No existing set changes.

| Code | Label | Params | Proposed class | Proposed gate |
|---|---|---|---|---|
| `assign-procedure` | Assign Procedure | `procedure` (Lookup, req.), `note` (opt.) | Routine | Tier2b, reversible, RecordOfTruthImpact |
| `pin-procedure` | Pin Procedure version | `procedure`, `version` (req.), `note` | Routine | same |
| `unpin-procedure` | Unpin Procedure version | `procedure`, `note` | Routine | same |
| `exclude-procedure` | Exclude Procedure | `procedure`, `reason` (opt., as `PAS:415`), `note` | Judgement | same |
| `exclude-rule` | Exclude rule for this record | `rule`, `reason` (req., `PAS:545`), `note` | Judgement | same |
| `change-rule` | Change rule for this record | `rule`, `reason` (req.), `note` | Judgement | same |
| `remove-override` | Remove override | `note` | Judgement | same |

- `ExternallyVisible` is false on every entry (§10).
- `PlanLane` and `WorkType` are null: no Signals plan offers these codes.
- The classes and tiers are proposals (§13 Q7).
- The tiers follow the reversible line-level billing entries (`Catalog:391-414`), because every override can be undone by another override.

## 9. The note field and its validation

- **Reason and note are kept apart in storage.** The three POST bodies already carry `reason`:
  - optional on assign (`PAS:38`, `CheckReason(..., required:false)` `PAS:415`);
  - required on rule exclude and change (`PAS:41`, `:52-53`, `:545`).

  That reason is stored on the row (`sprk_reason`, `PAS:26`) and is copied unchanged to the record's `sprk_reason`. The EB-D-236 note is a separate optional `note`, stored **only** in the record (`Values.note`, inside `sprk_steps`). It is never written to the override row.
- **Fields:**
  - an optional `note` on `ProcedureAssignmentRequest`, `RuleExcludeRequest` and `RuleChangeRequest`;
  - an optional JSON body `{ "note": "..." }` on DELETE `.../overrides/{overrideId}`, which takes no body today (`PAE:86-242`). A DELETE body is legal but unusual (§13 Q8 offers a query or POST alternative).
- **Validation is exactly 095f part 1's**, through `InvoiceCorrectionNote.Check` (`BFF/Services/Finance/InvoiceCorrectionNote.cs:51-67`):
  - trim;
  - empty means none;
  - over 2000 characters gives **400 `assignment.note_too_long`**;
  - any `char.IsControl` other than `\n`, `\r` and `\t` gives **400 `assignment.note_invalid_characters`**;
  - the text is never truncated or stripped.

  It runs before any read. The existing `reason` keeps its own check (`CheckReason` `PAS:2797-2808`, trim and length only). Aligning `reason` with the control-character rule is §13 Q9.
- **The client.** The `RecordProcedures` wizards gain one optional "Note" box on the confirm step. `MAX_REASON_CHARS` (`plan.ts:76`) is reused for its limit.

## 10. Firm visibility: the record stays internal

- The firm-facing readers reach Decision Records only through an invoice or line link (`BFF/Infrastructure/ExternalAccess/ExternalInvoiceVisibility.cs:103`) or through a Signal link (`BFF/Services/Finance/FirmFindingsProjection.cs:226`, `:790`). A record filed under a core record, resolving no Signal and linking no invoice, is reached by neither.
- The record's owner is the core record's answer (§5), and the catalog gate says `ExternallyVisible: false` (§8).
- A rule exclude may name a firm id in Values. That is an id on an internal record, and the record is never projected.
- **Guard:** a must-not-fire test asserts that neither external projection's query can select a `procedure-dr-` record (§11). If either projection ever widens to core-record filing, that test fails.

## 11. Tests (must-fire and must-not-fire)

**Writer (new partial `DecisionRecordWriterTests.RecordProcedure.cs`; existing files untouched)**

| # | Test |
|---|---|
| W1 (P1) | A procedure code through `WriteAsync` is refused, through the billing entry is refused, and a Signals or billing code through the new entry is refused before any I/O. |
| W2 (P2) | `sprk_invoice`, `sprk_billingevent`, a To Do and `sprk_servicerequest` as the subject are refused with zero reader calls and zero creates. Matter, project and work assignment are accepted. |
| W3 (P3) | A repeated key gives AlreadyRecorded with exactly one create. Another creator's row is not "already recorded". The same key with another code or another note is refused. |
| W4 (P4) | A lookup fault or a writer-identity fault writes nothing. |
| W5 (P5) | An empty confirmer is refused. A user decision records `sprk_confirmedby` and the gate tier. |
| W6 (filing) | The record carries `sprk_regarding{matter,project,workassignment}`, the stamp equals the subject, and there is no read for the core. |
| W7 (two walls) | The request type has no Signal member (reflection). The written `sprk_steps.resolvedSignalIds` is empty. The owner context's parents are exactly {core}. |
| W8 (catalog) | `RecordProcedureActions` is disjoint from `All` and `BillingActions`. The exact set of codes is pinned. `BillingActions` is unchanged (the existing test at `Billing.cs:191` stays green untouched). |

**Service and endpoints**

- `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Procedures/ProcedureAssignmentServiceTests.cs` and `.../Api/Finance/ProcedureAssignmentEndpointsTests.cs`:

| # | Test |
|---|---|
| S1 | Each of the seven acts on Done calls the recorder once, with the code, subject, values and confirmer of §2 and §4. |
| S2 (must-not-fire) | Duplicate, Conflict, Gone, Failed, owner-refused, validation-refused, reason-missing and dry-run each make zero recorder calls. |
| S3 | Unknown plus a matching read-back records once. Unknown plus a mismatch or a fault records none and keeps 500 `may or may not`. |
| S4 | A recorder refusal or exception after Done gives 201/200 with `decisionRecord.status = NotRecorded`, one Error log with ids only (asserting no note or reason text in the log), and no rethrow. |
| S5 | `change-rule` facts carry no count and the request carries no Signal id, including when the close spans records under this one. |
| S6 | Note validation: 2000 characters accepted; 2001 gives 400; a `\u0007` gives 400; `\n` is accepted; whitespace-only means none. Each check runs before any read. |
| S7 | An unresolved decider gives 403 before the service is called. |
| S8 | DELETE of a Pinned row gives `unpin-procedure` with `pinnedVersionId`; DELETE of any other row gives `remove-override`. |

- `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/BillingDecisionRecorderTests.cs`: `RecordProcedureAsync` preflight, and the lazy contact.
- `tests/integration/contract/Api/Finance/RecordProcedureWriteRig.cs` and `RecordWritePermissionsContractTests.cs`: the response gains `decisionRecord`, and the existing assertions stay unchanged.
- ArchTests:
  - `DecisionRecordContextImmutabilityTests.cs` must stay green: the context columns are still written only in `Content`.
  - `RecordOwnerAssignmentCensusTests.cs`: the writer stays Routed.
  - `DryRunNoWriteGuardTests.cs`: the recorder is not reachable from the dry run.
- External (must-not-fire): a `procedure-dr-` record under a matter is invisible to `ExternalInvoiceVisibility` and `FirmFindingsProjection` fixtures.

## 12. Known limits

| # | Limit |
|---|---|
| K-1 | A DELETE that gets no answer from Dataverse files no record, because a missing row does not prove which caller removed it. |
| K-2 | A repaired removal (§7.3) has no note, because the job payload is ids-only. |
| K-3 | A crash between the commit and the record (or the queue) leaves a change with no record and no surfaced signal. This is the same window as `GateDecisionRecordJob`. |
| K-4 | A Pin-to-Follow plan files two records (§2). |

## 13. Open questions

**For ontology-r1**

1. Does the new entry reuse existing refusal reasons (`SubjectUnsupported`, `ReviewInvalid`, `BillingRecordLookupFailed`), or does it need a new `RecordProcedureLookupFailed`, which is itself additive?
2. Do you accept that the request type has no `ResolvedSignalIds`, so that C9 holds by construction rather than by a check?
3. Is the ADR-004 repair job (§7.3) required, or is NotRecorded with a log and telemetry enough for R1?
4. Should `change-rule` set `sprk_policyversion` to the record rule's new version (`PAS:647`)? `sprk_policyversion` is yours. The draft leaves it null.
5. Should `assign-procedure` record the latest version in force at that moment? That costs one read.

**For the owner**

6. Should a Pin-to-Follow plan file two records (unpin, then assign), or one grouped record? Grouping needs a plan id from the client.
7. The record classes and gate tiers in §8.
8. Should the DELETE note go in a JSON body (proposed), in a query string (it would appear in logs), or on a new POST `.../overrides/{id}/remove`?
9. Should the existing `reason` also get the control-character rule? That would change the existing contract for any caller sending one.
10. Should the result's `decisionRecord` member ship to `RecordProcedures` now, or stay server-only until the UI work?

## 14. Files to change

| File | Owner | Change |
|---|---|---|
| `Writer` | ontology-r1 (path B) | Add the request, the key, the public and internal entry, the preflight, the validate method, the lookup and its writer filter, and the review mapper. Additive. |
| `Catalog` | ontology-r1 | Add `DeclaredRecordProcedure`, `RecordProcedureActions`, `TryGetRecordProcedure`, and a three-way `Separate`. Additive. |
| `Recorder` | eBilling | Add `RecordProcedureAsync` (reusing `DeciderContactAsync` `Recorder:135-163`) and `PreflightProcedure`. |
| `PAS` | eBilling | Add the decider parameter, the preflight before `SendAsync`, the record after Done, the create read-back on Unknown, the `LoadOverrideAsync` column, `decisionRecord` on the three views, the note, and the log. |
| `PAE` | eBilling | Resolve the decider (403), add the note to the bodies and the DELETE body, and the new codes. |
| `BFF/Services/Signals/RecordProcedureDecisionJob.cs` | eBilling (only if Q3 = yes) | Add the repair job and its registration. |
| `src/solutions/RecordProcedures/src/...` | eBilling | Add the note box and the NotRecorded warning. |
| The tests in §11 | both | As listed. |

**Component justification (CLAUDE.md §11)**

- **Existing:** the four writer entries, and `BillingDecisionRecorder`.
- **Extension:** the billing entry cannot take a core subject without changing its contract (§3.1). The recorder is extended, not copied.
- **Cost of doing nothing:** EB-D-238 is unmet. A per-record change of how a matter's invoices are checked leaves no Decision Record.

## 15. Proposed tasks (no POMLs yet)

| Id | Scope | Size | Review |
|---|---|---|---|
| 084h-1 | The writer entry, catalog set and writer tests W1–W8 | M | ontology-r1 (path B) plus one adversarial pass |
| 084h-2 | `Recorder` + `PAS` + `PAE`: order, Unknown read-back, `decisionRecord`, note, tests S1–S8 | M | code-review + adr-check |
| 084h-3 | The repair job, only if Q3 = yes | S | ontology-r1 |
| 084h-4 | `RecordProcedures` UI: the note box and the warning bar | S | none beyond the standard review |
| 084h-5 | Deploy with 112 (`sprk_recordprocedures` first deploy) | S | owner go |

Order: 084h-1 → 084h-2 → (084h-3) → 084h-4 → 084h-5. 084h-1 cannot merge without ontology-r1's approval of §3.
