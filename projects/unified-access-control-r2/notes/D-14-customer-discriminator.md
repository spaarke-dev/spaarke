# D-14 — How one customer is distinguished from another at RUNTIME

> **Status**: PROPOSED — awaiting owner decision
> **Raised**: 2026-09-29 (session 25) by the owner, during the item-6 Redis key work
> **Depends on**: [D-12](D-12-deployment-model-redefinition.md) (deployment model), [D-13](D-13-per-customer-bff-app-registration.md) (per-customer app registration)
> **Supersedes nothing.** This closes a gap D-12 exposed but did not answer.

---

## 0. The question, in the owner's words

> "for customerid — or some other way to distinguish one customer from another in the model 1 spaarke
> tenant, we need to resolve this and ensure we have this documented. is this an env variable in the app
> registration? also saved in dataverse on the customer business unit; or where does this need to be
> documented / stored / retrieved?"

---

## 1. Why the question exists at all

D-12's central finding: **`tenantId` is identical for every Model 1 customer.** They all live in the Spaarke
Entra tenant, so every `tenantId`-keyed control separates *Entra tenants*, not *customers* — and its tests
pass anyway, because there is only ever one value.

The separations that DO exist today:

| Axis | Discriminator | Where |
|---|---|---|
| Dataverse | a separate **environment** per customer | environment URL |
| Azure | a separate **resource group** (and per ADR-027 as amended, a separate **subscription**) per customer | `rg-spaarke-{customerId}-{env}` |
| Entra app identity | a separate **app registration** per customer | D-13 (BINDING) |

So the customer boundary is real at the infrastructure layer. What is missing is a **runtime** handle on it.

---

## 2. Verified state (2026-09-29, against source — not assumed)

| Layer | Carries `customerId`? | Evidence |
|---|---|---|
| Provisioning parameter | ✅ | `infrastructure/bicep/stacks/model2-full.bicep:14` — `param customerId string` |
| Resource group name | ✅ | `:62` — `var resourceGroupName = 'rg-spaarke-${customerId}-${environment}'` |
| Per-resource names | ✅ | Redis `sprk-{customerId}-{env}-redis`, UAMI `sprk-{env}-{customerId}-uami`, Key Vault, Cosmos, Search, OpenAI, App Insights — `infrastructure/bicep/customer.json:217-229` |
| Resource tags | ✅ | `model2-full.bicep:51` — `customer: customerId` |
| 🔴 **BFF app settings** | ❌ **NO** | The `bffApi` module's `appSettings` block (`model2-full.bicep:212`…`:252`) runs `DATAVERSE_URL` → `ServiceBus__CommunicationQueueName`. **No customer identifier.** |
| 🔴 **BFF code** | ❌ **NO** | Only three `customerId` occurrences in `Sprk.Bff.Api`, none of them an ambient identity: `Configuration/AnalysisOptions.cs:87` (`TenantFilterField` — an **AI Search field name**), `Api/Reporting/ReportingProfileManager.cs:18` (a Power BI **profile display-name** convention `sprk-{customerId}`), and the onboarding endpoints where it is a **request parameter** for provisioning someone else. |

**Conclusion**: `customerId` names everything the customer owns and is stamped on every resource, but a
running BFF instance cannot answer "which customer am I serving?".

---

## 3. The options considered

### ❌ (a) Read it from the app registration

D-13 gives each customer their own BFF app registration, so `AzureAd:ClientId` *is* per-customer today, and
one could map clientId → customer.

**Rejected as the primary mechanism.** An app registration is an **identity**, not a configuration store.
The mapping is implicit, needs a lookup table somewhere anyway, and breaks silently if a registration is
re-created or if D-13 is ever narrowed. It makes a security-relevant value depend on an inference.

*Retained as a cross-check*: a startup assertion MAY verify that the configured customer matches the
expected registration, turning a misconfiguration into a loud failure. That is a guard, not a source.

### ❌ (b) Look it up in Dataverse on the customer business unit

**Rejected as the runtime path.** Cache keys, log scopes and metric dimensions are constructed on hot paths;
none can afford a Dataverse round trip, and a lookup that fails would have to either block the request or
fall back — and the fallback is precisely the silent-shared-key failure this whole exercise exists to remove.

*Retained as the registry*: `sprk_dataverseenvironment` (already updated by `/provision-environment` Step 6)
is the right **human-facing** record of which customer maps to which environment, subscription and resource
group. It is the source of truth for OPERATORS, not for the request path.

### ✅ (c) An explicit app setting on the per-customer App Service — RECOMMENDED

The App Service is **already per-customer** (its own resource group, its own plan — a plan cannot span
subscriptions, which is one of the findings that forced D-12). The Bicep that creates it already has
`customerId` in hand and already writes it into the resource group name and the tags.

So emit it:

```bicep
appSettings: {
  Customer__Id: customerId        // NEW — the runtime handle
  DATAVERSE_URL: dataverseUrl
  ...
}
```

and bind it to a `CustomerOptions` with `[Required]`, so a stamp deployed without it **fails at startup**
rather than running anonymously. That fail-closed property is the whole point: an absent customer id must
never resolve to a shared default.

**Cost**: one line of Bicep, one options class, one DI registration.
**What it buys**: a cheap, explicit, fail-closed answer to "who am I serving?" available everywhere —
cache key prefixes, log scopes, metric dimensions, and the cross-check in (a).

---

## 4. What this does NOT change

🔴 **The customer boundary remains the dedicated per-customer resource, not the key.** Per D-12 §3 and the
ADR-009 amendment, Redis access control is **per-instance, not per-keyspace**: a customer-id key prefix is a
convention *our code* enforces, not a boundary Redis enforces. `Customer__Id` is **defence in depth** and an
observability handle. It is not a substitute for the dedicated instance, and no decision here may be read as
softening that.

Concretely, for the item-6 agent-thread fix: the segregation decomposes as

- **per customer** → the dedicated Redis **instance** (already decided; not a code change)
- **per user** → a key segment (the actual defect — both segments are compile-time constants today)
- **per session** → a key segment

---

## 5. Where it gets documented, once decided

| Artifact | What it records |
|---|---|
| `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` | that `Customer__Id` is a required per-stamp setting, and that a missing value fails startup |
| `docs/architecture/auth-azure-resources.md` | the naming/identity chain `customerId` → RG → resources → app setting |
| ADR-027 (subscription per customer) | a pointer: the runtime handle on the boundary ADR-027 creates |
| `projects/customer-provisioning-orchestration-r1` | the provisioning handler must emit the setting — **this is theirs, not ours** |

---

## 6. Open sub-questions for the owner

1. **Format.** `customerId` today is a short slug (`acme`, `spaarke`) constrained by resource-name limits —
   Key Vault caps at 24 chars *including* the env suffix. Is the slug the identifier of record, or should the
   app setting carry a **GUID** with the slug retained only for naming? A slug is human-readable and already
   exists; a GUID is stable under rename. **They can disagree**, and if they do, something must own the mapping.
2. **Model 1 today.** Until cpo-r1 retires `model1-*.bicep` and Model 1 adopts the dedicated shape, is there a
   Model 1 stamp that would need this setting added retroactively?
3. **Startup strictness.** Fail-closed on a missing `Customer__Id` is recommended — but it makes every existing
   deployment that lacks the setting refuse to start. Is that acceptable given no customer deployments exist yet
   (the same reasoning that made the `Secure Record` rename cheap now)?
