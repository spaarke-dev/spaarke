# CLAUDE.md — Spaarke Repository Instructions

> **Last Reviewed**: 2026-05-17
> **Reviewed By**: ai-procedure-quality-r1 Phase 3b (rewrite from 1190 → ~225 lines; OLD version archived at `.claude/archive/2026-05-17/CLAUDE.md`)
> **Purpose**: Repository-wide operational rules for Claude Code. Loads every session.

---

## 1. What is Spaarke?

Spaarke is an **enterprise AI-directed legal operations intelligence platform** built on Power Apps/Dataverse, SharePoint Embedded, and Azure AI services; backend in **.NET 8 Minimal API**; frontend in **custom React Code Pages and PCF components**; the AI layer combines an internal **JPS (JSON Prompt Schema)** playbook system with Azure OpenAI deployments and a retrieval layer over SharePoint Embedded documents.

---

## 2. Source of Truth: Code, then `.claude/`, then `docs/`

**Code wins. Docs lag.** When code and docs disagree, fix the docs. Loading priority for the agent:

1. **Code** — `.cs`, `.ts`, `.tsx` files in `src/` are the ground truth
2. **`.claude/patterns/`** — 25-line pointer files telling you WHICH code to read
3. **`.claude/adr/`** — concise ADR constraints (MUST / MUST NOT)
4. **`.claude/constraints/`** — topic-based summaries
5. **`docs/architecture/`** — design decisions + rationale (no implementation detail)
6. **`docs/guides/`** — operational procedures (deploy, configure)
7. **`docs/procedures/`** — development workflow (test, CI/CD, code review)

---

## 3. Sub-Agent Write Boundary (IMPORTANT)

**Sub-agents launched via the Agent tool CANNOT write to `.claude/` paths** (skills, patterns, constraints, catalogs, agents, settings). This is intentional — it protects skill definitions from accidental modification by parallel agents.

