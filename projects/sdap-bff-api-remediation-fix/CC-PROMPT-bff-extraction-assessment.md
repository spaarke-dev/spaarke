# Task: BFF AI Extraction Assessment

## Goal

Analyze `Sprk.Bff.Api` and determine whether the AI/Insights subsystems have grown enough to justify extracting them into a separate service (`Sprk.Insights.Api`), or whether the BFF is still within a healthy capacity envelope.

This is a **read-only code analysis task**. Do not modify any code. Produce a single markdown report with concrete numbers, file paths, and a recommendation grounded in what you actually find — not in what this prompt expects you to find.

## Scope of analysis

**In scope**:
- `src/server/api/Sprk.Bff.Api/` (full tree)
- `src/server/api/Sprk.Bff.Api.csproj`
- `src/server/api/Sprk.Bff.Api/Program.cs` and any `Add*` extension methods used during startup
- `tests/Sprk.Bff.Api.Tests/` (or equivalent test project)
- Any shared library projects referenced by `Sprk.Bff.Api` (e.g., `Sprk.Common`, `Sprk.Auth`) — list them, but you don't need to deep-analyze them
- Git history of `src/server/api/Sprk.Bff.Api/Services/` over the last 180 days

**Out of scope**:
- Other Spaarke projects/services (PCF, Power Apps, infra outside the BFF)
- The Function Apps (sync pipeline) — already separate from the BFF
- Code modifications of any kind

## What "AI subsystem" means in this codebase

For the purposes of this analysis, treat the following as AI subsystem code:
- `Services/Insights/**` — Insights Engine resolver, agent, tools, fact resolver
- `Services/Ai/**` — general AI infrastructure (IChatClient wiring, tool framework, embedding helpers)
- `Services/AiToolAgent/**` — the in-app AI tool agent
- `Services/Playbooks/**` — playbook execution engine and node executors
- Any class with `Ai`, `Insights`, `Embedding`, `Playbook`, `Inference`, `Observation`, `Rag`, or `Agent` in its name, even if outside the above folders

Treat everything else (Matter, Document, Invoice, Party, Person, Firm endpoints; auth handlers; Dataverse clients; SPE/Graph clients; generic infrastructure) as **CRUD/infrastructure**.

If a class is genuinely shared (auth, caching, telemetry, http client factory), categorize it as **shared**.

## Required analysis

### 1. Size and surface composition

- Total lines of code in `Sprk.Bff.Api` (excluding tests, generated code, and JSON resource files).
- Lines of code broken down by top-level folder under `Services/`, with a column indicating AI / CRUD / shared / other.
- File counts and class counts (top-level types) per top-level service folder.
- Full inventory of Minimal API endpoints registered in `Program.cs` (and any `Map*Endpoints` extension methods). For each endpoint: route, HTTP method, the handler class/method, and AI / CRUD / shared categorization.
- DI registration count: how many services registered in `Program.cs` (and `Add*` extensions), broken down by AI / CRUD / shared.

### 2. Dependency cardinality and coupling

- For each class in `Services/Insights/`, `Services/Ai/`, `Services/AiToolAgent/`, and `Services/Playbooks/`: list the classes outside those folders it depends on (constructor injection, static references, direct instantiation). Group dependencies as: shared infrastructure / CRUD code / other AI code.
- For each class outside those folders: does it depend on anything inside them? List the cross-boundary inbound dependencies (CRUD → AI). These are the dependencies that would have to be broken or HTTP-ified during extraction.
- List the project references in `Sprk.Bff.Api.csproj`. For each, note whether it's used by AI code, CRUD code, or both.
- Inventory of shared infrastructure components that AI code currently uses from the BFF: `IDataverseClient`, `IDistributedCache`, auth handlers, `IHttpClientFactory` configurations, telemetry helpers, `PendingPlanManager`, `RequestCache`, etc. For each, note: would it need to be duplicated, shared via library, or HTTP-accessed if AI code moved to a separate service?

### 3. Operational characteristics

- Scan for long-running request patterns: code paths involving `await` chains over LLM SDK calls (`IChatClient.GetResponseAsync`, `IChatClient.GetStreamingResponseAsync`, Azure OpenAI calls), multi-tool agent loops (`UseFunctionInvocation`), streaming response endpoints (Server-Sent Events). List the endpoint surface (route + handler) where these occur.
- Named HttpClient inventory: every named `HttpClient` configured via `IHttpClientFactory`, what external service it talks to, what Polly policies are attached, and AI/CRUD categorization.
- `IHostedService` and background service inventory: every implementation registered in the BFF. For each: what it does, AI/CRUD categorization, and whether it holds long-running state.
- Memory/connection-pool hotspots: classes that retain in-memory caches scaling with corpus size (embedding cache, graph cache, playbook registry, agent session state) rather than active-user count. List them with rough size estimates if discoverable from configuration.

