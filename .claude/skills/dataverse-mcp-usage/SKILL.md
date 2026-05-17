---
description: Use Dataverse MCP — built-in tools, Business Skills, App MCP custom tools
tags: [dataverse, mcp, business-skills, app-mcp, power-platform]
techStack: [dataverse, power-platform, mcp]
appliesTo: ["**/Dataverse/**/*", "**/SKILL.md", "**/playbooks/**/*", "**/business-skills/**/*"]
alwaysApply: false
---

# dataverse-mcp-usage

> **Category**: Development
> **Last Updated**: 2026-05-14

---

## Purpose

Anchor Dataverse-as-MCP work in current Microsoft idioms across **three flavors**: the built-in Dataverse MCP server (standard tools like `read_query`, `describe_table`), Business Skills (Markdown-with-YAML procedural knowledge surfaced through MCP), and App MCP (model-driven-app-scoped custom tools). The choice of flavor determines authoring format, deployability, and metering. This skill stops generated code from collapsing all three into "just call Dataverse Web API."

Without this skill, generated work drifts toward training-data patterns that predate Business Skills (preview surface), App MCP, and the migration of canonical samples from `microsoft/Dataverse-Web-API-Samples` (which doesn't exist) to `microsoft/Dataverse-MCP` + `microsoft/dataverse-business-skills`.

---

## Applies When

- Authoring a Business Skill (Markdown body + YAML frontmatter, surfaced through Dataverse MCP)
- Building an App MCP custom tool for a model-driven app
- Deciding whether procedural knowledge belongs in: a Dataverse table, a Business Skill, an App MCP tool, or the custom Spaarke MCP server
- Wiring Dataverse MCP into a declarative agent or server-side agent
- Migrating existing Spaarke playbook tables to Business Skills format
- **NOT applicable** for: pure data ops on records (use Dataverse MCP tools directly via `mcp__dataverse__*`), schema creation (use `dataverse-create-schema`), Spaarke's custom MCP server tool handlers (use `mcp-tool-handler`)

---

## Workflow

### Step 1: Load knowledge context (mandatory)

Read in this order:

1. **`knowledge/dataverse-mcp/NOTES.md`** — Spaarke's migration pattern from structured tables → Business Skills + App MCP; when each flavor is right.
2. **`knowledge/dataverse-mcp/samples/business-skill/SKILL.md`** — Canonical Business Skill authoring format (verbatim Microsoft example).
3. **`knowledge/dataverse-mcp/samples/business-skill/log-call-transcripts-SKILL.md`** — Second Business Skill example.
4. **`knowledge/dataverse-mcp/samples/business-skill/account-briefing-test-scenarios.md`** — Test scenarios convention.
5. **`knowledge/dataverse-mcp/samples/mcp-invocations/standard-tool-invocations.md`** — How the built-in MCP tools get called (verbatim Claude transcripts).
6. **`knowledge/dataverse-mcp/samples/mcp-client-config/`** — Client config (Claude Desktop, Claude Code, GitHub Copilot CLI). Use this when wiring a new client.
7. **`knowledge/dataverse-mcp/docs/data-platform-mcp.md`** — Authoritative tool catalog + metering.
8. **`knowledge/dataverse-mcp/docs/data-platform-intelligence.md`** — Business Skills umbrella (Work IQ extension surface).

### Step 2: Choose the right flavor

| Need | Flavor | Authoring format |
|---|---|---|
| Read/write Dataverse records imperatively | **Built-in MCP server** (standard tools) | Already exists; just call `mcp__dataverse__read_query`, `_create_record`, etc. |
| Procedural knowledge / playbook content the agent should follow | **Business Skill** | Markdown body + YAML frontmatter; ships in a solution; preview metering |
| App-scoped verb (e.g., "create matter brief from selected records") | **App MCP custom tool** | Model-driven-app-bound; deployable with the app |
| Imperative document operation (summarize doc, redline contract) | **Spaarke custom MCP server** | C# `IAiToolHandler` in BFF (use `mcp-tool-handler` skill) |

**Spaarke migration heuristic** (from `dataverse-mcp/NOTES.md`):

- Existing structured playbook tables → keep as reference data
- Procedural content currently embedded in app logic → migrate to **Business Skills** over time
- New agent-callable verbs that don't need imperative code → **App MCP custom tools**
- Document operations (read/write SPE, AI extraction) → **Spaarke MCP server** (BFF)

### Step 3: Business Skill authoring conventions

When authoring a Business Skill, follow `knowledge/dataverse-mcp/samples/business-skill/SKILL.md` exactly:

- **YAML frontmatter** required: `name`, `description`, `triggers` (or `use_when`/`do_not_use_when`), `model`, `dataverse_tools_used`
- **Trigger phrases** in YAML drive routing — be specific. Generic triggers cause overuse and metering pain.
- **Body is Markdown** — pure instructions to the agent. No code fences for non-code; use them for tool call examples.
- **Test scenarios** — every Skill should have a companion `*-test-scenarios.md` documenting expected invocations.
- **Solution packaging** — Business Skills move through solutions automatically; do NOT inline them in app code.

### Step 4: App MCP custom tool authoring

- Tools are bound to a specific model-driven app and gated by that app's security
- Custom tools surface as MCP-named custom Dataverse APIs (per docs; no Microsoft sample of a widget-attached custom tool exists yet — see `dataverse-mcp/SOURCE.md` GAPs)
- For widgets on custom tools, the pattern will come from `mcp-apps/` curation (cross-reference)

### Step 5: Metering awareness

Per `knowledge/dataverse-mcp/docs/data-platform-mcp.md`:

- Non-Copilot-Studio consumers (e.g., Claude Code calling Dataverse MCP) are **metered** in preview
- Spaarke production usage should budget for this — watch for cost regressions when adding new Business Skill triggers
- Built-in Dataverse MCP tools (read_query, etc.) called from Copilot Studio are inside the Copilot Studio license

### Step 6: Code review checklist

- [ ] Flavor choice matches the heuristic table above
- [ ] Business Skill YAML has specific (not generic) `use_when` / `do_not_use_when` triggers
- [ ] Test scenarios file exists for every new Business Skill
- [ ] Solution component included for Business Skills (so they move through environments)
- [ ] Metering implications flagged in PR description (if new triggers + non-Copilot-Studio consumer)
- [ ] No "let me write a custom Dataverse API endpoint" code when a Business Skill or App MCP tool would do

---

## Conventions

- Business Skills live under `src/dataverse/business-skills/<skill-name>/SKILL.md`
- App MCP custom tool definitions live with the model-driven app solution
- Triggers in Business Skill YAML use the **same vocabulary** users use in Copilot Chat (not internal jargon)

## Resources

| Resource | Purpose |
|----------|---------|
| `knowledge/dataverse-mcp/NOTES.md` | Spaarke's three-flavor migration pattern |
| `knowledge/dataverse-mcp/samples/business-skill/` | Canonical Business Skill authoring examples |
| `knowledge/dataverse-mcp/docs/data-platform-mcp.md` | Authoritative tool catalog + metering |
| `knowledge/dataverse-mcp/docs/data-platform-mcp-other-clients.md` | Claude Desktop / Claude Code wiring |

## Output

When this skill completes, expect:
- A new Business Skill / App MCP tool / Spaarke MCP tool handler **of the right flavor**
- A clear note in the PR description on metering implications (if applicable)
- A migration plan if replacing existing tables/code with Business Skills (don't drop the old surface until consumers move)
