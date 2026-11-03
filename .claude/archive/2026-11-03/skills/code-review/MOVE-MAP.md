# code-review restructure — MOVE-MAP (2026-11-03)

Original: `.claude/archive/2026-11-03/skills/code-review/SKILL.md` (byte-identical to the worktree SKILL.md before this change; confirmed with `cmp`).
Standard: `.claude/skills/_template/SKILL.md` HTML comment items 7–10.

## Line counts

| File | Before | After |
|---|---|---|
| SKILL.md | 1,097 (50 KB) | 470 (under the 500 cap; no `length-justification` needed) |
| references/review-checklist.md | 281 (worktree, with `## Contents`) | 282 (3 targeted edits, below) |
| references/bff-hygiene-check.md | — | 53 (moved Step 6.5 body) |
| references/poml-completeness-check.md | — | 28 (moved Step 6.7 body) |
| references/technology-checks.md | — | 62 (moved Step 7 body) |
| references/report-template.md | — | 99 (moved Step 8 template) |
| references/integration-points.md | — | 40 (moved Integration Points) |
| examples/ai-code-smells.md | — | 302 (moved Step 5.5 detail verbatim; has `## Contents`) |
| examples/invocation-examples.md | — | 36 (moved Examples) |

Files over 100 lines with `## Contents` in the first 40 lines: review-checklist.md (line 3), ai-code-smells.md (line 5). report-template.md is 99 lines and is also `*-template.md` (exempt).

## Section map (original line range → destination)

