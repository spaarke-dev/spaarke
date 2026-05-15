# Researcher Subagent Verification

> **Created**: 2026-05-14 by task 010
> **Status**: File-creation verified; functional smoke test deferred to user

## What was created

- `.claude/agents/researcher.md` — Opus, effort: high researcher subagent per design.md Directive 1

## Frontmatter as written

```yaml
name: researcher
description: Use for deep-dive technical investigation of Microsoft AI platform APIs, library behavior, unfamiliar errors, or comparing platform options. Accumulates findings across sessions via project memory. Do NOT use for project task execution, code review, or spec writing — those have dedicated patterns.
tools: Read, Bash, Glob, Grep, WebSearch, WebFetch
model: opus
effort: high
memory: project
```

## Version check

- **Claude Code CLI version observed**: `2.1.6` (per `claude --version`)
- **Task POML required**: `v2.1.33+` for subagent persistent memory feature

The version observed is the CLI shell launcher (separate from the VSCode-extension runtime, which is on Opus 4.7 per the environment description). The CLI version string does NOT necessarily indicate whether subagent persistent memory is supported in the active runtime — that depends on the harness/extension version.

**Decision**: Created the file anyway. Subagent persistent memory is a *feature* that may or may not be exercised; the file is benign even if the feature is unavailable. If the user invokes the researcher and persistent memory doesn't work, the symptom will be clear (no `MEMORY.md` written) and the fix is to upgrade the runtime, not to change the file.

## Functional smoke test — deferred

Per the task acceptance criteria:
- ❓ "The agent successfully invokes when explicitly delegated to (test with: 'Use the researcher subagent to find out the current preview status of Foundry IQ remote sources')"
- ❓ "The subagent operates in an isolated context"
- ❓ "The subagent creates and writes to its `MEMORY.md`"

These cannot be reliably verified by the current main session — `Agent` tool spawns sub-agents of types defined elsewhere (e.g., `general-purpose`, `Explore`, `Plan`), not arbitrarily named ones. To exercise this researcher subagent specifically, the user would invoke it explicitly (e.g., "Use the researcher subagent to investigate X"). This is a user-driven smoke test.

**Recommended user smoke test** (do this in a fresh session):
```
> Use the researcher subagent to find out the current preview status of Microsoft Foundry IQ remote sources.
```

Expected outcomes:
1. Claude Code dispatches the request to a sub-agent (visible in the UI as a delegated task)
2. The sub-agent's intermediate tool calls do NOT appear in the main conversation
3. The sub-agent returns a structured Summary/Details/Sources/Caveats response
4. A `MEMORY.md` file is created in the project memory directory (`C:\Users\<user>\.claude\projects\<project-id>\memory\` or similar — check via `Get-ChildItem` after the test)

If any of these fail, the symptom hints at fix:
- Sub-agent type not recognized → naming convention may have changed in the runtime; check Anthropic's current subagent docs
- Tool calls visible in main session → "memory: project" frontmatter may not be honored (older runtime); upgrade
- No MEMORY.md created → persistent memory feature requires v2.1.33+ CLI or equivalent runtime

## Acceptance criteria status

| Criterion | Status |
|---|---|
| `.claude/agents/researcher.md` exists with correct frontmatter | ✅ Done |
| Test invocation succeeds and produces structured findings | ⏭️ Deferred to user smoke test |
| `MEMORY.md` is created with at least one entry | ⏭️ Deferred to user smoke test |
| Subagent's tool use does not pollute main session context | ⏭️ Deferred to user smoke test |

The 3 deferred criteria can only be exercised by a real invocation — which is the user's call to make. The file itself is in place and structurally correct.

---

*Task 010 — `010-create-researcher-subagent.poml`. See also `.claude/CHANGELOG.md` for the release entry.*
