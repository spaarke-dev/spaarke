# Current Task State — spaarke-auth-v4-dataverse-MI

> **Last Updated**: 2026-08-21 — **tasks 021, 023, 024 COMPLETE. Phase 2 is done except 022.**
> **Recovery**: Read "Quick Recovery" first. Everything needed to continue is in this file.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | `spaarke-auth-v4-dataverse-MI` — eliminate `BFF-API-ClientSecret`; migrate every BFF-identity confidential client (incl. **OBO**) to a Managed-Identity federated credential |
| **Branch** | `work/spaarke-auth-v4-dataverse-MI` · worktree `c:/code_files/spaarke-wt-spaarke-auth-v4-dataverse-MI` |
| **Task** | **none active** — 021, 023, 024 all closed |
| **Status** | Committed, working tree clean. Auth seams **50 / 50** · ArchTests **36 / 36** · publish **44.98 MB** · CVE clean |
| **Next Action** | **`022` — the only remaining Phase 2 task, and the one everything downstream waits on.** `parallel-safe: false`, `opus/xhigh`, highest blast radius in the project, and it grew at task 021. **Give it a fresh session** and read `notes/decisions/021-credential-config-keys.md` first |
| **Progress** | **10 of 26 active complete** (001, 002, 003, 010, 011, 020, 021, 023, 024, 030) · **16 remaining** · 3 deferred |
| **Portfolio** | [#800](https://github.com/spaarke-dev/spaarke/issues/800) · Epic [#426](https://github.com/spaarke-dev/spaarke/issues/426) |

---

## 🔴 A defect shipped at 021 was caught at 023/024 — read this before trusting the 021 record

`IsFallThroughEligible` and its catch clause were typed to **`MsalServiceException`**. MSAL throws
**`MsalClientException`** for `managed_identity_all_sources_unavailable` — the *"no IMDS on this host"*
case, which is the **commonest fall-through** and the entire reason ordered selection exists. The clause
never matched it, so a developer workstation would have gotten a failed request instead of a fall-through
to the secret.

**The seam tests passed because the stub threw the type the implementation assumed.** A test written from
the same premise as the code cannot falsify that premise. What caught it was a real MSAL stack trace,
surfaced by an intermittent failure that task 021 had booked away as a "flake" (final tally: **2 failures
in 4 full-suite runs**, always passing in isolation — MSAL caches the selected MI source
process-statically, so which exception shape appears depends on test ordering).

Fixed in the predicate, the catch clause, the contract docs, and the task-020 test
(`Assert.ThrowsAsync<T>` demands an **exact** type match — it needed `ThrowsAnyAsync`). Two regression
tests pin the real shape. Full write-up: `notes/decisions/021-credential-config-keys.md` **§13a**.

**Carry the lesson, not just the fix:** an intermittent failure is evidence. Booking it away was the
wrong call; capturing the error at normal verbosity cost one suite run and found a shipped defect.

---

## Tasks 023 + 024 — what shipped

| File | Task | Purpose |
|---|---|---|
| `Configuration/IdentityConfigurationValidator.cs` | 023 (+024 rule 4) | **NEW** — the UAMI ↔ app-registration guard, 4 rules |
| `Configuration/DataverseOptions.cs` | 024 | `[Required]` off `ClientSecret` |
| `Configuration/GraphOptionsValidator.cs` | 024 | "MI disabled ⇒ `Graph:ClientSecret`" rule removed |
| `Configuration/AgentTokenOptions.cs` | 024 | `[Required]` + validator check off `ClientSecret` |
| `tests/integration/seam/Auth/IdentityConflationSeamTests.cs` | 023/024 | **NEW** — 10 tests |

Records: [`023-identity-conflation.md`](notes/decisions/023-identity-conflation.md) ·
[`024-relax-config-validators.md`](notes/decisions/024-relax-config-validators.md).

### ⚠️ Live trap found at 023 — `AZURE_CLIENT_ID` holds a managed identity

Verified on `spaarke-bff-dev` 2026-08-21: `AZURE_CLIENT_ID` = `5967251e-…` (the **UAMI**), while the app
registration is `1e40baad-…`. `GraphClientFactory.cs:54` reads `AZURE_CLIENT_ID ?? API_APP_ID` as the
**app-only** clientId — so it resolves to a managed identity where an app registration is required.

**Inert only because `Graph__ManagedIdentity__Enabled=true` makes that branch dead code.** Set the flag
false — plausible during an incident — and the BFF builds a `ClientSecretCredential` from a managed
identity and fails with an opaque `AADSTS` error naming neither identity.

Guarded (fatal when MI is disabled, `LogError` when enabled). **Not** fixed in the environment: app
settings are task 031/032/033 territory and app-only behaviour is out of 023's scope. **Booked onto 031** —
the single consumer falls back to `API_APP_ID`, which is correct, so *clearing* `AZURE_CLIENT_ID` removes
the trap rather than guarding it.

### Two POML instructions that could not be followed as written

- **023's `<outputs>` list `ManagedIdentityAssertionProvider.cs` as `modify`** — but no substantive change
  exists to make. It already resolves the UAMI through the shared resolver and never reads `API_APP_ID`,
  so there is **no cross-defaulting to remove**; and it must not fail at construction (task 020's
  contract, which 021's fall-through depends on), so the guard could not live there. Only a
  cross-reference was added. Said plainly rather than manufacturing a change to match the list.
- **024's step 3 says "move `AgentTokenOptions` under `ValidateOnStart`"** — **not done**, and the steps
  are `directional` so this is a permitted deviation. Verified: **no fixture seeds an `AgentToken`
  section**, and the validator still requires `TenantId`/`ClientId`/`AgentAppId`/`DataverseEnvironmentUrl`,
  so the move would fail startup for every fixture that boots `Program.cs` — violating 024's own
  criterion 3. `AgentModule.cs:19-21` already records the original deferral reason, which still holds.

### The finding that lowered 024's risk

Two of the three "validators that mandate the secret" mandated a secret **nothing reads**:
`DataverseOptions.ClientSecret` has **zero** consumers (only `EnvironmentUrl` is used), and
`GraphOptions.ClientSecret` has **zero** — the two `.WithClientSecret(_options.ClientSecret)` sites that
look like consumers take `IOptions<PowerBiOptions>`, a different type. Only
`AgentTokenOptions.ClientSecret` is real (`AgentTokenService.cs:105`).

So the startup-crash dependency on the secret was, in two of three cases, a mandate with no runtime
purpose. Validation did not disappear — it moved to `IdentityConfigurationValidator` **rule 4**, which
asserts the weaker correct thing (*some* credential is obtainable) and is deliberately conservative:
MI-FIC is never treated as provably-absent, so the rule cannot false-positive a workstation or fixture.

---

## ⚠️ Read before task 022 — task 021 grew 022's scope

**The credential contract is ASYNC**, and that is not cosmetic. Selection must *prove* a credential
(mint the assertion / fetch the certificate) **before binding it**, because a credential that is bound and
only then found broken surfaces as a failed OBO exchange rather than a fall-through — and on the OBO path
that is every user at once.

All four call sites build their confidential client in a **constructor** today and cannot await:

`DataverseAccessDataSource:225` · `AgentTokenService:98` · `DataverseUserClient:91` · `GraphClientFactory:83`

Each must move to **lazy first-use construction**. `CiamGraphClientFactory.GetOrCreateAppAsync` — a
`SemaphoreSlim` guarding a one-time build — is the in-repo precedent to copy. Booked as a constraint on the
022 POML. It is probably the largest single piece of work 022's original estimate did not include.

Also booked onto 022: **reconcile `AgentToken:ClientSecret`**. The provider resolves the secret as
`AzureAd:ClientSecret` → `API_CLIENT_SECRET` → `AZURE_CLIENT_SECRET`, deliberately excluding the
options-bound `AgentToken:ClientSecret` — folding it in silently could change which secret
`AgentTokenService` presents. Decide it where the change is observable.

---

## Task 021 — what shipped

| File | Purpose |
|---|---|
| `Spaarke.Dataverse/IConfidentialClientProvider.cs` | **NEW** — the client-level contract (MSAL-typed; no new package, FR-14 unaffected) |
| `Sprk.Bff.Api/Infrastructure/Auth/OrderedCredentialClientProvider.cs` | **NEW** — selection, the ONE cache, the fall-through predicate, the negative memo |
| `Sprk.Bff.Api/Configuration/CredentialSelectionOptions.cs` | **NEW** — `CredentialKind`, options, relational validator |
| `Sprk.Bff.Api/Infrastructure/Auth/KeyVaultCertificateLoader.cs` | **NEW** — extracted from `CiamGraphClientFactory` (behaviour-preserving by construction) |
| `Sprk.Bff.Api/Infrastructure/Graph/CiamGraphClientFactory.cs` | Delegates to the extracted loader |
| `Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs` | `AddCredentialSelection(...)` — options + validator + provider |
| `tests/integration/seam/Auth/CredentialOrderingSeamTests.cs` | **NEW** — 20 tests |

Full record: [`notes/decisions/021-credential-config-keys.md`](notes/decisions/021-credential-config-keys.md).

### Config surface (the Phase 3 runbook cares about this)

```
Graph__Credentials__Order__0                  = ManagedIdentityFederated
Graph__Credentials__Order__1                  = ClientSecret
Graph__Credentials__KeyVaultCertificateName   = <kv cert name>   # only if the cert kind is listed
Graph__Credentials__NegativeCacheSeconds      = 10               # optional, 0..120
Graph__Credentials__FailuresBeforeSuppression = 2                # optional, minimum 2
```

**Rollback = reorder `Order` + restart.** A restart is acceptable under NFR-06; a redeploy is not. The
POML escalation trigger did **not** fire.

### The five things a fresh session must NOT re-derive

1. **Q4 is DECIDED — `IClientAssertionProvider` does NOT widen.** The question *dissolves* once selection
   moves to the client level. Its premise was that a certificate assertion needs the `aud`/`iss`/`sub` the
   narrow contract drops — but the certificate branch calls `.WithCertificate(x509)` and **MSAL builds the
   assertion itself**. No Spaarke-owned request record is needed. Avoided, not deferred.
2. **Rollback is permitted to put the secret on top.** A4 says never promote a secret above a secret-free
   credential, but the ordered list *is* the rollback mechanism — refusing to boot in that configuration
   would disable the emergency exit at the one moment it is needed. So it is allowed and logged at
   **error** naming A4. Loud, not blocked.
3. **The selection itself expires.** Not in the design; added during implementation, and it is what
   actually closes the hazard. When a lower-priority credential wins, the selection stands only until the
   skipped credential's suppression lifts — then the next call re-evaluates from the top. Without it, one
   10-second MI-FIC blip would pin the process to the **secret** until somebody restarted it.
4. **Fall-through is an ALLOWLIST, not a denylist.** Only `managed_identity_unreachable_network` and
   `managed_identity_all_sources_unavailable` fall through. Everything else — including future MSAL codes —
   fails loud. "Unknown error ⇒ quietly downgrade to the secret" is the wrong default.
5. **There is no `appsettings.json` in the BFF** — only templates and `appsettings.Testing.json`. Any design
   that ships a default there fails startup everywhere. The canonical order lives in
   `AddCredentialSelection` and applies only when the section is **absent**; that is the AP-7 bound.

### Quality gates

| Gate | Result |
|---|---|
| Seam tests | **20 / 20** |
| Full BFF suite | **10,572 / 0** (97 skipped) |
| ArchTests | **36 / 36** — ADR-010 ceiling **not** raised (cross-assembly seam invisible to it, as at 020) |
| Publish | **44.98 MB** compressed incl. PDBs, 215 files, vs 44.96 baseline = **+0.02 MB** · ceiling 60 |
| CVE | clean · **no package added to the BFF** (only `FakeTimeProvider`, on the test project) |
| `code-review` | 2 defects found in my own output, both **fixed** |
| `adr-check` | 0 violations · the `.WithClientSecret` question answered explicitly (Path C — consolidation) |

**F-1 — certificate private-key leak.** `GetOrAdd` skips its factory on a cache hit, so a certificate
freshly loaded from Key Vault was orphaned **undisposed** whenever re-evaluation found the client already
cached. Re-evaluation is time-driven, so this leaked one ephemeral key handle per suppression window.
Fixed to dispose exactly when the factory did **not** run — disposing unconditionally is the opposite bug,
since MSAL owns the certificate for the client's lifetime.

**F-2 — "fails fast at startup" was not proven.** Only the validator was tested, which says nothing about
whether `ValidateOnStart` ever reaches it. A validator that is never invoked passes its own unit test
perfectly while the BFF boots on a misconfigured credential order. Fixed: `AddCredentialSelection` is now
independently callable, and two tests boot a real host — an invalid order throws at `StartAsync`, and the
negative control proves an absent section still boots on the canonical order.

### ⚠️ Observed flake in a TASK-020 test — not mine, not dismissed

`ClientAssertionProviderSeamTests.Provider_WhenNoManagedIdentityIsReachable_FailsAtFirstCall_...` failed
**once in two full-suite runs** (1/10571, then **0**/10572) and passes consistently in isolation. It makes
a **live IMDS attempt** and asserts one of three error codes; its own doc comment concedes host-dependence.
Task 021 never resolves the new provider during the suite, so it cannot be the cause.

**Deliberately NOT fixed here**: the failing run was captured at quiet verbosity, so the actual error code
was never recorded, and fixing a flake blind is how a real signal gets suppressed. **Booked onto task 060**
with the reproduction condition (full suite, not isolation).

---

## ⚠️ Open owner decisions (nothing is blocked on these)

1. **ADR-010 ratchet** — [#809](https://github.com/spaarke-dev/spaarke/issues/809). The gate is **blind to
   cross-assembly seams** (scans the BFF assembly only), and its ceiling is **153 against a real count of
   151**, so two in-assembly interfaces can land unreviewed today. Task 021 re-confirmed the blind spot:
   `IConfidentialClientProvider` is also invisible to it. Both fixes are one-liners with repo-wide blast
   radius — owner's call.
2. **`LayerDependencyTests`** — [#810](https://github.com/spaarke-dev/spaarke/issues/810). Enforces
   `ProjectReference` but **not** `PackageReference`, so half the constraint every task in this project
   cites rests on reviewer attention. (Task 021 verified its half by hand: no new package.)
3. **CLAUDE.md §10 publish-size baseline is not method-qualified.** Two honest measurements of one tree
   differed by ~1.3 MB purely on compression method — more than the +5 MB escalation threshold. Task 021
   therefore states its method inline: `Compress-Archive -CompressionLevel Optimal`, framework-dependent
   linux-x64, PDBs included.

---

## Completed (10 of 26)

| Task | Outcome |
|---|---|
| **001** | `staging` slot on `spaarke-bff-dev`, UAMI-assigned, healthy, **not swapped** |
| **002** | **OBO PROVEN under MI-FIC.** T0–T4 pass; T5 negative control fails as required |
| **003** | Credential decision recorded; ADR-028 A4 adoption status + E4′ correction |
| **010** | MI-flag gating fixed; **app-only decoupled from OBO** in `DataverseAccessDataSource` |
| **011** | Confidential clients shared process-wide; **ADR-009 token-cache decision made** |
| **020** | `IClientAssertionProvider` seam; **ADR-010 ceiling deliberately NOT raised** |
| **021** | **Ordered credential selection + the ONE client cache.** Rollback is now a config edit |
| **023** | UAMI / app-registration conflation guard; found a **live trap** in `AZURE_CLIENT_ID` |
| **024** | The three secret mandates relaxed; credential adequacy checked once, correctly |
| **030** | `Register-EntraAppRegistrations.ps1` FIC extension; **E2E-verified against the live tenant** |

Decision records: [`notes/decisions/`](notes/decisions/). **Read `020` and `021` before task 022** — between
them they changed 022's scope twice.

---

## Critical context (read before touching code)

**The premise is PROVEN.** Task 002 demonstrated on the wire that OBO works under a Managed-Identity-issued
client assertion — Graph/SPE, Dataverse `user_impersonation` (with `upn` preserved, so row-level
authorization still evaluates as the *user*), and long-running OBO. **No pivot to a certificate.** Three
prior audits concluded the secret could never be removed, on one false sentence. **Do not re-derive "OBO
needs a secret" from any stale doc — fix the doc.**

**OBO fails CLOSED.** A bad change locks out every user instantly and totally. The `staging` slot exists so
the credential mechanism is the only variable under test. **Never swap outside task 032.**

---

## Carried-forward obligations (booked in POMLs, not just here)

| Onto | Obligation |
|---|---|
| **022** | **Async contract → move all four client builds out of constructors** (see the top of this file) · reconcile `AgentToken:ClientSecret` · collapse the three per-class CCA caches — **task 011's A4 exception EXPIRES HERE**; escalate rather than defer · migrate `ConfidentialClientSharingSeamTests` when the diagnostics move · converge the **UAMI precedence** (four shared-lib sites read the two keys in the OPPOSITE order and don't call the shared resolver) |
| **024** | Workstation user-secret `API_CLIENT_SECRET` is **STALE** → `AADSTS7000215` |
| **031** | Verify slot `keyVaultReferenceIdentity` **before** the OBO checklist · do **not** use `Deploy-BffApi.ps1 -UseSlotDeploy` (it always swaps) · needs a second test principal for the fails-closed case · a single green check **inside Entra's ~2-min flap window proves nothing** |
| **032** | Site properties **do not swap** — verify identity + `keyVaultReferenceIdentity` on **both** slots first |
| **033** | Purge the secret from **BOTH** slots; on dev these are **plaintext app settings**, not KV references · the lowercase `bff-api-client-secret` KV alias breaks the Office add-in if missed · **also delete `ClientSecret` from the default order in `AddCredentialSelection`**, not just from app settings |
| **060** | **Allowlist `OrderedCredentialClientProvider` as the sanctioned `.WithClientSecret` binding point, WITH its reason** · diagnose the task-020 seam flake with the error code in hand · `_cca`-decoupling source guard (010) · no-call-site-bypasses-the-cache guard (011) · `ManagedIdentityClientAssertion` constructed only in a ctor/static-init (020) · Power BI allowlist entry **with the deferral reason** |
| **061** | Census must scan **ALL server assemblies**, not just the BFF (020's blind spot) · count the provider as **ONE consolidated site, not expansion** · keep both Power BI sites as secret-backed entries |
| **090** | Power BI criterion 10 **waived with reason** · `/test-diet` must adjudicate the four auth files under `seam/Auth/` — note `tests/integration/auth/**` is **empty AND not compiled into any csproj**, so a test authored there would run never |

---

## Owner directives (standing)

1. **Autonomous execution + parallel task agents where safe.** Escalation triggers and fail-closed gates
   (022, 031, 032, 033) still stop for judgment.
2. **CI issues handled separately.** The god-class ratchet one was already fixed on master
   (`866f9c101` retired it → complexity guidance).
3. **Power BI deferred** (tasks 040/041/042 ⏭️) — [DEF-001 / #804](https://github.com/spaarke-dev/spaarke/issues/804).
   `PowerBi:ClientSecret` stays. Made visible not silent via the 060/061/090 obligations above.
4. **`dataverse-access-unification-r1` is INACTIVE / not scheduled** — interlock cleared everywhere.
   Consequence: `DataverseWebApiService` + `DataverseWebApiClient` are **not** being deleted.
5. **Provisioning is not a blocker for dev.** If something must exist for dev to work E2E, create it in
   this project rather than deferring it.

---

## Live environment (verified 2026-08-19 / 2026-08-21)

| | |
|---|---|
| Tenant | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| App registration | `SDAP-BFF-SPE-API` · `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| UAMI | `mi-bff-api-dev` · clientId `5967251e-…` · **principalId `9fd47efb-…`** ← the FIC subject |
| FIC | `mi-bff-api-dev-assertion` · audience `api://AzureADTokenExchange` |
| App Service | `spaarke-bff-dev` in `rg-spaarke-dev` — **UserAssigned only**, plan P1v3 |
| Slot `/healthz` | **200** (`spaarke-bff-dev-staging`) |
| Production `/healthz` | **200** — **never swapped**, untouched all project |

**Five UAMIs exist in the dev subscription; `spaarke-bff-identity` is named like the BFF's but is NOT
attached to it. Resolve by resource ID, never by name.**

### Entra error codes — measured, and they contradict the older project docs

| Case | Older docs said | Actually (measured 2026-08-21) |
|---|---|---|
| Wrong FIC subject | `AADSTS70021` | **`AADSTS700213`** |
| Propagation | `AADSTS70021` | **`AADSTS70025`**, and it **flaps ~2 min** |

The flap window is why task 021's negative-cache TTL is in **seconds** and why a single failure must not
demote the credential.

### Distinguishable auth failure modes (hard-won)

| Condition | Error |
|---|---|
| No MI present (workstation) | `managed_identity_unreachable_network` — fails in **~80 ms**, not a timeout |
| Wrong identity requested | `managed_identity_request_failed` — **FR-B4 signature: fail loud, never fall through** |
| Wrong/stale **secret** | `AADSTS7000215` — opaque; no hint the value is merely wrong |
| Slot missing `keyVaultReferenceIdentity` | container exit **134 / SIGABRT** — looks like a crash, is a KV-reference failure |
| Project pins `linux-x64` | `dotnet run` on Windows fails "not a valid application for this OS platform" — **not** a code fault |

### Reusable recipe (tasks 031 / 041)

The BFF app registration pre-authorizes the Azure CLI, so this yields a **real delegated user token**:

```bash
az account get-access-token --resource "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
```

---

## Cross-project

| Direction | Item |
|---|---|
| **We owed** `customer-provisioning-orchestration-r1` | **Task 030 — DELIVERED.** Their PR #779 has zero FIC code, so the duplicate-work risk did not materialise |
| **They owe us** | Model 2 FIC issuer tenancy — `PROVISIONING-CHANGE-REQUEST.md` §9.2. **Still open**; may be structurally impossible (A4 same-tenant rule). Must be answered before their Wave G-3 task 130 executes. The script now *refuses* cross-tenant pairs at runtime, so the failure is loud rather than silent |
| **Watch** | PR #293 — `Azure.Identity` 1.17.1→1.21.0 affects `ClientAssertionCredential` |

## Open questions

1. **`Analysis:PromptFlowKey`** — still in use? Task 055.
2. **Model 2 FIC issuer tenancy** — with provisioning; does not gate this project (dev-only, Model 1).
3. **Power BI service-principal profiles under a managed identity** — deferred with DEF-001, **still
   unanswered**, travels with task 040 and gates 041/042.

---

## Recovery commands

```bash
cd c:/code_files/spaarke-wt-spaarke-auth-v4-dataverse-MI
git fetch origin master && git merge origin/master
cat projects/spaarke-auth-v4-dataverse-MI/tasks/TASK-INDEX.md
cat projects/spaarke-auth-v4-dataverse-MI/notes/decisions/021-credential-config-keys.md   # changes 022's scope
curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev-staging.azurewebsites.net/healthz  # 200
curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev.azurewebsites.net/healthz          # 200
# then: task-execute on tasks/022-migrate-confidential-clients.poml (serial, opus/xhigh, fresh session)
```

## Blockers

**None.** 022 is startable immediately. It is the last Phase 2 task and gates 031/032/033 and all of Phase 6.
