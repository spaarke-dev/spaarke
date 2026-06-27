# 02 — Docs Survey (Canonical-Truth Loop, Step 2)

> **Authored**: 2026-06-26
> **Lane**: DOCS ONLY. Companion Wave A agent is doing code archaeology in parallel — this survey identifies what docs *claim*, not what code does.
> **Scope**: AI / playbook / JPS / chat-routing / membership documentation across `docs/`, `.claude/`, and `projects/*` for R4-adjacent surfaces.
> **Output goal**: feed step 3 (canonical-truth writer) with a duplication-pruned, gap-explicit, naming-convention-compliant target structure.

---

## 1. Doc Inventory

Survey covered **27 docs** in full + skim. Length is approximate (lines). "Skim depth" indicates whether the doc was read end-to-end or skimmed via TOC + spot reads.

| Path | Title (H1) | Last reviewed | Topics covered | Length | Skim depth | Notes |
|---|---|---|---|---|---|---|
| `docs/architecture/AI-ARCHITECTURE.md` | Spaarke AI Architecture | 2026-05-17 (audit add 2026-06-05) | Tier 1–4 platform, scope library, tool handler framework, scope resolution, RAG, PublicContracts facade, Cosmos persistence, Safety pipeline, Capability Router | ~510 | FULL | The 800-lb gorilla. Conflates platform overview + JPS internals + R2 chat additions + facade boundary. Cohesive but heavy. |
| `docs/architecture/playbook-architecture.md` | Playbook Architecture | 2026-04-05 (R3 update 2026-06-22) | Playbook entity, canvas-type/NodeType/ActionType triplet, 9-canvas-type/4-NodeType/18-ActionType mapping, builder, scheduler, executors, R3 LookupUserMembership, R3 helpers, pitfalls G1–G11 | ~685 | FULL | The other 800-lb gorilla. Parent-child relationship with AI-ARCHITECTURE.md is asserted but content overlap is ~20%. |
| `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` | (skimmed) | through R13 | end-to-end pipeline cold-load → widget; storage; BFF surface; 6 system layouts | unknown | SKIM-TOC | Workspace-shell oriented; tangential to playbook/JPS truth. Not load-bearing for R4 canonical-truth. |
| `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` | (skimmed) | through R13 | three surfaces, two-wrapper architecture, dual-use Pattern D | unknown | SKIM-TOC | Frontend/wrapper-side; tangential. |
| `docs/architecture/SPAARKEAI-COMPONENT-MODEL.md` | (skimmed) | through R13 | `@spaarke/*` lib inventory | unknown | SKIM-TOC | Tangential. |
| `docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md` | (skimmed) | through R13 | coupling gaps + remediation backlog | unknown | SKIM-TOC | Tangential. |
| `docs/architecture/AI-CHAT-STRATEGY-M365-COPILOT-VS-SPRKCHAT.md` | (not read; name only) | unknown | M365 Copilot vs SprkChat strategy | unknown | NAME-ONLY | Tangential. |
| `docs/architecture/ai-document-summary-architecture.md` | (skim) | unknown | document summary feature | unknown | NAME-ONLY | Feature-specific. |
| `docs/architecture/ai-semantic-relationship-graph.md` | (name only) | unknown | semantic graph | unknown | NAME-ONLY | R5+ feature. |
| `docs/architecture/auth-AI-azure-resources.md` | (name only) | unknown | endpoints, models, CLI | unknown | NAME-ONLY | Cross-referenced; not surveyed. |
| `docs/guides/JPS-AUTHORING-GUIDE.md` | JPS Authoring Guide v3.0 | 2026-04-05 | JPS schema, sections, $ref, $choices, override merge, structured output, examples, deployment, troubleshooting, Claude-Code-design recipe, scope catalog | ~1390 | FULL | LOAD-BEARING. The only end-to-end JPS authoring reference. Internally consistent; ends with a "Designing playbooks with Claude Code" section that overlaps `jps-playbook-design` skill. |
| `docs/guides/PLAYBOOK-AUTHOR-GUIDE.md` | Playbook Author Guide | 2026-06-22 (R3 update) | maker-facing recipe, LookupUserMembership node, joinIds/default helpers, builder UI safety affordances, anti-patterns, smoke-test | ~575 | FULL | Maker-facing companion to playbook-architecture. R3-current. Covers 1 use case (per-user notifications) deeply; other shapes lightly. |
| `docs/guides/AI-DEPLOYMENT-GUIDE.md` | AI Document Intelligence Deployment Guide v3.0 | 2026-05-17 (Auth v2 add 2026-05-20) | Azure infra + Dataverse + PCF + Custom Pages + Form integration + RAG + Email-to-doc + RAG indexing pipeline | ~1000+ | SKIM-TOC | Operator-facing. NOT primarily about playbook/JPS deployment — heavy infra orientation. Mentions `Seed-JpsActions.ps1` and `Deploy-Playbook.ps1` only in passing. |
| `docs/guides/AI-MONITORING-DASHBOARD.md` | AI Monitoring Dashboard Guide | 2026-04-05 | Azure Dashboard panels, alert rules, dashboard access | ~80 (read) | FULL | Operator-facing observability. No overlap with JPS/playbook docs. |
| `docs/guides/SPAARKE-AI-STRATEGY-AND-ROADMAP.md` | Spaarke AI Strategy & Roadmap v1.0 | 2026-02-21 | strategic positioning, "playbooks are the product", roadmap | ~400+ | SKIM-HEAD | Business/exec-facing. Stale by 4+ months but still strategically aligned. |
| `docs/guides/AI-EMBEDDING-STRATEGY.md` | (name only) | unknown | embedding strategy | unknown | NAME-ONLY | Tangential to playbook surface. |
| `docs/guides/AI-MODEL-SELECTION-GUIDE.md` | (name only) | unknown | model selection | unknown | NAME-ONLY | Overlaps JPS-AUTHORING §14 + jps-playbook-design Step 5. |
| `docs/guides/WORKSPACE-AI-PREFILL-GUIDE.md` | (name only) | unknown | workspace AI prefill | unknown | NAME-ONLY | Feature-specific. |
| `docs/guides/ai-assistant-theming.md` | (name only) | unknown | theming | unknown | NAME-ONLY | UI. |
| `docs/guides/ai-document-summary.md` | (name only) | unknown | feature guide | unknown | NAME-ONLY | Feature-specific. |
| `docs/guides/ai-troubleshooting.md` | (name only) | unknown | troubleshooting | unknown | NAME-ONLY | Likely thin; not surveyed. |
| `docs/guides/spaarkeai-launch-points.md` | SpaarkeAi Launch Points | 2026-05-16 | URL parameter contract, 4 launch points | ~250+ | SKIM-HEAD | Workspace-shell oriented; tangential. |
| `.claude/adr/ADR-013-ai-architecture.md` | ADR-013 AI Architecture (Concise) | 2026-05-20 | extraction policy, 4-criteria exception, ChatHostContext, RagSearchOptions, facade rule | ~145 | FULL | Concise; canonical. Refined 2026-05-20 — softened "no separate AI microservice" to 4-criteria exception. |
| `.claude/adr/ADR-037-multinode-output-composition.md` | ADR-037 Multi-Node Output Composition (Concise) | 2026-06-25 | DeliverComposite node, section-keyed SSE events | ~110 | FULL | New (2026-06-25). Workspace-specific. R4 spec cites it as applicable; design 010 explains why R4 does NOT use it. |
| `.claude/skills/jps-action-create/SKILL.md` | jps-action-create | 2026-05-17 | 7-step JPS creation, examples directory, deployment hook | ~300 | FULL | Cites JPS-AUTHORING-GUIDE; uses `.claude/skills/jps-action-create/examples/` as canonical examples. |
| `.claude/skills/jps-playbook-design/SKILL.md` | jps-playbook-design | 2026-05-17 | 13-step orchestrator, Dataverse cross-check, Deploy-Playbook.ps1 | ~540 | FULL | Cites JPS-AUTHORING + playbook-architecture; scope-model-index.json is load-bearing. |
| `.claude/skills/jps-playbook-audit/SKILL.md` | jps-playbook-audit | 2026-05-16 | catalog vs deployed audit | ~250+ | SKIM-HEAD | Cited by R4 W0.4. Low-traffic skill (3 inbound refs). |
| `.claude/skills/jps-validate/SKILL.md` | jps-validate | 2026-05-17 | 30+ validation checks, render test | ~250+ | SKIM-HEAD | Cited by jps-action-create Step 4 + jps-playbook-design Step 8. |
| `.claude/skills/jps-scope-refresh/SKILL.md` | jps-scope-refresh | 2026-05-16 | catalog regeneration | ~150+ | SKIM-HEAD | Plumbing. |
| `.claude/patterns/ai/INDEX.md` | AI Patterns Index | 2026-04-05 | 7-pattern index | ~30 | FULL | Lightweight pointer. |
| `.claude/patterns/ai/node-executor-authoring.md` | Node-Executor Authoring | 2026-06-21 | Singleton+Scoped pattern, canvas↔server drift checklist | ~40 | FULL | R3-current. Cited by R4 CLAUDE.md. |
| `.claude/constraints/ai.md` | AI/ML Constraints | 2026-05-17 | MUST / MUST NOT rules from ADR-013/014/015/016 | ~150 | FULL | Constraint-only. |
| `.claude/constraints/bff-extensions.md` | BFF Extensions Governance | 2026-05-20 | placement decision, facade rule, package adds, asymmetric-registration §F.1/F.2/F.3 | ~600+ | SKIM-HEAD | LOAD-BEARING for every BFF-touching project including R4. |
| `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` | Data Access Decision Criteria | 2026-05-26 | Xrm.WebApi vs BFF 7-criterion decision frame | ~700+ | SKIM-HEAD | Cross-cutting; cited by R4 CLAUDE.md. |
| `projects/spaarke-daily-update-service-r4/CLAUDE.md` | R4 — AI Context | 2026-06-25 | project-tier context | ~280 | FULL | R4-current; cites entity-architecture binding. |
| `projects/spaarke-daily-update-service-r4/spec.md` | R4 — AI Implementation Specification | 2026-06-25 | 20 FRs, 6 NFRs, owner clarifications | ~380 | FULL | The R4 contract. |
| `projects/spaarke-daily-update-service-r4/notes/decisions/030-dispatch-path.md` | 030 — /narrate Dispatch Path | 2026-06-26 | Path A.5 + IConsumerRoutingService + IInvokePlaybookAi survey | ~275 | FULL | Captures the chat-routing → daily-briefing bridging decision. |
| `projects/spaarke-daily-update-service-r4/notes/design/010-daily-briefing-narrate-node-graph.md` | 010 — DAILY-BRIEFING-NARRATE Node Graph | 2026-06-25 | 6-node graph + payload contract + ADR-037 deferral + allow-list logic | ~225 | FULL | Implementation-level design. |
| `projects/spaarke-daily-update-service/CLAUDE.md` | R1 — AI Context | 2026-03-30 | R1 technical constraints (notification entity, scheduler, opt-out model) | ~145 | FULL | R1-base; predates JPS-deployment-as-data discipline. |
| `projects/spaarke-ai-platform-chat-routing-redesign-r1/architecture/stateful-chat-architecture.md` | Stateful Chat Architecture & Component Model | 2026-06-21 | 6-tier memory, P1–P7 principles, Insights reuse boundary | ~80+ (read), ~1000+ total | SKIM-HEAD | The chat-routing project's load-bearing architecture doc. ConsumerRoutingService precedent lives there. |
| `projects/spaarke-ai-platform-chat-routing-redesign-r1/CLAUDE.md` | R1 chat-routing context | 2026-06-21 | ADR set + architecture binding + decisions | ~250 | FULL (sysremind) | Canonical for chat-routing project; cited by R4 dispatch decision. |

