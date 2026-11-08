# Current Task State - spaarke-legal-ai-accelerator-r1 (NDA MVP spike → E2E POC, Act 7)

> **Last Updated**: 2026-11-08 (by context-handoff, before moving to the Claude Desktop session `acclerator-r1`)
> **Recovery**: Read "Quick Recovery" section first
> **Branch**: `work/spaarke-legal-ai-accelerator-r1` · **Worktree**: `C:\code_files\spaarke-wt-spaarke-legal-ai-accelerator-r1` · **PR**: #2528 (unmerged)
> Earlier checkpoints (verbatim): `notes/handoff-history/2026-11.md`. Session narrative: `git log`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | E2E POC **Act 7: document-type library** (no POML tasks; pre-pipeline spike). Can one habit ("attach the playbook for the document type, ask in plain words") serve a contract (NDA) **and** a non-contract (legal hold notice) in Word AI for Legal, and does NDA v1.2 keep its fallbacks (X2b)? |
| **Step** | Setup **done and pushed** (`3caf65c7bd`, `0f62b0f092`). **Waiting for the owner** to run `notes/poc-e2e/doctype-test-script.md` in Word desktop: one-time custom-instruction replacement, then W0, W1a, W1b, W2, W3, W1c (optional), W4, W5 (optional) |
| **Status** | Blocked on owner testing |
| **Next Action** | When the owner reports W-results (screenshots in OneDrive `Documents/Cowork/Test/POC-Act7/screens/`):<ol><li>score W1a/W1c/W4 against `answer-key/legal-hold-answer-key.md` and the v1.1 NDA key (`answer-key/answer-key.md`, Brightwater section; v1.1 = term GREEN);</li><li>record the results in `results.md` Act 7 and the script's results table;</li><li>revise the step lists in `kit-src/make_doctype_library.py` (CONTRACT_STEPS / OTHER_STEPS), which regenerates the quick card and `user-steps.md`;</li><li>fold the findings into design §5.9 / §8.1a / §12 / §18.</li></ol> |

### Files modified this stretch (all committed and pushed)
- `src/accelerators/build/render_word_playbook.py`: X2b, "If missing" and "Acceptable fallback (accept without redlining)" lines in Instructions.
- `src/accelerators/core/engines/playbook_parse.py`: optional `If missing` field; `parse(require_side=False)` for non-contract types (X14).
- `src/accelerators/core/engines/playbook_import.py`: round-trips If missing; drops the repeated fallback; strips the "Escalate if:" label (fixes a bug that predates this work).
- `src/accelerators/build/tests/test_render_word_playbook.py`: build 31 tests, engines 59, NDA build gates OK.
- `notes/poc-e2e/kit-src/make_doctype_library.py`:
  - generates the Act 7 kit: NDA v1.2 Word copy and master; legal hold master and Word copy; requirement-list variant;
  - drafts A/B; policy; template; precedent; demand letter; quick card v1.1; README;
  - writes `answer-key/legal-hold-answer-key.md` and `user-steps.md`;
  - output goes to `%TEMP%\act7-library` or `$env:KIT_OUT` (MAX_PATH);
  - to upload with Chrome `file_upload`, generate into the session scratchpad and use its **long** path (`C:\Users\RalphSchroeder\...`, not `RALPHS~1`).
- `kit-held/Act 7 - Document-type library/src/Spaarke NDA – Mutual v1.2.md`: NDA master source (positions = v1.1).
- `design.md` §5.9: folder standard by document type. Also `doctype-test-script.md`, `user-steps.md`, `results.md` (Act 7 setup + update), `package-backlog.md` (X2b built, X14 ◐), `runbook.md` (old → new path map), `word-test-script.md` + `gap-log.md` #32 (R1–R7, G1/G2 done).

