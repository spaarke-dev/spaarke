# Spaarke Microsoft Knowledge Base — Claude Code Setup Directive

**Audience**: Claude Code agent executing the knowledge base setup
**Goal**: Establish a curated, annotated, agent-readable knowledge base for the Microsoft AI platform pieces Spaarke depends on
**Estimated effort**: 1 week for initial build, 2-4 hours/month for refresh
**Deliverable location**: `spaarke/knowledge/`

---

## Why this work matters

Claude Code's training cutoff is months behind the Microsoft platform releases the Spaarke project depends on. Without a curated knowledge base, every task involving Foundry IQ, Agent Framework, MCP Apps, declarative agents, or Dataverse MCP risks producing plausible-looking wrong code. This setup creates a local reference that the agent loads on demand, ensuring code follows current platform idioms.

The knowledge base is intentionally simple: one directory per platform piece, with curated samples and annotations inside. No separation between Microsoft and community sources — both go in the same place, sorted by topic.

---

## Phase 0: Verify environment and prerequisites

Before any file creation, verify the environment is ready.

### Tasks

1. Confirm `spaarke/` is the project root (look for the existing `src/`, `ADR-*.md` files, etc.)
2. Verify `git` is available
3. Verify `gh` (GitHub CLI) is available — install if not (`brew install gh` on macOS, `apt install gh` on Debian)
4. Verify network access to `github.com`, `learn.microsoft.com`, `raw.githubusercontent.com`
5. Confirm no existing `spaarke/knowledge/` directory; if one exists, stop and ask the operator before proceeding

### Acceptance

Working directory verified, tools available, no conflicting structure. Proceed to Phase 1.

---

## Phase 1: Create the directory structure

Create the directory skeleton. Use the topic-per-folder pattern; do not subdivide by source type.

```
spaarke/
└── knowledge/
    ├── README.md
    ├── REFRESH-LOG.md
    ├── m365-copilot/
    ├── mcp-apps/
    ├── declarative-agents/
    ├── agent-framework/
    ├── foundry-agent-service/
    ├── foundry-iq/
    ├── work-iq/
    ├── dataverse-mcp/
    ├── sharepoint-embedded/
    ├── azure-ai-search/
    └── github-mcp/
```

### Tasks

1. Create `spaarke/knowledge/`
2. Create each topic subdirectory listed above
3. Create `knowledge/README.md` with the following structure:
   - Purpose of the knowledge base (one paragraph)
   - Directory conventions (each topic folder contains `SOURCE.md`, `NOTES.md`, and curated reference files)
   - How Claude Code uses it (skills in `.claude/skills/` reference files here)
   - Refresh cadence (monthly, tracked in `REFRESH-LOG.md`)
4. Create `knowledge/REFRESH-LOG.md` with a single initial entry: today's date and "Initial setup"

### Acceptance

Directory structure exists; README explains the convention; REFRESH-LOG.md has one entry.

---

## Phase 2: Populate each topic folder

For each topic folder, the work is the same shape: pull curated samples from the canonical Microsoft repo (and MVP repos when noted), write a `SOURCE.md` for provenance, write a `NOTES.md` for project-specific commentary. Do this for one topic at a time, completing all three artifacts before moving to the next.

### Common structure for every topic folder

Each `knowledge/<topic>/` directory contains:

- `SOURCE.md` — provenance: where each file came from, what commit, when pulled, why it's included
- `NOTES.md` — project-specific commentary: what this pattern does, how Spaarke applies it, what to modify, what to avoid
- One or more curated subdirectories containing the actual sample code

### Common process per topic

1. Identify the canonical repo(s) for this topic from the sources below
2. Clone the repo to a temporary location (e.g., `/tmp/<repo-name>`)
3. Identify the specific subdirectories or files that exemplify the pattern Spaarke needs
4. Copy them into `knowledge/<topic>/<sample-name>/`, preserving directory structure
5. Write `SOURCE.md` recording: source repo URL, commit SHA, date pulled, list of files included, brief description of what each demonstrates
6. Write `NOTES.md` with: what the sample teaches, how it applies to Spaarke, project-specific modifications, common pitfalls (initially can be stubs — substantive annotations come from the senior engineer review pass)
7. Delete the temporary clone
8. Add an entry to `REFRESH-LOG.md` noting the topic completed

