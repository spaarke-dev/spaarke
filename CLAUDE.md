<!--
MAINTAINER NOTES (block-level HTML comments are stripped before Claude Code injects this file, so these cost no context).

Last reviewed: 2026-10-07 — cleanup 66 KB / 499 lines → see the PR relocation table; previous version archived at .claude/archive/2026-10-07/CLAUDE.md.

WHAT BELONGS HERE: only what an agent must apply on a turn where it would not otherwise open another document —
binding rules, safety guards, naming rules, and "Before you X → read Y" triggers (§17). Keep a one-clause reason
when the reason prevents misapplication (literal executors need the intent, not just the rule).

WHAT DOES NOT: evidence, history, incident write-ups, worked examples, measured numbers that age, detail of a
document a trigger already points to, rules that only apply when editing certain files (use .claude/rules/*.md
with paths: frontmatter), procedure (use the skill). Put them in the target doc, .claude/FAILURE-MODES.md or the
owning constraint file.

BUDGET: Anthropic guidance is < 200 lines per CLAUDE.md ("longer files consume more context and reduce
adherence"). Hard ceiling 30 KB. A PR exceeding either removes an equivalent amount or carries the owner's
explicit OK. Every PR touching this file adds a .claude/CHANGELOG.md entry stating the byte delta.

STYLE: state rules plainly. Avoid emphasis inflation (🚨, ABSOLUTE, CRITICAL, MUST in caps on every line) — newer
models over-apply emphatic instructions. Use backticked paths, not markdown links (links are not followed; the
duplicate target only costs tokens).

SECTION NUMBERS ARE EXTERNAL API: skills and docs cite §6.5, §10, §11, §17 … Never renumber; retire a section by
leaving its number with a pointer.

PROCEDURE: .claude/skills/ai-procedure-maintenance/SKILL.md (Checklist F) governs changes to this file.

AFTER EDITING: run /doctor prompt-audit (Claude Code ≥ 2.1.283) to catch conflicts and dead references.
-->

# CLAUDE.md — Spaarke repository instructions

## 1. What is Spaarke?

An enterprise AI-directed legal operations platform on Power Apps/Dataverse, SharePoint Embedded and Azure AI. Backend: the .NET 10 Minimal API BFF (`src/server/api/Sprk.Bff.Api`). Frontend: React Code Pages and PCF controls. AI: the JPS (JSON Prompt Schema) playbook system over Azure OpenAI, with retrieval over SharePoint Embedded documents.

### 1.1 Product names — read before "fixing" a name

| Product name | What it is | Engineering identifiers (do not rename) |
|---|---|---|
| Spaarke Console | The three-pane user app; the code calls it "SpaarkeAi" | `sprk_spaarkeai`, `src/solutions/SpaarkeAi/`, `scripts/Deploy-SpaarkeAi.ps1`, `deploy-spaarke-ai.yml` |
| Spaarke Matter Management | The model-driven app | — |
| Spaarke External Access | The external SPA | — |
| Spaarke Connect (SKU) / Connection Engine | Third-party data binding | `Sprk.Connect.*` |
| Decision Record | The append-only record of decisions | "ledger" stays correct in engineering docs |

Product names and engineering identifiers deliberately differ. Read "SpaarkeAi" in code and older docs as the Console, and do not sweep-rename it (tracked as #1095). Terminology changes originate only in `projects/spaarke-ontology-platform-r1/notes/ontology-component-model.md` §3.

## 2. Source of truth

Code wins; docs lag. When they disagree, the code is right and the doc gets fixed. Load order: §14.

## 3. Sub-agent write boundary

Sub-agents launched with the Agent tool cannot write to `.claude/`. They read and audit; the main session applies edits. "Edit denied on `.claude/...`" is this boundary working.

## 4. Task execution

Project tasks always run through the `task-execute` skill — never by reading a POML and implementing it directly. That skill owns context loading, rigor level, checkpoints, quality gates and completion.

| User says | Do |
|---|---|
| "work on task X", "continue with task X", "resume task X" | Invoke task-execute with task X |
| "continue", "keep going", "next task" | Find the first 🔲 in `TASK-INDEX.md`, invoke task-execute |
| "pick up where we left off" | Read `current-task.md`, invoke task-execute |

Independent tasks run in parallel as one message with several task-execute invocations.

## 5. Context and checkpoints

- **Checkpoint** (`context-handoff`): during task work, at the points `task-execute` Step 8.5 sets; at any time, after a deployment or live change, before a risky step, and before ending a session. Claude cannot see its own context usage; when the user or the harness reports it is high, checkpoint and then compact.
- **`current-task.md` is state, not history.** Each checkpoint rewrites it to describe the present (target ≤ 10 KB); never prepend a new block above old ones. Standing directives and gotchas go to the project `CLAUDE.md`, decisions to notes, narrative to the commit message.
- **When compacting, preserve:** the active task id and step, uncommitted files, open owner questions, and the exact next action. After compaction a `SessionStart` hook re-injects the project's `current-task.md` and its standing-directive and gotcha sections when it can identify the project (§16); otherwise re-read them yourself.

## 6. Escalation

Ask the human for: ambiguous or conflicting requirements; security-sensitive code (auth, secrets, encryption); ADR conflicts (§6.5); breaking changes to API contracts or schema; scope beyond the task. Format: 🔔 **Human Input Required** — situation, options, recommendation.

### 6.5 ADR conflicts

ADRs are guardrails, not immutable laws. When a legitimate need conflicts with an ADR rule, surface it and resolve it through exactly one path; silently complying when that produces a worse outcome is itself a failure:

| Path | When | Action |
|---|---|---|
| A — project exception | The ADR stays right in general; this project has a narrow reason to deviate | Document deviation + rationale in the project's `design.md`/`spec.md` ADR Tensions; cite it in the PR; code-review approves explicitly |
| B — amendment | The ADR is no longer right as written | Propose the amendment (concise + full); merge it before or with the code |
| C — comply | A compliant approach meets the need as well or better | Record the reasoning; proceed under the ADR |

There is no fourth path: no silent violation, no "fix it later". This is not a licence to bypass auth, security or compliance ADRs without explicit human sign-off. When invoking it, output 🔔 **ADR Conflict — Resolution Required** with: the ADR, the quoted rule, the conflict, the proposed path, the rationale, the impact, and the alternatives rejected. The human chooses the path. Enforcement points and examples: `adr-check` skill.

## 7. Task completion

`task-execute` Steps 10–11 own completion (POML status, `TASK-INDEX.md`, resetting `current-task.md`) and the project-close `/test-diet` gate for `090-wrapup-*` tasks.

## 8. Rigor levels

`task-execute` Step 0.5 assigns FULL / STANDARD / MINIMAL per task and declares it before Step 0. Any task that modifies `tests/**` or is tagged `testing`, `test-reset`, `deletion` or `integration-test` runs code-review + adr-check regardless of level.

### 8.5 Execution and review

- Planning (`design-to-spec`, `project-pipeline`) runs on the top tier (Opus / Fable). Execution defaults to Sonnet 5 at effort `high`; each POML's `<model-tier>` and `<effort>` can raise it (`task-create` Step 3.5.5b). Write POMLs for literal execution: scoped constraints, closed-set acceptance criteria including negative cases, exact files and the reference implementation to copy (`task-create`).
- **Review limits ceremony, never fixing.** Findings are classified fix-now (F1–F4) or known-limit (K1–K4); a known-limit class never holds a confirmed defect on a real path. Re-checks cover the fix diff and its direct callers and callees; one full adversarial-verifier pass per task by default (two for `auth`, `security`, `tenant-isolation`), more when a fix changes the approach, with a one-line reason in the task notes. Fixing continues until no F-class finding remains.
- **Every defect found is fixed in scope, or filed and reported to the operator** — whether the work caused it or only uncovered it (pre-existing code, another project's code, config, data). Escalate when fixes are not converging, not on a round count. This applies to task-execute Step 9.5 and to verifier loops in workflow scripts sessions write themselves.
- **Agent cost** (`.claude/constraints/agent-cost.md`): sub-agents default to Sonnet (`CLAUDE_CODE_SUBAGENT_MODEL`); request Opus/Fable only for planning and the one independent review per change set. Give each agent one scoped deliverable; after an idle pause start a fresh agent with a short brief instead of resuming (its cache has expired). Run one or two heavy fan-out projects at a time on one machine.

## 9. Security

- Never commit secrets (`.env`, `appsettings.local.json`, credentials, keys). Local secrets go in `config/*.local.json` (gitignored); production secrets in Azure Key Vault.
- Every API endpoint requires auth except `/healthz` and `/ping`.
- Before creating, seeding, rotating, deleting or purging any Key Vault secret, or changing an identity's credential order, read `.claude/constraints/provisioning.md` "KV credential lifecycle" and ADR-028 (A4, E-1–E-3). The rule is time-boxed and environment-specific; apply its current text, not a summary.

## 10. BFF hygiene (binding)

The BFF is the single backend for every client surface. Any task that adds endpoints, services, DI registrations, packages or background work to `Sprk.Bff.Api` (or to `Spaarke.Core` / `Spaarke.Dataverse`) follows `.claude/constraints/bff-extensions.md`. Its obligations — stated placement decision, the AI `PublicContracts` facade, publish-size delta measured against a fresh master build on every BFF-touching task, no new HIGH CVE, updated tests — load automatically from `.claude/rules/bff-hygiene.md` when you edit those folders (its items 1–6 keep the numbering cited elsewhere as "§10 bullet N"). Publish-size thresholds: ≥ +5 MB in one task needs explicit justification; ≥ 55 MB total triggers an architecture review; ≥ 60 MB is a hard stop. At design time, read `.claude/constraints/bff-extensions.md` first. Projects adding to the BFF include a Placement Justification section in `design.md`; projects touching the BFF **or `src/solutions/SpaarkeAi/**`** include a `<hot-path-declaration>` there (registry: `projects/INDEX.md`).

## 11. Component justification — default to reuse (binding)

Every new service, abstraction, interface, endpoint, DI registration, package, Dataverse column or file surface answers three questions, one sentence each:

1. **Existing** — what does it overlap with? (Check with Grep/Glob before saying "none".)
2. **Extension** — can an existing component be extended instead? If not, why (≤ 2 sentences)?
3. **Cost of doing nothing** — what concrete behaviour or contract fails without it? ("Scalability" or "flexibility" is not an answer.)

No concrete answer to 3 means scope creep: extend or drop it. Enforced at `project-pipeline` Step 2, `task-create` Step 3.5.6 and `code-review` Step 6.6. The rule applies to new surface **even when it is added inside an existing file** (a new endpoint in an existing `*Endpoints.cs`, a new registration in an existing `*Module.cs`); only pure modification of existing surface needs no justification.

### 11.5 Component complexity

Judge cohesion, not line count. A large, cohesive, single-responsibility file is fine (say so in the PR); decompose when responsibilities diverge. No LOC gate. Standard: `docs/standards/COMPONENT-COMPLEXITY.md`.

## 12. Build commands

| Action | Command |
|---|---|
| Build the BFF | `dotnet build src/server/api/Sprk.Bff.Api/` |
| Run tests | `dotnet test` |
| Format C# | `dotnet format` |
| PCF production build | `npm run build:prod` (not `npm run build` — FAILURE-MODES AP-1) |
| Node install in `src/solutions/*` | `npm install --legacy-peer-deps --no-audit --no-fund` (not `npm ci`; most lock files are stale) |

Full reference: `docs/procedures/testing-and-code-quality.md`.

## 13. Entry points

| Subsystem | Start here |
|---|---|
| BFF API | `src/server/api/Sprk.Bff.Api/Program.cs` |
| PCF controls | `src/client/pcf/{Control}/control/index.ts` |
| Code pages | `src/solutions/{Page}/src/main.tsx` |
| Dataverse write path (no plugins) | `src/server/api/Sprk.Bff.Api/Services/Dataverse/CoreAncestorResolver.cs` |
| AI pipeline | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` |
| Shared UI | `src/client/shared/Spaarke.UI.Components/src/index.ts` |
| Auth | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` |
| Background jobs | `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs` |

## 14. Context layers (load order)

| Layer | Contains | Load |
|---|---|---|
| `src/**` | Implementation — the source of truth | Always, before implementing |
| `.claude/patterns/` | Pointer files to code entry points | Per task |
| `.claude/adr/` | Concise ADR rules | Per task |
| `.claude/constraints/` | Topic constraint summaries | Per task |
| `.claude/rules/` | Path-scoped rules | Automatically, when editing matching files |
| `.claude/catalogs/` | AI scope + model catalog | JPS playbook work |
| `docs/architecture/` | Decisions and rationale | When you need the why |
| `docs/standards/` | Cross-cutting coding standards | Before implementing new code |
| `docs/guides/`, `docs/procedures/` | Operations; development workflow | Deploying, configuring, testing |
| `docs/data-model/` | Dataverse schemas | When touching Dataverse data |
| `docs/adr/` | Full ADR history | Rarely |

## 15. Rapidly changing platform topics

For Microsoft and AI platform topics where training data may be stale (Azure AI Foundry, Power Platform, Dataverse MCP, Office add-ins, SharePoint Embedded), use the `researcher` subagent (`.claude/agents/researcher.md`). It checks `knowledge/` first, then Microsoft Learn and official repos.

## 16. Hooks and permissions

Configured in `.claude/settings.json`:
- `PostToolUse` on Edit/Write runs `scripts/quality/post-edit-lint.sh`, which returns lint findings to the agent as `additionalContext` (advisory, best-effort within its 4-second linter timeout); `Stop` runs `scripts/quality/task-quality-gate.sh`.
- `SessionStart` with the `compact` matcher runs `.claude/hooks/reinject-project-state.ps1`, which re-injects the current project's `current-task.md` and its standing-directive and gotcha sections after compaction. It finds the project from the `work/<project>` branch or the `spaarke-wt-<project>` worktree folder.
- `env` and top-level keys set the sub-agent model, concurrency caps, workflow size guideline and auto-compact window (`.claude/constraints/agent-cost.md`).
- `permissions.ask` makes the human confirm Key Vault secret delete/purge/recover/restore and client-secret writes (§9), and `git stash pop/apply/drop/clear` — the stash stack is shared by every worktree, so a pop can apply another session's work.

Other enforcement runs in skills (`task-execute`, `code-review`, `adr-check`), CI (`.github/workflows/`) and `doc-drift-audit` at project transitions.

**Enforcement ladder.** A rule in prose drifts and competes for attention; a rule the build enforces reaches the agent at the line it just wrote. When a lesson, ADR rule, constraint or bug fix creates a rule, enforce it with the strongest mechanism that works: a **type** the compiler checks → a **lint rule** (returned to the agent after each edit by the `PostToolUse` hook, and run in CI) → an **ArchTest / source-scan guard** (`Stop` hook and CI) → a **hook or permission rule** → **prose**, only with a one-line reason it cannot be mechanised. Record it with the rule ("Enforced by: …"). A new guard ships with must-fire and must-not-fire controls and, when it finds existing violations, a ratchet baseline (new violations fail; known ones are listed and worked down). Hooks and permission rules stay narrow — under 5 seconds, no false positives — and prefer `ask` over `deny` for policies that change over time. Detail: `ai-procedure-maintenance` Checklist G.

## 17. Before you … read … (triggers)

When about to do the thing on the left, read the document on the right first. 🔒 marks a guard that holds even if the document is not opened. Full catalogue: `docs/INDEX.md`.

| Before you… | Read first |
|---|---|
| Add anything to `Sprk.Bff.Api` | `.claude/constraints/bff-extensions.md` (§10) |
| Add background work | `.claude/adr/ADR-052-workload-placement.md`. In the BFF: queue → ADR-004 `IJobHandler`, schedule → ADR-036 `IScheduledJob`; no new hand-rolled timer `BackgroundService` |
| Add a create/update path or a rule a record must satisfy on save | `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`, `.claude/adr/ADR-002-thin-plugins.md`. 🔒 No Dataverse plugins; one server-side owner per invariant; wizards preview only; security fails closed |
| Change the document create → profile → index pipeline | `docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md` |
| Wire a new AI capability or narrative-output consumer, or author an AI executor or playbook | `docs/guides/ai-guide-consumer-wiring.md`, `docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md`, `docs/guides/BUILD-A-NEW-NARRATIVE-OUTPUT-CONSUMER.md` |
| Add a capability that opens an Assistant surface; choose bubble/chip/card/tab | `docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md`, `docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md` |
| Build a workspace widget; embed `LegalWorkspaceApp` | `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`, `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`; embedding: `docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` |
| Change Compose save or read/reference | `.claude/adr/ADR-049-compose-shadow-document.md`, `docs/architecture/COMPOSE-WRITE-RESIDUAL-LOSS.md`, `docs/architecture/COMPOSE-READ-REFERENCE-FIDELITY.md`. 🔒 "Every save ends in a defined outcome" and "untouched blocks are preserved" are a pair — never trade one for the other; no text search in the write path. A new citation shape needs a case in `tests/fixtures/compose-citation-parity/cases.json` |
| Open a modal | `docs/standards/MODAL-DECISION-CRITERIA.md`, `docs/standards/MODAL-DESIGN-SYSTEM.md`, `.claude/patterns/ui/record-modal-selection.md` (ADR-050) |
| Choose `Xrm.WebApi` vs the BFF | `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` |
| Create an SPE container type | `docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md`. 🔒 Types are permanent, capped at 25 per tenant and cannot be deleted; creation is delegated-only |
| Provision or deploy a customer environment | `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`, `/provision-environment`, and §9's Key Vault rule |
| Create a new code page, or add the Navigator side pane to an entity | `docs/architecture/SPAARKE-SIDE-PANE-NAVIGATION.md`. Registering the Navigator (`ensureNavigatorSidePane()`) is a standard code-page build step |
| Touch field mapping or set-regarding | `docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md` |
| Build or configure a data grid | `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` |
| Touch Calendar components | `src/client/shared/Spaarke.Events.Components/README.md`. 🔒 Keep `src/solutions/CalendarSidePane/` working (unused now, may return) |
| Change chat attachment limits | `docs/standards/CHAT-ATTACHMENT-POLICY.md` |
| Write, change or delete tests | `docs/adr/ADR-038-testing-strategy.md`, `docs/standards/TEST-ARCHITECTURE.md` |
| Touch auth | `.claude/adr/ADR-028-spaarke-auth-architecture.md`, `.claude/constraints/auth.md`, `.claude/patterns/auth/spaarke-sso-binding.md` |
| Start a project | `/design-to-spec` → `/project-pipeline`; `docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md`; active projects: `projects/INDEX.md` |
| Find anything else | `docs/INDEX.md`, `.claude/skills/INDEX.md`, `.claude/adr/INDEX.md`, `.claude/FAILURE-MODES.md` |

## 18. Maintaining this file

Read the maintainer notes in the HTML comment at the top of this file's source before editing it.
