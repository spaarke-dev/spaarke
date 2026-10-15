#!/usr/bin/env bash
# ============================================================================
# post-edit-lint.sh — Claude Code PostToolUse Hook for Edit operations
#
# Automatically lints files after Claude Code edits them.
# Dispatches to the correct linter based on file extension:
#   .cs       → dotnet format --verify-no-changes
#   .ts/.tsx  → npx eslint (compact format)
#   .ps1      → Invoke-ScriptAnalyzer via pwsh
#
# Registered in .claude/settings.json as a PostToolUse hook with matcher "Edit".
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

# --- Extract file path ---
# Try jq first; fall back to python3; fall back to simple grep/sed.
FILE=""
if command -v jq &>/dev/null; then
  FILE=$(echo "$INPUT" | jq -r '.tool_input.file_path // empty' 2>/dev/null || true)
fi

if [ -z "$FILE" ] && command -v python3 &>/dev/null; then
  FILE=$(echo "$INPUT" | python3 -c "
import json, sys
try:
    d = json.load(sys.stdin)
    print(d.get('tool_input', {}).get('file_path', ''))
except:
    print('')
" 2>/dev/null || true)
fi

if [ -z "$FILE" ]; then
  # Last resort: simple text extraction
  FILE=$(echo "$INPUT" | grep -oP '"file_path"\s*:\s*"([^"]+)"' | head -1 | sed 's/.*"file_path"\s*:\s*"//;s/"$//' 2>/dev/null || true)
fi

# --- Validate file path ---
if [ -z "$FILE" ]; then
  # No file path found — nothing to lint, exit silently
  exit 0
fi

if [ ! -f "$FILE" ]; then
  # File doesn't exist (may have been deleted or path is invalid)
  exit 0
fi

# --- Determine file extension ---
BASENAME=$(basename "$FILE")
EXT="${BASENAME##*.}"
EXT_LOWER=$(echo "$EXT" | tr '[:upper:]' '[:lower:]')

# --- Dispatch to appropriate linter ---
# FINDINGS holds only lines that are real lint findings (filtered per tool), so a clean file, a
# timeout (exit 124, no output), a missing config or a missing tool never reaches the agent.
FINDINGS=""
case "$EXT_LOWER" in
  cs)
    # C# files: use dotnet format in verify mode
    command -v dotnet &>/dev/null || { echo "[post-edit-lint] dotnet not found, skipping C# lint" >&2; exit 0; }
    OUTPUT=$(timeout 4 dotnet format --include "$FILE" --verify-no-changes 2>&1) || true
    FINDINGS=$(echo "$OUTPUT" | grep -E ': (error|warning) [A-Za-z]+[0-9]*' || true)
    ;;

  ts|tsx)
    # TypeScript files: use ESLint with compact format
    command -v npx &>/dev/null || { echo "[post-edit-lint] npx not found, skipping TypeScript lint" >&2; exit 0; }
    OUTPUT=$(timeout 4 npx eslint "$FILE" --format compact 2>&1) || true
    FINDINGS=$(echo "$OUTPUT" | grep -E ': line [0-9]+, col [0-9]+, (Error|Warning)' || true)
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
  PY=$(command -v python3 || command -v python || true)
  if [ -n "$PY" ]; then
    printf '%s' "$MSG" | "$PY" -c 'import json,sys; print(json.dumps({"hookSpecificOutput":{"hookEventName":"PostToolUse","additionalContext":sys.stdin.read()}}))'
  else
    echo "$MSG" >&2
  fi
fi

exit 0
