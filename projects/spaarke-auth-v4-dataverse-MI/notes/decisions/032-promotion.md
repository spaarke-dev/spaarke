# Task 032 — promotion of MI-FIC to the default slot

> **Status**: ✅ **COMPLETE.** Swap done and verified; staging slot deleted 15:37:45Z.
> The Office add-in check was deferred to the testing phase by owner decision — see §6.
> **Date**: 2026-08-24

---

## 1. What happened

| | |
|---|---|
| Swap started | **14:49:22Z** |
| Swap returned (exit 0) | **14:50:59Z** (~97 s) |
| Verification window | 14:51 – 15:00Z |
| Result | **green on every automated checklist item** |
| Staging slot | **DELETED 15:37:45Z** (§6). It was retained as the swap-back rollback target through the whole verification window, and released only after the add-in risk was measured rather than assumed |

The swap was the plain `az webapp deployment slot swap --slot staging --target-slot production`.
`Deploy-BffApi.ps1 -UseSlotDeploy` was NOT used, per the constraint — it re-deploys first, and this task
promotes an **already-verified** slot.

## 2. Pre-swap state (steps 1–3)

**Step 1 — gate.** 031's secret-first rollback re-verification (§5.6) present and committed; working tree
clean, 0 unpushed.

*Gate note, stated rather than glossed:* 031 is green on every surface **testable before the swap**. Its
one open item — the Office add-in save flows — is **definitionally untestable pre-swap** (see §5) and was
moved into this task's own acceptance criteria by owner-visible decision. The substantive content of the
step-1 gate, and the thing the third escalation trigger actually names, is the rollback re-verification.
That passed.

**Step 2 — site properties, which do NOT swap.** Identical on both slots:

| Property | Both slots |
|---|---|
| `keyVaultReferenceIdentity` | `…/userAssignedIdentities/mi-bff-api-dev` — **by resource ID**, the FIC subject identity, not the `spaarke-bff-identity` decoy |
| `identity.type` | `UserAssigned` |
| assigned UAMIs | exactly one: `mi-bff-api-dev` |
| state | Running |

**Step 3 — what the swap would actually move.** App settings were diffed between slots first (names +
value fingerprints only; no value printed):

```
default: 213    staging: 213
only on default : 0        only on staging : 0
differing values: 0        sticky (slot settings): none
Graph__Credentials__Order__* : absent on both  (canonical [MI-FIC, ClientSecret] applies)
```

**The two slots were byte-identical on configuration.** So the swap moved *only code*. That is the
cleanest possible pre-swap state and it removes a whole class of post-swap surprise.

Build identity confirmed different, so the swap was not a no-op:

| Slot | Deployment | Time |
|---|---|---|
| default (before) | `a55f075e…` | 2026-08-21 16:05Z |
| staging (before) | `62eb7755…` | 2026-08-24 12:52Z ← the build verified in 031 |

Rollback prepared and stated before swapping: the identical swap command run again, verified by deployment
id returning to `a55f075e…`.

## 3. Post-swap verification (steps 4–6)

**Swap confirmed structurally** — deployment ids exchanged exactly as predicted: default now `62eb7755…`
(2026-08-24), staging now `a55f075e…` (2026-08-21).

### The trap, a sixth time

The first log pull after the swap showed OBO working and `CCA configured with ClientId from API_APP_ID`,
but **no `built with credential …` line**. That is not evidence of the secret — it is the §5.6 cache-miss
rule again in a new form: **a slot swap does not restart the process.** It re-routes the already-warm
worker from staging, so the confidential-client cache carried across and never missed.

Inferring the answer from the surrounding facts would have been easy and would have been exactly the
mistake §5.5 was retracted for. A restart was forced instead (14:53:10, and again 14:56:23 after the
live log stream dropped with `Log stream interrupted`), and the line was recovered from the **persisted**
logs rather than the flaky stream:

```
2026-08-24T14:59:14.947Z  Confidential client for 1e40baad-e065-4aea-a8d4-4b7ab273458c
                          built with credential ManagedIdentityFederated.
```

**On the default slot, after the swap.** This is the project's central claim, proven at the §5.6 standard
on the now-primary slot.

Corroborating, from the same logs:

```
14:49:48Z  Ordered credential selection active: ManagedIdentityFederated > ClientSecret.   (swap warm-up)
14:53:10Z  Ordered credential selection active: ManagedIdentityFederated > ClientSecret.   (restart)
```

