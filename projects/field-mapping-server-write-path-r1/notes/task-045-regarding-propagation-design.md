# Task 045, Part A: the regarding pair, inventory and design

- **Status:** Part A (inventory and design) is written. It awaits the owner's decision and UAC-r2's review. Part B has not started. No `src/` file was changed.
- **Rigor:** FULL (POML 045).
- **Read-only:** code and solution XML only. No MCP query was run against dev, because the solution XML answered every schema question.
- **Branch:** `task/fm-045`, cut from `work/field-mapping-server-write-path-r1` at `656ab8ddb6`, which contains 042c and 044. All line numbers are at that commit.
- **Owner rule (decision 4, 2026-11-04):** "where there is a regarding set, there should not be the case where any related row that should carry the regarding value does not get it — make it not possible."
- **Direction that applies (W3, 2026-11-06):**
  - named exceptions plus guards;
  - no core redesign of `RecordCreationService` in R1, which means no given-id input, no multi-row transaction, no pinned owner and no app-only lookup translator.

## 0. Definitions

| Term | Meaning |
|---|---|
| **pair** | `sprk_regardingrecordtype` is a lookup to `sprk_recordtype_ref`; `sprk_regardingrecordid` is a string that holds the id. ADR-024. |
| **whole pair** | Both halves are set, or both are empty. |
| **type-only pair** | The type is set and the id is empty. |
| **id-only pair** | The id is set and the type is empty. This is valid when a lookup on the same row names the same id (the agreement rule, `CallerWriteAuthorization.cs:237-256`). |
| **trio** | The display columns `sprk_regardingrecordname`, `sprk_regardingrecordnumber` and `sprk_regardingrecordurl`. The trio is derived from the pair. |
| **I-1 stamps** | `sprk_regarding{matter,project,workassignment,servicerequest}`. These are derived by `CoreAncestorResolver`, bound at create step 10, and restamped by `CoreAncestorRestamper` and the `*/5` job. |
| **typed filing lookup** | A lookup that files a row under a parent: `SecureRootInheritance.TypedFilingColumns` `:409-414`, plus `IntermediateFilingColumns` `:437-442`, read through `TypedLookupsOf` `:457`. |
| **agreement columns** | The lookups whose id may stand in for the pair's type: `OwnedChildWrite.PairAgreementColumns` `:521-551`. |

**The canonical reference has moved.** The POML names "042c's core-pair fill-or-clear in RecordCreationService (as merged)". 042c fix round 4 (D-204) removed that code: Signal, Decision Record and budget revision are no longer created by the general create (§15.4 row 19). The pattern survives in two places:

- its rulings, in `notes/task-042c-notes.md` (fix round 2 rulings 1–11 and UAC-r2 conditions a–g);
- its guard, `tests/Spaarke.ArchTests/CorePairTypeImpliesIdGuardTests.cs`. Rule 1: a keyed write of the type needs a write of the id within 10 lines in the same file. Rule 2: body rewrites. The guard has must-fire and must-not-fire controls.

Part B copies the warning shape, its place before the AppendTo check, and the guard. It cannot copy the code.

## 1. Inventory

### 1(a) Carrier tables

**Tables with the full pair (18).** On each, the type is a lookup to `sprk_recordtype_ref` and the id is a string (`src/dataverse/solutions/SpaarkeMaster/Entities/*/Entity.xml`):

`sprk_analysis`, `sprk_billingevent`, `sprk_communication`, `sprk_decisionrecord`, `sprk_event`, `sprk_externalidentifier`, `sprk_invoice`, `sprk_invoiceapproval`, `sprk_invoicecorrection`, `sprk_invoicesubmission`, `sprk_kpiassessment`, `sprk_memo`, `sprk_project`, `sprk_reportcard`, `sprk_servicerequest`, `sprk_signal`, `sprk_todo`, `sprk_workassignment`.

The trio is on these tables too. Two exceptions:

- the analysis writer writes no url (`DataverseServiceClientImpl.cs:675-680`);
- communications and agreements have no `sprk_recordtype_ref` row, so their pair is id-only by design (`CoreAncestorStampReconciliationJob.cs:589-593`).

