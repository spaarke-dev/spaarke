# Coding Knowledge Base Setup — R1

> **Status**: Phases 0–1 complete · Phase 2 (topic curation) next
> **Owner (build)**: Claude Code (this worktree)
> **Owner (Phase 4 annotation)**: Ralph Schroeder
> **Branch**: `work/coding-knowledge-base-setup-r1`
> **Worktree**: `c:\code_files\spaarke-wt-coding-knowledge-base-setup-r1`
> **Started**: 2026-05-14

---

## Goal

Build `knowledge/` at the Spaarke repo root — a curated, agent-loadable reference tree for 11 Microsoft platform pieces Spaarke depends on (M365 Copilot, MCP Apps, Declarative Agents, Agent Framework, Foundry Agent Service, Foundry IQ, Work IQ, Dataverse MCP, SPE, Azure AI Search, GitHub MCP).

**Problem solved**: Claude's training cutoff lags Microsoft platform releases by months. Without curated context, agent output drifts toward plausible-but-stale patterns.

## Canonical plan

The directive doc **`SPAARKE-KNOWLEDGE-BASE-SETUP.md`** in this folder is the authoritative plan. This README is a status overview only — don't duplicate plan content here.

## Execution approach (lightweight)

- **No POML task decomposition** — the directive is already well-structured; 11 topics are the same shape and don't benefit from per-topic 5-step task files.
- **Track progress in `TASK-INDEX.md`** — one row per phase + one row per Phase 2 topic.
- **Parallelize Phase 2 topic curation** — 2–3 parallel sub-agents at a time research/curate from Microsoft repos; main session writes the files. Sub-agents cannot write to `.claude/` (write boundary), but Phase 2 writes to `knowledge/` which is fine.
- **Phase 3 from main session only** — creating `.claude/skills/*` files must happen here, not in sub-agents.
- **Phase 4 handoff** — senior-engineer annotation pass on `NOTES.md` files (Ralph owns).

## Constraints (from directive §"Important constraints")

1. One topic at a time, fully (`SOURCE.md` + samples + stub `NOTES.md`) before moving to next
2. Preserve provenance — every curated file traceable via `SOURCE.md` (source repo, commit SHA, date pulled)
3. Stub `NOTES.md` files marked honestly with `> ⚠️ STUB — senior engineer review pending`
4. Don't bloat — 1–3 examples per pattern, not whole repos
5. Stop and ask if a source URL/repo doesn't exist (don't silently skip)
6. `knowledge/` is tracked (not gitignored)
7. Commit at logical breakpoints (after each topic)

## Risks tracked

| Risk | Mitigation |
|---|---|
| Microsoft URLs/repos drift (404s expected for preview surfaces) | Log gap to `REFRESH-LOG.md` + escalate, don't silently skip |
| Repo bloat from over-curation | Review diff size before each topic commit; 1–3 examples max |
| SKILL.md trigger quality | Phase 5 explicit verification — 5 real prompts per skill |
| Stub `NOTES.md` mistaken for substantive | Required stub header on every NOTES.md until Phase 4 |
