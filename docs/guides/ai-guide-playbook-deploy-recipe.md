# AI Guide — Playbook Deploy Recipe (`Deploy-Playbook.ps1`)

> **Last reviewed**: 2026-06-29
> **Authored by**: canonical-truth loop step 3 (spaarke-daily-update-service-r4); updated by spaarke-ai-platform-unification-r7 Wave 6 task 063 (FR-28 + FR-20) on 2026-06-29 to remove Control-flow name-detection narrative + `__actionType` structural-fallback hint + document the new R7 explicit-`sprk_executortype` write path and Lint A.
> **Status**: Canonical operator runbook for `Deploy-Playbook.ps1`. Consolidates fragments from `JPS-AUTHORING-GUIDE.md` §10, `.claude/skills/jps-playbook-design/SKILL.md` Steps 9-10, and `.claude/skills/dataverse-deploy/SKILL.md`. Cited from R4 W0.
> **Scope**: Input file format (R7 explicit `executorType` field + backward-compat `nodeType` label mapping), 12-step deploy sequence (R7 Lint A pre-check), skip-vs-Force behaviour, the load-bearing `sprk_isactive` rule, the actionCode lint, failure recovery, verification queries, and the "Playbook has no nodes — using Legacy mode" diagnostic.
> **NOT in scope**: JPS schema (see `ai-guide-jps-authoring.md`); maker recipes (see `PLAYBOOK-AUTHOR-GUIDE.md`); runtime semantics (see `ai-architecture-playbook-runtime.md`); consumer routing (see `ai-architecture-playbook-consumer-routing.md`).

---

## 1. What `Deploy-Playbook.ps1` does

The script takes a single JSON file describing a playbook + its node graph and writes:

- (Optional) `sprk_analysisaction` rows for any inline-defined Actions
- One `sprk_analysisplaybook` row
- One `sprk_playbooknode` row per node in the graph
- N:N associations for playbook-level scopes (`sprk_analysisplaybook_action`, `sprk_playbook_skill`, `sprk_playbook_knowledge`, `sprk_playbook_tool`)
- N:N associations for node-level scopes (`sprk_playbooknode_{skill,knowledge,tool}`)
- The React Flow canvas JSON onto `sprk_analysisplaybook.sprk_canvaslayoutjson`

