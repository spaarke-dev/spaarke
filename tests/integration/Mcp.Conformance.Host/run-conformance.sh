#!/usr/bin/env bash
# spaarkeai-mcp-bff-api-r1 task 025 (NFR-09): the same steps the `mcp-conformance` CI job runs, for a developer machine.
#
#   bash tests/integration/Mcp.Conformance.Host/run-conformance.sh            # build, start the host, run the suite, stop the host
#   bash tests/integration/Mcp.Conformance.Host/run-conformance.sh --no-build # host already built
#
# Exit code = the conformance CLI's: 0 when every failing check is in conformance/conformance-baseline.yml and no baselined check
# passes; 1 on a regression or a stale baseline entry. Results (checks.json per scenario) and the host log (host.log) land in $OUT; the default
# is a new temp directory ($RUNNER_TEMP when set), never the repository. The folder is printed at the end.
# Needs: .NET SDK 10, Node 22+ (the suite imports fs.globSync). Pass MCP_SKIP_WIDGET=1 to leave the widget unbuilt (the resources/read
# caching check then fails).
set -euo pipefail

node_major="$(node -p 'process.versions.node.split(".")[0]' 2>/dev/null || echo 0)"
if (( node_major < 22 )); then
  echo "run-conformance.sh: Node 22 or later is required (found ${node_major}); the conformance suite imports fs.globSync." >&2
  exit 2
fi

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
host_dir="$repo/tests/integration/Mcp.Conformance.Host"
conf_dir="$host_dir/conformance"
out="${OUT:-$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/mcp-conformance-XXXXXX")}"
mkdir -p "$out"
injected_port="${MCP_CONFORMANCE_PORT:-5081}"
direct_port="${MCP_CONFORMANCE_DIRECT_PORT:-5082}"
log="$out/host.log"

build=1
[[ "${1:-}" == "--no-build" ]] && build=0

if [[ "$build" == 1 ]]; then
  if [[ "${MCP_SKIP_WIDGET:-}" != 1 ]]; then
    # The widget is embedded in the BFF only when dist/index.html exists at build time (Sprk.Bff.Api.csproj, Condition=Exists).
    (cd "$repo/src/client/widgets/spaarke-decisions" && npm install --legacy-peer-deps --no-audit --no-fund && npm run build)
    # r2 task 034: the briefing widget is embedded the same way.
    (cd "$repo/src/client/widgets/spaarke-briefing" && npm install --legacy-peer-deps --no-audit --no-fund && npm run build)
    # r2 task 035: the search results widget is embedded the same way.
    (cd "$repo/src/client/widgets/spaarke-search-results" && npm install --legacy-peer-deps --no-audit --no-fund && npm run build)
  fi
  dotnet build "$host_dir/Mcp.Conformance.Host.csproj" -c Debug
fi

(cd "$conf_dir" && npm ci --no-audit --no-fund)

dotnet "$host_dir/bin/Debug/net10.0/Mcp.Conformance.Host.dll" "$injected_port" "$direct_port" >"$log" 2>&1 &
host_pid=$!
trap 'kill "$host_pid" 2>/dev/null || true' EXIT

for _ in $(seq 1 120); do
  grep -q '^READY ' "$log" 2>/dev/null && break
  kill -0 "$host_pid" 2>/dev/null || { echo "The conformance host exited before it was ready:"; tail -40 "$log"; exit 2; }
  sleep 1
done
grep -q '^READY ' "$log" || { echo "The conformance host was not ready after 120 s:"; tail -40 "$log"; exit 2; }


set +e
(cd "$conf_dir" && npx conformance server \
  --url "http://127.0.0.1:${injected_port}/mcp/legal-operations" \
  --spec-version 2026-07-28 \
  --suite all \
  --expected-failures "$conf_dir/conformance-baseline.yml" \
  -o "$out")
status=$?
set -e
echo "conformance exit code: $status (results and host log: $out)"
exit "$status"
