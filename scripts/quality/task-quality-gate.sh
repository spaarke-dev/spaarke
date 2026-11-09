#!/usr/bin/env bash
# ============================================================================
# task-quality-gate.sh — Claude Code Stop hook: the session's quality gate
#
# Registered in .claude/settings.json under "Stop". Runs when Claude is about
# to end its turn, over the session's changes (tracked edits vs HEAD plus
# untracked, non-ignored files):
#   Gate 1  Build      dotnet build of each project that owns a changed .cs file
#   Gate 2  Typecheck  tsc --noEmit for each tsconfig that owns a changed .ts/.tsx
#   Gate 3  Lint       dotnet format / eslint on changed files (advisory)
#   Gate 4  Arch tests tests/Spaarke.ArchTests (when .cs changed) + the
#                      retired x- project-prefix check
#
# BLOCKING CONTRACT (Claude Code hooks):
#   exit 2  + report on stderr  → Claude is sent back to fix it (the stop is blocked)
#   exit 0                      → Claude may stop
#   any other exit              → non-blocking hook error; never used on purpose
# Only definite code failures block: compiler errors (CS*), TypeScript errors in
# a changed file, failing arch tests, a new x- project folder. Infrastructure
# trouble (timeouts, file locks from a concurrent build, restore/network errors,
# a missing tool) is reported as WARN and never blocks.
#
# LOOP GUARD: when the stop was already blocked once ("stop_hook_active": true
# on stdin), the gate never blocks again. If checks still fail it exits 0 and
# shows the user a one-line warning (systemMessage), so a failure the session
# cannot fix (another session's edits in a shared checkout, a pre-existing
# break) costs one extra turn, not a loop.
#
# The build deliberately does NOT pass -warnaserror: Directory.Build.props owns
# the warning policy (TreatWarningsAsErrors + WarningsNotAsErrors CS0618), the
# same reason CI tier1 "compile" omits it (see the comment on that job and
# docs/assessments/bff-warning-suppression-analysis-2026-06-01.md).
#
# Never call bare `python3` here: on Windows it can resolve to the WindowsApps
# Store stub, which has hung at 100% CPU for days (memory: python3 stub spins).
# ============================================================================

set -uo pipefail
# Log unexpected command failures and carry on. Never `exit` from this trap: an
# early exit 0 would silently drop a failure already recorded.
trap 'echo "[task-quality-gate] unexpected command failure (continuing): $BASH_COMMAND" >&2' ERR

# Keep the real stdout on fd 3 for the JSON systemMessage; everything else goes
# to stderr, which is what Claude reads when the gate exits 2.
exec 3>&1 1>&2

# --- Hook input (absent when run by hand from a terminal) ---
INPUT=""
if [ ! -t 0 ]; then INPUT=$(cat 2>/dev/null || true); fi
STOP_ACTIVE=false
if printf '%s' "$INPUT" | grep -Eq '"stop_hook_active"[[:space:]]*:[[:space:]]*true'; then
  STOP_ACTIVE=true
fi

# --- Time budget: no new expensive check starts after this many seconds ---
BUDGET_SECONDS=240
over_budget() { [ "$SECONDS" -ge "$BUDGET_SECONDS" ]; }

# Run a command under a hard time cap, output to a temp file; sets RUN_EXIT and
# RUN_OUTPUT. Never capture with $(...) here: command substitution waits for
# EOF on the pipe, which MSBuild nodes / testhost (grandchildren that `timeout`
# does not kill) hold open, so a 150 s cap measured 500+ s. A file redirect
# makes bash wait only for `timeout` itself.
GATE_TMP="${TMPDIR:-/tmp}/task-quality-gate.$$.out"
cleanup_tmp() { rm -f "$GATE_TMP" 2>/dev/null || true; }
trap cleanup_tmp EXIT
run_capped() {
  local secs="$1"; shift
  RUN_EXIT=0
  timeout -k 5 "$secs" "$@" >"$GATE_TMP" 2>&1 </dev/null || RUN_EXIT=$?
  RUN_OUTPUT=$(cat "$GATE_TMP" 2>/dev/null || true)
}

# --- Python for JSON validation (skip the WindowsApps python3 stub) ---
PY=""
for cand in python python3; do
  p=$(command -v "$cand" 2>/dev/null) || continue
  case "$p" in *WindowsApps*) continue ;; esac
  PY="$cand"; break
done

