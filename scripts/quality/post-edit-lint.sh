#!/usr/bin/env bash
# ============================================================================
# post-edit-lint.sh — Claude Code PostToolUse Hook for Edit operations
#
# Automatically lints files after Claude Code edits them.
# Dispatches to the correct linter based on file extension:
#   .ts/.tsx  → the package's own eslint, run from its ESLint-config folder
#   .ps1      → Invoke-ScriptAnalyzer via pwsh
#   .cs       → not here (cannot finish in 4 s; see the case statement)
#
# Registered in .claude/settings.json as a PostToolUse hook with matcher "Edit|Write".
#
# Exit codes:
#   0 — Always (lint violations are advisory, not blocking)
#   Non-zero — Only if hook infrastructure itself fails (should not happen
#              due to ERR trap, but theoretically possible)
#
# Constraints:
#   - Must complete in < 5 seconds (timeout 4 on all linter invocations)
#   - Must not crash the Claude Code session
#   - Must handle missing linters gracefully
# ============================================================================

# --- Global error trap: ensure we never crash Claude Code ---
trap 'echo "[post-edit-lint] Hook error (non-fatal): $BASH_COMMAND" >&2; exit 0' ERR
set -uo pipefail
# NOTE: We intentionally do NOT use `set -e` because the ERR trap + pipefail
# is sufficient, and `set -e` can interact poorly with the trap in some shells.

# --- Read stdin JSON payload from Claude Code ---
# The PostToolUse hook receives JSON on stdin with tool_input containing file_path.
INPUT=$(cat)

# --- Extract file path (pure bash, no fork) ---
# This hook runs after EVERY edit, so its fixed cost matters: on a loaded Windows machine each fork
# (jq lookup, python start) measured 6-8 s, ~18 s per edit even for files it never lints. A bash
# regex reads file_path; a file type it does not lint exits before any fork.
FILE=""
if [[ "$INPUT" =~ \"file_path\"[[:space:]]*:[[:space:]]*\"(([^\"\\]|\\.)*)\" ]]; then
  FILE="${BASH_REMATCH[1]}"
  FILE="${FILE//\\\\/\\}"   # JSON-unescape backslashes (Windows paths)
  FILE="${FILE//\\\//\/}"   # and escaped slashes
fi
[ -n "$FILE" ] || exit 0

BASENAME="${FILE##*[\\/]}"
EXT_LOWER="${BASENAME##*.}"
EXT_LOWER="${EXT_LOWER,,}"
case "$EXT_LOWER" in ts|tsx|ps1) ;; *) exit 0 ;; esac

[ -f "$FILE" ] || exit 0   # deleted, or not a real path

# Prefer `python` (used only to JSON-encode findings): on Windows, `python3` is often the Microsoft
# Store alias stub (…/WindowsApps/python3), which does not reliably forward stdin and has been seen
# spinning at 100% CPU for days (2026-10-13, 2026-10-15). Skip that stub entirely.
PY=""
for cand in python python3; do
  p=$(command -v "$cand" 2>/dev/null) || continue
  case "$p" in *WindowsApps*) continue ;; esac
  PY="$cand"; break
done

# --- Dispatch to appropriate linter ---
# FINDINGS holds only lines that are real lint findings (filtered per tool), so a clean file, a
# timeout (exit 124, no output), a missing config or a missing tool never reaches the agent.
FINDINGS=""

# Run "$@" (after cd to $2) under a hard cap of $1 seconds; print its output. On timeout the whole
# Windows process tree is killed: plain `timeout` kills only the direct child, which is how
# `dotnet format` left 97 BuildHost processes running for days (found 2026-11-09).
capped_in_dir() {
  local secs="$1" dir="$2"; shift 2
  local tmp="${TMPDIR:-/tmp}/post-edit-lint.$$.out" pid wp end=$((SECONDS + secs))
  ( cd "$dir" && exec "$@" ) >"$tmp" 2>&1 </dev/null &
  pid=$!
  # Wall-clock deadline: counting sleep iterations drifts badly on a loaded machine (measured 65 s
  # for a "4 s" cap).
  while kill -0 "$pid" 2>/dev/null; do
    if [ "$SECONDS" -ge "$end" ]; then
      wp=$(cat "/proc/$pid/winpid" 2>/dev/null || true)
      if [ -n "$wp" ] && command -v taskkill >/dev/null 2>&1; then taskkill //T //F //PID "$wp" >/dev/null 2>&1 || true; fi
      kill -9 "$pid" 2>/dev/null || true
      break
    fi
    sleep 0.2
  done
  wait "$pid" 2>/dev/null || true
  cat "$tmp" 2>/dev/null || true
  rm -f "$tmp" 2>/dev/null || true
}

