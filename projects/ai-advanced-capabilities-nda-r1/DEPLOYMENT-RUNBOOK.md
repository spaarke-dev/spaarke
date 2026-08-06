# DEPLOYMENT RUNBOOK — `ai-advanced-capabilities-nda-r1`

> **Status**: Implementation complete on PR #689 (branch `work/ai-advanced-capabilities-nda-r1`). All 18 code tasks committed + gated. This runbook is the ordered set of **live-environment steps** the code was built against but that require credentials/infrastructure not available to the autonomous build. Run these to ship.
>
> **Author**: autonomous build session, 2026-07-26. Each step cites the task that produced it + its notes file.

---

## 0. Pre-flight
- Confirm PR #689 CI is green (unit + seam + eval-gate `Category=GoldenUtteranceEval`).
- Confirm the branch merges cleanly to master.
- Confirm publish size ≤60 MB compressed (see step 5).

## 1. 🔔 Tenant-pin fix (SECURITY-ADJACENT — owner decision required) — task 012 / 052
**Problem** (confirmed, pre-existing, repo-wide): golden references (KNW-001…011) are seeded `tenantId="system"`, but `ReferenceRetrievalService.BuildSearchOptions` filters `tenantId eq '{executionTenant}'` with no `system` fallback → **retrieval returns ZERO chunks under any real tenant**, so NDA-REVIEW runs ungrounded (and, per the ADR-039 scope-guard + advisory rules, would decline/empty rather than hallucinate — safe but non-functional).
**Recommended fix (Path C, minimal, idiom already in repo)**: in `src/server/api/Sprk.Bff.Api/Services/Ai/ReferenceRetrievalService.cs`, change the tenant filter to `tenantId eq '{tenant}' or tenantId eq 'system'` — the same "system sentinel" pattern already used by `EmbeddingCache`, `PlaybookService`, `TextExtractorService`, `RecordSearchService`.
**Why it needs sign-off**: it widens a multi-tenant isolation filter. Full analysis + alternatives (per-tenant re-seed; per-source seeding) in [`notes/tenant-pin-analysis.md`](notes/tenant-pin-analysis.md) §6.
**On approval**: apply the fix + land task **052** (the standing integration test asserting non-zero grounding under the execution tenant — already specced).

## 2. Provision the Reasoning (GPT-5) deployment — task 013
Runbook: [`notes/task-013-reasoning-provisioning.md`](notes/task-013-reasoning-provisioning.md).
```bash
# a. inventory current deployments + regional model availability (verify West US 2 has gpt-5)
az cognitiveservices account deployment list --name spaarke-openai-dev --resource-group spe-infrastructure-westus2
az cognitiveservices account list-models          --name spaarke-openai-dev --resource-group spe-infrastructure-westus2
# b. create the reasoning deployment (recommended: gpt-5, reasoning_effort=medium; fallback gpt-5-mini)
az cognitiveservices account deployment create --name spaarke-openai-dev --resource-group spe-infrastructure-westus2 \
  --deployment-name gpt-5-reasoning --model-name gpt-5 --model-version "2025-08-07" --model-format OpenAI \
  --sku-name GlobalStandard --sku-capacity 10
# c. set the BFF App Setting (the token #{AI_REASONING_MODEL}# resolves here)
az webapp config appsettings set --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 \
  --settings DocumentIntelligence__ReasoningModel=gpt-5-reasoning
```
**⚠️ token-leak guard (follow-on)**: verify the CI/CD substitution resolves `#{AI_REASONING_MODEL}#` to empty-when-unset. If the literal placeholder can leak, add a resolver guard in `ModelTierDeploymentResolver` treating an unresolved `#{...}#` value as unset → fall back to Standard (else it 404s at call time). See [`current-task.md`](current-task.md) backlog.

