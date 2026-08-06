# Task 060 — Deploy Report (BFF + SpaarkeAi code page + Dataverse data)

> Rigor: FULL · Model tier: sonnet @ high · Step mode: prescriptive · Status: **complete**
> Target env: `spaarkedev1` (Dataverse) / `spaarke-bff-dev` (App Service, `rg-spaarke-dev`) / `sprk_spaarkeai` (web resource)
> Deployed from HEAD `057a41972e7df19f241c609ca7afc79c66278b79` — **zero code changes made by this task** (deploy-only).

## Summary

All three surfaces are live: the missing `agreement-classify` Action + Binding row are now deployed to Dataverse
(previously mirror-only), the BFF is redeployed from current HEAD and hash-verified, and the SpaarkeAi code page is
rebuilt (with all 3 touched shared libs recompiled first) and redeployed with content verified byte-identical to the
local build. Publish size is 48.25 MB (well under the 60 MB ceiling, a decrease vs the 49.63 MB baseline). CVE scan
shows the same 5 pre-existing HIGH advisories on `System.Security.Cryptography.Xml` 8.0.3 already documented by
sibling tasks 050/051 — confirmed not introduced here (zero `.csproj` changes). One known environment limitation
(no Reasoning-tier Azure OpenAI deployment) blocks live LLM grading, as flagged by every upstream task; endpoint-surface
smoke (routing/auth-shape) is the gate for this task, and it is green.

## Step 0 — Pre-checks (cited from orchestrator, not redone)

- `/conflict-check`: informational only — many worktrees touch BFF per the standing INDEX; nothing blocks a dev
  deploy. Soft note: `r5-cycle-6-to-9-closeout` has 1 unmerged ConversationPane commit — a merge-time concern, not a
  deploy-time blocker.
- **PR #690** (LFS Compose seam fixtures) — re-verified this session: **still OPEN**, not merged
  (`gh pr view 690`). CI seam/eval tests will fail on our PR until it merges. Recorded here for the PR description.
- Dependabot #266 (OpenXml bump) — no PR activity found touching `Sprk.Bff.Api.csproj`; not merged.
- Auth verified live before starting: `az account show` → `ralph.schroeder@spaarke.com`; `pac auth list` → active
  profile `[3] SPAARKE DEV 1` (`spaarkedev1.crm.dynamics.com`). No auth blocker.
- **Pre-deploy baseline / rollback anchor**:
  - `Capture-BffBaseline.ps1 -Samples 5` ran successfully (323 routes, 1615 probes, 177s) against the **pre-deploy**
    BFF → `projects/ai-advanced-capabilities-agreements-r1/notes/060-pre-deploy-baseline.json`. Status distribution:
    200×40, 400×25, 401×1055, 404×490, 429×5; average P95 latency 125 ms.
  - Pre-deploy SHA-256 snapshot of the 5 Kudu-critical files (the state immediately before this task's deploy —
    already reflects other worktrees' recent deploys, since this is a shared dev App Service; last deployment before
    ours was at `2026-07-31T16:09:07Z` per Kudu deployment history):
    ```
    Sprk.Bff.Api.dll        4226c13d20aeb4a9be3237f5302ac7290fa904f3eec810a75872047a6352ef34
    Sprk.Bff.Api.deps.json  6d9d4ce11426f439b16aeccd3fa6994f2efc3a3df018fb8228762b8b0f5cc00e
    Spaarke.Core.dll        5d3224b0e402bf67d513ef9dfa8874a82ca23d1a51c2f2b03299a3f9bf50f4a0
    Spaarke.Dataverse.dll   5c5dee965a91e2404c0516f90daa5a3912f9dffa039703f91b03a7c7c6b023b2
    web.config              bb4ff8c63631e3a1fca527cd6f60a2071c121a088be0f449d3906bea70999691
    ```
    App Service was `Running`/healthy before our deploy began.

## Step 1 — Dataverse data deploy

### agreement-classify Action (NEW — was mirror-only)

