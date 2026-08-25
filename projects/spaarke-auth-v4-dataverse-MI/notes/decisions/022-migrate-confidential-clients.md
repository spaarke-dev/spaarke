# Task 022 — migrating every BFF-identity confidential client onto ordered selection

> Implemented 2026-08-21. FR-B3. The largest, highest-blast-radius change in the project.

---

## 1. The shape that made this smaller than it looked

Task 021 left 022 with what looked like its biggest problem: **the credential contract is async**, and all
four OBO call sites built their confidential client in a **constructor**, which cannot await. The booked
plan was to give each site lazy first-use construction with a `SemaphoreSlim`, copying
`CiamGraphClientFactory.GetOrCreateAppAsync`.

**That was the wrong shape, and noticing why removed most of the work.** The provider *already* owns the
cache, the per-`(tenant|client)` gate, and — decisively — **selection expiry**. A call site that builds
once and holds the result forever defeats the last of those: when a lower-priority credential wins during
a blip, the selection is meant to expire so the next call re-evaluates from the top. A held client would
pin that process to the fallback until somebody restarted it, which is precisely the hazard task 021
added expiry to close.

So no call site holds a client. Each asks the provider at the moment of the exchange:

```csharp
var cca = await _confidentialClients.GetClientAsync(_tenantId, _clientId, ct);
var result = await cca.AcquireTokenOnBehalfOf(scopes, new UserAssertion(userToken)).ExecuteAsync(ct);
```

On the hot path that is a dictionary lookup and a timestamp compare. It satisfies "move construction out
of constructors" trivially, **and it is more correct than the design it replaces** — the four sites now
recover automatically to the preferred credential instead of each freezing an answer.

A second consequence, unplanned and worth stating: **constructing any of these types is now free of I/O
and free of MSAL entirely.** `GraphClientFactory` used to call `Build()` even when no credential was
configured, producing a client that looked healthy and failed only at the first OBO exchange — for every
user at once.

## 2. What moved

### The four OBO confidential clients (the heart of FR-B3)

| Site | Was | Now |
|---|---|---|
| `GraphClientFactory` (Graph OBO) | ctor `.WithClientSecret` | provider, per exchange |
| `DataverseAccessDataSource` (row-level authorization) | ctor `.WithClientSecret` + static `CcaCache` | provider via the `Spaarke.Dataverse` contract |
| `DataverseUserClient` (`dataverse.*` tools) | ctor `.WithClientSecret` + static `CcaCache` | provider, concrete injection |
| `AgentTokenService` (M365 Copilot) | ctor `.WithClientSecret` + static `CcaCache` | provider, concrete injection |

### The five residual app-only secret constructions

These are `Azure.Identity` sites, not MSAL ones: each had an `if (MI enabled) DefaultAzureCredential else
ClientSecretCredential` shape. The MI branch is untouched — it authenticates as the **managed identity's
own principal**, which is already secret-free and is #3b's outcome. Only the `else` moved.

| Site | Was |
|---|---|
| `GraphClientFactory:147` (app-only Graph) | `new ClientSecretCredential(...)` |
| `DataverseAccessDataSource:211` (app-only) | `new ClientSecretCredential(...)` + `SecretCredentialCache` |
| `DataverseWebApiService:83` | `new ClientSecretCredential(...)` |
| `DataverseWebApiClient:92` | `new ClientSecretCredential(...)` |
| `DataverseServiceClientImpl:114` | `AuthType=ClientSecret` connection string |

**`GraphClientFactory:147` was not in the POML's step list** — the POML named "the three Spaarke.Dataverse
impls". It is the same shape, the same identity and the same file as site 1, and leaving it would have
forced task 060 to allowlist a `ClientSecretCredential` in a file this task had just migrated. Included,
and said rather than done quietly.

## 3. The one new component

`Spaarke.Dataverse/ConfidentialClientTokenCredential.cs` (~40 lines). Root §11:

1. **Existing** — `ManagedIdentityCredentialFactory` produces a credential for the *managed identity's*
   principal (a different identity). `Azure.Identity.ClientAssertionCredential` handles MI-FIC but has no
   ordered fallback. Verified by grep, not assumed.
2. **Extension** — not possible. `OrderedCredentialClientProvider` yields an MSAL
   `IConfidentialClientApplication`; the five consumers need an `Azure.Core.TokenCredential`. Two SDKs,
   neither extensible into the other; an adapter is the join.
3. **Cost of doing nothing** — five inline secret constructions survive, task 060's ban needs five
   unexplained allowlist entries, and the MI-disabled-in-Azure path stays secret-bound.

It is a **class deriving a framework abstract type, not an interface**, so ADR-010's 1:1 ceiling is
untouched (ArchTests 36/36 confirm). No DI registration — consumers construct it. No new package: both
`Azure.Core` and `Microsoft.Identity.Client` are already referenced by `Spaarke.Dataverse`, so **FR-14 is
unaffected** (`git diff` on the `.csproj` is empty).

**The identity does not change.** `AcquireTokenForClient` on a provider-supplied client is the same
client-credentials grant for the same app registration the `ClientSecretCredential` used. Only the proof
of it moved.

