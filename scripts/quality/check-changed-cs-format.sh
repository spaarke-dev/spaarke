#!/usr/bin/env bash
# check-changed-cs-format.sh -- fail when a changed C# file under src/ or tests/ is not whitespace-formatted.
#
# The blocking CI gate for C# formatting (Tier 1 `Format (changed C# files)` in .github/workflows/ci-tier1-blocking.yml),
# and the same check developers and sessions can run locally before pushing.
#
# Why it exists: the pre-commit hook (.lintstagedrc.mjs) formats staged .cs files, but a `--no-verify` commit skips it,
# and the only CI check was the advisory Tier 2 `Format (dotnet format)` job. By 2026-11-10 master carried 2,569
# violations in 230 files and that check had been red for weeks, so it signalled nothing. This gate checks only the
# files a change touches, so nobody fails for code they did not change, and it costs seconds:
#   `dotnet format whitespace --folder` formats from .editorconfig without loading the MSBuild workspace (no restore,
#   ~2 s), and makes the same changes as the workspace mode the Tier 2 job and the old hook used.
#
# Scope: added, copied, modified, renamed or type-changed *.cs files under src/ and tests/ between <base> and <head>.
# knowledge/ (vendored samples), projects/ (notes) and scripts/ are not shipped code and are not checked.
#
# Usage:   bash scripts/quality/check-changed-cs-format.sh <base> [<head>]
#          <base> empty or all zeros (a new branch push, a manual run): every *.cs under src/ and tests/ is checked.
#          <head> defaults to HEAD. The working tree is what gets checked, so check out <head> first.
# Fix:     dotnet format whitespace . --folder --include <file>...   (or commit through the hook, which runs it)
# Exit:    0 formatted (or no C# change), 1 a file needs formatting, 2 the check itself could not run.

set -uo pipefail

base="${1:-}"
head="${2:-HEAD}"
repo_root=$(git rev-parse --show-toplevel) || exit 2
cd "$repo_root" || exit 2

scope=(':(glob)src/**/*.cs' ':(glob)tests/**/*.cs')

check_all=false
if [[ -z "$base" || "$base" =~ ^0+$ ]]; then
  check_all=true
  mode="C# files under src/ and tests/ (no diff base)"
  mapfile -t files < <(git ls-files -- "${scope[@]}")
else
  short() { [[ "$1" =~ ^[0-9a-f]{40}$ ]] && echo "${1:0:10}" || echo "$1"; }
  mode="C# files changed between $(short "$base") and $(short "$head")"
  if ! diff_out=$(git diff --name-only --no-renames --diff-filter=ACMRT "$base" "$head" -- "${scope[@]}"); then
    echo "::error::check-changed-cs-format: cannot diff $base..$head (is the base commit fetched?)" >&2
    exit 2
  fi
  mapfile -t files < <(printf '%s' "$diff_out" | sed '/^$/d')
fi

if [[ ${#files[@]} -eq 0 ]]; then
  echo "check-changed-cs-format: no $mode -- nothing to check."
  exit 0
fi

echo "check-changed-cs-format: checking ${#files[@]} $mode"
# Check-all passes the two folders; a diff passes its files in batches, because thousands of paths overflow the
# command line on Windows (32 KB) and a large merge can touch hundreds of files.
out=""
rc=0
if [[ "$check_all" == true ]]; then
  out=$(dotnet format whitespace . --folder --verify-no-changes --verbosity minimal --include src/ tests/ 2>&1) || rc=$?
else
  batch=150
  for ((i = 0; i < ${#files[@]}; i += batch)); do
    part=$(dotnet format whitespace . --folder --verify-no-changes --verbosity minimal --include "${files[@]:i:batch}" 2>&1)
    part_rc=$?
    out+="$part"$'\n'
    if [[ $part_rc -ne 0 ]]; then rc=$part_rc; fi
  done
fi

if [[ $rc -eq 0 ]]; then
  echo "check-changed-cs-format: all ${#files[@]} file(s) formatted."
  exit 0
fi

findings=$(printf '%s\n' "$out" | grep -E ': (error|warning) [A-Z]+:' || true)
if [[ -z "$findings" ]]; then
  # dotnet format failed without reporting a formatting finding: the check did not run. Fail closed.
  printf '%s\n' "$out" >&2
  echo "::error::check-changed-cs-format: dotnet format exited $rc without a formatting finding -- the check did not run." >&2
  exit 2
fi

# One annotation per file (GitHub shows them on the PR), then the raw findings.
printf '%s\n' "$findings" | sed -E 's/^(.*)\(([0-9]+),([0-9]+)\): (error|warning) ([A-Z]+): (.*)$/\1|\2|\5: \6/' \
  | sed "s#^$repo_root/##" | sort -u -t'|' -k1,1 \
  | while IFS='|' read -r file line msg; do echo "::error file=$file,line=$line::$msg (run: dotnet format whitespace . --folder --include $file)"; done
printf '%s\n' "$findings" | head -50
count=$(printf '%s\n' "$findings" | wc -l | tr -d ' ')
echo ""
echo "check-changed-cs-format: $count formatting finding(s) in changed C# files. Fix and push:"
echo "  dotnet format whitespace . --folder --include <file>..."
echo "Commits through the pre-commit hook are formatted automatically; this fails when a commit skipped it (--no-verify)."
exit 1
