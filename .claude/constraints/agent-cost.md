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

1. **Every agent states its model and effort, chosen for the work.** There is no blanket default model. Choose from the table in "Choosing a model and effort" below, by the work's difficulty and how much verification it needs. Pass `model` on the call, use an agent definition that sets `model` and `effort` (`implementer`, `adversarial-reviewer`, `code-mapper`, `researcher`), or name `model` in every workflow `agent()` call. A `PreToolUse` hook refuses a launch that names no model, and the session picks one and re-issues the call; it never asks the user. A task's POML `<model-tier>` / `<effort>` still decide task execution (`project-pipeline` Step 5).
2. **One independent review per change set, not per fix round.** This is one full adversarial pass on the top tier (two for `auth`, `security`, `tenant-isolation`, per root §8.5). Re-checks of fix diffs are scoped to the diff and its direct callers, and run on Sonnet. Review still limits ceremony, never fixing.
3. **Small, scoped agents.** An agent re-sends its whole growing context on every tool step, so a long agent costs more than proportionally. Give each agent one deliverable, the exact files and the expected output. If a brief will clearly take more than about 100 tool steps, split it into sequential agents with short hand-off briefs, or say why it can't be split.
4. **Don't resume an idle agent after a long pause.** A sub-agent's prompt cache lives 5 minutes. Messaging an agent that has been idle longer rewrites its whole context, often 200–400k tokens, at the cache-write price. For follow-up work after a pause, start a fresh Sonnet agent with a short brief: the branch, the commits, the findings and the files.
5. **Limit concurrency.**
   - **Settings:** `CLAUDE_CODE_MAX_CONCURRENT_SUBAGENTS=4`, `CLAUDE_CODE_WORKFLOW_MAX_CONCURRENT_AGENTS=4` and `workflowSizeGuideline: "small"` (aim for fewer than 5 workflow agents). These cap one session.
   - **Across sessions:** run one or two heavy projects (fan-outs, workflow batches, parallel task execution) at a time on one machine, not five. The settings cannot enforce that.
6. **Compact main sessions earlier.** `autoCompactWindow: 400000` in `.claude/settings.json`; the default is close to the 1M window. Main sessions averaged 438k context per call. Before compaction, checkpoint as root §5 says.
7. **Read-only research fans out once.** For "map X" research, launch the mappers once with full briefs and collect their reports. Don't keep them alive for follow-up questions (rule 4).

## Choosing a model and effort

The goal is the best code per dollar: a higher tier where it changes the result, and a lower one where it doesn't. Anthropic's guidance:
- **Model** (https://code.claude.com/docs/en/model-config): Sonnet for "daily coding tasks", Opus for "complex reasoning tasks", Haiku for "simple tasks". `opusplan` is Opus to plan and Sonnet to execute.
- **When to move up** (https://claude.com/blog/claude-model-and-effort-level-in-claude-code): use a larger model when it gets things wrong despite clear context. Use higher effort when it skipped files, tests or double-checks.
- **Effort** controls how much the model reasons and how many tool calls it makes. Thinking is billed as output.
  - Higher effort costs more tokens.
  - `max` "may show diminishing returns and is prone to overthinking".
  - At lower effort, Anthropic's Opus 4.7 guidance says the model "scopes its work to what was asked rather than doing more than requested".
  - Opus 5.5 "tends to think more per turn" than Opus 5, so Anthropic suggests starting at `medium`.

| Work | Model | Effort | Why |
|---|---|---|---|
| Planning, design, architecture, ADR conflicts (`design-to-spec`, `project-pipeline` Steps 0–3) | opus, or fable for the hardest | high | Mistakes here multiply through every task |
| Root cause of a hard or unclear bug | opus | high | Reasoning across code the brief cannot name |
| Independent adversarial review of a change set (`adversarial-reviewer`) | fable | high (`xhigh` only for security-critical) | One deep pass; finds what the author missed |
| Executing a well-specified task or brief (`implementer`) | the POML's `<model-tier>`, else sonnet | the POML's `<effort>`, else high | The brief carries the thinking; Sonnet executes literally and well |
| Fixing review findings that name file:line | sonnet | high | Scoped; verification matters |
| Re-check of a fix diff and its direct callers | sonnet | high | Narrow scope; the full review already ran |
| Search, inventory, "map X", counting (`code-mapper`) | sonnet | low | Locating, not judging |
| Mechanical edits: rename, formatting, regenerating a report | haiku or sonnet | low | No reasoning needed |
| Microsoft or AI platform research (`researcher`) | opus (definition) | high | Stale training data; needs synthesis |
| Claude Code or Anthropic docs questions (`claude-code-guide`) | its built-in model (Haiku) | — | Lookup and summary; the hook exempts it |

**How effort is set.** Effort comes from the agent definition's `effort:` frontmatter, or a workflow `agent()` option. The Agent tool call in this Claude Code build takes `model` only, so per-call effort isn't available. A POML `<effort>` that differs from a definition's effort needs a workflow `agent()` or another definition; until then `project-pipeline` Step 5 runs such tasks at the definition's or the session's effort.

**Where this deliberately differs from defaults:**
- **Workflows:** Claude Code's built-in workflow guidance says to omit `model` when unsure. In this repo every `agent()` names one; that is the owner's rule.
- **Implementation effort:** Anthropic suggests `medium` for well-specified Sonnet 5.5 work. The table keeps `high` for implementation and fixes, because verification matters there (root §8.5) and the owner puts quality first.
- **Opus rows:** Opus 5.5's default effort is `medium`. The Opus rows use `high`, because they are the hard-reasoning cases (planning, root cause) where Anthropic's guidance puts `high`.

**Escalate on evidence.** If an agent's result is wrong despite a clear brief, re-run that piece one tier up and say why in a line. If it skipped verification, raise the effort before the model.

**Don't switch models or effort inside a long-running session.** It invalidates the prompt cache, and in-flight work changes hands mid-stream. Start a fresh agent at the tier you want.

**The main session's own effort** comes from `modelSettings.<model>.effortLevel` in user settings. `/effort` saves a new default (its `s` option is session-only). Change it at a session start, not mid-task.

## Settings reference

All are documented at https://code.claude.com/docs/en/sub-agents, `/model-config`, `/workflows` and `/env-vars`, confirmed 2026-10-09.

| Setting | Where | Value | Effect |
|---|---|---|---|
| `PreToolUse` hook `scripts/quality/require-agent-model.py` (matcher `Agent\|Task\|Workflow`) | `hooks` | — | Denies a sub-agent launch with no `model` (and no definition `model:`), and a workflow script with an `agent()` call naming no model. Claude re-issues the call with a model; the user is never asked. Tests: `scripts/quality/tests/test_require_agent_model.py` |
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
