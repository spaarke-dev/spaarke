# GitHub MCP — Invocation Examples

**Source:** `github/github-mcp-server` `pkg/github/__toolsnaps__/*.snap` (JSON-Schema tool definitions) and `README.md` (parameter descriptions)
**Commit:** `39d86b80af711a3277ffab08fa7d3068b3652913`
**Captured:** 2026-05-14

These examples are derived directly from the tool definitions in the repo's `__toolsnaps__` directory (input-schema JSON) and the README parameter docs. They show realistic invocations for the three tools the directive calls out: `search_code`, `search_issues`, and `get_file_contents`.

The arguments shown below are the JSON object passed as `arguments` in an MCP `tools/call` request. They are protocol-level examples; how you express them in an MCP host (Claude Code, Copilot Chat, VS Code agent mode, etc.) will be wrapped by the host's tool-call UI.

---

## `search_code`

**Description (from snapshot):** "Fast and precise code search across ALL GitHub repositories using GitHub's native search engine. Best for finding exact symbols, functions, classes, or specific code patterns."

**Required parameters:** `query` (string).
**Optional parameters:** `order` (`asc` | `desc`), `sort` (`indexed` only), `page` (min 1), `perPage` (1–100).
**Required OAuth scope:** `repo`.

The `query` field accepts GitHub's code-search syntax. The schema's own example list:

- `content:Skill language:Java org:github`
- `NOT is:archived language:Python OR language:go`
- `repo:github/github-mcp-server`

Spaarke-relevant invocations (the orgs reflect the trusted-org list in the directive: `microsoft`, `Azure-Samples`, `OfficeDev`, `modelcontextprotocol`):

```json
// Find IAiToolHandler implementations across Microsoft's AI repos
{
  "name": "search_code",
  "arguments": {
    "query": "IAiToolHandler language:csharp org:microsoft",
    "perPage": 20
  }
}
```

```json
// Locate declarative-agent manifest examples in the Microsoft 365 sample tree
{
  "name": "search_code",
  "arguments": {
    "query": "\"$schema\" \"declarative-agent\" path:appPackage org:OfficeDev",
    "perPage": 30
  }
}
```

```json
// Find MCP server tool-registration patterns across MCP reference servers
{
  "name": "search_code",
  "arguments": {
    "query": "registerTool path:src org:modelcontextprotocol",
    "perPage": 50,
    "sort": "indexed",
    "order": "desc"
  }
}
```

```json
// Resolve a current API shape — list usages of GraphServiceClient in azureai-samples
{
  "name": "search_code",
  "arguments": {
    "query": "GraphServiceClient language:csharp repo:Azure-Samples/azureai-samples"
  }
}
```

> **Scoping tip.** `org:<name>` and `repo:<owner>/<name>` are the most reliable scoping qualifiers. Always include one when running from an agent loop — unscoped queries against GitHub's full corpus burn quota and return noise.

---

## `search_issues`

**Description (from snapshot):** "Search for issues in GitHub repositories using issues search syntax already scoped to `is:issue`."

**Required parameters:** `query` (string).
**Optional parameters:** `owner` + `repo` (scope to a single repo); `order` (`asc` | `desc`); `sort` (`comments`, `reactions`, `reactions-+1`, `reactions--1`, `reactions-smile`, `reactions-thinking_face`, `reactions-heart`, `reactions-tada`, `interactions`, `created`, `updated`); `page` (min 1); `perPage` (1–100).
**Required OAuth scope:** `repo`.

The server prepends `is:issue` automatically — write the rest of the query in GitHub's issue-search syntax.

```json
// Find open issues mentioning a specific regression in the upstream MCP server
{
  "name": "search_issues",
  "arguments": {
    "owner": "github",
    "repo": "github-mcp-server",
    "query": "is:open scope filter classic PAT",
    "sort": "updated",
    "order": "desc",
    "perPage": 25
  }
}
```

```json
// Cross-repo: long-tail Azure SDK issues mentioning OBO + Graph
{
  "name": "search_issues",
  "arguments": {
    "query": "org:Azure on-behalf-of graph token in:title,body",
    "sort": "comments",
    "order": "desc"
  }
}
```

```json
// Find Microsoft 365 agents toolkit issues about declarative-agent approval flow
{
  "name": "search_issues",
  "arguments": {
    "owner": "OfficeDev",
    "repo": "microsoft-365-copilot-samples",
    "query": "declarative agent admin approval label:question",
    "sort": "created",
    "order": "desc"
  }
}
```

> The `owner` + `repo` combo is the cleanest way to restrict to a single repository — equivalent to embedding `repo:owner/name` in the query but separated out for legibility.

---

## `get_file_contents`

**Description (from snapshot):** "Get the contents of a file or directory from a GitHub repository."

**Required parameters:** `owner`, `repo`.
**Optional parameters:** `path` (default `/`), `ref` (e.g. `refs/heads/main`, `refs/tags/v1.0.0`, `refs/pull/123/head`), `sha` (commit SHA — takes precedence over `ref`).
**Required OAuth scope:** `repo` (read-only tools work on public repos even without scopes — see `docs/scope-filtering.md`).

```json
// Read a specific file from the default branch
{
  "name": "get_file_contents",
  "arguments": {
    "owner": "microsoft",
    "repo": "agent-framework",
    "path": "samples/single-agent-loop/Program.cs"
  }
}
```

```json
// List a directory (omit path or use a trailing slash to list)
{
  "name": "get_file_contents",
  "arguments": {
    "owner": "Azure-Samples",
    "repo": "azureai-samples",
    "path": "scenarios/Assistants/"
  }
}
```

```json
// Pin to a specific commit SHA (reproducible reads)
{
  "name": "get_file_contents",
  "arguments": {
    "owner": "github",
    "repo": "github-mcp-server",
    "path": "docs/scope-filtering.md",
    "sha": "39d86b80af711a3277ffab08fa7d3068b3652913"
  }
}
```

```json
// Read a file from a specific pull-request head
{
  "name": "get_file_contents",
  "arguments": {
    "owner": "modelcontextprotocol",
    "repo": "servers",
    "path": "src/server.ts",
    "ref": "refs/pull/142/head"
  }
}
```

```json
// Read a release-tagged file
{
  "name": "get_file_contents",
  "arguments": {
    "owner": "microsoft",
    "repo": "mcp-interactiveUI-samples",
    "path": "trey-research-hr/manifest.json",
    "ref": "refs/tags/v0.3.0"
  }
}
```

> **Prefer `sha` for reproducibility.** When citing source in a curated knowledge-base entry or in an ADR, pin to a commit SHA — branches drift, tags can be moved.

---

## See also

- [`tool-catalog.md`](./tool-catalog.md) — full inventory of toolsets and tools
- [`docs/server-configuration.md`](./docs/server-configuration.md) — how to scope tool exposure via `--toolsets`, `--tools`, `--exclude-tools`
- [`docs/scope-filtering.md`](./docs/scope-filtering.md) — how PAT scopes filter tool availability
