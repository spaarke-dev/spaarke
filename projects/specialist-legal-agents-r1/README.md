# Specialist Legal Agents — umbrella program (`specialist-legal-agents-r1`)

> **Portfolio**: [Project #2 · Issue #3002](https://github.com/spaarke-dev/spaarke/issues/3002) under [Epic #421](https://github.com/spaarke-dev/spaarke/issues/421) `[Epic]: SPAARKE AI AND AUTOMATION` · [Board #2](https://github.com/users/spaarke-dev/projects/2)
> **Status**: Phase 0 in execution. `spec.md` written 2026-11-25; `/project-pipeline` run 2026-11-26 (20 tasks, [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md); plan [plan.md](plan.md)). **Start**: 2026-11-23
> **Worktree**: `C:\code_files\spaarke-wt-specialist-legal-agents-r1` · **Branch**: `work/specialist-legal-agents-r1`

Attorney-owned specialist legal agents ("attorney persona" agents). One governed runtime compiles a layered Role Profile: platform, archetype, firm, owner and matter layers, with a one-way ratchet on authority. Attorneys reach these agents in Microsoft 365 Copilot through plugin-packaged declarative agents, with the Spaarke BFF MCP connector behind them. Tuning is propose-and-approve, gated by a per-agent golden set.

- **Design:** [design.md](design.md), the program design. It covers owner decisions, the primitives corrected against code, the Copilot evidence mapping, the Phase 0 go/no-go, coordination points and ADR tensions.
- **Source:** [notes/source/design-synopsis-2026-11-22.md](notes/source/design-synopsis-2026-11-22.md), the owner's synopsis.
- **Shape:** an umbrella program with phased child projects (design.md §4). The first archetype is the NDA reviewer.
- **Phase 0 runs in this folder** (OD-3, amended 2026-11-24); Phase 1 is the child project `specialist-legal-agents-patents-r1`.
- **Next:** Wave 1 test kits (tasks 001–006), then 007 and 010. Current state: [current-task.md](current-task.md).
