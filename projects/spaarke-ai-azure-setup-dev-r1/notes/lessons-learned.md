# Lessons Learned — spaarke-ai-azure-setup-dev-r1

> **Project completed**: 2026-06-26
> **Branch**: `work/spaarke-ai-azure-setup-dev-r1`
> **Final commit count**: 28+ on branch ahead of master
> **Authored by**: spaarke-dev (Claude Code autonomous execution)

---

## 1. POML pre-audit grep would have saved hours

**What happened**: Group C (tasks 031-037) was scoped as 7 parallel mechanical refactors. On execution, 4 of 7 tasks (032, 033, 035, plus parts of others) were **zero-match no-ops** — the `spaarke-knowledge-index-v2` string had already been refactored to use the Options pattern (`_aiSearchOptions.KnowledgeIndexName`) in earlier R5 work. The POMLs were authored before Phase 2 atomic renames committed.

**Lesson**: Before executing a multi-task refactor phase, run a single grep audit:
```bash
grep -rn "<retired-name>" src/ --exclude="*.test.ts"
```
A 30-second audit at the start of Phase 4 would have:
- Identified which tasks were no-ops
- Saved 7 sub-agent dispatches worth of context
- Surfaced the structural DiscoveryIndexName refactor (task 031) much earlier

**Recommendation**: Add "Phase 4 pre-audit grep" as a generic step in `project-pipeline` Step 5.

---

## 2. POML scope is hypothesis, not contract

**What happened**: Task 031 POML said "Refactor RagService.cs, RagIndexingPipeline.cs, IndexRetrieveNode.cs — replace knowledge-v2 with files-index." Reality: zero references to that string + 5 CS0618 warnings flagging a STRUCTURAL dual-index code path (`DiscoveryIndexName` consumer pattern). The POML was a 30-second hypothesis written before deep code inspection.

This led to a **scope pivot mid-execution**: instead of "remove DiscoveryIndexName" per FR-14, the user decided to **reactivate `discovery-index` as canonical `spaarke-discovery-index`** (the runtime code path was always live; the 2026-06-25 retirement audit was wrong).

**Lesson**: POML scope is the starting hypothesis. When grep + code inspection contradicts it:
1. Stop. Escalate to user (CLAUDE.md §6 — scope expansion, breaking changes).
2. Present 2-3 concrete resolution options with cost estimates.
3. Reframe the spec FR if necessary (FR-14 was rewritten in commit `756a9c351`).

**The autonomous-mode rule**: if you can't articulate why the POML hypothesis is wrong + what the better hypothesis is, you're not ready to ask the user.

---

## 3. Schema property policy + Collection(Edm.ComplexType) sub-rule

**What happened**: Task 010 applied "default-enable-everything-Azure-allows" property policy uniformly across 7 schemas. This worked for 6 schemas but `spaarke-insights-index` failed deploy with HTTP 400: "field 'refType' cannot be enabled for sorting because it is directly or indirectly contained in a collection."

**Lesson**: NFR-09's "default-enable-everything-Azure-allows" stance has an Azure-restricted sub-rule:
> Fields inside `Collection(Edm.ComplexType)` parents CANNOT be `sortable=true` or `facetable=true` (Azure treats them as multi-valued).

**Recommendation**: Update `AI-SEARCH-INDEX-CATALOG.md` §2 Schema Property Policy table with a new row:
> | Field inside `Collection(Edm.ComplexType)` | per leaf — sortable/facetable Azure-forbidden | document with `// override-sortable-facetable` comment-key |

---

## 4. PowerShell ConvertTo-Json + single-element arrays = silent corruption

**What happened**: First implementation of `Deploy-AllIndexes.ps1`'s comment-key stripper used `ConvertFrom-Json | walk-and-strip | ConvertTo-Json -Depth 20`. Azure rejected EVERY deploy with HTTP 400: "null value found for the property named 'algorithms'". Root cause: PowerShell's `ConvertTo-Json` UNWRAPS single-element arrays into objects. `vectorSearch.algorithms` became `{...}` instead of `[{...}]`.

**Lesson**: For JSON manipulation where structural fidelity matters (arrays staying as arrays), prefer text-based regex over `ConvertFrom-Json + ConvertTo-Json` roundtrip. The roundtrip is lossy for single-element arrays.

