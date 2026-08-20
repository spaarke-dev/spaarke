# Design — Dataverse Access-Layer Unification (R1)

> ## 🟡 PAUSED — 2026-08-19 (open, not archived)
>
> A validation pass ([`notes/validation-2026-08-19.md`](notes/validation-2026-08-19.md)) re-derived this
> project's justification from code and found it **substantially weaker than the design claimed**: of three
> surviving justifications, **one is intact, one is heavily defused, and one is false**; the "two-stack" framing
> is wrong (there are **five** Dataverse access stacks — this project retires **one**, and it is the one the
> interim hardening already fenced); and the deletion scope was mis-drawn (`DataverseWebApiClient` has 45 refs
> across 16 files and cannot be deleted here).
>
> **Assessment: not necessary; as scoped, more risk than reward.** RED-4 itself classified this work as
> OPTIONAL — *"not required to remove the traps (B does that)"* — and option B shipped.
>
> **Operator decision (2026-08-19)**: keep the project **open but paused**. Re-evaluate after
> `spaarke-auth-v4-dataverse-MI` completes, and whenever a resume trigger fires (see "Pause & resume" below).
> Three residual items were **extracted** to the hardening track — they do not wait on this project.
>
> Everything below the Pause section is the design **as it would be executed if resumed**; it has been corrected
> for the validation findings, so a resumed project starts from an accurate map rather than the stale one.

> **Status**: PAUSED 2026-08-19 (was: INITIALIZED, design only) · **Surface**: `Spaarke.Dataverse` + BFF · **Risk**: HIGH
> **Grounding**: `../code-quality-and-assurance-r3/notes/red-item-analyses/RED-4-dataverse-two-stack-ASSESSMENT.md` (Fable-verified)
> **Also grounding**: [`docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md`](../../docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md) (canonical routing map, post-hardening) ·
> [`notes/auth-v4-coordination-memo.md`](notes/auth-v4-coordination-memo.md) (auth-v4 division of labour — §"Coordination with auth-v4" below)
> **Baseline re-verified**: 2026-08-19 against `master` (see "Problem" for the deltas since the RED-4 assessment)

## Hot-Path Declaration (CLAUDE.md §10)

