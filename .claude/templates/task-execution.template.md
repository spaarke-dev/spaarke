# Task POML — Canonical Skeleton (Pointer)

> **Version**: 3.0 · **Last Updated**: 2026-07-16 (rewritten by pipeline-modernization sweep; supersedes the v2.0 / Dec-2025 fossil)
> **Format**: Prompt Orchestration Markup Language (POML) — valid XML; `.poml` extension. Markdown only inside text nodes.
>
> ## ⚠️ This file is a POINTER, not the source of truth
>
> The **authoritative field set + semantics** for a task POML live in
> [`.claude/skills/task-create/SKILL.md`](../skills/task-create/SKILL.md), which is the single source of truth:
>
> | Field group | Authoritative rule |
> |---|---|
> | `<rigor>` + `<rigor-reason>` | task-create **Step 3.5.5** (rigor decision tree) |
> | `<model-tier>` + `<model-tier-reason>` · `<effort>` | task-create **Step 3.5.5b** (Sonnet-5 tiering + effort rubric) |
> | `<steps mode="…">` · `<escalation><trigger>` | task-create **Step 3.5.5c** (step mode + escalation) |
> | `<justification>` (NEW-surface tasks) | task-create **Step 3.5.6** (§11 three-question gate) |
> | `<parallel-group>` + `<parallel-safe>` | task-create **Step 3.8** (wave grouping — EVERY task) |
> | goal-eligibility (per **wave**, recorded in TASK-INDEX.md) | task-create **Step 3.85** |
> | `<ui-tests>` (pcf/frontend/fluent-ui/e2e-test) | task-create **Step 3.65** |
> | `<knowledge>` (tag → files) | task-create **Step 3.4** (Tag-to-Knowledge Mapping) |
>
> **Do NOT author a task by copying this skeleton alone** and stopping — run `task-create` (or read its Step 4
> block) so the per-field decision logic is applied. The skeleton below exists so you can see the *shape* the
> skill produces without a round-trip; the skill decides *what goes in each field*. Keeping the skeleton here in
> sync with task-create Step 4 is a maintenance obligation (see `ai-procedure-maintenance` Checklist F).
>
> **Execution protocol** (Steps 0.5 → 11, rigor gates, checkpointing) lives in
> [`.claude/skills/task-execute/SKILL.md`](../skills/task-execute/SKILL.md) — NOT here. A task file is *data*; how it
> runs is the skill.

---

## Current canonical skeleton (mirror of `task-create` Step 4)

