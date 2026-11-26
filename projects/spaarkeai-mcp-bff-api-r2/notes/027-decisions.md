# Task 027 — search authorization as shared methods: decisions and notes

Date: 2026-11-25. Branch: agent worktree (wave 5, merged into `work/spaarkeai-mcp-bff-api-r2` by the main session). Added after
task 022 escalated (the REST search routes authorize inside endpoint filters and private endpoint statics). House pattern: task 007
(`PlaybookAuthorizationFilter.AuthorizePlaybookRunAsync` over a private decide method), task 008.

## What changed

Document search (`POST /api/ai/search`, and the count route's half):

- `SemanticSearchAuthorizationFilter`'s request-level body moved into a private instance method `DecideRequestAsync(HttpContext,
  SemanticSearchRequest?, ct)` that returns a typed `SemanticSearchAccess`. It is the ONE implementation of the request checks, in
  the order they ran before: tenant (`tid`), request body, caller `oid`, bearer token, then the scope (`AuthorizeScopeAsync` →
  entity / documentIds / all, unchanged bodies, now returning typed refusals instead of `IResult`).
- `InvokeAsync` now binds the request, calls `DecideRequestAsync` with `RequestAborted`, publishes the decision on
  `HttpContext.Items` (unchanged key and type) and calls `next`, or maps the refusal to the same `Results.Problem(status, title,
  detail, extensions)` call it made before (`ToProblem`: outcome → status/title; detail and code from the typed result; the same
  `ProblemExtensions` bag with `errorCode`, `code`, `correlationId`).
- Moved, unchanged, from `SemanticSearchEndpoints` into the filter: `CrossRecordOverFetchFactor`, `CrossRecordOverFetchCap`,
  `MaxParentAuthorizationChecks`, `NarrowToAuthorized`, `AuthorizeResults`, `IsPermitted`, `WithCrossRecordCandidatePool`,
  `CrossRecordCandidatePoolSize`, `AuthorizeRowsByParentAsync`, `TrimToReadableDocumentsAsync`.
- New `internal static SearchAndAuthorizeResultsAsync(...)`: the handler's former result-level body (narrow, candidate pool, run
  the search through a delegate, per-row parent check or `AuthorizeResults`, trim) — the ONE implementation behind the handler and
  the public method. The `scope=all` caller/token re-check stays where it was (after the search) and is now the typed
  `CallerContextUnavailable`, which the handler maps to the same 500 "Caller context not available.".
- New `internal static DecideCount(...)`: the count route's `scope=all` refusal and request narrowing. The count handler maps it
  to the same 403 body.
- New `public static SearchDocumentsAsCallerAsync(HttpContext?, SemanticSearchRequest?, Func<SemanticSearchRequest, string,
  CancellationToken, Task<SemanticSearchResponse>> search, CancellationToken)` for non-HTTP hosts (task 022): identity test, then
  `DecideRequestAsync`, then `SearchAndAuthorizeResultsAsync`. The delegate receives the narrowed request and the caller's tenant
  id, so a tool never reads `tid` or the token itself.
- `SemanticSearchAccess` (record) and `SemanticSearchAccessOutcome` (enum) added in the filter file.
- `SemanticSearchEndpoints.Search` / `Count` now bind (tenant, `ValidateScope`, `ValidateFilters`, the Items decision) and map.

Record search (`POST /api/ai/search/records`):

- `RecordSearchAuthorizationFilter`'s body moved into a private instance method `DecideRequest(HttpContext, RecordSearchRequest?)`
  (synchronous: this phase reads nothing from Dataverse). Same order: tenant, body, `oid`, token, malformed types (400 with
  `invalidValues` / `validValues`), no types (400), unauthorizable types (403). `InvokeAsync` binds, publishes, maps (`ToProblem`
  reproduces each former `Problem(...)` / `Results.Problem(...)` call, including the extension key order).
- Moved, unchanged, from `RecordSearchEndpoints` into the filter: `MaxRecordAuthorizationChecks`, `AuthorizeRowsAsync`.
- New `internal static SearchAndAuthorizeRowsAsync(...)`: reads the caller `oid` and token BEFORE the search (as the handler did),
  runs the search through a delegate, authorizes every row. Missing caller → typed `CallerContextUnavailable`, no search; the
  handler maps it to the same 500.
- New `public static SearchRecordsAsCallerAsync(HttpContext?, RecordSearchRequest?, Func<RecordSearchRequest, CancellationToken,
  Task<RecordSearchResponse>> search, CancellationToken)`.
- `RecordSearchAccess` (record) and `RecordSearchAccessOutcome` (enum) added in the filter file.
- `RecordSearchEndpoints.PostRecordSearch` keeps its signature (tests call it) and its query / bounds / record-type validation and
  Items check; the caller check and row authorization are the shared method.

`HttpContext.Items` stays only for the REST filter → handler hand-off (ADR-008). Unused usings removed from both endpoint files.

## Outcome → REST → reasonCode

Document search (`SemanticSearchAccessOutcome`; `ReasonCode` = the REST `code` / `errorCode`; `Detail` = the REST `detail`):

| Outcome | REST response (unchanged) | `ReasonCode` |
|---|---|---|
| `UserContextRequired` | not reachable (route requires authentication) | `DATAVERSE_USER_CONTEXT_REQUIRED` (as 007/008) |
| `MissingTenantIdentity` | 401 Unauthorized | `MISSING_TENANT_IDENTITY` |
| `RequestBodyRequired` | 400 Bad Request | `REQUEST_BODY_REQUIRED` |
| `MissingCallerIdentity` | 401 Unauthorized | `MISSING_CALLER_IDENTITY` |
| `MissingCallerToken` | 401 Unauthorized | `MISSING_CALLER_TOKEN` |
| `InvalidScope` | 400 Invalid Scope | `INVALID_SCOPE` |
| `EntityTypeRequired` / `EntityIdRequired` | 400 Bad Request | `ENTITY_TYPE_REQUIRED` / `ENTITY_ID_REQUIRED` |
| `EntityTypeNotAuthorizable` | 403 Forbidden | `ENTITY_TYPE_NOT_AUTHORIZABLE` |
| `InvalidEntityId` | 400 Bad Request | `INVALID_ENTITY_ID` |
| `EntityAccessDenied` | 403 Forbidden (uniform: unreadable or absent) | `ENTITY_ACCESS_DENIED` |
| `DocumentIdsRequired` | 400 Bad Request | `DOCUMENT_IDS_REQUIRED` |
| `TooManyDocumentIds` | 400 Too Many Document Ids | `TOO_MANY_DOCUMENT_IDS` |
| `InvalidDocumentIds` | 400 Invalid Document Ids | `INVALID_DOCUMENT_IDS` |
| `NoReadableDocuments` | 403 Forbidden | `NO_READABLE_DOCUMENTS` |
| `ScopeAllNotPermitted` | 403 Forbidden (count route only) | `SCOPE_ALL_NOT_PERMITTED` |
| `CallerContextUnavailable` | 500 "Caller context not available." (no code) | **new on the typed result**: `MISSING_CALLER_IDENTITY` or `MISSING_CALLER_TOKEN` (the missing part) |
| `Allowed` | 200, `Response` | `null` |

Record search (`RecordSearchAccessOutcome`):

| Outcome | REST response (unchanged) | `ReasonCode` |
|---|---|---|
| `UserContextRequired` | not reachable | `DATAVERSE_USER_CONTEXT_REQUIRED` |
| `MissingTenantIdentity` / `MissingCallerIdentity` / `MissingCallerToken` | 401 | `MISSING_TENANT_IDENTITY` / `MISSING_CALLER_IDENTITY` / `MISSING_CALLER_TOKEN` |
| `RequestBodyRequired` | 400 | `REQUEST_BODY_REQUIRED` |
| `InvalidRecordTypes` | 400 Invalid Record Types (+ `invalidValues`, `validValues`) | `INVALID_RECORD_TYPES`; `InvalidValues` on the result |
| `RecordTypesRequired` | 400 Record Types Required | `INVALID_RECORD_TYPES` (REST's code for both 400s) |
| `RecordTypeNotAuthorizable` | 403 | `ENTITY_TYPE_NOT_AUTHORIZABLE` |
| `CallerContextUnavailable` | 500 "Caller context not available." (no code); no search ran | **new on the typed result**: `MISSING_CALLER_IDENTITY` or `MISSING_CALLER_TOKEN` |
| `Allowed` | 200, `Response` | `null` |

No new code string was invented: `CallerContextUnavailable` reuses the request phase's codes for the same condition.

## No-caller outcomes (ADR-028 A10)

- Null `HttpContext`, no authenticated identity, or no `RequestServices` → `UserContextRequired` before any Dataverse or search
  call (proven: no access-data call, no search request, no trim call).
- An authenticated principal without `tid`, `oid` or bearer token gets the REST code for that case (`MISSING_*`), as on the route;
  never an app-only evaluation.
- A caller with no `systemuser` row: every rights question is answered by Dataverse as the caller, so such a caller is "no rights"
  (`EntityAccessDenied`, `NoReadableDocuments`, or rows removed). Same as 007: a host that must refuse a no-row caller with
  `UserContextRequired` (A10) resolves the systemuser before calling; the methods cannot without changing REST's 403/200.

## Deviations from the POML (code wins, root CLAUDE.md §2)

- **Two-phase shape behind one public method.** REST must split the decision across the filter (before the handler's own
  validation) and the handler (after the search), so the ONE implementation is two internal methods (`DecideRequestAsync`,
  `SearchAndAuthorizeResultsAsync`), and the public method composes them around a search delegate. A tool therefore cannot run the
  result phase with a decision it built itself (`SemanticSearchAuthorization` is a public record with public init setters; the
  public method never accepts one).
- **The count route was included** (`DecideCount`), though the POML names only `POST /api/ai/search`: its `scope=all` refusal is a
  scope decision in an endpoint, which the "no scope check of its own" criterion forbids. REST body unchanged.
- **Input validation is not in the shared methods**: the semantic handler's `ValidateScope` (a duplicate the filter answers first)
  and `ValidateFilters` (`filters.entityTypes`), and the record handler's query / length / limit / offset checks, stay in the
  handlers. They are shape checks, not access decisions; task 022 validates its own tool input. Neither is an access hole: an
  invalid `entityTypes` value only narrows the index query, the `scope=all` candidate pool is capped at 150, and record rows past
  the 50-check budget are dropped. A negative `options.limit` makes the `scope=all` path throw (fails closed); 022 must bound it.
- **A missing `AuthorizationService` / `IRetrievalAccessTrim` registration throws** in the public methods
  (`GetRequiredService`), exactly as the REST filter factory and handler resolution do; 007 returned a typed refusal for its
  missing service, but here no REST answer exists to mirror.
- **Escalation triggers not fired.** Filters and handlers bind and map only (plus the handlers' pre-existing input validation and
  the Items presence check, which is the forcing function, not an access decision). The MCP caller obtains the caller-scoped
  identity from the request `HttpContext` (OBO via the Authorization header), with no token passed.

## Placement (ADR-013 / ADR-029, `.claude/constraints/bff-extensions.md` §A)

- In the BFF, in the existing `Api/Filters/SemanticSearchAuthorizationFilter.cs` and `Api/Filters/RecordSearchAuthorizationFilter.cs`
  (as 007/008). Public signatures carry only `HttpContext`, the search request/response models (`Models/Ai/...`), BCL delegates
  and the new result types; `IRetrievalAccessTrim` (a `Services/Ai/PublicContracts` facade) appears only in an `internal`
  signature. No AI-internal type (`IOpenAiClient`, `IPlaybookService`, …) in any signature; `ADR013_AiBoundaryTests` green. An MCP
  tool in `Api/Mcp/**` reaches the methods (same assembly, public static). Per-request authorization of a BFF route and a BFF MCP
  tool on the same host; no background or separable work (ADR-052 n/a); no DI registration (ADR-010); no package.
- Component justification (root §11): **Existing** — the two filters and the endpoint statics are the only owners (reuse-scan rows
  "Document search", "Record search"); `IRetrievalAccessTrim` covers only the final trim. **Extension** — extended in place: the
  endpoint statics moved into the filters, the new types are their result shape. **Cost of doing nothing** — task 022's tools
  would copy the scope and per-row checks (spec MUST NOT) or call the services alone, which return documents and records under
  parents the caller cannot read (must-not-leak).
- Size (§11.5): `SemanticSearchAuthorizationFilter.cs` grows to ~1,390 lines (much of it the moved remarks). It is cohesive: one
  responsibility, the document-search access decision, from request to rows.

## Tests

- New: `tests/integration/auth/Ai/SearchAuthorizationSharedMethodTests.cs` (13 cases). A local host maps BOTH real routes with the
  real filters, handlers, `AuthorizationService` and external narrowing (internal caller), substituted only at the access-data
  source (reuses `PlaybookRouteAuthorizationContractTests.RecordingAccessDataSource`), the two search services and
  `IRetrievalAccessTrim`. Each case calls the method directly over the SAME container, then sends the same input to the route:
  allowed answers must serialize (host JSON options) to the route's body exactly; refusals must carry the route's status, `code`,
  `errorCode` and `detail`; both must ask the access-data source the same questions in the same order; a refusal never reaches
  the index.
- Cases (all named in the criteria): entity scope allowed (off-parent row dropped, SPE pointers stripped, unreadable document
  trimmed, counts recomputed, canonical `entityId` searched); `scope=all` with an unreadable parent (removed, `PARTIAL_RESULTS`,
  same candidate pool); documentIds with an unreadable id (never reaches the index); five refusals (unreadable parent,
  unauthorizable entity type, no readable document, unknown scope, no bearer token); record search with an unreadable record
  (removed, not counted); two record refusals (invalid record type with `invalidValues`, no bearer token); null context and
  unauthenticated principal for both methods (no Dataverse call, no search, no trim).
- Seeding proof: (1) mapping `EntityAccessDenied` to 404 in `ToProblem` and (2) dropping the identity test in the public method
  failed 2 cases; (3) discarding the trim's result failed the entity-scope case. Restored.
- Existing suites unchanged and green, before and after: `Sprk.Bff.Api.Tests` filtered RecordSearch / SemanticSearch / Issue975 /
  SearchIndexName / DocumentWriteRouteNarrowing / AiDocumentNarrowing / PlaybookRunAuthorizationSharedMethod 372/372;
  `Spe.Integration.Tests` `SemanticSearch` namespace (SemanticSearchAuthorizationTests, SemanticSearchIntegrationTests,
  RecordSearchIntegrationTests) 90/90.

## Review

- Code review (inline, Step 9.5): no F-class finding. Checked: the filter order (tid, body, oid, token, scope) is unchanged and
  pinned by the unchanged route suites; both public methods refuse before any Dataverse or search call on a null context, an
  unauthenticated principal or no services; no Allowed result can carry a null Response (the dead `|| request is null` branch
  was removed: Allowed implies a body); the search delegate runs once, only after the request decision allows it; the
  scope=all and record paths read oid and token before the search (record) or before the per-row checks (document); no token or
  identity is logged; the moved code is verbatim (diff of the moved blocks against master). One pre-existing defect outside
  the access decision recorded as D-027-1 (not yet a GitHub issue) (500 detail echoes ex.Message on the document routes; REST bytes frozen here).
- ADR check (inline): ADR-008 filters stay on both routes and the handlers keep the forcing-function Items check; ADR-019 the
  shared methods return typed outcomes, never IResult or ProblemDetails, and the REST mapping reproduces the old bodies;
  ADR-028 A10 fails closed, caller-scoped OBO only, no app-only fallback; ADR-013 only `IRetrievalAccessTrim`
  (PublicContracts) is used from Api/Filters, no forbidden AI type; ADR-010 no new DI registration; ADR-029 publish delta
  and CVE in the task report; ADR-038 tests on the KEEP path `tests/integration/auth/Ai/`, no HttpMessageHandler or
  IServiceClient mocks; ADR-052 no background work. No conflict.
- rework: none task 027
- Reviews pending: **unified-access-control-r2** (caller path).
- Adversarial verifier passes: two, run by the main session (`auth`, `tenant-isolation`).

## Known limits (K-class)

- K4: as in 007, the narrowing (`InternalPlaneDocumentNarrowing`) and `IRetrievalAccessTrim` observe the request context, not the
  method's `cancellationToken`; for an MCP host whose token differs, cancelling only one ends the call as a refusal or an
  `OperationCanceledException`. Both fail closed.
- K2: a negative `options.limit` throws on the `scope=all` path (the route's `[Range(1, 50)]` metadata is not enforced by a
  validation filter on either route; this is pre-existing on REST). Fails closed.