**Column security.** Only `sprk_project`'s pair is field-secured: Entity.xml `:2560` and `:2679`; `FieldSecurityProfiles.xml` `:223`, `:231`, `:883` and `:1131`. The server knows this as `BffManagedPairTables = { sprk_project }` (`SecureRootInheritance.cs:484-485`). The remarks at `:469-483` explain why a work assignment's pair is not field-secured: client wizards write it as the user (#1616).

**Columns with similar names that are not the pair.** Part B scopes these out:

| Table | Column(s) | Why it is not the pair |
|---|---|---|
| `sprk_communicationthread` | `sprk_regardingrecordtype_ref` (`:1320`) + id/name/url | The thread's own anchor, with a different type column (R3) |
| `sprk_notificationoutbox` | `sprk_regardingrecordtype` as **nvarchar** (`:807`) | A per-user delivery table that holds text, not a lookup |
| `sprk_document` | id (`:4620`) + number (`:4660`); no type | No type column, so it cannot hold a type-only pair |
| `sprk_matter` | type (`:2562`) + number (`:2525`); no id column | Not a pair |
| `sprk_organization`, `sprk_budget` | number only (`:915`, `:1324`) | Not a pair |

**Values derived from the regarding:**

| Value | Derived by | Bound or written |
|---|---|---|
| trio | the writer that sets the pair (table below) | in the same write as the pair |
| I-1 stamps | `CoreAncestorResolver.ClassifyStampSource` `:442-501`, `StampSourceColumns` `:237-286`, `IntermediateRootColumns` `:171-216` | at create step 6½ (planned, `RecordCreationService.cs:783-793`, `PlanStampBeforeOwnerAsync` `:868-894`) and step 10 (bound, `:831-837`); post-create restamp `:1876-1885`; update restamp `CoreAncestorRestamper` (`WriteCanMoveAStamp` `:183-206`, `RestampChildrenOfAsync` `:209`); job `*/5` |
| invoice-family resolver fields (the pair and trio) | `InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync` `:731`, `InvoiceFamilyResolverFields.ToFields` `:905-917` (all five, whole) | by the family writers (table below) |
| `sprk_regardingrecordtypelogicalname` | `BudgetInquiryExecutor.cs:347-349` | beside the pair |

**`ClassifyStampSource` reads only the pair's id, never its type** (`:468-496`). Its outcomes are DirectRootLink, Source, InconsistentPair and AmbiguousSource. This shapes the design in §2.1: filling a pair id from an I-1 stamp column would turn a derived copy into a DirectRootLink, and restamps would stop following the intermediate.

#### Server writers of the pair (create and update)

The shape column says what the writer can leave on the row.

| # | Writer | Path | Tables | Writes | Shape | Evidence |
|---|---|---|---|---|---|---|
| W1 | `RecordCreationService` (generic create: child-records POST, chat `create_record`, Office quick-create, `email.draft`, decision cores) | create | any table that carries the pair, except row 19's tables | what the item sends | **any**: type-only passes step 5, because `PairTargetAsync` treats "no id" as "names nothing" (`CallerWriteAuthorization.cs:231-232`) | `RecordCreationService.cs:638-685` (step 5), `:217-260` (create); `CheckCallerMayCreateAsync` `:47-91` (pair via `PairOf` `:72`) |
| W2 | `ChildRecordEndpoints.UpdateAsync` (child-records PATCH; also events filing `EventEndpoints.cs:275-286` and communications filing `CommunicationEndpoints.cs:133`) | update | any table that carries the pair | what the body sends | **any**: `OwnedChildWrite.CheckCallerMayRefileByPairAsync` `:562-647` completes the target from the row it reads, but a type-only body with no id on the row passes as "names nothing" | `ChildRecordEndpoints.cs:702-855` (PATCH `:797`, restamp `:822`, `AfterRefileAsync` `:846`) |
| W3 | `ChildRecordEndpoints.RefileAsync` / `FileProjectAsync` | update | `RefileTables` `:112-115` (todo, memo, invoice, reportcard, analysis); project | the whole pair (`FilingShapeProblem` `:174-196`; project `ProjectFilingShapeProblem` `:555-583`) | whole | `:523-548`, `:590-647` |
| W4 | chat `DataverseUpdateRecordHandler` | update | any table | the body, through `OwnedChildWrite.KeepPairInStepAsync` `:385-487` | completes a half pair for **project only** (`completes`, `:400`); keeps a **work assignment's** pair in step when its typed lookup changes (`follows`, `:401`; clears pair and trio `:472-475`); a moved pair keeps the old trio (`:467-468`) | `DataverseUpdateRecordHandler.cs:298-332`; `RefileAsync` `:483` |
| W5 | playbook Update Record node and confirmed proposals (`UpdateRecordActionCore`) | update | any table | the configured payload, pair columns included | **any**; no pair completion | `UpdateRecordActionCore.cs:114-400` (restamp `:378`) |
| W6 | playbook output mapping (`OutputOrchestratorService` → `DataverseUpdateHandler`) | update | any table | the configured payload | **any**; no pair completion | generic update path |
| W7 | field-mapping push (`FieldMappingEndpoints.ApplyCopyOverwrite`) | update | targets | typed lookups and values; **never the pair or the trio** (`CreateTimeFieldMapping.RegardingPairAndResolverColumns` `:59-67`, applied `:230`, `:288`; `RecordCreationService.Mapping.cs:620`, `:680`) | the pair is untouched, so it can fall out of step with a moved typed lookup | `FieldMappingEndpoints.cs:1797`; re-file `:1601`, restamp `:1613` |
| W8 | `EventEndpoints.BuildCreatePayload` | create | event | typed lookup; type when the ref row exists (`:954-955`); id, name and url always (`:956-960`) | whole or id-only | `:919-964` |
| W9 | `DataverseServiceClientImpl.StageAnalysisRegardingFields` | create | analysis | id; type if present; no url | whole or id-only | `:688-720` (id `:705`, type `:717`) |
| W10 | `OfficeService` quick-create (project) | create | project | whole pair | whole | `:2396-2414` |
| W11 | `OfficeService` to-do | create | todo | typed lookup + id + name; type best effort; no url or number | whole or id-only; trio incomplete | `:2918-2940` |
| W12 | `TodoRegardingBuilder.ApplyResolverPairAsync` (from `ApplySubjectLookupAndPairAsync` `:247`) | create | todo | id, name and url always; type when resolved; no number | whole or id-only; no number | `:337-354` |
| W13 | `CommunicationService`, thread inherit | create | communication | lookup + id + name + url; no type | id-only (by design) | `:1269-1289` |
| W14 | `CommunicationService`, association | update | communication | id + name + url; no type | id-only (by design) | `:2910-2917` |
| W15 | `CommunicationService.CopyRegardingFromSourceAsync` (reply or forward) | create | communication | copies the source's columns; the type is read as `string` at `:2999`, so it is never copied; no number | id-only; no number | `:2968-3023` |
| W16 | `IncomingAssociationResolver` | update | communication | id always; name, number and url; type when the catalog read succeeds | whole or id-only | `:842-918` (id `:862`, type `:907`); update `:522`, `:540` |
| W17 | `EmailDraftToolHandler` | create (through W1) | communication | id + name; no type | id-only | `:1048-1051` |
| W18 | `ExternalDataService` | create | external-access rows | id, name and url; type if present | whole or id-only | `:2626-2640` |
| W19 | `InvoiceSubmissionEndpoints` | create | invoice submission | all five resolver fields from `DeriveResolverFieldsAsync`, or none | whole | `:533-558` |
| W20 | invoice-family writers (`InvoiceSubmissionPersister` `:447`, `:509`; `InvoiceSubmissionService` `:566`, `:1132`, `:1648`; `SubmitInvoiceFromEmailOperation` `:228`) | create | invoice family | `DeriveResolverFieldsAsync` → `ToFields` | whole | as listed |
| W21 | invoice-family copies (`InvoiceLineDecisionService` `:1232-1246`; `InvoiceCorrectionService` `:244`, `:1376-1388`; `InvoiceAdjustmentService` `:164`, `:597-609`) | create | correction, adjustment, line rows | their own core lookup, plus each of the five fields **copied from the invoice if present** | as whole as the invoice's | as listed |
| W22 | `DecisionActionExecutors.FileUnderAsync` | create | decision cores | typed lookup + id + name; type if present | whole or id-only | `:1264-1283` |
| W23 | `BudgetInquiryExecutor` | create | budget inquiry rows | id + type + `…typelogicalname` | whole | `:347-349` |
| W24 | `RecordCreationService.Repair` | update | filed rows | stamps and owner (no production caller, 052, DI-008) | — | `RecordCreationService.Repair.cs:126`, `:496` |

`DataverseWebApiService.BuildCreateEventPayload` / `AddRegardingWriteSet` (`:587-697`) has no production caller; only a comment in `EventEndpoints` refers to it. The documents PUT writes no pair: `sprk_document` has no type column, so it is not an AC3 path.

#### Client writers (direct Dataverse, as the user)

| Writer | Writes | Shape | Evidence |
|---|---|---|---|
| RegardingResolver PCF | pair + trio via `ctx.webApi.updateRecord`; the form's type lookup | whole | `ResolverWriteHandler.ts:158`; `RegardingResolverApp.tsx:1106-1109` |
| To-do regarding update builder | its clear function nulls type, id, name and url | **leaves `sprk_regardingrecordnumber`** | `TodoRegardingUpdateBuilder.ts:244-286` |
| To-do pre-save form script | pair | form-dependent | `sprk_todo_regarding_presave.js:152` |
| Notepad memo repository | pair | form-dependent | memo repository |
| Connections write handler, `clearPrimaryRegarding` | through the BFF filing route; clears all five | whole | `ConnectionsWriteHandler.ts:389-408` |
| Connections write handler, `unlinkRegarding` | nulls the typed lookup only; keeps the pair on purpose (`:375-378`) | the pair is out of step with the lookups | `:375-378` |
| BFF child-write adapter | posts a project pair to the BFF (W3) | whole | `bffChildWriteAdapter.ts:366` |

### 1(b) Propagation relationships

| # | From → to | Value | Where it happens today | In the same request? |
|---|---|---|---|---|
| R1 | intermediate (work assignment, invoice, event, …) → child row | I-1 stamps | create step 6½ / 10 (`RecordCreationService.cs:783-793`, `:831-837`); update `CoreAncestorRestamper` (`:183-206`, `:209`); `*/5` job | **Create:** yes, for the created row, bound at step 10 (a caller create). The system-principal bind comes after the create (G9). **Update or re-file:** the restamp of the row and its children runs in the request on a best-effort basis; if it does not finish, the job completes it (G8) |
| R2 | creator → the created child | pair + trio | each writer in 1(a) | yes; the shape varies (G1, G4, G5) |
| R3 | thread ↔ communication | thread anchor (`_ref`), and the communication's pair | `CommunicationService.cs:1269-1289` (inherit only when the communication has no regarding of its own); `ThreadResolver.cs:221`, `:1103`, `:1564`, `:1698` | yes (G11) |
| R4 | source communication → reply or forward | pair + trio | `CopyRegardingFromSourceAsync` `:2968-3023` | yes, but the type and number are not carried (G4, G5) |
| R5 | invoice → family rows (submission, approval, correction, adjustment, line) | resolver fields (pair + trio) | W19–W21 | yes on the dedicated writers; **not re-derived** on the generic create or update (`RecordTableRules.cs:93-98` only checks; `OwnedChildWrite` `:953-1069`) (G6) |
| R6 | subject → Signal or Decision Record | pair | the writers only (`SignalWriter`, `DecisionRecordWriter`, W22); the general create refuses these tables (row 19) | yes |
| R7 | project → its matter | pair (project's own, field-secured) | W3 `FileProjectAsync`, W10; W4 completes a half pair | yes |
| R8 | regarded record renamed → every row that names it | trio (name, number, url) | **nowhere** | no (G5) |
| R9 | re-filed parent → its children | I-1 stamps, children's owner | `SecureChildReconciler.AfterRefileAsync` (`ChildRecordEndpoints.cs:846`), restamp `:822` | yes on a best-effort basis; the job and the reconciler complete it (G7, G8) |

### 1(c) Gaps

Each gap is stated as the carrier and what can leave it short. §2 says how each one is closed.

| # | Gap | Carriers | Evidence |
|---|---|---|---|
| G1 | **Create can store a type-only pair.** Step 5 treats a pair with no id as naming nothing, so the item is written as sent | W1 (every generic create route) | `CallerWriteAuthorization.cs:231-232` |
| G2 | **Update can store a type-only pair, or clear the id and keep the type** | W2 (PATCH, events and communications filing); W4 for every table except project; W5; W6 | `OwnedChildWrite.cs:400`, `:562-647`; `UpdateRecordActionCore.cs:114-400` |
| G3 | **The pair falls out of step with a typed filing lookup that an update moves or clears.** Readers then see InconsistentPair (`CoreAncestorResolver.cs:495`) | W2, W5, W6, W7, and the client `unlinkRegarding`. Only W4 keeps a **work assignment's** pair in step (`:401`) | as listed |
| G4 | **Id-only pairs.** These are valid under the agreement rule. Some come from writers whose catalog read is best effort, and some from the reply copy, which never carries the type | W8, W9, W11, W12, W15, W16, W18, W22; W13, W14 and W17 are by design (no catalog row) | `CommunicationService.cs:2999` |
| G5 | **The trio is incomplete or stale** | W11 (no url or number), W12 (no number), W13–W15 (no number), the client clear (keeps the number), W4's move (keeps the old trio, `:467-468`), R8 (renames) | as listed |
| G6 | **Rows with two sources.** (a) An invoice-family copy row holds its own core lookup plus the pair copied from the invoice (W21); when the invoice's pair changes later, nothing follows. (b) A generic create or update of an invoice-family row that changes its core lookup does not re-derive the resolver fields. (c) A work assignment holds its typed filing lookup and its pair, kept in step only by W4 | W21, W1/W2 on invoice-family tables, the work assignment | `RecordTableRules.cs:93-98`; `OwnedChildWrite.cs:401` |
| G7 | **§15.4 rows 11 and 12.** A row can hold a stale stamp (Repair) or a sent stamp (create) until the restamp or the `*/5` job runs. The `*/2` reconciler runs on an independent schedule. UAC-r2 accepted this on 2026-11-04 | stamped rows | `design.md` §15.4 |
| G8 | **The in-request restamp does not finish**, and the job completes it later | create `:1876-1885`, PATCH `:822-828`, push `:1601`, `UpdateRecordActionCore` `:378` | as listed |
| G9 | **System-principal create** binds the stamp after the create. No production system creator exists | W1 with `user is null` | DI-007, §15.5 |
| G10 | **Direct Dataverse writes** (forms, PCFs, imports, the client writers in 1(a)) can store a half pair, an out-of-step pair or a stale trio. The stamp job's orphan clauses (`CoreAncestorStampReconciliationJob.cs:597-614`, FetchXML `:658-696`, untyped clause `:690-695`) cover stamps only; nothing reports pair completeness | all 17 tables other than project | as listed |
| G11 | **Thread ↔ communication** uses its own `_ref` type column and inherits only when the communication has no regarding of its own | R3 | `CommunicationService.cs:1269-1289` |
| G12 | **Lone type or number columns** on matter, organization and budget look like the pair but are not one | 1(a) table | Entity.xml |

## 2. Design

**Principle.** On every server path, one shared step keeps the pair whole (an id-only pair is still valid under the agreement rule). That step uses the existing owners: `PairTargetAsync`, `KeepPairInStepAsync` and `RecordCreationWarning`. The pair is prepared **before** the caller's AppendTo check, so the check sees the pair as it will be written. Direct writes follow the 044 pattern: they are detected and reported, not blocked. No refusal changes (NFR-07).

### 2.1 Create (G1): fill-or-clear at step "4½"

**Where:** in `RecordCreationService`, after the step-4 merged item and before step 5 (`RecordCreationService.cs:632-638`). Steps 5, 5½, 6½ and 7 then all see the pair as written. This is 042c's place in the step order.

**What:** the step acts only when the item sets the type and not the id.

1. **Read the type's table** from `sprk_recordtype_ref`, as the caller, exactly as `PairTargetAsync` does (`:258-271`). For the system principal it uses the reader that step 6½ already uses.
   - If the read fails (403, 404 or a fault), the item is left as sent. Step 5 then refuses as it does today, keeping the uniform denial, code and words (NFR-07).
2. **Look for a filling record** among the item's agreement-column lookups that name a record of that table. "The item's" means the caller-sent and server-set lookups only.
   - **Mapped lookups are excluded.** Step 5 can drop a mapped value (`:672-680`), and the pair must never name a lookup that is not written.
   - **I-1 stamp columns are excluded** whenever the item binds an intermediate source (`StampSourceColumns`). Filling the id from a stamp would make the stamp a DirectRootLink (`ClassifyStampSource` `:468-496`) and stop restamps.
3. **Exactly one record → fill** the id, as a lower-case `D` string.
4. **None, or more than one → clear** the type, and add one `RecordCreationWarning` with a new code.
   - The proposed code is `record_creation.regarding_type_cleared` (owner question Q3), with column `sprk_regardingrecordtype`.
   - The detail names no record id and no access state, following the 042c text rule.
   - The trio, if sent, is cleared with the type.
5. **Unchanged:** a whole pair, an empty pair and an id-only pair (AC6).

**Placement.** The step is a private method in `RecordCreationService.cs`, beside step 5's helpers. It needs no new class and no DI change. The table lookup is pure, so it can reuse `PairAgreementColumns` (made `internal` if it is not already) and `CoreAncestorResolver.StampSourceColumns`.

### 2.2 Update (G2, G3): one pair preparer on every generic update path

Today `KeepPairInStepAsync` (`OwnedChildWrite.cs:385-487`) does part of this for two tables and has one caller (`DataverseUpdateRecordHandler.cs:301`). The design extends it in four ways.

1. **Coverage:** every table that carries the pair (1(a)), not only `BffManagedPairTables` and `Inherits` tables.
2. **Rules:**
   - **(i) Type-only after the write → fill-or-clear.** The candidate lookups are the body's agreement lookups plus the row's kept lookups, with the stamp exclusion from §2.1. The row is read as the caller, which it already is (`:405`).
   - **(ii) Clearing the id clears the type and the trio.**
   - **(iii) A typed filing lookup moved or cleared by the body moves or clears the pair it repeated.** This is today's `follows` (`:401`), widened from work assignments to every table that carries the pair (owner question Q4).
   - **(iv) A moved pair also rewrites the trio.** Today the old trio stays (`:467-468`). Under option Q7-a the trio is cleared on a move; under Q7-b it is re-derived.
3. **Callers:**
   - `ChildRecordEndpoints.UpdateAsync` before `:797`, which covers PATCH and the events and communications filing routes;
   - the chat tool (already a caller);
   - `UpdateRecordActionCore.UpdateAsync` before its write;
   - `DataverseUpdateHandler`;
   - push, for (iii) only, because push never writes the pair.
4. **Shape:** split it into a pure planner (row snapshot + body → extra columns + warning) and the two readers that exist today: the caller's client and, for the app-only playbook run, the app-only client that path already uses. Each caller passes its own reader, so no path gains a reader it does not already have.

An update has no warning channel today. Under owner question Q5, a clear is either logged with a stable event id and the response left unchanged, or the warning is added to the response body (an additive contract change).

### 2.3 Id-only pairs (G4)

These are left as they are. The agreement rule already makes them filable, and communications have no catalog row. One optional fix (Q6) is to make the reply copy carry the type (`CommunicationService.cs:2999` reads a lookup as `string`). It is a one-line change and goes with 045c.

### 2.4 Trio (G5)

- **Server writers:** W11, W12 and W15 add the missing url or number where the writer already has the record.
- **Moves:** §2.2 rule (iv) covers moves.
- **Renames (R8):** these are not closed in the request, because a rename touches rows the caller did not name. Under Q7 they are either reported by 052 or left as a known limit.

### 2.5 Two-source rows (G6)

- **(b)** When a generic create or update sets or changes an invoice-family row's core lookup, run `DeriveResolverFieldsAsync` at step 5½ (`RecordTableRules`), the same owner the dedicated writers use (Q8). It writes all five fields whole, or none, as `ToFields` does.
- **(a)** A copy row's pair is a snapshot of the invoice's pair at the time of the copy. Following later invoice changes would make a re-file of the invoice cascade to its family. That needs its own decision (Q8) and is not proposed for R1.
- **(c)** §2.2 rule (iii) covers the work assignment.

### 2.6 Windows and timing (G7, G8, G9)

- **G7 and G8:** no change. Rows 11 and 12 were accepted on 2026-11-04. Making a restamp failure fail the request would add an outcome, which NFR-07 rules out. The job stays the completer.
- **AC4:** holds for rows created in the request. Their pair, trio and stamps are bound in the create.
- **G9:** unchanged (DI-007; there is no system creator).
- If the owner wants the windows shorter, the POML's escalation trigger applies: a schedule change goes to UAC-r2.

### 2.7 Direct writes (G10): the 044 pattern

- **No field security change.** The client writers in 1(a) write the pair as the user (#1616 reasoning). Any change is UAC-r2's, behind 044's gate.
- **Detection and report:** add a 052 line so the repair report lists:
  - half pairs (type-only, and id-only with no agreeing lookup);
  - pairs out of step with their typed filing lookup;
  - stale trios.
  
  Repair fixes them forward-only through the same planner (§2.2).
- **Column audit** on the pair columns of the 17 tables other than project, matching 044's audit (Q9). This is a solution change, packed with 052 or 044's follow-up.
- **Client fix:** `TodoRegardingUpdateBuilder.ts:244-286` also clears `sprk_regardingrecordnumber`. This is a one-line client change, offered for 045c (Q7).
- `unlinkRegarding` keeping the pair is a documented choice (`ConnectionsWriteHandler.ts:375-378`) and is left to its owner (Q10).

### 2.8 Scope exclusions

- **G11:** thread anchors have their own column and their own rule; R3 is unchanged.
- **G12:** lone columns are not a pair.
- **Documents:** they have no type column.
- **Push:** it never writes the pair; it gets §2.2 rule (iii) only.

## 3. Guards and tests (Part B)

### Guards (ArchTests, `tests/Spaarke.ArchTests/**`)

1. **`RegardingPairTypeImpliesIdGuardTests`**, a sibling of `CorePairTypeImpliesIdGuardTests`. A server source that writes `sprk_regardingrecordtype` by key must also write `sprk_regardingrecordid` **in the same method**, not within 10 lines: real writers place them further apart (IncomingAssociationResolver `:862` and `:907`, DataverseServiceClientImpl `:705` and `:717`).
   - **Must-fire:** a seeded writer that sets the type alone.
   - **Must-not-fire:** both halves, neither half, or id-only.
   - **Baseline:** a ratchet baseline with reasons for any writer that legitimately fills the id elsewhere.
   - **Proof:** one seeding proof, recorded in the notes and reverted.
2. **`GenericUpdatePathsPreparePairGuardTests`**, structural (the model is `WriterOnlyTablesWritersGuardTests`). Every generic update entry point must call the pair planner: `ChildRecordEndpoints.UpdateAsync`, `DataverseUpdateRecordHandler`, `UpdateRecordActionCore.UpdateAsync`, `DataverseUpdateHandler` and the push apply.
   - Must-fire: a seeded path that does not call it.
   - Must-not-fire: today's paths, once wired.

### Integration tests (`tests/integration/data-mutation/**`)

| AC | Test |
|---|---|
| AC2 | Create sends type-only with one agreeing lookup → filled; AppendTo is asked on the filled id |
| AC2 | Type-only with no lookup → type cleared, one warning; the row holds neither half |
| AC2 | Type-only with two candidate lookups → cleared, with a warning |
| AC2 | Type-only where the only candidate is an I-1 stamp and an intermediate is bound → cleared, with no DirectRootLink |
| AC2 | Type-only with only a mapped lookup → cleared |
| AC2 | Catalog read denied → today's refusal, byte for byte |
| AC3 | Per update path (PATCH, events filing, communications filing, chat tool, playbook node, output mapping): type-only → fill or clear |
| AC3 | Per update path: clearing the id clears the type and the trio |
| AC3 | Per update path: a moved typed lookup moves the pair (Q4) |
| AC3 | Push: a moved typed lookup moves the pair |
| AC4 | An invoice-family generic create re-derives the resolver fields (if Q8 is approved) |
| AC4 | A re-file restamps children, as today |
| AC6 | Whole, empty and id-only pairs on create and every update path: today's outcomes |
| AC6 | Every existing refusal suite is green with unchanged assertions |

No file is added under `tests/unit/Sprk.Bff.Api.Tests/**` (ADR-038).

## 4. Proposed split

This is more than one reviewable PR (POML escalation trigger 2).

| Task | Scope | Files | Rough size |
|---|---|---|---|
| **045a** | Create fill-or-clear (§2.1), the warning code, guard 1 | `RecordCreationService.cs`, `CallerWriteAuthorization.cs` (no behaviour change; maybe `PairAgreementColumns` visibility in `OwnedChildWrite.cs`), ArchTest, integration tests | ~150–250 lines of production code, ~8 tests, 1 ArchTest |
| **045b** | Update planner (§2.2) on every generic update path, guard 2 | `OwnedChildWrite.cs`, `ChildRecordEndpoints.cs`, `DataverseUpdateRecordHandler.cs`, `UpdateRecordActionCore.cs`, `DataverseUpdateHandler.cs`, `FieldMappingEndpoints.cs`, ArchTest, integration tests | ~300–450 lines, ~15 tests, 1 ArchTest |
| **045c** | Invoice-family re-derivation (§2.5 b), trio completion on W11, W12 and W15, reply type copy, client number clear | `RecordTableRules.cs`, `OfficeService.cs`, `TodoRegardingBuilder.cs`, `CommunicationService.cs`, `TodoRegardingUpdateBuilder.ts`, tests | ~150–250 lines, ~8 tests |
| **052 line** | Report half, out-of-step and stale-trio pairs; Repair fixes forward through the 045b planner; column audit (Q9) | 052's files | in 052 |

045a and 045b both edit files that 042c and 044 edited; they run after those merges, which are already in the branch. 045c can run in parallel with 045b. Each part goes to UAC-r2 before merge.

## 5. Owner questions

| # | Question | Options | Recommendation |
|---|---|---|---|
| Q1 | Which tables does the rule cover? | (a) the 18 tables with the full pair; (b) also the lone type and number columns on matter, organization and budget | (a) |
| Q2 | Which lookup can fill a type-only pair's id? The POML says "typed filing lookup", but `TypedFilingColumns` lists only the work assignment's | (a) `PairAgreementColumns`, caller-sent or server-set, excluding I-1 stamps when an intermediate is bound; (b) `TypedFilingColumns` only (fills almost nothing; nearly every type-only pair is cleared); (c) (a) including mapped lookups | (a) |
| Q3 | Which warning, and when? | (a) a warning on clear only (`record_creation.regarding_type_cleared`), fill silent (the POML's wording); (b) fill and clear warnings, as 042c had (`…_filled`, `…_cleared`) | (a) |
| Q4 | Should every update keep the pair in step with a moved or cleared typed filing lookup? | (a) no, only fill-or-clear half pairs (the POML minimum); (b) yes, for every table that carries the pair (today only work assignments, chat only); (c) yes, but only for tables with a typed filing lookup | (b) |
| Q5 | How does an update report a cleared type? | (a) a log with a stable event id, response unchanged; (b) an additive `warnings` field in the update responses | (a) |
| Q6 | Should id-only pairs get their type? | (a) leave them (the agreement rule); (b) fill the type where a catalog row exists; (c) only fix the reply copy's type (one line) | (c) |
| Q7 | How should the trio be completed and kept fresh? | (a) clear it on a pair move; complete the missing url and number on server writers; renames are reported by 052; (b) as (a), but re-derive the trio on a move (one extra read); (c) leave it | (b) for moves, (a) for the rest |
| Q8 | Invoice-family rows: should the generic create and update re-derive the resolver fields when the core lookup changes? Should copy rows follow later changes to the invoice? | (a) re-derive on the row's own change only (045c); (b) also cascade invoice re-files to the family; (c) neither, report via 052 | (a) |
| Q9 | What should catch direct writes? | (a) a 052 report + Repair through the planner + column audit on the pair columns; (b) 052 only; (c) field security on more tables (UAC-r2's call, behind 044's gate) | (a) |
| Q10 | Should the connections control's `unlinkRegarding` keep the pair when it unlinks? | (a) leave it (a documented choice); (b) clear the pair through the BFF filing route, as `clearPrimaryRegarding` does | (a) for R1; 052 reports it |
| Q11 | Should the windows (G7, G8) close further? | (a) no; the job completes them (UAC-r2 accepted rows 11 and 12); (b) a schedule change (UAC-r2's call) | (a) |
| Q12 | Is the proposed split right? | (a) 045a / 045b / 045c + a 052 line; (b) one task | (a) |

## 6. W3 check

There is **no conflict.** Part B:

- adds no given-id input, no create mode, no multi-row transaction, no pinned owner and no app-only lookup translator;
- adds no writer;
- modifies the existing create step order (one step before step 5) and the existing update helper;
- reuses the owners `PairTargetAsync`'s catalog read, `KeepPairInStepAsync`, `DeriveResolverFieldsAsync` and `RecordCreationWarning`;
- ships the guards W3 asks for.

**One divergence from the POML text:** its canonical reference ("042c's fill-or-clear, as merged") no longer exists in code (D-204). Part B follows 042c's notes and guard instead.

## 7. Placement and justification (CLAUDE.md §10, §11)

**Placement:** existing BFF write-path files only. There is no new endpoint, DI registration, package or background work. The publish-size delta and the CVE check are Part B's, measured against a fresh master build.

**New surface:**

- **Create fill-or-clear step** (a private method):
  - Existing: `KeepPairInStepAsync` does the same on update for one table, and 042c did it for the core pair before D-204.
  - Extension: it cannot live in `PairTargetAsync`, which answers a question and writes nothing. It sits in the step order where 042c's did.
  - Cost of doing nothing: a generic create stores a type-only pair, and its readers file it under nothing (G1).
- **Warning code `record_creation.regarding_type_cleared`** (a string constant, not an enum member):
  - Existing: `RecordCreationWarning` codes.
  - Extension: one more constant on the same record.
  - Cost of doing nothing: the caller would not learn why the type it sent was not stored.
- **Pure update planner split out of `KeepPairInStepAsync`:**
  - Existing: `KeepPairInStepAsync`.
  - Extension: yes, it is a split of that method, not a new class.
  - Cost of doing nothing: four update paths (W2, W5, W6, W7) keep storing type-only or out-of-step pairs (G2, G3).
- **Two ArchTests:**
  - Existing: `CorePairTypeImpliesIdGuardTests`, `WriterOnlyTablesWritersGuardTests`.
  - Extension: siblings with the same structure.
  - Cost of doing nothing: a new writer can reintroduce G1 or G2 unnoticed.

## 8. Review routing

This note goes to the owner (decision) and to UAC-r2 (review). The pair decides filing and ownership. The owner's decision and its date are recorded in `notes/decisions.md` before Part B starts.
