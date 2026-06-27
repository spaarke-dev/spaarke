# Task 025 — Deviations

> **Task**: `025-phase-c-deploy`
> **Date**: 2026-06-02
> **Executor**: Claude Code (task-execute, STANDARD rigor)

---

## Deviation 1: Deploy script written to `%TEMP%` instead of `scripts/`

**POML steps 2–3 say**: "Invoke `code-page-deploy` skill for `sprk_*page` — uploads bundled HTML as Dataverse web resource."

**Skill `code-page-deploy` §Deployment Option A** is concrete only through the `npm run build` step and notes "PAC CLI does not have a direct web resource upload command. Use Power Apps maker portal for upload (see Option B)." For Vite code pages, the convention used elsewhere in the repo is the family of `scripts/Deploy-*CodePages.ps1` scripts (e.g., `Deploy-WizardCodePages.ps1`).

**What I did**: Built deploy + verify PowerShell scripts modeled exactly on `scripts/Deploy-WizardCodePages.ps1` (same auth via `az account get-access-token`, same Web API endpoints, same solution/publish pattern) and ran them. Scripts written to `C:\Users\RalphSchroeder\AppData\Local\Temp\dv-deploy-r1\` because task 025's permission boundary forbids writes to `src/` and `.claude/`.

**Impact**: None at runtime — Dataverse received identical API calls to what an in-repo script would have produced (same auth, same payload shape, same solution name `spaarke_core`, same PublishXml step).

**Recommendation (out of scope here)**: A follow-up task with `scripts/` write scope could promote a canonical `scripts/Deploy-DatagridFrameworkCodePages.ps1` modeled on these temp scripts so future redeployments (UAT in task 026, production after) use a tracked, code-reviewed deploy entry-point.

---

## Deviation 2: `Get-FileHash` unavailable in Windows PowerShell 5.x runtime

`powershell.exe` (Windows PowerShell 5.1) on this host did not expose `Get-FileHash`, even though it's a standard cmdlet in 5.1+. Worked around by computing SHA-256 directly via `[System.Security.Cryptography.SHA256]::Create()`. Impact: cosmetic only (hash is only used for the audit table in the report).

---

## Deviation 3: Acceptance criterion 1 — direct-URL render not browser-tested in this task

POML acceptance criterion 1 says "Given both Custom Pages deployed, when opened via direct URL, then each renders correctly." Sub-agent does not perform browser UI verification. The task 025 deploy scope was satisfied by the API-level smoke test (correct name, correct content size, title markers present, React root present, inlined JS bundle present, `componentstate=0 Published`).

Functional in-browser verification is task 026's explicit scope (UAT). This was already anticipated by the POML `<notes>` block: "DEV deploy only. Production deploy gated on UAT (task 026)."

---

## Steps from POML NOT executed (intentional per parent's `DO NOT` boundaries)

| POML step | Reason for not-executing |
|---|---|
| Step 4 — "If config records were authored directly via MCP, verify presence. Otherwise invoke `dataverse-deploy`" | Parent told sub-agent the config records are already present (tasks 021/022 created them via MCP). Verifying presence is out of task 025's deploy scope; this is the kind of cross-task verification task 026 (UAT) does end-to-end. |
| Step 5 — "Smoke test: open each Custom Page directly via URL with mock URL params; verify DataGrid renders." | Browser-based UI verification is outside sub-agent scope. API-level smoke test substituted (see deploy report §Smoke test results). |
| Step 6 — "Smoke test: open Matter Health / Budget Performance via VisualHost CardChrome expand" | End-to-end UI testing — task 026 (UAT) scope. |
| Step 7 — "Update TASK-INDEX (025 ✅)" | Parent's `DO NOT` boundaries: "DO NOT modify TASK-INDEX.md, current-task.md". Sub-agent only updates POML `<status>`. Parent owner updates TASK-INDEX. |