```powershell
# WRONG — PowerShell roundtrip corrupts single-element arrays
$obj = Get-Content $file -Raw | ConvertFrom-Json
$obj.someField = "new value"
$obj | ConvertTo-Json -Depth 20  # arrays unwrap

# RIGHT — text-based regex preserves structure
$json = Get-Content $file -Raw
$cleaned = [regex]::Replace($json, 'pattern', 'replacement')
# JSON arrays stay arrays
```

The same lesson applies to ANY tool that parses + re-serializes JSON (PowerShell `ConvertTo-Json`, Newtonsoft.Json default settings, etc.).

---

## 5. NFR-14 Fixture-Config-FIRST protocol works

**What happened**: Task 042 ran the full test suite expecting potential DI-tightening regressions from FR-15 KV-ref migration (Redis project hit 337 such failures). Result: 1 unit test failure + 5 integration build errors. Per §F.2 protocol (Fixture-Config-FIRST), I stashed my changes + reran on baseline. ALL failures reproduced on baseline → confirmed pre-existing, NOT regressions from my work.

**Why FR-15 didn't trigger Redis-style failures**: my migration was at the App Service config layer (not C# DI registration layer). `AiSearchOptions` C# binding pattern unchanged (`IConfiguration.GetSection("AiSearch").Bind(options)`). The integration fixture already provided fake `AiSearch__*` config keys. Zero fixture changes required.

**Lesson**: §F.2 Fixture-Config-FIRST is the canonical defense against "my work broke the tests." Apply it ALWAYS:
1. Run failing tests on current state.
2. `git stash` my changes.
3. Run failing tests on baseline.
4. If failure reproduces on baseline → NOT a regression. Document + file as backlog.
5. Only if failure is unique to my changes → investigate as a real regression.

---

## 6. Healthz 200 is not the only deployment-success signal

**What happened**: `bff-deploy` skill's canonical verification rule: **"any endpoint behind `.RequireAuthorization()` should return 401 without a token. If it returns 404, the route didn't register (incomplete deployment)."**

In task 054 I tested 5 endpoints:
- `/api/ai/search` → 401 ✅
- `/api/ai/knowledge/test-search` → 401 ✅ (POML had wrong path; grep BFF source for correct path)
- `/api/insights/search` → 401 ✅
- `/api/insights/ask` → 401 ✅

All routes registered. If ANY returned 404 with healthz 200, that would have indicated incomplete deployment (the file-lock silent-failure case documented in `FAILURE-MODES.md` G-2). The hash-verify + 401-not-404 two-layer safety net both worked.

**Lesson**: When verifying a BFF deploy, the "deploy success" signal is **healthz 200 + sample-endpoint 401 (not 404)**. healthz alone is insufficient.

---

## 7. Audit list trust ≠ runtime reality

**What happened**: The 2026-06-25 "12 indexes audit" classified `discovery-index` as retired ("never wired into runtime; no live writer or query path found"). Task 031's runtime grep found the OPPOSITE: `RagIndexingPipeline.IndexDocumentAsync` actively writes to BOTH indexes; `RagService.GetIndexHealthAsync` queries BOTH; `KnowledgeIndexHealthResult` exposes BOTH names on the BuilderAdmin API contract.

**Lesson**: Document audits are point-in-time hypotheses. Before treating something as "retired", verify at runtime:
- Grep for the symbol/string usage in source
- Check for live API contract surfaces exposing it
- Inspect actual runtime traffic if production data exists

The catalog's retired-indexes appendix should require evidence files cited per entry (e.g., "verified via grep on commit X").

---

## 8. Two comment-key conventions in Spaarke schemas

**Discovery**: Spaarke schema JSON files use TWO comment-key conventions:
- `"// rationale": "<text>"` — most schemas (files-index, records-index, rag-references, etc.)
- `"_comment_": "<text>"` — insights-index only (Bicep-influenced, underscore-wrapped key)

**Lesson**: The Deploy-AllIndexes.ps1 stripper needed to handle both regex patterns. This represents architectural inconsistency. Future schema authoring should standardize on `// rationale` (more common pattern).

---

## 9. Stale-key cleanup in App Service config has security implications

