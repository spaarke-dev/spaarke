# task-create restructure — MOVE-MAP (2026-11-03)

Source: `.claude/skills/task-create/SKILL.md` as archived verbatim at `.claude/archive/2026-11-03/skills/task-create/SKILL.md` (1,040 lines). Output in this folder. This file is a working note for the main session; do not copy it into `.claude/skills/task-create/`.

## Line counts

| File | Before | After |
|---|---|---|
| `SKILL.md` | 1,040 | 574 (over the cap, justified: `length-justification:` key + "Read this file in full" line + `## Contents` map) |
| `examples/examples.md` | 59 | 151 (has `## Contents`) |
| `references/component-justification.md` | — | 25 (new) |
| `references/ui-tests.md` | — | 53 (new) |
| `references/goal-condition.md` | — | 36 (new) |

`scripts/quality/check-skill-length.py` was run against a throwaway git repo holding this folder: 0 violations, exit 0. The Step 4 skeleton was extracted and parsed as XML (well-formed) and passed `scripts/Validate-TaskPoml.ps1` (PASS, 0 warnings).

**Why over 500.** Every run applies Steps 3.4–3.85 to every task (eight per-task fields, each with its own decision rule) and writes the Step 4 skeleton (~95 lines). That skeleton is the authoritative POML definition the template, validator, `project-pipeline` and `code-review` point back to, so it stays inline. Getting under 500 would mean moving every-time decision trees or the skeleton behind a dereference, which is the reliability problem the 2026-05 review flagged. Only three branches are genuinely conditional, and those moved.

## Section map (original → destination)

