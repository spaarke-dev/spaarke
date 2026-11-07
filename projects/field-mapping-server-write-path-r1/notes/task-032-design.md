# Task 032 design: Matter field mapping for extracted invoices

Status: **design round 2, for re-confirmation by ebilling-r1 and unified-access-control-r2. No code yet.** Round 1 (a7b787ffbe)
was approved with conditions (`decisions.md` 2026-11-07: UAC-r2 review, ebilling-r1 review, UAC-r2 follow-up; ebilling
EB-D-207). This revision meets those conditions. Where one cannot be met as written, §14 says so and why.
POML: `tasks/032-invoice-extraction.poml`. Inputs: `notes/task-032-design-inputs.md`. Background: `notes/task-032-notes.md`.

Line references are to two trees, so re-grep before use:

- **fm** is `task/fm-032-design` at 275b1332f7, which is `work/field-mapping-server-write-path-r1` cc1ad6d280 with 052a merged;
- **eb** is `work/ebilling-r1` at 2ae26bae9d.

## 0. Summary

`InvoiceSubmissionPersister` stays the only creator of the invoice family. A fill-only mapping step inside it runs on the
in-memory invoice, after the persister has set every column it owns and immediately before `BuildInvoiceCreateRequest`.
The step calls the unchanged engine. Persister and extracted values always win. Nothing is added to `RecordCreationService`.

Changes from round 1:

| Area | Round 1 | Round 2 |
|---|---|---|
| Which targets may be written | A deny-list | **An allow-list of target columns** (§6.1) is the primary check, with the deny-list as a second check (§6.2). |
| Secure source | No separate gate | **A gate** (§5). Nothing is mapped when the owner answer is Secure or hard-deny, when an active, well-formed No Access entry covers the core record, or when Repair's secure-source logic says no. A failed read maps nothing. |
| Mapped value refused at commit | Two options | **One re-commit without the mapped columns**, through one local function, under Q-E1's conditions (§9). |
| Rule faults | Per column | **All or nothing**: any warning from the engine maps nothing (§8.2). |
| Admin note | Open question | **A new `AdminAttentionText` segment for faults only**, within the 500-character cap (§8.3). |
| Determinism | Not addressed | A static resolver-key set, ties broken by rule id, and the lookup overload of `IsFilingLookup` (§3.3). |
| Money guard | Not addressed | The persister exposes its owned-column list, so the step names no money or firm column (§10 G4). |
| Metadata | A reader still to be built | 052a's `IGenericEntityService.GetColumnMetadataAsync`, read per delivery with no cache (§7). |
| Dependencies | Three optional constructor parameters | **One optional parameter**: the step becomes a scoped class (§3.1; Q-E6 needs re-confirmation, §14). |
| Repair's gate and fit checks | Not reused | Reused by **moving** them, unchanged, out of `RecordCreationService.Repair.cs` into a shared static class that Repair delegates to (§5.3). This touches a `RecordCreationService*.cs` file, so it needs the owner's approval (§14). |

**The allow-list is narrow.** It proposes two columns: `sprk_description` and `sprk_highpriority`. The only profile on dev,
"Matter to Invoice (Attorney Matrix)", maps four attorney and paralegal Contact lookups. Those lookups grant access, so they
stay refused (§6.3). With these conditions, FR-20 for invoices maps nothing on dev until a profile targets an allow-listed
column. §14 puts the choice to the owner.

## 1. Binding constraints

| Source | Constraint |
|---|---|
| Owner ruling 2026-11-06 (`decisions.md` :886) | Extracted invoices get Matter field mapping in R1. The persister is the single creator. A fill-only step runs inside it, before its transaction, and calls the same mapping engine. `RecordCreationService` does not change. |
| W3 direction (`decisions.md` :871-881) | A creator outside the service is valid when it calls the same owner classes, and a guard pins it. The service gets no core redesign in R1. |
| UAC-r2 review (`decisions.md` :916-927) and follow-up (:950-953) | Q-U1 role-source assertion. Q-U2 partner and search-index columns excluded. Q-U3 one #2188 entry, pinned. Q-U4 field-security fail-closed rules. Q-U5 secure-source gate. Q-U6 lookups. No Access gate. An allow-list that both reviewers sign off. |
| ebilling-r1 review (`decisions.md` :929-949), EB-D-207 (eb `projects/ebilling-r1/notes/decisions-log.md` :215) | Q-E1 re-commit once. Q-E2 fault note segment. Q-E3 protected even when empty. Q-E4 re-delivery test. Q-E5 search index by reference. Q-E6 constructor and DI test. C1 allow-list. C2 Secure → no mapping, log a code, no note. C3 all or nothing. C4 determinism. C5 money guard. |
| Project `CLAUDE.md` §2 and §4 | Fill-only (FR-02). Fail closed (NFR-01). One invoice creator, fixed ids, one transaction. |

## 2. Where the step sits

| Step in `PersistCoreAsync` | eb |
|---|---|
| Resume: a linked invoice goes to `ResumeAsync` (no create, **no mapping**) | :314 (definition :1082) |
| `var invoice = new Entity(sprk_invoice, InvoiceIdFor(submissionId))` and the core lookup | :461-462 |
| Resolver fields (`InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync`) | :463-472 |
| Owner: `_ownership.ResolveOwnerAsync(...)`; unresolved → Refused | :476-485 |
| `FillInvoice(...)`, the creator stamp and `sprk_submittedby` | :546-559 |
| Supersedes link | :565-602 |
| Review detail | :606-612 |
| **→ `await _extractionMapping.ApplyAsync(invoice, core, owner, ct)`** | between :612 and :614 |
| `_lifecycle.BuildInvoiceCreateRequest(invoice, initial)` | :614 |
| Approval, request list, guard, admin attention, submission update | :620-716 |
| `ExecuteTransactionAsync` and its catches | :728-796 |

The reasons are unchanged from round 1:

- The step comes after every persister write, so `ApplyFillOnly` treats every persister value as supplied (fm
  `CreateTimeFieldMapping.cs` :126-139).
- It comes before the lifecycle copies the row and refuses a status pair (eb `InvoiceLifecycleService.cs` :382-417, :398-404).
- It runs only after the owner and the refusals are resolved.
- It never runs in `ResumeAsync`.

