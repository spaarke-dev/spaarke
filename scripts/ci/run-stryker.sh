#!/usr/bin/env bash
# Run Stryker.NET 5 on a list of changed source files, in since-mode, against the
# Sprk.Bff.Api.Tests project. Used by .github/workflows/mutation-nightly.yml and
# runnable locally (set STRYKER_TOOL to the dotnet-stryker executable).
#
# usage: run-stryker.sh <label> <project.csproj> <project-dir-prefix> <changed-files.txt> <since-sha>
#   label               bff | dataverse (names the output directory)
#   project.csproj      the project to mutate, e.g. Sprk.Bff.Api.csproj
#   project-dir-prefix  repo-relative dir of that project, with trailing slash
#   changed-files.txt   repo-relative .cs paths to mutate, one per line
#   since-sha           Stryker `since` target: only lines changed since it are mutated
#
# Writes <RUNNER_TEMP>/stryker-out-<label>/ (HTML + JSON reports) and, on success,
# <RUNNER_TEMP>/stryker-out-<label>/report.path pointing at the JSON report.
#
# Stryker 5.0.0 facts (2026-06-01 pilot): `coverage-analysis` and `test-case-filter` are
# accepted only in a config file, `output-path` is rejected there (use --output), and
# --skip-version-check avoids the newer-version probe.
set -euo pipefail

label="$1"; project="$2"; prefix="$3"; files="$4"; since="$5"

tmp="${RUNNER_TEMP:-/tmp}"
tool="${STRYKER_TOOL:-$tmp/stryker/dotnet-stryker}"
out="$tmp/stryker-out-$label"
cfg="$tmp/stryker-$label.json"
test_dir="tests/unit/Sprk.Bff.Api.Tests"

# Test set: only the tests that exercise the code being mutated, not the ADR-038 KEEP set.
# Stryker's initial test run must be green and must finish inside the job cap. Proof run
# 38509398853 (cap 100 min) used the broad KEEP set minus three host suites, 9,667 tests:
# the initial run alone took 71 min and the run was cancelled. A broad set that misses the
# direct suites (Filters, Services.Access, Infrastructure.Authentication/Errors were not in
# it) also reports false NoCoverage.
#
# So the filter is derived per run from the files being mutated:
#   1. SEEDS: the dedicated test namespaces of the allowlisted folders (always included).
#   2. REFERENCING CLASSES: every test class, in a Sprk.Bff.Api.Tests* namespace, declared
#      in a tests/**/*.cs file that names (whole word) a type declared in a mutated file.
# A night on which one filter changed therefore runs that filter's tests, not the suite.
# Limit: a test that reaches a mutated type only indirectly (never names it) is not in the
# set, so a mutant only that test would kill is reported as surviving / uncovered. Add a
# namespace to SEEDS below if a dedicated suite is missed.
ns="FullyQualifiedName~Sprk.Bff.Api.Tests."
case "$label" in
  bff)
    seeds="${ns}Filters.|${ns}Services.Access.|${ns}Infrastructure.Authentication.|${ns}Infrastructure.Errors."
    ;;
  *)
    seeds=""
    ;;
esac

# Types declared in the mutated files (plus the file stems, e.g. SecureRootInheritance.Cascade.cs).
types="$(
  while IFS= read -r f; do
    basename "$f" .cs | sed 's/\..*$//'
    grep -hoE '^[[:space:]]*(public|internal)?[[:space:]]*((sealed|static|abstract|partial|readonly|ref)[[:space:]]+)*(class|interface|record|enum|struct)[[:space:]]+[A-Za-z_][A-Za-z0-9_]*' "$f" | awk '{print $NF}'
  done < "$files" | sort -u
)"
type_re="($(echo "$types" | paste -sd'|'))"

classes="$(
  { grep -rlwE "$type_re" tests --include='*.cs' || true; } | sort -u | while IFS= read -r tf; do
    tns="$(grep -m1 -oE '^namespace[[:space:]]+[A-Za-z0-9_.]+' "$tf" | awk '{print $2}')"
    case "$tns" in
      Sprk.Bff.Api.Tests|Sprk.Bff.Api.Tests.*) ;;
      *) continue ;;
    esac
    grep -oE '^[[:space:]]*(public|internal)[[:space:]]+((sealed|static|abstract|partial)[[:space:]]+)*class[[:space:]]+[A-Za-z0-9_]+' "$tf" | awk -v n="$tns" '{print n"."$NF}'
  done | sort -u
)"
class_count="$(printf '%s\n' "$classes" | grep -c . || true)"
include="$seeds"
while IFS= read -r c; do
  [ -n "$c" ] || continue
  include+="${include:+|}FullyQualifiedName~${c}"
done <<<"$classes"
echo "Test selection for ${label}: types=[$(echo "$types" | paste -sd, )] referencing-test-classes=${class_count}"
if [ -z "$include" ]; then
  echo "::error::no test selected for ${label}: no seed namespace and no test names a mutated type"
  exit 1
fi

# Excluded: Compose tests parse Git-LFS .docx fixtures, which this job does not fetch (an
# LFS pointer fails the initial run), except ComposeDocumentAuthorizationFilterTests, a
# pure authorization-filter unit test with no fixture.
filter="(${include})&(FullyQualifiedName!~Compose|FullyQualifiedName~ComposeDocumentAuthorizationFilterTests)&Category!=Flaky"

# Local check of the selection without running Stryker.
if [ "${STRYKER_PRINT_FILTER_ONLY:-}" = "1" ]; then echo "$filter"; exit 0; fi

# Stryker globs are matched inside the project directory.
mutate_json="$(sed "s#^${prefix}#**/#" "$files" | jq -R . | jq -sc .)"
if [ "$mutate_json" = "[]" ]; then
  echo "::error::no files to mutate for ${label}"
  exit 1
fi

rm -rf "$out"
mkdir -p "$out"

jq -n \
  --arg project "$project" \
  --argjson mutate "$mutate_json" \
  --arg since "$since" \
  --arg filter "$filter" \
  --argjson concurrency "2" \
  '{
    "stryker-config": {
      "project": $project,
      "test-projects": ["Sprk.Bff.Api.Tests.csproj"],
      "mutate": $mutate,
      "reporters": ["html", "json", "cleartext"],
      "thresholds": { "high": 80, "low": 60, "break": 0 },
      "concurrency": $concurrency,
      "coverage-analysis": "perTest",
      "test-case-filter": $filter,
      "since": { "enabled": true, "target": $since },
      "verbosity": "info"
    }
  }' > "$cfg"

echo "Stryker config for ${label}:"
cat "$cfg"

cfg_rel="$(realpath --relative-to="$test_dir" "$cfg")"
(
  cd "$test_dir"
  "$tool" --config-file "$cfg_rel" --output "$out" --skip-version-check
)

report="$(find "$out" -name 'mutation-report.json' | sort | tail -1)"
if [ -z "$report" ]; then
  echo "::error::Stryker finished but wrote no mutation-report.json under $out"
  exit 1
fi
echo "$report" > "$out/report.path"
echo "Report: $report"
