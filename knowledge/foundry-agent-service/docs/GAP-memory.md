---
source: https://learn.microsoft.com/en-us/azure/ai-foundry/agents/memory
fetched: 2026-05-14
status: GAP
---

# GAP — Agent Memory documentation not found

## What was attempted

Tried fetching the agent memory reference doc at three plausible URLs on 2026-05-14:

| URL | Result |
|---|---|
| `https://learn.microsoft.com/en-us/azure/ai-foundry/agents/memory` | 404 |
| `https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/memory` | 404 |
| `https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/tools/memory` | 404 |

## What we know

- The Foundry Agent Service overview page (`/agents/overview`, captured in [`overview.md`](./overview.md)) lists `memory` as a built-in tool: *"Built-in tools including web search, file search, **memory**, code interpreter, MCP servers, and custom functions."*
- The overview page also notes: *"Some tools, including memory and web search, are in preview."*
- No dedicated concept or how-to page for Foundry agent memory is currently published at the obvious learn.microsoft.com paths.
- The `azureai-samples` and `ai-foundry-agents-samples` repos cloned on 2026-05-14 do **not** contain a memory-specific Foundry sample. The closest reference is `examples/mem0/` in `ai-foundry-agents-samples`, which uses the **third-party Mem0 library** with Azure OpenAI (not the Foundry-managed memory tool).

## Next steps at refresh time

1. Recheck `learn.microsoft.com/azure/foundry/agents/` for a memory page (likely under `concepts/` or `how-to/tools/`).
2. Watch `Azure-Samples/ai-foundry-agents-samples` for a `memory/` example folder.
3. If the memory tool exits preview, capture concept doc + cost model + scoping (user/thread/agent) here.