## 3. The step

### 3.1 Shape and dependencies

`InvoiceExtractionFieldMapping` is a **scoped concrete class**, with no interface (ADR-010; the `ADR010_DITests` ceiling is
unchanged). It lives in eb `Services/Finance/Submission/` and is registered once in `FinanceModule` next to the persister (eb
`FinanceModule.cs` :249).

The persister takes it as **one optional trailing constructor parameter**, `InvoiceExtractionFieldMapping? extractionMapping
= null`, which copies the `jobs` precedent (eb :209, :220). When the parameter is null, the step does not run and the
persister logs one Warning per persist (`fieldmapping.invoice_extraction.not_wired`).

The step's own constructor takes only services that are already registered:

| Dependency | Use | Registration (fm) |
|---|---|---|
| `IGenericEntityService` | Column metadata (`GetColumnMetadataAsync`, 052a) and the source read | existing |
| `IFieldMappingDataverseService` | The profile | `GraphModule.cs` :153 |
| `SecureChildShareSynchronizer` | The Secure Record owner team, for Repair's gate | `ExternalAccessModule.cs` :705 (scoped) |
| `NoAccessShareEnforcer`, `NoAccessEnforcementStore` | No Access coverage and entries (§5.2) | `ExternalAccessModule.cs` :329, :325 |
| `ExternalModuleRegistry` | Partner-readable columns (second check) | `ExternalAccessModule.cs` :391 |
| `IOptions<MembershipOptions>` | The bound access-conferring registry (second check) | `MembershipModule.cs` :76 |
| `ILogger<InvoiceExtractionFieldMapping>` | Codes only | n/a |

Why not three constructor parameters (Q-E6)? The gate that UAC-r2 required after Q-E6 was answered needs three more readers,
which would make five or six persister parameters. One parameter keeps ebilling's diff small and lets the DI test check the
whole chain. If ebilling-r1 prefers parameters, the fallback is five optional parameters: `IFieldMappingDataverseService`,
`SecureChildShareSynchronizer`, `NoAccessShareEnforcer`, `NoAccessEnforcementStore` and `IOptions<MembershipOptions>`, with
the registry check then done by a test only (§14).

### 3.2 Call sequence: `ApplyAsync(Entity invoice, EvaluatedCoreRecord core, RecordOwnerResolution owner, CancellationToken ct)`

It returns an `InvoiceMappingOutcome`: `Applied(columns)`, `NoOp(code)`, `Gated(code)` or `Fault(code)`. It never returns a
persist status and never throws, except when the job's own token is cancelled.

1. **Owner gate (§5.1).** If `owner.IsSecureOwner`, return `Gated(secure_owner)`. If the owner answer is hard-deny
   (`owner.InPlaceOfDefaultTeamId` is not null), return `Gated(hard_deny_owner)`. No read has been made yet.
2. **Profile.** `GetFieldMappingProfileWithRulesAsync(core.EntityLogicalName, "sprk_invoice", activeRulesOnly: true)`.
   A fault returns `Fault(profile_unreadable)`. No profile, or no rules, returns `NoOp`.
3. **Rules.** Normalise with `RecordCreationService.NormalizedRule`, then order by `(ExecutionOrder, Id)` (§3.3). Keep only
   rules whose target is on the allow-list (§6.1) and not on the deny-list (§6.2). Each skipped rule is logged with its code
   and rule id. If none remain, return `NoOp(no_applicable_rules)`.
4. **No Access (§5.2).** If the coverage read fails, or an entry read fails, return `Fault(no_access_unreadable)`. If an
   active, well-formed entry covers the record, return `Gated(no_access_entry)`.
5. **Metadata (§7).** Read `GetColumnMetadataAsync(core table)` and `GetColumnMetadataAsync("sprk_invoice")` per delivery.
   - A fault or an empty answer returns `Fault(metadata_unavailable)`.
   - Any source column of a kept rule whose `IsSecured` is null also returns `Fault(metadata_unavailable)` (Q-U4 a).
   - A source column missing from a successful answer drops only its rule, with a Warning naming the rule id (Q-U4 b).
   - A source column with `IsSecured == true` is removed from the read, so it is never read and looks empty to the engine
     (M1). A rule whose target is not `IsSecured == false` on `sprk_invoice` is dropped.
6. **Source read.** `MappingSourceReadProbe.ReadAsync(MappingSourceReader.System, new MappingSource(core.EntityLogicalName,
   core.RecordId), columns ∪ SourceGateColumns(core table), …)`. If the row is unavailable, return `Fault(source_unreadable)`.
7. **Secure-source gate (§5.3).** `MappingSourceCopyGate.DecideAsync(...)`: `Secure` returns `Gated(secure_source)`, and
   `Unknown` returns `Fault(secure_source_unknown)`.
8. **Engine.** Copy the invoice to `candidate`. Run `CreateTimeFieldMapping.ApplyFillOnly(rules, candidate, copyableSource,
   protectedTargets, warnings, logger, moneyAttributes: ∅, mappedColumns: written)`. If `warnings` is not empty, return
   `Fault(rule_fault)` (§8.2). If `written` is empty, return `NoOp`.
9. **Fit (§7).** Check every written column with `MappedTargetFit.Check` (§5.3). If any written column fails, return
   `Fault(value_unfit)`. All or nothing: no partial set reaches the invoice.
10. **Re-check.** Re-run `InvoiceFamilyCoreRecordGuard.Check(candidate)`. A refusal returns `Fault(core_record)`.
11. **Copy.** Copy the written columns onto `invoice` and return `Applied(written)`.

Steps 2-11 run inside one `try`. Any exception returns `Fault(faulted)`, and `invoice` is left untouched.

The source is passed explicitly as `outcome.CoreRecord`. `MappingSourceResolver` is never called (ebilling condition 1).

### 3.3 Determinism (C4)

- **Static resolver-key set.** The protected resolver and regarding keys are the static
  `CreateTimeFieldMapping.RegardingPairAndResolverColumns` (fm :59), not the keys of this submission's
  `decided.ResolverFields`. The protected set is therefore the same for every submission.
