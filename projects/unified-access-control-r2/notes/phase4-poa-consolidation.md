# Task 060 — POA share client consolidation

> **Written**: 2026-09-08, session 4. Step 1 deliverable (diff BEFORE code), per POML `<steps>` order 1
> and the §F.3 Empirical-Reproduction-FIRST spirit.

## 1. The two implementations, diffed precisely

Both ultimately POST the same two Dataverse actions (`GrantAccess` / `RevokeAccess`) with the same
envelope shape. They differ in principal kind, capability, auth path and error policy.

| Axis | `IDataverseAccessGrantService` → `DataverseWebApiService` | `PlaybookSharingService` private helpers |
|---|---|---|
| **Grant site** | `DataverseWebApiService.GrantAccessAsync` (`:1057`) | `GrantAccessToTeamAsync` (`:302`) |
| **Revoke** | ❌ none | ✅ `RevokeAccessFromTeamAsync` (`:331`) |
| **Principal kind** | `systemusers({id})` — hardcoded | `teams({id})` — hardcoded |
| **Target entity set** | caller-supplied (`entitySetName` param) | `sprk_analysisplaybooks` — hardcoded const |
| **AccessMask value** | caller-supplied CSV literal (`"ReadAccess"`) | `MapToDataverseAccessRights(PlaybookAccessRights)` → the same CSV literal |
| **Payload shape** | `Target.@odata.id` + `PrincipalAccess.{Principal.@odata.id, AccessMask}` | **identical** |
| **Revoke payload** | — | `Target.@odata.id` + `Revokee.@odata.id` |
| **Error policy (write)** | `EnsureSuccessStatusCode()` | `EnsureSuccessStatusCode()` — identical |
| **HTTP client** | `DataverseWebApiService`'s own client; MI (`DefaultAzureCredential`) or auth-v4 ordered confidential client | its own typed `HttpClient` + injected `TokenCredential` (= `ManagedIdentityCredentialFactory`) |
| **POA read** | `GetSharedSystemUserIdsAsync` (`:1092`) — `$select=principalid`, **assumes** every share is a systemuser, fails soft → `[]` | `GetSharedTeamsAsync` (`:377`) — `$select=principalid,accessrightsmask,modifiedon`, **assumes** every share is a team, fails soft → `[]` |
| **ObjectTypeCode resolve** | `GetEntityObjectTypeCodeAsync` (`:1025`) — **cached** (`ConcurrentDictionary`) | `GetEntityTypeCodeAsync` (`:430`) — **uncached**, re-queries every call |

### AccessMask semantics — NO disagreement, so the POML escalation trigger does NOT fire

The POML's `<escalation><trigger>` fires only "if the two implementations disagree on AccessMask
semantics". They do not. **Both send a CSV of Dataverse access-right literals in the same
`PrincipalAccess.AccessMask` slot.** The playbook path merely computes that CSV from a flags enum
(`Read`→`ReadAccess`; `Write`→`WriteAccess,AppendAccess,AppendToAccess`; `Share`→`ShareAccess`)
*before* handing it over. That mapping is a **caller-level** concern and stays in
`PlaybookSharingService`; the seam keeps taking the CSV literal verbatim, so no existing consumer's
granted rights change. Escalation not required — recorded here so the decision is auditable.

## 2. Two latent bugs the consolidation removes (behaviour changes — deliberate, documented)

1. **`GetSharedTeamsAsync` reported non-team principals as teams.** Its POA query has no
   `principaltypecode` filter, so a *user* share on a playbook came back as a `SharedWithTeam` with
   `TeamName = "Unknown Team"` (the name lookup against `teams({userId})` 404s and is swallowed).
   The consolidated read returns a **typed** principal, and the playbook path filters `kind == Team`.
