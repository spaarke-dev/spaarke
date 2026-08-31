# Tasks 051 + 053 — the live cutovers (the half that needed a deployed build)

> **Status**: ✅ **BOTH COMPLETE.** Service Bus and Azure AI Search now authenticate with the
> user-assigned managed identity, with **no key or SAS string configured anywhere** on the app.
> **Date**: 2026-08-24

---

## Why these were held at 🔄 until now

Both tasks were **code-complete since 2026-08-23** and deliberately stopped short of the live flip. The
reason was the same in each case and is worth restating, because it is the project's standing discipline:

> *"Flipping `AiSearch__ManagedIdentity__Enabled` on dev right now would do nothing — the deployed build
> has no `SearchClientFactory` to read it. … Deploying to verify one task's retrieval path is exactly the
> action that discipline exists to prevent."* — 053 record §9

Task **032** put the branch build on the default slot, so the precondition was finally met. Both factories
(`ServiceBusClientFactory`, `SearchClientFactory`) are present in the running build — verified before
touching either.

**RBAC verified live first**, not assumed:

| Identity | Role | Scope |
|---|---|---|
| `mi-bff-api-dev` (`9fd47efb-…`) | Azure Service Bus Data **Sender** | **namespace** `spaarke-servicebus-dev` |
| | Azure Service Bus Data **Receiver** | **namespace** `spaarke-servicebus-dev` |
| | **Search Index Data Contributor** | `spaarke-search-dev` |

The namespace-level Service Bus grants matter: earlier project notes recorded them as **topic-scoped
only** (`sprk-membership-changes`), which would have made a queue receive on `sdap-jobs` fail. They are
namespace-level now. Re-derived rather than trusted.

---

## The shape of both cutovers — narrow first, then delete

Neither factory logs which credential it picked, so **no log line can prove this one**. The same method
used for the OBO migration applies, and it is stronger than a log line anyway:

> Remove the fallback. With nothing else configured, success **is** the proof — MI by construction.

So each cutover ran as two rungs, each independently reversible:

### 051 — Service Bus

```
18:10:36Z  rung 1  ServiceBus__FullyQualifiedNamespace = spaarke-servicebus-dev.servicebus.windows.net
                   (210 -> 211)  SAS strings still present as fallback
18:12:34Z    verify   /healthz Healthy
18:13:19Z  rung 2  DELETE ConnectionStrings__ServiceBus + ServiceBus__ConnectionString
                   (211 -> 209, exactly -2)  <- no fallback remains
18:15:55Z    verify   /healthz Healthy, 3 consecutive
18:16:56Z    verify   Healthy after a further 60s of runtime
```

**The evidence is better than a status code**, from 051's own forcing function:

```
Health check servicebus-job-processing  status=Healthy
  message: "Service Bus job processing is authorized for queue 'sdap-jobs'."
MSAL cache partition: 5967251e-171c-46fe-a6c2-ef843c90309d_managed_identity_AppTokenCache
crit: lines in the sample window: 0
```

That health check is the one 051 added *precisely because* RBAC absence surfaces at **receive** time — the
2026-08-23 outage had `/healthz` returning 200 while the processor retried forever. It degrades on the
first auth failure. It reported **authorized**, repeatedly, across a window deliberately long enough to
include real receive attempts. And the MSAL partition key names the **UAMI's clientId**, so the token in
use is a managed-identity token, observed rather than inferred.

**Both key spellings were live and identical** (`f6d0dfd1ac9f` on `ConnectionStrings__ServiceBus` *and*
`ServiceBus__ConnectionString`) — the two-key fan-out 051 found. Both are now gone.

### 053 — Azure AI Search

```
18:18:14Z  rung 1  AiSearch__ManagedIdentity__Enabled = true   (209 -> 210)  keys still present
18:20:23Z    verify   /healthz Healthy
             probe    POST /api/ai/search  "contract" -> 200, 12 results
                                           "matter"   -> 200, 18 results
18:23:24Z  rung 2  DELETE AiSearch__ReferencesApiKey, DocumentIntelligence__AiSearchKey,
                          RecordSync__AiSearchApiKey, AiSearch__ApiKeySecretName
                   (210 -> 206, exactly -4)  <- no key remains anywhere
18:25:37Z    verify   /healthz Healthy
             probe    POST /api/ai/search  "contract" -> 200, 12 results  (IDENTICAL)
                                           "matter"   -> 200, 18 results  (IDENTICAL)
```