CHECKS_PASSED=0
CHECKS_FAILED=0
CHECKS_WARNED=0
CHECKS_SKIPPED=0
FAILED_GATES=""

pass() { echo "[GATE: PASS — $1]"; CHECKS_PASSED=$((CHECKS_PASSED + 1)); }
warn() { echo "[GATE: WARN — $1]"; CHECKS_WARNED=$((CHECKS_WARNED + 1)); }
skip() { echo "[GATE: SKIP — $1]"; CHECKS_SKIPPED=$((CHECKS_SKIPPED + 1)); }
fail() {
  echo "[GATE: FAIL — $1]"
  CHECKS_FAILED=$((CHECKS_FAILED + 1))
  case ", $FAILED_GATES, " in *", $2, "*) ;; *) FAILED_GATES="${FAILED_GATES:+$FAILED_GATES, }$2" ;; esac
}

# Nearest file matching a glob in $1 or an ancestor directory. Stops before the
# repo root (".") unless $3 = "include-root".
nearest_up() {
  local d="$1" pattern="$2" include_root="${3:-}" m
  while :; do
    if [ "$d" != "." ] || [ "$include_root" = "include-root" ]; then
      m=$(compgen -G "$d/$pattern" 2>/dev/null | head -1 || true)
      if [ -n "$m" ]; then printf '%s\n' "$m"; return 0; fi
    fi
    if [ "$d" = "." ] || [ "$d" = "/" ] || [ -z "$d" ]; then return 1; fi
    d=$(dirname "$d")
  done
}

# ============================================================================
# Changed files: tracked edits vs HEAD + untracked, non-ignored files
# ============================================================================
mapfile -t CHANGED < <( { git diff --name-only HEAD 2>/dev/null; git ls-files --others --exclude-standard 2>/dev/null; } | sort -u )

if [ "${#CHANGED[@]}" -eq 0 ]; then
  echo "[QUALITY GATE: PASSED] (no changes)"
  exit 0
fi

CS_FILES=(); TS_FILES=(); PS_FILES=(); JSON_FILES=()
for f in "${CHANGED[@]}"; do
  [ -n "$f" ] || continue
  case "$f" in
    *.cs)      CS_FILES+=("$f") ;;
    *.ts|*.tsx) TS_FILES+=("$f") ;;
    *.ps1)     PS_FILES+=("$f") ;;
    *.json)    JSON_FILES+=("$f") ;;
  esac
done

echo "=== Claude Code Stop Quality Gate ==="
echo "Changed files: ${#CHANGED[@]} (C#: ${#CS_FILES[@]}, TS: ${#TS_FILES[@]}, PS1: ${#PS_FILES[@]}, JSON: ${#JSON_FILES[@]})"
echo ""

# JSON validity (advisory) — always, cheap
if [ "${#JSON_FILES[@]}" -gt 0 ] && [ -n "$PY" ]; then
  for jf in "${JSON_FILES[@]}"; do
    [ -f "$jf" ] || continue
    if ! "$PY" -m json.tool "$jf" >/dev/null 2>&1; then warn "invalid JSON: $jf"; fi
  done
fi

if [ "${#CS_FILES[@]}" -eq 0 ] && [ "${#TS_FILES[@]}" -eq 0 ] && [ "${#PS_FILES[@]}" -eq 0 ]; then
  NONCODE=true
else
  NONCODE=false
fi

