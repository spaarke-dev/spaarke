# Current Task State - spaarke-legal-ai-accelerator-r1 (NDA MVP spike, tenant tests)

> **Last Updated**: 2026-11-05 (design v0.5 draft)
> **Recovery**: Read "Quick Recovery" section first
> **Branch**: `work/spaarke-legal-ai-accelerator-r1` · **Worktree**: `C:\code_files\spaarke-wt-spaarke-legal-ai-accelerator-r1`

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | NDA MVP spike: tenant tests per `notes/mvp-nda/runbook.md` (no POML tasks; pre-pipeline spike) |
| **Step** | **Act 0 done 2026-11-05 (Claude, via Chrome + synced OneDrive):** kit (32 files) uploaded to Acme Company › Shared Documents/Legal Knowledge (no answer-key/kit-held; verified by REST listing); profile playbook folder → team site (backup in `Cowork/Test/_parked-2026-11-05/`); personal skills `nda-library-review` + `spaarke-visual-probe` in `Cowork/Skills/`; plugin `spaarke-nda-mvp` 0.1.2 published "Only you"; Legal for Cowork was already ON; Copilot custom instruction set (one line: playbook + profile path, names no skill). Stray Cowork-authored `Cowork/Skills/nda-review/` (11-04) deleted from OneDrive at the owner's request (recycle bin), but Cowork › Plugins › Manage all still lists it ("Reviews a non-disclosure… against Ralph…"); its ⋯ › Delete is permanent, so the owner clicks it (Claude doesn't do permanent deletes). Confirm it's gone before Act 3 (T-P2). Cowork quota raised by the owner (was 8% left; `/cost` now "unlimited"). **Act 1 steps 1–2 done** (results in `notes/poc-e2e/results.md`, gaps #5–#10): `create-playbook` read the team-site precedents (T-P10 Cowork = yes), 262 credits, 32-row four-column table. Step 3 blocked: `playbook_import.py` doesn't exist (gap #9). **Next:** owner does Act 1 step 4 (Word desktop import of three formats); stray `nda-review` Cowork-side delete still the owner's click; then Act 2 (Word) and Act 3 (Cowork). Next: Act 1. Chrome in VS Code connects via `@browser` in the prompt. **End-to-end POC prep done 2026-11-05** (`notes/poc-e2e/runbook.md` §4 items 1–8): fresh synthetic kit + answer key; plugin 0.1.2-poc (`nda-review` + dashboard + audit pack); personal skill `nda-library-review`; T-P13 probe skill; local dry run. Next: owner uploads the kit and runs Act 0. Earlier: design.md **v0.5 drafted 2026-11-05**. It folds in results.md, protocol v0.2, the owner's 11-02 decisions, the expert redliner demo (`notes/demo-video/`) and the 11-04 discussion Q&A. Checks are in `notes/2026-11-05-external-inputs-review.md`. T-14 not started |
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
