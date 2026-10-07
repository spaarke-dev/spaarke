# 📨 INCOMING — the BFF now requires a customer identity at startup. Your provisioning handler must emit it.

> ✅ **STATUS (2026-10-01, customer-provisioning-orchestration-r1 T238)**: §1.1 done — H4b writes `Customer__Id` =
> the run's customerId (verbatim) to BOTH slots; §1.2 done — H13 trap T7 (`CustomerIdentityT7Probe`) quarantines a
> run whose stamp lacks it, has it blank, or carries another value on either slot. §1.3 (the two pre-D-12 platform
> stamps) remains an **owner decision** — not touched.

> **From**: `unified-access-control-r2`, task 123 (2026-09-29)
> **Decision**: [D-14](../unified-access-control-r2/notes/D-14-customer-discriminator.md) — option (c) for the
> source, §8 option D for the strictness. Owner-raised.
> **Siblings**: read [`INCOMING-CUSTOMERID-STANDARD.md`](INCOMING-CUSTOMERID-STANDARD.md) first — it defines the
> *value*; this note is about *delivering* it. [`INCOMING-D12-D13-REMEDIATION.md`](INCOMING-D12-D13-REMEDIATION.md)
> is the larger, independent one.

---

## 0. TL;DR

The BFF can now answer "which customer am I serving?" It reads the app setting **`Customer__Id`**, falling
back to deriving the id from `WEBSITE_RESOURCE_GROUP`. **If neither resolves, it refuses to start.**

Both customer stacks already emit the setting — that part is done and is not yours. What is yours is the
**provisioning handler** that configures App Service settings outside those templates, and the **two existing
stamps** that will not start once this ships.

---

## 1. 🔴 What you need to do

### 1.1 Emit `Customer__Id` from the App Service configuration handler

Wherever your pipeline sets App Service application settings (the H9-era configuration step, not the Bicep),
add:

```
Customer__Id = <the customerId from the run's intake>
```

It is the same value the Bicep stack receives as its `customerId` parameter and the same one stored on
`sprk_dataverseenvironment.sprk_customerid`. **Do not re-derive or re-abbreviate it** — a second derivation is
how two components end up with two spellings.

### 1.2 Treat a missing `Customer__Id` as a provisioning failure, not a warning

A stamp handed over without it either refuses to start (deployed env) or runs with an unresolved identity
that throws the first time anything asks for the customer. Both are worse to discover after handover.

### 1.3 ⚠️ Two EXISTING stamps will not start — and the value is the owner's to choose

Verified 2026-09-29:

| Stamp | Resource group | Why it will not derive |
|---|---|---|
| dev BFF | `rg-spaarke-dev` (`Deploy-BffApi.ps1:92` default) | not a per-customer shape — only one segment after the `rg-spaarke-` prefix |
| prod BFF | `rg-spaarke-platform-prod` (`Deploy-BffApi.ps1:73,80`) | **deny-listed** — see §3 |

`appsettings.Testing.json` records that App Service runs as the **`Production`** environment, so neither is
covered by the Development/Testing exemption.

Fix is one command per stamp:

```bash
az webapp config appsettings set \
  --resource-group rg-spaarke-platform-prod \
  --name <app-service-name> \
  --settings Customer__Id=<customerId>
```

🔔 **What customerId a pre-D-12 platform stamp should carry is an OWNER decision.** Task 123 deliberately did
not invent one — inventing a value here is the exact failure the mechanism exists to prevent.

---

## 2. How resolution works, so you do not have to read the code

| Order | Source | Result |
|---|---|---|
| 1 | `Customer__Id` app setting (config key `Customer:Id`) | ✅ used; logged at **Information** |
| 2 | derived from `WEBSITE_RESOURCE_GROUP` (App Service sets it; it is literally `rg-spaarke-{customerId}-{env}`) | ✅ used; logged at **WARNING** — see below |
| 3 | neither | 🔴 startup failure in deployed envs; throws at point of use in Development/Testing |

**Why the derived path warns.** It is a supported fallback and is why existing per-customer stamps keep
working with no change — but a stamp running on it *forever* is a stamp whose settings were never finished.
D-14 §8 asks specifically that drift into derivation be visible rather than assumed. **If your pipeline is
working correctly, no stamp it produces should ever log that warning.** It is a usable signal that a
handler skipped the setting.

**There is no default and no sentinel.** An absent customer identity must never resolve to a shared value.

---

## 3. 🔴 Resource groups that match the customer shape but are NOT customers

`rg-spaarke-platform-{env}`, `rg-spaarke-shared-{env}` and `rg-spaarke-byok-prod` all match
`rg-spaarke-{customerId}-{env}` *structurally*. Without a guard, the platform stamp would derive
`customerId = "platform"` — a silently invented customer, produced by the very mechanism meant to prevent one.

Derivation therefore refuses those three segments by name. **If you add another platform-function resource
group in that shape, it must be added to the deny-list** in
`src/server/api/Sprk.Bff.Api/Configuration/CustomerIdResolver.cs`, or a BFF deployed there will silently
claim to be a customer named after the function.

Derivation also **asserts** the standard rather than trusting it: an RG whose customer segment is not a legal
customerId is unresolved, not parsed-and-hoped.

---

## 4. What is already done, so you do not redo it

- ✅ `Customer__Id: customerId` emitted by `infrastructure/bicep/customer.bicep` and
  `infrastructure/bicep/stacks/model2-full.bicep`; both compiled artifacts regenerated with `az bicep build`
- ✅ `CustomerOptions` / `CustomerOptionsValidator` / `CustomerIdentity` / `CustomerIdResolver` in the BFF,
  registered in `ConfigurationModule`
- ✅ Fail-closed startup in deployed envs; Development/Testing short-circuit so the 30+ test fixtures need no
  new keys (the `PublicConfigOptionsValidator` precedent, `.claude/constraints/bff-extensions.md` §F.2.1)
- ✅ Deny-list + standard assertion on the derived path, pinned by `CustomerIdResolverTests` (32 cases)
- ✅ Documented in [`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` § 6.5.1](../../docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md),
  [`auth-azure-resources.md`](../../docs/architecture/auth-azure-resources.md), and ADR-027 § 2

**Not done, and yours**: the provisioning handler that emits the setting, and the two existing stamps in §1.3.

---

## 5. What this does NOT change — please do not let it drift

`Customer__Id` is **defence in depth and an observability handle**, not the customer boundary. Per D-12 §3 and
the ADR-009 amendment, Redis access control is per-**instance**, not per-keyspace: a customer-id key prefix is
a convention our code enforces, not one Redis enforces.

Nothing here may be read as softening the dedicated-per-customer-resource decision. If a design discussion
starts treating a customer-id prefix as a substitute for a dedicated instance, that is the drift this
paragraph exists to catch.
