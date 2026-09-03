# Repo-Level Failure Modes Catalog

> **Purpose**: Cross-cutting failure patterns that don't belong inside any single skill's Gotchas section. The agent should mentally cross-reference this catalog before executing a skill; sessions that hit a NEW failure type should append an entry here.

> **Last Updated**: 2026-09-02 (added AP-12: a comment becomes the constraint — prose outliving its mechanism, 8 instances in one session; also back-filled the missing AP-11 TOC entry)

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
- [AP-4: Silent dev/demo deployed-bundle drift causing /api-prefix bug](#ap-4-silent-devdemo-deployed-bundle-drift-causing-api-prefix-bug)
- [AP-5: AbortController in a useEffect whose deps include your own state transition](#ap-5-abortcontroller-in-a-useeffect-whose-deps-include-your-own-state-transition)
- [AP-6: Interpolating a raw GUID into `@odata.bind` — braces cause "Error in query syntax" (use `cleanGuid`)](#ap-6-interpolating-a-raw-guid-into-odatabind--braces-cause-error-in-query-syntax)
- [AP-7: Converting a silent fallback into fail-fast, verified with targeted tests only](#ap-7-converting-a-silent-fallback-into-fail-fast-verified-with-targeted-tests-only)
- [AP-8: A green suite treated as the END of verification rather than the start of it](#ap-8-a-green-suite-treated-as-the-end-of-verification-rather-than-the-start-of-it)
- [AP-9: Amending a failing test to match the source, without checking the source against the vendor contract](#ap-9-amending-a-failing-test-to-match-the-source)
- [AP-10: A JSON-aware renderer that escapes one nesting level, over a config that is re-parsed at a deeper level](#ap-10-a-json-aware-renderer-that-escapes-one-nesting-level-over-a-config-re-parsed-deeper)
- [AP-11: Code that RUNS but reaches the wrong destination — no compiler and no test spans the seam](#ap-11-code-that-runs-but-reaches-the-wrong-destination--no-compiler-and-no-test-spans-the-seam)
- [AP-12: A comment becomes the constraint — prose outlives the mechanism it describes](#ap-12-a-comment-becomes-the-constraint--prose-outlives-the-mechanism-it-describes)

### Gotchas
- [G-1: Settings-file schema malformation silently disables permission rules + hooks](#g-1-settings-file-schema-malformation-silently-disables-permission-rules--hooks)
- [G-2: Default health-check window sized for old behavior (Linux cold start)](#g-2-default-health-check-window-sized-for-old-behavior)
- [G-3: Zero-second GitHub Actions workflow failures are startup failures, not test failures](#g-3-zero-second-github-actions-workflow-failures-are-startup-failures-not-test-failures)
- [G-4: AI Search index field created without `filterable: true` cannot be made filterable later](#g-4-ai-search-index-field-created-without-filterable-true-cannot-be-made-filterable-later)
- [G-5: Dataverse Application User registration missing for Managed Identity](#g-5-dataverse-application-user-registration-missing-for-managed-identity)
- [G-6: `Connect-ExchangeOnline -UserPrincipalName` mismatch failure](#g-6-connect-exchangeonline--userprincipalname-mismatch-failure)
- [G-7: Git Bash MSYS path mangling on Azure resource IDs](#g-7-git-bash-msys-path-mangling-on-azure-resource-ids)
- [G-8: SPE container creation requires confidential client; canonical scripts use public client](#g-8-spe-container-creation-requires-confidential-client-canonical-scripts-use-public-client)
- [G-9: BFF AI Search has TWO index-name settings — `AiSearch:KnowledgeIndexName` (read) and `Analysis:SharedIndexName` (write)](#g-9-bff-ai-search-has-two-index-name-settings)
- [G-10: HTML5 DnD `dragEnter` fires on ancestors when preview extends beyond pointer](#g-10-html5-dnd-dragenter-fires-on-ancestors-when-preview-extends-beyond-pointer)
- [G-11: `Xrm.Navigation.navigateTo({ target: 2 })` opens a separate window — cross-window signaling requires sessionStorage, not `window.*`](#g-11-xrmnavigationnavigateto-target-2-opens-a-separate-window--cross-window-signaling-requires-sessionstorage-not-window)
- [G-12: `dotnet test --no-build` runs a stale assembly while the build *truthfully* reports "up-to-date"](#g-12-dotnet-test---no-build-runs-a-stale-assembly-while-the-build-truthfully-reports-up-to-date)
- [G-13: A Dataverse `$select` is all-or-nothing — one bad column name blanks the entire control](#g-13-a-dataverse-select-is-all-or-nothing)
- [G-14: `Xrm.Utility.getEntityMetadata` returns the Client API shape (numeric `AttributeType`), NOT the Web API shape](#g-14-xrmutilitygetentitymetadata-returns-the-client-api-shape)
- [G-15: A detached `Xrm` method loses `this` and dies inside the platform](#g-15-a-detached-xrm-method-loses-this-and-dies-inside-the-platform)

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

### AP-6: Interpolating a raw GUID into `@odata.bind` — braces cause "Error in query syntax"

**Title**: Building an `@odata.bind` value (or any `/entityset(guid)` reference URL) from a GUID that came from an Xrm source without normalizing it. Xrm returns registry-format GUIDs (`{UPPERCASE-...}`); the Dataverse OData key predicate requires a **bare** GUID and rejects a braced one with HTTP 400 `Bad Request - Error in query syntax`. The error names **no property** — it's a URL-parse failure, not payload validation — which is the tell that distinguishes it from a bad scalar field.

**Date**: 2026-07-09 (Create Matter/Project 400 on record creation)

**Classification**: Anti-pattern (sibling of [AP-3](#ap-3-guid-case-mismatch-between-xrm-and-web-api-clients) — same root cause, different symptom: AP-3 is GUID *case* breaking AI Search `eq`; AP-6 is GUID *braces* breaking `@odata.bind`).

**What happened**: Create Matter failed at `createRecord('sprk_matter', …)` with `Bad Request - Error in query syntax`. The payload's lookup binds carried braced GUIDs (`/sprk_mattertype_refs({6CEDD99B-…})`) while a clean-sourced bind alongside them worked. The braced values came from the native lookup picker (`Xrm.Utility.lookupObjects` via `DataverseLookupField.openLookup`); the create path interpolated them raw. Five of seven `Create*Wizard` services + two shared services had the raw pattern; only Invoice/ReportCard had a local cleaner — so the fix was never uniform, which is exactly how one wizard (Matter) shipped broken.

**Root cause**: `Xrm.Utility.lookupObjects`, `getGlobalContext().userSettings.userId`, and `Xrm.WebApi.createRecord` all return braced (often uppercase) GUIDs. The OData `@odata.bind` key predicate `/entityset(<guid>)` accepts only a bare GUID.

**Fix** (PR #603 + barrel export PR #609):
- Single canonical **`cleanGuid()`** in `@spaarke/ui-components` (`PolymorphicResolverService`) — strips braces/whitespace + lowercases; no-op on already-bare ids. Consolidated 5 duplicate cleaners into it.
- Applied at every `@odata.bind` site across all 7 `Create*Wizard` services + `FieldMappingService` + `EntityCreationService`.
- Boundary normalization where Xrm hands GUIDs in: `xrmNavigationServiceAdapter.openLookup`, `xrmDataServiceAdapter.createRecord` return, `DataverseLookupField.onChange`.
- Re-exported from the package barrel so external consumers can import it directly.

**Directive — when + how to use `cleanGuid`**:
- **WHEN**: any time you build an `@odata.bind` value OR a `/entityset(guid)` reference URL from a GUID that *could* originate from Xrm — the native picker / `lookupObjects`, `userSettings.userId`, a `Xrm.WebApi.createRecord` return, or an AI pre-fill/resolver that echoes Dataverse GUIDs. Also apply it at **ingestion** (the moment an Xrm GUID enters component/service state) so braces never propagate downstream.
- **HOW**:
  ```ts
  import { cleanGuid } from '@spaarke/ui-components';
  payload[`${navProp}@odata.bind`] = `/contacts(${cleanGuid(contactId)})`;
  ```
  Deep-import fallback if the consumer is pinned to a stale `@spaarke/ui-components` that predates the barrel export (e.g. a tarball-pinned PCF): `import { cleanGuid } from '@spaarke/ui-components/dist/services/PolymorphicResolverService'`.
- **Rule of thumb**: it's a no-op on bare GUIDs, so wrap **every** GUID that goes into an OData bind/URL — no downside, and it's the one place that cannot tolerate braces. **Do NOT hand-roll a local `.replace(/[{}]/g, '')`** — reuse the shared helper (scattered local copies are what caused this bug).

**Prevention**: Treat Dataverse GUIDs as having a canonical form (bare, lowercase) and normalize at every boundary crossing a system, per AP-3. Prefer the shared `cleanGuid` over per-file cleaners. Solution-local code that builds its own binds outside the shared services (e.g. SmartTodo's `DataverseService`, EventDetailSidePane) must normalize at its own GUID source — SmartTodo already does this correctly via `getUserId()`.

**Evidence**: PR #603 (fix, merged as `d2696b616`), PR #609 (barrel export).

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

### AP-4: Silent dev/demo deployed-bundle drift causing /api-prefix bug

**Title**: LegalWorkspace `FilePreviewDialog.tsx:320` constructed `${getBffBaseUrl()}/communications/send` without the `/api` segment. The bug was latent in BOTH dev and demo deployed bundles for an unknown duration; it surfaced only when the Email Document feature was first exercised on demo (returned 404).

**Date**: 2026-05-25 (Phase 5 demo prep)

**Classification**: Anti-pattern (codebase had a documented convention that one caller silently violated)

**What happened**: `getBffBaseUrl()` (per `src/solutions/LegalWorkspace/src/config/runtimeConfig.ts:65`) returns the host-only origin (e.g., `https://spaarke-bff-demo.azurewebsites.net`). Every caller MUST append `/api/...`. `FilePreviewDialog.tsx:320` constructed `${getBffBaseUrl()}/communications/send` and hit 404 because the route table is mounted under `/api`. The bug shipped in both deployed bundles unnoticed because no automated test exercises this client → BFF path.

**Root cause**:
1. **Convention not enforced.** `getBffBaseUrl()` returns host-only; all 100+ other callers prefix `/api` correctly. One caller drifted.
2. **No typed wrapper at the LegalWorkspace boundary for communications calls** — bare template-string URL construction allowed the typo through code review.
3. **Latent in deployed bundles** — dev bundle had the same bug but Email Document was never invoked there, so the failure mode never produced a logged 404. Demo was the first env where the feature was exercised end-to-end.

**Fix**: 3-line source fix at `FilePreviewDialog.tsx:320` → `${getBffBaseUrl()}/api/communications/send`. Commit `2561ce37`. Rebuilt LegalWorkspace bundle; redeployed to dev + demo. See `projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md` Phase 5 task 060 post-deploy testing notes.

**Prevention**:
- Code review MUST verify every `${baseUrl}/path` pattern includes the `/api` segment in TS sources.
- Prefer the typed `src/client/shared/Spaarke.UI.Components/src/services/communicationApi.ts` wrapper for any communications endpoint — typed wrappers cannot accidentally omit `/api`.
- Add a smoke test that exercises one end-to-end LegalWorkspace → BFF call per feature module per deploy.

**Evidence**: commit `2561ce37` (source fix); `projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md` Phase 5 task 060 post-deploy section (issue discovered during user E2E testing on demo).

---

### G-5: Dataverse Application User registration missing for Managed Identity

**Title**: Demo BFF MI UAMI (`mi-bff-api-demo`) was granted Graph app-roles + Key Vault access + Cosmos data-plane RBAC, but was NOT registered as a Dataverse Application User on `spaarke-demo.crm.dynamics.com`. First Dataverse call from BFF returned 403 `"The user is not a member of the organization"` — surfaced to the client as a 500.

**Date**: 2026-05-25 (Phase 5 demo prep — discovered during user E2E testing)

**Classification**: Gotcha (Dataverse requires a separate Application User registration on top of Azure AD identity; easy to miss when promoting to a new env)

**What happened**: All Azure-side identity wiring was complete (UAMI created, Graph app-roles assigned, Cosmos RBAC granted, Key Vault Secrets User role granted). When the Document Upload wizard invoked `useAiSummary` → BFF `GET /api/ai/playbooks/{name}` → BFF Dataverse query, Dataverse returned:
```
StatusCode=Forbidden, ReasonPhrase=Forbidden
{"error":{"code":"0x80072560","message":"The user is not a member of the organization."}}
```
The BFF dutifully bubbled the 403 up to the client as a 500. Dev had this configured during original cutover but demo missed it.

**Root cause**: Dataverse requires every app-only principal calling its Web API to be registered as a `systemuser` with `applicationid` set to the principal's appId. This is a separate registration step from any Azure AD setup. `docs/guides/auth-deployment-setup.md` §6 documents the pattern but does so via a PowerApps UI walkthrough that's easy to skim past in an env-promotion checklist.

**Fix** (applied to demo 2026-05-25 ~22:00 UTC):
1. Create Application User via Dataverse Web API:
   ```
   POST /api/data/v9.2/systemusers
   {
     "applicationid": "<UAMI app-id>",
     "firstname": "BFF",
     "lastname": "<env> MI",
     "businessunitid@odata.bind": "/businessunits(<root-bu-id>)"
   }
   ```
2. Assign appropriate security role (System Administrator for demo, mirroring dev) via `systemusers({uid})/systemuserroles_association/$ref`.
3. Restart BFF App Service to clear stale Dataverse token cache.

**Prevention**:
- Add an "env-promotion checklist" item to `auth-deployment-setup.md` §6: a parameterized Web API POST + role assignment snippet (now done in Phase 5 wrap-up doc updates).
- BFF startup probe could verify a known-cheap Dataverse query before declaring healthy on a fresh deploy.

**Evidence**: `projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md` Phase 5 task 060 Issue 1; created systemuser `61d1cce0-8458-f111-bec7-7ced8d6f9aa0`.

---

### G-6: `Connect-ExchangeOnline -UserPrincipalName` mismatch failure

**Title**: Passing `-UserPrincipalName admin@spaarke.com` to `Connect-ExchangeOnline` while signing in with a different admin account in the interactive browser flow fails with `OperationStopped: Admin account chosen for authentication is different`. No Exchange cmdlets load; every subsequent command reports `not recognized`.

**Date**: 2026-05-25 (Phase 5 email setup runbook)

**Classification**: Gotcha (cmdlet validates UPN vs browser-selected account; mismatch is hard-fail, not a warning)

**What happened**: Operator ran `Connect-ExchangeOnline -UserPrincipalName admin@spaarke.com -ShowProgress $true` but signed into the browser flow with a different admin account. The cmdlet reported the mismatch and terminated without loading the Exchange module. All subsequent `New-ApplicationAccessPolicy` / `Test-ApplicationAccessPolicy` calls then failed with `not recognized as the name of a cmdlet`.

**Root cause**: `Connect-ExchangeOnline` cross-checks the `-UserPrincipalName` parameter against the account selected in the browser flow. If they don't match, the connection is rejected. The parameter is meant as a pre-fill hint, not an enforcement key.

**Fix**: Omit `-UserPrincipalName`. The cmdlet then accepts whatever Exchange Administrator account the operator selects in the browser:
```powershell
Connect-ExchangeOnline -ShowProgress $true
```

**Prevention**:
- In any runbook that invokes `Connect-ExchangeOnline`, document the omit-UPN pattern as the default. Add a note: "Do NOT pass `-UserPrincipalName` unless you're certain you'll sign in with that exact account."
- The Phase 5 demo email runbook (`EXECUTION-LOG.md` Part A) already includes the warning text — keep it canonical.

**Evidence**: `projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md` Phase 5 task 060 §Part A "Exchange Online ApplicationAccessPolicy" operator runbook.

---

### G-7: Git Bash MSYS path mangling on Azure resource IDs

**Title**: Running `az` CLI commands from Git Bash on Windows with arguments that start with POSIX-style paths (`/subscriptions/...`, `/tenantId`, partition keys like `/tenantId`) causes MSYS path translation: e.g., `/subscriptions/abc123/...` is rewritten to `C:/Program Files/Git/subscriptions/abc123/...` before reaching `az`. Result: cryptic `LinkedInvalidPropertyId` (or similar) errors that don't mention path mangling.

**Date**: 2026-05-25 (Phase 5 demo prep — multiple `az identity / az cosmosdb` invocations affected)

**Classification**: Gotcha (Git Bash MSYS layer transparently rewrites path-looking arguments; behavior is documented but not obvious from the error message)

**What happened**: During Phase 5 demo prep, multiple `az` commands failed:
- `az webapp identity assign --identities <resource-id>` (resource ID starts `/subscriptions/...`)
- `az cosmosdb sql container create --partition-key-path /tenantId` (partition key path)
- Various role-assignment scope arguments

Errors looked like `LinkedInvalidPropertyId` or "resource not found at scope `C:/Program Files/Git/subscriptions/...`" — both misleading.

**Root cause**: MSYS (the POSIX layer underlying Git Bash on Windows) sees any argument starting with `/` as a potential POSIX path and rewrites it to a Windows path before exec'ing the target. `az` cannot tell the rewrite happened — it just receives the mangled string.

**Fix**: Prefix `az` with `MSYS_NO_PATHCONV=1` for any command passing Azure resource IDs or partition keys:
```bash
MSYS_NO_PATHCONV=1 az webapp identity assign \
  --identities /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.ManagedIdentity/userAssignedIdentities/<uami>
MSYS_NO_PATHCONV=1 az cosmosdb sql container create --partition-key-path /tenantId ...
```

**Prevention**:
- Default to PowerShell or WSL for `az` commands that pass Azure resource IDs — neither has MSYS path translation.
- In Git Bash, set `MSYS_NO_PATHCONV=1` in the shell session before running a batch of `az` commands: `export MSYS_NO_PATHCONV=1`.
- When adding `az` examples to runbooks, prefer PowerShell snippets; if Git Bash is used, include the `MSYS_NO_PATHCONV=1` prefix inline.

**Evidence**: `projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md` Phase 5 task 060 "Critical lessons" §6.

---

### G-8: SPE container creation requires confidential client; canonical scripts use public client

**Title**: `Provision-Customer.ps1` Step 8 and `New-BusinessUnitContainer.ps1` use a delegated user token (`az account get-access-token --resource https://graph.microsoft.com`); creating a container fails with `403: Container creation by a public client is not allowed`.

**Date**: 2026-05-28

**Classification**: Gotcha (platform change since these scripts were last validated)

**What happened**: While bringing up the Spaarke AI Assistant in dev, needed to create a new SPE container. The canonical Spaarke scripts use a delegated user token via `az account get-access-token`. POST to `/v1.0/storage/fileStorage/containers` returned 403 with two distinct messages: first `accessDenied: Caller does not have required permissions for this API` (when the user's CLI token lacks `FileStorageContainer.Selected`), then `accessDenied: Container creation by a public client is not allowed` (when granted the scope via Connect-MgGraph). Microsoft now enforces confidential-client creation regardless of the calling user's scopes.

**Root cause**: Microsoft hardened the SPE Graph API to require a confidential client (one with a registered client secret or certificate) for container CRUD. The owning application is a confidential client with the AppRole grant on the container type; standard CLI / PnP / Connect-MgGraph paths are public clients.

**Fix (working pattern)**:
1. Retrieve the owning app's secret from Key Vault (`spaarke-spekvcert/spe-owning-app-secret`).
2. Acquire an app-only token via the client-credentials flow for the owning app.
3. POST `/v1.0/storage/fileStorage/containers` with that token (returns 201, status=inactive).
4. POST `/v1.0/storage/fileStorage/containers/{id}/activate` with same token (returns 204).

Full working snippet documented in [`docs/guides/HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md`](../docs/guides/HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md) "Creating a Container Manually" section.

**Prevention**:
- Update `Provision-Customer.ps1` Step 8 and `New-BusinessUnitContainer.ps1` to use the client-credentials flow with the owning-app secret. Until that's done, the inline snippet in HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md is the operational fallback.
- New env provisioning: ensure the owning app's secret is in Key Vault BEFORE attempting container creation.

**Evidence**: 2026-05-28 chat — Phase 2 of `projects/spaarke-ai-assistant-new-resources-r1/`. Created `Spaarke Dev Container 2` (id `b!vzGDfDpd7km_-_H38Q6ZfbotQXLPXF9Ci71VoQmIOHUKlvxOqBsHQLrROZ5KySLh`) successfully using the confidential-client flow.

### G-9: BFF AI Search has TWO index-name settings

**Title**: Pointing the BFF at a different AI Search index requires flipping BOTH `AiSearch:KnowledgeIndexName` and `Analysis:SharedIndexName` — flipping only one results in split-brain (reads from new index, writes to old).

**Date**: 2026-05-28

**Classification**: Gotcha (subtle config naming inconsistency)

**What happened**: During the cutover from `spaarke-knowledge-index-v2` to `spaarke-file-index`, set `AiSearch__KnowledgeIndexName=spaarke-file-index` on the BFF, restarted, uploaded a test document. The document was indexed to `spaarke-knowledge-index-v2`, not the new index. Diagnosis: the indexing path (`RagIndexingJobHandler`) reads `_analysisOptions.SharedIndexName`, while the search path (`RagService.SearchAsync`, etc.) reads `_aiSearchOptions.KnowledgeIndexName`. Two different options classes, two different config keys, but both name "the index for customer documents".

**Root cause**: Two options classes (`AiSearchOptions.KnowledgeIndexName` and `AnalysisOptions.SharedIndexName`) both refer to what should be the same index but bind from different config sections. Historical artifact of features added over time without consolidating the configuration model.

**Fix**: Set BOTH:
```bash
az webapp config appsettings set --name <app> --resource-group <rg> --settings \
  "AiSearch__KnowledgeIndexName=spaarke-file-index" \
  "Analysis__SharedIndexName=spaarke-file-index"
az webapp restart --name <app> --resource-group <rg>
```

Affected code paths:
- Reads: [`RagService.cs`](../src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs), [`SemanticSearchService.cs`](../src/server/api/Sprk.Bff.Api/Services/Ai/SemanticSearch/SemanticSearchService.cs) (use `AiSearchOptions.KnowledgeIndexName`)
- Writes: [`RagIndexingJobHandler.cs`](../src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/RagIndexingJobHandler.cs), [`RagEndpoints.cs`](../src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs) (use `AnalysisOptions.SharedIndexName`)

**Prevention**:
- Long-term: consolidate to a single canonical setting (likely `AiSearch__KnowledgeIndexName`) and deprecate `Analysis__SharedIndexName`.
- Short-term: always flip both. Any future env-provisioning runbook section that flips index names must include both.

**Evidence**: 2026-05-28 chat — Phase 3 of `projects/spaarke-ai-assistant-new-resources-r1/`. First flip left `sprk_searchindexname` writing to `spaarke-knowledge-index-v2`; second flip (including `Analysis__SharedIndexName`) fixed it.

---

### AP-5: AbortController in a useEffect whose deps include your own state transition

**Title**: A React `useEffect` that (a) starts a fetch with an `AbortController`, (b) dispatches its own `idle → loading` state transition, AND (c) has that state field in its dependency array — will abort its own in-flight fetch on the very next render. Idle-guard skips the restart. Fetch is stuck forever.

**Date**: 2026-07-03 (spaarke-dataset-grid-framework-r2 UAT §2.5)

**Classification**: Anti-pattern — the code LOOKS correct (deps-exhaustive, guarded, cleanup wired) but the interaction between "status is a dep" and "cleanup aborts controller" is silently wrong.

**What happened**: `ArrangeStep.tsx` had one useEffect with deps `[entityName, configState.status, savedQueriesState.status, authenticatedFetch]` that (1) dispatched configs → "loading", (2) kicked off both configs + savedqueries fetches with the same `AbortController`, and (3) returned `() => controller.abort()` as cleanup. Sequence:
1. Effect fires on mount → controller1 → both fetches dispatched with `controller1.signal`.
2. React re-renders (configs status "idle" → "loading" and savedqueries "idle" → "loading" batched).
3. Deps changed → cleanup fires → `controller1.abort()` → **both in-flight fetches cancelled mid-flight**.
4. Effect body re-runs. Guards `if (X.status !== "idle") return` skip both re-dispatches.
5. Both requests forever `"canceled"` in DevTools Network tab, status blank, response body "Failed to load response data".
6. UI: Available Views dropdown shows "loading" spinner or empty forever. No console errors.

**Root cause**: The exhaustive-deps rule is right in principle (any value read inside the effect should be listed), but when the effect DISPATCHES the state transition itself, listing that state creates a self-reference loop where cleanup is triggered by the state change you just made. AbortController cleanup then destroys the work you just started.

**Fix** (applied to `ArrangeStep.tsx:1130-1256`):
1. **Split into two useEffects** — one per fetch, each with its own AbortController. When configs completes, only its own effect's cleanup fires; savedqueries fetch continues undisturbed.
2. **Remove status from deps** — deps are now `[entityName, authenticatedFetch]` only. The idle-guard reads status from CLOSURE (which decides whether to START a fetch). Status transitions no longer trigger cleanup. Cleanup fires only on unmount or entity change — the correct times to abort a stale fetch.
3. **Use functional setState** (`onPickerCacheChange(prev => ...)`) to avoid the secondary race where the two effects' "loading" dispatches clobber each other.

Full annotated fix comment at `src/solutions/WorkspaceLayoutWizard/src/steps/ArrangeStep.tsx:1130-1174` explains the failure sequence.

**Prevention**:
- Whenever a useEffect (a) uses AbortController, (b) dispatches its own state transition, and (c) reads that state — **remove the state from deps** and read it from closure via a guard. Add an `eslint-disable-next-line react-hooks/exhaustive-deps` comment WITH a paragraph explaining why status is excluded.
- Never share one AbortController across multiple independent async operations in the same effect — split into separate effects with independent controllers.
- Diagnostic signature in DevTools Network tab: request shows `(canceled)`, status blank, response tab says "Failed to load response data" — indicates the fetch was aborted mid-flight, not that the endpoint rejected it. If you see this on a request that fires and never resolves, the caller has this AP-5 bug.

**Evidence**: commit `08bd41182` (initial round-1 attempt with counter); commit `803c77ace` (correct round-2 fix). `projects/spaarke-dataset-grid-framework-r2/notes/lessons-learned.md` UAT addendum §"first §5.5/5.6/3.3 fixes were wrong".

**Cross-references**:
- Skill: any skill that reviews React `useEffect` code should cross-check for this pattern.
- Related canonical React docs: [React docs — "You Might Not Need an Effect"](https://react.dev/learn/you-might-not-need-an-effect) — the broader principle is that state changes shouldn't trigger effects that fight against them.

---

### G-10: HTML5 DnD `dragEnter` fires on ancestors when preview extends beyond pointer

**Title**: Counter-based `dragEnter` / `dragLeave` pairing (increment on enter, decrement on leave, `isDragOver = counter > 0`) is a widely-cited pattern for HTML5 drag-drop — but it does NOT solve the case where the drag preview image extends beyond the pointer position and `dragEnter` fires on ancestor elements the pointer isn't actually inside. The correct approach is to hit-test the pointer coordinates against `getBoundingClientRect()` on every `dragOver`.

**Date**: 2026-07-03 (spaarke-dataset-grid-framework-r2 UAT §5.5)

**Classification**: Gotcha — the platform behavior is subtle and most tutorials teach the wrong pattern.

**What happened**: `ArrangeStep.tsx` `GridSlot` had a `dragOver`/`dragLeave` handler that set `isDragOver` on `dragOver`. User reported: "the row activates when the drag-block is too far away — I see the drop indicator light up on Row 3 while My Documents is at the bottom of the screen." First fix attempt was counter-based (dragEnter increments, dragLeave decrements). Still broke — because `dragEnter` was ALREADY firing on rows the pointer wasn't inside, just because the drag preview visually overlapped them.

**Root cause**: HTML5 DnD events fire based on complex compositing calculations that include the drag-preview element, not just pointer position. When a drag preview is large (like a section card ~200px × ~40px), the browser can fire `dragEnter` on elements the preview overlaps even when the pointer's `clientX/Y` is nowhere near them. Counter pairing doesn't help because the enter fires legitimately (from the browser's perspective).

**Fix** (applied to `ArrangeStep.tsx:698-742`):
```typescript
const handleDragOver = React.useCallback((e: React.DragEvent) => {
  e.preventDefault();
  e.dataTransfer.dropEffect = "move";
  const rect = (e.currentTarget as HTMLElement).getBoundingClientRect();
  const inside =
    e.clientX >= rect.left && e.clientX <= rect.right &&
    e.clientY >= rect.top  && e.clientY <= rect.bottom;
  setIsDragOver((prev) => (prev === inside ? prev : inside));
}, []);

const handleDragLeave = React.useCallback(() => {
  setIsDragOver(false);
}, []);

const handleDrop = React.useCallback((e: React.DragEvent) => {
  e.preventDefault();
  setIsDragOver(false);
  // Re-hit-test on drop — reject stray ancestor-dispatched drops.
  const rect = (e.currentTarget as HTMLElement).getBoundingClientRect();
  const inside = /* same check */;
  if (!inside) return;
  const sectionId = e.dataTransfer.getData("text/plain");
  if (sectionId) onDrop(slotId, sectionId);
}, [slotId, onDrop]);
```

Hit-testing on every `dragOver` guarantees `isDragOver` is true iff the pointer is literally inside the drop target's rect. The `dragLeave` handler still fires on rapid movement (belt-and-suspenders correctness). Drop event is also hit-tested to reject stray ancestor-dispatched drops.

**Prevention**:
- For any HTML5 DnD drop target where visual state depends on pointer position, hit-test `e.clientX/Y` against `e.currentTarget.getBoundingClientRect()` on `dragOver`. Do NOT rely on counter-based `dragEnter`/`dragLeave` alone.
- If a user reports "drop indicator activates on wrong element" or "activates far from pointer" — suspect the ancestor-dispatch behavior; go straight to hit-testing.

**Evidence**: commit `708f18bb7` (round-1 counter attempt); commit `803c77ace` (correct hit-test fix). `projects/spaarke-dataset-grid-framework-r2/notes/lessons-learned.md` §"first §5.5 fix was wrong".

**Cross-references**:
- Any drag-drop UI in `src/client/**` (compose editor drop zones, workspace tab reordering, todo-list drag reordering) should audit for this same pattern.

---

### G-11: `Xrm.Navigation.navigateTo({ target: 2 })` opens a separate window — cross-window signaling requires sessionStorage, not `window.*`

**Title**: A wizard opened via `Xrm.Navigation.navigateTo({ pageType: "webresource", ... }, { target: 2 })` runs in a **separate window** from the opener. `window.__dialogResult` (or any `window.*` global) written in the wizard's `handleFinish` cannot be read by the opener when the `navigateTo` Promise resolves.

**Date**: 2026-07-03 (spaarke-dataset-grid-framework-r2 UAT §3.3)

**Classification**: Gotcha — the `navigateTo` docs describe it as "opening a dialog", which suggests same-window overlay semantics. It's actually a separate window (`window.open` under the hood).

**What happened**: `WorkspaceLayoutWizard/App.tsx` `handleFinish` wrote `(window as any).__dialogResult = { confirmed: true, layoutId }` on successful save. `SpaarkeAi/WorkspacePaneMenu.tsx` `handleCreateWorkspace` awaited the `navigateTo` Promise, then read `window.__dialogResult` to auto-open the new workspace as a tab. Result: `dialogResult` was always `undefined` — the SpaarkeAi shell's `window` object was different from the wizard's `window` object. New workspace saved successfully but never opened.

**Root cause**: `Xrm.Navigation.navigateTo({ ... }, { target: 2 })` uses browser `window.open` semantics internally. The two windows share the same origin (both hosted as web resources) but are distinct execution contexts with distinct `window` objects. `window.*` globals do NOT cross the boundary.

**Fix** (applied to `App.tsx:717-731` + `WorkspacePaneMenu.tsx:571-597`):
Use `sessionStorage` as a shared per-origin per-tab-set bridge, with a timestamp-gated max age to prevent stale-result reuse:

```typescript
// In the wizard (writer side, after save)
window.sessionStorage?.setItem(
  "spaarke:workspace-wizard:last-result",
  JSON.stringify({ confirmed: true, layoutId, at: Date.now() })
);

// In the opener (reader side, after navigateTo Promise resolves)
const raw = window.sessionStorage?.getItem("spaarke:workspace-wizard:last-result");
if (!raw) return null;
const parsed = JSON.parse(raw);
if (Date.now() - parsed.at > 60_000) return null; // stale — ignore
// consume: remove immediately so re-cancel doesn't re-fire
window.sessionStorage?.removeItem("spaarke:workspace-wizard:last-result");
```

Full pattern documented at [`.claude/patterns/ui/navigateto-popup-result-bridge.md`](patterns/ui/navigateto-popup-result-bridge.md).

**Prevention**:
- Any wizard opened via `Xrm.Navigation.navigateTo` with `target: 2` that needs to signal a result back to its opener MUST use sessionStorage (or localStorage or `postMessage`), NOT `window.*` globals.
- The pattern is generic: same-origin cross-window ↔ pick a storage / messaging primitive; do NOT assume `window` state propagates.

**Evidence**: commit `708f18bb7` (round-1 `window.__dialogResult` attempt); commit `803c77ace` (correct sessionStorage bridge). `projects/spaarke-dataset-grid-framework-r2/notes/lessons-learned.md` §"first §3.3 fix was wrong".

**Cross-references**:
- Every Spaarke wizard: `CreateEventWizard`, `CreateMatterWizard`, `CreateProjectWizard`, `WorkspaceLayoutWizard`, `DocumentUploadWizard`, `PlaybookLibrary`, `AllDocuments`, `SummarizeFilesWizard`, `FindSimilar`, `CreateTodoWizard`, `CreateWorkAssignmentWizard`. If any of them uses `target: 2` and returns a result to its opener, audit for this same pattern.

---

### AP-7: Converting a silent fallback into fail-fast, verified with targeted tests only

**Date**: 2026-08-20 · **Class**: Anti-pattern · **Source**: `spaarke-auth-v4-dataverse-MI` task 010, caught at task 011

**What happened.** Task 010 fixed a real defect: `DataverseWebApiClient` selected its credential from
*secret presence* rather than from the `Graph:ManagedIdentity:Enabled` flag. Part of the fix replaced a
**silent fallback** (`no secret → quietly use DefaultAzureCredential`) with **fail-fast validation**
(`no secret and flag off → throw, naming the missing setting`). That is the right change — selecting a
credential by accident is exactly the defect being fixed.

It was verified with the new seam tests, a build, a publish-size measurement and a CVE scan. All green.
It shipped **13 failing contract tests**, found only when the *next* task ran the full suite.

**Root cause.** A test double had been passing config keys the class never reads
(`Dataverse:ClientId` instead of `API_APP_ID`), and worked *only* because the silent fallback caught it.
The general shape:

> Callers that depend on a silent fallback are, by definition, **not visible at the change site**.
> They are the ones that supplied nothing — so there is no reference, no call, no type dependency to
> grep for. A targeted test run selects tests *near the change*, which is precisely the set that
> excludes them.

**Prevention.** When a change converts a silent fallback / default / permissive branch into a throw,
a rejection, or a required value:

1. **Run the FULL test suite, not a targeted filter.** This is the whole rule. The blast radius is
   unbounded by construction and cannot be scoped by inspection.
2. Do not report the task verified on the strength of targeted tests + build + publish + CVE. That
   combination looks thorough and is blind to exactly this class.
3. If failures appear in a *later* task, **check whether they are yours before calling them
   pre-existing** — stash your changes and re-run the same filter on the clean baseline. "It fails on
   master too" and "it fails without my current edits" are different claims; only the first means
   pre-existing.

**Fix applied.** Set the double's config to declare its branch explicitly, then (per code review) gave
`DataverseWebApiClient` an optional `TokenCredential? credential = null` parameter — the shape its
sibling `DataverseAccessDataSource` already had — so doubles need no credential configuration at all
and cannot break again when tasks 020/022/033 rewrite either branch.

**Evidence**: `projects/spaarke-auth-v4-dataverse-MI/notes/decisions/011-adr009-token-cache-decision.md` §8.

---

### AP-8: A green suite treated as the END of verification rather than the start of it

**Title**: Every verification standard in routine use is a *consistency* check. None of them can detect a test that encodes the **wrong rule** — and the errors that shape is capable of hiding are invisible for exactly as long as the code is loudly broken.

**Date**: 2026-08-26 (`unified-access-control-r2`, Phase 0c parallel batch: tasks 072/073/075/079)

**Classification**: Anti-pattern (verification method insufficient for the claim being made of it)

**What happened**: Across one batch of four authorization tasks, six distinct defects were found in artifacts that **all** of the following reported as healthy: full suite green, ArchTest failure-count at the known baseline, publish size unchanged, CVE clean, and — critically — *perturbation-verified*.

| # | Defect | What reported healthy |
|---|---|---|
| 1 | A route re-introduction guard compared `RoutePattern.RawText` against literals pinning `{containerId}`. A re-add spelled `{id}` matches nothing → **guard passes while the vulnerable route is live**. `{id}` was verifiably the likeliest spelling (the surviving sibling and two architecture docs all use it). | tests green |
| 2 | Route-absence tests immune to parameter-name drift but **false-passing on URL-shape drift** — so the source-scanning rule was load-bearing while the test file's framing implied the behavioural tests were. | tests green |
| 3 | A test **asserted a guaranteed 25-record outage as intended behaviour**. The guard threw for every container, including the correct owner's; the test constructed exactly that shape and asserted the refusal. | tests green |
| 4 | Two regression tests **passed vacuously since the day they were written** — the double routed rows by `Flag == true` and *fell back to match-everything* when it found no `Like` condition. Green, fast, correctly named, asserting nothing. | tests green |
| 5 | An ADR-010 ratchet **consumed to exactly its ceiling** (153/153). Still passing, zero headroom — the next interface added anywhere in the BFF would fail the build blaming an unrelated project. | failure-count parity |
| 6 | A truncation refusal still vacuous after its own fix: the double ignored `TopCount`, so both tests rested on fixture size. | tests green + a perturbation |

**Root cause**: Two compounding causes.

*The methods are consistency checks.* A green suite proves the code does what its tests say. Failure-count parity proves no new failure. A perturbation proves a branch is **load-bearing**. None of the three can prove the tests say the right thing, and none can see a ratchet consumed but not breached.

*The ordering hides the rest.* In a four-round review of one component, rounds 1–2 found wrong **code** and rounds 3–4 found wrong **verification**. The verification errors were present from round 1 — two of the vacuous tests were written in round 2. They only became *findable* once the code stopped being loudly wrong: while code is obviously broken, a green test reads as "not yet reached" and a design-note sentence reads as "provisional." Both readings are true at the time, and **both stop being true silently**.

**Fix**:
- Perturb every guard **individually, never in batches** — batching is precisely how #4 hid behind a passing suite.
- **Read the build result before the test result.** `dotnet test` will reuse a stale assembly and report a false PASS; this happened three times in one batch (an `if (false)` perturbation failing CS0162 under warnings-as-errors, a filter-detach perturbation, and a dangling reference that still reported 32 green).
- Treat a **test double as primary subject matter**, not scaffolding. A double must *evaluate* the query's real conditions and **throw** on an unmodelled operator — never default permissive. (#4's double failed the same way task 070's production `default:` did.)
- For a ratchet, assert **headroom**, not parity. "9 failures = baseline" cannot see 153/153.
- **Re-read any design document written during the defect rounds.** Its claims were formed while the mechanism was still moving — which is how a wrong sentence in an escalation note survived two review passes and would have mis-decided an operator question while every test stayed green.
- Record what a test **cannot** falsify. A hand-written double pins the query against a model of the platform written by whoever wrote the query; it proves the code matches the double, never that either matches the platform. Six such claims were booked to a live-org task rather than left implicit.

**Prevention**: **"The suite is green" is where to START checking the verification, not where to stop.** When a change is security-relevant, budget a pass whose *subject* is the tests and the documents rather than the code — and expect it to find things, because in this batch it found as many defects as the code-focused passes did. A green suite and an accurate document are **separate claims**; neither implies the other.

**Evidence**: `projects/unified-access-control-r2/notes/wave2-parallel-merge-plan.md` (§3, §3b, §4a, §4b-0, §7b) · `notes/task-072-gate-share-link.md` §5 · `notes/task-075-*` §12 verification debt · commits `bb1e442ea` (072), `dd3e38f6d` (073), `8185c8fcc` (079), `3289844` (075).

---

### G-12: `dotnet test --no-build` runs a stale assembly while the build *truthfully* reports "up-to-date"

**Title**: A test run can report confident, precise results from a binary that does not contain your change — and unlike the usual "you forgot to build" case, the build output is **not lying**. It correctly reports "up-to-date" about a DLL that MSBuild has correctly concluded needs no rebuild, because the *timestamps it compares* are wrong.

**Date**: 2026-08-27 (`unified-access-control-r2`, task 011; fifth instance across two waves)

**Classification**: Gotcha — the standard defence ("read the build result before the test result") **does not catch this one**, which is exactly why it deserves its own entry rather than a line in AP-8.

**What happened**: Task 011 hit a test failure that **contradicted the source on disk** — the assertion that failed could not fail given the code in the file. Chasing it: the agent had restored a file from a backup copy with `Copy-Item`. `Copy-Item` **preserves `LastWriteTime`**, so the restored file's mtime moved *backwards* to the backup's creation time. MSBuild's incremental check compares source mtime against output mtime, decided the existing DLL was newer than its input, and skipped compilation — truthfully reporting "up-to-date". `dotnet test --no-build` then executed the previous assembly. Resolved with `touch` + rebuild (DLL advanced 23:28 → 23:43).

Separately and independently: **`dotnet build Spaarke.sln` did not refresh the BFF test project's output.** The test csproj had to be built explicitly.

**Root cause**: two distinct mechanisms that produce the same symptom.
1. **Backwards-moving mtime.** Any operation that preserves timestamps (`Copy-Item`, `cp -p`, `git stash` restores, archive extraction, some editor "revert file" paths) can make a source file *older* than the binary built from a previous version of it. Incremental build is a timestamp comparison; it has no content hash to fall back on.
2. **Solution-level build ≠ every project's output refreshed.** A `.sln` build refreshes what the solution graph says needs refreshing, which is not necessarily the specific test assembly you are about to run.

**Why the existing rule misses it**: the rule from the 075 batch is *"always read the build result before the test result."* That defends against a **failed or skipped** build being masked by a stale-but-green test summary. Here the build **succeeded** and said the honest thing. The falsehood is upstream, in the filesystem metadata — so no amount of reading build output detects it.

**Fix / detection** — check the artifact, not the log:

```bash
# Is the assembly actually newer than the source you just edited?
ls -l --time-style=full-iso path/to/Tests.dll path/to/EditedFile.cs
# DLL older than the .cs  =>  the test run you are about to trust is meaningless.

# Force it, then confirm the timestamp advanced:
touch path/to/EditedFile.cs
dotnet build path/to/The.Tests.csproj        # the TEST csproj, explicitly — not just the .sln
```

Prefer dropping `--no-build` entirely when a result is load-bearing. When restoring files, avoid timestamp-preserving copies — or `touch` every restored file immediately afterwards.

**Prevention**: **A perturbation is only evidence if the perturbation reached the binary.** This is the fifth stale-assembly incident in this project across two waves — 072 (a filter-detach perturbation reported 12/12 green off a stale BFF assembly), 075 (`if (false)` → CS0162), 075 again (a dangling reference made the test build **fail** while `--no-build` still reported 32 green), 018 (re-run), and now 011 (this mechanism). Every one produced a **confident and wrong** verification result.

That matters disproportionately because perturbation testing is the primary anti-vacuity tool (see [AP-8](#ap-8-a-green-suite-treated-as-the-end-of-verification-rather-than-the-start-of-it)): a stale assembly silently converts *"I proved this guard is load-bearing"* into *"this guard is untested"* — while looking identical. Treat "the perturbation bit" as a claim that requires the binary's timestamp as its receipt.

**Evidence**: `projects/unified-access-control-r2/notes/task-011-fetchxml-join-posture.md` · `notes/wave2-parallel-merge-plan.md` §A11 (five-instance inventory) · commit `15924623d` (011).
### AP-9: Amending a failing test to match the source

> **Date**: 2026-08-26 · **Class**: Anti-pattern · **Surfaced by**: `record-header-and-notepad-r2` (tasks 020 → 033 UAT)

**What happened**: A task found `XrmDataverseClient.test.ts` asserting `getEntityMetadata('sprk_event', ['Attributes'])` while the source called it with one argument. The agent changed the **test** to match the **source**, reported it as "fixed a pre-existing stale assertion", and the orchestrator accepted it. Two waves later, first UAT of the new control showed every field blank, every renderer defaulted to text, and a lookup emitted as a bare column name causing an HTTP 400. The failing test had been the only signal.

**Root cause**: A red test is a *disagreement* between two artifacts. Deciding the source is right because it is the source begs the question. Neither artifact is authoritative — the **vendor contract** is.

**Fix**: When a test and its source disagree, resolve against the third party: vendor documentation, the live endpoint, or an in-repo implementation already proven against production. Only then amend whichever is wrong. Record which evidence settled it.

**Prevention**: Treat "I fixed a stale assertion" in an agent report as a **claim requiring evidence**, not a completed chore. The report should name what the assertion was checked against. If the only justification is "the source does X", the check has not happened.

**Evidence**: `projects/record-header-and-notepad-r2/notes/decisions/033-def1-metadata-never-reached-resolver.md`. The correct resolution was that the *source* was wrong (see G-14) — the original test had been closer to right than the code.

---

### G-13: A Dataverse `$select` is all-or-nothing

> **Date**: 2026-08-26 · **Class**: Gotcha · **Occurrences**: 3 (`RS-1`, RecordHeader UAT, and the generic guard that closed it)

**What happened**: Three separate times, one invalid column name in a `useRecordFieldValues` `$select` produced HTTP 400 for the **whole request**, so every field came back null and the entire control rendered em-dashes. It presents as "the control is broken", not as "one field is wrong", which sends diagnosis in the wrong direction.

- **RS-1**: `sprk_mattersummary` had been deleted; the shipped Matter header stopped loading entirely.
- **RecordHeader UAT**: metadata failed to resolve, so a Lookup was emitted as its bare name (`sprk_projecttype_ref`) instead of `_sprk_projecttype_ref_value` → 400 → whole header blank.

**Root cause**: OData rejects the request, not the offending property. Two consequences compound it: a Lookup's OData property is `_<name>_value` (the bare logical name exists in *metadata* but is **not** a queryable entity property), and any upstream failure that degrades renderer derivation silently changes which key is emitted.

**Fix**: `useRecordFieldValues` now retries once with **no `$select`** on failure, returning the full row including every decorated `_<lookup>_value`. Degrading to "fetch more than we need" beats blanking the control.

**Prevention**: Never let a `$select` be assembled from names that a *derivation step* produced without a fallback. When adding or renaming a Dataverse column that any control selects, grep for the old name across `src/client/**` — a deleted column is a live outage, not a stale reference.

**Evidence**: `projects/record-header-and-notepad-r2/notes/rs1-hotfix-decision.md`; `notes/decisions/033-def1-metadata-never-reached-resolver.md`.

---

### G-14: `Xrm.Utility.getEntityMetadata` returns the Client API shape

> **Date**: 2026-08-26 · **Class**: Gotcha · **Surfaced by**: `record-header-and-notepad-r2` task 033 UAT

**What happened**: `projectAttribute` parsed the metadata payload assuming Web API shapes — a **string** `AttributeType` (`"Lookup"`) and an object `DisplayName`. It guarded with `if (typeof attributeType !== 'string') return 'String'`. Because the Client API returns a **number**, *every attribute of every entity* projected as `String` with no label. Downstream: all renderers fell back to text, labels showed humanized logical names, and lookups were emitted as bare column names (see G-13).

**Root cause**: Two different Microsoft surfaces return two different shapes for the same concept:

| | `Xrm.Utility.getEntityMetadata` (Client API) | Web API `EntityDefinitions` |
|---|---|---|
| `AttributeType` | **Number** (`AttributeTypeCode`, e.g. `6` = Lookup) | String (`"Lookup"`) |
| `DisplayName` | **String** | Object (`UserLocalizedLabel.Label`) |

**Fix**: Map the numeric `AttributeTypeCode` (mirror `XrmEnum.AttributeTypeCode` from `@types/xrm`) and accept a string `DisplayName`. Parse **both** shapes defensively — the same projection function is reachable from either transport.

**Prevention**: `Xrm.WebApi.retrieveMultipleRecords('EntityDefinition', …)` **can never succeed** — `Xrm.WebApi` resolves its first argument to an entity *set* name, and metadata entities are not entities (`SemanticSearchControl/services/DataverseMetadataService.ts:222` records the same finding). Metadata via `Xrm` means `Xrm.Utility.getEntityMetadata`; anything else needs a direct `fetch`, which is an NFR-05 decision, not an implementation detail.

**Caveat CLOSED 2026-08-26**: `@types/xrm` is not silent — it is explicit. `Metadata.AttributeMetadata` (the `getEntityMetadata` result) declares exactly six members: `DefaultFormValue`, `LogicalName`, `DisplayName`, `AttributeType`, `EntityLogicalName`, `OptionSet`. **No `Targets`, and no `Format`.** Both must come from the live form — `Xrm.Page.getControl(name).getEntityTypes()` and `Xrm.Page.getAttribute(name).getFormat()` (a documented STRING, `"date"` / `"datetime"`). Read the shipped `.d.ts` before inferring a platform payload from symptoms; it cost three UAT rounds here not to.

---

### G-15: A detached `Xrm` method loses `this` and dies inside the platform

> **Date**: 2026-08-26 · **Class**: Gotcha · **Surfaced by**: `record-header-and-notepad-r2` task 033 UAT round 4
> **Second occurrence.** R1 hit the identical trap on `Xrm.Navigation.navigateTo` and shipped four releases of a silent no-op before finding it.

**What happened**: `RecordHeaderLookupField` aliased the picker before calling it:

```ts
const lookupObjects = xrm?.Utility?.lookupObjects;   // ← detaches from `xrm.Utility`
const results = await lookupObjects({ ... });        // ← `this` is now undefined
```

Every click threw `TypeError: Cannot read properties of undefined (reading '_clientApiExecutor')` — Xrm's own internals dereference `this`. The lookup rendered its value and appeared merely read-only.

**Root cause**: Two compounding failures.

1. **The alias.** Xrm methods are not free functions; they are bound to their namespace object. Extracting one strips the receiver.
2. **A bare `catch {}` swallowed the TypeError**, so the failure was indistinguishable from "not wired". The component even documented the swallow as intentional ("preserve the no-throw contract").

**Fix**: call directly on the namespace — `await xrm.Utility.lookupObjects({ ... })`. Never `const f = xrm.X.y`. Where a no-throw contract is genuinely required, `console.warn` the error; never discard it.

**Prevention — the part that generalizes**: the unit suite passed throughout, because it mocked `lookupObjects` as a plain `jest.fn()`, which needs no receiver. **The mock was strictly more permissive than the thing it replaced, so the one property that mattered went untested.** When mocking a platform API, replicate its *requirements*, not just its signature — here, a `this`-sensitive mock that throws when the receiver is missing. Verified by reverting the fix: 3 of 19 tests fail on the old code, all 19 pass on the new.

---

### AP-10: A JSON-aware renderer that escapes one nesting level, over a config re-parsed deeper

> **Date**: 2026-09-01 · **Class**: Anti-pattern · **Surfaced by**: `email-communication-intelligence-r2` (Pillar B Outlook add-in UAT — every saved document stuck at `sprk_filesummarystatus = Failed`)
> **Full write-up**: [`docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md`](../docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md) Part 4. GitHub #919.

**What happened**: The "Document Profile" **playbook**'s Update Record node writes the AI summary back to `sprk_document`. Its stored `sprk_configjson` is the Playbook-Builder **wrapper format** — an outer JSON object whose `configJson` property is the *real* config encoded as a **JSON string** (`{"__canvasNodeId":…,"configJson":"{\"fieldMappings\":[{\"value\":\"{{output_aiAnalysis…}}\"}]}"}`). The Layer-1 template renderer (`PlaybookOrchestrationService.RenderConfigJsonStructurally`) is explicitly JSON-aware — it parses the config as a tree so that substituted values land in valid JSON. But it parses only the **outer** wrapper; the nested `configJson` is just a *string* to it. It flat-renders the multi-line AI summary into that string (raw `0x0A` newlines) and escapes them at the **outer** level only, so the outer stays valid. Then `UpdateRecordNodeExecutor.ParseConfig` unwraps via `GetString()` (decodes back to a raw newline) and **re-parses the nested string** → `JsonException: '0x0A' is invalid within a JSON string. Path: $.fieldMappings[0].value` → node fails → playbook stops → `Failed`.

**Root cause**: The renderer's JSON-awareness is **single-level**, but the data is **double-nested** (JSON-inside-a-JSON-string) and gets **re-parsed at the inner level** by a different component. Escaping at the level you parsed is necessary but not sufficient when a downstream consumer re-parses a deeper level you treated as opaque text. The two components each look correct in isolation; the defect is in the seam between them.

**Two wrong beliefs this corrected**:
1. *"The renderer is JSON-aware, so it can't emit invalid JSON."* — It can't emit an invalid **outer** document, but it says nothing about the validity of a **nested** JSON string it never descended into.
2. *"It falls back to flat substitution at `PlaybookOrchestrationService.cs:2284`."* — The prior checkpoint note asserted this confidently. **Wrong**: the outer wrapper *is* valid JSON, so the structural path runs and the `:2284` fallback never fires. A fix aimed at `:2284` would have missed entirely. The precise site was settled by pulling the **live** node config from Dataverse and matching `fieldMappings[0] = sprk_filesummary` to the observed `$.fieldMappings[0].value` error path — not by reading the renderer and reasoning forward.

**Fix (options, see the doc)**: make Layer 1 **wrapper-aware** (recurse into a nested string that is itself JSON-containing-a-template, so newlines escape at the nested level) — fixes Update Record / Create Task / Create Notification / Send Email at once; or converge the app-only path onto the direct-Action spine that has no config re-parse. **Not yet applied.**

**Prevention**: (a) When two components share a serialized payload across a boundary, ask *at how many levels does someone parse this?* and escape at each. (b) A **stored-config** test passing is not evidence the **rendered** config is valid — the bug lives only after substitution; test the render, or test end-to-end. (c) The same capability (document profiling) exists on two spines here — a node **playbook** and a direct **Action** — and only one had the bug; when a feature "works in one entry point and fails in another", suspect **two implementations**, not one flaky one.

**Known aliasing sites already carrying warning comments** (do not "simplify"): `useRecordHeaderToolbarActions.ts` (navigateTo), `RegardingResolverApp.tsx:1483`, `DailyBriefingApp.tsx:566`.

---

## How to use this catalog

1. **Before executing a skill**, the agent should mentally cross-reference: does this skill touch anything in the catalog?
2. **When a skill says "NEVER" or "ALWAYS"** with confidence, but the agent has no recent empirical verification, the agent should add a brief "verify" step (per AP-1's prevention).
3. **When a session surfaces a NEW cross-cutting failure pattern** — something that affects more than one skill, or recurs across different sessions — append an entry here. Use the same shape: title, date, class, what-happened, root-cause, fix, prevention, evidence.
4. **Bidirectional links**: each affected skill should have a `See FAILURE-MODES.md#<anchor>` pointer in its Gotchas section. (Phase 2b refinements will add these.)

---

### AP-11: Code that RUNS but reaches the wrong destination — no compiler and no test spans the seam

> **Added 2026-09-01** by `unified-access-control-r2`. **Class**: silent under-delivery across a
> language/process boundary. Three independent instances found in one sweep, all shipped, all
> user-visible, none with a test.

**What happened.** Three defects of one shape reached production:

| Instance | Ran fine, but | User saw |
|---|---|---|
| `bffUploadServiceAdapter.uploadFile` | POSTed to `/api/documents/upload`, a route the BFF serves at **no** group prefix | every external-user upload 404'd, for the life of the feature |
| `SummarizeFilesDialog` create-project | built `ProjectService` without `authFetch`/`bffBaseUrl`, so `provisionSecureProject` never ran — while `sprk_issecure = true` was written anyway and `sprk_containerid` cascaded from the SHARED business unit | a project **marked secure** whose documents land in the shared container, **with no warning** |
| SpeAdmin `ContainersPage.handleDelete` | made **no server call at all**; stripped rows from local state and reported success | *"N containers deleted (moved to Recycle Bin)"* — nothing was deleted; rows returned on refresh |

**Root cause — three reinforcing blind spots.**

1. **No compiler spans the seam.** TypeScript cannot see C# routes; C# cannot see TS string literals. A
   client URL is just a string, so a URL nobody serves compiles perfectly and passes every client test.
2. **Optional collaborators degrade silently.** `ProjectService(dataService, authFetch?, bffBaseUrl?)` —
   the security-relevant leg sits behind `if (authFetch && bffBaseUrl)`. One host wires three
   collaborators, another wires one, and the under-wired host **silently skips the behaviour** instead of
   failing. The tell is **optional constructor params / optional props**, and the shape is *"a component
   rendered by two hosts where one wires fewer collaborators."*
3. **The warning lived on the path that was bypassed.** The real wizard surfaces
   *"Secure Project provisioning failed…"*; the dialog that skipped provisioning also skipped the warning.
   **The error path and the happy path were both in the wrapper the caller went around.**

**Why nothing caught them.** No test asserted any of it. The container-step failure copy still said
*"no client-supplied ContainerId"* long after that field was deleted — **prose has no compiler**, so
comments and user-facing strings outlive the mechanisms they describe. And the `ContainersPage` comment
asserted *"speApiClient.containers does not currently expose a delete method… when the endpoint is added"*
while `POST /api/spe/bulk/delete` had been live and a **sibling component was already calling it**.

**Prevention.**
- **Client↔server route agreement is a structural fitness function, not a unit test.**
  `tests/Spaarke.ArchTests/SpeAdminClientRouteAgreementTests.cs` (task 092, found two live 404s) and
  `ClientUploadRouteAgreementTests.cs` (this entry) are the instrument. When adding a client that builds
  `/api/...` URLs, extend the census — **a guard scoped to one file is why the next file slips past.**
- **Resolve nested `MapGroup` prefixes** when checking route existence. Ignoring them produced **nine
  false mismatches** in a sibling project, and "fixing" the client would have broken four working surfaces.
- **A destructive or security-relevant action must not degrade quietly.** If required collaborators are
  absent, **warn or refuse** — never complete the non-security half and report success. Prefer required
  params over optional ones for the leg that enforces isolation.
- **An enqueue is acceptance, not completion.** Don't claim "deleted" for work you have not observed
  finish, and don't optimistically mutate local state before the server acts — that is what made a
  no-op look successful.
- **When deleting a field or route, grep the PROSE too** — doc comments, `<see cref=…>`, and user-facing
  message strings. The compiler updates call sites; it does not update the sentences that explain them.
- **Distrust a comment that explains why something isn't wired.** Both false premises here were
  load-bearing comments. Check the claim before inheriting it: in one case the endpoint existed and a
  sibling file was already using it.

**Also — the meta-lesson about the sweeps themselves.** A broad automated debt sweep found candidates but
graded them badly: its **#1 severity claim was wrong** (the sibling delete path *does* call the server), it
**missed** the upload 404 entirely, and it listed `SprkChatBridge` as dead when it is type-imported by three
live files — actioning that would have broken the shared-lib build. An **adversarial verification pass**
(default verdict NOT-DEAD, ten consumption channels incl. `React.lazy(() => import(...))`, ribbon XML,
`window.__X__` globals, string registries, PCF `dist` deep-imports) is what made the list safe to use.
Error rate: ~48 claims → 40 confirmed / 3 refuted-or-wrong / 4 undercounted / 3 correctly unsure.
**Treat a sweep as a lead list, never a work list.**

**Evidence**: `projects/unified-access-control-r2/notes/tech-debt-sweep-VERIFICATION-2026-09-01.md` ·
`notes/client-tech-debt-sweep-2026-09-01.md` · `notes/create-wizard-duplication-analysis.md` · fixes in
commit `304b6d8f2`; guard in `tests/Spaarke.ArchTests/ClientUploadRouteAgreementTests.cs`.

---

### AP-12: A comment becomes the constraint — prose outlives the mechanism it describes

> **Added 2026-09-02** by `unified-access-control-r2`. **Class**: documentation drift promoted to
> de-facto behaviour. **Eight instances in a single session**, two of which produced wrong answers to
> the owner, and one of which this project had *already flagged as wrong* and still acted on.
> Sibling of [AP-11](#ap-11-code-that-runs-but-reaches-the-wrong-destination--no-compiler-and-no-test-spans-the-seam),
> which notes "prose has no compiler"; this entry is that observation promoted to its own failure mode,
> because the consequence is not a wrong destination but a **wrong decision**.

**The shape.** Something is deleted, never built, or built differently. The compiler dutifully updates
every call site it can see. **Nothing updates the sentences that explain them.** The prose survives,
reads as authoritative, and the next reader — human or agent — treats it as the specification.

The tell is a comment that states **a limit, a route, a role mapping, a capability, or a reason
something isn't wired**. Those are exactly the claims that (a) cannot be checked by any compiler and
(b) get believed without checking.

**Worked instances (all real, all this session).**

| Prose said | Reality | Cost |
|---|---|---|
| `PathValidator.SmallUploadMaxBytes` enforces a 4 MiB upload cap | The constant had **zero code references**; the guard using it was deleted long before | A **real product limit**. Files 4 MiB–250 MB were refused by clients alone, for no server-side reason. Three separate client copies of the same fiction |
| `ISpeFileOperations`: the simple PUT "takes no `@microsoft.graph.conflictBehavior` — not rename, not fail" | The **REST API honours it**; only the Kiota SDK doesn't expose it | Drove a design conclusion twice — **including after this project had written down that the claim was false** |
| SPE permissions are "additive-only"; a misrouted write "cannot be retracted" / is "irreversible" | Permissions are **container-level** — removing the item ends the access | Wrong mental model in an arch guard's own failure message; invites hunting for a per-file ACL that does not exist |
| A hook's docstring named a privilege route and a compound role model | The route never existed; `/status` already returned the privilege, and the filter already mapped three roles | An **unnecessary escalation to the owner** for a decision that had already been made in code |
| `TokenProvider`: "authentication handled by browser session / Dataverse authentication" | Returns `''`, and the caller then omits the `Authorization` header entirely | Describes auth that **cannot work** against a `RequireAuthorization` BFF. Survived because the path has zero callers |
| A retirement note described a 4 MiB ceiling on a path it was itself deleting | Same phantom constant | Propagated the fiction into a *new* file while removing the old one |

**Why it is so durable.** Deleting code is loud — the build breaks. Deleting a *claim* is silent, so
nobody does it. Worse, prose accretes authority with age: a comment that has survived several refactors
reads as battle-tested rather than merely unexamined. And an agent reading a file top-to-bottom
encounters the comment **before** the code, so the claim frames the reading of the very evidence that
would refute it.

**Prevention.**
- **A doc comment stating a limit, route, role mapping, or capability is a CLAIM, not a fact.** Verify
  it against code before believing it — and *especially* before quoting it to a human. Two of the eight
  produced wrong answers to the owner.
- **A constant with zero references is not harmless.** Grep for references before treating any named
  limit as real; if it has none, the limit does not exist — delete it, and say in its place why it must
  not come back (see `PathValidator`, `UploadOperation`, `uploadOrchestrator`).
- **When you delete a field, route, guard, or constant, grep the PROSE in the same change** — doc
  comments, `<see cref=…>`, retirement notes, user-facing strings, and *test* comments. AP-11 says this
  too; it keeps being the step that is skipped.
- **Correct in place, and say the claim was wrong.** A silent rewrite lets the next reader re-derive the
  old belief from history. Leave a dated "🔴 Corrected — do not re-derive X" line. Both corrections in
  commit `524a32fd3` do this, precisely because one of them had been silently corrected before and came
  back.
- **Distrust your own project's notes at the same rate.** This project's handoff asserted a missing
  `encodeURIComponent` that was present two lines above the cited line, and a consolidation plan that
  would have replaced a working client with one that cannot authenticate. **Re-derive; never inherit a
  claim, including your own.**

**Evidence**: commits `4044286a6` (dead constant + 4 MiB client ceiling), `13d8b878a` (stale docstring),
`68eb58ad0` (phantom route in a docstring), `09025ab39` (third copy of the phantom cap), `524a32fd3`
(conflictBehavior claim + the additive-only framing, both corrected with do-not-re-derive notes) ·
`projects/unified-access-control-r2/current-task.md` § "the five things that will bite a fresh session".

---

*Established 2026-05-14 by project `ai-procedure-quality-r1` (task 013). Cross-reference: [.claude/CHANGELOG.md](CHANGELOG.md) for the entry stream.*
