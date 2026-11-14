# Task 025 — CI job text for MCP conformance (NOT applied; owner decision pending)

Neither `.github/workflows/ci-tier1-blocking.yml` nor `ci-tier2-advisory.yml` was edited. Adding a job to Tier 1 needs the owner's CLAUDE.md §6.5 Path-A exception (the header says "strict scope list — do not extend without spec amendment", FR-A02). Three parts below, to paste together: (1) the classify-tier1 filter, (2) the CONTENTS header entry, (3) the job. Placement: after `format-changed-cs` (job 7), so this is job 8. If the owner prefers it advisory, put the same job in `ci-tier2-advisory.yml` (drop the §6.5 block, keep `continue-on-error: true`); NFR-09 says "blocking", which is why Tier 1 is the text below.

Proven locally, not in CI (no push; `act` is not installed): the same steps ran end to end with `bash tests/integration/Mcp.Conformance.Host/run-conformance.sh`, exit 0, "Baseline check passed", 92 passed / 38 baselined. The seeded break exited 1. See `nfr-measurements.md`. The CI run URL is therefore not available.

## 1. classify-tier1: new filter and output

In `classify-tier1` `outputs:` add:

```yaml
      mcp_conformance: ${{ steps.filter.outputs.mcp_conformance }}
```

In the `filters: |` block add (a change to anything the MCP route or the conformance host depends on, plus a workflow edit so a change to the job proves the job):

```yaml
            # Added by spaarkeai-mcp-bff-api-r1 task 025: runs mcp-conformance.
            mcp_conformance:
              - 'src/server/api/Sprk.Bff.Api/Api/Mcp/**'
              - 'src/server/api/Sprk.Bff.Api/Infrastructure/DI/McpModule.cs'
              - 'src/server/api/Sprk.Bff.Api/Program.cs'
              - 'src/client/widgets/spaarke-decisions/**'
              - 'tests/integration/Mcp.Conformance.Host/**'
              - 'tests/integration/contract/Api/Mcp/McpProgramHostFactory.cs'
              - '.github/workflows/**'
```

## 2. Header CONTENTS entry (insert after entry 7, before the blank `#` line)

```yaml
#   8. mcp-conformance        — the published modelcontextprotocol/conformance suite
#      (pinned 0.2.0-alpha.12, spec 2026-07-28) against /mcp/legal-operations on the
#      real BFF host (tests/integration/Mcp.Conformance.Host), BLOCKING, conditional
#      on the MCP route, the decisions widget, the host or a CI workflow changing,
#      ~3-4 min, parallel. Added by spaarkeai-mcp-bff-api-r1 task 025 (NFR-09) as an
#      owner-approved §6.5 PATH-A EXCEPTION, scoped to this one job: see the job's
#      comment.
```

(The "~3-4 min" is an estimate from the local pieces: widget build, host build, `npm ci`, the suite itself about 1 minute. Replace it with the first CI run's measured time.)

## 3. The job (append after `format-changed-cs`)

```yaml
  # ──────────────────────────────────────────────────────────────────────────
  # Job 8 — mcp-conformance (BLOCKING)
  #
  # §6.5 PATH-A EXCEPTION (owner approval PENDING for spaarkeai-mcp-bff-api-r1
  # task 025, NFR-09): the header's "do not extend without spec amendment"
  # (ci-cd-unit-test-remediation-r1 FR-A02) is waived for THIS ONE JOB.
  # Reason: the Spaarke MCP endpoint is called by third-party clients (Claude
  # Cowork and others) that speak the 2026-07-28 Streamable HTTP transport.
  # Our own tests exercise the tools through our own client, so a transport
  # regression (dual-Accept handling, stateless behaviour, header validation,
  # SSE) would pass every other check and fail only in a customer's client.
  # The published conformance suite is the only independent oracle for that.
  # A seeded break of dual-Accept handling makes it fail (exit 1, 7 passed /
  # 112 failed); the unmodified server passes (exit 0).
  #
  # WHAT RUNS: the real BFF (Program) in-process on localhost
  # (tests/integration/Mcp.Conformance.Host: a TEST-ONLY host project that is
  # not in Spaarke.sln and adds no test auth, switch or bypass to src/). Its JwtBearer
  # test scheme trusts a test key, and a token injector adds the bearer token
  # because the suite's CLI cannot send headers. Seeded fakes sit at the
  # module boundary (the decision capability and the lookup search).
  # The suite is pinned to an exact version by package-lock.json (`npm ci`);
  # 0.1.16 does not know spec 2026-07-28.
  #
  # BASELINE: conformance/conformance-baseline.yml lists, per check, the 38
  # failures that are expected (36 need reference fixtures this server must
  # not serve, 1 SDK SHOULD, 1 real gap: no Host/Origin validation on /mcp,
  # reported to the owner). A NEW failure fails the job; a baselined check that
  # starts passing also fails it (stale entry), so the list only shrinks.
  #
  # BLOCKING: no continue-on-error; a failure fails `CI / Router` the same way
  # compile / arch-tests do (no router change). RUNS when classify-tier1's
  # `mcp_conformance` filter matches, fail-closed (an empty output runs it),
  # and on workflow_dispatch. The same steps run locally:
  #   bash tests/integration/Mcp.Conformance.Host/run-conformance.sh
  # ──────────────────────────────────────────────────────────────────────────
  mcp-conformance:
    name: MCP conformance (2026-07-28)
    runs-on: ubuntu-latest
    timeout-minutes: 15   # hang guard
    needs: classify-tier1
    if: ${{ github.event_name == 'workflow_dispatch' || needs.classify-tier1.outputs.mcp_conformance != 'false' }}
    steps:
      - name: Checkout
        uses: actions/checkout@v6

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v6
        with:
          dotnet-version: '10.x'

      - name: Setup Node.js
        uses: actions/setup-node@v4
        with:
          node-version: '20'

      # The BFF embeds the decisions widget only when dist/index.html exists at
      # build time (Sprk.Bff.Api.csproj), and one conformance check reads it.
      # Same step as deploy-bff-api.yml.
      - name: Build the decisions widget
        working-directory: src/client/widgets/spaarke-decisions
        run: |
          npm install --legacy-peer-deps --no-audit --no-fund
          npm run build

      - name: Conformance suite against the real BFF MCP route
        shell: bash
        env:
          OUT: ${{ runner.temp }}/conformance-results
          MCP_SKIP_WIDGET: '1'   # built by the previous step
        run: bash tests/integration/Mcp.Conformance.Host/run-conformance.sh

      - name: Upload conformance results
        if: ${{ always() }}
        uses: actions/upload-artifact@v4
        with:
          name: mcp-conformance-results
          path: ${{ runner.temp }}/conformance-results
          if-no-files-found: ignore
```

Notes for whoever applies it:
- `run-conformance.sh` builds the host (`dotnet build ... -c Debug`), runs `npm ci` from `tests/integration/Mcp.Conformance.Host/conformance/package-lock.json`, starts the host, waits up to 120 s for its READY line, runs the suite and stops the host. `MCP_SKIP_WIDGET=1` skips its own widget build (the explicit step above does it).
- No secrets are used. The host needs no Azure, Dataverse or Redis: `CustomWebAppFactory` supplies fakes, and the test signing key lives in `McpProgramHostFactory` (tests only).
- Publish size: no BFF file changed in task 025, so no publish-size delta.
- Not measured: the job's wall time in CI (replace the estimates in the header entry and the `timeout-minutes` comment after the first run).
