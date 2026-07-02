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
- ADR conflicts or violations (see §6.5 for the resolution protocol)
- Breaking changes (API contracts, DB schema)
- Scope expansion beyond task boundaries

Format: 🔔 **Human Input Required** with situation, options, recommendation.

---

## 6.5. ADR Conflict Resolution Protocol (BINDING — added 2026-06-29 by `spaarkeai-compose-r1`)

**Principle**: ADRs are codified prior decisions, not immutable laws. They exist as guardrails to keep us out of known failure modes — not to force sub-optimal solutions when a legitimate technical need conflicts with them. When such a conflict surfaces, agents and humans MUST explicitly surface it and resolve it through one of three paths. Silent compliance with an ADR rule that produces a sub-optimal outcome is itself a failure mode.

### The three resolution paths

When code, design, or implementation legitimately conflicts with an ADR rule:

| Path | When to choose | Owner action |
|---|---|---|
| **(A) Project-scoped exception** | The ADR remains correct in general; this project has a narrow, documented reason to deviate | Document the deviation + rationale in the project's `design.md` and/or `spec.md` ADR Tensions section; cite in PR description; code-review approves explicitly |
| **(B) ADR amendment** | Context has changed; the ADR's prior decision is no longer correct as written | Propose an ADR amendment (concise + full versions); link from the proposing project; merge ADR change before or alongside the dependent code |
| **(C) Pivot to comply** | On further inspection, an ADR-compliant approach meets the requirement equally well or better | Document the pivot reasoning; proceed under existing ADR |

**No fourth path.** Silent violation, "we'll fix it later" tech debt, or hand-waving past an ADR rule without surfacing the conflict are all forbidden.

### When this protocol fires

Trigger conditions:
- An agent (you) recognizes that strict compliance with an ADR will produce a worse technical outcome than a documented exception or amendment would
- `adr-check` or `code-review` flags a violation that the implementer believes is justified
- During spec authoring, the design surfaces a requirement that conflicts with an existing ADR's MUST/MUST NOT rule
- During task execution, an ADR constraint blocks a legitimate implementation need

### Required output format when invoking this protocol

🔔 **ADR Conflict — Resolution Required**

- **ADR in question**: ADR-XXX [title]
- **Specific rule**: [quote the MUST / MUST NOT being challenged]
- **Conflict**: [explain the technical need and why the rule produces a sub-optimal outcome]
- **Proposed path**: A (exception) / B (amendment) / C (pivot to comply)
- **Rationale**: [why this path is correct]
- **Impact if path A or B is accepted**: [scope of the deviation/amendment]
- **Alternative considered (and rejected)**: [show that the other paths were genuinely considered]

The human reviewer chooses or refines the path. Do NOT proceed silently if escalation is warranted.

### Where this protocol is enforced

- **At design time** — `design-to-spec` and `project-pipeline` surface anticipated ADR tensions in a dedicated **ADR Tensions** section of `spec.md`
- **At code-review time** — `code-review` Step 6 (ADR Compliance Check) accepts a reasoned exception (path A) cited in the PR description; otherwise flags as Critical
- **At task-execute Step 9.5** — `adr-check` violations either are fixed (path C), formalized as exceptions (path A), or trigger amendment workflow (path B); silent retry-until-clean is not the loop
- **At ADR-check** — the skill output includes a "Challenge Path" section alongside violations, prompting the human to choose a resolution rather than just accepting the violation list as final

### What this protocol is NOT

- Not a license to ignore ADRs casually — the bar for path A/B is "documented + rationale + reviewer approval"
- Not an excuse to bypass auth, security, or compliance ADRs without explicit human sign-off
- Not retroactive — code that violated an ADR silently before this protocol existed is still in violation; this protocol applies to new decisions going forward

### Anti-patterns this catches

- ❌ "ADR says no, so I'll write worse code to comply" — surface as path B candidate
- ❌ "I violated the ADR but it's fine, the reviewer won't notice" — silent violation, forbidden
- ❌ "The ADR is wrong but I don't want to amend it" — surface as path B; the cost of amendment is part of the work
- ❌ "I'll comply now and document the exception later" — exception MUST be documented at the point of decision, not deferred