---

## Phase 2.1: `m365-copilot`

The core Microsoft 365 Copilot extensibility documentation samples — the foundation for everything else.

### Sources

**Primary** (clone and curate from):
- `https://github.com/OfficeDev/microsoft-365-copilot-samples` — official Microsoft 365 Copilot samples (declarative agents, knowledge sources, capabilities)
- `https://github.com/microsoft/copilot-camp` — walkthrough labs for building Copilot extensions

**Reference docs to snapshot** (curl as markdown into a `docs/` subfolder):
- `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/overview-declarative-agent` — declarative agent overview
- `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/whats-new` — current "what's new" page
- `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/optimize-content-retrieval` — grounding best practices

### What to curate

From `microsoft-365-copilot-samples`:
- One complete declarative agent example with manifest
- One example with `OneDriveAndSharePoint` knowledge source configured
- One example with API plugin / action

From `copilot-camp`:
- The "Path E" lab series materials (extend M365 Copilot)
- The declarative agent lab walkthrough

### NOTES.md guidance

The annotations for this folder should cover:
- How a declarative agent manifest is structured (key fields, what they control)
- How knowledge sources are wired in
- How to point a SharePoint knowledge source at an SPE container (referencing the `sharepoint-embedded` folder)
- The admin approval flow via Agent Builder
- Where Spaarke's declarative agent diverges from generic samples (matter context, Dataverse MCP wiring, custom MCP server bindings)

---

## Phase 2.2: `mcp-apps`

MCP Apps for rendering rich UI inside Copilot Chat — the surface for tabular review, comparison views, redline review panes.

### Sources

**Primary**:
- `https://github.com/microsoft/mcp-interactiveUI-samples` — official MCP Apps samples (Trey Research, Approvals/Box, Zava Insurance, FieldOps, Employee Training)
- `https://github.com/modelcontextprotocol/servers` — MCP protocol reference servers (for understanding protocol idiom)

**Reference docs to snapshot**:
- `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-mcp-apps` — MCP Apps overview
- `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-mcp-apps-ui-guidelines` — UX guidelines
- `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/declarative-agent-ui-widgets` — adding widgets to declarative agents
- `https://devblogs.microsoft.com/microsoft365dev/mcp-apps-now-available-in-copilot-chat/` — original announcement

### What to curate

From `mcp-interactiveUI-samples`:
- **Trey Research HR** — closest pattern to a Spaarke workspace widget (Fluent UI dashboard, bulk editor, detail views)
- **Approvals (Box)** — closest pattern to Spaarke's redline review queue (risk-triaged approval queue, bulk actions, inline review)
- The `agents-toolkit-screenshots/` folder — visual reference for inline vs side-by-side mode rendering

### NOTES.md guidance

Annotations should cover:
- Inline mode vs side-by-side mode — when each applies for Spaarke skills
- Widget state management — receiving data from MCP tool, capturing user interactions, returning state
- Fluent UI v9 usage (aligns with ADR-021)
- Sandboxed iframe constraints — no localStorage, scoped DOM, theme variables
- How Spaarke maps skills to widget patterns:
  - Redline → inline summary card + deep-link to side-by-side review pane
  - TabularReview → side-by-side grid widget
  - Compare → side-by-side diff widget
  - PlaybookDraft → inline rationale card
  - CitationCheck → inline status cards
- The `/generate-mcp-app-ui` skill Microsoft published for Claude Code and GitHub Copilot CLI

---

## Phase 2.3: `declarative-agents`

Deeper than the m365-copilot folder — specifically about manifest structure, knowledge bindings, and tool bindings.

### Sources

**Primary**:
- `https://github.com/OfficeDev/microsoft-365-copilot-samples` (specific declarative agent subdirectories)
- `https://github.com/microsoft/teams-toolkit` — M365 Agents Toolkit / Teams Toolkit
- MVP samples: search GitHub for `user:bobgerman declarative agent`, `user:garrytrinder declarative agent`, `user:waldekm declarative agent` — pick one or two that show production-quality patterns

**Reference docs to snapshot**:
- `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/declarative-agent-manifest` — manifest spec
- `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/agent-builder-add-knowledge` — knowledge source configuration
- `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/declarative-agent-architecture` — architecture overview

