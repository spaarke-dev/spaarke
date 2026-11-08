# Task 032b design: Matter→Invoice mapping with the access-conferring columns (option B)

Status: **design only, for review by ebilling-r1 and UAC-r2. No code.** The owner chose option B on 2026-11-08 (round 2,
`task/fm-032-design` 60ecb8e9e6, `notes/task-032-design.md` §14 item 3). This note replaces round 2's placement (a fill-only
step inside the persister, before its transaction) with ebilling-r1's preferred one: a post-commit step. It keeps every
round-2 condition that still applies. Trees: **fm** = `work/field-mapping-server-write-path-r1` 57648a15a2; **eb** =
`work/ebilling-r1` 31afe98fa6. Re-grep line numbers before use.

## 0. Summary

1. After the persister commits, `InvoiceSubmissionAfterSteps` queues one job per invoice. The job maps the invoice's core
   record (Matter, Project or Work Assignment) onto the invoice, fill-only, as the app identity.
2. Two target classes are allowed: plain allow-listed columns, and the access-conferring Assigned Attorney and Assigned
   Paralegal columns. Every other column is never a target.
3. **The grant comes first.** The job runs the core record's Assigned-To pass through the one invariant owner,
   `AssignedAccessMaterializer`. It then reads the core's ledger, and writes an assigned column only when the ledger shows
   that the same person holds access on the core for the same role. No column is written while any grant is pending.
4. The round-2 gates still apply, re-derived from current state: Secure owner answer, No Access coverage, field security,
   Repair's secure-source logic, the allow-list, all-or-nothing on rule faults, determinism, and the money guard.
5. The mapping runs once per persister-created invoice. Set-firm re-run, Add line, approval and re-file do not map. A
   Correction or Resubmission is a new invoice and maps again.

## 1. Flow

| # | Where | What |
|---|---|---|
| 1 | Persister (unchanged) | Commits invoice, lines, approval, files and submission link in one `ExecuteTransactionAsync`. |
| 2 | `RunFromStoredAsync`, new **step 3a**, after the index enqueue (step 3, eb `InvoiceSubmissionAfterSteps.cs` :505-524) and before the approver refresh, the notification and the terminal status | If `stored.Kind != AutoRejected`, enqueue `InvoiceMatterMappingJob` with key `invoice-matter-mapping-{invoiceId:N}`. The step passes only ids: no extraction data and no owner object. |
| 3 | `InvoiceMatterMappingJobHandler` (new, ADR-004 `IJobHandler`) | Calls `InvoiceMatterMapping.RunAsync(invoiceId, submissionId, ct)` and maps the result to a `JobOutcome`. |
| 4 | `InvoiceMatterMapping` | Runs the gates (§4), the engine, the grant (§3), then one conditional update of the invoice. |

**Step 3a status values** (after-steps): `Enqueued`; `Skipped(auto_rejected)`; or an enqueue fault, which ends the run
as `Retry` with `ebilling.submission.field_mapping_enqueue_failed`, the index step's precedent. A resume calls step 3a again.
The deterministic key de-duplicates it, and the job's done marker makes a replay a no-op.

**Job status values** (`InvoiceMatterMappingStatus`):

| Status | Meaning | `JobOutcome` | Admin note |
|---|---|---|---|
| `Applied(columns)` | Columns were written | Success | none |
| `NothingToMap(code)` | No profile, no rule left after filtering, or every target already filled | Success | none |
| `AlreadyMapped` | The done marker is set | Success | none |
| `Gated(code)` | Secure, No Access, core changed, not this submission's invoice | Success | none (C2) |
| `GrantPending(code)` | A grant, ledger, flag or other transient read did not complete | Failure (retry). On the final delivery the job is Poisoned and writes the note | `grant_pending`, final delivery only |
| `Faulted(code)` | Rule fault, unfit value, metadata unknown, or an unexpected exception | Poisoned | `<code>` |
| `Conflict` | The invoice changed twice between read and write | Failure (retry) | none |