**Binding for ≥6 months from 2026-06-29.** Reviewed by next major procedure-quality audit.

---

## 7. Task Completion & Transition

After completing any task:
1. Update task `.poml` status to "completed"
2. Update `TASK-INDEX.md`: 🔲 → ✅
3. **Reset `current-task.md`** for next task (clears steps, files, decisions)
4. Set `current-task.md` to next pending task (or "none" if project complete)
5. Report completion; ask if ready for next task

`current-task.md` tracks only the **active task** — history is in `TASK-INDEX.md` and per-task `.poml` files.

### Project-close test diet gate (BINDING, added 2026-06-26 by `ci-cd-unit-test-remediation-r1` task CICD-081 per spec FR-B09)

When the just-completed task is a `090-wrapup-*` task (i.e., the project is closing), `task-execute` Step 11 invokes `/test-diet` BEFORE marking the project complete. `/test-diet` reconciles tests added/modified during the project against the 17-ban build-vs-maintain classifier ([ADR-038 §7](docs/adr/ADR-038-testing-strategy.md#7-build-vs-maintain-criteria-scaffolding-test-bans--added-2026-06-26-per-spec-fr-b08)): MAINTAIN-class tests stay at their KEEP path, SCAFFOLDING-class tests are deleted, AMBIGUOUS tests require reviewer judgment. The skill is read-only — it emits `git rm` / `git mv` commands for the reviewer; it does not auto-execute. Output: `projects/{name}/notes/test-diet-report.md`. Skipping this gate is a HARD WARNING; wrap-up PR description MUST cite the report or document the skip rationale. Binding for ≥6 months from 2026-06-26.

---

## 8. Task Execution Rigor Levels

Every task is executed via `task-execute` at one of three rigor levels, auto-detected per task.

| Level | When applied | Quality gates |
|---|---|---|
| **FULL** | Code implementation, architecture changes, post-compaction recovery, tags include `bff-api`/`pcf`/`plugin`/`auth`, modifying `.cs`/`.ts`/`.tsx`, 6+ steps, deps on 3+ tasks | ✅ code-review + adr-check at Step 9.5 |
| **STANDARD** | New file creation, tasks with constraints, Phase 2.x+ tasks | ⏭️ Skipped |
| **MINIMAL** | Documentation, inventory, simple updates | ⏭️ Skipped |
| **TEST-MODIFYING (override row, added 2026-06-26 by ci-cd-unit-test-remediation-r1 spec FR-B07 + ADR-038)** | **Any task that modifies `tests/**` OR has tags including `testing` / `test-reset` / `deletion` / `integration-test`** | ✅ code-review + adr-check at Step 9.5 **UNCONDITIONALLY** (overrides default STANDARD skip; binding ≥6 months from 2026-06-26) |

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
4. **Verify publish-size impact** on EVERY BFF-touching task (not just NuGet adds — per R4 NFR-01 / F-3, strengthened 2026-05-26). Run `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, measure compressed output, and report absolute size + diff vs prior baseline in task notes / PR description. Binding **ceiling: ≤60 MB compressed** (spec NFR-01). Current baseline as of 2026-05-26: ~45.65 MB (post-Phase 5 Outcome A). Threshold for escalation: ≥+5 MB single-task delta → explicit justification required; ≥55 MB cumulative → architecture review; ≥60 MB → HARD STOP. Full rule in [`.claude/constraints/azure-deployment.md`](.claude/constraints/azure-deployment.md) "BFF Publish-Size Per-Task Verification Rule (NFR-01)" section.
5. **Verify no new HIGH-severity CVE** from `dotnet list package --vulnerable --include-transitive`.
6. **Update corresponding tests** per the [Test update obligation](.claude/constraints/bff-extensions.md#f-test-update-obligation-binding-per-fr-22--d-05) section in `bff-extensions.md`. PRs modifying `src/server/api/Sprk.Bff.Api/Services/` MUST add/update tests in `tests/unit/Sprk.Bff.Api.Tests/`. Endpoints that map unconditionally must have unconditional service registration (per RB-T028-03/04/05/06, filed 2026-05-31 by `sdap-bff.api-test-suite-repair`, fixed by `sdap.bff.api-test-suite-repair-r2` task 011 via 18-service Null-Object migration). Exceptions require explicit code review sign-off with reason. Enforcement is PR template (`.github/pull_request_template.md`) + code review checklist (`docs/procedures/testing-and-code-quality.md`) + reviewer judgment — **NOT a CI script** per design.md §5.5.

**The asymmetric-registration rule has 3 binding sub-mechanisms (added 2026-06-01)**. When a PR modifies a `*Module.cs` DI file inside an `if (flag) { ... }` block, the PR reviewer applies (in order):

- **§ F.1 Asymmetric-Registration Tier 1.5 Anti-Pattern** — [`.claude/constraints/bff-extensions.md` § F.1](.claude/constraints/bff-extensions.md#f1-asymmetric-registration-tier-15-anti-pattern-binding-per-r2-task-081--d-13). For every new conditional service, run the static-scan recipe + apply [ADR-032 Null-Object Kill-Switch Pattern](.claude/adr/ADR-032-bff-nullobject-kill-switch.md) (P1/P2/P3 per service).
- **§ F.2 Fixture-Config-FIRST Inspection Protocol** — [`.claude/constraints/bff-extensions.md` § F.2](.claude/constraints/bff-extensions.md#f2-fixture-config-first-inspection-protocol-binding-per-r2-task-081--d-13). When a test is Skip'd suspecting DI issue, FIRST inspect fixture config / claims / mocks for non-contract values per [`docs/procedures/test-fixture-contracts.md`](docs/procedures/test-fixture-contracts.md).
- **§ F.3 Empirical-Reproduction-FIRST Protocol** — [`.claude/constraints/bff-extensions.md` § F.3](.claude/constraints/bff-extensions.md#f3-empirical-reproduction-first-protocol-binding-per-r2-task-081--d-13). Before applying a ledger entry's recommended fix, hand-trace + reproduce empirically; file a path-b decision record if root cause differs.

Full procedure-doc reference: [`docs/procedures/testing-and-code-quality.md`](docs/procedures/testing-and-code-quality.md) §§18.1–18.4. ADR-030 is the canonical mechanism implementing §10 bullet 6 when a service must remain feature-gated.

**Project-level imperative**: every project that adds code to the BFF MUST have a `design.md` section titled **Placement Justification** answering the decision criteria for each major component. Projects skipping this section will be flagged in code review.

**Hot-Path Declaration (added 2026-06-26 by `ci-cd-unit-test-remediation-r1` task CICD-062 per spec FR-C04)**: any project that touches BFF (or the parallel SpaarkeAi code page at `src/solutions/SpaarkeAi/**`) MUST include a `<hot-path-declaration>` XML block in its `design.md`. Block enumerates: BFF Y/N, SpaarkeAi Y/N, ci-workflows Y/N, skill-directives Y/N, root-CLAUDE.md Y/N. See [`.claude/constraints/bff-extensions.md` § G](.claude/constraints/bff-extensions.md#g-hot-path-declaration-binding-per-ci-cd-unit-test-remediation-r1-fr-c04-added-2026-06-26) for the full rule + evidence base. `project-pipeline` Step 3 emits HARD WARNING if missing. Active-project registry: [`projects/INDEX.md`](projects/INDEX.md). 2026-06-26 sweep found 13 of 17 active worktrees touch BFF, 8 of 17 touch SpaarkeAi.

**Evidence base**: [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) — the 2026-05-20 BFF AI extraction assessment found the codebase structurally AI-dominant (69% of `Services/` LOC) but operationally justified to keep unified. It also surfaced the process debt this section addresses.

This is **not advisory**. It is a binding workflow rule for every BFF-touching task.

---

## 11. Component Justification — Default to Reuse (BINDING)

**Principle**: Every new component must justify its existence. Prefer extending an existing service over introducing a new one. Prefer one component that works exceptionally well over five that partially overlap.

Applies at EVERY scope boundary: spec authoring, plan WBS, task creation, code review. §10 BFF Hygiene is the BFF-specific instance; this is the universal rule.

### The three-question template

For every NEW service / abstraction / interface / endpoint / DI registration / package / Dataverse column / file surface, answer one sentence each:

1. **Existing** — What does this overlap with? (Verify by `Grep` / `Glob` before claiming "none".)
2. **Extension** — Can I extend the existing instead? (If yes → extend. If no → say why in ≤2 sentences.)
3. **Cost-of-doing-nothing** — Name a concrete behavior or contract that fails without this. (NOT "scalability" / "abstraction layer" / "future flexibility.")

A justification that cannot articulate concrete failure modes for question 3 = scope creep. Demote the task to "extend existing X" or drop it.

### Enforcement points

| Stage | Mechanism |
|---|---|
| Spec authoring | `project-pipeline` validates spec scope against existing components during Step 2 resource discovery |
| Plan WBS | `task-create` Step 3.5.6 requires `<justification>` element in each new-component POML |
| Code review | `code-review` Step 6.6 verifies justification is concrete + cites grep evidence |

### Anti-patterns this catches (real examples from chat-routing-redesign-r1)

- ❌ "Delete LegalWorkspace `CreateRecordStep.tsx` as dead code per OC-R4-05" — retirement doc actually preserves it as library; cost-of-doing-nothing was assumed wrongly
- ❌ "Add new `sprk_playbookcode` lookup keys" — `sprk_playbookid` already exists as the immutable opaque ID; existing-question unanswered
- ❌ "Build 8 retrieval tool handlers" — 7 of 8 fail extension test for the MVP use case; one excellent handler beats five that partially overlap

Tasks that ONLY modify existing files (edit, refactor, fix bug, add tests for existing surface) do NOT require justification — the rule applies to NEW surface, not modification.

Cost-of-rule: one paragraph per new component. Cost-of-rule-absence: shipped scope creep.

---

## 12. Build Commands

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

## 13. System Entry Points (where to start reading)

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

## 14. Context Layer Hierarchy

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

## 15. Knowledge Repository for Rapidly-Evolving Topics

Claude's training data has a knowledge cutoff. For rapidly-evolving Microsoft/AI platform topics where Claude's context may be stale (Azure AI Foundry, Power Platform updates, Dataverse MCP, Office Add-ins SDK, SharePoint Embedded), use the **`researcher` subagent**:

- **Mechanism**: `.claude/agents/researcher.md` (Opus, effort: high, project memory enabled)
- **Trigger**: Invoke when external/current technical knowledge is needed that's not in skills, ADRs, or patterns
- **Behavior**: Consults curated knowledge at [`knowledge/`](knowledge/) FIRST → falls back to Microsoft Learn, official Microsoft GitHub repos, then generic web search
- **Accumulation**: Findings stored in the subagent's `MEMORY.md` (project-scoped) — accumulates Microsoft-platform knowledge across sessions

**Knowledge repo location**: [`knowledge/`](knowledge/) at repo root — populated by parallel project `coding-knowledge-base-setup-r1` (merged to master before this rewrite). Currently includes `agent-framework/`, `azure-ai-search/`, refresh procedures, and a refresh log. The researcher consults this BEFORE external search and memoizes findings in its `MEMORY.md`.

---

## 16. Hooks — Current Guidance

Hooks are **NOT configured** in `.claude/settings.json` beyond what exists. Quality enforcement runs via (1) skill-level checks (`task-execute`, `adr-check`, `code-review`), (2) CI/CD (`.github/workflows/sdap-ci.yml`), and (3) the `doc-drift-audit` skill at project transitions. Reconsider hooks only for narrow, high-frequency automations that run in <5s with zero false positives.

---

## 17. Pointers — Where to find everything

| Topic | Pointer |
|---|---|
| Skills + trigger phrases + slash commands | [`.claude/skills/INDEX.md`](.claude/skills/INDEX.md) |
| ADRs (concise) | [`.claude/adr/INDEX.md`](.claude/adr/INDEX.md) |
| ADRs (full history) | [`docs/adr/`](docs/adr/) |
| Code patterns (25-line pointer files) | [`.claude/patterns/`](.claude/patterns/) |
| Cross-cutting constraints | [`.claude/constraints/`](.claude/constraints/) |
| **BFF additions governance (binding)** | [`.claude/constraints/bff-extensions.md`](.claude/constraints/bff-extensions.md) — load before adding to `Sprk.Bff.Api`. §G Config Boundary rewritten 2026-06-29 for R7 single-hop dispatch (FR-29). |
| **BFF AI extraction assessment (evidence base)** | [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) |
| **Wiring a new consumer (maker tutorial)** | [`docs/guides/ai-guide-consumer-wiring.md`](docs/guides/ai-guide-consumer-wiring.md) — what a consumer is, step-by-step wiring procedure, chat-summarize migration case study (R7 FR-31, created Wave 6 task 067) |
| **Playbook-driven LLM Output Pattern (architecture)** | [`docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md`](docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md) — Wave 11 two-layer architecture (Layer 1 orchestrator template resolution + Layer 2 PromptSchemaRenderer `## Input` section). The canonical pattern for narrative-output consumers (Daily Briefing shipped reference; Insight Engine matter-summary worked example). Required reading before authoring new AI executors or playbooks. R7 task 111a / operator-binding 2026-06-29. |
| **Build a new narrative-output consumer (maker tutorial)** | [`docs/guides/BUILD-A-NEW-NARRATIVE-OUTPUT-CONSUMER.md`](docs/guides/BUILD-A-NEW-NARRATIVE-OUTPUT-CONSUMER.md) — step-by-step authoring guide for Action JPS + playbook + destination (UpdateRecord / ReturnResponse / SendEmail / CreateNotification). Two worked examples (Daily Briefing shipped; Insight Engine future). R7 task 111a. |
| Cross-cutting failure modes (anti-patterns + gotchas) | [`.claude/FAILURE-MODES.md`](.claude/FAILURE-MODES.md) |
| Procedure-surface changelog | [`.claude/CHANGELOG.md`](.claude/CHANGELOG.md) |
| Architecture (subsystems, design) | [`docs/architecture/`](docs/architecture/) — includes `AI-ARCHITECTURE.md`, `auth-azure-resources.md` |
| **SpaarkeAi workspace architecture (end-to-end pipeline)** | [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — cold-load → widget render, storage, BFF surface, 6 system layouts (incl. Calendar), pane-width fracs + all-panes-collapsed overlay. Refreshed through R13 (task 123). |
| **SpaarkeAi dashboard + widget model (two-wrapper architecture — authoritative)** | [`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — three surfaces, Dashboard wrapper (`LegalWorkspaceApp`) vs Direct widget wrapper (`WorkspaceWidgetRegistry`) with intentional-retention rationale (OC-R4-06), four mount sources, dual-use pattern (Calendar canonical), LegalWorkspace-as-dashboard-engine framing (OC-R4-05). Required reading for any new widget design. R4 DR-01 / W-1. |
| **SpaarkeAi component model (inventory)** | [`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`](docs/architecture/SPAARKEAI-COMPONENT-MODEL.md) — `@spaarke/ui-components`, `@spaarke/ai-widgets`, `@spaarke/auth`, `@spaarke/legal-workspace`, `@spaarke/events-components`, PaneEventBus contract. Refreshed through R13 (task 123). |
| **SpaarkeAi componentization audit (honest reuse assessment)** | [`docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md`](docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md) — coupling gaps + prioritized remediation backlog; Calendar widget (§2A) is the proven canonical "shared-lib widget + thin LW shim" pattern. Refreshed through R13 (task 123). |
| **Build a new workspace widget (tutorial)** | [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — five archetypes with decision tree (composable section, sophisticated single-purpose direct, dual-use Pattern D, Context-pane, modal-launcher); Calendar Pattern D worked example. Rewritten in R4 W-2 (2026-05-26). |
| **LegalWorkspaceApp embedded-mode host contract (binding before embedding)** | [`docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`](docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md) — six host-requirement categories (config init, theme ownership, sessionStorage sentinels, webApi shim, mount semantics, lifecycle hooks) with 21 testable MUSTs; SpaarkeAi reference impl. R4 DR-07 / C-2. |
| **LegalWorkspace standalone code-page retirement** | [`docs/architecture/LEGALWORKSPACE-RETIREMENT.md`](docs/architecture/LEGALWORKSPACE-RETIREMENT.md) — retirement decision (OC-R4-05), consumer audit, components-as-library boundary; supersedes R3 FR-25 / NFR-10. R4 DR-03 / W-6. |
| **Spaarke DataGrid Framework (architecture)** | [`docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md) — `<DataGrid configId=… />` framework: shared lib component + `sprk_gridconfiguration` Dataverse contract + `IDataverseClient` adapter (MDA / BFF). Supersedes `universal-dataset-grid-architecture.md` (PCF, retires in Phase F). |
| **Spaarke To Do (architecture)** | [`docs/architecture/spaarke-todo-architecture.md`](docs/architecture/spaarke-todo-architecture.md) — `sprk_todo` first-class entity with 11-entity regarding (ADR-024), SmartTodo Code Page, parent-form subgrids, Outlook ribbon + LinkedTodosBanner, BFF Office endpoints, feature-gated MS To Do sync scaffolding (ADR-032). Supersedes `event-to-do-architecture.md`. R3 (PR #373). |
| **DataGrid Framework configuration guide** | [`docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md`](docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md) — maker + dev recipe: author a `sprk_gridconfiguration` record, host shell wiring, worked example, troubleshooting. |
| Coding standards (cross-cutting conventions) | [`docs/standards/`](docs/standards/) — `CODING-STANDARDS.md`, `INTEGRATION-CONTRACTS.md`, `ANTI-PATTERNS.md` |
| **Data access decision criteria (`Xrm.WebApi` vs BFF)** | [`docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`](docs/standards/DATA-ACCESS-DECISION-CRITERIA.md) — when to use host-context `Xrm.WebApi` vs BFF for Dataverse access; 7 criteria + worked examples; load alongside `.claude/constraints/bff-extensions.md` for BFF-side decisions |
| **Modal decision criteria (OOB `navigateTo` vs proprietary Fluent v9 vs browse-shell)** | [`docs/standards/MODAL-DECISION-CRITERIA.md`](docs/standards/MODAL-DECISION-CRITERIA.md) — when to open a record/document/form/picker via OOB `Xrm.Navigation.navigateTo` vs proprietary Fluent v9 Dialog vs proprietary + [`RecordNavigationModalShell`](src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/README.md) (browse "1 of N" pattern). Load alongside [`.claude/patterns/ui/record-modal-selection.md`](.claude/patterns/ui/record-modal-selection.md) whenever a task opens a modal. Includes hybrid pattern (proprietary browse + OOB escalation) and iframe-embedding anti-pattern. Created 2026-07-01. |
| **Chat attachment policy (binary cap, MIME, total-text, PDF pages, upgrade path)** | [`docs/standards/CHAT-ATTACHMENT-POLICY.md`](docs/standards/CHAT-ATTACHMENT-POLICY.md) — single source of truth for chat attachment sizing; 25 MB binary cap (client-only; server enforces text-char caps, not binary); MIME allow-list; single-LLM-call invariant. R4 A-4 / FR-04 (2026-05-26). |
| **Calendar shared components (two intentional variants)** | `@spaarke/events-components` — **`CalendarSection`** (workspace widget; click-day filter, controlled mode, stateless; existing) + **`CalendarFilterPane`** (side-pane filter builder; Calendar + From/To + date-field dropdown + Apply; session-storage; R4 task 055 / B-6 hoist 2026-05-26). Same lib, different intents per `notes/b6-pre-change-diff.md`. |
| Operational guides (deploy, configure, troubleshoot) | [`docs/guides/`](docs/guides/) — 40+ guides incl. `auth-deployment-setup.md`, `PCF-DEPLOYMENT-GUIDE.md`, `DATAVERSE-MCP-INTEGRATION-GUIDE.md`, `ENVIRONMENT-DEPLOYMENT-GUIDE.md` |
| Development procedures (test, CI/CD, code review) | [`docs/procedures/`](docs/procedures/) — `testing-and-code-quality.md`, `ci-cd-workflow.md`, `context-recovery.md` |
| **Testing strategy ADR (standalone)** | [`docs/adr/ADR-038-testing-strategy.md`](docs/adr/ADR-038-testing-strategy.md) — integration-heavy pyramid; 6 KEEP path categories as MUST rules; coverage = observation never gate (binding ≥6 months from 2026-06-26); ban `Mock<HttpMessageHandler>` + DI-registration + ctor null-check tests. **STANDALONE — does NOT supersede ADR-022 (PCF Platform Libraries)**. ci-cd-unit-test-remediation-r1 Phase 1. |
| **Test architecture standard (operational)** | [`docs/standards/TEST-ARCHITECTURE.md`](docs/standards/TEST-ARCHITECTURE.md) — test pyramid, 6 KEEP categories with examples, `TimeProvider` over `Stopwatch`, mock-boundary rules, forcing-function enforcement. Cross-referenced by `tests/CLAUDE.md` + `.claude/constraints/testing.md`. |
| **Active-project registry (hot-path coordination)** | [`projects/INDEX.md`](projects/INDEX.md) — every active worktree (last-30-day-active) with hot-path declarations (BFF / SpaarkeAi / ci-workflows / skill-directives / root-CLAUDE Y/N). Maintained atomically by `project-pipeline` (new project) + `task-execute` Step 0.5 (hot-path touch). No cron. Consumed by `/conflict-check` auto-invoke. 2026-06-26 sweep: 17 active, 13 touch BFF, 8 touch SpaarkeAi. |
| Dataverse data model (entity schemas, ERD) | [`docs/data-model/`](docs/data-model/) |
| Azure resources (endpoints, names, conventions) | [`docs/architecture/auth-azure-resources.md`](docs/architecture/auth-azure-resources.md) |
| Project initialization workflow | [`/design-to-spec`](.claude/skills/design-to-spec/) → [`/project-pipeline`](.claude/skills/project-pipeline/) |
| **Portfolio tracking + DevOps procedures** | [`docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md`](docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md) (initiation + portfolio integration) · [`docs/procedures/AI-CODING-PROCEDURES-GUIDE.md`](docs/procedures/AI-CODING-PROCEDURES-GUIDE.md) (lifecycle scenarios) · [project #2](https://github.com/users/spaarke-dev/projects/2) (board) — 9 `/devops-*` skills, 9 hooked existing skills; spec: `projects/spaarke-devops-project-tracking-r1/` |
| Active project state | `projects/{name}/current-task.md` |
| Auth architecture (Spaarke Auth v2 — canonical) | [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](.claude/adr/ADR-028-spaarke-auth-architecture.md), [`docs/guides/auth-deployment-setup.md`](docs/guides/auth-deployment-setup.md), [`.claude/patterns/auth/spaarke-sso-binding.md`](.claude/patterns/auth/spaarke-sso-binding.md) (design rationale archive: [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md)) |
| Active skill audit + sign-off | [`.claude/AUDIT-FINDINGS-SKILLS.md`](.claude/AUDIT-FINDINGS-SKILLS.md), [`.claude/AUDIT-FINDINGS-CLAUDEMD.md`](.claude/AUDIT-FINDINGS-CLAUDEMD.md) |
| Researcher subagent (deep-dive Microsoft platform topics) | [`.claude/agents/researcher.md`](.claude/agents/researcher.md) |
| Reversibility archive (removed content preserved by date) | [`.claude/archive/`](.claude/archive/) |
| Module-specific CLAUDE.md | [`src/server/api/Sprk.Bff.Api/CLAUDE.md`](src/server/api/Sprk.Bff.Api/CLAUDE.md), [`src/client/pcf/CLAUDE.md`](src/client/pcf/CLAUDE.md), [`src/server/shared/CLAUDE.md`](src/server/shared/CLAUDE.md) |
| Repository structure (top-level overview) | [`README.md`](README.md) |

---

## 18. Footer

**Maintained by** the project owner. To extend this file: follow the rules in `.claude/skills/ai-procedure-maintenance/SKILL.md`. When in doubt about whether content belongs here vs in `docs/`: if it's a binding rule the agent must apply every turn → here; if it's reference/tutorial → `docs/`. Every PR touching this file MUST add an entry to [`.claude/CHANGELOG.md`](.claude/CHANGELOG.md).
