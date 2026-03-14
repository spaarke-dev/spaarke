#!/usr/bin/env bash
# ============================================================================
# task-quality-gate.sh — Claude Code TaskCompleted Quality Gate Hook
#
# Runs three quality checks when a Claude Code task completes:
#   1. Build check:  dotnet build -warnaserror (clean compile)
#   2. Lint check:   dotnet format / eslint on changed files
#   3. Arch tests:   dotnet test Spaarke.ArchTests (if .cs files changed)
#
# Non-code tasks (only .md, .poml, .yaml, .json changes) get a fast path
# that skips build and architecture tests entirely.
#
# Registered in .claude/settings.json as a TaskCompleted hook.
#
# Output format:
#   [GATE: PASS — description]    Check passed
#   [GATE: FAIL — description]    Check failed (exit non-zero)
#   [GATE: WARN — description]    Advisory finding (does not fail)
#   [GATE: SKIP — description]    Check skipped (not applicable)
#   [QUALITY GATE: PASSED/FAILED] Final summary
#
# Exit codes:
#   0 — All checks passed (or non-code task fast path)
#   1 — One or more checks failed
#
# Constraints:
#   - Must complete in < 2 minutes total
#   - Must not crash the Claude Code session (ERR trap)
#   - Must preserve PostToolUse hook behavior (independent script)
# ============================================================================

# --- Global error trap: ensure we never crash Claude Code ---
trap 'echo "[task-quality-gate] Hook error (non-fatal): $BASH_COMMAND" >&2; exit 0' ERR
set -uo pipefail

# --- Counters for summary ---
CHECKS_RUN=0
CHECKS_PASSED=0
CHECKS_FAILED=0
CHECKS_SKIPPED=0
CHECKS_WARNED=0
GATE_FAILED=0

# ============================================================================
# Step 1: Detect changed files via git diff
# ============================================================================

# Get list of changed files (staged + unstaged relative to HEAD)
CHANGED_FILES=$(git diff --name-only HEAD 2>/dev/null || true)

if [ -z "$CHANGED_FILES" ]; then
  echo "[GATE: SKIP — no changed files detected]"
  echo ""
  echo "========================================"
  echo "[QUALITY GATE: PASSED] (no changes)"
  echo "========================================"
  exit 0
fi

# --- Categorize changed files ---
CS_FILES=""
TS_FILES=""
PS_FILES=""
DOC_FILES=""
OTHER_FILES=""

while IFS= read -r file; do
  case "$file" in
    *.cs)       CS_FILES="$CS_FILES $file" ;;
    *.ts|*.tsx)  TS_FILES="$TS_FILES $file" ;;
    *.ps1)      PS_FILES="$PS_FILES $file" ;;
    *.md|*.poml|*.yaml|*.yml|*.json)
                DOC_FILES="$DOC_FILES $file" ;;
    *)          OTHER_FILES="$OTHER_FILES $file" ;;
  esac
done <<< "$CHANGED_FILES"

# Trim leading spaces
CS_FILES=$(echo "$CS_FILES" | xargs 2>/dev/null || true)
TS_FILES=$(echo "$TS_FILES" | xargs 2>/dev/null || true)
PS_FILES=$(echo "$PS_FILES" | xargs 2>/dev/null || true)
DOC_FILES=$(echo "$DOC_FILES" | xargs 2>/dev/null || true)
OTHER_FILES=$(echo "$OTHER_FILES" | xargs 2>/dev/null || true)

# Detect if this is a code change or documentation-only task
HAS_CODE=false
if [ -n "$CS_FILES" ] || [ -n "$TS_FILES" ] || [ -n "$PS_FILES" ]; then
  HAS_CODE=true
fi

echo "=== Claude Code Task Quality Gate ==="
echo "Changed files: $(echo "$CHANGED_FILES" | wc -l | xargs)"
if [ -n "$CS_FILES" ]; then echo "  C# files:    $(echo "$CS_FILES" | wc -w)"; fi
if [ -n "$TS_FILES" ]; then echo "  TS/TSX files: $(echo "$TS_FILES" | wc -w)"; fi
if [ -n "$PS_FILES" ]; then echo "  PS1 files:   $(echo "$PS_FILES" | wc -w)"; fi
if [ -n "$DOC_FILES" ]; then echo "  Doc files:   $(echo "$DOC_FILES" | wc -w)"; fi
if [ -n "$OTHER_FILES" ]; then echo "  Other files: $(echo "$OTHER_FILES" | wc -w)"; fi
echo ""

