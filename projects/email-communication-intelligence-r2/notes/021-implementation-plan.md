# Task 021 — race-proof internet-message-id dedup — IMPLEMENTATION PLAN (investigation done 2026-08-06)

> Prereqs confirmed: **020 key ACTIVE** (`sprk_InternetMessageIdKey`, unique, over `sprk_internetmessageid`).
> Duplicate-insert error (captured via task 020): **HTTP 412 / error code `0x80060892`**, message *"Entity Key
> Internet Message Id Key violated…"*. Conflict-check clean (no open PR on Services/Communication or Services/Office).
> Rigor FULL, opus·xhigh. This plan is turnkey — execute in a fresh session for full context budget.

## Call-site map (verified)

**Capture path — `src/server/api/Sprk.Bff.Api/Services/Communication/IncomingCommunicationProcessor.cs`**
- L126 `ProcessAsync` Step-1 dedup: `if (await ExistsByGraphMessageIdAsync(graphMessageId, ct)) return;` — per-mailbox fast-path.
- L480 `ExistsByGraphMessageIdAsync` → delegates to `_communicationService.ExistsCommunicationByGraphMessageIdAsync`; try/catch → false (non-fatal). Layer 1-4 comment at L482-486.
- L531 `CreateCommunicationRecordAsync` → sets `sprk_internetmessageid` (L572) → creates at **L598** via `_genericEntityService.CreateAsync(communication, ct)`. **This create is what must become race-proof.**

**CommunicationService — `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs`**
- Existing fast-path: `ExistsCommunicationByGraphMessageIdAsync` (mailbox).
- SB idempotency helper: `IncomingMessagingJobHandler.IdempotencyKeyFor(acsMessageId)` (L803 usage) — keyed on ACS/graph id today.
- `StampInternetMessageIdAsync` (L2508) already patches `sprk_internetmessageid` — reference for field name.

**Dataverse seam — `src/server/shared/Spaarke.Dataverse/{ICommunicationDataverseService,DataverseServiceClientImpl,DataverseWebApiService}.cs`**
- Has `ExistsCommunicationByGraphMessageIdAsync`. **Add**: `ExistsCommunicationByInternetMessageIdAsync` + `QueryCommunicationByInternetMessageIdAsync(returns Guid? / Entity?)`. `DataverseServiceClientImpl` = real (SDK); `DataverseWebApiService` = NotImplemented stub (mirror task 031 pattern).

**Upload path — `src/server/api/Sprk.Bff.Api/Services/Office/`**
- `OfficeService.GenerateIdempotencyKey` (L423-429) **already** includes `request.Email?.InternetMessageId` in the SB idempotency key ✅ (upload SB-layer dedup mostly satisfied; verify + tighten to internet-message-id-primary).
- `OfficeDocumentPersistence` L109 `updateRequest.EmailMessageId = request.Email.InternetMessageId`; L179 `GetProcessingJobByIdempotencyKeyAsync`. **Check**: does the office save create a `sprk_communication` (vs only `sprk_document`)? From task 041 investigation, `OfficeService.SaveAsync` creates a `sprk_document` (no communication) — so the FR-B3 "upload dedups against capture via internet-message-id at the COMMUNICATION level" is really task 043's unification. For 021 scope: ensure the SB idempotency key on the upload path is internet-message-id-derived (it is) and document that communication-level upload dedup lands with 043. **Do NOT build the office→communication create here** (that's 043).

## Implementation steps (execute in fresh session)

1. **Dataverse seam** — add to `ICommunicationDataverseService`:
   - `Task<Guid?> QueryCommunicationIdByInternetMessageIdAsync(string internetMessageId, CancellationToken ct = default)`
   - `Task<bool> ExistsCommunicationByInternetMessageIdAsync(string internetMessageId, CancellationToken ct = default)`
   Implement in `DataverseServiceClientImpl` (QueryByAttribute on `sprk_internetmessageid`, ColumnSet minimal, Top 1); NotImplemented stub in `DataverseWebApiService`. Model on the existing graph-message-id methods.