- **Rule order.** The step sorts by `(ExecutionOrder, Id)` before calling `ApplyFillOnly`. `ApplyAll` orders by
  `ExecutionOrder` with LINQ's stable `OrderBy` (fm `CreateTimeFieldMapping.cs` :198), so ties keep the order by rule id. The
  engine does not change.
- **Filing lookups.** A written `EntityReference` is checked with the lookup overload
  `RecordCreationService.IsFilingLookup(table, DataverseWriteItemMapper.MappedLookup)` (fm `RecordCreationService.Mapping.cs`
  :106), which also considers the referenced table, as well as the column overload (:95).
- **Same inputs, same output.** The same profile, source row and metadata give the same columns. A test runs the step twice
  on shuffled rule lists and compares the results.

## 4. Principal, probe and masking

These rules are unchanged from round 1, and UAC-r2 accepted them:

- The step reads app-only as the system principal (`MappingSourceReader.System`, fm `MappingSourceReadProbe.cs` :47), which
  is the Repair model. There is no OBO, no impersonation and no FR-04 probe. The source is the submission's core record,
  authorized at intake (eb `InvoiceSubmissionService.cs` :31-32).

The masking rules now read:

| Rule | Mechanism | Fail closed |
|---|---|---|
| M1 Field security | A source column marked `IsSecured` is never read (§3.2 step 5). The allow-listed targets are not secured, and a target that becomes secured is dropped. | A metadata fault, or an unknown `IsSecured`, maps nothing (Q-U4 a). |
| M2 Partner-readable | The allow-list holds no column in any invoice module's `ReadableColumns` (a test pins this, §10 G3). At runtime the registry's set is also part of the deny-list. | No registry, or no invoice module, maps nothing (`Fault(partner_columns_unknown)`). |
| M3 Access and filing | §6.2. | Static sets, plus the bound registry. A fault reading it maps nothing. |
| M4 Secure source | §5. | Every read the gate makes fails closed. |
| Reader set (Q-U1) | A role-source assertion: for every role, `sprk_invoice` Read depth ≤ the smallest Read depth of `sprk_matter`, `sprk_project` and `sprk_workassignment` (§10 G5). | A role file that cannot be read fails the test. |

## 5. The secure-source gate (Q-U5, C2, UAC-r2 follow-up)

Nothing is mapped when any of the three parts below says no. A part that cannot answer also maps nothing. "Gated" outcomes
are not faults: they log a code (`fieldmapping.invoice_extraction.gated.<code>`) and **write no admin note**. Only a failed
read writes the note (Q-E2 "faults only", §8.3).

### 5.1 The owner answer's secure-if-any (C2)

The persister has already resolved the owner from the core record (eb :476-485). `RecordOwnerResolution.IsSecureOwner` (fm
`RecordOwnershipResolver.cs` :1514) is true when the owner team is the Secure Record unit's named owner team: the core record,
or one of its ancestors, is Secure. `InPlaceOfDefaultTeamId` (fm :1522-1525) is set only on a hard-deny row's answer. Either
one returns `Gated`, before any read (EB-D-207: "Nothing is mapped when the owner answer is Secure (persister :476)").

### 5.2 No Access entries (UAC-r2 follow-up)

This part uses the same three calls the per-record No Access read makes (fm `RecordNoAccessEndpoint.cs` :204-237):

1. `NoAccessShareEnforcer.ReadCoverageAsync(core table, core id)` (fm `NoAccessShareEnforcer.cs` :434). This is "the ONE
   answer to which ACTIVE No Access entries cover this record". It covers entries naming the record, entries on an
   organization the record references (internal walls), and entries covering any Secure record it is filed under.
2. `NoAccessEnforcementStore.ReadEntryAsync(entryId)` for each covering entry (fm `NoAccessEnforcementStore.cs` :96).
3. `NoAccessShareEnforcer.TryClassify(entry.Row, …)` (fm :261). Its record-id rule is `NoAccessListReader.TryParseObjectRecordId`.

The outcomes:

| Answer | Result |
|---|---|
| `!coverage.IsReadable` (fm :119-125) | `Fault(no_access_unreadable)`: nothing mapped, note written |
| An entry read throws | `Fault(no_access_unreadable)` |
| `coverage.Truncated` | `Gated(no_access_entry)`: more than `MaxEntriesPerRecord` (fm :364) entries exist |
| An entry is null or inactive | Ignored, as the endpoint ignores it |
| An entry is active but `TryClassify` is false (malformed) | Ignored; a malformed entry walls nothing (fm :252-256) |
| An entry is active and well-formed, any subject kind | `Gated(no_access_entry)` |

**Deviation from the letter of the follow-up, for UAC-r2 to confirm.** The follow-up names `NoAccessListReader`. Its query,
`GetDeniedRecordsAsync` (fm `NoAccessListReader.cs` :210-245), is keyed by subject: it answers "is this record denied to
these contacts, organizations or users". The step has no subject. It needs "does any entry cover this record", and that is
`ReadCoverageAsync`. The coverage is also broader than "the core record as its object", because organization walls and
Secure-parent entries count too. That means it fails closed more often.

### 5.3 Repair's secure-source logic, reused by moving it (Q-U5: "reuse, not a re-implementation")

Today the logic is private to Repair: `SourceMayBeCopiedAsync` and `SourceGateColumns` (fm `RecordCreationService.Repair.cs`
:744-812), and also the target fit checks `TargetColumnValue`, `IsNumberType`, `AsNumber` and `InRange` (:594-692). Reuse
needs them reachable, so:

- A new static class, `Services/RecordCreation/MappingWriteGates.cs`, receives the bodies **unchanged**:
  - `MappingSourceCopyGate.DecideAsync(string sourceTable, Entity read, EntityReference? rowOwner, Func<CancellationToken,
    Task<(Guid? TeamId, string? Refusal)>> secureOwnerTeam, CancellationToken ct)` returns `Allowed`, `Secure` or
    `Unknown(reason)`;
  - `MappingSourceCopyGate.SourceGateColumns(table)`;
  - `MappedTargetFit.Check(columns, column, value)`, which is `TargetColumnValue` and its helpers.
