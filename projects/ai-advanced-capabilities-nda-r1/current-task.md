# Current Task — `ai-advanced-capabilities-nda-r1`

> **Last Updated**: 2026-07-26 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. Project is BUILT + MERGED + DEPLOYED + ENABLED on GPT-5 in spaarkedev1. Two follow-ups remain (below).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **State** | NDA advisory vertical LIVE in spaarkedev1 (code deployed, Dataverse wired, RAG grounded, GPT-5 provisioned) |
| **PR #689** | ✅ MERGED to master (merge commit `751532d7e`) — the 22-task NDA vertical |
| **PR #690** | OPEN — CI Git-LFS fix (`work/ci-lfs-fix-r1`). Check it greened the Compose seam tests + merge. |
| **Current branch** | `work/ci-lfs-fix-r1` (= master + the CI-LFS yaml change) |
| **Next Action** | (a) Smoke-test the live NDA flow in spaarkedev1; (b) do the TWO FOLLOW-UPS below; (c) check/merge PR #690 |

### The TWO FOLLOW-UPS to do when we continue
1. **BFF code fix — Reasoning-tier request shape (small PR)**
   - **Omit temperature generically for the Reasoning tier.** GPT-5 (and all o-series reasoning models) REJECT any non-default temperature (`"Only the default (1) value is supported"`). Today `ActionRunner.cs:130` passes `(float?)action.Temperature` through; I worked around it by CLEARING the Action's temperature in Dataverse (per-Action), but the RIGHT fix is: in `ActionRunner` (or `ModelTierDeploymentResolver`/the OpenAI client), when `EffectiveModelTier == Reasoning`, force `temperature = null` so ANY reasoning Action works without manual per-Action clearing.
   - **Verify/fix `max_completion_tokens` vs `max_tokens`.** GPT-5 requires `max_completion_tokens` (rejects `max_tokens`). Confirm the BFF's `IOpenAiClient.GetStructuredCompletionRawAsync` sends the reasoning-correct token param (the Azure SDK may handle it; a live nda-review run is the definitive test). If it sends `max_tokens`, that's a one-line client fix.
   - Files: `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/ActionRunner.cs` (temp omission), the `IOpenAiClient` impl (token param). Add a seam test. Ship as a small follow-up PR.
2. **`nda-standard-summary` (UC3) Action + binding** (deferred)
   - Same reuse pattern as nda-review: find/reuse an existing summary-style `sprk_analysisaction` (or create), set its prompt/schema/Fast tier from `infra/dataverse/actions/nda-standard-summary.action.json`, then create a `sprk_playbookconsumer` binding `consumerType=nda-standard-summary`, surfaces=`assistant`, disposition=informational, `sprk_Action@odata.bind` → that Action's GUID.
   - Lower priority (secondary UC3 "explain the firm standard" capability).

### Critical Context
The 22-task project is complete + merged + deployed. GPT-5 works; the only compat issue found (temperature=0.3) is fixed on the Action. Everything below "Live-env state" is operational config/data changes made THIS session that are NOT in git (they're env state, intentionally).

---

## Full State (Detailed)

### What shipped (PR #689, merged `751532d7e`)
22 tasks: ADR-039 amendment (advisory-tier + grounding-mode-independent, strengthened) · model-tier resolver + runtime picker · NDA-REVIEW Action (advisory, Reasoning) · standard-summary Action · bindings/card/classification · whole-doc fan-out · Compose UX (summary panel, advisory-comments event/receiver, right-gutter, Draft Alternative) · comment-export fix · Summary-Page DOCX writer · SPE-versioning test · eval harness + dispatch eval + tenant-pin test · OR-clause tenant-pin fix · de-embed standard (RAG = source of truth). All waves build+code-review+adr-check gated.

### Live-env state (spaarkedev1 / Azure — NOT in git; operational)
- **BFF** deployed → `spaarke-bff-dev` (rg-spaarke-dev), hash-verified, healthy. App setting `DocumentIntelligence__ReasoningModel=gpt-5-reasoning`.
- **Code page** deployed → `sprk_spaarkeai` web resource in spaarkedev1 (published).
- **Action** `sprk_analysisaction` id `34c9ecf2-cb10-f111-8342-7ced8d1dc988` — REUSED the old "ACT-002 / NDA Analysis" stub → now code=`nda-review`, name=`NDA Review`, modeltier=Reasoning(100000002), **temperature=null** (GPT-5 compat), our advisory prompt + `{overallRisk, flaggedSections[]}` schema.
- **Binding** `sprk_playbookconsumer` — new `NDA Review`, consumerType=`nda-review`, enabled, surfaces=`assistant,compose`, disposition=informational, `sprk_Action` lookup → the Action GUID above.
- **RAG** — KNW-011 (firm NDA standard) ingested into `spaarke-rag-references` (8 chunks, 3072-dim, tenantId="system"). OR-clause tenant fix deployed → retrievable under any tenant.
- **Azure OpenAI** — `gpt-5-reasoning` deployment on `spaarke-openai-dev` (rg `spe-infrastructure-westus2`, region eastus): model gpt-5 2025-08-07, GlobalStandard, cap 10, Succeeded. Smoke-tested online. (Note: `Deploy-SpaarkeAi.ps1`, `Deploy-BffApi.ps1`, `Add-ReferenceToIndex.ps1` all use `az` tokens — auth is present in the main session.)

### Key facts for next session
- Dataverse write mechanism here: `az` token + Web API (Dataverse MCP is NOT surfaced in headless sessions; ToolSearch finds none). Binding→Action link = `sprk_Action@odata.bind` lookup (resolved from actionCode), NOT code-matching.
- Routing cache (`ConsumerRoutingService`) has ~5-min TTL — new binding may take up to 5 min to appear in capability-discovery.
- Since the standard was de-embedded from the prompt, RAG grounding (KNW-011) is load-bearing: no grounding → the review correctly DECLINES (does not hallucinate).

### Backlog (from the build, non-blocking)
- `sprk_outputdeterminism` Dataverse column + BFF read-path (make ADR-039 "mode = data" real; today prompt-enforced).
- Pre-existing test reds to triage: `Services.Communication.*` (5), `three-pane-compose-coordination` e2e (AiSessionProvider), and the Compose LFS-corpus seam tests (fixed by PR #690's `lfs:true`).
- `docs/adr/INDEX.md` was stale (reconciled during the build); the broader ADR-016 mis-citation was corrected repo-wide.