**Why a job, not an inline step in the after-steps.** ebilling-r1's answer 1 allows the grant to be queued: nothing reads
the invoice copies. An inline step that returns `Retry` on a grant fault keeps the submission Processing. On the final
delivery the submission is Poisoned with "Processing stopped", and an access read fault would hold invoice acceptance.
With the job, mapping and grant failures retry on their own, the submission completes, and the after-steps change is one
enqueue. §11 E1 offers the inline variant.

**Not in the transaction.** The fixed-id `ExecuteTransactionAsync` is not touched, so its replay contract and the persister
tests (104, 095c, 095d-2b-3, 165a/b) are unaffected. Round 2's re-commit (Q-E1) and its local-function commit are dropped:
a refused value fails the job's own update, not the invoice create.

## 2. Column classes

| Class | Columns | Decided by | Written when |
|---|---|---|---|
| **A, plain** | `sprk_description`, `sprk_highpriority` | A static `AllowedPlainTargets` set, pinned by a test (round 2 §6.1; sign-off still open, §11 E6) | Target empty at the job's read; passes the gates and the fit check |
| **B, access-conferring** | `sprk_assignedtoattorney1/2`, `sprk_assignedtoparalegal1/2` (the dev profile "Matter to Invoice (Attorney Matrix)") | **Both** a static `AllowedAssignedTargets` set **and** the one classifier, `PushRuleSelection.AssignedToColumnsOf(bound MembershipOptions.AccessConferringRoles)` for (`sprk_invoice`, column). The rule's **source** must also be a Contact-typed registry column of the core table, so that a core grant exists for it. | As class A, and also after §3 confirms the grant |
| **C, never** | Everything else. This includes `sprk_billingexport` (writer-only, protected); money columns (class (b), money guard); `InvoiceSubmissionPersister.OwnedInvoiceColumns`; the Q-E3 five; every invoice module's `ReadableColumns` (EB-D-207); `InvoiceIndexDocument.InvoiceColumns`; status and lifecycle; filing and regarding; secure and access columns; `sprk_invoiceduedate`; `sprk_billingapproverrole`; `sprk_assignedto1/2`; and contact, user or team lookups not in class B | Round 2's deny-list (§6.2), by reference, as a second check over A and B | Never |

When the registry drops a class-B column, the classifier answers false. The column then counts as a plain contact lookup,
which Q-U6 refuses. A static entry that the registry does not list is refused.

## 3. The grant

Per A6 (UAC-r2 task 142), child registry entries create no grant: only the three roots are ever materialized. So the grant
that matches an invoice copy is **the core record's own Assigned-To grant for the same person and role**. The invoice's
access derives from its core. Its owner is resolved from the core; external invoice access derives from the accessible
root (fm `ExternalCallerContext.cs` :350); a secure child mirrors its root's sharees.

| Item | Design |
|---|---|
| Writer | `AssignedAccessMaterializer.AfterWriteAsync(coreTable, coreId, writtenColumns: null, grantorOid: null, ct)`: the full pass, the same run as the reconciliation job and 049's queued create grant. It is not a one-subject pass: 049b's accepted limit is that such a pass acts only on a share. |
| Rows it writes | Only the core's: `sprk_assignedaccess` ledger rows, `sprk_externalrecordaccess` grant rows, and POA shares on the core. **None on the invoice.** No invite is sent: a background pass never invites (task 185 I-9). |
| Idempotency key | The ledger's `sprk_ledgerkey` = `AssignedAccessStore.LedgerKey(rootType, coreId, sourceField, subject)`. An unchanged core costs reads and zero writes. |
| Verify | After the pass, re-read the core's source columns and its ledger (`AssignedAccessStore.ReadLedgerAsync`). A class-B column may be written only when the core's source column still names the same subject, **and** the ledger row (core, rule source field, subject) is `Granted`, `Shared`, `CoveredByExisting` or `Adopted`. |
| Pending (retry) | The outcome is null, `Failed` or `FlagsUnreadable`; any `Failures` entry; or a `Skipped` row whose reason is transient (`flags-unreadable`, `link-unreadable`, `no-access-unverifiable`, `subject-unreadable`). The result is `GrantPending`, and **no column of either class is written**. |
| Refused by policy | `Declined`, `PendingConfirmation`, `Revoked`, or a `Skipped` row with any other reason (an unknown reason counts here). That column is dropped, with a log code and no note. The other columns proceed. |

