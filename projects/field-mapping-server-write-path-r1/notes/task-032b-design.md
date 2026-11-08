# Task 032b design: Matter→Invoice mapping with the assignee columns (option B)

Status: **design round 2, no code.** Round 1 (`2692910f2d`) was reviewed by ebilling-r1 (E1–E6 agreed, with conditions) and
by UAC-r2 (approved with C1 and C2). The owner then asked about after-save versus inline placement (§8). This round answers
C2 with a caller inventory. U3 lands on **(c)**: the invoice copies confer no access. So the grant machinery of round 1 is
deleted, and the recommended placement returns to **inline, before the persister's transaction** (the 2026-11-06 owner
ruling). The round-1 job design is kept in §8.4 only as the rejected alternative.

Trees: **fm** = `work/field-mapping-server-write-path-r1` 57648a15a2; **eb** = `work/ebilling-r1`. Line numbers are at
31afe98fa6; re-grep against d58e3a3554 (082d merged), the base for 032b-2 and 032b-3. Round 2 of task 032
(`task/fm-032-design` 60ecb8e9e6, `notes/task-032-design.md`) holds the detail this note reuses by section number.

## 0. Summary

1. UAC-r2's C2 asked whether any code resolves membership for `sprk_invoice` and grants rights from it. **None does**, on
   master or on `work/ebilling-r1` (§3). `sprk_invoice` is removed from the access-conferring registry. The four
   attorney and paralegal columns become **display copies**: an explicit list with a Q-U6 exception.
2. The mapping runs **inside `PersistCoreAsync`, before the transaction**. Reads and gates come first. The values go into
   the invoice create payload (request 0), and there is no second write path to `sprk_invoice`.
3. A gate or rule fault **maps nothing, and the invoice is still created**, with an admin-attention code (E7, agreed). A
   mapping fault never changes the persister's outcome or its Decision Records.
4. All round-2 gates stand: Secure owner answer, No Access coverage, field security, Repair's secure-source logic, the
   allow-list, all-or-nothing, determinism, and the money guard.
5. Rule for every record type carrying `sprk_assigned*` columns: **the values are in the create; grants may follow** (§8.5).

## 1. Flow

The step sits in `PersistCoreAsync` after `FillInvoice`, the creator stamp, the supersede link and the review detail (eb
:546-612), and immediately before `_lifecycle.BuildInvoiceCreateRequest` (eb :614). This is round 2 §2. The call is
`await _matterMapping.ApplyAsync(invoice, outcome.CoreRecord!, owner, ct)`. It receives only the in-memory invoice, the
core record and the owner answer that the persister has already resolved; no extraction data is passed.

| Status | Effect on the invoice | Admin note (submission) |
|---|---|---|
| `Applied(columns)` | The columns are set on `invoice`, so request 0 carries them | none |
| `NoOp(code)` | No profile, no rule left, or nothing empty to fill | none |
| `Gated(code)` | Nothing set (Secure, No Access, a display copy found access-conferring) | none (C2) |
| `Fault(code)` | Nothing set; `invoice` untouched | `Field mapping not applied: <code>` |

The commit is unchanged except for round 2 §9's **single re-commit**. If request 0 is refused at index 0 with mapped columns
present, the persister removes them, rebuilds request 0 and the submission update at the same indexes with the note, and
commits once more through the existing catch chain. A resume (a linked invoice, `ResumeAsync`) never maps.

**ebilling-r1's inline checks:**

| # | Check | How it is met |
|---|---|---|
| 1 | Reads and gates finish before the transaction; never half-mapped | `ApplyAsync` completes before `BuildInvoiceCreateRequest`. It works on a candidate copy and copies onto `invoice` only on `Applied` (round 2 §3.2 step 11). The re-commit removes every mapped column together. |
| 2 | Values only in the existing create payload | The step writes no row. It adds no request, and the request count and order are unchanged (T-T1). |
| 3 | Ownership columns through the resolver | The owner is resolved before the step (eb :476). Owner, team and BU columns are in `ProtectedFromMapping` (class C). |
| 4 | Persister tests unchanged; a refused or faulted gate writes no mapped value | §9 T-E7 and T-F1 |
| 5 | `sprk_findingsjson`, including the internal `procedureSnapshot` member (082d), is never a source or a target | `sprk_findingsjson` is on the submission; the step only sees `invoice`. It is also named in class C, and the source is the core record only. |