| Original heading | Orig. lines | Destination | Notes |
|---|---|---|---|
| Frontmatter | 1–9 | SKILL.md frontmatter | Triggers (`description`, `appliesTo`) unchanged. `last-reviewed` → 2026-11-03. `length-justification:` added. No `name:` added (71 of 72 repo skills omit it). |
| `> Last Reviewed` | 13 | SKILL.md header | → 2026-11-03 |
| `> Reviewed By` (audit-history blockquote) | 14 | **Dropped** | Audit history, and stale ("leave-alone-justified" no longer true). The CHANGELOG and archive hold the history. |
| `> Exemplar rationale` | 15 | SKILL.md header `> Exemplar:` (condensed) | |
| `> Justified length` | 16 | **Replaced** by `length-justification:` + "Read this file in full" line | Its step-number point is kept in Purpose ("Step numbers are an external contract"). |
| — | — | SKILL.md `> Category` + `## Contents` | New, required by the standard |
| Purpose | 18–20 | SKILL.md Purpose | `project-init` (skill does not exist) → `project-setup` / `project-pipeline` Step 2. Added: this skill is the authoritative POML definition; the step-number contract. |
| When to Use | 22–27 | SKILL.md When to Use | `project-init` → `project-pipeline` Step 3 |
| Inputs Required | 29–35 | SKILL.md When to Use (table) | |
| Step 1 | 39–51 | SKILL.md Step 1 | Condensed |
| Step 2 | 53–64 | SKILL.md Step 2 | Condensed |
| Step 3 | 66–79 | SKILL.md Step 3 | Also absorbs File Naming (873–877) and Task Sizing (910–917) |
| Step 3.4 + Tag-to-Knowledge Mapping table | 81–120 | SKILL.md Step 3.4 (**inline**) | Table kept inline and verbatim, except `src/server/api/CLAUDE.md (if exists)` → `src/server/api/Sprk.Bff.Api/CLAUDE.md` (the old path does not exist; matches `task-execute` Step 4c). Lessons-learned scan moved here from Step 3.7 item 5, where it was misplaced. |
| Step 3.5 | 122–145 | SKILL.md Step 3.5 | Defect fixes: Testing → **ADR-038** (was ADR-022, which is PCF Platform Libraries; ADR-038 has no concise copy, so the line names `docs/adr/ADR-038-testing-strategy.md`); Authorization adds **ADR-028** (matches `adr-aware` and the auth row of Step 3.4). |
| Step 3.5.5 | 147–192 | SKILL.md Step 3.5.5 | Tree kept. The three EXAMPLE values → `examples/examples.md` Example 6 (one kept inline). PURPOSE bullets condensed into one paragraph. |
| Step 3.5.5b (model tier) | 194–233 | SKILL.md Step 3.5.5b | Tree kept. "Opus 4.8 / Fable 5" → "Opus / Fable" (standard item 10; matches root CLAUDE.md §8.5). Two of three EXAMPLES → Example 6. "(added 2026-07-08 …)" stamp dropped from the heading. |
| Effort rubric | 235–258 | SKILL.md Step 3.5.5b "Effort rubric" | Condensed, all rules kept |
| Authoring for literal execution | 260–282 | SKILL.md Step 3.5.5b "Authoring for literal execution" | All nine bullets kept. Provenance dates dropped; "why" paragraphs condensed (the test-scope "two reasons" and the seeding/task-size evidence kept). |
| Step 3.5.5c | 284–306 | SKILL.md Step 3.5.5c | Condensed |
| Step 3.5.6: XML, decision logic, scope, NOT-required list, audit trail | 308–351 | SKILL.md Step 3.5.6 (**inline** — it is a gate) | Added: mark new files `role="new"` (the validator keys new-surface detection on it). Added: "applies to new surface even inside an existing file" (root CLAUDE.md §11). |
| Step 3.5.6: anti-patterns, cost-of-rule, component-complexity check | 353–363 | `references/component-justification.md` | Conditional (new surface or a component that grows a lot). Directive at Step 3.5.6. |
| Step 3.6 | 365–412 | SKILL.md Step 3.6 | Box-drawing table → markdown table. Skill column fixed: BFF "(azure deployment commands)" → `bff-deploy` (raw deploys are a known hazard); Azure AI → Bicep scripts (`azure-deploy`). Example phase structure → condensed inline + full in `examples/examples.md` Example 4. |
| Step 3.65: visual-direction rule | 416 | SKILL.md Step 3.65 (**inline**, MUST) | |
| Step 3.65: `<ui-tests>` contents list + REQUIREMENTS | 418–427, 460–467 | SKILL.md Step 3.65 (requirements, inline) + `references/ui-tests.md` (contents list) | |
| Step 3.65: `<ui-tests>` XML example | 428–458 | `references/ui-tests.md` | Conditional (frontend tags only). Directive at Step 3.65. |
| Step 3.8 | 470–546 | SKILL.md Step 3.8 | Both auto-demotion rules verbatim in substance. "WHY THIS MATTERS" + "DESIGN FOR PARALLELISM" condensed into two short paragraphs. "See CLAUDE.md 'Sub-Agent Write Boundary'" → root CLAUDE.md §3. |
| Step 3.85: background + eligibility rubric | 548–570 | SKILL.md Step 3.85 (**inline** — every wave gets an eligibility entry) | "Claude Code v2.1.139+" and "Haiku by default" dropped (model/version detail that goes stale, standard item 10). |
| Step 3.85: condition template, RECORD format, "Why by-reference + capped" | 572–592 | `references/goal-condition.md` | Conditional (eligible waves only). Directive at Step 3.85. |
| Step 3.7 | 594–646 | SKILL.md Step 3.7 (**inline**) | All 7 items kept. Item 5's "when authoring NEW tasks, scan lessons-learned" → Step 3.4. Added a pointer: `/test-diet` gate for `090-wrapup-*` (root CLAUDE.md §7). |
| Step 4 skeleton | 648–769 | SKILL.md Step 4 (**inline**) | **Aligned with `.claude/templates/task-execution.template.md` and `Validate-TaskPoml.ps1`** (see Fixes F5). |
| POML Tag Requirements | 919–945 | SKILL.md Step 4 "POML Tag Requirements" | Merged into one paragraph after the skeleton (it repeated the skeleton and the Completeness Lint). |
| Step 5 | 771–831 | SKILL.md Step 5 | Status token table kept + reconciled (F6). G-16 rationale condensed with a link. "Added 2026-09-03 by uac-r2" provenance dropped. "Invoke Task tool" → "Agent tool". |
| Status Values | 947–952 | SKILL.md Step 5 status token table | Merged: the two vocabularies contradicted each other (F6). |
| Step 6 | 833–869 | SKILL.md Step 6 (prose spec) + `examples/examples.md` Example 5 (full block, verbatim) | |
| Conventions → File Naming | 873–877 | SKILL.md Step 3 | |
| Conventions → Standard Tag Vocabulary | 879–902 | SKILL.md Step 3.4 (**inline**) | Purpose column dropped (the category name says it). |
| Conventions → "Context Loading Benefit" | 904–908 | SKILL.md Step 3.4, one sentence | Its item 2 ("bff-api tags → reference dataverse-deploy for deployment") was wrong; replaced by the accurate statement that `task-execute` Step 4 loads by tag. |
| Conventions → Task Sizing | 910–917 | SKILL.md Step 3 | |
| Resources → Templates | 956–957 | SKILL.md Purpose + References + Gotchas ("Template drift") | |
| Resources → Related Skills | 959–963 | SKILL.md References | `project-init` → `project-setup` / `project-pipeline`; added `adr-aware`, `code-review` |
| Examples pointer | 965–970 | SKILL.md References | |
| Failure Modes & Recovery | 974–982 | SKILL.md Gotchas | All 5 rows kept. Row 1 cited "Step 3.5.5" for the tag mapping; corrected to Step 3.4. Added: template drift, emoji-only status cells. |
| Validation Checklist | 984–1006 | SKILL.md Validation Checklist (**inline**, gate) | All 20 items kept (some merged into one line). Added checks for steps that had none: `<knowledge>` non-empty (3.4), `<justification>` (3.5.6), goal-eligibility (3.85), deployment tasks (3.6), wrap-up last (3.7). Added a fix → re-check loop. |
| Completeness Lint | 1008–1028 | SKILL.md Validation Checklist → Completeness Lint (**inline**, gate) | Added "REQUIRE well-formed XML" (the script has checked it since 2026-09-18). Added: the script reports missing `<justification>`/`<ui-tests>` as WARN only, so treat both as failures (the lint text always said REQUIRE). "Added 2026-07-16" provenance dropped. |
| Portfolio Hook | 1032–1040 | SKILL.md Portfolio Hook (**inline**, runs every time) | "(added 2026-06-23 by … FR-18)" provenance dropped |
| `examples/examples.md` Examples 1–3 | (59 lines) | `examples/examples.md` Examples 1–3 | Carried over verbatim; added a note to Example 1 that a current run would also add deploy and wrap-up tasks. |

