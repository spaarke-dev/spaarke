# Agent Framework Knowledge Base — Build Specification (R1)

> **Project**: agent-framework-knowledge-r1
> **Type**: Knowledge curation + skill activation (no source-code changes to Spaarke runtime; reads code to ground commentary)
> **Created**: 2026-06-03
> **Owner**: Ralph Schroeder
> **Quality bar**: Match `knowledge/fluent-ui-v9/` parity (~14 reference docs, ~8 curated samples, substantive NOTES.md, INDEX.md, skill + pattern files)

---

## 1. Goal

Bring `knowledge/agent-framework/` to **full Fluent-V9 parity** so Claude Code can author and modify **Microsoft Agent Framework**-based code in Spaarke's BFF (`src/server/api/Sprk.Bff.Api/Services/Ai/`) with the same rigor it has for Fluent UI v9 component work.

Microsoft Agent Framework was released early 2026 as the **successor to Semantic Kernel and AutoGen**, combining AutoGen's simple agent abstractions with Semantic Kernel's enterprise features (sessions, type safety, middleware, telemetry) and adding graph-based workflows for explicit multi-agent orchestration. Spaarke **is already actively using it in production** — `SprkChatAgent.cs` wraps `IChatClient`, registers tools via `AIFunction` / `AIFunctionFactory`, and pipes through a real middleware stack (`AgentTelemetryMiddleware`, `AgentContentSafetyMiddleware`, `AgentCostControlMiddleware`).

The current `knowledge/agent-framework/` is a thin starter (4 curated samples, 3 reference docs, stub NOTES.md awaiting senior-engineer pass). This project completes that pass.

## 2. Why this matters now

1. **Spaarke is already in flight.** Code under `Services/Ai/Chat/` uses `Microsoft.Extensions.AI` types (`IChatClient`, `AIFunction`, `ChatMessage`, `ChatResponseUpdate`) — the foundation Agent Framework builds on. Without curated context, Claude will reach for AutoGen-style or Semantic-Kernel-style idioms when modifying this code, producing valid-looking but wrong patterns.
2. **The platform is new.** Released early 2026; Claude's training cutoff trails. Community content is sparse and varies in quality. We need a vetted snapshot.
3. **Spaarke-specific decisions need to be encoded.** Where Agent Framework agents fit vs. Foundry Agent Service vs. Spaarke's JPS playbook engine vs. the AI Tool Framework (`IAiToolHandler` per ADR-013) is non-obvious. The NOTES.md must answer this so engineers don't end up with parallel tool registries.
4. **Refined ADR-013 (2026-05-20)** requires CRUD→AI access to go through `Services/Ai/PublicContracts/` facades — not direct injection of `IOpenAiClient`. Agent Framework's role on both sides of that boundary must be clear.

## 3. Scope and non-goals

### In scope

- **Provenance refresh** — re-clone `microsoft/agent-framework` at HEAD, capture new commit SHA, diff against current 4 curated samples
- **Sample curation** — add ~6 more .NET samples covering Spaarke patterns: Azure OpenAI / `ChatCompletionsAgent`, function-tool registration, middleware composition, MCP client tools, structured outputs, session state
- **Reference docs** — snapshot ~12 additional Microsoft Learn pages: chat-client, tools (function + hosted MCP), providers, middleware, observability, sessions/state, context providers, structured outputs, MCP clients, migration-from-SK, migration-from-AutoGen, hosted-agents/deployment
- **Community capture** — capture high-quality devblog / MVP posts (limited expected — release is recent)
- **NOTES.md rewrite** — substantive Spaarke-specific commentary grounded in `SprkChatAgent.cs`, middleware pipeline, ADR-013, the `PublicContracts/` facade, the `ChatMessage` namespace ambiguity already coded as `using AiChatMessage = ...`, the `UseFunctionInvocation` vs raw-client split for compound intent detection, the OTel pipeline integration, and when to reach for Agent Framework agents vs. Workflows vs. Foundry Agent Service
- **docs/INDEX.md** — verbose loader index matching `fluent-ui-v9/docs/INDEX.md`
- **`.claude/patterns/ai/agent-framework-*.md`** — 3-5 25-line pointer files (component-authoring, middleware-pipeline, tool-registration, observability-wiring, agent-vs-workflow-decision)
- **`.claude/skills/agent-framework-component/SKILL.md`** — modeled on `fluent-v9-component`; loads patterns + NOTES.md based on intent
- **Verification** — ensure the skill influences agent output on a realistic Spaarke prompt
- **Discoverability wire-up** — update root `CLAUDE.md` Pointers table, `.claude/skills/INDEX.md`, `knowledge/REFRESH-LOG.md`

