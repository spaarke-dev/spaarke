---
description: Audit existing playbooks against current scope catalog and standards, then recommend or apply updates
tags: [ai, jps, playbook, audit, compliance, scope]
techStack: [azure-openai, dataverse, powershell]
appliesTo: ["audit playbooks", "review playbooks", "check playbook compliance", "update playbooks"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-06-29
---

# jps-playbook-audit

> **Last Reviewed**: 2026-06-29
> **Reviewed By**: spaarke-ai-platform-unification-r7 task 072 (FR-32 — node-first audit). New Check 3.6 enumerates 7 R7 dispatch-drift patterns (A-G) mapped to the same shape produced by Wave 5 task 050's `Review-PlaybookNodes-Dispatch.ps1` (94-node CSV). Step 2 deployed-node query updated: `sprk_nodetype` (legacy, column removed pre-R7) → `sprk_executortype` (Choice, single source of dispatch identity). Check 3.5 dispatch-axis citation refreshed (FK is NO LONGER the canonical dispatch axis post-R7 single-hop refactor; `node.sprk_executortype` is). Prior review: ai-procedure-quality-r1 (Phase 2b Wave 2b-A).

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
  - .claude/catalogs/scope-model-index.json

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
      ?$select=sprk_name,sprk_executortype,sprk_outputvariable,sprk_dependsonjson,sprk_configjson,sprk_isactive
      &$filter=_sprk_playbookid_value eq '{playbookId}'
      &$expand=sprk_ActionId($select=sprk_actioncode,sprk_name),
               sprk_ModelDeploymentId($select=sprk_name)

    NOTE (R7, 2026-06-29): the legacy `sprk_nodetype` column was REMOVED from
    `sprk_playbooknode` pre-R7. Selecting it will 400. `sprk_executortype`
    (Choice, 33 values) is the sole node-level dispatch signal. If an audited
    playbook was deployed BEFORE the schema migration, the row will still
    exist but `sprk_executortype` may be NULL — Check 3.6 below flags that.

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

  CHECK 3.5 — Repo-vs-Deployed Reconciliation (BINDING per canonical-truth loop 2026-06-26)
    Per ai-guide-playbook-deploy-recipe.md, the runtime reads sprk_playbooknode rows, NOT canvas JSON.
    Audit MUST surface drift between repo definition files and deployed rows.
    - For each playbook with a repo-side definition JSON at projects/{project}/notes/playbooks/{name}.json:
      a. COMPARE definition.nodes.length vs deployed sprk_playbooknode row count
         FLAG 🔴 if mismatch (deploy gap — runtime will hit Legacy mode if count is 0)
      b. For each repo node: COMPARE repo.actionCode vs deployed _sprk_actionid_value
         RESOLVE deployed FK via sprk_analysisaction lookup → sprk_actioncode
         FLAG 🔴 if actionCode mismatch (Action FK carries SystemPrompt + OutputSchema + Temperature for prompt-driven executors; mismatch means the node runs the wrong prompt template. Action FK is NO LONGER the dispatch axis post-R7 — `node.sprk_executortype` Choice owns dispatch identity per design.md §2 Invariant 2; the previously-referenced `ai-architecture-playbook-runtime.md` §5 lookup-precedence section was DELETED in Wave 6 task 061)
      c. For each deployed node: VERIFY sprk_isactive=true
         FLAG 🔴 if false (load-bearing per Deploy-Playbook.ps1:823 comment;
         the column's default is false → row exists but runtime filter excludes it → Legacy mode)
    - Check for ORPHANED nodes (sprk_playbooknode rows where playbook was deleted but nodes remain)
      QUERY: sprk_playbooknodes where _sprk_playbookid_value not in (active playbook IDs)
      FLAG 🔴 if any found — these consume storage + may surface in stale queries

  CHECK 3.6 — R7 Dispatch-Drift (BINDING per spec FR-32, added 2026-06-29 by task 072)
    Mirrors the shape produced by Wave 5 task 050's `Review-PlaybookNodes-Dispatch.ps1`. Surface every node carrying drift from the single-hop dispatch model. Seven drift patterns:

    A. `sprk_executortype` is NULL or absent on the node row
       → 🔴 ERROR — runtime cannot dispatch; `PlaybookOrchestrationService.ExecuteNodeAsync` will throw "executor unknown". Recommended action: use Wave 5 owner-review CSV workflow to backfill.

    B. Any code/script still references `sprk_playbooknode.sprk_nodetype` for read or write
       → 🔴 ERROR — column was REMOVED from schema pre-R7. Will produce 400 at runtime. Recommended: replace with `sprk_executortype`.

    C. Any code/script still reads `sprk_analysisaction.sprk_actiontypeid` lookup as a DISPATCH signal
       → 🔴 ERROR — column was DROPPED in Wave 4 task 043 (2026-06-29). The lookup table (`sprk_analysisactiontype`) remains per FR-05 as decorative maker categorization, but no runtime code reads it for dispatch. Browse-only / display-name uses are fine if the column still existed; it doesn't.

    D. Any code/script reads `sprk_analysisaction.sprk_executoractiontype` INT column
       → 🔴 ERROR — column was DROPPED in Wave 4 task 044 (2026-06-29). Any read returns 400.

    E. Prompt-driven node (executor type ∈ {AiAnalysis=0, AiCompletion=1, AiEmbedding=2}) has NULL Action FK
       → 🔴 ERROR — executor's `Validate()` will fail at runtime ("Action FK required for prompt-driven executor"). Recommended: pick or author an Action row via `jps-action-create`.

    F. Pure executor (Condition, Start, ReturnResponse, EntityNameValidator, UpdateRecord, SendEmail, CreateTask, CallWebhook, SendTeamsMessage, etc. — anything NOT in the prompt-driven trio) has NON-NULL Action FK
       → 🟡 WARNING — Action FK is unused at runtime for pure executors. Not load-bearing but suggests author confusion about the R7 model. Recommended: NULL out the FK.

    G. Compose strategy + scope hints on multi-output playbooks
       → ✅ unchanged by R7 — verify ADR-037 compose strategy untouched.

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

## The R7 dispatch model — what this audit checks (and why)

> **Read this once.** It anchors Check 3.6 above.

**Before R7**, dispatch was resolved via a 3-layer ladder: `node.sprk_nodetype` → `Action.sprk_executoractiontype` INT → `Action.sprk_actiontypeid` lookup. None of the layers was enforced to agree, so they drifted across releases and every release shipped a different version of the same "wrong executor ran" bug (design.md §3.1 WHY history).

**After R7** (Invariants 1-3):
- Every node has `sprk_executortype` (Choice) set explicitly.
- `PlaybookOrchestrationService.ExecuteNodeAsync` reads it once. Single hop.
- Action is a reusable prompt template — `SystemPrompt + OutputSchema + Temperature` for prompt-driven executors only — and carries NO dispatch identity.

**This audit's load-bearing job** is to surface playbooks (and call sites) still carrying the legacy drift:
- 94 nodes existed in spaarkedev1 pre-R7. Wave 5 task 050 reviewed them via `Review-PlaybookNodes-Dispatch.ps1` (CSV output: 41 HIGH-confidence + 14 MEDIUM + 23 LOW + 16 NONE) and Wave 5 task 053 produced the backfill migration script. Any node still showing NULL `sprk_executortype` after Wave 5 = bug.
- The two Action-side columns (`sprk_actiontypeid` + `sprk_executoractiontype`) were dropped in Wave 4. Any code reading them = 400 at runtime.

**Audit report shape** (mirrors Wave 5 CSV):

| Column | Source | Notes |
|---|---|---|
| node-guid | `sprk_playbooknode.sprk_playbooknodeid` | Stable identifier |
| playbook-name | parent `sprk_analysisplaybook.sprk_name` | For grouping |
| node-name | `sprk_playbooknode.sprk_name` | Reviewer context |
| current sprk_executortype | `sprk_playbooknode.sprk_executortype` | NULL = pattern A (🔴) |
| sprk_nodetype (legacy) | — | column removed; SHOULD return empty/error |
| Action FK | `_sprk_actionid_value` | NULL for pure; non-NULL for prompt-driven |
| Action ActionType lookup (decorative) | — | column dropped (Wave 4 task 043); SHOULD return error |
| Drift flags | A-G from Check 3.6 | Multi-flag possible |
| Recommended action | per-flag | Backfill / cleanup / NULL out / no-op |

**Cross-references**:
- design.md §2 (Invariants 1-3) — binding rules
- design.md §3.1 (R3.1 WHY history) — failure modes that motivated the rewrite
- spec.md FR-03 / FR-04 / FR-05 — schema cleanup + decorative-table preservation
- spec.md FR-07 / FR-08 / FR-09 — single-hop refactor
- spec.md FR-19 — 94-node backfill via `Review-PlaybookNodes-Dispatch.ps1` + `Migrate-PlaybookNodes-to-ExecutorType.ps1`
- spec.md FR-32 — this skill's rewrite
- `PlaybookOrchestrationService.ExecuteNodeAsync` — runtime source of truth (Wave 2 task 024)

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

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Audit reports a scope as missing when it actually exists in Dataverse | `scope-model-index.json` is stale relative to live Dataverse | Run `/jps-scope-refresh` BEFORE audit. The scope index is the source of truth for the audit; stale index = false positives. |
| 🔴 ERROR auto-applied — broke a working playbook | Skill bypassed the "never auto-apply ERROR fixes" rule | Honor the Conventions: ERRORs require human review. Apply only ✅ GREEN suggestions automatically; 🟡 YELLOW with confirmation; 🔴 RED never. |
| Audit found no issues but production playbook fails at runtime | Audit didn't cover runtime concerns (e.g., model availability, scope record statecode) | Audit is static compliance check only. For runtime validation, run a test playbook execution end-to-end after major scope changes. |
| Singular/plural Dataverse table-name confusion in audit queries | `sprk_analysisaction` (logical name) vs `sprk_analysisactions` (Web API plural endpoint) | This skill should use plural endpoints (`sprk_analysisactions`) in OData URLs but singular names in references to the logical table. Verify against `mcp__dataverse__list_tables()` if in doubt. |
