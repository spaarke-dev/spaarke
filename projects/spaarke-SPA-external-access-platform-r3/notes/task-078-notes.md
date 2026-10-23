# Task 078 — ISS-010 quick fix: the generic Dataverse chat tools hide internal-only communications from external callers

Date: 2026-10-20. Rigor: **FULL** (tags `bff-api`, `security`, `ai`, `testing`; two adversarial passes, run by the main session). Implementation: Sonnet-tier implementer agent from the session's decided design.
Origin: ISS-010 (#1864). Owner decision 2026-10-20: "use the quick fix … using the 'external user' and the external/internal flag on communication record (same for email and messages)".

## Why

Owner decision 2026-10-14: a licensed Dataverse user whose systemuser has `sprk_isexternal = true` is treated like a partner for Internal Only. Task 065 hid internal-only and Private-thread communications from those callers on the BFF's own reads.

The assistant's generic tools still ran under the user's OBO token, so Dataverse security alone decided:
- `dataverse.read_query` could `SELECT sprk_body FROM sprk_communication`;
- `dataverse.search_data` returned hit text from any search-enabled table;
- `dataverse.describe` returned a full record (found in the inventory, below).

A licensed external user with read on a matter's communications could therefore read internal-only email and message bodies through the assistant.

## The rule (reused, not redefined)

An external caller sees a `sprk_communication` only when all three hold:
- `sprk_isinternalonly` is `false` (true or null hides; `CommunicationInternalOnlyFloor.InternalOnlyField`);
- its thread lookup `_sprk_communicationthread_value` is set (`CommunicationOpenThreadGate.MessageThreadLookupValue`);
- that thread reads Open now (`CommunicationOpenThreadGate.ReadOpenThreadIdsAsync`, which uses the floor's `IsConfirmedOpen`).

**Who is external.** The request principal (`IHttpContextAccessor`) goes to `ICallerSystemUserResolver.ResolveAsync`, then to `ISystemUserIdentityResolver.IsExternalAsync`. An unresolved caller (no principal, no oid, no active systemuser) is **external** (fail closed). A caller whose lookup **failed** (`UnresolvedReason = "lookup-failed"`, a Dataverse or cache fault the resolver swallows) makes the tool call **fail with no data** (review round 1, F2), and so does a throwing `IsExternalAsync`. Externality is decided **only** when the request touches the communication family (a logical name starting `sprk_communication`, or one of the content tables listed under "Inventory", OrdinalIgnoreCase) or `sprk_document`. Every other request is byte-identical and resolves nothing.

**Documents (review round 1, F1).** An external caller does not see a `sprk_document` whose `_sprk_relatedcommunication_value` points at a communication that fails the rule above (flag true or null, no thread, thread not Open, or the communication cannot be read or does not exist). A document with no related communication is unaffected.

## What changed

| File | Change |
|---|---|
| `Services/Ai/Handlers/Dataverse/ExternalCallerCommunicationGuard.cs` (new, `internal static`) | The family predicate, the `$filter` clause, `ApplyFilter` (the model's whole predicate in parentheses, ANDed with the clause), `IsExternalCallerAsync` (fail closed), `ThreadQueryOver(IDataverseUserClient)` (the `ThreadRowQuery` delegate over `sprk_communicationthreads`; **throws** on `!IsSuccess`), `ThreadIdOf`, `IsExplicitlyShareable`, the refusal message and the log event name. Carries the §11 header. |
| `Services/Ai/Handlers/DataverseReadQueryHandler.cs` | For an external caller on `sprk_communication`, the query is rebuilt with `AssembleODataQuery` (lookup rewrite kept). `_sprk_communicationthread_value` is added to `$select` if absent. The filter is `(<model filter>) and sprk_isinternalonly eq false and _sprk_communicationthread_value ne null`, or the clause alone. After the read: one batched thread-privacy read, then rows whose thread is not Open are dropped. An injected thread column is removed from the output rows, under the key it lands on (`FromValueForm`). On any other family table, an external caller is refused **before any Dataverse request**: `DATAVERSE_ACCESS_DENIED` with a model-actionable message. |
| `Services/Ai/Handlers/DataverseSearchDataHandler.cs` | Externality is resolved lazily: only when the entity filter names a family table or a family hit comes back. For an external caller, family names are removed from the filter silently. A filter of only family names returns the normal empty result, with **no search call** and never an unscoped one. Family hits are dropped by `entityname` after every call, including the H8 unscoped retry. Count and summary are post-filter, and no warning reveals a drop. Result building moved into a shared `Completed(...)` helper, so both paths read identically to the model. |
| `Services/Ai/Handlers/DataverseDescribeHandler.cs` | **In-scope fix found by the inventory** (same generic OBO read pattern). On the record path, an external caller is refused any family record except `sprk_communication`, before any Dataverse request. A `sprk_communication` record that fails the rule answers `DATAVERSE_NOT_FOUND` "not found or not visible", as `EmailDraftToolHandler` does. A thread-read fault is an error with no data. The table-list and schema paths carry no row content and are unchanged. |
| `Services/Ai/ToolFrameworkExtensions.cs` (both `AddToolFramework` overloads) | `services.AddHttpContextAccessor()` and `TryAddScoped<ICallerSystemUserResolver, CallerSystemUserResolver>()`, with a comment (CLAUDE.md §10 F.1). |
| `tests/unit/.../DataverseReadQueryHandlerTests.cs` | 9 new tests (11 cases). |
| `tests/unit/.../DataverseSearchDataHandlerTests.cs` | 7 new tests. |
| `tests/unit/.../DataverseDescribeHandlerTests.cs` | 6 new tests (10 cases). |
| `tests/unit/.../DataverseToolNameFreezeTests.cs` | Constructor call sites updated (5 sites). |

### Review round 1 changes (commit `8012c9341`)

| File | Change |
|---|---|
| `ExternalCallerCommunicationGuard.cs` | `RefusedContentTables` (`sprk_emailartifact`, `sprk_attachmentartifact`, `sprk_emailreviewlog`, `sprk_affinity`) joins the prefix in the one `IsCommunicationFamily` predicate; `sprk_document` stays out (`IsDocumentTable`, row-filtered). `IsExternalCallerAsync` throws on `lookup-failed`. `FilterClause` adds `sprk_CommunicationThread/sprk_privacystate eq 100000000` (K1). New `ReadVisibleCommunicationIdsAsync` (one batched `sprk_communications?$select=sprk_communicationid,sprk_isinternalonly,_sprk_communicationthread_value&$filter=… or …` read in chunks of 50, then `CommunicationOpenThreadGate.ReadOpenThreadIdsAsync` over `ThreadQueryOver`), `ReadRelatedCommunicationIdsAsync` (one batched `sprk_documents?$select=sprk_documentid,_sprk_relatedcommunication_value` read, for search hits), `RelatedCommunicationIdOf` and `RecordNotVisibleMessage`. Every read throws on `!IsSuccess`. |
| `DataverseReadQueryHandler.cs` | External caller on `sprk_document`: `_sprk_relatedcommunication_value` added to `$select` if absent (stripped from rows under the key it lands on if injected), one visibility read of the related communications after the read, failing documents dropped; a communication that does not come back hides its document. External caller on `sprk_communication`: `sprk_isinternalonly` also injected/stripped and every row must pass `IsExplicitlyShareable` (K4). Internal callers: no injection, no extra read. |
| `DataverseSearchDataHandler.cs` | Hits are collected first; for an external caller with `sprk_document` hits, one batched document read, one communication visibility read, and failing (or not-returned) documents are dropped silently. Count and summary are post-filter; no warning. |
| `DataverseDescribeHandler.cs` | Record path on `sprk_document` for an external caller: the record's related communication is checked the same way. On a guarded path (external caller, `sprk_communication` or `sprk_document`), a hidden record and a real 404 both answer `DATAVERSE_NOT_FOUND` with `RecordNotVisibleMessage` (K4); the comment now says so. |
| The three handler test classes | 39 new test cases (below). Existing ISS-010 URL assertions updated for the K1 clause and the injected flag column. |

**Behaviour change for internal callers on `sprk_document`.** The request URLs and results are byte-identical, but externality is now resolved (one cached systemuser lookup through `ISystemUserIdentityResolver`, not the user client). If that lookup faults, the tool call fails with no data (F2), for internal callers too.

The read_query and describe tests set up a resolved internal caller by default; `AsExternalCaller()` switches it. They assert on the exact request URLs captured from the `IDataverseUserClient` mock, and on the returned rows.

### DI notes

- **Handlers are scoped** (assembly scan). Their new dependencies are `IHttpContextAccessor`, `ICallerSystemUserResolver` (scoped) and `ISystemUserIdentityResolver` (singleton). None is captive.
- **`ICallerSystemUserResolver`** is already registered unconditionally by `CommunicationModule` (TryAdd) and conditionally by `AnalysisServicesModule` (Add). The tool-framework TryAdd never clobbers either registration.
- **`ISystemUserIdentityResolver` is not registered in `AddToolFramework`.** `NotificationsModule` registers it unconditionally (`Program.cs` line 116), before `AddAnalysisServicesModule` (line 154) composes the tool framework, so production cannot compose the framework without it. Its own dependencies (`IDataverseService` from `GraphModule`, `IDistributedCache` from `CacheModule`) do not belong to the tool framework. A TryAdd there would be dead in production. This is the same documented posture as `CommunicationModule`'s caller-resolver note.

### Deviation from the brief

The refusal code is `DataverseUserClientErrorCodes.AccessDenied` (`DATAVERSE_ACCESS_DENIED`), not a `ToolErrorCodes` value. `ToolErrorCodes` has no permission code. `ValidationFailed` would tell the model to rephrase the query, which is wrong for a refusal. The sibling handlers already return `AccessDenied` for BFF-side refusals (`DataverseCreateRecordHandler` :567/:688/:699, `DataverseUpdateRecordHandler` :520, `EmailDraftToolHandler` :389/:468). No new code was added.

## Inventory

### Communication-family tables (logical names starting `sprk_communication`, from `src/server` string literals)

- `sprk_communication`: the only table read, row-filtered.
- `sprk_communicationthread`: carries the thread name, which may be Private.
- `sprk_communicationattachment`: attachment metadata and text.
- `sprk_communicationparticipant`
- `sprk_communicationaccount`
- `sprk_communicationrule`
- `sprk_communicationchannelref`

The whole prefix is treated as the family. No table qualified for the "no content and no Private-thread name" exception, because threads carry the name and attachments and participants carry content and identities. A future family table is refused by default.

### Communication content OUTSIDE the prefix (review round 1, F1)

Found by grepping `Services/Communication`, `Services/Office`, `Services/Email` and `Workers` for every table they create or write (`new Entity(...)`, `new DataverseEntity(...)`, entity-name constants):

| Table | What it holds (evidence) | Disposition |
|---|---|---|
| `sprk_document` with `_sprk_relatedcommunication_value` set | The archived .eml (`sprk_isemailarchive = true`; subject in `sprk_documentname` / `sprk_emailsubject`, `sprk_emailfrom`, `sprk_emailto`, AI `sprk_filesummary`) and attachment documents. Writers: `IncomingCommunicationProcessor` (~:1122, ~:1341), `CommunicationService` (~:519, ~:2725, ~:3016), `MessageAttachmentMaterializer` (~:276), `OfficeDocumentPersistence` (~:336, ~:832). | **Row-filtered** by the related communication's rule. |
| `sprk_emailartifact` | Outlook add-in save: name = subject + date, sender, recipients JSON, `sprk_bodypreview` (first 2,000 body chars). `UploadFinalizationWorker` ~:754. | **Refused.** |
| `sprk_attachmentartifact` | The same save's attachment records: original file names, content type, size. `UploadFinalizationWorker` ~:782. Found by the grep (not in the review list). | **Refused** (file names of an internal-only email are content, as for `sprk_communicationattachment`). |
| `sprk_emailreviewlog` | AI suggestions (`sprk_aisuggestion`) and source refs derived from the email; carries a `sprk_communication` lookup. `CommunicationEnrichmentService` ~:1247, `CommunicationCreateTaskApplyService`. | **Refused** (simpler than a per-row link filter, as the brief prefers). |
| `sprk_affinity` | Per-tenant learning rows: full sender address, sender domain, **subject keywords** and participant sets (from + to + cc) of filed email, mapped to a record. `Services/Communication/Engine/AffinityStore.cs` (:54, :188). No communication link. Found by the grep. | **Refused.** |
| `sprk_event` (task created from an email) | Name/description the user confirms in the "create task from email" flow. `CommunicationCreateTaskApplyService` ~:386. | **Not refused**: a user-authored record created by an explicit action, and a core table refusal would break normal use. Known limit. |
| `sprk_todo` (Outlook add-in "create To Do") | Name/description the user types, optional `sprk_regardingcommunication`. `OfficeService.CreateTodoAsync` ~:2107. | **Not refused**: user-authored by explicit action. Known limit. |

Not in the grep scope but the same class (reported, not changed here): an AI analysis run on an archived .eml document (`sprk_analysis`, with its `sprk_analysisoutput` rows) holds output derived from that email. `sprk_analysis` carries `sprk_documentid` and `sprk_regardingcommunication` lookups (solution `Entities/sprk_analysis/Entity.xml`), so a row filter like the document one is possible; it is created by an explicit analysis run. Left for the main session to file. (`sprk_analysisemailmetadata` holds the recipients of an analysis the user mails out, not inbound email content, and has no BFF writer.)

### Chat tools that can read them through a generic path

| Tool | Path | Status |
|---|---|---|
| `dataverse.read_query` | model SQL → OData under OBO | **Fixed** (this task). |
| `dataverse.search_data` | Dataverse Search under OBO | **Fixed** (this task). |
| `dataverse.describe` `tables/{t}/records/{id}` | full-record GET under OBO; `search_data`'s own description sends the model here | **Fixed in scope** (same pattern). |
| `dataverse.describe` `tables/`, `tables/{t}` | metadata only | Unchanged (no row content). |
| `email.draft` / summarize-thread (`EmailDraftToolHandler`) | already runs the task 065 visibility read first (round 6 F1) | Already covered. |
| `spaarke.grid_overview` (`GridOverviewHandler`) | runs a saved `sprk_gridconfiguration` FetchXML by `configId` under OBO. Its published catalog includes **Communications/Messages = `e1826c4c-9575-f111-ab0e-7ced8ddc4a05`** | **NOT covered: a different pattern** (saved FetchXML, not model SQL). An external caller can list that grid's columns for internal-only and Private-thread rows. Needs a FetchXML-side guard or a refusal of communication-target configs for external callers. **Filed: ISS-014 (#2125).** |
| `dataverse.update_record` | a write, not a read; it returns no row content | Not a read gap. Note: an external user with write privilege could flip `sprk_isinternalonly` on a row they can already read. This is a write-path or Dataverse-security question for the real fix. |
| `dataverse.create_record` / `delete_record` | writes (create returns only the created row) | No read gap. |
| `DailyBriefingOverviewHandler`, `SendWorkspaceArtifactHandler` | composed services, not generic Dataverse reads | Not inspected in depth (no generic table path). |

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/ --no-incremental`: **0 errors, 0 warnings**.
- The four handler classes (`DataverseReadQueryHandlerTests`, `DataverseSearchDataHandlerTests`, `DataverseDescribeHandlerTests`, `DataverseToolNameFreezeTests`): **103/103**. Of these, 28 are the new ISS-010 cases.
- The brief's filter (`Dataverse | CommunicationThreadRead | CommunicationOpenThreadGate | ToolFramework | CallerSystemUser`): **1299/1299**.
- All of `Services.Ai.Handlers` plus the DI-named classes: **818/818**.
- `Spaarke.ArchTests`: **1152/1152**. `RecordOwnerAssignmentCensusTests` needed no change: it classifies run-as-user POST/PATCH, and this task adds only GETs. The search handler's single POST shape is unchanged.
- Failing first: the handler constructors changed, so the old handlers cannot compile against the new tests. The seeding proofs below stand in for "fails before". Each guard was disabled on the committed code and the matching tests failed.
- Internal byte-identity: the internal-caller tests assert literal URLs equal to the pre-078 translation, on a communication table and on a non-communication table. The non-guard code path in `read_query` is textually unchanged (the guard is a new first branch). Describe's internal record path asserts the exact two requests.

**Review round 1 re-run (commit `8012c9341`).** `dotnet build src/server/api/Sprk.Bff.Api/ --no-incremental`: 0 errors, 0 warnings. The four handler classes: **142/142** (39 new cases). The brief's filter (the four handler classes + `CommunicationOpenThreadGate | CallerSystemUserResolver | ToolFramework | DiGraphValidation`): **188/188**. `Services.Ai.Handlers` + `CommunicationThreadRead`: **856/856**. `Spaarke.ArchTests`: **1152/1152**. Publish-size delta not re-measured: the round adds no file, package or registration (code in four existing files).

### Seeding proofs (each restored with `git checkout`; runs over the three handler classes, 88 tests)

| # | Guard disabled | Failed |
|---|---|---|
| S1 | `ApplyFilter` returns the model filter (no clause) | 2: `…WithOr_GuardIsAndedAroundWholePredicate`, `…HidesRowsWhoseThreadIsNotOpen` |
| S2 | read_query Open-thread post-filter skipped | 2: `…HidesRowsWhoseThreadIsNotOpen`, `Telemetry_ExternalCommunicationGuard…` |
| S3 | read_query other-family refusal skipped | 5: the refusal theory (3 cases), `…NoRequestPrincipal_TreatedAsExternal`, `…CallerSystemUserUnresolved_TreatedAsExternal` |
| S4 | search_data family-hit drop skipped | 5: unscoped, scoped, H8-retry, unresolved, telemetry |
| S5 | unresolved caller mapped to internal | 4: read_query ×2, search_data ×1, describe ×1 (all the unresolved tests) |
| S6 | search_data filter-entry removal skipped | 2: `…ScopeNamesOnlyCommunicationFamily_NoSearchCallEmptyResult`, `…ScopeNamesCommunication_FilterEntryRemovedAndHitsDropped` |
| S7 | describe record-visibility check skipped | 5: the hidden-record theory (4 cases), `…UnresolvedCallerOnCommunicationRecord_TreatedAsExternal` |
| S8 | thread-read fault returns no rows instead of throwing | 2: read_query and describe `…ThreadPrivacyReadFails…` |

Review round 1 (on commit `8012c9341`; the same three classes, now 127 tests; each mutation applied to the committed file, the suite run, the file restored, `git status` clean afterwards):

| # | Guard disabled | Failed |
|---|---|---|
| S9 | `RefusedContentTables` ignored by `IsCommunicationFamily` | 13: the content-table theories in read_query (4), search_data (4), describe (4), and `…ScopeNamesOnlyContentTable_NoSearchCall` |
| S10 | `lookup-failed` treated as external (no throw) | 4: read_query `…CallerLookupFailed…` and `…RealResolverLookupFault…`, search_data and describe `…CallerLookupFailed…` |
| S11 | K1 navigation clause dropped from `FilterClause` | 2: read_query `…WithOr_GuardIsAndedAroundWholePredicate`, `…HidesRowsWhoseThreadIsNotOpen` |
| S12 | read_query per-row `IsExplicitlyShareable` re-check skipped | 1: `…RowWhoseFlagIsNotFalseHiddenAfterRead` |
| S13 | read_query document post-filter skipped | 1: `…OnDocument_HidesDocumentsWhoseRelatedCommunicationFailsRule` |
| S14 | read_query injected `_sprk_relatedcommunication_value` not stripped | 1: `…OnDocument_HidesDocumentsWhoseRelatedCommunicationFailsRule` |
| S15 | search_data: a document the document read did not return treated as visible | 1: `…DocumentHits_DropsDocumentsWhoseRelatedCommunicationFailsRule` |
| S16 | search_data document drop skipped | 1: `…DocumentHits_DropsDocumentsWhoseRelatedCommunicationFailsRule` |
| S17 | describe document check skipped | 7: the hidden-document theory (5 cases), `…DocumentRelatedReadFails…`, `…DocumentWithShareableRelatedCommunication…` (no communication read made) |
| S18 | describe guarded 404 not normalised | 1: `…ExternalCallerMissingRecord_AnswersExactlyLikeHiddenRecord` |
| S19 | related communication's flag check skipped in `ReadVisibleCommunicationIdsAsync` | 4: describe hidden-document theory (2 cases: flag true, flag null), read_query and search_data document tests |

**Failing first (round 1).** With the round-1 guard file and the three handler files reverted to `06b867a6b`, 15 of the 127 tests fail: all the document tests that hide or read (read_query 2, search_data 2, describe 7 cases), the K1/K4 URL and flag tests (read_query 3) and the describe missing-record test. The content-table and F2 tests pass in that set-up because those behaviours live in the guard file itself; S9 and S10 prove them.

### Logs (ADR-015)

Every new log line is `[dataverse.<tool>] sdap.ai.tool.external_communication_guard entity=… reason=…` (plus `decisionId` where the handler has it). The reason codes are `family_table_refused`, `thread_not_open`, `family_filter_removed`, `family_hits_dropped`, `record_hidden` and (round 1) `related_communication_hidden` (entity `sprk_document`). The entity is a translator-validated logical name, or `sprk_communication*`. No line carries query text, a filter, a body or row content. The read_query and search_data telemetry tests assert the guard line and run `AssertTelemetryRespectsAdr015` with a sensitive literal, row value and highlight.

### Not run

- The full BFF unit suite was not run; only the filters above.
- No live change (owner constraint).

## Placement (CLAUDE.md §10)

An inbound tool guard inside the existing AI chat handlers (`Services/Ai/Handlers`, ADR-013 in-zone), with one internal static helper beside the other `dataverse.*` helpers. No package, no endpoint, no new interface (ADR-010), no setting. The handlers consume the Communication and Identity services, as `EmailDraftToolHandler` already does.

**Publish-size delta** (Release publish, Compress-Archive): master `2b07480ab` = 192 files, 39,281,295 B; branch `a33c17652` merged with that master (`0f218f8ce`, local only) = 192 files, 39,286,107 B. **Delta +4,812 B**, no new file. `dotnet list package --vulnerable`: no vulnerable packages. (A first run on a stale non-git folder was discarded.)

## §11 answers

1. **Existing.** The row rule (`CommunicationInternalOnlyFloor`, `CommunicationOpenThreadGate`) and caller resolution (`ICallerSystemUserResolver`, `ISystemUserIdentityResolver`). All are reused unchanged; no name is redefined.
2. **Extension.** Those components read through the communication services' own query seams. The generic tools hold only an `IDataverseUserClient` and build OData from model SQL. The family predicate, the clause and the user-client thread delegate that three handlers share therefore sit in one internal static helper, not in three copies.
3. **Cost of doing nothing.** A licensed user flagged external reads internal-only email and message bodies through the assistant (ISS-010), which the owner decided must not happen.

## Known limits

- **The model-driven app and the direct Web API remain open.** The real fix is the owner/UAC Dataverse security decision (ISS-010 stays open for it).
- **`spaarke.grid_overview` over the Communications/Messages grid is not covered** (inventory above). Filed as ISS-014 (#2125).
- **Fewer rows than TOP, `sprk_document` only.** On `sprk_communication` the whole rule now rides in the query (round 1, K1: `sprk_CommunicationThread/sprk_privacystate eq 100000000`, navigation property and Open value confirmed from `src/dataverse/solutions/SpaarkeMaster/Other/Relationships/sprk_CommunicationThread.xml` and the `sprk_privacystate` option set in `Entities/sprk_CommunicationThread/Entity.xml`), so TOP counts only visible rows; the post-read flag and thread checks stay as defence in depth. On `sprk_document` the related-communication check is a post-filter, so a page can return fewer rows than `TOP` while the row-cap warning keys off the returned count. The K1 clause has not been exercised against live Dataverse (no live change in this task); a live smoke of an external caller's read_query on `sprk_communication` belongs to the deploy round.
- **search_data counts.** The Search API's own `top` (25) is applied before the drops (family hits and hidden documents), so an external caller can see fewer than 25 hits. By design, nothing says so.
- **Derived, user-authored records.** `sprk_event` (task created from an email) and `sprk_todo` (Outlook "create To Do") carry what the user typed or confirmed and are not refused. An AI analysis of an archived .eml (`sprk_analysis` / `sprk_analysisoutput`) is not covered; reported to the main session to file (see the inventory).
- **RAG retrieval and chat archive ingest** can surface communication content through other paths; filed separately by the main session.
- **Parenthesis safety** rests on the translator. It emits only balanced parentheses: a stray `)` at top level fails with "Unexpected token", so `ApplyFilter`'s wrapping cannot be escaped.
- **Externality cache.** `IsExternalAsync` caches for 10 minutes, so a newly flagged user stays internal for up to 10 minutes (the existing limit on `ISystemUserIdentityResolver`).
- **Write paths.** `dataverse.update_record` (and other write tools) can change `sprk_isinternalonly` or the thread lookup on a row the user can write. Out of scope per the POML (no change to any other tool); filed by the main session.

## Review round 1

Two adversarial passes (pass 1: F1, K1, K1, K4, K4; pass 2: F4, F2, F2, K4). The main session consolidated them into the fix brief's items below; the brief does not record which pass label maps to which item, so none is claimed here. Fixed in commit `8012c9341` unless stated.

| Brief item | Finding | Disposition |
|---|---|---|
| F1 | Internal-only email content copied outside `sprk_communication*`: `sprk_document` .eml archive and attachments, `sprk_emailartifact`, `sprk_emailreviewlog` | **Fixed.** Documents row-filtered by the related communication in all three tools; the content tables refused through the one family predicate; the grep added `sprk_attachmentartifact` and `sprk_affinity` (inventory). |
| F2a | `lookup-failed` (a Dataverse or cache fault the resolver swallows) was treated as external, so a fault decided who the caller is | **Fixed.** `IsExternalCallerAsync` throws on `lookup-failed`; the tool returns an error with no data. No principal, no oid and no active systemuser stay "external" (fail closed), as the POML requires. |
| F2b | No test proved that a throwing externality read leaves no data | **Fixed.** One test per handler (`…ExternalityReadThrows_ErrorWithNoData`; search_data returns a communication hit so the lazy check runs), plus `lookup-failed` tests per handler and one against the real `CallerSystemUserResolver` that pins the reason string. |
| K1 | TOP oracle: the Open-thread half was a post-filter, so TOP and the row count depended on hidden rows | **Fixed** for `sprk_communication`: the navigation-property clause rides in the query (navigation property and Open value confirmed from solution XML); the post-read check stays. Remains a known limit for `sprk_document` and search_data counts. |
| K4a | describe: a hidden record's answer differed from a real not-found (Dataverse's own 404 text), and the comment and test claimed otherwise | **Fixed.** A guarded 404 and a hidden record share `DATAVERSE_NOT_FOUND` + `RecordNotVisibleMessage`; the comment and the test's `because:` are now true, and a test compares the two answers. |
| K4b | read_query relied on the query filter alone for the flag | **Fixed.** `sprk_isinternalonly` injected (stripped if injected) and `IsExplicitlyShareable` required per row. |

## Re-check of review round 1 (scoped, Sonnet adversarial-reviewer, `06b867a6b..7d23af15b`)

No F1, no fail-open. Fixed in the main session (this commit):

| Finding | Disposition |
|---|---|
| F2: no test crossed the 50-id chunk boundary of the batched communication read (`read_query` allows TOP 200) | **Fixed.** `…RelatedCommunicationsSpanTwoChunks_RuleAppliedInEachChunk`: 60 documents, two communication reads (`$top=50`, `$top=10`), and the rule holds on both sides of the boundary. |
| K2: a related-communication link that is present but not a usable GUID read as "no related communication" (visible) | **Hardened.** `LinkOf` maps null or absent to empty (no link), and any other unusable value to `UnreadableLink` (`Guid.AllBitsSet`). That value is never sent to Dataverse and never visible, so the document stays hidden. This applies to `read_query`, `describe` and `search_data` (`ReadRelatedCommunicationIdsAsync`). Tests: the `read_query` theory (not a GUID, the empty GUID, a number) and one `search_data` test. **Seeding proof S20:** mapping the unusable case back to empty fails all 4. |
| K1: `sprk_document` TOP and count can reveal that hidden documents exist (no content) | Known limit (above). |
| K3: Outlook add-in saves write `sprk_emailbody`, `sprk_emailfrom`, `sprk_emailto` and `sprk_emailsubject` onto `sprk_document` with no `sprk_relatedcommunication` (`UploadFinalizationWorker.cs:681`, `OfficeDocumentPersistence.cs:323`), so those rows stay visible | Known limit. It follows the stated rule (no related communication, so the document is unaffected). It is the same owner question as ISS-015: a document deliberately saved to a record. |
| K4: the K1 navigation clause has not run against live Dataverse | **Deploy checklist:** run one external-caller `read_query` on `sprk_communication` after the next dev BFF deploy. A wrong name would fail closed (400, no data). |
| K5: `describe` normalises only the NotFound error; a native 403 text passes through | Known limit; the caller cannot read the record either way. |

Availability note: internal callers on `sprk_document` now resolve externality (two cached lookups). A lookup fault now fails those calls with no data.

Verification after the fix: `dotnet build` 0 warnings, 0 errors. Handler classes, `CommunicationOpenThreadGate`, `CallerSystemUserResolver` and `DiGraphValidation`: **192/192**.

## Quality gates (task-execute Step 9.5)

- **Code review:** covered by two Opus adversarial passes (pass 1 on bypasses, pass 2 on behaviour, DI, tests and inventory) and one scoped Sonnet re-check of the fix diff (CLAUDE.md §8.5: two passes for security). No F-class finding is open.
- **ADR check** (diff `ef87f6edc~1..27f33f7d3`, src):
  - ADR-001: no endpoint.
  - ADR-002: no plugin; security fails closed.
  - ADR-007: no Graph.
  - ADR-009: no `IMemoryCache`; the existing distributed-cache resolvers are reused.
  - ADR-010: no new interface; one internal static helper; `TryAdd` registrations, justified above.
  - ADR-013: the AI handlers consume Communication and Identity services, the allowed direction.
  - ADR-015: entity and reason code only, asserted by `AssertTelemetryRespectsAdr015`.
  - ADR-028: no credential, scheme or OBO change; the principal comes from `HttpContext`.
  - BFF hygiene §10: placement stated, +4,812 B, no package, no vulnerable package, tests updated.
  - CLAUDE.md §11 answers are above. **Compliant; no violation, no warning.**
- **Live:** no change in this task. It ships with the next owner-approved dev BFF deploy, together with the K1 smoke (deploy checklist).

## Dev deploy (2026-10-20, owner approval "yes bff deploy")

- **Candidate** `21184ecd4` = R3 (078 at `c935c70e1`) merged with master `2b07480ab` (UAC #2110).
  - The previous live `00aba6d99` is an ancestor, and so is 077.
  - It does not contain ontology 042c (that is on the ontology docs branch, not master) or UAC #2122 (draft).
- **Coordination:** OK from UAC, ontology, ebilling, provisioning, field-mapping, word-add-in and spe-admin.
- **Tests:**
  - ArchTests 1152/1152.
  - Unit filter (Dataverse, Communication, ExternalAccess, ToolFramework, DiGraph, Workforce, Registration): 6758/6758 passed (3 skipped) on a re-run. The first run had 1 intermittent failure that did not repeat.
- **Rollback snapshot** (Kudu zip of the live site, reads `00aba6d99`): `deploy/rollback/wwwroot-20261020T163210Z`.
- **Deploy:** `scripts/Deploy-BffApi.ps1` (direct), 16:32:41–16:35:10Z. Package 37.47 MB; 4 critical files SHA-256 verified; healthz and CORS passed.
- **Read-back:**
  - Kudu DLL ProductVersion `1.0.0+21184ecd4789c2124c7d142237ba4dce0d8d5b6e` (16:35:29Z);
  - `/healthz` 200, `/healthz/dataverse` 200;
  - `/api/me` anonymous 401, with a v2 token 200.
- **K1 live smoke (read-only, own token):**
  - `sprk_communications?$filter=sprk_isinternalonly eq false and _sprk_communicationthread_value ne null and sprk_CommunicationThread/sprk_privacystate eq 100000000&$top=3` → 200, 3 rows, so the navigation clause is valid on live Dataverse.
  - Private-state control → 200, 0 rows.
- Ontology smoke passed on the new build.
- **Rollback:** `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261020T163210Z` (= `00aba6d99`, which contains 077).

## Dev redeploy from master c3ec5d6e5 (2026-10-20, owner "approved bff deploy"; requested by UAC)

- **Candidate** master `c3ec5d6e5`. It adds UAC #2138 (#2128, guest auto-create retired) and #2133 (#2130/#2131, PII logs and census) to the live `21184ecd4`, which is an ancestor. Both PRs' CI was green with nothing failing.
- **Coordination:** OK from UAC (requester), ontology, ebilling (`sprk_invoicestatus` still exists; `e0df77339` is not in the candidate), provisioning, field-mapping, word-add-in and spe-admin. The SPA precondition for #2138 was met (dev SPA `28274e04e`). No data step was needed (UAC: the 3 guest-bound contacts are licensed guests).
- **Pre-checks:** the live DLL read `21184ecd4`; the last Kudu deployment was this project's 16:34Z deploy; healthz 200.
- **Rollback snapshot:** `deploy/rollback/wwwroot-20261020T180912Z` (222 files, DLL `21184ecd4`).
- **Deploy:** `scripts/Deploy-BffApi.ps1` from a detached worktree `C:\wtbffc` at `c3ec5d6e5`. Package 37.47 MB; 4 critical files SHA-256 verified.
- **Read-back** (18:12:51Z): ProductVersion `1.0.0+c3ec5d6e56f0074cf238b84bccf202cb1c0c84b7`; `/healthz` 200; `/healthz/dataverse` 200; `/api/me` anonymous 401.
- **Rollback:** `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261020T180912Z` (= `21184ecd4`).

## Dev redeploy from master 0b976628e (2026-10-20, owner "yes deploy"; requested by UAC for #2121)

- **Candidate:** master `0b976628e` = the live `c3ec5d6e5` + UAC #2122 (#2121: Secure suppresses a systemuser's lookup-derived membership on the SPA/Teams plane). #2122 CI was green, with nothing failing or pending. The ebilling content check matched the live build.
- **Coordination:** OK from ontology, provisioning, field-mapping, word-add-in and spe-admin. ebilling did not answer; per the owner's 2026-10-16 rule a session that does not answer does not block, and its content was unchanged.
- **Pre-checks:** the live DLL read `c3ec5d6e5`, which is an ancestor of the candidate; healthz 200.
- **Rollback snapshot:** `deploy/rollback/wwwroot-20261020T203441Z` (222 files, DLL `c3ec5d6e5`).
- **Deploy:** `scripts/Deploy-BffApi.ps1` from a detached worktree `C:\wtbffd`. Package 37.48 MB; 4 critical files SHA-256 verified.
- **Read-back** (20:37:26Z): ProductVersion `1.0.0+0b976628e48b0082c6235a40e9e3508603f5cd81`; `/healthz` and `/healthz/dataverse` 200; `/api/me` anonymous 401.
- **Rollback:** `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261020T203441Z` (= `c3ec5d6e5`).

## Dev redeploy from master 4f5f22694 (2026-10-20, owner "deploy approved"; UAC #2078 / task 036, flag OFF)

- **Candidate:** master `4f5f22694` = the live `0b976628e` + UAC #2078 (FR-20 flag-gated swap; 15 server files, including the shared `Spaarke.Dataverse/DataverseWebApiService.cs` +163, which only adds surface per spe-admin and ontology). #2078 CI was green.
- **Flag:** `ExternalAccess__ImpersonatedRootSets__Enabled` is absent on dev (= OFF), re-checked right before the deploy. The deploy changes no app settings.
- **Coordination:** OK from word-add-in, spe-admin, ontology and ebilling (`sprk_invoicestatus` exists; `e0df77339` not included), plus the standing OKs from provisioning and field-mapping.
- **Pre-checks:** the live DLL read `0b976628e`, which is an ancestor; healthz 200. A `git fetch` failed once on DNS, so the local commit was used (it was already verified on origin/master).
- **Rollback snapshot:** `deploy/rollback/wwwroot-20261020T233210Z` (222 files, DLL `0b976628e`).
- **Deploy:** `Deploy-BffApi.ps1` from a detached worktree `C:/wtbffe`. Package 37.49 MB; 4 critical files SHA-256 verified.
- **Read-back** (23:34:36Z): ProductVersion `1.0.0+4f5f22694c2965f4f0d6c78f9f322d3d5dfe1775`; `/healthz` and `/healthz/dataverse` 200; `/api/me` anonymous 401.
- **Rollback:** `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261020T233210Z` (= `0b976628e`).
- **Next (UAC):** a flag-off sanity check and the 034 canary. The flip needs a direct owner yes in this session, with the command shown first.

## UAC 036 flag ON on dev (2026-10-20 23:51Z, owner: "yes can turn on. i ran the command you provided")

- **Preconditions:**
  - UAC tasks 142 and 143 are in the live build (`d254d7166` is an ancestor of `4f5f22694`, verified here).
  - The 034 canary passed on `4f5f22694`: impersonated 58 ⊂ app-only 62, strictly fewer, and equal to the role-derived expected set; 2×20/20.
  - `prvActOnBehalfOfAnotherUser` is Global on both BFF app users (UAC read live).
  - UAC's flag-off sanity check was clean.
- **Change** (run by the owner): `az webapp config appsettings set -g rg-spaarke-dev -n spaarke-bff-dev --settings ExternalAccess__ImpersonatedRootSets__Enabled=true`.
- **Read-back:** the setting = `true`; the site was modified at 23:51:26Z. At 23:52:36Z: ProductVersion `4f5f22694` (unchanged), `/healthz` and `/healthz/dataverse` 200, `/api/me` anonymous 401.
- **Rollback:** `az webapp config appsettings delete -g rg-spaarke-dev -n spaarke-bff-dev --setting-names ExternalAccess__ImpersonatedRootSets__Enabled`. Never set it to an empty value; that fails startup.
- **Next:** UAC gates (i)–(v) and the disabled/unlicensed check.

## 036 flag-on live gates (UAC, 2026-10-20 23:55Z–00:13Z)

- Result from uac-r2 (record `projects/unified-access-control-r2/notes/batch7-live-gates-2026-10-17.md`, commit 0a4fbaa38): passed, no STOP, no rollback. (i) negative canary 20/20 ×2; (vii) disabled / unlicensed / team-only users refused or limited exactly by their roles; access source `ImpersonatedRootSet` ×12, 0 `Membership`; 96 requests since the flip, 0 5xx, 0 403.
- Not run: (iii)–(vi) (dev has no fixture that avoids a write); (ii) passed server-side. UAC's note gives the owner setup + SPA steps.
- Filed by UAC, non-blocking: #2167 (privilege answer for unlicensed users lists Create; safe because asked after RPA), #2168 (disabled/unlicensed refusal reported as a fault, not 403; fails closed). #1567 closed.
- Flag stays ON. Rollback unchanged: delete `ExternalAccess__ImpersonatedRootSets__Enabled`.


## Dev BFF deploy of master a10e814d02 (2026-10-21 14:34Z; owner: "yes approved to deploy a10e814d0")

- **Requested by** UAC, for #2169 (task 189) and #2150. The build also contains 3531ed65e (#2166, SPA-only), 44063b347 (ontology 042c) and 2a38e6ce6 (field-mapping #2184).
- **Peer OKs:** ontology, field-mapping, word-add-in, sdap-spe-admin and ebilling. Provisioning has a standing OK (dev BFF only). UAC passed on the owner's "yes deploy" from its session; it was not used as approval, and the owner approved directly in this session.
- **Pre-checks:**
  - Live DLL `4f5f22694`, an ancestor of a10e814d02. healthz 200.
  - Does NOT contain e0df77339 (ebilling 015), so the `sprk_invoicestatus` delete stays blocked.
  - Pre-existing exposure, not changed by this build: #2188 (fix #2191 is in UAC review).
- **Rollback snapshot:** `deploy/rollback/wwwroot-20261021T143151Z` (222 files, DLL `4f5f22694`).
- **Deploy:** `Deploy-BffApi.ps1` from a detached worktree `C:/wtbffe-a10`. Package 37.56 MB; 4 critical files SHA-256 verified; CORS OK.
- **Read-back** (14:35:33Z):
  - ProductVersion `1.0.0+a10e814d0200c4f20019647a17074219bb8161f9`.
  - `/healthz` and `/healthz/dataverse` 200; `/api/me` anonymous 401.
  - App settings unchanged (036 ON).
- **secure-child-reconciliation:**
  - Run 14:36:00–14:37:58Z: recordsWritten 279, failed 0, sweep incomplete (1000 rows).
  - Run 14:38:00–14:38:43Z: the first complete sweep (66 rows). changed 0, failed 0, writeCapReached false, carriedRows 0, tablesWithoutColumn [], failure null.
  - #2161 can start no earlier than 15:48:43Z.
- **Exceptions after the deploy** (spe-insights-dev-67e2xz) at 14:43Z: no new exception class.
  - BadImageFormatException: 2 at the 14:35 restart. One problemId was seen at earlier deploys.
  - Graph ODataError "Invalid hostname for this tenancy": existing background noise.
- **Rollback:** `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261021T143151Z` (= `4f5f22694`). The 059 floor still holds; 4f5f22694 contains 077.
- **Final 30-minute reading** (15:05:38Z): no new exception class; the same three groups (ODataError n=7, noise). Reconciliation runs from 14:58 to 15:04Z succeeded with nothing to do. Sent to UAC; tasks 189 and #2143 close on it. Ontology smoke PASS. SPE admin verified the live version.

## Dev BFF deploy of master 84291ecea8 (2026-10-21 16:41Z; owner: "yes, deploy 84291ecea8")

- **Requested by** UAC for #2161. The build also contains #2177 (word-add-in task 130) and #2205 (SPE admin #2032; the owner had said it could wait, but it sits below #2161 on master).
- **Peer OKs:** ebilling, word-add-in, ontology, SPE admin and field-mapping. Provisioning has a standing OK. UAC passed on the owner's "yes" from its session; it was not used, and the owner approved directly here.
- **Pre-checks:**
  - Live DLL `a10e814d02`, an ancestor. healthz 200.
  - `sprk_document.sprk_accessinheritance` present (Memo), which #2177 needs.
  - No e0df77339 (ebilling 015 stays blocked).
  - #2188 is still open on dev (#2191 not merged).
- **Rollback snapshot:** `deploy/rollback/wwwroot-20261021T164028Z` (222 files, DLL `a10e814d02`).
- **Deploy:** `Deploy-BffApi.ps1` from detached `C:/wtbffe-842`. Package 37.61 MB; 4 critical files SHA-256 verified; CORS OK. No settings changed.
- **Read-back** (16:42:49Z): `1.0.0+84291ecea8fa9c5148df04ae5ede2f185301a19e`. healthz and healthz/dataverse 200; `/api/me` anonymous 401.
- **Reconciliation, 16:44–17:12Z:**
  - Every run Succeeded.
  - Re-file pass: listed and examined 0, heldIsolated, terminal and failed 0, windowDrained true, watermarkHeld false. No re-file has happened yet, so the move logic hasn't been exercised live.
  - Access sweep complete at 16:46:08Z, nothing written.
- **Exceptions to 17:12Z:** no new class. Only the Graph "Invalid hostname" noise (6, ISS-006). There were no restart BadImageFormat errors this time.
  - T8 stager refusals: 0.
  - DocumentRelocationReleaseRetry log lines: none.
- **Ontology:** smoke PASS. SPE admin and ebilling were told the version.
- **Open:** whether the owner does UAC gate item 4 (two .eml saves, read back by word-add-in) or it relies on the first real saves.
- **Rollback:** `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261021T164028Z` (= `a10e814d02`).

## Dev BFF deploy of master 583f90c1d9 (2026-10-21 18:03Z; owner: "yes, deploy 583f90c1d9")

- **Contents:** #2191 (the field-mapping fix for the #2188 Office quick-create source-read leak; the source is now read as the caller), #2182 (word-add-in task 131, "email not filed" notice) and word-add-in docs (#2210, #2211).
- **Peer OKs:** field-mapping, word-add-in, UAC, ebilling, ontology and SPE admin. Provisioning has a standing OK.
- **Pre-checks:** live DLL `84291ecea8`, an ancestor. No e0df77339.
- **Rollback snapshot:** `deploy/rollback/wwwroot-20261021T180216Z` (222 files, DLL `84291ecea8`).
- **Deploy:** detached `C:/wtbffe-583`. Package 37.62 MB; 4 critical files SHA-256 verified; CORS OK. No settings changed.
- **Read-back** (18:04:37Z): `1.0.0+583f90c1d91198f810b18d60c86f86c130c751d1`. healthz and healthz/dataverse 200; `/api/me` anonymous 401.
- **30 minutes** (to 18:34Z): no new exception class (only the Graph "Invalid hostname" noise ×6). T8 refusals 0; #2191 refusal codes 0, because no Office quick-create has run yet. Reconciliation runs 18:16–18:34Z Succeeded. Ontology smoke PASS.
- **Rollback:** `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261021T180216Z` (= `84291ecea8`).

## Dev BFF deploy of master f1b3dadcf5 (2026-10-21 21:44Z; owner: "yes, deploy f1b3dadcf5")

- **Contents:** UAC task 188, D7 T7 (#2170: one predicate narrows external document reads; this is the fix for ISS-015 together with word-add-in T8 #2177, live since 84291ecea8). Slice D (#2186: document links Delete=Restrict; a parent's delete is refused with 409 `document_has_child_documents`). Field-mapping #2214 (inert on dev).
- **Peer OKs:** UAC, field-mapping, ontology, SPE admin, ebilling and word-add-in. Provisioning has a standing OK. No dev prerequisites (UAC).
- **Effect UAC measured:** 11 partner-reachable documents disappear for partners, all own-Restricted, all on matter b68299c6. Internal users still see them.
- **Partner baseline:** no one can sign in as the partner contacts, R3 or UAC. UAC recorded the pre-swap state from Dataverse data instead (grants and reachable documents on b68299c6 and 86335ce3).
- **Pre-checks:** live DLL `583f90c1d9`, an ancestor. No e0df77339.
- **Rollback snapshot:** `deploy/rollback/wwwroot-20261021T214302Z` (222 files, DLL `583f90c1d9`).
- **Deploy:** detached `C:/wtbffe-f1b`. Package 37.68 MB; 4 critical files SHA-256 verified; CORS OK. No settings changed.
- **Read-back** (21:45:37Z): `1.0.0+f1b3dadcf59fae5ff5142ac670a3860391c57c46`. healthz and healthz/dataverse 200; `/api/me` anonymous 401.
- **UAC's post-swap steps** (owner-approved in UAC's session), all verified:
  - `Set-DocumentLinkDeleteRestrict.ps1 -Apply`.
  - `Set-DocumentNarrowingFieldSecurity.ps1 -Apply` (columns masked about 17 s each around 21:5xZ; BFF reads are app-only).
  - `sprk_DocumentOperations.js` 1.28.2 published.
- **30 minutes (to 22:16Z):**
  - No new exception class (only the Graph "Invalid hostname" noise ×7).
  - No narrowing exceptions; 0x80040227 / `document_has_child_documents` 0; no 409s or 403s; no field-security errors.
  - Reconciliation runs Succeeded.
  - Ontology smoke PASS.
- **Not exercised:** no partner document reads and no document creates in the window. D7 narrowing and the BFF-writer path under the new field security are unproven live until real traffic, or the owner's optional partner check (a contact 8e9918a9/2e419a4f sign-in: b68299c6 lists empty, 86335ce3 lists its 3, aadbb740 download refused as not-under-parent).
- **Rollback:** `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261021T214302Z` (= `583f90c1d9`). Rolling back the BFF does NOT undo UAC's Restrict, field-security or web-resource changes; those are UAC's to reverse.

## Dev BFF deploy of master a7b956fb55 (2026-10-22 02:17Z; owner: "yes go ahead", in reply to the a7b956fb55 request)

- **Contents:**
  - #2223: field-mapping never writes the secure flag or access columns on matter/project; Office quick-create skips them with a warning. A no-op on dev, which has 0 such rules.
  - #2228 / fm-016: Repair mode, dormant.
  - #2221 / UAC task 190: `email-archive-communication-link-sweep`, registered disabled, a dry run unless `EmailArchiveLinkSweep:Apply` (not set).
- **Peer OKs:** UAC (requested it), ebilling, SPE admin, field-mapping, word-add-in and ontology.
- **Pre-checks:** live DLL `f1b3dadcf5`, an ancestor. No e0df77339. No external-access files changed.
- **Rollback snapshot:** `deploy/rollback/wwwroot-20261022T021635Z` (222 files, DLL `f1b3dadcf5`).
- **Deploy:** `Deploy-BffApi.ps1 -Environment dev` from detached `C:/wtbffe-a7b`. Package 37.71 MB; 4 critical files SHA-256 verified; CORS OK. No settings changed.
- **Read-back** (02:18:52Z):
  - `1.0.0+a7b956fb55d682e679cda007bea258a53bb76aa5`.
  - healthz and healthz/dataverse 200; `/api/me` anonymous 401.
  - Sweep job enabled=False, cron `45 3 * * *`, 0 runs.
- **UAC task 190 gate:** PASS. The sweep dry run at 02:21Z found 120 examined, 29 would link, 0 written, 0 failed. The container-migration report found 0 newly refused. Apply stays an owner item. Ontology smoke PASS.
- **30 minutes** (to 02:48Z): no new exception class (Graph noise ×6); all zero counts; reconciliation runs Succeeded.
- **Owner decision (UAC round 122), "the access should match the 'parent'":** a linked own-Restricted email archive follows its communication. UAC builds it (a provisional own-narrowing marker); word-add-in reviews. Known limit: a single attachment save stays Restricted.
- **Rollback:** `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261022T021635Z` (= `f1b3dadcf5`).

## Dev BFF deploy of master 1283c41817 (2026-10-22 12:49Z; owner: "yes, deploy 1283c41817")

- **Contents since a7b956fb55:**
  - UAC #2234 (round 122): a provisional own-Restricted email document is released to its communication by the reconciler.
  - UAC #2239: the AI profile writer is limited to the 6 ACT-011 columns.
  - Field-mapping 018a: dormant.
  - UAC #2244 and #2249: the app-only refusal covers BFF-owned document, owner and creator columns.
  - UAC #2248: archive verifies the attachment item before binding.
  - The candidate moved 6b3f35c79f → fa1de47276 → 1283c41817; each move was re-confirmed by the peers.
- **ExternalDocumentNarrowing.cs refactor (#2234):** UAC confirmed D7's decision is unchanged.
  - The test file is additions only (+172/−0).
  - R3 ran the narrowing and reconcile tests at 6b3f35c79f: 150 passed, 0 failed.
  - Later commits don't touch the narrowing.
- **Peer OKs:** UAC, ebilling, word-add-in, field-mapping (dev has no rules on the refused document, owner or creator columns), SPE admin and ontology.
- **Pre-checks:** live `a7b956fb55`, an ancestor. No e0df77339. No settings or schema change.
- **Rollback snapshot:** `deploy/rollback/wwwroot-20261022T124709Z` (222 files, DLL `a7b956fb55`).
- **Deploy:** detached `C:/wtbffe-128`. Package 37.73 MB; 4 critical files SHA-256 verified; CORS OK.
- **Read-back** (12:50:18Z): `1.0.0+1283c41817cefab38063486105d59ddd199b08b9`. healthz and healthz/dataverse 200; `/api/me` anonymous 401.
- **Ontology smoke:** PASS.
- **30 minutes (to 13:20Z):**
  - No new exception class (Graph noise ×6).
  - DOC-PROFILE warnings 0; `document_bff_column_server_only` 0; `releaseRefusedLinkColumnsNotSecured` 17 lines, all value 0.
  - No 409s; reconciliation runs Succeeded.
  - UAC's first gate reading is also clean.
  - Client redeploys (CommunicationActions, EmailPage, LegalWorkspace, Console) are UAC's, in its session.
- **Rollback:** `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261022T124709Z` (= `a7b956fb55`).

## Deploy 2026-10-22/23: dev BFF 1fa0cf5cb8 + #1830 container + external SPA (owner-approved)

**Owner approval.** Given directly in this session: "BFF deploy of 1fa0cf5cb8 is approved"; later "re-provisioning the 65a3fab2 container--yes approved", "the ISS-007 re-index--yes proceed" and "the SPA deploy - yes proceed".

**Peer OKs for 1fa0cf5cb8:**
- UAC, ontology, field-mapping, word-add-in, SPE admin;
- ebilling: a fresh OK, needed because #2258 touches InvoiceExtractionJobHandler and ontology 157 adds EarlierInvoiceReadability;
- provisioning: standing OK.

**Range 1283c41817..1fa0cf5cb8:**
- UAC #2253, #2268, #2258, #2277;
- R3 #2279 (ISS-001/003/004/007/011/012);
- field-mapping #2261, #2266, #2282;
- ontology #2233;
- CI #2252.

### BFF deploy
| Step | Result |
|---|---|
| A, live read | Live was 1283c41817 (23:56Z). Snapshot `deploy/rollback/wwwroot-20261022T235642Z` (222 files). healthz 200. |
| B, setting read | `SharePointEmbedded__OwnedContainerIds` held 2 ids, comma-separated. No slots. |
| C, setting change | Kept `b!vzGD…` only and removed `b!MVasATu…`. Read back: 1 id. Rollback: re-add the id. |
| D, deploy | `Deploy-BffApi.ps1 -Environment dev` from detached worktree `C:\wtbffe-1fa`. The script verified 4 file hashes, health and CORS. Live ProductVersion `1.0.0+1fa0cf5cb8…`, first read 2026-10-23 00:00:36Z. healthz 200, healthz/dataverse 200, anonymous /api/me 401. |
| E, server gate | UAC, window 00:00–00:30:36Z. The early read for 00:00–00:05Z was 0 exceptions, 0 "Invalid hostname", 0 deny-list refusals, 0 5xx. |

**Rollback:** `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261022T235642Z` (restores 1283c41817).

### #1830 Steps 0–2: project 65a3fab2
Run as the owner's delegated az login.

- **Step 0.** Matched the recorded state:
  - etag `W/"27392369"`;
  - container id was the dead `b!MVasATu…`;
  - owner team 6eabc7f9 (Secure Record Owners);
  - issecure true;
  - one share, to 1d02f31c with mask 262167.
- **Step 1.** PATCH container id to null with If-Match. Read back: null. New etag `W/"28862494"`.
- **Step 2.** POST provision-project returned 200 at 00:22:42Z.
  - `resumed=true`, with new container `b!euxXOoXL…`.
  - The share was not rewritten.
  - Children and filed records Completed.
- **Read-back.** The new id is held by exactly 1 project. No matter, work assignment or BU holds it. Owner and share unchanged.

### ISS-007 re-index: none needed on dev
- Read-only query: 5 documents are linked only to a service request; 2 are indexed (85585c76, cba17a34 in `spaarke-files-index`).
- None of the 4 service requests regards a matter, project or work assignment. Re-indexing would still give no parent (fail closed), so send-to-index was not run.

### External SPA: 3531ed65e → 1fa0cf5cb8
- Rollback tag `external-spa-dev-rollback-3531ed65e`; build tag `external-spa-dev-1fa0cf5cb8`.
- `gh workflow run deploy-external-spa.yml --ref external-spa-dev-1fa0cf5cb8 -f environment=dev`: run 39878122856, success.
- Verified: `/` 200, `auth-callback.html` 200. `assets/app.js` contains `hasFile` (ISS-011), `allowRemoteImages` and `contain:"paint` (ISS-003).
- **Rollback:** `gh workflow run deploy-external-spa.yml --ref external-spa-dev-rollback-3531ed65e -f environment=dev`.

## Dev BFF deploy of master 58ffc37da3 (2026-10-23 11:18Z; owner: "ok to deploy build 58ffc37da3")

Contents beyond the previous live build 1fa0cf5cb8: ontology 159/161/163 (#2303, 0317579272), word-add-in 132 (#2304) with fixes #2308 and #2309, and ontology 160 (#2313) with fix #2314. R3's `ContactBindingDecision.cs` change in #2314 is a pure extraction (`MatchingEnabledSystemUserIds`); `DecideLicensedUserEmail` keeps its guards and its result.

| Step | Result |
|---|---|
| Gates | Router and Client lint green on 58ffc37da3. OKs: UAC (after its reviews of #2304/#2308/#2309 and #2313/#2314), ontology, word-add-in, field-mapping, ebilling; provisioning standing OK. SPE admin session not running. |
| A, live read and snapshot | Live ProductVersion `1.0.0+1fa0cf5cb8…`, healthz 200. Snapshot `deploy/rollback/wwwroot-20261023T111635Z` (222 files, 148.07 MB). |
| D, deploy | `Deploy-BffApi.ps1 -Environment dev` from detached worktree `C:\wtbffe-58f`. 4 file hashes, health and CORS verified. OneDeploy ended 11:18:12Z. Read-back 11:19:20Z: ProductVersion `1.0.0+58ffc37da3…`, healthz 200, healthz/dataverse 200, anonymous /api/me 401. |
| E, server gate | R3 read 11:18–11:49Z (`gate58.ps1`): 75 requests, 0 5xx, 0 403, 0 "Invalid hostname", 0 worklist_unavailable, 0 signal_unreadable, 0 registration_owner_unconfigured. One `RedisConnectionException` (scheduler lease renew, 11:21:20Z, at startup); 15 Redis/lease warnings earlier the same day before the deploy, and scheduler traces continue to 11:50Z, so not deploy-caused. 21 deny-code log lines (`contact_bound_to_different_oid` 14, `linked_contact_bound_to_different_oid` 7) against 18 in the same 31 minutes the day before: the reconciliation job's known collisions. UAC runs its own gate. |

**Rollback:** `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261023T111635Z` (restores 1fa0cf5cb8).