## 2. Column classes

| Class | Columns | Decided by |
|---|---|---|
| **A, plain** | `sprk_description`, `sprk_highpriority` (E6, agreed) | Static `AllowedPlainTargets` |
| **B, display copies** | `sprk_assignedtoattorney1/2`, `sprk_assignedtoparalegal1/2`, copied from the core's `sprk_assignedattorney1/2` and `sprk_assignedparalegal1/2` (seeded profile, provisioning seeder `DataverseWebApiFieldMappingSeeder.cs` :175-189) | Static `AllowedDisplayCopies`. This is the **only** Q-U6 exception for contact lookups. At runtime the step also requires `PushRuleSelection.AssignedToColumnsOf(bound registry)` to answer **false** for each copy. If it answers true (an environment still binds the old entry), that column is `Gated(display_copy_confers)`. |
| **C, never** | Round 2 §6.2's deny-list (by reference), plus `sprk_assignedto1/2`, `sprk_findingsjson`, and every contact, user or team lookup not in B | Second check over A and B |

**E6 condition, recorded with the §7 constraint:** `sprk_description`, `sprk_highpriority` and the four display copies may
never join an external invoice module's `ReadableColumns` (eb `ExternalAccessModule.cs` :528-545) or the firm-facing
rendering (EB-D-207, task 132) without returning to ebilling-r1 and UAC-r2. Test G3 pins the allow-lists against the live
`ReadableColumns`.

## 3. Access: the U3 answer (C2)

**Every membership resolution in the BFF, master (fm tree) and `work/ebilling-r1` alike:**

| Caller | Entity types | Rights from the invoice entry? |
|---|---|---|
| `AccessibleRecordSetService` (`AccessConferringOnly`), :2384 systemuser, :2879/:2959 contact | Only what `CallerPrincipalResolver.cs` :831-835 composes: project, matter, work assignment. No other `ComposeAsync` / `IsRecordAccessibleAsync` caller exists. | **No**: never `sprk_invoice` |
| `DailyBriefingCollector` via `PeopleTargetedSet` (:168 `EntityInvoice`, :412-415, :722) | Seven tables, including `sprk_invoice` | **No**: people-targeting "selects; it does not authorize" (`IMembershipResolverService.cs` :194-214). Rows are read as the caller (`DailyBriefingCollector.cs` :333). |
| `LookupUserMembershipNodeExecutor` :318 | Playbook config | **No**: emits ids to later nodes, under the AI-scoping or people surface |
| `MembershipEndpoints` :269 | Route parameter | **No**: AI-scoping default, which does not filter by registry (`IMembershipResolverService.cs` :152-162) |
| `PortfolioService` :387 | Matter only | **No** |
| `EligibleApproverResolver` (eb :139-168) | Reads the **core's** role columns | Not a membership call; the invoice copies are not read |

`work/ebilling-r1` adds no new membership caller (`git grep` for `IMembershipResolverService` and `ResolveByContactAsync`).
The only production reference to the invoice assignee columns outside the registry is the Office invoice quick-create's
`sprk_assignedto1` (eb `OfficeService.cs` :2697), which is a writer.

**So U3 → (c).** Removing `sprk_invoice` from `MembershipOptionsDefaults.CanonicalAccessConferringRegistry` (`MembershipOptions.cs`
:352-360) and from any environment's bound `Membership:AccessConferringRoles` has these effects:

- **No grant exists or is owed.** Round 1 §3 is deleted: the grant step, verify, `GrantedAssignment` and I-G. The
  stale-copy problem goes away: after a re-file or an ended assignment, a copy is out-of-date display text that confers
  nothing.