### Out of scope

- **Python samples.** Spaarke's BFF is .NET 8. Mirror the existing `dotnet/`-only curation rule.
- **Modifying Spaarke runtime code.** This project reads `src/server/api/Sprk.Bff.Api/Services/Ai/` to ground commentary; it does not edit it. Any production refactor (e.g., extracting middleware to a shared package) is a separate project.
- **Workflows-as-graphs deep dive.** Curated samples cover handoff + streaming workflows because they exist upstream. NOTES.md will explicitly state that Spaarke's multi-step AI orchestration is JPS-driven (`AnalysisOrchestrationService`, `PlaybookOrchestrationService`) and Agent Framework Workflows is currently **not** the primary multi-step surface — that decision is out of scope to revisit here.
- **Foundry Agent Service overlap.** The existing `knowledge/foundry-agent-service/` topic covers durable / HITL / A2A. The agent-vs-Foundry decision is captured in NOTES.md but Foundry Agent Service curation is not re-opened.
- **JPS playbook engine documentation.** Spaarke-internal; documented separately under `docs/architecture/AI-ARCHITECTURE.md` and JPS skills.

## 4. Deliverables

| Path | Type | Purpose |
|---|---|---|
| `knowledge/agent-framework/SOURCE.md` | Updated | New commit SHA, expanded sample table, expanded docs table |
| `knowledge/agent-framework/dotnet/samples/**` | ~6 added | Spaarke-aligned curated .NET samples |
| `knowledge/agent-framework/docs/*.md` | ~12 added | Microsoft Learn snapshots with YAML provenance |
| `knowledge/agent-framework/docs/community/*.md` | 0-3 added | Devblog / MVP captures if quality content exists |
| `knowledge/agent-framework/docs/INDEX.md` | Added | Verbose loader index |
| `knowledge/agent-framework/NOTES.md` | Rewritten | Substantive Spaarke commentary (banner removed) |
| `.claude/patterns/ai/agent-framework-component-authoring.md` | Added | Pointer pattern (~25 lines) |
| `.claude/patterns/ai/agent-framework-middleware-pipeline.md` | Added | Pointer pattern (~25 lines) |
| `.claude/patterns/ai/agent-framework-tool-registration.md` | Added | Pointer pattern (~25 lines) |
| `.claude/patterns/ai/agent-framework-observability.md` | Added | Pointer pattern (~25 lines) |
| `.claude/patterns/ai/agent-framework-agent-vs-workflow-decision.md` | Added | Decision-tree pattern (~25 lines) |
| `.claude/patterns/ai/INDEX.md` | Added or updated | Index of patterns |
| `.claude/skills/agent-framework-component/SKILL.md` | Added | Skill that loads the above on Agent Framework tasks |
| `.claude/skills/INDEX.md` | Updated | Add skill to index |
| `CLAUDE.md` (root) | Updated | Pointers table entry |
| `knowledge/REFRESH-LOG.md` | Appended | Project completion entry |

**Size budget**: total curated samples ≤ 300 KB (mirror the upstream Fluent V9 budget per topic). Docs snapshots are text and not budgeted; community captures ≤ 50 KB each.

## 5. Execution approach

- **POML-decomposed.** Unlike `coding-knowledge-base-setup-r1` (which had 11 uniform topics and opted out of POML), this project has 13 discrete tasks of different shapes and uses `task-execute` for each.
- **Parallel-safe groups** documented in each POML's `<metadata>` block so future sessions can fan out (e.g., the three reference-doc snapshot tasks have no inter-dependency).
- **Sub-agent write boundary respected.** Sample curation and Microsoft Learn fetches can run in sub-agents (write to `knowledge/`). Pattern files, the SKILL.md, and INDEX updates run in main session only (write to `.claude/`).
- **Rigor level**: most tasks are STANDARD (curation + documentation, no `.cs`/`.ts` modification). The skill-creation task is STANDARD as well. Final verification task is FULL because it cross-validates against Spaarke runtime code.

## 6. Phases and tasks

| Phase | Tasks | Purpose |
|---|---|---|
| **1. Curation foundation** | 001, 002 | Refresh provenance + curate Spaarke-aligned .NET samples |
| **2. Reference docs** | 003, 004, 005 | Snapshot Microsoft Learn pages (parallel-safe group B) |
| **3. Project commentary** | 006, 007 | Community/MVP capture; rewrite NOTES.md from Spaarke code |
| **4. Discoverability** | 008 | Write docs/INDEX.md |
| **5. Skill activation** | 009, 010 | Patterns + SKILL.md (main session only — `.claude/` write boundary) |
| **6. Verification + wrap-up** | 011, 012, 013 | Skill output verification + root pointer wiring + sign-off |

