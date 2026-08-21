# Current Task State — spaarke-auth-v4-dataverse-MI

> **Last Updated**: 2026-08-21 — **task 022 COMPLETE. PHASE 2 IS DONE.**
> **Recovery**: Read "Quick Recovery" first. Everything needed to continue is in this file.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | `spaarke-auth-v4-dataverse-MI` — eliminate `BFF-API-ClientSecret`; migrate every BFF-identity confidential client (incl. **OBO**) to a Managed-Identity federated credential |
| **Branch** | `work/spaarke-auth-v4-dataverse-MI` · worktree `c:/code_files/spaarke-wt-spaarke-auth-v4-dataverse-MI` |
| **Task** | **none active** — 022 (`243b514c1`) and 060 (`bde4a640d`) closed and pushed |
| **Status** | Full suite **10,591 / 0** twice · auth seams **55/55** · ArchTests **44/44** (36 + 8 new) · publish **44.99 MB** · CVE clean |
| **Next Action** | **`061` → `062` → `063`.** 061/062 are group **G**, `parallel-safe: true`, gated on 022 (done); 063 needs 060+061. Read the ⚠️ on **062** below before starting it — it will fire on today's config if written naively |
| **Progress** | **12 of 26 active complete** · **14 remaining** · 3 deferred |
| **Portfolio** | [#800](https://github.com/spaarke-dev/spaarke/issues/800) · Epic [#426](https://github.com/spaarke-dev/spaarke/issues/426) |

---

## 🔴 031 / 032 / 033 ARE BLOCKED ON A HUMAN DECISION, NOT ON CODE

The owner asked for "022, then 031/032/033 and all of Phase 6". Phase 6 is startable. The rollout chain
is **not**, and the reason is by design rather than an oversight:

| Task | What it does | Why it cannot just be run |
|---|---|---|
| **031** | Deploy to the `staging` slot, run the full §6.1 OBO checklist | Needs a **real delegated user token** (recipe below) AND, per its own booked obligation, **a second test principal** for the fails-closed case — which does not exist yet. Owner directive says create it in this project rather than defer |
| **032** | **Slot swap** → dev runs on MI-FIC → **then soak** | The soak is a *time period*, not a step. It exists because **OBO fails closed for every user at once**. Running 031→032→033 back-to-back compresses it to zero and discards the entire staged-rollout safety design |
| **033** | Delete the secret from app settings **and Key Vault** | Irreversible, and gated on "032 soak complete". Also breaks the Office add-in if the lowercase `bff-api-client-secret` alias is missed |

**Recommendation**: run Phase 6 (it is the project's distinguishing deliverable — success criterion 12),
then do **031 as its own session** with the owner present, and let 032's soak actually elapse.

---

## Task 022 — what shipped (commit `243b514c1`)

| File | Change |
|---|---|
| `Spaarke.Dataverse/ConfidentialClientTokenCredential.cs` | **NEW** — the one new component; bridges the MSAL client to `Azure.Core.TokenCredential` for the five app-only consumers |
| `GraphClientFactory.cs` | OBO → provider per exchange; app-only secret branch → provider; **`AZURE_CLIENT_ID` fallback DELETED** |
| `DataverseAccessDataSource.cs` | OBO → provider via the `Spaarke.Dataverse` contract; app-only → provider; **3 statics deleted** |
| `DataverseUserClient.cs` | OBO → provider (concrete); `CcaCache` deleted |
| `AgentTokenService.cs` | OBO → provider (concrete); `CcaCache` + `CcaBuilds` deleted |
| `DataverseWebApiService.cs` · `DataverseWebApiClient.cs` · `DataverseServiceClientImpl.cs` | residual `ClientSecretCredential` / `AuthType=ClientSecret` → provider |
| `IdentityConfigurationValidator.cs` | **rule 5** added (AgentToken secret divergence, by fingerprint); **rule 2a downgraded** from fatal to LogError |
| `ConfidentialClientMigrationSeamTests.cs` | **NEW** — fail-closed proof on each migrated OBO path |
| `ConfidentialClientSharingSeamTests.cs` | **MIGRATED** onto the provider seam and strengthened |
| `CredentialSelectionSeamTests.cs` · `IdentityConflationSeamTests.cs` · `ClientAssertionProviderSeamTests.cs` | amended premises |

Full record: [`notes/decisions/022-migrate-confidential-clients.md`](notes/decisions/022-migrate-confidential-clients.md).

### The four things a fresh session must NOT re-derive

1. **Call sites do NOT hold a client.** The booked "lazy first-use + `SemaphoreSlim` per site" plan was
   deliberately not followed. The provider owns the cache, the gate AND **selection expiry**; a held
   client defeats expiry and pins the process to a fallback after one blip. Sites ask **per exchange** —
   a dictionary lookup on the hot path. This is simpler *and* more correct.
2. **Task 011's ADR-028 A4 exception is CLOSED.** `grep -rn "ConcurrentDictionary<string, IConfidentialClientApplication>" src/`
   returns **exactly one** site. Do not reopen it.
3. **`AgentToken:ClientSecret` is reconciled, by live measurement** — byte-identical to
   `API_CLIENT_SECRET` / `AzureAd:ClientSecret` / `Dataverse:ClientSecret` on `spaarke-bff-dev`, and
   `AgentToken:ClientId` is the app registration. Rule 5 makes future divergence loud by **fingerprint**.
4. **Task 023's `AZURE_CLIENT_ID` trap is REMOVED, not guarded.** Its only consumer is deleted;
   `grep AZURE_CLIENT_ID src/` returns nothing. That is why rule 2a is no longer fatal — failing startup
   over a setting nothing reads is a false positive (AP-7). Clearing it at 031 is now hygiene, not a fix.

---

## Phase 6 — what each task inherited (READ BEFORE STARTING 060)

| Task | Booked obligations |
|---|---|
| ~~**060**~~ | ✅ **DONE** (`bde4a640d`). `tests/Spaarke.ArchTests/CredentialGuardTests.cs`, 8 tests. All three booked guards landed. **Success criterion 12 demonstrated live** — a seeded ninth secret-bearing client failed the build naming `file:line`, then was removed. Record: [`notes/decisions/060-credential-guard.md`](notes/decisions/060-credential-guard.md) |
| **061** | Census must scan **ALL server assemblies**, not just the BFF (020's blind spot — and `ConfidentialClientTokenCredential` now lives in `Spaarke.Dataverse`) · count the provider as **ONE consolidated site, not expansion** · keep both Power BI sites as secret-backed entries |
| **062** | ⚠️ **READ THIS FIRST — written naively, this guard fires on today's dev configuration.** It must fail outside Development when a BFF credential *resolves to* a secret. But `AddCredentialSelection`'s default order still **contains** `ClientSecret`, deliberately, until task 033 — that is the E-3 fallback and the rollback target. So key the assertion on the credential actually **SELECTED** (`OrderedCredentialClientProvider.SelectedKindFor`), never on the order's contents; or ship it disabled until 033 flips it on. Decide explicitly and record which |
| **063** | Depends on 060+061. `tests/Spaarke.ArchTests/` is **not** a KEEP path, so `/test-diet` at 090 would delete the forcing functions this project exists to leave behind |

**Success criterion 12 (the distinguishing one)**: introduce a deliberate ninth secret-bearing
confidential client on a scratch branch and **the build must fail**. That is 060+061's real acceptance
test — everything else is table stakes.

---

## Critical context (read before touching code)

**The premise is PROVEN.** Task 002 demonstrated on the wire that OBO works under a Managed-Identity-issued
client assertion, with `upn` preserved so row-level authorization still evaluates as the *user*. Three
prior audits concluded the secret could never be removed, on one false sentence. **Do not re-derive "OBO
needs a secret" from any stale doc — fix the doc.**

**OBO fails CLOSED.** A bad change locks out every user instantly and totally. **Never swap outside 032.**

---

## Carried-forward obligations

| Onto | Obligation |
|---|---|
| **031** | Verify slot `keyVaultReferenceIdentity` **before** the checklist · do **not** use `Deploy-BffApi.ps1 -UseSlotDeploy` (it always swaps) · needs a **second test principal** for the fails-closed case · a single green check **inside Entra's ~2-min flap window proves nothing** · clear `AZURE_CLIENT_ID` on both slots (now hygiene) |
| **032** | Site properties **do not swap** — verify identity + `keyVaultReferenceIdentity` on **both** slots first |
| **033** | Purge from **BOTH** slots; on dev these are **plaintext app settings**, not KV references · the lowercase `bff-api-client-secret` KV alias breaks the Office add-in if missed · **also delete `ClientSecret` from the default order in `AddCredentialSelection`** · `Dataverse:ClientSecret`, `Graph:ClientSecret` and `AgentToken:ClientSecret` now have **zero consumers in `src/`** — delete them and retire rule 5 |
| **090** | Power BI criterion 10 **waived with reason** · `/test-diet` must adjudicate the auth seam files — `tests/integration/auth/**` is **empty AND not compiled into any csproj**, so a test authored there would run never · keep `ConfidentialClientMigrationSeamTests` (MAINTAIN) |

---

## Open owner decisions (nothing is blocked on these)

1. **ADR-010 ratchet** — [#809](https://github.com/spaarke-dev/spaarke/issues/809). Blind to cross-assembly seams; ceiling 153 vs a real count of 151.
2. **`LayerDependencyTests`** — [#810](https://github.com/spaarke-dev/spaarke/issues/810). Enforces `ProjectReference` but not `PackageReference`.
3. **CLAUDE.md §10 publish-size baseline is not method-qualified.** This project states its method inline: `Compress-Archive -CompressionLevel Optimal`, framework-dependent linux-x64, PDBs included.

---

## Live environment (verified 2026-08-19 / 2026-08-21)

| | |
|---|---|
| Tenant | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| App registration | `SDAP-BFF-SPE-API` · `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| UAMI | `mi-bff-api-dev` · clientId `5967251e-…` · **principalId `9fd47efb-…`** ← the FIC subject |
| App Service | `spaarke-bff-dev` in `rg-spaarke-dev` — **UserAssigned only**, plan P1v3 |
| Slot / prod `/healthz` | **200 / 200** — **never swapped** |

**Five UAMIs exist; `spaarke-bff-identity` is named like the BFF's but is NOT attached to it. Resolve by
resource ID, never by name.**

⚠️ **`API_CLIENT_SECRET`, `AzureAd__ClientSecret`, `Dataverse__ClientSecret` and `AgentToken__ClientSecret`
are PLAINTEXT app settings on `spaarke-bff-dev`** (not Key Vault references). Task 033 must purge all
four, on both slots.

### Entra error codes — measured

| Case | Actually (2026-08-21) |
|---|---|
| Wrong FIC subject | **`AADSTS700213`** |
| Propagation | **`AADSTS70025`**, and it **flaps ~2 min** |

### Reusable recipe (tasks 031 / 041) — yields a real delegated user token

```bash
az account get-access-token --resource "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
```

---

## Owner directives (standing)

1. **Autonomous execution + parallel task agents where safe.** Fail-closed gates (031, 032, 033) still stop for judgment.
2. **Power BI deferred** (040/041/042 ⏭️) — [DEF-001 / #804](https://github.com/spaarke-dev/spaarke/issues/804).
3. **`dataverse-access-unification-r1` is INACTIVE** — interlock cleared; `DataverseWebApiService` + `DataverseWebApiClient` are **not** being deleted.
4. **Provisioning is not a blocker for dev.** If something must exist for dev to work E2E, create it in this project rather than deferring it.

---

## Recovery commands

```bash
cd c:/code_files/spaarke-wt-spaarke-auth-v4-dataverse-MI
cat projects/spaarke-auth-v4-dataverse-MI/tasks/TASK-INDEX.md
cat projects/spaarke-auth-v4-dataverse-MI/notes/decisions/022-migrate-confidential-clients.md
# then: task-execute on tasks/060-archtest-credential-ban.poml
```

## Blockers

**Phase 6: none.** 031/032/033: an owner decision on the soak + a second test principal (see the red
section above).
