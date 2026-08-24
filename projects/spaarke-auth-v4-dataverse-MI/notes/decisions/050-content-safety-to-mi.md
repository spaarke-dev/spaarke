# Task 050 — Content Safety was already on MI. The perimeter it guards has never worked.

> 2026-08-21. FR-E1. Outcome: **credential objective already satisfied and now verified + made explicit
> in config. A separate, more serious defect was found and is ESCALATED, not fixed.**

---

## 1. The headline

The task premise was *"ContentSafetyAuthHandler prefers the API key when set, but a working Managed
Identity path already exists and is simply not selected. Clear the key and select bearer auth."*

Measured on the live dev environment, **there is no key to clear and bearer auth is already selected**:

| Fact | Evidence (2026-08-21) |
|---|---|
| No API key configured on dev | `az webapp config appsettings list` — the only `AiSafety*` settings are `Endpoint`, `ManagedIdentity__Enabled`, `PromptShield__ChatPipelineEnabled`. **`AiSafety__ContentSafety__ApiKey` is absent entirely** |
| MI explicitly enabled | `AiSafety__ContentSafety__ManagedIdentity__Enabled = true` |
| RBAC present | UAMI `9fd47efb-…` holds **`Cognitive Services User`** on `spaarke-openai-dev` — the exact account the endpoint resolves to |
| Key Vault secret never existed | `ContentSafety-ApiKey` is **not present** in `spaarke-spekvcert`, the vault the BFF's `KeyVaultUri` points at |
| No Content Safety resource exists | **Zero** `kind=ContentSafety` accounts across **all five** Spaarke subscriptions |

So the migration this task exists to perform had already happened, presumably during AIPU2. What had
**not** happened is anyone verifying that it worked. It doesn't — for a reason that has nothing to do
with credentials.

## 2. The real finding: the Prompt Shield perimeter has never completed a single scan

Over the **full 90-day App Insights retention window** on `spe-insights-dev-67e2xz`:

| Measure | Value |
|---|---|
| Prompt Shield scans | **122** |
| Completed | **0** |
| Failed (auth or otherwise) | **0** |
| Timed out → **failed OPEN** | **122** |

Every scan is cancelled at the 100ms `PromptShieldService` deadline and the request proceeds to the LLM
unscreened. `AiSafety__PromptShield__ChatPipelineEnabled = true` on dev, so this perimeter is *live and
believed to be protecting*.

The outbound dependency telemetry says the same thing from the other side:

```
target = spaarke-openai-dev.cognitiveservices.azure.com
name   = POST /contentsafety/text:shieldPrompt
n=43   success=False   resultCode=0   min 9ms   p50 92ms   p95 99ms   max 102ms
```

`resultCode=0` means **no HTTP response was ever received**, and the durations pile up against the 100ms
client deadline. Not one call in 90 days got an answer.

### Auth is not the cause, so restoring a key would not fix it

| Candidate cause | Measurement | Verdict |
|---|---|---|
| Token acquisition too slow | `DefaultAzureCredential.GetToken`: **68,024 successes, avg 7ms, p95 1ms**, 3 failures | **ELIMINATED** |
| Token acquisition failing, cache never warming | Two scans **3.6s apart** (2026-08-18 21:08:05.7 / 21:08:09.3) both timed out — but with a 7ms token and a singleton cache, a cold cache cannot explain the second | **ELIMINATED** |
| Missing RBAC | `Cognitive Services User` present at account scope, and it is the *correct* role (§4) | **ELIMINATED** |
| The `shieldPrompt` call itself does not answer within 100ms | 43 calls, all cancelled client-side, zero server responses | **The remaining explanation** |

This matters for the decision: the obvious remediation — "put the API key back" — would change a 7ms
auth step and leave a call that never answers. **It would restore a credential for zero benefit.**

## 3. Two methodological near-misses, recorded because both nearly produced a false finding

**(a) A broken measurement that looked like a real negative.** My first dependency query used
`az monitor app-insights query … -o table`, which in this CLI version **renders an empty result even when
rows exist**. I read the empty output as *"there are no calls to cognitiveservices at all"* and had
already drafted the conclusion *"the HTTP request is never made; the timeout is inside token
acquisition."* That was wrong, and it was wrong in the direction that would have sent the fix at the
credential path — this project's own subject matter, hence the most plausible-looking wrong answer.
Re-running the identical query with `--query "tables[0].rows" -o tsv` returned 43 rows. **The empty
result was a rendering artifact, not a measurement.**

**(b) A 401 that is identity-specific, not capability-specific.** A direct
`POST /contentsafety/text:shieldPrompt` with **my own** Entra token returned:

```
401 PermissionDenied — The principal `ralph.schroeder@spaarke.com` lacks the required data action
`Microsoft.CognitiveServices/accounts/ContentSafety/text:shieldprompt/action`
```

