# Knowledge Base Refresh Log

> Chronological log of refresh activity, curation events, gaps encountered, and interim updates between scheduled refreshes.

Format per entry: `## YYYY-MM-DD — <type>` where type is `Initial setup`, `Monthly refresh`, `Interim update`, or `Gap`.

---

## 2026-05-14 — Initial setup

- Created `knowledge/` directory skeleton with 11 topic subdirectories.
- Authored `knowledge/README.md` documenting conventions, topics, and refresh cadence.
- Refresh owner: Ralph Schroeder.
- Curation of topic folders proceeds in Phase 2 of the [`coding-knowledge-base-setup-r1`](../projects/coding-knowledge-base-setup-r1/) project.

## 2026-05-14 — Initial curation (Batch 1: m365-copilot, agent-framework, github-mcp)

Gaps and platform changes discovered during curation:

- **`OfficeDev/microsoft-365-copilot-samples` retired.** Microsoft migrated this repo to `pnp/copilot-pro-dev-samples` on 2026-01-02; original retiring 2026-01-30. Now-canonical primary source for declarative agent samples. Affects topics: `m365-copilot`, `declarative-agents`.
- **Agent Framework Learn doc restructure.** `/concepts/agents` → `/agents/`, `/concepts/workflows` → `/workflows/`. Snapshots in `knowledge/agent-framework/docs/` reference the current locations.
- **GitHub Copilot "skillsets" doc retired.** No direct replacement located in current docs TOC — appears folded into Custom agents / Skills. Recorded in `knowledge/github-mcp/SOURCE.md` for next refresh to hunt.
