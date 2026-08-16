# Task 013 — Atomic server upsert on sprk_graphitemid_uk (FR-07d): analysis + plan

> Captured 2026-08-16 after full trace (main session, opus). NOT yet implemented — precise plan so 013
> executes cleanly with proper verification. This is the SERVER-side D1 vector (the client vectors
> 010/011/012 are done). opus tier, BFF + shared-lib, data-integrity.

## Current state (traced)

`ComposeService.PromoteIfEphemeralAsync` (@2505):
1. **Read** idempotency check: `TryFindDocumentByGraphItemIdAsync(DocumentSpeId)` (@2519) — alt-key `sprk_graphitemid_uk`.
2. **Existing row →** no-op + `RebindSessionDocumentIdAsync` (conditional on SessionId) + `GraduateLinkedCopyIfDivergedAsync`; return `WasCreated=false` (@2522-2555). **This path is important and must be preserved.**
3. **Absent →** build full entity (@2563+) → `_dataverse.CreateAsync(entity)` (@2717) → **catch** `InvalidOperationException` (@2723) re-resolving the race winner by graphItemId then transientKey (@2736-2751).

So today it is **read-then-create with exception-driven race recovery**, relying on the `sprk_graphitemid_uk` unique constraint firing + a catch. FR-07d wants a **true atomic upsert** so there is no create-then-catch window, and the match is **canonicalization-reliable** (ADR-044).

## The subtlety (why a naive upsert is WRONG)

Dataverse `UpsertRequest` UPDATES (applies the whole target entity) when the alt-key matches. A blind upsert of the full promote entity would **clobber an already-promoted row's fields** on a later call. The existing-row path (step 2) has special no-op/rebind/graduate semantics that must NOT be replaced by a field-overwrite.

## Chosen design (preserve read-first; atomic-upsert only the create)

- **Keep** the read-first idempotency check (step 1/2) UNCHANGED — genuine pre-existing rows still get no-op + rebind + graduate.
- **Replace** the create+catch (step 3) with an atomic **`UpsertAsync`** keyed on `sprk_graphitemid_uk`. When two concurrent first-saves both pass the read as "absent" and both reach the upsert, the server-side key match means the second UPDATES the first's row (same DocumentSpeId → same derived field values → benign) instead of inserting a duplicate. Result: exactly one row, no exception-driven recovery, no TOCTOU window.
- **Canonicalize** the `sprk_graphitemid` value used in the key per ADR-044 so the match is reliable (two representations of the same id can't both insert — the real duplicate hole).

## Implementation steps

1. **`Spaarke.Dataverse` (shared lib, base layer)** — add `Task<Guid> UpsertAsync(Entity entity, CancellationToken ct)` to `IGenericEntityService` (entity's `KeyAttributes` carries the alt-key). Implement in `DataverseServiceClientImpl` via `Microsoft.Xrm.Sdk.Messages.UpsertRequest` (returns `UpsertResponse.Target.Id` + `RecordCreated`). Add the Web-API impl as `throw new NotImplementedException(...)` (existing pattern — the BFF runtime always resolves the ServiceClient impl; see the `RetrieveMultipleAsync(FetchExpression)` precedent). **Layer check**: additive to the base lib; run `tests/Spaarke.ArchTests/LayerDependencyTests.cs`.
2. **`ComposeService.PromoteIfEphemeralAsync`** — in the absent branch, set `entity.KeyAttributes["sprk_graphitemid"] = Canonicalize(request.DocumentSpeId)` (ADR-044) and call `UpsertAsync(entity)` instead of `CreateAsync` + the catch. Keep the read-first branch + rebind/graduate. Simplify/remove the now-dead catch (or keep a thin defensive re-resolve). Preserve `WasCreated` (UpsertResponse.RecordCreated) for the result.
3. **Seam test** (`tests/integration/seam/**`, ADR-038 DoD): concurrent/repeated first-saves for the same drive-item → exactly ONE sprk_document row (assert the upsert path, mock the facade to simulate key-match). Retried first-save from the same door → no second row.
4. **BFF gates**: `dotnet build -c Release`; publish ≤60 MB, delta vs **44.96 MB incl PDBs** (net10) — additive method, expect ~0 delta; no new HIGH CVE; `/conflict-check` before the BFF PR; Placement Justification (work stays in `Services/Compose/` + one additive `IGenericEntityService` method in the base lib — reduces coupling by exposing a standard primitive, no new service/endpoint/package). `docxBridge.ts` untouched.

## ADR-044 canonicalization note
Confirm how `sprk_graphitemid` is stored (SPE drive-item id — likely an opaque string, not a GUID). If it is NOT a GUID, ADR-044 GUID canonicalization may be a no-op / not applicable — in that case document that the alt-key match is exact-string and the canonicalization constraint is satisfied trivially. If any door stores a GUID-shaped value, apply ADR-044. Resolve this empirically during implementation before finalizing the key build.

## Sequencing
012 is done (client fork). 013 is the final FR-07 vector. After 013, Phase 1 (Save-Identity Fix / UC-8) is complete → 020 (Save dropdown) unblocks.
