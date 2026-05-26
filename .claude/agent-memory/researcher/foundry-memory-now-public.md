---
name: foundry-memory-now-public
description: Foundry agent memory transitioned from undocumented-preview to documented-public-preview in 2026-04; supersedes the foundry-agent-service GAP entry.
metadata:
  type: reference
---

# Foundry memory now publicly documented

As of 2026-05-19, Foundry agent memory has full concept + how-to documentation on Microsoft Learn:

- Concept: `https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/what-is-memory` (dated 2026-04-06, last update 2026-05-19)
- How-to: `https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/memory-usage` (dated 2026-04-10, last update 2026-05-18)

This supersedes the `knowledge/foundry-agent-service/docs/GAP-memory.md` entry which logged the docs as missing on 2026-05-14. **In the next monthly refresh, update that GAP file's "Next steps" section to point at `knowledge/foundry-memory-patterns/README.md`.**

Key facts about Foundry memory worth keeping:
- Backed by Cosmos DB, partitioned by `scope` parameter
- Two memory types: `user_profile` (static, retrieved at conversation start) and `chat_summary` (contextual, retrieved by relevance)
- `scope` resolves from `{{$userId}}` template via `x-memory-user-id` header or fallback to Entra TID+OID
- Quotas: 100 scopes/store, 10k memories/scope, 1000 RPM search/update
- Preview pricing: only pay for underlying chat + embedding model tokens; memory API is free
- No automatic TTL — explicit `delete_scope` required for GDPR

**Why this matters**: Spaarke's Insights Engine is NOT using Foundry-hosted agents (custom BFF agent is the correct choice), but Spaarke's *downstream* multi-day matter-diligence surfaces likely will use Foundry. Knowing the memory primitive's shape helps inform when to recommend Foundry-hosted vs. custom for future workstreams.

**How to apply**: When asked about Foundry memory, reference `knowledge/foundry-memory-patterns/README.md` first, not the older GAP entry. Mention that the GAP is superseded so the team knows the older note shouldn't be cited.
