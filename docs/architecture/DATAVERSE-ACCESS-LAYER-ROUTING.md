# Dataverse Access-Layer Routing (which `IDataverseService` impl serves what — and the traps)

> **Status**: canonical routing map · **Last Reviewed**: 2026-08-20 (`unified-access-control-r2` drift
> correction — Auth column updated: the #3b MI migration LANDED 2026-08-17, commit `a76e7e714`)
> **Why this exists**: two impls back one composite interface; a mis-route already shipped a bug
> (`GraphModule.cs:74-77`). Read this before injecting a Dataverse interface or editing either impl.
> **Projects**: interim `dataverse-access-hardening` (fences the traps) · `dataverse-access-unification-r1`
> (retires them) · `#3b` MI migration (task 011/NG1 — **done**, proven live on dev).

## The two implementations

| Impl | Mechanism | Auth (today) | Role |
|---|---|---|---|
| `DataverseServiceClientImpl` | SDK `ServiceClient` (+ raw OData via `ExecuteWebRequest`, impersonation via `Clone()`+`CallerId`) | **MI-first** (ADR-028 §24): `Graph:ManagedIdentity:Enabled=true` → `DefaultAzureCredential` pinned to the UAMI (`DataverseServiceClientImpl.cs:54-73`); `ClientSecret` (`API_CLIENT_SECRET`) retained as local-dev fallback only | **Primary** `IDataverseService` for documents / analysis / generic-entity / jobs / KPI / communication / health |
| `DataverseWebApiService` | REST / `HttpClient` | **MI-first** (ADR-028 §24): same flag → `DefaultAzureCredential` (`DataverseWebApiService.cs:53-65`); `ClientSecret` (`Dataverse:ClientSecret`) retained as local-dev fallback only | The **real impl** for events · field-mapping · impersonated reads (`RetrieveMultipleImpersonatedAsync`) · POA grants |

## Routing table (`Infrastructure/DI/GraphModule.cs:44-82`)

| Injected interface | Resolves to | Notes |
|---|---|---|
| `IDataverseService` (composite) | **SDK** | documents/analysis/generic/jobs/KPI/comm/health |
| `IDocumentDataverseService`, `IAnalysisDataverseService`, `IGenericEntityService`, `IProcessingJobService`, `IKpiDataverseService`, `ICommunicationDataverseService`, `IDataverseHealthService` | **SDK** (forward to `IDataverseService`) | |
| **`IEventDataverseService`** | **WebApi** | SDK impl STUBS events |
| **`IFieldMappingDataverseService`** | **WebApi** | SDK impl STUBS field-mapping |
| `IImpersonatedCommunicationQuery` (concrete `DataverseWebApiService`) | **WebApi** | NFR-06 row-level security. Registered in **`CommunicationModule.cs`** (~`:272`), not `GraphModule` |
| `IDataverseAccessGrantService` (concrete `DataverseWebApiService`) | **WebApi** | POA grants. Registered in **`CommunicationModule.cs`** (~`:648`), not `GraphModule` |

> **`UpdateRecordFieldsAsync` is single-impl as of 2026-08-20** (trap 2a below): `IFieldMappingDataverseService`
> → WebApi is the only live route. The SDK impl throws. Never call it through the composite.

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
2a. **✅ FIXED (B3 completion, 2026-08-20) — `UpdateRecordFieldsAsync` is now SINGLE-impl.** It was the last
   `IFieldMappingDataverseService` member live in **both** impls, so the same operation ran on a different
   implementation depending on which alias the consumer injected. The composite route had exactly one caller,
   `FinanceRollupService`, now switched to `IFieldMappingDataverseService`; the SDK impl's copy is a fail-loud
   stub like its 7 siblings. **This completes interim-hardening item B3** ("ONE live impl; route through the
   narrow interface"), which RED-4 B left open by fixing only the `GetEntitySetNameAsync` half.
   **A live defect was found and fixed in the same change**: `FinanceRollupService` passed SDK Entity-model
   `Money` wrappers for its 5 currency fields, which serialize to `{"Value":…,"ExtensionData":null}` — an
   object. The Web API PATCH takes a bare number, so **every matter/project recalculate had been failing with
   HTTP 400 since `b7b0d4011` (2026-03-03)** converted the impl from the Entity model to OData PATCH without
   updating this caller. Payload extracted to `FinanceRollupService.BuildRollupFields` and pinned by
   `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/FinanceRollupPayloadTests.cs` (4 tests, incl. a negative
   control proving the guard is not vacuous). Impersonation capability is unchanged — the live impersonated
   write (email-intelligence task 031 / FR-10) always ran through `UpdateRecordActionCore` →
   `IFieldMappingDataverseService` → WebApi (`MSCRMCallerID` on the PATCH); the SDK impl's `Clone()`+`CallerId`
   branch was never reached and is recoverable from git (`4aca6d65a`).
2. **✅ FIXED (RED-4 B, 2026-08-16) — WebApi field-mapping no longer throws (DEF-2).** `UpdateRecordFieldsAsync`
   was at that time live in BOTH impls (`FinanceRollupService` via composite → SDK; `InvoiceReviewService`/
   `ScorecardCalculatorService`/handlers via `IFieldMappingDataverseService` → WebApi) — see 2a for the
   completion. RED-4 B found the WebApi
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

## Auth (#3b) — ✅ LANDED 2026-08-17

Both impls are now **MI-first** per ADR-028 §24 (commit `a76e7e714`, "proven live on dev"): when
`Graph:ManagedIdentity:Enabled=true`, each constructor builds a `DefaultAzureCredential` pinned to the UAMI
(`ManagedIdentity:ClientId`); the client-secret path is the local-dev fallback and is retained (do NOT remove
`Dataverse:ClientSecret` / `API_CLIENT_SECRET`) per the migration's own guard comments. Original plan:
task 011/NG1 (`notes/task-011-ng1-3b-mi-migration.md`). `prvActOnBehalfOfAnotherUser` must be granted to the
MI app-user for the impersonated write path.
