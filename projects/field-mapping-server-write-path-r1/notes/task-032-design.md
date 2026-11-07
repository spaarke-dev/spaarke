# Task 032 design: Matter field mapping for extracted invoices

Status: **design for review by ebilling-r1 and unified-access-control-r2. No code before both reviews** (owner ruling 2026-11-06).
POML: `tasks/032-invoice-extraction.poml` (re-scoped 2026-11-07). Inputs: `notes/task-032-design-inputs.md` (ebilling-r1
conditions 1-6). Background: `notes/task-032-notes.md` (the 2026-11-06 escalation; its options are superseded by the ruling).

## 0. Summary

`InvoiceSubmissionPersister` stays the only creator of the invoice family. One new static step,
`InvoiceExtractionFieldMapping.ApplyAsync`, runs inside `PersistCoreAsync` on the in-memory invoice entity, after the persister has
set every invoice column it owns and immediately before `BuildInvoiceCreateRequest`. The step:

- reads the profile from the core record's table to `sprk_invoice`;
- reads the core record app-only as the system principal (`MappingSourceReader.System`), and never reads a field-secured
  source column;
- calls the unchanged engine (`CreateTimeFieldMapping.ApplyFillOnly`) on a copy of the invoice, so persister values always win;
- drops every protected target and every value that does not fit its column;
- copies the surviving values onto the invoice.

The existing single `ExecuteTransactionAsync` then commits them with the same fixed ids. Any fault maps nothing, never fails,
refuses or retries the submission, and leaves a note for the administrator in the same transaction. `RecordCreationService`
is not changed: only its existing `internal static` helpers are called.

Line references are to two trees. **fm** is `work/field-mapping-server-write-path-r1` at 656ab8ddb6. **eb** is
`work/ebilling-r1` at b07e220fa6, which the implementation rebases on (design input "Coordination"). Re-grep before use.

## 1. Binding constraints

| Source | Constraint |
|---|---|
| Owner ruling 2026-11-06 (`decisions.md` "Owner rulings", 032) | Extracted invoices get Matter field mapping in R1. The persister is the single creator. A fill-only step inside it runs before its transaction and calls the same mapping engine. `RecordCreationService` does not change. |
| W3 direction 2026-11-06 (`decisions.md`) | A creator outside the service is valid when it calls the same owner classes for each invariant and a guard pins it. No given-id input, multi-row create or system-principal mapping mode is added to the service. |
| Project `CLAUDE.md` §4 | ebilling-r1 rules: one invoice creator; fixed ids; one transaction. |
| ebilling-r1 conditions 1-6 (`task-032-design-inputs.md`) | 1: the source is the core record (matter, project or work assignment). 2: protected targets are never mapped, with a test per column. 3: partner-readable targets are excluded or approved. 4: identity is UAC-r2's call. 5: re-delivery is consistent, and a fault never blocks the outcome. 6: lines are not mapping targets. |
| Project `CLAUDE.md` §2 | Fill-only (FR-02). The Read probe as the caller (FR-04), which section 4 maps to the system principal. Fail closed (NFR-01). No package, no new DI registration. |

## 2. Where the step sits

`PersistCoreAsync` builds the invoice in memory and commits every row in one transaction.

| Step in `PersistCoreAsync` | eb | fm |
|---|---|---|
| Resume: a linked invoice short-circuits to `ResumeAsync` (no create) | :314 (def :1082) | :301 (def :962) |
| `var invoice = new Entity(sprk_invoice, InvoiceIdFor(submissionId))` plus the core lookup | :461 | :445-446 |
| Core-record resolver fields (`InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync`) | after :461 | :447-456 |
| Owner: `_ownership.ResolveOwnerAsync(...)`; unresolved → Refused | :476 | :460-469 |
| `FillInvoice(...)`: resolver fields, name, number, tracking number, firm or firm hold, dates, total, currency, correlation id, owner | :546 (def :1494-1550) | :512 (def :1250-1298) |
| Creator stamp (`RecordCreatorPerson.Stamp`) and `sprk_submittedby` | :547-559 | :513-525 |
| Supersedes link (097c correction target, or 104 automatic lookup) | :565-602 | :531-565 |
| Review detail (082c-2) | :606-612 | n/a |
| **→ the mapping step goes here** | between :612 and :614 | between :566 and :568 |
| `_lifecycle.BuildInvoiceCreateRequest(invoice, initial)`; copies the row and adds the status pair | :614 | :568 |
| Approval, findings JSON, Stored File owner, request list, admin attention, submission update | :620-716 | :574-664 |
| `_entities.ExecuteTransactionAsync(requests, ct)` | :730 | :678 |