**Totals**: 27 docs surveyed in full or with substantive skim; 13 more flagged by name only (peripheral to canonical-truth).

---

## 2. Topic Coverage Matrix

Rows = canonical-truth topics R4 cares about. Columns = docs (abbreviated). Cell values: ✓ = complete, ◐ = partial, ⚠ = stale, ✗ = absent, ‡ = contradicts another doc.

| Topic | AI-ARCH | playbook-arch | JPS-AUTH | PLAYBOOK-AUTHOR | AI-DEPLOY | jps-action-create | jps-playbook-design | ADR-013 | ADR-037 | ai.md | bff-ext | R4 CLAUDE | R4 spec | 030-dispatch | 010-node-graph |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Tier 1–4 platform overview | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ◐ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Scope library (Actions / Skills / Knowledge / Tools) | ✓ | ◐ | ✓ (§14) | ✗ | ✗ | ✓ | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| JPS schema sections + features | ✗ | ✗ | ✓ | ✗ | ✗ | ◐ | ◐ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| $choices resolution + 5 prefixes | ✓ (concise) | ✗ | ✓ (canonical) | ✗ | ✗ | ◐ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| $ref / scope-name resolution | ✓ | ✗ | ✓ | ✗ | ✗ | ◐ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Template parameters | ✗ | ✗ | ✓ | ◐ | ✗ | ◐ | ◐ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Override merge ($clear / __replace) | ✗ | ✗ | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Structured output (JSON Schema) | ✓ | ✗ | ✓ | ✗ | ✗ | ◐ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| `sprk_configjson` runtime contract | ✗ | ◐ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ◐ | ◐ | ✗ | ✗ |
| `sprk_playbooknode` row vs canvas JSON | ✗ | ◐ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Canvas type ↔ NodeType ↔ ActionType triplet | ✗ | ✓ | ✗ | ◐ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| 9 canvas types | ✗ | ✓ | ✗ | ◐ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| ActionType enum (currently 18 → 19 with EntityNameValidator=141, plus 52 LookupUserMembership) | ✗ | ◐ (18 vals) | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ◐ (DeliverComposite=42) | ✗ | ✗ | ◐ | ✓ (141) | ✗ | ✓ |
| Node executors (per ActionType) | ✓ (summary) | ✓ (detail) | ✗ | ◐ (R3 only) | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| PlaybookExecutionEngine + Orchestration | ✗ | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Scheduler (notification mode) | ✗ | ✓ | ✗ | ◐ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Tool handler framework (`IAnalysisToolHandler`) | ✓ | ◐ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Capability Router | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Safety pipeline (PromptShield / Groundedness / Citations) | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| RAG pipeline + indexes | ✓ | ◐ | ✗ | ✗ | ✓ (deploy) | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Cosmos persistence (R2) | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| PublicContracts / Facade boundary | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ | ✗ | ✗ | ✓ | ◐ | ◐ | ✓ | ✗ |
| `sprk_playbookconsumer` + ConsumerRoutingService | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ◐ | ◐ | ✓ | ◐ |
| `IInvokePlaybookAi` non-document path | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ | ✗ |
| LookupUserMembership ActionType 52 | ✗ | ✓ (R3) | ✗ | ✓ (R3) | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ | ✓ | ✗ | ✗ |
| `joinIds` / `default` Handlebars helpers | ✗ | ✓ | ✗ | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Builder UI safety affordances (rename guard, branch picker, edge hint) | ✗ | ✓ | ✗ | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Canvas↔server drift CI test | ✗ | ✓ | ✗ | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Notification entity (`appnotification`) idempotency | ✗ | ✓ (G4) | ✗ | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ | ✓ | ✗ | ✗ |
| Notification `customData` schema (enriched per FR-6) | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ◐ | ✓ | ✗ | ✗ |
| Deploy-Playbook.ps1 | ✗ | ✗ | ◐ (mentioned) | ✗ | ✗ | ✗ | ✓ (cited) | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Seed-JpsActions.ps1 | ✗ | ✗ | ✓ | ✗ | ✗ | ◐ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| scope-model-index.json | ✗ | ✗ | ◐ | ✗ | ✗ | ✗ | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| jps-scope-refresh skill | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ | ✓ | ✗ | ✗ | ✗ | ✗ | ✓ | ✓ | ✗ | ✗ |
| OOB activity entities vs `sprk_event`/`sprk_communication` | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ (binding) | ✗ | ✗ | ✗ |
| Spaarke entity architecture binding | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ | ◐ | ✗ | ✗ |
| Scope-array enforce-vs-advisory semantics | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Empty-payload behavior contract | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ◐ (FR-12) | ◐ | ◐ |
| `sprk_configurationjson` overuse anti-pattern boundary | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| Customer-facing playbook authoring (Spaarke-as-platform) | ✗ | ✗ | ✗ | ◐ (maker-facing but Spaarke-internal) | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| DeliverComposite (ADR-037) section-keyed SSE | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ |
| 6-tier memory model (chat-routing) | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |

**Key gaps surfaced by the matrix** (rows where every cell is ✗ or ◐):

- `sprk_configjson` vs `sprk_playbooknode` runtime contract
- Scope-array enforce-vs-advisory semantics
- `sprk_configurationjson` overuse anti-pattern boundary
- Customer-facing (platform-distribution) playbook authoring
- `IInvokePlaybookAi` non-document execution path (lives only in R4 decision note)

---

## 3. Duplication Clusters

### Cluster D1 — **Two competing parent docs for AI architecture**

- `docs/architecture/AI-ARCHITECTURE.md` (~510 lines)
- `docs/architecture/playbook-architecture.md` (~685 lines)

**Overlap**: ~20% — both describe ActionType dispatch, scope resolution, IAnalysisToolHandler bridging via AiAnalysisNodeExecutor, RAG L1/L2/L3, integration points table.

**Asymmetry**: AI-ARCHITECTURE.md claims to be the parent ("Tier 1–4 separation"); playbook-architecture.md explicitly cites it as parent. But AI-ARCHITECTURE.md ALSO contains substantial playbook-execution detail (Tool Handler Framework §, Scope Resolution §, Known Pitfalls §). The parent has too much child content.

**Resolution**: **Differentiate at top of each doc + consolidate the playbook-execution detail down to a single home.** Specifically:
- AI-ARCHITECTURE.md retains: 4-tier framing, Cosmos / Safety / Capability Router / PublicContracts / RAG. STRIPS: ToolHandlerRegistry internals, Scope Resolution detail, Known Pitfalls (move to playbook-architecture or a new dedicated doc).
- playbook-architecture.md becomes the canonical home for runtime/execution.
- Both docs explicitly state their scope at the top: "AI platform overview" vs "playbook runtime + execution engine".

### Cluster D2 — **JPS authoring split across guide + skill bodies**

- `docs/guides/JPS-AUTHORING-GUIDE.md` (~1390 lines) — sections 1–13 are JPS reference; §14 "Designing playbooks with Claude Code" is workflow.
- `.claude/skills/jps-action-create/SKILL.md` (~300 lines) — workflow + 7-step procedure.
- `.claude/skills/jps-playbook-design/SKILL.md` (~540 lines) — workflow + scope catalog selection + 13-step procedure.

**Overlap**: JPS-AUTHORING §14 "Designing playbooks with Claude Code" duplicates the goal of `jps-playbook-design` skill but with slightly different step ordering. JPS-AUTHORING §6 "Creating a new JPS Action" duplicates `jps-action-create` skill steps 1–4.

**Resolution**:
- **Reduce JPS-AUTHORING-GUIDE.md to schema-reference + decision-trees only (§§1–5, 7, 8, 11, 12).** Drop §6, §9, §10, §14 (these belong in skills).
- **Skills become procedural source-of-truth.** Each skill cites the guide for *what* JPS is, but each step is owned by the skill.
- **Result**: guide is *reference*; skills are *procedure*.

