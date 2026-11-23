# Task 060 — per-table ownership matrix and exception assessment (FR-26)

Rigor STANDARD (POML). Model: opus @ high. Branch `task/fm-060`, cut from `work/field-mapping-server-write-path-r1` at `a90ba97eba`.

## Result

- `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` gains **§5.2 Per-table ownership matrix**, placed after §5.1 and before §6. §5.1's intro already points to it. Row I-6 gains a one-sentence pointer at the start of its invariant cell. Nothing else in the file changed.
- The matrix has 25 rows, one for each `RecordTableRules.All` entry, in the registry's order. The self-check script compared the table names: no extra rows, none missing, the same order, no empty cell, and 8 cells in every row.
- The two open exceptions were assessed. Both are **keep** (see "Assessment").
- One out-of-scope doc defect was filed: DI-023, #2636 (row I-18 is stale).

## Adjustments applied (main session, 2026-11-08)

- Dependency 033 was dropped (decisions.md, "033 dropped"). The named-exception creators in the matrix come from the W3 entries and from task 034's guards: `InboundIngestionCreatorsGuardTests` C1–C7, `OfficeDocumentSaveGuardTests` (030b), and the census (`RecordOwnerAssignmentCensusTests`, including `UnscannedWriters` for `InvoiceSubmissionPersister` and `InvoiceLineDecisionService`).
- Contact: the row gives today's two rules and marks the pending change "052b, pending": every contact goes to the default Owner team of `ExternalAccess:CustomerBusinessUnitId`.
- Invoice: 032b's removal of `sprk_invoice` from the access-conferring membership registry is not mentioned. The matrix shows no access-conferring roles, and the owner rule is unchanged.
- `decisions.md` and `TASK-INDEX.md` were not edited. The text for the main session to copy is below.

## Row reconciliation (registry → matrix)

The registry source is `src/server/api/Sprk.Bff.Api/Services/RecordCreation/RecordTableRules.cs`, `All` (25 `Table =` entries).

| # | Registry table | Matrix row | Ownership exception in the registry |
|---|---|---|---|
| 1 | sprk_matter | yes | none |
| 2 | sprk_project | yes | none |
| 3 | sprk_workassignment | yes | none |
| 4 | sprk_todo | yes | none |
| 5 | sprk_event | yes | none |
| 6 | sprk_memo | yes | none |
| 7 | sprk_invoice | yes | none (pre-write `InvoiceFamilyCoreRecord`) |
| 8 | sprk_reportcard | yes | none |
| 9 | sprk_analysis | yes | none |
| 10 | sprk_document | yes | none |
| 11 | sprk_budget | yes | none |
| 12 | sprk_kpiassessment | yes | none |
| 13 | sprk_billingevent | yes | none (pre-write) |
| 14 | sprk_invoicesubmission | yes | none (pre-write) |
| 15 | sprk_invoiceapproval | yes | none (pre-write) |
| 16 | sprk_externalidentifier | yes | none (pre-write) |
| 17 | sprk_invoicecorrection | yes | none (pre-write) |
| 18 | sprk_communication | yes | `UnfiledCommunicationKeepsCreator` |
| 19 | sprk_servicerequest | yes | none |
| 20 | task | yes | none (mapping exclusion only) |
| 21 | contact | yes | none in the registry; the identity-contact rule is I-6's (052b pending) |
| 22 | account | yes | none (mapping exclusion only) |
| 23 | sprk_signal | yes | none (`CreatedOnlyByItsWriter`) |
| 24 | sprk_decisionrecord | yes | none in the registry; writer rule D-144 (assessed) |
| 25 | sprk_budgetrevision | yes | none (`CreatedOnlyByItsWriter`) |

Negative checks:
- No table outside the registry has a matrix row. The notification outbox, the Direct thread and the other census waivers are covered in prose under the table, not as rows.
- Every row has a non-empty rationale cell. A row without an exception says "None". The three rows that carry an exception (communication, contact, decision record) each give an access rationale.

## Role depths: source and confidence

- **Source:** `src/dataverse/solutions/SpaarkeMaster/Roles/{Spaarke Basic User,Spaarke Core User,Spaarke Office Add In User,Spaarke AI Analysis User}.xml` at `a90ba97eba`. A script read every `prv{Create,Read,Write,Delete,Append,AppendTo,Assign,Share}{table}` entry. `task` uses the `Activity` privileges.
- **Live:** task 046 found dev matching these files on 2026-11-05, except two differences on other roles. The 046c changes (G17, then the R3 removals) passed `-Verify` on dev on 2026-11-07. This task did not re-read dev.
- **Unverified:** none. Every depth in the matrix is declared in a role file, and the files matched dev at the last reads. If the owner wants a fresh live read, `scripts/Set-FeatureRolePrivileges.ps1 -Verify` checks only the declared changes, not whole roles; a whole-role read would follow task 046's method.