## Externally cited step ids — all preserved

Grep of the whole worktree (`.claude/`, `docs/`, `projects/`, `scripts/`, root) for lines that mention `task-create` together with a step id. Each id keeps its exact heading `### Step <id>:` and its meaning; the order of 3.8 → 3.85 → 3.7 is unchanged.

| Step id | Cited by (examples) | Preserved |
|---|---|---|
| 3.4 (Tag-to-Knowledge Mapping) | template L20, `ai-procedure-maintenance` Checklists A8/B/C + L353/L391, `project-pipeline` L544/L578 | ✅ table still inline at Step 3.4 |
| 3.5 (ADR mapping) | `project-pipeline` L550, `ai-procedure-maintenance` A8, two project design.md files | ✅ |
| 3.5.5 (rigor) | template L13, `task-execute` L140, `project-pipeline` L572, `Validate-TaskPoml.ps1`, ~48 lines total | ✅ |
| 3.5.5b (tier + effort; "Effort rubric"; "Authoring for literal execution") | root CLAUDE.md §8.5, template L14/L71, `project-pipeline` L573/L901 | ✅ all three labels kept under that heading |
| 3.5.5c (step mode + escalation) | template L15, `project-pipeline` L579–580 | ✅ |
| 3.5.6 (justification; "component-complexity check") | root CLAUDE.md §11, `code-review` L759/L802, `design-to-spec` L466, `docs/standards/COMPONENT-COMPLEXITY.md` L4/L59, `.claude/patterns/testing/god-class-ratchet.md` L18 | ✅ gate inline. The complexity check now sits in `references/component-justification.md` behind a directive *in* Step 3.5.6, so "§3.5.6 (component-complexity check)" still resolves. |
| 3.6 (deployment tasks) | `project-pipeline` L601 | ✅ |
| 3.65 (UI tests) | template L19, `Validate-TaskPoml.ps1` L148, POMLs (e.g. chat-routing-r1 021) | ✅ |
| 3.7 (wrap-up; ~88 citing lines, mostly wrap-up POMLs: "Load task-create §3.7 (wrap-up structure)") | `project-pipeline` L633, many `projects/*/tasks/090-*.poml` | ✅ all 7 items inline |
| 3.8 (parallel groups) | template L17, `project-pipeline` L574/L599, unification-r5 plan | ✅ |
| 3.85 (goal eligibility) | template L18, `project-pipeline` L907 | ✅ rubric inline; condition template behind a directive in 3.85 |
| 4 (skeleton) | template L22/L25/L33, CROSS-REFERENCE-MAP L152, CHANGELOG | ✅ |
| 5 (TASK-INDEX + "Status token table") | POMLs ("task-create Step 5 status table"; "see the Status token table + FAILURE-MODES G-16"), CHANGELOG L948 | ✅ bold label "Status token table" kept |
| Named sections: "Validation Checklist", "Completeness Lint", "Standard Tag Vocabulary", "Tag-to-Knowledge Mapping", "POML Tag Requirements" | template L52/L129/L133, `code-review` L807/L833, `project-pipeline` L591 | ✅ all still exist under those names |
| "Step 0" / "Step 0.5" attributed to task-create | ~8 POMLs in `spaarke-ai-platform-unification-r6` (084–089) and `chat-routing-redesign-r1` (021, 061, 073) | n/a — misattributions of `task-execute` Step 0.5; task-create never had a Step 0. Historical POMLs, no action. |
| "Step 3" attributed to task-create in plans | many `projects/*/plan.md` | These mean `project-pipeline` Step 3; task-create Step 3 also still exists. |

