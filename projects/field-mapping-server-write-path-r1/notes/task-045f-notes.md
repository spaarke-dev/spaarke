# Task 045f — the regarding record URL names the row's own regarding record, on every pair table

POML: `tasks/045f-regarding-record-url-canonical-on-every-pair-table.poml`. Part A (server, AC1–AC4) is below. Part B (the
client readers, AC5) is recorded by its own agent.

## Part A — server

### Rule

1. Scope: every pair table (`OwnedChildWrite.RegardingPairTables`) except the six invoice-family tables
   (`InvoiceFamilyCoreRecordGuard.CoreRecordLookups`), which keep 045e's decision against the core record. That leaves
   `sprk_analysis`, `sprk_communication`, `sprk_decisionrecord`, `sprk_event`, `sprk_kpiassessment`, `sprk_memo`,
   `sprk_project`, `sprk_reportcard`, `sprk_servicerequest`, `sprk_signal`, `sprk_todo` and `sprk_workassignment`
   (`OwnedChildWrite.RegardingRecordUrlRuleApplies`).
2. A `sprk_regardingrecordurl` the BFF writes must name the record that the row's regarding pair names after the write. The
   pair is the one the same request writes (after the 045b planner), else the stored one. A half not sent is read: as the
   caller on caller paths, app-only on app-only paths, as the planner reads. The pair's type gives the table, through the
   catalog row (`sprk_recordtype_ref.sprk_recordlogicalname`). A create has no stored row.
3. "Names" uses 045e's parser, `RegardingPairWrite.TryReadRecordUrl`, which requires exactly one `etn` and exactly one `id`
   that parses as a GUID. The string is never compared, so a relative form, an absolute form on any host or scheme, an
   upper-case value and a braced value all agree when their table and id do.
4. Outcomes:
   - An admitted URL is stored in canonical form: `InvoiceFamilyCoreRecordGuard.CanonicalRecordUrl` =
     `RegardingRecordType.BuildRecordUrl(table lower-case, lower-case "D" id)`.
   - The URL is refused, and nothing is written, when:
     - it names another table or another record;
     - it names no record (`TryReadRecordUrl` cannot read it);
     - the row has no pair after the write (no id, an id that is not a GUID, an unreadable pair value);
     - the pair's type names no table the reader can see (403 or 404 on the catalog row).
   - A clear (null or blank) is allowed and reads nothing.
   - A write that sends no URL is unchanged. A pair move with no URL therefore keeps the planner's derive.
   - A URL sent with a pair move is decided against the NEW pair.
5. A pair with an id and no type (option C, the one design choice). The URL is admitted when its id equals the pair id. Its
   own `etn` is kept, after it is validated as a logical name, and written in canonical form. A URL with any other id is
   refused.
   - Why: the BFF's own event and to-do creates, and the client wizards, send an id-only pair when the catalog row does not
     resolve (see the inventory). Refusing those would break legitimate writers. The pair names no table to compare the URL
     against, so the id is the whole of the pair.
6. Fail closed. A stored-row read or a catalog read that fails (anything but 403 or 404) writes nothing, through the path's
   existing failure form. The caller's cancellation is rethrown.
7. One decision function, `OwnedChildWrite.DecideRegardingRecordUrlAsync`, is called by each path through two wrappers:
   `KeepRegardingRecordUrlAsync` (caller paths) and `KeepRegardingRecordUrlAppOnlyAsync` (app-only paths). The create calls
   it directly. Its pure part is `RegardingPairWrite.TryAdmitRecordUrl`. 045e's parser and canonical builder are reused, not
   moved or forked.

### Paths and where the rule sits

