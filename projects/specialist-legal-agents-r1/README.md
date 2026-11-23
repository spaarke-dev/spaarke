# Specialist Legal Agents — umbrella program (`specialist-legal-agents-r1`)

> **Portfolio**: [Project #2 · Issue #3002](https://github.com/spaarke-dev/spaarke/issues/3002) under [Epic #421](https://github.com/spaarke-dev/spaarke/issues/421) `[Epic]: SPAARKE AI AND AUTOMATION` · [Board #2](https://github.com/users/spaarke-dev/projects/2)
> **Status**: Planning: the program design (v0.1) is written. `/design-to-spec` has not been run. **Start**: 2026-11-23
> **Worktree**: `C:\code_files\spaarke-wt-specialist-legal-agents-r1` · **Branch**: `work/specialist-legal-agents-r1`

Attorney-owned specialist legal agents ("attorney persona" agents). One governed runtime compiles a layered Role Profile: platform, archetype, firm, owner and matter layers, with a one-way ratchet on authority. Attorneys reach these agents in Microsoft 365 Copilot through plugin-packaged declarative agents, with the Spaarke BFF MCP connector behind them. Tuning is propose-and-approve, gated by a per-agent golden set.

- **Design:** [design.md](design.md), the program design. It covers owner decisions, the primitives corrected against code, the Copilot evidence mapping, the Phase 0 go/no-go, coordination points and ADR tensions.
- **Source:** [notes/source/design-synopsis-2026-11-22.md](notes/source/design-synopsis-2026-11-22.md), the owner's synopsis.
- **Shape:** an umbrella program with phased child projects (design.md §4). The first archetype is the NDA reviewer.
- **Next:** split off the Phase 0 child (`specialist-legal-agents-phase0-r1`) from design.md §7–§8, then run `/design-to-spec` on it.
