# D-14 — How one customer is distinguished from another at RUNTIME

> **Status**: ✅ **DECIDED AND IMPLEMENTED** — option (c) + §8 option D, by task 123 (2026-09-29).
> §6 (identifier format) was decided by the owner and implemented by task 124.
> Code: `src/server/api/Sprk.Bff.Api/Configuration/{CustomerIdResolver,CustomerOptions,CustomerOptionsValidator,CustomerIdentity}.cs`.
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

## 6. 🔴 The format is ALREADY inconsistent between Dataverse and Bicep

*(Added 2026-09-29 after the owner asked "24 character string or guid?" — the answer is neither, and the two
sides of the system already disagree.)*

**The enforced constraint** (`infrastructure/bicep/stacks/model2-full.bicep:11-14`):

```bicep
@description('Customer identifier (lowercase, alphanumeric only)')
@minLength(3)
@maxLength(10)
param customerId string
```

**3–10 characters, lowercase alphanumeric.** Not 24 — 24 is the Key Vault *name* cap for the composed
`sprk-{customerId}-{env}-kv`, not the identifier. Not a GUID.

🔴 **But the registry side uses a longer, hyphenated form.** `sprk_dataverseenvironment.sprk_customerid`
carries values like `trial-2026-08-18`
(`src/server/services/Sprk.Provisioning.ControlPlane.Tests/Registry/DataverseEnvironmentRegistryClientTests.cs:232`;
`provisioning-runs/_templates/intake.md` describes it only as "slug"). That is **16 characters with hyphens** —
it would fail the Bicep deploy on `maxLength(10)`.

⚠️ **And "alphanumeric only" is DESCRIPTION-ONLY — there is no pattern constraint.** A short hyphenated id
passes ARM and is then silently mangled:
`storageAccountName = take(toLower(replace('${baseName}sa','-','')),24)` strips hyphens, so `acme-x` and
`acmex` produce the SAME storage account name. A collision nothing validates.

### ✅ DECIDED 2026-09-29 (owner), and implemented by task 124

> "the current dataverse values can be changed and we can update our process so that it aligns with what is
> required for KV; we don't have production systems so this is easy clean up. Define the standard, document
> it, and then update components accordingly."

**The standard: `^[a-z][a-z0-9]{2,7}$` — 3 to 8 characters, lowercase letters and digits, starting with a
letter.** Full derivation in
[`docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` § "The `customerId` standard"](../../../docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md).

🔴 **Defining it exposed a latent DEPLOYMENT FAILURE, not just a naming preference.** `customer.bicep`
composes `take(format('sprk-{0}-{1}-kv', customerId, environmentName), 24)`, and Key Vault names may not end
in a hyphen. At `customerId` length 10 with `environmentName = 'staging'` the name becomes
`sprk-xxxxxxxxxx-staging-` — 24 characters ending in a hyphen, which **Azure rejects**. The previous
`@maxLength(10)` therefore admitted a value that cannot deploy, and `take()` concealed it: the name was
silently shortened rather than the template refusing.

⚠️ **The character rule cannot be enforced in the template.** ARM has no regex constraint on parameters —
there is no `@pattern` decorator, and this repo has no `bicepconfig.json` enabling experimental assertions.
Length is enforced in Bicep; the character rule is enforced **at assignment (provisioning intake)** and
documented at both parameters so the two cannot drift apart unnoticed.

**Assignment authority**: Dataverse (`sprk_dataverseenvironment.sprk_customerid`) — the row exists at intake,
before any Azure resource. Bicep CONSUMES it and never mints one.

**Still to do (cpo-r1)**: the existing registry/intake values (e.g. `trial-2026-08-18`, 16 chars with
hyphens) violate the standard and must be re-issued. Handed over in
`projects/customer-provisioning-orchestration-r1/INCOMING-CUSTOMERID-STANDARD.md`.

---

## 7. Does Azure also need a user identifier?

*(Owner question 2026-09-29: "if yes, then we can use the dataverse userid guid as the canonical user identifier.")*

**Two already exist, and both are resolved today:**

| Identifier | Resolved by | Covers |
|---|---|---|
| Entra `oid` | `CallerResolution.ResolveObjectId` (`Infrastructure/Authentication/CallerResolution.cs:89`) | every caller with a token — internal AND external CIAM contacts |
| Dataverse `systemuserid` | `CallerResolver.ResolveAsync` → `SystemUserId` | internal users only |

**Recommendation: Entra `oid` is the canonical RUNTIME user identifier; `systemuserid` stays the Dataverse
OWNERSHIP identity.** Two reasons, the second decisive:

1. `oid` is in the token — no round trip — so it is usable on hot paths (cache keys, log scopes) where a
   Dataverse lookup is not (§3b).
2. 🔴 **External CIAM contacts are `contact` rows, not `systemuser` rows.** They have no `systemuserid` at all.
   Making it canonical would leave the entire external-access plane without a user identity.

