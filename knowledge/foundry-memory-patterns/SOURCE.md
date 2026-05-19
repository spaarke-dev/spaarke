# SOURCE — foundry-memory-patterns

> Provenance for Foundry memory + agent pattern reference.

**Curated**: 2026-05-19
**Curator**: researcher subagent (Insights Engine pre-design research)
**Refresh cadence**: monthly (see `knowledge/REFRESH-LOG.md`)

---

## Relationship to existing `foundry-agent-service/`

This topic is a *Spaarke-specific reference* on Foundry memory + agent patterns as they relate to the Insights Engine's custom-agent design decision. The broader Foundry Agent Service curation (overview, samples, MCP tool binding, approval-gate samples) lives in [`knowledge/foundry-agent-service/`](../foundry-agent-service/). This folder adds the *memory specifics* and the *when-to-use-Foundry-vs-custom* decision criteria.

The [`docs/GAP-memory.md`](../foundry-agent-service/docs/GAP-memory.md) entry in `foundry-agent-service/` is **superseded by the README in this folder** — Microsoft published Foundry memory docs in 2026-04 (after that gap was logged). Update the GAP file's "Next steps" section to point here in the next monthly refresh.

## Source documents

| Page | URL | Fetched / accessed | Relevance |
|---|---|---|---|
| What is Memory? - Microsoft Foundry | https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/what-is-memory | 2026-05-19 | Concept doc; memory types (user profile, chat summary); extraction/consolidation/retrieval phases; quotas (100 scopes, 10k memories, 1000 RPM); regional availability |
| Create and Use Memory - Microsoft Foundry | https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/memory-usage | 2026-05-19 | How-to with Python/C#/TypeScript/REST examples; memory store creation; scope resolution via `{{$userId}}` + `x-memory-user-id` header; `update_delay` semantics |
| What is Microsoft Foundry Agent Service? | https://learn.microsoft.com/en-us/azure/foundry/agents/overview | already snapshotted in `foundry-agent-service/docs/overview.md` | Three agent types (Prompt, Workflow, Hosted); built-in tools; A2A |
| Microsoft Foundry blog: User-Scoped Persistent Memory | https://techcommunity.microsoft.com/blog/azure-ai-foundry-blog/microsoft-foundry-unlock-adaptive-personalized-agents-with-user-scoped-persisten/4505622 | 2026-05-19 | Announcement context; Cosmos DB backing; tenant-ID / object-ID extraction |
| InfoQ: Foundry Agent Memory Preview (Dec 2025) | https://www.infoq.com/news/2025/12/foundry-agent-memory-preview/ | 2026-05-19 | Third-party recap of the announcement; useful for time-stamping when the feature went public preview |

## Gaps / open

- **GA pricing model for Foundry memory** — preview is "underlying model tokens only" with no memory-API fee. GA pricing not yet announced. Watch for it.
- **Memory store regional availability** — currently listed regions are reasonable but smaller than Cosmos DB or Azure OpenAI's footprint. EU/UK presence is good (France Central, Italy North, UK South, Sweden Central). Worth confirming Spaarke's target regions.
- **Multi-tenant security model** for memory across tenants in a shared Foundry project — docs describe scope-based isolation but the cross-tenant story isn't fully detailed. Not load-bearing for Spaarke r1 (we're not using Foundry-hosted), but relevant if downstream surfaces adopt Foundry.

## Samples not curated (deliberate)

No sample code copied yet. The how-to page has Python/C#/TypeScript/REST examples inline; the README references them. Candidate to curate in a future refresh: a runnable C# sample combining memory + agent + MCP tools, *if* Spaarke ever takes a dependency on Foundry-hosted agents.