**Discovery during task 041**: The dev BFF's App Service config had:
- `DocumentIntelligence__AiSearchKey = 1GpyI95Hi...` (plaintext STALE pre-2026-06-25 admin key)
- `RecordSync__AiSearchApiKey = 1GpyI95Hi...` (same stale key)
- `AiSearch__ReferencesApiKey = @Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AzureAISearchApiKey` (MISSING closing `)` — App Service treated as literal string)

The stale plaintext keys were leaked visibly in App Service config (anyone with `Microsoft.Web/sites/config/read` could see them). They had been STALE since the 2026-06-25 service recreate — BFF code reading those settings was effectively broken.

**Lesson**: When a service is recreated (admin key rotation), audit ALL App Service settings carrying that key:
- KV-ref settings → automatically refresh via vault
- HARDCODED settings → become STALE silently; BFF calls via those paths break
- BROKEN KV-refs (truncated syntax) → return literal `@Microsoft.KeyVault(...)` text → BFF treats as malformed endpoint/key

**Recommendation**: Add quarterly KV-ref audit to operational runbook (`docs/guides/ai-search-azure-setup.md`).

---

## 10. Project pivot mid-execution is OK if escalated properly

**The FR-14 reframe** (2026-06-26 user decision) converted "remove DiscoveryIndexName property" into "rename + reactivate as canonical `spaarke-discovery-index`". This was a substantial scope change discovered mid-Phase 4 via the structural-refactor escalation.

**What worked**:
- Escalated to user via `AskUserQuestion` with 3 concrete options (A/B/C/D) + rationale + cost estimate
- User chose new Option D ("build the discovery index if provided for in BFF; use canonical naming")
- Implemented atomically: new schema file + AiSearchOptions update + Deploy-AllIndexes catalog + catalog doc move from §5 to §4 + operator guide update + spec FR-14 reframe — all in one commit (`756a9c351`)
- Updated downstream tasks (046 step 10 retired) + project-level docs (count 7→8 indexes everywhere)

**Lesson**: Mid-project scope changes are normal in autonomous AI execution. The discipline is:
1. Surface ambiguity EARLY (don't push through)
2. Present options with cost (not just "what do you want?")
3. Implement atomically with cascading updates
4. Document the pivot in spec + decisions log

---

## Cross-References to Evidence Files

- `notes/pre-phase-3-verification.md` — 10-check pre-flight (FR-21)
- `notes/group-c-disposition.md` — Phase 4 Group C disposition (4 of 7 no-ops + structural escalation)
- `notes/kv-migration-verification.md` — Task 041 KV-ref migration (stale keys + broken parenthesis)
- `notes/test-fixture-sweep.md` — Task 042 NFR-14 (§F.2 protocol applied; pre-existing failures filed)
- `notes/phase-4-final-verification.md` — Tasks 045 + 046 (publish-size + grep)
- `notes/phase-5-deploy-evidence.md` — Task 050 (deploy 8 schemas + 3 script bugfixes)
- `notes/phase-5-ingestion-evidence.md` — Tasks 051 + 052 (194 docs ingested + FR-17 verified)
- `notes/phase-5-functional-verification.md` — Task 054 (5-endpoint smoke + REST proxy)

---

## Handoff to `spaarke-environment-factory-r1`

This project delivers the prerequisites the future environment-factory project needs:

| Deliverable | Location | Factory-r1 usage |
|---|---|---|
| Canonical 8-index catalog | `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` | Factory iterates §4 entries to deploy per-env |
| Operator runbook | `docs/guides/ai-search-azure-setup.md` | Factory consumes §2 step-by-step procedure |
| Unified deployer | `scripts/ai-search/Deploy-AllIndexes.ps1` | Factory invokes with `-Environment staging|prod|demo` (NFR-05 Force gate enforced) |
| KV-ref pattern | `notes/kv-migration-verification.md` | Factory replicates 7-setting cutover per new env |
| Index property policy | `AI-SEARCH-INDEX-CATALOG.md` §2 + this doc §3 | Factory enforces via post-deploy verifier (NFR-02) |
| Schema files | `infrastructure/ai-search/*.json` (8 files) | Factory references by canonical path |

Per `spec.md`: this project is the environment-factory's prerequisite, not part of it. Factory-r1 inherits all 8 deliverables above.

---

*Lessons v1.0 — 2026-06-26. Cross-reference for future Spaarke infrastructure work.*
