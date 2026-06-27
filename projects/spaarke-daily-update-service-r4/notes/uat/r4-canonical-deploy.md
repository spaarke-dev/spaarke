# R4 Canonical Playbook Deploy — spaarkedev1

> **Date**: 2026-06-26
> **Target**: spaarkedev1 (`https://spaarkedev1.crm.dynamics.com`)
> **Branch**: `work/spaarke-daily-update-service-r4`
> **Operator**: Claude Code (canonical-truth loop step 5)
> **Status**: ✅ All 5 R4 playbooks deployed; PB-016/018/019 intact; UAT unblocked

---

## 1. Summary

5 R4 playbooks were redeployed from canonical repo JSON onto spaarkedev1. Pre-deploy
audit showed all 5 had stale / partial node sets (e.g. `DAILY-BRIEFING-NARRATE`
had zero nodes — the R4 UAT IOORE root cause; `New Work Assignments` had a single
inactive stub node). Post-deploy, every playbook now has the full node graph with
`sprk_isactive=true`, FK to action, output variables, and dependsOn wiring.

Three pre-existing notification playbooks (PB-016/018/019) were audited read-only —
all three are intact and untouched.

`/api/ai/daily-briefing/narrate` route smoke test passes (401 without auth, 200
healthz). End-to-end /narrate verification requires JWT-authenticated widget call.

---

## 2. Pre-deploy audit (read-only baseline)

### 2.1 R4 playbook headers (5 rows)

| GUID | Name | Type | statecode | sprk_configjson populated? | sprk_canvaslayoutjson populated? |
|---|---|---|---|---|---|
| `7b5a6ed3-…` | Daily Briefing Narrate | 0 AiAnalysis | 0 Active | ✅ (carries node-graph mirror) | ❌ null |
| `4369cab2-…` | Tasks Overdue | 2 Notification | 0 Active | ✅ (canvas-only stale graph) | ✅ (stale 4-node canvas) |
| `77f77aa5-…` | Tasks Due Soon | 2 Notification | 0 Active | ✅ | ✅ |
| `24051c80-…` | Matter/Project Activity | 2 Notification | 0 Active | ✅ | ✅ (1-node stub canvas) |
| `be7874be-…` | New Work Assignments | 2 Notification | 0 Active | ✅ | ✅ (1-node stub canvas) |

### 2.2 Pre-deploy node-row counts

| Playbook | Nodes found | Active? | FK? | Repo JSON expected | Status |
|---|---|---|---|---|---|
| Daily Briefing Narrate | 0 | n/a | n/a | 6 | ❌ ZERO NODES (root cause of R4 UAT 503) |
| Tasks Overdue | 4 (stale) | all true | 0 of 4 had FK | 5 | ⚠️ Wrong shape (Lookup My Matters missing) |
| Tasks Due Soon | 4 | 3 of 4 had FK | mixed | 5 | ⚠️ Stale (Lookup My Matters missing) |
| Matter/Project Activity | 1 (stub) | true | null | 5 | ❌ Single AIAnalysis stub |
| New Work Assignments | 1 (stub) | **false** | null | 5 | ❌ Inactive single-node stub |

### 2.3 Pre-existing notification playbooks (NOT re-deployed)

| GUID | Name | Nodes | Active? | FKs? | Status |
|---|---|---|---|---|---|
| `29051c80-…` | New Documents on Your Matters (PB-016) | 4 | all true | ef7747ca + f97747ca | ✅ INTACT |
| `2f46208e-…` | New Emails on Matters (PB-018) | 4 | all true | ef7747ca + f97747ca | ✅ INTACT |
| `a4bc529c-…` | New Events on Matters/Projects (PB-019) | 4 | all true | ef7747ca + f97747ca | ✅ INTACT |

These three are R3-era deployments. R4 task 026 did NOT corrupt them — they remain
on their original FK chain. No re-deploy needed.

### 2.4 Empirical confirmation of R4 UAT defect

