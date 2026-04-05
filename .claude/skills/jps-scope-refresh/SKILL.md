# jps-scope-refresh

---
description: Refresh the scope & model index from Dataverse so Claude Code has the latest catalog
tags: [ai, jps, playbook, scope, index, refresh]
techStack: [azure-openai, dataverse, powershell]
appliesTo: ["refresh scope index", "update scope catalog", "sync scope index", "refresh scopes"]
alwaysApply: false
---

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

## Tips for AI

- This is a lightweight operational skill — run quickly, report results, move on
- Always commit the updated index so it's available in future sessions
- If called from within another skill (jps-action-create, jps-playbook-design), skip the commit — let the parent skill handle it