### 4. Deployment and release-cadence signals

- Git log analysis of `Services/Insights/`, `Services/Ai/`, `Services/AiToolAgent/`, `Services/Playbooks/` over the last 30, 90, and 180 days. Number of commits, distinct authors, and approximate lines changed per window.
- Same for the largest non-AI service folders (e.g., `Services/Documents/`, `Services/Matters/`) for comparison.
- PR-scope analysis: of the most recent 30 merged PRs touching `Sprk.Bff.Api`, classify each as "AI only", "CRUD only", or "mixed". (Use commit-level file paths if PR metadata isn't easily accessible.)
- Feature flag inventory: any flags in configuration that gate AI features independently of CRUD features.

### 5. Test surface

- Test project structure under `tests/Sprk.Bff.Api.Tests/` (or wherever BFF tests live). Test counts grouped by AI / CRUD / shared.
- Integration test patterns: for AI integration tests, what external services do they depend on (Azure OpenAI? AI Search? Cosmos? Service Bus?) and how are those configured (mocks, fakes, real)?
- Shared test fixtures that would need to move or be duplicated on extraction.

### 6. MCP server prospect (if it exists in the codebase yet)

The MCP server is a Phase 1 design deliverable per the r2 architecture document. If any scaffolding for it exists yet:

- File paths.
- What it currently does (stub vs implemented).
- Its dependencies on Engine code.
- Its dependencies on CRUD code (should be zero or near-zero if the design is right).

If no MCP server code exists yet, note that explicitly.

## Trigger thresholds to evaluate against

After completing the analysis, evaluate the codebase against each of these triggers and answer **yes / no / partial** with the evidence:

1. **AI code is >40% of `Sprk.Bff.Api` by line count or class count.** Yes/no, with the numbers.
2. **Cross-subsystem dependency profile is favorable for extraction** — AI code depends on a small, well-defined set of shared services; few or no CRUD classes depend on AI classes. Yes/no, with the dependency counts.
3. **Background services or long-running operations in the BFF are predominantly AI-related.** Yes/no, with the inventory.
4. **>70% of recent BFF PRs touch only AI or only CRUD, rarely both.** Yes/no, with the PR classification numbers.
5. **MCP server (if it exists) has zero or minimal CRUD-code dependencies.** Yes/no/N/A, with file-level evidence.

## Deliverable

A single markdown report at `docs/assessments/bff-ai-extraction-assessment-{YYYY-MM-DD}.md` with the following structure:

```
# BFF AI Extraction Assessment

## Summary
- One-paragraph executive summary of findings
- Recommendation: [Full extraction now | MCP-only extraction now | Defer with re-assessment in N months]

## Codebase composition
[results from analysis section 1]

## Dependency coupling
[results from analysis section 2]

## Operational characteristics
[results from analysis section 3]

## Release cadence
[results from analysis section 4]

## Test surface
[results from analysis section 5]

## MCP server status
[results from analysis section 6]

## Trigger evaluation
[results against the five triggers, with yes/no/partial and evidence]

## Recommendation rationale
[2-4 paragraphs explaining the recommendation, anchored in the evidence above]

## Risks and caveats
[any analysis limitations, missing data, or judgments worth flagging]
```

## Output expectations

- **Be concrete**: every claim should have a file path, line count, or measurement behind it.
- **Be quantitative where possible**: "AI code is 38% of LOC (12,400 of 32,600 lines)" not "AI code is a substantial portion."
- **Be honest about uncertainty**: if a categorization is ambiguous (is `PendingPlanManager` AI or shared?), say so and explain the judgment call.
- **Don't shape findings to a predetermined conclusion.** The recommendation should follow from the evidence. If the codebase is healthy and the BFF is well within capacity, the right answer is "defer."
- **Don't propose code changes in this report.** This is an assessment, not a refactor plan. If extraction is recommended, the implementation plan is a separate task.

## What this assessment is NOT

- It is not a refactor.
- It is not a request to *do* the extraction.
- It is not a code-quality review (no judgments on test coverage gaps, code smells, or refactoring opportunities unless they directly bear on the extraction question).
- It is not an architecture proposal beyond the extraction recommendation.

Report only what you find; recommend only what the findings support.