Why there:

- **After every persister write.** `ApplyFillOnly` snapshots what the target holds when it is entered as "supplied"
  (`CreateTimeFieldMapping.cs` fm :99-139). Every value the persister writes is therefore supplied, and mapping can never
  overwrite it.
- **Before `BuildInvoiceCreateRequest`.** The lifecycle service copies the row into the create request (eb
  `InvoiceLifecycleService.cs` :382-417). It also refuses a row that already carries `statecode` or `statuscode`
  (`StatusSuppliedByCaller`, eb :398-404), which would throw at eb :615-618. The status pair is therefore protected (§5).
- **After the owner and every refusal that needs no mapping.** No mapping read is made for a submission that is refused,
  re-evaluated or resumed.
- **Not in `ResumeAsync`.** A resumed submission creates nothing and is never mapped again. This keeps a committed invoice
  stable across deliveries.

The handler may call `PersistAsync` twice in one delivery for a source re-stage (eb `InvoiceSubmissionProcessingJobHandler.cs`
:355, :370). The step then runs on each call with nothing carried over, and only the committing call's result is stored.

## 3. How it reaches the engine without `RecordCreationService`

The engine is a set of static, already-shared pieces. The service is only one of their callers.

| Piece | What it does | Where (fm) | How the step uses it |
|---|---|---|---|
| `IFieldMappingDataverseService.GetFieldMappingProfileWithRulesAsync(sourceTable, "sprk_invoice", activeRulesOnly: true, ct)` | The profile, keyed by the two tables | interface `Spaarke.Dataverse/IFieldMappingDataverseService.cs` :109; used at `RecordCreationService.Mapping.cs` :279-281 and `Repair.cs` :303-305 | Same call. The service is already registered and the persister receives it as an optional constructor parameter. |
| `RecordCreationService.NormalizedRule` | Trims and normalises a rule | `Mapping.cs` :1145 (`internal static`) | Called as is |
| `RecordCreationService.ProtectedFromMapping(table, rule, serverSetLookupColumns: null)` | Common server-owned columns, the registry's mapping-protected columns, the owner and creator columns, the creator person | `Mapping.cs` :184-207 (`internal static`; "internal so the parity test reads the very set the service applies") | Called as is, with `RecordTableRules.Find("sprk_invoice")` |
| `InheritedAccessPermission.RefuseAppOnlyWrite(table, [target], WriteIdentity.AppOnly)` | The per-target app-only refusal that `DropAppOnlyRefusedRules` asks | `Mapping.cs` :226-249 (private wrapper) | The same predicate, asked per rule target. The step owns a six-line loop, because the wrapper is private and the ruling keeps the service unchanged. |
| `RecordCreationService.IsFilingLookup(table, column)` | Whether a lookup files the row | `Mapping.cs` :94 (`internal static`) | Drops a mapped filing lookup |
| `CreateTimeFieldMapping.CollectSourceColumns(rules)` | Source columns, read once | `CreateTimeFieldMapping.cs` :77 | Called as is |
| `MappingSourceReadProbe.ReadAsync(MappingSourceReader.System, source, columns, entities, logger, ct)` | App-only source read with no probe. A missing row or a fault returns `Unavailable` and never throws. | `MappingSourceReadProbe.cs` :265-345; `System` at `:47` | Called as is (the Repair precedent, `Repair.cs` :332-333) |
| `CreateTimeFieldMapping.ApplyFillOnly(rules, target, source, protected, warnings, logger, moneyAttributes, mappedColumns)` | The FR-02 engine | `CreateTimeFieldMapping.cs` :126 | Called once, on a copy of the invoice |
| App-only column metadata (IsSecured, type, IsValidForCreate, MaxLength, DateTimeBehavior, lookup Targets) | Source field security and target fit | Not present yet. Repair's seam is `Repair.cs` :458-465 (`TODO(task 033)`), now task 052 scope (`052-...poml` :41, :117). `MetadataService` (fm `Services/Dataverse/MetadataService.cs` :41, :113-118) reads attributes app-only, but its DTO has no IsSecured. | One reader shared with 052 (§11 R6 and Q-F1) |

Call sequence (`InvoiceExtractionFieldMapping.ApplyAsync(invoice, core, deps, logger, ct)`):