2. **The uncached ObjectTypeCode lookup is gone.** `PlaybookSharingService` re-queried
   `EntityDefinitions` on every `GetSharingInfoAsync`; the seam's resolution is process-cached
   (object type codes are immutable for the environment's lifetime).

Symmetrically, `GetSharedSystemUserIdsAsync`'s documented "assume every share is a systemuser"
assumption is now **enforced rather than assumed** — it filters on principal kind. That strictly
tightens `DirectThreadAccessService`'s no-leak property; it cannot loosen it.

## 3. Naming + placement decision (POML `<goal>` requires this be stated)

| Decision | Choice | Why |
|---|---|---|
| **Interface name** | `IDataverseAccessGrantService` → **`IDataverseRecordShareService`** | The seam no longer only *grants*: it grants, revokes and reads. Keeping "Grant" in the name of a type that revokes is the kind of drift this project keeps paying for. |
| **Method names** | `GrantAccessAsync` / `RevokeAccessAsync` / `GetPrincipalAccessAsync` | Named for the Dataverse actions they invoke (`GrantAccess`, `RevokeAccess`, POA read), so the wire call is obvious at the call site. |
| **Principal** | `DataversePrincipalRef(DataversePrincipalKind Kind, Guid Id)` in `Spaarke.Dataverse` | One parameterized shape, **not** overloaded per-kind copies (acceptance criterion 3). Lives in `Spaarke.Dataverse` so the primitive and the BFF seam share one type — no duplication across the assembly boundary. |
| **Placement** | moved `Services/Communication/Access/` → **`Services/Access/`** | The seam is now cross-cutting: Communication (threads), Ai (playbooks), and Secure Projects next (061/063). Leaving a cross-cutting authorization seam under `Communication/` would misfile the write path FR-28/FR-29 depend on. |
| **Concrete primitives** | extended in place on `DataverseWebApiService` | POML: "extend, never duplicate". It is already the BFF's generic Web API singleton and already hosts the POA primitives. |
| **Extension point** | `DataversePrincipalKind` enum: `SystemUser`, `Team` only | Documented as the extension point for future kinds (e.g. `AccessTeam`); **none added now** per the POML. |

## 4. What is deliberately NOT consolidated

- `PlaybookSharingService.GetTeamNameAsync` — a `teams` display-name lookup, not POA.
- `PlaybookSharingService.MapToDataverseAccessRights` / `MapFromDataverseAccessRights` — the
  playbook-domain rights enum ↔ Dataverse mask translation. Caller-level by the §1 finding above.
- `SetOrganizationWideAsync` — a documented no-op stub, unrelated to POA.

## 5. What was built

| File | Change |
|---|---|
| `src/server/shared/Spaarke.Dataverse/DataversePrincipalRef.cs` | **NEW** — `DataversePrincipalKind` (values ARE Dataverse's `principaltypecode`: 8/9), `DataversePrincipalRef`, `DataversePrincipalAccess`, and the entity-set / type-code mappings. |
| `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` | `GrantAccessAsync` generalized to any principal kind; **`RevokeAccessAsync` added**; `GetSharedSystemUserIdsAsync` → `GetPrincipalAccessAsync` (selects `principaltypecode` + `accessrightsmask` + `modifiedon`, returns typed principals). |
| `src/server/api/…/Services/Access/IDataverseRecordShareService.cs` | **NEW location + name** for the seam (was `Services/Communication/Access/IDataverseAccessGrantService.cs`, deleted). Grant + Revoke + read. |
| `src/server/api/…/Services/Communication/Access/DirectThreadAccessService.cs` | Consumes the renamed seam; a private `GetSharedSystemUserIdsAsync` projects the typed read back to systemuser ids. |
| `src/server/api/…/Services/Ai/PlaybookSharingService.cs` | Private POA helpers **deleted**; `GrantAccessToTeamAsync` / `RevokeAccessFromTeamAsync` are now one-line delegations, and `GetSharedTeamsAsync` reads through the seam. Its own `GetEntityTypeCodeAsync` is gone. |
| `Infrastructure/DI/CommunicationModule.cs` | One registration, renamed. Still unconditional (ADR-010, no asymmetric registration). |
| `tests/Spaarke.ArchTests/PoaShareClientSingletonGuardTests.cs` | **NEW** — 3 guards keeping the consolidation from regressing. |
| `tests/unit/…/Services/Access/DataversePrincipalRefTests.cs` | **NEW** — the principal parameterization. |
| `tests/unit/…/Services/Communication/DirectThreadAccessServiceTests.cs` | Migrated to the new seam; **one new negative** — a team share on a Direct thread is not a participant. |
| `tests/integration/Spe.Integration.Tests/DataverseRecordShareRoundTripTests.cs` | **NEW**, LIVE-gated — grant→read→revoke→read for both kinds, revoke-without-grant, error propagation. |

## 6. Test strategy — why the round-trip is LIVE-gated

Acceptance criteria 2–4 are claims about **Dataverse**: that `RevokeAccess` removes the row `GrantAccess`
wrote, that revoking an absent share is a no-op, and that a failed action surfaces. A mock cannot be
wrong about any of them — it would assert our own assumptions back at us, which is precisely the
wiring-test failure mode ADR-038 §7 bans. `Mock<HttpMessageHandler>` is banned outright, so the honest
options were a live test or none. Live it is, following the `DataverseWebApiFieldMappingRegressionTests`
`SkippableFact` precedent already in this repo.

**What holds the line in CI** (always-on, no environment needed):
- `PoaShareClientSingletonGuardTests` — exactly one file constructs POA payloads; the seam exposes
  revoke; `PlaybookSharingService` holds no private client. **Perturbation-checked**: seeding a
  duplicate `PostAsJsonAsync("GrantAccess", …)` into `PlaybookSharingService` turned 2 of the 3 red,
  and removing the seed turned them green again.
- `DataversePrincipalRefTests` — the mappings that decide *which* principal a share lands on.
- `DirectThreadAccessServiceTests` — the no-leak negatives, unchanged in expectation, plus the new
  team-share negative.

## 7. Behaviour deltas a reviewer should look at deliberately

1. **PlaybookSharingService's POA calls moved auth paths.** They used to go over its own typed
   `HttpClient` + injected `TokenCredential`; they now go over `DataverseWebApiService`'s client. Both
   are app-only against the same environment and, with `Graph:ManagedIdentity:Enabled`, both resolve to
   the same UAMI — so this is identity-preserving and moves the calls onto the ADR-028 path. Its
   non-POA calls (team names) still use its own client.
2. **`GetSharedTeamsAsync` now filters to teams** (§2 item 1) — a user share no longer appears as
   "Unknown Team".
3. **`GetParticipantSystemUserIdsAsync` now filters to systemusers** — strictly tightens no-leak.
4. **Errors still propagate on the write path, unchanged**: both grant and revoke
   `EnsureSuccessStatusCode()`; both POA reads still fail soft to empty, as before.