### What to curate

- One complete agent with full manifest, instructions, conversation starters, knowledge sources, action plugins
- One agent that uses an MCP server (custom MCP, not just built-in tools)
- One agent that demonstrates the `OnlyAllowedSources` behavior

### NOTES.md guidance

Annotations should cover:
- Manifest structure field by field (declarative agent v1.x schema)
- Knowledge source types: SharePoint, OneDrive, embedded files, web, Copilot connectors, MCP servers
- How instructions affect grounding behavior (priority, fallback, retrieval scoping)
- Admin approval flow and what changes trigger re-approval
- Spaarke's declarative agent shape — three knowledge sources (SPE via SharePoint knowledge source, Dataverse MCP, Foundry IQ knowledge base), tool bindings to Spaarke MCP server, Work IQ MCP for collaboration context

---

## Phase 2.4: `agent-framework`

Microsoft Agent Framework 1.0 — for server-side agent execution in the Spaarke BFF.

### Sources

**Primary**:
- `https://github.com/microsoft/agent-framework` — the framework itself (clone and curate from `samples/`)

**Reference docs to snapshot**:
- `https://learn.microsoft.com/en-us/agent-framework/overview` — framework overview
- `https://learn.microsoft.com/en-us/agent-framework/concepts/agents` — agent concept
- `https://learn.microsoft.com/en-us/agent-framework/concepts/workflows` — workflows

### What to curate

From the `samples/` directory:
- A simple single-agent loop with tool calling
- A multi-agent workflow with handoffs
- An example with streaming responses
- An example with OpenTelemetry tracing wired up

### NOTES.md guidance

Annotations should cover:
- Agent Framework's place in the Spaarke architecture (server-side single-agent loops in BFF for event-driven or scheduled agent work)
- When to use Agent Framework vs. when to use Foundry Agent Service (in-process vs. durable)
- Tool definition idioms (type-safe, attribute-based)
- Streaming response handling
- How OTel traces flow into the customer's Application Insights
- Where Spaarke's `IAiToolHandler` pattern (per ADR-013) intersects with Agent Framework tool definitions

---

## Phase 2.5: `foundry-agent-service`

Foundry Agent Service — for multi-step durable workflows with HITL gates.

### Sources

**Primary**:
- `https://github.com/Azure-Samples/azureai-samples` — Azure AI samples (clone, navigate to Foundry-relevant subdirectories)
- `https://github.com/Azure-Samples/azure-ai-foundry` — Foundry-specific samples
- `https://github.com/Azure-Samples/openai-end-to-end-baseline` — opinionated end-to-end architecture (for reference architecture context, not direct copy)

**Reference docs to snapshot**:
- `https://learn.microsoft.com/en-us/azure/ai-foundry/agents/overview` — Agent Service overview
- `https://learn.microsoft.com/en-us/azure/ai-foundry/agents/workflows` — workflow runtime
- `https://learn.microsoft.com/en-us/azure/ai-foundry/agents/memory` — agent memory (preview)
- `https://learn.microsoft.com/en-us/azure/ai-foundry/agents/durable-orchestration` — Durable Agent Orchestration pattern

### What to curate

- A graph-based workflow definition with multiple nodes
- An example with `wait_for_external_event` (HITL gate)
- An example with A2A protocol composition
- An example with MCP server tool binding

### NOTES.md guidance

Annotations should cover:
- When Foundry Agent Service is the right choice (durable, multi-day, HITL, A2A)
- Workflow definition syntax and graph patterns
- HITL primitives — `wait_for_external_event`, approval gates, resumption semantics
- A2A protocol basics — how cross-system agent composition works
- Foundry Memory — agent state persistence, custom user-scope headers, cost model
- Evaluator integration — how to wire evaluators into agent executions
- Spaarke's multi-step legal workflows (full-matter diligence, NDA negotiation chain, regulatory monitoring) and how they map to Foundry workflows

---

## Phase 2.6: `foundry-iq`

Foundry IQ — the managed knowledge layer for grounded retrieval.

### Sources

**Primary**:
- `https://github.com/Azure-Samples/azure-ai-foundry` (knowledge base subdirectories)
- `https://github.com/Azure-Samples/azureai-samples` (knowledge / RAG samples)