1. If any dependency is null (a test seam only), map nothing and return `NotRun`.
2. Read the profile `core.EntityLogicalName → sprk_invoice`. A fault returns `Fault(profile_unreadable)`. No profile, or no
   rules, returns `NoOp`.
3. Normalise the rules. Drop each rule whose target is protected (§5) or refused by `RefuseAppOnlyWrite`, logging its code.
   If none remain, return `NoOp`.
4. Read metadata for the source columns (IsSecured) and the target columns (fit facts). A fault returns
   `Fault(metadata_unavailable)`.
5. Remove every IsSecured source column from the read list, so it is never read and looks empty to the engine, as the caller
   mask does (`MappingSourceCallerMask.cs` :53-57). A column the successful metadata answer does not know drops its rule (Q-U4).
6. Read the source with `MappingSourceReadProbe.ReadAsync(System, new MappingSource(core.EntityLogicalName, core.RecordId), …)`.
   If it is not readable, return `Fault(source_unreadable)`.
7. Copy the invoice to `candidate` and run
   `ApplyFillOnly(rules, candidate, read.Record, protectedTargets, warnings, logger, moneyAttributes, written)`.
8. Check each written column for fit (§6). A failing column is dropped and logged.
9. Re-run `InvoiceFamilyCoreRecordGuard.Check(candidate)`. A refusal (unreachable, since every core lookup is protected)
   returns `Fault(core_record)`.
10. Copy the kept columns onto `invoice`. Return `Applied(columns)`.

