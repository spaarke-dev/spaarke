# Task 005 — SYS-LOOKUP-MEMBERSHIP Action Row Deployment Smoke

> **Date**: 2026-06-25
> **Task**: 005-deploy-sys-lookup-membership-action-row
> **Phase**: PR 1 (W0 JPS Action rows + EntityNameValidator)
> **Spec ref**: R4 FR-1 (spec line 109-110), AC-1

---

## a) MCP Availability

**YES** — Dataverse MCP server is available + connected to spaarkedev1 in this worktree session. Verified at task start via `mcp__dataverse__read_query` returning a successful result (empty array — row did not exist yet, exactly the expected idempotent pre-deploy state).

This **closes the carry-forward** from `notes/debug/001-mcp-smoke-deferred.md` (task 001 had deferred MCP smoke to task 030). Task 005 is the first task in PR 1 that actually exercised MCP against spaarkedev1; the smoke succeeded, so task 030's cross-purpose smoke (re-verifying spaarkedev1 access + SYS-LOOKUP-MEMBERSHIP presence) is now de-risked.

---

## b) Deployment Outcome

**CREATED** (no prior row existed; clean create — no upsert/update path exercised).

| Field | Value |
|---|---|
| `sprk_analysisactionid` (Dataverse GUID) | `ca44b7aa-fc70-f111-ab0e-7ced8ddc4cc6` |
| `sprk_actioncode` | `SYS-LOOKUP-MEMBERSHIP` |
| `sprk_name` | `Lookup User Membership` |
| `sprk_executoractiontype` | `52` |
| `sprk_temperature` | `0` |
| `statecode` | `0` (Active) |
| `statuscode` | `1` (Active) |

**Tool**: `mcp__dataverse__create_record` against `sprk_analysisaction` (logical name).

**Pre-create idempotency check**: Two parallel read_queries — one by `sprk_actioncode = 'SYS-LOOKUP-MEMBERSHIP'`, one by `sprk_executoractiontype = 52`. Both returned `[]` (no row). Safe to create.

---

## c) Verification (AC-1)

Per AC-1: "mcp__dataverse__read_query SELECT WHERE sprk_executoractiontype = 52 returns exactly 1 row."

**Query executed**:
```sql
SELECT sprk_analysisactionid, sprk_actioncode, sprk_executoractiontype,
       statecode, statuscode, sprk_name, sprk_temperature
FROM sprk_analysisaction
WHERE sprk_executoractiontype = 52
  AND sprk_actioncode = 'SYS-LOOKUP-MEMBERSHIP'
```

**Result**: 1 row returned, matching expected canonical values (see table above). Both `statecode` and `statuscode` resolve to "Active" name labels. ✅ AC-1 satisfied.

**Additional verifications**:
- AC-1 row count = 1 (also confirmed via the broader `WHERE sprk_executoractiontype = 52` query — 1 row, the row we created).
- `sprk_temperature = 0` (system action — not LLM-rendered). Compliant with the "this is a data-ops action, not AiAnalysis" framing.

---

## d) Manual Re-Run Payload (if needed)

If a future operator must re-run this deployment manually (e.g., environment refresh), the canonical JSON source is at:

`projects/spaarke-daily-update-service/notes/playbooks/actions/sys-lookup-membership.action.json`

That file contains the full `sprk_systemprompt` JPS payload + all field values. The MCP call shape:

```
mcp__dataverse__create_record
  tablename: sprk_analysisaction
  item: {
    sprk_actioncode: "SYS-LOOKUP-MEMBERSHIP",
    sprk_name: "Lookup User Membership",
    sprk_description: "<see canonical JSON .fields.sprk_description>",
    sprk_executoractiontype: 52,
    sprk_temperature: 0,
    sprk_systemprompt: "<stringified JSON from canonical JSON .fields.sprk_systemprompt>"
  }
```

The canonical JSON's `dataverseEntity`, `deploymentMode: "upsert"`, and `lookupKey: "sprk_actioncode"` metadata makes the file self-describing for future automation (e.g., a `Deploy-AnalysisActions.ps1` script in the same family as `Deploy-Playbook.ps1`).

**Note on statecode/statuscode**: Not explicitly passed at create time — Dataverse defaults to Active (`statecode=0`, `statuscode=1`) which is what we want. If a future re-deploy needs to flip statecode, use `mcp__dataverse__update_record` against the existing record GUID.

---

## Sign-off

- Canonical JSON: ✅ authored at `projects/spaarke-daily-update-service/notes/playbooks/actions/sys-lookup-membership.action.json`
- MCP available: ✅ yes
- Deployment: ✅ created (no prior row; clean create)
- Verification: ✅ AC-1 satisfied (1 row, Active, executoractiontype 52, code SYS-LOOKUP-MEMBERSHIP)
- Manual re-run path: ✅ documented above (canonical JSON is self-describing)

Task 005 closed. Unblocks task 011 (Deploy + validate DAILY-BRIEFING-NARRATE playbook) — the SYS-LOOKUP-MEMBERSHIP Action row referenced by the upcoming playbook nodes is now present in spaarkedev1.