## 4. Task 011's A4 exception is CLOSED

The binding criterion, verified:

```
$ grep -rn "ConcurrentDictionary<string, IConfidentialClientApplication>" src/
src/.../Infrastructure/Auth/OrderedCredentialClientProvider.cs:74
```

**Exactly one.** Three per-class caches — `DataverseAccessDataSource`, `DataverseUserClient`,
`AgentTokenService` — plus `SecretCredentialCache` and two `CcaBuilds` counters are gone. One process now
holds one confidential client, and one MSAL OBO token cache, per `(tenant|client|kind|fingerprint)`
instead of three per identity. Task 011 booked this as a time-boxed ADR-028 A4 path-A exception with no
later owner; it expired here and it is discharged, not deferred.

## 5. The `AgentToken:ClientSecret` reconciliation — settled by measurement

Task 021 deliberately excluded `AgentToken:ClientSecret` from the provider's secret precedence
(`AzureAd:ClientSecret` → `API_CLIENT_SECRET` → `AZURE_CLIENT_SECRET`) because folding it in silently
could change which secret the agent path presents. Task 022's constraint required an explicit decision.

**Verified live on `spaarke-bff-dev`, 2026-08-21** — `AgentToken__ClientSecret`, `API_CLIENT_SECRET`,
`AzureAd__ClientSecret` and `Dataverse__ClientSecret` all hold the **same value**
(`BFF-API-ClientSecret`), and `AgentToken__ClientId` is the BFF app registration `1e40baad-…`.
`Reconcile-DemoEnvironment.ps1:76` wires the demo environment identically. So the migration presents the
same secret it presented before.

**"Identical today" is not "identical forever"**, so divergence is not left to be discovered as an opaque
`AADSTS7000215` on one endpoint. `IdentityConfigurationValidator` **rule 5** compares the two at startup
**by fingerprint** — never by value — and reports at error level. Reported rather than fatal: the
divergence is entirely inert while a secret-free credential is selected, and taking the whole BFF down
over the agent endpoint's credential would be disproportionate.

## 6. ⚠️ Task 023's live trap was REMOVED, not just guarded — and its rule changed accordingly

Task 023 found `AZURE_CLIENT_ID` on `spaarke-bff-dev` holding the **UAMI's** clientId while
`GraphClientFactory.cs:54` read `AZURE_CLIENT_ID ?? API_APP_ID` as the **app registration's**. It could
only guard it: changing app-only semantics was outside 023's scope.

This task owns that branch, and **deleted the fallback**. `GraphClientFactory` now resolves the app
registration from `API_APP_ID` alone, so:

```
$ grep -rn "AZURE_CLIENT_ID" src/ --include=*.cs
(no consumers)
```

Consequently **rule 2a was downgraded from fatal to error-level reporting**. Failing startup over a
setting that no code reads is a false positive, and this project's own AP-7 rule forbids converting an
inert condition into an outage. The rule is *not* deleted: the setting still signals that someone
believed the key meant something it does not, and reporting it is what gets it cleared at **task 031**.
`IdentityConflationSeamTests` was amended in place with the reasoning inline.

## 7. Two seam-test files migrated, one added

- **`ConfidentialClientSharingSeamTests`** — migrated in place (ADR-038 KEEP path; migration, not
  deletion). Its three questions now go to the provider. **It got stronger**: the old version could only
  count builds within one type, because each type had its own cache and two consumers provably did *not*
  share. It now asserts **reference identity** of the returned client, which is the property the
  consolidation actually buys.
- **`CredentialSelectionSeamTests`** (task 010) — amended. It asserted that the non-MI branch throws
  naming `API_CLIENT_SECRET`. That premise is what FR-B5 removes: the secret is no longer a
  construction-time requirement. The tests now assert the constructor fails actionably when its *wiring*
  is absent, plus a new positive that a **secret-free** configuration constructs.
- **`ConfidentialClientMigrationSeamTests`** — new. Fail-closed on each migrated OBO path
  (`AccessRights.None` for row-level authorization; `OboNotConfigured` for `dataverse.*` with no app-only
  fallback; explicit failure for the agent path), plus proof the app-only adapter is a **pass-through** to
  ordered selection and asks it on **every** acquisition rather than caching.

### Self-review caught a vacuous assertion of my own

The first draft of the rotated-secret test built **two** providers and asserted their clients differed.
Two providers hold two caches, so that passes whatever the cache key is — including if the secret
fingerprint had been dropped from it, which is the exact W-1 regression the test exists to prevent.
Rewritten to rotate the value underneath **one** provider through a mutable `IConfiguration`. Paired with
`TwoDifferentConsumers_…_GetTheSameClientInstance` (same secret ⇒ same instance), the two now bracket the
property from both sides.

### One test deliberately not written

A constructor guard test for blank tenant/client ids on the adapter. The guard exists in production code,
but the test sits on the wrong side of ADR-038 ban B4 — it would assert a framework one-liner — and every
consumer already refuses to reach the adapter without a configured identity. Stated in the file rather
than silently omitted.

