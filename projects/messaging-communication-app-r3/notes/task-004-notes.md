# Task 004 — FR-17: participant-based thread naming + BFF rename endpoint (edit-preserve)

> Spec FR-17 + Success Criterion 6. Rigor: FULL. No Dataverse plugin (hard MUST NOT).

## What shipped

1. **Participant roll-up naming** for record-less threads — `ThreadResolver.BuildParticipantRollupNameAsync`,
   wired into `ReDeriveThreadNameAsync`'s no-anchor branch (replacing the generic "Conversation" fallback).
2. **Atomic rename write** — `IThreadResolver.RenameThreadAsync` sets `sprk_name` + flips
   `sprk_nameisautoderived = false` (Edited) in ONE `UpdateAsync`.
3. **BFF rename endpoint** — `POST /api/communications/threads/{threadId}/rename`.
4. **Visibility check** — `CommunicationThreadReadService.CanCallerSeeThreadAsync` (impersonated existence probe).

## Files changed

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Communication/IThreadResolver.cs` | Added `RenameThreadAsync` to the interface |
| `src/server/api/Sprk.Bff.Api/Services/Communication/ThreadResolver.cs` | `RenameThreadAsync` impl; `BuildParticipantRollupNameAsync` + `BuildParticipantDisplayName`; wired roll-up into `ReDeriveThreadNameAsync` no-anchor branch; roll-up constants |
| `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationThreadReadService.cs` | `CanCallerSeeThreadAsync` (impersonated visibility probe) |
| `src/server/api/Sprk.Bff.Api/Services/Communication/Models/RenameThreadRequest.cs` | NEW request DTO (`{ name }`) |
| `src/server/api/Sprk.Bff.Api/Services/Communication/Models/RenameThreadResponse.cs` | NEW response DTO (`{ threadId, name }`) |
| `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs` | Mapped `POST /threads/{threadId}/rename` + handler |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/ThreadResolverTests.cs` | 6 new tests (roll-up, truncation, fallback, rename write, blank guard, rename→re-derive no-op) |
| `tests/integration/contract/Api/Communication/CommunicationRenameThreadContractTests.cs` | NEW endpoint contract test (401/400/**403**/200) |

## Naming roll-up rule

- Runs only in `ReDeriveThreadNameAsync`'s **no-anchor** branch. The master-thread guard
  (`sprk_regardingrecordtype == "systemuser"` → return) and the marker gate (`sprk_nameisautoderived == false`
  → return; `true`/null → re-derive) are checked **before** the branch and are unchanged.
- Two bounded queries (NFR-07, no per-row fan-out): (1) the thread's messages
  (`sprk_communication` where `sprk_communicationthread == threadId`, `TopCount 200`); (2) those messages'
  participants (`sprk_communicationparticipant` where `sprk_communication In (messageIds)`, `TopCount 500`).
  **REUSES the existing ADR-024/ADR-048 message-grain junction — no second participant/person store.**
- Display name per row: `sprk_systemuser`.Name → `sprk_contact`.Name → `sprk_addresstext` (SDK populates the
  lookup `EntityReference.Name`). Distinct (case-insensitive), ordered **deterministically** (ordinal,
  case-insensitive — independent of query order), rendered `"Alice, Bob, +N"` (up to 3 shown), truncated to 200.
- No messages / no usable name → returns `null` → caller keeps the `"Conversation"` fallback.

## Rename contract

- **Route**: `POST /api/communications/threads/{threadId:guid}/rename` (distinct from
  `GET .../messages`, `GET .../unread-count`, `POST /threads/direct` — POST + literal `/rename` segment).
- **Body**: `{ "name": "<string>" }`. **Response**: `{ "threadId": <guid>, "name": "<persisted>" }` (200).
- **Auth (ADR-028)**: `.AddEndpointFilter<CommunicationAuthorizationFilter>()`; caller resolved **server-side**
  via `ICallerSystemUserResolver` (never client-supplied).
- **Marker semantics (edit-preserve)**: the write sets `sprk_name` AND `sprk_nameisautoderived = false` (Edited)
  **atomically in one `UpdateAsync`**. On the next `ReDeriveThreadNameAsync`, the marker gate short-circuits →
  the user's name is never overwritten.

### Enforcement of the four non-negotiables

- **No plugin (hard MUST NOT)**: the marker flip happens ONLY in `ThreadResolver.RenameThreadAsync`, called only
  by the BFF endpoint. No plugin file added (`git diff` is BFF `.cs` + tests + models only).
- **Edit-preserve**: `sprk_nameisautoderived == false` gate in `ReDeriveThreadNameAsync` (unchanged) + the atomic
  rename write. Verified by `RenameThenReDerive_OnEditedThread_IsNoOp_NamePreserved` and the existing
  `ReDeriveThreadNameAsync_WhenMarkerEdited_PreservesName`.
- **403 / 400**: endpoint validates non-blank name → 400; `CanCallerSeeThreadAsync` (impersonated) returns false →
  403; unresolved caller → 403 (fail-closed, `ResolveCallerOrThrowAsync`). A caller renaming a thread they cannot
  see never reaches the write.
- **Re-derive stays best-effort/non-fatal** (NFR-02): the roll-up runs inside `ReDeriveThreadNameAsync`'s existing
  try/catch. `RenameThreadAsync` is deliberately NOT swallowed (a user action's failure must surface → 500).

## Escalation — NOT triggered

The POML escalation fires only if edit-preserve **cannot** be guaranteed via `sprk_nameisautoderived` alone. It
can: `ReDeriveThreadNameAsync` is the ONLY re-derive write path and it honors the marker gate; the RegardingResolver
PCF writes the *regarding* client-side but does **not** compute/write `sprk_name` (trigger wiring is out of scope,
documented in `ThreadResolver` XML docs), so no client path re-derives over an Edited name. No plugin needed. **No
escalation.**

## Placement Justification (per `.claude/constraints/bff-extensions.md`)

- **Belongs in BFF**: extends the existing `Services/Communication/` resolver + read model and adds ONE write
  endpoint inside `Api/CommunicationEndpoints.cs`. The rename is a client-initiated Dataverse write that requires
  server-side caller resolution + impersonated authorization — exactly the BFF's job; the only alternative trigger
  (a Dataverse plugin) is a hard MUST NOT.
- **No new package / AI / background work / DI module**: reuses `IGenericEntityService`,
  `IImpersonatedCommunicationQuery`, `ICallerSystemUserResolver`, `CommunicationAuthorizationFilter` (all already
  registered). No new CRUD→AI dependency. `§11` reuse: the participant roll-up reuses the ADR-024/ADR-048 junction;
  visibility reuses the read model's impersonation seam.
- **ADRs**: ADR-045 (non-fatal re-derive), ADR-024 (junction reuse, no second regarding mechanism), ADR-028
  (auth v2, server-side caller, no OBO/credential `new`), ADR-008 (endpoint filter), ADR-010 (feature-module DI —
  no new registration needed), ADR-038 (unit extend + contract test incl. negative-auth).

## Verification

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 errors (19 pre-existing warnings) |
| Tests — `Services.Communication` | 553 passed, 5 pre-existing skips |
| Tests — `Seam.Communication` + rename contract | 43 passed |
| Tests — rename contract + `ThreadResolverTests` | 29 passed |
| Publish (compressed, incl PDBs) | **45.74 MB** vs ~46 MB baseline → **delta ≈ 0** (≤60 MB ceiling) |
| `dotnet list package --vulnerable --include-transitive` | 0 NEW HIGH CVE (only pre-existing `System.Security.Cryptography.Xml 8.0.3`) |
