# Current Task State - spaarke-legal-ai-accelerator-r1 (tiered package: individual base/advanced)

> **Last Updated**: 2026-11-10 (by context-handoff, Claude Desktop session `acclerator-r1`)
> **Recovery**: Read "Quick Recovery" section first
> **Branch**: `work/spaarke-legal-ai-accelerator-r1` · **Worktree**: `C:\code_files\spaarke-wt-spaarke-legal-ai-accelerator-r1` · **PR**: #2528 (unmerged)
> Earlier checkpoints (verbatim): `notes/handoff-history/2026-11.md`. Session narrative: `git log`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Build and test the **four-tier package** (individual base/advanced, team no-IT/IT) from `notes/accelerator-components-by-surface.md` (DRAFT v2). Pre-pipeline spike work; no POML tasks |
| **Step** | (a) OneDrive clean-up DONE (cloud moves; `notes/poc-e2e/onedrive-layout.md`). (b) Five build items: package build `src/accelerators/build/Build-Package.ps1` (legal hold Cowork skill, readable playbook renderer, Spaarke Review Method SRM-1110-A, zip laid out like `Documents/`, agent recipes) — verify + commit. (c) Owner script `notes/poc-e2e/tier-test-script.md` (W-1, G-1..G-4, H-1, P-1, C-1..C-3, A-1..A-3) |
| **Status** | waiting on owner to run the tier test script |
| **Next Action** | Put the two zips where the owner can extract them; when results arrive, record a "Tier tests round 1" section in `results.md` |

### Critical context
- **Microsoft folders:** `Documents/Cowork/skills/<name>/SKILL.md` is the only documented auto-loaded location (Cowork FAQ). `Cowork/Tasks|Projects|AGENTS.md` are Cowork's. `Documents/Copilot/Created` = Copilot outputs; `Copilot/Memory/v2` = undocumented memory store. Nothing reads instructions from `Documents/Copilot`. Our files: `Cowork/Skills/` + `Legal Knowledge/` + `Spaarke Test Documents/`.
- **OneDrive sync on the owner's PC is broken** since ~2026-11-07 (downloads fail: "cloud file provider exited unexpectedly"); all clean-up was done in the cloud via the M365 connector. Owner must restart OneDrive before installing.
- **Archive:** OneDrive root `Spaarke test archive (not current)/` (still searchable → tests identify files by verification code). `Copilot/Created/_before-Act2/` still to move (423 locked).
- Owner's original protocol (PBR-1109-A) copied to `src/accelerators/core/shared-references/review-method.owner-v1.0.md`.

---

## Next Actions (in order)
1. Verify the package build (tests, file list), commit + push; give the owner the zips.
2. Owner runs `tier-test-script.md`; Claude records results in `results.md` ("Tier tests round 1") and updates the component doc statuses.
3. Owner decisions pending: adopt the four tiers (revokes "no declarative agents" for the advanced tiers only).
4. Team-tier script (T-1..T-4, needs a second non-owner user) and IT-tier script (I-1..I-3, needs admin).
5. Still open: gap #35 (draft A version check); DA-1 package naming (" DA" suffix) — superseded by Agent Builder recipes unless I-2 runs; Cowork item 11 (owner's legal hold step in Word for the web) result never recorded; backlog X9, X12, X13, X14, gaps #27/#29.

## Open Owner Questions / Blockers
- Results of the tier script. Four-tier decision. Design ODs (OD-20..25 with `mcp-r1`, OD-39..43), policy-as-input, saved Word playbooks (gap #32).

## Related sessions and projects
- **r2 Claude accelerator:** `spaarke-legal-ai-accelerator-claude-r2`, Desktop session `accelerator-claude-r2`; reuses `src/accelerators/`; keep engines Python 3.11-compatible.
- **MCP / Copilot surface:** `spaarkeai-mcp-bff-api-r1`, Desktop session `mcp-r1`. Don't edit its worktree from here.
- **Ontology:** `spaarke-ontology-platform-r1`, session `ontology-r1`; terminology originates in its §3 (D-209).
- **Provisioning:** `customer-provisioning-orchestration-r1`, session `provisioning-r1` (T257 Copilot agent packaging).
- This worktree's own memory (`~/.claude/projects/c--code-files-spaarke-wt-spaarke-legal-ai-accelerator-r1/memory/`) is NOT auto-loaded — read it at start.

## Standing directives (owner) — keep until revoked
- Never commit secrets. Keep the Corteva NDA and the real RPX/Helios NDA out of the repo. Never upload `answer-key/`.
- Never run python in Bash; use `C:\Python313\python.exe -B` from PowerShell. No bare `git stash`.
- Sub-agents can't write `.claude/`. Every Agent launch states a model.
- Skills and scripts embed no secrets and make no LLM or network calls.
- Claude never types passwords and never sends email; choose "No email" on Cowork cards.
- Leave the owner's tabs alone. Prefer moving over deleting; no permanent deletes by Claude. Ask before T-04 (share).
- No Copilot Tuning in design.md yet. No declarative agents in the package **(under review: four-tier draft puts Agent Builder agents in the advanced tiers)**.
- The Office add-in for redlining is out of scope; don't raise it.
- Two editions/components (Word vs Cowork) are wanted.
- Mechanics first; scoring and quality later (owner, 2026-11-10).
- Current test files live only where the package puts them; earlier files go to `Spaarke test archive (not current)/`, never mixed in (owner, 2026-11-10).
- Terminology changes originate in ontology §3. "Playbook" = the standard procedure for any document type (D-209).
- `--no-verify` only on master-merge commits.

## Tooling
- **Build:** `./src/accelerators/build/Build-Accelerator.ps1 -Practice nda`; package: `./src/accelerators/build/Build-Package.ps1`. Tests: `python -B -m unittest discover -s build/tests` and `-s core/engines/tests` from `src/accelerators`.
- **OneDrive/SharePoint in the cloud:** M365 connector tools (`sharepoint_move_item`, `sharepoint_create_folder`, `read_resource` with `file:///<driveId>/<path>`). OneDrive driveId `b!doU18j_zUUuipR60_RrF-fXvUOXGbW1OlwiIkv-_T8mVMlP029gNR48sqbO5iayh`; Acme team site driveId `b!67wowiTUC0uH8Yz4yzGcyzoNRS0j4_1MtT9naOptnCQ7B3LE8mwuTbI8aqzlzbpX`. 423 = file open somewhere.
- **Repo:** root `.gitignore` ignores `build/`; `src/accelerators/.gitignore` re-includes it (but `build/out` stays untracked).
