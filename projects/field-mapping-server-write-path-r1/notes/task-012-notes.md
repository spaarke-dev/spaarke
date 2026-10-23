# Task 012 notes: per-table rules registry (FR-11)

## Rigor declaration

- RIGOR LEVEL: FULL (POML `<rigor>`; tags bff-api, dataverse, refactor, testing; modifies .cs in Sprk.Bff.Api; adds an
  ArchTest and unit tests).
- Model: opus, effort high. Step mode: directional.
- Branch `task/fm-012`, cut from `83bf5cfa8` (project branch head: tasks 001, 002, 003, 011). The "before" for every
  view is the source at `83bf5cfa8`.
- Gate: task 010 sign-off and the 2026-10-20 owner decisions (`decisions.md`: W2 proceeds on master; the second lander
  reconciles the invoice-family guard).
- Context loaded: POML, spec FR-06/FR-09/FR-10/FR-11, design.md §4, w2-coordination.md, decisions.md, task-011-notes.md,
  project CLAUDE.md, BFF CLAUDE.md, ADR-002, ADR-010, testing.md (KEEP paths), the source of every list and consumer,
  and ebilling's `OwnedChildWrite.cs` / `InvoiceFamilyCoreRecordGuard.cs` / `ChildRecordEndpoints.cs` at
  `origin/work/ebilling-r1` (read only, via `git show`).

## Inventory (POML step 1): the five lists at 83bf5cfa8, per table

Lists: **SO** = `OwnedChildWrite.ServerOwnedColumns` (caller payload refused, every table on the app-only path);
**RSO** = `ChildRecordEndpoints.RootServerOwnedColumns` (caller payload refused, a root on `POST /api/v1/child-records`
only); **MP** = `MatterProtectedAttributes`; **PP** = `ProjectProtectedAttributes` (both: a field-mapping rule may not
write the column; Office quick-create); **FP** = `OwnedChildWrite.ForPersonColumns`.

SO (all tables): `ownerid, owninguser, owningteam, owningbusinessunit, createdby, createdon, createdonbehalfby,
modifiedby, modifiedon, modifiedonbehalfby, overriddencreatedon, importsequencenumber`. The creator person
(`sprk_createdbyperson`) is refused beside it by `RecordCreatorPerson.NamesColumn` (not a list; unchanged).

| Table | On `CreateTables` | RSO | MP / PP | FP |
|---|---|---|---|---|
| sprk_matter | no (Office quick-create) | — | MP: sprk_matternumber, ownerid, sprk_containerid, sprk_createdbyperson | sprk_assignedtointernal → contact |
| sprk_project | no (Office quick-create) | — | PP: sprk_projectnumber, ownerid, sprk_containerid, sprk_createdbyperson, sprk_issecure | sprk_assignedtointernal → contact |
| sprk_workassignment | yes (root) | sprk_containerid, sprk_securitybu, sprk_accesspermission | — | sprk_assignedtointernal → contact |
| sprk_todo | yes | — | — | sprk_assignedto → contact |
| sprk_event | yes | — | — | sprk_assignedto → contact |
| sprk_memo, sprk_invoice, sprk_reportcard, sprk_analysis, sprk_document, sprk_budget, sprk_kpiassessment, sprk_billingevent | yes | — | — | — |
| sprk_communication | no | — | — | sprk_sentby → systemuser |

No table appears in two lists with conflicting content: MP/PP are mapping-side, SO/RSO caller-side, and RSO is a
strict addition for one table.

## Registry shape (POML step 2)

`src/server/api/Sprk.Bff.Api/Services/RecordCreation/RecordTableRules.cs`, `internal static`, no DI, no interface
(ADR-010). `RecordTableRules.CommonServerOwnedColumns` (the SO set) + `All` (one `RecordTableRule` per table) +
`Find(table)` (null = not found; trims, ignores case) + `Get(table)` (throws `KeyNotFoundException` naming the table;
used by the views).

A `RecordTableRule` declares:

| FR-11 field | Property |
|---|---|
| root/child | `Kind` (`RecordTableKind.Root` / `Child`) |
| protected columns | `TableServerOwnedColumns` (caller side, beyond the common set) and `MappingProtectedColumns` (mapping side) — two policies today, kept apart |
| autonumber column | `AutonumberColumn` |
| "for"-person column | `ForPerson` (`ForPersonLookup(Column, RelatedTable)`) |
| server defaults | `ServerDefaults` (`ServerDefault(Column, Path, Rule)`, fill-only, recorded per create path because the paths differ today) |
| search-indexed Y/N | `SearchIndexed` |
| mapping-applies Y/N | `MappingApplies` = `MappingExclusion is null`; a "no mapping" table names a `NamedRule` with its rationale |
| ownership exception | `OwnershipException` (`NamedRule(Name, Rationale)`) |
| (coordination) pre-write rule | `PreWriteRules` (`PreWriteRule(NamedRule Rule, PreWriteCheck Check)`) |

`NamedRule`'s constructor refuses a blank name or rationale, so no exception can exist without a rationale (also unit
tested over the whole registry).

**Entries (17):** the 11 `CreateTables`, `sprk_matter`, `sprk_project`, `sprk_communication` (named by FP), and the
ontology writers' `sprk_signal`, `sprk_decisionrecord`, `sprk_budgetrevision` (named so "no mapping" is expressible
and expressed — ontology-r1 §0.3).

### Values not in any old list (OWNER REVIEW)