**053 criterion 2 is satisfied literally, not approximately**: *"RAG retrieval returns identical results
for a known query set."* Same counts **and the same documents** — `EX-10.1.pdf`,
`Corteva -NDA- August 2022_Signed.docx`, `Daily Briefing — 852026_…eml` — before and after the keys were
removed. Zero `403`s from `*.search.windows.net` (053 §9 step 3 names that as the missing-role signal) and
zero search errors.

Three settings held the **same** key value (`df2d166efb4d`) — the fan-out 053 predicted. The fourth,
`AiSearch__ApiKeySecretName`, held a Key Vault secret *name*, not a value; removed too, since a name that
resolves a key is just a slower way to configure one.

**`UseManagedIdentity` = flag OR key-absent**, so after rung 2 the Entra path is selected two independent
ways. The flag is redundant now and deliberately left in place: it is the documented opt-in, and removing
it would make the configuration less legible, not more secure.

### No regression

`/healthz` Healthy throughout, and the full OBO checklist re-run after both cutovers: byte-exact SPE
round-trip, **15/15 authenticated 200 · 15/15 unauthenticated 401**.

---

## 🔻 Rollback — and a deliberate decision to keep two Key Vault entries

| Cutover | Rollback | Source |
|---|---|---|
| **051** | re-add `ServiceBus__ConnectionString` (and/or `ConnectionStrings__ServiceBus`), remove the namespace setting | KV **`ServiceBus-ConnectionString`** — still present, fingerprint `3db62606e51e` (the post-rotation **primary**; the app settings had held the **secondary**, `f6d0dfd1ac9f` — both valid, proven at the data plane) |
| **053** | re-add the key settings, or just `AiSearch__ManagedIdentity__Enabled=false` | KV **`AiSearch--AdminKey`** — still present |

**Both Key Vault entries are deliberately RETAINED for now, and this is a departure from what 053 §9 step 5
and obligation 051-E ask for.** The reasoning:

- The project is **not closing** — build / deploy / **UAT** still lie ahead (owner direction, 2026-08-24).
- Deleting the vault copies during the phase where a rollback is most likely to be needed removes the
  cheap recovery path and leaves only a redeploy.
- Unlike the BFF client secret, there is no acceptance criterion here demanding vault deletion *now*;
  053's own wording gates it on live verification, which has only just happened.
- The security benefit is small and deferrable: nothing on the app **references** either entry any more,
  so neither is reachable by the running system. What remains is a stored credential, not a live path.

**Booked to project close** (after UAT): delete `ServiceBus-ConnectionString` and `AiSearch--AdminKey`
without `--purge`, exactly as the BFF secret was handled.

---

## Code-side follow-ups deliberately NOT done before UAT

Obligation **051-E** also asks to *"delete the PostConfigure back-fill in `ConfigurationModule.cs` … and
the SAS branch in `ServiceBusClientFactory.cs`"*, updating
`ServiceBusClientGuardTests.Factory_SupportsBothNamespaceAndConnectionStringPaths` in the same commit.

**Not done, on purpose.** Removing the SAS branch converts rollback from a config change into a redeploy —
during UAT. That trade is the same one weighed for the `ClientSecret` branch in
`OrderedCredentialClientProvider` (033 §6) and resolved the same way: **keep the branch, remove the
configuration.** The credential is unreachable either way; only the recovery cost differs.

Booked to project close alongside the vault deletions, as one deliberate commit that also updates the
guard test — which is exactly why 051-E flagged that test in the first place.

---

## Final state

```
Service Bus  ServiceBus__FullyQualifiedNamespace = spaarke-servicebus-dev.servicebus.windows.net
             NO ConnectionStrings__ServiceBus · NO ServiceBus__ConnectionString
             health: "authorized for queue 'sdap-jobs'"

AI Search    AiSearch__ManagedIdentity__Enabled = true
             NO AiSearch__ReferencesApiKey · NO DocumentIntelligence__AiSearchKey
             NO RecordSync__AiSearchApiKey · NO AiSearch__ApiKeySecretName
             RAG: 12 + 18 results, identical to the key-based baseline

App settings 206 (from 210) — every delta attributable
```