**Why this adds no access.** The subjects are the core's current assignees, and the core's invariant owner already owes
them this access: the reconciler gives it within one cycle. The job only brings that forward.

**Invariant I-G: never a name without its grant.** For every class-B column c that the job writes with value s, at write
time the core's source(c) = s, and ledger(core, source(c), s) is in a state that confers access. Enforcement, from
strongest to weakest:

1. **Type.** The update builder accepts class-B values only as `GrantedAssignment`. This sealed record has a private
   constructor, and only `VerifyGrantsAsync` creates it, so the compiler rejects a class-B write that bypasses
   verification.
2. **ArchTest.** In `InvoiceMatterMapping`, the single invoice-update call follows `VerifyGrantsAsync`, which follows the
   materializer call (Roslyn order pin, with must-fire controls).
3. **Unit tests T-G1 to T-G5** (§9).

## 4. Gates, in order

| # | Gate | Fail-closed outcome |
|---|---|---|
| 1 | Done marker `invoice-matter-mapped:{invoiceId:N}` (`IIdempotencyService`) | Set → `AlreadyMapped` |
| 2 | The invoice exists, is this submission's (`InvoiceBlockedHold.IsOwnSubmission`), and its core lookup equals the submission's core and passes `InvoiceFamilyCoreRecordGuard.Check` | Missing or foreign → `Gated(not_own_invoice)`; core changed → `Gated(core_changed)` (the D-96 rule, §7); read fault → `GrantPending` |
| 3 | Secure-if-any (C2): ask `IRecordOwnershipResolver.ResolveOwnerAsync` the persister's question again (`RecordOwnershipContext.ForRow(sprk_invoice, core column → core)`, eb :476-480). The invoice row's `sprk_issecure` is also checked | `IsSecureOwner`, a hard-deny answer (`InPlaceOfDefaultTeamId`) or a secure flag → `Gated(secure)`; unresolved → `GrantPending` |
| 4 | No Access: `NoAccessShareEnforcer.ReadCoverageAsync(core)`, then `ReadEntryAsync` and `TryClassify` per entry (round 2 §5.2) | An active, well-formed entry or a truncated answer → `Gated(no_access)`; unreadable → `GrantPending` |
| 5 | Profile (core table → `sprk_invoice`, active rules), normalized and ordered by `(ExecutionOrder, Id)` (C4) | Fault → `GrantPending`; none → `NothingToMap` |
| 6 | Class filter (§2): class-A or class-B allow-list, then the deny-list | Skipped rules are logged; none left → `NothingToMap` |
| 7 | Metadata per delivery, no cache (`GetColumnMetadataAsync` for the core and the invoice; Q-U4) | A fault or an unknown `IsSecured` → `Faulted(metadata_unavailable)`. A secured source column is never read. A missing source column drops only its rule, with a Warning. |
| 8 | Source read (`MappingSourceReadProbe.ReadAsync(MappingSourceReader.System, core)`), then `MappingSourceCopyGate.DecideAsync` (Repair's logic, moved; round 2 §5.3) | `Secure` → `Gated(secure_source)`; `Unknown` or unreadable → `GrantPending` |
| 9 | Engine: `CreateTimeFieldMapping.ApplyFillOnly` on a candidate built from the invoice's current values, so a filled target counts as supplied (C3) | Any warning → `Faulted(rule_fault)`, all or nothing |
| 10 | Fit: `MappedTargetFit.Check` | Any failure → `Faulted(value_unfit)` |
| 11 | Grant, then verify (§3), for class-B subjects only | Pending → `GrantPending`, nothing written; refused → that column dropped |
| 12 | One update with the surviving columns, conditional on the invoice row version read in gate 2 (`BulkUpdateIfRowVersionsMatchAsync`, one row) | A conflict restarts once from gate 2; a second → `Conflict`. A Dataverse refusal → `Faulted(update_refused)`; nothing is written. |
| 13 | Set the done marker | A fault is logged; a replay then writes nothing, because the targets are filled |

The admin note (Q-E2) is a new `AdminAttentionText` segment kind, `"Field mapping not applied: <code>"`, codes only. It is
written with `NoteAsync`, which merges into the current text, and only for `Faulted` and for a final-delivery
`GrantPending`. `Merge` and `WithoutStopped` recognise and keep it, in a fixed order (Signals, policy, mapping, stopped,
undone). The Signal segment shrinks to make room, so the whole stays within `AdminAttentionMaxLength` (500). The note may
land after the terminal write: `NoteAsync` is not a status write, and the terminal write already re-reads once on a
row-version conflict.

## 5. Identity

There is no caller. The job runs in the BFF's background host with the **BFF application user**
(`IGenericEntityService` / `DataverseWebApiClient`, app-only). It uses no OBO token, no impersonation and no FR-04 probe:
this is the Repair model that UAC-r2 accepted in round 1.

| Read or write | Why it is safe |
|---|---|
| Invoice read and update | The id comes from the submission (`InvoiceIdFor`) and is checked against the submission's link. The update writes only class-A and class-B columns into targets that are empty, conditional on the row version. |
| Core read | The core is the invoice's own core lookup, which the persister wrote from the core record authorized at intake (eb `InvoiceSubmissionService.cs` :31-32). Secured source columns are never read. |
| Profile, metadata, No Access, ledger | Configuration and access-system reads, used only to decide. |
| Core grant | Written by the invariant owner, with its own policy (Secure, Restricted, Limited, No Access, decline). The grantor is null. |

#2188: one `UnmaskedCallExemptions` entry, `Uses = 1`, pinned to the `MappingSourceReader.System` call site (Q-U3).

## 6. Replay, idempotency and crashes

| Case | Result |
|---|---|
| Duplicate enqueue (resume, redelivery) | Same key; the second run is `AlreadyMapped`, or writes nothing because the targets are filled |
| Crash after the grant, before the update | Redelivery: the grant pass makes zero writes, verify passes, the update writes. I-G holds throughout, because the columns land only after verify. |
| Crash after the update, before the marker | Redelivery: every target is filled → `NothingToMap` |
| Job dead-lettered while `GrantPending` | The invoice copies stay empty (the safe direction). The core grant is still owed and is completed by the reconciler. The note says `grant_pending`. A replay of the dead-lettered message resumes. |
| A user fills a target first | Fill-only: that column is skipped. A fill between the read and the write is caught by the row-version condition. |

Determinism (C4): the static resolver-key set, `(ExecutionOrder, Id)` order, and the lookup overload of `IsFilingLookup`.
Same inputs give the same columns.

## 7. Re-file (D-96) and later changes

- **Rule: no re-map.** Gate 2 refuses when the invoice's core is no longer the submission's core. The job never runs on the
  re-file path, the Set-firm re-run, Add line or approval (pinned by an ArchTest, §9).
- After a re-file, or after the core's assignment ends, the invoice copies name the earlier assignee. ebilling decides
  nothing from them. Approval and its kin decide only from server-written sources (EB-D-200, EB-D-208), and this note
  records that **no ebilling decision may ever read the invoice-level copies**.
- **Open for UAC-r2 (U3):** whether the invoice copy must be cleared, or what it confers withdrawn, once the core grant
  that justified it ends.

## 8. §11 answers and placement

| New member | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `InvoiceMatterMapping` (scoped concrete class, eb `Services/Finance/Submission/`) | `RecordCreationService`'s mapping and Repair, and `CreateTimeFieldMapping` | Neither creator-side path runs post-commit on a job-created row without a caller, and the W3 direction bars a core redesign. The step calls their owner classes and duplicates none of them. | FR-20 is unmet for extracted invoices: the owner's option B ruling cannot be delivered |
| `InvoiceMatterMappingJobHandler`, a new job type and one DI registration beside `InvoiceIndexingJobHandler` | `AssignedAccessCreateGrantJobHandler` (roots only, no column write) and `InvoiceIndexingJobHandler` (a different owner) | Neither can write invoice columns. Folding the work into the after-steps holds acceptance on access faults (§1). | A grant fault either strands the submission in Processing or silently drops the mapping |
| Step 3a in `RunFromStoredAsync`, plus an optional `enqueueMapping` seam parameter that defaults to a no-op, built from the already-injected `JobSubmissionService` | The step-3 index enqueue | It is the same pattern, one more call. No public constructor change. | The job is never queued |
| `InvoiceSubmissionPersister.OwnedInvoiceColumns` (an `internal static` set) | None; round 2 C5 | — | The money guard (C5) cannot exclude persister columns by reference |
| `AdminAttentionText` mapping segment | The stopped and undone segments | It extends the existing class | Faults are invisible to administrators (Q-E2) |
| `GrantedAssignment` record | None | — | I-G would rest on tests alone (enforcement ladder) |
| `MappingWriteGates.cs` (round 2's move of Repair's gate and fit logic) | Repair's private methods | A move, not a copy (Q-U5) | The gate would be re-implemented |

**Placement** (`bff-extensions.md`): BFF, an in-process ADR-004 job (ADR-052: no Function). No endpoint, no package and no
AI call. The persister hunk is one static set. The publish-size delta is measured against a fresh master build and is
expected to be under 0.1 MB.

## 9. Test plan

| Id | Case | Expected | Kind |
|---|---|---|---|
| T-N1 | Secure core (owner `IsSecureOwner`; hard-deny; invoice flagged) | `Gated(secure)`; no profile read, no grant, no note | must-fire |
| T-N2 | Active, well-formed No Access entry (record, org wall, Secure parent); truncated | `Gated(no_access)`, nothing written | must-fire |
| T-N2b | Inactive or malformed entry; ordinary core | Maps | must-not-fire |
| T-N3 | Field-secured source column | Not read, not mapped; the other rules map. A metadata fault or unknown `IsSecured` → `Faulted`, note | pair |
| T-N4 | Any engine warning | Nothing mapped, either class; `rule_fault` note | must-fire |
| T-G1 | The materializer fails, or a transient `Skipped` | `GrantPending`; **no column of either class written**; retry | must-fire |
| T-G2 | Ledger `Granted`, `Shared`, `Covered` or `Adopted` | Class-B column written | must-not-fire |
| T-G3 | `Declined`, `PendingConfirmation`, `Revoked`, Restricted `Skipped` | That column dropped; class A still written | must-fire |
| T-G4 | The core source changed between the grant and the verify | Column dropped | must-fire |
| T-G5 | Order: a recording fake shows materializer < ledger read < update; crash after the grant, then redelivery | Columns written once, after verify | invariant |
| T-R1 | Set-firm re-run, Add line, approval, re-file | No enqueue and no mapping. ArchTest: `InvoiceMatterMapping` and its job are referenced only from step 3a and the handler | must-fire |
| T-R2 | A Correction's new invoice | Its own job maps from the current Matter; the old invoice is not touched | must-not-fire |
| T-R3 | AutoRejected | `Skipped(auto_rejected)` | pin |
| T-I1 | Fill-only: a pre-filled target; a row-version conflict twice | Skipped; `Conflict` | pair |
| T-I2 | Replay after success, with the marker or without it | Zero writes | idempotency |
| T-A1 | Allow-lists: exact sets, no overlap with the deny-list groups; seeds `sprk_billingexport`, a money column, `sprk_assignedto1` | Must-fire on each seed | G3 |
| T-A2 | `AdminAttentionText`: 60 Signal refusals plus a mapping note ≤ 500; `Merge` order; `WithoutStopped` keeps it | cap | Q-E2 |
| T-DI | Real container: the handler, `InvoiceMatterMapping` and every dependency resolve | non-null | DI |
| T-M1 | `MappingWriteGates` moved logic; the Repair suites pass unchanged | move proof | |

Regression, with assertions unchanged: the persister suites (104, 095c, 095d-2b-3, 165a/b); the after-steps suites;
`InvoiceMoneyColumnsAppOnlyWriteGuardTests`; `MappingSourceCallerMaskGuardTests` (apart from the entry);
`ADR010_DITests`; `FeatureRolePrivilegeGuardTests` plus G5 (Q-U1); the Repair suites; the materializer suites.

## 10. Task split and ADR tension

| Task | Branch | Content | Est. |
|---|---|---|---|
| 032b-1 | fm work branch | `MappingWriteGates` move, Repair wrappers, T-M1, the G5 role-source assertion | 5-7 h |
| 032b-2 | eb task branch, cut after eb 082d's sha, with 052a and 032b-1 present (§11 E3) | `InvoiceMatterMapping`, the class lists, gates, grant and verify, `GrantedAssignment`, unit tests T-N*, T-G*, T-I*, T-A1 | 10-12 h, two adversarial passes (access) |
| 032b-3 | the same eb branch | Job handler and DI, step 3a, `OwnedInvoiceColumns`, `AdminAttentionText` segment, ArchTests (order pin, T-R1, the #2188 entry), T-R*, T-A2, T-DI, publish delta | 6-8 h, one pass |

**ADR and ruling tensions.**

- (1) The owner ruling of 2026-11-06 says the step goes "before its transaction". This design runs it post-commit, at
  ebilling-r1's request, so the owner must re-confirm (O1).
- (2) Project binding rule (`CLAUDE.md` §2): "apply field mapping server-side, inline, in the create request". A
  post-commit update deviates from it. The W3 direction (`decisions.md` :874) allows a named-exception creator whose table
  is covered by "L3/L4 fill-only reconciliation, fail-closed". The job uses that model: Repair's fill-only semantics
  against the row's current values (spec :272), app-only, with no FR-04 caller probe. Proposed resolution is path A, a
  project exception recorded in `design.md` ADR Tensions, decided with O1.
- (3) The `Repair.cs` move is still awaiting the owner's approval from round 2 (O2).
- No ADR conflict: ADR-002 (no plugin), ADR-004 (job), ADR-010 (concrete class), ADR-015 (codes only), and one owner per
  invariant (the materializer for grants, the engine for mapping, the persister for creation).

## 11. Open questions

**UAC-r2**

| # | Question | Recommendation |
|---|---|---|
| U1 | Is the matching grant the core's materializer full pass (A6: no invoice-level grant), run from the invoice job with a null grantor? | Yes. It adds nothing that the reconciler would not give. |
| U2 | Conferring states {Granted, Shared, CoveredByExisting, Adopted}; transient `Skipped` reasons as in §3; any other state refused | Approve as listed |
| U3 | After a re-file (D-96) or an ended core assignment, the invoice copy remains. (a) Clear class-B copies through 045c's re-derivation on a core change, plus a 052 report line for ended assignments; (b) report only; (c) remove `sprk_invoice` from the access-conferring registry, so the copies confer nothing | (c) if no surface relies on conferral at invoice level; otherwise (a) |
| U4 | Class-B allow-list: the four attorney and paralegal columns; `sprk_assignedto1/2` left out | Approve the four |
| U5 | Carried from round 2: `ReadCoverageAsync` in place of `NoAccessListReader`; no note when Secure | Confirm both |

**ebilling-r1**

| # | Question | Recommendation |
|---|---|---|
| E1 | A queued job (§1), or inline in the after-steps with `Retry` (which holds the terminal status and Poisons on the final delivery)? | The job |
| E2 | Leave AutoRejected invoices unmapped? | Yes, as the index step does |
| E3 | The base needs 052a and 032b-1, which are on the fm work branch. Merge order: fm to master and then into `work/ebilling-r1`, or merge the fm tip into the task branch? | fm to master first |
| E4 | The mapping note written after the terminal status | Accept: it is not a status write |
| E5 | Register the job beside `InvoiceIndexingJobHandler` | Yes |
| E6 | Class-A list `sprk_description`, `sprk_highpriority` (round 2, unsigned) | Sign off |

**Owner:** O1, the post-commit placement and its project exception to "inline, in the create request" (§10 (2));
recommend approving it. O2, the `Repair.cs` move; recommend approving it as a move with no change in behaviour.
