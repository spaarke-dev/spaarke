# 📨 INCOMING — the `customerId` standard is now defined and enforced. Your intake values violate it.

> **From**: `unified-access-control-r2`, task 124 (2026-09-29)
> **Owner-approved**: yes — D-14 §6, *"the current dataverse values can be changed and we can update our
> process so that it aligns with what is required for KV; we don't have production systems so this is easy
> clean up."*
> **Sibling note**: read [`INCOMING-D12-D13-REMEDIATION.md`](INCOMING-D12-D13-REMEDIATION.md) first if you
> have not — that one is larger and this is independent of it.

---

## 0. TL;DR

`customerId` is now **3–8 characters, lowercase letters and digits, starting with a letter**. The Bicep
parameter enforces the length in both customer stacks. Your intake process and the values already in
`sprk_dataverseenvironment.sprk_customerid` do not comply, and one class of non-compliant value would have
**failed the deployment** rather than merely looked untidy.

---

## 1. Why the limit is 8, and why it is not a preference

`infrastructure/bicep/customer.bicep` composes its Key Vault name as:

```bicep
take(format('sprk-{0}-{1}-kv', customerId, environmentName), 24)
```

Azure Key Vault names are 3–24 characters and **may not begin or end with a hyphen**. The binding case is
`environmentName = 'staging'` — the longest allowed value:

| `customerId` length | Resulting name | Outcome |
|---|---|---|
| 8 | `sprk-xxxxxxxx-staging-kv` | ✅ complete, exactly 24 |
| 9 | `sprk-xxxxxxxxx-staging-k` | ⚠️ truncated — loses the `v` |
| **10** | `sprk-xxxxxxxxxx-staging-` | 🔴 **INVALID — trailing hyphen. Azure rejects the name; the deployment fails.** |

The parameter previously allowed 10. **`take()` concealed the problem**: the name was silently shortened
rather than the template refusing, so the failure would have surfaced as an opaque Azure error at deploy
time rather than as a validation failure at submit time.

## 2. Why lowercase letters and digits only

The storage-account name is `take(toLower(replace('sprk{customerId}{env}sa', '-', '')), 24)` — it **strips
hyphens**. So `acme-x` and `acmex` resolve to the **same storage account name**, and nothing detects the
collision.

🔴 **This rule cannot be enforced in Bicep.** ARM has no regex constraint on parameters — no `@pattern`
decorator, and the repo has no `bicepconfig.json` enabling the experimental assertions feature. **Only the
length is enforceable in the template.**

**That makes the character rule YOUR enforcement point.** It has to be validated where the value is
assigned — at intake — because there is no later layer that can catch it.

## 2a. 🔴 The three rules are NOT equally binding — read this before you build validation

#### Which rules are HARD, and where each can be enforced

🔴 **These three rules are not equally binding, and only one of them can be enforced by a Dataverse column.**
Treating them as one rule is how a late, opaque deploy-time failure gets built in.

| Rule | Hard? | Dataverse column | Bicep/ARM | Consequence if violated |
|---|---|---|---|---|
| **max 8** | ✅ Azure-derived | ✅ `MaxLength = 8` | ✅ `@maxLength(8)` | 🔴 deployment **FAILS** — Key Vault name ends in a hyphen |
| **lowercase letters + digits** | ✅ collision risk | ❌ no regex on text columns | ❌ no `@pattern` | 🔴 **SILENT** — `acme-x` and `acmex` share one storage account |
| **min 3** | ❌ **convention only** | ❌ Dataverse has no minimum | ✅ `@minLength(3)` | nothing breaks — every composed name is ≥10 chars even at length 1, because of the `sprk` / `rg-spaarke-` prefixes |

**Why this matters for where the value is created.** A Dataverse text column enforces a maximum length but
has **no minimum and no regex**. If `customerId` is first typed into Dataverse, the column can catch the one
rule that would break a deployment (set `MaxLength = 8`) and cannot catch the other two.