## Directives added (branch points)

| Where in SKILL.md | Directive |
|---|---|
| Step 3.5.6, after the gate | "If a task adds new surface or materially grows an existing component, read `references/component-justification.md` before writing its `<justification>`" |
| Step 3.65, end | "If any task carries one of these tags, read `references/ui-tests.md` before writing its `<ui-tests>` section." |
| Step 3.85, end | "If a wave is goal-eligible, read `references/goal-condition.md` before writing its goal condition." |

Plain pointers (not directives, because what they point to is optional): Step 3.5.5b → Example 6; Step 3.6 → Example 4; Step 6 → Example 5.

**Lookup tables — judgment call: kept inline, not moved.** The Tag-to-Knowledge Mapping (11 lines), the Standard Tag Vocabulary (11 lines) and the Step 3.5 ADR list (10 lines) are used for every task and are small. Moving them behind a "read now" directive would save about 35 lines and add a dereference on the step whose omission is the top recorded failure (empty `<knowledge>`, Gotcha 1). Because they stay at Step 3.4/3.5, **`ai-procedure-maintenance` Checklists A/B/C need no edit**.

## Fixes applied (content changes beyond the move)

These are defects the restructure surfaced. They are fixed here, in scope, and not just moved:

- **F1** `project-init` named as the producer of plan.md; no such skill exists → `project-setup` / `project-pipeline`.
- **F2** `src/server/api/CLAUDE.md (if exists)` does not exist → `src/server/api/Sprk.Bff.Api/CLAUDE.md`.
- **F3** Step 3.5 mapped Testing → ADR-022 (PCF Platform Libraries) → ADR-038. Authorization gained ADR-028.
- **F4** A Failure Modes row cited Step 3.5.5 for the tag mapping → Step 3.4.
- **F5** The Step 4 skeleton had drifted behind the template and validator. It had no `role="new|modify|canonical-reference"` on `<relevant-files>`, and `Validate-TaskPoml.ps1` detects new surface **only** by `role="new"`, so a POML that followed the skill's skeleton could never trigger the justification check. It also had no `<context><dependencies>`, no `<justification>` or `<ui-tests>` slots, and no negative-case criterion. All are now aligned. The skeleton also gained an escaped `&lt;…&gt;` in `<protocol>`, a "quote element names in prose as `&lt;name&gt;`" note (the 2026-09-18 parse-failure class) and the PCF `build:prod` note.
- **F6** Three status vocabularies disagreed: the Status Values section (`not-started/in-progress/blocked/completed/deferred`), the Step 5 table (`pending/…/completed-with-escalation/blocked-shipped`, with no cancelled or deferred) and the skeleton (`not-started`). They are merged into one table, with `not-started` emitted (`pending` accepted), `[cancelled]`/`[deferred]` rows added (which `check-task-status-drift.ps1` already recognises), and POML `blocked` documented as open work, distinct from `[blocked]` (shipped).
- **F7** Step 3.6 BFF deploy skill "(azure deployment commands)" → `bff-deploy`. Azure AI → `azure-deploy`.
- **F8** Completeness Lint: XML well-formedness added; WARN-vs-REQUIRE gap called out (see the table above).

