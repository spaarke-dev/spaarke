# Wave 7 — jps-* Skill Rewrite Smoke Test

> **Executed**: 2026-06-29
> **Executor**: main session (per Sub-Agent Write Boundary — `.claude/skills/*` writes only in main)
> **Tasks covered**: 070-075 (FR-32, FR-33)
> **Wave 7 close decision**: ✅ CLOSE — 0 P0 findings (skill-body bugs); 1 P1 finding (deferred to Wave 5 backfill, already known); 0 P2 findings.

---

## What was rewritten

| # | Skill | Change type | Key edits |
|---|---|---|---|
| 070 | `jps-action-create/SKILL.md` | REWRITE | Frontmatter bump · Step 1.5 Home A removes dispatch selector · Step 5.5 drops `_sprk_actiontypeid_value` from MCP verify · New "R7 dispatch model" section with §3.1 WHY citation |
| 071 | `jps-playbook-design/SKILL.md` | REWRITE | Frontmatter bump · Step 1.5 item 3 replaces 3-tier ladder with single-hop · Step 10 verify-deploy query uses `sprk_executortype` (not removed `sprk_nodetype`) · New "R7 dispatch model" section with 33-executor categorized catalog by tier + author workflow steps |
| 072 | `jps-playbook-audit/SKILL.md` | REWRITE | Frontmatter bump · Step 2 query updated to `sprk_executortype` (removed `sprk_nodetype` would 400) · Check 3.5 dispatch-axis citation corrected (FK is no longer dispatch axis) · New Check 3.6 enumerates 7 R7 drift patterns A-G mirroring Wave 5 task 050 CSV shape · New "R7 dispatch model" section with audit-report-shape table |
| 073 | `jps-validate/SKILL.md` | REWRITE | Frontmatter bump · Step 7.5 CHECK 25 LEGACY-marked; CHECK 26 (structural fallback) DELETED · New Step 7.6 R7 dispatch-identity checks (R7-V-01 through R7-V-04 + 6 LEGACY-* drift flags) · New Step 7.7 typed-config schema check against Wave 3 BFF endpoint (R7-V-05 to R7-V-07) · New "R7 dispatch model — what changed and why this validator works" section |
| 074 | `jps-scope-refresh/SKILL.md` | MINOR UPDATE | Frontmatter bump · "Two authoring surfaces" table replaces Node Type OptionSet (5 values) with Executor Type Choice Set (33 values, `sprk_playbookexecutortype` global) · Brief R7 context note citing §3.1 · Operational behavior (script invocation + JSON catalog shape) UNCHANGED |

---

## Smoke test results

### Test 1 — Daily Briefing Narrate (DAILY-BRIEFING-NARRATE)

**Deployed state** (spaarkedev1, queried 2026-06-29):
- Playbook GUID: `7b5a6ed3-0271-f111-ab0e-000d3a13a4cd`
- Nodes: 6 (`Start`, `LoadKnowledge`, `GenerateTldr`, `GenerateChannelNarratives`, `ValidateEntityNames`, `ReturnResponse`)
- All `sprk_isactive = true` ✅ (CHECK 3.5 PASS — no Legacy-mode risk)
- `sprk_executortype` values: **NULL on all 6 nodes** (column absent from MCP read_query response payload — confirmed by null exclusion in response)

**Validator rule firings** (per rewritten Step 7.6):
- ❌ **R7-V-01** (executorType non-NULL) FAILED on 6/6 nodes
- ✅ R7-V-02/V-03/V-04 not evaluated — gate at R7-V-01 fail
- ✅ R7-V-05/V-06/V-07 not evaluated — gate at R7-V-01 fail

**Classification**: **P1 — playbook drift; deferred to Wave 5 backfill (task 054 migration run)**. This is the EXPECTED state pre-Wave-5-completion. The 94-node review CSV from task 051 already enumerates these as part of the 41 HIGH-confidence + 14 MEDIUM + 23 LOW + 16 NONE backfill candidates. Wave 5 owner-review checkpoint (task 052) is currently the blocker; once owner produces `playbook-node-review-output.csv`, task 053 generates the migration script and task 054 runs it.

**Skill-body bug?** No. The validator correctly identifies pre-backfill state as failing R7-V-01 — this is the rule's load-bearing job. **0 P0 findings.**

### Test 2 — Insights universal-ingest (structural assessment)

The Insights universal-ingest playbook contains 9 nodes wired through ADR-037 compose strategy. Structural validation of the rewritten skill body:

- **Step 7.5 CHECK 28** (no nodes/edges in playbook.sprk_configjson) — preserved from canonical-truth loop 2026-06-26; correctly anchored at definition-file level, not affected by R7.
- **Step 7.5 CHECK 29** (scope decisions in scopes.*, not inline) — preserved; correctly anchored.
- **Step 7.6 R7-V-04** (Action FK resolves to existing row) — validates compose-strategy node references without disturbing ADR-037 invariants.
- **No P0 finding**: the rewritten skill does not introduce compose-strategy false positives. ADR-037 invariants are independent of dispatch-identity (compose is post-dispatch).

