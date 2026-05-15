# Inventory: Root `CLAUDE.md`

> **Project**: ai-procedure-quality-r1
> **Task**: 002-inventory-claudemd
> **Wave**: Phase 0, Wave 0-A (parallel inventory)
> **Source**: `c:\code_files\spaarke\CLAUDE.md`
> **Generated**: 2026-05-14
> **Status**: Read-only baseline. The rewrite is Phase 3b.

---

## Summary Statistics

| Metric | Value |
|---|---|
| Total lines | **1190** |
| Front-matter / blockquote (lines 1-10) | 10 |
| Trailing blank + footer (line 1190) | 1 |
| Body content | 1179 |
| H2 sections (`##`) | 17 |
| H3 sections (`###`) | 47 |
| H4 sections (`####`) | 4 |
| Tables (markdown `|...|`) | 27 |
| Fenced code blocks | 24 |
| Target after rewrite (per spec) | **<200 lines** |
| Reduction required | **~83%** (need to remove or extract ~990 lines) |

**Counted "extract candidate" sections**: **22 of 64** H2/H3/H4 sections marked Yes/Likely-Yes for extraction. Combined extract candidates ≈ **~720 lines** (61% of the file) — which gets us close to the <200-line target.

---

## Top 5 Largest Sections (by line count)

| Rank | Section | Lines | Length | Note |
|---|---|---|---|---|
| 1 | `### Trigger Phrases → Required Skills` | 693-725 | 33 | One row per skill; pure tool listing |
| 2 | `### Adaptive Thinking & Effort Control (Opus 4.6)` | 14-54 | 41 | Model-specific tutorial; semi-volatile |
| 3 | `### Claude Code Model and Permission Settings` | 55-148 | 94 | Largest single block; includes 4 fenced examples |
| 4 | `### Trigger Phrases → Required Skills` + adjacent `### Auto-Detection Rules` (726-752) | 693-752 | 60 | Combined "skills routing" block is the longest contiguous unit |
| 5 | `### Hooks System` | 151-186 | 36 | Tool listing + example JSON; deeply duplicative with `### Hooks: Current Guidance` (833-843) |

(Note: ranks 1 and 4 overlap because the "Trigger Phrases" table is itself ~33 lines and is immediately followed by the equally-table-heavy "Auto-Detection Rules". I treat them as one effective unit for the rewrite.)

---

## Hard Target: Sections that MUST be preserved verbatim (always-in-context operational rules)

These are the load-bearing rules that justify CLAUDE.md being read every session. They should stay (possibly tightened) in the rewritten file. **Total of these = ~165 lines**, which fits under the 200-line budget.

| Section | Lines | Why it must stay |
|---|---|---|
| Front-matter / Purpose blockquote | 1-10 | Identifies the file's role |
| `### 🚨 MANDATORY: Task Execution Protocol for Claude Code` | 423-490 | The single most-violated rule per the project's failure-mode evidence; MUST stay inline |
| `## 🚨 AI Execution Rules (Critical)` — Context Management (514-523) | 514-523 | Numeric thresholds the agent acts on every turn |
| `### Proactive Checkpointing (MANDATORY)` | 525-541 | Numeric thresholds + behavior |
| `### Human Escalation Triggers` | 565-574 | Behavior rule for ambiguity |
| `### Task Completion and Transition` | 576-587 | State-machine transition the agent owns |
| `## Task Execution Rigor Levels` (overview + decision tree) | 591-648 | Decision tree the agent applies per task |
| `### Sub-Agent Write Boundary (IMPORTANT)` | 820-831 | Permission boundary that explains an expected error |
| `### Read Code First, Docs Second` (1-paragraph version) | 802-818 | Source-of-truth principle |
| Module-Specific Instructions pointer | 1181-1186 | Tells agent where subordinate CLAUDE.md files live |

Everything else is candidate for extraction, deletion, or compression.

---

## Section-by-Section Inventory

