> ⚠️ STUB — senior engineer review pending

# NOTES — `dataverse-mcp`

Project-specific commentary on `dataverse-mcp`. Annotate from real Spaarke project experience; don't fabricate. Section structure:

- **§1. How this fits Spaarke's architecture** — when to reach for this, role/composition with other surfaces, what it replaces or composes with, preview/cost/licensing implications, decision criteria
- **§2. How we build with it** — manifest/code shape, auth wiring, gotchas, Spaarke divergence from canonical samples, code review checklist

Both sections required for "done"; honest TODOs are fine for what isn't yet known. When annotating, remove the `⚠️ STUB` banner above only after both §1 and §2 have substantive content (or honest TODOs).

---

## 1. How this fits Spaarke's architecture

### The three flavors of Dataverse-as-MCP

Microsoft surfaces Dataverse to MCP clients in (at least) three distinct ways. Their boundaries are not always crisp in current docs — getting clarity on which one to reach for in a given situation is the highest-leverage thing this NOTES.md can do.

#### 1a. Built-in MCP server (GA)

- Endpoint: `https://<org>.crm.dynamics.com/api/mcp`
- Tool catalog: `list_tables`, `describe_table`, `read_query`, `create_record`, `update_record`, `Delete Record`, `Create Table`, `Update Table`, `Delete Table`, `Search`, `Fetch` — see `samples/mcp-invocations/standard-tool-invocations.md` and `docs/data-platform-mcp.md`.
- Auth: tenant admin consent for the **Dataverse CLI** app (`0c412cc3-0dd6-449b-987f-05b053db9457`) + per-environment client allowlist in PPAC.
- Billing: chargeable from 2025-12-15 when called from non-Copilot-Studio agents, unless covered by Dynamics 365 Premium / M365 Copilot USL.

_TODO: When is this the right choice? What does Spaarke actually use it for today (schema inspection during dev, ad-hoc queries, post-deployment verification per repo-root CLAUDE.md)? What are the things teams keep reaching for it that the BFF should be doing instead?_

#### 1b. Business Skills (preview) — a.k.a. Dataverse intelligence / Work IQ extension

- The unit: a Markdown record with YAML frontmatter (see `samples/business-skill/SKILL.md` for shape).
- Distribution: ships as **Dataverse solution components** (the `microsoft/dataverse-business-skills` repo distributes a managed solution `.cab`).
- Available **only** on the preview endpoint `/api/mcp_preview`. Requires Managed Environment + admin enablement of Dataverse intelligence (see `docs/data-platform-intelligence.md`).
- Authored by humans in the markdown-with-YAML format; consumed by agents as natural-language procedure.

_TODO: How does this relate to Spaarke's existing **structured playbook tables** (`sprk_playbook` / `sprk_playbookstep` / etc.)? The directive explicitly says: "existing structured playbook tables stay as reference data; procedural content migrates to Business Skills over time." Draft the concrete migration pattern — which existing playbook content is a candidate? What stays in tables (rule data, threshold values, allowed enums) vs. what moves to Business Skills (the procedural how-to-think-about-it)? See also `.claude/skills/jps-*` and `docs/architecture/playbook-architecture.md` for current state._

#### 1c. App MCP (status uncertain) / custom MCP tools

- The directive describes this as: "model-driven app exposed as an MCP server with custom tool definitions."
- **No dedicated Learn page found** as of 2026-05-14 (see GAPs in `SOURCE.md`).
- The admin doc gives one hint: *"MCP-named custom APIs are regular Dataverse APIs and aren't restricted by this setting."* — suggesting custom tools surface as **MCP-named custom Dataverse APIs** discoverable via `/api/mcp`.

_TODO: This is the murkiest of the three. Find a Microsoft contact or wait for GA docs. Until then, the practical guidance for Spaarke is: **don't bet new agent-callable surface on App MCP yet.** Document what we'd want to know before adopting it (auth model, solution packaging, widget binding, performance, governance)._

### When each is the right choice

A decision tree for the team:

```
Need to expose a verb to an agent?
├── It's a Spaarke-domain document operation (open, preview, move, classify, route)
│   └── Custom Spaarke MCP server in the BFF (see knowledge/mcp-apps/ and knowledge/foundry-agent-service/)
├── It's procedural knowledge ("how do we qualify a lead", "how do we run a QBR")
│   └── Business Skill (preview, /api/mcp_preview)
├── It's data CRUD / schema inspection during development
│   └── Built-in Dataverse MCP server (Claude Code, GitHub Copilot)
└── It's data CRUD at runtime in production code
    └── NOT MCP — use the BFF + SpeFileStore + Dataverse SDK; see ADR-007
```

_TODO: Validate this tree with the team. Add concrete Spaarke examples to each branch — past decisions where we picked X over Y and why. The decision tree must reflect what we actually do, not aspiration._

### Metering for non-Copilot-Studio consumers

