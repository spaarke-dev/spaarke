# AI Procedure Quality R1 — Specification

> **Status**: Draft for human review (2026-05-14)
> **Project Type**: AI-procedure / repo-hygiene (no source-code changes outside `.claude/`, `scripts/quality/`, `projects/`)
> **Branch (when started)**: `work/ai-procedure-quality-r1`
> **Expected effort**: ~1 working week of agent execution (split across phases) + ~3 hours of human review
> **Cadence after launch**: monthly proactive audit; per-commit automated validation; quarterly deep audit

---

## Executive Summary

Spaarke's Claude Code procedure surface (`.claude/skills/`, `.claude/patterns/`, `.claude/adr/`, `.claude/constraints/`, `CLAUDE.md`, `.claude/settings.json`) has grown organically through two previous R-series projects (`ai-procedure-refactoring-r1` and `-r2`), both now complete. Three classes of problems have emerged that those projects didn't address and that an ongoing maintenance skill alone cannot catch:

1. **Latent drift** — facts in skills become wrong over time as the codebase moves; nothing catches contradictions until a developer hits one.
2. **Schema malformation** — settings/hook configurations can silently fail to parse, disabling permission rules and hooks without obvious symptoms.
3. **Procedure dilution** — `CLAUDE.md` grows, skills overlap, and the agent's effective attention is increasingly competed for.

This project (a) hardens the AI procedure surface against these three problems with concrete automation, (b) executes a one-time audit + refinement pass on skills and CLAUDE.md per the directives from the 2026-05-14 claude.ai consultation, (c) introduces the `researcher` subagent for accumulating external-platform knowledge across sessions, and (d) establishes an ongoing cadence so this work stays valuable rather than decaying back to where it is today.