**Reference docs to snapshot**:
- `https://learn.microsoft.com/en-us/azure/ai-foundry/concepts/knowledge` — knowledge concept
- `https://learn.microsoft.com/en-us/azure/ai-foundry/agents/knowledge-base-create` — creating knowledge bases
- `https://learn.microsoft.com/en-us/azure/ai-foundry/agents/knowledge-source-types` — source types reference

### What to curate

- An example creating a knowledge base over Azure Blob (indexed source)
- An example creating a knowledge base over Azure AI Search
- An example with SharePoint as a remote source (Copilot Retrieval API path)
- An example wiring a knowledge base into an agent's grounding config

### NOTES.md guidance

Annotations should cover:
- Indexed sources vs. remote sources — currency vs. control trade-off
- Hybrid retrieval, semantic reranking, permission filtering
- Citation handling in retrieval responses
- When to use Foundry IQ vs. direct AI Search index queries
- Spaarke's pattern: Foundry IQ knowledge bases for golden documents (playbooks, exemplar contracts, legal research) where curation matters; direct AI Search index for application-code queries; SPE substrate index for default agent grounding on matter documents

---

## Phase 2.7: `work-iq`

Work IQ — the M365 collaboration context layer, exposed as MCP servers.

### Sources

**Primary**:
- Microsoft has limited public samples for Work IQ MCP specifically (preview surface). Curate from:
- `https://github.com/microsoft/copilot-camp` (any Work IQ MCP lab content)
- Microsoft 365 Developer Blog posts on Work IQ MCP (snapshot as markdown)

**Reference docs to snapshot**:
- `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq-mcp` — Work IQ MCP catalog (if available; otherwise capture the announcement post)
- The Microsoft Mechanics video transcript on "Work IQ: Copilot, Data & Agent Skills" — Jeremy Chapman walkthrough

### What to curate

- The Work IQ MCP server URLs and tool definitions catalog (as documentation, since this is a remote service)
- Any example agent manifests that wire Work IQ MCP in
- The "Work IQ Copilot" MCP server's `copilot_chat` tool documentation specifically — this is the omnibus tool that delegates to M365 Copilot

### NOTES.md guidance

Annotations should cover:
- What Work IQ is (real-time work context graph from M365 signals) and what it isn't (not a queryable knowledge base, not a substrate for storing data)
- How Work IQ MCP differs from Foundry IQ knowledge bases (collaboration context vs. curated knowledge)
- Licensing prerequisites (M365 Copilot license per user)
- Preview status and naming evolution (Agent 365 MCP → Work IQ MCP)
- When the Spaarke agent should call Work IQ MCP (for "what did we discuss," "who's on this matter," "what's been happening")
- The composition pattern: Spaarke agent uses Work IQ for collab context AND Foundry IQ for curated knowledge AND Dataverse MCP for records AND SharePoint knowledge source for matter docs — four retrieval surfaces

---

## Phase 2.8: `dataverse-mcp`

Dataverse as MCP server — built-in tools, Business Skills, App MCP.

### Sources

**Primary**:
- `https://github.com/microsoft/PowerApps-Samples` — Power Apps samples (App MCP subdirectories if available)
- `https://github.com/microsoft/Dataverse-Web-API-Samples` — Dataverse Web API patterns (some applicable to MCP server interactions)

**Reference docs to snapshot**:
- `https://learn.microsoft.com/en-us/power-platform/dataverse/mcp-server` — Dataverse MCP server overview
- `https://learn.microsoft.com/en-us/power-platform/dataverse/business-skills` — Business Skills (preview)
- `https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/app-mcp` — App MCP
- `https://learn.microsoft.com/en-us/power-platform/dataverse/mcp-server-custom-tools` — custom MCP tools

### What to curate

- Examples of Dataverse MCP standard tool invocations
- A Business Skill record example (Markdown-with-YAML format)
- An App MCP-enabled model-driven app example with a custom tool definition
- A custom tool with a widget attached (if Microsoft samples include one yet; otherwise note as gap to fill from the `mcp-apps` curation)

### NOTES.md guidance

