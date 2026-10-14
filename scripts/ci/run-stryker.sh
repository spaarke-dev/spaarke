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

# The KEEP-path test set (ADR-038): see the comment above the "Mutate" steps in
# mutation-nightly.yml for how the namespaces were derived and what is excluded and why.
keep='FullyQualifiedName~Sprk.Bff.Api.Tests.Api.|FullyQualifiedName~Sprk.Bff.Api.Tests.Integration.|FullyQualifiedName~Sprk.Bff.Api.Tests.Domain.|FullyQualifiedName~Sprk.Bff.Api.Tests.Seam.|FullyQualifiedName~Sprk.Bff.Api.Tests.Regression.|FullyQualifiedName~Sprk.Bff.Api.Tests.AccessControl.|FullyQualifiedName~Sprk.Bff.Api.Tests.Auth.|FullyQualifiedName~Sprk.Bff.Api.Tests.DataMutation.|FullyQualifiedName~Sprk.Bff.Api.Tests.Contract.|FullyQualifiedName~Sprk.Bff.Api.Tests.Tenant.'
# Excluded for cost: Compose (LFS fixtures) and the three host-booting namespaces that
# dominate test time (measured 2026-10-13, summed test-minutes: Api.Office 20.2,
# Api.Insights 9.0, Api.Documents 4.5 of ~89 in the whole suite). The first proof run
# with them in (10,152 tests) had not finished Stryker's initial test run after 56 min.
filter="(${keep})&FullyQualifiedName!~Compose&FullyQualifiedName!~Sprk.Bff.Api.Tests.Api.Office.&FullyQualifiedName!~Sprk.Bff.Api.Tests.Api.Insights.&FullyQualifiedName!~Sprk.Bff.Api.Tests.Api.Documents.&Category!=Flaky"

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