case "$EXT_LOWER" in
  # C# (.cs) is deliberately not linted here. Even whitespace-only `dotnet format --folder` takes
  # ~12 s and a full `dotnet format` loads the MSBuild workspace (minutes), so neither fits this
  # hook's 4 s budget: the old call never finished, never reported, and leaked a BuildHost per
  # edit. C# formatting is checked by the Stop gate (task-quality-gate.sh) and CI tier2 `format`.

  ts|tsx)
    # ESLint from the nearest folder holding an ESLint config, with that package's own eslint, so
    # its config and plugins resolve. Default "stylish" output: the "compact" formatter left
    # ESLint core in v9, so the old `--format compact` call exited 2 on every edit.
    REL=$(realpath --relative-to=. "$(cygpath -u "$FILE" 2>/dev/null || echo "$FILE")" 2>/dev/null) || exit 0
    case "$REL" in ../*|/*) exit 0 ;; esac
    D=$(dirname "$REL"); CFGDIR=""
    while [ "$D" != "." ] && [ "$D" != "/" ]; do
      if compgen -G "$D/eslint.config.*" >/dev/null 2>&1 || compgen -G "$D/.eslintrc*" >/dev/null 2>&1; then CFGDIR="$D"; break; fi
      D=$(dirname "$D")
    done
    [ -n "$CFGDIR" ] || exit 0
    D="$CFGDIR"; ESLINT=""
    while :; do
      if [ -f "$D/node_modules/.bin/eslint" ]; then ESLINT="$(cd "$D/node_modules/.bin" && pwd)/eslint"; break; fi
      [ "$D" = "." ] && break
      D=$(dirname "$D")
    done
    [ -n "$ESLINT" ] || exit 0
    # POST_EDIT_LINT_TIMEOUT overrides the 4 s cap (used by the hook's own controls).
    OUTPUT=$(capped_in_dir "${POST_EDIT_LINT_TIMEOUT:-4}" "$CFGDIR" "$ESLINT" "${REL#"$CFGDIR"/}")
    # A file the package's config ignores is not a finding.
    FINDINGS=$(echo "$OUTPUT" | grep -E '^[[:space:]]+[0-9]+:[0-9]+[[:space:]]+(error|warning)[[:space:]]' | grep -v 'File ignored because' || true)
    ;;

  ps1)
    # PowerShell files: use PSScriptAnalyzer via pwsh
    command -v pwsh &>/dev/null || { echo "[post-edit-lint] pwsh not found, skipping PowerShell lint" >&2; exit 0; }
    OUTPUT=$(timeout 4 pwsh -NoProfile -Command "
      if (Get-Module -ListAvailable -Name PSScriptAnalyzer) {
        Invoke-ScriptAnalyzer -Path '$FILE' -Severity Warning,Error |
          ForEach-Object { 'line ' + \$_.Line + ': ' + \$_.Severity + ' ' + \$_.RuleName + ' - ' + \$_.Message }
      }
    " 2>&1) || true
    FINDINGS=$(echo "$OUTPUT" | grep -E '^line [0-9]+: (Warning|Error) ' || true)
    ;;

  *)
    # Unsupported file type — exit silently
    exit 0
    ;;
esac

# --- Report findings to the agent ---
# A PostToolUse hook's plain stdout goes only to the debug log. Findings reach the agent through
# hookSpecificOutput.additionalContext (root CLAUDE.md §16, enforcement ladder: the lint rung).
# Capped so a noisy file cannot flood the context. Still advisory: exit 0, never blocks the edit.
if [ -n "$FINDINGS" ]; then
  MSG="post-edit-lint: $BASENAME has lint findings (advisory - fix the ones your edit introduced):
$(echo "$FINDINGS" | head -c 3000)"
  # $PY was chosen above (python first; the WindowsApps python3 stub is skipped).
  if [ -n "$PY" ]; then
    printf '%s' "$MSG" | "$PY" -c 'import json,sys; print(json.dumps({"hookSpecificOutput":{"hookEventName":"PostToolUse","additionalContext":sys.stdin.read()}}))'
  else
    echo "$MSG" >&2
  fi
fi

exit 0
