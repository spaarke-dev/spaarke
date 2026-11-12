# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-12 ~18:15Z (mcp-r1, checkpoint at 85% context)
> **Recovery**: Read "Quick Recovery" first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **003: Run and record Phase 0 tests** (`tasks/003-record-phase0-results.poml`), STANDARD (testing tags → Step 9.5 applies), prescriptive steps. Started 2026-11-12 ~19:00Z. |
| **Step** | Steps 1–2 done; gate decisions applied for T5, T0, T1-Chat. `notes/phase0-results.md` created (T5 ✅, T0 ✅ incl. live 401/406/200/405 curl 19:07Z, T1 Chat ✅, E3 ✅, T11 observed, Size 1 MB). **043 cancelled** (T1 gate; POML + TASK-INDEX 🚫, drift check clean). |
| **Status** | in-progress. Remaining tests need (a) one spike change + new spike SHA deployed via uac-r2 with the owner's per-SHA go, and (b) the owner + test user live in Chat/Cowork/Word/Outlook. |
| **Spike** | New spike tip **`d7369fecf1`** pushed 2026-11-12 (`da99a4bff3` host tools + widget button + 44 spike tests; `d7369fecf1` two app packages under `src/solutions/CopilotPlugins/spike-phase0/`). Write tools verified as static no-ops. SHA sent to uac-r2 for the owner's per-SHA go; uac-r2's in-flight deploy carries the OLD tip `755987b385`. Checklist: `notes/phase0-test-checklist.md` (batch A runnable now; B = owner adds connector B; C after the new SHA is deployed). |
| **Next Action (was)** | Spike change on `spike/spaarkeai-mcp-phase0` (worktree `C:\code_files\spaarke-wt-spaarkeai-mcp-phase0-spike`): `spike_echo_3mb`/`_6mb` (Size), `spike_write` model-visible not-readOnly (T4), `spike_write_app` app-only + widget button (T3), `spike_unannotated` (T12), server `instructions` (T7), and a spike plugin package with two RemoteMCPServer plugins (T2, T14). Then: tell uac-r2, owner go for the SHA, owner registers a second connector on `/mcp/spike-area-b` (T13), run the checklist with the owner. |

### Critical Context
- **T5 passed 2026-11-12** (demo@demo.spaarke.com, Copilot Chat web, "Spaarke MCP", OBO systemuserid verified). Task 002 ✅. Evidence: `notes/phase0-registration-facts.md`.
- Copilot testing needs a **Microsoft 365 Copilot licence**; new chat after licence change; select Spaarke MCP via **+ › Sources**; tools appear as `SpaarkeMCP_<tool>`. Dev App Insights samples MCP requests (25–33%) — chat output is primary evidence.
- **Plan refreshed against master 2026-11-12** (`c4b8394737`, `notes/decisions.md`): 020 ports the `MapMcp` scanner vocabulary (+ minimum census data, interim Pending waiver owned by 021); CS0618 is a build error; blocking "Format (changed C# files)"; 025's Tier 1 job needs an owner Path-A exception; 024/032 pass D-216 warnings; connector "Spaarke MCP" (ontology `324cf6e5e2` on docs/ontology-platform-design); confidential client.

---

## Active Lanes
- **Dev BFF runs `6dbe69b9e2`** (uac-r2, 2026-11-12): local merge of master `a654da59c4` + spike `755987b385`. Standing rule: every uac-r2 dev deploy includes the spike tip until we declare Phase 0 done. Spike never merged (PR #2615 draft).
- **This branch** current with master at `301fab7285` (master `4901e832ed`); build clean. Earlier today: ArchTests 2507/2507, unit 5694/5694.
- **Side work (owner: finish, then resume MCP):** #2734 merged (`c019f812c4`), #2732 merged (`4901e832ed`, uac-r2 agreed). **#2729 on auto-merge (merge commit), waiting on CI**; side session `local_88739aa2-740a-4bbb-bfb5-b818dc7560e2` watches it and will message when merged. When it merges: `git -C C:\code_files\spaarke fetch origin master:master` (main checkout is on `chore/review-quick-fixes`, ref-only update).

## Open Owner Questions
- **#2731** (pre-existing F1: Direct carry over cap holds the watermark forever): uac-r2 chose option A (advance watermark to earliest dropped `modifiedon`, same tie rule as the horizon) and offered our side session to build it; owner has NOT decided who builds it or approved the "owner path" issue comment wording.
- **#1821** follow-up (dispatch mutation run since=56d3463c1a max_files=6, then likely survivor tests): owner deferred ("leave for later").
- **#1717**: links fixed by #2720; close the issue? (owner)
- Later area connector names ("Spaarke {Area} MCP"?) — ontology-r1 asks; undecided.
- What did the demo user see at Connect (sign-in only or consent)? Record under T5 in `phase0-results.md`.
- Demo account holds two Copilot SKUs; one seat can be freed.
- Owner reviews PR #2622 (Compose fix).

## Next Actions (after 001)
1. 003: create `notes/phase0-results.md`; import T5 (passed) and T1 evidence; run T0, T13–T14, T1–T4, T6–T12, E1–E5 and Size (1/3/6 MB init time in Chat and Cowork) with the owner. Tell uac-r2 before the window.
2. Phase B: 020 → 021 → 022/023/024 → 010 → 025; widgets 030–032; 040–043; docs 050–052; 090.
3. Owed: connector secret rotation runbook (052); ADR-028 A10 follow-up note; user-setup docs state the Copilot licence requirement.

## Peer obligations
- **ontology-r1**: ping when PR #2587 merges to master; tell them the owner's answer on area connector names.
- **uac-r2**: orchestrates dev deploys (owner gives per-SHA go there); reviewer for auth diffs; owns #2731 design.
