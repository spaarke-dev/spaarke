# Task 141: shared regarding builder for Signal and Decision Record (2026-10-13)

Branch `task/ontology-141-regarding-builder` (worktree `C:\wts-141`), based on `origin/docs/ontology-platform-design` at
`376eca286` (#1665 and #1746 included). Draft PR into `docs/ontology-platform-design`.

## Commits

1. **Builder generalisation (uac-r2 reviews this commit by itself).** `TodoRegardingBuilder` is parameterised by host table:
   `RegardingLookupsByHostTable` maps `sprk_todo` to its existing map, and `sprk_signal` / `sprk_decisionrecord` to the 9 lookups the
   two tables share (task 140 schema). Validation, mutual exclusion and the pair write run over the host table's own map. The
   untyped pair (no catalog row for the subject table, e.g. `sprk_communication`) is tolerated on every host (D-133). New
   `internal ApplySubjectLookupAndPairAsync`: steps 1-2 only (subject lookup + pair), no core-ancestor stamp. The existing
   `TodoRegardingBuilder` and `TaskActionCore` tests pass with no edits; one new test per table
   (`tests/integration/data-mutation/RegardingBuilder/RegardingBuilderHostTableTests.cs`, typed + untyped case each).
   Messages for `sprk_todo` are unchanged text.
2. **SignalWriter on the builder, readers, guard, docs.**

## Design decisions

- **No stamp from the builder on a Signal.** `SignalWriter` calls `ApplySubjectLookupAndPairAsync`, not `ApplyResolverFieldsAsync`.
  Contract C2: the Signal's create-time stamp belongs to the restamper registration (task 144) and its ownership to 143. Neither
  is changed here.
- **A builder per write (kept; its original reason is gone).** `SignalWriter` still constructs the builder per write. It was
  justified by the builder caching a FAILED catalog read as "no row"; since the fix round (F1 below) a fault is never cached, so
  that reason no longer applies. Removing the per-write construction is not trivial (one builder shared by concurrent writes
  needs a thread-safe cache), so it stays; its cost (one `sprk_recordtype_ref` read per write) is #1806.
- **Readers derive the subject table from the subject lookup, not from the pair type.** `SignalWriter.SubjectEntityOf(pairId,
  lookupIdOf)` returns the one table whose `sprk_regarding*` lookup carries the pair id (`sprk_regardingrecordid`); null when
  none or more than one does (callers fail closed). It works for typed and untyped pairs and for rows written before 141 (they
  carry the lookup too), needs no catalog read, and is the agreement rule uac-r2 uses for an untyped pair. A later stamp in
  another `sprk_regarding*` column (144) carries a different id and never matches.
- **Dedupe key.** Value format unchanged, composed from the request's subject logical name (`BuildDedupeKey`), never read back.

## String readers changed (file:line, after the change)

| File:line | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Signals/SignalWriter.cs:548` | Writer: subject lookup + pair through the shared builder; private pair write and `BuildRecordUrl` deleted |
| `src/server/api/Sprk.Bff.Api/Services/Signals/SignalWriter.cs:332` | `RegardingLookupByEntity` is the builder's `sprk_signal` map, not a copy |
| `src/server/api/Sprk.Bff.Api/Services/Signals/SignalWriter.cs:352` | New `SubjectEntityOf` (the reader rule above) |
| `src/server/api/Sprk.Bff.Api/Services/Signals/SignalCoreRecordAccess.cs:115` | `$select` drops `sprk_regardingrecordtype`, adds the 9 `_sprk_regarding*_value` columns (`SubjectLookupSelect`, :310) |
| `src/server/api/Sprk.Bff.Api/Services/Signals/SignalCoreRecordAccess.cs:194-200` | No-core subject from `SubjectEntityOf`, not the string |
| `src/server/api/Sprk.Bff.Api/Services/Signals/DecisionCommitService.cs:283-291` | `SignalColumns` drops the pair type, adds the 9 lookups |
| `src/server/api/Sprk.Bff.Api/Services/Signals/DecisionCommitService.cs:1377` | `ToRow` subject type from `SubjectEntityOf` (was `GetAttributeValue<string>`, which returns null on a lookup) |
| XML docs: `DecisionCommitService.cs:1414` (`SubjectOf`), `DecisionRecordWriter.cs:52`, `SignalCoreRecordAccess.cs:57`, `SignalWriter.cs:52, 237-247, 743` | Updated |

All 9 lookups exist on live `sprk_signal` (140-schema-delta section 3), so the widened `$select` cannot 400.
`git grep -i regardingrecordtype -- src/server/api/Sprk.Bff.Api/Services/Signals src/server/api/Sprk.Bff.Api/Api/Signals` now finds
only comments plus the To Do / Communication writers that 140-schema-delta section 1a lists as not readers. No hit in
`src/solutions`, `src/client`, `src/server/shared` or `scripts`.

## Verification (local, 2026-10-13)

| Filter / run | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | succeeded, 0 warnings |
| `FullyQualifiedName~TodoRegardingBuilder` | 20 passed (no test edits) |
| `FullyQualifiedName~TaskActionCore` | 7 passed (no test edits) |
| `FullyQualifiedName~RegardingBuilderHostTable` | 4 passed |
| `FullyQualifiedName~SignalPairTypeSchemaAgreement` | 5 passed |
| `FullyQualifiedName~Signal` | 1354 passed |
| `FullyQualifiedName~Decision` | 614 passed |
| `FullyQualifiedName~Workspace` | 557 passed |
| `ServerWriterAncestorStamping\|SecureChildOwnership\|InquiryReply\|CreateTaskNodeExecutor` | 363 passed |
| `Spaarke.ArchTests` | 968 passed |

Mutations (code restored after each, no marker left):

- Untyped-pair tolerance reverted (the untyped branch throws): `RegardingBuilderHostTableTests` 2 failed (both communication
  cases), 2 passed.
- Writer emits the logical name as a string (`entity["sprk_regardingrecordtype"] = subjectEntity;` after the builder):
  `SignalWriter_WritesThePairTypeInTheShapeTheSolutionDeclares` failed with "declared lookup to sprk_recordtype_ref, written
  String 'sprk_matter'" (1 failed, 4 passed).

Publish size (`dotnet publish -c Release`, `Compress-Archive`, 192 files both): base `origin/docs/ontology-platform-design`
`07c4971f8` 38,586,476 B; head 38,587,338 B; delta +862 B compressed (+3,080 B uncompressed, `Sprk.Bff.Api.dll` +2,560 B).
Total 36.80 MB, under the 55 MB review line. No package reference changed, so no new CVE.

## Dedupe-key compatibility

spaarkedev1 holds **0** `sprk_signal` rows (read-only query, 2026-10-13), so there is no live key to compare against.
`SignalWriterTests.WriteAsync_DedupeKey_IsThePreLookupKey_AndAnExistingRowWithItIsReconciled_D132` pins the pre-141 keys for the
dev seed subjects (`POL-COMMIT-BUDGET|sprk_matter|2444af6d-e1f2-f011-8406-7ced8d1dc988|1`,
`POL-COMMENT-FLAG|sprk_communication|f00b6389-8fbf-f111-aaaf-0022482913fc|1`): the new writer composes exactly that key, and a
seeded existing row carrying it is reconciled (duplicate path), not re-created.

## Live seam run: PENDING OWNER APPROVAL (Q7)

Not run: creating rows on spaarkedev1 needs the owner's Q7 decision. The exact command, from `C:\wts-141`:

```
SIGNALS_LIVE_DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com \
SIGNALS_LIVE_CALLER_ID=3121bf1b-9fbf-f111-aaaf-0022482913fc \
AZURE_TOKEN_CREDENTIALS=AzureCliCredential \
dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj --filter "FullyQualifiedName~SignalWriterSeamTests"
```

Afterwards: `zz-037` rows must be 0 and no `ONTOLOGY-DEV-TEST` rows remain. The seam test now types the pair from a live catalog
read (`RecordTypeCatalogStub` over the operator client) and asserts the event/To Do pair type names the subject's catalog row
and the communication pair is untyped.

## Section 11 component justification

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `TodoRegardingBuilder.RegardingLookupsByHostTable` + Signal/DR map (commit 1) | `TodoRegardingBuilder.RegardingLookupByEntity` (To Do only) and `SignalWriter`'s private copy | It is the extension: the To Do map is reused as the `sprk_todo` entry | `SignalWriter` keeps a private regarding copy that writes a string into a lookup column; Dataverse refuses the create (D-132) |
| `ApplySubjectLookupAndPairAsync` (internal) | `ApplyResolverFieldsAsync` | Same private steps 1-2, without step 3; a flag on the public method would change the To Do/Event signature uac-r2 reviews | Using `ApplyResolverFieldsAsync` would stamp the Signal from the builder, which contract C2 assigns to 144 |
| `SignalWriter.SubjectEntityOf` (internal) | `ReadString(..."sprk_regardingrecordtype")` / `GetAttributeValue<string>` in the readers | Replaces them; one helper for both readers | The no-core Do-lane access check and the Decision Record subject return null on a lookup pair (silent deny / lost subject) |
| `SignalWriter` ctor param `ICommunicationDataverseService` | Already registered singleton (`GraphModule.cs:150`), the builder's catalog source for To Do and Event | Reuses that registration | The builder cannot type the pair |
| `RecordTypeCatalogStub` (test infra) | Strict Moq setups per test | One shared fake gives every Signal writer test the dev catalog shape through one default argument | Every Signal test would need its own catalog mock |

Placement: modification of existing BFF services (`Services/Workspace`, `Services/Signals`); no endpoint, no DI registration, no
package added.

## Test scope (beyond the named cases)

- Existing Signal tests edited (`SignalWriterTests`, `SignalReevaluationJobTests`, `DecisionCommitServiceTests`,
  `DecisionActionCatalogTests`, `SignalSuppressionTests`, `SignalWriterSeamTests`): their fixtures wrote or asserted the string pair
  type, which is now wrong. Fixture-only changes plus the typed/untyped assertions the criteria name. One metadata-fault case in
  `DecisionCommitServiceTests` used the subject `sprk_unknown`, which no longer resolves to a subject at all; it now uses `sprk_event`
  so it still exercises the metadata fault it was written for.
- `SignalPairTypeSchemaAgreementTests`: besides the two writer checks the criterion names, `TheSolution_Declares...` pins the
  premise (lookup to `sprk_recordtype_ref` on both tables), `Disagreement_Fires...` holds the must-fire/must-not-fire controls, and
  `DeclaredType_ReadsATextDeclarationAsText...` proves the parser would see a column changed back to text. Each is a control the
  enforcement ladder requires for a new guard.

## Open items

- **Signal create-time stamp**: not written by 141 (contract C2: 144 registers it; 143 owns ownership).
- **No `sprk_communication` catalog row** on spaarkedev1, so communication Signals carry the untyped pair. Valid (D-133); add the
  row if typed communication pairs are wanted.
- **One catalog read per Signal write** (K-class, filed as #1806): `QueryRecordTypeRefAsync` is uncached in
  `DataverseServiceClientImpl`, and the writer builds a builder per write (see design). A shared, TTL-bounded catalog cache would
  remove it; not fixed here.
- Live seam run pending Q7 (above). Adversarial review and uac-r2's review of commit 1 are run by the main session.

## Fix round: PR #1805 review (adversarial review + uac-r2), 2026-10-13

Merged `origin/docs/ontology-platform-design` (`a30d60e89`: docs, POML and TASK-INDEX only, no conflicts; no #1797 code on it) as `af6ac0ae6`, then
one fix commit.

### F1. A catalog fault is never "absent" (all hosts)

`TodoRegardingBuilder.ResolveRecordTypeRefAsync` (:375-393) no longer catches. A read that succeeds and finds no row is untyped
(D-133) and cached; a fault (429, timeout) propagates and is never cached; caller cancellation propagates as itself. The real
defect: `TodoGenerationService` holds ONE builder in its singleton `IScheduledJob`, so one 429 made every later generated To Do of
that parent type untyped until restart.

Per-caller throw analysis (does the throw become "do not create", with no other user-visible change?):

| Caller | Before (fault) | After (fault) | Handling |
|---|---|---|---|
| `TodoGenerationService.CreateTodoAsync` (rules 1-5) | untyped To Do created; negative cached for the job's lifetime | throws; each rule's per-row `catch` logs it, `failed++`, the pass continues; the next pass retries | existing, unchanged |
| `InquiryReplyTodoCreator` | untyped To Do created | throws inside `CreateIfNeededAsync`; the `OnReplyArrivedAsync` catch returns `Failed` with a warning | existing, unchanged |
| `TaskActionCore` (`sprk_event`) | untyped task created | builder fault caught in `TaskActionCore` (:201-218) and returned as the degraded `Guid.Empty`, the contract its stamp failure already uses; cancellation propagates | new catch (see below) |
| `SignalWriter` | untyped Signal created | `SignalWriterEscalationException(SubjectPairUnresolved)` (:545-560), logged `WriteRefused` and metered once; the job's `GuardAsync` counts it refused and does not meter it again | new reason constant |

Why `TaskActionCore` catches rather than propagating: its callers are `CreateTaskNodeExecutor` (catch-all, node error),
`ActionSeam.CreateTaskAsync` (no catch) and, through the seam, `CommunicationCreateTaskApplyService` (two apply paths with NO catch
around the seam call, so a propagated throw would be a 500 instead of the handled 422 `CREATE_TASK_FAILED`),
`CommunicationEnrichmentService` (two paths, catch then degrade, row left for retry) and `CommunicationRiActionService` (a
propagated throw would abort the outbox write and ping that a degraded `Guid.Empty` lets continue). Mapping the fault to
`Guid.Empty`, the way the class already handles its stamp failure, makes every one of them "not created" through its existing
branch: no caller changes, no new user-visible behaviour.

`SubjectPairUnresolved` (`OntologyWriterTelemetry.cs:273-276`), section 11. Existing: the bounded reason set; none names the
subject pair (`CoreRecordTypeNotCataloged` is the CORE table's catalog). Extension: it is one more constant in that set. Cost of
doing nothing: the escalation would carry a wrong reason, so log and metric queries would misattribute catalog faults.

### F2. `SubjectEntityOf` and the catalog test names

New `SignalWriterTests.SubjectEntityOf_ExactlyOneLookupCarryingThePairId_NamesTheSubject_NoneOrTwoAreNull` (0, 1 and 2 lookups
carrying the pair id, plus one lookup carrying another id). The `DecisionActionCatalogTests` theory is renamed to
`ANoCoreDoItem_WhosePairIdNoSubjectLookupCarries_IsTheUniformNotFound_AndNothingIsRead`: `sprk_signal` and `systemuser` have no
`sprk_signal` subject lookup, so all three cases take the "no lookup carries the pair id" path, not the K2 ownership-parent check.

### F3. Fail closed on an underivable subject

- FR-51 (`DecisionCommitService.CallerMayDecideAsync`, :1297-1303): a Signal whose `SubjectId` is set but whose `SubjectEntity`
  cannot be derived answers **Forbidden** (`NotAuthorized`), as an unreadable subject does. A Signal with no pair id still adds
  nothing.
- DR owner parents: new `internal static SignalSubjectsOf` (:1425-1451) replaces `.OfType<DecisionRecordRef>()` (:1674). An
  unknown subject throws `DecisionRecordRefusedException(OwnerRefused, ParentUndetermined)`, which the route maps to **Refused**
  (the step 1e / 1f catches). In the full route FR-51 refuses first, so this is the second line; it is tested at the helper.
- Real route: the `sprk_signal_reg*` relationships cascade delete as RemoveLink, so deleting the subject clears the lookup and
  leaves the pair id.

### K1. `ApplySubjectLookupAndPairAsync` host guard

Accepts `SubjectLookupHostTables` (`sprk_signal`, `sprk_decisionrecord`) only (:238-265); any other host, `sprk_todo` included,
throws `ArgumentException` (param `host`). A To Do written through it would skip the FR-26 stamp and the task-173 inherited
Access Permission, which fails open.

### Notes only

- **K2 (uac-r2): a constraint for task 144 and any later Decision Record writer.** On `sprk_signal` and `sprk_decisionrecord` the
  ADR-024 mutual-exclusion guard covers every `sprk_regarding*` lookup of the host, and four of them (`sprk_regardingproject`,
  `sprk_regardingmatter`, `sprk_regardingworkassignment`, `sprk_regardingservicerequest`, `CoreAncestorResolver.CoreAncestorLookups`)
  are also the C2 stamp columns. A writer that stamps BEFORE calling the builder is refused (`InvalidOperationException`, "already
  has a regarding lookup set"). Call the builder first, then stamp: the order `ApplyResolverFieldsAsync` already uses. Also in
  `140-schema-delta.md` section 9.
- **Review K2 (log noise, communication):** every communication-subject write logs the untyped-pair warning, because spaarkedev1
  has no `sprk_communication` catalog row. Expected (D-133); a catalog row or a lower log level would quiet it. Not changed.
- **Review K1 (TopCount = 1 vs exactly one):** `QueryRecordTypeRefAsync` takes the first active row, while
  `SignalWriter.ResolveRecordTypeRefIdAsync` (the core type) demands exactly one. A duplicated catalog row would type the pair from
  either row silently. Not changed.
- **Review K4:** the `SignalPairTypeSchemaAgreementTests` seam check re-implements the catalog query instead of calling the
  production one. Not changed.

### Existing test edits

No existing `TodoRegardingBuilderTests` case asserted that a fault yields untyped, so none was changed. The only existing-test
edit is the F2 rename in `DecisionActionCatalogTests` (method name and case comments; cases and assertions unchanged).

### Verification

| Run | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | succeeded, 0 warnings |
| filter `TodoRegardingBuilder`, `TaskActionCore`, `RegardingBuilderHostTable`, `SignalPairTypeSchemaAgreement`, `SignalWriter`, `DecisionCommit`, `DecisionActionCatalog`, `TodoGeneration`, `ServerWriterAncestorStamping`, `SignalSuppression`, `SignalReevaluation` | 723 passed |
| filter `Signal`, `Decision`, `Workspace`, `SecureChild`, `RecordOwnership` | 2678 passed |
| filter `InquiryReply`, `CreateTaskNodeExecutor`, `ActionSeam`, `CommunicationCreateTaskApply`, `CommunicationRiAction`, `CommunicationEnrichment` | 53 passed |
| `Spaarke.ArchTests` | 968 passed |

Mutations (each applied alone, rebuilt, the 14 new tests run, the code restored byte for byte):

| Mutation | New tests failing |
|---|---|
| A: the builder catches the fault again (old `catch (Exception)`, negative cached) | 7: the builder fault and cancel tests, the `TodoGeneration` fault test, both `TaskActionCore` tests, both `SignalWriter` tests |
| B: K1 guard disabled | `TodoHost_SubjectLookupAndPair_IsRefused...` |
| C: FR-51 `continue` restored | `ASignalWhoseSubjectTableCannotBeDerived_IsForbidden...` |
| D: unknown subject dropped (the old `OfType`) | `TheDecisionRecordOwnerParents_Refuse...` |
| E: `TaskActionCore` catch disabled | `CreateTask_WhenTheRecordTypeCatalogReadFaults...` |
| F: `SignalWriter` escalation wrap disabled | `WriteAsync_SubjectCatalogReadFaults_Escalates...` |
| G: `SubjectEntityOf` keeps the first of two matches | `SubjectEntityOf_...(2, null)` |

Publish size (`dotnet publish -c Release`, `Compress-Archive`, 192 files both): base `origin/docs/ontology-platform-design`
`fd161c1ea` (docs-only ahead of the merged `a30d60e89`) 38,586,487 B; head 38,587,782 B; delta **+1,295 B** compressed
(+5,936 B uncompressed, `Sprk.Bff.Api.dll` +5,120 B). Total 36.80 MB. No package change.