| Path | Entry | Where 045f is asked | Refusal form (existing) |
|---|---|---|---|
| Child-record PATCH, event and communication filing routes | `ChildRecordEndpoints` → `OwnedChildWrite.KeepRegardingPairWholeAsync` | after the 045b planner, before the 045d family step | `RefileOutcome.Invalid`: 400 `InvalidPayloadCode`, detail "… (regarding_record_url_mismatch)" |
| Chat tool `dataverse.update_record` | `DataverseUpdateRecordHandler` → `KeepPairInStepAsync` | non-project tables: as above. `sprk_project` (BFF-managed pair): at the top of the project branch | `ToolErrorCodes.ValidationFailed`, "The update was NOT written: … (code)" |
| Playbook Update Record node and action seam | `UpdateRecordActionCore` → `KeepRegardingPairWholeAppOnlyAsync` | after the planner, inside the wrapper. The gate widens to `RegardingPairWriteApplies` (planner applies, OR a URL the rule decides is sent) — one changed line | `RecordOwnerUnresolvedException(table, regarding_record_url_mismatch, "The update was NOT written: …")`. A failed read: `ParentUndetermined` |
| Output orchestrator | `DataverseUpdateHandler` → `KeepRegardingPairWholeAppOnlyAsync` (unconditional) | inside the wrapper | as the playbook node |
| Create | `RecordCreationService.CreateAsync` | after step 5 (the caller checks), before step 5½. Step-5 refusals keep their order | `MapPreWriteRefusal(PreWriteRefusal.Refused(code, reason))`: 400 with the code. A failed caller read is `Upstream`; a failed app-only read is `Undecided` / `CheckFailedCode` |
| Field-mapping push | `ApplyMappingsToChildRecordsAsync` | shares the app-only wrapper, but never maps the URL (`CreateTimeFieldMapping.cs:65` skips it), so the rule is a no-op there | — |

### Inventory of URL writers (merged code, 1627657d0c)

| Writer | Reaches | Form sent | Decision |
|---|---|---|---|
| `EventEndpoints` event create (`:1245`, `TodoRegardingBuilder.BuildRecordUrl`) | create | relative; etn and id = the pair's. Typed when `RecordTypeRefId` resolves, otherwise id-only | admitted, unchanged (rule 5 when id-only) |
| `OfficeService` to-do create (`:2941`) | create (`_recordCreation`) | relative; etn and id = the pair's. Type best effort, otherwise id-only | admitted, unchanged (rule 5 when id-only) |
| `DecisionRouteCores` → `ChildRecordEndpoints.CreateAsync` | create | as its caller sends it | decided |
| Chat create and update tools | create / chat update | anything the model sends | decided — the case this rule exists for |
| Client wizards (event, to-do, report card, work assignment), `useInlineTodoCreate`, `TodoRegardingUpdateBuilder.buildTodoRegardingUpdate` → `PolymorphicResolverService.applyResolverFields` → `buildRecordUrl` | create / PATCH | absolute with `appid` (another host form), or relative fallback; etn = parent table, id = the clean id. Typed when the catalog row resolves, otherwise id-only | admitted, canonicalised (rule 5 when id-only) |
| `TodoRegardingUpdateBuilder.buildTodoRegardingClear`, `ConnectionsWriteHandler` | PATCH | null | clear: allowed |
| `ResolverWriteHandler` | Xrm (not the BFF) | null | not on these paths |
| `sprk_todo_regarding_presave.js` | model-driven form | — | not on these paths (known limit) |
| `ExternalDataService` (`:3589`) | SDK / its own path (inventory exception) | relative, its own pair | not on these paths |
| `IncomingAssociationResolver` (`:903`), `ThreadResolver`, `CommunicationService` | SDK writes | relative, own pair | not on these paths |
| `TodoRegardingBuilder.ApplyResolverPairAsync` (`:355`), `TaskActionCore`, `TodoGenerationService`, `InquiryReplyTodoCreator`, `SignalWriter`, `DecisionRecordWriter` | SDK `CreateAsync` | relative, own pair | not on these paths |
| Invoice-family writers | 045d / 045e | — | out of scope (045e) |
| Playbooks (daily-update notifications) | read only | — | — |
| Field-mapping push | never maps the URL | — | no-op |

No legitimate writer sends a form the rule refuses, so no STOP was needed. The forms that would have been refused were the
id-only pairs, and rule 5 admits them.