Steps 2-10 run inside one `try`. A fault (anything other than cancellation of the job's own token) returns `Fault(faulted)`,
and `invoice` is untouched. The step never returns a persist status.

The source is passed **explicitly**, never inferred: `MappingSourceResolver` is not called. The invoice carries resolver
fields and possibly a regarding pair, and the only source ebilling allows is the core record (condition 1).

Placement: `Services/Finance/Submission/InvoiceExtractionFieldMapping.cs`, beside the persister. It is static, with no DI
registration and no interface (ADR-010; the `ADR010_DITests` ceiling is unchanged). Component justification is in the POML.

## 4. Principal, probe and masking

### 4.1 Who reads

The persister runs inside an ADR-004 job (eb handler :157 onward). There is no caller token. For the SPA channel the submitter
may be a contact with no Dataverse user (eb persister :439-456). The step therefore reads **app-only, as
the system principal**: the profile, the metadata and the source row (`MappingSourceReader.System`). There is no OBO, no
impersonation and no WhoAmI. This is condition 4's proposal and Repair's model ("System principal (ADR-028, FR-07)",
`Repair.cs` :32-34).

Rejected: masking as the submitter by impersonation.

- An SPA submitter often has no user.
- Two channels would give different values for the same core record.
- A job would act as a user without that user's token, an ADR-028 change that UAC-r2 would have to own.

### 4.2 The FR-04 Read probe

**Not run.** `MappingSourceReader.System` skips it by design (`MappingSourceReadProbe.cs` :121-122: "System principals read
app-only with no probe … the managed identity is never used to answer a caller's Read question"). FR-04 exists so that a
caller cannot name a source they may not read. Here no one names the source:

- The source is the submission's core record (`outcome.CoreRecord`). The intake route filters authorized it against the
  submitter (eb `InvoiceSubmissionService.cs` :31-32). The persister then checks it against the stored row (fm :304-316).
- The invoice's owner is the core record's own record-first answer, secure-if-any (fm :460-469; eb :476), resolved before the
  step. Its record-level readers are therefore the core record's readers. Q-U1 covers roles whose invoice read reaches further.

### 4.3 Masking: mapped values limited to what the invoice's readers may read

The system principal has no caller to mask against (`MappingSourceCallerMask` needs a caller client, :112-118). Four rules
replace the mask:

| Rule | Mechanism | Fail closed |
|---|---|---|
| M1 Field security | A source column marked IsSecured is never read (step 5). Field-secured data on the core record is never copied into an invoice column that lacks the same field security. | A metadata fault, or an existing column whose IsSecured is unknown, maps **nothing** (033 condition 1, which UAC-r2 set for Repair; `033-...poml` :130). |
| M2 Partner-readable targets | Excludes the union of `ReadableColumns` of every registered `ExternalModuleDescriptor` whose `RecordEntity` is `sprk_invoice`, read from the `ExternalModuleRegistry` singleton (eb `ExternalModuleRegistry.cs` :476, `Modules` :485; registration `ExternalAccessModule.cs` :391; the invoices module :511-546). This is condition 3's "exclude" branch, needing no per-column approval. Read live, so a column added to a module later is excluded at once. | No registry, or no invoice module found: map nothing. |
| M3 Access and filing | Filing lookups (`IsFilingLookup`), the regarding pair and resolver fields (the engine refuses them, `CreateTimeFieldMapping.cs` :59-67), access-conferring columns and access columns (§5) are never targets. | n/a (static sets) |
| M4 Secure source | No separate gate. The owner is the core record's secure-if-any answer, so a Secure core record gives a Secure-team invoice. Repair needs a gate (`SourceMayBeCopiedAsync`, `Repair.cs` :527) because its row's owner is not derived from its source. Q-U5 asks whether UAC-r2 wants it reproduced anyway. | n/a |

## 5. Protected targets (ebilling condition 2; one test per column, AC4)

A rule whose target is in this set is skipped and logged with its code. It is never written, **whether or not the persister
set that column on this invoice**. A conditionally written column (date, period, currency, supersedes, review detail, firm)
carries meaning when it is absent: the document showed no date, or there is no firm.

| Group | Columns | Source of the list (no copies where a list exists) |
|---|---|---|
| Service protected set | Common server-owned (`ownerid`, `owninguser`, `owningteam`, `owningbusinessunit`, created/modified columns, …); `sprk_invoice` `MappingProtectedColumns` (core lookups; eb also adds `sprk_reviewdetail`, `sprk_totalamount` and the five derived totals, EB-D-200); the owner and creator columns; `sprk_createdbyperson` | `RecordCreationService.ProtectedFromMapping` (fm `Mapping.cs` :184-207; `RecordTableRules.cs` fm :56-60, :287-297; eb :290-310) |
| App-only refusal | `ParentLineage.AccessColumns["sprk_invoice"]` (firm hold, firm, money totals, …) and every other `RefuseAppOnlyWrite` refusal | `InheritedAccessPermission.RefuseAppOnlyWrite` (eb `ParentLineage.cs` :1081, asked at :1335) |
| Lifecycle | `statecode`, `statuscode` | The lifecycle refusal (eb `InvoiceLifecycleService.cs` :398-404) |
| Identity | `sprk_invoiceid` (the fixed id) | `InvoiceIdFor` (eb :229) |
| Persister-owned (D-106, D-130, D-131, 094, 097c, 104, 082c-2) | `sprk_vendororg` (also when absent: no firm → FirmMissing hold, eb :1509-1521); `InvoiceFirmHold.InvoiceHoldColumn`; `sprk_submittedby`; the supersedes link (`InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn`); `sprk_document` (the invoice never names a document, eb :1523-1524); `sprk_name`, `sprk_invoicenumber`, `sprk_trackingnumber`, `sprk_invoicedate`, `sprk_billingperiodstart`, `sprk_billingperiodend`, `sprk_totalamount`, `sprk_currency`, `sprk_correlationid`; the resolver-field keys of `decided.ResolverFields` | The persister's own constants, plus a unit test that drives a fully populated outcome and asserts every column the persister sets is protected (§10, T-P1). This makes the list self-checking. |
| Search index | `sprk_searchindexname`, `sprk_ai_search_index` (`sprk_invoice` is `SearchIndexed`) | `SearchIndexNameResolver.LookupColumn` and `LegacyTextColumn`, as Repair adds them (`Repair.cs` :362-363) |
| Access conferring | Secure flag, access permission, access inheritance, `sprk_securitybu`, `sprk_containerid`, the regarding pair, and the table's entries in `MembershipOptionsDefaults.CanonicalAccessConferringRegistry` and the bound `MembershipOptions.AccessConferringRoles` | The same sources as Repair's never-map set (`Repair.cs` :485-514), read through the same static registry and the options |
| Partner readable | M2 above | `ExternalModuleRegistry` |
| Filing | Any lookup `IsFilingLookup("sprk_invoice", column)` | `Mapping.cs` :94 |

Never targets at all: lines (`sprk_billingevent`), the approval, Stored File rows and the submission row (condition 6). Only
the `invoice` local is passed to the step, and the guard pins this (§9).

## 6. Value fit (a mapped value must never fail the transaction)

The transaction is all or nothing. A mapped value Dataverse refuses would roll back the whole submission on every delivery
(§11 R1). Each written column is therefore checked against app-only metadata, and the value is **dropped** (never coerced
silently) when:

- the column is unknown or not `IsValidForCreate`;
- the SDK type is not the exact CLR type the column takes (`Integer`→`int`, `BigInt`→`long`, `Decimal`→`decimal`,
  `Double`→`double`, `Money`→`Money`, `Picklist`/`State`/`Status`→`OptionSetValue`, `Boolean`→`bool`, `DateTime`→`DateTime`,
  `Lookup`/`Customer`→`EntityReference`, multi-select→`OptionSetValueCollection`). This is stricter than
  `RecordCreationService.Fits` (`Mapping.cs` :1066-1078), which is lenient because the Web API converts. The SDK does not.
- a string is longer than `MaxLength`;
- a date column's `DateTimeBehavior` is unknown (a `DateOnly` column gets the value's date at midnight, `Unspecified`);
- a lookup's `EntityReference.LogicalName` is not among the column's `Targets`.

Money targets are passed to `ApplyFillOnly` as `moneyAttributes` (#2124 behaviour).

## 7. Faults, missing profile, unreadable source

| Case | Mapped | Note to administrator | Persist outcome |
|---|---|---|---|
| No profile for core table → `sprk_invoice` | nothing | none | unchanged |
| Profile with no rules, or every rule skipped (protected target, app-only refusal, unknown source column) | nothing | none; each skip logged with its code | unchanged |
| A value dropped for fit (§6) | the rest | none; logged with column and reason | unchanged |
| Profile read fault | nothing | `fieldmapping.invoice_extraction.profile_unreadable` | unchanged |
| Metadata fault, or IsSecured unknown for an existing column | nothing | `…metadata_unavailable` | unchanged |
| Source row missing or read fault (`Unavailable`) | nothing | `…source_unreadable` | unchanged |
| Registry absent, or no invoice module (M2) | nothing | `…partner_columns_unknown` | unchanged |
| Any other exception inside the step | nothing (the invoice is untouched) | `…faulted` | unchanged |
| Cancellation of the job's token | n/a | n/a | propagates, as today |

"Unchanged" means the same status, reason code, ids, request order and request count as the same submission with no profile
(AC6). Mapping never refuses, retries or re-evaluates a submission. That is the brief's rule and condition 5's second rule
(D-101).

**Where the note goes (condition 5, D-133).** The note joins the admin-attention text written in the **same** submission
update (eb :708-716, `ConcurrencyBehavior.IfRowVersionMatches`), so it commits or rolls back with the invoice.
`AdminAttentionText` today has fixed segment kinds (signals, system policy, processing stopped, undone; eb
`AdminAttentionText.cs` :10-34), so a mapping note needs one new segment kind. Its owner decides (Q-E2). Logs carry codes and
column names only (ADR-015).

**Re-delivery consistency (condition 5, first rule).** The step chooses "skip, never retry", and four properties make the
outcome consistent:

1. A delivery that does not commit stores nothing, neither the mapped values nor the note.
2. The committing delivery stores its mapped values and its note in one transaction, so the stored invoice and the note
   always agree.
3. Once committed, the invoice is never mapped again, because a later delivery resumes (§2).
4. Each delivery recomputes from the current profile and source with no carried state. The stored answer is therefore one
   delivery's coherent answer, never a mix.

A transient fault on the committing delivery gives an invoice without mapping and with a note. It is never retried into a
different answer later. Q-E4 asks ebilling-r1 to confirm that this meets condition 5.

## 8. Fixed ids and one transaction

Unchanged:

- `InvoiceIdFor`, `LineIdFor` and `ApprovalIdFor` (fm :217-224; eb :229 onward);
- the request list built from `create.Requests` first (eb :663; the invoice create is request 0);
- the guards' indexes (`guardIndex`, `submissionIndex`, eb :697-716);
- the outcome-unknown re-read (eb :732-755);
- the generic catch's rechecks (eb :757-796).

The step adds **no request and no write**. It adds columns to request 0's target only.

**The one case the step cannot rule out.** Despite §6, Dataverse may still refuse request 0 because of a mapped value. Today
that is the ordinary `Retry` (`TransactionFailedCode`, eb :795), and it would repeat on every delivery. Two options:

- **(a) Recommended.** In the generic catch, when `ex.FailedRequestIndex` is 0 and the step applied columns, rebuild request 0
  without the mapped columns (`BuildInvoiceCreateRequest` again) and execute the same list **once** more in the same delivery,
  with the outcome-unknown handling shared, and add `…create_refused_with_mapping` to the note. This is safe:
  - the first transaction rolled back (not outcome-unknown), so the fixed ids are free;
  - the row versions the guards carry are unchanged by a rollback.
- **(b)** No re-commit. Rely on §6 and accept that a refused value turns the submission into Retry until the job's attempts
  run out.

This is ebilling-r1's code region, and its owner decides (Q-E1). Under (b) the brief's "never fail the submission" holds only
as far as §6 holds.

## 9. The guard: a named exception

Under the W3 direction (`decisions.md` :874), the persister is the named creator of `sprk_invoice` on the extraction path, and
it calls the shared owners: the owner resolver (unchanged; census `RecordOwnerAssignmentCensusTests.cs` fm :322-328), the
creator stamp, and now the mapping engine through the step. The guards are:

**G1. New `tests/Spaarke.ArchTests/InvoiceExtractionMappingGuardTests.cs`** (Roslyn syntax tree, the
`MappingSourceCallerMaskGuardTests` method):

1. `InvoiceSubmissionPersister.PersistCoreAsync` contains exactly one call to `InvoiceExtractionFieldMapping.ApplyAsync`. Its
   first argument is the local `invoice`. It appears after the `FillInvoice(` call and before the `BuildInvoiceCreateRequest(`
   and `ExecuteTransactionAsync(` calls, in source order.
2. No other member of the persister calls `ApplyAsync`, `ResumeAsync` in particular, and no other file under `src/server`
   calls it: one caller.
3. Neither `InvoiceSubmissionPersister.cs` nor `InvoiceExtractionFieldMapping.cs` names any `RecordCreationService` member
   except the allow-list `{ProtectedFromMapping, NormalizedRule, IsFilingLookup}`. No instance and no `CreateAsync`.
4. `InvoiceExtractionFieldMapping.cs` passes `MappingSourceReader.System` to `MappingSourceReadProbe.ReadAsync`. It names
   neither `MappingSourceResolver` nor `MappingSourceReader.ForCaller`. The `MappingSource` it builds takes only the `core`
   parameter's table and id.

Must-fire controls (each a snippet through the same scan):

- the call after `BuildInvoiceCreateRequest`;
- a second call;
- a call in `ResumeAsync`;
- a call passing `lines[0]` or `approval`;
- a call from another file;
- `new RecordCreationService(` or `_records.CreateAsync(` in either file;
- `RecordCreationService.MapFieldsAsync`;
- `MappingSourceResolver.ResolveAsync` in the step;
- `MappingSourceReader.ForCaller(`;
- a `MappingSource` built from an invoice column.

Must-not-fire controls:

- the sanctioned shape;
- the call named in a comment or a string;
- the allow-listed statics used alone.

**G2. #2188 mask guard** (`MappingSourceCallerMaskGuardTests.cs`). Add one `UnmaskedCallExemptions` entry,
`…/Services/Finance/Submission/InvoiceExtractionFieldMapping.cs|InvoiceExtractionFieldMapping.ApplyAsync` with `Uses = 1`.
Its reason: system principal, field-secured source columns are never read (M1), partner-readable targets are excluded (M2),
UAC-r2 approval (Q-U3). `KnownCallSites` goes from 3 to 4 (fm :104). The existing ratchet (`EveryUnmaskedCallExemption_IsStillUsed`)
and the exemption controls (fm :152-259) then cover a second unmasked call and a stale entry, so no new control is needed
there.

**G3. Census.** `RecordOwnerAssignmentCensusTests`' `UnscannedWriter` entry for the persister is unchanged (the owner path
does not change), and it must still pass.

## 10. Test plan

New files at KEEP paths (`testing.md` :132; project gotcha 2026-10-20): `tests/unit/domain/Finance/Submission/*`, plus G1.
The fakes come from ebilling's existing persister tests (eb `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Submission/InvoiceSubmissionPersisterTests.cs`)
and are reused, not copied.

| Id | Test | Kind | AC |
|---|---|---|---|
| T-P1 | Every column a fully populated outcome makes the persister set on the invoice is in the protected set. Variants: with firm and without (hold); with and without date, period and currency; correction and automatic supersedes; review detail; submitter and contact. | must-fire if a new persister column is not protected | AC4 |
| T-P2 | One test per §5 column: a Copy rule into it writes nothing, and the rest still apply. Also run with the persister leaving the column empty (date, period, currency, firm). | must-fire (protection) | AC4 |
| T-P3 | A Copy into an unprotected, unset column fills it, for matter, project and work-assignment core records. | must-not-fire (mapping happens) | AC3 |
| T-P4 | A rule into a column the persister set keeps the persister value (name, number, total). | fill-only | AC2 |
| T-S1 | A source column marked IsSecured is not in the read request and not on the invoice. Other rules still apply. | must-fire / must-not-fire pair | AC5 |
| T-S2 | A metadata fault, or IsSecured unknown for an existing column: nothing mapped, note added, persist outcome identical. | must-fire | AC5, AC6 |
| T-S3 | A target in an invoice module's `ReadableColumns` (for example `sprk_invoicedate`, `sprk_reviewdetail`) is skipped. A column outside it is filled. With no registry: nothing mapped, note added. | pair | AC4 |
| T-F1 to T-F4 | Profile read fault, source read fault, a source row that no longer exists, an exception thrown in the engine path: each gives the same status, code, ids, request order and count as no profile, with no mapped column and the note in the submission update. | must-fire (no failure) | AC6 |
| T-F5 | No profile, empty profile, all rules skipped: no note. | must-not-fire (no note) | AC7 |
| T-V1 | Wrong SDK type, over MaxLength, not valid for create, unknown date behaviour, lookup to a table not in Targets: the value is dropped and request 0 lacks it. | must-fire | AC8 |
| T-T1 | Ids and request order and count with and without mapping are equal. Lines, approval, Stored File rows and the submission update carry no mapped column. | shape | AC9 |
| T-T2 | A resumed submission with a throwing profile reader: no mapping read, `Resumed` as today. | must-not-fire (no read) | AC9 |
| T-I1 | Recording fakes: the source read names `outcome.CoreRecord`'s table and id. `MappingSourceReader.System` is used. No OBO client or probe is touched. | identity | AC10 |
| T-R1 | Only if Q-E1 = (a): request 0 refused once with mapped columns, then the second execution commits without them, with the note. Refused with no mapped columns: plain `Retry` as today. Refused twice: `Retry`, and no third execution. | pair | AC6 |
| G1, G2 | §9, with their controls | guard | AC11 |

Regression (unchanged assertions): ebilling's persister and handler suites, `MappingSourceCallerMaskGuardTests` (apart from
the entry), `RecordOwnerAssignmentCensusTests`, `ADR010_DITests`, and the field-mapping unit suites (`CreateTimeFieldMapping*`,
`PushCopyParityTests`). Each new behaviour test is proven red against the persister without the step (copy back, run,
restore).

