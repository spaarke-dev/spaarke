# Task 025 — CI job text for MCP conformance

**Applied 2026-11-15** to `.github/workflows/ci-tier1-blocking.yml` as job 8, BLOCKING. The owner chose Tier 1 ("Use tier 1") and approved the §6.5 Path-A exception. The text applied matches the parts below, with two changes: the job comment cites issue #2835 for the one real baseline gap instead of describing it (the repository is public), and the classification summary table gained an `mcp_conformance` row. The first CI run's URL and time are in `tasks/025-*.poml` notes and `nfr-measurements.md`.

History: neither workflow was edited at first. Adding a job to Tier 1 needs the owner's CLAUDE.md §6.5 Path-A exception (the header says "strict scope list — do not extend without spec amendment", FR-A02). Three parts below, to paste together: (1) the classify-tier1 filter, (2) the CONTENTS header entry, (3) the job. Placement: after `format-changed-cs` (job 7), so this is job 8. If the owner prefers it advisory, put the same job in `ci-tier2-advisory.yml` (drop the §6.5 block, keep `continue-on-error: true`); NFR-09 says "blocking", which is why Tier 1 is the text below.

Proven locally, not in CI (no push; `act` is not installed): the same steps ran end to end with `bash tests/integration/Mcp.Conformance.Host/run-conformance.sh`, exit 0, "Baseline check passed", 92 passed, 41 baselined entries (38 failures and 3 warnings). The seeded break exited 1. See `nfr-measurements.md`. The CI run URL is therefore not available.

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
              # Everything the real Program host boots the MCP route through: pipeline order, rate limiter, authorization, route mapping,
              # the MCP SDK package version, and the factory the conformance host builds on.
              - 'src/server/api/Sprk.Bff.Api/Program.cs'
              - 'src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj'
              - 'src/server/api/Sprk.Bff.Api/Infrastructure/DI/MiddlewarePipelineExtensions.cs'
              - 'src/server/api/Sprk.Bff.Api/Infrastructure/DI/RateLimitingModule.cs'
              - 'src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs'
              - 'src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs'
              - 'tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs'
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
#      about 1 min of job commands measured locally (+ checkout, tool setup and a cold
#      BFF build on a hosted runner), parallel. Added by spaarkeai-mcp-bff-api-r1 task 025 (NFR-09) as an
#      owner-approved §6.5 PATH-A EXCEPTION, scoped to this one job: see the job's
#      comment.
```

(Measured locally 2026-11-14, Windows 11, Debug, one run, started cold-ish: the host's `bin`/`obj`, the test assembly's `bin`/`obj`, the conformance `node_modules` and the widget `dist` deleted first; the BFF project itself and the NuGet cache were warm. Wall clock with `date +%s` around each command: widget `npm install` + `npm run build` 27 s; `dotnet build tests/integration/Mcp.Conformance.Host -c Debug` 22 s (it recompiles the whole `Sprk.Bff.Api.Tests` assembly); `run-conformance.sh --no-build`, which does `npm ci`, starts the host and runs the suite, 17 s; total **66 s**. A hosted runner adds checkout, `setup-dotnet`/`setup-node`, a NuGet restore on a cold cache and a full BFF compile, so expect a few minutes there; replace this with the first CI run's measured time.)

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
  # BASELINE: conformance/conformance-baseline.yml lists, per check, the 41
  # expected entries: 38 failures (37 need reference fixtures this server must
  # not serve, 1 real gap: Host/Origin validation per the MCP transport
  # requirements is not enforced on /mcp, reported to the owner) and 3 SHOULD
  # warnings (2 fixture, 1 SDK data.uri). A NEW failure fails the job; a
  # baselined check that starts passing also fails it (stale entry), so the
  # list only shrinks.
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

      # Same cache as the other Tier 1 jobs (an optimization, never a gate). The job restores the BFF and the test assembly's packages,
      # so the key hashes every csproj, as the golden-utterance job does.
      - name: Cache NuGet packages
        uses: actions/cache@v6
        continue-on-error: true
        with:
          path: ~/.nuget/packages
          key: ${{ runner.os }}-nuget-mcp-conformance-${{ hashFiles('**/*.csproj') }}
          restore-keys: |
            ${{ runner.os }}-nuget-mcp-conformance-
            ${{ runner.os }}-nuget-

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

      # run-conformance.sh writes the suite's per-scenario results AND the host's log (host.log) into OUT, so a failed run uploads both.
      - name: Upload conformance results and host log
        if: ${{ always() }}
        uses: actions/upload-artifact@v6
        with:
          name: mcp-conformance-results
          path: ${{ runner.temp }}/conformance-results
          if-no-files-found: ignore
```

Notes for whoever applies it:
- `run-conformance.sh` builds the host (`dotnet build ... -c Debug`), runs `npm ci` from `tests/integration/Mcp.Conformance.Host/conformance/package-lock.json`, starts the host, waits up to 120 s for its READY line, runs the suite and stops the host. `MCP_SKIP_WIDGET=1` skips its own widget build (the explicit step above does it).
- No secrets are used. The host needs no Azure, Dataverse or Redis: `CustomWebAppFactory` supplies fakes, and the test signing key lives in `McpProgramHostFactory` (tests only).
- Publish size: task 025 proper changed no BFF file. Its fix round (2026-11-14) changed `GetDecisionPlanTool.cs` and `PlanAndLookupToolSupport.cs` (code only: no package, no new file), so no publish-size delta beyond those few hundred bytes of IL.
- Not measured: the job's wall time in CI (replace the estimates in the header entry and the `timeout-minutes` comment after the first run).
