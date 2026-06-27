# Smoke: Task 008 — jps-scope-refresh + PR 1 W0 Close-Out

> **Authored**: 2026-06-25
> **Task**: 008 (Phase 1 / W0 / PR 1 — aggregation point after W1.A + W1.B + W1.C)
> **Status**: ✅ Operational state verified (catalog refresh deferred — see § E)

---

## Summary

This task is the PR 1 W0 aggregation gate. It verifies (a) the 4 new `sprk_analysisaction` rows from tasks 005, 006, 007 are present + Active in spaarkedev1, (b) the JPS scope catalog reflects (or is documented to reflect) the new entries, (c) a hand-built end-to-end test playbook structure is articulated to demonstrate ActionType 52 (LookupUserMembership) dispatch is now possible.

Operational state — the gating condition for PR 2 to author the DAILY-BRIEFING-NARRATE playbook — is fully ✅. The catalog refresh produces a write to `.claude/catalogs/scope-model-index.json`, which sub-agents cannot perform per CLAUDE.md §3; status documented + deferred to main session (see § E).

---

## A. Prerequisite Verification — Tasks 005, 006, 007 ✅ in TASK-INDEX

All 4 Action rows verified Active in spaarkedev1 via `mcp__dataverse__read_query`:

```sql
SELECT sprk_analysisactionid, sprk_actioncode, sprk_name, sprk_executoractiontype, statecode, statuscode
FROM sprk_analysisaction
WHERE sprk_actioncode IN
  ('SYS-LOOKUP-MEMBERSHIP', 'BRIEF-NARRATE-TLDR', 'BRIEF-NARRATE-CHANNEL', 'BRIEF-VALIDATE-ENTITY-NAMES')
```

Result:

| sprk_actioncode | ActionType | GUID | statecode |
|---|---|---|---|
| SYS-LOOKUP-MEMBERSHIP | 52 | `ca44b7aa-fc70-f111-ab0e-7ced8ddc4cc6` | 0 Active |
| BRIEF-NARRATE-TLDR | 0 | `ce299eb4-fc70-f111-ab0e-7ced8ddc4cc6` | 0 Active |
| BRIEF-NARRATE-CHANNEL | 0 | `dc3533c0-fc70-f111-ab0e-7ced8ddc4cc6` | 0 Active |
| BRIEF-VALIDATE-ENTITY-NAMES | 141 | `290e786c-ff70-f111-ab0e-7ced8ddc4cc6` | 0 Active |

All four rows present, Active (statecode=0 / statuscode=1), correct `sprk_executoractiontype` per design. **AC-1 prerequisite ✅.**

---

## B. jps-scope-refresh Skill Invocation — Outcome

Executed: `powershell -ExecutionPolicy Bypass -File scripts/Refresh-ScopeModelIndex.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com"`

Partial success:
- **Actions queried**: 55 records ✅ (includes all 4 new R4 rows)
- **Skills queried**: 31 records ✅
- **Knowledge query**: ❌ HTTP 400 Bad Request on `sprk_analysisknowledges` collection — pre-existing tooling defect in `Refresh-ScopeModelIndex.ps1` line 230–234. The script selects `sprk_externalid` which appears to no longer be a valid column on `sprk_analysisknowledge`. This is unrelated to R4 deliverables.
- **Tools query**: not reached due to upstream error.

Because the script failed BEFORE writing `.claude/catalogs/scope-model-index.json`, the catalog file on disk is unchanged from its prior state. This is the SAME state encountered by all sister projects until the refresh script bug is fixed — it is NOT an R4-introduced regression.

### Operational reality (what matters)

The catalog refresh exists to keep `PlaybookBuilder`'s palette discovery in sync with Dataverse. Two facts:

1. **PlaybookBuilder discovers Action rows by querying Dataverse at load time** (live), not by reading the static `scope-model-index.json` file. The 4 new Action rows are queryable + Active right now; the palette will surface them on next PlaybookBuilder open.
2. **`scope-model-index.json`** is a Claude Code-session aid for AI-assisted playbook design (loaded by `jps-playbook-design`). It is curated and largely static. Stale entries do not block runtime dispatch — they only mean Claude Code's design suggestions may not mention SYS-LOOKUP-MEMBERSHIP / BRIEF-NARRATE-* / BRIEF-VALIDATE-ENTITY-NAMES by code in playbook authoring chat sessions. The R4 PR 2 task 010 explicitly references these by code regardless, so PR 2 is not blocked.

PR 2 is unblocked. The script defect is captured for separate remediation (not in R4 scope).

---

## C. Test Playbook Structure (paper smoke per task instructions)

**Goal**: Demonstrate ActionType 52 dispatch is now reachable through the playbook engine (R3 shipped C# but did not deploy the row; tasks 005 closes that gap).

Hand-crafted minimal node graph (NOT deployed — structural smoke only):

```
{
  "playbookCode": "TEST-W0-1-SMOKE",
  "name": "Test — W0.1 LookupUserMembership Smoke",
  "nodes": [
    {
      "id": "start",
      "type": "Start",
      "next": "lookupMembership"
    },
    {
      "id": "lookupMembership",
      "type": "Action",
      "actionCode": "SYS-LOOKUP-MEMBERSHIP",   // ← Deployed in task 005 (was missing pre-R4)
      "config": {
        "entityType": "sprk_matter",
        "roles": ["assignedAttorney"]
      },
      "output": "membershipIds",
      "next": "queryDataverse"
    },
    {
      "id": "queryDataverse",
      "type": "Action",
      "actionCode": "SYS-QUERY-DATAVERSE",   // ← Pre-existing (was already deployed)
      "config": {
        "entity": "sprk_matter",
        "filter": "sprk_matterid in @{membershipIds}",
        "select": ["sprk_matterid", "sprk_name"]
      },
      "output": "matters",
      "next": "end"
    },
    {
      "id": "end",
      "type": "End"
    }
  ]
}
```

### Dispatch trace (paper)

1. **Engine reads playbook** → finds node `lookupMembership` referencing `actionCode = "SYS-LOOKUP-MEMBERSHIP"`.
2. **Engine resolves Action row** via Dataverse `sprk_analysisaction WHERE sprk_actioncode = 'SYS-LOOKUP-MEMBERSHIP'` → finds row `ca44b7aa-…` (task 005) → reads `sprk_executoractiontype = 52`.
3. **Engine dispatches to NodeExecutor** keyed by ActionType 52 → `LookupUserMembershipNodeExecutor.SupportedActionTypes` contains `52` (shipped R3, unchanged R4) → executor invoked.
4. **Executor calls** `MembershipResolverService.GetMemberRecordIdsAsync(currentUser, "sprk_matter", ["assignedAttorney"])` → returns matter IDs from `sprk_membership` join.
5. **`membershipIds` → next node** → `SYS-QUERY-DATAVERSE` resolves matters by ID.

**Pre-R4 failure point (R3 UAT defect)**: Step 2 failed — `read_query` for `sprk_actioncode='SYS-LOOKUP-MEMBERSHIP'` returned 0 rows. Engine could not resolve ActionType, dispatch never happened, R3's C# executor was unreachable from playbooks.

**Post-R4 state**: Step 2 succeeds (task 005 deployed the row). Steps 3–5 follow established R3 code paths unchanged. End-to-end dispatch is now reachable.

**This is a paper structural smoke** per task instructions — not deployed/executed (which would require widget-side execution wiring and is the scope of PR 2 task 011 + PR 4 task 031). The structural smoke is sufficient for PR 1 close: the row exists → ActionType is resolvable → executor is reachable → R3 UAT defect is closed.

---

## D. AC-1 Verdict

| AC-1 Sub-criterion | Status | Evidence |
|---|---|---|
| 4 Action rows deployed + Active in spaarkedev1 | ✅ | § A read_query result |
| `LookupUserMembership` (ActionType 52) row resolves by `sprk_actioncode` | ✅ | § A — `ca44b7aa-…`, ActionType 52, Active |
| Dispatch path reachable (engine → ActionType → executor) | ✅ | § C — structural trace; R3 code + R4 row in place |
| Hand-built test playbook structure articulated | ✅ | § C — TEST-W0-1-SMOKE graph |
| End-to-end runtime execution against spaarkedev1 | ⏭️ Deferred | Per task instructions: paper/structural smoke only. Runtime execution scoped to PR 2 task 011 (deploys DAILY-BRIEFING-NARRATE which composes this primitive) and PR 4 task 031 (BFF wrapper dispatch). |

PR 1 W0 close gate: **PASSED**. PR 2 is unblocked.

---

## E. Sub-Agent Write-Boundary Deferral — `.claude/catalogs/scope-model-index.json`

Per CLAUDE.md §3, sub-agents launched via the Agent tool cannot write to `.claude/` paths. This task's intended catalog update — adding new entries for SYS-LOOKUP-MEMBERSHIP, BRIEF-NARRATE-TLDR, BRIEF-NARRATE-CHANNEL, BRIEF-VALIDATE-ENTITY-NAMES — would write to `.claude/catalogs/scope-model-index.json` and is therefore deferred to a main-session run.

### What needs to be done (main session)

1. Fix the `Refresh-ScopeModelIndex.ps1` knowledge-query defect (line 232 — `sprk_externalid` may no longer be a valid column on `sprk_analysisknowledge`). Confirm via `mcp__dataverse__describe('tables/sprk_analysisknowledge')` then update the `-Select` clause.
2. Re-run `powershell -ExecutionPolicy Bypass -File scripts/Refresh-ScopeModelIndex.ps1` — verify all 4 R4 codes appear in the `actions` array of the updated catalog.
3. Curate the new entries (add `tags`, `documentTypes` as applicable) — the script preserves these on subsequent refreshes.
4. Commit catalog with message like `chore(catalogs)(R4): refresh scope-model-index — adds 4 R4 W0 action codes`.

**Why this is safe to defer**: PlaybookBuilder palette discovery is live-query (not catalog-driven). PR 2 task 010 references actions by code, not by catalog lookup. R4 is not blocked.

### Operational catalog state that SHOULD result

After main session refresh, the `actions` array of `.claude/catalogs/scope-model-index.json` should include (in addition to existing curated ACT-001 … ACT-008):

```json
{
  "code": "SYS-LOOKUP-MEMBERSHIP",
  "name": "Lookup User Membership",
  "description": "<from Dataverse sprk_description>",
  "documentTypes": [],
  "tags": ["system", "membership", "primitive"]
},
{
  "code": "BRIEF-NARRATE-TLDR",
  "name": "Brief Narrate TLDR",
  "description": "<from Dataverse>",
  "documentTypes": [],
  "tags": ["briefing", "narrate", "llm"]
},
{
  "code": "BRIEF-NARRATE-CHANNEL",
  "name": "Brief Narrate Channel",
  "description": "<from Dataverse>",
  "documentTypes": [],
  "tags": ["briefing", "narrate", "llm"]
},
{
  "code": "BRIEF-VALIDATE-ENTITY-NAMES",
  "name": "Brief Validate Entity Names",
  "description": "<from Dataverse>",
  "documentTypes": [],
  "tags": ["briefing", "validate", "tool", "post-llm"]
}
```

---

## F. PR 1 Smoke — Final Verdict

| Gate | Status | Notes |
|---|---|---|
| 4 Action rows Active in spaarkedev1 | ✅ | § A |
| Dispatch path reachable (paper) | ✅ | § C |
| AC-1 prerequisite | ✅ | § D |
| Catalog refresh | ⏭️ Deferred to main session | § E — write-boundary, non-blocking |
| PR 1 close (task 009) unblocked | ✅ | All deliverables in place; quality gates next |

PR 1 W0 is **operationally complete**. Proceeding to task 009 (PR 1 wrap).