Annotations should cover:
- The three flavors of Dataverse-as-MCP: built-in MCP server, Business Skills, App MCP
- When each is the right choice
- Business Skills authoring conventions (Markdown body, YAML frontmatter, "use when" / "do not use when" trigger phrases)
- Solution-packaging behavior (skills move through solutions automatically)
- Metering for non-Copilot-Studio consumers
- Spaarke's pattern: existing structured playbook tables stay as reference data; procedural content migrates to Business Skills over time; new agent-callable verbs that don't need imperative code go through App MCP custom tools; imperative document operations stay in the custom Spaarke MCP server

---

## Phase 2.9: `sharepoint-embedded`

SharePoint Embedded — document storage substrate, agent grounding via SharePoint knowledge source, substrate semantic indexing.

### Sources

**Primary**:
- `https://github.com/microsoft/SharePoint-Embedded-Samples` — official SPE samples
- `https://github.com/microsoft/SharePoint-Embedded-VS-Code-Extension` — VS Code extension samples

**Reference docs to snapshot**:
- `https://learn.microsoft.com/en-us/sharepoint/dev/embedded/overview` — SPE overview
- `https://learn.microsoft.com/en-us/sharepoint/dev/embedded/development/declarative-agent/sharepoint-embedded-knowledge-source` — SPE as Foundry knowledge source
- `https://learn.microsoft.com/en-us/sharepoint/dev/embedded/concepts/app-concepts/containers` — container concepts
- `https://learn.microsoft.com/en-us/microsoftsearch/semantic-index-for-copilot` — substrate semantic index

### What to curate

- A container creation and configuration example
- An example of permission management on a container
- An example of the SharePoint knowledge source configuration pointing at an SPE container-type
- The boilerplate SPE TypeScript/React sample for understanding the embedded chat experience

### NOTES.md guidance

Annotations should cover:
- Container type registration — the gateway that makes SPE work in the agent ecosystem
- Permission scopes for SPE — application vs. delegated, when each applies
- The substrate semantic index — what it indexes automatically, what consumers it serves, what it doesn't expose directly
- The Copilot Retrieval API (pay-as-you-go preview) as the way to query the substrate index from application code
- Spaarke's pattern: one container per client (ADR-005), webUrl-based opens for Copilot integration, BFF for upload and operations, Foundry SharePoint knowledge source for agent grounding
- The "Document record opens in Word with Copilot context" pattern (webUrl flow, not download-and-open)

---

## Phase 2.10: `azure-ai-search`

Azure AI Search — for application-code retrieval against custom-indexed content, and for Foundry IQ knowledge bases over AI Search.

### Sources

**Primary**:
- `https://github.com/Azure/azure-search-vector-samples` — vector search samples
- `https://github.com/Azure-Samples/azure-search-openai-demo` — RAG reference architecture (heavily used as a baseline; worth curating even if Spaarke does it differently)
- `https://github.com/Azure-Samples/azure-search-openai-demo-csharp` — C# version (closer to Spaarke's BFF language)

**Reference docs to snapshot**:
- `https://learn.microsoft.com/en-us/azure/search/vector-search-overview` — vector search overview
- `https://learn.microsoft.com/en-us/azure/search/vector-search-integrated-vectorization` — integrated vectorization
- `https://learn.microsoft.com/en-us/azure/search/semantic-search-overview` — semantic search / reranking
- `https://learn.microsoft.com/en-us/azure/search/search-howto-index-sharepoint-online` — SharePoint indexer (not directly applicable to SPE but useful for understanding the pattern)

### What to curate

- An integrated vectorization indexer setup with Azure Blob as source and Azure OpenAI for embeddings
- A hybrid search query (vector + keyword + semantic reranking) in C#
- A skillset with structure-aware chunking via Document Intelligence layout model
- The C# end-to-end RAG demo with permission filtering at query time

### NOTES.md guidance

Annotations should cover:
- Spaarke's AI Search index schema and ingestion pipeline (reference existing `RAG-ARCHITECTURE.md` and `RAG-CONFIGURATION.md`)
- Structure-aware chunking for legal documents — why it matters, how Document Intelligence layout model fits in
- Hybrid retrieval defaults — BM25 + vector + semantic reranking
- Permission filtering at query time vs. coarse filtering at index time
- When to use AI Search directly vs. through Foundry IQ knowledge base
- The parallel-ingestion pattern (file → SPE + AI Search) and why both indexes exist
- ADR-009 cache layer over AI Search results