# ============================================================================
# Step 2: Non-code fast path
# ============================================================================

if [ "$HAS_CODE" = false ]; then
  echo "[GATE: SKIP — no code changes (documentation/config only)]"
  echo ""

  # Validate JSON files if any were changed
  if [ -n "$DOC_FILES" ]; then
    JSON_FILES=""
    for f in $DOC_FILES; do
      case "$f" in
        *.json) JSON_FILES="$JSON_FILES $f" ;;
      esac
    done
    JSON_FILES=$(echo "$JSON_FILES" | xargs 2>/dev/null || true)

    if [ -n "$JSON_FILES" ]; then
      JSON_VALID=true
      for jf in $JSON_FILES; do
        if [ -f "$jf" ]; then
          if command -v python3 &>/dev/null; then
            if ! python3 -m json.tool "$jf" >/dev/null 2>&1; then
              echo "[GATE: WARN — invalid JSON: $jf]"
              JSON_VALID=false
              CHECKS_WARNED=$((CHECKS_WARNED + 1))
            fi
          fi
        fi
      done
      if [ "$JSON_VALID" = true ]; then
        echo "[GATE: PASS — JSON files valid]"
        CHECKS_PASSED=$((CHECKS_PASSED + 1))
      fi
      CHECKS_RUN=$((CHECKS_RUN + 1))
    fi
  fi

  echo ""
  echo "========================================"
  echo "[QUALITY GATE: PASSED] (non-code task)"
  echo "========================================"
  exit 0
fi

# ============================================================================
# Step 3: Gate 1 — Build check (dotnet build -warnaserror)
# ============================================================================

if [ -n "$CS_FILES" ]; then
  echo "--- Gate 1: Build Check ---"
  CHECKS_RUN=$((CHECKS_RUN + 1))

  if ! command -v dotnet &>/dev/null; then
    echo "[GATE: SKIP — dotnet not found in PATH]"
    CHECKS_SKIPPED=$((CHECKS_SKIPPED + 1))
  else
    BUILD_OUTPUT=$(timeout 90 dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror --no-incremental 2>&1) || BUILD_EXIT=$?
    BUILD_EXIT=${BUILD_EXIT:-0}

    if [ "$BUILD_EXIT" -eq 0 ]; then
      echo "[GATE: PASS — build clean (no warnings, no errors)]"
      CHECKS_PASSED=$((CHECKS_PASSED + 1))
    else
      echo "[GATE: FAIL — build errors or warnings detected]"
      echo "$BUILD_OUTPUT" | grep -E "(error |warning )" | head -20
      CHECKS_FAILED=$((CHECKS_FAILED + 1))
      GATE_FAILED=1
    fi
  fi
  echo ""
else
  echo "--- Gate 1: Build Check ---"
  echo "[GATE: SKIP — no C# files changed, skipping build]"
  CHECKS_SKIPPED=$((CHECKS_SKIPPED + 1))
  CHECKS_RUN=$((CHECKS_RUN + 1))
  echo ""
fi

# ============================================================================
# Step 4: Gate 2 — Lint changed files
# ============================================================================

echo "--- Gate 2: Lint Changed Files ---"
LINT_ISSUES=0

# Lint C# files with dotnet format
if [ -n "$CS_FILES" ]; then
  if command -v dotnet &>/dev/null; then
    for csfile in $CS_FILES; do
      if [ -f "$csfile" ]; then
        CHECKS_RUN=$((CHECKS_RUN + 1))
        FORMAT_OUTPUT=$(timeout 10 dotnet format --include "$csfile" --verify-no-changes 2>&1) || FORMAT_EXIT=$?
        FORMAT_EXIT=${FORMAT_EXIT:-0}
        if [ "$FORMAT_EXIT" -ne 0 ]; then
          echo "[GATE: WARN — format issue: $(basename "$csfile")]"
          LINT_ISSUES=$((LINT_ISSUES + 1))
          CHECKS_WARNED=$((CHECKS_WARNED + 1))
        else
          CHECKS_PASSED=$((CHECKS_PASSED + 1))
        fi
      fi
    done
  fi
fi