## 11. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | A mapped value Dataverse refuses rolls back the whole submission on every delivery. | §6 fit checks; Q-E1 (a) re-commit once without mapping. |
| R2 | Roles that read invoices more widely than the core table would see core values they cannot read on the core record. | Q-U1: accept, limit targets, or add an approved allow-list. |
| R3 | Invoice columns reach partners or the search index by a path other than the module's `ReadableColumns` (notifications, exports, chat tools, the invoice index document). | Q-U2, Q-E5. Exclude any further set named. |
| R4 | A column secured after a metadata read is copied until the read is refreshed. | Read per delivery, or a short in-process cache (Q-U4). Never the 6-hour `MetadataService` cache. |
| R5 | Administrator notes flood the attention view during a metadata or profile outage. | Notes only for faults, never for configuration skips; one segment per submission (Q-E2). |
| R6 | Two metadata readers (here and in 052). | One reader, built by whichever task runs first (Q-F1). |
| R7 | Rebase friction with active ebilling work near the persister (095d-2b-2, 164a, 165a). | ebilling-r1 says nothing in flight touches the persister or the handler. Rebase on `work/ebilling-r1` and run `/conflict-check`. `InvoiceSubmissionRerunJobHandler.cs` is not touched (AC12). |
| R8 | Three more app-only reads per persisted submission (profile, metadata, source). | Background job; reads only when a profile exists, except the profile read itself. |
| R9 | A future persister edit sets a new invoice column after the step, or leaves one conditionally empty. | G1 order pin; T-P1 makes an unprotected new column fail the build. |