| Original | Lines | Destination |
|---|---|---|
| Frontmatter | 1–9 | SKILL.md frontmatter. Triggers (`description`, `tags`, `appliesTo`) unchanged. Added `name: code-review` (template has it). `last-reviewed` → 2026-11-03 |
| Title + `Last Reviewed` | 11–13 | SKILL.md header; stamp → 2026-11-03; added `Category: Code-quality` (template) |
| `Reviewed By` blockquote | 14 | **Dropped**: audit history ("leave-alone-justified", "no procedural split") that this restructure supersedes |
| `Exemplar rationale` | 15 | SKILL.md header, shortened. Dropped "The 600-line Workflow body IS the canonical procedure" (stale: the body was 846, then 1,097 lines) |
| `Justified length (846 lines)` | 16 | **Dropped**: stale (file was 1,097 lines) and superseded, since the file is now under the cap |
| CI/CD coverage note | 17 | SKILL.md "When to Use" paragraph 2. Dropped the "~40% / 60%" figures (unsourced) |
| Purpose | 19–21 | SKILL.md "When to Use" paragraph 2 |
| When to Use | 23–28 | SKILL.md "When to Use" |
| Inputs Required | 30–36 | SKILL.md "Inputs" + directive to `examples/invocation-examples.md` |
| Step 0 Coverage-First Contract | 40–53 | SKILL.md Step 0 (inline). Model-specific framing ("for Sonnet-5", "Sonnet 5 follows…") reworded to "a literal-following model" (template item 10) |
| Step 1 Determine Scope | 55–70 | SKILL.md Step 1 (inline), extended; see judgment calls 2 and 3 |
| Step 2 Load Context | 72–81 | SKILL.md Step 2 (inline) + conditional directive to `references/review-checklist.md` |
| Step 2.5 Metrics | 83–160 | SKILL.md Step 2.5 (inline). Detection commands condensed into one block (every command kept). Example output table → `references/report-template.md` |
| Step 2.6 Quality Direction | 162–223 | SKILL.md Step 2.6 (inline), condensed. Example tables → `references/report-template.md`. See judgment calls 3 and 4 |
| Step 3 Security | 225–250 | SKILL.md Step 3 (inline, compacted) |
| Step 4 Performance | 252–274 | SKILL.md Step 4 (inline, compacted) |
| Step 4.5 Linting | 276–297 | SKILL.md Step 4.5 (inline) |
| Step 5 Style | 299–335 | SKILL.md Step 5 (inline); absorbed "Code Smells to Flag" (977–981) |
| Step 5.5 intro | 337–339 | SKILL.md Step 5.5 (inline) |
| Step 5.5 smells 1–5 (WHY / DETECTION / EXAMPLE / SEVERITY / ACTION) | 342–607 | The rule per smell (detect, flag condition, exception, severity, action) **inline** in Step 5.5; full text **verbatim** in `examples/ai-code-smells.md` (one `##` per smell) |
| Step 5.5 output format | 610–623 | `references/report-template.md` (AI Code Smell table) + verbatim copy in `examples/ai-code-smells.md` "Report table" |
| Step 5.5 "See: review-checklist" | 625 | Replaced by the Step 5.5 directive to `examples/ai-code-smells.md` |
| Step 6 ADR Compliance | 628–662 | SKILL.md Step 6 (inline; whole always-check set kept, since `adr-check` SKILL.md:173 cites "the code-review Step 6 always-check set") |
| Step 6.5 BFF Hygiene | 664–716 | Trigger + directive + the three Critical findings **inline**; Sections A–D + severities **verbatim** in `references/bff-hygiene-check.md` |
| Step 6.55 Enforcement | 718–731 | SKILL.md Step 6.55 (inline, verbatim; short gate) |
| Step 6.6 Component Justification | 733–802 | SKILL.md Step 6.6 (inline; root CLAUDE.md §11 cites it). Anti-pattern table merged into the numbered workflow (every row kept). See judgment call 6 |
| Step 6.7 POML Completeness | 804–833 | Trigger + directive + script **inline**; body **verbatim** in `references/poml-completeness-check.md` (added the path of the 2026-07-16 finding) |
| Step 7 Technology-Specific | 835–884 | Directive by file type + the plugin = Critical gate **inline**; three checklists in `references/technology-checks.md`. See judgment call 8 |
| Step 8 Report template | 886–959 | Instruction + ordering/clustering rules **inline**; template in `references/report-template.md` (adds per-file direction tables, a conditional-steps section, triage fields, `npx eslint --fix`) |
| Conventions → Severity Levels | 963–968 | SKILL.md Conventions (inline) + one line that the triage class, not the severity, drives the caller |
| Conventions → Review Depth | 970–975 | SKILL.md Conventions (inline), redefined; judgment call 1 |
| Conventions → Code Smells to Flag | 977–981 | Folded into Step 5 "Code organization" |
| Integration Points 1–3 | 983–1018 | `references/integration-points.md` + directive at the end of SKILL.md. Item 1 updated to match task-execute Step 9.5 today (fix-scope re-review; out-of-scope F-class filed + reported; tests-modifying override) |
| Resources → Reference Files / Related Skills | 1022–1036 | SKILL.md "References" (compacted; every skill and file kept) |
| Examples 1–4 | 1038–1071 | `examples/invocation-examples.md`. Example 3 "vs main branch" → "vs master"; Examples 2 and 4 worded to match Step 0 |
| Validation Checklist | 1073–1084 | SKILL.md "Acceptance Criteria" (all 9 items kept; 2 strengthened: N/A lines, triage fields) |
| Failure Modes & Recovery | 1088–1097 | SKILL.md "Gotchas" (all 6 rows) + Constraints + Step 8 bullets. Row 1 "The 10-dimension checklist in Workflow" → "every Workflow step" (no 10-dimension checklist exists). Row 5 "the 846-line procedure… Invoke on Opus high… Don't run on Haiku" → pick model and effort from `.claude/constraints/agent-cost.md`; kept "never the mechanical tier (Haiku)" |

Content dropped outright, with reasons: lines 14 and 16 (stale audit history); the "600-line" clause of 15; the "~40%/60%" figures of 17; the `====` separators in 342–608; and in Step 2.5 the parenthetical "(DI injection count -- flag if > 5)". That parenthetical contradicted the same step's look-closer value (> 7) and its "a metric never produces a finding" rule.

## Externally cited step ids (all kept with the same meaning)

