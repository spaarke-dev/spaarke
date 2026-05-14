# copilot-camp BAF6 — Lab fragment: calling the Microsoft 365 Copilot Retrieval API

> **Provenance**: Extract from `microsoft/copilot-camp` repo (commit `f0ebf675a85aaee81749d07670a221faf4169b31`) — `docs/pages/custom-engine/agent-framework/06-add-copilot-api.md`.
>
> **Why curated here**: copilot-camp Lab BAF6 is named "Add Microsoft 365 Work IQ API Integration" — this is the only lab in Microsoft's public sample inventory that calls the Work IQ surface from a Custom Engine Agent. It uses the **REST API** (`graph.microsoft.com/v1.0/copilot/retrieval`), not the MCP server. Useful to compare with the MCP server approach in `tool-catalog.md`.
>
> **Original lab**: https://github.com/microsoft/copilot-camp/blob/main/docs/pages/custom-engine/agent-framework/06-add-copilot-api.md

## What the lab shows

Builds a `ClaimsPoliciesPlugin` for a Custom Engine Agent (Microsoft 365 Agents SDK + Agent Framework). The plugin:

1. Calls `POST https://graph.microsoft.com/v1.0/copilot/retrieval` with a SharePoint-scoped query
2. Passes results to a Language Model for compliance analysis
3. Adds streamed citations to the agent's response

The OBO (On-Behalf-Of) token is cached from prior auth and passed as `Authorization: Bearer {accessToken}`.

## Key code (excerpted)

### Retrieval API request payload (the Work IQ API surface in REST form)

```csharp
var retrievalPayload = new
{
    queryString = $"Retrieve the claims policies for claims of type '{claimType}' in region '{region}'",
    dataSource = "SharePoint",
    resourceMetadata = new[] { "title", "author" }
};

var jsonContent = JsonSerializer.Serialize(retrievalPayload);
var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

_httpClient.DefaultRequestHeaders.Clear();
_httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
_httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

// Call the Microsoft 365 Copilot Retrieval API
var response = await _httpClient.PostAsync(
    "https://graph.microsoft.com/v1.0/copilot/retrieval",
    httpContent);
```

### Best practices (from the lab)

- Provide as much context in the query as possible
- `queryString` should be a single sentence
- Avoid generic queries
- Send all extracts returned to your LLM for answer generation

### Licensing prerequisite (from the lab)

> The Copilot Retrieval API is available at no extra cost to users with a Microsoft 365 Copilot add-on license. Support for users without a Microsoft 365 Copilot add-on license isn't currently available.

## Comparison: Retrieval API vs Work IQ Copilot MCP server

| Aspect | Retrieval API (BAF6) | Work IQ Copilot MCP server (`mcp_M365Copilot`) |
|---|---|---|
| Transport | REST POST to `graph.microsoft.com` | Streamable HTTP MCP at `agent365.svc.cloud.microsoft` |
| Tool name | n/a (single POST) | `Copilot Chat` (i.e. `copilot_chat`) |
| Returns | Text extracts with metadata (chunks for the LLM to ground on) | Synthesized chat response (Copilot already grounded + summarized) |
| Citations | App constructs from response | Built into the synthesized response |
| When to use | Custom orchestration where the app's own LLM does synthesis | Delegate the whole turn to Copilot |
| Licensing | M365 Copilot add-on license | M365 Copilot license |

The BAF6 lab pattern (REST + custom LLM synthesis) is appropriate when the calling agent has its own LLM and orchestration. The MCP server pattern is appropriate when you want Copilot to do the synthesis end-to-end inside another agent (e.g. a Copilot Studio agent or Foundry agent calling another agent as a tool).

## What this lab does NOT show

- No example of registering the Work IQ Copilot MCP server (`mcp_M365Copilot`) in an agent
- No example of calling `copilot_chat` tool via MCP
- No example of an Agent 365 enterprise app registration for MCP server access

For those scenarios, see `docs/work-iq-mcp-overview.md` (.mcp.json examples) and `docs/workiq-mcp-server-reference-copilot.md`.
