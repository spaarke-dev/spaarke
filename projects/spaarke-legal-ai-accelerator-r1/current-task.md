# Current Task State - spaarke-legal-ai-accelerator-r1 (tiered package: individual base/advanced)

> **Last Updated**: 2026-11-26 (Claude Desktop session `acclerator-r1`). **Next action:** owner reviews `notes/user-guide-draft.md` and answers its 6 open items (delete route for saved playbooks, "24" = topics or comments, Word "Skills folder" path, WP-1 script probe, upload folder, format); then wire the guide into `package_docs.py` (`WORD_STEPS`/`render_readme`; drop "legal skills turn on by themselves"), rebuild, mirror to PR #3163. On "probe removed", tell copilot-platform-coordination. Nothing uncommitted except the owner's untracked `notes/copilot v claude nda comparison/`.

## 2026-11-26: Word desktop round 2 recorded (`results.md` "Tier tests round 2: Word desktop")

- **WP-2 pass in Word desktop (3/3):** Word follows a third-party instructions-only skill (WPI-1112-A + headings). WP-1 (script) unknown. Word desktop has a new **Custom skills** panel ("your Skills folder", off, empty).
- Owner's takeaway: in Word, start with `/` (Review contract with a playbook → saved playbook); plain "review this NDA" or Spaarke MCP gives a generic review.
- Saved playbooks are now **deletable** (owner found how; reverses R6). After Extract playbook only + save: still one v1.2 card dated Nov 11.
- Owner's key ask: clear setup/training instructions → draft `notes/user-guide-draft.md` (for review, not yet in the package).
> **Recovery**: Read "Quick Recovery" section first
> **Branch**: `work/spaarke-legal-ai-accelerator-r1` · **Worktree**: `C:\code_files\spaarke-wt-spaarke-legal-ai-accelerator-r1` · **PR**: #2528 (unmerged)
> Earlier checkpoints (verbatim): `notes/handoff-history/2026-11.md`. Session narrative: `git log`.

---

## 2026-11-25: Claude ran tier tests round 2 in Chrome (done; results in `notes/poc-e2e/results.md` "Tier tests round 2")