## 12. Questions

### For ebilling-r1

- **Q-E1. Mapped value refused at commit.** (a) One inline re-commit without the mapped columns, only when request 0 failed
  and mapping applied, with a note (§8). Or (b) no re-commit, accepting Retry until attempts run out. Recommendation: (a).
- **Q-E2. Fault note.** (a) A new `AdminAttentionText` segment kind, "Field mapping not applied: <code>", written in the same
  submission update, ordered after the policy segment and kept by `Merge`. Or (b) logs only. Recommendation: (a), for faults
  only.
- **Q-E3. Conditional persister columns.** The design protects `sprk_currency`, `sprk_invoicedate`,
  `sprk_billingperiodstart`/`end` and `sprk_correlationid` even when extraction left them empty. Confirm, or name the ones
  that may be filled from the core record.
- **Q-E4. Condition 5.** Does "skip and note, never retry; the committing delivery's answer is stored with its note in one
  transaction; a resumed submission is never re-mapped" (§7) satisfy "re-delivery is consistent"?
- **Q-E5. Search index.** Which invoice columns does the invoice index document include, and is that index trimmed to the
  invoice's readers? Should any mapped target be excluded for that reason?
- **Q-E6. Constructor.** May the persister take three optional trailing parameters (`IFieldMappingDataverseService`, the
  metadata reader, `ExternalModuleRegistry`), all already registered, with null meaning "the step maps nothing" (the `jobs`
  precedent, eb :209)? Or do you prefer one parameter object?