**Canonical pattern** (proven across this project's 31 Phase 2a audits):
1. Sub-agents READ + AUDIT `.claude/` files in parallel; return findings as structured text
2. The MAIN SESSION applies fixes using Edit/Write tools

When an agent reports "Edit denied on `.claude/...`" — that's the boundary working correctly, not a bug.

---

## 4. 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing project tasks, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Why this matters

The `task-execute` skill ensures:
- ✅ Knowledge files loaded (ADRs, constraints, patterns)
- ✅ Context tracked in `current-task.md`
- ✅ Proactive checkpointing every 3 steps
- ✅ Quality gates run (code-review + adr-check at Step 9.5)
- ✅ Progress recoverable after compaction
- ✅ PCF version bumping + deployment skills invoked correctly

**Bypassing leads to**: missing ADR constraints, lost progress after compaction, skipped quality gates, manual errors.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases, you MUST invoke `task-execute`:

| User Says | Required Action |
|---|---|
| "work on task X" | Invoke task-execute with task X POML |
| "continue" / "keep going" / "next task" | Read `TASK-INDEX.md`, find first 🔲, invoke task-execute |
| "continue with task X" / "resume task X" | Invoke task-execute with task X POML |
| "pick up where we left off" | Load `current-task.md`, invoke task-execute |

### Parallel Task Execution

When tasks can run in parallel (no inter-task dependencies), each task STILL uses `task-execute`. Pattern: ONE message with MULTIPLE Skill tool invocations (one per task). Sequential invocations waste parallelism.

For details, see [`.claude/skills/task-execute/SKILL.md`](.claude/skills/task-execute/SKILL.md).

---

## 5. Context Management & Checkpointing

### Usage thresholds

| Context usage | Action |
|---|---|
| < 60% | Proceed normally |
| 60–70% | Run `/checkpoint` (proactive save), then continue |
| > 70% | STOP. Run `/checkpoint`, request `/compact` |
| > 85% | EMERGENCY. Run `/checkpoint`, stop immediately |

**Commands**: `/context` (check) · `/checkpoint` (save) · `/compact` (compress) · `/clear` (wipe)

### Proactive Checkpointing (MANDATORY)

Claude MUST checkpoint frequently. These rules are NOT optional:

| Condition | Action |
|---|---|
| After every 3 completed task steps | Run `context-handoff` (silent: "✅ Checkpoint.") |
| After modifying 5+ files | Run `context-handoff` |
| After any deployment | Run `context-handoff` |
| Before a complex step | Run `context-handoff` |
| Context > 60% | Run `context-handoff` (verbose report) |
| Context > 70% | Run `context-handoff` + STOP + request `/compact` |

All work state must be recoverable from files alone: `projects/{name}/current-task.md`, `projects/{name}/tasks/TASK-INDEX.md`, `projects/{name}/CLAUDE.md`. See `.claude/skills/context-handoff/SKILL.md`.

---

## 6. Human Escalation Triggers

**MUST request human input for**:
- Ambiguous or conflicting requirements
- Security-sensitive code (auth, secrets, encryption)
- ADR conflicts or violations
- Breaking changes (API contracts, DB schema)
- Scope expansion beyond task boundaries

Format: 🔔 **Human Input Required** with situation, options, recommendation.

---

## 7. Task Completion & Transition

After completing any task:
1. Update task `.poml` status to "completed"
2. Update `TASK-INDEX.md`: 🔲 → ✅
3. **Reset `current-task.md`** for next task (clears steps, files, decisions)
4. Set `current-task.md` to next pending task (or "none" if project complete)
5. Report completion; ask if ready for next task

`current-task.md` tracks only the **active task** — history is in `TASK-INDEX.md` and per-task `.poml` files.

---

## 8. Task Execution Rigor Levels

Every task is executed via `task-execute` at one of three rigor levels, auto-detected per task.

| Level | When applied | Quality gates |
|---|---|---|
| **FULL** | Code implementation, architecture changes, post-compaction recovery, tags include `bff-api`/`pcf`/`plugin`/`auth`, modifying `.cs`/`.ts`/`.tsx`, 6+ steps, deps on 3+ tasks | ✅ code-review + adr-check at Step 9.5 |
| **STANDARD** | Tests, new file creation, tasks with constraints, tags include `testing`/`integration-test`, Phase 2.x+ tasks | ⏭️ Skipped |
| **MINIMAL** | Documentation, inventory, simple updates | ⏭️ Skipped |

### Mandatory Rigor Level Declaration

At task start, Claude Code MUST output:

```
🔒 RIGOR LEVEL: [FULL | STANDARD | MINIMAL]
📋 REASON: [Why this level was chosen]
📖 PROTOCOL STEPS TO EXECUTE: ...
Proceeding with Step 0...
```

This declaration is non-negotiable and makes protocol shortcuts visible. **Override**: "Execute with FULL protocol" / "Execute with MINIMAL protocol" — use sparingly.

For the full decision tree, see `.claude/skills/task-execute/SKILL.md` Step 0.5.

---

## 9. Security Rules

- **NEVER** commit secrets (`.env`, `appsettings.local.json`, credentials, API keys)
- Use `config/*.local.json` for local secrets (gitignored)
- Use Azure Key Vault for production secrets
- All API endpoints require auth (except `/healthz`, `/ping`)

---

## 10. BFF Hygiene — Binding Governance (READ BEFORE ADDING TO `Sprk.Bff.Api`)

**The BFF is the single backend for every Spaarke client surface.** Past projects (R1, R2, R3, Insights Engine, others) each added features without holistic consideration of overall BFF quality. The 2026-05-19 publish-size jump (65 → 75+ MB) and the 20 inbound CRUD→AI direct dependencies are downstream consequences. This stops here.

When a task adds NEW endpoints, services, DI registrations, packages, or background work to `src/server/api/Sprk.Bff.Api/` (or to `Spaarke.Core` / `Spaarke.Dataverse` consumed by BFF), you MUST:

1. **Load [`.claude/constraints/bff-extensions.md`](.claude/constraints/bff-extensions.md)** before designing the addition. It is the binding pre-merge checklist + decision criteria.
2. **State the placement decision explicitly** — even if the answer is "in BFF" — in the PR description or design doc. Cite the decision criteria from `bff-extensions.md`.
3. **Use the `Services/Ai/PublicContracts/` facade** for any CRUD code that needs AI capability. Do NOT inject `IOpenAiClient`, `IPlaybookService`, or other AI-internal types directly into CRUD code (per refined ADR-013, 2026-05-20).
4. **Verify publish-size impact** before merging if adding NuGet packages. Baseline is ~60 MB compressed per [`.claude/constraints/azure-deployment.md`](.claude/constraints/azure-deployment.md).
5. **Verify no new HIGH-severity CVE** from `dotnet list package --vulnerable --include-transitive`.

**Project-level imperative**: every project that adds code to the BFF MUST have a `design.md` section titled **Placement Justification** answering the decision criteria for each major component. Projects skipping this section will be flagged in code review.

**Evidence base**: [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) — the 2026-05-20 BFF AI extraction assessment found the codebase structurally AI-dominant (69% of `Services/` LOC) but operationally justified to keep unified. It also surfaced the process debt this section addresses.

This is **not advisory**. It is a binding workflow rule for every BFF-touching task.

---

## 11. Build Commands

| Action | Command |
|---|---|
| Build BFF API | `dotnet build src/server/api/Sprk.Bff.Api/` |
| Run tests | `dotnet test` |
| Format C# | `dotnet format` |
| PCF prod build | `npm run build:prod` (**NOT** `npm run build` — see [`FAILURE-MODES.md#AP-1`](.claude/FAILURE-MODES.md#ap-1-skill-prescribes-x-but-x-is-wrong)) |

For full build/test reference, see [`docs/procedures/testing-and-code-quality.md`](docs/procedures/testing-and-code-quality.md).

### Node Installs — Avoid `npm ci` for Vite Solutions

Many `src/solutions/*` Vite projects have stale `package-lock.json` files; `npm ci` fails on ~14 of 16 solutions. Use `npm install --legacy-peer-deps --no-audit --no-fund` instead. The build scripts handle this automatically; don't add raw `npm ci` to new scripts.

---

## 12. System Entry Points (where to start reading)

| Subsystem | Start here | Shows |
|---|---|---|
| BFF API | `src/server/api/Sprk.Bff.Api/Program.cs` | Endpoint registration, DI, middleware |
| PCF Controls | `src/client/pcf/{Control}/control/index.ts` | Control lifecycle (init, updateView, destroy) |
| Code Pages | `src/solutions/{Page}/src/main.tsx` | React 18 SPA entry with auth bootstrap |
| Dataverse Plugins | `src/dataverse/plugins/.../BaseProxyPlugin.cs` | Plugin base class + lifecycle |
| AI Pipeline | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` | AI tool orchestration |
| Shared UI | `src/client/shared/Spaarke.UI.Components/src/index.ts` | Component library exports |
| Auth | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` | OBO + app-only Graph auth |
| Background Jobs | `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs` | Service Bus job processing |

## 13. Context Layer Hierarchy

| Layer | Contains | When to load |
|---|---|---|
| **Code** | Implementation (source of truth) | Always — read before implementing |
| **`.claude/patterns/`** | 25-line pointer files → code entry points | Per-task — tells you what to read |
| **`.claude/adr/`** | Concise ADR constraints (MUST / MUST NOT) | Per-task — rules to follow |
| **`.claude/constraints/`** | Topic-based constraint summaries | Per-task — quick rule reference |
| **`.claude/catalogs/`** | AI scope + model catalog (JSON) | JPS playbook authoring/auditing |
| **`docs/architecture/`** | Decisions + rationale only | When you need WHY behind a decision |
| **`docs/standards/`** | Cross-cutting coding standards | Before implementing new code |
| **`docs/guides/`** | Operational procedures | When deploying, configuring, troubleshooting |
| **`docs/procedures/`** | Development workflow | During development process |
| **`docs/data-model/`** | Dataverse entity schemas, ERD | When touching Dataverse data |
| **`docs/adr/`** | Full ADR history | Rarely — deep architectural context only |

---

## 14. Knowledge Repository for Rapidly-Evolving Topics

Claude's training data has a knowledge cutoff. For rapidly-evolving Microsoft/AI platform topics where Claude's context may be stale (Azure AI Foundry, Power Platform updates, Dataverse MCP, Office Add-ins SDK, SharePoint Embedded), use the **`researcher` subagent**:

- **Mechanism**: `.claude/agents/researcher.md` (Opus, effort: high, project memory enabled)
- **Trigger**: Invoke when external/current technical knowledge is needed that's not in skills, ADRs, or patterns
- **Behavior**: Consults curated knowledge at [`knowledge/`](knowledge/) FIRST → falls back to Microsoft Learn, official Microsoft GitHub repos, then generic web search
- **Accumulation**: Findings stored in the subagent's `MEMORY.md` (project-scoped) — accumulates Microsoft-platform knowledge across sessions

**Knowledge repo location**: [`knowledge/`](knowledge/) at repo root — populated by parallel project `coding-knowledge-base-setup-r1` (merged to master before this rewrite). Currently includes `agent-framework/`, `azure-ai-search/`, refresh procedures, and a refresh log. The researcher consults this BEFORE external search and memoizes findings in its `MEMORY.md`.

---

## 15. Hooks — Current Guidance

Hooks are **NOT configured** in `.claude/settings.json` beyond what exists. Quality enforcement runs via (1) skill-level checks (`task-execute`, `adr-check`, `code-review`), (2) CI/CD (`.github/workflows/sdap-ci.yml`), and (3) the `doc-drift-audit` skill at project transitions. Reconsider hooks only for narrow, high-frequency automations that run in <5s with zero false positives.

---

## 16. Pointers — Where to find everything

| Topic | Pointer |
|---|---|
| Skills + trigger phrases + slash commands | [`.claude/skills/INDEX.md`](.claude/skills/INDEX.md) |
| ADRs (concise) | [`.claude/adr/INDEX.md`](.claude/adr/INDEX.md) |
| ADRs (full history) | [`docs/adr/`](docs/adr/) |
| Code patterns (25-line pointer files) | [`.claude/patterns/`](.claude/patterns/) |
| Cross-cutting constraints | [`.claude/constraints/`](.claude/constraints/) |
| **BFF additions governance (binding)** | [`.claude/constraints/bff-extensions.md`](.claude/constraints/bff-extensions.md) — load before adding to `Sprk.Bff.Api` |
| **BFF AI extraction assessment (evidence base)** | [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) |
| Cross-cutting failure modes (anti-patterns + gotchas) | [`.claude/FAILURE-MODES.md`](.claude/FAILURE-MODES.md) |
| Procedure-surface changelog | [`.claude/CHANGELOG.md`](.claude/CHANGELOG.md) |
| Architecture (subsystems, design) | [`docs/architecture/`](docs/architecture/) — includes `AI-ARCHITECTURE.md`, `auth-azure-resources.md` |
| **SpaarkeAi workspace architecture (end-to-end pipeline)** | [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — cold-load → widget render, storage, BFF surface, 6 system layouts (incl. Calendar), pane-width fracs + all-panes-collapsed overlay. Refreshed through R13 (task 123). |
| **SpaarkeAi dashboard + widget model (two-wrapper architecture — authoritative)** | [`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — three surfaces, Dashboard wrapper (`LegalWorkspaceApp`) vs Direct widget wrapper (`WorkspaceWidgetRegistry`) with intentional-retention rationale (OC-R4-06), four mount sources, dual-use pattern (Calendar canonical), LegalWorkspace-as-dashboard-engine framing (OC-R4-05). Required reading for any new widget design. R4 DR-01 / W-1. |
| **SpaarkeAi component model (inventory)** | [`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`](docs/architecture/SPAARKEAI-COMPONENT-MODEL.md) — `@spaarke/ui-components`, `@spaarke/ai-widgets`, `@spaarke/auth`, `@spaarke/legal-workspace`, `@spaarke/events-components`, PaneEventBus contract. Refreshed through R13 (task 123). |
| **SpaarkeAi componentization audit (honest reuse assessment)** | [`docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md`](docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md) — coupling gaps + prioritized remediation backlog; Calendar widget (§2A) is the proven canonical "shared-lib widget + thin LW shim" pattern. Refreshed through R13 (task 123). |
| **LegalWorkspaceApp embedded-mode host contract (binding before embedding)** | [`docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`](docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md) — six host-requirement categories (config init, theme ownership, sessionStorage sentinels, webApi shim, mount semantics, lifecycle hooks) with 21 testable MUSTs; SpaarkeAi reference impl. R4 DR-07 / C-2. |
| **LegalWorkspace standalone code-page retirement** | [`docs/architecture/LEGALWORKSPACE-RETIREMENT.md`](docs/architecture/LEGALWORKSPACE-RETIREMENT.md) — retirement decision (OC-R4-05), consumer audit, components-as-library boundary; supersedes R3 FR-25 / NFR-10. R4 DR-03 / W-6. |
| **Build a new workspace widget (tutorial)** | [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — five archetypes with decision tree (composable section, sophisticated single-purpose direct, dual-use Pattern D, Context-pane, modal-launcher); Calendar Pattern D worked example. Rewritten in R4 W-2 (2026-05-26). |
| Coding standards (cross-cutting conventions) | [`docs/standards/`](docs/standards/) — `CODING-STANDARDS.md`, `INTEGRATION-CONTRACTS.md`, `ANTI-PATTERNS.md` |
| **Data access decision criteria (`Xrm.WebApi` vs BFF)** | [`docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`](docs/standards/DATA-ACCESS-DECISION-CRITERIA.md) — when to use host-context `Xrm.WebApi` vs BFF for Dataverse access; 7 criteria + worked examples; load alongside `.claude/constraints/bff-extensions.md` for BFF-side decisions |
| Operational guides (deploy, configure, troubleshoot) | [`docs/guides/`](docs/guides/) — 40+ guides incl. `auth-deployment-setup.md`, `PCF-DEPLOYMENT-GUIDE.md`, `DATAVERSE-MCP-INTEGRATION-GUIDE.md`, `ENVIRONMENT-DEPLOYMENT-GUIDE.md` |
| Development procedures (test, CI/CD, code review) | [`docs/procedures/`](docs/procedures/) — `testing-and-code-quality.md`, `ci-cd-workflow.md`, `context-recovery.md` |
| Dataverse data model (entity schemas, ERD) | [`docs/data-model/`](docs/data-model/) |
| Azure resources (endpoints, names, conventions) | [`docs/architecture/auth-azure-resources.md`](docs/architecture/auth-azure-resources.md) |
| Project initialization workflow | [`/design-to-spec`](.claude/skills/design-to-spec/) → [`/project-pipeline`](.claude/skills/project-pipeline/) |
| Active project state | `projects/{name}/current-task.md` |
| Auth architecture (Spaarke Auth v2 — canonical) | [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](.claude/adr/ADR-028-spaarke-auth-architecture.md), [`docs/guides/auth-deployment-setup.md`](docs/guides/auth-deployment-setup.md), [`.claude/patterns/auth/spaarke-sso-binding.md`](.claude/patterns/auth/spaarke-sso-binding.md) (design rationale archive: [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md)) |
| Active skill audit + sign-off | [`.claude/AUDIT-FINDINGS-SKILLS.md`](.claude/AUDIT-FINDINGS-SKILLS.md), [`.claude/AUDIT-FINDINGS-CLAUDEMD.md`](.claude/AUDIT-FINDINGS-CLAUDEMD.md) |
| Researcher subagent (deep-dive Microsoft platform topics) | [`.claude/agents/researcher.md`](.claude/agents/researcher.md) |
| Reversibility archive (removed content preserved by date) | [`.claude/archive/`](.claude/archive/) |
| Module-specific CLAUDE.md | [`src/server/api/Sprk.Bff.Api/CLAUDE.md`](src/server/api/Sprk.Bff.Api/CLAUDE.md), [`src/client/pcf/CLAUDE.md`](src/client/pcf/CLAUDE.md), [`src/server/shared/CLAUDE.md`](src/server/shared/CLAUDE.md) |
| Repository structure (top-level overview) | [`README.md`](README.md) |

---

## 17. Footer

**Maintained by** the project owner. To extend this file: follow the rules in `.claude/skills/ai-procedure-maintenance/SKILL.md`. When in doubt about whether content belongs here vs in `docs/`: if it's a binding rule the agent must apply every turn → here; if it's reference/tutorial → `docs/`. Every PR touching this file MUST add an entry to [`.claude/CHANGELOG.md`](.claude/CHANGELOG.md).