### Cluster D3 — **Pitfalls scattered across architecture + maker guide + project notes**

- `playbook-architecture.md` "Known Pitfalls" G1–G11 (~3000 words inline; canonical catalog).
- `PLAYBOOK-AUTHOR-GUIDE.md` "Anti-Patterns to Avoid" (10 numbered anti-patterns; partial overlap with G1/G2/G5).
- R4 spec.md owner-clarification "Spaarke entity architecture" — a new "OOB tasks/emails are forbidden" pitfall that didn't exist when G1–G11 were written.

**Overlap**: G1 (Handlebars `??`) is in both architecture + author guide. G5 (membership) is in both. G2 (rename) is in both.

**Resolution**:
- **Architecture doc retains canonical pitfall catalog G1–G11** (engineering-grade analysis).
- **Author guide retains the same G-numbered pitfalls but as plain-language summaries** with cross-link to architecture doc for depth. Avoid divergent content — when one is updated the other MUST be.
- **Add G12 (OOB activity entities)** to both per R4 owner clarification 2026-06-25.

### Cluster D4 — **Deployment guidance scattered**

- `docs/guides/AI-DEPLOYMENT-GUIDE.md` — heavy Azure infra deploy; barely mentions `Seed-JpsActions.ps1` / `Deploy-Playbook.ps1`.
- `JPS-AUTHORING-GUIDE.md` §10 (Deployment) — mentions `Seed-JpsActions.ps1`.
- `.claude/skills/jps-playbook-design/SKILL.md` Steps 9–10 — actual `Deploy-Playbook.ps1` invocation.
- `.claude/skills/dataverse-deploy/SKILL.md` — solution-level deploy (not surveyed in detail).

**Overlap**: Three separate places mention `Seed-JpsActions.ps1` with slightly different command shapes.

**Resolution**:
- **Create new authoritative "ai-guide-playbook-deploy-recipe.md"** consolidating playbook + Action deployment procedures (verify deployed; reconcile drift; redeploy from repo; refresh scope index). This is the doc R4 W0 needs and that today does not exist as a focused operator runbook.
- **AI-DEPLOYMENT-GUIDE.md stays Azure-infra-focused** with one section pointing to the new playbook-deploy recipe.
- **JPS-AUTHORING §10 collapses to a 1-paragraph pointer** to the new recipe.

### Cluster D5 — **`sprk_playbookconsumer` / ConsumerRoutingService / IInvokePlaybookAi triangle**

- `projects/spaarke-ai-platform-chat-routing-redesign-r1/architecture/stateful-chat-architecture.md` §5 + §11 — pattern-level reuse boundary.
- `projects/spaarke-daily-update-service-r4/notes/decisions/030-dispatch-path.md` — Path A.5 binding for R4.
- R4 CLAUDE.md "`sprk_playbookconsumer` Dispatch Investigation" — points at R4 task 030.
- R4 spec.md FR-12 / Q-row — owner directive.

**Overlap**: None outside the projects; this concept has **no canonical doc in `docs/architecture/`**. Three projects reference it but no global home.

**Resolution**:
- **Add new canonical doc `docs/architecture/ai-architecture-consumer-routing.md`** documenting:
  - `sprk_playbookconsumer` entity + columns
  - `IConsumerRoutingService.ResolveAsync` contract + cache behavior
  - `IInvokePlaybookAi.InvokePlaybookAsync` non-document execution path
  - `ConsumerTypes` constants + adoption pattern
  - Decision frame: when to use ConsumerRouting vs direct invoke vs degenerate playbook (Path A / A.5 / B from R4 030).
- Reference this doc from R4 decision 030, chat-routing CLAUDE.md, and any future project that needs to dispatch.

### Cluster D6 — **Scope catalog (Actions / Skills / Knowledge / Tools)**

- `JPS-AUTHORING-GUIDE.md` §14 — lists ACT-001…ACT-008, SKL-001…SKL-010, KNW-001…KNW-010, TL-001…TL-008.
- `.claude/catalogs/scope-model-index.json` (referenced; not surveyed) — programmatic catalog.
- `AI-ARCHITECTURE.md` "Scope Types" table — 4-row summary, no codes.

**Overlap**: JPS-AUTHORING duplicates the JSON catalog *manually*. Catalog drift is inevitable.

**Resolution**: **Remove the inline catalog from JPS-AUTHORING §14.** Replace with pointer "Run `/jps-scope-refresh` to view current catalog from `scope-model-index.json`." This eliminates the manual sync risk.

---

## 4. Stale or Contradicted Content

### S1 — `projects/spaarke-daily-update-service/CLAUDE.md` references "OOB task / activity / email"

The R1-base CLAUDE.md says "MUST use native `appnotification` entity" — OK — but doesn't mention `sprk_event` / `sprk_communication`. The R4 owner clarification (2026-06-25) reveals "we do not use OOB tasks / activities or email — our corresponding entities are `sprk_event` and `sprk_communication`." This is a **load-bearing architecture rule the R1/R2/R3 docs never captured**.

**Impact**: every existing notification playbook JSON file in `projects/spaarke-daily-update-service/notes/playbooks/*.json` was written against OOB `task` / `email` / `appointment` — those files need rewrite per R4 W1 spec.

**Action**: flag for cross-check with code archaeology agent (do the actual deployed playbook configs target `sprk_event` / `sprk_communication`?). If yes, the repo JSON files are wrong; if no, BOTH need correction.

### S2 — `docs/guides/SPAARKE-AI-STRATEGY-AND-ROADMAP.md` last reviewed 2026-02-21

4 months stale. Roadmap content mentions "10 pre-built playbooks" but R3 work has been on R3-foundation and chat-routing patterns. Strategic content (playbooks-are-the-product, AI Foundry positioning) is still aligned per AI-ARCHITECTURE.md.

**Action**: SOFT update — add a 2026-Q3 status block at the top; don't rewrite.

### S3 — JPS-AUTHORING §1 "Supersedes PLAYBOOK-JPS-PROMPT-SCHEMA-GUIDE.md, PLAYBOOK-DESIGN-GUIDE.md"

References two docs that may not exist (or were intentionally removed). Survey did not find them. The "supersedes" claim should be verified — if they exist they need redirect; if they don't, the claim is harmless but stale-looking.

### S4 — `docs/guides/PLAYBOOK-AUTHOR-GUIDE.md` references INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md