From `docs/data-platform-mcp.md`:

> *"Starting December 15, 2025 Dataverse MCP tools are charged when accessed by AI agents created outside of Microsoft Copilot Studio. If you have Dynamics 365 Premium licenses … or a Microsoft 365 Copilot User Subscription License (USL), you aren't charged for accessing Dynamics 365 data, even when that data is accessed from outside Microsoft Copilot Studio."*
>
> *"The Search tool is billed at the same Copilot Credit rate as Tenant graph grounding, while all other tools follow the Text and generative AI tools (basic) per 10 response Copilot credit rate."*

_TODO: Audit which Spaarke flows touch the Dataverse MCP server today (per the repo-root `.mcp.json`, it's configured as a Claude Code dev-time tool). Confirm we don't have any **runtime** code paths that hit `/api/mcp` — those would be unexpectedly billed. Document the Spaarke licensing posture: do our Dataverse environment licenses cover the people who use Claude Code against the dev environment, or is each developer's Claude Code usage a metered call?_

### Spaarke's migration pattern — structured playbook tables → Business Skills + App MCP

This is the strategically important section. The directive gives the headline:

> *"existing structured playbook tables stay as reference data; procedural content migrates to Business Skills over time; new agent-callable verbs that don't need imperative code go through App MCP custom tools; imperative document operations stay in the custom Spaarke MCP server."*

Decomposing this into actionable Spaarke guidance:

#### What stays in structured Dataverse tables (`sprk_playbook*`)

- Rule data (thresholds, allowed enums, scoring weights).
- Cross-skill **reference data** that multiple skills query (chart of accounts, document classifications, jurisdiction lookups).
- Anything an analyst would maintain via the Dataverse UI rather than a markdown edit.

_TODO: Walk the actual `sprk_playbook` schema and bucket each table column into "stays as data" vs. "migrates to skill prose." See `docs/data-model/` and `docs/architecture/playbook-architecture.md`._

#### What migrates to Business Skills (markdown-with-YAML)

- The procedural how-to embedded in our current JPS playbook bodies.
- "When the user says X, do Y, then Z" routing logic.
- BANT-style discovery scripts (see Microsoft's `account-briefing-generator` for the template).

_TODO: Pick the highest-leverage existing playbook to migrate first as a proof-of-concept. Probably one of the analysis actions managed via `.claude/skills/jps-action-create`. Document the conversion: original JPS → equivalent Business Skill SKILL.md._

#### What goes to App MCP / custom MCP tools (when GA)

- Agent-callable verbs that read/write Dataverse but don't fit naturally as a SQL `read_query` (e.g., a verb that computes a derived value using current business rules baked into a Custom Action).
- Operations that need transactional / multi-record consistency the built-in tools don't give.

_TODO: Maintain a backlog of "verbs we'd build on App MCP if it shipped tomorrow." Refresh it each time App MCP docs evolve._

#### What stays in the custom Spaarke MCP server (BFF)

- Anything that calls Microsoft Graph (SharePoint Embedded operations: container CRUD, file upload, file webUrl resolution).
- Anything that needs OBO auth flow with downstream Graph permissions.
- Anything that requires Azure AI / Document Intelligence orchestration.
- Anything that crosses ADR-007's facade boundary (SPE file operations must not leak Graph SDK types).

_TODO: Reconcile this with `knowledge/mcp-apps/NOTES.md` once that's written. The split between "Dataverse MCP custom tool" and "Spaarke BFF MCP server" is the single most important architectural decision in this domain — both notes files must agree._

---

## 2. How we build with it

### Business Skills authoring conventions

Based on inspection of the 16 published Microsoft skills in `microsoft/dataverse-business-skills`:

#### YAML frontmatter shape

```yaml
---
name: skill-slug-kebab-case
description: |
  One-sentence purpose. Followed by exhaustive natural-language trigger phrases:
  "Use when user says X", "Y", "Z" — or uploads/pastes a transcript / asks "how do I prep for...".
metadata:
  author: Dataverse                # or your org / team name
  version: 1.0.0                   # semver
  category: sales-productivity     # taxonomy bucket — see Microsoft's set
---
```

#### Body conventions (observed across all 16 skills)

- **H1 = display name** of the skill (matches `name` slug → Title Case).
- **Opening prose paragraph** sets context: who calls this and why.
- **`## Instructions`** with sub-steps `### Step 1: …`, `### Step 2: …`. Each step contains:
  - A short paragraph saying *what to do*.
  - A T-SQL `SELECT` / `INSERT` query block — the agent will call `read_query` / `create_record` with this.
  - Field interpretation tables (e.g., budget code 0 = No budget, 1 = May buy).
- **`### Workflow`** subsection inside Instructions for multi-step procedures with branching.
- **`### Output Format`** — exact template (sometimes a box-drawn ASCII layout) of what the agent should return.
- **`### Example Interaction`** — at least one prompt + expected output pair.
- **`## Examples`** (top-level, separate from Instructions) — 2-3 more end-to-end scenarios.
- **`### Dataverse Tables Used`** — explicit table list (gives the agent a hint to call `describe_table` on these).
- **`### Key Fields Reference`** — schema documentation for non-obvious fields, including choice-code mappings.
- **`## Troubleshooting`** — common failure modes with cause/solution pairs.

#### "Use when" / "do not use when" phrasing

Observed pattern in `description`:

> *"Use when user says 'log this call', 'process this transcript', 'save this call to CRM', 'create a phone call record from this transcript', uploads a call recording transcript, or pastes meeting/call notes to be logged."*

- Trigger phrases are **inside the YAML `description`** (not a separate field).
- They are quoted verbatim user utterances ("log this call") plus action-shape descriptions ("uploads a call recording transcript").
- **No explicit "do not use when" examples were found in the 16 Microsoft skills inspected.** The directive mentions this as expected convention; if the team adopts it, decide where to put it — probably as a final clause in `description` or as a new top-level body section.

_TODO: Confirm Microsoft's "use when / do not use when" convention by looking at the GA Microsoft samples once they ship. For now, the pragmatic Spaarke pattern is: put "use when" phrases in `description`; put "do not use when" exclusions in a `## When NOT to use this skill` body section near the top._

_TODO: Document Spaarke's own naming convention for skill slugs (kebab-case? include domain prefix? `sprk-` namespace?) and our category taxonomy._

### Solution-packaging behavior

- Business Skills are **first-class Dataverse solution components**. The `microsoft/dataverse-business-skills` repo ships a managed `.cab` (see `solutions/msdyn_BusinessSkills_managed.cab`).
- That means they move through environments **the same way as forms, views, web resources, plugin registrations**: export from dev → import to test → import to prod via PAC CLI.
- Implication: a skill checked into source control as `SKILL.md` is **a build artifact** — the solution import is what actually registers it with the environment's MCP preview endpoint.

_TODO: Define Spaarke's source-of-truth model. Option A: skills live in this repo as `.md`, get packed into a `Sprk.BusinessSkills` Dataverse solution, deployed via `dataverse-deploy` skill. Option B: skills are authored directly in the Dataverse UI; this repo only mirrors them. Pick one and document it. Spaarke's existing pattern (per CLAUDE.md / dataverse-deploy skill) is "source-of-truth in repo, deploy via PAC CLI" — so Option A aligns. But we need the build pipeline._

_TODO: Document how a Business Skill `.md` file becomes a record in the Dataverse `msdyn_businessskill` table (or whatever the actual table name is — verify via `mcp__dataverse__list_tables` once you have an environment with the preview endpoint on)._

### Operational gotchas observed during curation

_TODO: Each of these is a hypothesis from reading the docs; validate against actual Spaarke usage._

- **Tenant admin consent must be granted before any developer can use the local proxy.** This is a one-time admin action on the Dataverse CLI app ID `0c412cc3-0dd6-449b-987f-05b053db9457`. If a new tenant onboards, this is step 0.
- **The `Dataverse CLI` allowlist entry sometimes doesn't auto-appear in PPAC.** The doc tells admins they may need to add it manually with the same app ID. Likely worth a script in `scripts/`.
- **Business Skills require the preview endpoint** (`/api/mcp_preview`), not `/api/mcp`. Two separate Claude Code MCP server registrations may be necessary during the preview period — one for stable tools, one for skills.
- **`@microsoft/dataverse` npm package transports differ.** Claude desktop uses stdio (proxy as command); GitHub Copilot CLI uses HTTP directly to `/api/mcp`; the .NET dotnet-tool proxy (legacy) uses HTTP too. If team members report intermittent failures, ask which transport they're on.

---

## Cross-references

- ADRs likely to interact with this topic: ADR-002 (thin Dataverse plugins — Business Skills are **not** plugins, but the rule "plugins must stay <50ms" extends conceptually: a Business Skill that the agent invokes should not chain into heavy plugin execution chains either). _TODO: confirm._
- Spaarke skills: `.claude/skills/dataverse-deploy/` (used to import the solution containing skills), `.claude/skills/jps-playbook-design/` (parallel concept — the playbook authoring discipline transfers).
- Repo-root MCP config: see `.mcp.json` for how the Dataverse MCP server is wired into Claude Code at the Spaarke repo root (uses `@microsoft/dataverse` npm proxy against `https://spaarkedev1.crm.dynamics.com`).
- Other knowledge topics this NOTES.md should align with once written: `mcp-apps/` (widget pattern, custom tool authoring), `foundry-agent-service/` (tool-binding patterns), `work-iq/` (the upstream concept that Dataverse intelligence extends).

---

_End of stub. Senior engineer: please replace every `_TODO_` with substantive project-specific commentary and remove the stub warning at the top once the file has been substantively annotated._