2. **Race-proof create in `CommunicationService`** — add a helper
   `Task<(Guid id, bool wasDuplicate)> CreateCommunicationDedupedAsync(Entity communication, string? internetMessageId, CancellationToken ct)`:
   - If `internetMessageId` is null/blank → plain `CreateAsync` (no key applies; e.g. drafts).
   - Else: try `_genericEntityService.CreateAsync(communication, ct)` → return `(id, false)`.
   - **Catch the duplicate-key fault** — over the SDK (`DataverseServiceClientImpl` uses `IOrganizationService`), a unique alternate-key violation surfaces as `FaultException<OrganizationServiceFault>` with `Detail.ErrorCode == unchecked((int)0x80060892)` (the same code captured via Web API). Catch it, then `QueryCommunicationIdByInternetMessageIdAsync` → return `(existingId, true)`.
   - ⚠️ **ESCALATION GATE (POML)**: confirm empirically that `_genericEntityService.CreateAsync` propagates a *catchable, deterministic* fault for the 020 key (write a focused test / hand-trace `GenericEntityService.CreateAsync` exception mapping). If it swallows/rewraps into a non-deterministic type, STOP and escalate per §6/§6.5 — do NOT fall back to check-then-insert-only (violates NFR-02).
   - Whole helper wrapped so a *reconcile* failure is non-fatal (NFR-04) — worst case logs + proceeds.

3. **Wire capture path** — in `IncomingCommunicationProcessor`:
   - Keep the L126 graph-id fast-path (telemetry parity).
   - Add an internet-message-id fast-path pre-check (cheap) BEFORE create.
   - Route `CreateCommunicationRecordAsync`'s create (L598) through `CreateCommunicationDedupedAsync`; if `wasDuplicate`, log "reconciled to canonical row {id}" and short-circuit the rest of processing (attachments/association already exist on the canonical row) — mirror the existing "Duplicate detected … Skipping" early-return semantics.

4. **Service Bus idempotency** — ensure the inbound email enqueue path derives its idempotency key from `internetMessageId` (canonical) rather than per-mailbox graph id, so cross-mailbox redelivery dedups at the queue layer (ADR-004). Reuse/extend `IncomingMessagingJobHandler.IdempotencyKeyFor`. Keep graph-id as a secondary/telemetry key.

5. **Tests** (ADR-038 KEEP paths):
   - `tests/integration/seam/Communication/MessageIdDedupSeamTests.cs` (NEW): N-mailboxes/M-users same internet-message-id → exactly one `sprk_communication`; concurrent-insert race → one create succeeds, the other catches `0x80060892` and reconciles (no second row, no unhandled throw); null internet-message-id → not deduped (multiple allowed).
   - Unit: `CreateCommunicationDedupedAsync` reconcile branch (dup-key fault → returns existing id, wasDuplicate=true) + non-fatal branch (reconcile query throws → logged, primary not failed). Module-boundary Moq on `ICommunicationDataverseService`/`IGenericEntityService` only; no banned shapes.

6. **Verify**: `dotnet build` + `dotnet test` (Communication+seam); `dotnet publish -c Release` compressed size + delta (baseline ~50.9 MB incl PDBs); `dotnet list package --vulnerable`. Step 9.5 code-review + adr-check. Placement Justification (extends Services/Communication in place — no new service). `/conflict-check` before PR.

7. Update TASK-INDEX 021 → ✅; note any deviation (esp. the actual catchable fault type from step-2 escalation gate) in notes/.

## Scope guard
- 021 delivers the **capture-path** race-proof dedup + SB key + the Dataverse seam methods. The **upload→communication** unification (office save producing/deduping a `sprk_communication`) is **task 043** (not here) — 021's upload obligation is satisfied by the internet-message-id-derived SB idempotency key already present in `OfficeService.GenerateIdempotencyKey`. Document this boundary in the PR so FR-B3's "upload dedups against capture" is correctly attributed to 043.