- Repair's private methods become wrappers. They keep their log lines and map `Allowed` → true and the others → false, so
  Repair behaves exactly as before. The Repair suites are the regression proof: `RecordRepairColumnMetadataTests`,
  `RecordRepairColumnValueTests` and the Repair mapping tests, with no assertion changed.
- The step passes the candidate invoice's owner as `rowOwner`. Because §5.1 has already gated a Secure owner, a Secure or
  flagged source always comes back `Secure`: the row is never "isolated".

**This edits `RecordCreationService.Repair.cs`.** The owner ruling says "no change to `RecordCreationService`" (W3 direction:
no core redesign). The move adds no input and no mode, and Repair's behaviour is unchanged. It is still a diff in that file,
so it needs the owner's explicit approval (§14). The alternative, copying the logic, breaks UAC-r2's "not a
re-implementation".

## 6. Which targets may be written (C1, Q-U2, Q-U6, Q-E3, Q-E5)

### 6.1 The allow-list (primary check; PROPOSED, needs sign-off from both ebilling-r1 and UAC-r2)

The list is a static set, `InvoiceExtractionFieldMapping.AllowedTargets`, with one reason per entry. A test pins it exactly
(§10 G3), so any change shows in review. The types and lengths come from dev metadata (read-only, 2026-11-07) and the solution
(eb `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Invoice/Entity.xml`: both `ValidForCreateApi` 1, not secured).

| Column | Type | Reason it is safe to fill from the core record |
|---|---|---|
| `sprk_description` | Memo (max 5000) | Free-text context from the matter, project or work assignment. It is not in any invoice module's `ReadableColumns` (eb `ExternalAccessModule.cs` :528-545), not in `InvoiceIndexDocument.InvoiceColumns` (eb :48), and written by neither the persister nor extraction. Its readers are the core record's (§4, G5). A value over 5000 characters is not fit (§7). |
| `sprk_highpriority` | Boolean | The core record's priority flag. It is a rank input of the worklist (eb `DailyBriefingCollector.cs` :385) and decides no access. It is not partner-readable, not indexed, and not set by the persister. |

The list is limited to types whose fit `ColumnMetadata` can check fully. It has no Decimal, Double or DateTime column, so
precision and `DateTimeBehavior`, which `ColumnMetadata` does not carry (fm `Models.cs` :829-833), are never needed. A test
pins that every allow-listed column has an `AttributeType` in {`Memo`, `String`, `Boolean`, `Picklist`, `Virtual`
(multi-select), `Lookup`}. Adding a number or date column therefore fails until `ColumnMetadata` gains those facts (§14).

### 6.2 The deny-list (second check)

A target in any of these sets is refused even when it is allow-listed. A test pins that the allow-list and the deny-list do not
overlap, so on the shipped list this check fires only if someone widens the allow-list wrongly.

| Group | Columns | Source (by reference, never a copy) |
|---|---|---|
| Service protected set | Common server-owned columns, `sprk_invoice` `MappingProtectedColumns` (core lookups, review detail and money totals on eb, and `sprk_billingexport` once eb 165d lands), owner and creator columns, `sprk_createdbyperson` | `RecordCreationService.ProtectedFromMapping` (fm `RecordCreationService.Mapping.cs` :184-207) |
| App-only refusal | `ParentLineage.AccessColumns["sprk_invoice"]`, and `WriterOnlyColumns` once eb 165d lands | `InheritedAccessPermission.RefuseAppOnlyWrite`, asked per target |
| Lifecycle and identity | `statecode`, `statuscode`, `sprk_invoiceid` | eb `InvoiceLifecycleService.cs` :398-404; `InvoiceIdFor` |
| Persister-owned | Every column the persister writes on the invoice | **`InvoiceSubmissionPersister.OwnedInvoiceColumns`**, a new `internal static` set in the persister (C5; §10 G4) |
| Protected even when empty (Q-E3) | `sprk_currency`, `sprk_invoicedate`, `sprk_billingperiodstart`, `sprk_billingperiodend`, `sprk_correlationid` | Part of `OwnedInvoiceColumns`, and also named in a test |
| Search index (Q-E5) | `InvoiceIndexDocument.InvoiceColumns`; `sprk_searchindexname`, `sprk_ai_search_index` | eb `InvoiceIndexDocument.cs` :48 (by reference); `SearchIndexNameResolver` columns |
| Partner-readable (Q-U2) | The union of `ReadableColumns` of every registered module whose `RecordEntity` is `sprk_invoice` | `ExternalModuleRegistry.Modules`, read at runtime |
| Access-conferring (Q-U6) | Secure flag, access permission, access inheritance, `sprk_securitybu`, `sprk_containerid`, the regarding pair, the canonical and bound access-conferring columns | The same sources as Repair's never-map set (fm `RecordCreationService.Repair.cs` :712-741; `MembershipOptions.cs` :352-360 for `sprk_invoice`) |
| Contact, user and team lookups (Q-U6) | Any lookup whose metadata `Targets` include `contact`, `systemuser` or `team`, unless allow-listed (none are) | Metadata, per delivery |
| Filing | Any column `IsFilingLookup` (both overloads, §3.3) | fm :95, :106 |
| ebilling C1 | `sprk_billingexport` (AP export claim, EB-D-207 and 165d); `sprk_invoiceduedate` (unless the owner approves it) | Named constants in the step |

Lines, approval, Stored File rows and the submission row are never targets. Only the `invoice` local is passed to the step (G1).

### 6.3 The dev profile's targets, and other columns considered

Dev has one profile into `sprk_invoice`, read with a read-only Web API `GET` on 2026-11-07. It is "Matter to Invoice (Attorney
Matrix)": active, source `sprk_matter`, with 4 Copy rules (sync mode 1, execution order 1-4). All four targets are refused:

| Column (dev rule target) | Type | Why not proposed |
|---|---|---|
| `sprk_assignedtoattorney1`, `sprk_assignedtoattorney2`, `sprk_assignedtoparalegal1`, `sprk_assignedtoparalegal2` (and `sprk_assignedto1`, `sprk_assignedto2`) | Lookup → contact | They grant access: canonical access-conferring entries for `sprk_invoice` (fm `MembershipOptions.cs` :354-359). The persister runs no assigned-access pass, so writing them at create would record an assignment with no matching grant. Allowing them needs a UAC-r2 ruling, a grant path, and a carve-out from §6.2. That is out of scope (§14 option B). |

