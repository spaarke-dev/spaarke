# Decision record — 001: dev deployment slot

> **Task**: `tasks/001-create-dev-deployment-slot.poml` · **Executed**: 2026-08-20 · **Rigor**: FULL
> **Outcome**: slot live and healthy; **not swapped**. Three findings that change later tasks.

---

## 1. What now exists

| Property | Value |
|---|---|
| Slot | `staging` on `spaarke-bff-dev` (`rg-spaarke-dev`) |
| URL | `https://spaarke-bff-dev-staging.azurewebsites.net` |
| `/healthz` | **200 `Healthy`** · `/ping` → `pong` |
| Identity | **UserAssigned only** — `mi-bff-api-dev` |
| Identity resource ID | `/subscriptions/484bc857-…/resourcegroups/spe-infrastructure-westus2/providers/Microsoft.ManagedIdentity/userAssignedIdentities/mi-bff-api-dev` |
| principalId | `9fd47efb-7962-492b-ac44-e5ccd0268ebb` — the FIC subject |
| `keyVaultReferenceIdentity` | the same UAMI resource ID (**set manually — see Finding A**) |
| App settings | 213, mirrored from the production slot |
| Swapped? | **NO.** Production slot untouched, still 200 throughout |

Identity was assigned **by full resource ID**, never by name. The decoy `spaarke-bff-identity`
(principalId `c8cdf6fc-a414-4a5b-981c-006d0d84850f`, in resource group `SharePointEmbedded`) was confirmed to
exist in the subscription and is **not** attached to the slot. Note the real UAMI lives in a *different*
resource group (`spe-infrastructure-westus2`) from the App Service (`rg-spaarke-dev`) — which is precisely why
name-based resolution is banned.

**Reversal** (one command):

```
az webapp deployment slot delete --name spaarke-bff-dev --resource-group rg-spaarke-dev --slot staging
```

---

## 2. FINDING A (red) — `keyVaultReferenceIdentity` is not copied by `--configuration-source`, and is not in IaC

**This cost the first deploy attempt.** The slot was created with `--configuration-source spaarke-bff-dev`,
which faithfully copied all 213 app settings. The app then **aborted at startup with exit code 134 (SIGABRT)**
after 8.2 s — the same signature as the `#3b` attempt-1 outage this project's CLAUDE.md warns about.

Cause: `keyVaultReferenceIdentity` is a **site-level property, not an app setting**, so it is not copied.

| | `keyVaultReferenceIdentity` |
|---|---|
| Production slot | `…/userAssignedIdentities/mi-bff-api-dev` |
| Staging slot, as created | `SystemAssigned` — and the slot has **no** system-assigned identity |

Every Key Vault reference in the slot (`ConnectionStrings__Redis`, `AzureOpenAI__ApiKey`,
`ServiceBus__ConnectionString`, `AiSearch__ReferencesApiKey`, `DocumentIntelligence__AiSearchKey`,
`RecordSync__AiSearchApiKey`, `Communication__WebhookSigningKey`) therefore failed to resolve. Setting the
property to the UAMI and restarting fixed it: `/healthz` → 200.

### Why this matters beyond task 001

1. **It is a swap hazard.** `keyVaultReferenceIdentity` is a site property, and site properties **do not swap**.
   It must be correct on the slot permanently — it will not arrive via the swap at task 032.
2. **It is unmanaged production drift.** `grep -rn keyVaultReferenceIdentity infrastructure/bicep/` returns
   **nothing**. Production `spaarke-bff-dev` depends on this property today, and re-applying the IaC would reset
   it to `SystemAssigned` and break every Key Vault reference in production. Filed as **ISS-001**.
3. **Deployment slots are absent from the IaC entirely** — no slot resource in
   `infrastructure/bicep/modules/app-service.bicep`. The slot created here is drift by construction.

**Obligation added to task 031**: verify `keyVaultReferenceIdentity` on the slot *before* running the §6.1 OBO
checklist. A slot that cannot resolve Key Vault references fails in ways that look like credential failures —
exactly the wrong diagnosis to reach while testing a credential change.

---

## 3. FINDING B (amber) — escalation trigger FIRED: app-setting mirroring copied plaintext secrets

The POML carries this trigger:

> *"Mirroring app settings would copy a secret value into a new location — STOP; the secret surface is being
> reduced, not expanded."*

**It fired, and it fired because of the action rather than before it.** `--configuration-source` was passed on
the `slot create` call, so the mirror happened as part of slot creation rather than as a separate, reviewable
step. That was a sequencing mistake: the trigger was meant to be evaluated first.

### What was actually copied

The dev App Service holds its secrets as **plaintext app-setting values, not Key Vault references**. 16 such
settings were duplicated into the slot, including the ones central to this project:

`AzureAd__ClientSecret` · `API_CLIENT_SECRET` · `Dataverse__ClientSecret` · `AgentToken__ClientSecret` ·
`PowerBi__ClientSecret` · `Ai__OpenAiKey` · `Ai__DocIntelKey` · `DocumentIntelligence__DocIntelKey` ·
`DocumentIntelligence__OpenAiKey` · `BuilderAdmin__ApiKey` · `Rag__ApiKey` · `ConnectionStrings__ServiceBus` ·
`Notifications__SignalR__ConnectionString` · `Compose__Webhook__SigningKey` ·
`EmailProcessing__WebhookSecret` · `EmailProcessing__WebhookSigningKey`

Seven *other* settings **are** proper Key Vault references, so the estate is inconsistent rather than uniformly
plaintext.