Detailed task definitions live under `tasks/*.poml`. The index is `TASK-INDEX.md`.

## 7. Constraints

1. **No invented URLs or sample content.** Every reference doc has YAML provenance (source URL + fetched date). Every curated sample preserves the upstream path structure under `dotnet/samples/`.
2. **Provenance preserved.** SOURCE.md updated with new commit SHA before adding new curated files. Any URL substitution (404 → canonical replacement) is recorded in the doc's frontmatter and SOURCE.md GAPs.
3. **No Spaarke runtime edits.** Read `src/server/api/Sprk.Bff.Api/Services/Ai/` to ground NOTES.md commentary; do not modify.
4. **`.claude/` writes from main session only.** Sub-agents can write to `knowledge/` but not `.claude/` (per root CLAUDE.md §3).
5. **Honest gaps logged.** If a URL is 404 or content is preview-only, log a `GAPs` entry in SOURCE.md and continue — do not silently skip and do not invent placeholder content.
6. **Stub banner removed only when honest.** The `> ⚠️ STUB` banner on NOTES.md is removed only when both §1 and §2 of the file have substantive content backed by reading Spaarke code (per the existing NOTES.md scaffold rules).

## 8. Acceptance criteria (project-level)

- [ ] `knowledge/agent-framework/SOURCE.md` reflects current upstream HEAD SHA + expanded sample/doc tables
- [ ] At least 6 new .NET samples curated under `dotnet/samples/`, each chosen for a Spaarke pattern
- [ ] At least 10 new reference docs under `docs/` (or `docs/community/`), each with YAML provenance
- [ ] `knowledge/agent-framework/NOTES.md` has the STUB banner removed and both §1 + §2 are populated with substance grounded in `src/server/api/Sprk.Bff.Api/Services/Ai/` code
- [ ] `knowledge/agent-framework/docs/INDEX.md` matches the format of `knowledge/fluent-ui-v9/docs/INDEX.md`
- [ ] `.claude/skills/agent-framework-component/SKILL.md` exists, follows the `fluent-v9-component` template
- [ ] `.claude/patterns/ai/agent-framework-*.md` — at least 5 pattern files exist, each ≤ 30 lines
- [ ] `.claude/skills/INDEX.md` and root `CLAUDE.md` Pointers table list the new skill
- [ ] Verification prompt run (Phase 6 task 011) demonstrates the skill changes agent output on a realistic prompt
- [ ] `knowledge/REFRESH-LOG.md` has a completion entry

## 9. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Microsoft Learn URLs 404 / move (already seen on `/concepts/agents`) | High | Log GAP + find canonical replacement; do not silently skip |
| Upstream sample churn between refresh and project completion | Medium | Pin SHA at task 001; do not re-pull mid-project unless logged |
| Spaarke code shape changes during project execution | Low | Re-verify file paths in NOTES.md against current code at task 007; task 011 acts as final guardrail |
| Skill triggers too aggressively / under-triggers | Medium | Phase 6 verification step (task 011) runs realistic prompts and tunes `appliesTo` / triggers if needed |
| Community content quality is thin (release is new) | High | Accept low community count honestly; document in SOURCE.md GAPs; do not pad with low-quality posts |
| Token / context cost on long task POMLs | Low | Each task targets ≤ 9 steps so `task-execute` protocol fits in one context window |

## 10. References

- **Quality bar template**: [`knowledge/fluent-ui-v9/`](../../knowledge/fluent-ui-v9/) + [`.claude/skills/fluent-v9-component/SKILL.md`](../../.claude/skills/fluent-v9-component/SKILL.md)
- **Existing starter**: [`knowledge/agent-framework/`](../../knowledge/agent-framework/) (4 samples + 3 docs + stub NOTES.md)
- **Spaarke AI code** (NOTES.md grounding): [`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs), [`Middleware/AgentTelemetryMiddleware.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentTelemetryMiddleware.cs), [`AgentContentSafetyMiddleware.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentContentSafetyMiddleware.cs), [`AgentCostControlMiddleware.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentCostControlMiddleware.cs)
- **Architectural anchor**: [`.claude/adr/ADR-013-ai-architecture.md`](../../.claude/adr/ADR-013-ai-architecture.md), [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md)
- **Refresh procedure** (project follows this on completion): [`knowledge/REFRESH-PROCEDURE.md`](../../knowledge/REFRESH-PROCEDURE.md)
- **Upstream**: [`microsoft/agent-framework`](https://github.com/microsoft/agent-framework), [`learn.microsoft.com/en-us/agent-framework/`](https://learn.microsoft.com/en-us/agent-framework/)
