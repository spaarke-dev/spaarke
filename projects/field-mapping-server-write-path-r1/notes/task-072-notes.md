# Task 072 — GET /api/v1/records/{table}/{id}/family (FR-31)

Branch `task/fm-072`, cut from master `7008180304` (task 071 merged, #2442). Rigor FULL (new authenticated BFF endpoint,
route-authorization ledger, existence-equalised 404). Executed on sonnet at high effort.

## What was built

| File | What |
|---|---|
| `src/server/api/Sprk.Bff.Api/Api/Records/RecordFamilyEndpoints.cs` | New. `MapRecordFamilyEndpoints`, the handler `GetFamilyAsync`, and the wire DTOs (`RecordFamilyResponse`, `RecordFamilyRef`, `RecordFamilyMemberDto`; all `internal`). |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` | One `app.MapRecordFamilyEndpoints()` call next to `MapFieldMappingEndpoints()` (+ one `using`). |
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordFamilyReader.cs` | Small, behaviour-preserving: `FamilyTablesOf(kind)` (the set `Walk` built inline) and `TryGetFamilyEntitySet(table, kind, out entitySet)` (internal static), so the endpoint's table gate uses the reader's own notion of a family table. `Walk` now calls `FamilyTablesOf`. |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` | `GovernedFile` + `HandlerDecision` for the route; census 133 -> 134 with a MAINTENANCE log comment. |
| `tests/unit/domain/Api/Records/RecordFamilyEndpointsTests.cs` | 21 test cases (see Tests). |

`RecordNoAccessEndpoint.cs` is untouched (CLAUDE.md §11.5).

## Gate order, and why

1. **Query** (`kind`, `direction`, `continuation`), before anything else: a bad `kind` or `direction` is a 400 with fixed text
   and no echo, with no probe and no Dataverse call. `kind` absent -> Filing; "Filing" is case-insensitive; a repeated or empty
   `kind` is refused. `direction` absent -> full; `full` / `down` case-insensitive. A `continuation` that is repeated or longer
   than 4096 characters is the same 400 as an invalid one (a real token is a few hundred characters); an empty one is no
   continuation.
2. **Caller**: Entra oid from `CallerResolution.ResolveObjectIdGuid` (never a hand-rolled claim read; null -> 401, per that
   type's contract), then the Dataverse systemuserid by `CallerRecordAccessProbe.GetCallerSystemUserIdAsync` (WhoAmI on the
   caller's own token), exactly as `/push` does (`FieldMappingEndpoints.AuthorizePushSourceAsync`). An unresolvable
   systemuserid, or a faulting lookup, is the uniform 404 with no app-only fallback (the /push failure behaviour).
3. **Table gate**: a `{table}` that is not a family table (`RecordFamilyReader.TryGetFamilyEntitySet`: the reader's own
   registry notion, trimmed and lower-cased as the reader does) or an empty id is the uniform 404. Table names are public (the
   registry is in the repository), so this branch needs no probe and reveals nothing about any record.
4. **Equalised Read probe on the anchor**: `CallerRecordAccessProbe.GetCallerRightsForAuthorizationGateAsync` on the family
   table's entity set, then `OperationAccessPolicy.HasRequiredRights(rights, "read")`. Anything but Read (no rights, unknown
   id, probe fault) is the uniform 404; the detail (table, id, decision) is logged at Information / Warning, never in the body.
   Cancellation of `ct` propagates.
5. **`RecordFamilyReader.ReadAsync`**. Outcome map: `Read` -> 200; `AnchorNotFound` (a race after the probe) -> the uniform 404;
   `ContinuationInvalid` -> ONE fixed 400 (same body as a malformed or oversize continuation); `KindNotSupported` (cannot
   happen after step 1) -> the bad-kind 400. A Dataverse fault propagates to the ProblemDetails middleware; nothing is caught.

### Decision beyond the brief: the probe call, not `MappingSourceReadProbe.CheckCallerReadAsync`

The brief named `CheckCallerReadAsync` "or the probe method it wraps, if that is the cleaner reuse". `CheckCallerReadAsync`
resolves the entity set through `EntityAccessFilter.TryResolveEntitySet`, whose table is closed at `contact`, `sprk_matter`,
`sprk_project`, `sprk_invoice`, `sprk_workassignment`, `sprk_event` and `sprk_todo`. Through it a family opened on a document,
an agreement, a signal, a communication etc. (most of the Filing registry's tables) would be the uniform 404 for everyone. So
the endpoint calls the wrapped `GetCallerRightsForAuthorizationGateAsync` (the equalised, non-virtual gate method that
`CheckCallerReadAsync` itself calls) on the entity set the reader already knows for each family table. Same equalisation, same
`"read"` key (`MappingSourceReadProbe.ReadOperation` is reused), no second entity-set map. If the owner prefers the narrower
set, swapping in `CheckCallerReadAsync` is a three-line change in the handler.

## Response (what `recordFamilyService.ts` parses, unchanged)

camelCase, default JSON options (nulls are written): `anchor { id, table, name }`; `root { id, table, name }` = the read's `Top`
on the full view, `null` on `down` (never anything that hints at an unreadable record above the top, UAC F2); `truncated`;
`continuation`; `members[]` each `id, table, name, recordNumber, recordType, status, modifiedOn (ISO 8601), kind ("Filing"),
edgeKind (the EdgeKind name; null for the top), parentId, depth (>= 0)`. No access metadata and no `CountsByTable` (the client
contract does not list it). The first page always holds the anchor (the reader guarantees it; asserted in a test).

### `recordType`: emitted as null — OPEN ITEM

The owner decision: the display name of the member's TABLE (e.g. "Matter"), a property of the table and never a per-record
value. Search result: **no existing mechanism covers the family tables** without a Dataverse call per request.

- `MetadataService` (Redis-cached, 6 h) projects the table's attributes only; `ProjectToDto` explicitly drops the localized
  labels, and `EntityMetadataDto` has no display-name field.
- The static maps are partial: `StandaloneChatContextProvider.EntityDisplayNames` (7 tables, AI chat, `internal` to
  `Services/Ai/Chat`), `RegardingRecordType.GetDisplayName(int)` (8 regarding types, keyed by integer), and the AI
  `DataverseDescribeHandler` (a live metadata call per request). The Filing registry has dozens of tables.

Per the brief (no new service, no per-request metadata call) the field is emitted as `null`; the modal tolerates it. The
mapping lives in one place (`RecordFamilyEndpoints.ToMember`), with a comment, and a test pins the null. **Follow-up for the
owner**: either (a) add a `DisplayName` to `EntityMetadataDto` (read from `EntityMetadata.DisplayName.UserLocalizedLabel`,
one extra field on an already cached, already fetched call; the cache key bump is the only cost) and have the endpoint read it
once per distinct table per page, or (b) a static table-label map beside `RecordFamilyEdges`, pinned by a test that it covers
every Filing-registry table. Either is a separate, small task.

## Ledger scope chosen, and why

`Api/Records/RecordFamilyEndpoints.cs` — `Scope.RouteLevelGate` (the scanner derives it: the file maps its own group), plus one
`HandlerDecision` (`GetFamilyAsync`, seam `CallerRecordAccessProbe`, no hops) citing the file:line of the systemuserid lookup,
the table gate, the probe, and the read. The guard accepted an in-handler gate through `HandlerDecisions` (its option 2); it
did not demand a route-level filter, so nothing was weakened. A route filter was not an option: the
`RecordRouteAccessAuthorizationFilter` fixed-entity-set overload cannot take a route-supplied table, and its declared-target
overload denies with a 403, the wrong shape. Census `ExpectedEndpointFileCount` 133 -> 134 with a MAINTENANCE log comment in the
064 style. (Other branches that also add an endpoint file will conflict on that line; reconcile at merge.)

## Placement Justification (CLAUDE.md §10)

- **Where.** `Sprk.Bff.Api/Api/Records/` — a new endpoint file in the BFF, which is the single backend for every client surface;
  its caller (the Family modal, task 073, and the Family tool) is a browser/assistant client. The existing `/api/v1/records`
  group is extended by a second `MapGroup` of the same prefix and conventions (`dataverse-query` rate limit,
  `RequireAuthorization`), registered with ONE Map call in `EndpointMappingExtensions.MapDomainEndpoints`, unconditionally
  (`RecordFamilyReader` is registered unconditionally, `AddDataverseMetadataServices`).
- **Not AI:** no `PublicContracts` facade. **No background work** (ADR-052 N/A). **No DI registration added, no package added.**
- **Publish-size delta and CVE:** not measured here (main session, per the brief).

## Component justification (CLAUDE.md §11, from the POML `<justification>`)

- **Existing:** the `/api/v1/records` group (`RecordNoAccessEndpoint.cs`), extended with a new route in its own file.
- **Extension:** the group's conventions are extended; the route gets its own file so `RecordNoAccessEndpoint.cs` keeps one
  responsibility (§11.5).
- **Cost of doing nothing:** the TrackingFieldTrio Family tool and the ribbon (and the Family modal, 073) have no way to list a
  record's family.

The two small additions to the reader (`FamilyTablesOf`, `TryGetFamilyEntitySet`) are extensions of existing surface that remove
a second copy of the family-table test; no new service or abstraction.

## Tests (`tests/unit/domain/Api/Records/RecordFamilyEndpointsTests.cs`, 21 cases)

KEEP path `tests/unit/domain/**` (ADR-038 §2). A minimal TestServer host (the push contract tests' shape) maps the REAL
`MapRecordFamilyEndpoints` over the REAL `RecordFamilyReader`; substituted at module boundaries only: `CallerRecordAccessProbe`
(its virtual rights and WhoAmI seams; it also records whether each rights question arrived through the equalised gate method, by
reading the gate's `AsyncLocal`) and `IImpersonatedCommunicationQuery` (an in-memory caller-scoped Dataverse that records every
call and the caller it impersonated).

| Case | Criterion |
|---|---|
| 200 for a readable anchor: exact property sets (no extras), kind and edgeKind on members, anchor among the members, `recordType` null, every read impersonates the caller | shape, first page |
| `direction=down` / `DOWN&kind=filing`: root omitted, anchor on top | root omitted |
| default / `full` / `FULL&kind=Filing`: root present | root present |
| unknown anchor vs unreadable anchor: identical 404 (apart from ids), both asked through the gate method, nothing read | equalised 404 |
| probe fault / unresolvable caller / vanished after the probe: the same 404 | uniform 404 |
| `contact`, `sprk_notatable`, the alias `matter`: the same 404 with no probe and no Dataverse call | table gate |
| bad `kind` / `direction` (6 shapes): 400, no echo, no probe, no WhoAmI, no Dataverse call | 400 before any call |
| foreign-bound, tampered, garbage and oversize continuation: one identical 400 body, no echo | continuation |
| unauthenticated: 401, nothing touched | 401 |

Red phase (before the endpoint existed, with a 501 skeleton mapping the route): 20 failed / 1 passed (the 401 case passes on
the skeleton because the group's `RequireAuthorization` already existed). Green: 21 / 21. ArchTests: see the report.

## Known limits / open items

- **K-class: `recordType` is null** (above). Not a defect on a real path; the modal tolerates it.
- **Residual timing:** the table gate and the Guid.Empty branch answer without a probe (public information, by design); the
  systemuserid lookup (WhoAmI) runs before the table gate, as the brief orders, so a non-family table costs one OBO exchange.
- The reflection read of `CallerRecordAccessProbe.EqualizeNoAccessWithNotFound` in the test fails loudly (it throws in the
  probe double) if that field is renamed; the gate method is deliberately non-virtual, so there is no other seam that proves a
  call came through it.
- No live Dataverse call was made; the endpoint is proven against in-memory doubles only.