| Step | Cited by | Kept |
|---|---|---|
| Step 0 (coverage-first) | CHANGELOG; task-execute SKILL.md:753 ("coverage-first by design") | ✔ inline |
| Step 4 (performance) | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ExternalAccessModule.cs:30` | ✔ |
| Step 5.5 (AI smells) | `projects/ai-advanced-capabilities-nda-r1/notes/task-041-summary-page-docx-writer.md:82` | ✔ inline |
| Step 6 (ADR compliance / always-check set) | `adr-check` SKILL.md:152, :173; `constraints/provisioning.md:25`; sdap-SPE-admin-app-r2 design.md:505; spaarke-modal-system plan.md:226 | ✔ inline, set unchanged |
| Step 6.5 (BFF hygiene) | CHANGELOG:2114; analysis-hub-r1 notes | ✔ heading inline, body in reference |
| Step 6.55 (enforcement) | CHANGELOG:228 | ✔ inline |
| Step 6.6 (component justification) | root CLAUDE.md §11; design-to-spec SKILL.md:466; task-create SKILL.md:351; templates/task-execution.template.md:95; several POMLs and notes | ✔ inline |
| Step 6.7 (POML completeness) | task-create SKILL.md:1027; AUDIT-FINDINGS-PIPELINE-MODERNIZATION-2026-07-16.md; CHANGELOG:1962; unified-access-control-r2 notes | ✔ heading inline, body in reference |
| Steps 1, 2, 2.5, 2.6, 3, 4.5, 5, 7, 8 | not cited externally (searched `.claude/`, `docs/`, `projects/`, `scripts/`, `src/`, root) | ✔ kept anyway |

Line-number citations `code-review/SKILL.md:12…825` exist only in `projects/ai-procedure-quality-r1/notes/inventory/skill-cross-refs.json`, a dated inventory snapshot. Not updated.

## Directives added (all in "If <condition>, read … before continuing" form)

| Where in SKILL.md | Condition | Target |
|---|---|---|
| Inputs | the request names a depth or a narrow focus | `examples/invocation-examples.md` |
| Step 2 | the scope is too large to read every line closely | `references/review-checklist.md` |
| Step 5.5 | unsure whether a pattern matches a smell or is an exception | `examples/ai-code-smells.md` |
| Step 6.5 | any file under the three BFF paths | `references/bff-hygiene-check.md` |
| Step 6.7 | the diff touches `projects/*/tasks/*.poml` | `references/poml-completeness-check.md` |
| Step 7 | the scope has `.cs`, `.ts/.tsx`, or write-path/plugin code | `references/technology-checks.md` |
| Step 8 | always (every review ends here) | `references/report-template.md` |
| End of file | invoked by another skill and need the caller's use of the report | `references/integration-points.md` |

Hedges against a reference not being opened: Step 1 routes every file category to its conditional step up front. Steps 6.5 and 7 state their Critical gates inline, so those hold even if the reference is not opened. Steps 6.5 and 6.7 require an explicit "N/A" line when they do not fire. The Workflow checklist lists every step.

## Judgment calls (review these)

1. **Review Depth vs Step 0 contradiction resolved.** Original: quick = "Critical issues only", standard = "Critical + warnings". Both contradict Step 0 ("report every issue, including low-severity"). New: depth sets *how far you look*, never what you drop. Standard = every step at every severity, and is what task-execute Step 9.5 runs (it passes no depth, so the default applies). Quick (user-requested) puts style suggestions out of scope, consistent with Step 0's existing "explicitly scoped out" exception. This makes standard strictly broader than before.
2. **Step 1 gained two scope branches:** "re-review after a fix round" (moves Step 0's existing rule to the point where scope is chosen) and "branch or PR → merge-base with master" (from Example 3, which already did this).
3. **Step 2.6 baseline fixed (defect).** The original always used `git show HEAD~1:{file}`. That is wrong for uncommitted changes (the before-version is HEAD) and for multi-commit branches. The new BASE depends on how the scope was chosen and is set in Step 1. `/tmp/prev_*` → "a temp file" (Windows host).
4. **Step 2.6 "Degraded" redefined.** Original: "crossing the 30 critical line", a threshold that no longer exists anywhere (look-closer values replaced it). Now: crossing a Step 2.5 look-closer value. Added that signals are observations like the metrics, so Step 2.6 cannot become a back door to a size-based finding (§11.5).
5. **Steps 3/4/5 FLAG vocabulary**: "Info" → "Suggestion". Info was never defined; Conventions define Critical/Warning/Suggestion.
6. **Step 6.6 severity contradiction resolved toward the stricter rule.** Original item 3 and the anti-pattern table say each hollow answer → WARNING; item 4 said any hollow → SUGGESTION, all three → WARNING. Kept WARNING per hollow answer; item 4 now says "also recommend revisiting scope". The fire condition was reworded from "pure modifications to existing files" to "pure modification of existing surface", with "including new surface added inside an existing file", to match root §11 exactly. Dropped "plugins" from the list of places §11 applies (ADR-002: no plugins exist). Original item 5 ("PASS through to Step 7") dropped as redundant with the Workflow checklist.
7. **Step 8 clustering.** The original failure-mode advice said that with 50+ findings you "present the top 5 with detail, the rest as a summary count". A bare count would hide findings from task-execute triage (Step 0: nothing dropped). New: the rest are listed one line each, grouped, with counts. Also moved "check `gh pr checks`, don't restate CI" into Step 8 as "cite the CI check in one line", so a CI-reported issue is referenced, not dropped.
8. **Step 7 write-path list updated to ADR-002 as it stands.** WP-1 wording follows the 2026-10-03 amendment: the owner may be a platform-native declarative mechanism under WP-1 (a)–(c), not only the BFF. Added WP-7 and WP-8, which Step 6 already named ("WP-1…WP-8") but Step 7 omitted.
9. **Step 0 additions:** "A K class never holds a confirmed defect on a real path…" is copied from task-execute Step 9.5 / root §8.5, so the suggested class cannot be used to park a real defect. Also added "this limits review ceremony; fixing continues until no F-class finding remains", and a Constraints MUST NOT on the same point, so no line reads as "stop fixing".
10. **Step 2 directive threshold** ("too large to read every line closely: a whole-project wrap-up review, or dozens of files") is my wording; the original only listed the checklist under Resources.
11. **Step 4.5** gained "other packages: run that package's own lint script, if it has one". The original named only `src/client/pcf`, but most TS now lives under `src/solutions/` and `src/client/shared/`.
12. **review-checklist.md edited (3 rows)** despite "carry over unchanged", because it contradicted the skill. Smell 5 said "Critical (>5)" vs Step 5.5's "Never Critical on a count". The Severity Guide said "ADR violation → Warning" vs Step 6's "silent violations are ALWAYS Critical"; that row is now split into undocumented → Critical and documented Path A/B → Warning. "Long method >50 lines → Suggestion" is reworded so length alone is not the finding (§11.5). Its AI Code Smells section still duplicates `examples/ai-code-smells.md`. That duplication predates this change and is left in place; it could become a pointer later.
13. **Not added:** a directive to `docs/procedures/CODE-REVIEW-BY-MODULE.md` (see Problems below). It would close a real gap but would also bring in that doc's stale "> 5 constructor parameters" rule. Owner's call.

## Problems in other files (not fixed here)

1. `docs/procedures/CODE-REVIEW-BY-MODULE.md:186` says "Module-specific checklist | `/code-review` skill loads this document". The skill has never referenced it, in the original or in this version. Either the skill should load it (a Step 2 directive by module) or the doc should drop the claim.
2. `docs/procedures/CODE-REVIEW-BY-MODULE.md:35` "Flag if any service has > 5 constructor parameters" contradicts root CLAUDE.md §11.5, `docs/standards/COMPONENT-COMPLEXITY.md:29` (">~8–12") and code-review Step 2.5 (look closer at > 7; never a finding on a number).
3. `docs/procedures/testing-and-code-quality.md:272–300` describes the code-review skill as 4 categories ("Style → Suggestion", "ADR Compliance → Critical"). It is out of date with Steps 2.5–6.7 and with Step 6's documented-exception rule.
4. `.claude/skills/code-review/references/review-checklist.md`, ADR-010 block: `grep -c "services.Add.*<I[A-Z]" src/api/` points at a path that does not exist (the BFF is `src/server/api/Sprk.Bff.Api/`). Left unedited: it does not contradict the skill, but the command is broken.
5. `projects/ai-procedure-quality-r1/notes/inventory/skill-cross-refs.json` cites `code-review/SKILL.md:<line>` numbers that are now meaningless (historical snapshot).
6. `.claude/skills/INDEX.md:43` describes code-review as "(security, performance, style)" and omits ADR compliance. Minor.