Two existing test fixtures sent a URL that did not name the row's pair, and were corrected to the form the real writer sends:
- `SecureChildOwnershipAiToolTests.EventFiling_TheRegardingLookupsAndTheResolverFields_AreTheFiling_AndAreWrittenAsTheCaller`
  sent `/main.aspx?etn=sprk_matter`, which has no id. The real resolver always sends the id.
- `WorkAssignmentCreate_TheWizardsInvoiceShape_UntypedBecauseTheCatalogRowIsMissing_IsFiledUnderTheInvoice` kept the matter's
  URL beside an invoice pair. The wizard builds the URL of the record it files under.

### §11 — the one new reason code

- **Code:** `regarding_record_url_mismatch` (`RegardingPairWrite.RecordUrlMismatchCode`).
- **Fixed text:** "the regarding record URL does not name the record this row is regarding". It names no record.
- **Existing:** 045e's `invoice_family_regarding_pair_core_record_mismatch` decides the pair (and its URL) against the CORE
  record on the six family tables only. Its text and its scope are about the core record, so reusing it on the other twelve
  tables would mislabel the refusal. 045e's `TryReadRecordUrl` and `CanonicalRecordUrl` and the 045b planner's derive are
  reused, not forked.
- **Extension:** the new members extend `RegardingPairWrite` (the code, the reason and `TryAdmitRecordUrl`) and the existing
  planner wrappers in `OwnedChildWrite`. The playbook gate is the existing call site, with a wider predicate.
- **Cost of doing nothing:** on the twelve non-family pair tables, a write can store a URL that names another record than the
  row's pair, so every reader that opens the URL opens the wrong record.

### Before → after

- **Before:** on a non-family pair table, every path wrote any `sprk_regardingrecordurl` as sent: another record, another
  table, a URL naming no record, or a URL kept from the old pair on a pair move.
- **After:** the URL is written only when it names the pair's record after the write, and always in canonical form. Otherwise
  the write is refused with `regarding_record_url_mismatch` through the path's existing form, and nothing is written. A clear
  and a pair move without a URL behave as before.

### Evidence

New tests (KEEP paths):

| File | Tests | Fail with the rule off |
|---|---|---|
| `tests/unit/domain/Dataverse/RegardingRecordUrlDecisionTests.cs` | 103 | 87 |
| `tests/integration/data-mutation/RecordOwnership/InvoiceFamilyGenericUpdatePathTests.RegardingRecordUrl.cs` (playbook and orchestrator, `sprk_todo` and `sprk_event`) | 38 | 32 |
| `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.RegardingRecordUrl.cs` (PATCH, event filing, chat, chat project) | 26 | 20 |
| `tests/integration/seam/RecordCreation/RecordCreationServiceMappingSeamTests.RegardingRecordUrl.cs` (caller and system) | 14 | 12 |

- **Rule off:** a temporary local switch made `RegardingRecordUrlRuleApplies` false. It was never committed, and the file was
  restored from a copy.
- **What still passes with the rule off:** every must-not-fire case that needs no rewrite — clears, no URL, the family and
  no-pair tables, the pure helper, and URLs already in canonical form. Two must-fire cases pass too:
  - `ChildRefile_AStoredPairReadFault_WritesNothing`, because the PATCH already reads the row earlier and that read fails
    closed as well;
  - the event filing pair move with no URL, which is the planner's derive.
- **The ChildRefile read fault:** the chat read-fault test, the app-only read-fault tests and the create catalog-fault test do
  fail with the rule off.

Gates (after the fix):
- `dotnet build Spaarke.sln`: 0 warnings, 0 errors.
- ArchTests, run alone and in full: 2544/2544, including guard 2 `GenericUpdatePathsPreparePairGuardTests`.
- Filtered `Sprk.Bff.Api.Tests` (RecordOwnership | InvoiceFamily | RecordCreation | ChildRecord | UpdateRecord |
  DataverseUpdateHandler | ActionSeam | FieldMapping | RegardingPair | RegardingRecordUrl | SecureChildOwnership |
  OwnedChildWrite, minus `Api.Office.`): 5052/5052.
