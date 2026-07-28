# Task 013 — Reasoning deployment provisioning: notes

> Status: **blocked-external** for the actual Azure provisioning + live App Setting; **in-repo config
> wiring closed out** (token added, resolver confirmed correct, no code changes to
> `ModelTierDeploymentResolver`/`ActionRunner` per task constraint — task 010's committed code is
> unmodified). Not committed (orchestrator commits per wave gates).

## 1. Config wiring confirmed (trace)

`DocumentIntelligenceOptions.ReasoningModel` (nullable string, `Configuration/DocumentIntelligenceOptions.cs:102`)
→ `ModelTierDeploymentResolver.Resolve(AiModelTier? tier, DocumentIntelligenceOptions options)`
(`Services/Ai/LinearConsumers/ModelTierDeploymentResolver.cs:40-53`):

```csharp
AiModelTier.Reasoning => !string.IsNullOrWhiteSpace(options.ReasoningModel)
    ? options.ReasoningModel!
    : options.StandardModel,
```

→ consumed inline in `ActionRunner.RunAsync` (`Services/Ai/LinearConsumers/ActionRunner.cs:117`):

```csharp
var deploymentName = ModelTierDeploymentResolver.Resolve(action.ModelTier, _modelOptions);
...
var rawJson = await _openAi.GetStructuredCompletionRawAsync(..., model: deploymentName, ...);
```

This is exactly the contract task 010 established. **No changes made to `ModelTierDeploymentResolver.cs`
or `ActionRunner.cs`** — confirmed correct as-is; the graceful null/empty→`StandardModel` fallback is the
intended safety net until a live Reasoning deployment exists, and remains correct after this task's config
changes (see §2).

## 2. In-repo changes made this task

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/appsettings.template.json` | `"ReasoningModel": null` → `"ReasoningModel": "#{AI_REASONING_MODEL}#"` (tokenized, distinct from `#{AI_SUMMARIZE_MODEL}#` used by Fast/Standard). Updated the adjacent `_ModelTier_comment` to describe the new token + fallback behavior. |
| `src/server/api/Sprk.Bff.Api/appsettings.tokens.md` | Added `#{AI_REASONING_MODEL}#` row to the Token Reference table; added `AI_REASONING_MODEL=` (empty) to the Development Values block; rewrote the existing `ReasoningModel` bullet to point at this runbook instead of "set directly in Azure App Settings" (now token-driven like the other two tiers, with the same CI/CD substitution mechanism). |
| `src/server/api/Sprk.Bff.Api/Configuration/DocumentIntelligenceOptions.cs` | Updated the `ReasoningModel` XML doc `<remarks>` to reference the token + this runbook + the recommended model (`gpt-5`). No logic change. |
| `docs/architecture/auth-azure-resources.md` | Added a "NOT yet deployed" note under the Azure OpenAI Model Deployments table recording the recommended Reasoning deployment + env-blocked status; added the (unset) `DocumentIntelligence__ReasoningModel` line to the App Service Settings block. |

### Why tokenize rather than "set directly in Azure App Settings" (task 010's original plan)

Task 010 deliberately left `ReasoningModel` as a literal `null` in the template with a comment saying to
set the App Setting directly once provisioned. This task's instructions ask to tokenize it consistent with
`FastModel`/`StandardModel`, so this environment's config-generation path (template → tokens.md →
CI/CD substitution → `appsettings.json`) stays uniform across all three tiers rather than having Reasoning
be the one field managed out-of-band. Azure App Settings still take precedence over `appsettings.json` at
runtime either way (standard ASP.NET Core config layering) — ops can still set
`DocumentIntelligence__ReasoningModel` directly as an emergency override without touching the token
pipeline; the token just gives CI/CD-driven environments (dev/test/prod bicep parameter files) a first-class
place to carry the value too.