- **OneDrive gotcha:** the user's `Documents/` is a FOLDER inside the drive root (server path `/personal/ralph_schroeder_spaarke_com/Documents/Documents/...`). Package uploaded via SharePoint REST; old files in `Spaarke test archive (not current)/2026-11-25 before Cowork tests/`.
- **Passed:** C-1, C-2, C-3 (defect fixed: skills print review + plan in chat before the card, commit `72fdc9ab98` on PR #3163, mirrored here), A-1 both agents (24 / 13 topics), A-2 both (chat-only review, no file changed), A-3 (knowledge edit visible after about 15 min; marker removed by restoring v1.0), WP-0 (probe works in Cowork).
- **Both agents now carry v0.3** (NDAR-1112-A / LHR-1112-A) with the OneDrive `(agent knowledge).docx` as knowledge (pasting the file link into Add knowledge works). Edit URLs: NDA `agents/edit/T_a428b503-…e46b9389…`, Legal hold `agents/edit/T_c7a9abfd-…514e6b6c…`.
- **WP-2b (Word for the web):** probe listed (plugin + both skills), but no WPI/WPS code in (a) or (b); every prompt with "Spaarke" is routed to the Spaarke connector / Spaarke MCP (MCP prompts declined). The test is **confounded by our name**. Follow-up offered: rename the probe trigger phrases (`build/build_word_probe.py`) and re-upload (new inventory row).
- **Probe plugin P1 is still uploaded (Only you)**, for the owner's Word desktop WP-1/WP-2. copilot-platform-coordination recorded it (`45022ea1c6`) and expects a message when it's removed (planned the same day) or a new date. The owner removes it in Cowork › Customize › Plugins.
- **Side finding:** the owner's saved custom instruction (names `Spaarke Review Method.docx`) leaks into both agents ("could not be located").
- **P-1 done:** the Copilot custom instruction reaches Copilot Chat and agents, not Cowork or Word for the web. Then dropped the "read Spaarke Review Method.docx" line from `package_docs.CUSTOM_INSTRUCTION` (owner OK; 9e982f7146 here, e0140591a0 on #3163). Also removed (owner OK, by Claude in Chrome) from the owner's saved Copilot instruction (874 → 653 characters) and from OneDrive `Start here/Spaarke – Custom instruction.txt` (now version 2.0, 660 bytes).
- **Still the owner's:** WP-1, WP-2 (Word desktop; pick the skill chip and leave "Spaarke" out of the prompt), WP-3, contract steps, remove `/spaarke-visual-probe` from the Cowork skills list, then remove the probe plugin and tell me.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Build and test the **four-tier package** (individual base/advanced, team no-IT/IT) from `notes/accelerator-components-by-surface.md` (adopted 2026-11-19). Pre-pipeline spike work; no POML tasks |
| **Step** | (a) OneDrive clean-up DONE (cloud moves; `notes/poc-e2e/onedrive-layout.md`). (b) Five build items: package build `src/accelerators/build/Build-Package.ps1` (legal hold Cowork skill, readable playbook renderer, Spaarke Review Method SRM-1110-A, zip laid out like `Documents/`, agent recipes) — verify + commit. (c) Owner script `notes/poc-e2e/tier-test-script.md` (W-1, G-1..G-4, H-1, P-1, C-1..C-3, A-1..A-3) |
| **Status** | Word round 1 recorded 2026-11-11 (`results.md` "Tier tests round 1: Word"; gaps #36–#38): W-1 pass, G-1 fail 0/3, "attach both" works for non-contracts, contracts take one file. Word round 2 written into `tier-test-script.md` |
| **Next Action** | Word process defined 2026-11-11 by four use cases (contract review / contract drafting / non-contract review / non-contract drafting); owner decision: Word reads no folder, the user attaches. Read me "Word 1–4" sections (`package_docs.WORD_STEPS`); Word round 2 rewritten by use case (U-1 saved playbooks, G-3b +, R-2, G-5, H-1, R-3; optional W-2, R-1b; P-1 moved to Cowork); test zip `References/` (5 files). Owner re-extracts both zips (replace) and runs Word round 2; record as "Tier tests round 2" in `results.md`. **Copilot app decision made 2026-11-12: skills first** (`notes/copilot-app-components.md`): reviews and redlines through the Cowork skills; agents optional, v0.3 instructions (`NDAR-1112-A`/`LHR-1112-A`) review in chat only and point to `/nda-review`/`/legal-hold-review`; build gate `package_docs.recipe_problems()`; A-2 rewritten. Owner rebuilds both agents from the new recipes. **Next:** record Tests 1–3 output comparison in `results.md` (gap candidates: §4.4 derived data rated differently across runs; numbering gaps after section deletions) once the owner says whether Test 3 started from a fresh copy (2:12 PM edits under the owner's name). **2026-11-25 (checkpoint at 85% context):** owner asked who owns the NDA standard v1.2: here, or `spaarke-intake-pipeline-r1` (worktree `spaarke-wt-spaarke-intake-pipeline-r1`, read-only; its design §5 runs our Python engines in a Container Apps Job, its open Q6 = which NDA standard is master). Recommended: stays here (one master → all renders, build gates); intake uses a fixed version; later it moves to the Spaarke platform (specialist-legal-agents / an ontology Playbook, D-209), never to intake. Blocker: `src/accelerators/` isn't on master (PR #2528 draft, conflicting, 3,467 behind). **Owner decided: NDA standard stays here.** **PR #3163** (branch `work/legal-ai-accelerator-src-to-master`, worktree `C:\code_files\spaarke-wt-accelerator-src-to-master`; remove only with `scripts/worktree/Remove-WorktreeSafe.ps1`) puts `src/accelerators/` on master: opened 2026-11-25, mergeable, CI running; intake-pipeline-r1 told and agreed (its design.md commit 33540db7e2: pins v1.2, uses `src/accelerators` from master, no copies). **Owner decision 2026-11-25 (intake Q3):** the platform runs our Python engines unchanged in a Container Apps Job, no C# twins (design §6.3 updated); flag any change to the engines' JSON in/out CLI contract to intake-pipeline-r1. Terminology: intake notes the later platform home is a *Playbook* (D-209), not "Policy versions"; design.md and solution-strategy.md still say "Policy versions" in about 10 places (not yet swept). Any later `src/accelerators` change on this branch must also go to master (or wait for #3163 to merge, then merge master here). Status summary given 2026-11-25; still waiting on the owner's test runs. **Owner decisions 2026-11-19:** personal templates in `Cowork/Playbooks/<type>/Templates` (confirmed); **four-tier package adopted** (design.md "Owner decisions after v0.6"; revokes "no declarative agents" for the advanced tiers only). **Word plugin tests WP-0..WP-4** added 2026-11-12 (`tier-test-script.md`; probe built by `build/build_word_probe.py`, expected `WPS-1112-A 528452740`); the 2026-11-01 Word test covered Word web, a user-scoped upload and Python skills only. **WP-3 = mcp-r1's T1-Word:** send mcp-r1 the result (date; web/desktop; listed under + › Sources; each tool ran; echo widget or text, init ms; consent/confirm prompt) |

### Critical context
- **Microsoft folders:** `Documents/Cowork/skills/<name>/SKILL.md` is the only documented auto-loaded location (Cowork FAQ). `Cowork/Tasks|Projects|AGENTS.md` are Cowork's. `Documents/Copilot/Created` = Copilot outputs; `Copilot/Memory/v2` = undocumented memory store. Nothing reads instructions from `Documents/Copilot`. Our files: `Cowork/Skills/` + `Cowork/Playbooks/` (owner decision 2026-11-10; was top-level `Legal Knowledge/`) + `Spaarke Test Documents/`. Team library keeps the name `Legal Knowledge`.
- **OneDrive sync on the owner's PC is broken** since ~2026-11-07 (downloads fail: "cloud file provider exited unexpectedly"); all clean-up was done in the cloud via the M365 connector. Owner must restart OneDrive before installing.
- **Archive:** OneDrive root `Spaarke test archive (not current)/` (still searchable → tests identify files by verification code). `Copilot/Created/_before-Act2/` still to move (423 locked).
- Owner's original protocol (PBR-1109-A) copied to `src/accelerators/core/shared-references/review-method.owner-v1.0.md`.

---

## Next Actions (in order)
1. Verify the package build (tests, file list), commit + push; give the owner the zips.
2. Owner runs `tier-test-script.md`; Claude records results in `results.md` ("Tier tests round 1") and updates the component doc statuses.
3. Four tiers adopted 2026-11-19; design.md body still to be revised to them (override block at its top for now).
4. Team-tier script (T-1..T-4, needs a second non-owner user) and IT-tier script (I-1..I-3, needs admin).
5. Still open: gap #35 (draft A version check); DA-1 package naming (" DA" suffix) — superseded by Agent Builder recipes unless I-2 runs; Cowork item 11 (owner's legal hold step in Word for the web) result never recorded; backlog X9, X12, X13, X14, gaps #27/#29.

## Open Owner Questions / Blockers
- Results of the tier script. Test 3: fresh copy or already reviewed? Design ODs (OD-20..25 with `mcp-r1`, OD-39..43), policy-as-input, saved Word playbooks (gap #32).

## Related sessions and projects
- **r2 Claude accelerator:** `spaarke-legal-ai-accelerator-claude-r2`, Desktop session `accelerator-claude-r2`; reuses `src/accelerators/`; keep engines Python 3.11-compatible.
- **MCP / Copilot surface:** `spaarkeai-mcp-bff-api-r1`, Desktop session `mcp-r1`. Don't edit its worktree from here. Its T1-Word run = our WP-3 (tool runs in Word only after + › Choose plugins, approval card even for read-only, no widget): record in `results.md`.
- **Copilot tenant inventory:** `copilot-platform-coordination` (Issue #3006, session `copilot-platform-coordination`). Send it a row **before any tenant upload**; tell it when the owner uploads or removes **P1 Spaarke Word Probe** (planned: remove the same day after WP-0..WP-2). Our skills are OneDrive skill folders (S1–S5), not tenant uploads; rename to `spaarke-*` = OD-42, owner decision pending, after OD-20.
- **Specialist legal agents:** `specialist-legal-agents-r1` (worktree `spaarke-wt-specialist-legal-agents-r1`, read-only from here), our T2/T3 continuation. Aligned 2026-11-23 (its design v0.4 §10.1, commit `3fed5d95f3`). Hand-offs: its **G6 subsumes our T-P2** (results come to us); **T-14 stays ours** (I-1); its G4 waits on our **WP-0..WP-2**. Owner decided 2026-11-24 (told us 2026-11-25): **D-A11** split by layer: we keep the file-based T0/T1 tiers incl. NDA agent content (v0.3 instructions, recipes, recipe gate); they own the T2/T3 specialist; our agents later become presets of their area agent or retire (when a connector exists). **Q7** one shared `knowledge/copilot-for-legal/` with a machine-readable list read by the single `gates.py` lint; our `notes/legal-for-cowork-skills/` (20 skills, v1.0.10) is the seed and **we refresh it until our R1 closes**. **D-A16** one roadmap table (our 4 tiers, T-P2/T-14, their Phase 0–N). **OD-2** (2026-11-25): their first archetype is patent attorney; nothing NDA-specific comes to us in their Phase 1. Our defect: `gates.py` RESERVED_NAMES covers 7 of Microsoft's 20 legal skill names, names only (fix offered to the owner 2026-11-23).
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
- No Copilot Tuning in design.md yet. Declarative (Agent Builder) agents only in the advanced tiers of the four-tier package (owner, 2026-11-19); the base tiers stay files only.
- Skills first in the Copilot app (owner, 2026-11-12): Cowork skills do reviews and redlines; Agent Builder agents are optional, review in chat only and never build redlines (enforced by the package build).
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