Cross-link at top: `[`docs/guides/INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md`](./INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md)`. Not in this survey's inventory; likely exists. Cross-check: it's not in the `docs/guides/` glob results above. **Likely a broken link.** Flag for cross-check.

### S5 — ADR-013 vs `.claude/constraints/ai.md` "MUST NOT create separate AI microservice"

`ai.md` line 65: "**MUST NOT** create separate AI microservice" — categorical.
ADR-013 (refined 2026-05-20): the categorical rule was **replaced** by a 4-criteria exception clause. `ai.md` still has the categorical wording.

**Status**: ‡ Contradicts ADR-013 refinement. ai.md is technically stale by 1 month. The wording is mostly correct in spirit (default is BFF) but the constraint file misses the exception path.

**Action**: update `ai.md` to match refined ADR-013 (default + exception criteria).

### S6 — `playbook-architecture.md` §"Three-Level Node Type System" table predates ActionType 52 + 141

The 18-value ActionType enum tables list ranges 0–2, 10–12, 20–24, 30–33, 40–41, 50–51. But ActionType 52 (LookupUserMembership) IS documented later in the same doc (R3 R3 update 2026-06-22). The table is internally inconsistent: row "30-33 Control" matches "Start (33)" but no row covers 52. Similarly, R4 adds EntityNameValidator=141 — not yet anywhere in the architecture doc.

**Action**: refresh the ActionType range table to include 52 + 141, plus DeliverComposite=42 from ADR-037.

### S7 — `docs/guides/AI-DEPLOYMENT-GUIDE.md` heavy R1/R2/R3 history; no R4 mention

The doc reads as a chronological "what we deployed" log rather than a *current-state* operator runbook. Mentions phases 1–8 but not R3 LookupUserMembership, R4 narrate-playbook dispatch, or chat-routing infrastructure.

**Action**: keep as-is for R1–R3 historical context; explicitly mark sections by R-phase; pull current-state operator procedures into the new playbook-deploy recipe doc (Cluster D4 resolution).

---

## 5. Gaps — Topics NOT Covered Anywhere

