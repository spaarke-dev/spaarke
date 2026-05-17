# Root CLAUDE.md Audit Findings — Phase 3a

> **🚪 REVIEWER SIGN-OFF REQUIRED BEFORE PHASE 3b (rewrite)**
>
> This document audits the root `CLAUDE.md` (1,190 lines) against community best practices for Claude Code project files + the Spaarke-specific Phase 0 inventory. No file has been modified. Phase 3b (the actual rewrite to <200 lines) cannot begin until this audit's per-section recommendations carry ✅ or ❌ from the reviewer + the proposed new skeleton is approved.
>
> **Created**: 2026-05-17 by project `ai-procedure-quality-r1` (Phase 3a)
> **Sources**: Current `CLAUDE.md` at 1190 lines (audit-stamp 2026-04-05) · Phase 0 inventory at [`notes/inventory/claudemd.md`](../projects/ai-procedure-quality-r1/notes/inventory/claudemd.md) · 75-section breakdown already complete
> **Target**: <200 lines hard, ~180 lines aspirational (spec F-15.1)

---

## 1. Best Practices Synthesis (Applied)

Consensus from Anthropic docs + community posts (Anthropic blog, simonwillison, Geoffrey Huntley, others, May 2025–April 2026):

### What `CLAUDE.md` is FOR

A `CLAUDE.md` loads at every session start. It pays for its line count in prompt tokens every turn. So its content should be:

1. **Project-specific** — what makes THIS codebase unusual. Generic Claude Code knowledge belongs in Anthropic's docs, not yours.
2. **Operational** — binding rules the agent must apply every turn. Anything purely informational belongs in `docs/`.
3. **Front-loaded** — the first 50 lines matter most. Critical rules + project identity at top.
4. **MUST/MUST NOT** — binding rules use this voice. Suggestions and explanations don't belong.
5. **Pointer-heavy, not content-heavy** — link to skills/ADRs/patterns by path. Don't duplicate.

### What `CLAUDE.md` is NOT for

- ❌ Tutorials on Claude Code features (model modes, hooks system, agent teams) — the agent already knows these; loading the tutorial wastes tokens every session
- ❌ Marketing or "why we built this" prose — useful for humans, useless for agents per-turn
- ❌ Long reference tables (slash commands, skills, ADRs) — these belong in dedicated INDEX files
- ❌ Code samples / DO/DON'T blocks — these belong in `docs/standards/` or skill bodies
- ❌ Content that contradicts other content (e.g., `Hooks System` tutorial vs. `Hooks: Current Guidance` — current Spaarke has BOTH)
- ❌ Speculative or dated content ("New Claude Code Features (February 2026)" — the date is already aging)

### Target shape (~180 lines)

| Block | Purpose | Lines |
|---|---|---|
| Front-matter blockquote | File identity + Last Reviewed | 5 |
| Project identity (1-paragraph "what is Spaarke") | Orientation | 5 |
| Source-of-truth principle | "Code wins; docs lag" | 8 |
| Sub-agent write boundary | Permission rule the agent will hit | 8 |
| **🚨 MANDATORY: Task Execution Protocol** | Binding behavior (load-bearing) | 40 |
| Context management + checkpointing | Numeric thresholds + behavior | 25 |
| Human escalation triggers | Behavior rule for ambiguity | 8 |
| Task completion + transition | State-machine rule | 8 |
| Task Execution Rigor Levels (decision tree) | Conditional logic the agent applies | 25 |
| Security rules | Hard rules (no secrets, auth model) | 6 |
| Build commands cheat-sheet | Exact strings | 5 |
| Pointers section | One-line pointers to extract destinations | 25 |
| Failure-modes pointer | `.claude/FAILURE-MODES.md` | 3 |
| Module-specific CLAUDE.md pointer | Subordinate files | 5 |
| Footer | Last-updated, owner | 2 |
| **Total** | | **~180 lines** |

---

## 2. Executive Summary

| Statistic | Current | Target |
|---|---|---|
| Total lines | 1190 | <200 |
| H2 sections (`##`) | 17 | ~10 |
| H3 sections (`###`) | 47 | ~8 |
| Tables | 27 | ~3 (small) |
| Fenced code blocks | 24 | 0-2 |
| Reduction required | — | ~83% |
| Extract candidates identified | 22 sections (~720 lines) | — |
| Hard-target sections (keep verbatim) | 10 sections (~165 lines) | — |

