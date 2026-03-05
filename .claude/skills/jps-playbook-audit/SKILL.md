# jps-playbook-audit

---
description: Audit existing playbooks against current scope catalog and standards, then recommend or apply updates
tags: [ai, jps, playbook, audit, compliance, scope]
techStack: [azure-openai, dataverse, powershell]
appliesTo: ["audit playbooks", "review playbooks", "check playbook compliance", "update playbooks"]
alwaysApply: false
---

## Purpose

**Tier 2 Orchestrator Skill** — Reviews all existing playbooks in Dataverse against the current scope catalog (`scope-model-index.json`) and platform standards. Identifies gaps, recommends improvements, and optionally applies updates.

**Why This Skill Exists**:
- As new scopes (actions, skills, knowledge, tools) are added, existing playbooks don't automatically benefit
- Model selection rules evolve — older playbooks may use suboptimal or deprecated models
- Scope associations may be incomplete (e.g., a contract review playbook missing a newly created "Termination Analysis" skill)
- Without systematic auditing, playbooks drift from current best practices

## Applies When

- User says "audit playbooks", "review playbooks", "check playbook compliance", "update playbooks"
- Explicitly invoked with `/jps-playbook-audit`
- After adding new scopes to the catalog (post `/jps-scope-refresh`)
- After platform upgrades that change model availability or standards
- Periodic maintenance review

---

## Workflow

### Step 1: Load Current Standards

```
LOAD scope-model-index.json:
  - docs/ai-knowledge/catalogs/scope-model-index.json

EXTRACT:
  - All available actions (codes, names, documentTypes, tags)
  - All available skills (codes, compatibleActions)
  - All available knowledge sources (codes, tags)
  - All available tools (codes, handlers)
  - Model selection rules (taskType → model mapping)
  - Known compositions (PB-001 through PB-010)
```

### Step 2: Query Existing Playbooks from Dataverse

```
AUTHENTICATE:
  az account get-access-token --resource https://spaarkedev1.crm.dynamics.com

QUERY playbooks:
  GET /api/data/v9.2/sprk_analysisplaybooks
    ?$select=sprk_name,sprk_description,sprk_analysisplaybookid,sprk_ispublic,createdon,modifiedon
    &$filter=statecode eq 0
    &$orderby=sprk_name

FOR EACH playbook:
  QUERY nodes:
    GET /api/data/v9.2/sprk_playbooknodes
      ?$select=sprk_name,sprk_nodetype,sprk_outputvariable,sprk_dependsonjson,sprk_configjson
      &$filter=_sprk_playbookid_value eq '{playbookId}'
      &$expand=sprk_ActionId($select=sprk_actioncode,sprk_name),
               sprk_ModelDeploymentId($select=sprk_name)

  QUERY node scope associations:
    GET sprk_playbooknodes({nodeId})/sprk_playbooknode_skill
    GET sprk_playbooknodes({nodeId})/sprk_playbooknode_knowledge
    GET sprk_playbooknodes({nodeId})/sprk_playbooknode_tool

  QUERY playbook-level scope associations:
    GET sprk_analysisplaybooks({playbookId})/sprk_analysisplaybook_action
    GET sprk_analysisplaybooks({playbookId})/sprk_playbook_skill
    GET sprk_analysisplaybooks({playbookId})/sprk_playbook_knowledge
    GET sprk_analysisplaybooks({playbookId})/sprk_playbook_tool
```

### Step 3: Run Compliance Checks

```
FOR EACH playbook:
  CHECK 1 — Scope Coverage:
    FOR each node:
      - Does the action code exist in scope-model-index.json?
      - Are all associated skills compatible with the action? (check compatibleActions)
      - Are there NEW skills in the index that are compatible but not associated?
      - Are there NEW knowledge sources relevant to this document type but not associated?
      - Are there NEW tools that could enhance this node?

  CHECK 2 — Model Optimization:
    FOR each node:
      - What task type does this node perform? (classify, extract, analyze, summarize)
      - What model is currently assigned?
      - What does modelSelectionRules recommend for this task type?
      - Is there a cost savings opportunity? (e.g., gpt-4o used for classification → should be gpt-4o-mini)

  CHECK 3 — Structural Integrity:
    - Do all nodes have an action assigned?
    - Are dependsOn references valid (no orphaned nodes)?
    - Are outputVariable names unique within the playbook?
    - Is there a canvas layout saved?

  CHECK 4 — Standards Compliance:
    - Does the playbook have a description?
    - Is it marked public/private appropriately?
    - Are there fewer than 10 nodes? (recommend splitting if more)
    - Does each node have 2-4 skills? (not overloaded, not empty)

  CLASSIFY each finding:
    🔴 ERROR: Broken reference, missing action, invalid scope code
    🟡 WARNING: Suboptimal model, missing recommended scope, > 8 nodes
    🟢 SUGGESTION: New scope available that could enhance analysis
    ✅ PASS: Compliant with current standards
```

### Step 4: Generate Audit Report

