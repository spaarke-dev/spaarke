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

**Who is external.** The request principal (`IHttpContextAccessor`) goes to `ICallerSystemUserResolver.ResolveAsync`, then to `ISystemUserIdentityResolver.IsExternalAsync`. An unresolved caller (no principal, no oid, no active systemuser, lookup failed) is **external** (fail closed). Externality is decided **only** when the request touches the communication family (a logical name starting `sprk_communication`, OrdinalIgnoreCase). Every other request is byte-identical and resolves nothing.

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

### Logs (ADR-015)

Every new log line is `[dataverse.<tool>] sdap.ai.tool.external_communication_guard entity=… reason=…` (plus `decisionId` where the handler has it). The reason codes are `family_table_refused`, `thread_not_open`, `family_filter_removed`, `family_hits_dropped` and `record_hidden`. The entity is a translator-validated logical name, or `sprk_communication*`. No line carries query text, a filter, a body or row content. The read_query and search_data telemetry tests assert the guard line and run `AssertTelemetryRespectsAdr015` with a sensitive literal, row value and highlight.

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
- **Fewer rows than TOP.** The Open-thread half is a post-filter, because thread privacy lives on another table. A page can return fewer rows than `TOP` while the row-cap warning still keys off the returned count. The flag and thread-set halves ride in the query, so TOP stays honest for them.
- **search_data counts.** The Search API's own `top` (25) is applied before the drop, so an external caller can see fewer than 25 hits. By design, nothing says so.
- **Parenthesis safety** rests on the translator. It emits only balanced parentheses: a stray `)` at top level fails with "Unexpected token", so `ApplyFilter`'s wrapping cannot be escaped.
- **Externality cache.** `IsExternalAsync` caches for 10 minutes, so a newly flagged user stays internal for up to 10 minutes (the existing limit on `ISystemUserIdentityResolver`).
- **Write paths.** `dataverse.update_record` can change `sprk_isinternalonly` on a row the user can write. This is a write-side or Dataverse-security matter, not part of this read-side quick fix.
