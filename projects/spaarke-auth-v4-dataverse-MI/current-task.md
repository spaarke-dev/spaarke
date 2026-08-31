# Current Task State — spaarke-auth-v4-dataverse-MI

> **Last Updated**: 2026-08-24 (task 090 close-out)
> **Recovery**: nothing to recover — **the project is closed.**

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **none — project COMPLETE** |
| **Status** | ✅ **completed 2026-08-24** |
| **Next Action** | **None for this project.** Everything below belongs to *other* owners. |

**26 of 26 active tasks complete** · 3 deferred (Power BI 040–042, owner decision DEF-001).
Merged to master: PR [#814](https://github.com/spaarke-dev/spaarke/pull/814) + [#816](https://github.com/spaarke-dev/spaarke/pull/816). Deployed and **UAT PASSED** on `spaarkedev1`.

### Critical context

`BFF-API-ClientSecret` is gone from app settings **and** Key Vault. Every BFF-identity confidential client —
**including OBO** — authenticates via a Managed-Identity federated credential.

It is verified as **in live use, not merely configured**: Dataverse `createdby` on the UAT rows is
`# mi-bff-api-dev`, and Entra shows MI sign-ins to Dataverse plus the `AAD Token Exchange Endpoint` assertion
exchange, while the app registration's secret-based sign-ins **stopped at the cutover**. Proof + re-run recipe:
[`notes/decisions/mi-proof-dataverse-side.md`](notes/decisions/mi-proof-dataverse-side.md).

⚠️ **Entra sign-in logs retain 30 days.** That note is the durable artifact — the query is not re-runnable after
roughly 2026-09-24.

---

## Rollback, if ever needed

Config-only, ~2 min. **There is no slot to swap back to** — task 032 deleted it, so a code rollback means a
redeploy with a real outage window. Prefer config.

1. `az keyvault secret recover --vault-name spaarke-spekvcert --name BFF-API-ClientSecret` (**until 2026-11-22**)
2. Restore the 4 app settings (fingerprint `b09a140a603e`)
3. Add `ClientSecret` back to `Graph__Credentials__Order`
4. Set `Graph__Credentials__RequireSecretFreeIdentity=false` — **deliberately**, so the deviation is recorded
   rather than hidden

Service Bus / AI Search rollback secrets are recoverable in the same vault **until 2026-11-23**
(fingerprints `3db62606e51e` / `f20f0def4444`).

---

## ⚠️ Do not "fix"

`ClientSecret` remains in the **code-side canonical default** in `AddCredentialSelection` **on purpose**. 033's
own carried-forward obligation said to remove it; doing so **broke every unconfigured environment** (no
`Graph:Credentials` section + no UAMI ⇒ validator fails fast) and was caught by `CredentialOrderingSeamTests`.
Reverted, with the reasoning recorded at the code. The secret-free guarantee is delivered by **configuration** on
deployed environments instead — which is also what keeps local development possible.

---

## Carried forward — updated 2026-08-25

> The original list had **10 items**. The operator challenged it — *"we generally do not defer work to other
> projects unless it cannot be handled in this project"* — and **6 were closed** rather than deferred.

### ✅ Closed since (6)

| # | Item | How |
|---|---|---|
| 1 | Local-dev OBO (criterion #9) | [`docs/guides/local-dev-obo-setup.md`](../../docs/guides/local-dev-obo-setup.md). **Residual**: create the option-D local-dev app registration — one Azure action, commands in the guide |
| 2 | Provisioning §5.1 (criterion #15) | Owner decided **Reading 1** — one shared multitenant app registration for Model 1 |
| 5 | `AZURE_CLIENT_ID` | Deleted; MI sign-ins verified `errorCode=0` afterwards. This was task 031's own unfinished job |
| 6 | `/api/containers` | **6** endpoints deleted (031 booked "two"), plus the orphaned `canmanagecontainers` policy and a skipped test for a deleted route |
| 8 | `OfficeEndpoints` identity precedence | All 9 handlers aligned to `Items[UserIdKey]`-first |
| 9a | Stale CORS origin | `agreeable-hill-…-preview` removed; verified it no longer receives ACAO |

### 📅 Dated — one reminder, not two issues

Both keyed to the Key Vault recovery window lapsing. **Until then, doing either converts a 2-minute config
rollback into a redeploy on a fail-closed surface** — which is why they wait.

| # | Item | Trigger |
|---|---|---|
| 3 | Rotate `Dataverse-Checkout-20251218` (partial value in git history since 2026-03-09) | **2026-11-22** |
| 4 | 051-E code half — drop the SAS branch + `PostConfigure` back-fill, updating `ServiceBusClientGuardTests` in the same commit. Includes `appsettings.template.json`'s stale `ServiceBus-ConnectionString` reference | **2026-11-23** |

### 🔧 Genuinely open, with a real recipient

| # | Item | Owner |
|---|---|---|
| 9b | **CORS config drift has no forcing function** — the only item that *has already bitten us* (it caused the UAT blocker). Nothing fails in CI when a deployed environment's allow-list omits a live SWA | platform / CI |
| 7 | `/healthz/catalog` — **self-shrinking**: 8 of 12 findings resolve as agreements / nda / compose / smart-todo merge their constants. Residue is 4 legacy description rows + the Unhealthy-vs-Degraded asymmetry (an ADR-039 question) | AI-catalog |
| — | `Deploy-AllIndexes.ps1 -CutoverBffSettings` writes `AzureAISearchApiKey` + `AiSearch__AdminKey` onto the BFF as KV refs to the **deleted** `AiSearch--AdminKey`, re-introducing the key config task 053 removed. Warned in `auth-deployment-setup.md` §4; **not yet gated in the script**. *(An earlier note here said the script "silently re-mints a key" — wrong: it `show`s, not `renew`s, and index management legitimately uses an admin key.)* | AI-search / provisioning |
| — | `Configure-ProductionAppSettings.ps1` + `Provision-Customer.ps1` still write `ServiceBus-ConnectionString` | provisioning (#779) |
| — | Scope `spec.md:236` / `design.md:57` to Model 2; §5.2 `design.md:1006` fix; §9.2 Model-2 FIC issuer question | provisioning (#779) |
| 10 | Word "no extractable content" — the authenticated write completed; this is content extraction | owner-routed to a focused project |

**The MI environment contract is no longer in this list** — it was promoted into
[`auth-deployment-setup.md`](../../docs/guides/auth-deployment-setup.md) **§5.1**, where someone provisioning
will actually find it.

## Where the reasoning lives

| Document | What it holds |
|---|---|
| [`notes/lessons-learned.md`](notes/lessons-learned.md) | The retrospective. §1 the root cause was a **sentence**; §2 **eight shapes** in which a status code establishes nothing here; §3 forcing functions + the ADR-038 gap (closed as Amendment A1); §4 systematic under-counting; §5 the obligation that would have broken everything |
| [`notes/decisions/mi-proof-dataverse-side.md`](notes/decisions/mi-proof-dataverse-side.md) | Three-layer live proof that MI is in use, + re-run recipe |
| [`notes/decisions/033-secret-removal.md`](notes/decisions/033-secret-removal.md) | The removal, the rung ladder, and the near-miss |
| [`notes/decisions/051-053-live-cutover.md`](notes/decisions/051-053-live-cutover.md) | Service Bus + AI Search cutovers |
| [`notes/uat-findings-2026-08-24.md`](notes/uat-findings-2026-08-24.md) | All six UAT findings — **none an auth-v4 regression** |
| [`notes/test-diet-report.md`](notes/test-diet-report.md) | 73 methods added, **0 scaffolding** |
| [`spec.md` § Success Criteria](spec.md#success-criteria) | The graduation walk, with evidence per criterion |

**The one method worth carrying forward**: *find the log line, the Graph status, or a byte-level artifact — and
if you cannot, **remove the fallback and let success be the proof.*** The second half proved stronger than the
first, and it is how every cutover here was established.