- **Push** (`FieldMappingEndpoints.cs` :558, `PushRuleSelection.cs` :51-52): the four invoice targets stop being Assigned-To
  columns. They are seeded as One-time, so an ordinary push still skips them, and `includeAssignedTo` no longer adds them.
  Invoice copies are therefore never re-pushed. **This is intended**, and it matches "no re-map". The class tests
  (`SyncModeMigrationClassDataTests`, `FieldMappingSeederSyncModeClassTests`) are updated with the registry change.
- **Daily Briefing:** its invoice channel no longer selects an invoice *for* a person through the invoice's Assigned
  columns, only through Created By or a user owner. No extracted invoice carries copies today, so nothing that exists is
  lost. Office quick-create invoices with `sprk_assignedto1` drop out of that selection. Recommendation: accept it (the
  core record is still selected), or let UAC-r2 decide (U7).
- **E7 condition 1 (fail closed for access)** holds trivially: copies confer nothing, so an unmapped invoice is exactly as
  reachable as a mapped one. **U6 (conditional):** if U3 is ever re-opened so that copies confer or narrow access, then
  "map nothing on fault" needs UAC-r2's explicit OK.
- **Guard:** a test asserts `AllowedDisplayCopies ∩ AssignedToColumnsOf(canonical registry) = ∅` and that the registry has
  no `sprk_invoice` entry. Re-adding the entry fails CI (must-fire seed: a registry with the entry).

**Sequencing:** the registry change (UAC-r2-owned, or ours with their review) lands before or with 032b-2.

## 4. Gates, in order (all before the transaction)

| # | Gate | Fail-closed outcome |
|---|---|---|
| 1 | Core pin (C1): the source is `outcome.CoreRecord`, the same object written into the invoice's core lookup (eb :459-462). An argument guard refuses any other core. | Mismatch → `Fault(core_mismatch)` |
| 2 | Owner answer (C2): `owner.IsSecureOwner`, or hard-deny (`InPlaceOfDefaultTeamId`) | `Gated(secure)`, before any read |
| 3 | Profile (core table → `sprk_invoice`), active rules, ordered by `(ExecutionOrder, Id)` | Fault → `Fault`; none → `NoOp` |
| 4 | Class filter (§2) | None left → `NoOp`; skips are logged |
| 5 | No Access: `ReadCoverageAsync` + `ReadEntryAsync` + `TryClassify` (U5 approved) | Covered or truncated → `Gated`; unreadable → `Fault` |
| 6 | Metadata per delivery, no cache (Q-U4) | Fault or unknown `IsSecured` → `Fault`. A secured source column is not read. A missing source column drops its rule, with a Warning. |
| 7 | Source read (`MappingSourceReader.System`) + `MappingSourceCopyGate.DecideAsync` | `Secure` → `Gated`; unknown → `Fault` |
| 8 | Engine `ApplyFillOnly` on a candidate copy | Any warning → `Fault(rule_fault)`, all or nothing (C3) |
| 9 | Fit `MappedTargetFit.Check`; re-run `InvoiceFamilyCoreRecordGuard.Check` | → `Fault` |
| 10 | Display-copy runtime check (§2 B) | → `Gated(display_copy_confers)` for that column |

**E7 (agreed with ebilling-r1, D-101):** every fault maps nothing and the invoice is created. ebilling-r1's first wording
("a gate fault refuses the whole submission") was withdrawn: mapping is an optional fill, and intake must not fail because
a copy could not be made. Both readings satisfy "never half-mapped".

**E7 conditions:**
- The note carries **ids and a reason code only, never a fault message**. It sits on no firm-readable surface (the SPA
  projection, 165e/165f).
- The outcome, its reason code, the ids, the request order and count, and the Decision Records are identical to an
  unmapped run.

**E4 condition:** the note is written by the existing `AdminAttentionText` builder into the submission update the persister
already sends. It stays within 500 characters, with the Signal segment shrinking (round 2 §8.3). It writes no status and
no other column.

## 5. Identity

There is no caller. The persister runs in an ADR-004 job as the **BFF application user**, app-only. The step reads the
profile, metadata, No Access entries and the core row with that identity: the Repair model, accepted by UAC-r2 (§5 accepted
with C1).

