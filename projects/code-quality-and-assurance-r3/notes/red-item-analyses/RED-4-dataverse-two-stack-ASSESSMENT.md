# RED-4 — Rigorous Assessment: the Two Dataverse Implementations

> **Type**: assessment (supersedes the RED-4 seed's "unify two duplicates" framing) · **Date**: 2026-08-15
> **Method**: direct code investigation (DI wiring, stub confirmation, usage, credentials) + Fable adversarial verify.
> **Status**: findings VERIFIED against code; recommendation pending the Fable verdict (in progress).

## The question

Why are there two `IDataverseService` implementations, is it real debt, and does fixing it warrant a
**full project** or can it be done in-session?

## Investigation & evidence (all confirmed against code)

### Finding 1 — it is a CAPABILITY SPLIT, not duplication
`Infrastructure/DI/GraphModule.cs:44-82` wires them deliberately:
- **`DataverseServiceClientImpl`** (SDK `ServiceClient`) is the **primary** `IDataverseService` → serves
  Documents, Analysis, GenericEntity, Jobs, KPI, Communication, Health (the 9-interface composite).
- **`DataverseWebApiService`** (REST) is separately registered and is the **"real implementation"** for the
  narrow interfaces the SDK impl **stubs**:
  - `IEventDataverseService` (GraphModule:73) — the SDK impl **throws**
    `NotImplementedException("... implemented in DataverseWebApiService")` for CreateEvent/UpdateEvent/
    UpdateEventStatus (`DataverseServiceClientImpl.cs:1759-1784`).
  - `IFieldMappingDataverseService` (GraphModule:78) — `QueryChildRecordIdsAsync` lives only in the WebApi
    impl. The comment records a real bug that came in via a master merge when a consumer got routed to the
    stub (asymmetric-registration anti-pattern, CLAUDE.md §10 F.1).

### Finding 2 — impersonation + POA are Web-API capabilities the Communication security model depends on
`DataverseWebApiService` takes an `impersonateSystemUserId` (MSCRMCallerID) on every request
(`DataverseWebApiService.cs:115-153`) and exposes `RetrieveMultipleImpersonatedAsync` + POA grant primitives
(`GrantAccessAsync`, principalobjectaccess). The Communication module relies on impersonated reads for
row-level security so Dataverse enforces access natively (NFR-06 — `CommunicationModule.cs:243/265/648`,
`IDataverseAccessGrantService`). **This is load-bearing.**
> ⚠ Open question for the assessment: the SDK `ServiceClient` ALSO supports impersonation (`CallerId`). So
> "impersonation *requires* a separate REST impl" is weaker than it looks — the split is partly historical
> (the WebApi impl was built to avoid WCF/`System.ServiceModel` on .NET 8 — now net10). Fable is verifying.

### Finding 3 — naive unification (delete one) is WRONG
Deleting `DataverseWebApiService` would break events, field-mapping, impersonated reads, and POA grants.
Deleting the SDK impl would lose the rich SDK ops (QueryExpression/Upsert via `OrganizationService` used by
~10 domain services through `UnwrapServiceClient`). Neither is removable as-is.

### Finding 4 — the #3b credential concern applies to BOTH, and is separable
Both authenticate with a **client secret** today: SDK impl uses `API_CLIENT_SECRET`
(`DataverseServiceClientImpl.cs:44,60`), WebApi impl uses `Dataverse:ClientSecret`
(`DataverseWebApiService.cs:40`). ADR-028 §24 mandates Managed Identity. The MI migration is a distinct,
bounded, high-value SECURITY item that can proceed **independently** of any class consolidation — but it is
an identity-attribution change needing per-env MI-as-Application-User setup + live validation (dev-only now).

### Finding 5 — the REAL debt (corrected)
Not "5,700 LOC of duplicate work." It is: (a) **stub-methods-that-throw** on the SDK impl (traps that have
already caused a routing bug); (b) a **fragile split of one composite interface across two impls** at the DI
layer (asymmetric-registration anti-pattern); (c) two **~2,800-LOC god-classes** (both `GodClassGuardTests`
waivers); (d) **secret-based auth on both** (ADR-028 violation, #3b).

## Fable adversarial-verify corrections (2026-08-15) — VERIFIED against code

The initial "capability split, NOT duplication" framing was **overstated**. Verified corrections:

1. **~1,100 LOC of the WebApi impl is runtime-DEAD** — full `CreateDocument`/`GetAnalysis`/KPI/etc.
   implementations that are unreachable because those interfaces resolve to the SDK impl. So there IS real
   duplication (~20% of the combined 5,686 LOC), just not 100%.
2. **`UpdateRecordFieldsAsync` is SPLIT-BRAIN** — a live implementation in BOTH classes; which one runs
   depends on which alias of the composite interface a consumer injected (`FinanceRollupService` → SDK;
   `InvoiceReviewService`/`ScorecardCalculatorService`/handlers → WebApi). Correctness hazard.
3. **The "impersonation needs REST" justification is REFUTED** — the SDK impl already impersonates via
   `ServiceClient.Clone()` + `CallerId` (`DataverseServiceClientImpl.cs:1875-1884`) AND issues raw OData
   PATCH via `ExecuteWebRequest`. The SDK path CAN do everything the REST path does; the split is history
   (built to avoid WCF on .NET 8), not a hard capability boundary.
4. **7 SDK stubs return SILENT-EMPTY (LogWarning), not throw** — worse than `NotImplementedException`; a
   mis-route fails silently.
5. **#3b (→MI) VERIFIED separable + already the plan of record** — task 011/NG1; a third Dataverse camp
   (Services/Ai raw-HTTP) was already migrated to MI in AUTHV2-042 Phase C (`appsettings.template.json:80`).
   Constructor-scoped per impl. Caveat: `prvActOnBehalfOfAnotherUser` must be granted to the MI's app-user
   for the impersonated WRITE path (regression-test in the MI task).
6. Abstraction already leaky: consumers **downcast** `IDataverseService` to the concrete SDK class for
   FetchXML (`UnwrapServiceClient`), so "just swap the impl" is not available.

## Recommendation (FINAL — Fable-verified) — SPLIT THE DECISION

Not a single "full project," and not "keep-as-is." Three separable pieces:

- **A. #3b Managed-Identity migration** → route to the **existing task 011 / NG1 (Idea #742)** track. Separable,
  ADR-028-mandated, half-done elsewhere, constructor-scoped per impl. Highest security value. **No new folder.**
- **B. Bounded hardening pass (2–3 sessions, ~5% of full-unification cost) — removes the sharp edges NOW:**
  1. Delete the ~1,100 runtime-dead document/analysis/KPI LOC from `DataverseWebApiService` (fix the 2 test
     references); shrinks it to its real 4-capability surface (relieves its ratchet freeze).
  2. Convert the 7 silent-empty SDK stubs → throw (loud, not silent-empty).
  3. Resolve the `UpdateRecordFieldsAsync` split-brain (ONE live impl; route through the narrow interface).
  4. Write a short **routing-table doc/ADR** (which interface → which impl → why) so the daily-update-service-r4
     mis-route bug can't recur.
- **C. Full single-impl unification (OPTIONAL, future project w/ ADR)** — port events/field-mapping/
  impersonation/POA onto the SDK path + delete the WebApi class. Feasible (the SDK proves both mechanisms) but
  drags in decomposition of both god-classes + NFR-06 impersonation re-verification. **Only if the owner wants
  the single-impl end-state.** Not required to remove the traps (B does that).

**Strongest counter-argument (Fable):** the most security-critical surface (impersonated row-level access) lives
in a majority-dead class reached by concrete-class injection that bypasses the interface layer, and piecemeal
cleanup historically never graduates to the real fix — if heavy Dataverse growth continues, paying once for C
(single-impl + decomposition + MI in one ADR-backed project) amortizes the security-test burden and permanently
retires the trap. Cleanup (B) *fences* the trap; the project (C) *retires* it.
