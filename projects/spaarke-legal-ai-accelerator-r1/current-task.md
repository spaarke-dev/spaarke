# Current Task State - spaarke-legal-ai-accelerator-r1 (tiered package: individual base/advanced)

> **Last Updated**: 2026-11-27 (WP-6 upload settled; Claude Desktop session `acclerator-r1`)
> **Branch**: `work/spaarke-legal-ai-accelerator-r1` · **Worktree**: `C:\code_files\spaarke-wt-spaarke-legal-ai-accelerator-r1` · **PR**: #2528 (draft, unmerged)
> Earlier checkpoints verbatim: `notes/handoff-history/2026-11.md` (the pre-rewrite file of 2026-11-27 is appended there). Narrative: `git log`.
> This worktree's memory (`~/.claude/projects/c--code-files-spaarke-wt-spaarke-legal-ai-accelerator-r1/memory/`) is NOT auto-loaded; read it at start.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Active** | WP-6 DONE 2026-11-27: all five runs passed, hash-exact (`results.md`, "WP-6 runs"). New finding: Word's Copilot has two code runners (Office.js via sub-agent; HTML `(doc) => {}` with a Copilot-written shim); error channel proven on the HTML runner only. Probe still installed (P1b, app id `9248c601-…`) |
| **Next action** | WP-7 built (probe v0.8, PR #3266 `c4d65a8696`; steps in `tier-test-script.md` WP-7, `cdb4ff8f52`), zips sent, coordination told before upload. Owner: update P1b in the admin center with `v0.8 no script new id.zip` (or remove + upload `v0.8 no script id admin-test-2.zip`, id `15c2936d-…` → P1c), then runs 7a–7d. Then: tell coordination which route landed; record results in `results.md` |
| **Uncommitted** | None (except the owner's untracked `notes/copilot v claude nda comparison/` — never commit it) |

### Admin-upload finding (2026-11-27, settled)
- The admin center refuses an app id already uploaded through Cowork › Customize › Plugins, even after removal ("We couldn't validate this manifest file", no detail). New id = accepted. Size and 0.x version were not the cause. Recorded in `results.md` (WP-6 setup, Correction 2) and `copilot-app-components.md`.
- Open: does the admin center accept skills that ship Python, on a fresh id? (accepted zip had none). Accelerator packages use a fixed uuid5 id per package (`build_accelerator.py`), so an id scheme for Cowork-tested vs admin-deployed packages is needed before org deployment.

## Active lanes

**Word probe / WP-6** — branch `test/accelerator-word-js-probe`, worktree `C:\code_files\spaarke-wt-accelerator-word-js-probe`, **PR #3266**. v0.7 = 8 skills (instructions, Python script, JS v0.4 form, + WP-6: `-edits`, `-error`, `-size-5k/10k/17k`), manifest 1.7.0. `--without-script` build flag. New gate `gates.py` `skill-md-size` (≤ 20,000 chars, precaution; unconfirmed as an upload rule). 71 build tests pass. Expected values: `EXPECTED.txt` beside the zip and `tier-test-script.md` WP-6. After WP-6: record in `results.md`; owner removes the probe → tell coordination. After #3266 merges: sync `build_word_probe.py`, `gates.py`, tests from master.

**WP-5 result (done 2026-11-27):** Word desktop runs fixed Office.js an instructions-only skill carries, as written (hash-proven, owner checked by eye). Runner form `async function main(context) {…}`, return value not passed back, refuses `toString` and top-level `await`. Cowork control: no Office.js runner, safe "code not run" fallback; Cowork has its own fixed Word editing commands (edit in place).

**Surfaces research spike (done 2026-11-27)** — `notes/research/office-surfaces-2026-11/` (start `00-synthesis.md`). Microsoft owns open-document editing; third parties add skills + MCP; federated MCP connector is the only route documented across Word/Excel/PowerPoint/Outlook; add-in agent actions cancelled 2026-06-17; Copilot Chat Premium runs skills with Python. Owner decisions open (synthesis §6): Chat as second host; Word-edition design now or after WP-6; federated connector (MCP projects); store/certification vs zero-secret auth; Excel/PowerPoint later; move synthesis to `docs/architecture/`. Proposed skill guard: never edit the source file in place (CC-14 risk).

**PR #3263** (nda-review fallback rule; branch `fix/accelerator-nda-fallback-rule`, worktree `C:\code_files\spaarke-wt-accelerator-fallback-rule`): open, merge is the owner's. After merge: `git checkout origin/master -- src/accelerators/core/engines src/accelerators/practices/nda/skills` here, rebuild, owner replaces `Documents/Cowork/Skills/nda-review/`, re-run W6. Remove worktree only via `scripts/worktree/Remove-WorktreeSafe.ps1` on owner OK.

**Gap 1 file probe (#3257)** — `notes/poc-e2e/spe-file-probe/README.md`. Part A (Cowork script egress) can run now. Part B waits on owner's per-SHA deploy go for `spike_return_file` (`9e8f0ca8e0`, mcp-r2 / uac-r2).

## Open owner questions
1. WP-7 upload route + runs (above).
2. **Word edition proposal** `notes/word-edition-design.md` (2026-11-28): adopt (withdraw protocol rule 4.2 for Word)? Then WP-8–WP-10 + AD-1 (admin center with a Python skill on a new id), app-id scheme (Cowork id kept, admin id derived). design.md §2.4.7/§2.4.8, protocol and synthesis now carry the WP facts, decisions marked "under review".
3. Surfaces synthesis §6 decisions (decision 2 now = adopt the proposal).
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