**Verdict**: The 22 extract candidates plus targeted compression of the ~165-line hard-target sections lands us at ~180 lines — within budget.

---

## 3. Cross-Cutting Findings

### 3.1 Internal contradictions (must resolve in rewrite)

| Conflict | Resolution |
|---|---|
| `### Hooks System` (lines 151-186, tutorial implying hooks are useful) vs `### Hooks: Current Guidance` (lines 833-843, "hooks are NOT configured") | Delete tutorial; keep the one-paragraph "Current Guidance" disclaimer |
| Footer date `*Last updated: February 11, 2026*` (line 1188) vs front-matter `Last Updated: 2026-04-05` (line 3) | Delete footer; one date in front-matter only |
| `### Step 3: Execute Tasks` trigger phrases (lines 395-421) vs `### 🚨 MANDATORY: Task Execution Protocol` table (lines 444-457) | Keep MANDATORY table; delete the duplicate in Step 3 |
| `### Before Starting Work` (1161-1167) vs `### Working Checklist` (1168-1179) | Merge into single "Working Discipline" block OR delete both (covered by Rigor Level + task-execute) |
| `### Quality Gates with Hooks` (346-351) describes hooks that are NOT configured | Delete entirely |

### 3.2 Speculative/dated content to remove

- `## New Claude Code Features (February 2026)` (lines 149-186) — date is aging; subsections (Hooks, Headless, MCP) belong elsewhere
- `### Audit Trail in current-task.md` (661-680) — points at task-execute Step 0.5 which already covers it
- `### Quality Gates with Hooks` (346-351) — not configured
- `### Agent Teams for Spaarke Projects` (293-320) — three worked examples for an experimental feature

### 3.3 High-confidence duplications (delete + point at canonical source)

| In CLAUDE.md | Canonical source |
|---|---|
| `### Trigger Phrases → Required Skills` (693-725, 33 lines) | `.claude/skills/INDEX.md` |
| `### Auto-Detection Rules` (726-752, 27 lines) | `.claude/skills/INDEX.md` |
| `### Slash Commands` (763-797, 35 lines) | `.claude/skills/INDEX.md` (skill frontmatter) |
| `### Always-Apply Skills` (753-762, 10 lines) | Each skill's frontmatter (`alwaysApply: true`) |
| `## Architecture Decision Records (ADRs)` table (964-976, 17 lines) | `.claude/adr/INDEX.md` and `docs/adr/` |
| `### Coding Standards` code blocks (993-1082, 90 lines) | `docs/standards/` |
| Endpoint tables in Azure Infrastructure (884-921, 38 lines) | `docs/architecture/auth-azure-resources.md` |
| `## Repository Structure` ASCII tree (932-958, 27 lines) | Top-level `README.md` |

### 3.4 Tutorials to extract to `docs/guides/`

| Section | Extract target | Lines |
|---|---|---|
| `### Adaptive Thinking & Effort Control (Opus 4.6)` (14-54) | `docs/guides/claude-code-thinking-and-effort.md` | 41 |
| `### Claude Code Model and Permission Settings` (55-148) | `docs/guides/claude-code-permission-modes.md` | 94 |
| `### Hooks System` (151-186) | `docs/guides/claude-code-hooks-reference.md` | 36 |
| `### Headless / Non-Interactive Mode` (187-200) | Fold into ci-cd docs | 14 |
| `### MCP Server Integration` (202-240) | Keep pointer to existing `DATAVERSE-MCP-INTEGRATION-GUIDE.md` | 39 |
| `## Agent Teams (Experimental)` (260-351) | `docs/guides/claude-code-agent-teams.md` | 92 |

Total tutorial extract: **~316 lines** moved out of CLAUDE.md into 4-5 new/existing guide files.

### 3.5 Reference tables to consolidate or extract

| Section | Action | Why |
|---|---|---|
| `## Commands` (1083-1100) | Extract to `docs/procedures/build-and-test.md` | Reference data, not session-critical |
| `## File Naming Conventions` (1113-1122) | Extract to `docs/standards/naming-conventions.md` | Reference data |
| `## Error Handling` (1123-1134) | Extract to `docs/standards/error-handling.md` | Reference data |
| `## Development Lifecycle` (1142-1156) | Extract to `docs/procedures/development-lifecycle.md` | Reference data |
| `## AI Architecture` (978-989) | Replace with pointer to existing `docs/architecture/AI-ARCHITECTURE.md` | Already documented |
| `## Project Overview` (924-930) | Compress to 3 lines for orientation; extract long version | Marketing prose |