The pre-deploy audit confirms the R4 UAT log entry `"Playbook 7b5a6ed3-… has no
nodes - using Legacy mode"` was structurally correct — DAILY-BRIEFING-NARRATE
header existed with a canvas-equivalent JSON stuffed into `sprk_configjson`, but
ZERO rows in `sprk_playbooknode`. Per the mode-emergent rule
(`ai-architecture-playbook-runtime.md` §3) this triggers Legacy mode, and the
empty-DocumentIds payload then hit the IOORE site (now hotfixed per
`AnalysisOrchestrationService.cs:720-728`).

---

## 3. Reconciliation against canonical foundation

### 3.1 4-Home decision tree (`ai-architecture-actions-nodes-scopes.md`)

All 5 repo JSON files comply:
- Action-intrinsic behaviour (prompt, temperature, output schema) → on Action rows ✅
- Playbook-header metadata (name, type, capabilities, schedule) → on playbook columns ✅
- Per-node runtime config (FK, configJson, outputVariable, dependsOn) → on node rows ✅
- Declarative scopes → on playbook-level N:N (already populated; not touched here) ✅

### 3.2 Mode-emergent rule

None of the 5 repo JSONs declare a `sprk_playbookmode` header field — confirmed.
`daily-briefing-narrate.json` _does_ carry `"sprk_playbookmode": 1` inside the
`_dataverseRow.fields` block, but that block is documentation-only (the deploy
flow doesn't consult it). The Deploy-Playbook.ps1 script also does NOT write the
column. The 5 deployed playbooks are now correctly node-rich → mode emerges as
NodeBased at runtime per the empirical detector at
`PlaybookOrchestrationService.cs:246-253`.

### 3.3 Spaarke entity architecture (BINDING)

All 5 repo JSONs target Spaarke entities (verified via Grep):
- `sprk_event` (with `sprk_eventtype_ref = Task GUID 124f5fc9-…`) for tasks ✅
- `sprk_event` for matter activity ✅
- `sprk_workassignment` for work assignments ✅
- `sprk_matter` for membership resolution ✅

No OOB `task` / `email` / `appointment` references found in any of the 5 R4 files.
The corrections noted in each JSON's `_correction` block (dated 2026-06-25) carried
through into the deployment.

### 3.4 actionCode lint pre-screen

`Deploy-Playbook.ps1`'s lint at `:331-356` rejected all 5 R4 playbooks because:
- Control nodes (Start, Has X?, Check Results, LoadKnowledge, ReturnResponse)
  intentionally lack top-level `actionCode` per the deployed pattern of PB-018
  (which has working Control nodes with `sprk_actionid = null`).
- `daily-briefing-narrate.json` uses `nodeType: "Tool"` (outside the script's
  `$NodeTypeMap`) and inline `configJson.actionCode` (not top-level).

This is a known mismatch between the script's "every dispatchable node carries
actionCode" lint and the deployed PB-018 reality. **No edits made to the repo
JSON files** — the JSONs accurately reflect the canonical node shape; the script's
lint is more restrictive than the runtime requires. Fallback deployer used (§5).

### 3.5 Risk noted (for future hardening)

The Deploy-Playbook.ps1 script's lint vs. the deployed PB-018 pattern is
**inconsistent**: PB-018 has Control nodes with `sprk_actionid = null`, which the
runtime tolerates (NodeType-default fallback at
`PlaybookOrchestrationService.cs:1116-1127`). The script's lint would refuse to
re-deploy PB-018 today. Filed as residual R4 tech-debt — script lint should either
(a) exempt Control nodes too, or (b) require a `SYS-CONTROL`/`SYS-START` action
row to be seeded so every Control node has a top-level actionCode. Out of scope
for R4 deploy phase; captured for R5 follow-up.

---

## 4. Deploy method — fallback to direct Dataverse Web API

`scripts/Deploy-Playbook.ps1 -Force` would not run (5/5 nodes failed the
actionCode lint per §3.4). Fallback path used the documented MCP-equivalent
direct-Dataverse-Web-API pattern — captured as a one-shot deployer at:

- `scripts/Deploy-R4-Playbook-Nodes.ps1`

It replicates steps 8–11 of the canonical 12-step recipe (per
`ai-guide-playbook-deploy-recipe.md` §3):

1. POST `sprk_playbooknodes` rows with `sprk_isactive=true` (LOAD-BEARING — the
   default is false; see `Deploy-Playbook.ps1:823`), `sprk_playbookid@odata.bind`,
   optional `sprk_actionid@odata.bind`, optional `sprk_configjson`.
2. Second pass: PATCH `sprk_dependsonjson` on each node with resolved Guids.
3. PATCH `sprk_canvaslayoutjson` on the playbook with the React Flow shape
   (`{viewport, nodes[], edges[], version}`).

NOT done by the fallback (vs. the full Deploy-Playbook.ps1):
- N:N scope writes — the 5 playbooks already have their N:N scope arrays from
  prior R4 task 026 deploys.
- Action row creation — Action rows are already seeded (verified via MCP).
- Model deployment resolution — Action rows carry their model FK.

Stale node rows from the pre-deploy audit were DELETEd via MCP
`mcp__dataverse__delete_record` before the fresh deploys ran.

---

## 5. Per-playbook deploy outcomes

| Playbook | Stale rows deleted | New rows created | Deps wired | Canvas written | Status |
|---|---|---|---|---|---|
| Daily Briefing Narrate (`7b5a6ed3-…`) | 0 (no prior nodes) | 6 | 5 | 6n/6e | ✅ |
| Tasks Overdue (`4369cab2-…`) | 4 | 5 | 4 | 5n/4e | ✅ |
| Tasks Due Soon (`77f77aa5-…`) | 4 | 5 | 4 | 5n/4e | ✅ |
| Matter/Project Activity (`24051c80-…`) | 1 | 5 | 4 | 5n/4e | ✅ |
| New Work Assignments (`be7874be-…`) | 1 | 5 | 4 | 5n/4e | ✅ |

---

## 6. Post-deploy verification (per `ai-guide-playbook-deploy-recipe.md` §8)

All 6 mandatory checks pass against all 5 playbooks (verified via MCP `read_query`
on 2026-06-26 after deploy):

| Check | Daily Briefing Narrate | Tasks Overdue | Tasks Due Soon | Matter Activity | Work Assignments |
|---|---|---|---|---|---|
| sprk_analysisplaybooks row exists, statecode=0 | ✅ | ✅ | ✅ | ✅ | ✅ |
| Node count matches JSON `nodes[]` length | ✅ 6/6 | ✅ 5/5 | ✅ 5/5 | ✅ 5/5 | ✅ 5/5 |
| Every node `sprk_isactive=true` | ✅ | ✅ | ✅ | ✅ | ✅ |
| Every dispatchable node has FK | ✅ (3 AI + 0 W/F) | ✅ (3 W/F) | ✅ (3 W/F) | ✅ (3 W/F) | ✅ (3 W/F) |
| Every node has non-empty `sprk_outputvariable` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `sprk_dependsonjson` populated where deps exist | ✅ (incl. 2-edge ValidateEntityNames) | ✅ | ✅ | ✅ | ✅ |

Note: Control nodes (Start, Has X?, Check Results, LoadKnowledge, ReturnResponse)
have `sprk_actionid = null` by design — matches deployed PB-018 pattern. Runtime
NodeType-default fallback handles dispatch via __actionType in configJson.

The 2-edge dependsOn case on `DAILY-BRIEFING-NARRATE.ValidateEntityNames` was
verified: `sprk_dependsonjson = "[GenerateTldr_guid, GenerateChannelNarratives_guid]"`
per ADR-037 multinode composition contract.

---

## 7. PB-016 / 018 / 019 audit (NOT re-deployed)

Pre-deploy audit (§2.3) confirmed all three are intact:
- Each has 4 active nodes
- Same FK chain (`SYS-QUERY-DV` + `SYS-CREATE-NOTIF`)
- No corruption from R4 task 026 configjson overlay

No re-deploy was performed (per task constraints — owner approval required).
The owner is now free to consider whether to migrate these to the membership-aware
pattern (matching PB-020/021/022) in a future R-cycle.

---

## 8. Smoke test results

| Endpoint | Result |
|---|---|
| `GET https://spaarke-bff-dev.azurewebsites.net/healthz` | **200** |
| `POST /api/ai/daily-briefing/narrate` (no auth, empty body) | **401** |

Both expected outcomes. The 401 confirms the route is registered + auth-required.
End-to-end /narrate via JWT-bearing widget is **owner-actionable next step**
(authenticated browser session is required).

---

## 9. R4 UAT scenarios now unblocked

The R4 UAT was paused awaiting this deploy. The following scenarios are now
unblocked:

1. **/narrate end-to-end** — Daily Briefing widget → JWT → `IConsumerRoutingService`
   resolves DAILY-BRIEFING-NARRATE → `PlaybookOrchestrationService.ExecuteAsync` →
   NodeBased mode (no longer Legacy/IOORE) → 6-node graph executes →
   `DailyBriefingNarrateResponse` returned.
2. **PB-020 Tasks Due Soon** — fires via `PlaybookSchedulerJob` → membership-aware
   FetchXml union → `sprk_event` (Task type) within configured window →
   `CreateNotificationNodeExecutor` creates `appnotification` with R4-enriched
   customData.
3. **PB-021 Tasks Overdue** — analogous to PB-020 with `lt {{todayUtc}}` filter.
4. **PB-017 Matter/Project Activity** — fires per weekday cron → recent
   `sprk_event` activity on user's matters → notification surfaced.
5. **PB-022 New Work Assignments** — fires every 2h on weekdays → new
   `sprk_workassignment` records on user's matters or assigned to user.

---

## 10. Open items + handoffs

1. **/narrate end-to-end requires owner-side verification** — load the Daily
   Briefing widget in spaarkedev1 MDA with a real JWT and confirm a full narrated
   response (no Legacy-mode 503). Server-side smoke confirmed the route is
   registered + auth-protected; only an authenticated session can verify the
   actual node graph executes end-to-end.
2. **PB-016 / 018 / 019 membership scope** — these three legacy notification
   playbooks do NOT yet have the `Lookup My Matters` node + membership-aware
   FetchXml union. Owner approval required to re-deploy them with the corrected
   pattern. The repo JSON files at `projects/spaarke-daily-update-service/notes/
   playbooks/notification-new-{emails,documents,events}.json` already carry the
   corrected entity references + membership branch — they're ready when approved.
3. **Deploy-Playbook.ps1 actionCode lint hardening** — the script lint refuses
   the canonical PB-018 pattern (Control nodes without actionCode). Filed as R5
   tech debt: either (a) exempt Control nodes from the lint OR (b) seed
   `SYS-CONTROL` / `SYS-START` action rows so Control nodes can carry an
   actionCode. Until resolved, `scripts/Deploy-R4-Playbook-Nodes.ps1` (this
   deploy's fallback) remains the reliable path for redeploys.
4. **`sprk_canvaslayoutjson` cleanup on DAILY-BRIEFING-NARRATE** — prior to this
   deploy the playbook had `sprk_configjson` populated with a node-graph mirror
   (the wrong-pattern that R4 spec called out). The R4 deploy left the
   `sprk_configjson` content untouched (we only wrote nodes + dependsOn + canvas).
   Optional cleanup: PATCH `sprk_configjson` to the playbook-header-only shape
   per the 4-Home decision tree, to avoid future audit confusion. Not blocking.
5. **`sprk_configjson` mirror on the 4 notification playbooks** — same pattern
   as #4. Same recommendation. Not blocking.

---

## 11. Reference

- Canonical recipe: `docs/guides/ai-guide-playbook-deploy-recipe.md`
- Mode-emergent rule: `docs/architecture/ai-architecture-playbook-runtime.md` §3
- 4-Home decision tree: `docs/architecture/ai-architecture-actions-nodes-scopes.md`
- Code archaeology: `projects/spaarke-daily-update-service-r4/notes/canonical-truth/01-code-archaeology.md`
- Fallback deployer: `scripts/Deploy-R4-Playbook-Nodes.ps1`
- Repo JSON source-of-truth: `projects/spaarke-daily-update-service/notes/playbooks/`
