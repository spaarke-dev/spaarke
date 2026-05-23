# Repo-Level Failure Modes Catalog

> **Purpose**: Cross-cutting failure patterns that don't belong inside any single skill's Gotchas section. The agent should mentally cross-reference this catalog before executing a skill; sessions that hit a NEW failure type should append an entry here.

> **Last Updated**: 2026-05-14 (inaugural entries)

---

## Classification

| Class | Meaning |
|---|---|
| **Anti-pattern** | Something that LOOKS RIGHT but isn't. A skill or doc may even prescribe it — but it's wrong. Discovery requires empirical pushback. |
| **Gotcha** | Something that HAPPENS UNEXPECTEDLY. The doc/skill is fine; the runtime/platform/environment has surprising behavior. |

The distinction matters because the fix is different. Anti-patterns require *unlearning* (update the offending skill or doc, and capture the wrong-belief here so it doesn't return). Gotchas require *defensive code* and clearer warnings.

---

## Table of Contents

### Anti-patterns
- [AP-1: Skill prescribes X but X is wrong (`/pcf-deploy` "NEVER use `build:prod`")](#ap-1-skill-prescribes-x-but-x-is-wrong)
- [AP-2: Optional field in BFF contract that two clients drift apart on (orphan RAG chunks)](#ap-2-optional-field-in-bff-contract-that-two-clients-drift-apart-on)
- [AP-3: GUID case mismatch between Xrm and Web API clients (case-sensitive AI Search filters)](#ap-3-guid-case-mismatch-between-xrm-and-web-api-clients)

### Gotchas
- [G-1: Settings-file schema malformation silently disables permission rules + hooks](#g-1-settings-file-schema-malformation-silently-disables-permission-rules--hooks)
- [G-2: Default health-check window sized for old behavior (Linux cold start)](#g-2-default-health-check-window-sized-for-old-behavior)
- [G-3: Zero-second GitHub Actions workflow failures are startup failures, not test failures](#g-3-zero-second-github-actions-workflow-failures-are-startup-failures-not-test-failures)
- [G-4: AI Search index field created without `filterable: true` cannot be made filterable later](#g-4-ai-search-index-field-created-without-filterable-true-cannot-be-made-filterable-later)

---

## Anti-patterns

### AP-1: Skill prescribes X but X is wrong

**Title**: `/pcf-deploy` skill said "NEVER use `npm run build:prod`" — actually `build:prod` IS the correct invocation.

**Date**: 2026-05-14 (caught after user pushback)

**Classification**: Anti-pattern (skill prescribed wrong behavior with confident "NEVER" framing)

**What happened**: While deploying SpeDocumentViewer PCF, the bundle size jumped from 440 KB to 6.7 MB. Initially deferred as "needs investigation." User pushed back: "did you use the skill `/pcf-deploy` to check the build process?" — investigation revealed the skill explicitly said "NEVER use `npm run build:prod` — pcf-scripts does not have a separate production build script; use `npm run build`." This was wrong on both counts: (1) `pcf-scripts build --buildMode production` IS a separate production mode, and (2) `npm run build` defaults to dev mode (no tree-shaking) producing 5-10× larger bundles.

**Root cause**: A doc/skill confidently asserted a "NEVER" rule. Wrong-belief was reinforced because the rule was framed as authoritative. The check that would have caught it (an empirical build-mode comparison) never ran because the skill already "had the answer."

**Fix**:
- Removed wrong "NEVER" instruction from `.claude/skills/pcf-deploy/SKILL.md`
- Added "Bundle Size & Production Mode" section mandating `build:prod`
- Fixed 3 PCFs whose `package.json` `build:prod` scripts had wrong flags (`-- --mode production` and `--production` are silently ignored by `pcf-scripts`; correct form is `pcf-scripts build --buildMode production`)
- Commit: `c132773c`

**Prevention**: When a skill says "NEVER" or "ALWAYS," that's the cue to verify empirically before trusting. Stronger claims in docs warrant stronger evidence — and visible evidence (e.g., a comparison run, a link to the upstream CLI docs) should accompany absolute rules. Phase 2a skill audit `needs-substantive-rewrite` recommendation exists specifically for this class of issue.

**Evidence**: commit `c132773c` (skill fix + 3 package.json fixes)

---

## Gotchas

### G-1: Settings-file schema malformation silently disables permission rules + hooks

**Title**: `.claude/settings.json` had a flat-format `hooks` block (using `{matcher, command}`) for ~2 months — it silently failed to register, so the format-on-edit and quality-gate hooks never ran.

**Date**: 2026-03-14 introduced. 2026-05-14 caught (when a user screenshot showed "Settings file failed to parse: Expected array, but received undefined").

**Classification**: Gotcha (the runtime tolerated invalid shape silently)

**What happened**: The settings.json `hooks` block was written in a flat shape — `{matcher: "Edit", command: "..."}` — at the same time `TaskCompleted` was added as a hook event. Claude Code's runtime parser silently rejected the malformed shape AND `TaskCompleted` (which is not a real event). The settings parsed as JSON (no syntax errors) but the hooks never fired. We went 2 months thinking format-on-edit was running when it wasn't.

**Root cause**: (1) Settings schema does not have a hard reject on shape mismatch — invalid sub-blocks just silently no-op. (2) The agent had no validation step against the published schema during edits. (3) The "tested by use" feedback loop (hooks visibly firing) is too quiet — if the hook does nothing or does only background work, you don't notice it's not running.

**Fix**: Reshaped to the correct nested form:
```json
"hooks": {
  "PostToolUse": [
    {
      "matcher": "Edit",
      "hooks": [{ "type": "command", "command": "bash scripts/quality/post-edit-lint.sh" }]
    }
  ],
  "Stop": [
    { "hooks": [{ "type": "command", "command": "bash scripts/quality/task-quality-gate.sh" }] }
  ]
}
```
Changed `TaskCompleted` (not a real event) to `Stop`. Commit: `8ca796ab`.

**Prevention**: Phase 4a task 060 introduces a JSON-schema validator for `.claude/settings.json` that runs in pre-commit. Note from Phase 0 inventory: the published schema's `permissionRule` regex is stricter than Claude Code's runtime parser, so the validator must focus structural validation on the `hooks` block (where the actual bug lived) and not enforce the strict regex on `permissions.allow`.

**Evidence**: commit `8ca796ab` (settings.json fix); Phase 0 task 004 inventory at `projects/ai-procedure-quality-r1/notes/inventory/settings.md` confirms current state is nested-correct.

---

### G-2: Default health-check window sized for old behavior

**Title**: `Deploy-BffApi.ps1` 60-second health-check window false-failed Linux App Service deploys to the demo environment.

**Date**: 2026-05-14

**Classification**: Gotcha (default tuned for Windows historical behavior; Linux platform has different cold-start)

**What happened**: Demo BFF deploy reported failure at the health-check step. The actual deployment had succeeded (SHA-256 hash-verify of 6 critical files all matched) but the `/healthz` endpoint hadn't responded within 60 seconds. Linux App Service cold start is 90-120 seconds.

**Root cause**: The deploy script's `$MaxHealthCheckRetries = 12` (with 5-second waits = 60s window) was tuned to Windows App Service warm-restart behavior. When demo was created on Linux, nobody re-tuned the window.

**Fix**: Bumped `$MaxHealthCheckRetries = 24` (= 120s window). Also clarified in `bff-deploy` skill that hash-verify success + healthz timeout means the deploy IS correct, just still booting (two-layer safety net). Commit: `6d7bcf45`.

**Prevention**: When tuning defaults (timeouts, retry counts, batch sizes), verify against CURRENT behavior, not historical assumptions. If a default is environment-dependent (Linux vs Windows, dev vs prod), make it explicit in the script comments. Phase 4a task 067 will add `Check-DeployScriptDrift.ps1` that compares deploy-script defaults against observed runtimes.

**Evidence**: commit `6d7bcf45` (script tuning + skill update)

---

### G-3: Zero-second GitHub Actions workflow failures are startup failures, not test failures

**Title**: 5 of 13 workflows fail 100% of recent runs in 0 seconds. The failures look like "tests failing" but they're actually action-resolution failures at workflow startup.

**Date**: First observed 2026-05-14 during Phase 0 inventory (task 003).

**Classification**: Gotcha (failure presentation is misleading — `gh run view` shows "failed" without distinguishing startup-failure vs test-failure)

**What happened**: Phase 0 workflow inventory found 5 workflows failing 100% of recent runs (sdap-ci, deploy-infrastructure, deploy-promote, Deploy BFF API, Nightly Quality) — every run terminates in 0-2 seconds. Hypothesis: action references like `actions/checkout@v6`, `actions/upload-artifact@v6`, `actions/download-artifact@v7`, `actions/cache@v5` reference major versions that do not exist in the GitHub Actions registry (current published majors are v4/v5/v3). GitHub fails the run instantly without proceeding to any job step.

**Root cause**: Action references can be wrong without any local validation. The wrong version gets through PR review because `gh run view` shows "failed" — a reviewer assumes "the tests broke," not "the workflow couldn't even start." Diagnosis requires drilling into the run logs or reading the workflow file carefully.

**Fix**: Phase 4b task 070 will diagnose and fix these specific workflows. Phase 4b task 071 adds `actionlint` to a `procedure-quality` workflow that runs on every PR touching `.github/workflows/*.yml` — `actionlint` catches non-existent action versions BEFORE merge. Phase 4b task 074 introduces `dependabot.yml` to keep action versions in sync going forward.

**Prevention**:
- Use exact SHA pins or trusted-action tags only (F-20 target — currently 0 of 115 actions are SHA-pinned per Phase 0 inventory)
- Lint workflow YAML with `actionlint` in CI
- When a workflow shows 0-second failure, look at action version mismatches FIRST, test logic last

**Evidence**: Phase 0 task 003 inventory at `projects/ai-procedure-quality-r1/notes/inventory/workflows.md` enumerates the 5 affected workflows and the suspect actions.

---

### AP-2: Optional field in BFF contract that two clients drift apart on

**Title**: `/api/ai/rag/index-file` accepts `documentId` as optional. The Document Upload Wizard never sent it; the "Send to Index" ribbon did. Result: every wizard-uploaded file produced **orphan chunks** in `spaarke-knowledge-index-v2` (indexed but with `documentId=null`).

**Date**: 2026-05-22 (caught after multi-month regression visible only as `sprk_searchindexed=No` on Dataverse Document records)

**Classification**: Anti-pattern (contract was "valid" — the field is genuinely optional for some callers — but the wizard treated it as not-needed when in fact it was load-bearing for the downstream UX)

**What happened**: The BFF endpoint's [`FileIndexRequest.DocumentId`](../../src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs) is typed `string?` and the Dataverse tracking-field write at `RagEndpoints.cs:480` is gated on `if (!string.IsNullOrEmpty(request.DocumentId))`. The wizard's `triggerRagIndexing` at [`uploadOrchestrator.ts:447`](../../src/solutions/DocumentUploadWizard/src/services/uploadOrchestrator.ts) sent `{ driveId, itemId, fileName, tenantId }` only. `record.recordId` was available in the caller's scope but never threaded through. The endpoint responded 200, chunks landed in the index without `documentId` or `parentEntityType`, and the user-facing affordances that join chunks back to Dataverse (Search Indexed toggle, Find Similar, Open from search results, DocumentRelationshipViewer graph) all silently failed.

**Root cause**:
1. **Two entry points, one contract, asymmetric callers.** The "Send to Index" ribbon was built by the team that knew it needed `documentId` (it looks the doc up to construct the request anyway). The Document Upload Wizard was built by a different work stream that thought of indexing as a fire-and-forget "send file bytes" call.
2. **Fire-and-forget client call swallows server signals.** The wizard's `.catch(err => logger.warn(...))` made every error a warning — never an error toast, never blocked the success indicator.
3. **No regression test asserts the indexed → linked → searchable lifecycle**.
4. **No telemetry on the indexing pipeline** before 2026-05-22 — `LogInformation` calls inside `RagService` and `FileIndexingService` did not include the resolved index name or per-chunk failure reasons. Even when investigation started, the data wasn't there.
5. **No cross-check between the Dataverse "Search Indexed" field and the actual index state** — two observation surfaces, no reconciliation.

**Fix**: commit `dd288532` (wizard now passes `documentId` + `parentEntity`), commit `15f82369` (BFF diagnostic logs + silent-success guard at `FileIndexingService:316` — `Success = allSucceeded && results.Count > 0`), commit `fbbaee29` (paired with AP-3 fix). See [`.claude/patterns/ai/indexing-pipeline.md`](patterns/ai/indexing-pipeline.md) for the canonical contract.

**Prevention**:
- Any new BFF endpoint that has a "linkage" side effect (writing to Dataverse, updating tracking fields, etc.) should treat the linkage as **part of the contract**, not as an optional optimization. Either make the field required or move the linkage to a separate explicit endpoint.
- Client-side fire-and-forget patterns should log at `error` level (not `warn`) on non-2xx, and include response body excerpts. Hidden warnings hide regressions for months.
- Add the indexing pipeline to the [observability-as-contract checklist](#observability-as-contract): every indexing call should emit `Resolved deployment ... IndexName=...` so the destination is auditable from logs alone.

**Evidence**: project artifacts at `projects/ai-search-indexing-fix/ISSUE.md` (original investigation, 2026-05-19) and the today-session resolution that produced commits `15f82369`, `dd288532`, `fbbaee29`.

---

### AP-3: GUID case mismatch between Xrm and Web API clients

**Title**: `Xrm.Page.data.entity.getId()` returns `{UPPERCASE-GUID}`; the Dataverse Web API client returns `lowercase-guid`. Azure AI Search `Edm.String` filters are case-sensitive. So the same document ended up indexed with two different documentId casings depending on which entry point was used — and downstream lookups by either casing missed half the data.

**Date**: 2026-05-22 (discovered during AP-2 investigation when Find Similar worked for wizard-uploaded files but failed for Send-to-Index'd ones)

**Classification**: Anti-pattern (well-known Dataverse gotcha that should have been normalized at the boundary but wasn't)

**What happened**: After AP-2 was fixed, a follow-up test showed:
- Wizard upload of "Deposition Transcript" → `documentId=ca7d0dda-...` (lowercase) → Find Similar works
- Send-to-Index on "Settlement Memo" → `documentId=3FBA84FA-...` (uppercase) → Find Similar fails

Both chunks were correctly indexed and linked — but the Find Similar lookup queries by the document's lowercase Dataverse GUID against an index where some chunks had uppercase IDs. `Edm.String eq` doesn't match across cases.

**Root cause**:
- The wizard uses the Dataverse Web API client (`createCodePageDataverseClient`), which returns lowercase GUIDs.
- The ribbon at [`sprk_DocumentOperations.js:2146`](../../src/client/webresources/js/sprk_DocumentOperations.js) uses `Xrm.Page` / `getId()`, which returns `{UPPERCASE}`. It strips braces but doesn't normalize case.
- The BFF passes whatever it receives through to the index unchanged.
- Azure AI Search `Edm.String` equality is case-sensitive (vector search, full-text search, and `search.ismatch` are not — but `eq` is).

**Fix**: commit `fbbaee29`
1. BFF (defensive): `FileIndexingService.IndexTextInternalAsync` normalizes `documentId` to lowercase at the single convergence point — covers all three entry points (OBO, app-only, content-only).
2. Ribbon (clean contract): `sprk_DocumentOperations.js:sendToIndex` `.toLowerCase()`s the documentIds in all three context paths (selectedItemIds, form context, SelectedControl getGrid).

Existing uppercase chunks in dev were intentionally left as-is per owner decision (would re-incur indexing cost; dev data only). They'll heal on the next Send to Index for each doc.

**Prevention**:
- Treat Dataverse GUIDs as if they have a canonical form (lowercase, no braces) and **normalize at every boundary** that crosses a system (Dataverse ↔ BFF ↔ AI Search ↔ external API). Don't trust callers.
- When designing an index schema, prefer using fields with case-insensitive analyzers (e.g., `Edm.String` filterable with the default analyzer is fine for full-text but case-sensitive for `eq` — consider a normalizer if exact-match comparisons must be case-insensitive).
- Type-safe ID wrappers would help long-term. Today every GUID is a `string` in TypeScript and C#; both languages have the tools to make a stronger guarantee (branded types in TS, `record struct DocumentId(Guid Value)` in C#).

**Evidence**: commit `fbbaee29`, live Azure Search records showing both casings coexisting after the 2026-05-22 test session.

---

### G-4: AI Search index field created without `filterable: true` cannot be made filterable later

**Title**: Azure AI Search makes most field properties **immutable after creation**. If a `Collection(Edm.String)` field is created without `filterable: true`, any query that tries to filter on it (e.g., the AIPU2-027 privilege-group security filter) returns 400 — and the only fix is to create a NEW field or rebuild the entire index.

**Date**: 2026-05-19 (discovered when a Portal-added `privilege_group_ids` field on `spaarke-knowledge-index-v2` had `filterable: false` and no way to change it)

**Classification**: Gotcha (Azure platform constraint; not obvious unless you've hit it)

**What happened**: The `privilege_group_ids` field was supposed to be deployed from `infrastructure/ai-search/spaarke-knowledge-index-v2.json:228` (which correctly declares `filterable: true, retrievable: true`), but the deploy script [`scripts/ai-search/Deploy-IndexSchemas.ps1:42`](../../scripts/ai-search/Deploy-IndexSchemas.ps1) targeted the **wrong index name** (`spaarke-knowledge-index` vs the actually-used `-v2`), so the schema file was never applied. When a 400 error surfaced for null writes, the field was added manually via the Azure Portal UI to unblock the immediate problem — and Portal defaults landed `filterable: false`. Subsequent attempts to change it via REST API returned: *"Existing field 'privilege_group_ids' cannot be modified."*

**Root cause**:
1. **Azure AI Search field properties are largely immutable post-creation.** `filterable`, `searchable`, `sortable`, `facetable`, and `analyzer` cannot be changed after a field is first created. Only the field-level `retrievable` flag and a few collection-level settings (synonym maps, scoring profiles) are mutable.
2. **Portal "Add field" UI defaults are not the same as the schema-file declared values.** Portal-added fields land with conservative defaults.
3. **Deploy script bug compounded**: schema file was correct, deploy script targeted the wrong index name, so the live index never received the canonical schema.

**Fix** — short-term (dev): leave `privilege_group_ids` on dev `spaarke-knowledge-index-v2` as `filterable: false`. The privilege filter in `RagService.cs:817` will return 400 on retrieval queries in dev (affects chat/RAG retrieval only; semantic search PCF does NOT use this filter). This is acceptable for dev where security boundaries are relaxed and the cost of re-indexing 739 docs would be wasted on a test environment.

**Fix** — long-term (demo + production):
1. Provision the index from `infrastructure/ai-search/spaarke-knowledge-index-v2.json` **directly via the REST API** (not the Portal UI). Use `PUT /indexes/{name}?api-version=2024-07-01` with the schema file's body.
2. After fixing `scripts/ai-search/Deploy-IndexSchemas.ps1` so `IndexMap` targets `spaarke-knowledge-index-v2`, run the script during environment provisioning.
3. Verify before declaring the environment ready:
   ```bash
   curl -s -H "api-key: $KEY" "https://{search-svc}.search.windows.net/indexes/spaarke-knowledge-index-v2?api-version=2024-07-01" \
     | python -c "import sys,json; d=json.load(sys.stdin); f=[x for x in d['fields'] if x['name']=='privilege_group_ids'][0]; print(f)"
   # Expect: filterable=True, retrievable=True
   ```

**Prevention**:
- **Treat index schemas as immutable code, not as Portal UI artifacts.** Schema lives in `infrastructure/ai-search/*.json` and is the source of truth. The Portal is for inspection only.
- **Deploy-IndexSchemas.ps1 needs a CI smoke test** that compares the live index field set to the schema file. Drift detection prevents this from happening to the next environment.
- **For new environments**: provision the index **first**, then enable any code paths that filter on its fields. Don't let a code feature ship that filters on a field the live index doesn't have configured for filtering.
- **For new index fields**: when adding a field to an existing index, deploy the schema change first via `PATCH /indexes/{name}` (NOT Portal UI). If a `Collection(Edm.String)` field needs `filterable: true`, that's the only chance to set it.

**Evidence**: live dev index field config at 2026-05-22 confirms `filterable: false`. Existing project `projects/ai-search-indexing-fix/ISSUE.md` §2 documents the original 400 incident and the Portal-add workaround. Schema file `infrastructure/ai-search/spaarke-knowledge-index-v2.json:228` shows the canonical correct declaration that should land in new environments.

---

## How to use this catalog

1. **Before executing a skill**, the agent should mentally cross-reference: does this skill touch anything in the catalog?
2. **When a skill says "NEVER" or "ALWAYS"** with confidence, but the agent has no recent empirical verification, the agent should add a brief "verify" step (per AP-1's prevention).
3. **When a session surfaces a NEW cross-cutting failure pattern** — something that affects more than one skill, or recurs across different sessions — append an entry here. Use the same shape: title, date, class, what-happened, root-cause, fix, prevention, evidence.
4. **Bidirectional links**: each affected skill should have a `See FAILURE-MODES.md#<anchor>` pointer in its Gotchas section. (Phase 2b refinements will add these.)

---

*Established 2026-05-14 by project `ai-procedure-quality-r1` (task 013). Cross-reference: [.claude/CHANGELOG.md](CHANGELOG.md) for the entry stream.*
