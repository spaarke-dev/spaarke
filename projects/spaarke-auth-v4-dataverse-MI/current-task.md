# Current Task State — spaarke-auth-v4-dataverse-MI

> **Last Updated**: 2026-08-23 (by `task-execute` 051) — **GROUP F COMPLETE (7 of 7). Remaining work is 031→032→033 + 090.**
> **Recovery**: Read "Quick Recovery" first. Everything needed to continue is in this file.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | `spaarke-auth-v4-dataverse-MI` — eliminate `BFF-API-ClientSecret`; migrate every BFF-identity confidential client (incl. **OBO**) to a Managed-Identity federated credential |
| **Branch** | `work/spaarke-auth-v4-dataverse-MI` · worktree `c:/code_files/spaarke-wt-spaarke-auth-v4-dataverse-MI` |
| **Task** | **none active. Group F is CLOSED** — 055, 052, 050, 054, 056 done; **051 🔄 + 053 🔄 code-complete, cutover booked to 031/033** |
| **Status** | Full suite **10,614 / 0** (97 skipped) · seam **891/891** · ArchTests **56/56** · publish **44.99 MB compressed** (delta 0.00) · CVE clean |
| **Next Action** | **Owner-gated — no autonomous work remains in Group F.** Next is **031** (slot deploy + OBO verification), which now carries 4 booked obligations from 051 and 1 from 053. 031→032→033 need an owner decision, a second test principal that does not exist, and a soak that cannot be compressed. |
| **Progress** | **21 of 26 active complete** · **5 remaining** (031,032,033,090 + **053🔄/051🔄 awaiting cutover**) · 3 deferred |
| **Portfolio** | [#800](https://github.com/spaarke-dev/spaarke/issues/800) · Epic [#426](https://github.com/spaarke-dev/spaarke/issues/426) · synced 2026-08-21: `Tasks Completed 4 → 17`. **`Task Count` deliberately left at 26, not 29**: 29 poml − 3 deferred (040/041/042, DEF-001) = 26 active. Setting 29 would make 100% unreachable and pull Power BI back into scope |

## §0 🔴 READ FIRST — things that will mislead you if you don't

### 1. The SAS rotation is DONE and IRREVERSIBLE. Do not repeat it.

| Fact | Evidence |
|---|---|
| The leaked key was the namespace **PRIMARY** of `RootManageSharedAccessKey` | fingerprint match vs `C:/code_files/spaarke/src/.../appsettings.Development.json` (gitignored, **never committed**) |
| It is **rotated and dead** — fp `348e57a64503` no longer exists | `az servicebus ... keys renew --key PrimaryKey` |
| **Both** current keys are valid — proven at the data plane | hand-built SAS → `POST /sdap-jobs/messages/head` → **HTTP 204** |
| Prod + `staging` slots both run the **secondary** string | fp `f6d0dfd1ac9f` on both |
| KV `ServiceBus-ConnectionString` holds the **new primary** | fp `3db62606e51e` |

A live SAS value was echoed in terminal output (it was a plaintext app setting). Never written to
any file, commit or record — only fingerprints. That key is the rotated one, so it is dead.

### 2. 🔴 A `staging` slot EXISTS and is RUNNING — the note that said "0 exist" was FALSE

It has its **own app settings** and reports the **same `cloud_RoleName`** to App Insights. It caused
a ~40-minute dev outage I misdiagnosed for six attempts: I fixed *production* repeatedly (each fix
fingerprint-verified correct) while the slot looped on the dead key.

**The tell I misread**: the failure rate never dipped at *any* production restart. Check for slots
the **second** time a restart changes nothing, not the sixth.

Corrected in `CLAUDE.md:86`. **031 must not create the slot — it exists. 033 must purge BOTH.**

### 3. ⚠️ Always pass `--subscription` to `az`

The CLI default drifted to **"Spaarke Model 1 Production"** during 051, which made correctly-granted
RBAC look absent and surfaced a prod-named shared namespace. Owner decision #1 is **dev only**.
Dev subscription: **`484bc857-3802-427f-9ea5-ca47b43db0f0`**.

### 4. Live environment changes made across this project (all authorised)

| Change | Reversible? |
|---|---|
| `spaarke-search-dev` `apiKeyOnly` → `aadOrApiKey` (053) | yes |
| UAMI granted namespace-level SB `Data Sender` + `Data Receiver` | yes |
| **Service Bus PRIMARY key regenerated** | ❌ **NO — permanent** |
| KV `ServiceBus-ConnectionString` → new primary | yes (versioned) |
| Prod + staging app settings → secondary connection string | yes |
| Owner granted **my user** `Cognitive Services User` on `spaarke-openai-dev` (050 measurement) | **STILL STANDING — offer to remove** |

**Nothing changed in this session** — 051's code half used read-only Azure queries only.

---

## §0.1 What 051 delivered (code half, 2026-08-23)

Full record: [`notes/decisions/051-sas-rotation.md`](notes/decisions/051-sas-rotation.md).

- **The census was wrong four ways.** The POML named `ServiceBusJobProcessor.cs` — it constructs
  nothing (client is injected). Real state: **4 construction sites, 3 duplicate `ServiceBusClient`
  singleton registrations**; last-registration-wins meant only `JobProcessingModule` was ever used,
  and the two dead ones read a **different config key**, so the SAS credential was live under **two
  spellings** (`ConnectionStrings:ServiceBus` + `ServiceBus:ConnectionString`).
- **New `Infrastructure/Auth/ServiceBusClientFactory.cs`** — one credential decision, mirroring
  `SearchClientFactory` (053). Namespace wins over a present SAS string → the cutover is one setting
  and the rollback is removing it.
- **One unconditional registration**; the ADR-032 asymmetric-registration gate removed (3rd instance).
- **`ServiceBusOptions`**: `FullyQualifiedNamespace` added; `ConnectionString` `[Required]` relaxed.
- **The two keys reconciled via `PostConfigure`** — without it this task would have caused a
  **second outage** (the deployed app sets only the legacy key).
- **`MembershipJunctionUpdaterHost` migrated** off its inline `DefaultAzureCredential`, so the new
  guard needs **no allowlist**.
- **Criterion 4 needed more than a throw**: RBAC-absent surfaces at *receive* time and the processor
  retried forever while `/healthz` returned **200** — the observed outage. Auth failures now log
  **Critical**; `ServiceBusJobProcessor` implements `IHealthCheck` (Degraded at 1, Unhealthy at 3).
  Host deliberately **not** killed (FAILURE-MODES AP-7).
- **`tests/Spaarke.ArchTests/ServiceBusClientGuardTests.cs`** (4 tests). Negative control fired on a
  seeded violation — and **caught a defect in my own first version**, which could be evaded by
  renaming a local.
- **`adr-check` found one violation, mine**: a seam test using the ADR-038 ban-B4
  `ArgumentNullException` shape. Replaced with a structural assertion (path C).

**RBAC verified present** — escalation trigger did NOT fire: `mi-bff-api-dev` (principal
`9fd47efb-…`) holds `Azure Service Bus Data Sender` + `Data Receiver` at **namespace** scope on
`spaarke-servicebus-dev.servicebus.windows.net` (RG `SharePointEmbedded`).

**Booked into the POMLs** (not just these notes): 031 obligations `051-A..D`, 033 obligations
`051-E..F`. Includes the exact setting 031 must apply to prod **and** staging:
`ServiceBus__FullyQualifiedNamespace = spaarke-servicebus-dev.servicebus.windows.net`.

**Open owner question**: principal `38f7693f-…` also holds Sender+Receiver on the dev namespace —
unidentified.

---

### Files modified this session — ALL COMMITTED AND PUSHED (working tree clean)

| Commit | Task | What |
|---|---|---|
| `243b514c1` | **022** | 9 files: the 4 OBO clients + 5 app-only credential sites → the provider. New `ConfidentialClientTokenCredential.cs`. `IdentityConfigurationValidator` rule 5 |
| `bde4a640d` | **060** | New `tests/Spaarke.ArchTests/CredentialGuardTests.cs` (8 tests) |
| `502f31395` | **061** | New `CredentialCensusTests.cs` (5) + `SourceScan.cs`; `CredentialGuardTests` rewired onto it |
| `047a9ae40` | **062** | `IdentityConfigurationValidator` rule 6 + `CredentialSelectionOptions.RequireSecretFreeIdentity`; 5 seam tests. Booked the enable-flag onto 033's POML |
| `05818d8b2` | **063** | `.claude/skills/test-diet/SKILL.md` heuristic 0 + seam drift fix; `tests/CLAUDE.md` fitness-function section |
| `250a5faae` | **055** | `AnalysisOptions.cs` (2 dead properties), `appsettings.template.json`, 2 provisioning scripts |
| `0ece27567` | **052** | `.claude/adr/ADR-028` E-2 re-affirmation block (no code) |
| `7a15d8df1`, `153cf9f69`, `35bc0d9c7` | — | Checkpoints |

### Critical context (3 sentences)

Phase 2 and Phase 6 are done: every BFF-identity confidential client now takes its credential from
`OrderedCredentialClientProvider`, and the forcing functions that prevent regression are in place and
**demonstrated live** (a seeded ninth secret-bearing client fails the build). The remaining work splits
cleanly in two: **Group F (050–056)** is safe autonomous code-and-config work, while **031→032→033** is
blocked on an owner decision, a second test principal that does not exist, and a soak period that cannot
be compressed. Nothing is broken and nothing is half-done — every task is committed with its decision
record.

### ⚠️ Two items needing the OWNER, not more work

1. **ADR-038 path-B amendment** (task 063) — add `tests/Spaarke.ArchTests/**` as an eighth KEEP path.
   The directive + skill changes already hold the line, so nothing is blocked.
2. **Task 051's "rotate the leaked SAS"** — `appsettings.Development.json` holds a live Service Bus SAS
   key. Rotating a shared live credential is outward-facing; do not run 051's rotation step unattended.

---

## ⚠️ Group F (050–056) — free prerequisites already established by task 052's RBAC sweep

**Read before starting any of these.** A subscription-wide role listing for the UAMI
(`9fd47efb-7962-492b-ac44-e5ccd0268ebb`) turned up prerequisites the individual tasks would otherwise
each go and discover:

| Task | What is already true |
|---|---|
| **053** AI Search | 🔴 **THAT LINE WAS WRONG — CORRECTED 2026-08-22.** The role was **INERT**: `spaarke-search-dev` was `apiKeyOnly`, so the data plane returned **HTTP 403** to every Entra token before any role was evaluated. **Owner authorised the fix and it is APPLIED** — the service is now `aadOrApiKey` (additive; reverse with `--auth-options apiKeyOnly`). Entra probe now returns **200** |
| **051** Service Bus | UAMI holds **`Data Sender`** + **`Data Receiver`**, but **scoped to the `sprk-membership-changes` TOPIC ONLY**, not the namespace. Do NOT assume namespace-level access — any other topic/queue needs its own grant. ⚠️ This task also says "rotate the leaked SAS": `appsettings.Development.json` contains a live Service Bus SAS key. **Rotating a live shared credential is an outward-facing action — confirm with the owner first** |
| **050** Content Safety | ✅ **DONE.** There IS no separate Content Safety resource — `spaarke-openai-dev` (kind=AIServices) *is* the endpoint, and the UAMI's `Cognitive Services User` is the correct role for it |
| **033 / 055** | The Key Vault is **`spaarke-spekvcert`**, resource group **`SharePointEmbedded`** (UAMI holds `Key Vault Secrets User`). This answers the vault question task 055 could not resolve — it is where the secret purge happens |

### 🔴 The UAMI is NOT in `rg-spaarke-dev`

It lives in **`spe-infrastructure-westus2`**. `az identity show -g rg-spaarke-dev -n mi-bff-api-dev`
returns **`ResourceNotFound`**. Resolve it from the App Service instead — by resource ID, per this
project's own rule:

```bash
az webapp identity show -g rg-spaarke-dev -n spaarke-bff-dev --query userAssignedIdentities
```

`spaarke-openai-dev` and `spaarke-search-dev` are also in `spe-infrastructure-westus2`, and the Service
Bus namespace is in `SharePointEmbedded`. **The dev estate spans at least four resource groups.**

---

## Group F — completed

| Task | Outcome |
|---|---|
| **053** | ⛔ **BLOCKED at step 1.** `spaarke-search-dev` is `apiKeyOnly` — Entra returns **403**, so the UAMI's `Search Index Data Contributor` is inert. Also found: **7 key sites, not the POML's 2** (the POML names a DEAD property and misses the single `SearchIndexClient` the whole RAG stack uses), and clearing the key would **un-register 6 services** (ADR-032 asymmetric registration). **No code changed.** [Record](notes/decisions/053-ai-search-to-entra.md) |
| **056** | ✅ Bing key resolution changed per constraint — **but criterion 1 is structurally unmeetable** by the mandated pattern (App Service resolves KV refs INTO config either way); stated, not redefined. 🔴 **Real find: web search was returning FABRICATED results with real-looking URLs as web citations, with no degradation note** — the only path dev ever took, since no Bing key exists anywhere. Now empty + explicit note; mocks behind an opt-in. New `FabricatedResultGuardTests` (negative control demonstrated). [Record](notes/decisions/056-bing-key-kv-by-name.md) |
| **054** | ✅ Three DocIntel keys resolved: `AiSearchKey` already done by 053 (no double work), `OpenAiKey` made key-or-MI with the key **retained under E-2**, `DocIntelKey` **escalated** — `spaarke-docintel-dev` has no custom subdomain so Entra is impossible there (prod HAS one). **Also closed a gap 053 left**: the options validator still required `AiSearchKey`, so clearing it would have failed startup anyway. [Record](notes/decisions/054-doc-intelligence-to-entra.md) |
| **053** | 🔄 **Code-complete.** Service unblocked to `aadOrApiKey` (owner-authorised). **6 of 7 sites migrated** onto a new `SearchClientFactory` — the POML said 2 and missed the only `SearchIndexClient` in the codebase. Fixed an **ADR-032 asymmetric registration** where clearing the key would have un-registered 6 services. Site 6 = E-1 carve-out; site 7 was **dead** (3rd this workstream). **Keys NOT cleared** — live cutover booked to 031. [Record](notes/decisions/053-ai-search-to-entra.md) |
| **050** | Content Safety was **already on MI** — no key existed to clear. Verified RBAC + no-key + MI-enabled, removed a dead `ContentSafety-ApiKey` KV reference from the template. 🔴 **Found a live safety defect**: the Prompt Shield perimeter has failed OPEN on **122 of 122** scans over the full 90-day window — cause is the 100ms deadline, **not** auth (token = 7ms). **ESCALATED**, not fixed. [Record](notes/decisions/050-content-safety-to-mi.md) |
| **055** | `Analysis:PromptFlowKey` — **DEAD, deleted** (`250a5faae`). Zero readers, never deployed, KV entries were never-updated placeholders. The one Prompt Flow artifact in the repo uses the Foundry SDK's `@tool` decorator and does not read this key. KV purge booked to 033. [Record](notes/decisions/055-promptflow-key-disposition.md) |
| **052** | ADR-028 **E-2 RE-AFFIRMED**, key NOT cleared (`0ece27567`). Custom subdomain IS configured → the hoped-for one-config fix never existed. **Near-miss**: a user-token 200 nearly became "E-2 disproven", but E-2 already documents that exact test passing — the distinguishing fact is user-200 vs MI-401. The MI half is untestable from here (Kudu SCM has no `IDENTITY_ENDPOINT`). One cheap test booked to 031. [Record](notes/decisions/052-azure-openai-e2-retest.md) |

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

## Phase 6 — COMPLETE. The forcing functions are in place

| Task | Outcome |
|---|---|
| **060** | `tests/Spaarke.ArchTests/CredentialGuardTests.cs`, 8 tests (`bde4a640d`). The credential ban + all three booked guards (010 decoupling, 023 no-name-resolution, 020 assertion reuse). **Success criterion 12 demonstrated live.** [Record](notes/decisions/060-credential-guard.md) |
| **061** | `CredentialCensusTests.cs`, 5 tests (`502f31395`). 7 sites / 6 files, per-FILE counts, both SDKs. **Cross-assembly blind spot demonstrated live** from `Spaarke.Dataverse`. [Record](notes/decisions/061-credential-census.md) |
| **062** | `IdentityConfigurationValidator` **rule 6** + `RequireSecretFreeIdentity` (`047a9ae40`). Asserts the ORDER, not a resolution — a startup probe would refuse to boot during Entra's measured ~2-min flap. Inert by default. [Record](notes/decisions/062-startup-credential-assertion.md) |
| **063** | `/test-diet` heuristic 0 + `tests/CLAUDE.md` fitness-function category (`05818d8b2`). **Also fixed a repo-wide defect**: the classifier was missing `tests/integration/seam/**`, so every seam test in the repo was a delete candidate. **Carries an OPEN ADR-038 path-B proposal.** [Record](notes/decisions/063-archtest-keep-path.md) |

## 🔔 OPEN OWNER DECISION — ADR-038 amendment (task 063, CLAUDE.md §6.5 path B)

ADR-038 names *"NetArchTest-style architecture tests at Tier 1"* as the sanctioned replacement for the
discovery lost to bans B1–B5 — and its own KEEP-path list leaves them unprotected, so `/test-diet`
recommends deleting them. **Proposal: add an eighth category, `tests/Spaarke.ArchTests/**` (structural
fitness functions).** The gap is general — `LayerDependencyTests` and `ADR010_DITests` have had the same
exposure since before this project existed. ADR-038 was NOT edited; the directive + skill changes hold
the line meanwhile and are marked ratification-open. Full write-up in the 063 record §4.

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