- ebilling (Finance | ProcedureAssignment | InvoiceFamily, minus `Api.Office.`): 8239 passed, 4 skipped (existing skips),
  0 failed.
- `dotnet format whitespace --folder --verify-no-changes` on the touched files: clean.

Before the two fixture corrections, the first filtered run failed 2 of 4869, both on those stale fixtures.

### Known limits

- **Reads.** One extra stored-row read on an update that sends a URL without both pair halves (as the caller, or app-only),
  and one catalog read when the pair is typed. Nothing is read for a clear or for a write without a URL.
- **Option C (rule 5).** On an id-only pair, the URL's table is not checked against a type, because there is none. Only its
  id is checked.
- **App-only missing row.** A row that does not exist (404) is read as having no pair, so a URL sent to it is refused with
  this code. The write would have answered not-found anyway.
- **The push.** The field-mapping push is not exercised: it never maps the URL.
- **Model-driven form.** Edits on the model-driven form (`sprk_todo_regarding_presave.js`, Xrm writes) do not pass through
  the BFF and are not covered.
- **Id-only update.** An update that sends only the id keeps the stored type, so a URL whose `etn` disagrees with the stored
  type is refused.

### Fix round 1 (coordinator review)

#### Rules added or sharpened

1. **The communication thread is under the rule** (F1b). `sprk_communicationthread` carries the URL column but no pair; it is
   filed by typed regarding lookups (`ThreadResolver.ThreadRegardingColumns`). On the thread, a sent URL must name the record
   its first typed regarding lookup names after the write, in ADR-024 priority order (`RegardingFieldMap.All`): a lookup the
   same write sets decides; a lookup it clears is skipped; otherwise the stored lookup decides. A thread anchored nowhere
   refuses a URL. Same code, same forms, same canonical write, same fail-closed reads (the caller's row on caller paths,
   the app-only row on app-only paths, a missing app-only row read as anchored nowhere).