### Critical context
- **Word test set R1–R7, G1, G2 complete (2026-11-07)** (`results.md` "Word test set"):
  - the four-column table imports directly (R1 = A);
  - Word keeps only Name / Rules / Examples, so the Compromise column is dropped (gap #30 → X2b);
  - a saved playbook is a **frozen copy** with no delete (R6, gap #32);
  - a plain prompt with the playbook attached routes to Review contract without `/` and without saving (R7). This is the recommended Word path;
  - review scores 19–20/24 and varies between runs;
  - G1 Identify regulatory gaps against our policy = 4/4 (internal policy only via "Something else"); G2 execution readiness passes.
- **Library = team site Acme Company › Shared Documents › `Legal Knowledge/`, reorganised by document type (design §5.9):**
  - `<Type>/` holds only the playbook `.docx` to attach;
  - each type has the same subfolders: `Playbook master/ · Policies/ · Precedents/ · Templates/ · Archive/`;
  - at the root: `Start here – Quick card.docx`, `README.txt`, `Shared templates/`;
  - types: **NDA** (Spaarke NDA – Mutual v1.2) and **Legal Hold Notice** (Spaarke Legal Hold Notice – Employee Custodians v1.0, with Acme policy, template, Kestrel precedent);
  - test material lives in `Shared Documents/POC Workspace/`: Matters/Brightwater Systems, Matters/Harbor Point Logistics (demand letter, drafts A/B), Inbox/NDA batch, Samples/NDA, Test variants;
  - the old function-first folders are in the site recycle bin.
- **Cowork profile** `OneDrive/Documents/Cowork/Profile/legal-profile.md` points at `Legal Knowledge/NDA/Playbook master/`.
- **The owner's Copilot custom instruction is stale**: it names v1.0 at `Legal Knowledge/Playbooks/NDA/`. The owner should replace it with the generic text in the quick card or `user-steps.md` before the W-tests; W3 tests whether Word follows it.
- **Terminology** (owner 2026-11-07, D-209): "playbook" is the standard for any document type, contract or not, distinct from skills and tools. Users attach only the Word copy; the master is never attached; *creating* a playbook from precedents is a library-owner task (contracts only), not part of a review.
- **Library file history:** `NDA/Archive/Spaarke NDA – Mutual v1.1 (table).docx` (item `6b17d5a3…`) is version 3.0, restored to the original after the R6 snapshot test.

---

## Next Actions (in order)
1. Owner runs the W-tests → Claude scores and records them (see Next Action above).
2. Depending on W1: if the table works for non-contracts, one procedure; if only the requirement list works, the non-contract playbook format changes; if neither, non-contract review is Cowork-only. Update the quick card steps either way.
3. W4 confirms X2b → carry "If missing" into `src/accelerators/practices/nda/positions/baseline-mutual.md` and rebuild.
4. X14 remainder: a `practices/legal-hold/` catalogue in `src` so the build and gates cover non-contract types.
5. Backlog P1: X12 (package zip), X13 (renderer as a setup script), X9 (absence flag), gaps #27 (double-counting) and #29 (rating stability).
6. Offered, not accepted: record the M365 app-package assessment in design/backlog; an r1 → r2 pointer in r2's research.

## Open Owner Questions / Blockers
- W-test results (above).
- Design ODs still open: OD-20 to OD-25 (incl. plugin naming, agreed with `spaarkeai-mcp-bff-api-r1`, pending the owner), OD-39 to OD-43, policy-as-input (backlog 3-2); new from gap #32: is a saved Word playbook acceptable for governed positions?
- Component protocol v0.2 review (K/E plugin split deferred, design §5.10).
- Spaarke portal submission `5d88b948…` (Teams, pending since 2026-10-29).

## Related sessions and projects
- **r2 Claude accelerator:** project `spaarke-legal-ai-accelerator-claude-r2`, worktree `C:\code_files\spaarke-wt-spaarke-legal-ai-accelerator-claude-r2`; now in the Claude Desktop session `accelerator-claude-r2`.
  - It reuses `src/accelerators/` and sends proposals for shared parts.
  - Keep the engines **Python 3.11-compatible** (the Claude sandbox runs 3.11).
  - Claude plugin zips must have no top-level `bin/`.
- **MCP / Copilot surface:** `spaarkeai-mcp-bff-api-r1` (worktree `C:\code_files\spaarke-wt-spaarkeai-mcp-bff-api-r1`). Don't edit it from here. Shared naming in its `notes/coordination/2026-11-06-copilot-naming-and-ownership.md`.
- **Ontology:** `spaarke-ontology-platform-r1`. Terminology changes originate in its §3 (D-209 adopted).
- **Provisioning:** `customer-provisioning-orchestration-r1` is now the Claude Desktop session `provisioning-r1`.

## Standing directives (owner) — keep until revoked
- Never commit secrets. Keep the Corteva NDA and the real RPX/Helios NDA out of the repo. Never upload `answer-key/`.
- Never run python in Bash; use `C:\Python313\python.exe -B` from PowerShell. No bare `git stash`.
- Sub-agents can't write `.claude/`. Every Agent launch states a model.
- Skills and scripts embed no secrets and make no LLM or network calls.
- Claude never types passwords and never sends email; choose "No email" on Cowork cards.
- Leave the owner's tabs alone. Prefer moving over deleting; no permanent deletes by Claude. Ask before T-04 (share).
- No Copilot Tuning in design.md yet. No declarative agents in the package.
- The Office add-in for redlining is out of scope; don't raise it.
- Two editions/components (Word vs Cowork) are wanted.
- Terminology changes originate in ontology §3.

## Tooling
- **Build:** `./src/accelerators/build/Build-Accelerator.ps1 -Practice nda`, or `C:\Python313\python.exe -B src/accelerators/build/build_accelerator.py --practice nda --out <dir>`. Tests: `python -B -m unittest discover -s build/tests` and `-s core/engines/tests` from `src/accelerators`.
- **SharePoint from Chrome** (team site `https://spaarke.sharepoint.com/sites/AcmeCompany`):
  - REST via `javascript_tool`: `/_api/contextinfo` digest; `SP.MoveCopyUtil.MoveFileByPath` / `MoveFolderByPath`; `folders/addUsingPath`; `Files/AddUsingPath(...,overwrite=true)` fed by a temporary `<input type=file>` and `file_upload`.
  - Versioning is on. `423 SPFileLockException` means the owner has the file open.
  - Tabs in the MCP group get replaced; re-read `tabs_context_mcp`.
- **Word COM checks / revision dump:** scratchpad `revs.py` pattern (lxml over `word/document.xml` and `comments.xml`). Recreate if missing.
- **Repo:** the repo-root `.gitignore` ignores `build/`, so `src/accelerators/.gitignore` re-includes `build/` and ignores only `build/out/`.