## 8. Escalation triggers — neither fired

- *"Any OBO path cannot be migrated without changing its exchange semantics."* All four migrated with the
  scopes, the `UserAssertion`, and the MSAL call unchanged. Only the source of the client moved.
- *"A site turns out to authenticate as something other than the BFF identity."* None did. The E-1 sites
  (`SpeAdminTokenProvider`, `SpeAdminGraphService`) and the certificate site (`CiamGraphClientFactory`)
  and Power BI were checked and have **zero diff**.

## 9. The one risk I am accepting, stated plainly

`DataverseServiceClientImpl`'s non-MI branch acquires its token **synchronously**, because that is what
`ServiceClient`'s `tokenProviderFunction` shape and the #3b lesson call for. MSAL has no synchronous API,
so `ConfidentialClientTokenCredential.GetToken` blocks — on a pool thread via `Task.Run`, so it cannot
deadlock even with a synchronization context.

**Why this is not a #3b relapse**: that SIGABRT came from acquiring sync-over-async on the **startup
thread** under `ValidateOnBuild`. The mitigation for it — the `Lazy<ServiceClient>` deferring connect to
first use — is untouched, so this runs on a request thread, at most once per token lifetime. The branch is
also unreachable in every deployed environment, all of which set `Graph:ManagedIdentity:Enabled=true`.

Also worth watching, not blocking: in fixtures that boot `Program.cs` **without** the MI flag, an OBO
attempt now reaches the provider, which probes for a managed identity (~80 ms, negatively cached) before
falling through. On GitHub-hosted runners IMDS *is* reachable but carries no matching identity, which is
the fail-**loud** case — correctly rethrown, and caught fail-closed by every consumer. The full suite is
green locally; if CI timings move, this is the first place to look.

## 10. Verification

| Criterion | Evidence |
|---|---|
| All six sites take their credential from the provider; no inline construction remains | grep for `.WithClientSecret` / `new ClientSecretCredential` / `AuthType=ClientSecret` leaves **only** Power BI ×2 (deferred), E-1 ×3, and the provider's own sanctioned binding point |
| **ZERO per-class static CCA caches** | grep returns **exactly one** site — the provider (§4) |
| `DataverseAccessDataSource` uses the `Spaarke.Dataverse` contract | `IConfidentialClientProvider? confidentialClients = null`, replacing task 020's `IClientAssertionProvider` placeholder |
| Secret still configured and selectable as lowest-priority fallback | order unchanged: `[ManagedIdentityFederated, ClientSecret]`; nothing deleted |
| Both orderings work (rollback) | `CredentialOrderingSeamTests` criteria 1–2, unchanged and green |
| **Negative**: OBO fails CLOSED on every migrated path | `ConfidentialClientMigrationSeamTests` — 3 paths, incl. `AccessRights.None` for row-level authorization |
| **Negative**: E-1 + certificate + Power BI untouched | `git diff --stat` on those files is **empty** |
| All fixtures pass; full suite green | **10,592 / 0** (97 skipped, 10,689 total) · auth seams **55 / 55** · ArchTests **36 / 36** |
| `DataverseServiceClientImpl` edit confined | changed lines **44–51** (ctor param + its doc) and **108–155** (the credential block). Nothing else in the file |
| Publish size + CVE | **44.99 MB** compressed incl. PDBs, 215 files (`Compress-Archive -CompressionLevel Optimal`, framework-dependent linux-x64) — **+0.01** vs task 024's 44.98, **+0.03** vs the 44.96 baseline, ceiling 60. **No vulnerable packages.** No package added |

## 11. Placement justification (CLAUDE.md §10)

No new endpoint, no new DI registration, no new package, no new background work. One new **class** in
`Spaarke.Dataverse` (the base layer) because three of its five consumers live there and cannot reference
the BFF. The BFF-side change is net **deletion**: three static caches, two build counters, five inline
credential constructions and one ambiguous config fallback removed; one adapter added.

## 12. Booked onward

- **031** — clearing `AZURE_CLIENT_ID` on both slots is now pure hygiene rather than a fix; the trap is
  gone from the code. Rule 2 will stop logging once it is cleared.
- **033** — `Dataverse:ClientSecret` joins `Graph:ClientSecret` as a key with **zero consumers in `src/`**;
  both are deletion candidates in that task's reconciliation. `AgentToken:ClientSecret` likewise, and
  removing it also retires rule 5.
- **060** — allowlist `OrderedCredentialClientProvider` (the one sanctioned `.WithClientSecret`) and the
  E-1/Power BI sites, each with its reason. The `_cca`-decoupling source guard is still needed:
  `DataverseAccessDataSource` keeps two independent selections and a future "simplification" back into one
  `if/else` would disable OBO whenever MI is enabled.
- **061** — the census counts the provider as **one consolidated site, not expansion**, and must scan all
  server assemblies (`ConfidentialClientTokenCredential` is in `Spaarke.Dataverse`).
- **090** — `/test-diet` should treat `ConfidentialClientMigrationSeamTests` as MAINTAIN: it is the
  fail-closed contract for the project's central change.
