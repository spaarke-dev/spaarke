#!/bin/sh
# check-project-archive-prefix.sh -- fast guard against archiving a project by renaming it to x-<slug>.
#
# Reads added/renamed repo paths on stdin (one per line, e.g. from
# `git diff --cached --name-only -M --diff-filter=AR`) and exits 1 if any path is under
# projects/x-*/ (or X-) in a folder that is not listed in the baseline. Exit 0 otherwise.
#
# Why this exists: tests/Spaarke.ArchTests/ProjectFolderArchivePrefixGuardTests.cs enforces the same
# rule, but the CI router classifies a projects/-only diff as docs-only and the local Stop hook skips
# Gate 3 when no .cs file changed -- so the PR that introduces the rename never runs that test and a
# later, unrelated PR goes red. This script has no dotnet dependency and runs in well under a second:
#   - .husky/pre-commit                          (every commit)
#   - scripts/quality/task-quality-gate.sh       (Gate 3, session diff)
#   - .github/workflows/projects-layout-guard.yml (PRs touching projects/**)
#
# Usage:   <paths on stdin> | sh scripts/quality/check-project-archive-prefix.sh [baseline-file]
# Baseline defaults to tests/Spaarke.ArchTests/ProjectFolderArchivePrefix.baseline.txt next to this repo
# root. '#' comments and blank lines are ignored; folder names match exactly.
# Exit codes: 0 ok, 1 violation, 2 baseline missing.

script_dir=$(cd "$(dirname "$0")" && pwd)
baseline=${1:-$script_dir/../../tests/Spaarke.ArchTests/ProjectFolderArchivePrefix.baseline.txt}

if [ ! -f "$baseline" ]; then
  echo "check-project-archive-prefix: baseline not found: $baseline" >&2
  exit 2
fi

bad=""
while IFS= read -r path || [ -n "$path" ]; do
  path=$(printf '%s' "$path" | tr -d '\r')
  case "$path" in
    projects/[xX]-*/*) ;;
    *) continue ;;
  esac
  folder=${path#projects/}
  folder=${folder%%/*}
  case " $bad " in *" $folder "*) continue ;; esac
  if ! awk -v f="$folder" '{ sub(/\r$/, "") } /^[ \t]*#/ { next } $0 == f { found = 1 } END { exit !found }' "$baseline"; then
    bad="$bad $folder"
  fi
done

if [ -n "$bad" ]; then
  echo "ERROR: new project folder(s) with the retired x- archive prefix:" >&2
  for f in $bad; do echo "  projects/$f" >&2; done
  echo "Archive with /devops-project-archive (a .archived marker plus board Status); do not rename project folders to x-." >&2
  echo "Renaming breaks every link to the project. The 37 existing x- folders are listed in" >&2
  echo "tests/Spaarke.ArchTests/ProjectFolderArchivePrefix.baseline.txt and keep their names." >&2
  exit 1
fi
exit 0
