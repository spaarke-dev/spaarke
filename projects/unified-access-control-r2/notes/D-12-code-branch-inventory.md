# D-12 remediation — the code-side tenancy branch inventory

> **Verified 2026-09-28** by direct read of the source tree, not recalled.
> **Purpose**: the precise list that the shared `TenancyModel` enum (D-12 §6 item 2) must absorb.
> **Scope**: production code only. Test fixtures (~25 sites, mechanical) are excluded and tracked separately.

⚠️ **This corrects and extends D-12 §6 / the synopsis §5 table**, which recorded *"7 branch sites + 3 silent
defaults"*. The real shape is **9 branch sites and 4 silent defaults** (recorded: 7 and 3), in four comparison styles — **two** of which are case-sensitive while the other seven are not.

---

## 1. Branch sites — production (**9**, was recorded as 7)

| # | File:line | Compares | Style | Selects |
|---|---|---|---|---|
| B1 | `…/BicepInfraDeploy/ArmDeploymentRunner.cs:232` | `"Model1Shared"` | `string.Equals(…, OrdinalIgnoreCase)` | Bicep template **blob key** |
| B2 | `…/BicepInfraDeploy/FileBicepTemplateInspector.cs:110` | `"Model1Shared"` | `string.Equals(…, OrdinalIgnoreCase)` | Bicep template **file path** |
| B3 | `…/AiSearchIndex/H2bAiSearchIndexHandler.cs:295` | `"Model1Shared"` | `string.Equals(…, OrdinalIgnoreCase)` | shared-index vs per-customer index path |
| B4 | `…/SubscriptionReadiness/H1SubscriptionReadinessHandler.cs:401` | set `{CustomerOwned, Model2Dedicated}` | `HashSet<string>` + `StringComparer.OrdinalIgnoreCase` | → **CustomerOwned** ⇒ **Lighthouse required** |
| B5 | `…/SubscriptionReadiness/H1SubscriptionReadinessHandler.cs:406` | set `{SpaarkeOwned, Model1Shared}` | `HashSet<string>` + `StringComparer.OrdinalIgnoreCase` | → SpaarkeOwned ⇒ no Lighthouse |
| B6 | `…/E2EAcceptance/AiSearchTenantFilterInvariantProbe.cs:250` | `Model1TenancyModel` const | `string.Equals(…, OrdinalIgnoreCase)` | I2 probe: expect template artifact |
| B7 | `…/E2EAcceptance/AiSearchTenantFilterInvariantProbe.cs:301` | `Model2TenancyModel` const | `string.Equals(…, OrdinalIgnoreCase)` | I2 probe: expect NO template artifact |
| B8 | `…/E2EAcceptance/ArmCostEnvelopeChecker.cs:264-268` | both literals | **`switch` on raw string — CASE-SENSITIVE** | which monthly **cost envelope** is expected |
| **B9** | `…/EntraAppReg/H3EntraAppRegHandler.cs:272` | `Model1Shared` const (`:129`) | `string.Equals(…, **StringComparison.Ordinal**)` — **case-SENSITIVE** | 🔴 **whether a per-customer BFF app registration is created at all** |

🔴 **B9 was missing from this inventory until 2026-09-28 and is the most consequential site of the nine.**
`HandleModel1Async` (`:406-460`) is documented as *"MODEL 1 (shared multitenant BFF app-reg). **Creates ZERO
new app-reg objects and ZERO new FIC objects** (acceptance criterion)"* — it verifies the shared app
registration and returns `BffAppRegId = _options.SharedBffAppRegistrationId`.

**This is live code implementing exactly what D-13 forbids.** D-13 is therefore **not a documentation
change**: closing it requires deleting the Model 1 branch, the `SharedBffAppRegistrationId` /
`SharedPlatformKeyVaultName` options, `EntraAppRegSharedVerifyRequest` and the shared-path tests, so that
H3 provisions a per-customer app registration unconditionally. Note
`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md:335` **already** documents H3's output as the per-customer
`appreg-{customerId}-{tenantId}` — the guide and the code already disagree, independently of D-13.

⚠️ **Correction 2026-09-28: the case-sensitivity finding was understated.** B8 was described as the only case-sensitive comparison; **B9 is also ordinal/case-sensitive** (`StringComparison.Ordinal` at `:272` and `:516`). So two of nine sites reject a wrong-cased value that the other seven accept.

✅ **And there are TWO fail-loud sites, not one.** B9, like H1 (B4/B5), **rejects** an unrecognized `tenancyModel` with no default (`:220-224`, citing invariant I6 — *"requires an explicit value with NO default"*). That strengthens §4's conclusion: parse-or-reject is already the pattern in the two handlers that gate identity and subscription, and the enum should generalise it rather than invent it.

**B6/B7 are the only sites that name the literal through a *file-private* constant** (`Model1TenancyModel` /
`Model2TenancyModel`, `AiSearchTenantFilterInvariantProbe.cs:142,145`). Those two constants are file-private
— they are a local tidy, not a shared vocabulary, and the other six sites do not see them.

### 🔴 B8 is the odd one out and it fails differently

```csharp
internal decimal SelectExpectedEnvelope(string tenancyModel) => tenancyModel switch
{
    "Model2Dedicated" => _options.Model2EmptyEnvelopeUsd,
    "Model1Shared"    => _options.Model1MarginalEnvelopeUsd,
    _                 => _options.Model1SharedFloorEnvelopeUsd,
};
```