| # | Gap | Why it matters | Source-of-discovery |
|---|---|---|---|
| 5.1 | **`sprk_configjson` vs `sprk_playbooknode` runtime contract** | What lives in `sprk_canvaslayoutjson` vs `sprk_configjson` vs `sprk_playbooknode.sprk_configjson`? When does the orchestrator read which? `playbook-architecture.md` mentions both but doesn't draw the boundary explicitly. R4 caught this gap empirically (see R4 spec.md "Repo JSON files = canonical source-of-truth" decision, later REVISED). | R4 spec.md §Decisions |
| 5.2 | **Scope-array enforce-vs-advisory semantics** | `playbook.scopes.actions[]` lists scope codes; node-level `node.scopes.skills[]` lists scope codes. Are they enforced (only-these-allowed) or advisory (UI hint)? No doc states this. JPS-AUTHORING-GUIDE shows the JSON shape but not the runtime contract. | Survey |
| 5.3 | **Empty-payload behavior contract** | R4 FR-12 ACs require "empty-payload tolerance" — the dispatcher returns 200 + empty response when there's nothing to narrate. Is this a general playbook contract or daily-briefing-specific? No doc says. | R4 decision 030 + spec FR-12 |
| 5.4 | **Deploy-Playbook.ps1 contract** | What does it do? Pre-flight scope check + node create + N:N associate + canvas layout save — mentioned in skill but no operator runbook. | Cluster D4 |
| 5.5 | **JPS Action vs Playbook deployment lifecycle** | R4 W0 introduces "JPS is data, not code" as a first-class concern. No existing doc captures the deployment-as-data discipline (deploy Action rows before code that references them; reconcile via `jps-playbook-audit` before redeploy). | R4 CLAUDE.md §Quick Reference |
| 5.6 | **`IInvokePlaybookAi` non-document execution path** | The only doc describing this is R4 decision 030, where it was *discovered* during the survey. It's the canonical non-document playbook invocation surface but lives in no architecture doc. | R4 decision 030 |
| 5.7 | **Customer-facing playbook authoring (Spaarke-as-platform)** | Spaarke distributes the playbook engine. PLAYBOOK-AUTHOR-GUIDE.md is internal-team-oriented and assumes Spaarke environment access. Customers will eventually author + deploy their own playbooks. No "third-party AI engineer onboarding" doc exists. | Owner directive in survey prompt |
| 5.8 | **`sprk_configurationjson` overuse anti-pattern boundary** | When does a config belong in `sprk_configjson` (per-node) vs `sprk_configurationjson` (alternate column? Or hypothetical?) vs `customData` JSON on `appnotification`? R4 enriches `customData` and adds dual-write `sprk_category` column — owner clarification implies a boundary rule. | R4 spec.md §FR-6 + §FR-17c |
| 5.9 | **R4 `BRIEF-NARRATE` playbook end-to-end design** | Lives in `projects/spaarke-daily-update-service-r4/notes/design/010-...md` — project-internal. After R4 ships, there's no canonical home for this design in `docs/`. | Survey |
| 5.10 | **DeliverComposite usage decision tree** | ADR-037 introduces it; R4 design 010 explains why R4 does NOT use it. No doc captures "when to choose DeliverComposite vs single-action Output" for future playbooks beyond ADR-037's terse table. | Cross-ref |
| 5.11 | **ActionType allocation policy** | EntityNameValidator=141 was assigned per R4 spec ("slots into post-LLM cluster 130/140"). Where's the rule about ActionType integer ranges? Nowhere documented. | R4 spec.md §FR-3 |
| 5.12 | **Spaarke entity architecture (sprk_event vs OOB)** | R4 owner clarification 2026-06-25 is BINDING but lives only in R4 CLAUDE.md memory pointer. No global standard. | R4 CLAUDE.md |
| 5.13 | **Membership ↔ UAC orthogonality** | R4 spec.md owner clarification: "membership and UAC are orthogonal." No standard doc captures this — operators reading `MEMBERSHIP-RESOLUTION-GUIDE.md` (which I didn't survey but is cited) may not infer it. | R4 spec.md §Owner Clarifications |
| 5.14 | **Playbook dispatch grammars (which path for which use-case)** | After R4, three dispatch paths exist: ConsumerRouting + IInvokePlaybookAi (Path A.5), ConsumerRouting + ExecutePlaybookAsync (Path A — document-only), and direct invoke (Path B). Six consumers + R4 daily-briefing now use this — no canonical decision-tree doc. | R4 decision 030 + chat-routing context |

---

## 6. Naming Convention Compliance

Owner's stated preference: prefix-based sortable names — `ai-architecture-*`, `ai-guide-*`, `ai-skill-*`, `ai-standards-*`.

### Current state — 0% compliance

NONE of the surveyed docs use the prefix convention. The closest is `auth-AI-azure-resources.md` (caps inconsistent) and `ai-*.md` files in `docs/architecture/` and `docs/guides/` that mix lowercase with all-caps and SCREAMING_SNAKE.

### Per-doc proposed rename

| Current path | Proposed renamed path | Rename type | Notes |
|---|---|---|---|
| `docs/architecture/AI-ARCHITECTURE.md` | `docs/architecture/ai-architecture-overview.md` | HARD rename + redirect stub | High-traffic; many cross-refs to update. |
| `docs/architecture/playbook-architecture.md` | `docs/architecture/ai-architecture-playbook-runtime.md` | HARD rename + redirect stub | Heavily cross-referenced from skills + CLAUDE.md files. |
| `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` | `docs/architecture/ai-architecture-workspace-shell.md` | HARD rename + redirect stub | Lower-traffic. |
| `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` | `docs/architecture/ai-architecture-dashboard-widget-model.md` | HARD rename + redirect stub | |
| `docs/architecture/SPAARKEAI-COMPONENT-MODEL.md` | `docs/architecture/ai-architecture-component-inventory.md` | HARD rename + redirect stub | |
| `docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md` | `docs/architecture/ai-architecture-componentization-audit.md` | HARD rename + redirect stub | |
| `docs/architecture/AI-CHAT-STRATEGY-M365-COPILOT-VS-SPRKCHAT.md` | `docs/architecture/ai-architecture-chat-strategy.md` | HARD rename + redirect stub | |
| `docs/architecture/ai-document-summary-architecture.md` | `docs/architecture/ai-architecture-document-summary.md` | SOFT rename (already prefix) | Reorder words. |
| `docs/architecture/ai-semantic-relationship-graph.md` | `docs/architecture/ai-architecture-semantic-graph.md` | SOFT rename | |
| `docs/architecture/auth-AI-azure-resources.md` | `docs/architecture/ai-architecture-azure-resources.md` | HARD rename + redirect stub | Move out of `auth-*` cluster. |
| `docs/guides/JPS-AUTHORING-GUIDE.md` | `docs/guides/ai-guide-jps-authoring.md` | HARD rename + redirect stub | After §3-resolution content-trim. |
| `docs/guides/PLAYBOOK-AUTHOR-GUIDE.md` | `docs/guides/ai-guide-playbook-author.md` | HARD rename + redirect stub | |
| `docs/guides/AI-DEPLOYMENT-GUIDE.md` | `docs/guides/ai-guide-platform-deploy.md` | HARD rename + redirect stub | After D4-resolution content-trim to platform infra. |
| `docs/guides/AI-MONITORING-DASHBOARD.md` | `docs/guides/ai-guide-monitoring.md` | HARD rename + redirect stub | |
| `docs/guides/SPAARKE-AI-STRATEGY-AND-ROADMAP.md` | `docs/guides/ai-guide-strategy-roadmap.md` | HARD rename + redirect stub | |
| `docs/guides/AI-EMBEDDING-STRATEGY.md` | `docs/guides/ai-guide-embedding-strategy.md` | HARD rename + redirect stub | |
| `docs/guides/AI-MODEL-SELECTION-GUIDE.md` | `docs/guides/ai-guide-model-selection.md` | HARD rename + redirect stub | |
| `docs/guides/WORKSPACE-AI-PREFILL-GUIDE.md` | `docs/guides/ai-guide-workspace-prefill.md` | HARD rename + redirect stub | |
| `docs/guides/ai-assistant-theming.md` | `docs/guides/ai-guide-theming.md` | SOFT rename | |
| `docs/guides/ai-document-summary.md` | `docs/guides/ai-guide-document-summary.md` | SOFT rename | |
| `docs/guides/ai-troubleshooting.md` | `docs/guides/ai-guide-troubleshooting.md` | SOFT rename | |
| `docs/guides/spaarkeai-launch-points.md` | `docs/guides/ai-guide-launch-points.md` | SOFT rename | |
| `.claude/constraints/ai.md` | `.claude/constraints/ai-constraints.md` (or stay) | NO RENAME | Constraint files have own convention; `.claude/constraints/` is already prefix-sorted. |
| `.claude/skills/jps-*/SKILL.md` | (no rename — skill dir name is the contract) | NO RENAME | Skill names are bound to the harness. |

**Doc count after rename**: identical, but **lexical sort puts every AI-related doc together**: `ai-architecture-*` first, then `ai-guide-*`. Operators and Claude Code both benefit.

**Recommendation**: do HARD renames in **one PR** with a 1-line redirect stub at each old path. SOFT renames in any subsequent PR. Avoid the temptation to do this incrementally — every cross-ref needs sweeping.

---

## 7. Skill ↔ Doc Alignment

| Skill | Cites which docs | Are the cited docs canonical? | Procedural consistency |
|---|---|---|---|
| `jps-action-create` | JPS-AUTHORING-GUIDE.md (schema reference); `examples/document-profiler.json` + `examples/clause-analyzer.json` (exemplars) | YES — JPS-AUTHORING is the canonical schema reference (despite duplication with §6). Examples are inline. | Consistent. Step 4 validation list maps to JPS-AUTHORING §12. |
| `jps-playbook-design` | scope-model-index.json (REQUIRED); playbook-architecture.md (node model); JPS-AUTHORING-GUIDE.md (schema); examples/ dir | YES on 3 of 4 (catalog, architecture, schema). Examples reuse jps-action-create's dir. | Mostly consistent. The skill's Step 4 scope selection algorithm is more detailed than JPS-AUTHORING §14 "Designing playbooks with Claude Code." Skill is the more current source. |
| `jps-playbook-audit` | scope-model-index.json + live Dataverse query patterns | YES. | Low-traffic; sees ~3 inbound refs. Procedural alignment OK. |
| `jps-validate` | JPS-AUTHORING-GUIDE.md (schema reference); `examples/document-profiler.json` (exemplar) | YES. | Step 2–8 validation maps to JPS-AUTHORING §12 checklist. |
| `jps-scope-refresh` | scope-model-index.json + Refresh-ScopeModelIndex.ps1 + Dataverse table names | YES. | Plumbing skill; no doc dependency beyond catalog. |
| `dataverse-mcp-usage` (not surveyed) | MCP tool docs | unknown | not in scope. |
| `dataverse-deploy` (not surveyed) | solution-level deploy | unknown | covers solution but not playbook-data-deployment. |

**Finding**: skills are well-aligned with docs they cite. **Inconsistency direction is doc → skill**: docs (especially JPS-AUTHORING §6, §14) duplicate skill-level procedure, drift over time, and don't get refreshed alongside skill updates. **Treat skills as the procedural source-of-truth.** Docs cite schema + reference content only.

---

## 8. Customer-Readiness Audit

Spaarke ships the playbook engine as a platform. Customers will author + deploy their own playbooks against their own data. Current doc surface against that need:

| Doc | Customer-readable? | Gap for customer AI engineer |
|---|---|---|
| AI-ARCHITECTURE.md | Internal-team — references internal file paths (`src/server/api/Sprk.Bff.Api/Services/Ai/...`), assumes Spaarke env vocabulary (`spaarkedev1`, `sprk_*` prefix). | Customer doesn't have these paths; needs an "Spaarke AI Platform Architecture (External)" overview that explains the public concepts (Actions, Skills, Knowledge, Tools, Playbook node graph, $choices) without internal implementation paths. |
| playbook-architecture.md | Internal — heavy on .cs file references; G1–G11 pitfalls reference internal class names. | Customer needs a "Playbook Authoring Reference" abstracted from Spaarke internals — covers the same node types + dispatch model but in vendor-neutral language. |
| JPS-AUTHORING-GUIDE.md | **PARTIALLY customer-readable.** Sections 1–8 (schema reference) are vendor-neutral; §10 (deployment) assumes Spaarke deploy scripts; §14 references Spaarke-specific scope codes (ACT-001…). | Closest existing customer-facing doc. Split out the schema-only sections + customer-runtime contract → "ai-customer-jps-reference.md". |
| PLAYBOOK-AUTHOR-GUIDE.md | **Partially.** Use-case walkthrough is plain-language; cites `MembershipResolverService` (Spaarke-internal). | Could become customer-facing with replacement of Spaarke-specific service references. |
| AI-DEPLOYMENT-GUIDE.md | Operator-facing for Spaarke internal env. Heavy Azure CLI / Bicep / spaarkedev1 references. | Customer environment is theirs; this doc is structurally wrong for them. |
| AI-MONITORING-DASHBOARD.md | Operator-facing Spaarke env. | Same. |
| `.claude/skills/jps-*/` | **NOT customer-readable** — skills are harness-internal procedures. | Customer needs a non-skill procedural doc for the same workflows. |

**Recommendation**:
- **Mark every existing doc with an audience header** (Internal / Customer / Both).
- **Create at minimum 2 new customer-facing docs in a separate `docs/customer-ai/` folder** (or `docs/ai-platform-external/`): `ai-customer-overview.md` (what is the platform; what can I do) + `ai-customer-playbook-reference.md` (how do I write a playbook; what's the JPS schema; how do I deploy).
- Leave the customer-readiness backlog as a documented work item — not a R4 blocker, but capture it now so step 3 of the canonical-truth loop produces an internal target structure that doesn't preclude later customer doc work.

---

## 9. Recommended Canonical Doc Structure

Target post-consolidation structure (illustrative — adjust per step-3 owner review):

### `docs/architecture/` — concept + decisions

| Path | Scope (1 line) | Replaces |
|---|---|---|
| `docs/architecture/ai-architecture-overview.md` | Tier 1–4 platform, Cosmos / Safety / Capability Router / PublicContracts facade, RAG | AI-ARCHITECTURE.md (trimmed) |
| `docs/architecture/ai-architecture-playbook-runtime.md` | LOAD-BEARING runtime: PlaybookExecutionEngine, NodeExecutor framework, scheduler, canvas-type/NodeType/ActionType triplet, sprk_configjson vs sprk_playbooknode contract, scope-array semantics | playbook-architecture.md (refreshed) |
| `docs/architecture/ai-architecture-jps.md` | JPS pipeline architecture: format detection, override merge, $ref / $choices resolution, structured output | NEW — extracted from JPS-AUTHORING §2 + AI-ARCHITECTURE chunks |
| `docs/architecture/ai-architecture-consumer-routing.md` | sprk_playbookconsumer + IConsumerRoutingService + IInvokePlaybookAi + ConsumerTypes + Path A / A.5 / B decision tree | NEW — closes gap 5.6 + 5.14 |
| `docs/architecture/ai-architecture-rag.md` | RAG L1/L2/L3 + indexes + EmbeddingCache + SearchClient routing | extracted from AI-ARCHITECTURE + AI-DEPLOYMENT |
| `docs/architecture/ai-architecture-chat-memory.md` | 6-tier memory + Insights reuse boundary + Cosmos containers | rehome stateful-chat-architecture.md from project to docs/ |

### `docs/guides/` — operator + author runbooks

| Path | Scope | Replaces |
|---|---|---|
| `docs/guides/ai-guide-jps-authoring.md` | JPS schema reference + decision trees | JPS-AUTHORING-GUIDE.md (trimmed §§1–5, 7, 11, 12) |
| `docs/guides/ai-guide-playbook-author.md` | Maker recipe + builder UI affordances + anti-patterns | PLAYBOOK-AUTHOR-GUIDE.md (refreshed with G12 OOB-activity rule) |
| `docs/guides/ai-guide-playbook-deploy-recipe.md` | Deploy-Playbook.ps1 + Seed-JpsActions.ps1 + jps-scope-refresh + verification | NEW — closes gap 5.4 + 5.5; pulls from JPS-AUTHORING §10 + skill steps |
| `docs/guides/ai-guide-platform-deploy.md` | Azure infra deploy (R1–R3 history + current-state) | AI-DEPLOYMENT-GUIDE.md (annotated by R-phase) |
| `docs/guides/ai-guide-monitoring.md` | App Insights dashboard + alerts | AI-MONITORING-DASHBOARD.md |
| `docs/guides/ai-guide-strategy-roadmap.md` | Business-stakeholder strategy | SPAARKE-AI-STRATEGY-AND-ROADMAP.md (updated header only) |
| `docs/guides/ai-guide-troubleshooting.md` | "Playbook has no nodes — using Legacy mode" + other UAT-discovery diagnostics | NEW — closes UAT-discovery-driven gap |
| `docs/guides/ai-guide-launch-points.md` | SpaarkeAi web-resource URL contract | spaarkeai-launch-points.md |
| `docs/guides/ai-guide-model-selection.md` | gpt-4o-mini / gpt-4o cost decision | AI-MODEL-SELECTION-GUIDE.md (consolidate JPS-AUTHORING §14 model bits) |

### `docs/standards/` — cross-cutting rules

| Path | Scope | Replaces |
|---|---|---|
| `docs/standards/ai-standards-playbook-design.md` | sprk_configurationjson overuse boundary; sprk_event/sprk_communication entity rule; OOB-activity ban; ActionType allocation policy; scope-array enforcement | NEW — closes gap 5.8 + 5.11 + 5.12 |
| `docs/standards/ai-standards-deployment-discipline.md` | "JPS is data, not code" rule; deploy Action rows before code; reconcile via audit before redeploy | NEW — closes gap 5.5 |
| `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` | (existing — keep) | unchanged |

### `.claude/` — procedural source-of-truth

| Path | Status |
|---|---|
| `.claude/skills/jps-*/SKILL.md` | UNCHANGED. Skills remain procedural source-of-truth. |
| `.claude/adr/ADR-013-ai-architecture.md` | UNCHANGED. Refined ADR is canonical. |
| `.claude/adr/ADR-037-multinode-output-composition.md` | UNCHANGED. |
| `.claude/constraints/ai.md` | UPDATED — sync to refined ADR-013 (default + 4-criteria exception). |
| `.claude/patterns/ai/*.md` | UNCHANGED. |

**Net doc count**: ~21 architecture+guide+standards docs after consolidation vs ~22 currently — but **content is reorganized, not multiplied**. The new additions (consumer-routing, chat-memory rehoming, JPS architecture, deploy recipe, troubleshooting, playbook standards, deployment discipline) absorb gaps. Reductions come from JPS-AUTHORING trim, AI-ARCHITECTURE trim, and the inline-catalog removal in §6.

---

## 10. Removal / Redirect Plan

| Source doc | Action | Target | Reason |
|---|---|---|---|
| JPS-AUTHORING-GUIDE.md §6 "Creating a new JPS Action" | REMOVE | `.claude/skills/jps-action-create/SKILL.md` | Duplicate of skill procedure (Cluster D2). |
| JPS-AUTHORING-GUIDE.md §9 "Migration Guide (Hardcoded to JPS)" | REMOVE | NEW `docs/guides/ai-guide-jps-migration.md` (R4-out-of-scope; queue for later) OR delete entirely | Single-use historic procedure; few-active-tasks. |
| JPS-AUTHORING-GUIDE.md §10 "Deployment" | REMOVE | `docs/guides/ai-guide-playbook-deploy-recipe.md` | Cluster D4 consolidation. |
| JPS-AUTHORING-GUIDE.md §14 "Designing playbooks with Claude Code" | REMOVE | `.claude/skills/jps-playbook-design/SKILL.md` | Duplicate of skill (Cluster D2). |
| AI-ARCHITECTURE.md "Tool Handler Framework", "Scope Resolution", "Known Pitfalls" | RELOCATE | `ai-architecture-playbook-runtime.md` (refreshed playbook-architecture.md) | Cluster D1 consolidation. |
| AI-ARCHITECTURE.md "Audit findings — bff-ai-architecture-audit-r1" callout | KEEP IN PLACE but link out | (no move) | Audit findings are historical decisions; keep visible at top. |
| docs/guides/AI-DEPLOYMENT-GUIDE.md "RAG Infrastructure" + "Email-to-Document" + "RAG Indexing Pipeline" sections | RELOCATE | `ai-architecture-rag.md` (architecture) and `ai-guide-platform-deploy.md` (operator runbook) split | These are not really deployment-guide content; they're feature-area subjects. |
| stateful-chat-architecture.md | RELOCATE | `docs/architecture/ai-architecture-chat-memory.md` | Project-internal architecture should be cross-project canonical. |
| `projects/spaarke-daily-update-service-r4/notes/design/010-...md` | RELOCATE summary | `docs/architecture/ai-architecture-overview.md` + `ai-guide-playbook-author.md` "Worked example: BRIEF-NARRATE" | After R4 ships. R4-internal design becomes platform-canonical example. |
| `docs/guides/PLAYBOOK-AUTHOR-GUIDE.md` "Quick-Start Recipe" | KEEP, REFRESH | (no move) | Add G12 OOB-activity-rule per R4 owner clarification. |
| SPAARKE-AI-STRATEGY-AND-ROADMAP.md | KEEP, add 2026-Q3 status block | (no move) | Stale by 4 months; soft refresh sufficient. |
| `projects/spaarke-daily-update-service/CLAUDE.md` "MUST use native `appnotification` entity" | KEEP, ADD a pointer to the new `ai-standards-playbook-design.md` G12 entity rule | (no move) | R1-base context; current R4 binding rule must be linkable from anywhere. |
| `.claude/constraints/ai.md` "MUST NOT create separate AI microservice" | UPDATE | (in place) | Sync to refined ADR-013 — Stale-by-1-month per §4 S5. |

---

## Cross-Check Asks for Code Archaeology Agent (Wave A)

The following claims in docs need verification against actual code state. Listed here so Wave A can confirm or flag:

1. **playbook-architecture.md** "9 canvas types / 4 NodeType / 18 ActionType" — does the code today match (with R3 52 + R4-pending 141 + ADR-037 42)?
2. **ADR-037** says `NodeType.DeliverComposite = 100_000_004` is appended. Confirm not yet a deployed/active enum value in master, OR if it is, when.
3. **playbook-architecture.md G6** asserts a CI drift test at `CanvasServerMappingDriftTests.cs` — confirm test exists + passes today.
4. **R4 decision 030** claims `IInvokePlaybookAi.cs` exists with documented "non-document semantic" at lines 67–72. Confirm.
5. **R4 spec §FR-3** asserts existing ExecutorActionType values include 0/51/52/60/70/80/90/100/110/120/130/140. Is the gap-analysis (no 141) accurate?
6. **AI-ARCHITECTURE.md** "Audit findings" lists 4 binding decisions + cites file paths for `.claude/patterns/ai/*.md`. Confirm those pattern files exist and reflect the decision content.
7. **PLAYBOOK-AUTHOR-GUIDE.md** references `INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md` (apparent broken link per §4 S4).
8. **OOB activity entities vs sprk_event/sprk_communication** — owner says deployed playbooks target `sprk_event`. Confirm against actual `sprk_configjson` data + actual `CreateNotificationNodeExecutor` FetchXml templates.

---

*Survey complete. Step 3 (canonical-truth writer) input is the union of §3 duplication clusters, §5 gaps, and §9 target structure. Step 4 (rename PR scoping) input is §6 + §10.*