Other columns considered and left out:

| Column | Type | Reason |
|---|---|---|
| `sprk_monitor` | Boolean | A subscription to Know (eb `DailyBriefingCollector.cs` :386). It is a reader's choice, not a fact of the matter. |
| `sprk_workspaceflag` | Boolean | A user's workspace pin, toggled from the UI. |
| `sprk_billingapproverrole` | Multi-select choice | It decides who approves, an authority input (eb `EligibleApproverResolver.cs` :64, :88). |
| `sprk_recordsummary` | Memo | An AI-written summary (eb `InvoiceExtractionJobHandler.cs` :406). |
| `sprk_extractedjson`, `sprk_extractionstatus`, `sprk_confidence` | Memo, Choice, Decimal | Owned by extraction. |
| `sprk_visibilitystate` | Choice | External visibility. |
| `sprk_searchprofile`, `sprk_searchindexname`, `sprk_ai_search_index` | Memo, Text, Lookup | Search index. |
| `sprk_regardingagreement`, `sprk_regardingrecord*` | Lookup, Text | Filing. |
| `sprk_invoiceduedate` | DateTime | Excluded unless the owner approves it (C1). It would also need `DateTimeBehavior`, which `ColumnMetadata` lacks. |
| `sprk_billingexport` | Lookup | The AP export claim (EB-D-207). |
| Money totals, `sprk_firmhold`, `sprk_createdbyperson`, `sprk_issecure` | Money, Text, Lookup, Boolean | EB-D-200, field-secured, or access columns. |

## 7. Metadata and value fit (Q-U4, Q-E1 §6 checks)

- **Reader.** `IGenericEntityService.GetColumnMetadataAsync(table)` (fm `IGenericEntityService.cs` :157; 052a). It makes one
  uncached `RetrieveEntityRequest` per call and throws on a fault. It is called twice per delivery that reaches §3.2 step 5,
  for the core table and for `sprk_invoice`, and never cached (Q-U4 c).
  - An empty answer counts as a fault, as Repair's `ColumnMetadataForRepairAsync` treats it (fm :530-553).
  - The persister already holds `IGenericEntityService` (eb :179), so the step's copy is the same scoped instance.
- **Fit.** Every written column goes through `MappedTargetFit.Check` (moved, unchanged, §5.3). A column fails when:
  - it is unknown, or not `IsValidForUpdate == true` (the create-validity of allow-listed columns is pinned from `Entity.xml`
    by a test, because `ColumnMetadata` has no `IsValidForCreate`);
  - its type does not match;
  - it holds text longer than `MaxLength`;
  - it holds a number outside a stated `MinValue`/`MaxValue`, or the range is not stated;
  - it holds a lookup whose table is not in `Targets`, or `Targets` are not stated;
  - it holds an option, or any option of a multi-select, not in `OptionValues`, or the choices are not stated.

  Decimal precision is not checked, because no allow-listed column is a number (§6.1). Any failing column fails the whole step
  (§3.2 step 9).

## 8. Faults, all-or-nothing and notes

### 8.1 Outcomes

| Case | Mapped | Admin note (Q-E2) | Log | Persist outcome |
|---|---|---|---|---|
| Owner Secure or hard-deny; No Access entry; source Secure | nothing | **none** | `gated.<code>` | unchanged |
| No profile; no rules; no rule left after the allow-list and deny-list | nothing | none | `noop.<code>`; each skipped rule with its code and id | unchanged |
| Source column missing from the metadata | the other rules | none | Warning naming the rule id | unchanged |
| Profile, metadata, No Access, source or secure-team read fault | nothing | `<code>` | Warning with the code | unchanged |
| Any engine warning (C3) | nothing | `rule_fault` | each warning's rule id | unchanged |
| A value not fit (§7) | nothing | `value_unfit` | column and reason | unchanged |
| Registry absent | nothing | `partner_columns_unknown` | | unchanged |
| Any other exception in the step | nothing | `faulted` | exception type only | unchanged |
| `extractionMapping` not wired (null) | nothing | none | one Warning `not_wired` | unchanged |
| Request 0 refused with mapped columns (§9) | nothing (re-commit) | `create_refused_with_mapping` | | the re-commit's outcome |
| The job's token cancelled | n/a | n/a | | propagates |

"Unchanged" means the same status, reason code, ids, request order and request count as the same submission with no profile.

### 8.2 All or nothing (C3)

`ApplyFillOnly` adds a warning when a rule faults or refuses (fm `CreateTimeFieldMapping.cs` :211, :283-446). Because §3.2
step 3 removes every protected, regarding and denied target first, any warning that remains means a rule did not do what it
says. That includes "placeholder left blank" (:431) and "source value is not a lookup" (:363). So **any** warning maps nothing
and writes `rule_fault`. The engine does not change, and no `faultedColumns` parameter is added to `ApplyFillOnly`.

### 8.3 The admin-attention segment (Q-E2)

`AdminAttentionText` (eb) gains one segment kind:

- **Prefix.** `MappingPrefix = "Field mapping not applied: "`, followed by one code (≤ `MaxCodeLength`, 80). Codes only, never
  values (ADR-015).
- **Builder.** `MappingNote(code)`.
- **Fixed order.** Signals, policy, **mapping**, stopped, undone, then other text.
- **`Merge` (eb :175-230)** recognises the prefix, takes a `mappingNote` argument, keeps an existing mapping segment, and counts
  it in the room it leaves for the Signal segment.
- **`WithoutStopped` (eb :239-254)** keeps the mapping segment. A test pins this.
- **The commit write.** The persister builds the text with a new overload, `Build(items, mappingNote)`. It gives the Signal
  segment `AdminAttentionMaxLength − mapping segment − Separator` characters, so the Signal segment shrinks to make room. The
  mapping segment is at most 107 characters, so the Signal room stays above `MinSignalsLength` (80). The mapping segment is
  never cut.
