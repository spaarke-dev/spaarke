# Task 031 — OBO verification on the slot

> ## 🔴 STATUS: PRE-FLIGHT ONLY. THE CHECKLIST HAS **NOT** BEEN RUN.
>
> 2026-08-23. FR-C1. Everything below §1 is a read-only pre-check completed autonomously so that the
> owner-present session can start at the checklist instead of at the setup. **No OBO surface has been
> verified. Nothing has been deployed. No live setting was changed.** Task 031 remains `not-started`
> and MUST NOT be marked complete on the strength of this document.

---

## 1. Why this task cannot be finished autonomously

031 verifies eight OBO surfaces. OBO is *delegated* auth — every one of them requires **a real signed-in
user's token**, which cannot be minted from an automation context. Three of the eight additionally
need a human driving a real client:

| Step | Blocker |
|---|---|
| SPE upload / download / preview | real delegated user token |
| `dataverse.*` tool calls over `/api/ai/chat` SSE | real delegated user token |
| **Office add-ins: Outlook + Word save flows** | a human in Outlook and Word |
| **M365 Copilot agent `/api/agent`** | a human in Copilot |
| Dataverse row-level authorization | real delegated user token |
| Send-as-user email `/api/communications` | real delegated user token |
| Long-running OBO | real delegated user token |
| Inbound validation regression (3 schemes) | workforce JWT + Ciam + RagApiKey |
| **Negative case: unauthorised user is denied on every surface** | **a SECOND test principal, which does not exist** |

The second principal is the sharpest blocker and it is not a technicality: the fail-closed criterion is
what proves the migration did not turn an authorization boundary into an error-open one. It cannot be
demonstrated with a single identity. The owner directive for this project is *"if something must exist
for dev to work E2E, create it in this project rather than deferring it"* — so creating it is in scope,
but creating an Entra principal is a tenant-level change that needs explicit authorization.

## 2. Pre-checks completed (read-only, 2026-08-23)

### 2.1 The SIGABRT trap is NOT armed ✅

Task 001 Finding A: `keyVaultReferenceIdentity` is a **site property**, and `--configuration-source`
does not copy it. When it defaulted to `SystemAssigned` (which the slot does not have), every Key Vault
reference failed and startup aborted with exit code 134 — a failure that *looks exactly like a
credential failure*, which is the worst possible misdiagnosis while testing a credential change.

Verified on both slots of `spaarke-bff-dev`:

| | default slot | `staging` slot |
|---|---|---|
| `keyVaultReferenceIdentity` | `…/spe-infrastructure-westus2/…/mi-bff-api-dev` | **same** ✅ |
| identity type | `UserAssigned` | `UserAssigned` ✅ |
| attached UAMI | `mi-bff-api-dev` | `mi-bff-api-dev` ✅ |

Resolved by **resource ID**, so this is not the `spaarke-bff-identity` decoy.

### 2.2 Both slots are healthy ✅

`https://spaarke-bff-dev.azurewebsites.net/healthz` → **200**
`https://spaarke-bff-dev-staging.azurewebsites.net/healthz` → **200**

⚠️ Note for the checklist: after task 051, `/healthz` also carries the new
`servicebus-job-processing` check. **A 200 is no longer a weak signal for job processing specifically**
— but it remains a weak signal for OBO, which has no health check. Do not substitute `/healthz` for the
checklist.

### 2.3 `AZURE_CLIENT_ID` — the booked obligation resolves to "record why not" ✅

031 carries a constraint from task 023: *"CLEAR AZURE_CLIENT_ID ON BOTH SLOTS, or record why not."*

Live state — **identical on both slots**:

```
AZURE_CLIENT_ID = 5967251e-171c-46fe-a6c2-ef843c90309d   ← the UAMI's clientId
API_APP_ID      = 1e40baad-e065-4aea-a8d4-4b7ab273458c   ← the app registration
```

**Recording why not: the trap is already defused in code.** Task 022 deleted the
`AZURE_CLIENT_ID ?? API_APP_ID` fallback from `GraphClientFactory`. Verified by grep — the *only*
remaining reader anywhere in `src/` is `IdentityConfigurationValidator.cs:65`, which reads it in order
to **detect and log the conflation**, not to resolve an identity from it.

So clearing the setting is now **hygiene, not a fix**: it removes a misleading value that reads like
configuration but drives nothing. The original danger — *"set `Graph:ManagedIdentity:Enabled=false`
during an incident and the BFF builds a `ClientSecretCredential` from a managed identity"* — **no longer
exists**, because the consuming branch is gone.

`Graph__ManagedIdentity__Enabled = true` on both slots.
`Graph__Credentials__RequireSecretFreeIdentity` is **absent on both** — correct for now; task 062 made it
off-by-default deliberately so it could not block 031/032 while the secret is still the fallback. **Task
033 must set it in the same change that drops the secret**, or the FR-F3 forcing function silently never
engages.

**Deliberately not changed here.** Clearing an app setting restarts the app. Doing that as a fragment of
a task that cannot be completed buys nothing and costs a restart on both slots. It belongs in the
owner-present session.

### 2.4 Finding: the `staging` slot is a live competing consumer on `sdap-jobs`

Both slots carry **all 213 app settings** — identical — including the Service Bus connection string and
queue name, and both are **Running**. Service Bus queues are competing-consumer, so this is not
duplicate processing; but it does mean **the "staging" slot has been silently doing real work against
dev data**, not sitting idle. That is how it came to be logging `InvalidSignature` during the
2026-08-23 outage in the first place.

This strengthens the 032 rescope (see [`032-slot-strategy.md`](032-slot-strategy.md)): the slot is not a
dormant spare. It is a second live instance, invisible in telemetry because it reports the same
`cloud_RoleName`.

## 3. What the owner-present session needs

1. **A delegated user token** for a real dev user (the recipe is in `current-task.md`).
2. **A second test principal** — decision needed: create it (per the standing directive) or descope the
   fail-closed negative criterion with a written reason.
3. **A human in Outlook, Word and Copilot** for three of the eight surfaces.
4. Roughly two minutes of tolerance for **AADSTS70025 flapping** if any FIC is touched — 8 failures
   interleaved with successes over ~130s (measured, task 030). A single green check inside that window
   proves nothing; sample across it.

## 4. Not done, explicitly

- Nothing deployed to the slot.
- No app setting changed on either slot.
- No OBO surface exercised.
- No FIC created or modified.
- The secret-first rollback re-verification — **which 032 now depends on**, since it is the evidence
  that config-only rollback works before the slot is deleted.