---

## Phase 2.11: `github-mcp`

GitHub MCP server — for runtime currency and long-tail API research.

### Sources

**Primary**:
- `https://github.com/github/github-mcp-server` — the GitHub MCP server itself

**Reference docs to snapshot**:
- `https://github.com/github/github-mcp-server/blob/main/README.md` — setup and tool catalog
- `https://docs.github.com/en/copilot/customizing-copilot/extending-copilot-chat-with-skillsets/about-skillsets` — GitHub Copilot skillset model (related context)

### What to curate

- The full tool catalog from the GitHub MCP server (as documentation)
- Configuration examples for scoped access (toolset restrictions, org scoping)
- Examples of `search_code`, `search_issues`, and `get_file_contents` invocations

### NOTES.md guidance

Annotations should cover:
- When to reach for GitHub MCP (long-tail API research, currency between refreshes, debugging unfamiliar errors, cross-referencing patterns)
- When NOT to reach for it (anything covered by the curated reference tree should be read first)
- Scoping configuration for Spaarke's trusted orgs (microsoft, Azure-Samples, OfficeDev, modelcontextprotocol, plus selected MVP accounts)
- Toolset restrictions to enable for the project
- Auth setup (PAT vs OAuth)
- Cost discipline — GitHub MCP calls are cheap individually but agents will overuse without explicit triggering rules

---

## Phase 3: Wire knowledge into Claude Code skills

The knowledge base only gets used if the agent loads it for relevant tasks. Wire it in via the `.claude/skills/` mechanism.

### Tasks

1. Verify `.claude/skills/` exists at the Spaarke repo root; create if it doesn't
2. Create or update a skill for each major platform piece:
   - `.claude/skills/mcp-tool-handler/SKILL.md` — references `knowledge/mcp-apps/` and `knowledge/foundry-agent-service/` (for tool-binding patterns)
   - `.claude/skills/declarative-agent/SKILL.md` — references `knowledge/declarative-agents/` and `knowledge/m365-copilot/`
   - `.claude/skills/foundry-agent/SKILL.md` — references `knowledge/foundry-agent-service/`, `knowledge/foundry-iq/`, `knowledge/agent-framework/`
   - `.claude/skills/dataverse-mcp-usage/SKILL.md` — references `knowledge/dataverse-mcp/`
   - `.claude/skills/spe-integration/SKILL.md` — references `knowledge/sharepoint-embedded/`
   - `.claude/skills/widget-design/SKILL.md` — references `knowledge/mcp-apps/`
3. Each SKILL.md should:
   - Have a focused trigger description (when this skill loads)
   - Be 100-200 lines maximum
   - Instruct the agent to read specific files in the relevant `knowledge/` subdirectory before generating code
   - Cross-reference the existing Spaarke ADRs the skill interacts with
4. Update or create `CLAUDE.md` at the repo root to mention the `knowledge/` tree as a primary reference for Microsoft platform questions

### Acceptance

Each skill loads on the right triggers, points at the right knowledge files, doesn't duplicate content. Test by giving Claude Code a task that should trigger each skill and verifying the right knowledge files appear in context.

---

## Phase 4: Senior engineer annotation pass

The curated samples and SOURCE.md files are mechanically gathered. The NOTES.md files are where your team's judgment lives, and they're the highest-leverage artifact in the knowledge base. The Claude Code agent can produce stub NOTES.md files; a senior engineer should review and substantively annotate them.

### Tasks for the senior engineer (not Claude Code)

For each topic folder:

1. Read the curated samples
2. Review and revise the stub NOTES.md to include:
   - What this pattern actually teaches in practice
   - Where Spaarke's existing code follows or modifies this pattern
   - Specific gotchas, preview limitations, or platform constraints encountered
   - Cross-references to relevant ADRs and Spaarke-specific decisions
3. Commit each updated NOTES.md with a clear message ("annotate <topic> with project-specific guidance")

### Acceptance

Every NOTES.md has substantive project-specific commentary, not just stub placeholders.

---

## Phase 5: Verify and test