### For unified-access-control-r2

- **Q-U1. Reader set.** The invoice's record-level readers are the core record's (same owner team, secure-if-any). Are there
  roles whose `sprk_invoice` read reaches wider than their read of the core table? For those, is a mapped core value on the
  invoice acceptable, or must the targets be limited to an approved allow-list per core table?
- **Q-U2. Partner columns.** Is the union of `ReadableColumns` of the registered `sprk_invoice` external modules the complete
  set of invoice columns an external partner can see (SPA list and detail, notifications, exports, tools)? If not, which
  other set must be excluded?
- **Q-U3. #2188 exemption.** Do you approve one `UnmaskedCallExemptions` entry for `InvoiceExtractionFieldMapping.ApplyAsync`
  (`Uses = 1`, `KnownCallSites` = 4), on the grounds in §9 G2?
- **Q-U4. Field-security fail-closed details.**
  - (a) A metadata fault, or unknown IsSecured on an existing source column, maps nothing (your 033 condition 1, applied
    here). Confirm.
  - (b) A source column absent from a successful metadata answer (it does not exist) drops only its rule. Confirm, or
    require "map nothing".
  - (c) The metadata freshness bound: read per delivery, or a cache of at most 5 minutes in process. Choose one.
- **Q-U5. No probe, no secure gate.** The source is fixed by the submission's filing, authorized at intake, and read app-only
  as the system principal (the Repair model, owner 2026-10-21 option 1). The owner is the core record's secure-if-any answer.
  Is a separate secure-source gate, like Repair's `SourceMayBeCopiedAsync`, required anyway?
