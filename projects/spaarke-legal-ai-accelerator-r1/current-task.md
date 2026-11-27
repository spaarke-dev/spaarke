# Current Task State - spaarke-legal-ai-accelerator-r1 (tiered package: individual base/advanced)

> **Last Updated**: 2026-11-27 (by context-handoff; Claude Desktop session `acclerator-r1`)
> **Branch**: `work/spaarke-legal-ai-accelerator-r1` · **Worktree**: `C:\code_files\spaarke-wt-spaarke-legal-ai-accelerator-r1` · **PR**: #2528 (draft, unmerged)
> Earlier checkpoints verbatim: `notes/handoff-history/2026-11.md` (the pre-rewrite file of 2026-11-27 is appended there). Narrative: `git log`.
> This worktree's memory (`~/.claude/projects/c--code-files-spaarke-wt-spaarke-legal-ai-accelerator-r1/memory/`) is NOT auto-loaded; read it at start.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Active** | WP-6 (Word desktop: edit list, error channel, code size) with Word probe plugin. Blocked on **uploading the probe**: the admin center (Tools › Upload) refused v0.5, v0.6 and v0.7 with "We couldn't validate this manifest file" |
| **Next action** | Send the owner `Spaarke Word probe plugin v0.7 no script.zip` (built, commit `4c8a6455f1`, in `C:\code_files\spaarke-wt-accelerator-word-js-probe\src\accelerators\build\out\word-probe\`). Ask: (1) admin center upload of the no-script zip — accepted means the admin center refuses skills that ship scripts (big for org deployment of our Python skills); (2) if refused, upload the full v0.7 via **Cowork › Customize › Plugins › Upload** (the route v0.4 used), then run WP-6 6a–6e (`tier-test-script.md`). Tell copilot-platform-coordination before upload (row P1) |
| **Uncommitted** | None (except the owner's untracked `notes/copilot v claude nda comparison/` — never commit it) |

### Admin-upload diagnosis so far (2026-11-27)
- v0.4 (0.4.0, 3 skills incl. Python) went in via **Cowork's** upload. v0.5–v0.7 went via the **admin center** and all failed.
- Not the cause (v0.6, v0.7 still refused): SKILL.md size (all under 20,000 chars in v0.6+); manifest version 0.x (v0.7 = 1.7.0; `atk validate --package-file` passes all 59 rules).
- Admin-deployed `spaarke-decisions` (installed fine; `src/solutions/CopilotPlugins/spaarke/appPackage/manifest.json`): version 1.0.0, ONE instructions-only skill, folder `skills/x` (no `./`), privacy/terms URLs `/privacy` `/terms`. Our probe: Python skill, `./skills/x`, 8 skills, privacy=terms=website. The no-script zip changes only the Python skill. Next candidates if that fails: `./` prefix, skill count, URLs.
- If admin center refuses script skills, our accelerator plugins (Python, versions 0.1.x) can't be admin-deployed as built → record in `results.md` + `notes/copilot-app-components.md`, tell coordination + mcp-r1.

## Active lanes

**Word probe / WP-6** — branch `test/accelerator-word-js-probe`, worktree `C:\code_files\spaarke-wt-accelerator-word-js-probe`, **PR #3266**. v0.7 = 8 skills (instructions, Python script, JS v0.4 form, + WP-6: `-edits`, `-error`, `-size-5k/10k/17k`), manifest 1.7.0. `--without-script` build flag. New gate `gates.py` `skill-md-size` (≤ 20,000 chars, precaution; unconfirmed as an upload rule). 71 build tests pass. Expected values: `EXPECTED.txt` beside the zip and `tier-test-script.md` WP-6. After WP-6: record in `results.md`; owner removes the probe → tell coordination. After #3266 merges: sync `build_word_probe.py`, `gates.py`, tests from master.

**WP-5 result (done 2026-11-27):** Word desktop runs fixed Office.js an instructions-only skill carries, as written (hash-proven, owner checked by eye). Runner form `async function main(context) {…}`, return value not passed back, refuses `toString` and top-level `await`. Cowork control: no Office.js runner, safe "code not run" fallback; Cowork has its own fixed Word editing commands (edit in place).

**Surfaces research spike (done 2026-11-27)** — `notes/research/office-surfaces-2026-11/` (start `00-synthesis.md`). Microsoft owns open-document editing; third parties add skills + MCP; federated MCP connector is the only route documented across Word/Excel/PowerPoint/Outlook; add-in agent actions cancelled 2026-06-17; Copilot Chat Premium runs skills with Python. Owner decisions open (synthesis §6): Chat as second host; Word-edition design now or after WP-6; federated connector (MCP projects); store/certification vs zero-secret auth; Excel/PowerPoint later; move synthesis to `docs/architecture/`. Proposed skill guard: never edit the source file in place (CC-14 risk).

**PR #3263** (nda-review fallback rule; branch `fix/accelerator-nda-fallback-rule`, worktree `C:\code_files\spaarke-wt-accelerator-fallback-rule`): open, merge is the owner's. After merge: `git checkout origin/master -- src/accelerators/core/engines src/accelerators/practices/nda/skills` here, rebuild, owner replaces `Documents/Cowork/Skills/nda-review/`, re-run W6. Remove worktree only via `scripts/worktree/Remove-WorktreeSafe.ps1` on owner OK.

**Gap 1 file probe (#3257)** — `notes/poc-e2e/spe-file-probe/README.md`. Part A (Cowork script egress) can run now. Part B waits on owner's per-SHA deploy go for `spike_return_file` (`9e8f0ca8e0`, mcp-r2 / uac-r2).

## Open owner questions
1. WP-6 upload route result (above), then WP-6 runs.
2. Surfaces synthesis §6 decisions.
3. Gap 2 (#3257): matter email via `spaarke_compose_email` — not started.
4. User guide draft `notes/user-guide-draft.md` — owner review + 6 open items; then wire into `package_docs.py` via its own PR to master.
5. Older: design.md body to four tiers; team/IT tier scripts; gaps #35, #27/#29; backlog X9, X12–X14.

## Related sessions
- **copilot-platform-coordination**: send a row BEFORE any tenant upload; tell it on every probe upload/removal (row P1; v0.4 removed, v0.5/v0.6 refused, v0.7 planned).
- **intake-pipeline-r1**: messages us before changing the closed refusal code set; we message it before changing the engine CLI contract.
- **mcp-r1 / mcp-r2** (`spaarkeai-mcp-bff-api-*`), **specialist-legal-agents-r1**, **accelerator-claude-r2**, **ontology-r1**, **provisioning-r1**: read-only from here.
- `src/accelerators` changes go to master by their own PR, then sync into this branch.

## Standing directives (owner) — keep until revoked
- Never commit secrets. Keep the Corteva NDA and the real RPX/Helios NDA out of the repo. Never upload `answer-key/`.
- Never run python in Bash; use `C:\Python313\python.exe -B` from PowerShell. No bare `git stash`.
- Sub-agents can't write `.claude/`. Every Agent launch states a model. Reviewer agents never checkout in our worktree.
- Skills and scripts embed no secrets and make no LLM or network calls.
- Claude never types passwords and never sends email; choose "No email" on Cowork cards.
- Leave the owner's tabs alone. Prefer moving over deleting; no permanent deletes by Claude. Ask before T-04 (share).
- Run NDA, agreement and Legal Hold review tests with **Spaarke MCP off** unless the test is about the MCP (dev connector spike tools `spike_review_nda`, `spike_get_profile`, `spike_review_agreement`, `spike_review_legal_hold`, row T1, until the next master deploy of the dev BFF).
- No Copilot Tuning in design.md yet. Declarative (Agent Builder) agents only in the advanced tiers; base tiers stay files only.
- Skills first in the Copilot app: Cowork skills do reviews and redlines; Agent Builder agents review in chat only, never redlines.
- The Office add-in for redlining is out of scope; don't raise it.
- Two editions/components (Word vs Cowork) are wanted. Mechanics first; scoring and quality later.
- Current test files live only where the package puts them; earlier files go to `Spaarke test archive (not current)/`.
- Terminology changes originate in ontology §3. `--no-verify` only on master-merge commits.
- Run downloaded/untrusted files with `-I`. Remove worktrees only with `scripts/worktree/Remove-WorktreeSafe.ps1`.

## Tooling
- Build: `./src/accelerators/build/Build-Accelerator.ps1 -Practice nda`; package `Build-Package.ps1`; probe `C:\Python313\python.exe -B build_word_probe.py [--without-script]` in `src/accelerators/build`. Tests: `python -B -m unittest discover` from `src/accelerators/build/tests` (71) and `core/engines/tests`.
- `atk validate --package-file <zip> --telemetry false` (Agents Toolkit 1.1.18 installed) runs Microsoft's validation rules (calls Microsoft's service). `wiqd` is not installed (installing = a download; ask first).
- OneDrive/SharePoint: M365 connector tools; OneDrive driveId `b!doU18j_zUUuipR60_RrF-fXvUOXGbW1OlwiIkv-_T8mVMlP029gNR48sqbO5iayh`. `Documents/` is a folder inside the drive root. 423 = file open somewhere.