Read carelessly this says *"the AIServices account doesn't serve Content Safety"* or *"MI will fail too"*.
Both readings are wrong, and §4 explains why. This is the mirror image of task 052's near-miss: there, a
user-token **200** nearly became "E-2 disproven"; here, a user-token **401** nearly became "the endpoint
is wrong". **A user-token result is evidence about that user, not about the endpoint or the MI.**

## 4. The role distinction that makes the 401 informative rather than alarming

| Role | dataActions | Covers `text:shieldprompt/action`? |
|---|---|---|
| `Cognitive Services User` | `Microsoft.CognitiveServices/*` | **YES** — wildcard |
| `Cognitive Services OpenAI User` | 16 explicit `accounts/OpenAI/**` actions | **NO** |
| `Cognitive Services OpenAI Contributor` | OpenAI-scoped | **NO** |
| `Owner` (subscription) | control plane only, **no dataActions** | **NO** |

My principal holds `Owner` + `Cognitive Services OpenAI Contributor` — neither grants the Content Safety
data action, which is exactly and only why I got 401. The **UAMI holds `Cognitive Services User`**, whose
wildcard does cover it.

Two things follow, both useful:

1. The 401 **proves the API route exists** on this account. Azure evaluated authorization against a
   specific named data action; a route that did not exist would have returned 404. The multi-service
   `AIServices` account does serve Content Safety.
2. **The managed identity has strictly broader data-plane rights here than the human operator does.**
   Worth stating plainly because the intuition "if it fails for me it will fail for the app" is
   backwards on this resource.

## 5. What changed

| File | Change |
|---|---|
| `appsettings.template.json` | **Deleted** `"ApiKey": "@Microsoft.KeyVault(…secrets/ContentSafety-ApiKey)"`; set `ManagedIdentity.Enabled` `false → true`; comment rewritten to record the verified state + the fail-open measurement |
| `ContentSafetyAuthHandler.cs` | Doc comment: the operator prerequisite is now *satisfied and verified*, the role distinction from §4, and the ⚠️ block stating that correct auth is **not** sufficient — with the numbers |

**Why the endpoint's `ApiKey` line had to go rather than just being left unused.** It referenced a Key
Vault secret that does not exist in the vault the BFF actually uses. `ContentSafetyAuthHandler` selects
the key branch on a **non-empty** value, so a dead Key Vault reference is not inert the way an absent
setting is. Removing it, and making `ManagedIdentity.Enabled` explicitly `true` rather than relying on
the absent-key fallback, closes that path twice over.

This is task 055's pattern again in a second instance: **a credential wired into provisioning, pointing
at a Key Vault secret that was never created, for a resource that was never provisioned.** Two of these
in one workstream is no longer a coincidence — noted for 090.

### What was deliberately NOT changed

- **The 100ms deadline.** The task's own escalation trigger says *"The MI path cannot meet the 100ms
  deadline — STOP and report rather than degrading safety latency."* Raising it is a safety-latency
  decision with a live blast radius, and it is not a credential decision.
- **Fail-open semantics.** Converting the fail-open catch into fail-closed would block chat turns on a
  Content Safety outage. That is FAILURE-MODES **AP-7** (unbounded blast radius) and far outside a
  credential task.
- **`ContentSafetyAuthHandlerTests`.** Criterion 4 says "updated and green". They are green (57 passed),
  and they did **not** need updating: they already assert MI-when-flag-on, MI-when-key-absent,
  throw-and-emit-nothing when no credential source exists, and single-acquisition caching. The default
  they encode was already correct. Manufacturing a diff to satisfy the wording would have been noise.
- **A guard against literal unresolved `@Microsoft.KeyVault(...)` values.** Tempting, and adjacent to
  the line I removed — but I could not verify from here that App Service actually surfaces an
  unresolvable reference as the literal string, and the template change plus `Enabled: true` already
  removes the trigger. Building a guard on an unverified premise is the failure mode this project
  catalogs. **Recorded as a hazard, not acted on.**

## 6. 🔔 ESCALATION — the 100ms Prompt Shield budget (escalation trigger FIRED)

**Trigger**: *"The MI path cannot meet the 100ms deadline — STOP and report rather than degrading safety
latency."*

It fires, with one correction to its premise: **the deadline is missed regardless of auth mode.** MI is
not the cause; the trigger's implied remedy (fall back to the key) would not help.

- **Situation** — the Prompt Shield perimeter is enabled on the live dev chat pipeline and has failed
  open on 100% of 122 scans over 90 days. The `ai.safety.shield_evaluations` counter and
  `scripts/kql/ai-metering/shield-coverage.kql` were built for exactly this and record it faithfully.
  The observability was built; nobody read it. The template comment even said *"watch
  ai.safety.shield_evaluations … after the flip."*
