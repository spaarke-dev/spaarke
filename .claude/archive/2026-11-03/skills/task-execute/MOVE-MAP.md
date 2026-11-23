# task-execute restructure — move map (2026-11-03)

Source: `.claude/skills/task-execute/SKILL.md` as of `21730d5d65` (archived verbatim at `.claude/archive/2026-11-03/skills/task-execute/SKILL.md`).
This file is a review aid for the main session; do not ship it inside the skill folder.

## Line counts

| File | Before | After |
|---|---|---|
| `SKILL.md` | 1,308 lines / 67,516 bytes | 721 lines / ~46.5 KB (justified: `length-justification` + read-in-full + `## Contents`) |
| `references/parallel-waves.md` | — | 56 |
| `references/goal-wave-loop.md` | — | 21 |
| `references/surface-checklists.md` | — | 35 |
| `references/ui-testing.md` | — | 72 |
| `references/script-library-maintenance.md` | — | 30 |
| `references/multi-file-decomposition.md` | — | 65 |
| `examples/worked-examples.md` | — | 86 |

No reference/example file exceeds 100 lines, so none needs a `## Contents` list.

**Why SKILL.md stays over 500.** Everything left inline runs on every task: the Step 0.5 hot-path check, rigor tree, rigor table and declaration; Steps 0–8.5; Step 9.5 with "Finding triage and review scope" (~100 lines of binding rules cited by `code-review` and root CLAUDE.md §8.5); Steps 10, 10.6, 11 with the drift check and test-diet gate. Getting under 500 would mean moving Step 9.5 triage or the rigor/tag tables behind a link, which is exactly the dereferencing risk the earlier audit raised. The previous audit's concern is honoured for every-time steps and not applied to the conditional branches.

## Section map

