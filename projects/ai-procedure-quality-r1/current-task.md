# Current Task State — AI Procedure Quality R1

> **Last Updated**: 2026-05-16 (by context-handoff, mid-Phase-2b)
> **Recovery**: Read "Quick Recovery" first; the entire continuation context is in this file.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | ai-procedure-quality-r1 |
| **Branch** | `work/ai-procedure-quality-r1` |
| **PR** | [#294 (draft)](https://github.com/spaarke-dev/spaarke/pull/294) |
| **Latest commit** | `da992a04` — Wave 2b-A Batch 3 — 5 more skills refined (15 of 31 total) |
| **Status** | **Phase 2b Wave 2b-A IN PROGRESS** — 15 of 31 skills refined; **16 remaining across Batches 4, 5, 6** |
| **Next Action** | Resume Wave 2b-A from **Batch 4** (skills below); apply the same edit pattern that worked for Batches 1-3 |
| **Human Gate 1** | ✅ PASSED 2026-05-16 (sign-off committed in `4a1fd56d`) |

### How to resume

Say "continue Wave 2b-A of ai-procedure-quality-r1" or "resume Phase 2b". Read this file, the audit doc (`.claude/AUDIT-FINDINGS-SKILLS.md`), and the per-batch reports (`projects/ai-procedure-quality-r1/notes/audit/batch-*.md`).

---

## What's done (skills refined this session)

### Batch 1 — committed in `2d91edf6` (with Batch 2)
- `add-reference-to-index` — frontmatter-above-H1; exemplar `none-too-volatile`; Failure Modes & Recovery
- `docs-data-model` — exemplar `docs/data-model/sprk_matter-related-tables.md`; reshape Drafting Rules as MUST/MUST NOT
- `docs-procedures` — populated `techStack: [all]`; exemplar `docs/procedures/testing-and-code-quality.md`
- `docs-standards` — populated `techStack: [all]`; exemplar `docs/standards/CODING-STANDARDS.md`
- `jps-scope-refresh` — frontmatter-above-H1; exemplar `none-too-volatile`; singular/plural Dataverse drift note

### Batch 2 — committed in `2d91edf6`
- `adr-aware` — frontmatter-above-H1; fixed stale `design-to-project` → `design-to-spec`; exemplar `none-too-volatile`
- `adr-check` — normalized minimal frontmatter (added tags/techStack/appliesTo); removed duplicate "Be thorough" tip line; exemplar `tests/Spaarke.ArchTests/`
- `ai-procedure-maintenance` — frontmatter-above-H1; fixed U+FFFD em-dash on line 79; exemplar `none-too-volatile`
- `context-handoff` — documented that the "duplicate Quick Recovery H2" was grep false-positive (inside Example: code fence); exemplar `none-too-volatile`
- `ci-cd` — **ADDED ENTIRE FRONTMATTER** (inventory anomaly #1 resolved — was zero frontmatter); exemplar `none-too-volatile`

### Batch 3 — committed in `da992a04`
- `code-page-deploy` — exemplar `src/client/code-pages/DocumentRelationshipViewer/`
- `conflict-check` — exemplar `none-too-volatile`
- `dev-cleanup` — normalized REDUCED frontmatter (added tags/techStack/appliesTo per inventory anomaly #6); exemplar `none-too-volatile`
- `docs-architecture` — populated empty `techStack: []` with `[all]`; exemplar `docs/architecture/AI-ARCHITECTURE.md`
- `docs-guide` — populated `techStack: [all]`; marked `leave-alone-justified` (hub of docs-* family); exemplar `docs/guides/DATAVERSE-MCP-INTEGRATION-GUIDE.md`

---

## What's remaining in Wave 2b-A (16 skills)

### Batch 4 (5 skills) — frontmatter-above-H1 fixes + content
- `design-to-spec` (727L) — move frontmatter ABOVE H1; exemplar (any recent spec.md, e.g., `projects/ai-procedure-quality-r1/spec.md`) OR `none-too-volatile`; note: ~80-100 lines of redundancy from embedded spec.md template — flag for future template extraction, but **don't extract in Wave 2b-A** (that's 2b-B territory if it happens)
- `spaarke-conventions` (353L) — move frontmatter ABOVE H1; exemplar `none-too-volatile`; Failure Modes & Recovery (anti-patterns: mixing Fluent v8+v9, React 18 in PCF, global auth middleware)
- `ui-test` (552L) — move frontmatter ABOVE H1; exemplar `none-too-volatile`; Failure Modes & Recovery (session timeout, dark-mode-doesn't-survive-reload). **DO NOT** rename `## Integration with task-execute` heading — task-execute Step 9.7 binds it
- `worktree-setup` (733L) — exemplar `none-too-volatile`; Failure Modes & Recovery (lift content from Isolation Rules + Troubleshooting)

### Batch 5 (5 skills) — mostly cosmetic + special handling
- `dataverse-deploy` (802L) — **hub #2 with 269 refs — ADDITIVE ONLY**. Consolidate the 4 `🚨 CRITICAL` callouts under `## Failure Modes & Recovery`; exemplar `none-too-volatile`. **Do NOT** rename top-level sections.
- `deploy-new-release` (457L) — `leave-alone` per audit. Just add Last Reviewed + Failure Modes & Recovery stub heading + exemplar `none-too-volatile`. Minimal edits.
- `doc-drift-audit` (229L) — **R2 GOLD-STANDARD COMPARATOR**. Already has `## Failure Modes & Recovery`. Just BUMP `Last Reviewed: 2026-04-05` to `2026-05-16`. Add note that this is the canonical reference. Minimal edits.
- `jps-playbook-audit` (273L) — exemplar `none-too-volatile`; reshape Conventions block as MUST/MUST NOT; Failure Modes & Recovery
- `jps-validate` (265L) — exemplar `examples/document-profiler.json` (NOTE: this path will be valid AFTER 2b-D moves the file there per Option A — for now use `none-too-volatile` and add a TODO comment). Failure Modes & Recovery.

### Batch 6 (7 skills) — final batch
- `merge-to-master` (359L) — exemplar `none-too-volatile`; Failure Modes & Recovery (from Tips for AI content); unified MUST/MUST NOT block
- `power-page-deploy` (278L) — **normalize minimal frontmatter** (add tags/techStack/appliesTo); exemplar `src/client/external-spa/`; Failure Modes & Recovery (promote inline ⚠️ warnings — especially the "deploys to a different target" warning at ~line 68); unified MUST block
- `project-continue` (530L) — exemplar `none-too-volatile`; Failure Modes & Recovery (don't skip ADR loading at Step 5; master staleness check in worktrees)
- `pull-from-github` (286L) — **normalize minimal frontmatter** (add tags/techStack/appliesTo); exemplar `none-too-volatile`; Failure Modes & Recovery
- `ribbon-edit` (414L) — **normalize minimal frontmatter** (add tags/techStack/appliesTo per anomaly #6); exemplar `none-too-volatile`; optional Failure Modes & Recovery from Critical Requirements
- `script-aware` (304L) — `leave-alone` per audit. Already has gold-pattern `## Failure Modes to Avoid` — **RENAME TO `## Failure Modes & Recovery`** for project-wide consistency. Add Last Reviewed bump. Minimal edits.
- `worktree-sync` (417L) — RENAME `## Tips for AI` → `## Failure Modes & Recovery` (content is already excellent gotchas material). Add bullet about `wc -l` PowerShell-vs-bash portability. Exemplar `none-too-volatile`.

---

## The edit pattern that worked (apply to all 16 remaining)

For each skill, apply some subset of:

### 1. Frontmatter additions (always)
Add to existing frontmatter block:
```yaml
exemplar: <real-path OR none-too-volatile>
last-reviewed: 2026-05-16
```

### 2. Audit-stamp block in body (always)
Right after the H1, replace any `> **Last Updated**: ...` line with:
```markdown
> **Last Reviewed**: 2026-05-16
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2b-A)
> **Exemplar rationale**: <one sentence — why the chosen exemplar OR why none-too-volatile>
```

### 3. Move frontmatter above H1 (if needed)
Some skills have:
```markdown
# skill-name

---
description: ...
---
```
Flip to:
```markdown
---
description: ...
exemplar: ...
last-reviewed: 2026-05-16
---

# skill-name

> **Last Reviewed**: 2026-05-16
...
```

### 4. Normalize reduced frontmatter (if needed)
Some skills only have `description:` + `alwaysApply:`. Add:
```yaml
tags: [<2-5 topic tags>]
techStack: [<tools/platforms used>]
appliesTo: [<glob patterns or trigger phrases>]
```

### 5. Failure Modes & Recovery section (always)
Add a new section before the trailing closing line, or RENAME existing `## Gotchas` / `## Anti-Patterns` / `## Tips for AI` to `## Failure Modes & Recovery`:

```markdown
## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| <symptom> | <root cause> | <prevention or recovery step> |
| <symptom> | <root cause> | <prevention or recovery step> |
| <symptom> | <root cause> | <prevention or recovery step> |
| <symptom> | <root cause> | <prevention or recovery step> |
```

Aim for 3-5 rows per skill. Draw failures from: the skill's existing Error Handling tables, Anti-Patterns sections, Tips for AI bullets, and the audit row notes in `.claude/AUDIT-FINDINGS-SKILLS.md`.

---

## Commit cadence

Commit after every 5 skills (~Batch boundary). Push after each commit. Message pattern:
```
refactor(skills): Wave 2b-A Batch N — N skills refined (M of 31 total)

<body listing each skill with its specific changes>

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
```

After all 31 done, final commit + push + `dotnet build src/server/api/Sprk.Bff.Api/` (build verification per project CLAUDE.md "Build verification between waves" rule).

---

## After Wave 2b-A completes (user's stated next step)

Per user direction earlier this session ("follow your recommendation" — option 1 then option 4 minus task-execute):

| Wave | Scope | Risk | Notes |
|---|---|---|---|
| **2b-B** | Content extraction for 4 skills: `bff-deploy` (move WEBSITE_RUN_FROM_PACKAGE section to references/), `project-setup` (merge duplicate Resources, move template content), `push-to-github` (extract PR-template H2s to references/pr-template.md), `repo-cleanup` (consolidate 3 dup `## Repository Cleanup Report` H2s; verify docs/ai-knowledge/ reference) | MODERATE | 4 dedicated mini-tasks |
| **2b-C** | Splits (4 of 5): `azure-deploy` (17 refs — also fixes 2 broken workflow refs + AP-1 stale BFF section), `code-review` (242 refs — move 600+ line Workflow body), `project-pipeline` (116 refs — also fixes MAX_THINKING_TOKENS AP-1 stale claim), `task-create` (~80 refs) | HIGH | **STOP before task-execute split** — mandatory user gate per audit doc §5 binding constraint |
| **2b-D** | Substantive rewrites (4 skills): `pcf-deploy` (fix 2 broken ADR paths + split + add AP-1 cross-ref + exemplar), `jps-action-create` (**Option A** — move 10 JSON examples to `.claude/skills/jps-action-create/examples/` per user decision 2026-05-16), `jps-playbook-design` (split + Gotchas + exemplar), `dataverse-create-schema` (lighter scope per row 12 decision — point Reference Scripts at REAL existing scripts; extract inline PowerShell to references/) | HIGH | 4 dedicated tasks |
| **Mandatory user gate** | `task-execute` split (1,234 refs) | EXTREME | Body keeps Rigor decision tree + 7 trigger phrases + FULL/STANDARD/MINIMAL vocab. Pre/post-split grep verification required. **Do NOT proceed without explicit user "go ahead" on this single skill** |

After all of Phase 2b: Phase 3a (CLAUDE.md audit) → Human Gate 2 → Phase 3b (CLAUDE.md rewrite to <200 lines) → Phase 4a (validators) → Phase 4b (GitHub Actions hardening) → wrap-up.

---

## User decisions captured 2026-05-16

| Decision | Recorded in |
|---|---|
| Row 12 `dataverse-create-schema` — lighter rewrite (point Reference Scripts at real existing scripts; no new scripts to build) | `.claude/AUDIT-FINDINGS-SKILLS.md` §4 row 12 |
| Row 23 `jps-action-create` — **Option A**: move 10 canonical JSONs to `.claude/skills/jps-action-create/examples/` | `.claude/AUDIT-FINDINGS-SKILLS.md` §4 row 23 |
| Open Q1 — keep `add-reference-to-index` orphan | `.claude/AUDIT-FINDINGS-SKILLS.md` §6 row 1 |
| Open Q2 — rewrite `dataverse-create-schema` (lighter scope per row 12) | `.claude/AUDIT-FINDINGS-SKILLS.md` §6 row 2 |
| Open Q3 — refine-in-place `jps-playbook-audit` | `.claude/AUDIT-FINDINGS-SKILLS.md` §6 row 3 |
| Open Q4 — canonical heading is `## Failure Modes & Recovery` (most descriptive for Claude Code prompting; matches doc-drift-audit gold-standard) | `.claude/AUDIT-FINDINGS-SKILLS.md` §3.3 + §6 row 4 |

---

## Critical constraints (apply to every task)

| | |
|---|---|
| **Reversibility** | Every removal/replacement goes to `.claude/archive/2026-05-14/<original-path>` BEFORE the file is changed. NEVER `rm` from git history. |
| **No application source changes** | `src/` is off-limits except for skills' code pointers (read-only verification). |
| **Sub-agent write boundary** | Sub-agents CANNOT write to `.claude/`. Phase 2b is main-session only — DO NOT dispatch sub-agents for refinements. |
| **Build verification between waves** | After Wave 2b-A completes, run `dotnet build src/server/api/Sprk.Bff.Api/` — even though we're not modifying app source, discipline check per project CLAUDE.md. |
| **Honesty in refinements** | Where the audit said "refine-in-place" but the skill is genuinely good (e.g., `script-aware`, `doc-drift-audit`), do minimal cosmetic edits only. Don't fabricate Failure Modes that don't apply. |

---

## Files Modified This Session (pre-handoff)

- `.claude/AUDIT-FINDINGS-SKILLS.md` — Human Gate 1 sign-off (commit `4a1fd56d`)
- 10 skill SKILL.md files refined in commit `2d91edf6` (Batches 1+2)
- 5 skill SKILL.md files refined in commit `da992a04` (Batch 3)
- `projects/ai-procedure-quality-r1/current-task.md` — this file (handoff state)

All committed; working tree clean for project-relevant paths.

---

## Sanity check (proven this session)

- ✅ Edit pattern works mechanically (15 skills refined cleanly)
- ✅ Skill listing runtime picks up new frontmatter immediately (full descriptions now showing for all 15)
- ✅ No cross-ref drift introduced (and one stale ref repaired: `design-to-project` → `design-to-spec` in adr-aware)
- ✅ Commit cadence (every 5-10 skills) works without lost work
- ✅ Build clean (verified `dotnet build` passes between Phase 2a and 2b)

Pattern is proven. Remaining 16 skills follow the same template.

---

## Ready state

✅ Branch checked out: `work/ai-procedure-quality-r1`
✅ Master sync: 0 commits behind origin (last fetch was during Phase 0 dispatch)
✅ PR #294 open and current at commit `da992a04`
✅ Audit doc + per-batch notes + cross-ref map committed and pushed
✅ Sub-agent permission boundary understood (no `.claude/` writes from sub-agents — Phase 2b is main-session-only)
✅ User authorized scope: Wave 2b-A → 2b-B → 2b-C up to but not including task-execute → 2b-D → mandatory user gate for task-execute

**Ready for fresh session resume: "continue Wave 2b-A of ai-procedure-quality-r1".**