`A4 DEVIATION`: **absent** after 14:49Z — the positive signal, not merely the absence of an error.
Auth-namespace `warn:`/`fail:` after 14:49Z: **none**. No fall-through, no MI-FIC failure. That negative
matters: the ordered provider reaches `ClientSecret` only when `ManagedIdentityFederated` *fails*, and a
failure is logged.

### Checklist, run against the default slot

| Surface | Result |
|---|---|
| `/healthz` | 200 |
| `/api/me` (OBO) | 200 |
| no token | 401 |
| malformed token | 401 |
| CIAM dual-scheme group `/api/v1/external/me`, workforce JWT | 200 |
| RagApiKey endpoint with a valid workforce JWT | **401** — still genuinely pinned, no fallback to the default scheme |
| SPE upload (OBO) | 200 |
| SPE download (OBO) | 200, **byte-identical round-trip** |
| SPE delete (OBO, cleanup) | 204 |
| Chat SSE `dataverse.*` | 200 `text/event-stream`, 1 `SYS-Dataverse_Read_Query`, **2 live `sprk_matter` citations** |

### Step 6 — error rates

Sampled 14:59:55 → 15:00:27, interleaved authed/unauthed:

```
authenticated OBO : 25/25 = 200   (0 non-200)
unauthenticated   : 25/25 = 401   (0 unexpected)
```

Flat. Zero auth errors. Combined with the ~90 probes run during verification, no failure was observed at
any point after the swap.

## 4. One thing I could not explain — recorded, not buried

App-setting count went **213 → 212 on BOTH slots** between the 14:48Z baseline and the post-swap check.
Both slots remain byte-identical to each other (212 each, zero differing values), and every
credential-relevant key was verified present:

```
AzureAd__ClientSecret PRESENT · API_CLIENT_SECRET PRESENT · Dataverse__ClientSecret PRESENT
AgentToken__ClientSecret PRESENT · AZURE_CLIENT_SECRET absent (expected — 3rd in the resolution chain)
Graph__Credentials__Order__* absent (canonical default) · Graph__ManagedIdentity__* PRESENT
```

**I cannot name the dropped key**, because the pre-swap capture printed only *differences* and totals, not
the full name list — a gap in my own method, not in the platform. There is no functional symptom across
~140 probes and the credential surface is verified intact, so this is non-blocking.

`ServiceBus__FullyQualifiedNamespace` is absent on both slots — that is **expected**, not the missing key:
it is task 051's cutover setting, still 🔄 pending.

**Obligation for 033**, which edits app settings deliberately: capture and commit the **full app-setting
name list for both slots before the first change**, so a delta like this is attributable rather than
speculative. 6-hourly config snapshots exist back to 2026-07-25 (`az webapp config snapshot list`) as a
forensic fallback; restoring one was rejected here as riskier than the discrepancy.

## 5. The Office add-in gate — why it existed, and why it was released

Step 7 deletes the staging slot. The acceptance criterion added 2026-08-24 requires the **Outlook and Word
save flows** to pass against the default slot *before* that, and it needs a human — the add-in mints its
own token inside the Office host and cannot be driven over HTTP.

**Why this is now testable, when it was not before.** The add-ins are deployed and live at
`https://icy-desert-0bfdbb61e.6.azurestaticapps.net`, and their BFF URL is baked in at **build time**:
`deploy-office-addins.yml` sets `BFF_API_BASE_URL=https://spaarke-bff-dev.azurewebsites.net` — the default
slot. Before the swap that was the pre-migration build on the client secret, so a green add-in test would
have proven nothing about MI-FIC. **After the swap, the same deployed artifact points at the migrated
build**, with no rebuild, no redeploy and no repointing.

### Rollback is still available in BOTH forms at this point

| Form | Command | Status |
|---|---|---|
| Swap back | `az webapp deployment slot swap … --slot staging --target-slot production` | **available — the staging slot still exists** |
| Reorder credentials to secret-first | set `Graph__Credentials__Order__0=ClientSecret` | available, and **proven** in 031 §5.6 |

Deleting the slot retires the first form permanently. That is the whole reason ordering is load-bearing
here, and the reason this stop is a real gate rather than a formality.

---

## 6. Step 7–8 — slot deleted, and the rollback ladder has moved down a rung

**Owner decision 2026-08-24**: finish the project; pursue the UAT proof methods in the testing phase.
The Office add-in check moves there with them.

### The risk assessment that made releasing the gate defensible

The gate existed to protect against a **code** regression on the add-in path, since only a swap-back
fixes that. That premise was measured rather than assumed, and it is weak:

| Question | Answer |
|---|---|
| Does this branch touch the add-in client code (`src/client/office-addins/**`)? | **No** |
| Does it touch the add-in HTTP surface (`Api/Office/OfficeEndpoints.cs`)? | **No** |
| Any Office-adjacent change at all? | One: `Workers/Office/OfficeWorkersModule.cs` — task 051 removing a **provably shadowed** `ServiceBusClient` registration (`JobProcessingModule` registered the same singleton later and last-registration-wins, so Office workers already received that one). Its `"ConnectionString is required for Office workers"` guard therefore never fired — and would have blocked the MI cutover if it had |
| Is that change verified live? | Yes — `/healthz` is **Healthy** on the default slot, which exercises the 051 `servicebus-job-processing` check |

So the add-in's dependency on this branch reduces to **inbound validation + OBO + Graph/Dataverse
calls** — every one of which is proven at credential level in 031 §5 and re-proven post-swap in §3.

### Execution

```
15:37:33Z  step 7 precondition re-confirmed:
             4 secret keys present · order overrides absent -> canonical [MI-FIC, ClientSecret]
15:37:33Z  az webapp deployment slot delete --slot staging
15:37:45Z  returned exit 0  (12s)
15:37:45Z  slot list -> EMPTY. Only the default slot remains.
15:38:05Z  post-deletion: /healthz Healthy · OBO 10/10 = 200 · unauth 401
```

The staging slot's app-setting **names were captured before deletion**
([`notes/appsettings-baseline-pre-033.md`](../appsettings-baseline-pre-033.md)), so the 16 plaintext
secret app settings task 001 mirrored into it are gone with it — which was one of the two stated
reasons for the rescope. The other, the shared `cloud_RoleName` diagnostic blind spot that cost ~40
minutes on 2026-08-23, is also gone.

### 🔻 Rollback has moved down a rung — state this plainly

| Rung | Mechanism | Status |
|---|---|---|
| 1 | **Swap back** to the pre-migration build | ❌ **RETIRED 15:37:45Z** — the slot no longer exists |
| 2 | **Reorder credentials** to secret-first (`Graph__Credentials__Order__0=ClientSecret`) | ✅ **available, and PROVEN** in 031 §5.6 at credential level |
| 3 | Redeploy | available, slow |

Rung 2 is the live rollback today. **Task 033 removes the secret and therefore retires rung 2 as
well** — after 033 the only rollback is a redeploy. That is the intended end state (the whole point
is that the secret cannot be reached), but it must be a conscious step, not a surprise: 033 should
not be run casually, and its own escalation triggers are the last safety net before rung 3.

## 7. Carried into 033

1. **The 213→212 delta (§4)** — obligation discharged in advance: full app-setting **name** baseline
   for both slots captured at [`notes/appsettings-baseline-pre-033.md`](../appsettings-baseline-pre-033.md)
   before 033 touches anything. 033 diffs against it.
2. **Estate numbers were stale** — the survey found **15** scripts referencing a client secret, not the
   11 in the project notes, plus 13 docs/config/workflow files. 033 must re-derive, not trust a number.
3. **Key Vault name was wrong in the notes** — it is **`spaarke-spekvcert`**, not `spaarke-spekv-dev`
   (which does not resolve). Determined by parsing the BFF's own KV-reference app settings.
4. **`spe-owning-app-secret` lives in the same vault and is OUT OF SCOPE** (ADR-028 **E-1**,
   per-customer owning apps). 033 must not touch it. Nor `Graph-API-ClientSecret` without a check.
5. **The Office add-in check** is now a testing-phase item, alongside the UAT proof method in §8.

## 8. How to prove MI in the testing phase (for the record)

The UI cannot show it, and a status code cannot prove it — while `ClientSecret` remains in the order, a
completely broken MI-FIC would still serve every record and document successfully off the secret.

The decisive method is to **make the secret unreachable and let success be the proof**:

```
Graph__Credentials__Order__0            = ManagedIdentityFederated     # single entry, no fallback
Graph__Credentials__RequireSecretFreeIdentity = true                   # refuses to START if ClientSecret is in the order
```

Two independent guarantees: the canonical default is applied **only when the section is absent**
(deliberately — see `AuthorizationModule` §"The canonical default is applied HERE", which names this exact
edit), so narrowing the order does not silently merge `ClientSecret` back; and
`RequireSecretFreeIdentity` turns startup itself into the assertion. Note the dev app runs with
`ASPNETCORE_ENVIRONMENT=Production`, so the guard is **active** there.

After 033 this becomes the permanent state rather than a test harness.