- **Cap test.** 60 Signal refusals plus a mapping note give text of at most 500 characters. It ends with the whole mapping
  segment, and the Signal segment ends "… (+N more)". The same holds through `Merge` with a policy note and a stopped note.

### 8.4 Re-delivery (Q-E4)

A delivery that does not commit stores nothing. The committing delivery stores its mapped values and its note in one
transaction. A resumed submission never maps again. Each delivery recomputes from current inputs.

The test: delivery 1 maps "A", then its transaction fails at the submission update (rolled back). The profile and the source
change. Delivery 2 maps "B" and commits. The stored invoice holds exactly B's columns and B's note. Delivery 3 resumes and
makes no profile, metadata, No Access or source read: every one of those fakes throws if it is called.

## 9. Fixed ids, one transaction, and the single re-commit (Q-E1)

Unchanged: `InvoiceIdFor`, `LineIdFor` and `ApprovalIdFor`; the request list order; `guardIndex` and `submissionIndex` (eb
:697-716); the transaction bound (eb :718-723). The step adds no request.

**Re-commit.** The commit and its whole catch chain (eb :728-796) move into **one local function**:

```text
async Task<SubmissionPersistResult?> CommitAsync(bool mayRecommit)   // null = committed, continue to the after-commit code
```

The first call is `CommitAsync(mayRecommit: mapping is Applied)`.

In the generic `DataverseTransactionException` catch, the **first** test is:

```text
mayRecommit && ex.FailedRequestIndex == 0 && !ex.OutcomeUnknown   (the outcome-unknown catch comes first anyway)
```

When it holds, the function returns a "re-commit" marker. It does not run the rechecks. The caller then:

1. removes every mapped column from `invoice`;
2. calls `_lifecycle.BuildInvoiceCreateRequest(invoice, initial)` again and replaces `create.Requests` in place, at the same
   positions;
3. rebuilds the submission update at `submissionIndex` with `mappingNote = create_refused_with_mapping`, with the same row
   version and `IfRowVersionMatches`;
4. asserts that the request count and every index are unchanged (if not, it returns the ordinary `Retry`
   `TransactionFailedCode` and does not re-commit);
5. calls `CommitAsync(mayRecommit: false)`.

The second attempt goes through the whole existing chain: outcome-unknown re-read, source recheck, supersede recheck,
submission recheck, guard, then `Retry`. There is never a third execution.

Why this is safe:

- The first transaction rolled back (it was not outcome-unknown), so the fixed ids are free.
- A rollback does not move a row version.
- The guard and supersede requests are rebuilt from the same inputs, at the same indexes.

Tests (§11 T-R1):

| Case | Expected |
|---|---|
| Refused once | The second execution commits without the mapped columns, with the note, as `Persisted`. |
| Refused twice | `Retry` `TransactionFailedCode`, exactly two executions. |
| Refused with nothing mapped | `Retry`, exactly one execution. |
| Refused at an index other than 0 with mapping | The existing chain, one execution. |
| Outcome unknown at index 0 | The existing re-read, no re-commit. |

## 10. Guards

| # | Guard | What it pins |
|---|---|---|
| G1 | New `tests/Spaarke.ArchTests/InvoiceExtractionMappingGuardTests.cs` (Roslyn, the `MappingSourceCallerMaskGuardTests` method) | (1) `PersistCoreAsync` has exactly one `ApplyAsync` call, whose first argument is the local `invoice`, after `FillInvoice(` and the review-detail write and before `BuildInvoiceCreateRequest(`. (2) No `ApplyAsync` call in `ResumeAsync` or in any other file. (3) The persister and the step name no `RecordCreationService` member except `{ProtectedFromMapping, NormalizedRule, IsFilingLookup}`, and never `CreateAsync` or an instance. (4) The step's one `MappingSourceReadProbe.ReadAsync` call passes `MappingSourceReader.System` literally and builds its `MappingSource` from the `core` parameter only, never `MappingSourceResolver` or `ForCaller`. (5) The step calls `MappingSourceCopyGate.DecideAsync` and `ReadCoverageAsync` before `ApplyFillOnly`. Must-fire controls: each round-1 control, plus the gate call after the engine and a missing No Access call. Must-not-fire: the sanctioned shape, a comment, a string. |
| G2 | #2188 (`MappingSourceCallerMaskGuardTests.cs`) | One `UnmaskedCallExemptions` entry, `…/Services/Finance/Submission/InvoiceExtractionFieldMapping.cs\|InvoiceExtractionFieldMapping.ApplyAsync`, `Uses = 1`. `KnownCallSites` goes from 3 to 4 (fm :104). G1 (4) pins the entry to the system-reader call site (Q-U3). |
| G3 | Allow-list tests (unit, in `InvoiceExtractionFieldMappingTests`) | The exact set. No overlap with the deny-list, `ReadableColumns` of every invoice module from the real registry, `InvoiceIndexDocument.InvoiceColumns`, `OwnedInvoiceColumns`, the five Q-E3 columns, the access-conferring registry, `sprk_billingexport` or `sprk_invoiceduedate`. Every entry has a full-fit type (§6.1) and `ValidForCreateApi` 1 in `Entity.xml`. Must-fire seeds: add `sprk_invoicedate`, `sprk_assignedtoattorney1` or a Decimal column to a copy of the list. |
| G4 | Money guard (C5): `InvoiceMoneyColumnsAppOnlyWriteGuardTests` (eb :38) | The step never names a money or firm column. The deny-list reaches them only through `InvoiceSubmissionPersister.OwnedInvoiceColumns` and `ProtectedFromMapping`, so `InvoiceExtractionFieldMapping` is **not** added to `Writers` or `Readers`. The guard joins the regression list unchanged. If its scan counts a type that only references the set, ebilling-r1 decides whether the step is listed under `Readers`. |
| G5 | Role-source assertion (Q-U1), a new fact in `FeatureRolePrivilegeGuardTests` (fm :53) | For every role file under `RolesDirectory` (fm :695): `Rank(prvReadsprk_invoice) ≤ min(Rank(prvReadsprk_matter), Rank(prvReadsprk_project), Rank(prvReadsprk_workassignment))`. A role holding invoice Read without one of the three core Reads fails. It reuses `LoadRoles` and `Rank`. Must-fire: a seeded role document with invoice Read at Global and matter Read at Local. Must-not-fire: the real files. |
| G6 | Census | `RecordOwnerAssignmentCensusTests`' `UnscannedWriter` entry for the persister is unchanged and passes. |