- The source is the core that intake authorized (eb `InvoiceSubmissionService.cs` :31-32).
- The target is the invoice the persister is creating, with an id derived from the submission.
- Secured source columns are never read.
- The #2188 caller-mask exemption is one entry, `Uses = 1`, pinned to the system-reader call site.

## 6. Replay and idempotency

- One transaction carries the invoice and its mapped values, so a host crash leaves either nothing or everything; there is
  no "grant before columns" window.
- A rolled-back delivery stores nothing. The next delivery recomputes from current inputs (Q-E4 two-delivery test).
- A resume makes no mapping read.
- The re-commit runs at most once (round 2 §9).
- Determinism: static resolver keys, `(ExecutionOrder, Id)` order, and the lookup overload of `IsFilingLookup` (C4).

## 7. Re-file and later changes

- **No re-map.** Only the persister create maps. The Set-firm re-run, Add line, approval and re-file (D-96) do not, and
  push never touches the copies (§3). A Correction or Resubmission is a new invoice and maps from the current Matter.
- After a re-file or an assignee change, a copy is stale **display text with no access effect** (U3 (c)).
- **Constraint (ebilling-r1, binding):** no ebilling decision may ever read the invoice-level copies. Approval and its kin
  decide only from server-written sources (EB-D-200, EB-D-208). The E6 condition (§2) sits beside this one.

## 8. Placement: after-save job versus inline (owner question O1)

### 8.1 Effects

| Effect | After-save job | Inline in the create |
|---|---|---|
| Record visible without the values | Yes: seconds normally, minutes on retry, for ever if dead-lettered | Never |
| "On create" consumers (create-message triggers, create-time snapshots) | See the values empty | See the values |
| "On update" consumers | A second update fires them, from the app user, with a second audit row and a changed `modifiedon` | Nothing extra |
| Form OnLoad / OnChange | A server write fires no OnChange either way. A form opened in the window shows blanks and does not refresh. | OnLoad shows the values |
| Power Automate / Dataverse triggers with column filters | "Added" sees blanks; "Modified" filtered on these columns fires | "Added" sees the values; nothing modified |
| BFF jobs reading the columns | Race the mapping job (for example the acceptance index job, enqueued in the after-steps) | Always see the values |
| Search index and AI profile snapshots at create | Snapshot blanks | Snapshot the values |

### 8.2 Inventory of `sprk_assigned*` consumers today (any table)

| Consumer | Where | When it reads | Invoice copies? |
|---|---|---|---|
| Assigned-To pass (PostCreate inline maker grant + 049 queue) | `RecordCreationService.cs` :1877, :1890, :1944-2059; `AssignedAccessCreateGrantJobHandler` | After create, roots only (`AssignedAccessMaterializer.cs` :363; A6 :175) | No |
| Reconciler, push and sync endpoint | `AssignedAccessReconciliationJob`; `FieldMappingEndpoints.cs` :1621; `AssignedAccessSyncEndpoint` | Sweep; after a push; MDA post-save | No (roots only) |
| Form post-save script `sprk_assignedaccess_postsave.js` | Matter, Project and Work Assignment main forms; referenced from 13 tables' `RibbonDiff.xml`, including `sprk_Invoice` | After save | No: the sync endpoint resolves roots only (`AssignedAccessSyncEndpoint.cs` :158-161) |
| Membership resolution | §3 table | Query time | Registry only; removed |
| Approver eligibility | eb `EligibleApproverResolver.cs` :139-168 | Query time, core columns | No |
| Signals and decisions (to-do and event `sprk_assignedto*`, `sprk_assignedtointernal`) | `DecisionActionExecutors.cs` :861-1491; `DecisionRecordWriter.cs` :81ff; `PersonalToDoAssignee.cs` :15 | Action time | No |
| Client reads (to-do, event, record header, VisualHost) | `SmartTodo`, `LegalWorkspace` `queryHelpers.ts`, `EventDetailSidePane`, VisualHost due-date cards | Display time | No |
| Invoice index | eb `InvoiceIndexDocument.InvoiceColumns` | Acceptance | Excluded (Q-E5) |
| Flows, classic workflows, plugins | None in `src/dataverse/solutions/SpaarkeMaster` (no Workflows folder; ADR-002) | n/a | n/a |
| Invoice main form | Does not carry the four columns | n/a | n/a |