### Resolution: keep the faithful mirror. Reasoning, and the alternatives rejected

**Kept.** Not because it is harmless, but because both alternatives are worse:

- **Strip the secrets from the slot.** The slot would not boot — Finding A is the empirical demonstration of
  what incomplete config does here — so task 031's OBO checklist could not run. Worse, app settings that are not
  marked sticky **swap with the code**, so a secret-less slot swapped at task 032 would strip *production's*
  secrets. On a fail-closed OBO path that is a total dev outage. This is the most dangerous option available.
- **Convert the slot's secrets to Key Vault references.** Superficially attractive and better hygiene, but it
  introduces a *second variable* into the exact comparison the slot exists to make. The slot's purpose is to
  isolate one change — the credential mechanism. If the slot also differs from production in how it resolves
  configuration, a failure during the §6.1 checklist becomes ambiguous between the two causes. It would also
  silently push a config-resolution change into production at swap time.

The faithful mirror keeps **the credential provider as the only difference** between slot and production, which
is what makes the Phase 3 verification interpretable.

### Cost of this decision, booked now rather than discovered later

- **Task 033 must purge the secret from BOTH slots**, not one. Its acceptance criteria have been amended.
- Duplication is confined to the **same App Service resource, same RBAC boundary, same subscription** — no new
  trust boundary was crossed and no secret left Azure.
- Fully reversible by deleting the slot.

**Owner: reverse in one command if you disagree** — see §1.

### The larger point, which belongs to task 033

That `AzureAd__ClientSecret` and `API_CLIENT_SECRET` sit in App Service configuration as **plaintext** — not as
Key Vault references — is itself a finding. `.claude/constraints/auth.md` states *"MUST NOT add plaintext
secrets to `appsettings*.json` — Key Vault references only in production; dev OK with plain values"*. Dev is
explicitly carved out, so this is **not** a violation. It does mean FR-C3's removal work is app-setting
deletion, not merely Key Vault deletion, and now across two slots.

---

## 4. FINDING C (amber) — `Deploy-BffApi.ps1` cannot deploy to a slot without swapping

`-UseSlotDeploy` runs deploy → health-check → **swap** → verify → rollback-on-failure as one flow. There is no
`-SkipSwap` / `-NoSwap` parameter (verified against the full param block at `scripts/Deploy-BffApi.ps1:86-115`).

That is incompatible with this project's rollout, which requires deploy and swap to be **separate,
operator-gated steps** (tasks 031 and 032, with a soak between). The deploy here was therefore done explicitly:

```
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
# zip deploy/api-publish -> deploy/api-publish.zip
az webapp deploy --resource-group rg-spaarke-dev --name spaarke-bff-dev \
                 --slot staging --src-path deploy/api-publish.zip --type zip
```

**Obligation added to tasks 031 and 032**: do **not** reach for `-UseSlotDeploy`. Either use the explicit
commands above, or add a `-SkipSwap` switch to the script first. Task 032 owns the swap, alone.

---

## 5. Incidental confirmation for task 023 (FR-B4)

The UAMI/app-registration conflation hazard is real and now empirically confirmed rather than inferred. In the
slot's own app settings:

| Setting | Value | Actually is |
|---|---|---|
| `AZURE_CLIENT_ID` | `5967251e-171c-46fe-a6c2-ef843c90309d` | the **UAMI** clientId |
| `API_APP_ID` | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | the **app registration** |
| `AzureAd__ClientId` | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | the **app registration** |

`GraphClientFactory.cs:54` resolves `AZURE_CLIENT_ID ?? API_APP_ID`. In Azure, `AZURE_CLIENT_ID` is populated
and is the **UAMI's** — so that fallback silently yields the wrong identity. Task 023's guard now has a live
reproduction to test against.

---

## 6. Per-task obligations (root CLAUDE.md §10)

| Obligation | Result |
|---|---|
| Publish size | **43.67 MB compressed, incl. PDBs** (137.25 MB uncompressed; 2.23 MB of PDBs) |
| Delta vs 44.96 MB net10 baseline | **−1.29 MB.** No dependency change in this task — treat as build/compression variance, not a real shrink |
| Ceiling (60 MB) | ✅ well under |
| CVE scan | ✅ `dotnet list package --vulnerable --include-transitive` → no vulnerable packages |
| Build | ✅ 0 errors, 7 pre-existing obsolete-API warnings |
| Placement justification | N/A — no code added to the BFF; this task is infrastructure only |

---

## 7. Acceptance criteria

| # | Criterion | Result |
|---|---|---|
| 1 | Slot exists and `/healthz` returns 200 | ✅ `Healthy` (after Finding A was fixed) |
| 2 | `az webapp identity show` reports UserAssigned only, `mi-bff-api-dev` | ✅ |
| 3 | Slot NOT swapped; production slot unchanged and still serving | ✅ production 200 throughout |
| 4 | No new secret value written to a new location | ⚠️ **NOT MET — see Finding B.** Trigger fired; mirror deliberately retained with reasoning and a booked cleanup obligation on task 033 |
| 5 | `dev.bicepparam` declares P1v3 | ✅ was `B1`; the drift was in the file, not the environment |
| 6 | principalId is `9fd47efb-…`, NOT the `c8cdf6fc-…` decoy | ✅ decoy confirmed present in the subscription and unattached |

**7 of 7 steps executed. 5 of 6 criteria met; criterion 4 is a reasoned, reversible, recorded exception.**
