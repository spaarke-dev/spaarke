# Post-cutover item — ADR-002 dead plugin CI (added 2026-09-25)

**Source**: ADR-002 review (branch `work/adr-002-server-side-write-path`). Spaarke ships no Dataverse plugins; the only plugin (`Spaarke.CustomApiProxy`) was deleted.

## What was done mid-window (verdict-neutral, PR #865 precedent)

The ADR-002 zero-plugin guard was **armed in `ci-tier1-blocking.yml`**: `ADR002_PluginTests.R1..R5` plus its negative, positive and scanner controls. Zero live instances existed when it was armed, and it is a set of deterministic source scans, so it cannot flip a Router verdict on any commit legacy CI passes. Locally the Tier-1 filter ran 33/33 green.

## Deferred until the shadow window closes (do NOT do mid-window)

Each of these is dead but was left in place because it edits a frozen workflow:

| Workflow | Item | Why dead |
|---|---|---|
| `ci-tier2-advisory.yml` ~568-620 | `plugin-size` job (+ its `needs:`/result vars/PR-comment line; header "jobs 1-7") | Builds `power-platform/plugins/Spaarke.Plugins/Spaarke.Plugins.csproj`, which has never existed |
| `ci-router.yml` ~252 | `run-plugin-size:` input passed to Tier 2 | Feeds the job above |
| `sdap-ci.yml` ~128-135 | .NET Framework 4.8 targeting-pack step | No net4x project remains. Moot if task 077 retires `sdap-ci.yml` |
| `sdap-ci.yml` ~570-580 | Plugin size check | Skips when the DLL is missing, and the DLL never exists. Moot after 077 |

**When**: bundle this with task 071 step 9b (the post-cutover merge of PR #869), or with 077.