```xml
<?xml version="1.0" encoding="UTF-8"?>
<task id="{NNN}" project="{project-name}">
  <metadata>
    <title>{Task Title}</title>
    <phase>{N} {Phase Name}</phase>
    <gate>startable | blocked | {dep-condition}</gate>       <!-- readiness -->
    <status>not-started</status>                              <!-- not-started | in-progress | completed | blocked | deferred -->
    <rigor>FULL | STANDARD | MINIMAL</rigor>                  <!-- Step 3.5.5 (author hint; task-execute may override) -->
    <rigor-reason>{trigger from the Step 3.5.5 decision tree}</rigor-reason>
    <model-tier>sonnet | opus | fable</model-tier>           <!-- Step 3.5.5b; sonnet default -->
    <model-tier-reason>{trigger}</model-tier-reason>
    <effort>low | medium | high | xhigh</effort>             <!-- Step 3.5.5b rubric; high default, xhigh only where justified -->
    <parallel-group>{group id or "none"}</parallel-group>    <!-- Step 3.8 -->
    <parallel-safe>true | false</parallel-safe>              <!-- Step 3.8; FORCED false if <relevant-files> touches .claude/ -->
    <parallel-reason>{why, when parallel-safe is false}</parallel-reason>
    <deps>{comma-separated task IDs or "none"}</deps>
    <tags>{standard vocabulary — see task-create Standard Tag Vocabulary}</tags>
    <estimated-effort>{optional, e.g. 2-4 hours}</estimated-effort>
  </metadata>

  <prompt>{1-3 sentences: what to accomplish + why. Explicit, literal — Sonnet-5 does not infer intent.}</prompt>
  <role>SPAARKE platform developer. {Specific expertise needed.} Follow ADRs strictly.</role>
  <goal>{Clear, measurable definition of done — the artifact(s) that will exist.}</goal>

  <context>
    <background>{Why this task exists — from spec.md/plan.md.}</background>
    <relevant-files>
      <file role="new|modify|canonical-reference">{exact path — name the reference impl to copy}</file>
    </relevant-files>
    <dependencies>
      <dependency task="{NNN}" status="pending|complete">{what it provides}</dependency>
    </dependencies>
  </context>

  <constraints>
    <!-- Each constraint carries an explicit SCOPE clause; a bare rule is a defect (Step 3.5.5b authoring rule). -->
    <constraint source="ADR-{NNN}">{scoped rule}</constraint>
    <constraint source="project">{project-specific rule}</constraint>
  </constraints>

  <knowledge>
    <topic>{domain}</topic>
    <files><file>.claude/adr/ADR-{NNN}-{slug}.md</file></files>          <!-- concise ADR; Step 3.4 mapping -->
    <patterns><pattern name="{name}" location="{path}">{how to apply}</pattern></patterns>
  </knowledge>

  <steps mode="directional">   <!-- directional (default): goal+criteria+constraints bind, sequence adaptable.
                                    prescriptive: exact sequence binds (migrations/deploys/irreversible) — Step 3.5.5c -->
    <step order="0" name="Context + rigor declaration">{...}</step>
    <step order="1">{concrete action}</step>
    <step order="N-1">Update TASK-INDEX.md: set this task's status to ✅.</step>
    <step order="N">Document any deviation in projects/{project-name}/notes/.</step>
  </steps>

  <escalation>   <!-- REQUIRED for tasks with a known judgment boundary / failure mode (Step 3.5.5c) -->
    <trigger>{If X differs from the spec, STOP and escalate per CLAUDE.md §6 rather than adapting.}</trigger>
  </escalation>

  <justification>   <!-- REQUIRED only for NEW-surface tasks (new file/endpoint/DI/package/column) — §11 / Step 3.5.6.
                         OMIT for modify-only tasks. Hollow answers fail code-review Step 6.6. -->
    <existing>{closest neighbor — cite file:line from Grep, or "none found" + the grep run}</existing>
    <extension>{Yes/No + reason ≤2 sentences. "Cleaner separation" is NOT a reason.}</extension>
    <cost-of-doing-nothing>{concrete behavior/contract that fails — NOT "scalability"/"flexibility".}</cost-of-doing-nothing>
  </justification>

  <ui-tests>   <!-- REQUIRED for tags pcf/frontend/fluent-ui/e2e-test — Step 3.65. Include ADR-021 dark-mode check. -->
    <test name="{name}"><steps><step>{action}</step></steps><expected>{outcome}</expected></test>
  </ui-tests>

  <tools><tool name="dotnet">Build/test .NET</tool><tool name="npm">Build PCF/TS (build:prod for PCF)</tool></tools>

  <outputs>
    <output type="code">{exact path}</output>
    <output type="test">{exact path}</output>
  </outputs>

  <acceptance-criteria>
    <!-- CLOSED SET: exhaustive, not illustrative. Include negative/authorization cases (401, empty-input, unauthorized). -->
    <criterion testable="true">Given {precondition}, when {action}, then {result}.</criterion>
    <criterion testable="true">Negative: {unauthorized/malformed path} returns {expected}, not an unhandled error.</criterion>
    <criterion testable="true">All unit tests pass; build is green.</criterion>
  </acceptance-criteria>

  <notes>{Implementation hints; spec.md section refs. Completion summary appended here when done.}</notes>

  <execution><skill>.claude/skills/task-execute/SKILL.md</skill></execution>
</task>
```

---

## Completeness gate (what a valid task POML MUST carry)

A task POML is **incomplete** (and fails the `task-create` Validation Checklist + `code-review` POML check) if it is
missing any of: `<model-tier>`, `<effort>`, `<rigor>`, `<parallel-group>`, `<parallel-safe>`, `<steps mode="…">`, or —
for a NEW-surface task — `<justification>`. This is the exact drift that produced the 2026-07-16 finding; the gate
exists so the omission is caught mechanically rather than by author vigilance. See
`scripts/Validate-TaskPoml.ps1` and `task-create` Validation Checklist.

> **Deprecated field aliases** (accepted for back-compat, not emitted by new task-create runs): `<rigor-hint>` → use
> `<rigor>`; `<dependencies>` (as a metadata sibling) → use `<deps>`. The `<dependencies>` element is still valid
> *inside* `<context>` to describe prerequisite tasks.

---

*Template version: 3.0 | POML pointer | authoritative source: `.claude/skills/task-create/SKILL.md`*