2. **A write run as a person reads the stored pair as that person** (F1c). On the playbook node and the output orchestrator,
   when the run acts as a person, the stored pair (or the thread's lookups) is read with that person's query
   (`OwnedChildWrite.PersonStoredRowReader`). A row the person cannot read (403) or that is not there for them (no row, 404)
   is the read-failed answer (`record_owner_parent_undetermined`), never the URL-mismatch code. A readable row is decided as
   before.
3. **Only a URL the write sends is decided** (K1). The URL step runs only when the original request sends the URL; a URL the
   planner itself derived on a pair move is not re-decided.
4. **Server code never copies one row's stored URL onto another** (F1a and the grep). Every BFF write of the URL builds it
   from the record it names (`InvoiceFamilyCoreRecordGuard.CanonicalRecordUrl`), or admits a copied one only when it names
   the record being copied (`RegardingPairWrite.TryAdmitRecordUrl`):
   - a communication sent into a thread takes the canonical URL of the thread's own anchor, not the thread's stored text;
   - a reply copies the source's URL only when it names the copied record, in canonical form;
   - an explicit association writes the canonical URL of the associated record, never the request's text; an association
     of a table the regarding map does not know writes no URL;
   - the thread anchors `ThreadResolver` builds from an email or another thread carry the canonical URL of the anchor record.

#### Table enumeration (every `Entity.xml` with `sprk_regardingrecordurl`)

| Table | Writable via the five paths? | Pair? | Action |
|---|---|---|---|
| the 12 non-family pair tables | yes | id + type | under the rule (part A) |
| the 6 invoice-family tables | yes | id + type | 045e (unchanged) |
| `sprk_communicationthread` | chat update, playbook node, output orchestrator (not the PATCH re-file routes, not the create: no table rule) | typed regarding lookups | brought under the rule (rule 1 above) |

No other table carries the column, so no "writable with no pair" refusal was needed.

#### URL-copy grep (`sprk_regardingrecordurl` / `RegardingUrl` / `RecordUrl` writes in `src/server`)

| Site | Before | After |
|---|---|---|
| `CommunicationService.ApplyTargetThreadAsync` | the thread's stored URL | canonical URL of the thread's anchor |
| `CommunicationService` reply copy | the source's stored URL | admitted only when it names the copied record, canonical |
| `CommunicationService.MapAssociationFieldsAsync` | the request's `EntityUrl` text | canonical URL of the associated record; none for an unknown table |
| `ThreadResolver` email anchor and thread-to-thread anchor (feed thread create, re-anchor, default thread) | the source row's stored URL | canonical URL of the anchor record |
| `InvoiceSubmissionEndpoints` re-file body | built by `DeriveResolverFieldsAsync` | unchanged (built, not copied; family) |
| `ExternalDataService`, `IncomingAssociationResolver`, `TodoRegardingBuilder`, `EventEndpoints` | built from their own pair | unchanged |

Nothing outside the BFF copies the column.

#### Comments (F2b)

The "It adds no check" comments on the orchestrator, the playbook node and the push now say the same call decides a sent URL.
The PATCH comment states that, with a URL sent, a missing row is refused.

#### Known limits added

- **K2.** On an id-only pair (rule 5), any `etn` is kept when the id matches.
- **K4.** A re-file that sends only the id over a stale stored type now gets 400 when the URL names the new table. This
  depends on the live catalog.

#### Evidence

New or changed tests:

| File | New | Fail before the fix |
|---|---|---|
| `tests/unit/domain/Dataverse/RegardingRecordUrlDecisionTests.cs` (the thread) | 10 | 9 with the rule off (the clear passes) |
| `InvoiceFamilyGenericUpdatePathTests.RegardingRecordUrl.cs` (as a person, ×2 paths) | 8 | 8 with the rule off; the 4 read-failed cases also fail when the stored row is read app-only |
| `InvoiceFamilyGenericUpdatePathTests.RegardingRecordUrl.cs` (the thread, ×2 paths) | 8 | 6 with the rule off (the canonical same-write anchor passes) |
| `SecureChildOwnershipAiToolTests.RegardingRecordUrl.cs` `ChildRefile_AStoredPairReadFault_WritesNothing` (F2a: faults only the full-row read, asserts 503) | changed | fails with the rule off |
| `tests/integration/data-mutation/Communication/ReplyRegardingCopyTests.cs` | 4 | 4 on the old communication code |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/ThreadResolverTests.cs` (not a KEEP path) | 1 | fails on the old code |
| `AssociationMappingTests` (2 expectations changed to the canonical URL / no URL) | changed | both fail on the old code |

The domain test `ATableWithNoRegardingPair_IsNotDecided` no longer lists the thread.

Rule off (`RegardingRecordUrlRuleApplies` false, a temporary local switch, restored from a copy): 24 of the 27 new or changed
045f tests fail; the 3 that pass are must-not-fire cases (a clear, and a URL already canonical for the anchor the same write
sets). The communication fixes were proved by putting the previous `CommunicationService.cs` and `ThreadResolver.cs` back
temporarily: 7 of 196 tests in `ReplyRegardingCopyTests`, `ThreadResolverTests` and `AssociationMappingTests` fail, all the
new or changed ones.

Gates (fix round 1):
- `dotnet build Spaarke.sln`: 0 warnings, 0 errors.
- ArchTests, alone and in full: 2544/2544.
- Filtered `Sprk.Bff.Api.Tests` (the part-A classes plus `Communication` and `ThreadResolver`, minus `Api.Office.`):
  7830 passed, 2 skipped, 0 failed. The first run, in the gates script beside the ArchTests build, had 1 failure whose name
  the quiet run did not print; the rerun after a formatting-only change had none.
- ebilling (Finance | ProcedureAssignment | InvoiceFamily, minus `Api.Office.`): 8255 passed, 4 skipped, 0 failed.
- `dotnet format whitespace --folder --verify-no-changes` on the touched files: clean.
