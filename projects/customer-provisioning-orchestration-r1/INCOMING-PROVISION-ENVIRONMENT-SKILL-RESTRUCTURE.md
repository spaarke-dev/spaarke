# 📨 INCOMING — `provision-environment` skill restructured into scripts; your review and a dev dry run are needed before merge

> **From**: the skill-authoring-standard change (main session, 2026-11-03) · **Date**: 2026-11-03
> **To**: whoever next picks up `customer-provisioning-orchestration-r1` (operator or agent)
> **Status**: 🟡 **PROPOSED — NOT MERGED.** Branch `chore/provision-environment-scripts`, draft PR (linked from the
> tracking issue labelled `customer-provisioning-orchestration-r1`). The owner chose to ship it separately so that
> **this project reviews it and dry-runs it against dev** before it replaces the live skill.
> **Why you are reading this**: you own the procedure this skill drives, and ~790 lines under this project cite its
> step ids.

---

## 0. TL;DR

1. `.claude/skills/provision-environment/SKILL.md` goes from **3,057 lines (229 KB, ~57k tokens per invocation) to
   437**. The inline PowerShell is now **17 scripts** in `scripts/provision-environment/` (a shared module
   `ProvisionEnvironment.psm1` + one script per step group) and two in `scripts/provisioning-prereqs/`
   (`Invoke-PrereqPass.ps1`, `Test-SpeTopology.ps1`). Detail moved to `references/` (intake fields, fallback matrix,
   fresh-sub feasibility, troubleshooting, manual gates, Copilot agent gate).
2. **Every step id is preserved** as a heading (0 … 7d, including 1a-bis … 1e-ter, 4.0, 6a–6f). Your notes, POMLs and
   `validate.ps1` citations stay valid.
3. **State now passes explicitly**: the harness PowerShell tool keeps no variables between calls, so the old
   cross-block `$script:` variables could not work. A session file (`runs/provision-session-*.json`, git-ignored) is
   passed to every script; results come back as exit codes and `KEY=` stdout lines; exit 7 = "ask the operator in
   chat" (the tool has no stdin).
4. **Static checks only.** Every script parses with 0 errors and `Invoke-PSAnalysis.ps1` reports 0 errors (5 warnings,
   explained in the move map). **No live path has run** — nothing touched Azure, Dataverse, Key Vault or L2.
5. While extracting, the agent found **live defects in the current skill** (§2). Eight are fixed in the scripts; seven
   need your decision.

## 1. What we need from you

- [ ] Review the branch: `SKILL.md`, `references/`, the scripts, and the full section-by-section map at
      `.claude/archive/2026-11-03/skills/provision-environment/MOVE-MAP.md` (original archived verbatim beside it).
- [ ] Dry-run it against **dev** end to end (interactive and batch), including a resume (`-ResumeRunId`) and a gate
      advance. This is the gate for merging.
- [ ] Decide D9–D15 (§2) and whether to merge `provisioning-runs/_templates/handoff-report.md` with the new
      `scripts/provision-environment/templates/handoff-report.md`.
- [ ] Merge the PR (or tell the owner what blocks it). Until then the live skill is the 3,057-line version; the new
      skill-length guard baselines it, so nothing fails in the meantime.

## 2. Defects found in the current skill

Two were re-verified against the code by the main session: **D3** (`DataverseEnvironmentRecord.cs:27` `Ready = 2`;
the skill writes the string `'Ready'` at archived SKILL.md L2175/L2204) and **D13** (`RunsEndpoints.cs` has create,
preflight, get, gate advance, resume, cancel, clear-quarantine — no `/quarantine`).

**Fixed in the scripts** (the original text survives in the archive):

| Id | Defect |
|---|---|
| D1 | `Read-Host` prompts cannot work in the non-interactive PowerShell tool |
| D2 | `pac data query/create/update` do not exist (pac 1.46.1 has only `export`/`import`); the Web API fallback is now the path |
| D3 | Step 6a writes `sprk_setupstatus = 'Ready'` (string) to an option set whose Ready value is `2` |
| D4 | Interactive mode hard-coded `$env = "dev"`, never ran `once_per_env` prereqs, and 0.5c stopped on a null container type |
| D5 | Batch mode read `ProvisioningRun` fields that do not exist → null diagnostics; the H8 25-hour cap could never apply |
| D6 | Interactive `$operatorUpn` was never set, yet Steps 4.0 and 7a use it |
| D7 | The handoff report put the BFF version in the solution-version slot |
| D8 | Step 7a copied the postmortem template into a folder it never created |
| D16 | `--resume <runId>` was documented but never implemented |

**Reported, not changed — your call:**

| Id | Defect |
|---|---|
| D9 | Exit code 5 means both `Cancelled` (4b) and registry-stale (6c) |
| D10 | Step 7c appends a 4-column row to `provisioning-runs/INDEX.md`, whose table has 10 columns |
| D11 | Contradictions: F1 recipes write `sprk_currentrunid = null` (against the 6a single-writer rule); "use `/resume` for gates" vs Step 5's advance; `GET /api/runs?customerId=` is not a route |
| D12 | Step 2.5 F3 references `refs/test-s1-plan.bicep` (does not exist) and undefined `$tempRg` / `$candidateRegion` |
| D13 | "Abandon" sends `POST /api/runs/{id}/quarantine`, a route that does not exist (references now say `cancel`) |
| D14 | Interactive Step 2 schema re-validation cannot pass before Step 3 (`confirmationAcknowledgment` is required) |
| D15 | The Step 2 preflight sample shows server-side checks the client cannot produce (now labelled illustrative) |

Small behaviour additions (also in the move map): Step 0 reports every failed check before stopping; `tenantId` is
validated as a GUID; batch `autoResumeOnce` skips a dry-run end; the postmortem records prereq results;
`Start-ProvisioningRun.ps1` refuses a second POST.

## 3. Context

The restructure is one part of a repo-wide skill authoring standard (500-line cap; conditional branches in
`references/` behind explicit "If …, read …" directives; a ratcheted length guard,
`scripts/quality/check-skill-length.py`). That standard ships in its own PR; this one was split out because it adds
never-run scripts to a production procedure.
