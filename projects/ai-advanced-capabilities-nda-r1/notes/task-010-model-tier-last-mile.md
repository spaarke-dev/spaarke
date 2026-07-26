# Task 010 — Model-tier last-mile: notes

> Status: implementation complete. Build/tests/publish/CVE verified locally in the worktree. Not committed
> (per task instructions — orchestrator commits after wave gates). TASK-INDEX.md / current-task.md left
> untouched — 012 runs in parallel per current-task.md; those shared files are the orchestrator's to update
> at wave close.

## Placement Justification (CLAUDE.md §10 / bff-extensions.md)

This task extends existing BFF code — it does not add a new service, endpoint, or package. All new
surface lives inside the already-BFF-hosted `Services/Ai/LinearConsumers/` executor path:

- **Existing**: `ActionRunner.RunAsync` already calls `IOpenAiClient.GetStructuredCompletionRawAsync`
  with a hardcoded `model: null`. The `AiModelTier` vocabulary (`Services/Ai/PublicContracts/Binding.cs`)
  and `sprk_analysisaction.sprk_modeltier` Dataverse column already existed but were dead-ended (never
  read into `AnalysisAction`, never consulted by `ActionRunner`).
- **Extension**: mirrored the proven per-Action `Temperature` plumbing exactly — added `AnalysisAction.ModelTier`
  (sourced from `sprk_modeltier`, same 3 read call sites as `Temperature` in `AnalysisActionService`), added
  three deployment-name config fields to `DocumentIntelligenceOptions` (`FastModel`/`StandardModel`/`ReasoningModel`),
  and added a single pure static resolver (`ModelTierDeploymentResolver`) consumed inline in `ActionRunner`.
  No new DI registration, no new endpoint, no new package.
- **Cost of doing nothing**: an Action authored with `sprk_modeltier=Reasoning` (the NDA-REVIEW use case,
  project north star) would silently execute against `gpt-4o-mini` — wrong model, wrong quality, no
  observable failure (the exact dead-end this task closes).

Stays in BFF: decision criteria table in `bff-extensions.md` all resolve "BFF" (in-process latency/session
coupling, no new external surface). No Placement Justification deviation.

## ADR-016 (single routing surface)

