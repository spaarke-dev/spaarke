# Additional Best-Practice Notes

> These notes were the working draft of "things to add beyond the three claude.ai directives." The high-signal items are now folded into `spec.md`. This file is preserved as the longer-form reasoning record in case anything is removed from the spec for scope reasons and needs to be re-examined later.

## Inventory of suggestions considered

### Folded into spec.md

1. **Reference exemplars per skill** — every operational skill names a known-good code path; the audit harness re-verifies against it (F-9, F-15)
2. **Bundle-size sanity check** — `stat -c '%s' bundle.js` against committed reference (F-10)
3. **Failure-mode catalog as a first-class artifact** — `.claude/FAILURE-MODES.md` (F-12)
4. **Schema validation in CI** — settings.json + .mcp.json validated against published schemas (F-7)
5. **Drift detector for pattern-file code pointers** — periodic scan that pattern.md references resolve (F-8)
6. **Procedure-surface changelog** — `.claude/CHANGELOG.md` (F-11)
7. **Standardized skill template** — `.claude/skills/_template/` (F-6)
8. **`Last Reviewed` stamps on skills** — folded into F-1 and F-6
9. **`procedure-quality-audit` as single entry point** — F-13
10. **Hooks actually fire** — smoke test in F-13
11. **Subagent fleet** — researcher first (F-5); others called out as out-of-scope for R1
12. **Anti-patterns distinct from gotchas** — folded into FAILURE-MODES sectioning (F-12)

### Considered but deferred (not in R1 spec)

These are real practices used by sophisticated Claude Code teams but each has a non-trivial setup cost and would have stretched R1 beyond the "one-week" effort budget. Each is a candidate for a follow-up R2 if R1 surfaces evidence they matter.

13. **Standardized effort tagging across all skills** — every skill declares an explicit `effort: low|medium|high|max`. Today this is implicit in the model's adaptive thinking; making it explicit per-skill would help cost-tracking. Cost: visit every skill's frontmatter. Defer to R2.

14. **PR-based protection for `.claude/` specifically** — the repo uses admin-bypass on master. For `.claude/` changes specifically, requiring PR review would have caught the 2026-03 hook schema bug. Trade-off: more friction for routine procedure edits. Defer until R1's CI validators are in place and we can measure whether they're sufficient.

15. **Cost telemetry / team-level monitoring** — `/status` is per-session. Team-level visibility into "which skills are firing most, what's the token cost per task" would inform R2 tuning. Not viable today without external telemetry; defer.

16. **Operator handoff doc** — "new developer joining the team: start here." Aspirational. Defer until CLAUDE.md rewrite (Phase 3) is done; the result of that may be the handoff doc.

17. **Anti-pattern playbook** — beyond a catalog file, an actual "when you see X, do Y" decision tree. Premature without 6+ months of catalog entries to learn from. Defer.

18. **Cross-session memory pruning** — auto-memory accumulates. Need a way to age out stale entries. Not urgent today (memory is small). Defer to R2.

19. **MCP server hygiene audit** — `.mcp.json` enumerates the Dataverse MCP server. Need to confirm which tools are actually used vs noise. Tangential; called out as out-of-scope in the spec.

20. **Per-skill version numbers** — like ADRs. Lets the changelog reference "skill X v1.0 → v1.1." Marginal benefit over `Last Reviewed` stamps. Defer.

21. **Skill creation discipline / threshold** — "when do we add a new skill vs extend an existing one?" The R1 audit will produce empirical data on this; codify in R2.

22. **`/loop` cadence tuning** — the F-14 monthly schedule may be too infrequent or too frequent depending on observed drift rates. Pick a starting value (monthly) and adjust based on three months of observation.

## Patterns observed in sophisticated Claude Code teams (research notes)

These are patterns I've seen referenced in the broader Claude Code community / Anthropic guidance, included as context for why some of the above suggestions made the cut:

- **"Skills are advisory, settings are binding"** — this is core. Anything that MUST happen every time belongs in `settings.json` or hooks, not in skill prose. The 2026-03 hook malformation made this visible: the quality gate was prose-only when it should have been a hook.

- **"Trust the agent within constraints"** — over-prescriptive skill prose ("step 1, step 2, step 3") fights against the model's strengths. The new skill template should encourage goal-oriented framing.

- **"The description field IS the trigger"** — Claude Code does fuzzy matching on it to decide whether to load a skill. Descriptions that bury the trigger phrase in the middle of a paragraph perform worse than descriptions that lead with the trigger.

- **"CLAUDE.md is the router, not the manual"** — context-budget thinking. Every line in CLAUDE.md is paid for in every session.

- **"Subagent for accumulating knowledge, main session for using it"** — the researcher pattern. Keeps deep dives out of the main session's context window.

- **"Failure modes are higher signal than success modes"** — what tripped us up is more useful to record than what worked. `FAILURE-MODES.md` operationalizes this.

- **"Latent bugs in the procedure surface compound silently"** — the 2-month-old `settings.json` bug. CI validation is the only reliable defense.

## Open questions for the human reviewer

These are decisions that should be made before `/project-pipeline` generates the implementation tasks:

1. **R1 scope is ambitious — should we split?** R1 covers 15 functional requirements across 5 phases. An alternative: split into R1a (Phase 0 + Phase 1 + Phase 4 — additive infrastructure only, no audit) and R1b (Phase 2 + Phase 3 + Phase 5 — audits + cadence). Trade-off: more momentum loss between, but each is more digestible.

2. **Reference-exemplar deviation threshold** — 20% feels right empirically (SDV's 6.7 MB vs 440 KB = 15× = obviously broken; a 30% variance is normal day-to-day jitter). But validate by sampling actual build-to-build variation in the working PCFs over a week.

3. **What gets archived vs left in place** — the spec says ALL removals go to `.claude/archive/<date>/`. For very small skills (< 50 lines), the archive overhead may exceed the value. Consider a size threshold below which it's OK to just delete from git history (the history itself is the archive).

4. **Researcher subagent — start with one or with three?** Spec says one (researcher) for R1. The directive doc agrees. But once the pattern is proven, `code-reviewer`, `data-investigator`, and `deploy-verifier` are obvious follow-ups. Question: name them as R2 work, or sketch them in R1 so the architecture supports them from day one?