Created via `mcp__dataverse__create_record` on `sprk_analysisaction` (schema introspected first via
`mcp__dataverse__describe` — no dispatch-identity `actionType` column exists post-Wave-4 drop; `sprk_kind` is the
live analog and defaults to Prompted when unset). Field values sourced verbatim from
`infra/dataverse/actions/agreement-classify.action.json` (`systemPrompt`, `outputSchema`) and
`infra/dataverse/inputschemas/agreement-classify.input.schema.json` (`inputSchema`), serialized to compact JSON
matching the live shape of the sibling `agreement-review` row (spot-verified by reading that row back first).

| Field | Value |
|---|---|
| `sprk_analysisactionid` (new GUID) | `53406e5b-5b8d-f111-8076-70a8a58a7766` |
| `sprk_actioncode` | `agreement-classify` |
| `sprk_name` | `Agreement Classify` |
| `sprk_modeltier` | `100000002` (Reasoning) |
| `sprk_temperature` | `0.1` |
| `sprk_allowsknowledge` | `false` (classifier is self-contained over doc + injected registry cues — no RAG) |
| `sprk_kind` | `100000000` (Prompted) |
| `sprk_systemprompt` | 6,538 chars — verbatim from the action mirror |
| `sprk_outputschemajson` | 3,519 chars — verbatim `outputSchema` object from the action mirror |
| `sprk_inputschema` | 1,013 chars — verbatim `inputSchema` object from the input-schema mirror |

**Verification**: read the row back post-create — all fields round-tripped exactly (em-dashes and all), `sprk_kindname`
resolved to `Prompted`, `sprk_modeltiername` to `Reasoning`, `sprk_allowsknowledgename` to `No`. Also ran
`scripts/Verify-OutputSchemaField.ps1` (the general `sprk_outputschemajson` column-shape check requested for
`agreement-review` re-verification) — **PASSED** (Memo, MaxLength 1,048,576, RequiredLevel None, IsCustomAttribute
true, column queryable).

### agreement-classify Binding (NEW — task 021's mirror row, was mirror-only)

Created via `mcp__dataverse__create_record` on `sprk_playbookconsumer`, values taken verbatim from the mirror row at
`infra/dataverse/sprk_playbookconsumer-rows.json:6-25` (already authored/committed by task 021 — no mirror edits made
by this task).

| Field | Value |
|---|---|
| `sprk_playbookconsumerid` (new GUID) | `ed92d769-5b8d-f111-8076-70a8a58a7766` |
| `sprk_consumertype` / `sprk_consumercode` / `sprk_environment` | `agreement-classify` / `default` / `*` |
| `sprk_name` | `Agreement Classify (Reasoning tier)` |
| `sprk_priority` / `sprk_enabled` | `500` / `true` |
| `sprk_action` (lookup) | → `53406e5b-5b8d-f111-8076-70a8a58a7766` (resolves to "Agreement Classify") |
| `sprk_disposition` / `sprk_risk` / `sprk_capturemode` | `100000000` Informational / `100000000` None / `100000000` LoopElicitation |
| `sprk_surfaces` | `assistant` |
| `sprk_tooldescription` | non-empty (capability-discovery projection requirement) — verbatim from mirror |

**Verification**: `mcp__dataverse__describe` on the created record confirms the `sprk_action` lookup resolves to
`sprk_analysisaction` / "Agreement Classify" with the correct GUID.

### Seed-PlaybookConsumers.ps1 `-DiffOnly` (round-trip proof, read-only)

```
scripts\dataverse\Seed-PlaybookConsumers.ps1 -DiffOnly
```

Result: **15 differences, none touching `agreement-classify`** — our new row is clean (env == mirror). All 15 are
**pre-existing drift** from other projects' live edits (matches the orchestrator's "~16 unrelated fields" warning;
count is 15 at this exact moment — consistent within noise of ongoing shared-env activity):

