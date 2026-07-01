---
description: Refresh the scope & model index from Dataverse so Claude Code has the latest catalog
tags: [ai, jps, playbook, scope, index, refresh]
techStack: [azure-openai, dataverse, powershell]
appliesTo: ["refresh scope index", "update scope catalog", "sync scope index", "refresh scopes"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-06-29
---

# jps-scope-refresh

> **Last Reviewed**: 2026-06-29
> **Reviewed By**: spaarke-ai-platform-unification-r7 task 074 (FR-33 — enum + schema rename touch-up). Two-authoring-surfaces table updated: legacy Node Type OptionSet (5 values) REMOVED; replaced with Executor Type Choice Set (`sprk_playbookexecutortype`, 33 values, global) — updated independently via Wave 8 task 081. C# enum `ActionType` references updated to `ExecutorType` per Wave 2 task 022 rename. Operational behavior (script invocation + JSON catalog shape) UNCHANGED — this is a terminology touch-up only. Prior review: ai-procedure-quality-r1 (Phase 2b Wave 2b-A).

## Purpose

**Tier 3 Operational Skill** — Regenerates `.claude/catalogs/scope-model-index.json` from current Dataverse state. This keeps Claude Code's scope catalog in sync after new actions, skills, knowledge, or tools are created.

**Why This Skill Exists**:
- Claude Code reads `scope-model-index.json` when designing playbooks (`jps-playbook-design`)
- If the index is stale, Claude Code won't know about newly created scopes
- Running the refresh manually is error-prone — this skill ensures consistency

## What This Skill DOES NOT Do (BINDING per canonical-truth loop 2026-06-26)

**The scope catalog is ADVISORY for authoring, not runtime enforcement.** Per [`docs/architecture/ai-architecture-playbook-runtime.md`](../../../docs/architecture/ai-architecture-playbook-runtime.md) §6:

- The BFF runtime does NOT cross-check that a node's action references a Skill in `playbook.scopes.skills`. Scope arrays are pre-fetch hints, not gates.
- This skill refreshes `.claude/catalogs/scope-model-index.json` — which is consumed by `jps-playbook-design` and the PlaybookBuilder UI dropdowns (Code Page) for **authoring hints**, not by the orchestrator at runtime.
- The model-driven app form's **Executor Type Choice Set** is a **separate authoring surface** (Dataverse-side) — this skill does NOT update that Choice Set. If a new executor is added to `INodeExecutor.cs` (and the C# `ExecutorType` enum + global `sprk_playbookexecutortype` Choice), the Choice Set must be updated independently via Dataverse customization (Wave 8 task 081 establishes the current contract — pre-R7 the surface used the now-removed Node Type OptionSet).

**Two authoring surfaces, one catalog** (updated 2026-06-29 per R7 FR-21 + FR-33):
| Surface | What it shows | Updated by |
|---|---|---|
| PlaybookBuilder UI dropdowns (Code Page) | actions/skills/knowledge/tools from scope-model-index.json | This skill (after Refresh-ScopeModelIndex.ps1) |
| **Executor Type Choice Set** on `sprk_playbooknode` (Model-Driven App form) | 33 Executor Type values from global `sprk_playbookexecutortype` Choice (mirrors C# `ExecutorType` enum at `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/ExecutorType.cs`) | Manual Dataverse customization — Wave 8 task 081 set the column default to AiCompletion=1; future executor additions follow the same path. NOT this skill. |

The legacy `sprk_nodetype` column + its 5-value Node Type OptionSet (AIAnalysis, Output, Control, Workflow, DeliverComposite) were REMOVED from `sprk_playbooknode` pre-R7. Any code or doc referencing them is stale — flag as drift.

If a UAT user reports "new executor missing from the form Choice Set," this skill cannot fix it — escalate to the Dataverse customizer (add the option value via `Add-NodeTypeChoiceOption.ps1` pattern, then update C# `ExecutorType` enum + INodeExecutor implementation).

**Brief R7 context — why renamed?** Wave 2 (FR-07 to FR-10, 2026-06-28) collapsed the legacy 3-layer dispatch model (`node.sprk_nodetype` → `Action.sprk_executoractiontype` INT → `Action.sprk_actiontypeid` lookup) to a single-hop `node.sprk_executortype` read in `PlaybookOrchestrationService.ExecuteNodeAsync`. The C# enum rename (`ActionType` → `ExecutorType`, Wave 2 task 022) + Dataverse Choice naming (`sprk_playbookexecutortype` global Choice with 33 values) aligns naming with reality. See R7 design.md §3.1 for the WHY history — the 3-layer drift caused every release to ship a different version of the same "wrong executor ran" bug.

## Applies When

- User says "refresh scope index", "update scope catalog", "sync scopes"
- Explicitly invoked with `/jps-scope-refresh`
- After running any `Seed-*.ps1` script that creates/updates scope records
- After manually creating scopes in the Dataverse UI
- Called automatically by `jps-action-create` (Step 6) and `jps-playbook-design` (Step 9.5)

---

## Workflow

### Step 0: Quick Schema Check via MCP (Optional, Recommended)

Before running the full refresh, use MCP tools to check what scope entities exist:

```
QUICK CHECK (fast — no script needed):
  mcp__dataverse__read_query() — SELECT sprk_name, sprk_code, statecode
    FROM sprk_analysisskill WHERE statecode = 0
  mcp__dataverse__read_query() — SELECT sprk_name, sprk_code, statecode
    FROM sprk_analysistool WHERE statecode = 0
  mcp__dataverse__read_query() — SELECT sprk_name, sprk_code, statecode
    FROM sprk_analysisknowledge WHERE statecode = 0
  mcp__dataverse__read_query() — SELECT sprk_name, sprk_code, statecode
    FROM sprk_analysisaction WHERE statecode = 0

COMPARE counts against current scope-model-index.json
  → If counts match: "Index appears current — skip refresh?"
  → If counts differ: "New scopes detected — proceed with full refresh"
```

This is useful for a quick sanity check without running the full PowerShell script.

### Step 1: Run Refresh Script

```
RUN:
  powershell -ExecutionPolicy Bypass -File scripts/Refresh-ScopeModelIndex.ps1 -Environment dev

EXPECTED OUTPUT:
  - Queries Dataverse for all active scope records
  - Merges with curated fields (tags, documentTypes, compatibleActions) from existing file
  - Preserves static sections (models, modelSelectionRules, compositions)
  - Writes updated scope-model-index.json
```

### Step 2: Verify Update

```
READ .claude/catalogs/scope-model-index.json

REPORT:
  "Scope index refreshed:
   Actions: {N} (was {prev})
   Skills: {N} (was {prev})
   Knowledge: {N} (was {prev})
   Tools: {N} (was {prev})
   New entries: {list any new codes}"
```

### Step 3: Commit Updated Index (if changes detected)

```
IF scope-model-index.json has changes:
  git add .claude/catalogs/scope-model-index.json
  git commit -m "chore(ai): refresh scope-model-index from Dataverse"

IF no changes:
  → "Scope index is already current — no changes needed."
```

---

## Conventions

- The index file lives at `.claude/catalogs/scope-model-index.json`
- The refresh script lives at `scripts/Refresh-ScopeModelIndex.ps1`
- Curated fields (tags, documentTypes, compatibleActions) are preserved across refreshes
- Static sections (models, modelSelectionRules, compositions) are never overwritten by refresh

---

## Error Handling

| Situation | Response |
|-----------|----------|
| Script not found | STOP — inform user to check `scripts/Refresh-ScopeModelIndex.ps1` exists |
| Dataverse auth fails | Suggest `az login` or `/dev-cleanup` |
| Index file not found | Script will create it fresh from Dataverse state |
| No changes detected | Report "index is current" — skip commit |

---

## Related Skills

- `jps-action-create` — Creates new scopes (calls this skill at Step 6)
- `jps-playbook-design` — Designs playbooks using the index (calls this skill at Step 9.5)
- `jps-validate` — Validates JPS definitions
- `dev-cleanup` — Fix auth issues if refresh fails

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Refresh succeeds but `scope-model-index.json` shows no new entries | Dataverse query filter excludes records that should be included (e.g., `statecode = 0` misses freshly-created inactive records) | Check the records' `statecode` directly in Dataverse. Either activate them, or temporarily adjust the script's filter. Re-run refresh. |
| Curated fields (`tags`, `documentTypes`, `compatibleActions`) get wiped on refresh | Refresh logic in `Refresh-ScopeModelIndex.ps1` not preserving curated fields correctly | The script SHOULD merge curated fields from the existing file. If wiped, restore from git history (`git show HEAD~1:.claude/catalogs/scope-model-index.json`) and re-apply curated fields manually. File a bug on the refresh script. |
| `jps-action-create` references a scope code that's in the index but not in Dataverse | Index drift after a scope was deleted from Dataverse without re-running refresh | Re-run refresh AFTER any scope-record deletion. The index is the source of truth FOR Claude Code; Dataverse is the source of truth FOR runtime — both must agree. |
| Singular vs plural Dataverse table-name confusion | `sprk_analysisaction` (this skill, singular logical name) vs `sprk_analysisactions` (used elsewhere with plural endpoint suffix) | This skill uses the **singular logical name** (`sprk_analysisaction`) — the Dataverse Web API expects the **plural collection name** (`sprk_analysisactions`) as the endpoint segment. Both are correct in context. Verify against `mcp__dataverse__list_tables()` output if in doubt. |

## Tips for AI

- This is a lightweight operational skill — run quickly, report results, move on
- Always commit the updated index so it's available in future sessions
- If called from within another skill (jps-action-create, jps-playbook-design), skip the commit — let the parent skill handle it
