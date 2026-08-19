# Dataverse Access-Layer Routing (which `IDataverseService` impl serves what — and the traps)

> **Status**: canonical routing map · **Last Reviewed**: 2026-08-15 (r3 RED-4 hardening)
> **Why this exists**: two impls back one composite interface; a mis-route already shipped a bug
> (`GraphModule.cs:74-77`). Read this before injecting a Dataverse interface or editing either impl.
> **Projects**: interim `dataverse-access-hardening` (fences the traps) · `dataverse-access-unification-r1`
> (retires them) · `#3b` MI migration (task 011/NG1).

## The two implementations

| Impl | Mechanism | Auth (today) | Role |
|---|---|---|---|
| `DataverseServiceClientImpl` | SDK `ServiceClient` (+ raw OData via `ExecuteWebRequest`, impersonation via `Clone()`+`CallerId`) | `ClientSecret` (`API_CLIENT_SECRET`) — **ADR-028 violation, #3b** | **Primary** `IDataverseService` for documents / analysis / generic-entity / jobs / KPI / communication / health |
| `DataverseWebApiService` | REST / `HttpClient` | `ClientSecret` (`Dataverse:ClientSecret`) — **ADR-028 violation, #3b** | The **real impl** for events · field-mapping · impersonated reads (`RetrieveMultipleImpersonatedAsync`) · POA grants |

## Routing table (`Infrastructure/DI/GraphModule.cs:44-82`)

| Injected interface | Resolves to | Notes |
|---|---|---|
| `IDataverseService` (composite) | **SDK** | documents/analysis/generic/jobs/KPI/comm/health |
| `IDocumentDataverseService`, `IAnalysisDataverseService`, `IGenericEntityService`, `IProcessingJobService`, `IKpiDataverseService`, `ICommunicationDataverseService`, `IDataverseHealthService` | **SDK** (forward to `IDataverseService`) | |
| **`IEventDataverseService`** | **WebApi** | SDK impl STUBS events |
| **`IFieldMappingDataverseService`** | **WebApi** | SDK impl STUBS field-mapping |
| `IImpersonatedCommunicationQuery` (concrete `DataverseWebApiService`) | **WebApi** | NFR-06 row-level security |
| `IDataverseAccessGrantService` (concrete `DataverseWebApiService`) | **WebApi** | POA grants |

## The rule (do this to avoid the trap)

**Inject the NARROW interface that matches your capability** — never call event/field-mapping methods through
the composite `IDataverseService`. The composite resolves to the SDK impl, whose event/field-mapping methods
are **stubs**: some throw `NotImplementedException`, and seven return **silent-empty** (empty/null +
`LogWarning`) — a query that silently returns nothing.

## Known traps (being remediated by the hardening project)

1. **🐞 LATENT BUG — `TodoGenerationService` silently gets zero events.** It injects the composite
   `IDataverseService` (`TodoGenerationService.cs:213`) and calls `QueryEventsAsync`
   (`TodoGenerationService.cs:334`) → the SDK impl's **silent-empty stub** → its overdue-events pass always
   returns empty. Fix: inject `IEventDataverseService` (→ WebApi) for the event query. **Behavior change
   (starts returning real events) — validate the todo-generation side effects before enabling.** Tracked in
   `projects/code-quality-and-assurance-r3/notes/defer-issues.md`.
2. **✅ FIXED (RED-4 B, 2026-08-16) — WebApi field-mapping no longer throws (DEF-2).** `UpdateRecordFieldsAsync`
   is live in BOTH impls (`FinanceRollupService` via composite → SDK; `InvoiceReviewService`/
   `ScorecardCalculatorService`/handlers via `IFieldMappingDataverseService` → WebApi). RED-4 B found the WebApi
   half was worse than "duplicate": its first call was the WebApi impl's `GetEntitySetNameAsync`, a stub that
   **threw `NotImplementedException`** — so field-mapping read/child-query/write via the WebApi path threw (the
   landmine that surfaced in the compose cold-session UAT, `ComposeOutputsColdSessionTests`). **Fixed** by
   implementing `GetEntitySetNameAsync` (`:176`) against the `EntityDefinitions` metadata endpoint (cached,
   fails loud), mirroring `GetEntityObjectTypeCodeAsync`. Both impls now resolve set names correctly; the
   remaining "which impl runs" question is cosmetic and folds into RED-4 C unification. Regression:
   `tests/integration/Spe.Integration.Tests/DataverseWebApiFieldMappingRegressionTests.cs` (live-gated).
   Tracked as **DEF-2** (`projects/code-quality-and-assurance-r3/notes/defer-issues.md`) — behavioral
   live-dev smoke pending.
3. **✅ DONE (RED-4 B, 2026-08-16) — runtime-dead code deleted.** `DataverseWebApiService` shrank **2,822 → 1,409
   LOC** (−1,414): the document/analysis/generic-entity/processing-job/KPI/communication-query/health surfaces
   (unreachable — those interfaces route to SDK) were removed and the class declaration narrowed from the
   composite `IDataverseService` to `: IEventDataverseService, IFieldMappingDataverseService`. Kept: the live
   event + field-mapping + impersonation/POA surface, the shared `GetEntitySetNameAsync`/`ConvertJsonElementToObject`
   helpers, and both `UpdateRecordFieldsAsync` impls. Waiver removed from `GodClassGuardTests` (now under the
   2,000 ceiling). Verified: BFF 10,402 tests pass, ArchTests 38/38.
4. **✅ DONE (RED-4 B, 2026-08-17) — silent-empty SDK stubs → throw.** The 8 event/field-mapping query stubs on
   `DataverseServiceClientImpl` (`QueryEventsAsync`, `GetEventAsync`, `QueryEventLogsAsync`, `GetEventTypesAsync`,
   `GetEventTypeAsync`, `QueryFieldMappingProfilesAsync`, `GetFieldMappingProfileAsync`, `GetFieldMappingRulesAsync`)
   returned empty + `LogWarning` — masking a mis-route as "no data" (the DEF-1 bug class). Now they `throw
   NotImplementedException`, consistent with the sibling event/field-mapping methods, so a mis-route (injecting
   the composite `IDataverseService` instead of the narrow interface) fails LOUD. Gated behind DEF-1: done only
   after `smart-todo-r5` rerouted `TodoGenerationService` Rules 1 & 3 to `IEventDataverseService` (commit
   `8e0aa4e6e`), so no live path hits the throw. Verified: no legitimate caller routes these via the composite
   (all event callers inject `IEventDataverseService`, all field-mapping callers inject
   `IFieldMappingDataverseService`); full BFF suite 10,427 pass.

## Auth (#3b)

Both impls use a client secret → ADR-028 §24 mandates Managed Identity. Migration is constructor-scoped per
impl, routed to task 011/NG1 (`notes/task-011-ng1-3b-mi-migration.md`). Grant `prvActOnBehalfOfAnotherUser`
to the MI app-user for the impersonated write path; never remove the secret until MI is proven live.
