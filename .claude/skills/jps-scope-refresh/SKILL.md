---
description: Refresh the scope & model index from Dataverse so Claude Code has the latest catalog
tags: [ai, jps, playbook, scope, index, refresh]
techStack: [azure-openai, dataverse, powershell]
appliesTo: ["refresh scope index", "update scope catalog", "sync scope index", "refresh scopes"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-05-16
---

# jps-scope-refresh

> **Last Reviewed**: 2026-05-16
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2b-A)
> **Exemplar rationale**: The contract is "run the script, verify counts changed, commit if changed." There is no useful canonical reference output — the catalog content itself changes with every scope addition.

## Purpose

**Tier 3 Operational Skill** — Regenerates `.claude/catalogs/scope-model-index.json` from current Dataverse state. This keeps Claude Code's scope catalog in sync after new actions, skills, knowledge, or tools are created.

**Why This Skill Exists**:
- Claude Code reads `scope-model-index.json` when designing playbooks (`jps-playbook-design`)
- If the index is stale, Claude Code won't know about newly created scopes
- Running the refresh manually is error-prone — this skill ensures consistency

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
