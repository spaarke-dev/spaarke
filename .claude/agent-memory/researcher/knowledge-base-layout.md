---
name: knowledge-base-layout
description: How Spaarke's knowledge/ tree is organized and where to add new researcher topics.
metadata:
  type: reference
---

# Knowledge base layout convention

Spaarke's `knowledge/` directory at the repo root is the curated Microsoft platform reference tree (established 2026-05-14 by the `coding-knowledge-base-setup-r1` project). Each topic gets a subdirectory with:

- **`SOURCE.md`** — required. Provenance: repo URLs, commit SHAs, date pulled, what each curated file demonstrates.
- **`NOTES.md`** — required for *fully curated* topics. Spaarke-specific commentary in two sections: §1 "How this fits Spaarke's architecture" and §2 "How we build with it". Stubs are marked `> ⚠️ STUB — senior engineer review pending`.
- **`<sample-name>/`** — one or more curated samples copied from source repos.
- **`docs/`** — snapshotted Microsoft Learn pages (optional).

For **reference-only topics** (no curated samples, just a substantive README), drop the NOTES.md and put the substance in `README.md` with a clear "Status as of YYYY-MM" header, key URLs section, findings, implications, and open questions. SOURCE.md still records what was consulted.

**Existing topics** (as of 2026-05-19): m365-copilot, mcp-apps, declarative-agents, agent-framework, foundry-agent-service, foundry-iq, work-iq, dataverse-mcp, sharepoint-embedded, azure-ai-search, github-mcp, **cosmos-gremlin**, **azure-functions-isv**, **dataverse-sync**, **foundry-memory-patterns** (last four added 2026-05-19 by the researcher subagent).

**Monthly refresh** is the responsibility of Ralph (first business day). Interim updates go into `REFRESH-LOG.md` immediately. The researcher subagent appends an entry to REFRESH-LOG whenever it adds new topics.

**Why**: Avoid drift; preserve provenance; make agent output reproducible across sessions.

**How to apply**: Before adding new external research, check if a topic folder already exists. Supplement rather than duplicate. New folders get SOURCE.md + README.md (or NOTES.md if curated samples are included). Always update REFRESH-LOG.md and `knowledge/README.md`'s topics table.
