# Task 040 — Microsoft Learn MCP server for the researcher

> spec FR-10 · design.md §7.1 · config commit `cb613e3098` (`.mcp.json`, `.claude/agents/researcher.md`, `knowledge/README.md`).

## What was added

- **`.mcp.json`**: `microsoft-learn` server, `{"type": "http", "url": "https://learn.microsoft.com/api/mcp"}`, next to the unchanged `dataverse` entry. The `dataverse` args array was only reflowed onto one line by Prettier; the values are identical.
- **Endpoint** (confirmed before writing): no authentication, streamable HTTP, three tools: `microsoft_docs_search`, `microsoft_docs_fetch`, `microsoft_code_sample_search`.
- **`.claude/agents/researcher.md`**:
  - The frontmatter `tools:` list names the three `mcp__microsoft-learn__*` tools.
  - Research order: own memory, then `knowledge/`, then repo docs and ADRs, then Learn MCP (search, then fetch one or two pages), then WebSearch/WebFetch.
  - Privacy line: Learn MCP queries never contain customer data, tenant names or secrets.
- **`knowledge/README.md`**: a project-scoped MCP server needs one-time approval per machine.

## Verification run (2026-11-16)

The owner approved the project MCP server on this machine (the Learn tools appeared in the session's tool list). One `researcher` run (Sonnet), on a Spaarke-relevant question with no customer data: *are root CLAUDE.md's SharePoint Embedded container-type limits ("permanent, capped at 25 per tenant, cannot be deleted, creation delegated-only") still accurate?*

Tool calls, in order. The research order was followed:
1. Own memory index: no entry on the topic.
2. `Grep` on `knowledge/`: hit `knowledge/sharepoint-embedded/NOTES.md` and `docs/learn-containertypes.md`.
3. `Grep` on `docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md`.
4. **`mcp__microsoft-learn__microsoft_docs_search`**: "SharePoint Embedded container types limit per tenant delete container type".
5. **`mcp__microsoft-learn__microsoft_docs_fetch`**: `https://learn.microsoft.com/en-us/graph/api/filestorage-post-containertypes`.

Answer, with the Learn citations:

| Claim | Learn today | Repo statement |
|---|---|---|
| 25 per tenant | "Each tenant can create up to 25 container types in total" (one may be a trial). https://learn.microsoft.com/sharepoint/dev/embedded/build/create-container-type | Accurate |
| Cannot be deleted | Only **trial** types can be deleted; "deletion of standard container types isn't yet supported". Same page; https://learn.microsoft.com/powershell/module/microsoft.online.sharepoint.powershell/remove-spocontainertype | Accurate for standard types |
| Creation delegated-only | `POST /storage/fileStorage/containerTypes`: delegated `FileStorageContainerType.Manage.All`; application permission "Not supported". https://learn.microsoft.com/en-us/graph/api/filestorage-post-containertypes | Accurate |

- **Escalation trigger** (auth, or data sent beyond the query): **not fired**. There was no auth prompt and no error. Only the query string and the page URL left the machine.
- **Measures:** 6 tool calls, about 62k subagent tokens, 27 s.
- **Memory file:** the researcher saved its finding to `.claude/agent-memory/researcher/spe-container-type-limit-delete-2026-11-16.md` and indexed it. Researcher memory is tracked in the repo, so the file ships with this task.

**Found in passing (reported, no change):** root CLAUDE.md's 🔒 line says container types are "permanent" and "cannot be deleted". Learn is more precise: trial types can be deleted, and standard types cannot "yet". The guard is conservative and correct for the standard types Spaarke creates, and the topology doc already states the trial nuance. This is not a defect, so the root file is left unchanged.

## Known limit

- (K) The Learn MCP tool names may change; the `tools:` allow-list in `researcher.md` would then need updating. The researcher falls back to `WebFetch` on learn.microsoft.com when the tools are missing.
