# Spike: session naming for cross-session messaging (2026-10-17)

> **Question:** can every session have a stable address that identifies its project, so that Claude can message a project by name and a restarted session is reachable again?
> **Environment:** native Windows. VS Code extension 2.1.300 (bundled `claude.exe`). The `claude` on `PATH` is an npm global at **2.1.6**, far below the messaging minimum, so that binary can't message at all.
> **Method:** headless `claude -p` sessions started from the scratchpad with the bundled binary, `--model haiku`, and `crossSessionInbound: "accept"` passed via `--settings`. Their addresses were read with `ListAgents` from this session, and a token was sent to each.

## Results

| # | Test | Address seen in `ListAgents` | Message to the bare name |
|---|---|---|---|
| 1 | `--name coord-spike-a` | `coord-spike-a` (exact, no suffix) | Delivered (the session quoted the token back) |
| 2 | No `--name`; `SessionStart` hook returns `hookSpecificOutput.sessionTitle: "coord-spike-b"` | `scratchpad-bd` (the default) | Delivered to `scratchpad-bd` |
| 3 | A new session with the same `--name coord-spike-a` (a restart) | `coord-spike-a`, new ref | Delivered. The sender's tool result noted "a new session under a previously used name" |
| 4 | `--resume` of session 2 | `scratchpad-13` (**new** default suffix) | — |

Observed in session 2's transcript: the hook **ran**, and the transcript recorded `custom-title: coord-spike-b`. The title was set, but the messaging address was not.

## Conclusions

1. **`--name` gives a stable, restart-proof project address.** This is confirmed for CLI sessions.
2. **The default address isn't stable.** Its suffix changes on every start, even on resume of the same session. Truncation (about 40 characters) also makes some projects indistinguishable: `spaarke-wt-spaarkeai-assistant-enhanceme-*` is both r3 and r4.
3. **A hook's `sessionTitle` doesn't set the messaging address** in a headless session (2.1.300), although the docs say it has "the same effect as `/rename`". This may be a product defect; report it with `/feedback`.
4. **Not tested (needs an interactive session):**
   - whether the VS Code "Rename Session Tab" command or `/rename` in an interactive session changes the messaging address live;
   - whether an interactive session's hook `sessionTitle` does.

   This is spec FR-00's first acceptance step. It takes about 30 seconds by hand: rename one VS Code session, then run `ListAgents` from another.

## Also recorded

- `crossSessionInbound` and `dialogExpiry` are both absent from user settings today.
- `crossSessionInbound` is honoured from user, managed or `--settings` scope. A project-scope value applies only when stricter.
- `dialogExpiry` is user or managed scope only. The repo can't set either one to `accept` / `never`.