**Safety behavior preserved**: `ModelTierDeploymentResolver` treats null/empty/**whitespace** as "unset."
The PowerShell substitution pattern documented in `appsettings.tokens.md` (`-replace '#{TOKEN}#',
$env:VAR`) resolves an unset `$env:AI_REASONING_MODEL` to an empty string, not a literal placeholder — so
an environment that never sets the CI/CD variable ends up with `"ReasoningModel": ""`, which the resolver
still treats as unset and falls back to `StandardModel`. This matches task 010's original "no hard failure"
guarantee. **Caveat**: if a different substitution mechanism (e.g. a GitHub Action that skips absent
secrets rather than substituting empty) is used for a given pipeline, verify it doesn't leave the literal
`#{AI_REASONING_MODEL}#` string in place — that WOULD reach `IsNullOrWhiteSpace` as non-empty and 404
against Azure OpenAI. Flagged here so ops verify the substitution behavior once CI/CD wiring for this token
is added.

## 3. Recommended reasoning-class model + rationale

**Recommendation: `gpt-5` at `reasoning_effort=medium`. Fallback: `gpt-5-mini` (same effort knob) if
per-call cost/latency needs to come down.**

Rationale (researched via the `researcher` subagent against current Microsoft Learn documentation, since
Azure OpenAI's model catalog moves faster than this assistant's training cutoff):

- The GPT-5 family (not the legacy o-series `o1`/`o3`/`o4-mini`, which are still deployable but superseded)
  is Microsoft's current reasoning tier. Microsoft's own model-choice guidance names **"legal or financial
  document analysis" explicitly as a GPT-5 use case** — a direct match for NDA-REVIEW (whole-document
  contract review, risk flagging, cited findings, draft-alternative language).
- **Structured Outputs / JSON schema mode is supported across the whole GPT-5 series**, so
  `IOpenAiClient.GetStructuredCompletionRawAsync` (which this project's Actions rely on for structured
  findings) works unchanged — no BFF code change needed beyond the deployment name itself.
  `reasoning_effort` (none/minimal/low/medium/high/xhigh) lets ops tune reasoning depth vs latency without
  a new deployment; `medium` is the recommended default for an **interactive** advisory review flow (vs
  `high`/`xhigh`, which would push time-to-first-token too far for a UI the user is actively waiting on).
  Reserve `high` for a later "deep review" mode if the north-star quality bar isn't met at `medium`.
- `gpt-5-mini` is the fallback if `gpt-5` proves too expensive/slow in practice — same effort-tuning
  behavior, lower absolute cost, still reasoning-class (vs `gpt-4o-mini`, which is not a reasoning model).
- Cost posture: NDA-REVIEW is a low-frequency, high-value call path (whole-document review, not the
  high-frequency Fast tier used for classification/validation), so the higher per-call cost of `gpt-5` is
  acceptable per the project's spec (NFR-01: advisory quality judged at least as useful/deep as a strong
  general LLM).

**Deployment mechanics**:
- SKU: `GlobalStandard` (or `DataZoneStandard` if data-residency requirements apply — not currently a
  Spaarke requirement per `auth-azure-resources.md`).
- Model version: `2025-08-07` for `gpt-5` (and `gpt-5-mini`) — verify against
  `az cognitiveservices account list-models` at provisioning time in case a newer dated version has since
  become the recommended default.
- **Region caveat (verify before provisioning)**: the researched region-availability tables show West US 3
  (this platform's **production** region per `infrastructure/bicep/parameters/platform-prod.bicepparam`)
  with full `gpt-5` support in both SKUs, but **West US 2 (the `spaarke-openai-dev` resource's region) was
  not listed as a column at all** in the same tables as of the research pass. `GlobalStandard` routes
  inference globally so it may still deploy successfully in a West US 2 resource, but this needs empirical
  confirmation — run `az cognitiveservices account list-models --name spaarke-openai-dev --resource-group
  spe-infrastructure-westus2` (see §4) before assuming dev-environment parity with prod.

## 4. Env-blocked steps (cannot run from this worktree — no Azure credentials/portal access)

### Step 1 — Confirm current deployment inventory (both envs)

```bash
# Dev
az cognitiveservices account deployment list \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2

# Also check model *availability* (not just current deployments) before committing to gpt-5 in West US 2:
az cognitiveservices account list-models \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2
```

### Step 2 — Provision the Reasoning deployment (once model availability is confirmed for the target region)

```bash
az cognitiveservices account deployment create \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2 \
  --deployment-name gpt-5-reasoning \
  --model-name gpt-5 \
  --model-version "2025-08-07" \
  --model-format OpenAI \
  --sku-name GlobalStandard \
  --sku-capacity 10
```

If `gpt-5` is unavailable in West US 2 (per the region caveat in §3), either:
(a) provision in the West US 3 (prod) resource instead and cross-region-call it via a distinct endpoint/key
    pair (would need a second `OpenAiEndpoint`/`OpenAiKey` pair — out of scope for this task's config
    surface, which assumes a single Azure OpenAI resource per `DocumentIntelligenceOptions`), or
(b) fall back to `gpt-5-mini` if it has broader regional coverage, or
(c) fall back further to `o3-mini`/`gpt-4o` as an interim reasoning-adjacent option and re-provision `gpt-5`
    once regional availability catches up.
Escalate to the Azure/infra owner per the POML's escalation trigger if (a) is needed — it implies a
multi-resource routing change beyond a config value.

Suggested deployment name: `gpt-5-reasoning` (parallel to the existing `gpt-4o-mini` bare-model-name
convention would suggest just `gpt-5`, but a suffix disambiguates the *purpose* from the *model* if ops
later deploy `gpt-5` for something else at a different effort/quota tier — ops discretion).

### Step 3 — Set the App Setting (and/or CI/CD token) on the BFF App Service

```bash
az webapp config appsettings set \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --settings DocumentIntelligence__ReasoningModel=gpt-5-reasoning
```

(App Service name from `.claude/constraints/azure-deployment.md` Environment Reference table — dev is
`spe-api-dev-67e2xz`.) For CI/CD-driven environments, set the `AI_REASONING_MODEL` pipeline
variable/secret to the same deployment name (`gpt-5-reasoning`) instead of/in addition to the direct App
Setting.

### Step 4 — Smoke test

Once deployed and configured, exercise a Reasoning-tier Action (e.g. via the NDA-REVIEW Action once task
020 lands, or any existing Action with `sprk_modeltier=Reasoning` set) and confirm the BFF log / Azure
OpenAI request shows `model=gpt-5-reasoning` (not `gpt-4o-mini`), and that the response is a valid
completion (not a 404).

```bash
# Example generic smoke call directly against the deployment (bypassing the BFF) to validate the
# deployment itself responds before wiring the App Setting:
curl -X POST "https://spaarke-openai-dev.openai.azure.com/openai/deployments/gpt-5-reasoning/chat/completions?api-version=2025-04-01-preview" \
  -H "api-key: <from Key Vault: ai-openai-key>" \
  -H "Content-Type: application/json" \
  -d '{"messages":[{"role":"user","content":"Reply with the single word OK."}],"reasoning_effort":"medium"}'
```

## 5. Acceptance-criteria self-check

| Criterion | Status |
|---|---|
| The task-010 resolver returns a live, reachable Reasoning deployment name from config. | **env-blocked** — resolver logic confirmed correct (§1); config now token-driven (§2) but the live deployment does not yet exist, so today the resolver still (correctly) falls back to `StandardModel`. Cannot be "live and reachable" without §4 Steps 1-3. |
| A smoke call on the Reasoning tier returns a valid completion. | **env-blocked** — needs §4 Step 4, which needs Steps 1-3 first. |
| No secret is committed; deployment reference is in config/Key Vault. | **met** — `ReasoningModel` is a deployment *name* (not a secret) carried via the template token mechanism, same as `FastModel`/`StandardModel`/`SummarizeModel`; the actual API key (`ai-openai-key`) remains in Key Vault, referenced via `@Microsoft.KeyVault(...)`, unchanged by this task. |

## 6. TASK-INDEX disposition

Recommend marking 013 **blocked-external** (not ✅) in `TASK-INDEX.md`, with owner = Azure/infra owner and
the §4 commands as the exact unblock path. Per the project's `current-task.md` Wave A note, 013 is
parallel-safe and non-blocking for 011/012's own completion; per spec (`design.md`), downstream advisory
tasks (e.g. 020 NDA-REVIEW Action) can develop and merge against the `StandardModel` fallback and pick up
the Reasoning deployment automatically once ops complete §4 — no code changes needed on the consumer side
when that happens, by design of the resolver's fallback (§1).