## 3. Ingest the NDA standard into RAG — task 012
Source authored at `projects/x-ai-spaarke-platform-enhancements-r1/notes/design/knowledge-sources/KNW-011-spaarke-nda-standard.md` (co-located with KNW-001…010).
```bash
pwsh scripts/ai-search/Add-ReferenceToIndex.ps1 -SourceDir "projects/x-ai-spaarke-platform-enhancements-r1/notes/design/knowledge-sources" -Pattern "KNW-011-*.md"
# verify: retrieval returns non-zero chunks incl. KNW-011 UNDER THE EXECUTION TENANT (depends on step 1).
```
Runbook + verification asserts: [`notes/tenant-pin-analysis.md`](notes/tenant-pin-analysis.md) §8.

## 4. Seed Dataverse Actions + Bindings — tasks 020/021/022
Deploy the Action mirrors + binding rows (via Dataverse MCP `create_record` / `Seed-PlaybookConsumers.ps1`; `Seed-JpsActions.ps1` retired 2026-07-07):
- Actions: `infra/dataverse/actions/nda-review.action.json`, `infra/dataverse/actions/nda-standard-summary.action.json` (+ their input/output schema mirrors). Set `sprk_modeltier`, `temperature` (read by `AnalysisActionService`).
- Bindings: `infra/dataverse/sprk_playbookconsumer-rows.json` → `nda-review/default`, `nda-standard-summary/default`.
- **Follow-on (makes ADR-039 real)**: add the `sprk_analysisaction.sprk_outputdeterminism` column (global choice `fact`|`advisory`) + BFF read-path, then map the Action's `outputDeterminism`. Today it's prompt-enforced (correct) but not yet catalog data. See task 020 `$comment-outputDeterminism`.

## 5. Deploy BFF + code page — task 060
```bash
# definitive compressed publish-size gate (§10, ≤60 MB HARD stop):
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish
# measure the COMPRESSED artifact (zip) — subagents measured ~51.29 MB compressed incl. PDBs (under ceiling).
# then: BFF deploy (bff-deploy), SpaarkeAi code page (code-page-deploy), AI Search index seed (step 3).
```
Verify no new HIGH CVE: `dotnet list package --vulnerable --include-transitive` (one pre-existing HIGH `System.Security.Cryptography.Xml`, unrelated).

## 6. Eval + UAT — tasks 050/061
- **Advisory-quality eval** (grades NDA-REVIEW against NFR-01): `tests/eval/README.md` — run each of the 9 cases through `POST /api/ai/analysis/execute` (`actionCode: nda-review`), then `python tests/eval/metrics/citation_accuracy.py <live-output.json> <case-file>`. Rubric: coverage ≥90%, citation-accuracy ≥95%, 0 hallucinated High/Critical on the non-NDA case, risk-band ≥83%.
- **UI UAT** (task 061, needs `--chrome` on a deployed org): the flow — upload NDA → "Review an NDA" card → Compose tab → review-summary panel + right-gutter advisory comments → Draft Alternative per clause → Summary-Page + comment-baked export → SPE save. Plus dark-mode + console-error checks on each new surface (011 picker, 022 card, 030 panel, 032 gutter).

## 7. Close-out (task 090, post-deployment)
- Run `/test-diet` (project-close gate, CLAUDE.md §7) — reconcile the seam/eval tests added against the ADR-038 build-vs-maintain classifier.
- Flip README status → Complete; check graduation criteria; append `notes/lessons-learned.md`.
- Merge PR #689 to master (`/merge-to-master`).

---

## Known follow-ons (backlog, non-blocking) — carried in `current-task.md`
1. `sprk_outputdeterminism` column + BFF read-path (make ADR-039 mode = data).
2. ReasoningModel `#{...}#` token-leak resolver guard.
3. DEF-11/DEF-13 AI-review-flag comments don't export to native w:comment (separate data source; task 040 scope note).
4. Worst-case 50-finding output vs ADR-040 128 KB inline ledger cap → blob/SPE offload.
5. 010 low code-review items (resolver doc, `Binding.cs:120` comment, Fast-tier symmetry test, startup config validation).
6. Pre-existing unrelated test failures to triage: `Services.Communication.*` (5), `three-pane-compose-coordination.e2e` `AiSessionProvider` (9).
