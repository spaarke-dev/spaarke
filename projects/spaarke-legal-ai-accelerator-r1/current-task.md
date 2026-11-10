# Current Task State - spaarke-legal-ai-accelerator-r1 (E2E POC → declarative-agent spike DA-1)

> **Last Updated**: 2026-11-09 (by context-handoff, Claude Desktop session `acclerator-r1`)
> **Recovery**: Read "Quick Recovery" section first
> **Branch**: `work/spaarke-legal-ai-accelerator-r1` · **Worktree**: `C:\code_files\spaarke-wt-spaarke-legal-ai-accelerator-r1` · **PR**: #2528 (unmerged; master merged in 3cf9252e63)
> Earlier checkpoints (verbatim): `notes/handoff-history/2026-11.md`. Session narrative: `git log`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Spike **DA-1**: specialist declarative agents in Word (agent mode) for **NDA Review** and **Legal Hold Review** (no POML tasks; pre-pipeline spike). Owner asked (2026-11-09): spike both; refresh DA research to 2026-11-09 (done); thoroughly review the existing Spaarke AI agent (done) |
| **Step** | Setup round 1 failed (owner 2026-11-09: Agent Builder rejects `.md` knowledge, has no skills; agents built in Copilot Studio don't appear in Word; see `results.md` "Spike DA-1"). Round 2: S1 now = upload three DA **packages** (v1.9, agent skills, no knowledge) from OneDrive `Documents/Cowork/Test/POC-DA1/packages/`, built by `da-spike/Build-SpikePackages.ps1`; @-names end in " DA". Table-free playbook copies in `POC-DA1/knowledge/` |
| **Status** | blocked on owner installing the packages and running the spike |
| **Next Action** | When the owner reports results/screenshots (`OneDrive Documents/Cowork/Test/POC-DA1/screens/`): score N-1/N-3/B-1 against `answer-key/answer-key.md`, LH-1/LH-4/B-2 against `answer-key/legal-hold-answer-key.md`; record "Spike DA-1" in `results.md` |

### Files modified this session (uncommitted unless noted)
- `notes/poc-e2e/da-spike-script.md` — spike script draft (LH-1..6, N-1..7, FD-1, CM-1); §S1 placeholder. **Uncommitted**
- `notes/poc-e2e/da-spike/legal-hold-review.instructions.md` (4,466 chars) and `nda-review.instructions.md` (5,390 chars) — agent instructions with full topic lists, completeness guard, verification codes LHR-1109-A / NDAR-1109-A, ratings GREEN/YELLOW/RED mapping. **Uncommitted**
- `.claude/agent-memory/researcher/declarative-agents-in-word-2026-11-09.md` + `MEMORY.md` (index compacted 22→15.5 KB). **Uncommitted**
- `notes/handoff-history/2026-11.md` (previous checkpoint appended). **Uncommitted**
- Committed + pushed earlier today: results.md Act 7 results + owner mode follow-up, gap-log #33–#35 (`7e2e29d574`, `d9dcb414a3`)

### Critical context
- **Act 7 Word results:** in *Allow editing* mode Word turns a non-contract review into editing suggestions (7/8 issues, no ratings). With the owner's review protocol (`OneDrive/Documents/Copilot/playbook-document-review.md` v1.0, PBR-1109-A) via custom instruction, totals matched the legal hold key exactly (5/2/6). **Chat mode** ignores custom instructions and can't `@` agents; **agent mode** does both (owner, Obs).
- **Research 2026-11-09 (key facts):** a DA in Word reads the **whole document as plain text, tables dropped**; SharePoint knowledge can't parse tables (≤20 files → full-content search); a DA **cannot edit** (Word Agent can); **DA v1.9** adds `agent_skills` (≤8 per agent, GA MC1476977; SKILL.md ≤20,000 chars; Agent Builder takes skill zips ≤50 MB); instructions ≤8,000; ≤12 starters (render in Word); org prompts not in Word pane; no on-open hook; `worker_agents` preview, not in Word; SharePoint/embedded knowledge needs M365 Copilot licence or metering; a DA runs only in M365 Copilot (components portable: skills standard, MCP, MCP Apps). Open issue ATK #16869: duplicate answers in Word.
- **Existing Spaarke AI agent** (`src/solutions/CopilotAgent/`): DA v1.8, app manifest 1.30, OpenAPI plugin v2.4 (28 BFF functions, OAuthPluginVault), instructions 4,111 chars, general legal-ops assistant, **no knowledge capability by design** (keeps it free for Copilot Chat users; the build gate in `scripts/copilot-agent/CopilotAgentPackage.psm1:272` refuses capabilities). Per-customer packaging: `Render-CopilotAgentPackage.ps1` + `publish-copilot-agent-template.yml` (T257: customer IT uploads in its own tenant). Specialist review agents need knowledge → a separate template/gate, not this one. No MCP server in the BFF on master; planned `/mcp/legal-operations` in `spaarkeai-mcp-bff-api-r1`.
- **Standing directive "No declarative agents in the package" is under owner review** — the owner proposed specialist DAs on 2026-11-09; do not change design.md until the spike decides.

---

## Next Actions (in order)
1. Finish `da-spike-script.md` (Next Action above); commit + push the spike files and the researcher note.
2. Report to the owner: research deltas, inventory findings, the two architectures (A: two specialist DAs; B: one DA + skills), the script.
3. Owner runs the spike → Claude scores N-1/N-3 against `answer-key/answer-key.md` (Brightwater under v1.1 positions: 6 RED, 6 YELLOW incl. term GREEN, 12 GREEN; b01 Kestrel all GREEN) and LH against `answer-key/legal-hold-answer-key.md`; record in `results.md` (new "Spike DA-1").
4. Then design: new ODs (specialist DA architecture; front door; revoke "no DAs"; one rating scale), design §2.4.6 / §5 / §8.1a / §12 / §18, quick card v2; coordinate DA ownership with `mcp-r1`.
5. Still open from Act 7: OneDrive stray-file clean-up (gap #34), draft A version check (gap #35), owner's custom-instruction path (`Copilot/Skills/` vs `Copilot/`); W1c/W2/W4 folded into the spike.
6. Backlog (unchanged): X9, X12, X13, X14 remainder, gaps #27/#29; carry "If missing" into `practices/nda/positions/baseline-mutual.md`.

## Open Owner Questions / Blockers
- Spike results (above). Decision on specialist DAs vs the "no declarative agents" directive.
- Design ODs open: OD-20 to OD-25 (naming, agreed with `mcp-r1`), OD-39 to OD-43, policy-as-input (backlog 3-2), saved Word playbooks for governed positions (gap #32).
- Component protocol v0.2 review. Spaarke portal submission `5d88b948…` (Teams, pending since 2026-10-29).

## Related sessions and projects
- **r2 Claude accelerator:** `spaarke-legal-ai-accelerator-claude-r2`, Desktop session `accelerator-claude-r2`; reuses `src/accelerators/`; keep engines Python 3.11-compatible; Claude plugin zips have no top-level `bin/`.
- **MCP / Copilot surface:** `spaarkeai-mcp-bff-api-r1`, Desktop session **`mcp-r1`** (send OD-20 naming there). Don't edit its worktree from here.
- **Ontology:** `spaarke-ontology-platform-r1`, Desktop session **`ontology-r1`**; terminology originates in its §3 (D-209).
- **Provisioning:** `customer-provisioning-orchestration-r1`, Desktop session `provisioning-r1` (owns T257 Copilot agent packaging).
- This session's memory folder is the main repo's; the worktree's own memory (`~/.claude/projects/c--code-files-spaarke-wt-spaarke-legal-ai-accelerator-r1/memory/`) is NOT auto-loaded — read it at start.

## Standing directives (owner) — keep until revoked
- Never commit secrets. Keep the Corteva NDA and the real RPX/Helios NDA out of the repo. Never upload `answer-key/`.
- Never run python in Bash; use `C:\Python313\python.exe -B` from PowerShell. No bare `git stash`.
- Sub-agents can't write `.claude/`. Every Agent launch states a model.
- Skills and scripts embed no secrets and make no LLM or network calls.
- Claude never types passwords and never sends email; choose "No email" on Cowork cards.
- Leave the owner's tabs alone. Prefer moving over deleting; no permanent deletes by Claude. Ask before T-04 (share).
- No Copilot Tuning in design.md yet. No declarative agents in the package **(under review, spike DA-1)**.
- The Office add-in for redlining is out of scope; don't raise it.
- Two editions/components (Word vs Cowork) are wanted.
- Terminology changes originate in ontology §3. "Playbook" = the standard procedure for any document type (D-209).
- `--no-verify` only on master-merge commits.

## Tooling
- **Build:** `./src/accelerators/build/Build-Accelerator.ps1 -Practice nda`. Tests: `python -B -m unittest discover -s build/tests` and `-s core/engines/tests` from `src/accelerators`.
- **SharePoint from Chrome** (team site `https://spaarke.sharepoint.com/sites/AcmeCompany`): REST via `javascript_tool` (`/_api/contextinfo` digest; `SP.MoveCopyUtil.*`; `folders/addUsingPath`; `Files/AddUsingPath(...,overwrite=true)` via a temporary `<input type=file>` + `file_upload`). `423 SPFileLockException` = owner has the file open.
- **Owner screenshots** land in OneDrive `C:\Users\RalphSchroeder\OneDrive - Spaarke Inc\Documents\Cowork\Test\<folder>\screens\` (Act 7 ones went to `POC-Act2/screens/`).
- **Repo:** root `.gitignore` ignores `build/`; `src/accelerators/.gitignore` re-includes it.
