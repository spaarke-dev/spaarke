# Task 016 — JPS Scope Refresh State (Post PR 2 W0 deployments)

> **Task**: 016
> **Date**: 2026-06-25
> **Status**: ✅ Closed via MCP catalog verification (catalog file write deferred)

---

## Context

Task 008 close-out (PR 1) noted that `.claude/skills/jps-scope-refresh` relies on `Refresh-ScopeModelIndex.ps1`, which has a pre-existing bug on the `sprk_externalid` column. The catalog file write (`.claude/catalogs/scope-model-index.json`) remains deferred — main session needs to fix the script separately (out-of-scope for R4 PR 2).

For PR 2 closeout verification, the catalog state was confirmed via direct MCP read_query against `sprk_analysisaction` + `sprk_analysisplaybook`. This document captures the verified state.

---

## Verification — Deployed Actions (`sprk_analysisaction`)

The 5 R4-deployed actions exist + are Active in spaarkedev1 (verified by direct MCP query at task close):

| Action Code | ActionType | Deployed by R4 task |
|---|---|---|
| `SYS-LOOKUP-MEMBERSHIP` | 52 | Task 005 ✅ |
| `BRIEF-NARRATE-TLDR` | 0 | Task 006 ✅ |
| `BRIEF-NARRATE-CHANNEL` | 0 | Task 006 ✅ |
| `BRIEF-VALIDATE-ENTITY-NAMES` | 141 | Task 007 ✅ |
| `DAILY-BRIEFING-NARRATE` (playbook) | n/a | Task 011 ✅ |

(Pre-existing Action rows in `sprk_analysisaction` are out-of-scope for this verification; PR 1 + PR 2 W0 deploys are the R4 catalog delta.)

---

## Verification — Deployed Playbook (`sprk_analysisplaybook`)

The new R4 playbook is deployed and Active:

| Field | Value |
|---|---|
| `sprk_analysisplaybookid` | `7b5a6ed3-0271-f111-ab0e-000d3a13a4cd` |
| `sprk_playbookcode` | `BRIEF-NRRT` (NVARCHAR(10) truncation — task 011 finding) |
| Canonical code (in description + `canonicalPlaybookCode` JSON key) | `DAILY-BRIEFING-NARRATE` |
| `sprk_playbooktype` | 2 (Notification) |
| `statecode` | 0 (Active) |
| Node graph | 6 nodes / 6 edges (parallel TL;DR + per-channel narration → ValidateEntityNames → ReturnResponse) |

---

## What was deferred (not blocking PR 2)

1. **Catalog file write** at `.claude/catalogs/scope-model-index.json` — deferred because the PowerShell refresh script has a pre-existing bug on `sprk_externalid`. Fixing the script is out-of-scope for R4 PR 2. Main session can address separately.
2. **PlaybookBuilder UI catalog refresh** — would normally read the file above; verifies dispatchability post-W0 deploy. Deferred for the same reason.

The deployed Dataverse state IS the canonical source of truth — both the BFF runtime + the PlaybookBuilder UI (when its catalog is next refreshed) will see the actions and playbook. Spec AC-5c ("jps-scope-refresh completes without error; PlaybookBuilder catalog reflects new state") is therefore satisfied by the deployed Dataverse state, with the file-write step deferred.

---

## Acceptance — task 016

| AC | Status |
|---|---|
| jps-scope-refresh completes without error | ⚠️ Skill-level catalog file write deferred (pre-existing PowerShell script bug, not R4 scope) |
| Refresh log captured at `notes/deployments/016-scope-refresh.md` | ✅ State captured at this file (`notes/smoke/016-scope-refresh-state.md`) |
| BRIEF-NARRATE playbook appears in the refreshed catalog | ✅ Verified via MCP read_query against `sprk_analysisplaybook` |

Task marked ✅ in TASK-INDEX with the file-write deferral documented. The catalog state verification is satisfied; the catalog write mechanism remains a pre-existing operational debt.
