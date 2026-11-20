# 084h: a Decision Record for each per-record Procedure change (EB-D-238): design for review

> **Status:** DESIGN DRAFT, 2026-11-15, revised the same day after ontology-r1 review round 1 (CHANGES-REQUIRED; §16). No code has been written. It goes to ontology-r1 before the build, as EB-D-238 asks (`notes/decisions-log.md:247`). §3.2, §7.3 and §8 are re-sent to ontology-r1 after these fixes.
> **Base:** `work/ebilling-r1` at f9f74de82e; citations re-verified at f22abed770 (no `src/` or `tests/` change between the two).
> **Reviewers:** ontology-r1 (the writer, the catalog, and the task 142 contract clauses C1–C3 and C9) and the owner (the open questions in §13).
> **Path legend:** `BFF/` = `src/server/api/Sprk.Bff.Api/`; `Writer` = `BFF/Services/Signals/DecisionRecordWriter.cs`; `Catalog` = `BFF/Services/Signals/Actions/DecisionActionCatalog.cs`; `PAS` = `BFF/Services/Finance/Procedures/ProcedureAssignmentService.cs`; `PAE` = `BFF/Api/Finance/ProcedureAssignmentEndpoints.cs`; `Recorder` = `BFF/Services/Finance/BillingDecisionRecorder.cs`.

## 1. What is asked, and the short answer

Every per-record write that 089 makes files one Decision Record on the **core record** (matter, project or work assignment). There are seven of these writes: assign, pin, unpin, Procedure-level exclude, rule exclude, "Change for this record" and remove. Each record names:

- the act;
- the Procedure and its version, or the rule;
- the person;
- the optional note.

Short answer:

- **A new additive writer entry is proposed under root CLAUDE.md §6.5 path B**, the same shape as D-100 (`notes/decisions-log.md:106`): `WriteRecordProcedureDecisionAsync`. No existing writer method fits (§3). No existing writer or catalog signature or behaviour changes, and the existing writer tests are untouched (P7, §3.4). Shared private helpers (the canonical hash value, the lookup query) are reused, not copied.
- **The Decision Record is written by `PAS` after its transaction commits**, never before. A refused or failed change files none.
- **A record that fails after the change landed keeps the 2xx answer** (089's house rule, `PAS:686`, `PAS:1513`). The failure is queued to a **required** ADR-004 repair job that ships in the same deploy (§7.3, task 084h-3), and the response carries a new additive `decisionRecord` member that says so, plus a log and the writer's failure telemetry.

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
- **The 089e closes file no Decision Record.** A change closes the base rule's open findings `Superseded` (`SupersedeBaseFindingsAsync` `PAS:953`), and the removal of a changed rule closes the record rule's findings `PolicyRetired` (`OverrideRemoved`, `PAS:79-81`). Both are record-free system closures (D-145b, D-170). They are not Decision-Record-producing acts and are not listed in the table above; the record of the human act (`change-rule`, `remove-override`) names none of them (§4.2, §5).

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
- **Impact:** one new public method, one request record, one key class, one catalog set, one lookup and one new additive refusal reason (`RecordProcedureLookupFailed`, §13 Q1). No change to any existing writer signature or behaviour (P7).

The shape mirrors the billing entry (`Writer:233-282`, `:1313-1393`). Names are proposals:

```text
public sealed record RecordProcedureDecisionRecordRequest(
    Guid DecisionId,                      // server-minted once per change, before the preflight (§6)
    string ActionCode,                    // one of RecordProcedureActions (§8)
    DecisionRecordRef Subject,            // the core record: sprk_matter | sprk_project | sprk_workassignment
    Guid ConfirmedByUserId,               // the deciding user (systemuserid); never empty: no automated decider on this path
    string DeciderRole,                   // e.g. "procedure-editor"
    IReadOnlyDictionary<string, object?> Values,   // keyed by the catalog parameter codes (§4.1, §8); note under "note" only when given
    string Outcome,                       // "Assigned" | "Pinned" | "Excluded" | "Changed" | "Unpinned" | "Removed"
    IReadOnlyDictionary<string, object?> Facts,    // non-decided context (§4.2)
    string? Reason,                       // POST: the change's reason as stored on the row; unpin/remove: null (§9)
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
   - a reason over `ReasonMaxLength` (`Writer:516`): `ReviewInvalid`;
   - a missing required parameter, checked exactly as `ValidateBilling` does, as `Values[parameter.Code]` (or `Reason` for a `reason` parameter) (`Writer:1580-1591`): `ReviewInvalid`. The Values keys are therefore the declared parameter codes, one name each (§4.1, §8).
2. `FindRecordProcedureRecordsAsync`, the "already recorded" lookup (§6). It is not a copy of the billing lookup (`Writer:1619-1677`). The billing query's body (`BillingRecordsQueryAsync` `Writer:1652-1666`, `AddBillingWriterFilterAsync` `Writer:1671-1677`) moves into one private helper that takes the `sprk_actioncode In` code set as an argument. The two billing methods keep their signatures and pass `BillingLookupCodes`, so their behaviour is unchanged; the new lookup passes the `RecordProcedureActions` codes. A lookup or writer-identity fault is refused as the new `RecordProcedureLookupFailed` (§13 Q1).
3. The lazy decider contact (`Writer:1371-1374` pattern).
4. **The core pair.** `DecisionRecordCore` needs a `RecordTypeRefId` (`Writer:30`), so `Core` cannot simply be "the subject". The entry calls the existing `ResolveBillingCoreAsync` (`Writer:2081-2116`) unchanged. For a core subject `ResolveStampsAsync` takes its CORE branch, which returns the target itself with no ancestor read (`BFF/Services/Dataverse/CoreAncestorResolver.cs:635-645`), and the method then makes one `sprk_recordtype_ref` read through the writer's shared regarding builder, which caches a found row for the writer's life (`Writer:563-565`, `TodoRegardingBuilder.cs:398-412`). Its derived result is handed on as `plan with { AlreadyDerived = derived }`, as the billing entry does (`Writer:1379-1384`), so filing does not derive again. Its refusals keep their classes (`FilingFailed`, retryable; `SubjectUnfileable`).
5. `CreateAsync` (`Writer:2156-2216`) → `FiledAndOwnedAsync` (`Writer:2349-2446`) → `FileUnderSubjectAsync` (`Writer:2457-2494`). These are the one shared create path, with a `DecisionRecordRequest` built by a `RecordProcedureReview` mapper (as `BillingReview` `Writer:1521-1537`):
   - `Core` = the `DecisionRecordCore` from step 4 (the subject, with its record-type row);
   - `ItemSubject` = the subject;
   - `ResolvedSignalIds` = [];
   - `PolicyVersionId` = Empty;
   - `ReviewHash` = `RecordProcedureDecisionHash`, a new method with the billing hash's shape (`Writer:1419-1437`) that calls the existing `CanonicalHashValue` (`Writer:1446`) for every value, never a copy of it;
   - `GateTier` from the catalog gate.

   The billing-only reads (`RequireBillingPolicyVersionAsync`, `BillingSignalSubjectsAsync`) are not called: there is no policy version and no Signal.

### 3.3 Filing under a core record is an existing, tested path

- `FileUnderSubjectAsync` first runs the shared regarding builder, whose decision-record lookup map already includes `sprk_matter` → `sprk_regardingmatter`, `sprk_project` → `sprk_regardingproject` and `sprk_workassignment` → `sprk_regardingworkassignment` (`BFF/Services/Workspace/TodoRegardingBuilder.cs:103-116`). It then runs the core-ancestor stamp.
- For a CORE target, `CoreAncestorResolver.ResolveStampsAsync` returns the target itself with no read (`BFF/Services/Dataverse/CoreAncestorResolver.cs:605`, `:637-645`).
- `RequireCoreIsTheInvoiceFamilySubjectsOwn` returns early for a subject outside the invoice family (`Writer:2504-2507`), so the 026 checks do not apply and are not bypassed.
- A matter-filed record is already covered: `DecisionRecordWriterTests.cs:1178` (`A142_AnOrdinaryMatter_...FiledUnderTheMatterWithATypedPair`).

No change to the builder, the resolver or the guard is needed.

### 3.4 The binding conditions (P1–P7, mirroring D-100's C1–C7, `notes/074-billing-actions.md` §8.2)

| # | Condition | How it holds |
|---|---|---|
| P1 | The code sets are disjoint. A procedure code through `WriteAsync` or the billing entry is refused, and a Signals or billing code through the new entry is refused. | `RecordProcedureActions` is built with the same `Separate` check as `Catalog:476`, extended to fail on a clash with BOTH `Definitions` and `BillingDefinitions`. `ValidateBilling` already refuses a code it does not know, or one with no `Gate` (`Writer:1555`); `WriteAsync` refuses a step whose code `Catalog.TryGet` does not know (`Writer:2854`). |
| P2 | A subject outside matter, project or work assignment is refused before any I/O: zero reader calls and zero creates. | Step 1 of §3.2. |
| P3 | Idempotent by key. The lookup's `createdby` is the writer identity, read through the sysadmin reader. Another creator's row is not "already recorded". | The shared lookup helper of §3.2 step 2 (today's `AddBillingWriterFilterAsync` body, `Writer:1671-1677`, with the code set passed in) and `WriterUserIdAsync` (`Writer:758`). |
| P4 | A lookup fault writes nothing. | A new additive reason, `RecordProcedureLookupFailed` (§13 Q1, answered), raised as `BillingRecordLookupFailed` is (`Writer:346`, `:1641-1645`). |
| P5 | There is no null confirmer: this path is human-only. | Step 1. |
| P6 | The shared filing and owner checks run by reuse, never copied. | The one `CreateAsync` path. |
| P7 | No existing signature or behaviour changes; the existing tests are untouched. | Every existing public and internal writer and catalog member keeps its signature and its answers. The shared helpers (`CanonicalHashValue` `Writer:1446`, the lookup query and writer filter `Writer:1652-1677`) are reused, never copied; the lookup body moves into one helper that takes the code set (§3.2 step 2), so a few existing lines move, which is allowed. `git diff <base>..HEAD` changes no existing writer test file (`tests/unit/Sprk.Bff.Api.Tests/Services/Signals/DecisionRecordWriterTests*.cs`), and those tests stay green. |

## 4. What the record says

### 4.1 Values (decided content: hashed, §6)

The record holds ids only, never names, counts or text other than the note.

**One name each.** The writer checks a required parameter as `Values[parameter.Code]` (`Writer:1580-1591`), so every Values key that the catalog declares (§8) is written under exactly the declared code. The precedent is `assign-firm`: it declares `firm` (`Catalog:450`) and writes `Values["firm"]` (`BFF/Services/Finance/InvoiceAssignFirmService.cs:442`). The round-0 draft declared `procedure`, `version` and `rule` but wrote `procedureId`, `pinnedVersionId` and `policyId`, so every write would have been refused (500 `assignment.decision_unrecordable` at the preflight). That is fixed here.

| Code | Values (declared in §8) | Values (undeclared ids) |
|---|---|---|
| `assign-procedure` | `procedure` | `override` |
| `pin-procedure` | `procedure`, `version` | `override` |
| `exclude-procedure` | `procedure` | `override` |
| `exclude-rule` | `rule`, `firm` (when set) | `override` |
| `change-rule` | `rule`, `firm` (when set) | `override`, `basedOnVersion`, `recordRuleVersion` (`PAS:647`) |
| `unpin-procedure` | `procedure`, `version` (after `LoadOverrideAsync` reads it, §2) | `override` |
| `remove-override` | `procedure` or `rule`, `firm` (when set) | `override`, `kind` (`KindOf` `PAS:1591`) |

- `override` is the assignment row id. The undeclared ids are accepted by the writer, which checks required parameters only and never refuses an undeclared key (`Writer:1580-1591`); they are in the hash like every value.
- `note` (declared optional on every code, §8) is added **only when a note is given**. The key is never written with a null value: the hash covers every key, including one whose value is null (`Writer:1429-1430`), so a null `note` would make two otherwise identical records hash differently.

`assign-procedure` records the Procedure but not the version in force at that moment: FollowLatest has no fixed version by design. ontology-r1 answered §13 Q5 "no": no extra read is added. If the version is ever wanted, it goes in as an unhashed fact, never in Values.

### 4.2 Facts (context, not hashed, as `Writer:1410-1418`)

- `coreTable`, `recordId`.
- `reconstructed` is never set by the inline write. The repair job (§7.3) carries the full content and does not set it either.

`closeComplete` is **not** a fact: it is already in the response (`RuleChangedView.CloseComplete`, `OverrideRemoved.CloseComplete`, `PAS:67-69`, `:80-81`), and the close is a record-free system closure (§2), not part of the human act.

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

`sprk_policyversion` stays null, including for `change-rule` (§13 Q4, answered by ontology-r1: the new record-rule version is the output of the act, and it is already in Values as `recordRuleVersion`).

`sprk_reason` is the change's reason on the POST codes (as stored on the row). On `unpin-procedure` and `remove-override` it is null, because a DELETE carries no reason; it is **never** the removed row's own assign reason, which describes an earlier act by possibly another user (§9).

## 5. (b) The two-wall refusal holds

- **Where C9 lives.** C9 is `DecisionCommitService.SecureWallsRefusalAsync` (`BFF/Services/Signals/DecisionCommitService.cs:1522`). It runs only when a review resolves more than one Signal (`:1275`), and it refuses Signals that sit under different secure families (`projects/spaarke-ontology-platform-r1/notes/task-142-decision-record-writer-contract.md:18-21`; test `tests/integration/data-mutation/Signals/DecisionCommitServiceTests.cs:1059`).
- **Why this path cannot reach it.** The new request type has **no `ResolvedSignalIds`** (§3.2). The parents the owner resolver sees (`OwnershipContextOf` `Writer:2281-2309`) are therefore exactly {the core record}: one family by construction. A two-family record cannot be expressed.
- **The writer-level wall still applies.** It is the owner resolved over every parent, secure-if-any (`ResolveOwnerAsync` `Writer:2229`). The record takes the core record's own secure-family answer.
- **The `change-rule` close does not cross the wall.** It touches findings of records under this one (`PAS:953`), but the record names none of them (§4.2). It links no Signal and lists no closed id. A must-not-fire test proves the request carries no Signal and the facts carry no count (§11).
- **Leaving `ResolvedSignalIds` out is deliberate.** ontology-r1 accepted C9-by-construction (§13 Q2), on condition that test W7 pins it. Adding the member later would be a contract change that ontology-r1 reviews.

## 6. (c) Written only after the change lands, and (d) a retry files no duplicate

### 6.1 Order

1. **Endpoint.** It resolves the decider with `PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync`, as reject and return do (`BFF/Api/Finance/InvoiceRejectReturnEndpoints.cs:89-93`). An unresolved user gets **403 `assignment.decider_unresolved`** before the service runs, so nothing is written. Today the service receives only the caller's oid, which it uses as `RequestedBy` (`PAE:638-652`, `PAS:2406-2460`). It now also receives the systemuserid.
2. **Service, before the transaction.** It mints `decisionId` (`Guid.NewGuid()`) once, **before** the preflight, so the preflight, the record and any repair job all use the same id and key. It then runs `PreflightRecordProcedureDecision`, the pure validation of §3.2 step 1 (including the required-parameter check), with no I/O. A request that the writer would refuse is refused as **500 `assignment.decision_unrecordable`** before `SendAsync`, so nothing lands. This only happens if the service builds a bad request, which is a bug; test S1 builds each of the seven requests through the real preflight, so a Values key that does not match its declared parameter (§4.1) fails a unit test, not a deploy. It is the same guard the billing path uses (`Recorder:199`).
3. **The transaction** (`SendAsync` `PAS:2662-2699`), unchanged.
4. **Only on `SendResult.Done`** (or on change's proven commit, `PAS:673-676`), the service:
   - finishes the existing close and count, best effort as today;
   - then calls `Recorder.RecordProcedureAsync(...)` **as the last write**.

   Every non-Done result writes nothing and files no record. These are Conflict, Duplicate, Gone, Failed and Unknown (`WriteRefused` `PAS:2626-2636`), plus every refusal before the send (owner refused, reason missing, and so on).
5. **Dry run.** The image-only path (`AssignmentCreateAsync(..., imageOnly: true)`, `PAS:2406`) never calls the recorder. That is a must-not-fire test, plus a `tests/Spaarke.ArchTests/DryRunNoWriteGuardTests.cs` entry if the guard enumerates writers.

**Unknown (no answer from Dataverse).**

- `change-rule` already proves its commit by read-back (`PAS:673-676`) and records only then.
- For the single-row create (assign, pin, the excludes) the design adds the same kind of read-back. It reads the row by its derived id (`AssignmentIdFor` `PAS:2739`) and compares mode, procedure, version, firm, **`sprk_reason`** and **`sprk_createdbyperson`**. The creator person must be the value `ApplyTo` stamps for this caller: the create names the caller as `RequestedBy` (`PAS:2446-2450`), and `sprk_procedureassignment` is a stamped table (`BFF/Services/Dataverse/RecordCreatorPerson.cs:127`). Without those two columns, another user's identical create that landed in the same window would be recorded a second time, under the wrong decider. On a match the result is Done and the change is recorded. On no match or a fault, the result stays Unknown and no record is filed.
- For a DELETE that gets no answer, nothing is filed. A missing row cannot prove that THIS call removed it, because another remover may have. That is known limit K-1 (§12).

### 6.2 The idempotency key

- **The key** is `procedure-dr-{coreTable}-{recordId}-{decisionId}`, where `decisionId` is **minted by the server once per change**, before the preflight (§6.1 step 2). A change that does not land discards it.
- **What it covers.** The key protects every retry of the record write: the writer's own repeated lookup, an in-process retry, and the repair job of §7.3, which carries the same `decisionId` and the same content, so it hashes the same.
- **A client retry of the change itself never reaches the writer:**
  - a re-sent POST after a landed create gets 409 `assignment.duplicate` (`WriteRefused` `PAS:2628`);
  - a re-sent DELETE gets 404 (`PAE:605-611`).
- **Why not a natural key on the row id.** I-A1 row ids are derived from (core, subject column, subject id) (`PAS:2739`), so a remove followed by a re-add reuses the same row id. A row-id key would make the second episode look "already recorded". A natural key would need (row id, row version), and the version is not returned by `SendAsync`, so it is rejected.
- **Same key, different content.** As in billing (`Writer:1334-1357`), the hash covers code, subject, confirmer, role, Values (note included, when given), outcome and reason. The same key with other content is refused as `ReviewInvalid`, never written twice.

## 7. (e) A record that fails after the change landed is surfaced, not swallowed

### 7.1 What the caller gets

The change stands, so the status stays **201/200** (089's rule: "nothing from here on may turn that into an error", `PAS:686`, `PAS:1513`). A repeat cannot complete the record the way reject/return's 503 does (`BFF/Services/Finance/InvoiceRejectReturnService.cs:129`, `:989-998`): a repeat of an 089 POST gets 409 `assignment.duplicate` (`WriteRefused` `PAS:2626-2636`, `Duplicate` `PAS:2822-2823`), and a repeat of a DELETE gets 404 from the endpoint's row check (`PAE:605-611`). Every post-commit refusal is therefore retryable by the system but never by the user, which is why the repair job of §7.3 is required.

The write results therefore gain one additive member. It goes on `OverrideView` (`PAS:44-45`) and on `OverrideRemoved` (`PAS:80-81`) only. `RuleChangedView` (`PAS:67-69`) embeds an `OverrideView` as `Override`, so a change's answer carries the member once, inside `Override`; it is not added to `RuleChangedView` itself. `OverrideView` is built only on the write paths (`PAS:382`, `:530`, `:701`), so no read model gains it.

```text
"decisionRecord": { "status": "Recorded" | "Queued" | "NotRecorded", "recordId": "<guid>|null", "errorCode": "assignment.decision_record_failed|null" }
```

- **Recorded:** the record id.
- **Queued:** the inline write was refused or failed, and the repair job was enqueued (§7.3). `RecordProcedures` (`src/solutions/RecordProcedures/src/services/overridesApi.ts`) shows nothing extra: the record will follow.
- **NotRecorded:** the inline write failed **and** the enqueue failed. It carries a stable error code (the `assignment.` prefix of `PAS:268-281`). `RecordProcedures` shows a warning bar: "The change was saved, but its Decision Record was not written. Support has been notified." The change itself is not rolled back. This is the K-3 window made visible, not a normal outcome.

### 7.2 Log and telemetry

- **The writer's own refusal path** (`LogRefusal` `Writer:2119-2147`) already logs at Error and calls `OntologyWriterTelemetry.RecordFailure` (`BFF/Telemetry/OntologyWriterTelemetry.cs:105`), so ontology-r1's failure metric counts it.
- **`PAS` adds one Error log** with a new EventId in the Procedures range, chosen at build. It carries ids only: override id, core table, record id, decision id, code and refusal reason. It never includes the note or the reason text.
- **A non-refusal exception** (transport) is caught in `PAS`, logged the same way, and goes to the repair job like a refusal. It is never rethrown.

### 7.3 Repair (REQUIRED; ships in the same deploy; §13 Q3 answered)

ontology-r1 ruled the repair job required (§13 Q3). Without it, NotRecorded would be a permanent gap on a real path: the user cannot repeat the change to complete the record (§7.1). Task 084h-3 builds it, and 084h-2 does not deploy without it (§15).

**Placement (ADR-052).** The repair is an **ADR-004 `IJobHandler` in the BFF**, not an ADR-036 `IScheduledJob` and not an Azure Function:

- It is queue work by nature: one message per failed write, enqueued by the request that saw the failure. ADR-052 says queue work in the BFF is ADR-004, and a queue consumer is never a scheduled job.
- An `IScheduledJob` sweep would have to discover the gaps by re-reading override rows, which the next paragraph rules out, and nothing else stores the gap; it would also delay every repair to the next schedule.
- BFF signals: B2 (the job calls `DecisionRecordWriter` and `BillingDecisionRecorder`, BFF domain code) and B3 (low volume: only failed writes; same identity and release cadence). No F-signal is material: there is no event intake independent of the BFF, no bursty load and no multi-step orchestration. Tie-breaker: fewer moving parts.
- Precedent: `GateDecisionRecordJob` (`BFF/Services/Signals/GateDecisionRecordJob.cs:77`, enqueuer; `:168`, `GateDecisionRecordJobHandler : IJobHandler`).
- ADR-052's "idempotent per unit of work": the unit is the decision id, and the writer's key makes a second delivery AlreadyRecorded (§6.2).

**The payload carries the landed change; the job never re-reads the override row.** Row ids recur across episodes: the Procedure and rule-exclude rows are derived from (core, subject column, subject id) (`AssignmentIdFor` `PAS:2739`), and a change's Exclude row reuses the same derived id as a plain Exclude (`ChangeExcludeRow` `PAS:714-715`). A job that re-read the row could therefore see a later remove-and-re-add, record that episode's content under this decision's key, and then be refused on hash. So `PAS` enqueues `RecordProcedureDecisionJob` with everything the inline request had:

- `decisionId`, `code`, `coreTable`, `recordId`;
- the Values of the landed change, exactly as the inline request built them (§4.1), and the `outcome`;
- `confirmedByUserId`, `deciderRole`, `decidedOn`;
- `reason` and `note`, when present.

The job rebuilds the identical `RecordProcedureDecisionRecordRequest`, so its hash equals the inline one. If the inline create in fact landed (a create timeout), the job's write answers AlreadyRecorded instead of being refused.

- **Why the text travels in the payload.** ADR-004 forbids document bytes and large blobs in a payload, not bounded text: the reason and the note are each at most 2000 characters. The alternative the reviewer allowed, omitting them and marking the record `reconstructed: true`, was rejected: an omitted reason or note changes the hash, so a repair after a create that actually landed would be refused permanently.
- **Logging.** The enqueuer and the handler log ids only (EventIds in the Procedures range, chosen at build), never the reason or note text, as `GateDecisionRecordJob` does for its own payload.
- **Retries and poison.** The handler retries a retryable refusal (`FilingFailed`, `RecordProcedureLookupFailed`, a transport fault) through the job's normal delivery count. A permanent refusal (`SubjectUnfileable`, `SubjectUnsupported`, `ReviewInvalid`) is logged at Error, metered through `OntologyWriterTelemetry.RecordFailure`, and completed, never retried forever. The dead-letter queue is the existing ADR-004 one.
- **Residual window.** A process crash between the commit and the enqueue files nothing and surfaces nothing (K-3), the same window as `GateDecisionRecordJob`'s. A failed enqueue is surfaced as NotRecorded (§7.1).

## 8. The new DecisionActionCatalog entries

A **new set, `RecordProcedureActions`**, separate from `All` (Signals) and from `BillingActions`. It is resolved by a new `TryGetRecordProcedure`, as `Catalog:490-500`. No existing set changes.

Each parameter code below is also the Values key the service writes (§4.1): one name each.

| Code | Label | Params | Proposed class | Proposed gate |
|---|---|---|---|---|
| `assign-procedure` | Assign Procedure | `procedure` (Lookup, req.), `note` (opt.) | Routine | Tier2b, reversible, RecordOfTruthImpact |
| `pin-procedure` | Pin Procedure version | `procedure` (req.), `version` (Lookup, req.), `note` (opt.) | Routine | same |
| `unpin-procedure` | Unpin Procedure version | `procedure` (req.), `version` (req.), `note` (opt.) | Routine | same |
| `exclude-procedure` | Exclude Procedure | `procedure` (req.), `reason` (opt., as `PAS:415`), `note` (opt.) | Judgement | same |
| `exclude-rule` | Exclude rule for this record | `rule` (Lookup, req.), `firm` (Lookup, opt.), `reason` (req., `PAS:545`), `note` (opt.) | Judgement | same |
| `change-rule` | Change rule for this record | `rule` (req.), `firm` (opt.), `reason` (req.), `note` (opt.) | Judgement | same |
| `remove-override` | Remove override | `procedure` (opt.), `rule` (opt.), `firm` (opt.), `note` (opt.) | Judgement | same |

- Every entry has a `Gate` (`DecisionGateFactors`): the writer refuses a code with none (`Writer:1555`).
- A `reason` parameter is checked against the request's `Reason`, not `Values` (`Writer:1582-1586`). `unpin-procedure` and `remove-override` declare no `reason`, so their null `Reason` (§4.3) is valid.
- `ExternallyVisible` is false on every entry (§10).
- `PlanLane` and `WorkType` are null: no Signals plan offers these codes.
- The classes and tiers are proposals (§13 Q7).
- The tiers follow the reversible line-level billing entries (`Catalog:391-414`), because every override can be undone by another override.

## 9. The note field and its validation

- **Reason and note are kept apart in storage.** The three POST bodies already carry `reason`:
  - optional on assign (`PAS:38`, `CheckReason(..., required:false)` `PAS:415`);
  - required on rule exclude and change (`PAS:41`, `:52-53`, `:545`).

  That reason is stored on the row (`sprk_reason`, `PAS:26`) and is copied unchanged to the record's `sprk_reason`. A DELETE (unpin, remove) carries no reason, so its record's `sprk_reason` is null; the removed row's reason belongs to the act that created the row and is never copied (`LoadOverrideAsync` does not read it, `PAS:1559-1560`, and the design keeps it that way). The EB-D-236 note is a separate optional `note`, stored **only** in the record (`Values["note"]`, inside `sprk_steps`, present only when given, §4.1). It is never written to the override row.
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
| W3 (P3) | A repeated key gives AlreadyRecorded with exactly one create. Another creator's row is not "already recorded". The same key with another code or another note is refused. A request without a note and one with `note` absent hash identically; the service never adds a null `note` key (asserted on the built Values). |
| W4 (P4) | A lookup fault or a writer-identity fault writes nothing and is refused as `RecordProcedureLookupFailed`. The billing lookup, after its body moved into the shared helper, still filters on `BillingLookupCodes` only (the existing billing lookup tests stay green, untouched). |
| W5 (P5) | An empty confirmer is refused. A user decision records `sprk_confirmedby` and the gate tier. |
| W6 (filing) | The record carries `sprk_regarding{matter,project,workassignment}`, the stamp equals the subject, `sprk_corerecordtype` is the subject's record-type row, the ancestor resolver makes no read (CORE branch), and there is exactly one `sprk_recordtype_ref` read per core type across repeated writes (the builder's cache). A record-type read fault is `FilingFailed` and writes nothing. |
| W7 (two walls) | The request type has no Signal member (reflection). The written `sprk_steps.resolvedSignalIds` is empty. The owner context's parents are exactly {core}. |
| W8 (catalog) | `RecordProcedureActions` is disjoint from `All` and `BillingActions`. The exact set of codes is pinned. Every entry has a `Gate`. For every code, each required parameter is present in the Values the service builds (one name each, §4.1). `BillingActions` is unchanged (the existing test at `Billing.cs:191` stays green untouched). |

**Service and endpoints**

- `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Procedures/ProcedureAssignmentServiceTests.cs` and `.../Api/Finance/ProcedureAssignmentEndpointsTests.cs`:

| # | Test |
|---|---|
| S1 | Each of the seven acts on Done calls the recorder once, with the code, subject, values and confirmer of §2 and §4, and each built request passes the real `PreflightRecordProcedureDecision` (so a Values key that differs from its declared parameter fails here). The decision id is minted before the preflight and is the same in the preflight, the record and any job. |
| S2 (must-not-fire) | Duplicate, Conflict, Gone, Failed, owner-refused, validation-refused, reason-missing and dry-run each make zero recorder calls. |
| S3 | Unknown plus a matching read-back records once. Unknown plus a mismatch or a fault records none and keeps 500 `may or may not`. Must-not-fire: a row that matches on mode, procedure, version and firm but has another `sprk_createdbyperson`, or another `sprk_reason`, records none. |
| S4 | A recorder refusal or exception after Done gives 201/200 with `decisionRecord.status = Queued` and one enqueue whose payload equals the inline request's content (Values, outcome, confirmer, role, decided-on, reason, note); one Error log with ids only (asserting no note or reason text in the log); no rethrow. A failed enqueue as well gives `NotRecorded`. |
| S5 | `change-rule` facts carry no count, no `closeComplete`, and the request carries no Signal id, including when the close spans records under this one. |
| S9 | Unpin and remove records have a null `sprk_reason`, even when the removed row had a reason. |
| S10 | `decisionRecord` appears once in a change's answer (inside `Override`), on assign/pin/exclude answers and on `OverrideRemoved`. |

**Repair job (`tests/unit/Sprk.Bff.Api.Tests/Services/Signals/RecordProcedureDecisionJobTests.cs`)**

| # | Test |
|---|---|
| J1 | The handler rebuilds the identical request from the payload; its hash equals the inline request's hash. |
| J2 | When the inline create in fact landed, the job's write answers AlreadyRecorded with zero creates. |
| J3 (must-not-fire) | The handler makes zero reads of `sprk_procedureassignment`; a row with the same id and other content (a later episode) does not change what is recorded. |
| J4 | A retryable refusal is retried; a permanent refusal is logged, metered and completed, not retried. |
| J5 | No log line from the enqueuer or the handler contains the reason or note text. |
| S6 | Note validation: 2000 characters accepted; 2001 gives 400; a `\u0007` gives 400; `\n` is accepted; whitespace-only means none. Each check runs before any read. |
| S7 | An unresolved decider gives 403 before the service is called. |
| S8 | DELETE of a Pinned row gives `unpin-procedure` with `Values["version"]` (the row's `sprk_pinnedversion`); DELETE of any other row gives `remove-override`. |

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
| K-2 | Withdrawn in review round 1. The repair payload now carries the reason and the note (§7.3), so a repaired record loses nothing. |
| K-3 | A crash between the commit and the record (or the queue) leaves a change with no record and no surfaced signal. This is the same window as `GateDecisionRecordJob`. A failed enqueue is surfaced as NotRecorded. |
| K-4 | A Pin-to-Follow plan files two records (§2). |

## 13. Open questions

**Answered by ontology-r1 (review round 1, 2026-11-15)**

1. *Reuse existing refusal reasons, or a new `RecordProcedureLookupFailed`?* **A new additive reason, `RecordProcedureLookupFailed`.** Applied in §3.2 step 2, P4 and W4. `SubjectUnsupported` and `ReviewInvalid` are reused as before.
2. *Accept no `ResolvedSignalIds`, so C9 holds by construction?* **Accepted, with W7** as the guard. Applied in §5.
3. *Is the repair job required?* **Required, shipping in the same deploy.** Removed from the open list. Applied in §1, §7.1, §7.3, §14 and §15 (084h-3).
4. *Should `change-rule` set `sprk_policyversion`?* **No: leave it null.** The new version is the act's output and is in Values (`recordRuleVersion`). Applied in §4.3.
5. *Should `assign-procedure` record the latest version in force?* **No.** If ever wanted, it is an unhashed fact, never a Values key. Applied in §4.1.

**For the owner (still open)**

6. Should a Pin-to-Follow plan file two records (unpin, then assign), or one grouped record? Grouping needs a plan id from the client.
7. The record classes and gate tiers in §8.
8. Should the DELETE note go in a JSON body (proposed), in a query string (it would appear in logs), or on a new POST `.../overrides/{id}/remove`?
9. Should the existing `reason` also get the control-character rule? That would change the existing contract for any caller sending one.
10. Should the result's `decisionRecord` member ship to `RecordProcedures` now, or stay server-only until the UI work?

## 14. Files to change

| File | Owner | Change |
|---|---|---|
| `Writer` | ontology-r1 (path B) | Add the request, the key, the public and internal entry, the preflight, the validate method, the lookup, the hash method (calling `CanonicalHashValue`), the review mapper and the `RecordProcedureLookupFailed` reason. Move the billing lookup's query body into one helper that takes the code set; the billing methods keep their signatures and behaviour. Reuse `ResolveBillingCoreAsync` unchanged. No existing signature or behaviour changes. |
| `Catalog` | ontology-r1 | Add `DeclaredRecordProcedure` (every entry with a `Gate`), `RecordProcedureActions`, `TryGetRecordProcedure`, and a three-way `Separate`. Additive. |
| `Recorder` | eBilling | Add `RecordProcedureAsync` (reusing `DeciderContactAsync` `Recorder:135-163`) and `PreflightProcedure`. |
| `PAS` | eBilling | Add the decider parameter, the decision id minted before the preflight, the preflight before `SendAsync`, the record after Done, the create read-back on Unknown (with creator person and reason), the `LoadOverrideAsync` column, `decisionRecord` on `OverrideView` and `OverrideRemoved`, the note, the enqueue on failure, and the log. |
| `PAE` | eBilling | Resolve the decider (403), add the note to the bodies and the DELETE body, and the new codes. |
| `BFF/Services/Signals/RecordProcedureDecisionJob.cs` | eBilling | Add the required ADR-004 repair job (payload, enqueuer, `IJobHandler`) and its registration (§7.3). |
| `src/solutions/RecordProcedures/src/...` | eBilling | Add the note box and the NotRecorded warning. |
| The tests in §11 | both | As listed. |

**Component justification (CLAUDE.md §11)**

- **Existing:** the four writer entries, and `BillingDecisionRecorder`. For the repair job: `GateDecisionRecordJob`.
- **Extension:** the billing entry cannot take a core subject without changing its contract (§3.1). The recorder is extended, not copied. `GateDecisionRecordJob` is bound to the gate entry and its payload, so the repair job copies its shape rather than extending it.
- **Cost of doing nothing:** EB-D-238 is unmet. A per-record change of how a matter's invoices are checked leaves no Decision Record. Without the repair job, any post-commit write failure leaves a permanent gap the user cannot close (§7.1).

## 15. Proposed tasks (no POMLs yet)

| Id | Scope | Size | Review |
|---|---|---|---|
| 084h-1 | The writer entry, catalog set and writer tests W1–W8 | M | ontology-r1 (path B) plus one adversarial pass |
| 084h-2 | `Recorder` + `PAS` + `PAE`: decision id before preflight, order, Unknown read-back, `decisionRecord`, note, enqueue on failure, tests S1–S10 | M | code-review + adr-check |
| 084h-3 | **Required.** The ADR-004 repair job (`RecordProcedureDecisionJob`: payload, enqueuer, `IJobHandler`, registration) and tests J1–J5. Placement Justification per ADR-052 (§7.3) | S–M | ontology-r1 + code-review + adr-check |
| 084h-4 | `RecordProcedures` UI: the note box and the NotRecorded warning bar | S | none beyond the standard review |
| 084h-5 | Deploy 084h-1 to 084h-4 together with 112 (`sprk_recordprocedures` first deploy) | S | owner go |

Order: 084h-1 → 084h-2 → 084h-3 → 084h-4 → 084h-5. 084h-1 cannot merge without ontology-r1's approval of §3. **084h-2 and 084h-3 ship in the same deploy**: 084h-2 is never deployed without 084h-3, because its enqueue has no consumer without it, and 084h-5's checklist verifies the job's registration before the owner go.

## 16. Review round 1 (ontology-r1, 2026-11-15)

**Verdict:** CHANGES-REQUIRED; the approach is sound. §3.2, §7.3 and §8 are re-sent to ontology-r1 after these fixes. Every citation below was re-checked against the code at f22abed770.

**Required items**

| # | Item | Applied |
|---|---|---|
| 1 | §8 vs §4.1 parameter names: the writer checks `Values[parameter.Code]` (`Writer:1580-1591`); the draft declared `procedure`/`version`/`rule` but wrote `procedureId`/`pinnedVersionId`/`policyId`, so every write would be refused. | Applied. One name each: the Values keys are the declared codes (`procedure`, `version`, `rule`, `firm`, `note`), as `assign-firm` (`Catalog:450`, `InvoiceAssignFirmService.cs:442`). §4.1, §8; guarded by S1 (real preflight) and W8. |
| 2 | §3.2 step 4: `DecisionRecordCore` needs `RecordTypeRefId` (`Writer:30`); reuse `ResolveBillingCoreAsync` (`Writer:2081-2116`); reword W6. | Applied. §3.2 step 4 (new), W6 reworded: no ancestor read (CORE branch), one cached record-type read. |
| 3 | §7.3 / Q3: the repair job is required and ships in the same deploy. | Applied. §7.3 rewritten; placement is an ADR-004 `IJobHandler` in the BFF, justified against ADR-052 (queue work; B2, B3; no material F-signal; an `IScheduledJob` sweep would need the row re-read item 4 forbids). Task 084h-3 is required; 084h-2 never deploys without it. Q3 removed from the open list. |
| 4 | §7.3: the job must not re-read the override row (row ids recur across episodes). | Applied. The payload carries the landed change's Values, outcome, confirmer, role, decided-on, reason and note; the job makes zero override reads (J3). The text option was chosen over `reconstructed: true` so the repair hashes as the inline write did (§7.3 explains why). K-2 withdrawn. |
| 5 | §6.1: the Unknown-create read-back must also match `sprk_createdbyperson` = caller and the reason. | Applied. §6.1; must-not-fire case added to S3. |
| 6 | §3.4 P7 "0 lines removed" forces copying the hash and the lookup filter. | Applied. P7 restated as "no existing signature or behaviour changes; existing tests untouched". The new hash calls `CanonicalHashValue`; the lookup body moves into one helper that takes the code set, and the billing methods keep their signatures (§3.2 step 2, §14). |

**Optional items (all applied)**

| Item | Applied |
|---|---|
| Unpin/remove `sprk_reason` is null (or the remover's reason), never the removed row's assign reason. | §4.3, §9; S9. A DELETE carries no reason, so it is null. |
| Drop `closeComplete` from the facts (it is already in the response). | §4.2; S5. |
| `decisionRecord` on one view only (`RuleChangedView` embeds `OverrideView`). | §7.1: on `OverrideView` and `OverrideRemoved`, not on `RuleChangedView`; S10. |
| Mint `decisionId` before the preflight. | §6.1 step 2, §6.2; S1. |
| P1 cites `:1555` (`ValidateBilling`); the `WriteAsync` refusal is about `:2854`. | §3.4 P1. Both confirmed. |
| (b) The 089e `Superseded` / `PolicyRetired` closes are record-free system closures (D-145b, D-170) and are not Decision-Record-producing. | §2 (new bullet). The draft did not list them as producing records; the new bullet says so explicitly, and `closeComplete` is no longer a fact. |

**Answers recorded:** Q1–Q5 in §13 (answered block). Q6–Q10 stay open for the owner.

**Citation checks**

- All of the reviewer's writer, catalog, `PAS` and assign-firm line numbers match the code at f22abed770.
- One partial mismatch: for "a repeat gets 409/404" the review cites `PAS:2626-2636`. That range (`WriteRefused`) gives the 409 (`Duplicate`, which returns 409 at `PAS:2822-2823`). The DELETE's 404 comes from the endpoint's row check (`PAE:605-611`), not from `PAS`. The substance holds; §7.1 cites both.
- The draft's own `CoreAncestorResolver.cs:637-645` is the core branch's body; the branch starts at `:635`. Cited as `:635-645` in §3.2.

## 17. Owner answers (2026-11-16, EB-D-239)

These settle §13 Q6-Q10. This section overrides earlier sections where they differ.

| Q | Answer |
|---|---|
| 6 | Two records for a Pin-to-Follow plan (unpin, then assign). K-4 stands as designed. |
| 7 | The §8 classes and gate tiers are accepted as proposed. |
| 8 | The DELETE note travels in a JSON body `{ "note": "..." }`. No query string, no new POST route. The owner asked that the changes be documented: each of the seven acts files a Decision Record naming the act, the Procedure and version or rule, the person, and the note (§4). |
| 9 | `reason` keeps its existing check (trim and length). Only the new `note` refuses control characters. |
| 10 | The `decisionRecord` member and the NotRecorded warning ship to `RecordProcedures` (084h-4). The owner requires server-side retry: the ADR-004 repair job (084h-3, §7.3) retries a retryable refusal (J4) and ships in the same deploy as 084h-2. |

No open owner questions remain. Next: the POMLs for 084h-2, 084h-3 and 084h-4.
