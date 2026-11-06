# Current Task State - spaarke-legal-ai-accelerator-r1 (NDA MVP spike, tenant tests)

> **Last Updated**: 2026-11-06 (E2E POC: Claude-side acts complete)
> **Recovery**: Read "Quick Recovery" section first
> **Branch**: `work/spaarke-legal-ai-accelerator-r1` · **Worktree**: `C:\code_files\spaarke-wt-spaarke-legal-ai-accelerator-r1`

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | NDA MVP spike: tenant tests per `notes/mvp-nda/runbook.md` (no POML tasks; pre-pipeline spike) |
| **Step** | **E2E POC (`notes/poc-e2e/runbook.md`) — all Claude-side (non-Word) items DONE 2026-11-06 ~01:30** (owner runs the Word items in parallel). Results in `notes/poc-e2e/results.md` (Acts 0, 1, 3, 4, 5, 6), gaps #1–#29 in `gap-log.md`, evidence in `evidence/act3..act6/`. Headlines: Act 3 Brightwater 12/13 (122 cr); Act 4 Cowork steps done (`summarize-redlines` has no playbook rating → "respond to markup" skill candidate, gap #19); **Act 5** playbook v1.1 swapped on the team site (v1.0 MOVED to `Playbooks/NDA/Archive/`), Cowork re-run says v1.1 and notes the profile still names v1.0, `nda.term` GREEN, 0.1.3 chart colours confirmed (148 cr); **Act 6** 6a `compare-key-terms` 259 cr (no ratings), 6b `review-contract` 449 cr (overall 16/16, narrative), **6c `nda-library-review` 185 cr, 375/384 topic cells (380/384 corrected for kit bugs, gap #26), all 356 quotes verbatim**, then "redline the top three" in the same task for 96 cr (15 files in `Inbox/NDA batch/`). Plugin 0.1.3-poc published. Month total 4,298 credits ("unlimited" now). **Owner-side still open:** Act 2 (Word), Act 4 steps 1–2 (Word), Act 5 step 3 (Word picker still on v1.0), Word A/B (gap #11). **Build backlog from the POC:** representatives/whistleblower silence wording (#16), double-count rule (#27), version stamping (#28), rating stability for comment-only items (#29), HTML gate for `library_grid` (#13), `playbook_import` into `accelerator-setup` (#9), the five playbook gaps (#25), kit fixes (#26), policy as review input (#18). Chrome: Cowork tabs 1800352181 (6c) and 1800352184 (Act 5), SharePoint tab 1800352185 |
| **Status** | Waiting for the owner to review: design v0.5 (OD-20 to OD-25); `notes/solution-packs-and-components.md` (OD-26 to OD-32; OD-31 and OD-32 decided 2026-11-05); and `notes/playbook-library-and-grounding.md` (OD-33 to OD-38, from the owner's W1–W7 points, 2026-11-05). The Microsoft Legal for Cowork 1.0.10 skill texts are committed in `notes/legal-for-cowork-skills/` (internal reference only; never shipped) |
| **Next Action** | (1) Owner review of the ODs above. (2) Fold both notes into design v0.6 (each note's edit table lists the edits). (3) Close the spike (design §16 Phase 0): L15/L16/L9/L10; Word A/B import retest; T-14 with T-P2/T-P7/T-P8; **T-P15** (Microsoft's four-column playbook vs our format, with T-P4 and T-P10); re-baseline gates on wiqd 0.24.0. Uncommitted: only the root `Screenshot 2026-11-04 234407.png`, a duplicate deliberately left out |

### Critical Context
- **The MCP and Copilot-surface work is split out** (owner, 2026-11-05) to `projects/spaarkeai-mcp-bff-api-r1`, in its own worktree `C:\code_files\spaarke-wt-spaarkeai-mcp-bff-api-r1`, branch `work/spaarkeai-mcp-bff-api-r1`. The owner works on it in a separate window; don't edit it from here.
- **Word deliverable in R1:** the `.docx` Word playbook only (owner confirmed, 2026-11-05).
- **Opal (Frontier):** a browser-only computer-use agent on Windows 365 for Agents Cloud PCs. See researcher note `opal-frontier-computer-use-agent-2026-11-05.md`. Not an integration path for Spaarke. If evaluated: dev tenant, owner-only group, block-all plus allowlist, pool of 1.
- **Word has never run our plugin.** No third-party skill loader is documented for Word (L25). But we have never tested a correctly built, instruction-only plugin there, and Harvey appears in Word.
- **Cowork works**, and it currently has NO Spaarke install (clean-up done 2026-11-02).
- **The component protocol** `notes/component-protocol.md` v0.2 is the design direction. Owner review is pending, except §4.5, which the owner approved.

---

## State now
- **Build:** `./src/accelerators/build/Build-Accelerator.ps1 -Practice nda` → BUILD OK. Gate tests 22 OK; engine tests 51 OK (41 + 10 in `test_visuals.py`).
- **Package:**
  - manifest 1.30, version 0.1.2-poc (manifest 0.1.2), GUID `211eba15-6c68-5ecb-8cfb-ecb8779148e2`;
  - `nda-review` is at 20 companion files (the gate limit; gap-log #1). `nda-library-review` is `in_plugin: false` (own zip);
  - single-line SKILL.md descriptions, with gate rule `description-plain`.
- **Word playbook renderer** (`build/render_word_playbook.py`): Heading 1/2, `Topic ID:`/`Priority:`/`Review question:` lines, and General guidelines with MUST/MUST NOT.
- **A/B import files** in OneDrive `Documents/Cowork/Test/for-T-12-desktop/` (generator in the previous session's scratchpad `make_ab.py`; regenerate from the renderer if lost).
- **Tenant:**
  - Cowork Added (8): Legal for Cowork (toggle OFF), Patent, Fabric IQ, Dataverse, PandaDoc, Harvey, Legora, SignatureAPI.
  - The Spaarke plugin and its folder skills are deleted.
  - Word web's Spaarke toggle was left ON; the plugin itself is gone.

## Component protocol v0.2 (decisions in effect)
- Positions live only in playbooks; procedures only in skills; defaults only in the profile.
- **Playbook set:** one playbook per agreement type with its own topic namespace; shared positions live in `core/positions/` and are compiled into each playbook.
- **Plugin K (Knowledge):** instruction-only skills. Assume it loads everywhere, Word included. It holds no review-against-playbook procedure, because in Word that is Microsoft Legal plus our Word playbook.
- **Plugin E (Engines):** Cowork-only; holds `nda-review` plus scripts.
- **Owner-approved §4.5:** NDAs in Cowork route to Spaarke `nda-review`. A customer who doesn't want our package uses Microsoft `review-contract` with our playbook.
- **Playbook locations:**
  - Governed playbooks go in a read-only SharePoint library (T-P10 to prove).
  - Personal playbooks stay in the attorney's OneDrive `Documents/Cowork/Playbooks/<practice>/`.
- **MCP connector** (Dataverse/SPE) = v2. It should extend the existing "Spaarke Dataverse MCP" agent.

## Next Actions (in order)
1. ~~**T-P9 + leftovers check**~~ ✅ done 2026-11-02:
   - Harvey = 1 RemoteMCPServer tool (5 operations) + 5 thin skills, with no instructions (L26).
   - No accelerator copies remain in the tenant (L27).
   - An owner item is still open: the Spaarke portal submission `5d88b948…` (Teams, pending since 2026-10-29).
2. **After the owner reviews protocol v0.2:**
   - Split the build into K and E plugins (separate GUIDs and names).
   - Build a one-skill harmless Word probe plugin (T-P7).
3. **T-14:** admin-deploy, scoped to the owner. Then test:
   - Cowork: nda-01 ×3 with check_outputs (T-P3);
   - routing coexistence with Legal for Cowork ON (T-P2);
   - Word desktop and web (T-P7);
   - PowerPoint "Organization".
4. **Log** research round 2 (researcher note `word-vs-cowork-plugin-kinds-harvey-legora-tuning-2026-11-02.md`), the clean-up and T-14 in `notes/mvp-nda/results.md` and `run-log.md`.
5. **Fix queue:** L9, L10/L17, L15, L16, onboarding items, model language for all topics (needs attorney input, H2).

## Open Owner Questions / Blockers
- Review `notes/component-protocol.md` v0.2: the K/E split and the playbook set.
- Word desktop UAT evidence still owed: the A/B import screenshots, the saved D2 file, D3–D6. (**T-P4 answered 2026-11-05:** `/` Review contract → saved-playbook picker; recorded in design §2.4.6.)
- User-guide location (shipped onboarding vs attorney guide). Owner decisions H1, H2, H3.

## Standing directives (owner) — keep until revoked
- Never commit secrets. Keep the Corteva NDA out of the repo.
- Never run python in Bash; use `C:\Python313\python.exe -B` from PowerShell. No bare `git stash`.
- Sub-agents can't write `.claude/`. Every Agent launch states a model.
- Skills and scripts embed no secrets and make no LLM or network calls.
- Claude never types passwords and never sends email.
- Leave the owner's tabs alone. Prefer moving over deleting. Ask before T-04 (share).
- No Copilot Tuning in design.md yet. No declarative agents in the package.
- The Office add-in for redlining is out of scope; don't raise it.
- Two editions/components (Word vs Cowork) are wanted.

## Tooling
- **Word COM checks:** scratchpad scripts `word-open-check.ps1 -Dir` and `word-revisions-dump.ps1 -File`. They live in the previous session's scratchpad; recreate them if missing.
- **Chrome:** use the active tab; background tabs time out on screenshots.
- **Plugin upload:** Plugins › Cowork › Add › Upload → file input → Publish "Only you".
- **Repo:** the repo-root `.gitignore` ignores `build/`, so `src/accelerators/.gitignore` re-includes `build/` and ignores only `build/out/`.
