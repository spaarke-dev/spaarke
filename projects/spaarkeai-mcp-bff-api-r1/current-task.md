# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-12 ~18:15Z (mcp-r1, checkpoint at 85% context)
> **Recovery**: Read "Quick Recovery" first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 001: Spike MCP server, echo tool and test widget (`tasks/001-spike-mcp-echo-widget.poml`) — **closing out** (owner said "resume MCP" 2026-11-12). |
| **Step** | Completion only (task-execute Steps 9–11). The spike is built AND deployed: spike tip `755987b385` on `spike/spaarkeai-mcp-phase0` has the 1/3/6 MB widget variants (`ui://spaarke/spike-1mb.html`, `-3mb`, `-6mb`; `spike.html` = 1 MB alias), T13 two-route tool sets, 401/406 checks, 19 spike tests. Measurements note `notes/spikes/phase0-spike-measurements.md` exists (head `5c9e301572` at writing, publish delta +0.703 MB, widget sizes) but its §1/§8 still say "not deployed". |
| **Status** | in-progress (completion). The earlier current-task claim "size-test widgets not built" was WRONG — they exist; only the size TEST run (task 003) is outstanding. |
| **Next Action** | (1) Update the measurements note: spike tip `755987b385` (later than `5c9e301572` — list what changed, e.g. scanner commit `fdfd7b1d23`), first deployed 2026-11-10 by mcp-r1 with the owner's go, now deployed via uac-r2 local merges (current dev BFF `6dbe69b9e2` = master `a654da59c4` + spike); dev URL `https://spaarke-bff-dev.azurewebsites.net/mcp/legal-operations`. (2) Verify 001 acceptance criteria against the note; the ui-tests ran live in Copilot Chat 2026-11-12 16:12Z (widget rendered, callServerTool echo worked). (3) POML status completed + notes + `rework: none task 001`; TASK-INDEX `✅ [done]`; `pwsh scripts/check-task-status-drift.ps1`; portfolio sync (#2608 item `PVTI_lAHODW0Pv84BEgWuzhGsit4`, Tasks Completed 8→9, field `PVTF_lAHODW0Pv84BEgWuzhWPlLY`). (4) Then task 003. |

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
