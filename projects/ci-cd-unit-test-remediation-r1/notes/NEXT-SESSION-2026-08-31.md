# Next session — start here (2026-08-31)

## 1. Azure: delete the deprecated prod stack — READY, evidence captured, NOT executed

Owner: *"those 'prod' azure resources are deprecated/not used (from old prod environment that
was never finalized/used); we should remove them."*

**Corroborating evidence gathered 2026-08-31:**
- `spaarke-bff-prod` — App Service **state = Stopped**
- `spaarke.com` DNS zone has **no `api` CNAME**, so nothing routes to it
  (the app still carries the `api.spaarke.com` hostname binding, but DNS does not point at it)

**Target: `rg-spaarke-platform-prod` (8 resources), in subscription "Spaarke Devlopment Environment"**

```
spaarke-bff-prod              Web/sites          (Stopped)
spaarke-bff-prod-plan         Web/serverFarms
spaarke-openai-prod           CognitiveServices
spaarke-docintel-prod         CognitiveServices
sprk-platform-prod-kv         KeyVault/vaults    <-- soft-delete: needs purge, or it blocks name reuse
sprk-platform-prod-insights   Insights/components
sprk-platform-prod-logs       OperationalInsights/workspaces
api.spaarke.com               Web/certificates
```

**NOT executed this session, deliberately** — 8 irreversible deletions including a Key Vault and a
bound certificate, attempted at ~80% context. Do this with fresh context.

**Sequence when executing:**
1. Confirm each of the 8 individually (this session only verified the App Service + DNS).
2. Check whether `spaarke-openai-prod` / `spaarke-docintel-prod` hold deployments any other
   environment references — CognitiveServices accounts are cheap to keep and expensive to recreate
   (quota re-approval).
3. Export `sprk-platform-prod-kv` secret NAMES (not values) for the record before deleting.
4. Delete the RG, then decide on Key Vault **purge** — soft-delete retains the name and will block
   re-creating a vault with the same name.
5. The `api.spaarke.com` cert: re-issuable; confirm no other binding first.

## 2. Then: the subscription / resource-group restructure

Owner: *"we need a better defined subscription/resource group structure because we have accumulated
inconsistencies, misgrouped, and unused resources."*

The full review with the proposed layout and naming rule is
`docs/assessments/azure-resource-group-review-2026-08-30.md` (PR #901, merged). Headlines:
- `spe-infrastructure-westus2` is a 28-resource catch-all holding **no SPE resources**, but holding
  the company-wide **`spaarke.com` DNS zone**
- one application spans **three RGs across two regions** — the dev BFF's secrets live in
  `spaarke-spekvcert`, in the `SharePointEmbedded` RG, in **eastus**
- five RG names referenced in the repo exist in **no** subscription
- proposed rule: `rg-spaarke-{workload}-{env}`, no region in the name

**Deleting the deprecated prod stack (item 1) is step one of this restructure** — it removes the
worst inconsistency before anything is moved.

## 3. Finish the live-service test deletion

PR #912 deleted 39 tests in 6 fully-dark files. **Skip= 121 -> 88.**

~35 live-service skips remain in **partial** files (siblings still run), so they need method-level
removal, not file deletion. Concentrations:
- `PlaybookExecutionIntegrationTests` — 19
- `OfficeEndpointsContractTests` — 10

Do NOT automate with brace-matching. That is the exact parsing class that produced six classifier
defects in this project, every one an over-call. Read and remove them.

## 4. Cutover chain — the clock

Shadow window was **21/20 agreeing PRs, 0 false greens, ~3/5 calendar days** at last check.
When it closes: `071 cutover -> 075 soak (7d) -> 077 retire sdap-ci.yml -> 076 (30d) -> 090 wrapup`.

- **071 step 4 is now a VERIFY step** — branch protection was enabled 2026-08-29 via ruleset
  `21824191` (required check **`Router`**, NOT `CI / Router`). The classic
  `/branches/master/protection` endpoint 404s on this repo; use rulesets.
- **071 step 9b**: merge PR **#869** (alls-green bump) immediately after cutover. Held because
  `re-actors/alls-green` is the aggregation step in `router-result`, i.e. the `Router` check itself.
- **#894** merges once the window closes (Tier 2 scope + 64 orphaned tests + failure visibility).