Headline: **nothing reads the invoice copies at create, update or later.** The only time-sensitive consumers of
`sprk_assigned*` are root grants, which are post-create by design and do not apply to invoices. The risk of after-save is
therefore future and customer-added consumers, especially create- or update-triggered flows, which no repo artefact can
rule out.

### 8.3 How the other inheriting types work

- **Matter, Project, Work Assignment** (wizards, chat, Office): through `RecordCreationService`. Mapped values and
  caller-entered values are in the create (FR-02). The grant follows: queued since 049, with the maker's share inline.
- **Event and Report Card:** mapped from the Matter in the create (seeded profiles, seeder :159, :191). No grant (A6).
- **Office invoice quick-create** (`sprk_assignedto1`): in the create.

The inline option matches this model; the job does not.

### 8.4 Comparison and recommendation

| Criterion | Inline (recommended) | Job (round 1) |
|---|---|---|
| Values present at create | Yes | No (§8.1) |
| Second write path to `sprk_invoice` | None | A conditional update, plus a new job type and DI registration |
| Replay | Recomputed per delivery; resume never maps | Idempotent key and marker, plus a row-version retry |
| Fixed-id conditional commit | Mapped columns ride request 0. A refusal triggers the one re-commit without them (round 2 §9, Q-E1 approved). | Untouched |
| Persister tests | Unchanged; the commit moves into a local function (round 2 §9) | Unchanged; the persister is not touched |
| Intake latency | Profile read always; when rules apply, about 5–7 more reads (No Access, 2 × metadata, source, secure team), in a background job. Measured in 032b-3; most submissions stop at the profile. | None at intake; the cost moves to a job |
| Fault effect | Maps nothing; the invoice is created with a note | Same, later |

**Recommendation: inline.** U3 (c) removed the only reason for a post-commit step, which was a grant that could fault
inside the transaction. Inline keeps the values in the create, as every other inheriting type does, and adds no write path.
Its cost is the re-commit, which ebilling-r1 has already approved, and a bounded number of reads before intake. If the
owner prefers the job, C1 applies as UAC-r2 wrote it: payload core == invoice core == submission core, or
`Gated(core_changed)`.

### 8.5 Rule for all types carrying `sprk_assigned*`

> **The values are present in the create. Grants may follow.** A creator sets or maps every `sprk_assigned*` value in the
> create request itself. Access derived from those values may be granted after the commit, inline or queued, through the
> one owner. No creator fills these columns with a post-create update.

| Creator | Status |
|---|---|
| `RecordCreationService` (every route), MDA form create, Office quick-create | Complies |
| `InvoiceSubmissionPersister` | Complies with 032b inline; **would break the rule under the job alternative** |
| Repair pass (FR-25) | Named exception: it is a fill-only backstop on existing rows, not a creator |
| `/push` | Out of scope: an update of existing rows, by request |
| `ExternalDataService` creates | Inventory exception, not yet mapped. When it adopts mapping, it must comply. |

Task 060's matrix gains this rule with a guard: an ArchTest that no creator's post-create step writes a column the registry
or a profile marks `sprk_assigned*`, with the Repair pass as the listed exception.

## 9. Test plan (changes from round 2 §11)

