# FINDING: POML task template drift vs. current `task-create` convention

> **Filed**: 2026-07-16 · during `/project-pipeline projects/spaarkeai-compose-r3`
> **Severity**: Medium (correctness-of-generated-artifacts; silent — no error surfaced)
> **Owner**: team responsible for project-setup discipline & hygiene (`.claude/skills/{task-create,project-pipeline}` + `.claude/templates/`)
> **Type**: Documentation/template drift — authoritative skill logic is current; the referenced template artifact is stale and contradicts it.

---

## Summary

The canonical POML template that both the `project-pipeline` and `task-create` skills point at as "the POML format" — `.claude/templates/task-execution.template.md` (v2.0, "Last Updated: December 4, 2025") — is **stale**. It is missing **every task-metadata field that the current `task-create` skill body itself mandates** as binding: `<model-tier>`, `<effort>`, `<rigor>`, `<gate>`, `<parallel-group>`, `<parallel-safe>`, `<deps>`, `<tags>`, `<phase>`, plus the body-level `<justification>`, `<steps mode="…">`, and `<ui-tests>` elements.

The result: anyone (human or agent) who follows the skill's own pointer and treats the template as the structural exemplar produces POMLs that are missing the fields the same skill declares mandatory — and nothing flags it, because the template is well-formed and the omission is silent. `task-execute` (Step 0.5) and `project-pipeline` (Step 5 dispatch) then have no `<model-tier>`/`<effort>`/`<parallel-safe>` to read.

## Evidence

**The authoritative logic is current** — `.claude/skills/task-create/SKILL.md` fully specifies the modern field set:
- `<model-tier>` + `<model-tier-reason>` — Step 3.5.5b (lines ~216–230)
- `<effort>` (low/medium/high/xhigh rubric) — Step 3.5.5b (lines ~235–256)
- `<justification>` (three-question §11 template) — Step 3.5.6 (lines ~297–336)
- `<parallel-group>` / `<parallel-safe>` — Step 3.8 (lines ~473–511); "EVERY task MUST have parallel-group and parallel-safe metadata — no exceptions"
- goal-eligibility per wave — Step 3.85 (lines ~525–561)
- Full metadata block + completeness checklist — lines ~632–636, ~929–935

**But the same skill (and the orchestrator) still cite the stale template as the format source:**
- `task-create/SKILL.md:44` — `LOAD: .claude/templates/task-execution.template.md  # POML format`
- `task-create/SKILL.md:888` — lists the template under resources
- `project-pipeline/SKILL.md` Step 3 — `LOAD: .claude/templates/task-execution.template.md (POML format)`

**The template itself** (`.claude/templates/task-execution.template.md`, v2.0 / Dec 4 2025) contains only:
- `<metadata>`: `title, status, estimated-effort, actual-effort, assigned, started, completed`
- body: `prompt, role, goal, inputs, constraints, knowledge, context, steps, tools, outputs, examples, acceptance-criteria`

### Field-level diff

| Field mandated by current `task-create` | In the template? |
|---|---|
| `<phase>` | ❌ |
| `<gate>` | ❌ |
| `<rigor>` | ❌ |
| `<model-tier>` + `<model-tier-reason>` | ❌ |
| `<effort>` | ❌ |
| `<parallel-group>` | ❌ |
| `<parallel-safe>` | ❌ |
| `<deps>` | ❌ |
| `<tags>` | ❌ |
| `<justification>` (new-component tasks) | ❌ |
| `<steps mode="directional\|prescriptive">` | ❌ (has bare `<steps>`) |
| `<ui-tests>` (PCF/frontend tasks) | ❌ |

Every real task file authored under current conventions carries these — e.g. `projects/spaarkeai-compose-r2/tasks/050-docx-annotation-writer.poml` (authored 2026-07-08). The template is the outlier, not the practice.

## Impact

- **Silent under-specification.** A pass that trusts the template emits POMLs with no execution tier, no effort, no parallel-safety, no justification. `task-execute` Step 0.5 and `project-pipeline` Step 5 dispatch then fall back to defaults or can't dispatch waves correctly; `code-review` Step 6.6 (justification concreteness) has nothing to check.
- **Forces a workaround.** In this run I could not rely on the template as the structural source; I had to cross-reference a recent same-subsystem project (`spaarkeai-compose-r2`) to recover the live field set. That workaround is undocumented and depends on picking a recently-authored exemplar — a newcomer or an agent following the literal pointer would not know to do it.
- **Root-CLAUDE.md §8.5 already declares the fields binding** ("Model tier + effort per task", "`<escalation><trigger>`", goal-eligibility), so the template is out of sync with a *binding-every-turn* rule, not just a skill.

## Root cause

The Sonnet-5 execution tuning (2026-07-08, root CLAUDE.md §8.5), the §11 component-justification rule, and the parallel-wave/goal-eligibility additions all landed in the **skill bodies and root CLAUDE.md** but the **shared template file was never updated** — and the skills' "LOAD the template (POML format)" pointers were never repointed. The template became a fossil that still holds the "source of truth for structure" label.

## Recommended remediation (pick one of A/B, then do C+D)

**A. Update the template to match the skill (lowest-friction).** Regenerate `.claude/templates/task-execution.template.md` to include the full current metadata block (all fields above), a `<justification>` stub with a "delete if not a new component" comment, `<steps mode="directional|prescriptive">`, and a `<ui-tests>` stub for PCF/frontend tasks. Bump to v3.0 with a "fields are binding — see task-create Steps 3.5.5b / 3.5.6 / 3.8 / 3.85" header.

**B. Demote the template; make the skill body the single source of truth (cleaner).** Replace `task-execution.template.md` with a short pointer file that says "the authoritative POML structure is defined in `task-create/SKILL.md` Steps 3.5.x–3.85; do not author from a static template," and repoint `project-pipeline` Step 3 + `task-create:44/888` at the skill's own inline block. Removes the possibility of future drift.

**C. Add a completeness lint (regardless of A/B).** A tiny check (script or `code-review`/`adr-check` step) that fails a POML missing `<model-tier>`, `<effort>`, `<parallel-safe>`, `<parallel-group>`, `<rigor>`, or (for new-component tasks) `<justification>`. This is what would have surfaced the drift automatically instead of relying on author diligence.

**D. Sweep for other stale template pointers.** `grep` found the same reference in `.claude/skills/_archived/design-to-project/*` (archived — ignore) and `.claude/CHANGELOG.md` (historical — ignore). Confirm `project-pipeline` and `task-create` are the only *live* pointers, and fix both together.

## Note for the record

This did **not** corrupt the `spaarkeai-compose-r3` output — the 27 generated POMLs carry the full current field set (validated against the `task-create` mandate + a live exemplar). The finding is about the **discipline surface**: the pipeline's own referenced template would mislead a less-defensive pass, and the safeguard today is author vigilance rather than the tooling.
