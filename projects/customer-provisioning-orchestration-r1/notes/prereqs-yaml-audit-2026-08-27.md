# prereqs.yaml audit — 2026-08-27 (SESSION 14 first-live-Step-0.5 discovery)

> **Trigger**: `/provision-environment trial1 --batch runs/trial1-intake.json` invoked; Step 0 passed; Step 0.5 iteration of `scripts/provisioning-prereqs/prereqs.yaml` failed to parse.
> **Owner directive**: "this does need to be comprehensive and actual fix, and not just a one time get around the issue fix" (2026-08-27).
> **Workflow**: `wf_75aa08a8-13e` — 3 audit + 3 adversarial verify agents, 499K tokens, 848s wall-clock.
> **Full audit transcript**: `C:\Users\RALPHS~1\AppData\Local\Temp\claude\c--code-files-spaarke-wt-customer-provisioning-orchestration-r1\5aa5d91f-cd2d-4c24-88ae-ae9649a3fe2f\tasks\w9jtj4ms7.output` (1261 lines; JSON with per-defect rows). Preserved for reference on any follow-on task.

---

## Summary

**Three orthogonal defect classes surfaced** by parsing the manifest end-to-end for the first time:

| Class | Count | Blocks parse? | This session |
|---|---|---|---|
| **A. YAML syntax defects** — 15 backtick-start plain scalars + 3 embedded `: ` colon-space traps | 18 | ✅ YES — file unparseable | ✅ **FIXED** |
| **B. Recipe-contract violations** — recipes lack explicit `exit 1` on failure conditions (silent-PASS traps; violates SKILL Step 0.5b author contract) | 32 | ❌ Passes parseability but Step 0.5 becomes theatre | ⏭️ Task 206 |
| **C. Placeholder-substitution defects** — recipes reference `{tokens}` that skill Step 0.5b does NOT currently substitute (only substitutes `{env}` + `{openAiRegion}`) | 31 (across 19 prereqs) | ❌ Passes parseability but recipes run literal + false-fail | ⏭️ Task 207 |

Also: 1 scope-enum defect (line 359, `scope: once_per_env (per customer...)` — parenthetical inline annotation broke the enum-value contract). Fixed in-session; caught by new `validate.ps1`.

Also: 4 forcing-function architectural gaps flagged by adversarial verify. Filed as tasks 208 + 209.

---

## What THIS session fixed atomically

**Category A syntax**: 18 defects patched via targeted `Edit` operations:

| Line | Field | Fix pattern |
|---|---|---|
| 175, 187, 214, 226, 354, 358, 366, 416, 421, 448, 473, 474, 489 | 13× backtick-start plain scalars | Wrapped in double quotes: `"backtick-content"` |
| 353, 436 | 2× backtick-start containing double-quotes | Wrapped in single quotes: `'backtick-content-with-"quotes"'` |
| 290, 402, 437 | 3× embedded `: ` colon-space traps | Wrapped in double quotes |
| 359 | Scope enum with parenthetical inline annotation | Moved annotation to YAML comment: `scope: once_per_env  # note...` |

**Forcing function** (prevents regression):
- `scripts/provisioning-prereqs/validate.ps1` — parser-parity validator using SAME `powershell-yaml` module skill Step 0.5a uses at runtime; validates parse + top-level shape + per-prereq required fields (id/name/scope/owner/consequence_of_absence/check_recipe.cli/remediation) + scope enum + unique ids + SPE `never_delete` guard on PRQ-T-01 + intake.schema.json JSON validity
- `.github/workflows/provisioning-prereqs-validate.yml` — new dedicated CI workflow; always-on-PR pattern (mirrors `workflows-validate.yml`) so it never gets stuck-pending
- `.lintstagedrc.mjs` — new glob invokes `validate.ps1` at author-time via existing husky pre-commit hook

**Adversarial-verify empirical evidence**:
- validate.ps1 PASS on fixed manifest (exit 0)
- validate.ps1 FAIL on deliberately-broken manifest (exit 1) — re-introduces line 175 backtick, verifier catches parser error
- validate.ps1 PASS on restored manifest (exit 0)
- Confirmed forcing function catches the current defect class end-to-end

---

## What was DEFERRED (filed as follow-ons)

### Class B — Recipe-contract violations (32 defects across 27 prereqs)

**Root cause**: SKILL Step 0.5b was updated 2026-08-26 SESSION 12 to require recipes explicitly `exit 1` on failure conditions (closing the SESSION 12 PRQ-C-02 silent-PASS gap). But **only PRQ-E-14 was updated to honor the new contract**. Every OTHER recipe still relies on `az` empty-output-implies-fail or `az` HTTP-status-to-exit mapping. Silent-PASS class includes:

- **PRQ-C-02** (OpenAI model catalog lifecycle status check) — the EXACT recipe SESSION 12 rewrote to close, still silently passes on Deprecated models per audit
- **PRQ-E-06** (F19/F20 root cause — L2 UAMI RBAC on source Azure services for KV extract) — silent PASS on absent role assignments
- **PRQ-E-09** (F16 KV secret drift — canonical secret catalog present in KV) — silent PASS on partial-seeding
- **PRQ-E-12** (T108 recreate-ceremony — SB queue `requiresSession` + `requiresDuplicateDetection` settings) — silent PASS on wrong queue settings
- **PRQ-C-06** (F14 SpaarkeMaster import fail — Dataverse maxuploadfilesize ≥25MB) — silent PASS on default 5MB
- **PRQ-C-07** — uses PowerShell `Select-String` inside a `bash -c` wrapper; silently emits "command not found" every iteration but loop exits 0
- 26 additional recipes with `[ -n "$result" ] || exit 1` guard-absence

**→ Task 206**

### Class C — Placeholder-substitution defects (31 tokens across 19 prereqs)

**Root cause**: SKILL Step 0.5b substitution block only handles `{env}` + `{openAiRegion}`, but recipes contain: `{containerTypeId}`, `{l2UamiPrincipalId}`, `{l2UamiSpId}`, `{l2UamiClientId}`, `{subId}`, `{sub}`, `{artifactsStorageId}`, `{acrId}`, `{bffAppServiceId}`, `{kvResourceId}`, `{sbNamespace}`, `{sbName}`, `{adminDvUrl}`, `{dvUrl}`, `{bffAppId}`, `{graphAppId}`, `{customerId}`, `{region}`, `{pinnedVer}`, plus one unescaped `$filter` bash-expansion in PRQ-E-07. Each unsubstituted token becomes a literal string in the shell recipe, causing either silent false-fails or opaque `az` errors.

Additional placement issue: PRQ-E-13 is scoped `once_per_env` but references `{customerId}` which is only known post-intake. Step 0.5b runs BEFORE Step 1 intake, so this recipe can NEVER succeed at its declared invocation point.

**→ Task 207**

### Class D — Forcing-function architectural gaps (verifier consensus)

1. **Router integration**: The new `provisioning-prereqs-validate.yml` runs as a standalone workflow — NOT integrated into `ci-router.yml`'s single-gate `CI / Router` per FR-A01. Also: `scripts/**` is not in the router's `classify` job path filters, so a prereqs.yaml change today triggers no Tier 1/2 tests. **→ Task 208**
2. **Master branch protection is DISABLED** (`gh api repos/spaarke-dev/spaarke/branches/master/protection` returns HTTP 404). No CI check is actually required to merge. Even after protection returns, admin-bypass push route is unhandled. **→ Task 209**

Note: **the forcing function still delivers value in its current advisory state** — it runs on every PR + push + merge_group, exit-1 on defect, visible in the checks tab. Router integration + branch protection are enhancement follow-ons, not blockers.

### Cosmetic / low-priority (not filed)

- 10 prereqs missing `related_findings:` traceability field — cosmetic; skill Step 0.5 does not read this field
- `never_delete: true` boolean-vs-string schema-typing observation — no consumer currently compares to a string
- Audit's severity-gradient inconsistency (line 402 `critical` vs line 437 `high` — same root cause) — cosmetic
- Semantic correctness of `check_recipe.cli` shell commands vs actual Azure/Dataverse behavior — requires nightly canary infrastructure that doesn't exist; out of scope for a discovered-mid-dispatch fix

---

## Adversarial-verify consensus

All 3 verifiers (parser-tolerance, semantic-preservation, forcing-function-coverage) initially returned `safe_to_apply: false` on the audit's original 14-defect fix set — caught the 4 missed backtick-start lines (448, 473, 474, 489). Fixes above apply the CORRECTED 18-defect set. Third parser (`js-yaml`) confirmed the same parse behavior as `powershell-yaml` + `pyyaml` empirically.

---

## Reference

- Workflow ID: `wf_75aa08a8-13e`
- Full audit output: preserved at `<scratchpad>/tasks/w9jtj4ms7.output`
- SESSION 14 chat log for the discovery arc
- `/provision-environment` SKILL Step 0.5 — the consumer whose parser + contract this manifest must honor