That is acceptable **only because of how the three rules fall**:

- the rule Dataverse CAN enforce is the one that would otherwise fail a deployment;
- the rule it cannot enforce and that MATTERS (character set) needs code-level validation at intake anyway,
  because ARM cannot enforce it either;
- the rule it cannot enforce and that it would be brittle to depend on (min 3) **has no technical
  consequence** — it is a readability convention. A 2-character id deploys perfectly well.

**So: set `MaxLength = 8` on the column, validate the character set in the intake code path, and treat
min-3 as advisory.** Do not let min-3 become a late failure: `@minLength(3)` stays in the template as a
backstop, but intake should catch it first, and if it is ever hit in practice the correct response is to
relax it rather than to reject the customer.

---

## 3. 🔴 What you need to change

### 3.1 Validate at intake

Reject a `customerId` that does not match `^[a-z][a-z0-9]{2,7}$` **before** the row is written and before
any deployment is enqueued. A value that reaches Bicep and violates the length is refused by ARM; a value
that violates the character set is silently mangled. Neither should be reachable.

### 3.2 Re-issue the non-compliant values already in the registry

`sprk_dataverseenvironment.sprk_customerid` currently carries values shaped like `trial-2026-08-18`
— **16 characters with hyphens** — visible in
`src/server/services/Sprk.Provisioning.ControlPlane.Tests/Registry/DataverseEnvironmentRegistryClientTests.cs:232`
and described only as "slug" in `provisioning-runs/_templates/intake.md`.

That value is illegal on both counts. The owner has ruled these can simply be changed — there are no
production systems.

⚠️ **Check the fixtures too.** The test values above are not just documentation: a fixture that encodes an
illegal id will keep the old shape alive after the process changes.

### 3.3 Decide and record the abbreviation

Names longer than 8 characters have to be abbreviated at onboarding — `northwind` → `nwind`. Make that a
decision taken **once**, at intake, recorded on the registry row. It must not be re-derived anywhere, or two
components will abbreviate differently.

### 3.4 Update the intake template

`provisioning-runs/_templates/intake.md` describes `customerId` only as "slug". Replace that with the actual
rule and a pointer to the standard.

---

## 4. Where the standard lives

**[`docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` § "The `customerId` standard"](../../docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md)**
— the rule, the derivation, and the binding resource. Cite it rather than restating the number, so a future
change to the Key Vault name form updates one place.

⚠️ That same doc had **two drifts corrected** in the same change, which may affect your assumptions:

- The per-customer resource-group pattern was written `rg-spaarke-prod-{customer}` — **env before customer**,
  the reverse of what the Bicep deploys (`rg-spaarke-{customerId}-{environment}`).
- The section closed by saying customer resources *"share the environment-level AI services and BFF API from
  `rg-spaarke-prod`"*. **D-12 retired that** — there is no shared tier; each customer's stamp includes its
  own BFF, Redis, OpenAI and Search.

---

## 5. What is already done, so you do not redo it

- ✅ `@maxLength` 10 → 8 in `infrastructure/bicep/customer.bicep` and `stacks/model2-full.bicep`
- ✅ Both compiled artifacts regenerated from source (`customer.json`, `stacks/model2-full.json`)
- ✅ The standard + derivation written into the naming-convention doc
- ✅ Both Bicep parameters document the character rule AND the fact that ARM cannot enforce it

**Not done, and yours**: intake validation, re-issuing the registry values, the abbreviation decision, and
the intake template.

---

## 6. If you think the limit is wrong

The limit follows from the Key Vault name form in `customer.bicep`. If 8 is too tight, the honest fix is to
change that form — `stacks/model2-full.bicep` already uses `sprk{customerId}{env}-kv` (no separators), which
fits 10. **That is a live Key Vault rename**, so it is an owner decision, not a parameter change. Escalate
per CLAUDE.md §6.5 rather than widening the parameter back.
