# SOURCE — github-mcp

> Provenance for everything curated under `knowledge/github-mcp/`. See [`../README.md`](../README.md) for directory conventions.

## Upstream repos

| Repo | Branch | Commit SHA | Pulled | Notes |
|---|---|---|---|---|
| [`github/github-mcp-server`](https://github.com/github/github-mcp-server) | `main` | `39d86b80af711a3277ffab08fa7d3068b3652913` | 2026-05-14 | Official GitHub MCP server (Go). Commit captured during shallow clone to `c:/tmp/github-mcp-server`; clone deleted after curation. |

No MVP/community repos were pulled for this topic — the upstream README and `docs/` tree already document the protocol surface comprehensively.

## Curated files

### Top level

| File | Origin | Description |
|---|---|---|
| `tool-catalog.md` | `README.md` lines 553–1431 (sections "Available Toolsets", "Additional Toolsets in Remote GitHub MCP Server", "Tools", "Additional Tools in Remote GitHub MCP Server"), prepended with a synthesized header | Full inventory of toolsets and individual tools, with each tool's required OAuth scopes and input parameters. Snapshot of the auto-generated README catalog at commit `39d86b8`. |
| `invocation-examples.md` | Synthesized from `pkg/github/__toolsnaps__/{search_code,search_issues,get_file_contents}.snap` (input-schema JSON) + matching README parameter descriptions | Protocol-level invocation examples for the three tools the directive calls out. Examples use Spaarke's trusted-org list (microsoft, Azure-Samples, OfficeDev, modelcontextprotocol). |
| `NOTES.md` | Stub | Senior-engineer review pending — headings only, no fabricated substance. |

### `docs/` — reference docs snapshots

| File | Origin | Description |
|---|---|---|
| `docs/scope-filtering.md` | `docs/scope-filtering.md` in upstream repo | How the server filters tools based on classic PAT scopes; OAuth scope-challenge behavior for the remote server; PAT vs OAuth vs fine-grained vs GitHub-App token handling. |
| `docs/server-configuration.md` | `docs/server-configuration.md` in upstream repo | Complete recipe set for `--toolsets`, `--tools`, `--exclude-tools`, read-only, dynamic discovery, lockdown, insiders, MCP Apps, and scope filtering — both local-server CLI flags and remote-server `X-MCP-*` headers. |
| `docs/toolsets-and-icons.md` | `docs/toolsets-and-icons.md` in upstream repo | Internal documentation of the toolset metadata model (`ToolsetID`, `Description`, `Default`, `Icon`). Useful when reasoning about how the toolset taxonomy is shaped. |

Each docs/ file has YAML frontmatter recording the source URL, commit SHA, and fetch date.

## Files NOT curated and why

- **Full upstream `README.md`**: 87 KB and ~60 KB of that is the tool catalog already extracted into `tool-catalog.md`. The remaining sections (install instructions for 12+ MCP hosts, GHES/ghe.com setup, Docker invocations, lockdown/insiders mode mechanics) duplicate material in `docs/server-configuration.md`. Skipping the full snapshot to stay within the curation budget; consult upstream directly when needed.
- **`docs/installation-guides/install-*.md`** (Claude, Cursor, VS Code, Copilot CLI, etc.): host-specific config snippets — referenced in `docs/server-configuration.md` and not needed for the Spaarke knowledge base. Pull individual ones if/when we standardize on a host.
- **`docs/remote-server.md`**, **`docs/host-integration.md`**, **`docs/policies-and-governance.md`**, **`docs/error-handling.md`**, **`docs/testing.md`**, **`docs/tool-renaming.md`**, **`docs/streamable-http.md`**, **`docs/insiders-features.md`**: skipped for budget. These cover hosting/policy/protocol detail that is operational rather than core curation.
- **Tool source code** (`pkg/github/*.go`): 50+ Go files implementing each tool. The README's auto-generated catalog is itself a faithful summary of every tool's parameter shape and scope, so copying the Go is redundant. The schema snapshots in `pkg/github/__toolsnaps__/*.snap` (also not copied) were read as references for `invocation-examples.md` and are easily re-readable from upstream when needed.

## Gaps

- **GAP — GitHub Copilot skillsets documentation (`https://docs.github.com/en/copilot/customizing-copilot/extending-copilot-chat-with-skillsets/about-skillsets`):** URL **404s** on 2026-05-14. Several adjacent URLs also 404 (`/en/copilot/concepts/build-copilot-extensions/about-skillsets`, `/en/copilot/how-tos/build-copilot-extensions/build-a-skillset-extension`, `/en/copilot/concepts/build-copilot-extensions`). The GitHub Copilot docs TOC at `https://docs.github.com/en/copilot` (fetched 2026-05-14) lists "Custom agents" and "Skills" under Customization but no longer surfaces a discrete skillsets concept page. The skillsets surface appears to have been folded into the broader Copilot extensions / custom agents documentation. **Action for next refresh:** search the current Copilot docs tree for the canonical replacement page and snapshot it; if the concept has been retired, note that in `NOTES.md`.

## Refresh notes

- Upstream is highly active — at clone time the latest commit was `39d86b8` ("Replace ingress IFC reader list with private marker (#2478)"), 2026-05-14. Re-clone and diff at next monthly refresh; the tool catalog in particular drifts as new toolsets and tools land (recent additions include `copilot_spaces` and `github_support_docs_search` on the remote server, both noted in `tool-catalog.md`).
- The `__toolsnaps__/*.snap` files are the most stable place to verify tool input schemas — they're committed JSON schemas regenerated by CI. Cite them when a tool's behavior is ambiguous from the README alone.