| Value | Evidence | Note |
|---|---|---|
| Roots = matter, project, work assignment | `RecordCreatorPerson.StampedTables` ("the three secure roots"); `SecureRootInheritance` | unit tested equal to `StampedTables` |
| `AutonumberColumn` matter/project | `scripts/Set-RecordNumberingSchema.ps1` lines 113-116 | |
| `SearchIndexed` = matter, project | Office `ApplyBusinessUnitDefaultsAsync` | |
| `SearchIndexed` = document | spec FR-10 acceptance names Document; **no create path fills it today** | OWNER REVIEW |
| `MappingApplies` default true for memo, budget, kpiassessment, billingevent, communication | not FR-16 wizard tables; a missing profile maps nothing | OWNER REVIEW |
| `ServerDefaults`, per path: chat create fills every "for" column with the caller (`DataverseCreateRecordHandler.WithForPersonAsync`; all six "for" tables); Office quick-create matter/project `sprk_assignedtointernal` (request → mapped → maker's contact); server writers todo/event `sprk_assignedto` (`AssignedToDefaults`) | cited per entry | OWNER REVIEW: 013 picks the one default the service applies (adversarial review K2) |
| `OwnershipException` communication `UnfiledCommunicationKeepsCreator` | `OwnedChildWrite.PathFor` / `KeepsItsCreatorWhenUnfiled`, E1 | `sprk_communicationthread` (E2) and `PerUserTables` are not create tables of this inventory; 013/060 add them |
| Children have no `MappingProtectedColumns` | today no server path maps them | 014 must decide (e.g. a work assignment's container / security BU / secure flag) before mapping roots other than matter/project |

## Views (POML step 3) — same member names and types

| Old list | Now |
|---|---|
| `OwnedChildWrite.ServerOwnedColumns` | `= RecordTableRules.CommonServerOwnedColumns` |
| `OwnedChildWrite.ForPersonColumns` | built from the registry's `ForPerson` entries into the same `IReadOnlyDictionary<string, ForPersonColumn>` (OrdinalIgnoreCase) |
| `ChildRecordEndpoints.RootServerOwnedColumns` | `= RecordTableRules.Get("sprk_workassignment").TableServerOwnedColumns` (the only root the route creates; the route still applies it to every root, unchanged) |
| Office `MatterProtectedAttributes` / `ProjectProtectedAttributes` | `= RecordTableRules.Get(MatterEntity / ProjectEntity).MappingProtectedColumns`; `private` → `internal` so the unit test asserts them (ADR-038 A2) |

No column literal remains in any of the five. `CallerWriteAuthorization.cs` and the members task 011 moved are
untouched (UAC-r2 is reviewing that range). `ServerOwnedColumns` and `ForPersonColumns` keep their declarations in
`OwnedChildWrite` (only the initialisers changed), so the `using static` binding rule from task 011 is unaffected.
Static initialisation: the registry reads only constants (`AssignedToDefaults.AssignedToAttribute`,
`RecordCreatorPerson.Column`), never a static field of a class that reads it, so there is no init cycle.

## Deviations

1. **`ChildRecordEndpoints.CreateTables` stays a literal** (POML relevant-files said it would become a view).
   `SecureChildNewCommandAgreementTests` (ArchTests) regex-scans that literal in source, and the step-5 guard checks
   "every table in `CreateTables` has an entry" — a view over the registry would make the guard pass by construction
   and break the unchanged ArchTest. `CreateTables` is the route's accept-list, not per-table rule data; FR-11's
   five lists do not include it.
2. **Unit test path**: `tests/unit/domain/Dataverse/RecordCreation/RecordTableRulesTests.cs` (KEEP path, pure data,
   compiled into `Sprk.Bff.Api.Tests` by the `..\domain\**` glob) instead of the POML's
   `tests/unit/Sprk.Bff.Api.Tests/Services/RecordCreation/` (main-session correction; testing.md "eight KEEP paths").
3. **TASK-INDEX.md, current-task.md, POML `<status>` not updated** (POML step 7) — reserved for the main session.
4. **The old lists are not deleted** — by design of the POML (013/014/018 delete them).

## Pre-existing path difference (escalation trigger assessed)

POML `<escalation>`: "two cores disagree about the same table's protected columns in a way that changes security".
Assessment: the five **lists** do not disagree (no list names a column another allows; RSO is a strict addition for
the work assignment). The disagreement is in **which path enforces** RSO: `POST /api/v1/child-records/sprk_workassignment`
refuses `sprk_containerid` / `sprk_securitybu` / `sprk_accesspermission`; the chat `dataverse.create_record` tool
(G5 → `OwnedChildWrite.CreateAsync`) does not. This is **already filed as #1608** ("uac-r2 G5 core: chat create can
carry a root's sprk_containerid", OPEN, ontology-deferred). The registry records the route's set as the work
assignment's own `TableServerOwnedColumns`, separate from the common set, with a comment citing #1608 — it chooses
neither the stricter nor the looser behaviour. Task 013 enforces the registry on every path and is where the owner
decides (enforcing it in the service closes #1608). Not treated as a stop for 012 because 012 declares and does not
enforce (ADR-002 constraint), and nothing was merged or chosen.

## Invoice-family reconciliation recipe (ebilling-r1 #1911 vs W2)

ebilling's guard (on `origin/work/ebilling-r1` only) is called inside `OwnedChildWrite.CreateAsync` right after
`CheckCallerMayCreateAsync` succeeds (AppendTo on every bound record), before the secure-root plan:
`CheckInvoiceFamilyAsync(user, table, item, stored: null, ct)` → `InvoiceFamilyStep(Refusal, ClientFailure,
ParentUnavailable)`; and inside `RefileAsync` with `stored: (entitySet, id)`. Its tables:
`InvoiceFamilyCoreRecordGuard.CoreRecordLookups` keys — `sprk_invoice`, `sprk_billingevent`, `sprk_invoicesubmission`,
`sprk_invoiceapproval`, `sprk_externalidentifier`, `sprk_invoicecorrection`.

The registry's `PreWriteRule` is that shape: table-keyed, runs on the row as it will be written, after the caller
checks and before plan/owner/create, refuses with a stable code. `PreWriteRefusal` carries exactly the three
`InvoiceFamilyStep` outcomes, through its only constructors: `Refused(code, detail)`, `Failed(clientFailure)`,
`NotFound()`.

**Whoever lands second — all five steps in ONE commit, never step 2 without a runner:**

1. **Move the check out of `OwnedChildWrite` (pure move).** `CheckInvoiceFamilyAsync` and its helpers
   (`InvoiceFamilyStep`, `ReadLookupsAsCallerAsync`, `EffectiveLookups`, `LineTable`, `InvoiceEntitySet`) move beside
   `InvoiceFamilyCoreRecordGuard` in `Services/Dataverse/` (e.g. `InvoiceFamilyWriteCheck`), so the registry does not
   depend on `OwnedChildWrite` (which 013 dissolves). `RefileAsync` calls it from there.
2. **Create half → ONE registry rule on ALL SIX invoice-family tables**, unconditionally. Register it on every
   `InvoiceFamilyCoreRecordGuard.CoreRecordLookups` key — `sprk_invoice`, `sprk_billingevent` (entries exist) and
   `sprk_invoicesubmission`, `sprk_invoiceapproval`, `sprk_externalidentifier`, `sprk_invoicecorrection` (add entries).
   The chat `dataverse.create_record` tool accepts any table but `sprk_document` and goes through the create core
   whenever `PathFor` says Owned, so ebilling's inline call guards all six today; registering fewer would drop the
   guard silently for the rest.
   ```csharp
   // declare before All (static initialisers run in textual order) or as a property
   private static readonly PreWriteRule InvoiceFamilyCoreRecord = new(
       new NamedRule("InvoiceFamilyCoreRecord",
           "ebilling-r1 FR-05 (ISS-010, #1973): an invoice-family row names exactly one core record, and an invoice " +
           "supersedes only an invoice of that record; asked after AppendTo on every record the row binds."),
       async (row, ct) => await InvoiceFamilyWriteCheck.CheckAsync(row.Caller, row.Table, row.Item, stored: null, ct) switch
       {
           null => null,
           { ParentUnavailable: true } => PreWriteRefusal.NotFound(),
           { ClientFailure: { } failure } => PreWriteRefusal.Failed(failure),
           { Refusal: { } refused } => PreWriteRefusal.Refused(refused.RefusalCode!, refused.Reason!),
           _ => PreWriteRefusal.Refused(InvoiceFamilyCoreRecordGuard.ReadFailedCode, "The invoice-family check gave no answer."),
       });
   ```
   Add a test: every `CoreRecordLookups` key has a registry entry whose `PreWriteRules` contains this rule.
3. **Runner, mapped back to today's `Outcome` exactly.** At the inline call's position in the create core (or the
   service's step 5½ once 013 lands), run `RecordTableRules.Find(table)?.PreWriteRules` in order; the first refusal
   becomes:
   - `Refused(code, detail)` → `Outcome { InvoiceFamilyRefusal = InvoiceFamilyCoreRecord.Refused(table, code, detail) }`
     (the route and chat map it with `InvoiceFamilyRefused` by code: `RecordNotFoundCode` → uniform 404,
     `ReadFailedCode`/`UnknownEntityCode` → 500, else 400) — or, once FR-12 lands, the shared map with that table;
   - `Failed(failure)` → `Outcome { ClientFailure = failure }`;
   - `NotFound()` → `Outcome { Denied = NotAvailableToFileUnder, ParentUnavailable = true }` (the chat handler tests
     `Denied` before `InvoiceFamilyRefusal`; without `Denied` it prints "its owner could not be decided").
   Then delete the inline create-half call. Prove it with ebilling's guard tests (a create naming two core records is
   still refused, with the same code) and the push update-only wire tests, all unchanged.
4. **Refile half stays** in `OwnedChildWrite.RefileAsync` (`stored: (entitySet, id)`), unchanged — UAC owns re-file.
5. **One copy:** `git grep` must find exactly two calls of the check (the registry rule and `RefileAsync`). Ping
   ebilling to review the rule (shared rule 4).

If #1911 lands before 013, steps 1-5 can be done in OwnedChildWrite.CreateAsync with the runner there; if that is not
wanted before 013, keep ebilling's inline call and do steps 1-5 inside 013. Never remove the inline call first.

Open point for 013: `PreWriteRow.Caller` is the caller's client. A **system-principal** create (FR-07) of an
invoice-family table has no caller; 013 decides whether the rule reads app-only or the table refuses system creates.
Open point for 013: `PreWriteRow.Caller` is the caller's client. A **system-principal** create (FR-07) of an
invoice-family table has no caller; 013 decides whether the rule reads app-only or the table refuses system creates.

## #2115 item 5 (missing MayRefile gate)

The FR-11 fields carry no "may re-file" fact, and `ChildRecordEndpoints.RefileTables` was not in scope, so nothing was
added. How 013/016 could absorb it: add `bool MayRefile` (or a `RefileRoute`) to `RecordTableRule`, make `RefileTables`
a view over it, and have `OwnedChildWrite.RefileAsync` (or its caller) refuse a table without it — a behaviour change
UAC must review (shared rule 2). `OwnedChildWrite` behaviour is unchanged by 012.

## Placement Justification (CLAUDE.md §10, bff-extensions)

In the BFF, `Services/RecordCreation/`: the registry is the per-table policy of the BFF's own create paths (child-records
route, chat create, Office quick-create) and the future home of the unified `RecordCreationService` (spec Placement
table). Pure static data: no endpoint, DI registration, package, background work, Dataverse column or AI dependency
(ADR-013: no `Services/Ai` reference).

## Component justification (CLAUDE.md §11) — each new type

| Type | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `RecordTableRules` / `RecordTableRule` | five lists in three files (git grep); `git grep RecordTableRules` found nothing | the lists live in three cores two of which are dissolved by 013/018; the lists become views over the registry | per-table policy keeps drifting between paths and 013 has no single source (spec §11 row "Per-table rules registry": Yes) |
| `RecordTableKind` | `ChildAncestorLinks.RecordKind` (Root/Intermediate/Party) describes an ancestor link, not a create table | different question (a lookup target's role vs a table's create kind) | FR-11's root/child field has no type |
| `ForPersonLookup` | `OwnedChildWrite.ForPersonColumn` | that type is nested in a class whose create half 013 removes; the view converts to it so consumers are unchanged | the registry would depend on `OwnedChildWrite`, which is being dissolved |
| `ServerDefault`, `NamedRule` | none (`git grep`) | — | FR-11 server defaults / ownership exceptions with rationale (FR-21 "each exception is a named registry rule with its rationale") have no representation |
| `PreWriteRule`, `PreWriteCheck`, `PreWriteRow`, `PreWriteRefusal` | ebilling's `InvoiceFamilyStep` (not on master) | the registry cannot reference a type not on master; `PreWriteRefusal` carries the same three outcomes | the invoice-family create half (decision 2026-10-20: "goes to the registry") would need a registry contract change when reconciled |

## Tests

| Suite | Result |
|---|---|
| New unit `~RecordTableRulesTests` (25 tests) | 25 / 25 pass |
| New ArchTests `~RecordTableRulesRegistryGuardTests` (3 tests) | 3 / 3 pass |
| ArchTests full | 1155 / 1155 pass (1152 before + 3 new; `SecureChildNewCommandAgreementTests` unchanged and passing) |
| Unit `~SecureChildOwnershipAiToolTests` | 310 / 310 (same as task 011) |
| Unit `~OwnedChildWrite\|~ChildRecord\|~CreateRecord` | 410 / 410 (same as task 011) |
| Unit `~RecordCreation\|~QuickCreate` (Office quick-create contract + new) | 202 pass, 3 skipped (pre-existing skips), 0 failed |
| Unit `~SignalRead\|~SignalSuppression` (ForPersonColumns readers) | 124 / 124 |
| Unit full | see "Full suite" below |

**Fail-before / pass-after.**
- Before (83bf5cfa8): the new tests do not compile (`RecordTableRules` does not exist; the two Office sets are private).
- Mutation proof (registry edited, tests re-run, file restored byte-identical — hash checked):
  - removing the `sprk_billingevent` entry → `EveryCreateTable_HasARegistryEntry` FAILED naming `sprk_billingevent`
    (must-fire on the real create set);
  - removing `sprk_issecure` from the project's `MappingProtectedColumns` →
    `ProjectProtectedAttributes_View_EqualsThePreRegistryList` FAILED;
  - restored → all pass.
- Guard controls: `Guard_MustFire_ACreateTableWithNoEntry_IsReported` (fixture table on the real registry + a fixture
  lookup) and `Guard_MustNotFire_EveryEntryPresent_ReportsNothing`; plus a floor assertion (≥ 13 create tables) so a
  shrinking create set cannot silently weaken the guard.

**Full suite** (`Sprk.Bff.Api.Tests`, 25534 tests, at 4c210fd3e4 before the review fixes): 25501 passed, 22 skipped
(same 22 as task 011), 11 failed — all load failures on a machine running several other agents' suites
(`TaskCanceledException`, and `ObjectDisposedException` in a shared `WebApplicationFactory` host) in
RelatedRecordCard, ExternalWorkforceLink, AuthorizationModuleWorkforceHome, SpeVersionHistoryOboSeam,
ExternalProjectEvent and ScopePersonas. Re-run of those six classes plus the registry tests after the fixes, without
other changes: **144 / 144 passed**. None is in a write-path, ownership or Office suite. So the suite passes.

After the review fixes (final tree): `~RecordTableRulesTests` 25 / 25; SecureChildOwnership 310 / 310;
OwnedChildWrite/ChildRecord/CreateRecord 410 / 410; RecordCreation/QuickCreate 204 pass + 3 pre-existing skips;
SignalRead/SignalSuppression 124 / 124; ArchTests 1155 / 1155.

## Build, publish size, CVE

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. `dotnet build Spaarke.sln`: 0 warnings, 0 errors.
- Publish size (bff-hygiene item 4): origin/master moved to `28274e04e0`, so master was built fresh.
  Method as task 001/011: `dotnet publish -c Release` of `Sprk.Bff.Api` from fresh detached worktrees, zipped with
  `Compress-Archive -CompressionLevel Optimal`.
  - Master `28274e04e0` (`C:\tmp\f12m`): **39,290,595 B, 192 files**.
  - Branch `4c210fd3e4` (`C:\tmp\f12a`; the review fixes after it are a few lines of data and comments):
    **39,295,498 B, 192 files**.
  - **Delta +4,903 B, file count unchanged.** It also covers the project branch's tasks 001-003 and 011 and the
    master commits the branch lacks (branch base `2b07480ab`). Far below the +5 MB / 55 MB / 60 MB thresholds.
- CVE: `dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive`: "no vulnerable packages given the current sources". No package change.

## Quality gates

- **/conflict-check:** 26 open PRs; none touches the six files (`gh pr view --json files`). #1911 (draft, 1839 files)
  checked by `git diff` instead: it changes `OwnedChildWrite.cs` (+254, create/refile guard) and
  `ChildRecordEndpoints.cs` (+39) but none of the lines 012 edits (`ServerOwnedColumns`, `ForPersonColumns`,
  `RootServerOwnedColumns`; `CreateTables` untouched); no Office `RecordCreationService` change. Master has no change
  to these files since the merge-base `2b07480ab`. Task 004/005 files (Spaarke.Dataverse, seeder) not touched.
- **code-review (scoped):** self-review (metrics: `RecordTableRules.cs` new, ~350 lines, cohesive — one responsibility, the per-table
  rule data and its lookups; the three edited files shrink by a net 1 line, initialisers only). No interface, no DI,
  no try/catch, no null checks on non-nullable input (the `ThrowIfNull*` guards are on factory/constructor input that
  enforces the "rationale never empty" / "refusal has a reason" invariants). Then one **adversarial review**
  (adversarial-reviewer, opus) of 4c210fd3e4. Verdict: the commit preserves behaviour (content, comparer, types, init
  order, `using static` binding all verified); findings and triage:
  - **F2** recipe registered the invoice-family rule on two of six tables → fixed: all six, plus a test to add.
  - **F1** recipe did not map `PreWriteRefusal` back to `Outcome` (`ParentUnavailable` needs
    `Denied = NotAvailableToFileUnder`), and allowed removing the inline call before a runner exists → fixed: explicit
    mapping, one-commit rule, check moved out of `OwnedChildWrite`.
  - **K1** `PreWriteRefusal` "exactly one of" was prose → fixed by type: private constructor + `Refused` / `Failed` /
    `NotFound`; unit test for a blank code/detail.
  - **K2** `ServerDefaults` did not match the chat path → fixed: `ServerDefault(Column, Path, Rule)` records each
    path's default (chat: the caller for every "for" column), OWNER REVIEW on the type.
  - **K2** guard summary overstated its scope → fixed wording (chat tool has no accept-list; 013 adds the service's).
  - **K3** ownership exception vs `KeepsItsCreatorWhenUnfiled` could drift → fixed: test ties them on registry tables.
  - **F3** one assertion could never fail → removed.
  Re-check of the fix diff: build 0/0, the suites above green. No second full pass: the fixes are data, a factory and
  notes, not a change of approach.
- **adr-check:** ADR-002 (no plugin; the registry declares, the service will enforce; each exception is a named rule
  with a rationale), ADR-010 (static, no DI, no interface), ADR-013 (no `Services/Ai` dependency; D-66 still passes),
  ADR-038 (KEEP paths; must-fire and must-not-fire controls; no reflection — `internal` per A2; no
  `Mock<HttpMessageHandler>`; no DI-registration tests), bff-extensions (Placement Justification above; publish delta
  and CVE recorded; no endpoint). No violations.

## Files

- `src/server/api/Sprk.Bff.Api/Services/RecordCreation/RecordTableRules.cs` (new)
- `src/server/api/Sprk.Bff.Api/Services/Dataverse/OwnedChildWrite.cs` (two initialisers → views)
- `src/server/api/Sprk.Bff.Api/Api/ChildRecordEndpoints.cs` (`RootServerOwnedColumns` → view)
- `src/server/api/Sprk.Bff.Api/Services/Office/RecordCreationService.cs` (two sets → views, private → internal)
- `tests/unit/domain/Dataverse/RecordCreation/RecordTableRulesTests.cs` (new)
- `tests/Spaarke.ArchTests/RecordTableRulesRegistryGuardTests.cs` (new)
- `projects/field-mapping-server-write-path-r1/notes/task-012-notes.md` (this file)

rework: fix-round-1 task 012

## ebilling-r1 review of the reconciliation recipe (2026-10-20): substance OK, three corrections (binding)

Confirmed against InvoiceFamilyCoreRecordGuard on work/ebilling-r1:
- The six tables match `CoreRecordLookups` (InvoiceFamilyCoreRecordGuard.cs:131-142).
- Ordering is preserved: WhoAmI → CheckCallerMayCreateAsync → CheckInvoiceFamilyAsync → planSecureCreate →
  ResolveOwnerAsync → create.
- The refile half (~:1546) stays in RefileAsync.
- Every outcome arm keeps its status, code and body, including the chat-tool arms in DataverseCreateRecordHandler
  (~560-575).

Corrections the reconciliation (and task 013's runner) must apply:
- **(a)** If `EffectiveLookups` moves, update
  `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.InvoiceFamilyGuard.cs:650` in the
  same commit, or leave a forwarder. Also fix the doc cref to `OwnedChildWrite.CheckInvoiceFamilyAsync` at
  ChildRecordEndpoints.cs:411.
- **(b)** `NotAvailableToFileUnder` (private const, OwnedChildWrite.cs:1675, used at :339 and :1552) is shown by the
  chat tool. The runner's NotFound arm must use that exact text: share the const, or copy it verbatim.
- **(c)** Rebuilding the refusal as `InvoiceFamilyCoreRecord.Refused(table, code, detail)` loses the decided object's
  own EntityLogicalName (the OTHER invoice's table for a supersedes refusal, :1780/1795/1801), plus Regarding,
  RegardingColumn and ResolverFields. No create consumer reads them today. Either carry the original refusal object
  through, or add a test asserting EntityLogicalName and Reason for a supersedes refusal so the difference is
  deliberate.
- `PreWriteRefusal.Refused` throws on a null Reason; every current path sets one, so keep it that way.