# ============================================================================
# Gate 1 — Build each project that owns a changed .cs file (src/ and tests/)
# ============================================================================
if [ "${#CS_FILES[@]}" -gt 0 ]; then
  echo "--- Gate 1: Build ---"
  if ! command -v dotnet &>/dev/null; then
    skip "dotnet not found in PATH"
  else
    declare -A PROJ_SEEN=()
    PROJECTS=()
    for f in "${CS_FILES[@]}"; do
      case "$f" in src/*|tests/*) ;; *) continue ;; esac
      proj=$(nearest_up "$(dirname "$f")" "*.csproj") || continue
      if [ -z "${PROJ_SEEN[$proj]:-}" ]; then PROJ_SEEN[$proj]=1; PROJECTS+=("$proj"); fi
    done
    if [ "${#PROJECTS[@]}" -eq 0 ]; then
      skip "no changed .cs file belongs to a project under src/ or tests/"
    fi
    for proj in "${PROJECTS[@]}"; do
      if over_budget; then warn "time budget reached; not built: $proj"; continue; fi
      run_capped 150 dotnet build "$proj" -nologo -v:q
      BUILD_EXIT=$RUN_EXIT; BUILD_OUTPUT=$RUN_OUTPUT
      CS_ERRORS=$(printf '%s\n' "$BUILD_OUTPUT" | grep -E ': error CS[0-9]+' | sort -u || true)
      if [ "$BUILD_EXIT" -eq 0 ]; then
        pass "build clean: $proj"
      elif [ -n "$CS_ERRORS" ]; then
        fail "compiler errors: $proj" "build"
        printf '%s\n' "$CS_ERRORS" | head -20
      elif [ "$BUILD_EXIT" -eq 124 ]; then
        warn "build timed out (150s), not checked: $proj"
      else
        warn "build failed without compiler errors (file lock, restore or tool problem), not blocking: $proj"
        printf '%s\n' "$BUILD_OUTPUT" | grep -E ': error ' | head -5 || true
      fi
    done
  fi
  echo ""
fi

# ============================================================================
# Gate 2 — tsc --noEmit for each tsconfig that owns a changed .ts/.tsx
#          Blocks only on errors located in a changed file.
# ============================================================================
if [ "${#TS_FILES[@]}" -gt 0 ]; then
  echo "--- Gate 2: TypeScript typecheck ---"
  declare -A TSC_SEEN=() TS_CHANGED=()
  TSCONFIGS=()
  for f in "${TS_FILES[@]}"; do
    TS_CHANGED[$f]=1
    case "$f" in src/*|tests/*) ;; *) continue ;; esac
    [ -f "$f" ] || continue
    cfg=$(nearest_up "$(dirname "$f")" "tsconfig.json") || continue
    if [ -z "${TSC_SEEN[$cfg]:-}" ]; then TSC_SEEN[$cfg]=1; TSCONFIGS+=("$cfg"); fi
  done
  if [ "${#TSCONFIGS[@]}" -eq 0 ]; then
    skip "no changed TS file belongs to a tsconfig under src/ or tests/"
  fi
  for cfg in "${TSCONFIGS[@]}"; do
    if over_budget; then warn "time budget reached; not typechecked: $cfg"; continue; fi
    TSC=$(nearest_up "$(dirname "$cfg")" "node_modules/.bin/tsc" include-root) || TSC=""
    if [ -z "$TSC" ]; then skip "typescript not installed for $cfg (run npm install there)"; continue; fi
    run_capped 120 "$TSC" --noEmit --pretty false -p "$cfg"
    TSC_EXIT=$RUN_EXIT; TSC_OUTPUT=$RUN_OUTPUT
    if [ "$TSC_EXIT" -eq 0 ]; then pass "typecheck clean: $cfg"; continue; fi
    if [ "$TSC_EXIT" -eq 124 ]; then warn "typecheck timed out (120s), not checked: $cfg"; continue; fi
    IN_CHANGED=""; OTHER_COUNT=0
    while IFS= read -r line; do
      path=${line%%(*}; path=${path//\\//}
      if [ -n "${TS_CHANGED[$path]:-}" ]; then IN_CHANGED+="$line"$'\n'; else OTHER_COUNT=$((OTHER_COUNT + 1)); fi
    done < <(printf '%s\n' "$TSC_OUTPUT" | grep -E '^[^ ].*\([0-9]+,[0-9]+\): error TS[0-9]+' || true)
    if [ -n "$IN_CHANGED" ]; then
      fail "TypeScript errors in changed files: $cfg" "typecheck"
      printf '%s' "$IN_CHANGED" | head -20
    fi
    if [ "$OTHER_COUNT" -gt 0 ]; then warn "$OTHER_COUNT TypeScript error(s) in unchanged files under $cfg (not blocking)"; fi
    if [ -z "$IN_CHANGED" ] && [ "$OTHER_COUNT" -eq 0 ]; then
      warn "tsc exited $TSC_EXIT without file errors, not blocking: $cfg"
      printf '%s\n' "$TSC_OUTPUT" | head -5
    fi
  done
  echo ""
fi

# ============================================================================
# Gate 3 — Lint changed files (advisory, capped)
# ============================================================================
LINT_CAP=15
if [ "${#CS_FILES[@]}" -gt 0 ] || [ "${#TS_FILES[@]}" -gt 0 ]; then
  echo "--- Gate 3: Lint (advisory) ---"
  n=0
  if command -v dotnet &>/dev/null; then
    for f in "${CS_FILES[@]}"; do
      [ -f "$f" ] || continue
      n=$((n + 1)); [ "$n" -le "$LINT_CAP" ] || break
      over_budget && break
      if ! timeout -k 2 10 dotnet format --include "$f" --verify-no-changes >/dev/null 2>&1 </dev/null; then
        warn "format issue: $f"
      fi
    done
  fi
  n=0
  if command -v npx &>/dev/null; then
    for f in "${TS_FILES[@]}"; do
      [ -f "$f" ] || continue
      n=$((n + 1)); [ "$n" -le "$LINT_CAP" ] || break
      over_budget && break
      if ! timeout -k 2 10 npx eslint "$f" --format compact >/dev/null 2>&1 </dev/null; then
        warn "eslint issue: $f"
      fi
    done
  fi
  echo ""
fi

# ============================================================================
# Gate 4 — Architecture tests + retired x- project-prefix check
# ============================================================================
echo "--- Gate 4: Architecture ---"

# Fast project-archive-prefix check (no dotnet). The CI router treats a
# projects/-only diff as docs-only, so a rename to projects/x-<slug> would
# otherwise reach no test until a later unrelated PR.
PROJECT_PATHS=$( { git diff --name-only -M --diff-filter=AR HEAD 2>/dev/null; git ls-files --others --exclude-standard 2>/dev/null; } | grep -E '^projects/[xX]-' || true)
if [ -n "$PROJECT_PATHS" ]; then
  if PREFIX_OUTPUT=$(printf '%s\n' "$PROJECT_PATHS" | sh scripts/quality/check-project-archive-prefix.sh 2>&1); then
    pass "no new x- project folders"
  else
    fail "new project folder with the retired x- archive prefix" "project prefix"
    printf '%s\n' "$PREFIX_OUTPUT" | head -10
  fi
fi

if [ "${#CS_FILES[@]}" -eq 0 ]; then
  skip "no C# files changed, skipping architecture tests"
elif ! command -v dotnet &>/dev/null; then
  skip "dotnet not found in PATH"
elif over_budget; then
  warn "time budget reached; architecture tests not run"
elif [ -n "$FAILED_GATES" ] && [[ "$FAILED_GATES" == *build* ]]; then
  skip "build failed; architecture tests not run"
else
  run_capped 150 dotnet test tests/Spaarke.ArchTests/ -nologo
  ARCH_EXIT=$RUN_EXIT; ARCH_OUTPUT=$RUN_OUTPUT
  if [ "$ARCH_EXIT" -eq 0 ]; then
    pass "architecture tests passed"
  elif [ "$ARCH_EXIT" -eq 124 ]; then
    warn "architecture tests timed out (150s), not checked"
  elif printf '%s\n' "$ARCH_OUTPUT" | grep -Eq 'Failed:[[:space:]]*[1-9]|: error CS[0-9]+'; then
    fail "architecture tests failed" "arch tests"
    printf '%s\n' "$ARCH_OUTPUT" | grep -E '^[[:space:]]*Failed |Failed:|: error CS[0-9]+' | head -15 || true
  else
    warn "architecture test run failed for a non-test reason (lock, restore or tool problem), not blocking"
    printf '%s\n' "$ARCH_OUTPUT" | grep -E ': error ' | head -5 || true
  fi
fi
echo ""

# ============================================================================
# Summary and verdict
# ============================================================================
echo "=== Quality Gate: passed $CHECKS_PASSED, failed $CHECKS_FAILED, warnings $CHECKS_WARNED, skipped $CHECKS_SKIPPED (${SECONDS}s) ==="

if [ "$CHECKS_FAILED" -eq 0 ]; then
  if [ "$NONCODE" = true ]; then echo "[QUALITY GATE: PASSED] (non-code changes)"; else echo "[QUALITY GATE: PASSED]"; fi
  exit 0
fi

if [ "$STOP_ACTIVE" = true ]; then
  # Already blocked once this stop: never loop. Tell the user instead.
  echo "[QUALITY GATE: FAILED] (not blocking again: already blocked once)"
  printf '{"systemMessage":"Stop quality gate still failing (%s) after one retry. Run: bash scripts/quality/task-quality-gate.sh"}\n' "$FAILED_GATES" >&3
  exit 0
fi

echo "[QUALITY GATE: FAILED] ($FAILED_GATES)"
echo "Fix the failures above before finishing. If a failure was not caused by this session's changes"
echo "(another session's edits in a shared checkout, or a break already on the branch), say so plainly"
echo "in your reply and stop: the gate blocks only once per stop."
exit 2