Legend for **Classification** column:
- **Rule** = operational rule the agent must apply every turn (KEEP)
- **Decision-tree** = conditional logic the agent applies per task (KEEP, possibly compressed)
- **Pointer** = points to a file/skill the agent should load (KEEP if frequently used; extract if rarely)
- **Reference-table** = lookup data (mostly EXTRACT)
- **Tutorial** = explanatory prose / examples (mostly EXTRACT)
- **Background** = "why" context, not actionable (EXTRACT)
- **Duplication** = restates content already in skill/doc (DELETE or REPLACE WITH POINTER)
- **Code-example** = embedded code samples (EXTRACT to standards/patterns)
- **Stale/Speculative** = describes features not in current settings or experimental (DELETE or EXTRACT to "future")

Legend for **Extract?**:
- **Yes** = clear extract candidate
- **No** = must stay in CLAUDE.md
- **Borderline** = could go either way; flag with rationale (Honesty rule)

| # | Lines | Section (Heading) | Lines | Classification | Extract? | Notes / Rationale |
|---|---|---|---:|---|---|---|
| 0 | 1-10 | (Front-matter blockquote: title + Last Updated + Purpose) | 10 | Rule | **No** | Identifies file role; keep but tighten the audit-stamps to one line. |
| 1 | 12-13 | `## Development Environment` (heading only) | 2 | Structural | **No** | Heading only — keeps anchor for the kept Adaptive Thinking subsection if any survives. |
| 2 | 14-54 | `### Adaptive Thinking & Effort Control (Opus 4.6)` | 41 | Tutorial + Reference-table | **Yes** | Model-version-specific tutorial. Effort guidance is useful but volatile (changes with each Opus minor version). **Extract** to `docs/guides/claude-code-thinking-and-effort.md`. |
| 3 | 55-148 | `### Claude Code Model and Permission Settings` | 94 | Tutorial + Reference-table + Code-example | **Yes** | Largest block. 6 code examples, 3 tables, repeated `--dangerously-skip-permissions` warnings. Settings live in `.claude/settings.json` (single source of truth). **Extract** the tutorial to `docs/guides/claude-code-permission-modes.md`. Keep only: 1-line "settings live in .claude/settings.json; see guide." |
| 4 | 149-150 | `## New Claude Code Features (February 2026)` | 2 | Structural / dated | **Yes (whole block)** | Dated header — already aging. Subsections below either belong elsewhere or are duplicative. |
| 5 | 151-186 | `### Hooks System` | 36 | Tutorial + Reference-table | **Yes** | Hooks are NOT configured (see lines 833-843, which contradicts the implication of "Spaarke use cases"). **Extract** to `docs/guides/claude-code-hooks-reference.md`. Keep a 1-line pointer in the "Hooks: Current Guidance" section that's already in Architecture Discovery. |
| 6 | 187-200 | `### Headless / Non-Interactive Mode` | 14 | Tutorial | **Yes** | CI/CD-specific. Not used every session. **Extract** to `docs/guides/claude-code-headless-mode.md` or fold into ci-cd docs. |
| 7 | 202-240 | `### MCP Server Integration` | 39 | Tutorial + Reference-table | **Yes** | 12-tool table is reference data. Already pointed to `docs/guides/DATAVERSE-MCP-INTEGRATION-GUIDE.md` (line 239). Keep that pointer (2 lines); **extract** rest. |
| 8 | 241-249 | `### Session Management` | 9 | Reference-table | **Yes** | Built-in slash commands; documented in Claude Code itself. Belongs in a slim "Claude Code commands cheatsheet" guide. |
| 9 | 251-257 | `### Cost & Token Awareness` | 7 | Background | **Yes** | Bullet list reminders. **Extract** to the effort/thinking guide. |
| 10 | 260-263 | `## Agent Teams (Experimental)` (intro paragraph) | 4 | Background | **Borderline** | "Experimental" flag is honest; whether to keep depends on adoption. **Recommendation**: Extract the whole Agent Teams section to `docs/guides/claude-code-agent-teams.md` and keep one line in CLAUDE.md: "Agent Teams are experimental — see guide before using." |
| 11 | 264-279 | `### Enabling Agent Teams` | 16 | Tutorial + Code-example | **Yes** | Configuration boilerplate. **Extract**. |
| 12 | 280-292 | `### When to Use Agent Teams vs. Subagents` | 13 | Decision-tree + Reference-table | **Borderline** | Genuinely useful decision-table. **Recommendation**: Keep a 3-line summary inline; move full table to agent-teams guide. |
| 13 | 293-320 | `### Agent Teams for Spaarke Projects` | 28 | Tutorial (examples) | **Yes** | Three worked examples. **Extract** all to agent-teams guide. |
| 14 | 321-332 | `### Display Modes` | 12 | Reference-table | **Yes** | Configuration detail. **Extract**. |
| 15 | 333-336 | `### Delegate Mode` | 4 | Reference | **Yes** | One paragraph. **Extract** to agent-teams guide. |
| 16 | 337-345 | `### Key Rules for Spaarke Agent Teams` | 9 | Rule | **Borderline** | These are actual operational rules (no nested teams, one team per session) — but only fire when agent teams are active. **Recommendation**: Keep in agent-teams guide where they have context; don't load every session. |
| 17 | 346-351 | `### Quality Gates with Hooks` | 6 | Speculative | **Yes** | Describes hooks that are NOT configured (see "Hooks: Current Guidance" at 833-843). **Delete** or extract as "future" content. |
| 18 | 354-357 | `## 🚀 Project Initialization: Developer Workflow` (intro) | 4 | Pointer | **No** | Anchor for the workflow rules below. |
| 19 | 358-373 | `### Step 1: Create AI-Optimized Specification` | 16 | Workflow-instruction | **Borderline** | Documented in `.claude/skills/design-to-spec/SKILL.md`. **Recommendation**: Compress to a 4-line "use design-to-spec, then project-pipeline" inline pointer; keep the skill as canonical. |
| 20 | 376-392 | `### Step 2: Initialize Project (Full Pipeline)` | 17 | Workflow-instruction | **Borderline** | Same as #19 — pipeline steps duplicate `.claude/skills/project-pipeline/SKILL.md`. **Recommendation**: Compress to one pointer line. |
| 21 | 395-421 | `### Step 3: Execute Tasks` | 27 | Workflow-instruction | **Borderline** | Trigger phrases here duplicate the table at lines 444-457 within the MANDATORY section. **Recommendation**: Keep ONE trigger-phrases table (the MANDATORY one); delete this version. |
| 22 | 423-490 | `### 🚨 MANDATORY: Task Execution Protocol for Claude Code` | 68 | **Rule (load-bearing)** | **No** | The single most important block per failure-mode evidence. KEEP, possibly tighten. |
| 23 | 492-509 | `### ⚠️ Component Skills (AI Internal Use Only)` | 18 | Reference-table | **Borderline** | Tells developers which skills NOT to call directly. **Recommendation**: Move to `.claude/skills/INDEX.md` (which is the canonical skill registry); keep a 1-line "see INDEX for orchestrator-only skills" pointer. |
| 24 | 512-513 | `## 🚨 AI Execution Rules (Critical)` (heading) | 2 | Structural | **No** | Anchor for the critical rules below. |
| 25 | 514-523 | `### Context Management` | 10 | **Rule (load-bearing)** | **No** | Numeric thresholds the agent acts on. KEEP. |
| 26 | 525-541 | `### Proactive Checkpointing (MANDATORY)` | 17 | **Rule (load-bearing)** | **No** | KEEP. |
| 27 | 543-563 | `### Context Persistence` | 21 | Pointer + Reference-table | **Borderline** | The "resume" trigger-phrase table partly duplicates lines 444-457. **Recommendation**: Keep the file-table (3 rows); move the "resume" triggers to a single consolidated trigger-phrase table. |
| 28 | 565-574 | `### Human Escalation Triggers` | 10 | **Rule (load-bearing)** | **No** | KEEP. |
| 29 | 576-587 | `### Task Completion and Transition` | 12 | **Rule (load-bearing)** | **No** | KEEP. |
| 30 | 591-594 | `## Task Execution Rigor Levels` (intro) | 4 | Structural | **No** | Anchor. |
| 31 | 595-602 | `### Rigor Level Overview` | 8 | Decision-tree + Reference-table | **No** | KEEP — agents apply this per task. |
| 32 | 603-622 | `### Automatic Detection (Decision Tree)` | 20 | Decision-tree | **No** | KEEP — three small bullet lists; tight. |
| 33 | 624-642 | `### Mandatory Rigor Level Declaration` | 19 | Rule + Code-example | **Borderline** | The "MUST output" rule is load-bearing. **Recommendation**: Keep the rule (4 lines); extract the example template to the task-execute skill (which already references it on line 679). |
| 34 | 643-649 | `### User Override` | 7 | Reference | **Borderline** | Three short bullets. **Recommendation**: Compress to one line. |
| 35 | 650-660 | `### Examples by Task Type` | 11 | Reference-table | **Yes** | Examples table; duplicates the task-execute skill's own examples. **Extract** to skill. |
| 36 | 661-680 | `### Audit Trail in current-task.md` | 20 | Code-example | **Yes** | Code template. Already pointed to `.claude/skills/task-execute/SKILL.md` Step 0.5 (line 679). **Extract**. (Note: line 666 `### Task XXX Details` is *inside* a fenced code block, not an actual heading — the grep picked it up as a false positive.) |
| 37 | 683-686 | `## 🛠️ AI Agent Skills (MANDATORY)` (intro) | 4 | Structural | **No** | Anchor — but very thin; can become one paragraph. |
| 38 | 687-692 | `### Skill Discovery` | 6 | Pointer | **No** | KEEP — short, points to `.claude/skills/INDEX.md`. |
| 39 | 693-725 | `### Trigger Phrases → Required Skills` | 33 | Reference-table | **Yes** | **THE largest reference table.** 32 rows mapping phrases to skills. This is exactly the content that belongs in `.claude/skills/INDEX.md` (it has its own "trigger phrases" inventory). **Extract.** Keep one line: "See `.claude/skills/INDEX.md` for trigger phrases → skills mapping." Also flagged as a primary duplication source vs. INDEX.md by parallel task 001. |
| 40 | 726-752 | `### Auto-Detection Rules` | 27 | Reference-table | **Yes** | Same pattern: file-conditions → skills. **Extract** to `.claude/skills/INDEX.md`. |
| 41 | 753-762 | `### Always-Apply Skills` | 10 | Pointer | **Borderline** | Three skills marked always-on. **Recommendation**: Move to `.claude/skills/INDEX.md`; the always-apply property already lives in each skill's SKILL.md front-matter. |
| 42 | 763-797 | `### Slash Commands` | 35 | Reference-table | **Yes** | 30+ slash commands. Pure lookup data. Many duplicate the Trigger Phrases table above. **Extract** to a single source in `.claude/skills/INDEX.md`. |
| 43 | 800-801 | `## Architecture Discovery` (heading) | 2 | Structural | **No** | Anchor. |
| 44 | 802-818 | `### Read Code First, Docs Second` | 17 | **Rule (load-bearing)** | **No** | The source-of-truth principle. KEEP. Could compress the 9-item ordered list to 4 items. |
| 45 | 820-831 | `### Sub-Agent Write Boundary (IMPORTANT)` | 12 | **Rule (load-bearing)** | **No** | KEEP — explains an error the agent will see. |
| 46 | 833-842 | `### Hooks: Current Guidance` | 10 | Background | **Borderline** | "We evaluated hooks; they're not configured" — meta-decision. **Recommendation**: Keep ONE line ("Hooks are not configured — see guide if reconsidering"); extract rationale to a guide. |
| 47 | 844-855 | `### System Entry Points` | 12 | Pointer-table | **Borderline** | Useful "where to start" map. **Recommendation**: This is genuinely useful per-session if the agent ever needs to navigate code. Keep, or extract to `docs/architecture/SYSTEM-ENTRY-POINTS.md` and reference. Lean toward extract if budget is tight. |
| 48 | 857-872 | `### Context Layer Hierarchy` | 16 | Reference-table | **Borderline** | 11-row table of directories. Useful but rarely consulted mid-task. **Recommendation**: Extract to `docs/architecture/context-layer-hierarchy.md`. |
| 49 | 876-883 | `## Azure Infrastructure Resources` (intro) | 8 | Rule | **Borderline** | Contains a useful rule ("avoid discovery queries"). **Recommendation**: Keep the rule (2 lines); extract endpoint tables. |
| 50 | 884-892 | `### Quick Endpoints (Dev Environment)` | 9 | Reference-table | **Yes** | Endpoint table. **Extract** — already documented in `docs/architecture/auth-azure-resources.md`. |
| 51 | 894-901 | `### Resource Documentation` | 8 | Pointer-table | **Yes** | Pointer-of-pointers. **Extract** or fold into Azure resources doc. |
| 52 | 903-914 | `### Key Resource Names` | 12 | Reference-table | **Yes** | Resource-name lookup. **Extract** to Azure resources doc. |
| 53 | 916-921 | `### Dataverse Environments` | 6 | Reference-table | **Yes** | One row. **Extract** to environment registry. |
| 54 | 924-930 | `## Project Overview` | 7 | Background | **Borderline** | The "what is Spaarke" elevator pitch. **Recommendation**: Keep 3-4 lines for orientation; extract longer version to `docs/architecture/README.md`. |
| 55 | 932-958 | `## Repository Structure` | 27 | Reference / Tree-diagram | **Yes** | ASCII tree of the repo. **Extract** to top-level `README.md` or `docs/architecture/repository-structure.md`. |
| 56 | 960-976 | `## Architecture Decision Records (ADRs)` | 17 | Reference-table | **Yes** | 11-row ADR summary table. **Extract** to `.claude/adr/INDEX.md` (which already exists). |
| 57 | 978-989 | `## AI Architecture` | 12 | Background + Reference-table | **Yes** | Component table for AI tools. **Extract** to `docs/architecture/AI-ARCHITECTURE.md` (already referenced on line 980). |
| 58 | 991-992 | `## Coding Standards` (intro) | 2 | Structural | **Yes** | The next 3 subsections are pure code samples — should NOT be in CLAUDE.md. |
| 59 | 993-1013 | `### .NET (Backend)` | 21 | Code-example | **Yes** | DO/DON'T C# code samples. **Extract** to `docs/standards/dotnet-coding-conventions.md`. |
| 60 | 1014-1035 | `### TypeScript/PCF (Field-Bound Form Controls — React 16/17)` | 22 | Code-example | **Yes** | DO/DON'T PCF code samples. **Extract** to standards. |
| 61 | 1036-1065 | `### TypeScript/React Code Pages (Standalone Dialogs & Pages — React 18)` | 30 | Code-example | **Yes** | DO/DON'T Code Page samples. **Extract** to standards. |
| 62 | 1066-1082 | `### Dataverse Plugins` | 17 | Code-example | **Yes** | DO/DON'T plugin samples. **Extract** to standards. |
| 63 | 1083-1100 | `## Commands` | 18 | Reference-table | **Yes** | Build/test commands. **Extract** to `docs/procedures/build-and-test.md`. |
| 64 | 1101-1111 | `### Node Installs: Avoid `npm ci` for Vite Solutions (2026-05-13)` | 11 | Rule + Background | **Borderline** | Dated guidance ("until lock regeneration is scheduled"). Has a real "DO/DON'T" rule. **Recommendation**: Keep the 2-line rule; extract the explanation to `docs/procedures/node-dependencies.md`. |
| 65 | 1113-1122 | `## File Naming Conventions` | 10 | Reference-table | **Yes** | Naming conventions. **Extract** to `docs/standards/naming-conventions.md`. |
| 66 | 1123-1134 | `## Error Handling` | 12 | Reference | **Yes** | Two short subsections. **Extract** to `docs/standards/error-handling.md`. |
| 67 | 1125-1129 | `### API Responses` | 5 | Reference | **Yes** | (Subsection of #66.) |
| 68 | 1130-1134 | `### PCF Controls` | 5 | Reference | **Yes** | (Subsection of #66.) |
| 69 | 1135-1141 | `## Security Considerations` | 7 | **Rule** | **Borderline** | Four security rules. **Recommendation**: KEEP — security rules should not require a doc lookup. Compress to 3 lines. |
| 70 | 1142-1156 | `## Development Lifecycle` | 15 | Reference-table | **Yes** | 8-phase lifecycle table. **Extract** to `docs/procedures/development-lifecycle.md`. |
| 71 | 1157-1160 | `### 🤖 AI-Assisted Development` | 4 | Pointer | **Yes** | One paragraph pointing to Project Initialization section. **Delete** (the pointer already exists at the top of "Project Initialization"). |
| 72 | 1161-1167 | `### Before Starting Work` | 7 | Workflow-instruction | **Borderline** | Four-step checklist. **Recommendation**: Fold into the streamlined "Task Execution Protocol" up top; delete this duplicate. |
| 73 | 1168-1179 | `### Working Checklist` | 12 | Workflow-instruction | **Borderline** | Pre/post-work checklist. **Recommendation**: Same as #72 — fold into protocol. |
| 74 | 1181-1186 | `## Module-Specific Instructions` | 6 | Pointer | **No** | KEEP — tells agent where subordinate CLAUDE.md files live. |
| 75 | 1188-1190 | (Footer line: `*Last updated: February 11, 2026*`) | 3 | Stale | **Yes** | Stale (file's actual Last-Updated is 2026-04-05 per line 3). **Delete** — the front-matter blockquote already carries the date. |

---

## Cross-Reference Audit

All cross-references in the file resolve as of 2026-05-14:

| Reference | Status |
|---|---|
| `.claude/settings.json` (line 57) | OK (exists; project-pipeline modifies it) |
| `.mcp.json` (line 204) | OK |
| `docs/guides/DATAVERSE-MCP-INTEGRATION-GUIDE.md` (line 239) | OK |
| `.claude/skills/INDEX.md` (line 689) | OK |
| `docs/procedures/context-recovery.md` (line 563) | OK |
| `.claude/protocols/` (line 587) | OK (4 files: AIP-001/002/003 + INDEX) |
| `.claude/skills/task-execute/SKILL.md` (line 679) | OK |
| `docs/architecture/auth-AI-azure-resources.md` (line 898) | OK |
| `docs/architecture/auth-azure-resources.md` (line 899) | OK |
| `infrastructure/ai-foundry/README.md` (line 900) | OK |
| `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` (line 901) | OK |
| `docs/architecture/AI-ARCHITECTURE.md` (line 980) | OK |
| `docs/architecture/playbook-architecture.md` (line 980) | OK |
| `src/server/api/Sprk.Bff.Api/Program.cs` (line 848) | OK |
| `src/client/pcf/{Control}/control/index.ts` (line 849) | OK pattern (verified controls exist) |
| `src/solutions/{Page}/src/main.tsx` (line 850) | OK pattern |
| `src/dataverse/plugins/.../BaseProxyPlugin.cs` (line 851) | OK — resolves to `src/dataverse/plugins/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/BaseProxyPlugin.cs` |
| `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` (line 852) | OK |
| `src/client/shared/Spaarke.UI.Components/src/index.ts` (line 853) | OK |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` (line 854) | OK |
| `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs` (line 855) | OK |
| `.claude/patterns/`, `.claude/adr/`, `.claude/constraints/`, `.claude/catalogs/` (lines 862-865) | OK (all exist; ADR-001 through ADR-027 plus INDEX) |
| `docs/architecture/`, `docs/standards/`, `docs/guides/`, `docs/procedures/`, `docs/data-model/`, `docs/enhancements/`, `docs/adr/` (lines 866-872) | OK (all exist) |
| `src/server/api/Sprk.Bff.Api/CLAUDE.md` (line 1184) | OK |
| `src/client/pcf/CLAUDE.md` (line 1185) | OK |
| `src/server/shared/CLAUDE.md` (line 1186) | OK |
| `projects/ci-cd-github-enhancement/` (line 839) | **Speculative** — text says "when implemented"; no current directory at that path. Honest disclaimer, but flag for review at rewrite time. |
| `.claude/patterns/auth/spaarke-auth-initialization.md` (line 1057) | Not verified by this task; assumed OK based on parallel task 001 (skills audit) scope. |

**No broken references found.** One speculative reference (`projects/ci-cd-github-enhancement/`) is honestly flagged in-line with "when implemented" — not a defect.

---

## Suspected Duplications (cross-check with parallel tasks)

These are areas where this inventory suspects content overlaps with `.claude/skills/*` or `.claude/protocols/*`. Confirmation depends on output of parallel task 001 (skills inventory) and task 003 (workflows inventory).

| CLAUDE.md section | Likely duplicate location | Confidence | Action at rewrite |
|---|---|---|---|
| `### Trigger Phrases → Required Skills` (693-725) | `.claude/skills/INDEX.md` | High | Replace with pointer |
| `### Auto-Detection Rules` (726-752) | `.claude/skills/INDEX.md` | High | Replace with pointer |
| `### Slash Commands` (763-797) | `.claude/skills/INDEX.md` (and each skill's frontmatter) | High | Replace with pointer |
| `### ⚠️ Component Skills (AI Internal Use Only)` (492-509) | `.claude/skills/INDEX.md` | Medium | Replace with pointer |
| `### Always-Apply Skills` (753-762) | Each skill's SKILL.md front-matter | High | Replace with pointer |
| `### Hooks System` (151-186) | Contradicts `### Hooks: Current Guidance` (833-843) | High | Hooks System is tutorial-only; current guidance is the rule. Replace System with pointer. |
| `### Step 1/Step 2/Step 3` (358-421) | `.claude/skills/design-to-spec/SKILL.md`, `.claude/skills/project-pipeline/SKILL.md`, `.claude/skills/task-execute/SKILL.md` | High | Replace with one-line pointers |
| `### Audit Trail in current-task.md` (661-680) | `.claude/skills/task-execute/SKILL.md` Step 0.5 (already referenced) | High | Delete |
| `### Examples by Task Type` (650-660) | `.claude/skills/task-execute/SKILL.md` | Medium | Delete |
| Coding standards code samples (993-1082) | `docs/standards/` (already exists per line 811) | High | Replace with one-line pointer |
| `## Architecture Decision Records (ADRs)` table (964-976) | `.claude/adr/INDEX.md` | High | Replace with pointer |
| `### Before Starting Work` (1161-1167) | `### Working Checklist` (1168-1179) | High (self-duplicate) | Merge or fold into protocol |
| `### Before Starting Work` (1161-1167) | `.claude/skills/task-execute/SKILL.md` Steps 1-3 | Medium | Replace with pointer |

---

## Recommended Disposition for Rewrite (Phase 3b preview, not committed)

A target skeleton that fits in ~180 lines:

1. Front-matter (5 lines)
2. Source-of-truth principle (10 lines — compressed "Read Code First")
3. Sub-Agent Write Boundary (8 lines — current text minus one paragraph)
4. **MANDATORY Task Execution Protocol** (40 lines — preserve as-is)
5. Context Management + Checkpointing (20 lines — preserve)
6. Human Escalation Triggers (8 lines — preserve)
7. Task Completion + Rigor Levels (30 lines — keep decision tree)
8. Security rules (5 lines — current rules)
9. Module CLAUDE.md pointer (5 lines)
10. Pointers section (20 lines — every "extract candidate" lands here as a one-line pointer)
11. Failure-modes pointer to `.claude/FAILURE-MODES.md` (3 lines, new per spec F-2)

Approximate total: **~155 lines**, well under the 200-line target.

---

## Notes for Phase 3a Reviewer

1. **Honesty disclosures**: 12 sections are flagged "Borderline" — I have given my recommendation for each but the call should be made deliberately, not auto-applied.
2. **Cross-team dependency**: 8 of the high-confidence "duplication" findings depend on parallel task 001 (skills audit) confirming the duplicate content actually exists in `.claude/skills/INDEX.md`. If task 001 returns "INDEX.md does not actually catalog those trigger phrases", then CLAUDE.md is the only source and they should NOT be extracted without first populating INDEX.md.
3. **One self-contradiction worth resolving in the rewrite**: lines 151-186 ("Hooks System" — implies hooks are useful) vs. lines 833-843 ("Hooks: Current Guidance" — hooks are NOT configured). The "Current Guidance" version wins; the tutorial should be moved or deleted.
4. **Three sections that look load-bearing but are actually stale**:
   - Line 1188-1190 footer date (`February 11, 2026`) contradicts line 3 (`2026-04-05`). Delete the footer.
   - Lines 346-351 (`### Quality Gates with Hooks`) describes a feature that is NOT actually in use.
   - Lines 295-319 contain example team-spawn prompts but Agent Teams are experimental per line 260; users following these examples may not have the flag enabled.
5. **Line 666 false-positive**: `### Task XXX Details` on line 666 was picked up by heading-grep but is *inside* a fenced code block (lines 665-677). It is not a real H3 section. Excluded from the H3 count above.

---

*End of inventory.*