- **Options**
  - **(A)** Raise the deadline to a value the API can actually meet, and re-measure. Costs pre-first-token
    latency on every chat turn. The true server-side latency is **unknown** — no call has ever completed,
    so it must be measured, not guessed.
  - **(B)** Keep 100ms and accept that Prompt Shield is decorative, but then set
    `ChatPipelineEnabled=false` so the system stops asserting a protection it does not provide.
  - **(C)** Move the scan off the synchronous pre-first-token path.
- **Recommendation** — **(A), preceded by a measurement.** The cheapest decisive test: temporarily grant a
  principal `Cognitive Services User` on `spaarke-openai-dev` and time one `shieldPrompt` call, or raise
  the deadline on dev and read the resulting latency histogram. Not done here: the first is a live RBAC
  grant on a shared resource and the second changes safety latency — both are owner-present actions.
- **Out of scope for this project.** This is a safety-perimeter latency defect, not a credential defect.
  It should be its own task with an owner present. **This project's FR-E1 objective — Content Safety off
  the API key and onto MI — is met.**

## 7. Verification

| Criterion | Status | Evidence |
|---|---|---|
| Content Safety authenticates via MI with no API key configured | ✅ **MET** | No `ApiKey` app setting on dev; `ManagedIdentity__Enabled=true`; `Cognitive Services User` verified on the endpoint's account; the dead template reference removed |
| The 100ms deadline is still met, **with a measurement recorded** | ❌ **NOT MET — measurement recorded** | 122 scans / 90 days, **0** completions; 43 dependency records, all `resultCode=0`, p95 99ms. **Not met before this task either** — no regression was introduced. §6 |
| Negative case: with MI unavailable, the handler fails in a way that does not silently disable safety checks | ⚠️ **PARTIAL — honestly** | The *handler* is correct: `SendAsync_Throws_WhenBearerModeHasNoCredentialSource` proves it throws and **no unauthenticated request escapes**. But the *pipeline* then fails open by design. It is not silent (WARN log + `failed_open_*` counter), yet §2 shows the safety check has been disabled in practice for 90 days and nobody noticed. Recording this as partial rather than green |
| `ContentSafetyAuthHandlerTests` updated and green | ✅ **GREEN** (not updated — see §5) | 57 passed / 0 failed |
| Build + suites | ✅ | `dotnet build` 0 errors · unit suite **10,596 / 0** (97 skipped) · ArchTests **49 / 49** |
| Publish size (CLAUDE.md §10) | ✅ | **44.99 MB** compressed incl. PDBs / 215 files — **delta 0.00 MB**, ceiling 60 MB. Doc + config only; no package added |
| Nothing changed in a live environment | ✅ | All Azure calls read-only, plus one `shieldPrompt` probe that was rejected 401 |

## 8. Side findings for the rest of Group F

- **The dev subscription is `484bc857-…` ("Spaarke Devlopment Environment", *sic*), NOT the CLI default**
  (`cd95fcec-…`, "Spaarke Model 1 Production"). Every `az` lookup in Group F must target it explicitly or
  it returns a confident, empty, wrong answer — `az cognitiveservices account list` returns `[]` rather
  than erroring.
- **`az monitor app-insights query … -o table` silently renders empty.** Use
  `--query "tables[0].rows" -o tsv`. §3(a).
- **Task 056 (Bing) / 053 (AI Search) / 054 (DocIntel)** — these are still live `@Microsoft.KeyVault`
  references on dev: `AiSearch__ReferencesApiKey`, `AiSearch__Endpoint`, `AiSearch__ReferencesEndpoint`,
  `AzureOpenAI__ApiKey`, `DocumentIntelligence__AiSearchKey`, `DocumentIntelligence__AiSearchEndpoint`,
  `ServiceBus__ConnectionString`, `Communication__WebhookSigningKey`,
  `Communication__WebhookClientState`, `ConnectionStrings__Redis`. Check each against the vault before
  assuming the secret exists — this task and 055 both found references to secrets that never did.
- **Doc Intelligence (054)**: dev is `spaarke-docintel-dev` in `spe-infrastructure-westus2`, endpoint
  `https://westus2.api.cognitive.microsoft.com/` (regional, **not** a per-resource subdomain — MI auth
  against a regional endpoint is worth checking early). It is live: 1,398 dependency calls in 30 days.
- **Relevant to E-2 / task 031**: `spaarke-openai-dev.openai.azure.com` shows **2,772 successful
  dependency calls in 30 days**, and the UAMI holds `Cognitive Services User` (`…/*`) on that account.
  Whether those succeeding calls are running under MI or under `AzureOpenAI__ApiKey` (still a live KV
  reference) is **not** established here — the key being present means the SDK selects it. Do not read
  this as E-2 being resolved; read it as one more reason the cheap test booked onto 031 is worth running.