## 11. Test plan

The new tests live at KEEP paths: `tests/unit/domain/Finance/Submission/*`, `tests/unit/domain/RecordCreation/MappingWriteGatesTests.cs`
and the ArchTests above. They reuse ebilling's persister fakes rather than copying them. Every behaviour test is shown red
against the code without the change.

| Id | Test | Kind |
|---|---|---|
| T-P1 | A fully populated outcome in every variant (firm or hold; with and without date, period and currency; correction and automatic supersedes; review detail; submitter and contact) sets only columns in `OwnedInvoiceColumns` | must-fire if a new persister column is not listed |
| T-P2 | One test per §6.2 group and per Q-E3 column: an allow-listed copy of the column is still refused. The Q-E3 columns are also tested with the persister leaving them empty | must-fire |
| T-P3 | A copy into `sprk_description` and `sprk_highpriority` fills them, for matter, project and work-assignment core records | must-not-fire |
| T-P4 | A rule into a column outside the allow-list is skipped, including the dev profile's four targets | must-fire |
| T-G1 | Gate. The owner is `IsSecureOwner` → nothing mapped, **no note**, a log code, no profile read. Hard-deny owner → the same. An active, well-formed No Access entry covering the core record (record object, organization wall, Secure parent) → nothing, no note. Coverage unreadable, or an entry read fault → nothing, **note**. Truncated → nothing, no note. Inactive or malformed entry → maps. A flagged or Secure-team source → nothing, no note. Secure team unresolved → nothing, note. An ordinary core → maps | must-fire / must-not-fire pairs |
| T-S1 | A secured source column is not read and not mapped, and the others are. A metadata fault, an empty answer or an unknown `IsSecured` → nothing, note. A missing source column → only its rule dropped, with a Warning naming the rule id | pair |
| T-S2 | A metadata reader that counts calls: two calls per delivery, and two again on the next delivery (no cache) | must-fire on a cache |
| T-C3 | Any engine warning (a rule that throws, a placeholder left blank, a non-lookup source into a lookup) → nothing mapped, note `rule_fault` | must-fire |
| T-C4 | Shuffled rule order with equal `ExecutionOrder` → the same result, decided by rule id. Filing lookup refused through the lookup overload | determinism |
| T-V1 | Each `MappedTargetFit` refusal (type, length, range, target table, option) → nothing mapped, note `value_unfit` | must-fire |
| T-F1 | Profile, source and secure-team read faults and a thrown exception: same status, code, ids, order and count as no profile, with the note | must-fire |
| T-F2 | No profile, empty profile, every rule skipped: no note | must-not-fire |
| T-T1 | Ids, request order and count are the same with and without mapping. Lines, approval, Stored File rows and the submission update carry no mapped column | shape |
| T-T2 | A resumed submission makes no read: profile, metadata, No Access and source fakes all throw | must-not-fire |
| T-I1 | The source read names `outcome.CoreRecord`'s table and id with `MappingSourceReader.System`. No OBO client is touched | identity |
| T-R1 | §9's five cases | pair |
| T-E4 | §8.4's two-delivery test | Q-E4 |
| T-A1 | `AdminAttentionText`: the cap test (§8.3), `Merge` order, `WithoutStopped` keeps the mapping segment, the Signal segment shrinks | Q-E2 |
| T-DI | Build the real container (`Program`'s module registrations, the `ADR010_DITests` and DI-test host): resolve `IInvoiceSubmissionPersister`; its `extractionMapping` is non-null, and so is every dependency of `InvoiceExtractionFieldMapping`. With `extractionMapping` null, a persist logs exactly one Warning `not_wired` and maps nothing | Q-E6 |
| T-M1 | `MappingWriteGates`: the moved logic's cases (from the Repair suites' expectations) called directly. The Repair suites pass unchanged | move proof |

Regression, with unchanged assertions:

- ebilling's persister and handler suites;
- `InvoiceMoneyColumnsAppOnlyWriteGuardTests`;
- `MappingSourceCallerMaskGuardTests` (apart from the entry);
- `RecordOwnerAssignmentCensusTests`;
- `ADR010_DITests`;
- `FeatureRolePrivilegeGuardTests` (apart from G5);
- the Repair suites: `RecordRepairColumnMetadataTests`, `RecordRepairColumnValueTests` and the Repair mapping tests;
- the field-mapping unit suites (`CreateTimeFieldMapping*`, `PushCopyParityTests`).

## 12. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | A mapped value Dataverse refuses rolls the submission back. | §7 fit, then §9's one re-commit without the mapping. |
| R2 | Roles that read invoices more widely than the core table. | G5 makes such a role fail CI. |
| R3 | Invoice columns reach partners by another path. | EB-D-207: partners have no invoice search path, and notifications (eb task 132) render only `ReadableColumns` or protected columns. The allow-list holds neither. |
| R4 | A column is secured after the metadata read. | Read per delivery, no cache. |
| R5 | Notes flood the view during an outage. | Faults only; gated outcomes and configuration skips never write a note. |
| R6 | The moved Repair logic drifts from Repair. | One copy: Repair calls the shared class; T-M1 and the Repair suites. |
| R7 | Rebase friction with ebilling work near the persister (eb 095d-2b-3, 165d, 132). | Base-branch plan (§15) and `/conflict-check`. |
| R8 | More reads per persisted submission: profile, plus, only when rules apply, No Access, two metadata reads and the source. | Background job. Most submissions stop at the profile read. |
| R9 | A persister edit adds an invoice column after the step. | G1 order pin; T-P1. |
| R10 | FR-20 gives little value on dev (§6.3). | Owner choice (§14). |

## 13. Review questions: resolved