## Other files the main session should update (outside this skill)

1. **`.claude/templates/task-execution.template.md`** L86: step N-1 reads "set this task's status to ✅" **without the mandatory `[done]` token**, which is the G-16 defect Step 5 forbids. Change it to the Step 4 wording (`✅ [done]`). L42: the status comment lists the old vocabulary; point it at "task-create Step 5 status token table". Also consider mirroring the skeleton's new `<protocol>` escape and the negative criterion (`ai-procedure-maintenance` Checklist F).
2. **`.claude/skills/adr-aware/SKILL.md`** L98: `Testing … ADR-022` has the same wrong-ADR defect as F3 → ADR-038.
3. **`.claude/skills/project-pipeline/SKILL.md`** (also being restructured): L550 says "Applicable ADRs MUST be included via docs/adr/*.md (see task-create Step 3.5)", but Step 3.5 says to use the concise `.claude/adr/` copies, so the two contradict. L596 tells authors to use `<metadata><dependencies>`, the deprecated field; it should be `<deps>`. L636 says "Creates lessons-learned.md", but Step 3.7 *appends* to the existing file.
4. **`ai-procedure-maintenance`** Checklists A8/B/C: **no change needed**, because the tables stay at Step 3.4 and 3.5.
5. **`.claude/CHANGELOG.md`**: add an entry for the restructure (with the F1–F8 fixes).
6. **`scripts/quality/skill-length.baseline.txt`**: does not exist yet in this worktree. If it is generated before this restructure lands, it will list `task-create|1040`; apply this restructure first, or delete that line afterwards (the new file passes as justified).
7. Optional: **`scripts/Validate-TaskPoml.ps1`** reports a missing `<justification>`/`<ui-tests>` only as WARN, while the skill's lint says REQUIRE. The skill now says to treat them as failures. Whether to promote them to ERROR in the script is the owner's call: it would break existing POMLs that lack `role="new"` discipline, so it may need a ratchet.
