---
source: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/ai-services/chat/overview
fetched: 2026-05-14
ms_date: 2025-09-18
ms_updated_at: 2026-03-24
---

# Microsoft 365 Copilot Chat API Overview (Preview)

The Microsoft 365 Copilot Chat API allows you to programmatically engage in multi-turn conversations with Microsoft 365 Copilot while using enterprise search grounding and web search grounding.

> The Chat API is the REST API equivalent of what the Work IQ Copilot MCP server exposes as the `copilot_chat` tool (`mcp_M365Copilot` server in the Work IQ MCP catalog).

## Why use the Chat API?

Secure and compliant integration of M365 Copilot in custom generative AI solutions. No need to:

- Egress data
- Break permissions
- Build separate vector indexes, LLMs, orchestration layers

Custom applications hand off prompts to the Chat API and receive fully synthesized answers grounded in web and work data.

### Manage compliance and safety risks

Uses M365's built-in security and compliance features so data source permissions and compliance settings are preserved. Prevents data leaks. Org permission model ensures individuals only get results from content they're allowed to access.

### Solve for relevancy and freshness

Enterprise search grounding in place means answers are always fresh and relevant. No separate, costly data pipelines or orchestrators that don't fully understand Microsoft 365 context and signals.

### Lower cost of ownership

Eliminates the need to maintain a custom orchestration layer or build a secure data export and indexing pipeline.

## Chat API capabilities

Can use these capabilities to answer natural language prompts:

- Enterprise search grounding
- Web search grounding

Supports natural language prompts. Returns relevant answers within the M365 trust boundary. You can provide OneDrive/SharePoint files as context and toggle web search grounding.

## Licensing

Available at no extra cost to users with a Microsoft 365 Copilot add-on license. Support for users without a Microsoft 365 Copilot add-on license isn't currently available.

## Known limitations

- No action or content generation skills (creating files, sending emails, scheduling meetings)
- Text responses only
- No tools (code interpreter, graphic art)
- No long running tasks — chat messages with long running tasks are prone to gateway timeouts
- Both enterprise + web search grounding by default
- Toggling off web search grounding is a single-turn action — must be toggled off for each chat message where not required
- Subject to limitations of the Microsoft 365 Copilot semantic index
- AI-generated responses — verify before use
- Graph explorer doesn't support streamed conversations with the Chat API