- **Q-U6. Mapped lookups.** These are written app-only with no AppendTo check (the system principal, as in Repair). Filing
  lookups are dropped and access-conferring columns are protected. Confirm, or require a further check, such as an app-only
  existence check of the referenced row.

### For the field-mapping project (owner, not a reviewer gate)

- **Q-F1. Metadata reader sequencing.** 052 has not started. Should 032 build the app-only column-metadata reader (extending
  `MetadataService`, the scope 052 inherited from 033), with 052 consuming it? Or should 032 wait for 052?

## 13. Size

| Part | Estimate |
|---|---|
| `InvoiceExtractionFieldMapping.cs` | ~300 lines |
| Persister edit | ~40-60 lines (+~40 for Q-E1 (a)) |
| `AdminAttentionText` | ~30 lines (Q-E2 (a)) |
| Unit tests | ~700-900 lines |
| G1 guard with controls | ~300 lines; G2 is 1 entry and 1 constant |
| Metadata reader, if built here (Q-F1) | ~150 lines plus ~200 lines of tests, +4-6 h |

About 10-14 hours of execution at Sonnet/high and FULL rigor, plus two adversarial review passes (security). It is one PR,
within the ~100-step bound if the metadata reader is excluded. With the reader, split it as 032a (reader) and 032b (step).

## 14. Not changed

- `RecordCreationService*.cs`;
- `RecordTableRules.cs`;
- `InvoiceSubmissionRerunJobHandler.cs`;
- the handler;
- the owner path and the census;
- the lines, approval and Stored File builders;
- the transaction order;
- every endpoint;
- no package and no DI registration.