```xml
<hot-path-declaration>
  <bff>Y</bff>                <!-- Spaarke.Dataverse shared lib consumed by BFF; DI in GraphModule -->
  <spaarke-ai>N</spaarke-ai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>Y</skill-directives> <!-- authors an ADR (.claude/adr + docs/adr), main-session-only -->
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

## Problem (verified)

Two `IDataverseService` impls: `DataverseServiceClientImpl` (SDK, primary) + `DataverseWebApiService` (REST,
serves events/field-mapping/impersonation/POA). The split is **historical** (REST built to avoid WCF on .NET 8)
— the SDK path already does raw OData PATCH (`ExecuteWebRequest`) AND `CallerId` impersonation
(`DataverseServiceClientImpl.cs:1944-1949`, inside the impersonated-PATCH block `:1940-1963`; read-path
precedent at `UserPrivilegeChecker.cs:143-148`), so it is not a hard capability boundary. The split produces a
**split-brain routing trap**: one composite interface fronting two impls, where the SDK impl's event and
field-mapping methods are stubs — a mis-route bug already shipped (`GraphModule.cs:74-77`).

### What the interim hardening + #3b already changed (re-measured 2026-08-19 on `master`)

Three of this design's original premises have moved. The remaining problem is smaller and sharper:

| Original premise | Status today | Consequence for this project |
|---|---|---|
| "~1,100 LOC of runtime-dead duplicate code" | **Deleted** by RED-4 B (−1,414 LOC); `DataverseWebApiService` narrowed from the composite `IDataverseService` to `: IEventDataverseService, IFieldMappingDataverseService` | Phase 2's porting surface is the **live** event / field-mapping / impersonation / POA code only — no dead code to reconcile |
| "two ~2,800-LOC god-classes" | **One**: `DataverseServiceClientImpl` **2,975**; `DataverseWebApiService` is **1,468** and its waiver was removed 2026-08-16 | Phase 4 removes **one** waiver, not two (README graduation criterion is stale on this point) |
| "secret-based auth on both" | **#3b landed (2026-08-17, live on dev)**: both impls are MI-first. `DataverseWebApiService.cs:55-86` is flag-gated on `Graph:ManagedIdentity:Enabled` (secret only as local-dev fallback, `:70-86`); `DataverseWebApiClient.cs:42-54` picks `ClientSecretCredential` on **secret presence alone** and otherwise falls through to `DefaultAzureCredential` | The credential work is **residual cleanup, not a migration**. Deleting `DataverseWebApiService` is secret-shaped-code hygiene, **not** a security win — it does not block removal of `BFF-API-ClientSecret` (it degrades to MI). State it that way in the PR. |
| "delete `DataverseWebApiService` **+ `DataverseWebApiClient`**" | **`DataverseWebApiClient` is NOT a two-consumer cleanup** — **45 references across 16 consumer files** (verified 2026-08-19): registered singleton in `SpeAdminModule.cs:56` (not `GraphModule`), injected across all 4 `Api/SpeAdmin/*Endpoints.cs`, all 6 `Api/ExternalAccess/*Endpoint.cs`, `SpeAdminGraphService.cs` (the 4,911-LOC RED-1 god class), `SpeAuditService`, `SpeDashboardSyncService`, `RegistrationDataverseService`, `ScopeManagementService`. It is a **third, independent REST stack**, not part of the `IDataverseService` family, and `DataverseWebApiService` never consumes it | **Removed from scope** — see "Non-goals". Phase 3 deletes `DataverseWebApiService` only. The T3-halving claim inherited from the coordination memo is **withdrawn**. |

**Pre-existing ratchet red (not caused by this project, but Phase 4 owns half of it)**: `GodClassGuardTests`
currently **fails on `master`** — `DataverseServiceClientImpl` is 2,975 vs its 2,864 waiver (+100 grace = 2,964,
over by 11, grown by #3b) and `ComposeEndpoints.cs` is 2,755 vs 2,651 (+4). Phase 0 should decide whether to
re-baseline the Dataverse waiver as a one-line hygiene PR up front or absorb it in Phase 4; the Compose half is
not ours.

## Goal

**One** `IDataverseService` implementation family, MI-authenticated, decomposed below the god-class ceiling,
with the NFR-06 impersonation row-level-security paths preserved and re-verified.

## Pause & resume (2026-08-19)

**Why paused** — full reasoning + evidence in [`notes/validation-2026-08-19.md`](notes/validation-2026-08-19.md).
Summary: high risk (fail-OPEN impersonation is the failure mode, on a near-zero test baseline, verifiable only
on dev, in the repo's most contended shared lib), against a reward that shrank twice — once when the hardening
consumed most of the justification, once when the scope correction revealed the project retires one of five
stacks rather than converging "the two."

**Extracted now (hardening track — NOT gated on this project):**

1. ✅ **DONE 2026-08-20 — `UpdateRecordFieldsAsync` collapsed to one live impl** (finishes hardening item B3).
   WebApi is the single live impl; the SDK copy fails loud. **Surfaced and fixed a live production defect in
   the process**: `FinanceRollupService` sent SDK `Money` wrappers to an OData PATCH, so every matter/project
   recalculate had returned HTTP 400 since 2026-03-03. See
   [`notes/validation-2026-08-19.md` §7a](notes/validation-2026-08-19.md).
2. **Write the impersonation characterization + negative-canary suite** — valuable independent of unification,
   and `spaarke-auth-v4-dataverse-MI` needs it too (it is changing the credential under the OBO path).
3. **Resolve the god-class ratchet red** — `DataverseServiceClientImpl` 2,975 vs waiver 2,864 (+100 grace),
   failing on `master` today.

**Resume triggers** — re-evaluate this assessment when any fires:

- A capability genuinely needs porting across the SDK/REST boundary (a forcing function, not a cleanup).
- The SpeAdmin / ExternalAccess REST retirement gets scoped — then unify **all five** stacks once, from a
  correct map, rather than one-fifth now.
- A second mis-route reaches production despite the fail-loud stubs (evidence the fencing is not holding).
- The routing table grows past ~12 narrow interfaces (9 today).
- **`spaarke-auth-v4-dataverse-MI` completes** — the operator's standing instruction: re-read the assessment
  then and check whether anything auth-v4 surfaced changes it.

**Stack inventory** (why "two-stack" understates the problem): `DataverseServiceClientImpl` (SDK) ·
`DataverseWebApiService` (REST — *the only one this project retires*) · `DataverseWebApiClient` (REST, SpeAdmin +
ExternalAccess) · `RegistrationDataverseService` (hand-rolled REST clone, `:93`) · `DataverseAccessDataSource`
(own HTTP + MSAL, auth-v4's). Plus the `Services/Ai` raw-HTTP camp migrated separately in AUTHV2-042 Phase C.

### Justification after the hardening landed (CLAUDE.md §11 — read before committing)

RED-4 itself framed this project (its option **C**) as **OPTIONAL** — "only if the owner wants the single-impl
end-state; not required to remove the traps (B does that)" — and its strongest pro-C counter-argument rested on
the impersonation surface living in a "majority-dead class." **That premise is now stale**: option B shipped and
deleted the dead code. The justification must therefore stand on what is left, honestly stated:

1. **Existing / overlap** — nothing new is built; this converges two existing impls. §11's new-component test
   does not apply, but its spirit does: the question is whether convergence still earns its cost.
2. **Extension instead?** — already taken. Option B *was* the extension path (fence the traps, publish the
   routing map, fail loud). This project is the retirement path.
3. **Cost of doing nothing** — three were claimed. Re-derived from code on 2026-08-19, **one survives intact**:
   - ✅ **INTACT** — `UpdateRecordFieldsAsync` still has **two live implementations** selected by which alias a
     consumer injects (`FinanceRollupService.cs:157` → SDK; `InvoiceReviewService.cs:296`,
     `ScorecardCalculatorService.cs:224`, `SignalEvaluationService.cs:226`, `DataverseUpdateHandler.cs:42/98`,
     `UpdateRecordActionCore.cs:135` → WebApi). The exact drift class that produced the DEF-2 landmine.
     **Fixable on its own** — extracted to the hardening track (see "Pause & resume").
   - 🟡 **DEFUSED** — narrow interfaces are hand-routed in DI and a mis-route fails at runtime, not compile
     time. But the hardening converted the 7 silent-empty stubs to `NotImplementedException`
     (`DataverseServiceClientImpl.cs:1802-1916`): the catastrophic mode (silently wrong/empty data) became a
     loud, minutes-to-diagnose crash. Bad-but-cheap bug class.
   - ❌ **FALSE** — "the NFR-06 seam is reached by concrete-class injection bypassing the interface layer."
     Every consumer injects `IImpersonatedCommunicationQuery`; tests mock it; the concrete type appears
     **once**, in a stateless 5-line pass-through adapter (`IImpersonatedCommunicationQuery.cs:40-56`,
     registered `CommunicationModule.cs:272`). That is the prescribed **ADR-010 "interface as testing seam
     only"** pattern — `CommunicationModule.cs:265` says so explicitly. Same shape for
     `IDataverseAccessGrantService`. This was the project's most rhetorically load-bearing argument and it does
     not survive contact with the file.

   RED-4's own strongest pro-C argument — the impersonation surface "lives in a **majority-dead** class" — was
   separately invalidated when the hardening deleted that dead code (−1,414 LOC).

**Honest verdict**: a **defect-prevention** project of medium-and-shrinking value — not a security project, not
a dead-code project, and not "one Dataverse access layer" (it retires one of five stacks). The residual value is
real but capturable at ~10–15% of the cost without touching the impersonated read path. **Hence PAUSED**; see
"Pause & resume" above. If resumed, Phases 0–3 alone retire the routing trap and Phase 4 should be a follow-on.

## Approach (phased)

0. **ADR** — target architecture: a single SDK-based implementation, decomposed into per-concern services;
   how impersonation (`Clone()`+`CallerId`), POA (principalobjectaccess), events, and field-mapping map onto it.
   Records the migration + the security-test plan. (Path B per CLAUDE.md §6.5 if it touches an ADR MUST.)
   **On the credential dimension, cite [ADR-028 Amendment A4](../../.claude/adr/ADR-028-spaarke-auth-architecture.md#amendment-a4-2026-08-17-secret-free-confidential-credential-for-obo-and-bff-identity-clients) rather than re-deriving it** — A4 (merged) explicitly sanctions
   `DefaultAzureCredential` (UAMI-pinned) for **app-only** outbound, which is exactly this project's target.
   No §6.5 escalation is needed for the credential question.
1. Consume the #3b MI outcome (task 011/NG1 — **done and live on dev, 2026-08-17**); target impl is MI-only.
   Per A4's shared-provider rule, **resolve the DI-registered `TokenCredential`** (UAMI-pinned via the BFF's
   `ManagedIdentityCredentialFactory`) rather than `new`-ing a credential at the call site — the current
   `DataverseWebApiClient`/`DataverseWebApiService` constructors build their own, and the single impl must not.
   Grant `prvActOnBehalfOfAnotherUser` to the MI app-user for impersonated writes.
   **1a. Characterization tests FIRST.** The existing baseline is one live-gated method
   (`tests/integration/Spe.Integration.Tests/DataverseWebApiFieldMappingRegressionTests.cs` covers only
   `GetEntitySetNameAsync`); everything else mocks the seams *above* the concrete (`Mock<IImpersonatedCommunicationQuery>`
   in `CommunicationPrivilegePrivacySeamTests.cs:64` and four sibling suites), and the events surface has **zero**
   behavioral tests on either impl. "Contract-test each against BOTH impls" therefore means **writing** that suite,
   live-gated, before any porting — it is a work package, not a checkbox. Pin the known behavioral quirks
   explicitly: `UpdateEventStatusAsync` deriving `statecode` from `statuscode` (`DataverseWebApiService.cs:461-464`);
   naive `{entityName}s` pluralization in `CreateEventAsync` (`:383`); asymmetric fail-loud/fail-soft semantics
   (`GetEntitySetNameAsync :221-240` throws vs `GetEntityObjectTypeCodeAsync :1015-1021` / `GetSharedSystemUserIdsAsync :1087-1093`
   returning 0/empty); `$skip`/`$count=true` paging vs SDK `PagingInfo` (`QueryEventsAsync :288`).
2. **Port the 4 WebApi-only capability groups onto the SDK connection** behind the existing narrow interfaces
   (events, field-mapping, `RetrieveMultipleImpersonatedAsync`, POA grants).
   **Port target = NEW per-concern services, NOT `DataverseServiceClientImpl`.** Each group lands in its own
   file (e.g. `DataverseEventService : IEventDataverseService`) sharing the SDK connection. Rationale: the impl
   is already 11 LOC past waiver+grace, so porting ~1,000 live LOC *into* it hard-fails `GodClassGuardTests`
   mid-project and would need an interim waiver bump that Phase 4 then unwinds. Porting outward instead keeps
   every PR small, never grows the frozen file, and *is* the Phase 0 ADR's stated target shape.
   **"SDK path" means SDK-managed connection + auth, NOT "QueryExpression everywhere."** The impersonated-read
   seam passes raw OData query strings and returns `Dictionary<string, JsonElement>` rows including
   `@OData.Community.Display.V1.FormattedValue` annotations (`IImpersonatedCommunicationQuery.cs:50-55`); a
   QueryExpression rewrite changes the wire shape and forces a rewrite of the Communication read stack. Freeze
   the seam contracts and port via `ExecuteWebRequest` passthrough.
   **Scope guard**: this must NOT touch `DataverseAccessDataSource` (it implements `IAccessDataSource`, not
   `IDataverseService`, and is auth-v4's — verified: zero symbol overlap in either direction). If porting turns
   out to reach it, the §"Coordination with auth-v4" division of labour needs re-negotiating before proceeding.
3. Repoint DI to the single impl and **delete `DataverseWebApiService`** (only — see "Non-goals").
   **The repoint surface is larger than `GraphModule.cs`**: the NFR-06 seams bind the **concrete type in code**,
   not just DI — `DataverseImpersonatedCommunicationQuery` and `DataverseAccessGrantService` take
   `DataverseWebApiService` as a constructor parameter (`Services/Communication/IImpersonatedCommunicationQuery.cs:42-47`,
   `Services/Communication/Access/IDataverseAccessGrantService.cs:36`), registered in `CommunicationModule.cs`
   (~`:272`, ~`:647`). Enumerate: `GraphModule.cs`, `CommunicationModule.cs`, both seam adapters' ctor types.
   PR description records: (a) the deleted class carried a secret-reading fallback branch — hygiene, **not** a
   security win (it did not block the Key Vault secret's removal); (b) `GraphModule.cs` is repointed but
   `GraphClientFactory.cs` is untouched (auth-v4's file); (c) `DataverseWebApiClient` and its T3 gating fix are
   explicitly **out of scope** and remain live work for T3's owner
   ([#791](https://github.com/spaarke-dev/spaarke/issues/791) item 1). Also refresh
   `docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md` and
   `src/server/shared/Spaarke.Dataverse/docs/TECHNICAL-OVERVIEW.md` in the same PR (both describe the two-stack
   world). Ping auth-v4 on merge so its credential inventory re-baselines.
4. **Decompose** the residual `DataverseServiceClientImpl` into per-concern services ≤ 2,000 LOC; remove its
   `GodClassGuardTests` waiver (the `DataverseWebApiService` waiver is already gone). **Separable**: Phases 0–3
   fully retire the split-brain routing trap; Phase 4 is god-class hygiene on a pre-existing file and may be
   split into a follow-on project if the operator wants a smaller first landing. If it stays, it must keep one
   concrete owner of `OrganizationService` and update `UnwrapServiceClient`
   (`DataverseServiceClientExtensions.cs:34-77`) in the same PR — ~16 downcast sites hard-cast to the concrete
   class and break at runtime otherwise.

## Non-goals (explicit)

Naming these prevents the scope creep the validation pass caught:

- **`DataverseWebApiClient` + its SpeAdmin / ExternalAccess consumers** (45 refs, 16 files). A separate REST
  stack outside the `IDataverseService` family. Retiring it is a legitimate follow-on project; it is **not**
  this one. Its T3 gating defect (`DataverseWebApiClient.cs:42` never reads `Graph:ManagedIdentity:Enabled`)
  stays with T3.
- **`DataverseAccessDataSource`, `GraphClientFactory`, `DataverseUserClient`, `AgentTokenService`** — auth-v4's
  OBO targets (see "Coordination with auth-v4").
- **`RegistrationDataverseService`'s hand-rolled REST client** — same category as `DataverseWebApiClient`.
- **The `TodoGenerationService` behavioral follow-up** (trap #1 in the routing doc: it injects the composite and
  its overdue-events pass returns empty). Fixing it changes behavior and needs its own validation; tracked in
  r3 `defer-issues.md`.
- **Removing `BFF-API-ClientSecret`** — auth-v4's, and not achievable by this project's deletions.

## Rollback & landing strategy

Phase 3 is the only irreversible step, so it is isolated:

1. Phases 1a–2 are **purely additive** (new per-concern services + tests); the REST impl stays live and routed.
2. Cut over behind **flag-gated dual DI routing** (ADR-032 seam), REST impl still present → revert = flip the flag.
3. Soak on dev with the parity suite green, including the negative canary in the risk table below.
4. **Delete `DataverseWebApiService` in its own final PR** → revert = `git revert` of that single PR.

Verification is **dev-only** (the sole live environment; demo/prod are decommissioned). Record a deferred
re-verify obligation — including the MI app-user `prvActOnBehalfOfAnotherUser` grant — for demo/prod
re-provisioning, rather than claiming "green per env".

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| **🔴 Fail-OPEN impersonated read** — if the ported `RetrieveMultipleImpersonatedAsync` relies on `Clone()`+`CallerId` and `ServiceClient` does **not** stamp `MSCRMCallerID` on its `ExecuteWebRequest` HTTP path, the query silently runs **app-only** and returns org-wide rows with no error. Invisible to green-path tests; the highest-severity NFR-06 class | Stamp `MSCRMCallerID` **explicitly** via `ExecuteWebRequest`'s `customHeaders` parameter (currently passed `null`, `DataverseServiceClientImpl.cs:1962`) — never rely on `CallerId` for the HTTP path. Preserve the `Guid.Empty` rejection guard (`DataverseWebApiService.cs:962-965`). Add a **negative canary**: an impersonated low-privilege read MUST return strictly fewer rows than the app-only read of the same query |
| **NFR-06 impersonation regression** (row-level security) | Contract + seam tests on every impersonated read/write path BEFORE deleting the REST impl; parity-test SDK vs REST impersonation; `prvActOnBehalfOfAnotherUser` on the MI app-user (recorded as an unconfirmed go-live prerequisite at `DataverseImpersonation.cs:24-27` — an environment-config task that **precedes** the test task) |
| **`Clone()` under the MI token provider is empirically unproven** — #3b went live 2026-08-17; the `Clone()` sites (`DataverseServiceClientImpl.cs:1945`, `UserPrivilegeChecker.cs:147`) may not have executed under `tokenProviderFunction` auth yet, and `ServiceClient.Clone()` has version-specific edge cases with externally-managed tokens | Phase 1 smoke on dev: one impersonated **read** and one impersonated **write** under MI, before any porting begins. If `Clone()` misbehaves under MI, the whole port strategy changes — find out first |
| Behavior drift between the two impls of a ported method | Characterization-test each capability against both impls, switch behind the interface, keep the REST impl until parity is green (ADR-032 seam). Pin the four known quirks listed in Phase 1a |
| **Sync-over-async on hot read paths** — `ExecuteWebRequest` is synchronous (wrapped in `Task.Run`, `DataverseServiceClientImpl.cs:1957`); moving events + Communication feed reads onto it converts fully-async `HttpClient` reads into threadpool-blocking calls | Measure threadpool impact on the queue-feed endpoint under load before cutting over; prefer the ServiceClient's async Web-API surface where available |
| Leaky abstraction — ~16 sites downcast to the concrete SDK class (`UnwrapServiceClient`, `DataverseServiceClientExtensions.cs:34-77`) | Unaffected through Phases 1–3 (the single impl keeps `OrganizationService`). **Phase 4 breaks this** if decomposition renames/splits the concrete registered as `IDataverseService` — see Phase 4's same-PR requirement |
| Interim god-class ratchet failure mid-project | Port outward into new per-concern files (Phase 2), never into `DataverseServiceClientImpl.cs`; no interim waiver bump needed |
| Highest-contention shared lib | `/conflict-check`; quiet windows; small reviewable PRs; land AFTER the interim hardening |
| Concurrent edits to `Spaarke.Dataverse` from `spaarke-auth-v4-dataverse-MI` | **File-level overlap is nil** (see below) — mitigation is PR serialisation + `/conflict-check`, not sequencing the projects |

## Coordination with auth-v4 (`spaarke-auth-v4-dataverse-MI`)

Source: [`notes/auth-v4-coordination-memo.md`](notes/auth-v4-coordination-memo.md) (2026-08-19, from the auth-v4
research phase). Summary of what binds this design:

**Division of labour — no overlap.**

| | Owns | Credential target |
|---|---|---|
| **This project** | **App-only** Dataverse (`IDataverseService` family) | `DefaultAzureCredential` (UAMI), resolved from DI |
| **auth-v4** | **OBO / delegated** confidential clients | MI-FIC client assertion (or KV certificate) |

`DefaultAzureCredential` cannot perform an OBO exchange — that asymmetry is why the projects are separate, and
A4 now writes it down. Our target is app-only, so `DefaultAzureCredential` is correct and sufficient.

**Neither project blocks the other.** The memo explicitly retracts an earlier "unification unblocks auth-v4"
framing: both classes we delete already degrade to Managed Identity when their secret is absent. What actually
blocks `BFF-API-ClientSecret` removal is a different set — `DataverseOptions.ClientSecret` (`[Required]` +
`ValidateOnStart`), `GraphClientFactory`, `DataverseAccessDataSource`, `DataverseUserClient`, `AgentTokenService`
— **none of which are ours**.

**Out of scope for us (auth-v4 targets that survive unification):** `GraphClientFactory.cs`,
`DataverseAccessDataSource.cs` (+ its T3/T4 defects — the transient-typed-HttpClient MSAL hazard at
`SpaarkeCore.cs:39`), `DataverseUserClient.cs`, `AgentTokenService.cs`. We repoint `GraphModule.cs` (DI) but
never `GraphClientFactory.cs` (implementation).

**Correction to the memo (2026-08-19 validation).** The memo's §4 table and §6 assume `DataverseWebApiClient`
is deleted by this project's Phase 3, and conclude that "T3's scope halves." **Both are withdrawn** — the class
has 45 references across 16 files in the SpeAdmin / ExternalAccess surface and is now an explicit non-goal.
T3 keeps both of its sites (`DataverseAccessDataSource.cs:53` **and** `DataverseWebApiClient.cs:42`). Everything
else in the memo verified clean. Notify auth-v4 of this correction.

**Sequencing** — landing this project first is a *convenience*, not a dependency: it avoids merge contention on
the highest-contention shared lib, spares auth-v4 pointless churn tidying the secret-fallback branch in the class
we delete, and shrinks auth-v4's per-environment verification surface. Do **not** accelerate on auth-v4's
account, and do not treat an auth-v4 slip as a blocker here. Given the T3 correction above, the convenience
argument is now **weaker** than the memo stated — auth-v4 should not wait on this project.

## Dependencies

- **Interim `dataverse-access-hardening`** (fences the traps) → **merged to `master`** (RED-4 B; routing doc
  published; dead code deleted; DEF-2 fixed). Satisfied.
- **#3b MI migration** (task 011/NG1) — **done, live on dev** (2026-08-17). Satisfied.
- **`spaarke-auth-v4-dataverse-MI`** — **not a dependency in either direction** (see above). Coordinate PR
  timing only.
- Needs its own ADR (Phase 0). INITIALIZE-ONLY; tasks at execution start (worktree exists:
  `spaarke-wt-dataverse-access-unification-r1`, branch `work/dataverse-access-unification-r1`).