```
PRESENT audit report using AskUserQuestion or direct output:

  ═══════════════════════════════════════════
  PLAYBOOK AUDIT REPORT
  Date: {timestamp}
  Playbooks audited: {count}
  Scope catalog version: {$generated date from index}
  ═══════════════════════════════════════════

  📊 Summary:
    ✅ Compliant: {count} playbooks
    🟡 Warnings: {count} playbooks
    🔴 Errors: {count} playbooks

  ───────────────────────────────────────────
  PLAYBOOK: {name} ({guid})
  Status: 🟡 {N} warnings, {M} suggestions
  ───────────────────────────────────────────

  Nodes: {count}
  Last modified: {date}

  Findings:
    🟡 Node "Classify Document" uses gpt-4o but modelSelectionRules
       recommends gpt-4o-mini for classification tasks
       → Potential savings: ~$5/1M tokens

    🟢 New skill available: SKL-009 (Termination Analysis)
       is compatible with ACT-001 (Contract Review) used by
       node "Deep Contract Review" but not currently associated
       → Would add termination clause analysis capability

    🟢 New knowledge source available: KNW-010 (Red Flags Catalog)
       relevant to document type "Contract" but not associated
       → Would enhance risk detection

    ✅ Structural integrity: PASS
    ✅ Canvas layout: saved

  ───────────────────────────────────────────
  [... repeat for each playbook ...]

  ═══════════════════════════════════════════
  RECOMMENDED ACTIONS:
  1. Update model assignments for 3 nodes (est. 40% cost reduction)
  2. Add SKL-009 to 2 playbooks with contract analysis
  3. Add KNW-010 to 4 playbooks for enhanced risk detection
  ═══════════════════════════════════════════
```

### Step 5: User Decision

```
USE AskUserQuestion:
  "Audit complete. What would you like to do?"

  Options:
  1. Apply all recommendations automatically
  2. Apply specific recommendations (I'll choose)
  3. Export report only (no changes)
  4. Review individual playbooks in detail
```

### Step 6: Apply Updates (if approved)

```
IF user approves updates:

  FOR EACH approved recommendation:
    CASE "Add scope association":
      POST sprk_playbooknodes({nodeId})/sprk_playbooknode_skill/$ref
        Body: { "@odata.id": "sprk_analysisskills({skillGuid})" }

    CASE "Update model assignment":
      PATCH sprk_playbooknodes({nodeId})
        Body: { "sprk_ModelDeploymentId@odata.bind": "sprk_aimodeldeployments({modelGuid})" }

    CASE "Remove deprecated scope":
      DELETE sprk_playbooknodes({nodeId})/sprk_playbooknode_skill({skillGuid})/$ref

  REPORT:
    "Updates applied:
     - {N} scope associations added
     - {M} model assignments updated
     - {K} deprecated scopes removed
     - {J} playbooks modified"
```

### Step 7: Post-Audit Refresh

```
RUN: Refresh-ScopeModelIndex.ps1 (if any new scopes were created during remediation)

COMMIT audit report if saved to file:
  projects/{project}/notes/playbook-audit-{date}.md
```

---

## Audit Modes

| Mode | Trigger | Scope | Updates |
|------|---------|-------|---------|
| **Full Audit** | `/jps-playbook-audit` | All playbooks | Interactive — user approves each change |
| **Single Playbook** | `/jps-playbook-audit {name}` | One playbook | Detailed node-by-node analysis |
| **Scope Impact** | `/jps-playbook-audit --scope SKL-009` | Playbooks affected by a specific scope | Shows which playbooks should get the new scope |
| **Model Optimization** | `/jps-playbook-audit --models` | All playbooks | Focus only on model assignments and cost savings |
| **Dry Run** | `/jps-playbook-audit --dry-run` | All playbooks | Report only, no changes offered |

---

## Conventions

- Audit reports saved to `projects/{project}/notes/playbook-audit-{YYYY-MM-DD}.md`
- Never remove scope associations without user confirmation
- Model changes require user approval (cost vs quality tradeoff)
- New scope suggestions are always 🟢 (informational), never auto-applied
- Broken references (🔴) should be flagged for immediate attention

---

## Error Handling

| Situation | Response |
|-----------|----------|
| scope-model-index.json not found | STOP — run `/jps-scope-refresh` first |
| Dataverse connection fails | Suggest `az login` or `/dev-cleanup` |
| Playbook has no nodes | Flag as 🔴 ERROR — empty playbook |
| Action code not in index | Flag as 🔴 — action may be deleted or renamed |
| Scope code in playbook but not in index | Flag as 🟡 — scope may be new and index needs refresh |
| Too many findings (>50) | Summarize by category, offer detailed view per playbook |

---

## Related Skills

- `jps-playbook-design` — Create new playbooks (this skill reviews existing ones)
- `jps-scope-refresh` — Refresh the scope index before auditing
- `jps-action-create` — Create missing actions identified during audit
- `jps-validate` — Validate JPS definitions for actions used by playbooks

---

## Tips for AI

- Always run `/jps-scope-refresh` before a full audit — stale index produces false positives
- Group findings by playbook for readability, not by finding type
- Lead with the most impactful recommendations (cost savings, missing critical scopes)
- When suggesting new skills, only recommend those where `compatibleActions` includes the node's action
- Model optimization is the highest-ROI recommendation — always calculate estimated savings
- For scope impact mode (`--scope`), show the delta clearly: "3 of 8 contract playbooks already have this skill"
- Never auto-apply 🔴 ERROR fixes — these may indicate data issues requiring investigation
- Keep audit reports concise — use tables for multi-playbook summaries
