> ⚠️ STUB — senior engineer review pending

# NOTES — github-mcp

Project-specific commentary on the GitHub MCP server and how Spaarke uses it. The headings below are scaffolded from the directive's "NOTES.md guidance" for this topic. Each section is a placeholder until reviewed by a senior engineer who has actually used GitHub MCP in agent loops against the Spaarke codebase.

---

## When to reach for GitHub MCP

_TODO: enumerate the specific situations where invoking a GitHub MCP tool is the right move for a Spaarke agent — long-tail API research between curated-knowledge refreshes, currency checks on Microsoft platform repos, cross-referencing patterns against MVP samples, debugging unfamiliar errors by searching upstream issues, locating a current example of an API shape that the model's training data is stale on._

## When NOT to reach for GitHub MCP

_TODO: define the negative case — what the agent should read from `knowledge/` (curated samples, NOTES.md files, snapshotted Microsoft Learn pages, ADRs) **before** reaching for live GitHub. Establish the rule that "anything covered by the curated reference tree should be read first." Note the specific tasks where GitHub MCP would be redundant or wasteful._

## Trusted-org scoping for Spaarke

_TODO: spell out the trusted-org allow-list and how it gets enforced. Directive lists: `microsoft`, `Azure-Samples`, `OfficeDev`, `modelcontextprotocol`, plus selected MVP accounts. Decide whether scoping is enforced via `org:` qualifiers baked into agent prompts, via a custom MCP host config that rejects out-of-list orgs, or both. Document the specific MVP accounts the team has vetted._

## Toolset restrictions for the project

_TODO: pick the minimal toolset enable-list. Candidates from the directive context: `context`, `repos`, `issues`, `pull_requests` (the default toolset minus `users`); add `code_security` and `dependabot` only if/when used. Decide on `--read-only` posture (almost certainly yes — Spaarke shouldn't be opening PRs or filing issues from an agent loop). Decide whether to use `--exclude-tools` to drop write operations that slip through. See `docs/server-configuration.md` for syntax._

## Auth setup (PAT vs OAuth)

_TODO: choose PAT or OAuth and document why. Classic PAT (`ghp_`) gets automatic scope filtering at startup — see `docs/scope-filtering.md`. Fine-grained PAT (`github_pat_`) skips scope filtering, with API enforcing instead. Remote-server OAuth uses scope challenges. For Spaarke's local-dev usage by individual engineers, classic PAT with `repo` and `read:org` may be enough; for any shared/server-side use, decide on rotation cadence and storage (env var, Key Vault, etc.). Reference `docs/scope-filtering.md` for the scope hierarchy._

## Cost discipline

_TODO: write the cost-discipline rules. GitHub MCP calls are individually cheap but agents will overuse them without explicit triggering rules. Concrete rules to consider: require an `org:` or `repo:` qualifier on every `search_code` / `search_issues` query; cap `perPage` at sensible defaults; prefer `get_file_contents` with a pinned `sha` over `ref:` for reproducibility; never read whole directory trees when a `search_code` query would do. Also note that the model can be tempted to chain `search_code` → `get_file_contents` for every match — the prompt or skill wrapper should discourage that._

## Open questions / decisions pending

- _TODO: which MCP host(s) does Spaarke standardize on (Claude Code, VS Code agent mode, both)?_
- _TODO: is GitHub MCP installed locally per-engineer (Docker image / Go binary) or accessed via the remote server at `api.githubcopilot.com`?_
- _TODO: how does GitHub MCP usage interact with the existing Dataverse MCP setup in `.mcp.json` — do we want both servers active simultaneously?_
- _TODO: do we surface GitHub MCP behind a Spaarke-specific skill (e.g. `.claude/skills/github-research/`) with explicit triggering rules, or leave it as a generic tool the agent can reach for at will?_

## Cross-references

- [`tool-catalog.md`](./tool-catalog.md) — full tool inventory
- [`invocation-examples.md`](./invocation-examples.md) — protocol-level examples
- [`docs/scope-filtering.md`](./docs/scope-filtering.md) — PAT scope handling
- [`docs/server-configuration.md`](./docs/server-configuration.md) — `--toolsets` / `--tools` / `--exclude-tools` / read-only
- [`docs/toolsets-and-icons.md`](./docs/toolsets-and-icons.md) — toolset taxonomy
- [`SOURCE.md`](./SOURCE.md) — provenance and gaps
