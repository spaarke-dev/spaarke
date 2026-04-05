# Lessons Learned — AI Procedure Refactoring R2

> **Project**: ai-procedure-refactoring-r2
> **Completed**: April 5, 2026
> **Scope**: 74 documentation files across 5 document types (architecture, guide, standards, data model, procedures)

## What Worked Well

### 1. Parallel execution via concurrent agents
Phase 2 (architecture restoration) and Phase 3 (new architecture docs) ran multiple tasks concurrently. Once the per-task context pattern was established (read code → read trimmed doc → draft using `/docs-architecture` skill), agents could work independently on separate files without stepping on each other. This cut wall-clock time dramatically for the 25+ architecture-doc tasks.

### 2. Autonomous mode with skill-driven drafting
Requiring every document to be drafted via its type's skill (`/docs-architecture`, `/docs-guide`, etc.) produced consistent structure across authors. The skill files acted as templates and quality checklists simultaneously. Autonomous mode worked because the skills gave each task a tight, well-defined shape.

### 3. Code-first verification discipline
The constraint "code is source of truth — always read current code before documenting" caught dozens of places where existing docs described out-of-date implementations. Restoring over-trimmed docs by running `git log -p` on the file and cross-checking against current code gave us the best of both worlds: recovered depth plus current accuracy.

### 4. Per-document-type skills
Splitting the single old "`/write-docs`" concept into five type-specific skills (architecture, guide, standards, data-model, procedures) prevented category confusion. Architecture docs didn't drift into how-to instructions; guides didn't accidentally become design rationale.

### 5. Phase-based structure with explicit verification phase
Phase 6 (verification of existing entity docs and guides) caught a significant drift set that would have been invisible if we had only created new docs. The report-and-fix pattern of the verification tasks produced actionable deltas.

## Key Discoveries

### 1. Over-trimming was more severe than spec suggested
R1's trimming removed not just verbose prose but also critical detail — configuration options, error handling flows, deployment sequence. Multiple architecture docs needed substantial restoration from git history, not just light touch-ups.

### 2. Drift between docs and code was systemic
Examples:
- Entity docs missing fields that had been added to Dataverse schemas
- Architecture docs referencing classes that had been renamed or moved
- Guides pointing to config files that had been replaced by `.template` variants
- Pattern directories referenced as `.claude/patterns/bff-api/` when the actual path was `.claude/patterns/api/`

### 3. Cross-reference rot from project renames
Renaming `projects/ai-spaarke-platform-enhancements-r1/` to `projects/x-ai-spaarke-platform-enhancements-r1/` (to mark as archived) invalidated every doc that linked into that project. This happened silently.

### 4. Short-form ADR links proliferate
38 occurrences of `.claude/adr/ADR-NNN.md` without the slug suffix — a convention drift that built up over time and wasn't caught until the Phase 7 cross-reference verification. Auto-fixable once identified.

### 5. Library-relative paths vs. repo-relative paths
SHARED-UI-COMPONENTS-GUIDE.md and shared-ui-components-architecture.md use paths like `src/components/` that are relative to the library root (`src/client/shared/Spaarke.UI.Components/`). These look broken to a naive verifier but are intentional shorthand. A better convention would be to note this once at the top of the doc and then qualify paths, or to always use full paths.

### 6. Placeholder `...` in paths is common but breaks verification
AI-ARCHITECTURE.md used `src/server/api/.../Services/Ai/...` as shorthand for the project root. Humans parse this fine; verifiers do not. Either use the full path always, or make the placeholder convention explicit in a legend at the top.

### 7. Missing INDEX.md files in subdirectories
`docs/procedures/` and `docs/data-model/` had no INDEX.md despite being referenced from the root docs index. Easy to miss, easy to fix — should be part of the `/docs-*` skill checklists going forward.

### 8. Synthetic examples pollute verification
Testing and standards docs used pedagogical paths like `src/server/api/Services/DataService.cs:45` that were never meant to be real. A verifier can't distinguish these from genuine broken links without content understanding. Recommend using a clearly-fictional namespace (e.g., `Example.Services.DataService`) for pedagogical samples.

## Recommendations for Future Documentation Projects

### Tooling
1. **Build a repo-integrated link checker** that runs in CI. The ad-hoc Node script written for Task 080 should become a permanent tool at `scripts/verify-doc-paths.js`, with an allow-list for known pedagogical paths and library-relative shorthand.
2. **Auto-fix ADR short-form references** on every commit touching `docs/`. The 38 fixes here could have been prevented by a pre-commit hook.
3. **Project rename sweep**: whenever a project directory is renamed/archived, grep the entire repo for the old name and produce a rename list for manual review.

### Process
4. **Verify phase is non-negotiable**. The project's Phase 6 verification tasks caught issues that authoring alone would have missed. Future doc projects should always include a verification phase, not just an authoring phase.
5. **Make INDEX.md creation part of the `/docs-*` skills**. Every time a new document type is introduced or a new doc is added, its INDEX.md must be updated (or created).
6. **Require Known Pitfalls section for architecture docs**. This project's architecture doc skill enforced this, and it surfaced valuable gotchas that would otherwise live only in people's heads.
7. **Prefer full paths over shorthand**. Even when a doc is scoped to a specific directory, full paths (`src/client/shared/Spaarke.UI.Components/src/components/...`) are robust against tooling and movement.

### Content
8. **Read code first, always**. Multiple agents in this project started from the existing (stale) doc and had to be corrected. A hard rule: "Read the entry-point code file before the existing doc" prevented stale-doc carry-over.
9. **Git history is your friend for over-trimmed docs**. `git log -p -- {file}` gave back content that would have been impossible to reconstruct from memory or current code alone.
10. **Cross-entity reference docs are high-leverage**. The new `entity-relationship-model.md`, `field-mapping-reference.md`, and `json-field-schemas.md` unlock far more value per page than any individual entity doc because they answer questions that require reading multiple entity docs otherwise.

## Metrics

- **Documents created**: ~22 (13 new architecture + 3 standards + 3 data-model cross-refs + 2 guides + 2 procedures)
- **Documents restored/enhanced**: ~15 over-trimmed architecture docs
- **Documents verified**: ~24 existing entity docs + guides
- **Auto-fixes applied in Task 080**: 42 broken file path references
- **Broken references flagged for manual review**: 10 (see `verification-report.md` Section E)
- **Wall-clock**: Single-day execution via parallel agent tasks