Confirm the knowledge base is functional by running representative tasks through Claude Code.

### Tasks

1. Run a representative task that should trigger the `mcp-tool-handler` skill (e.g., "draft a spec for a new Spaarke MCP tool that returns document summaries")
2. Verify the agent reads from `knowledge/mcp-apps/NOTES.md` and the curated samples
3. Verify the output reflects Spaarke's patterns from the NOTES.md, not generic MCP patterns
4. Repeat for `declarative-agent`, `foundry-agent`, `dataverse-mcp-usage`, `spe-integration`
5. If the agent fails to pull in the right context, adjust the SKILL.md trigger description and re-test

### Acceptance

Each skill demonstrably influences agent output. The knowledge base is integrated, not just present.

---

## Phase 6: Establish the monthly refresh ritual

Set up the process for keeping the knowledge base current.

### Tasks

1. Add a `REFRESH-PROCEDURE.md` to `knowledge/` documenting:
   - Monthly cadence (first business day of the month)
   - For each topic: re-clone the source repos, diff against curated copy, identify changes, update curated samples and SOURCE.md
   - For platform announcements between refreshes: capture in REFRESH-LOG.md immediately as "interim update," then formalize at next refresh
   - Sign-off: one engineer owns the refresh, ~2-4 hours total
2. Create a calendar reminder or recurring task for the refresh
3. After the first refresh (next month), verify the procedure works and adjust if needed

### Acceptance

Procedure is documented; owner assigned; first refresh scheduled.

---

## Total time estimate

- Phase 0 (verify): 30 minutes
- Phase 1 (skeleton): 30 minutes
- Phase 2 (populate, 11 topics × ~30 min each): 5-6 hours
- Phase 3 (wire into skills): 2-3 hours
- Phase 4 (senior annotation, async): 1-2 days of senior engineer time spread over a week
- Phase 5 (verify and test): 2 hours
- Phase 6 (refresh ritual): 30 minutes

**Total Claude Code execution time**: roughly one full day for Phases 0-3 and 5-6.
**Total human review time**: 1-2 days of senior engineer time for Phase 4.

---

## Important constraints for Claude Code execution

A few rules to apply throughout:

1. **One topic at a time, fully.** Complete `SOURCE.md`, curated files, and stub `NOTES.md` for one topic before moving to the next. Don't half-populate ten topics.

2. **Preserve provenance.** Every curated file should be traceable to its origin. The `SOURCE.md` is what makes the knowledge base trustworthy — without it, six months from now nobody knows where these files came from or whether they're current.

3. **Stub `NOTES.md` should be honest about being stubs.** A NOTES.md that says "TODO: senior engineer to annotate" is more useful than one that pretends to have insight it doesn't. Use the structure suggested in each phase as a template; mark unfilled sections clearly.

4. **Don't bloat curated samples.** For each topic, pull the minimum useful subset — usually 1-3 complete examples per pattern. Don't clone whole repos and dump them in. The signal-to-noise ratio is what makes the knowledge base usable.

5. **Stop and ask if a source doesn't exist.** Microsoft moves fast and some of the URLs and repos listed above may have changed by execution time. If a repo URL returns 404, search for the renamed version using `gh` or `web_search`, find the canonical source, and update the SOURCE.md to reflect what was actually used. Don't silently skip topics.

6. **Don't add the knowledge base to `.gitignore`.** It belongs in the repo, tracked, reviewed in PRs like any other artifact.

7. **Run `git status` after each phase.** Confirm the work landed and is ready to commit. Commit at logical breakpoints (after Phase 2 for each topic, after Phase 3, etc.) with descriptive messages.

---

## What success looks like

After completion, an engineer working on a Spaarke feature involving any of the curated Microsoft platform pieces can:

1. Open Claude Code with a task description
2. The relevant skill auto-loads
3. The skill reads the curated samples and NOTES.md
4. The agent produces code that follows Spaarke's established patterns against the current Microsoft platform
5. The output is reviewable against the same knowledge base the reviewer can read

Six months from now, the refresh ritual has kept the knowledge current, NOTES.md files have accumulated real-world annotations from actual project work, and the agent's effective Microsoft platform knowledge is months ahead of where its training data ended. That's the structural improvement this work produces.