**Why now**: the 2026-05-14 working session exposed three production-impacting bugs in our own AI procedures within a single afternoon (skill said "NEVER use `build:prod`" → 10× bundle regression; `settings.json` hooks malformed since 2026-03 → permission rules silently disabled and quality-gate hook never fired; `/bff-deploy` skill's 60-second health-check window false-failed on Linux App Service). Each was caught reactively. The cost of NOT having proactive infrastructure is now measurable.

---

## Scope

### In scope

| Area | Specifics |
|---|---|
| **One-time audits** | Skills inventory + per-skill audit; `CLAUDE.md` section-by-section audit; reference-exemplar verification for every skill that names a code path |
| **One-time fixes** | Apply approved refinements from audits; archive (not delete) anything removed; consolidate overlapping skills |
| **Standing infrastructure** | JSON-schema CI check for `.claude/settings.json` + `.mcp.json`; drift-detector for pattern-file code pointers; bundle-size guard for PCFs; reference-exemplar test harness; failure-mode catalog file |
| **New artifacts** | `researcher` subagent + memory; `.claude/skills/_template/` canonical skill scaffold; `.claude/CHANGELOG.md` for procedure changes; `.claude/archive/<date>/` for safe retirement |
| **Recurring cadence** | New skill `procedure-quality-audit` that runs the full audit + reports drift; monthly auto-schedule; quarterly deep review checklist |

### Out of scope

| Area | Why |
|---|---|
| Knowledge-base content (`spaarke/knowledge/`) | Covered by separate `coding-knowledge-base-setup-r1` project |
| MCP server inventory + cleanup | Defer to a focused MCP project once the procedure surface is stable |
| Plugin marketplace exploration | Premature — finish what's in flight first |
| GitHub CLI / branch-protection tuning | Tangential; can be a follow-up if procedure quality reveals it matters |
| Application code in `src/` | This project is procedure-side only |
| Hook proliferation | Add hooks only where a specific recurring issue is concretely documented |

---

## Background — The Three Issues Surfaced 2026-05-14

These are not hypothetical. Each happened during one working session and is the load-bearing evidence for why this project is worth doing.

### Issue 1: `/pcf-deploy` skill recommended the wrong build command

**What**: The skill stated `❌ NEVER use 'npm run build:prod' — pcf-scripts does not have a separate production build script; use 'npm run build'`.

**Reality**: `npm run build` runs `pcf-scripts build` which defaults to **development mode** (no tree-shaking, no minification). `@fluentui/react-icons` bloats by 10–15× under dev mode. Production builds require `pcf-scripts build --buildMode production`, which most PCF `package.json` files alias as `build:prod`. The skill's "NEVER" was exactly inverted from reality.

**Cost**: SpeDocumentViewer was deployed at v1.0.24 with a **6.7 MB bundle / 1.1 MB ZIP**. The known-good size (v1.0.22, committed in master) is **440 KB / 111 KB ZIP** — a 10× regression. Caught only when the user pushed back on "we shipped a bundle that's 10× larger than the committed reference."

**Generalization**: There is no automated check that a skill's prescribed build command actually produces output that matches the committed reference. The reference exemplar (SemanticSearchControl, per the skill itself) was never re-built to confirm the skill's claim.

### Issue 2: `.claude/settings.json` hook schema was malformed for months

**What**: The hooks block had entries shaped like `{ matcher, command }` but the current Claude Code schema requires `{ matcher, hooks: [{ type: "command", command }] }`. Additionally, `TaskCompleted` was used as an event name — but no such event exists in the Claude Code schema (the real events are `PreToolUse`, `PostToolUse`, `Notification`, `UserPromptSubmit`, `SessionStart`, `SessionEnd`, `Stop`, `SubagentStop`, `PreCompact`).

**Reality**: An orange "Settings file failed to parse" banner had been showing on every session since commit `50f2d7bf` (2026-03-14). The result: the `acceptEdits` mode + the entire Read/Glob/Grep/Skill/Task/Bash allow list were silently ignored; the `post-edit-lint.sh` and `task-quality-gate.sh` scripts never fired despite being executable and present.

**Cost**: ~2 months of degraded permission-prompt UX + ~2 months of "quality gate" infrastructure that wasn't running. The user noticed the banner but the parse error was generic ("Expected array, but received undefined") and unactionable.

**Generalization**: There is no CI step that validates `.claude/settings.json` against the published schema (`https://json.schemastore.org/claude-code-settings.json`). The IDE shows a banner; nothing else does.

### Issue 3: `/bff-deploy` skill's health-check window was too short for Linux App Service

**What**: The deploy script's default `MaxHealthCheckRetries = 12` × 5s = 60 second health-check window. Linux App Service after a `stop → Kudu zipdeploy → start` cycle (the auto-recover path) routinely takes 90–120 seconds before `/healthz` responds. The script reported "FAILED" at 60s, even though hash-verify had succeeded and the app was simply still booting.

**Cost**: Two falsely-reported deploy failures during one session. Each cost ~5 minutes of investigation + re-verification. More damaging: a future operator who doesn't know the hash-verify-is-authoritative distinction would re-deploy unnecessarily, multiplying the cost.

**Generalization**: The script's defaults were sized for an older Windows direct-deploy path. They drifted away from current reality. No mechanism alerts when a tunable default no longer matches observed behavior.

**Resolution (already done 2026-05-14)**: Default bumped to 24 retries × 5s = 120s. SKILL.md clarified that hash-verify is the authoritative success signal and healthz timeout after hash-verify success is NOT a real failure. But this fix was reactive.

---

## Requirements

### Functional

| # | Requirement | Acceptance |
|---|---|---|
| F-1 | Inventory every file under `.claude/skills/` and produce `AUDIT-FINDINGS-SKILLS.md` with per-skill assessment against 7 best practices | File exists with one section per skill, every skill assessed |
| F-2 | Inventory `CLAUDE.md` and produce `AUDIT-FINDINGS-CLAUDEMD.md` with section-by-section disposition + target outline | File exists, target outline reviewed by human before rewrite |
| F-3 | Execute approved skill refinements (refine in place / split / merge / archive) | All actions in audit findings executed; archives placed under `.claude/archive/<date>/`; no destructive change without explicit human sign-off |
| F-4 | Rewrite `CLAUDE.md` per approved target outline | New file < 200 lines (target < 150); old file archived; all cross-references resolve |
| F-5 | Create `.claude/agents/researcher.md` subagent per Directive 1 spec | File exists; subagent invokes successfully; isolated context confirmed; writes its `MEMORY.md` |
| F-6 | Create `.claude/skills/_template/` canonical skill scaffold | Template exists with frontmatter, body structure, gotchas, references, exemplar sections |
| F-7 | Create CI step that validates `.claude/settings.json`, `.claude/settings.local.json`, `.mcp.json` against published JSON schemas on every commit | CI step exists in `.github/workflows/` or as a pre-commit hook; deliberately-malformed file fails the check; valid file passes |
| F-8 | Create drift-detector that checks every `.claude/patterns/**/*.md` file's referenced code paths actually exist; report missing/moved files | Script under `scripts/quality/`; runs on demand and in CI; produces machine-readable report |
| F-9 | Create reference-exemplar test harness for `/pcf-deploy` (later extensible to other deploy skills): rebuild the named exemplar with the skill's prescribed command, verify bundle size within 20% of committed reference | Script under `scripts/quality/`; running it against the exemplar today returns PASS; deliberately-misconfigured `build:prod` makes it return FAIL |
| F-10 | Create bundle-size guard: a script that walks every PCF's committed `bundle.js`, records expected size, and warns when a fresh build deviates by >20% | Script + JSON record file; runs in CI for any PR that touches `.claude/skills/pcf-deploy/` or any PCF `package.json` |
| F-11 | Create `.claude/CHANGELOG.md` — chronological record of procedure-surface changes (skill added, ADR added, hook added, schema fix, etc.) | File exists with starting entries back-filled from recent git history of `.claude/` |
| F-12 | Create `.claude/FAILURE-MODES.md` — chronological catalog of things that went wrong + what was learned (anti-patterns + gotchas pulled to project level) | File exists; the three 2026-05-14 issues are the inaugural entries |
| F-13 | Create new skill `procedure-quality-audit` that runs the full audit (skills inventory, CLAUDE.md inventory, drift detector, schema validation, reference-exemplar tests, hook health smoke test) and produces a single report | Skill exists; invoking it produces `.claude/QUALITY-AUDIT-<date>.md` with PASS/FAIL across all checks |
| F-14 | Schedule `procedure-quality-audit` to run monthly via a `/loop` or scheduled remote agent; output to be reviewed by human within 1 week | Schedule exists (`scripts/quality/Schedule-MonthlyQualityAudit.ps1` or equivalent) + procedure for handling failed checks |
| F-15 | Add "Reference Exemplar" requirement to skill template — every skill that prescribes a build/deploy/operation must name a known-good code path that the audit can rebuild against | Template enforces this; existing skills with operations are audited for compliance during Directive 2 |

### Non-functional

| # | Requirement |
|---|---|
| NF-1 | **Reversibility** — every removal is an archive operation, not a delete. `.claude/archive/<date>/` preserves anything retired. |
| NF-2 | **Honesty** — audits MUST NOT fabricate problems to justify work. If a skill is in good shape, the audit says so. |
| NF-3 | **Human gates on destructive changes** — Phase 1 (additive: subagent, template, CHANGELOG, FAILURE-MODES) needs no sign-off. Phase 2 (skills audit findings → refinements) and Phase 3 (CLAUDE.md audit findings → rewrite) require explicit human sign-off on the findings document before execution. |
| NF-4 | **Atomic commits per phase** — Phase 0 inventory commit, Phase 1 additive commit(s), Phase 2 audit commit, Phase 2 refinement commit(s), Phase 3 audit commit, Phase 3 rewrite commit, Phase 4 infrastructure commit(s), Phase 5 cadence commit. Each commit independently reviewable. |
| NF-5 | **No new dependencies** — the validators and audit tools must run with the existing tooling (PowerShell, node, gh CLI, az CLI). No new runtimes or package managers introduced. |
| NF-6 | **Pin to current Claude Code version** — verify subagent persistent memory, hook schema, and agents directory are supported in the running Claude Code version before specifying behaviors that depend on them. |

---

## Success Criteria

This project is **done** when all of the following are true:

1. The audit findings for skills and `CLAUDE.md` exist, are reviewed, and the approved refinements have been executed
2. `CLAUDE.md` is under 200 lines (ideally under 150), routes rather than instructs, and every cross-reference resolves
3. Every skill has: a precise trigger description, a body under 200 lines (or split), a Gotchas section, a Reference Exemplar (if it prescribes a build/deploy operation), and no contradictions with other skills
4. The `researcher` subagent exists and has at least one verified successful invocation
5. CI validates `settings.json` schemas on every commit; deliberately-malformed config fails CI
6. The drift detector, reference-exemplar tester, and bundle-size guard are operational and run automatically on relevant PRs
7. `procedure-quality-audit` skill exists and produces a useful report on demand
8. A recurring monthly run of `procedure-quality-audit` is scheduled and producing reports
9. `.claude/CHANGELOG.md` and `.claude/FAILURE-MODES.md` exist with starter content and are referenced from `CLAUDE.md`
10. A human can read this project's `README.md` post-completion and understand both what was done and how the team keeps it from drifting back

The project is **a failure** if it ends with the same kinds of bugs surfacing reactively that we surfaced on 2026-05-14. The infrastructure must catch those classes of bug going forward.

---

## Technical Approach

### Phasing

| Phase | Theme | Gating | Estimated effort |
|---|---|---|---|
| **Phase 0** | Inventory + baseline | None | 2–3 hours |
| **Phase 1** | Additive infrastructure (researcher subagent, skill template, CHANGELOG, FAILURE-MODES, archive convention) | None | 2 hours |
| **Phase 2** | Skills audit + refinement | Human sign-off on `AUDIT-FINDINGS-SKILLS.md` before Phase 2b refinements | 3–4 hours audit + 2–4 hours refinements |
| **Phase 3** | CLAUDE.md audit + rewrite | Human sign-off on `AUDIT-FINDINGS-CLAUDEMD.md` target outline before Phase 3b rewrite | 1–2 hours audit + 1–2 hours rewrite |
| **Phase 4** | Standing infrastructure (CI schema validators, drift detector, exemplar tester, bundle-size guard) | None | 3–4 hours |
| **Phase 5** | Recurring cadence (audit skill, monthly schedule, recurring review docs) | None | 1–2 hours |

Phases 1 and 4 can run before, after, or alongside Phases 2 and 3 — they're independent. Phases 2 and 3 are sequenced: Phase 2's findings may surface content that should move to CLAUDE.md (or vice versa), so the order matters.

### Architecture decisions

| Decision | Choice | Rationale |
|---|---|---|
| Where do procedure-quality scripts live? | `scripts/quality/` (existing folder — `post-edit-lint.sh` and `task-quality-gate.sh` already live there) | Co-locate; one logical home; CI references stay clean |
| Where do audit findings live? | `.claude/AUDIT-FINDINGS-*.md` (per-audit-type) | Inside `.claude/` because they're procedure artifacts; renamed/archived after applied |
| Where does archived content go? | `.claude/archive/<YYYY-MM-DD>/<original-path>` | Single root, date-organized; easy to grep for "what was removed when" |
| How does CI run the validators? | GitHub Actions workflow `.github/workflows/procedure-quality.yml` (new) | Matches existing `.github/workflows/` convention; gated on PRs that touch `.claude/`, `scripts/quality/`, or any PCF/skill referenced by an exemplar |
| How does the recurring audit get scheduled? | `/schedule` (Claude Code scheduled remote agent) OR `scripts/quality/Run-MonthlyQualityAudit.ps1` invoked from a calendar reminder | Pick after Phase 5 prototype — both have tradeoffs (visibility, cost, reliability) |
| Researcher subagent scope | Read-only on codebase + WebSearch/WebFetch; no Edit/Write/Bash-write tools | Per Directive 1 spec; protects main session context from research noise |

### Reference exemplars (concept introduced by this project)

A **reference exemplar** is a named code path that a skill identifies as "the canonical, known-good implementation of what this skill prescribes." Every skill that gives operational guidance (build, deploy, refactor, deploy-with-version-bump) should name one.

Concretely for skills already in the repo:

| Skill | Reference exemplar |
|---|---|
| `/pcf-deploy` | `src/client/pcf/SemanticSearchControl` (current canonical virtual PCF + smallest reproducible bundle when built with `build:prod`) |
| `/bff-deploy` | `scripts/Deploy-BffApi.ps1` itself + a record of a recent known-good deploy log |
| `/code-page-deploy` | `src/solutions/LegalWorkspace` (most complex code page + the canonical Deploy-CorporateWorkspace.ps1 path) |
| `/power-page-deploy` | `src/client/external-spa` |
| `/dataverse-deploy` | (TBD — to be filled in during Phase 2) |

The exemplar-tester (F-9) periodically rebuilds each exemplar with its skill's prescribed command and verifies output matches the committed reference. If they diverge, the skill is wrong about something.

### Standing infrastructure components

```
scripts/quality/
├── post-edit-lint.sh                       (existing)
├── task-quality-gate.sh                    (existing)
├── Validate-ClaudeSettings.ps1             (new — F-7)
├── Find-PatternDrift.ps1                   (new — F-8)
├── Test-ReferenceExemplars.ps1             (new — F-9)
├── Check-BundleSizeDrift.ps1               (new — F-10)
├── Run-ProcedureQualityAudit.ps1           (new — F-13, orchestrates above)
└── Schedule-MonthlyQualityAudit.ps1        (new — F-14)

.github/workflows/
└── procedure-quality.yml                   (new — runs validators on PR)
```

---

## Additional Best-Practice Recommendations (Beyond the Three Directives)

These come from the 2026-05-14 working-session experience plus broader patterns observed in sophisticated Claude Code usage. Each is included in the spec above; this section explains the *why* in one place.

1. **Reference exemplars per operational skill** (F-15) — A skill saying "do X" should name a working example that proves X works. Today, `/pcf-deploy` says "use SemanticSearchControl as the reference exemplar" but doesn't say "and here's how to verify the skill's instructions reproduce SSC's committed bundle." Without that verification step, the skill can drift away from reality without warning.

2. **Bundle-size sanity check** (F-10) — A `stat -c '%s' bundle.js` comparison against the committed reference is the single cheapest drift detector in this codebase. Surfaces dev-mode-build-misconfigured-as-prod, regression in dependencies, accidental import of barrel files. Cost: <1 second. Value: catches the entire class of bug Issue 1 represented.

3. **Failure-mode catalog as a first-class artifact** (F-12) — Today's `Gotchas` sections live inside each skill, scoped to that skill. A repo-level `FAILURE-MODES.md` collects the cross-cutting patterns: "We had a deploy say success while files weren't replaced" applies to every deploy, not just BFF. The agent reads this before any deploy work, gaining whole-team learning.

4. **Schema validation in CI, not just IDE banner** (F-7) — The 2-month-old `settings.json` malformation shows the IDE banner alone isn't sufficient — humans dismiss banners, the parse error message ("Expected array, but received undefined") is unactionable, and the cost (silently disabled permission rules) isn't visible. CI catches it immediately and blocks merge.

5. **Drift detector for pattern-file code pointers** (F-8) — `.claude/patterns/` files exist explicitly to point at code locations. When code moves, the pointers rot. A periodic scan catches this before a developer follows a stale pointer.

6. **Procedure-surface changelog** (F-11) — Git history of `.claude/` exists but is hard to use for "when did skill X change?" or "when did the hooks last work?" A curated `CHANGELOG.md` makes drift archaeology fast.

7. **Standardized skill template** (F-6) — `.claude/skills/_template/SKILL.md` with the seven-section structure (description, when-to-use, what-to-do, gotchas, references, reference-exemplar, last-reviewed-stamp). New skills clone this. Existing skills audited against it.

8. **Skill `Last Reviewed` stamps** — Like docs/architecture has. A skill that hasn't been reviewed in 6+ months is automatically suspect and surfaced by the quality audit. (Folded into F-1 audit and into the template at F-6.)

9. **`procedure-quality-audit` skill as a single entry point** (F-13) — Rather than running 5 different validators by hand, one invocation runs them all and produces a single report. Reduces friction enough that monthly cadence becomes feasible.

10. **Anti-patterns are distinct from gotchas** — A *gotcha* is "X happens unexpectedly." An *anti-pattern* is "X looks like good practice but isn't." The 2026-05-14 `/pcf-deploy` "NEVER use build:prod" was an anti-pattern coded INTO a skill; today we have no place to log that anti-pattern so it doesn't recur. `.claude/FAILURE-MODES.md` includes both, sectioned. (Folded into F-12.)

11. **Subagent fleet (researcher first, others later)** — Directive 1 creates `researcher`. Once that's working, future named subagents earn their keep: `code-reviewer` (already partially a skill), `security-reviewer`, `data-investigator` (Dataverse multi-query investigations), `deploy-verifier` (specifically: rebuild and verify a deploy). Out of scope for R1; called out so the architecture supports them.

12. **Test the hooks actually fire** — When a hook is configured, a one-time smoke test ("edit a `.cs` file, did `post-edit-lint.sh` run?") gives confidence. Without it, the hook can be silently broken for months (as happened 2026-03 → 2026-05). Add a hook health smoke test to F-13's audit.

---

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Audit fabricates problems to justify work | Medium | High (procedure churn for no benefit) | NF-2 (honesty requirement) + human review of audit findings before refinements |
| Skill refinements introduce new bugs in skills | Medium | Medium | Phase 2.4 verification step (fresh session + representative task); reversibility via `.claude/archive/` |
| CLAUDE.md rewrite removes content the agent was actually using | Medium | High | Phase 3.5 verification + classify "Move to" not "Delete" wherever possible |
| Researcher subagent persistent memory not supported in current Claude Code version | Low | Low | Phase 1 pre-flight check; fall back to file-based persistence if memory feature unavailable |
| CI validators block legitimate PRs due to schema confusion | Medium | Low | Validators run as warnings first, promoted to errors after one week of clean signal |
| Recurring cadence (Phase 5) becomes shelf-ware | Medium | High (degrades back to today's state) | Make the audit produce actionable items, not just a report; require a human action within 1 week of each monthly run |
| This project itself becomes shelf-ware after R1 | Medium | High | Bake the cadence (F-14) into the project's deliverables — it's not "done" until the recurring run is scheduled and has produced its first report |

---

## Dependencies

- `coding-knowledge-base-setup-r1` — the researcher subagent (Directive 1) references the `spaarke/knowledge/` directory. If that's not created yet, the subagent's "Check known sources first" guidance is partially inert. Either project can proceed first; cross-reference where they meet.
- Current Claude Code version supports: subagent persistent memory, `Stop` hook event, `agents/` directory format. Verify in Phase 0.
- `az` CLI + `gh` CLI authenticated and available (for the validators that hit Azure / GitHub).

---

## References

- [design.md](design.md) — claude.ai consultation output (preserved as input)
- [docs/architecture/sdap-overview.md](../../docs/architecture/sdap-overview.md) — project architecture overview the agent should preserve awareness of
- [.claude/skills/ai-procedure-maintenance/SKILL.md](../../.claude/skills/ai-procedure-maintenance/SKILL.md) — existing reactive maintenance skill; this project extends it with proactive infrastructure
- [projects/ai-procedure-refactoring-r2/spec.md](../ai-procedure-refactoring-r2/spec.md) — prior project that handled substantive content refactoring
- [projects/coding-knowledge-base-setup-r1/SPAARKE-KNOWLEDGE-BASE-SETUP.md](../coding-knowledge-base-setup-r1/SPAARKE-KNOWLEDGE-BASE-SETUP.md) — parallel knowledge-base project
- Failure-mode evidence:
  - 2026-05-14 SpeDocumentViewer bundle regression (commit `c132773c` + `8ca796ab`)
  - 2026-05-14 settings.json hook schema parse error (commit `8ca796ab`)
  - 2026-05-14 `/bff-deploy` health-check window false-failure (commit `6d7bcf45`)

---

## Open Decisions (resolve before `/project-pipeline`)

These are choices the spec doesn't pre-commit; they should be made before task generation:

1. **Cadence schedule for `procedure-quality-audit`** — first of the month? aligned with a sprint cycle? on Mondays? Pick one and write it into F-14.
2. **CI hosting for validators** — GitHub Actions workflow vs pre-commit hook vs both? Pre-commit is faster feedback; GHA is canonical. Recommendation: both, with GHA as authoritative.
3. **Researcher subagent model** — spec says `model: opus, effort: high`. Confirm this matches the project's overall cost posture. If most main-session work runs Opus, dropping researcher to `medium` effort may be wise.
4. **Reference-exemplar threshold** — F-9 says ±20% deviation triggers FAIL. Validate this against historical SSC bundle variation; tighten or loosen as evidence supports.
5. **What goes in `.claude/CHANGELOG.md` back-fill** — back to last R-series project (April 2026)? back to repo origin? Pick a reasonable horizon.

---

*This spec is comprehensive intentionally. The implementation plan from `/project-pipeline` should fan it out into 30–60 task POMLs across the five phases. R1 here means "round 1" — a successor R2 may follow once the standing infrastructure surfaces patterns that need codifying.*
