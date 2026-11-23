# Current Task State — copilot-platform-coordination

> **Last Updated**: 2026-11-23
> **Worktree**: `C:\code_files\spaarke-worktrees\nostalgic-faraday-81fa74` · **Branch**: `work/copilot-platform-coordination` · **Board**: Issue #3006 under Epic #421
> **Phase**: coordination. No POML tasks; no `/design-to-spec` unless the owner asks.

## Quick Recovery

| Field | Value |
|---|---|
| **Active work** | Removal cleanup (owner approved 2026-11-23) per `notes/removal-runbook.md`; structure clarification before rules are confirmed (`notes/copilot-structure.md`) |
| **Next action** | Owner sends step-0 screenshots (All agents › Registry "Spaarke"; Copilot Studio agents) → name which D1/D5 copy to delete → owner runs runbook steps 1–3 → move rows to "Removed" |
| **Uncommitted** | none after the last commit |

## Owner decisions so far

- 2026-11-23: project name `copilot-platform-coordination`.
- 2026-11-23: removal list approved.
- 2026-11-23: rules **not** confirmed yet. The owner is unsure how the MCP connector relates to plugins, skills and tools, and shared Copilot's answer that duplicates don't make every request run twice. Response: `notes/copilot-structure.md` (Copilot is right about the mechanism; the model chose to call both copies in T2); R1 rationale reworded; R2 marked open pending S-2 (= T10) and S-3; R8 safe-to-repeat writes proposed.

## Open owner items

1. Step-0 screenshots (runbook); name an owner for D5 "Spaarke Dataverse MCP"; decide whether D1 "Spaarke AI" stays.
2. Rules: approve R1 and R3–R7 now, R2 later? R8? Q2 (R7), Q3 (customer tenants).
3. The seven inventory screenshots (end of `notes/tenant-inventory.md`).

## Waiting on other sessions

- mcp-r1: agreed R1 rationale; R8 already met by `spaarke_complete_decision`. S-1 approved by the owner (per mcp-r1, 2026-11-23): runs after 040, before C2 is deleted. Still waiting: 040 ship date, T10 + S-1 results (mcp-r1 sends both).
- acclerator-r1: P1 Word probe upload/removal.
- specialist-legal-agents-r1: G8 interim = Agent Builder agent cannot use C1 (2026-11-23). R1′ proposed (agent-private actions). Owner: G8-P2 waits until specialist finishes its current steps, then decide. Still to come: G8-p (P1) result, G8-a removal (latest 2026-11-30); D-A11.

## Standing directives

See `CLAUDE.md` (owner does all tenant actions; inventory first; never stash; never python3; agents name a model).