| Original heading (lines) | Destination | Notes |
|---|---|---|
| Frontmatter (1–9) | inline | Added `name: task-execute` and `length-justification`; `last-reviewed: 2026-11-03`. `description`/`appliesTo`/tags unchanged. |
| `> Category` / `> Last Reviewed` (13–14) | inline | Last Reviewed → 2026-11-03. |
| `> Reviewed By` (15) | **dropped** | Audit history ("NOT SPLIT per dereferencing-reliability concern"); superseded by the owner's 2026-11-03 direction. Git keeps it. |
| `> Exemplar rationale` (16) | inline | "11-step protocol" → "step protocol" (there are more than 11 numbered steps). |
| `> Justified length (1,084 lines)` (17) | **replaced** | Stale narrative (wrong count, "catastrophic dereferencing risk"); replaced by `length-justification:` + the read-in-full line. |
| `> External API surface preserved` (18) | inline → Gotchas row "External callers silently break" | Expanded to every id actually cited in the repo (list below). Trigger phrases remain in `appliesTo` + When to Use. |
| Purpose (22–30) | inline | Condensed, same four points. |
| Permission Mode: Auto-Accept banner (34–54) | **dropped** | Instruction to the human about the Shift+Tab UI, not agent procedure; the permission mode is a session setting the skill cannot change; its one substantive sentence ("Step 9.5 runs code-review and adr-check") duplicates Step 9.5. |
| When to Use (58–64) | inline | Trigger phrases from `appliesTo` listed; cites root §4. |
| Execution Protocol header (68) | inline | Added the run order (0.3 → 0.5 → 0 → 1…11) and "never renumber". |
| Step 0.3 (70–123) | inline stub (L65–75) + `references/parallel-waves.md` | Stub keeps the two must-rules (one message for parallel-safe tasks / sequential for `parallel-safe=false`; failed build stops the run). Reference is verbatim plus one added line: each launch states model+effort (agent-cost hook would otherwise refuse it). "root CLAUDE.md 'Sub-Agent Write Boundary' section" → "§3". |
| Step 0.5 hot-path pre-step (129–137) | inline | Provenance ("added 2026-06-26 by … CICD-060") dropped. **Stale cite fixed**: `projects/INDEX.md` maintenance cited "CLAUDE.md §17"; the registry rule is in root §10. |
| Step 0.5 rigor intro (139–143) | inline | Verbatim. |
| Rigor decision tree (145–172) | inline | ASCII box drawing → plain FULL / ELSE STANDARD / ELSE MINIMAL list; criteria identical. |
| Rigor Level Protocol Requirements (174–194) | inline | Verbatim + † footnote on Step 9.5 (test-modifying tasks run gates at every level — root §8; previously only stated 560 lines later) + one sentence: steps not in the table (9.6, 9.7, 10.5, 10.6, 11) run at every level when their condition holds. |
| Mandatory declaration (196–212) | inline | Verbatim. |
| Model Tier + Effort (214–220) | inline | Dropped "added 2026-07-08 for Sonnet-5"; default now cites root §8.5 instead of naming the model as a standing fact; "Because Sonnet 5 follows instructions literally" → model-neutral (template item 10). Rules unchanged, incl. the STOP when session model < task tier. |
| Step Mode (222–228) | inline | **Stale pointer fixed**: "write BLOCKED.md — see Step 11 wave-loop notes" pointed at notes that never existed in Step 11 → now `references/goal-wave-loop.md`. |
| Sonnet-5 execution hygiene (230–232) | inline "Execution hygiene" | Model name removed; rule (keep verification that yields an artifact or gate decision) unchanged. |
| User Override (234–239), Audit Trail (241–255) | inline | Audit trail example trimmed by two lines. |
| Step 0 (259–291) | inline | "added 2026-10-15, owner direction" dropped. Resume branch **merged with "Context Recovery Protocol" (1099–1119)**: on resume, reload Step 1 + Steps 4–7 before continuing at `next_step`. The original Step 0 said "SKIP to the step in next_step" while the recovery protocol said reload knowledge + ADRs first; the merged text keeps the reload (stronger, and consistent with the rigor tree's "resuming → FULL"). Branch/worktree check added from context-handoff Failure Modes. |
| Step 1 (293–304) | inline | Added "VERIFY each `<knowledge><files>` path resolves", moved here from Failure Modes & Recovery row (1284), which already said to verify before step 1. |
| Step 2 (306–327), Step 3 (329–341) | inline | Step 3 "see Handoff Protocol below" → "RUN context-handoff". |
| Step 4, 4a (343–372) | inline | 4a table verbatim (adr-aware Rule 1 is checked against it). |
| 4b (374–386) | inline | Header "RECOMMENDED" → "RECOMMENDED; required at FULL" (matches the rigor table). Provisioning row: the inline list of 9 pattern names dropped — `.claude/patterns/provisioning/INDEX.md` is authoritative and the copy would drift. |
| 4c (388–402) | inline | + directive to `references/surface-checklists.md`. |
| Step 5 (404–422), 6 (424–430), 6.5 (432–455), 7 (457–465) | inline | Condensed formatting; 6.5 BFF deploy line adds "(via the bff-deploy skill)". |
| Step 8.0 (469–530) | inline decision rule (L390–394) + `references/multi-file-decomposition.md` | Inline keeps when to / not to delegate, the `.claude/` boundary, the one-line record, and model+effort on launch. Reference is verbatim plus two lines (put `task <NNN>` in Agent description — from Step 10; delegated files still go through 9/9.5). |
| Step 8 loop (532–587) | inline | Inline checkpoint trigger list deduplicated into the Step 8.5 table (single list; "significant decision" row merged). "deployment → FOLLOW dataverse-deploy" → "the matching deploy skill (dataverse-deploy, pcf-deploy, bff-deploy, code-page-deploy)" — dataverse-deploy alone is wrong for BFF and code pages. |
| Step 8.5 triggers, behaviour, silent/verbose (589–634) | inline | Checkpoint behaviour block condensed to the minimum fields; `context-handoff` owns the format and "State, not history". |
| Example Checkpoint Flow (636–660) | `examples/worked-examples.md` | Verbatim. |
| Step 9 (662–669) | inline | + "tick the surface checklist" directive. |
| Step 9.5 (671–749) | inline | Content unchanged; §6.5 path prose reflowed; "include files subagents changed" added to the file list. Test-modifying override provenance ("CICD-060 per spec FR-B07") condensed to date + ADR-038; the "≥6 months from 2026-06-26" window kept (see judgment calls). |
| Finding triage and review scope (751–785) | inline | Heading kept (cited by `code-review` by name). "Why this exists" condensed from 5 sentences to 2 with the same facts; owner correction kept with the quote; defect rule, F/K table, over-engineering check, review-scope rules 1–6 verbatim. |
| Step 9.7 (787–854) | inline stub (L568–572) + `references/ui-testing.md` | Stub states the applicability conditions; reference verbatim + one line: UI-test defects go through Step 9.5 triage. |
| Step 10 (856–890) | inline | Procedure verbatim + one line: notes list out-of-scope defects filed at 9.5 (already required by the triage rule's "name it in the completion report"). Why-narrative condensed; repo-wide figure (82/151) dropped, the instructions (resolve from evidence; current project only; `-All`) kept. |
| Step 10.5 (892–917) | inline directive (L601–603) + `references/script-library-maintenance.md` | Reference verbatim + root §11 justification line for new scripts. |
| Step 10.6 (919–956) | inline | Condensed; same commands and decision. |
| Step 11 (958–1021) | inline | Verbatim procedure; test-diet gate kept as binding; warning text "Spec FR-B09 requires…" → "Test diet must pass before project close" (FR-B09 is another project's spec id). Why-reset condensed, measured cost kept. |
| `/goal` Wave-Completion Loop (1025–1043) | `references/goal-wave-loop.md` | Verbatim except "Haiku by default" → "a small model by default" (model-specific, goes stale). Section title kept; project-pipeline:913 cites it by name — **update that pointer** to `references/goal-wave-loop.md`. |
| Handoff Protocol (1047–1086) | **pointer** → `context-handoff` (Step 3, Step 8.5) | The "manual update" alternative duplicates context-handoff Steps 2–4 (Capture Critical State / Rewrite / Verify and Report), which are authoritative. Its closing line ("'/checkpoint' to resume") was also wrong — `/checkpoint` saves, it does not resume. |
| Trigger Phrases for Handoff (1088–1095) | inline one sentence in Step 8.5 | context-handoff "Manual Triggers" owns the list. |
| Context Recovery Protocol (1099–1119) | merged into Step 0 | See Step 0 row. Report template kept inline. |
| PCF-Specific Checklist (1123–1132) | `references/surface-checklists.md` | Verbatim. |
| BFF API-Specific Checklist (1136–1148) | `references/surface-checklists.md` | Verbatim. Also enforced independently by `.claude/rules/bff-hygiene.md` (auto-loads on BFF edits) and root §10. |
| Deployment-Specific Checklist (1152–1162) | `references/surface-checklists.md` | Verbatim + "checkpoint after the deployment (Step 8.5)". |
| Example Execution (1166–1193) | `examples/worked-examples.md` | Updated to the current step list (adds 0.5, script-aware, 9.5, 9.6, drift check). |
| Example Recovery (1197–1217) | `examples/worked-examples.md` | Line 4 now "knowledge, constraints, ADRs (Steps 4–7)". |
| Failure Modes to Avoid (1221–1230) | inline `## Gotchas` | Merged with the second table; duplicates collapsed. |
| Related Skills (1234–1247) | inline `## Related` | Merged with the second Related; added conflict-check, devops-project-sync, pcf/bff/code-page deploy, test-diet. |
| Final Task Merge Prompt (1249–1266) | inline, Step 11 | Was misfiled under "Related Skills"; it is a Step 11 branch. |
| Related (1268–1271) | inline `## Related` | "superseded the retired AIP-00x docs on 2026-07-08" history dropped. |
| Failure Modes & Recovery (1275–1286) | inline `## Gotchas` | **Stale fixes**: `Find-SkillReferenceDrift.ps1` does not exist → `doc-drift-audit` skill; "root CLAUDE.md 'Parallel Task Execution'" → root §4; "the audit-stamp block lists the preserved surface" → list inlined in the row. The "verify knowledge paths" row became part of Step 1. Added rows: resume without reload; fix-round runaway / unfixed defect. |
| Footer (1290) "This skill ensures…" | **dropped** | Decorative; restates Purpose. |
| Portfolio Hook (1294–1308) | inline Step 9.6 | Was below the footer, after "Failure Modes" — the most likely place for a literal executor to miss an every-task step. Moved to its numbered position between 9.5 and 9.7; content unchanged (sync, promotion prompt, warn-not-block). |

## Externally cited step ids (repo-wide grep, excluding archives)

Counts are occurrences of `task-execute … Step N` in `.claude/`, `docs/`, `projects/`, `scripts/`, root CLAUDE.md.

| Id | Cites | Preserved at | Meaning unchanged |
|---|---|---|---|
| Step 0 | 69 | L193 | ✅ (resume now also reloads context) |
| Step 0.3 | 0 external (listed in the old audit stamp as protected) | L65 | ✅ |
| Step 0.5 | 181 (root §8, conflict-check ×5) | L77 | ✅ hot-path pre-step + rigor + declaration |
| Step 1 | 3 | L233 | ✅ |
| Step 2 | 1 | L248 | ✅ |
| Step 4a | 8 (adr-aware, provisioning notes) | L295 | ✅ table verbatim |
| Step 5 / 6 / 7 | 1 / 4 / 1 | L338 / L350 / L378 | ✅ |
| Step 8 | 12 | L388 | ✅ |
| Step 8.0 | 8 (project CLAUDE.md files) | L390 | ✅ judgment call, 4+ independent files |
| Step 8.5 | 3 (root §5, context-handoff) | L429 | ✅ |
| Step 9 | 14 | L453 | ✅ |
| Step 9.5 | 174 (root §8.5, code-review ×4, adr-check) | L463 | ✅ incl. "Finding triage and review scope" heading (L526) |
| Step 9.6 | 3 (project-spend-update, update-board-spend.py, HOW-TO-INITIATE) | L562 | ✅ portfolio sync |
| Step 9.7 | 26 (ui-test, project-pipeline, task-create, testing-and-code-quality) | L568 | ✅ |
| Step 10 / 10–11 | 26 (root §7) | L576 / L627 | ✅ |
| Step 10.5 | — | L601 | ✅ |
| Step 10.6 | 3 (SKILL-INTERACTION-GUIDE, parallel-claude-sessions) | L605 | ✅ |
| Step 11 | 31 | L627 | ✅ |
| Section "`/goal` Wave-Completion Loop" | project-pipeline:913 | `references/goal-wave-loop.md` | ⚠️ pointer needs updating (see below) |

## Directives added (SKILL.md line numbers)

| Line | Directive |
|---|---|
| L69 | "If invoked for more than one task, read `references/parallel-waves.md` before continuing." |
| L73 | "If the operator has run `/goal` for this wave …, read `references/goal-wave-loop.md` before continuing." |
| L166 | "…inside a `/goal` wave loop, write `BLOCKED.md` per `references/goal-wave-loop.md`" (escalation-trigger branch) |
| L336 | "If the task's tags include `pcf`, `react`, `fluent-ui`, `bff-api`, `api`, `minimal-api` or `deploy`, read `references/surface-checklists.md` before continuing." |
| L394 | "If you decide to delegate, read `references/multi-file-decomposition.md` before launching agents." |
| L461 | "If you read `references/surface-checklists.md` at Step 4c, tick every item … now — re-open it if it is no longer in context." |
| L572 | "If the task's tags include `pcf`, `frontend`, `fluent-ui` or `e2e-test`, read `references/ui-testing.md` before continuing." |
| L603 | "If the task used an existing script, changed one, repeated a command sequence 3+ times …, or made a script's behaviour obsolete, read `references/script-library-maintenance.md` before continuing." |

Pointers to other skills (not directives to local files): Step 3 / 8.5 → `context-handoff`; Step 0 → `docs/procedures/context-recovery.md`; Step 9.6 → `devops-project-sync`.

## Follow-ups for the main session (found, not fixed — outside this folder)

1. `.claude/skills/project-pipeline/SKILL.md:913` says 'See task-execute "`/goal` Wave-Completion Loop"' → change to `task-execute/references/goal-wave-loop.md`.
2. `.claude/skills/ui-test/SKILL.md:379` lists "Step 9.6: Conflict Sync Check" — wrong; conflict sync is 10.6, 9.6 is portfolio sync.
3. `.claude/skills/conflict-check/SKILL.md` auto-trigger criterion 2 says task-execute invokes `/conflict-check` when a step *modifies* a watchlist file mid-task (via `git diff` after each step). task-execute has never done this — it checks only POML inputs/outputs/tags at Step 0.5. One of the two needs to change; owner decision. Not added here (it would add a per-step check to every task).
4. `projects/customer-provisioning-orchestration-r1/notes/task-051-h8-deviations.md:3` cites "task-execute Step 12" (no such step; historical note, likely meant §6.5 / Step 9.5).
5. `.claude/skills/INDEX.md:61` trigger column lists only 3 phrases; frontmatter has 8 (pre-existing, cosmetic).