The runtime mode (Legacy vs NodeBased) is **emergent from node-row presence**, not declarative — the script does NOT write a `sprk_playbookmode` column (it doesn't exist in the BFF read path). See `ai-architecture-playbook-runtime.md` §3.

---

## 2. Input file format

The script accepts a JSON file shaped roughly as follows (1069-line full grammar; required fields highlighted). All optional unless noted.

```jsonc
{
  "$comment": "Free-form comment ignored by the script",

  "actions": [
    /* Optional: inline Action definitions to upsert BEFORE the playbook.
       More commonly, Actions are deployed separately via Seed-JpsActions.ps1
       and referenced by code from playbook.scopes.actions[]. */
  ],

  "playbook": {                                  // REQUIRED
    "name": "DAILY-BRIEFING-NARRATE",            // REQUIRED — primary key for skip-by-name
    "description": "...",
    "isPublic": true,
    "isSystemPlaybook": false,
    "sprk_playbooktype": 0,                      // 0=AiAnalysis, 2=Notification
    "sprk_configjson": { /* optional */ },       // NO BFF read site (advisory only)
    "code": "DAILY-BRIEFING-NARRATE",            // optional; for error messages
    "scopes": {                                  // declarative resource declaration
      "actions":  ["BRIEF-NARRATE", "ENTITY-NAME-VALIDATOR", ...],
      "skills":   ["..."],
      "knowledge":["..."],
      "tools":    ["..."]
    }
    // OR flat shape: "actions": [], "skills": [], "knowledge": [], "tools": []
  },

  "nodes": [                                     // REQUIRED (array; can be empty but then runtime is Legacy mode)
    {
      "name": "narrate-llm",                     // unique within the playbook
      "executorType": 1,                         // R7 PREFERRED — integer from sprk_playbookexecutortype Choice (33 values: AiAnalysis=0, AiCompletion=1, AiEmbedding=2, Condition=30, Start=33, DeliverOutput=40, DeliverComposite=42, CreateNotification=50, ... EntityNameValidator=141, ReturnResponse=143). Validated by Lint A pre-write.
      "nodeType": "AIAnalysis",                  // OPTIONAL backward-compat friendly label (R3/R4-era playbooks). If executorType is absent, Deploy-Playbook.ps1 maps `nodeType` string → executorType integer via $LegacyNodeTypeToExecutorType. New playbooks SHOULD use executorType directly.
      "actionCode": "BRIEF-NARRATE",             // REQUIRED for prompt-driven executors (AiAnalysis=0, AiCompletion=1, AiEmbedding=2). Optional/forbidden for pure executors (Condition, Start, ReturnResponse, etc.). actionCode lint at Deploy-Playbook.ps1:331-356.
      "model": "gpt-4o-mini",                    // optional; resolved against sprk_aimodeldeployments
      "outputVariable": "narration",             // string — downstream nodes reference by name
      "positionX": 100,
      "positionY": 200,
      "configJson": {                            // per-node runtime config (canonical home for executor-specific fields). Validated against node-routing-config.schema.json per executor type (Wave 3 FR-16).
        "inputBindings": { /* executor-specific */ }
      },
      "dependsOn": ["lookup-membership"],
      "scopes": { "skills": [], "knowledge": [], "tools": [] }
    }
  ]
}
```

> **R7 change (FR-20, Wave 5 task 055)**: every node now writes `sprk_executortype` explicitly. The legacy `sprk_nodetype` column was removed from `sprk_playbooknode` pre-R7; the `__actionType` structural-fallback hint that the orchestrator used to read from `configJson` was deleted in Wave 2 task 025 (~150 LOC removed). Do NOT add `__actionType` to new playbook JSONs — it is no longer load-bearing. The R7 `Deploy-Playbook.ps1` REMOVES any `__actionType` that appears in input JSON before posting `sprk_configjson` to Dataverse (logged at debug level).

**The actionCode lint** at `Deploy-Playbook.ps1:331-356` rejects any prompt-driven node (executorType ∈ {0, 1, 2}) missing `actionCode`. `DeliverComposite` (executorType 42) is the documented exemption because its executor dispatches by composite-strategy semantics, not Action FK. For prompt-driven nodes: every node MUST carry actionCode, which the script resolves to an Action FK at deploy time.

**Lint A — R7 executor-type validation** (Wave 5 task 055, FR-20): runs BEFORE any Dataverse write. For each node, the script verifies the resolved `executorType` integer is one of the 33 known `sprk_playbookexecutortype` Choice values. If unknown → exit 1 with the offending node name, the value seen, and the full 33-value reference. This catches typos and stale playbook JSONs that pre-date the 33-value catalog. The 33-value `$KnownExecutorTypes` hashtable lives inline in the script for now (flagged as a future codegen-from-`INodeExecutor.cs` opportunity).

---

## 3. The 12-step deploy procedure

The script narrates each step. Here is the full sequence with file:line references against the work-branch state.

| Step | Action | Reference |
|---|---|---|
| 1 | Parse + validate definition (includes the actionCode lint + R7 Lint A executor-type validation per FR-20) | `:296-356` |
| 2 | Collect all scope codes from playbook-level + node-level | scope code accumulator |
| 3 | Resolve scope codes → Guids via `sprk_analysisactions[sprk_actioncode]`, `sprk_analysisskills[sprk_skillcode]`, `sprk_analysisknowledges[sprk_externalid]`, `sprk_analysistools[sprk_toolcode]` | `:419-477` |
| 4 | Resolve model deployments by name (`sprk_aimodeldeployments`) | model resolver |
| 5 | Check for existing playbook by name. Without `-Force`: SKIP if exists. With `-Force`: delete + recreate. | `:587-590` |
| 6 | POST `sprk_analysisplaybooks` row with: name, description, isPublic, optional sprk_playbooktype, optional sprk_issystemplaybook, optional sprk_configjson | `:621` |
| 7 | Associate playbook-level N:N scopes (`sprk_analysisplaybook_action`, `sprk_playbook_skill`, `sprk_playbook_knowledge`, `sprk_playbook_tool`) | N:N associate calls |
| 8 | Per-node loop: validate `sprk_configjson` against schema (FR-14e gate) → resolve `executorType` (explicit field preferred; `nodeType` string mapped via backward-compat table) → POST `sprk_playbooknodes` row with **`sprk_executortype = <integer>`** (R7 FR-20, the single-hop dispatch axis), `sprk_isactive=true` (LOAD-BEARING), `sprk_playbookid@odata.bind`, optional `sprk_actionid@odata.bind` (REQUIRED for prompt-driven executors), optional `sprk_modeldeploymentid@odata.bind`, optional `sprk_configjson` | `:765-853`, schema validate at `:789`, `sprk_isactive` comment at `:823` |
| 9 | Second pass: PATCH each node with `sprk_dependsonjson` (built from resolved node Guids) | `:891` |
| 10 | Associate node-level N:N scopes (`sprk_playbooknode_{skill,knowledge,tool}`) | node-scope associate calls |
| 11 | Build canvas layout JSON (nodes + edges in React Flow shape) → PATCH playbook with `sprk_canvaslayoutjson` | `:1044` |
| 12 | Summary print | end of script |

> **R7 step-1 change (Wave 5 task 055, FR-20)**: Lint A runs *before* scope resolution (step 3). If any node's resolved executorType is not in the 33-value `sprk_playbookexecutortype` Choice catalog, the script exits 1 immediately — no scope queries, no Dataverse writes. This catches stale executorType values + typos before they create partially-deployed playbook rows.

---

## 4. The `sprk_isactive` rule (load-bearing)

**`sprk_playbooknode.sprk_isactive` MUST be set true at deploy time.** The column's Dataverse default is **false**. If the script omits this field, the row is created inactive and `NodeService.GetNodesAsync` filters it out → runtime sees `Length == 0` → Legacy mode fires.

Reference: `Deploy-Playbook.ps1:823` — the comment is load-bearing; do not remove it. If you fork the script or write a parallel deployer, replicate the explicit `sprk_isactive = $true` write.

---

## 5. Skip-vs-`-Force` behaviour (not a true upsert)

The script's idempotency model is **skip-by-name**, not upsert:

| Flag combo | Existing playbook by name | Behaviour |
|---|---|---|
| (default) | exists | **SKIP** entire playbook; no nodes / scopes / canvas updated |
| (default) | does not exist | Create everything |
| `-Force` | exists | **Delete + recreate**. Old playbook + nodes are destroyed, new ones created from definition file |
| `-Force` | does not exist | Create everything |

This means:
- A simple `Deploy-Playbook.ps1 my-playbook.json` after a small edit is a NO-OP. You must use `-Force` to update.
- Update semantics are destructive — the playbook Guid changes; any external reference by Guid breaks. References to the playbook by code (via `sprk_playbookconsumer` rows) survive because the row is re-resolved by name → Guid.

**Future improvement** (not in R4): a true upsert mode (`-Update`) that preserves the playbook Guid and patches in-place. Flag this as tech debt — the destructive delete-then-recreate makes incremental iteration awkward and breaks consumer-routing rows that target the playbook by Guid (though current rows use the data-driven lookup, so this is mitigated).

`-DryRun` mode at `:412-421` + `:644-656` lets the operator preview the planned writes before commit.

---

## 6. Failure recovery — no rollback

Each step throws on failure. There is **no rollback**. A failure at step 8 (after step 6 created the playbook row, step 7 associated scopes) leaves a partially-deployed playbook in Dataverse:

| State after failure | Effect |
|---|---|
| Playbook row exists, no nodes | Runtime: Legacy mode → if Path A.5, returns 503 `PLAYBOOK_INVOCATION_FAILED` (post-R4 hotfix) |
| Playbook row + N:N scopes, partial nodes | Runtime may execute partially; dependency graph likely invalid |
| All nodes created, canvas JSON missing | Runtime fine; Builder UI shows raw node list with no layout |

**Recovery procedure**:
1. Run with `-DryRun` first to confirm what would change.
2. If partial deploy occurred: rerun with `-Force` to delete and recreate cleanly.
3. If the deploy script itself is at fault (bug in the script not the data), fix the script before re-running.

---

## 7. Critical pre-conditions

Before running `Deploy-Playbook.ps1`:

1. **Scope rows must already exist** — Action, Skill, Knowledge, Tool rows are read-only at deploy. They must be seeded via `Seed-JpsActions.ps1` and equivalents BEFORE this script runs. Step 3 resolution fails loudly if any referenced code is not found.
2. **Model deployments must exist** — if any node references `model: "gpt-4o-mini"` etc., the corresponding `sprk_aimodeldeployments` row must exist.
3. **Dataverse environment is the right one** — the script uses the connected Power Platform CLI auth profile. Verify with `pac auth list`. **You can deploy to the wrong env if your default profile is wrong.**
4. **The repo JSON file targets the correct entities** — R4 binding: tasks → `sprk_event`, emails → `sprk_communication`, not OOB `task`/`email`. See `ai-architecture-playbook-runtime.md` §10 (G12).

---

## 8. Verification queries (post-deploy smoke checks)

After `Deploy-Playbook.ps1` completes, **verify both the playbook row AND the node rows exist**. The playbook row alone is not enough — runtime needs node rows.

### Via Dataverse MCP `read_query`:

```
read_query against sprk_analysisplaybooks where sprk_name = 'DAILY-BRIEFING-NARRATE'
  expect: 1 row; capture sprk_analysisplaybookid

read_query against sprk_playbooknodes where _sprk_playbookid_value = <captured Guid>
  expect: N rows where N matches your deploy file's nodes[] length
  expect: every row sprk_isactive = true
  expect: dependency chain looks right (sprk_dependsonjson values are within the set)
```

### Quick CLI smoke (if you have PAC CLI auth ready):

```powershell
pac data list -e sprk_analysisplaybook -fl "sprk_name,sprk_analysisplaybookid" -filt "sprk_name eq 'DAILY-BRIEFING-NARRATE'"
pac data list -e sprk_playbooknode -fl "sprk_name,sprk_isactive,_sprk_playbookid_value" -filt "_sprk_playbookid_value eq <Guid>"
```

If the node-row query returns ZERO, the deploy script did NOT create nodes — Legacy mode will fire at runtime. Re-deploy with `-Force` or investigate the actionCode lint at step 3.

---

## 9. Troubleshooting — "Playbook has no nodes — using Legacy mode" + R7 lint failures

This is the canonical R4 UAT log entry. It fires from `PlaybookOrchestrationService.cs:250` and means **the runtime queried `sprk_playbooknode` filtered by your playbook GUID and got zero rows back**. Possible causes:

| Cause | Diagnostic | Fix |
|---|---|---|
| Playbook never had nodes deployed (only canvas JSON populated) | `pac data list -e sprk_playbooknode -filt "_sprk_playbookid_value eq <Guid>"` returns 0 | Run `Deploy-Playbook.ps1 -Force` |
| Nodes were created but `sprk_isactive` is false | Same query, plus check `sprk_isactive` column | Inspect deploy script's step 8 — confirm `sprk_isactive = true` is being written |
| Nodes were created but `sprk_executortype` is NULL | `pac data list -e sprk_playbooknode -fl "sprk_name,sprk_executortype" -filt "_sprk_playbookid_value eq <Guid>"` shows NULL on any row | Wave 5 backfill applies — run `scripts/dataverse/Migrate-PlaybookNodes-to-ExecutorType.ps1` (R7 task 053) after the owner has filled in the review CSV (task 052). Or, if the source JSON is missing `executorType`, fix the JSON + re-deploy with `-Force`. |
| Skip-by-name swallowed a failed re-deploy | Playbook row exists but is stale (old node set) | Run `Deploy-Playbook.ps1 -Force` |
| You ran against the wrong Dataverse environment | `pac auth list` shows wrong profile | Switch profile, re-deploy |
| The playbook Guid the runtime is querying doesn't match what you deployed | Different env, or stale `sprk_playbookconsumer` cache (5 min TTL) | Wait 5 min OR restart BFF to clear `IMemoryCache` |

After fixing, the next dispatch should log `"Executing playbook in NodeBased mode"` (or equivalent) instead. The Legacy log site catalog is in `ai-architecture-playbook-runtime.md` §3.

### R7 Lint A failure — "Unknown executor type"

If `Deploy-Playbook.ps1` exits 1 with `Lint A: node '<name>' has unknown executorType value <N>` (or similar):

| Cause | Fix |
|---|---|
| Typo in playbook JSON (`executorType: 999`) | Set `executorType` to one of the 33 known `sprk_playbookexecutortype` Choice values (see [§2 above](#2-input-file-format) for the list). |
| Stale `nodeType` label not in the backward-compat table | Either replace `nodeType` with an explicit `executorType` integer OR add the label to `$LegacyNodeTypeToExecutorType` in `Deploy-Playbook.ps1` (small set; covers the R3/R4-era 17 friendly labels). |
| Playbook JSON pre-dates R7 entirely | Wave 5 backfill applies to the deployed rows; re-author the JSON to use explicit `executorType` integers and re-deploy with `-Force`. |
| New executor was added to the C# enum but not to the script's 33-value `$KnownExecutorTypes` table | Append the new value (key=integer, value=enum-name string) to `$KnownExecutorTypes` in `Deploy-Playbook.ps1`. Codegen from `INodeExecutor.cs` is a tracked DEF-NNN follow-up. |

---

## 10. Coordinated deploy — Action rows before playbook rows before consumer rows

The deployment dependency order is:

1. **Action rows** via `Seed-JpsActions.ps1` (or sibling scripts) — must exist before any referencing playbook is deployed.
2. **Playbook rows + nodes + N:N scopes + canvas** via `Deploy-Playbook.ps1` — references the Action rows by code.
3. **Consumer rows** (`sprk_playbookconsumer`) — references the Playbook row by FK. After the playbook row exists, add a routing row.
4. **Refresh the scope catalog** — `jps-scope-refresh` to update `.claude/catalogs/scope-model-index.json` for AI-tooling consistency.

Skipping or reordering breaks the FK chain. Specifically, deploying a playbook that references an Action that doesn't exist yet will fail at step 3 of the script.

---

## 11. Customer/external context

The deploy script is currently **Spaarke-internal tooling** — it uses PAC CLI auth, repo-relative paths, and assumes write access to the Dataverse environment. Customers will eventually need a customer-facing equivalent (likely an MCP-exposed playbook deploy tool or a managed solution import flow). That's not in R4 scope — captured for future work.

For internal teams: the canonical input files live at `projects/spaarke-daily-update-service/notes/playbooks/` (R1 notification set) and `projects/spaarke-daily-update-service-r4/notes/playbooks/` (R4 BRIEF-NARRATE + EntityNameValidator additions).

---

## 12. Relationship to other canonical docs

| Question | Read |
|---|---|
| Runtime mode detection + log site catalog | `ai-architecture-playbook-runtime.md` §3 |
| Why a config field goes on Action vs Node vs Playbook | `ai-architecture-actions-nodes-scopes.md` |
| How a consumer surface looks up + dispatches the playbook after deploy | `ai-architecture-playbook-consumer-routing.md` |
| JPS schema for `sprk_systemprompt`, structured output, `$choices` | `ai-guide-jps-authoring.md` |
| Maker UI procedure (build a playbook in the visual canvas instead) | `PLAYBOOK-AUTHOR-GUIDE.md` |
| Action row deploy (`Seed-JpsActions.ps1`) | `.claude/skills/jps-action-create/SKILL.md` |
| Playbook design + scope selection | `.claude/skills/jps-playbook-design/SKILL.md` |
| Deployed-vs-repo reconciliation audit | `.claude/skills/jps-playbook-audit/SKILL.md` |
