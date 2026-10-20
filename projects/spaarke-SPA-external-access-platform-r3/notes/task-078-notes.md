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