---

## 8. Startup strictness — and a fourth option that removes the dilemma

*(Owner question 2026-09-29: "what is the other option and what are the implications?")*

| Option | Implication |
|---|---|
| **A. Fail closed** | Safe and loud, but every existing stamp refuses to start until the setting is added. |
| **B. Warn and continue** | Reinstates the shared-default failure this note exists to remove. Rejected. |
| **C. Fail closed outside Development** | Softer — but a misconfigured PRODUCTION stamp is the case most worth catching. |
| ⭐ **D. Derive from `WEBSITE_RESOURCE_GROUP`** | App Service sets it automatically, and it is literally `rg-spaarke-{customerId}-{env}`. The customerId is therefore ALREADY present on every existing stamp. |

**Recommended: D, with A as the floor.** Explicit `Customer__Id` wins when present; derive from the resource
group when absent; fail only when NEITHER resolves; and log which path was used, so silent drift into
derivation is visible rather than assumed. Nothing breaks on existing stamps, and fail-closed is kept for the
case that actually matters — no identity available at all.

The cost is a dependency on the resource-group naming convention, which must be ASSERTED at startup rather
than trusted: an RG name that does not match the expected shape is a failure, not a parse-and-hope.

---

## 9. Remaining open questions — both now CLOSED

1. ✅ **Slug vs registry form** — §6. **Decided by the owner 2026-09-29**, implemented by task 124:
   `^[a-z][a-z0-9]{2,7}$`, assignment authority Dataverse. No longer blocking.
2. ✅ **Model 1 today — NO stamp needs retrofitting.** `model1-shared.bicep` does deploy a *shared*
   multi-tenant BFF, which would have had no single customer identity. But D-12 §6 records the shared
   tier as **never implemented** and the file as a **retired artifact that has not compiled since
   2026-08-17** (`bicep-e2e-dry-run.ps1` even asserts that it fails). There is no running shared BFF.
   ⚠️ While checking this, its `MULTI_TENANT_MODE: 'true'` setting turned out to be **premise rot**:
   the Bicep comment says it is *"consumed by BFF at boot for per-request tenant-context resolution"*
   and a search of `src/` finds **zero** consumers. Same shape as the `AsAIAgent()` comment in #1027
   and the agent-thread comment task 122 corrected.

---

## 10. What task 123 actually built (2026-09-29)

**Resolution order** — `Customer:Id` (app setting `Customer__Id`) → derive from
`WEBSITE_RESOURCE_GROUP` → fail. Both paths read the *same* customerId from different places; neither
invents one.

**Three fail-closed properties, at three different moments:**

| Moment | Mechanism | Behaviour |
|---|---|---|
| Startup, deployed env | `CustomerOptionsValidator` | refuses to start, message names both sources |
| Startup, Development / Testing | same validator, short-circuits | boots; identity stays **unresolved** |
| Point of use, anywhere | `CustomerIdentity.Id` | **throws** when unresolved — never returns a placeholder |

The third is the one that matters most and was not in the original §3c sketch. `CustomerOptions.Id`
reads as `""` when unset, and `""` used as a cache-key prefix is a key **shared by every customer** —
the same defect task 122 found in the Foundry agent-thread key. `CustomerIdentity` exists so that the
Development/Testing exemption cannot quietly become a shared default; consumers inject it, never the
options.

### 🔴 The finding that changed the design: derivation can invent a customer

`rg-spaarke-platform-{env}` matches the per-customer shape `rg-spaarke-{customerId}-{env}` **exactly**,
so naive derivation yields `customerId = "platform"` — a silently invented customer, arrived at through
the very mechanism meant to prevent one. It is a live group: it hosts the BFF (`platform.bicep:123`) and
the L2 control plane (`platform-controlplane.bicep:209`). `rg-spaarke-shared-{env}` and
`rg-spaarke-byok-prod` have the same shape.

Derivation therefore carries an explicit **deny-list** (`platform`, `shared`, `byok`), and a match there
is *unresolved*, not a customer. Pinned by `CustomerIdResolverTests`; verified to bite by removing
`platform` and watching exactly those two cases fail.

### ⚠️ §8's claim that option D "removes the breakage entirely" is not true of the current estate

It holds for stamps deployed from `customer.bicep` / `model2-full.bicep`. It does **not** hold for the
two stamps that exist today: `Deploy-BffApi.ps1` defaults to `rg-spaarke-dev` (not a per-customer shape
— only one segment after the prefix) and documents `rg-spaarke-platform-prod` (deny-listed).
`appsettings.Testing.json` records that App Service runs as the **Production** environment, so neither
is exempt from the startup failure.

⇒ **Both need `Customer__Id` set explicitly before this branch deploys.** That is a one-line
`az webapp config appsettings set`, documented in the deployment guide § 6.5.1 — but *which* customerId
a pre-D-12 platform stamp should carry is an **owner decision**, and task 123 deliberately did not
invent one.
