# Decommission record — `rg-spaarke-platform-prod`

> **Date**: 2026-08-31
> **Authorized by owner**: *"those 'prod' azure resources are deprecated/not used (from old prod
> environment that was never finalized/used); we should remove them."*
> **Subscription**: Spaarke Devlopment Environment (`484bc857-3802-427f-9ea5-ca47b43db0f0`)
> **Companion**: [`azure-resource-group-review-2026-08-30.md`](azure-resource-group-review-2026-08-30.md)
> — this deletion is **step one** of the restructure proposed there.

This is the pre-deletion evidence record, written while the resources were still queryable.

---

## 1. What was deleted (8 resources)

| Resource | Type | Region |
|---|---|---|
| `spaarke-bff-prod` | Web/sites | westus2 |
| `spaarke-bff-prod-plan` | Web/serverFarms | westus2 |
| `spaarke-openai-prod` | CognitiveServices (OpenAI) | westus3 |
| `spaarke-docintel-prod` | CognitiveServices (FormRecognizer) | westus2 |
| `sprk-platform-prod-kv` | KeyVault/vaults | westus2 |
| `sprk-platform-prod-insights` | Insights/components | westus2 |
| `sprk-platform-prod-logs` | OperationalInsights/workspaces | westus2 |
| `api.spaarke.com` | Web/certificates | westus2 |

All created **2026-03-13** — a single provisioning pass that was never finalized.

---

## 2. Evidence the stack was dark

| Check | Result |
|---|---|
| `spaarke-bff-prod` state | **Stopped** |
| `spaarke.com` DNS | **no `api` CNAME** — nothing routed to the app |
| `spaarke-openai-prod` — `TokenTransaction`, 30d | **zero non-zero days** |
| `spaarke-docintel-prod` — `SuccessfulCalls`, 30d | **zero non-zero days** |
| Service principal `spaarke-bff-api-prod` | **`accountEnabled: false`** — already disabled |
| Inbound references from other live apps | **none** (see §3) |

---

## 3. Orphaned-reference check

Checked every web app in the subscription for references to any of the 8 resource names:

| App | Resource group | References |
|---|---|---|
| `spaarke-bff-dev` | `rg-spaarke-dev` | none |
| `spaarke-provisioning-controlplane-dev` | `rg-spaarke-platform-**dev**` | none |
| `spaarke-provisioning-controlplane-worker-dev` | `rg-spaarke-platform-**dev**` | none |
| `spaarke-bff-prod` | `rg-spaarke-platform-prod` | `sprk-platform-prod-kv` — **self-reference, deleted together** |

> ⚠️ `rg-spaarke-platform-**dev**` and `rg-spaarke-platform-**prod**` differ by one word. Both were
> checked explicitly. The dev pair is **live and untouched**.

---

## 4. Key Vault — secret NAMES preserved (values never read)

`sprk-platform-prod-kv` held 24 secrets:

```
ai-docintel-endpoint            ai-docintel-key
ai-openai-endpoint              ai-openai-key
ai-search-endpoint              ai-search-key
AppInsights-ConnectionString    BFF-API-Audience
BFF-API-ClientId                BFF-API-ClientSecret
Communication-DefaultMailbox    communication-webhook-secret
Communication-WebhookUrl        Dataverse-ServiceUrl
Email-WebhookSecret             PromptFlow-Endpoint
PromptFlow-Key                  Redis-ConnectionString
ServiceBus-ConnectionString     SPE-CommunicationArchiveContainerId
SPE-ContainerTypeId             SPE-DefaultContainerId
Tenant-demo                     TenantId
```

### 4a. The `BFF-API-ClientSecret` question — resolved, not waived

Root `CLAUDE.md` §17 carries a binding rule: **never delete `Dataverse-ClientSecret` /
`BFF-API-ClientSecret`**. This vault contained a `BFF-API-ClientSecret`, so the rule was checked
rather than assumed inapplicable:

1. **It is not the live one.** The live dev vault (`spaarke-spekvcert`) has **no**
   `BFF-API-ClientSecret` and no `BFF-API-ClientId` at all — dev authenticates via
   `MANAGED-IDENTITY-CLIENT-ID` / `UAMI-ClientId`, consistent with the ADR-028 A4 / auth-v4
   secret-free direction. The two vaults are not copies of each other.
2. **Its app registration is already disabled.** The prod secret belonged to app registration
   `92ecc702-…` (`spaarke-bff-api-prod`), whose **service principal is `accountEnabled: false`**.
   The credential (`prod-secret-2026`, expiring 2027-03-13) cannot be used to sign in.
3. **The deletion is reversible for 90 days.** See §5.

The rule protects a live credential. This one is disabled, uncopied, and recoverable — so the
deletion does not do the harm the rule exists to prevent.

**Residual item**: app registration `spaarke-bff-api-prod` (`92ecc702-…`) still exists in Entra with
a credential valid until **2027-03-13**. It is disabled and therefore not exploitable, but it is
orphaned. Deleting the registration is an Entra operation, **not** part of this resource-group
deletion — tracked separately.

---

## 5. Key Vault recoverability — purge is impossible, by design

```
enableSoftDelete:          true
enablePurgeProtection:     true      <-- the vault CANNOT be force-purged
softDeleteRetentionInDays: 90
```

**This corrects the staged plan.** The handoff note listed "decide on Key Vault purge" as a step and
warned that soft-delete would block name reuse. With **purge protection enabled, purging is not
possible at all** — the vault remains recoverable for the full 90 days and the name
`sprk-platform-prod-kv` is reserved until ~2026-11-29. No decision to make; recover with
`az keyvault recover -n sprk-platform-prod-kv` if ever needed.

---

## 6. The quota trap — checked, and it did not apply

The staged plan flagged CognitiveServices accounts as *"expensive to recreate (quota re-approval)"*.
Measured, that concern did not hold:

- `spaarke-openai-prod` held 3 deployments — `gpt-5.1` (50), `gpt-4.1-mini` (120),
  `text-embedding-3-large` (200).
- **Subscription-level Azure OpenAI access is already approved**, evidenced by `spaarke-openai-dev`
  continuing to run. Recreation is not gated on a new approval.
- Regional quota was **not scarce**: `OpenAI.Standard.gpt-5.1` in westus3 was **50 used of 670**.
  Deleting *returns* 50 units to a pool that was already ~92% free.

Deleting freed capacity rather than forfeiting it.

---

## 7. Certificate

`api.spaarke.com` (Web/certificates) had **no active SSL bindings**. Re-issuable if the hostname is
ever brought back. It was deleted with the resource group.

---

## 8. What this leaves for the restructure

Per the companion review, the remaining inconsistencies are unchanged by this deletion and are the
next items:

- `spe-infrastructure-westus2` is a 28-resource catch-all holding **no SPE resources** — but it does
  hold the company-wide `spaarke.com` DNS zone, and `spaarke-openai-dev`, which is in **eastus**
  despite the RG's `westus2` name.
- The dev BFF's secrets live in `spaarke-spekvcert`, in the `SharePointEmbedded` RG, in **eastus** —
  one application spanning three RGs across two regions.
- Proposed naming rule: `rg-spaarke-{workload}-{env}`, no region in the name.