Observation for UAC-r2 (K4, no failure scenario confirmed; not filed): Core User holds no privilege on `sprk_signal`, `sprk_decisionrecord` or `sprk_budgetrevision`. Basic User reads them (Signal and Decision Record at User depth, Budget Revision at Deep), and Console User reads all three at Deep. A Core User without Console User therefore cannot read a budget revision that a Basic User can. That is fine if no Core-only surface shows them; this task did not check whether one does.

## Assessment (FR-26, spec Unresolved Q3)

### Notification outbox (`sprk_notificationoutbox`): keep as an exception

- **Owner today:** the user the row is for (`OutboxService.WriteAsync`, `ownerid = systemuser(userId)`).
- **Access rationale:**
  - The owner is the per-user delivery key. `GetPendingAsync` filters on `ownerid = caller`, and `NotificationsEndpoints` derives the systemuserid from the caller's token. The routes take no target-user parameter, and dismiss checks that the row is among the caller's own pending rows first.
  - No Spaarke role file holds any privilege on the table (grep of `src/dataverse`), so the owner gets no direct Dataverse access. Every read is app-only, through the BFF.
  - The row is filed under nothing (its regarding is plain text, ADR-024 minimal), so record-first has no record to follow. A team owner would remove the key and add nothing.
- **Not a registry table**, so it has no matrix row. The FR-27 guard ("every server create of a registry table …") does not cover it, and nothing needs it to.

### `DecisionRecordWriter` communication-owner case: keep as an exception

- **Owner today:** the communications gate's Decision Record about an **unfiled** communication is owned by that communication's own owning principal (D-144 as amended; `ResolveOwnerCoreAsync` / `ReadCommunicationOwnerAsync`). It is shared Read with the communication's internal sharees after the create (D-169, `GateDecisionRecordSharing`).
- **Access rationale:**
  - The record carries the communication's subject, privilege flag and fact snapshot. Its readers follow the communication's: the same owner and the same sharees.
  - The roles that read Decision Records (Basic User at User depth; Console User at Deep, which needs Core User) reach no row that the same person could not reach on the communication itself.
  - The standard owner would be a business-unit default team, which every user of that unit belongs to.
- **Fail-closed properties (code):**
  - Secure-if-any is asked first, and a Secure answer is applied as returned.
  - Only a `systemuser` owner with an explicit `isdisabled = false` is accepted. Anything else is a by-design skip with nothing written, never a fall-through to a team.
  - Filing the communication re-owns the record to the resolver's team and revokes the shares only after the read-back (census entry for `DecisionRecordWriter.cs`).
- **Not a registry `OwnershipException`:** `sprk_decisionrecord` is `CreatedOnlyByItsWriter`, so the service never creates one. The rule is the writer's, and the matrix row names it.

Neither decision is a convert, so no defer issue was opened for either.

## Text for decisions.md (main session to copy)

> ## 2026-11-08 — Task 060: ownership matrix; outbox and gate-record exceptions kept
>
> - §5.2 of `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` holds the per-table ownership matrix. It has 25 rows, one per `RecordTableRules.All` entry, and I-6 points to it.
> - **Notification outbox: keep.** Recipient-owned rows are the BFF's per-user delivery key (app-only reads filtered to the caller; no target-user parameter). No Spaarke role holds any privilege on the table, and the row is filed under nothing.
> - **`DecisionRecordWriter` gate record about an unfiled communication: keep.** It is owned by the communication's own owning user and shared Read with its internal sharees (D-144 as amended, D-169). Its readers follow the communication's. It is secure-if-any first and fails closed without a readable, active user owner.
> - Contact row: today's two rules, plus "052b, pending" (every contact to the `ExternalAccess:CustomerBusinessUnitId` team).
> - Found in passing: row I-18 is stale (the Signal owner is now the resolver's team): DI-023, #2636.

## Follow-ups

| Id | What | Where |
|---|---|---|
| DI-023 (#2636) | Row I-18 says the writer identity owns a Signal; since ontology task 143 the code writes the resolver's team. Doc only | `notes/defer-issues.md`; task 062 or ontology can take it |
| — | 052b: when the contact rule lands, the contact row's "pending" note becomes the rule | task 052b / 062 |

## Deviations

- POML step 4 (TASK-INDEX) and the `decisions.md` edit were not done in this task, per the main session's adjustments.
- POML step 0's `/conflict-check` ran by hand. None of the 33 open PRs touches the doc. `origin/work/ebilling-r1` and `origin/work/unified-access-control-r2` change §4.3 and §5.1 lines 113–233 and row I-23 (as diffs against master). This edit changes row I-6 (line 136) and inserts after line 237, so no hunk overlaps theirs. Master's copy of the doc was identical to the work branch's just before the edit.

## Quality gates

Documentation only; no code and no `tests/**` changed. STANDARD rigor skips Step 9.5. The checks are the row self-check above and a scan for conflict markers (0 found).

rework: none task 060
