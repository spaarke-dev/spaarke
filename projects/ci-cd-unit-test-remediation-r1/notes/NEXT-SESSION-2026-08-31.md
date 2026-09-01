# Next session — start here

> **Updated 2026-08-31 (later session).** Items 1 and 3 are now **DONE**. What remains is item 2
> (the RG restructure) and item 4 (the cutover clock).

---

## ✅ 1. Azure prod-stack deletion — **EXECUTED 2026-08-31**

`rg-spaarke-platform-prod` (8 resources) is **deleted**. Took ~100s.

Full record, written pre-deletion while the resources were still queryable:
[`docs/assessments/azure-prod-stack-decommission-2026-08-31.md`](../../../docs/assessments/azure-prod-stack-decommission-2026-08-31.md)

Three things the staged plan got wrong, corrected by measurement:

- **The Key Vault purge decision was moot.** `enablePurgeProtection: true` means the vault
  **cannot be purged at all**. It is soft-deleted, recoverable until **2026-11-29**, and the name
  `sprk-platform-prod-kv` is reserved until then. `az keyvault recover` if ever needed.
- **The quota trap did not apply.** Subscription-level Azure OpenAI access is already approved
  (proven by `spaarke-openai-dev` still running), so recreation is not gated on re-approval. And
  `OpenAI.Standard.gpt-5.1` in westus3 was **50 used of 670** — deleting *returned* 50 units.
- **The `BFF-API-ClientSecret` binding rule was checked, not waived.** The vault held one, but it is
  not the live secret (the dev vault has no `BFF-API-*` at all — it uses MI/UAMI per auth-v4), and
  its service principal `spaarke-bff-api-prod` is **already `accountEnabled: false`**.

### ⚠️ Residual item from the deletion

App registration **`spaarke-bff-api-prod` (`92ecc702-d9ae-492d-957e-563244e93d8c`)** still exists in
Entra with credential `prod-secret-2026` valid until **2027-03-13**. Its SP is disabled so it is not
exploitable, but it is orphaned. Removing it is an **Entra** operation, not a resource-group one.

---

## 2. NEXT ACTION — the subscription / resource-group restructure

Owner: *"we need a better defined subscription/resource group structure because we have accumulated
inconsistencies, misgrouped, and unused resources."*

Full review: [`docs/assessments/azure-resource-group-review-2026-08-30.md`](../../../docs/assessments/azure-resource-group-review-2026-08-30.md) (PR #901, merged).
Deleting the prod stack was step one; these remain:

- `spe-infrastructure-westus2` is a **28-resource catch-all holding no SPE resources** — but it does
  hold the company-wide **`spaarke.com` DNS zone**, and `spaarke-openai-dev`, which is in **eastus**
  despite the RG's `westus2` name.
- One application spans **three RGs across two regions** — the dev BFF's secrets live in
  `spaarke-spekvcert`, in the `SharePointEmbedded` RG, in **eastus**.
- Five RG names referenced in the repo exist in **no** subscription.
- Proposed rule: `rg-spaarke-{workload}-{env}`, no region in the name.

**Open question for the owner** (unchanged): production currently runs in a subscription named
*"Spaarke Devlopment Environment"*. A separate prod subscription is the normal split — worth deciding
before moving resources, since moves are cheap now and expensive after the restructure.

---

## ✅ 3. Live-service test deletion — **DONE (PR #920)**

**Skip= 88 → 67. Live-service-dependent skips: 21 → 0.**

The framing in the previous version of this note (*"~35 live-service skips in partial files"*) was
**wrong** — it grouped by file. By skip **reason** the 88 are five categories; only 21 were
live-service. Deleting all 88 would have destroyed ~57 tests of recoverable coverage.

Full analysis: [`skip-classification-2026-08-31.md`](skip-classification-2026-08-31.md)

The 67 that remain are NOT deletion candidates on the current instruction:

| Category | Count | What it actually needs |
|---|---:|---|
| B — *"requires fully mocked X"* (factory gap) | ~35 | **PR #894 wires these** |
| C — stale assertions (signature drift) | ~11 | Fix or delete per test, by reading |
| D — CI timing flakes | 5 | A perf lane |
| E — endpoint not implemented | 2 | Delete with the feature decision |
| F — Graph SDK sealed classes | 4 | `IGraphClientWrapper` or WireMock — a production seam |

**Known gap, recorded honestly**: nothing automated now asserts *"Redis on ⇒ real multiplexer, not
the null object."* Its old skip claimed coverage by `tests/manual/RedisValidationTests.ps1` — that
claim was **false** (the harness greps config/source text). Closing it needs a Redis container in the
test lane.

**B3 guard blind spot — open by decision, not oversight.** The armed guard misses the
assign-then-assert form. A widening was measured: 4 sites, **2 genuine / 2 false positives**, and both
false positives were the ADR-032 "which implementation resolved" shape the guard must not attack. The
instances were fixed by hand; the detector was left alone. Doubt = KEEP.

---

## 4. Cutover chain — the clock

Shadow window was **21/20 agreeing PRs, 0 false greens, ~3/5 calendar days** at last check.
When it closes: `071 cutover → 075 soak (7d) → 077 retire sdap-ci.yml → 076 (30d) → 090 wrapup`.

- **071 step 4 is a VERIFY step** — branch protection was enabled 2026-08-29 via ruleset
  `21824191` (required check **`Router`**, NOT `CI / Router`). The classic
  `/branches/master/protection` endpoint **404s on this repo**; use rulesets.
- **071 step 9b**: merge PR **#869** (alls-green bump) immediately after cutover — it is the
  aggregation step in `router-result`, i.e. the `Router` check itself.
- **#894** merges once the window closes (Tier 2 scope + 64 orphaned tests + failure visibility).

---

## Hard-won facts — do not re-derive

- **A green Tier 2 check does NOT mean green Tier 2 tests** — the test step has `continue-on-error`,
  so the job is green while the advisory comment says `fail`. Read the comment or the TRX artifacts.
- **Removing a git worktree does NOT delete its branch.** Worktrees went 86 → 14 safely.
- **B8 is NOT a quick win** — 12 sites/10 files, and the ban covers `InternalsVisibleTo`, so the only
  compliant fix is a production refactor.
- **Substring/line counting produced a misleading number SIX times in this workstream.** Match exact
  names. `67e2xz` is a shared env suffix; "SSN" hides inside "STATELESSNESS"; and `e3b0c442…` is the
  SHA-256 of the **empty string** — comparing against it reads as "different" when it means
  "unreadable".
- **`rg-spaarke-platform-dev` and `rg-spaarke-platform-prod` differ by one word.** The dev pair is
  live (the provisioning control-plane). Check which one you are pointed at.