# Lint TypeScript files with ESLint
if [ -n "$TS_FILES" ]; then
  if command -v npx &>/dev/null; then
    for tsfile in $TS_FILES; do
      if [ -f "$tsfile" ]; then
        CHECKS_RUN=$((CHECKS_RUN + 1))
        ESLINT_OUTPUT=$(timeout 10 npx eslint "$tsfile" --format compact 2>&1) || ESLINT_EXIT=$?
        ESLINT_EXIT=${ESLINT_EXIT:-0}
        if [ "$ESLINT_EXIT" -ne 0 ]; then
          echo "[GATE: WARN — eslint issue: $(basename "$tsfile")]"
          LINT_ISSUES=$((LINT_ISSUES + 1))
          CHECKS_WARNED=$((CHECKS_WARNED + 1))
        else
          CHECKS_PASSED=$((CHECKS_PASSED + 1))
        fi
      fi
    done
  fi
fi

if [ "$LINT_ISSUES" -eq 0 ] && { [ -n "$CS_FILES" ] || [ -n "$TS_FILES" ]; }; then
  echo "[GATE: PASS — lint clean]"
elif [ "$LINT_ISSUES" -gt 0 ]; then
  echo "[GATE: WARN — $LINT_ISSUES file(s) with lint issues]"
elif [ -z "$CS_FILES" ] && [ -z "$TS_FILES" ]; then
  echo "[GATE: SKIP — no lintable files changed]"
  CHECKS_SKIPPED=$((CHECKS_SKIPPED + 1))
  CHECKS_RUN=$((CHECKS_RUN + 1))
fi
echo ""

# ============================================================================
# Step 5: Gate 3 — Architecture tests (only if .cs files changed)
# ============================================================================

echo "--- Gate 3: Architecture Tests ---"
CHECKS_RUN=$((CHECKS_RUN + 1))

if [ -z "$CS_FILES" ]; then
  echo "[GATE: SKIP — no C# files changed, skipping architecture tests]"
  CHECKS_SKIPPED=$((CHECKS_SKIPPED + 1))
else
  if ! command -v dotnet &>/dev/null; then
    echo "[GATE: SKIP — dotnet not found in PATH]"
    CHECKS_SKIPPED=$((CHECKS_SKIPPED + 1))
  else
    ARCH_OUTPUT=$(timeout 90 dotnet test tests/Spaarke.ArchTests/ --no-build 2>&1) || ARCH_EXIT=$?
    ARCH_EXIT=${ARCH_EXIT:-0}

    if [ "$ARCH_EXIT" -eq 0 ]; then
      echo "[GATE: PASS — architecture tests passed]"
      CHECKS_PASSED=$((CHECKS_PASSED + 1))
    else
      # If --no-build fails (no prior build), retry with build
      if echo "$ARCH_OUTPUT" | grep -qi "build"; then
        ARCH_OUTPUT=$(timeout 90 dotnet test tests/Spaarke.ArchTests/ 2>&1) || ARCH_EXIT=$?
        ARCH_EXIT=${ARCH_EXIT:-0}
        if [ "$ARCH_EXIT" -eq 0 ]; then
          echo "[GATE: PASS — architecture tests passed (with build)]"
          CHECKS_PASSED=$((CHECKS_PASSED + 1))
        else
          echo "[GATE: FAIL — architecture tests failed]"
          echo "$ARCH_OUTPUT" | grep -E "(Failed|Error)" | head -10
          CHECKS_FAILED=$((CHECKS_FAILED + 1))
          GATE_FAILED=1
        fi
      else
        echo "[GATE: FAIL — architecture tests failed]"
        echo "$ARCH_OUTPUT" | grep -E "(Failed|Error)" | head -10
        CHECKS_FAILED=$((CHECKS_FAILED + 1))
        GATE_FAILED=1
      fi
    fi
  fi
fi
echo ""

# ============================================================================
# Step 6: Summary
# ============================================================================

echo "========================================"
echo "=== Quality Gate Summary ==="
echo "  Checks run:     $CHECKS_RUN"
echo "  Passed:         $CHECKS_PASSED"
echo "  Failed:         $CHECKS_FAILED"
echo "  Warnings:       $CHECKS_WARNED"
echo "  Skipped:        $CHECKS_SKIPPED"
echo "========================================"

if [ "$GATE_FAILED" -eq 1 ]; then
  echo "[QUALITY GATE: FAILED]"
  echo "========================================"
  exit 1
else
  echo "[QUALITY GATE: PASSED]"
  echo "========================================"
  exit 0
fi