| Question | Answer (2026-11-07) | Where met |
|---|---|---|
| Q-E1 Refused mapped value | Re-commit once, under conditions | §9 |
| Q-E2 Fault note | A new segment, faults only, within the cap | §8.3 |
| Q-E3 Protected even when empty | The five columns | §6.2, T-P2 |
| Q-E4 Re-delivery | Two-delivery test, and a resume reads nothing | §8.4, T-E4, T-T2 |
| Q-E5 Search index | `InvoiceColumns` by reference; partners have no search path | §6.2 |
| Q-E6 Constructor | Three optional parameters and a DI test | **Changed to one parameter, needs re-confirmation** (§3.1, §14) |
| C1 Allow-list | Primary, with the deny-list second | §6 (the list needs sign-off) |
| C2 Secure → no note | Log a code | §5, §8.1 |
| C3 All or nothing | | §8.2 |
| C4 Determinism | | §3.3 |
| C5 Money guard | | §10 G4 |
| Q-U1 Reader set | Role-source assertion | §10 G5 |
| Q-U2 Partner columns | `ReadableColumns` plus the index columns | §6.2 |
| Q-U3 #2188 | One entry, `Uses = 1`, pinned | §10 G1 (4), G2 |
| Q-U4 Field security | Fault or unknown → nothing; missing → drop rule with a Warning; per delivery | §3.2 step 5, §7 |
| Q-U5 Secure gate | Required, reuse Repair's logic | §5.3 (**the move needs the owner's approval**, §14) |
| Q-U6 Lookups | Contact, user and team lookups protected unless allow-listed | §6.2 (none are allow-listed) |
| UAC-r2 follow-up | No Access gate; a failed read maps nothing | §5.2 (**`ReadCoverageAsync` in place of `NoAccessListReader`**, §14) |
| Q-F1 Metadata reader | 052a has landed | §7 |

## 14. Open for the owner, ebilling-r1 and UAC-r2 (round 3)

1. **Moving Repair's gate and fit logic** (§5.3) edits `RecordCreationService.Repair.cs`. It is a move with no behaviour
   change, but the owner ruling says "no change to `RecordCreationService`". Owner: approve the move, or choose a copy, which
   UAC-r2 would have to accept against "not a re-implementation".
2. **The allow-list** (§6.1): `sprk_description` and `sprk_highpriority`. ebilling-r1 and UAC-r2 to sign off or amend.
   `sprk_invoiceduedate` stays out unless the owner approves it. It is also a DateTime column, which needs `ColumnMetadata` to
   gain `DateTimeBehavior` (an additive 052a follow-up).
3. **FR-20's value with these conditions** (§6.3). The dev profile maps only access-conferring Contact lookups, which stay
   refused. Owner options:
   - **(A)** ship the narrow list (recommended for R1);
   - **(B)** a UAC-r2 ruling on access-conferring invoice targets, plus an assigned-access pass for job-created invoices, as
     its own task;
   - **(C)** defer FR-20 for invoices, as the W3 direction first did.
4. **Q-E6 shape** (§3.1): one optional parameter for a scoped class, instead of three parameters. ebilling-r1 to confirm, or
   pick the five-parameter fallback.
5. **No Access read** (§5.2): `ReadCoverageAsync` with `TryClassify`, in place of the subject-keyed `NoAccessListReader`.
   UAC-r2 to confirm.
6. **Admin note wording.** UAC-r2's Q-U5 test text says "a Secure core maps nothing and writes the admin note". ebilling-r1's
   C2 says no note. This design follows C2 (no note, a log code). UAC-r2 to confirm that the Q-U5 test asserts "no note".

## 15. Base branch

The persister layout exists only on `work/ebilling-r1`. It is 578 commits ahead of master, and
`work/field-mapping-server-write-path-r1` has 319 commits that `work/ebilling-r1` does not (master's own history plus fm's 11).
052a's metadata reader is on the fm work branch only.

Recommended:

- **032a** (§16) needs only fm code: the move, G5, and `MappingWriteGates` tests. Cut it from
  `work/field-mapping-server-write-path-r1` now.
- **032b and 032c** touch the persister, `AdminAttentionText` and `FinanceModule`. Cut them from master **after ebilling-r1
  and the fm work branch (with 052a and 032a) have both merged to master**. That gives one base with every piece and no
  cross-project merge.
- **If R1 cannot wait:** cut 032b and 032c from `work/ebilling-r1` and merge the fm work tip into them (which brings master's
  history with it), with ebilling-r1's agreement that the PR targets `work/ebilling-r1`. Sequence them after eb 165d
  (`RefuseAppOnlyWrite` `WriterOnlyColumns`) and 095d-2b-3, and run `/conflict-check`.

## 16. Size and split

| Part | Estimate |
|---|---|
| 032a: `MappingWriteGates.cs` (moved, ~230 lines), Repair wrappers (~40), T-M1 (~250), G5 with controls (~120) | 5-7 h |
| 032b: `InvoiceExtractionFieldMapping.cs` (~450), the allow-list and deny-list, unit tests T-P*, T-G1, T-S*, T-C*, T-V1, T-F*, G3 (~1,100), the G2 entry | 9-12 h |
| 032c: persister wiring (parameter, call, `OwnedInvoiceColumns`, local-function commit and re-commit, ~150), `AdminAttentionText` segment (~60), `FinanceModule` registration (1), persister tests T-T*, T-I1, T-R1, T-E4, T-A1, T-DI (~700), G1 (~350), G4 regression | 8-10 h |

The total is about 22-29 hours at Sonnet/high and FULL rigor, which is more than one ~100-step agent can do. Each of the three
is one PR within that bound.

- 032a and 032c each get one adversarial-review pass. 032b gets two (security).
- Order: 032a, then 032b, then 032c. 032b's class is not called until 032c lands.

## 17. Not changed

- `RecordCreationService*.cs`, except the §5.3 move (owner approval) and its private wrappers in `Repair.cs`;
- `CreateTimeFieldMapping` (no `faultedColumns` on `ApplyFillOnly`);
- `RecordTableRules.cs`;
- `InvoiceSubmissionRerunJobHandler.cs`;
- the handler;
- the owner path and the census;
- the lines, approval and Stored File builders;
- the transaction order;
- every endpoint;
- `ColumnMetadata`;
- no package.

One new DI registration (`InvoiceExtractionFieldMapping`, scoped, concrete); its justification is in the POML.
