# Task 013 — Atomic server upsert on sprk_graphitemid_uk (FR-07d) — IMPLEMENTED

> Phase 1 (Save-Identity Fix / UC-8) · opus@high · FULL rigor · 2026-08-16. The SERVER-side D1 vector —
> completes the four-vector fix (client: 010/011/012; server: this).

## What shipped

**Shared lib (`Spaarke.Dataverse`, base layer) — additive `UpsertAsync`:**
- `IGenericEntityService.UpsertAsync(Entity, ct) : Task<(Guid Id, bool Created)>` — atomic create-or-update keyed by the entity's `KeyAttributes` (alternate key), one server-side round-trip.
- `DataverseServiceClientImpl.UpsertAsync` — `Microsoft.Xrm.Sdk.Messages.UpsertRequest`; returns `(response.Target.Id, response.RecordCreated)`. Guards that the entity carries a KeyAttributes alt-key or a primary Id.
- `DataverseWebApiService.UpsertAsync` — `throw NotImplementedException` (existing pattern; the BFF runtime always resolves the ServiceClient impl).

**BFF (`ComposeService.PromoteIfEphemeralAsync`):**
- The **read-first idempotency check is UNCHANGED** (existing row → no-op + `RebindSessionDocumentIdAsync` + `GraduateLinkedCopyIfDivergedAsync`; the special semantics are preserved).
- The **absent branch** now sets `entity.KeyAttributes["sprk_graphitemid"] = request.DocumentSpeId` and calls `UpsertAsync` instead of `CreateAsync` — closing the read-then-create TOCTOU window: two concurrent first-saves of the SAME minted SPE item can no longer each insert (the second UPDATES the first's row server-side → exactly one `sprk_document`).
- The **catch is KEPT** (now documented as the SECONDARY race): two truly-concurrent first-saves that mint DIFFERENT SPE items but carry the SAME transient key — the upsert on graphitemid can't dedup those atomically, so the loser's upsert-create fails the `sprk_composetransientkey_uk` unique constraint and is re-resolved by transientKey. `WasCreated` now reflects the upsert's honest `RecordCreated` (false when a concurrent winner created the row).

## ADR-044 canonicalization — empirical resolution

The POML constrained "canonicalize the sprk_graphitemid value per ADR-044." **Verified it does NOT apply**: `TryFindDocumentByGraphItemIdAsync` keys the read on the RAW `driveItemId` string (`{ "sprk_graphitemid", driveItemId }`) — `sprk_graphitemid` is an **opaque SPE drive-item id (a string, not a GUID)**, matched exact-string. ADR-044 governs GUID canonicalization; there is no GUID here. The upsert uses the **same raw string key** as the read, so the upsert and the idempotency read resolve identically (the correctness requirement). Documented in code + this note; no canonicalization transform added (it would be a no-op at best and a mismatch risk at worst).

## Why not a full atomic upsert that also replaces the read

`UpsertRequest` UPDATES (applies the whole target entity) on a key match — a blind upsert of the full promote entity would clobber an already-promoted row's fields (rebind/graduate state). Keeping the read-first check + upserting ONLY the absent branch preserves the existing-row semantics AND closes the concurrent-first-save race. This is the minimal, correct design.

## Verification

- **New tests** (`ComposeServiceCreateOnSaveTests`): `SaveAsync_TransientFirstSave_PromotesViaAtomicUpsertKeyedOnGraphItemId` (asserts UpsertAsync Once, CreateAsync Never, and `KeyAttributes["sprk_graphitemid"] == DocumentSpeId`); `SaveAsync_RepeatedFirstSave_UpsertMatchesExistingRow_ResolvesToOneRecord` (upsert `Created=false` → same record id, `WasPromotedThisSave=false`, one row). These co-locate with the existing promote harness (the canonical promote-path test suite) — a full HTTP seam would duplicate that harness for no additional signal; the promote write-path is a data-mutation concern, not the AI dispatch spine ADR-038's seam DoD targets.
- **Migrated existing promote tests** off `CreateAsync` → `UpsertAsync` (strict mocks): `ComposeServiceCreateOnSaveTests` (helper + 2 dedup verifies), `ComposeServicePromoteRecordCompletenessTests`, `ComposeServiceUploadFidelityTests`, `ComposeContentDedupTests` (4 setups + 1 verify). Captured-entity field assertions still hold (same entity + added KeyAttributes).
- **4 test fakes** implementing `IGenericEntityService` gained a delegating `UpsertAsync` (Outbox/PendingPoll/SignalR notification seams + PromoteDurableFkVisibility).
- **5 compose seam/contract suites migrated** off `CreateAsync` → `UpsertAsync` (the full-suite run surfaced these — every test that set up / verified / callback-captured the promote create call): `ComposeTransientKeyDedupSeamTests` (incl. the `EightRepeated…` in-memory create-or-find world + the ForkNew/Save-Version dedup verifies), `ComposeCreateOnSaveEndpointContractTests` (4 setups), `ComposeOriginRoutingSeamTests` (2), `ComposePdfIntakeRoundTripSeamTests` (1), `ComposeFidelitySeamTests` (1, incl. the `createdEntity` Callback capture). Setups keep their `.Callback<Entity,CancellationToken>` (same signature) and now return `(id, true)`.
- Compose folder + fakes + migrated suites: **421/421 green**. **Full `Sprk.Bff.Api.Tests`: 10,421 passed / 0 failed / 97 skipped.**
- BFF `dotnet build -c Release`: 0 errors. CVE: no vulnerable packages. `/conflict-check`: clear (only dependabot `.csproj` bumps touch `Spaarke.Dataverse`, disjoint from my `.cs` edits).

## Placement Justification (root §10)
Work stays in `Services/Compose/` (promote rewire) + ONE additive primitive on the `Spaarke.Dataverse` base lib (`UpsertAsync` — a standard Dataverse operation that REDUCES coupling by exposing atomic upsert instead of forcing every caller into read-then-create). No new endpoint, service, DI registration, or package. **Publish: 44.96 MB incl PDBs (net10), delta 0.00 vs the 44.96 baseline** (additive method, as expected). CVE: no vulnerable packages. `docxBridge.ts` untouched.