`ModelTierDeploymentResolver` is the ONLY tier→deployment mapping added. Task 011 (runtime picker via
`sprk_playbookconsumer.sprk_modeltieroverride` / `Binding.EffectiveModelTier`) is expected to COMPOSE with
this same resolver (call `ModelTierDeploymentResolver.Resolve` with the Binding's effective tier instead of
the Action's raw tier) rather than introduce a second lookup.

## ADR-013 (facade boundary)

Not applicable as a violation risk: all new code lives inside `Services/Ai/` (the AI-internal layer itself,
not CRUD code reaching into AI internals). `ModelTierDeploymentResolver` references
`Sprk.Bff.Api.Services.Ai.PublicContracts.AiModelTier` (vocabulary enum, not an AI-internal service type)
and `Sprk.Bff.Api.Configuration.DocumentIntelligenceOptions` (already a dependency of `OpenAiClient`, the
existing AI-internal implementation). No new CRUD→AI dependency introduced.

## ADR-032 (Null-Object kill-switch) — deliberately NOT invoked for a new registration

`ModelTierDeploymentResolver` is a stateless `static` class, not a DI-registered service — mirrors the
`temperature` plumbing pattern, which is a plain inline computation, not a separate service. There is
nothing to gate: `ActionRunner` (the sole caller) is already registered conditionally behind the compound
AI kill switch (`Analysis:Enabled` AND `DocumentIntelligence:Enabled`) with its existing Null-Object peer,
`NullActionRunner` (`Services/Ai/LinearConsumers/NullActionPrimitives.cs`) — unchanged by this task. No new
asymmetric-registration risk is introduced.

## Config defaults — a deliberate zero-blast-radius choice

Per the 2026-03-04 Azure OpenAI model inventory
(`projects/ai-spaarke-platform-enhancments-r3/notes/azure-openai-model-inventory.md`), the only chat
deployment CONFIRMED provisioned in the dev resource (`spaarke-openai-dev`) is `gpt-4o-mini`; `gpt-4o` and
any o-series reasoning model were referenced in code but NOT deployed as of that inventory. Given no live
Azure access to re-verify current state (see env-blocked steps below), `FastModel` and `StandardModel` both
default to `gpt-4o-mini` (same as the pre-existing `SummarizeModel`), and `appsettings.template.json` reuses
the existing `#{AI_SUMMARIZE_MODEL}#` token for both — NO new CI/CD token wiring required, and NO
environment sees a behavior/cost change from this task alone. `ReasoningModel` defaults to `null`; the
resolver falls back to `StandardModel` when unset, so a Reasoning-tagged Action (e.g. the future NDA-REVIEW
Action from task 020) still executes rather than 404ing, until task 013 provisions the real o-series
deployment and ops set `DocumentIntelligence__ReasoningModel` explicitly.

**This is a documented interim-state deviation, not a silent one**: acceptance criterion "Action with
sprk_modeltier=Reasoning executes against the CONFIGURED Reasoning deployment" is satisfied and proven by
the seam tests (which configure a distinct `ReasoningModel` value and assert it reaches the LLM boundary).
The remaining gap — an actual live o-series deployment existing in Azure — is task 013's scope, called out
explicitly in root-cause form here so it isn't lost.

## Build / test / publish / CVE results

- `dotnet build src/server/api/Sprk.Bff.Api/` — **PASS** (0 errors; pre-existing warning count unchanged
  aside from expected new code, no new warnings introduced by task 010 edits).
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj` — full suite: **8941 passed, 5
  failed, 101 skipped** of 9047. The 5 failures are ALL in `Services.Communication.*`
  (`CommunicationThreadReadServiceTests` x3, `CommunicationByRegardingReadTests` x1,
  `CommunicationFilteredQueryTests` x1 — sender-identity projection tests) — a module with zero file
  overlap with this task's diff (`git diff --stat` touches only `Configuration/`,
  `Services/Ai/AnalysisActionService.cs`, `Services/Ai/IScopeResolverService.cs`,
  `Services/Ai/LinearConsumers/*`, `appsettings.template.json`, `appsettings.tokens.md`, and the seam test
  file). Pre-existing and unrelated to task 010. The new/updated
  seam tests targeting this task (`ContextBinderActionRunnerSeamTests` — 7 tests incl. the 2 new
  model-tier resolution tests) all PASS, as do all 3 sibling seam-test files that also construct
  `ActionRunner` directly (`DispositionRoutabilitySeamTests`, `DispositionRoutabilityNotificationSeamTests`,
  `DispatchPromptGroundingSeamTests` — 22 tests, confirming the new optional 4th constructor param is
  fully backward-compatible).
- `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` — **PASS**. Compressed
  size (Compress-Archive Optimal, matching the documented convention): **47.49 MB incl. PDBs** / **46.67 MB
  excl. PDBs**. Baseline (2026-07-08, task 055): 49.63 MB incl. PDBs / 45.87 MB excl. PDBs. Delta:
  **-2.14 MB incl. PDBs** (below the ceiling; well under the +5 MB single-task escalation threshold). Note:
  this branch has accumulated other work since the 2026-07-08 baseline (incl. task 001 of this project) —
  the measured delta reflects the branch's current state vs. the last recorded baseline, not task 010's
  edits in isolation (task 010's own diff is ~185 net new lines across 7 files, no new packages — negligible
  standalone byte impact).
- `dotnet list package --vulnerable --include-transitive` — one pre-existing HIGH-severity finding
  (`System.Security.Cryptography.Xml` 8.0.3, 5 advisories). **Not new** — task 010 added zero package
  references (`Sprk.Bff.Api.csproj` untouched, confirmed via `git diff --stat`).

## Env-blocked steps (cannot verify from this worktree)

1. **Live Reasoning deployment existence** — `az cognitiveservices account deployment list --name
   spaarke-openai-dev --resource-group spe-infrastructure-westus2` to confirm whether `gpt-4o` and/or an
   o-series model (e.g. `o3-mini`) are now provisioned (the 2026-03-04 inventory said no). If still
   missing, task 013 must run `az cognitiveservices account deployment create ...` (commands already
   documented in the inventory note) before `DocumentIntelligence__ReasoningModel` can be set to a value
   that will actually resolve at the Azure OpenAI endpoint.
2. **End-to-end live LLM call against the Reasoning deployment** — requires (1) above plus a deployed BFF
   with `DocumentIntelligence__ReasoningModel` set; out of reach from a local build/test pass by design
   (NFR-01 / ADR-013 latency-in-BFF boundary, not something to fake).