A C# `switch` on a string is **ordinal and case-SENSITIVE**. Every other site normalizes with
`OrdinalIgnoreCase`. So `"model2dedicated"` — which B1–B7 all accept — falls through B8's `_` arm and is
costed against **`Model1SharedFloorEnvelopeUsd`**, the envelope of the tier D-12 retires. The failure is a
wrong number in an advisory report, not a crash, which is why it has survived.

---

## 2. Silent defaults — **four**, not three

Each coerces an absent or unknown value to a *specific* model without saying so.

| # | File:line | Coerces | To | Consequence if wrong |
|---|---|---|---|---|
| D1 | `H2aBicepInfraDeployHandler.cs:284` | blank/whitespace | `"Model2Dedicated"` | deploys the **wrong Bicep stack** |
| D2 | `H2bAiSearchIndexHandler.cs:289` | blank/whitespace | `"Model2Dedicated"` | provisions the **wrong AI Search index shape** |
| D3 | `AiSearchTenantFilterInvariantProbe.cs:240-242` | blank/whitespace | `Model2TenancyModel` | the **I2 isolation probe** asserts against the wrong expectation |
| D4 | `ArmCostEnvelopeChecker.cs:268` (`_` arm) | **anything unrecognized, incl. blank AND wrong-case** | `Model1SharedFloorEnvelopeUsd` | wrong **cost envelope**; the widest net of the four |

**D3 was not in the recorded inventory.** Its comment says it exists to *"Match H2b's own default fallback"*
— so it is a deliberate mirror of D2. That is exactly the coupling the enum has to absorb: today, changing
D2 without changing D3 silently desynchronizes a handler from the probe that is supposed to check it.

**D1–D3 coerce to Model 2; D4 coerces to the retired shared tier.** They do not agree with each other.

---

## 3. The one site that does NOT silently default — and is the live defect

`H1SubscriptionReadinessHandler` deliberately **rejects** an unknown value
(`SubscriptionReadinessRejectionCodes.InvalidTenancyModel`) rather than guessing. That is the correct
behaviour and should be the enum's model.

But its mapping is the **P-2 defect**, now confirmed by direct read:

```csharp
CustomerOwnedTenancyValues = { "CustomerOwned", "Model2Dedicated" }   // ⇒ Lighthouse REQUIRED
SpaarkeOwnedTenancyValues  = { "SpaarkeOwned",  "Model1Shared"   }   // ⇒ no Lighthouse
```

`Model2Dedicated` covered **both** old-2a (Spaarke's subscription) and old-2b (the customer's). H1 maps the
single literal to `CustomerOwned` unconditionally, so **it demands Lighthouse delegation for subscriptions
Spaarke already owns.**

✅ **Useful finding for the migration**: the *ownership* vocabulary (`SpaarkeOwned` / `CustomerOwned`) is
already present and already correct, and D-12's new model axis maps onto it exactly — Model 1 → SpaarkeOwned,
Model 2 → CustomerOwned. The repair is to retire the two *model* literals from these sets, not to redesign
the classifier.

---

## 4. What this means for sequencing

1. **The enum must carry the rejection behaviour, not the defaulting behaviour.** Four of the five
   defaulting sites disagree with each other; only H1 fails loudly. Parse-or-reject at the edge, then pass
   a typed value inward, and D1–D4 all disappear rather than being individually corrected.
2. **B8's case-sensitivity is a latent bug today**, independent of D-12. Worth fixing in the same change
   because the enum removes the string comparison entirely.
3. **`Model1SharedFloorEnvelopeUsd` becomes meaningless** when the shared tier retires — it is the default
   arm of B8, so the option cannot simply be deleted without choosing a new default. Under D-12 the honest
   default is *no default*: an unrecognized value is a rejection, matching H1.
4. **Do not relabel `sprk_tenancymodel` value `1`.** It holds both old-2a and old-2b, which now split
   across *different* models — see D-12 §6 item 3. This inventory does not change that conclusion; B4/B5
   are why it matters, because the two halves need **opposite** Lighthouse answers.

---

## 5. Not counted here

- **~25 test-fixture sites** setting `TenancyModel = "…"` as inert data (`RunsEndpointsTests`,
  `CurrentRunGuardTests`, `DispatchCoreDecisionTests`, and others). Mechanical; they follow the enum.
- **`AiSearchTenantFilterInvariantProbeTests`** is the exception — it pins the *defaulting* behaviour
  explicitly (`ProbeAsync_Model2_UnspecifiedTenancyModel_DefaultsToModel2_ReturnsPassed`). That test
  encodes D3 as a contract and **must be rewritten, not mechanically updated**, when the default is removed.
- **Doc/comment mentions** in `Program.cs:201-202,279-280`, `IBicepDeployRunner.cs:69-72`,
  `ProvisioningRun.cs:68`, `SubscriptionReadinessRejectionCodes.cs:57-59`, `ArmDeploymentRunner.cs:40`,
  `H2aBicepInfraDeployHandler.cs:29`, `H1…Handler.cs:73-78,245`, `AiSearchTenantFilterInvariantProbe.cs:72,76`.
  These describe the branch behaviour and go stale the moment the enum lands — they are the AP-12 surface of
  this change.
