# Procedure-Surface Changelog

> **Forward-only from 2026-05-14.** No back-fill from history.

This file tracks changes to the agent-procedure surface — `.claude/skills/`, `.claude/agents/`, `.claude/settings.json`, `.claude/patterns/`, `.claude/constraints/`, `.claude/FAILURE-MODES.md`, and the root `CLAUDE.md`. Git history covers everything; this file is the **curated** view that a human (or future agent) can scan to answer "when did skill X change?" or "when did hooks last get fixed?" without bisecting commits.

Format follows [Keep a Changelog](https://keepachangelog.com/) conventions.

---

## How to maintain this

**Every PR that touches** `.claude/skills/`, `.claude/agents/`, `.claude/settings.json`, `.claude/patterns/`, `.claude/constraints/`, `.claude/FAILURE-MODES.md`, or the root `CLAUDE.md` **MUST add an entry to the `[Unreleased]` section below** before merge.

- One entry per logical change. Cite the commit SHA or PR number.
- Use the categories: **Added**, **Changed**, **Deprecated**, **Removed**, **Fixed**.
- "Bumped version" and trivial typo fixes can be omitted.
- When a project releases (a `work/<project>` branch merges to master), promote `[Unreleased]` to `[<project-name>] - <date>` and start a fresh `[Unreleased]`.

If you're not sure whether to add an entry, add one. Too granular is better than missing.

---

## [Unreleased]

### Changed (2026-07-01 spaarkeai-compose-r1 task 102 — ADR-013 Path B amendment)
- [`docs/adr/ADR-013-ai-architecture.md`](../docs/adr/ADR-013-ai-architecture.md) — new §"Amendment 2026-07-01 — Document-context invocation on `IInvokePlaybookAi` facade". Documents the widened facade contract (adds optional `userContext: string?` + `document: DocumentContext?` parameters, both defaulted, positioned after `cancellationToken` so existing 4-arg callers are unaffected). Motivating consumer: `spaarkeai-compose-r1` (Compose R1 drafting workspace). Boundary preserved — CRUD-side code STILL only injects `IInvokePlaybookAi` + `IConsumerRoutingService` (never AI-internal types). Reflection guard test updated with named allow-list for `Sprk.Bff.Api.Services.Ai.DocumentContext` (task 095). Signature change first shipped in tasks 095/096; SSE-mode consumer landed in task 097. Amendment filed via CLAUDE.md §6.5 Path B (amendment) — Path A (per-project exception) rejected because Compose is the first of many document-context consumers (Rewrite, Find Similar, Lookup References, downstream Matter/Communication/Insights consumers all inherit the widened facade cleanly).
- [`.claude/adr/ADR-013-ai-architecture.md`](adr/ADR-013-ai-architecture.md) — status updated to "Accepted (amended 2026-07-01)". Added two MUST rules: (1) use the new optional parameters for document-context invocation (no bypass to `IPlaybookOrchestrationService` allowed); (2) update the `PhaseAVerticalSliceTests.ADR013_InvokePlaybookAiFacade_DoesNotExposeAiInternalTypesInSurface` reflection guard's allow-list with a NAMED entry + citation when adding new types to the facade surface (silent bypass forbidden per CLAUDE.md §6.5).
- [`.claude/adr/INDEX.md`](adr/INDEX.md) — ADR-013 row updated: key constraint cites the amendment; status "Accepted (amended 2026-07-01)".
- **Enforcement of the CLAUDE.md §6.5 protocol in the field**: this is the first Path B amendment landed via the protocol added on 2026-06-29. Silent bypass was avoided; the reflection test's allow-list is a compile-time proof that the amendment was formally landed rather than tolerated.

### Added (2026-06-29 spaarkeai-compose-r1 — ADR Conflict Resolution Protocol governance)
- `CLAUDE.md` — new §6.5 "ADR Conflict Resolution Protocol (BINDING)". Introduces the three resolution paths for ADR conflicts: (A) project-scoped exception with documented rationale, (B) ADR amendment when context has changed, (C) pivot to comply when an ADR-compliant alternative exists. Establishes "silent compliance with a sub-optimal ADR is itself a failure mode" as the principle. Binding for ≥6 months from 2026-06-29.
- `.claude/skills/adr-check/SKILL.md` — new Step 5.5 "Surface Challenge Paths" + updated Output Format Violations block to display the three resolution paths alongside each violation. Reviewer now chooses intentionally instead of defaulting to silent compliance.
- `.claude/skills/code-review/SKILL.md` — Step 6 ADR Compliance Check rewritten to accept reasoned exceptions documented in PR description / `spec.md` "ADR Tensions" section. Silent violations still Critical; documented Path A exceptions = Warning with reviewer judgment. Cross-links CLAUDE.md §6.5.
- `.claude/skills/task-execute/SKILL.md` — Step 9.5 quality gates updated: ADR violations no longer default to "STOP, must fix" silent-comply loop. Agent applies CLAUDE.md §6.5 protocol (path A/B/C choice with user escalation for A and B).
- `.claude/skills/design-to-spec/SKILL.md` — both spec.md templates (inline Step 4 + standalone bottom template) extended with mandatory "ADR Tensions" section (table format: ADR / rule / conflict / path / rationale). Default content if no tensions: explicit "No ADR tensions surfaced" statement.
- `.claude/skills/project-pipeline/SKILL.md` — Step 1 spec validation now requires "ADR Tensions" section; new Step 1.7 processes declared tensions before Step 2 resource discovery (validates rationale concreteness, flags Path B amendment prerequisite, summarizes path counts).
- **Driver**: design conversation during `spaarkeai-compose-r1` surfaced governance gap — agents and humans default to silent ADR compliance even when path A (exception) or path B (amendment) would produce a better technical outcome. User explicit ask: "if we have surfaced a legitimate exception or required modification to an ADR then we MUST surface this conflict and resolve it. We cannot have our ADRs drive us to sub-optimal solutions." This protocol formalizes the resolution.
- Reinforcement points: design-time (`design-to-spec` + `project-pipeline`), code-review-time (`code-review`), task-execute-time (`task-execute` Step 9.5), and ad-hoc (`adr-check`). Five enforcement layers ensure the principle isn't a doc-only addition.

### Added (2026-06-29 spaarke-ai-platform-unification-r7 Wave 6 task 068 — root CLAUDE.md §17 pointer to consumer-wiring guide; Wave 6 task 064 — bff-extensions §G rewrite; Wave 7 — jps-* skill rewrites)
- Root [`CLAUDE.md`](../CLAUDE.md) §17 Pointers — added row for [`docs/guides/ai-guide-consumer-wiring.md`](../docs/guides/ai-guide-consumer-wiring.md) (created Wave 6 task 067 per FR-31). §17 row "BFF additions governance" annotated with §G rewrite date.
- [`.claude/constraints/bff-extensions.md`](constraints/bff-extensions.md) §G "Action / Node / Playbook Config Boundary" — REWRITTEN for R7 single-hop dispatch (FR-29). New 4-Home table reflects dispatch on the NODE (Home C) + Action as reusable prompt template (Home A, dispatch removed) + decorative `sprk_analysisactiontype` lookup table (Home D). New "Binding MUST rules" + "Binding MUST NOT rules" sections explicitly enumerate dropped columns + structural-fallback delete + categorization-only stance. Hot-Path Declaration section RENUMBERED §G → §H to fix duplicate-§G ambiguity introduced by sibling project ci-cd-unit-test-remediation-r1 landing.
- [`.claude/skills/jps-action-create/SKILL.md`](skills/jps-action-create/SKILL.md) — Wave 7 task 070 (FR-32). Step 1.5 Config-Home Guard table updated; Step 5.5 MCP verify drops `_sprk_actiontypeid_value`; new "R7 dispatch model" section with §3.1 WHY citation.
- [`.claude/skills/jps-playbook-design/SKILL.md`](skills/jps-playbook-design/SKILL.md) — Wave 7 task 071. Step 1.5 item 3 replaces 3-tier lookup ladder with single-hop. Step 10 verify-deploy query uses `sprk_executortype`. New 33-executor catalog by tier + Executor-Type-FIRST workflow.
- [`.claude/skills/jps-playbook-audit/SKILL.md`](skills/jps-playbook-audit/SKILL.md) — Wave 7 task 072. Step 2 query updated; Check 3.5 citation corrected; new Check 3.6 enumerates 7 R7 drift patterns A-G mirroring Wave 5 task 050 CSV shape.
- [`.claude/skills/jps-validate/SKILL.md`](skills/jps-validate/SKILL.md) — Wave 7 task 073. Step 7.5 CHECK 25 marked LEGACY; CHECK 26 (structural-fallback) DELETED; new Step 7.6 R7-V-01-V-04 + 6 LEGACY-* drift flags; new Step 7.7 typed-config schema check against Wave 3 BFF endpoint.
- [`.claude/skills/jps-scope-refresh/SKILL.md`](skills/jps-scope-refresh/SKILL.md) — Wave 7 task 074 (FR-33). Two-authoring-surfaces table updated (Node Type OptionSet → Executor Type Choice Set, 33 values). C# enum rename `ActionType` → `ExecutorType` applied throughout. Operational behavior unchanged (terminology touch-up only).

Commits:
- `d79432f9e` — Wave 4 schema drops (043+044, FR-03+FR-04)
- `7f28da008` — Wave 4 AnalysisActionService cleanup (046)
- `dd95dff69` — Wave 4 publish-hygiene gate PASS (047)
- `79ced1c6a` — Wave 8 form default (081, FR-21)
- `6e5e070e3` — Wave 8 placeholder schemas (085, FR-23)
- `2a5ff9e5a` — Wave 8 promptSchemaOverride wiring (087, FR-25)
- `e020c25e4` — Wave 7 jps-* skill rewrites + smoke test (070-075, FR-32/33)
- (this commit) — Wave 6 tasks 064 + 068

### Added (2026-06-25 smart-todo-r4 R4-112 — PCF `noAposStringType` XSD failure mode)
- `.claude/skills/pcf-deploy/SKILL.md` — new row in Failure Modes & Recovery table for `noAposStringType` XSD validation failure (Dataverse PCF import rejects apostrophes in `description-key` attribute values). Discovered during RegardingResolver v1.2.0 deploy (commit 5b7a62812) — `entity's` and `'sprk_todo'` in description-key blocked the import. Comments are fine (XSD skips them); only attribute values matter. Burned ~10 min on first import attempt; this entry saves the next operator.

### Added (2026-06-25 spaarke-ai-platform-chat-routing-redesign-r1 Phase 5R Wave 5-C — ADR-037 Multi-Node Output Composition)
- `.claude/adr/ADR-037-multinode-output-composition.md` — concise ADR (~115 lines). Decision: introduce `NodeType.DeliverComposite` + per-section SSE streaming (`section_started` / `section_data` / `section_completed` keyed by section NAME, not schema position) + FE widget rework consuming `sections: Record<string, SectionState>`. Replaces the legacy 5-coordination-point schema-aware widget model (schema-on-action + schema-aware widget + ordinal indexing + implicit linkage + author-side-only contract) with a 2-point pattern (section name + section state). Legacy `FieldDelta` path preserved via runtime event-type detection for unmigrated playbooks. Chat-destination playbooks STAY single-action (composition adds no value for one streamed paragraph). MUST / MUST NOT rules + backward-compat invariants + reference implementation table (tasks 114R/114a/114b/118R).
- `docs/adr/ADR-037-multinode-output-composition.md` — full ADR with the 5-coordination-point fragility analysis (with examples of how rename / reorder broke rendering silently), 4 alternatives considered + rejection reasons, consequences (positive / negative / neutral), per-playbook migration runbook, open questions (section-name versioning policy, per-section regeneration UX). Driver: 2026-06-24 user design conversation surfaced architectural frailty in legacy widget; Phase 5R Wave 5-C is the rework.
- `.claude/adr/INDEX.md` — new ADR-037 entry placed after ADR-036.

### Added (2026-06-23 spaarke-devops-project-tracking-r1 — GitHub-native portfolio tracking)
- **9 new `/devops-*` skills** — `.claude/skills/devops-{portfolio-setup,epic-create,idea-create,idea-promote,project-start,project-register,project-sync,portfolio-status,project-archive}/SKILL.md`. Lifecycle: capture → promote → start → sync → archive. All idempotent (NFR-04). `/devops-project-start` is THE BLESSED HANDOFF (D-13) — the one canonical bridge from a Project Issue to a local worktree.
- **9 hook injections into existing skills** — `design-to-spec`, `project-pipeline`, `task-create`, `task-execute`, `context-handoff` (HIGHEST VALUE per spec §6.2), `worktree-setup`, `worktree-sync`, `repo-cleanup`, `merge-to-master` each gained a "Portfolio Hook" appendix section (additive only per NFR-03 — existing contracts unchanged). Hooks call `/devops-project-sync` (or `register`/`archive` where appropriate) at end of host skill execution.
- **GitHub Project #2 schema** — `Type=Project` option added (preserving 6 existing); 6 new custom fields (Project Type, Worktree Path, Project Folder, Task Count, Tasks Completed, Project Status); 7 labels (epic, project, backlog, worktree:active/archived, on-hold, cancelled); 3 issue templates at `.github/ISSUE_TEMPLATE/{epic,project,idea}.yml`; 12 initial Epic Issues #421–#432.
- **`.claude/skills/INDEX.md`** — 9 new rows for `/devops-*` family.
- **`CLAUDE.md` §17 Pointers** — new row for portfolio tracking + DevOps procedures.
- **`docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md`** — extended with "Portfolio Integration" section (FR-29): Step 0 idea capture, Epic ↔ Project mechanics, Idea promotion paths, BLESSED HANDOFF walkthrough, auto-hook behaviors table, 9-skill command reference, portfolio-specific troubleshooting.
- **`docs/procedures/AI-CODING-PROCEDURES-GUIDE.md`** — extended with "Portfolio Scenarios" section (FR-30): 7 new scenarios in existing tri-section pattern (capture idea / promote / update status / close project / see what's running / package ideas / stakeholder view).

### Critical lesson surfaced
- **`updateProjectV2Field` reassigns option IDs** — empirically verified during Phase 1 task 001 (2026-06-23). The GitHub GraphQL mutation REPLACES the full option list AND generates new internal option IDs for every option, even unchanged ones. Items currently bound to old option IDs lose their references. The `/devops-portfolio-setup` skill MUST implement snapshot → mutate → reconcile pattern. Logged in `projects/spaarke-devops-project-tracking-r1/notes/spikes/phase1-task001-execution-log-2026-06-23.md`.

### Added (2026-06-22 spaarke-ai-platform-chat-routing-redesign-r1 — Component Justification governance)
- `CLAUDE.md` — new §11 "Component Justification — Default to Reuse (BINDING)" + renumber §11→§12 through §17→§18. Introduces the three-question template (Existing / Extension / Cost-of-doing-nothing) for every NEW service / abstraction / interface / endpoint / DI registration / package / Dataverse column / file surface. Enforcement points: spec authoring (project-pipeline), plan WBS (task-create Step 3.5.6), code review (code-review Step 6.6). Anti-patterns documented from real R1 examples (LegalWorkspace dead-code misreading, sprk_playbookcode field-choice error, 8-tool surface overreach). Driver: chat-routing-redesign-r1 Q&A surfaced three scope-creep failures that the rule would have caught at authoring time.
- `.claude/skills/task-create/SKILL.md` — new Step 3.5.6 "Component Justification Gate (REQUIRED per CLAUDE.md §11)". Requires `<justification>` POML element on every new-component task; decision logic for REWRITE-as-extension vs DEMOTE/DROP vs PROCEED. Scope explicitly excludes pure modifications to existing files.
- `.claude/skills/code-review/SKILL.md` — new Step 6.6 "Component Justification Check (Universal — CLAUDE.md §11)". Extends Step 6.5 (BFF Hygiene) from BFF-only to all new components. Verifies the three answers are concrete (cite file:line, name a concrete failure mode); flags hollow / boilerplate justifications as WARNINGs.

### Changed (2026-06-21 spaarke-ai-platform-chat-routing-redesign-r1 — ADR-030 v2 `memory` channel amendment)
- `.claude/adr/ADR-030-pane-event-bus.md` — v2 amendment adds 5th channel `memory` to the PaneEventBus closed union. New `MemoryPaneEvent` interface with 5 initial discriminants: `promotion_pending`, `promotion_resolved`, `fact_promoted`, `pin_added`, `pin_removed`. Channel union expanded from 4 → 5; sixth channel still requires successor ADR. Amendment Record section appended documenting context (chat-routing-redesign-r1 6-tier memory subsystem), constraints preserved (ADR-015 tier-1 safety on payloads — deterministic IDs + 80-char summaries only; tenant scope via subscriber context), required implementation updates (PaneEventTypes.ts extension; ContextPane subscriber wiring; MatterMemoryPromotionService dispatch site). Driver: chat-routing-redesign-r1 architecture §6.4 promotion approval workflow needed dedicated semantic channel instead of namespaced `workspace.*` workaround.
- `docs/adr/ADR-030-pane-event-bus.md` — full ADR amended in lockstep with concise version. Decision section §1 expanded from 4 → 5 channels; "fifth channel" references throughout updated to "sixth channel"; verification grep commands updated; AI-Directed Coding Guidance updated with new guidance for memory-domain events; Amendment History section appended (v2 record). Both ADR versions stay in sync.

### Added (2026-05-26 R4 Phase 1 F-3 — publish-size per-task verification rule)
- `.claude/constraints/azure-deployment.md` — new "BFF Publish-Size Per-Task Verification Rule (NFR-01)" section. Binding rule: every BFF-touching task MUST run `dotnet publish` + report compressed size + diff vs prior baseline. Ceiling: ≤60 MB (spec NFR-01). Current baseline ~45.65 MB. Escalation thresholds: ≥+5 MB single-task → justification; ≥55 MB cumulative → architecture review; ≥60 MB → HARD STOP. Driver: R4 NFR-01 / F-3 (operationalizes ADR-029).
- `CLAUDE.md` (root) §10 item 4 — strengthened from "verify if adding NuGet packages" → "verify on EVERY BFF-touching task" with explicit `dotnet publish` command, absolute-size + diff reporting requirement, and escalation thresholds. Cross-links to azure-deployment.md.

### Changed (2026-05-26 sdap-bff-api-remediation-fix Phase 5 wrap-up)
- `docs/guides/auth-deployment-setup.md` §3 expanded with new §3.5 covering 25+ App Settings discovered during Phase 5 demo prep beyond the original "8 settings" inventory (MI identity disambiguation 5 keys + Cosmos persistence + AgentService placeholders + feature-flag=false patterns + email subsystem).
- `docs/guides/auth-deployment-setup.md` §7c — drop `-UserPrincipalName` from `Connect-ExchangeOnline` example to avoid the mismatch failure mode discovered in Phase 5 (operator's browser-selected account vs param).
- `.claude/constraints/azure-deployment.md` Publish & Packaging — added linux-x64 RID + sourcemap exclusion MUST rules; baseline compressed size updated 60 → 45 MB (Phase 5 measured 45.65 MB post-Outcome-A).
- `.claude/FAILURE-MODES.md` extended with 4 new entries (AP-4 dev/demo bundle drift `/api` bug; G-5 Dataverse Application User registration; G-6 `Connect-ExchangeOnline -UPN` mismatch; G-7 Git Bash MSYS path mangling).
- `.claude/adr/ADR-007-spefilestore.md` — cross-reference added to refined ADR-013 + the new `Services/Ai/PublicContracts/` facade as parallel example of facade-over-SDK pattern.
- `.claude/adr/ADR-010-di-minimalism.md` — Phase 5 baseline note (265 registrations; +4 from facade is within expected delta).
- `docs/architecture/AI-ARCHITECTURE.md` — new "AI Public Contracts Facade Boundary" section documenting the 4 facade interfaces + 5 documented AI-API-surface exceptions + handler relocation to `Services/Ai/Jobs/`.
- `docs/architecture/AUTH-AND-BFF-URL-PATTERN.md` — cross-env consistency callout + checklist item for `sprk_BffApiBaseUrl` format.
- `docs/architecture/INFRASTRUCTURE-PACKAGING-STRATEGY.md` — new §5 BFF Binary Packaging covering linux-x64 RID + sourcemap exclusion + transitive override pattern + measured baselines.
- `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` — added MI + Cosmos DB rows to per-environment resources table.
- `docs/guides/CUSTOMER-DEPLOYMENT-GUIDE.md` — resolved `sprk_BffApiBaseUrl` `/api` suffix contradiction with auth-deployment-setup.md.
- `docs/guides/COMMUNICATION-DEPLOYMENT-GUIDE.md` — added full 17-setting email inventory discovered in Phase 5 (9 Communication + 8 EmailProcessing).
- `docs/guides/DATAVERSE-AUTHENTICATION-GUIDE.md` — added MANDATORY Application User registration section with full Web API walkthrough.
- `docs/guides/PCF-DEPLOYMENT-GUIDE.md` — added URL construction convention section documenting `getBffBaseUrl()` host-only pattern.
- `docs/guides/AI-DEPLOYMENT-GUIDE.md` — added mandatory Cosmos DB infrastructure section (account + DB + 5 containers + RBAC + App Settings).

### Added (2026-05-26)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/` facade per refined ADR-013 — 4 interfaces (`IBriefingAi`, `IInvoiceAi`, `IRecordMatchingAi`, `IWorkspacePrefillAi`) + 4 implementations. 10 CRUD consumers migrated (Finance, Workspace, Jobs, Dataverse, Filters, Endpoints); 5 documented AI-API-surface exceptions (Chat/Playbook/Builder/Agent endpoints + auth filter). 92% reduction in direct AI injection in CRUD code.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/` (5 files relocated from `Services/Jobs/{Handlers,}` per FR-E3): AppOnlyDocumentAnalysisJobHandler, BulkRagIndexingJobHandler, EmailAnalysisJobHandler, ProfileSummaryJobHandler, EmbeddingMigrationService.
- LegalWorkspace `/api` prefix fix (3 sites: `FilePreviewDialog.tsx:320`, `closureService.ts:63`, `provisioningService.ts:81`) — commit `2561ce37`. Deployed to both dev + demo `sprk_corporateworkspace` web resource.

### Changed
- **`code-review` + `adr-check` now enforce CLAUDE.md §10 BFF Hygiene + `bff-extensions.md`** — closes the gap where the binding §10 rule was loaded as context but never explicitly checked. `adr-check` Step 2's quick-reference table adds ADR-013 (refined 2026-05-20); new Step 2.5 conditionally loads `bff-extensions.md` and applies its 5-rule pre-merge checklist when changed files touch `Sprk.Bff.Api/`, `Spaarke.Core/`, or `Spaarke.Dataverse/`. `code-review` Step 6 adds ADR-013 to its CRITICAL ADRs list; new Step 6.5 runs the same §10 checklist with explicit severity assignment (missing Placement Justification → Critical; new direct CRUD→AI dep → Critical; new HIGH-severity CVE → Critical). Both edits cite `bff-extensions.md` as the single source of truth — zero duplication of rule content.

### Added
- `.claude/AUDIT-FINDINGS-CLAUDEMD.md` — Phase 3a audit of root `CLAUDE.md` against community best practices + Phase 0 inventory (75-section sign-off table + proposed skeleton + open questions). Commit `0c11cd43`.
- `.claude/archive/2026-05-17/CLAUDE.md` — preserved copy of the 1190-line OLD root `CLAUDE.md` before Phase 3b rewrite (reversibility per NF-1).
- **Auth v2 pre-flight** — STOP banners on 5 partially-superseded docs (`.claude/patterns/auth/spaarke-sso-binding.md`, `.claude/patterns/auth/token-caching.md`, `.claude/constraints/auth.md`, `docs/architecture/AUTH-AND-BFF-URL-PATTERN.md`, `docs/architecture/sdap-auth-patterns.md`) + full-deprecation banners on 2 DEPRECATED-* files. Each banner names what stays canonical (INV-1..INV-7, server-side OBO, `buildBffApiUrl()`, etc.). PF-4..PF-10. Commit `281f7210`.
- **Auth v2 pre-flight** — Pointer row in root `CLAUDE.md` §15 directing all agents (any worktree) to `.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md` as the active auth v2 design until ADR-027 ships. PF-12. Commit `5b04b6ff`.

### Changed
- **Root `CLAUDE.md` rewritten** from 1190 → 264 lines (78% reduction) per Phase 3b. Applies community best practices: project-specific operational rules only; tutorials/marketing/long reference tables moved out; pointer-heavy structure. User-locked decisions: §1 identity updated to "enterprise AI-directed legal operations intelligence platform"; §11 System Entry Points + §12 Context Layer Hierarchy kept inline (user judgment); §13 Knowledge Repository section added pointing at `spaarke/knowledge/` + `researcher` subagent for rapidly-evolving Microsoft platform topics; Rigor Level template kept inline; Hooks: Current Guidance compressed to one paragraph.
- 5 internal contradictions resolved in the rewrite (Hooks System vs Current Guidance; trigger phrases in 2 places; Before-Starting-Work vs Working-Checklist; etc.).
- **Auth v2 pre-flight** — 11 in-scope references updated to point at the new `DEPRECATED-*` filenames with "⛔ DEPRECATED — superseded by Spaarke Auth v2" markers: `.claude/patterns/auth/INDEX.md`, `.claude/patterns/INDEX.md`, `.claude/constraints/auth.md`, `.claude/patterns/auth/spaarke-sso-binding.md`, `.claude/patterns/webresource/{code-page-wizard-wrapper.md, full-page-custom-page.md}`, `.claude/skills/code-page-deploy/SKILL.md`, `docs/architecture/sdap-auth-patterns.md`, `CROSS-REFERENCE-MAP.md`, `src/solutions/SpaarkeAi/src/App.tsx`, `src/solutions/Reporting/{main.tsx, services/authInit.ts, config/runtimeConfig.ts, config/reportingConfig.ts}`. Historical `projects/*` references, `.claude/archive/`, and the audit doc's rename-action narrative left intentionally unchanged. PF-3. Commit `c2198007`.

### Deprecated
- **Auth v2 pre-flight** — Two fully-superseded auth pattern docs renamed with `DEPRECATED-` prefix so the filename itself is a stop signal in Grep/Glob output:
  - `.claude/patterns/auth/msal-client.md` → `.claude/patterns/auth/DEPRECATED-msal-client.md`
  - `.claude/patterns/auth/spaarke-auth-initialization.md` → `.claude/patterns/auth/DEPRECATED-spaarke-auth-initialization.md`
  Both files will be removed when v2 ships (Workstream F4, task 094). PF-1, PF-2. Commit `c2198007`.

### Removed
- The 22 extract-candidate sections totaling ~720 lines from old `CLAUDE.md`. Content remains preserved in `.claude/archive/2026-05-17/CLAUDE.md`. Topics removed: detailed Adaptive Thinking tutorial, Permission Modes tutorial, Hooks System tutorial, Headless Mode, Agent Teams (experimental), Component Skills note (now in `.claude/skills/INDEX.md`), Trigger Phrases table, Slash Commands table, Coding Standards code samples (in `docs/standards/`), Repository Structure tree (in `README.md`), ADR summary table (in `.claude/adr/INDEX.md`), Quality Gates with Hooks (feature not configured), and dated/duplicate sections.

### Fixed
- N/A — Phase 3a/3b are restructuring; no behavioral fixes in this scope.

### Verified
- **Auth v2 pre-flight** — `projects/spaarke-auth-v2-and-hardening/CLAUDE.md` "🚨 ACTIVE AUTH V2 REFACTOR — DO NOT REGRESS" section cross-checked against audit §8.2 Layer 3 (PF-11) requirements. All MUST/MUST NOT bullets present plus extras (/debug endpoint ban, plain-text secret ban, INV-1..INV-8 preservation). No edits required. PF-11. Commit `f58317b0`.

### Retirement note
- All "Auth v2 pre-flight" entries above (PF-1..PF-13) are transitional. They will be retired during Workstream F (Engineering canonical docs): F1 ships ADR-027, F2 partial-rewrites `spaarke-sso-binding.md`, F3 ships `docs/guides/auth-deployment-setup.md`, F4 deletes the `DEPRECATED-*` files and removes the STOP banners + project CLAUDE.md prohibition + root CLAUDE.md pointer row. See `.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md` §8.4–§8.5.

---

## [ai-procedure-quality-r1] - planned for 2026-05-XX

---

## [ai-procedure-quality-r1] - planned for 2026-05-XX

> Entry will be promoted from `[Unreleased]` when the project's PR #294 merges. The deliverables below are the planned set.

### Added
- `.claude/agents/researcher.md` — Opus, effort: high researcher subagent for deep-dive Microsoft platform investigation; accumulates findings via project memory (`MEMORY.md`). Per design.md Directive 1. (Task 010)
- `.claude/skills/_template/SKILL.md` — canonical skill scaffold enforcing the 7 best practices; new skills clone this; existing skills are measured against it during Phase 2a audit. (Task 011)
- `.claude/CHANGELOG.md` — this file. Forward-only convention. (Task 012)
- `.claude/FAILURE-MODES.md` — repo-level catalog of cross-cutting failure patterns. 4 inaugural entries derived from 2026-05-14 incidents. (Task 013)
- `.claude/archive/` directory with date-organized subdirectory convention; reversibility-first removal pattern. (Task 014)
- `scripts/quality/Validate-SkillReferences.ps1` — Light reference check across all 49 skills (file paths, URLs, skill names). Runs in CI; <10s. (Task 065)
- `scripts/quality/Find-SkillReferenceDrift.ps1` — 7-surface drift detector; catches broken refs after rename/split/merge. (Task 066)

### Changed
- Root `CLAUDE.md` rewritten to the tiered target (<200 lines). Reference content moved to subdirectories. The pre-rewrite version is preserved in `.claude/archive/2026-05-14/CLAUDE.md`. (Phase 3b deliverable)
- Multiple skills refined per `.claude/AUDIT-FINDINGS-SKILLS.md`. Specific refactors listed under each skill in the per-skill section of the audit findings. (Phase 2b deliverable)

### Removed
- Skills audit-recommended-and-approved for removal (specific list determined at Human Gate 1). Folders archived to `.claude/archive/2026-05-14/skills/<name>/`, not deleted from disk. (Phase 2b deliverable)

### Fixed
- N/A — Phase 0 inventory surfaced existing issues (5 failing workflows, 3 PCFs with wrong `build:prod`, etc.) but their fixes are in separate scope from this project.

---

*Established 2026-05-14 by project `ai-procedure-quality-r1` (task 012). See [.claude/archive/README.md](archive/README.md) for the reversibility convention referenced above.*