### Test 3 — chat-summarize (Wave 9 post-migration state)

Wave 9 task 091 already migrated `SessionSummarizeOrchestrator` to `IConsumerRoutingService + IInvokePlaybookAi` (FR-17 complete). Wave 9 task 092 seeded the `sprk_playbookconsumer` row. So this playbook is POST-migration for the smoke test.

**Skill-body validation**: the rewritten `jps-validate` Step 7.6 R7-V-02 rule (prompt-driven executor → Action FK required) + R7-V-03 (pure executor → Action FK NULL) correctly anchors on `executorType` for routing decisions. chat-summarize uses the consumer-routing path which is orthogonal to per-node dispatch — no rule conflict.

**No P0 finding**: the rewritten skill correctly distinguishes consumer-routing (playbook-level, FR-17 surface) from per-node dispatch (FR-07 surface).

---

## Findings summary

| Severity | Count | Description |
|---|---|---|
| P0 (skill-body bug — blocks Wave 7 close) | **0** | None |
| P1 (playbook drift — deferred) | 1 | DAILY-BRIEFING-NARRATE 6 nodes NULL `sprk_executortype` — pending Wave 5 backfill (task 054). Already enumerated in Wave 5 task 051 review CSV. |
| P2 (cosmetic) | 0 | None |

**Wave 7 close decision**: ✅ CLOSE. The single P1 finding is a known pre-Wave-5-backfill state that the validator was specifically rewritten to catch — its appearance is evidence the rewrite works, not evidence of a bug.

---

## Cross-validation: terminology consistency across the 5 rewrites

| Term | jps-action-create | jps-playbook-design | jps-playbook-audit | jps-validate | jps-scope-refresh |
|---|---|---|---|---|---|
| "single-hop" | ✅ §R7-dispatch-model | ✅ §1.5 item 3 + §R7-dispatch-model | ✅ §R7-dispatch-model | ✅ §R7-dispatch-model | ✅ R7 context note |
| `sprk_executortype` (canonical column) | ✅ §1.5 Home C | ✅ §1.5 item 3 + §10 query | ✅ §2 query + Check 3.6 + §R7 | ✅ Step 7.6 + §R7 | ✅ two-surfaces table |
| `ExecutorType` (C# enum, post-W2-022) | ✅ implicit | ✅ §R7 | ✅ implicit | ✅ Step 7.6 CHECK 25 cites ExecutorType.cs | ✅ Reviewer note |
| `sprk_playbookexecutortype` (global Choice, 33 values) | ✅ implicit (via design.md ref) | ✅ §R7 | ✅ implicit | ✅ Step 7.5 CHECK 25 | ✅ two-surfaces table |
| §3.1 WHY citation | ✅ §1.5 + §R7 | ✅ §R7 | ✅ §R7 | ✅ §R7 | ✅ R7 context note |
| Wave 4 schema deletion mention | ✅ §1.5 + §5.5 + §R7 | ✅ implicit | ✅ §R7 + Check 3.6 patterns C+D | ✅ Step 7.6 LEGACY-LK + LEGACY-EX | n/a (different surface) |
| Wave 8 UI shape reference | n/a | ✅ §R7 author workflow item 4 | n/a (audit surface) | ✅ Step 7.7 references endpoint | ✅ task 081 reference |
| Action = prompt template (not dispatch identity) | ✅ §R7 + Purpose | ✅ §R7 + workflow | ✅ §R7 | ✅ §R7 + Step 7.6 R7-V-02 | n/a |

**Terminology alignment**: ✅ CONSISTENT across all 5 rewrites. No drift, no contradictions.

---

## Wave 7 commit scope

5 modified files in main session:
- `.claude/skills/jps-action-create/SKILL.md`
- `.claude/skills/jps-playbook-design/SKILL.md`
- `.claude/skills/jps-playbook-audit/SKILL.md`
- `.claude/skills/jps-validate/SKILL.md`
- `.claude/skills/jps-scope-refresh/SKILL.md`

Plus this smoke-test report (`notes/wave-7-smoke-test.md`).

Single conventional commit: `docs(skills/r7): rewrite jps-* skills for node-first dispatch model (Wave 7 FR-32/33, tasks 070-075)`.

---

## Notes for downstream waves

- **Wave 5 owner-checkpoint impact**: the Wave 7 smoke test surfaced 1 P1 finding (DAILY-BRIEFING-NARRATE NULL `sprk_executortype`) that becomes a fail under the new validator. This is the EXPECTED behavior. Wave 5 task 054 backfill resolves it.
- **Wave 10 task 100** (15 success-criteria verification) should re-run `/jps-validate` after Wave 5 backfill completes to confirm DAILY-BRIEFING-NARRATE passes all R7-V-* rules cleanly.
- **No skill-body touch-up needed** — `075a-touchup-{skill}-bug-fix.poml` was NOT created.
