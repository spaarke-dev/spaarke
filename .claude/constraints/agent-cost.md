# Agent cost — sub-agents, fan-outs, context size

> Owner direction 2026-10-09, after spend reached roughly $1–2.5k a day (list-price estimate from local transcripts). These are guardrails, not caps: each says what to do by default and when to do something else with a stated reason.

## Why

Measured from local Claude Code transcripts (`scripts/ai-cost/get-project-cost.py` pricing):

| | Early Sept 2026 | Oct 3–9, 2026 |
|---|---|---|
| Model calls per day (all sessions) | ~300–900 | 6,500–19,000 |
| Share made by sub-agents | 0–46% | 65–92% |
| Average context sent per call | ~300–400k tokens | ~300–380k tokens |
| Estimated spend per day | $60–240 | $1,100–2,600 |

The price of a call did not change; the **number of calls** grew about 20×, almost all of it from sub-agents and workflows. On Oct 8–9:
- **Sub-agents** made 20,381 of the 23,641 calls and cost $3,158 of $4,200.
- **About two-thirds of sub-agent cost was cache writes.** Each new or resumed agent pays to write its whole context.
- **Most sub-agents ran on Opus.** `settings.json` sets `"model": "opus"` and sub-agents inherited it.

The same parallelism exhausted the machine's memory. Bash fork failed with `0xC000012D` (commit limit) and Claude Code exited with `0xC0000409`.

## Rules

1. **Sub-agents default to Sonnet.** `CLAUDE_CODE_SUBAGENT_MODEL=sonnet` in `.claude/settings.json` `env`. Ask for a higher tier per call (`model: "opus"` / `"fable"`) only for:
   - planning and design (`design-to-spec`, `project-pipeline`, architecture questions);
   - the independent adversarial review (below);
   - a task whose POML `<model-tier>` says so.
   Implementation, fixes, searches, test runs and re-checks run on Sonnet.
2. **One independent review per change set, not per fix round.** This is one full adversarial pass on the top tier (two for `auth`, `security`, `tenant-isolation`, per root §8.5). Re-checks of fix diffs are scoped to the diff and its direct callers, and run on Sonnet. Review still limits ceremony, never fixing.
3. **Small, scoped agents.** An agent re-sends its whole growing context on every tool step, so a long agent costs more than proportionally. Give each agent one deliverable, the exact files and the expected output. If a brief will clearly take more than about 100 tool steps, split it into sequential agents with short hand-off briefs, or say why it can't be split.
4. **Don't resume an idle agent after a long pause.** A sub-agent's prompt cache lives 5 minutes. Messaging an agent that has been idle longer rewrites its whole context, often 200–400k tokens, at the cache-write price. For follow-up work after a pause, start a fresh Sonnet agent with a short brief: the branch, the commits, the findings and the files.
5. **Limit concurrency.**
   - **Settings:** `CLAUDE_CODE_MAX_CONCURRENT_SUBAGENTS=4`, `CLAUDE_CODE_WORKFLOW_MAX_CONCURRENT_AGENTS=4` and `workflowSizeGuideline: "small"` (aim for fewer than 5 workflow agents). These cap one session.
   - **Across sessions:** run one or two heavy projects (fan-outs, workflow batches, parallel task execution) at a time on one machine, not five. The settings cannot enforce that.
6. **Compact main sessions earlier.** `autoCompactWindow: 400000` in `.claude/settings.json`; the default is close to the 1M window. Main sessions averaged 438k context per call. Before compaction, checkpoint as root §5 says.
7. **Read-only research fans out once.** For "map X" research, launch the mappers once with full briefs and collect their reports. Don't keep them alive for follow-up questions (rule 4).

## Settings reference

All are documented at https://code.claude.com/docs/en/sub-agents, `/model-config`, `/workflows` and `/env-vars`, confirmed 2026-10-09.

| Setting | Where | Value | Effect |
|---|---|---|---|
| `CLAUDE_CODE_SUBAGENT_MODEL` | `env` | `sonnet` | Default model for sub-agents and workflow `agent()` calls. A per-call `model` and agent frontmatter `model:` still win. |
| `CLAUDE_CODE_MAX_CONCURRENT_SUBAGENTS` | `env` | `4` | Concurrent Agent-tool sub-agents per session (default 20) |
| `CLAUDE_CODE_WORKFLOW_MAX_CONCURRENT_AGENTS` | `env` | `4` | Concurrent workflow agents per run (default up to 16) |
| `workflowSizeGuideline` | top level | `small` | Advice to Claude: fewer than 5 workflow agents. Not a cap. |
| `autoCompactWindow` | top level | `400000` | Auto-compact threshold in tokens (100000–1000000 or `"auto"`) |

Not changed:
- **Prompt-cache TTL** (`subagentPromptCacheTtl`) stays at the 5-minute default. Rule 4 removes most rewrites without paying the 1-hour write premium on every agent.
- **Skill listing:** project skills are about 8.6k characters of the roughly 32k listing. The rest is built-in and plugin skills, about 3% of a request. Hiding skills that other skills invoke is a risk, so `skillOverrides` is not used.

## Measuring

- `python scripts/ai-cost/get-project-cost.py <slug>` gives a project's all-time total.
- `/project-spend-update` refreshes the board fields.
- For a per-day split by project, model, or main vs sub-agent, group the transcript `usage` records by `timestamp[:10]`; see the 2026-10-09 PR description for the script.