---

## 4. Per-Section Sign-Off Table

Legend:
- **KEEP** = stays in body (possibly tightened)
- **EXTRACT** = moves to docs/ or skill bodies (link from CLAUDE.md with 1-2 line pointer)
- **DELETE** = removed entirely (content is duplicate, contradictory, or obsolete)
- **COMPRESS** = stays but shortened (verbose → terse)
- **MERGE** = combines with another section

Reviewer should mark each row ✅ (approved) or ❌ (rejected, see notes).

| # | Lines | Section | Current Lines | Recommended Action | Destination / Notes | Sign-off |
|---|---|---|---:|---|---|:---:|
| 0 | 1-10 | Front-matter blockquote | 10 | **COMPRESS** to 5 lines | One date line only; remove dual audit lines | ☐ |
| 1 | 12-13 | `## Development Environment` heading | 2 | **DELETE** | Section becomes empty after subsections extract; remove heading | ☐ |
| 2 | 14-54 | `### Adaptive Thinking & Effort Control` | 41 | **EXTRACT** | → `docs/guides/claude-code-thinking-and-effort.md` | ☐ |
| 3 | 55-148 | `### Claude Code Model and Permission Settings` | 94 | **EXTRACT** | → `docs/guides/claude-code-permission-modes.md`; keep 1-line "Settings in `.claude/settings.json`" | ☐ |
| 4 | 149-150 | `## New Claude Code Features (February 2026)` heading | 2 | **DELETE** | Dated; subsections all extract or delete | ☐ |
| 5 | 151-186 | `### Hooks System` | 36 | **EXTRACT** | → `docs/guides/claude-code-hooks-reference.md` (and resolve contradiction with line 833) | ☐ |
| 6 | 187-200 | `### Headless / Non-Interactive Mode` | 14 | **EXTRACT** | → Fold into ci-cd docs OR `docs/guides/claude-code-headless.md` | ☐ |
| 7 | 202-240 | `### MCP Server Integration` | 39 | **COMPRESS** | Keep 5-line pointer (description + canonical `docs/guides/DATAVERSE-MCP-INTEGRATION-GUIDE.md` link); extract tables | ☐ |
| 8 | 241-249 | `### Session Management` | 9 | **EXTRACT** | → Built-in Claude Code commands; belongs in slim cheatsheet OR delete (agent knows these) | ☐ |
| 9 | 251-257 | `### Cost & Token Awareness` | 7 | **EXTRACT** | → Fold into thinking-and-effort guide | ☐ |
| 10 | 260-263 | `## Agent Teams (Experimental)` intro | 4 | **COMPRESS** to 2 lines | "Experimental; see guide" pointer | ☐ |
| 11 | 264-279 | `### Enabling Agent Teams` | 16 | **EXTRACT** | → `docs/guides/claude-code-agent-teams.md` | ☐ |
| 12 | 280-292 | `### When to Use Agent Teams vs. Subagents` | 13 | **EXTRACT** | → guide | ☐ |
| 13 | 293-320 | `### Agent Teams for Spaarke Projects` | 28 | **EXTRACT** | → guide | ☐ |
| 14 | 321-332 | `### Display Modes` | 12 | **EXTRACT** | → guide | ☐ |
| 15 | 333-336 | `### Delegate Mode` | 4 | **EXTRACT** | → guide | ☐ |
| 16 | 337-345 | `### Key Rules for Spaarke Agent Teams` | 9 | **EXTRACT** | → guide (only fires when agent teams active) | ☐ |
| 17 | 346-351 | `### Quality Gates with Hooks` | 6 | **DELETE** | Describes features that are NOT configured | ☐ |
| 18 | 354-357 | `## 🚀 Project Initialization: Developer Workflow` intro | 4 | **COMPRESS** to 2 lines | Compact pointer to design-to-spec + project-pipeline | ☐ |
| 19 | 358-373 | `### Step 1: Create AI-Optimized Specification` | 16 | **COMPRESS** to 3 lines | Pointer to `design-to-spec` skill | ☐ |
| 20 | 376-392 | `### Step 2: Initialize Project (Full Pipeline)` | 17 | **COMPRESS** to 3 lines | Pointer to `project-pipeline` skill | ☐ |
| 21 | 395-421 | `### Step 3: Execute Tasks` | 27 | **DELETE** | Trigger phrases duplicate MANDATORY section table | ☐ |
| 22 | 423-490 | `### 🚨 MANDATORY: Task Execution Protocol` | 68 | **KEEP** + minor compress to 40 lines | Load-bearing; preserve trigger phrase table | ☐ |
| 23 | 492-509 | `### ⚠️ Component Skills (AI Internal Use Only)` | 18 | **EXTRACT** | → `.claude/skills/INDEX.md`; keep 1-line pointer | ☐ |
| 24 | 512-513 | `## 🚨 AI Execution Rules (Critical)` heading | 2 | **KEEP** | Anchor for kept critical rules | ☐ |
| 25 | 514-523 | `### Context Management` | 10 | **KEEP** | Load-bearing numeric thresholds | ☐ |
| 26 | 525-541 | `### Proactive Checkpointing (MANDATORY)` | 17 | **KEEP** | Load-bearing | ☐ |
| 27 | 543-563 | `### Context Persistence` | 21 | **COMPRESS** to 10 lines | File table + pointer; remove duplicated trigger phrases | ☐ |
| 28 | 565-574 | `### Human Escalation Triggers` | 10 | **KEEP** | Load-bearing | ☐ |
| 29 | 576-587 | `### Task Completion and Transition` | 12 | **KEEP** | Load-bearing | ☐ |
| 30 | 591-594 | `## Task Execution Rigor Levels` intro | 4 | **KEEP** | Anchor | ☐ |
| 31 | 595-602 | `### Rigor Level Overview` | 8 | **KEEP** | Decision tree | ☐ |
| 32 | 603-622 | `### Automatic Detection (Decision Tree)` | 20 | **KEEP** | Decision tree | ☐ |
| 33 | 624-642 | `### Mandatory Rigor Level Declaration` | 19 | **COMPRESS** to 6 lines | Keep the MUST rule; move template example to task-execute skill | ☐ |
| 34 | 643-649 | `### User Override` | 7 | **COMPRESS** to 2 lines | Three bullets → one paragraph | ☐ |
| 35 | 650-660 | `### Examples by Task Type` | 11 | **DELETE** | Examples; duplicates task-execute | ☐ |
| 36 | 661-680 | `### Audit Trail in current-task.md` | 20 | **DELETE** | Duplicates task-execute Step 0.5 (already referenced) | ☐ |
| 37 | 683-686 | `## 🛠️ AI Agent Skills (MANDATORY)` intro | 4 | **COMPRESS** to 2 lines | One-paragraph pointer to INDEX | ☐ |
| 38 | 687-692 | `### Skill Discovery` | 6 | **KEEP** | Short pointer to INDEX | ☐ |
| 39 | 693-725 | `### Trigger Phrases → Required Skills` | 33 | **EXTRACT** | → `.claude/skills/INDEX.md`; keep 1-line pointer | ☐ |
| 40 | 726-752 | `### Auto-Detection Rules` | 27 | **EXTRACT** | → `.claude/skills/INDEX.md` | ☐ |
| 41 | 753-762 | `### Always-Apply Skills` | 10 | **EXTRACT** | → INDEX; each skill's frontmatter already has alwaysApply | ☐ |
| 42 | 763-797 | `### Slash Commands` | 35 | **EXTRACT** | → INDEX | ☐ |
| 43 | 800-801 | `## Architecture Discovery` heading | 2 | **KEEP** | Anchor | ☐ |
| 44 | 802-818 | `### Read Code First, Docs Second` | 17 | **KEEP** + compress to 8 lines | Source-of-truth principle; tighten 9-item list to 4 | ☐ |
| 45 | 820-831 | `### Sub-Agent Write Boundary (IMPORTANT)` | 12 | **KEEP** + compress to 8 lines | Permission rule | ☐ |
| 46 | 833-843 | `### Hooks: Current Guidance` | 10 | **COMPRESS** to 3 lines | "Hooks not configured — see guide if reconsidering" | ☐ |
| 47 | 844-855 | `### System Entry Points` | 12 | **EXTRACT** | → `docs/architecture/SYSTEM-ENTRY-POINTS.md`; keep 1-line pointer | ☐ |
| 48 | 857-872 | `### Context Layer Hierarchy` | 16 | **EXTRACT** | → `docs/architecture/context-layer-hierarchy.md`; keep 1-line pointer | ☐ |
| 49 | 876-883 | `## Azure Infrastructure Resources` intro | 8 | **COMPRESS** to 4 lines | Keep the "avoid discovery queries" rule | ☐ |
| 50 | 884-892 | `### Quick Endpoints (Dev Environment)` | 9 | **EXTRACT** | → `docs/architecture/auth-azure-resources.md` (already referenced) | ☐ |
| 51 | 894-901 | `### Resource Documentation` | 8 | **EXTRACT** | → already pointed at; fold into 49 | ☐ |
| 52 | 903-914 | `### Key Resource Names` | 12 | **EXTRACT** | → auth-azure-resources.md | ☐ |
| 53 | 916-921 | `### Dataverse Environments` | 6 | **EXTRACT** | → environment registry | ☐ |
| 54 | 924-930 | `## Project Overview` | 7 | **COMPRESS** to 3 lines | Identity blurb; extract longer version to `docs/architecture/README.md` | ☐ |
| 55 | 932-958 | `## Repository Structure` | 27 | **EXTRACT** | → top-level `README.md` | ☐ |
| 56 | 960-976 | `## Architecture Decision Records (ADRs)` | 17 | **EXTRACT** | → `.claude/adr/INDEX.md` (already exists) | ☐ |
| 57 | 978-989 | `## AI Architecture` | 12 | **EXTRACT** | → `docs/architecture/AI-ARCHITECTURE.md` (already pointed at) | ☐ |
| 58 | 991-992 | `## Coding Standards` intro | 2 | **EXTRACT** | → docs/standards/ (already exists) | ☐ |
| 59 | 993-1013 | `### .NET (Backend)` code samples | 21 | **EXTRACT** | → `docs/standards/dotnet-coding-conventions.md` | ☐ |
| 60 | 1014-1035 | `### TypeScript/PCF` code samples | 22 | **EXTRACT** | → `docs/standards/pcf-coding-conventions.md` | ☐ |
| 61 | 1036-1065 | `### TypeScript/React Code Pages` code samples | 30 | **EXTRACT** | → `docs/standards/code-pages-conventions.md` | ☐ |
| 62 | 1066-1082 | `### Dataverse Plugins` code samples | 17 | **EXTRACT** | → `docs/standards/dataverse-plugins.md` | ☐ |
| 63 | 1083-1100 | `## Commands` | 18 | **EXTRACT** | → `docs/procedures/build-and-test.md`; keep 4-line cheatsheet inline | ☐ |
| 64 | 1101-1111 | `### Node Installs: Avoid npm ci for Vite` | 11 | **COMPRESS** to 3 lines | Keep the DO/DON'T rule; extract rationale | ☐ |
| 65 | 1113-1122 | `## File Naming Conventions` | 10 | **EXTRACT** | → `docs/standards/naming-conventions.md` | ☐ |
| 66 | 1123-1134 | `## Error Handling` | 12 | **EXTRACT** | → `docs/standards/error-handling.md` | ☐ |
| 67-68 | — | (Sub-sections of #66) | — | Merge with parent into single extract | ☐ |
| 69 | 1135-1141 | `## Security Considerations` | 7 | **KEEP** + compress to 4 lines | Hard security rules stay inline | ☐ |
| 70 | 1142-1156 | `## Development Lifecycle` | 15 | **EXTRACT** | → `docs/procedures/development-lifecycle.md` | ☐ |
| 71 | 1157-1160 | `### 🤖 AI-Assisted Development` | 4 | **DELETE** | Pointer to Project Initialization already at top | ☐ |
| 72 | 1161-1167 | `### Before Starting Work` | 7 | **DELETE** | Duplicates task-execute Steps 1-3 + Rigor Levels | ☐ |
| 73 | 1168-1179 | `### Working Checklist` | 12 | **DELETE** | Duplicates task-execute | ☐ |
| 74 | 1181-1186 | `## Module-Specific Instructions` | 6 | **KEEP** | Pointer to subordinate CLAUDE.md files | ☐ |
| 75 | 1188-1190 | Footer (`*Last updated: February 11, 2026*`) | 3 | **DELETE** | Stale date + duplicates front-matter | ☐ |

**KEEP total** (sections + compressed versions): ~155-165 lines
**EXTRACT total** (to docs/ or skills): ~720 lines across 4-6 new/existing files
**DELETE total**: ~120 lines (duplicates, contradictions, obsolete)

---

## 5. Proposed New CLAUDE.md Skeleton (~180 lines)

Reviewer should evaluate this as the rewrite target. Phase 3b produces this verbatim from the keep/compress decisions above.

```markdown
# CLAUDE.md — Spaarke Repository Instructions

> **Last Reviewed**: 2026-05-1X (after Human Gate 2)
> **Reviewed By**: ai-procedure-quality-r1 Phase 3b
> **Purpose**: Repository-wide context for Claude Code. Loads every session.

---

## 1. What is Spaarke?  [~5 lines]
{One-paragraph identity: SharePoint Document Access Platform; .NET 8 + Power Platform; design doc pointer.}

## 2. Source of Truth: Code, not docs  [~8 lines]
{Compressed "Read Code First, Docs Second" principle. 4-item priority order — code → patterns → ADRs → guides.}

## 3. Sub-Agent Write Boundary  [~8 lines]
{Permission rule the agent will hit. "Sub-agents cannot write to .claude/." Pattern: read in parallel, return findings, main session writes.}

## 4. 🚨 MANDATORY: Task Execution Protocol  [~40 lines — preserved verbatim]
{The full block from current lines 423-490. Trigger phrases → invoke task-execute skill. Auto-detection table. Why-this-matters consequences. Parallel execution rule.}

## 5. Context Management & Checkpointing  [~25 lines — preserved]
{Numeric thresholds (60%, 70%, 85%); proactive checkpointing rules every 3 steps; checkpoint behavior.}

## 6. Human Escalation Triggers  [~8 lines — preserved]
{When to ask the user instead of acting.}

## 7. Task Completion & Transition  [~8 lines — preserved]
{State-machine transition after task completion.}

## 8. Task Execution Rigor Levels  [~25 lines — decision tree preserved]
{FULL / STANDARD / MINIMAL determination. Auto-detection rules. Mandatory declaration format.}

## 9. Security Rules  [~6 lines]
{NEVER commit secrets. Auth model summary. Config patterns.}

## 10. Build Commands  [~5 lines]
{`dotnet build src/server/api/Sprk.Bff.Api/`. `dotnet test`. `npm run build:prod` (NOT `npm run build` — see FAILURE-MODES.md#AP-1). PCF deploy patterns. For more, see docs/procedures/build-and-test.md.}

## 11. Pointers (one-liners, no inline content)  [~25 lines]
- Skills + trigger phrases + slash commands → `.claude/skills/INDEX.md`
- ADRs + concise constraints → `.claude/adr/INDEX.md` and `docs/adr/`
- Code patterns → `.claude/patterns/`
- Constraints (MUST/MUST NOT topic summaries) → `.claude/constraints/`
- Failure modes (cross-cutting anti-patterns + gotchas) → `.claude/FAILURE-MODES.md`
- Architecture (subsystems, design decisions) → `docs/architecture/`
- Coding standards (cross-cutting conventions) → `docs/standards/`
- Operational guides (deploy, configure, troubleshoot) → `docs/guides/`
- Development procedures (test, CI/CD, code review) → `docs/procedures/`
- Dataverse data model (entities, fields, JSON schemas) → `docs/data-model/`
- Project initialization workflow → `.claude/skills/design-to-spec/` + `.claude/skills/project-pipeline/`
- Active project state → `projects/{name}/current-task.md`
- Azure resources (endpoints, names) → `docs/architecture/auth-azure-resources.md`
- Module-specific CLAUDE.md → `src/server/api/Sprk.Bff.Api/CLAUDE.md`, `src/client/pcf/CLAUDE.md`, `src/server/shared/CLAUDE.md`

## 12. Footer  [~2 lines]
{Owner + how to extend this file.}
```

**Anticipated total**: 175-185 lines, comfortably under the 200-line hard target.

---

## 6. Open Questions for Reviewer

The audit surfaced 4 questions where the recommended action depends on your judgment:

1. **`### System Entry Points` (line 844-855, 12 lines)** — useful "where to start" map. Audit recommends EXTRACT. Your call: keep inline (genuinely useful per-session for code-navigating tasks) or extract.
2. **`### Context Layer Hierarchy` (line 857-872, 16 lines)** — 11-row directory table. Audit recommends EXTRACT. Your call: same trade-off as #1.
3. **`### Mandatory Rigor Level Declaration` example template (line 624-642, 19 lines)** — audit recommends keeping the rule (6 lines) and moving the template to task-execute skill. Your call: keep template inline if you want declarations visible at top-of-CLAUDE.md, or extract for tightness.
4. **`### Hooks: Current Guidance` (lines 833-843, 10 lines)** — the meta-decision document. Audit recommends compressing to 3 lines. Your call: keep the full rationale (for future revisits) or accept the compression.

---

## 7. Reviewer Sign-Off Section

> **Instructions**: For each of the 75 rows in §4, mark Sign-off as ✅ (approved) or ❌ (rejected with notes). Decide the 4 open questions in §6. Approve or amend the proposed skeleton in §5. When all done, append the reviewer's name and date below. Phase 3b execution begins after that signature.

| Field | Value |
|---|---|
| Reviewer | _________________________________ |
| Date signed | _________________________________ |
| Skeleton approved as proposed (§5)? | ☐ Yes / ☐ Yes with edits / ☐ No |
| Notes / changes to proposed skeleton | _________________________________ |
| Special handling for Open Questions (§6) | Q1: __ Q2: __ Q3: __ Q4: __ |
| Override notes (any rows marked ❌ in §4) | _________________________________ |

After sign-off, the project advances to Phase 3b (CLAUDE.md rewrite) per the approved skeleton. The OLD `CLAUDE.md` (1190 lines) will be archived to `.claude/archive/2026-05-1X/CLAUDE.md` BEFORE the rewrite per the project's reversibility convention (NF-1).

---

## 8. Phase 3b Execution Plan (preview, post-approval)

1. **Archive current `CLAUDE.md`** → `.claude/archive/2026-05-1X/CLAUDE.md` (untouched copy)
2. **Create new guide files** for tutorial extractions (4-6 new files in `docs/guides/`)
3. **Update existing index/registry files** with extracted content (`.claude/skills/INDEX.md`, `.claude/adr/INDEX.md`, `docs/architecture/auth-azure-resources.md`, top-level `README.md`)
4. **Write new `CLAUDE.md`** per approved skeleton (~180 lines)
5. **Verify build** (`dotnet build src/server/api/Sprk.Bff.Api/`)
6. **Run cross-reference drift detector** (manual until Phase 4a ships the script) — ensure every pointer in new CLAUDE.md resolves
7. **Smoke test**: start a fresh session, ask "where do I start with this codebase?" — verify the new CLAUDE.md is loadable + the agent navigates correctly
8. **Commit + push to PR #294**

---

## Appendix A — Method and Sources

- **Phase 0 inventory** at [`projects/ai-procedure-quality-r1/notes/inventory/claudemd.md`](../projects/ai-procedure-quality-r1/notes/inventory/claudemd.md) — 75-section breakdown with classifications and extract recommendations
- **Best practices synthesis** from: Anthropic Claude Code docs (Feb 2025–April 2026), Anthropic engineering blog, Simon Willison's notes on Claude Code, Geoffrey Huntley's CLAUDE.md series, community discussions on r/ClaudeAI and Discord
- **Spec F-15.1** — the <200-line hard target for CLAUDE.md (tiered targets resolution)
- **Cross-reference audit** (in Phase 0 inventory) — all 25+ inline references in current CLAUDE.md verified to resolve; 1 honestly-flagged speculative reference ("when implemented")
- **Honesty rule**: 12 sections marked "Borderline" in the Phase 0 inventory have been resolved into KEEP/EXTRACT/COMPRESS in this document; reviewer can override any specific row

---

*Phase 3a complete 2026-05-17. Awaiting Human Gate 2 sign-off before Phase 3b begins. See [`CHANGELOG.md`](CHANGELOG.md) for the entry once this audit's outcomes ship.*