| Id | Case | Expected |
|---|---|---|
| T-N1 | Secure core (owner answer; hard-deny) | `Gated`, no read, no note; ordinary core maps (pair) |
| T-N2 | No Access entry (record, org wall, Secure parent; truncated) | `Gated`; inactive or malformed entry maps (pair) |
| T-N3 | Field-secured source column | Not read, not mapped; the others map. A metadata fault → `Fault`, note |
| T-N4 | Rule fault | Nothing mapped, either class; note |
| T-C1 | The mapping source equals the core lookup in request 0; a different core is refused | Pair (C1) |
| T-E7 | Gate fault: the invoice is created unmapped with the note; status, code, ids, request order and count, and Decision Records are identical to an unmapped run. Control: maps | Pair (E7 condition 3, check 4) |
| T-E7b | The note holds ids and a code only, never an exception message; within 500 characters | Must-fire on a seeded message |
| T-D1 | Display copies are written from the Matter's four columns. `AssignedToColumnsOf` true for a copy → that column is gated | Pair |
| T-D2 | Registry guard (§3) | Must-fire on a registry with `sprk_invoice` |
| T-R1 | Re-run, Add line, approval, re-file, push | No mapping (ArchTest: one `ApplyAsync` call site, in `PersistCoreAsync`) |
| T-R2 | Correction | A new invoice maps from the current Matter |
| T-R3 | Request 0 refused with mapping | The re-commit cases (round 2 §9) |
| T-F1 | `findingsjson` / `procedureSnapshot` | Never a source or a target (allow-list seed must-fire) |

Kept from round 2: T-P1–T-P4, T-S1–T-S2, T-C3–T-C4, T-V1, T-T1–T-T2, T-I1, T-E4, T-A1, T-DI, T-M1, and G1–G6. Deleted:
round 1's T-G1–T-G5 and the job tests. Regression: the persister suites (104, 095c, 095d-2b-3, 165a/b) with assertions
unchanged.

## 10. §11, tasks and tensions

**§11 answers:**

| New member | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `InvoiceMatterMapping` (scoped concrete, eb `Services/Finance/Submission/`) | `RecordCreationService` mapping, Repair, `CreateTimeFieldMapping` | The service cannot take a fixed-id, multi-row create (W3: no core redesign). The step calls the owner classes. | FR-20 is unmet for extracted invoices |
| One optional persister parameter, `OwnedInvoiceColumns`, and the re-commit local function | Round 2 §3.1, §9, C5 | — | No call site; no money guard; a refused value poisons intake |
| `AdminAttentionText` mapping segment | The stopped and undone segments | Extends the class | Faults are invisible (Q-E2) |
| `MappingWriteGates.cs` (move) | Repair's private methods | A move, not a copy | The gate is re-implemented (Q-U5) |
| Registry removal | — | A deletion | The copies would confer access with no grant |

Round 1's job handler and `GrantedAssignment` are dropped.

**Placement:** BFF, in-process inside the existing ADR-004 job. No endpoint, no package, no new job type. The publish
delta is measured on a fresh master build.

**Tasks:**

| Task | Branch | Content |
|---|---|---|
| 032b-0 | UAC-r2 (or ours, with their review) | Registry removal; push class tests; T-D2 |
| 032b-1 | fm work branch | `MappingWriteGates` move; G5 |
| 032b-2 | eb task branch from d58e3a3554, after fm reaches master (E3) | `InvoiceMatterMapping`, classes, gates, unit tests; two adversarial passes |
| 032b-3 | Same branch | Persister wiring, re-commit, note segment, ArchTests, T-E7, T-DI, latency and publish delta; one pass |

**Tensions:**
- None for the project binding rule "inline, in the create request": the inline option meets it.
- The `Repair.cs` move still needs the owner's approval (O2).
- No ADR conflict (ADR-002, ADR-004, ADR-010, ADR-015).

## 11. Open questions and status

**UAC-r2**

| # | Item | Status / recommendation |
|---|---|---|
| U1, U2, U4, U5 | Approved; U1, U2 and U4 are moot under (c) | Closed |
| U3 | (c), on the §3 inventory | Confirm the inventory |
| U6 | Map-nothing-on-fault if the copies ever confer or narrow access | Conditional; not triggered under (c) |
| U7 | The Daily Briefing invoice channel loses selection by invoice Assigned columns | Accept |

**ebilling-r1:** E1 is superseded (inline recommended). E2–E6 are agreed with their conditions (§2, §4). E7 is agreed
(§4). E3: base d58e3a3554.

**Owner:** O1, placement — **recommend inline** (§8.4). O2, the `Repair.cs` move — recommend approving it.