- `MISSING IN ENV`: `email-create-task`, `email-propose`, `email-triage` (another project's mirror rows, not yet seeded)
- `MISSING IN MIRROR`: `create-project`, `create-todo` (live rows from another project, mirror not yet exported)
- `DRIFT` (env ahead of mirror, pre-existing per task 030's notes): `chat-classify`/`chat-summarize` chipTransitions,
  `create-matter`/`create-task` disposition+chipTransitions+toolDescription, `draft-correspondence` chipTransitions,
  `nda-review` toolDescription

**Not touched** — these are out of this project's scope per the binding clobber-hazard rule (§ CLOBBER HAZARD in the
task brief): no full `Seed` was run; only the two surgical `create_record` calls above.

### Registry verification

```sql
SELECT sprk_agreementtypeid, sprk_key, sprk_name, sprk_isfallback FROM sprk_agreementtype ORDER BY sprk_name
```
Returns exactly **10 rows**, exactly **1 fallback** (`general` / "General", `sprk_isfallback=true`). Matches spec.

## Step 2 — BFF deploy

```
scripts\Deploy-BffApi.ps1
```

| Metric | Result |
|---|---|
| Build | Success (Release, from HEAD `057a41972`) |
| Package size | **48.25 MB** |
| Pre-deploy hash capture | 4 critical files captured |
| Deploy | Success (direct deploy, no slot) |
| Hash-verify | **4/4 files matched local build (SHA-256)** — genuine replacement, no silent file-lock failure |
| Health check | **Passed** (`/healthz` → 200) |

### Post-deploy hash cross-check (this task, independent of the script's own check)

Fetched the 5 Kudu-critical files from the live app *after* deploy and compared against a fresh, independent
`dotnet publish -c Release -o deploy/api-publish/` from the same HEAD (done separately for the size/CVE measurement
below) — **all 4 DLL/deps.json hashes match exactly**, confirming the live app is running exactly this task's HEAD
with no staleness:

```
Sprk.Bff.Api.dll        508145d7004553f2e17eaf0aaf55e74614c5ad6d382efb0a3f07c71cffcc525a  (live == local)
Sprk.Bff.Api.deps.json  6d9d4ce11426f439b16aeccd3fa6994f2efc3a3df018fb8228762b8b0f5cc00e  (live == local)
Spaarke.Core.dll        58d912ee25b0a1b4ac6dadc65cbe63f78d50767d9c50ff8d98e14a7947acef17  (live == local)
Spaarke.Dataverse.dll   d7534dfaec0d20b2cd92a036ca19f421aec6bc965ba18def9a7c91fa30bf76b8  (live == local)
```

### Endpoint smoke (unauthenticated — expect 401 = route registered + auth required; 404 = sanity control)

| Endpoint | Method | Result |
|---|---|---|
| `/healthz` | GET | 200 |
| `/ping` | GET | 200 |
| `/api/ai/analysis/fork` | POST | **401** |
| `/api/ai/analysis/promote` | POST | **401** |
| `/api/ai/chat/sessions/by-analysis/{guid}` | GET | **401** |
| `/api/ai/chat/sessions/{id}/compose-outputs` | GET | **401** |
| `/api/ai/chat/sessions/{id}/review-memo` | POST | **401** |
| `/api/ai/chat/sessions/{id}/review-memo` | GET | **401** |
| `/api/ai/chat/sessions/{id}/review-memo/docx` | GET | **401** |
| (sanity control) bogus route | GET | **404** |

All routes registered (401, not 404) except the deliberate bogus-route control (404) — confirms a complete package,
not a truncated deploy silently dropping routes.

## Step 3 — SpaarkeAi code page deploy

### Shared-lib rebuild (mandatory per `code-page-deploy` skill — 3 libs touched by this project's history)

`git log --name-only --grep="agreements-r1" -- 'src/client/shared/**'` showed this project touched
`Spaarke.AI.Widgets`, `Spaarke.Compose.Components`, `Spaarke.UI.Components`. All three rebuilt (`npm run build` = `tsc`)
**before** touching SpaarkeAi — all three compiled clean, zero errors.

### Build

```
cd src/solutions/SpaarkeAi
rm -rf dist node_modules/.vite .vite   # mandatory cache clear
npm run build
```
- `check-html-css-reset`: PASS. `tsc-surface-gate`: 73 pre-existing errors in shared libs (deferred to Phase B per the
  gate's own policy), **0 surface-owned** — gate passes.
- Vite build: 4,008 modules, output `dist/spaarkeai.html` = 5,251.51 KB (gzip 1,455.07 KB).
- Ribbon bundles rebuilt (`AnalysisRecordLaunch.js`, `DocumentComposeLaunch.js`, `EntityFormLaunch.js`,
  `WorkspaceLaunch.js`) — unaffected by this task, rebuilt as part of the standard `npm run build` chain.

### Cache-poisoning verification (mandatory per skill)

Grepped the built bundle for two known-recent strings before deploying:
- `locationLabel` (task 042, most recent commit `057a41972`) — **present** (3 occurrences).
- `agreement-classify` consumerType check (tasks 020/021) — **present**.

Confirms the bundle is NOT stale/cache-poisoned.

### Deploy

```
scripts\Deploy-SpaarkeAi.ps1
```
Updated existing web resource `sprk_spaarkeai` (`5206a442-3451-f111-bec7-7ced8d1dc988`), published. Bundle size
5,134 KB.

### Post-deploy content verification (load smoke substitute — no browser available in this session)

Fetched the live web resource content back via the Dataverse Web API and decoded it:
- Decoded size: **5,257,154 bytes** — byte-identical to the local `dist/spaarkeai.html` build.
- Both marker strings (`locationLabel`, `agreement-classify`) present in the live content.
- `modifiedon` timestamp fresh (`2026-08-01T03:50:22Z`, i.e. this deploy).

**UI/browser load smoke (three-pane render, review flow) was NOT performed in this session** — no browser tool
available. Per the POML fallback, the deployment artifact upload + version is verified above (byte-identical content,
fresh timestamp, both feature markers present); the actual UI load smoke is deferred to **task 061** (e2e UI tests),
which is the next task in the critical path and explicitly scoped for this.

## Step 4 — Report

### Publish size (ADR-029 / NFR-01)

```
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
```

| Metric | Value |
|---|---|
| Files | 251 |
| Uncompressed | 146 MB (≤150 MB ✓) |
| `runtimes/` directory | absent (linux-x64 framework-dependent confirmed ✓) |
| `*.js.map` in `wwwroot/` | 0 ✓ |
| **Compressed (Compress-Archive Optimal)** | **48.25 MB** |
| Ceiling (hard stop) | 60 MB |
| Review threshold | 55 MB |
| Prior baseline (task 055, 2026-07-08, incl. PDBs) | 49.63 MB |
| **Delta vs baseline** | **-1.38 MB** (decrease) |

No escalation triggers fire (not ≥55 MB, not ≥+5 MB delta). This task made **zero** `Sprk.Bff.Api.csproj` /
`PackageReference` changes — the delta reflects normal build variance across the merged history since task 055, not
anything introduced here. §10 BFF Hygiene + NFR-01 verified: publish size = 48.25 MB, delta = -1.38 MB vs baseline,
no new HIGH CVE (below).

### CVE scan

```
dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive
```

Result: **5 HIGH-severity advisories on `System.Security.Cryptography.Xml` 8.0.3**
(GHSA-g8r8-53c2-pm3f, GHSA-8q5v-6pqq-x66h, GHSA-cvvh-rhrc-wg4q, GHSA-23rf-6693-g89p, GHSA-mmjf-rqrv-855v).

**Not introduced by this task** — identical finding to tasks 050 and 051 (both ran this exact scan earlier in this
same project and documented the same 5 advisories on the same pinned version). `git diff` confirms zero changes to
any `.csproj` across this task and the whole project's commit range for this package. Pre-existing, out of scope to
remediate here (no server-side cryptography code touched by this project). Flagged for an owner-level package-bump
follow-up outside this task's boundary (no code changes permitted per HARD BOUNDARIES).

### Deployed hashes / versions

| Component | Identifier | Verification |
|---|---|---|
| BFF | HEAD `057a41972e7df19f241c609ca7afc79c66278b79` | Hash-verify 4/4 (script) + independent post-deploy cross-check (this report) |
| SpaarkeAi web resource | `sprk_spaarkeai` (`5206a442-3451-f111-bec7-7ced8d1dc988`) | Content byte-identical (5,257,154 bytes) to local build |
| `agreement-classify` Action | `sprk_analysisaction` (`53406e5b-5b8d-f111-8076-70a8a58a7766`) | Read-back verified field-by-field |
| `agreement-classify` Binding | `sprk_playbookconsumer` (`ed92d769-5b8d-f111-8076-70a8a58a7766`) | Read-back verified, lookup resolves |

### Rollback path

- **BFF**: Azure App Service (Linux, OneDeploy) does not retain a separately-restorable "previous package" artifact
  in a form this script exposes, but the practical rollback is well-defined and low-risk: (1) the pre-deploy hash
  snapshot above documents exactly what was live immediately before this deploy; (2) Kudu deployment history
  (`GET /api/deployments`) retains prior deployment IDs (5 most recent captured, oldest inspected:
  `14243cae-4a4b-4c5e-a04e-83888fdd5215` at `2026-07-31T16:09:07Z`, `56476210-e327-43f1-8f88-16beac9b95b8`,
  `bc3554fb-2f16-4bd3-bca7-ba3fdeb7c5f3`, `088c0aaf-84db-49a2-9fac-fabc5b386459`); (3) since this is a shared dev
  App Service with multiple active worktrees deploying to it, the durable rollback anchor is git history — check out
  the last-known-good commit and re-run `Deploy-BffApi.ps1`. This task introduced no schema/contract changes to the
  BFF (data + web-resource deploy only), so the blast radius of a rollback need is limited to the redeploy itself.
- **SpaarkeAi web resource**: Dataverse `webresourceset` does not version content automatically. Rollback = rebuild
  from a prior commit (`git checkout <sha>` in a scratch worktree, rebuild per the shared-lib + cache-clear procedure
  above, `Deploy-SpaarkeAi.ps1`). The immediately-prior deployed bundle's commit is not separately archived by this
  task; the safe reference point is `057a41972` (this task's HEAD, i.e. re-running this same deploy is itself the
  "known good" recovery path since nothing here was code-changed).
- **Dataverse rows**: both new rows (`agreement-classify` Action + Binding) are pure additions — rollback is a
  `delete_record` on either GUID above if ever needed; no existing row was modified.

### Known limitations (recorded honestly, not deploy failures)

- **No Reasoning-tier Azure OpenAI deployment in `spaarkedev1`** (carried forward from tasks 002/003/020). The new
  `agreement-classify` Action (Reasoning tier) and the existing `agreement-review` Action will fail at the
  model-call layer until this is provisioned — `ModelTierDeploymentResolver` falls back to the Standard deployment
  per its documented behavior, so the Action still executes rather than 404ing, but live grading remains env-blocked.
  This is an **owner action** (Azure OpenAI capacity/deployment provisioning), not something this task can fix.
  Endpoint-surface smoke (auth, routing, 4xx/5xx semantics — all green above) is the correct gate for a data +
  infrastructure deploy task; model-layer behavior is out of scope here.
- **PR #690 (LFS Compose fixtures) still OPEN** — CI seam/eval tests will fail on this project's eventual PR until
  #690 merges. Recorded for the wrap-up PR description.
- **UI/browser load smoke not performed** (no browser tool in this session) — deployment-artifact verification
  (byte-identical content + fresh timestamp + feature-marker presence) substitutes; actual three-pane render / review
  flow smoke is task 061's responsibility per the POML's own fallback instruction.

## Deviations / escalations

**None required a hard stop.** Two judgment calls, both low-risk and documented:

1. **`Capture-BffBaseline.ps1` output redirected** to this project's `notes/` directory instead of the script's
   hardcoded default (`projects/sdap-bff-api-remediation-fix/baseline/`) — avoids writing into an unrelated project's
   directory from this worktree; the script's `-OutputJson` parameter exists exactly for this. Reduced `-Samples` to
   5 (from default 10) to keep the capture under 3 minutes; still produced meaningful P50/P95 aggregates across all
   323 routes.
2. **`sprk_kind = Prompted (100000000)` set explicitly** on the new Action row even though `ConsumerRoutingService`
   defaults an unset `sprk_kind` to `Prompted` — explicit is safer than relying on the default for a new row, and
   costs nothing.

No ADR conflicts encountered. No scope expansion. HARD BOUNDARIES honored: no `.claude/**` writes, no
`current-task.md`/`TASK-INDEX.md` edits, no git commit/push, zero code changes (only this task's own POML `<status>`
and this report were written under `projects/`).
