# Task 050 notes — root CLAUDE.md §1.1 row (2026-11-25, main session)

**Rigor:** STANDARD (POML, prescriptive). Main session (root CLAUDE.md and `.claude/`).

## Result
- One row added to root `CLAUDE.md` §1.1: **Spaarke for Microsoft 365 Copilot** (Copilot connector **"Spaarke MCP"**), citing `projects/spaarke-ontology-platform-r1/notes/ontology-component-model.md` §1.1. `git diff CLAUDE.md`: one line added, nothing else.
- Before: 218 lines, 21,730 bytes (LF, `git show HEAD:CLAUDE.md | wc -l -c`). After: 219 lines, 22,121 bytes. **+391 bytes**, ≤ 30 KB.
- **Owner OK for the +1 line:** "Yes, OK the +1" (owner, in this session, 2026-11-25). Recorded in the CHANGELOG entry.
- Cited source agrees: ontology-component-model.md §1.1 on master names the connector "Spaarke MCP" (row ~L29; surfaces row ~L580), from commit `324cf6e5e2` (now on master).
- Negative: "Spaarke Copilot" is not used as a name; the Spaarke Connect row is unchanged.

## Checklist F (ai-procedure-maintenance)
1. Parallel updates: `.claude/skills/INDEX.md` `mcp-tool-handler` rows (table and knowledge list) still described `IAiToolHandler`; updated with its Last Updated date. `SKILL-INTERACTION-GUIDE.md`, `docs/CLAUDE.md`: no MCP mention to update. Project `CLAUDE.md`: no change needed. — done
2. Paths: every identifier in the row exists (`src/server/api/Sprk.Bff.Api/Api/Mcp/`, `src/client/widgets/spaarke-decisions/`, `src/solutions/CopilotPlugins/spaarke/`, the ontology file). — done
3. Last Updated dates: skills INDEX updated; root CLAUDE.md has no Last Updated line (its maintainer "Last reviewed" is for full reviews, unchanged). — done
4. Budget and placement: within 30 KB; over 200 lines by one with the owner's OK; a §1.1 naming row is in scope (naming rules belong in root CLAUDE.md); no section renumbered. — done
5. Byte delta stated in `.claude/CHANGELOG.md`. `/doctor prompt-audit` is a CLI command not available in this desktop session; not run (noted). — done, with that gap
6. Consistency: `scripts/quality/Test-InstructionPaths.ps1` → no new missing path from this work (it found `scripts/check-bundle-size.mjs` in the widget-design edit, fixed to the repo-rooted path). One new miss is not ours: `.claude/adr/ADR-026-full-page-custom-page-standard.md` names `src/dataverse/webresources/`, which master removed in #3074 (2026-11-24). Reported to the owner. — done

## Deviation
- Ran before 042 closed (owner, 2026-11-25: test in parallel and continue).
