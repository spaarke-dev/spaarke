# 📨 INCOMING — D-12 / D-13 deployment-model change, and what this project must do

> **From**: `unified-access-control-r2` · **Date**: 2026-09-28
> **To**: whoever next picks up `customer-provisioning-orchestration-r1` (operator or agent)
> **Status of the decisions**: 🔴 **OWNER-DECIDED and MERGED TO MASTER** (`38f48723e`, PR #950). Not a
> proposal. Not up for re-litigation.
> **Why you are reading this**: the deployment model this project was built around has **changed**, and
> four of the required code changes are **in this project's code**, not ours.

---

## 0. TL;DR — five sentences

1. **"Model 1 = shared trial/SMB tier" is RETIRED.** Both models are now dedicated stamps; they differ
   **only** in which **Azure tenant** owns the customer's subscription.
2. **One Azure subscription + resource group per customer** (ADR-027 amended), which **forces** a dedicated
   App Service Plan.
3. 🔴 **The BFF Entra app registration is per customer, in both models (D-13, BINDING)** — and
   `H3EntraAppRegHandler` currently does the **opposite** for Model 1.
4. Your branch is **812 commits behind master** and has **99 unmerged commits**. You must rebase/merge
   before doing anything.
5. Four work items are yours (§5). Two related items are ours and are not your concern (§8).

---

## 1. What changed, and why it is not arbitrary

### The old model (this project's D3 v3)

| | Dataverse env | Fixed-floor resources |
|---|---|---|
| Model 1 (trial/SMB) | **shared** | App Service Plan, Azure OpenAI, AI Search **shared across customers** |
| Model 2 (dedicated) | dedicated | dedicated |

### The new model (D-12)

| | Dataverse env | Azure tenant | Azure subscription + RG |
|---|---|---|---|
| **Model 1** | dedicated per customer | **Spaarke's** | dedicated per customer |
| **Model 2** | dedicated per customer | **the customer's own** | dedicated per customer |

**Only two things differ between the models**, and both follow from tenant ownership:

| | Model 1 | Model 2 |
|---|---|---|
| H0.5 admin consent | not needed | **required** |
| H1 Azure Lighthouse delegation | not needed | **required** |

⚠️ The models are now **nearly identical infrastructurally**. When you rewrite anything, resist the pull to
justify two models by inventing differences. The table above is the complete list.

### Why the shared tier went away — three findings, in order of force

1. 🔴 **It was never implemented.** `H5DataverseEnvCreationHandler` creates a Dataverse environment
   **unconditionally**, keyed `dvenv-{customerId}`. There has never been a code path in which two customers
   share an environment. The tier existed in docs, Bicep, schema and an Azure subscription — not in the engine.
2. 🔴 **Its isolation story could not have worked.** Sharing was to be made safe by `tenantId`-keyed
   controls (I2 AI Search filter, I3 Cosmos partition, I4 SPE resolver). **Under Model 1 every customer
   presents the same `tenantId`** — Spaarke's. Those controls separate nothing between customers **and their
   ArchTests pass anyway.** The safeguard the tier depended on was itself the defect.
3. **Cost allocation is solved differently.** Per-customer subscriptions are Azure's billing boundary, so
   per-customer billing is native. The APIM/token-metering layer D3 v3 required *for fair allocation* is no
   longer needed for that purpose. (Its **runaway-loop guardrail** purpose survives — key it on `customerId`.)

### The binding terminology, because the whole change turns on it

| Term | Meaning |
|---|---|
| **tenant** / `tenantId` | the Entra/Azure/Dataverse **tenant GUID**. Model 1 ⇒ always Spaarke's |
| **customer** | the business entity. **Many customers share one Azure tenant** under Model 1 |

🔴 **Never write "tenant" when you mean "customer."**

### 🔴 D-13 — why the app registration must be per customer

```
Entra app registration → Dataverse APPLICATION USER → assigned to exactly ONE business unit
                       → every BFF-created record is OWNED by it → lands in THAT business unit
```

A record can only land in customer X's business unit if the BFF authenticated as an app registration
dedicated to customer X. **This is the reason the deployment model was redefined at all.**

⚠️ The obvious counter-argument — *"each customer has their own Dataverse environment now, so one shared app
registration could be an application user in each"* — is **technically true and already rejected**. Reasons
in `projects/unified-access-control-r2/notes/D-13-per-customer-bff-app-registration.md` §2. Short version:
one shared app registration = one credential whose compromise reaches every customer; it reproduces the
shared-thing-relied-on-to-behave-differently pattern D-12 exists to remove; it doesn't survive Model 2 at
all; and it hits the **unraisable 20-FIC-per-application cap at customer 21**.

---

## 2. Read these before touching code

All on master as of `38f48723e`:

| File | What it gives you |
|---|---|
| `projects/unified-access-control-r2/notes/D-12-deployment-model-redefinition.md` | the decision |
| `projects/unified-access-control-r2/notes/D-13-per-customer-bff-app-registration.md` | 🔴 the app-registration mechanism + rejected counter-arguments |
| `projects/unified-access-control-r2/notes/D-12-code-branch-inventory.md` | 🔴 **your work list** — all 9 branch sites and 4 silent defaults, with file:line |
| `projects/unified-access-control-r2/notes/D-12-resource-sharing-analysis.md` | the shared-vs-dedicated criteria, per resource |
| `projects/unified-access-control-r2/notes/coordination-cpo-r1-2026-09-28.md` | the overlap analysis behind this note |

Amended ADRs: **027** (subscription per customer), **009** (cache key), **015** (Cosmos partition),
**052** (no shared Model 1 compute), **028** (auth shapes), **013** (AI resources).

---

## 3. 🔴 STEP 1 — update your worktree. Do this first, alone, and verify.

**Current state** (measured 2026-09-28): branch `work/customer-provisioning-orchestration-r1`,
**812 behind master**, **99 unmerged commits**, worktree clean, **no open PR**.

```bash
cd C:/code_files/spaarke-wt-customer-provisioning-orchestration-r1
git status --porcelain          # MUST be empty before you start
git fetch origin
git merge origin/master         # merge, not rebase — 99 commits of history, no force-push
```

**Why merge and not rebase**: rebasing 99 commits over 812 means resolving conflicts up to 99 times and a
force-push on a shared branch. Merge resolves once.

### Conflicts you WILL hit — 9 files, and the resolution rule

These files were edited by both sides. **Our text is the incumbent and is the corrected version**:

```
.claude/adr/ADR-028-spaarke-auth-architecture.md
.claude/skills/provision-environment/SKILL.md          ← 🔴 hardest, see below
docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md
docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md
projects/customer-provisioning-orchestration-r1/CLAUDE.md
projects/customer-provisioning-orchestration-r1/current-task.md
projects/customer-provisioning-orchestration-r1/design.md
projects/customer-provisioning-orchestration-r1/spec.md
scripts/provisioning-prereqs/prereqs.yaml
```

**Rule**: where a conflict is about the **deployment model, the shared tier, or the app registration**, take
**MASTER's** side — it encodes the owner decision. Where it is about **your handler mechanics**, take
**yours**. `current-task.md` is your rolling log — take yours.

🔴 **`provision-environment/SKILL.md` is the dangerous one.** Your side is **+1467/−139**. Do **not**
resolve it "take theirs" wholesale — master's version contains a deliberate **hard stop** that rejects
`spaarke-hosted-model1-trial` with an error naming the decision. If you take your whole file, that
protection silently disappears. Merge your 1467 lines **around** it.

⚠️ **`scripts/provisioning-prereqs/prereqs.yaml` — take master's.** It did not parse **at all** before
(unquoted scalars starting with a backtick, and scalars containing `: `). Confirmed pre-existing. Master's
version parses and yields 32 entries. `PRQ-T-07` is retired there but its id is retained so references don't
dangle.

### 🔴 The failure mode to watch for — it already bit us

Where the two sides touched **different lines of the same file**, git merges **cleanly** and you get
master's *"the shared tier is RETIRED"* text sitting beside your shared-tier-premised prose, **with no
conflict marker**. We hit exactly this merging master into our own branch an hour before writing this, and
only a forcing function caught it.

**So after the merge, run the full arch suite — not just changed-file tests:**

```bash
dotnet test tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj
```

Expect **326/326**. `WorkloadPlacementDocDriftTests` is the one that catches this class.

---

## 4. Verify the merge before starting work

```bash
dotnet build src/server/api/Sprk.Bff.Api/
dotnet test tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj      # 326/326
grep -c "ONE APP REGISTRATION PER CUSTOMER" .claude/adr/ADR-028-spaarke-auth-architecture.md   # expect 1
python -c "import yaml;print(len(yaml.safe_load(open('scripts/provisioning-prereqs/prereqs.yaml',encoding='utf-8'))))"
```

Do not proceed to §5 until the arch suite is green.

---

## 5. 🔴 YOUR WORK — four items, in this order

Ordered by consequence. Earlier items reduce the risk of later ones.

### Item 1 — Delete the H3 shared-app-registration branch (D-13)

🔴 **This is live code doing what D-13 forbids.**

- `Handlers/EntraAppReg/H3EntraAppRegHandler.cs:272` branches on `Model1Shared` → `HandleModel1Async`
  (`:406-460`), documented as *"MODEL 1 (shared multitenant BFF app-reg). **Creates ZERO new app-reg objects
  and ZERO new FIC objects** (acceptance criterion)"*. It returns
  `BffAppRegId = _options.SharedBffAppRegistrationId`.
- Remove: the branch, `EntraAppRegOptions.SharedBffAppRegistrationId`, `SharedPlatformKeyVaultName`,
  `EntraAppRegSharedVerifyRequest`, and the shared-path tests in `H3EntraAppRegHandlerTests.cs`.
- Result: H3 provisions **one app registration per customer, unconditionally**, in both models.

✅ **The docs already say this.** `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` H3 row already documents the output
as `appreg-{customerId}-{tenantId}` — the guide and the code have disagreed since before D-13 existed.

### Item 2 — One shared `TenancyModel` enum (do this BEFORE any renaming)

🔴 **A partial rename fails silently**: wrong Bicep stack, wrong Lighthouse gate, wrong cost envelope.

**9 branch sites, 4 silent defaults** — the full table with file:line is in
`notes/D-12-code-branch-inventory.md`. Highlights:

| | Site | Note |
|---|---|---|
| B1/B2 | `ArmDeploymentRunner.cs:232`, `FileBicepTemplateInspector.cs:110` | template blob key / file path |
| B3 | `H2bAiSearchIndexHandler.cs:295` | shared vs per-customer index |
| B4/B5 | `H1SubscriptionReadinessHandler.cs:401,406` | 🔴 **the P-2 defect** — see below |
| B6/B7 | `AiSearchTenantFilterInvariantProbe.cs:250,301` | only sites using a named constant, and it's file-private |
| B8 | `ArmCostEnvelopeChecker.cs:264-268` | ⚠️ raw-string `switch` — **case-SENSITIVE** |
| **B9** | `H3EntraAppRegHandler.cs:272` | ⚠️ also case-sensitive (`StringComparison.Ordinal`) |

🔴 **The enum must adopt PARSE-OR-REJECT, not defaulting.** H1 (B4/B5) and H3 (B9) already reject an
unrecognized value with no default — H3 cites invariant I6 explicitly. The other four sites **default
silently and disagree with each other**: `H2a:284`, `H2b:289` and `AiSearchTenantFilterInvariantProbe:240`
coerce blank → Model 2; `ArmCostEnvelopeChecker:268` coerces **anything unrecognized, including wrong-case**
→ the **retired shared tier's** cost envelope. Parse at the edge, pass a typed value inward, and all four
disappear rather than being corrected one at a time.

⚠️ `AiSearchTenantFilterInvariantProbeTests` **pins the defaulting as a contract**
(`ProbeAsync_Model2_UnspecifiedTenancyModel_DefaultsToModel2_ReturnsPassed`). **Rewrite it, don't mechanically
update it.**

**P-2, the live defect this closes**: `H1SubscriptionReadinessHandler` holds
`CustomerOwnedTenancyValues = { "CustomerOwned", "Model2Dedicated" }`. `Model2Dedicated` covers **both** old-2a
(Spaarke's subscription) and old-2b (the customer's), so H1 **demands Lighthouse delegation for subscriptions
Spaarke already owns**. ✅ Good news: the *ownership* vocabulary (`SpaarkeOwned`/`CustomerOwned`) is already
correct and D-12's axis maps onto it exactly — retire the two *model* literals from those sets rather than
redesigning the classifier.

### Item 3 — 🔴 MIGRATE `sprk_tenancymodel`. Do NOT relabel.

- Today: `Model1Shared` = 0, `Model2Dedicated` = 1 (created by
  `scripts/Extend-DataverseEnvironmentSchema-v3.3.ps1:211,217-218`).
- 🔴 **Value `1` holds BOTH old-2a and old-2b**, which now split across **different** models and need
  **opposite** Lighthouse answers. Re-pointing labels **silently misclassifies every existing dedicated
  customer**.
- Fan value `1` out by the run's **`Profile`**: `spaarke-hosted-model2` → new **Model 1**;
  `customer-owned-model2` → new **Model 2**.
- Value `0` has **no successor**.
- ⚠️ H12c idempotency keys embed the tenancy string (`h12c-{customerId}-{tenancyModel}-{hash}`) — renaming
  **invalidates completed phases**. Plan for it.

### Item 4 — Retire `model1-*.bicep` — a SEVEN-surface atomic change

Deleting the stack alone **breaks the build**. All of these move together:

1. `infrastructure/bicep/stacks/model1-shared.bicep` + its checked-in `model1-shared.json`
2. `infrastructure/bicep/stacks/model1-customer.bicep` (orphaned — no `using`, no `module` caller)
3. `infrastructure/bicep/modules/model1-shared-l2-rbac.bicep` (sole caller is the stack, `:690`)
4. `infrastructure/bicep/parameters/model1-prod.bicepparam`
5. ⚠️ **`dev.bicepparam`, `prod.bicepparam`, `staging.bicepparam`** — all three bind the stack via `using`
   and **break compilation the moment it's deleted**. *Not on D-12's original list.*
6. 🔴 `scripts/tests/bicep-e2e-dry-run.ps1` — **Assertion 8 (`Model1SharedDeferredCallerFix`, `:483-490`)
   has INVERTED polarity: it PASSES only while the build FAILS** (`ExpectedBuild = 'EXPECTED_FAILURE'`).
   Delete the file and `$Results.BuildResults[…]` is `$null` → the assertion **fails** and the dry run goes
   red. Remove the `$Stacks` entry (`:174-180`), the assertion, the doc lines (`:12,18,79-85,597-598,666`)
   and drop the stack count 4 → 3.
7. 🔴 `.github/workflows/publish-provisioning-arm-artifacts.yml` — **compiles and uploads the stack to blob
   storage on every run**, and `.github/workflows/schemas/provisioning-arm-manifest.json:32` declares
   `model1-shared` a **`required`** key. H2a reads that manifest at provisioning time. ⚠️ **The harder
   blocker, and it is not in D-12's original list.**

*(Context: the stack has not compiled since 2026-08-17 — an unmigrated caller of the task-029 UAMI-only
`app-service.bicep`. Retiring it removes a permanently-red test.)*

---

## 6. Other things in your tree that now contradict the decisions

Found during our sweep, **not fixed** because they are yours:

- `scripts/provisioning-prereqs/prereqs.yaml` — four entries still name the shared BFF,
  `rg-spaarke-shared-{env}` and `model1-shared.bicep` (`PRQ-E-05`, `PRQ-E-06`, `PRQ-C-02`). Entangled with
  the H4-shared handler and item 4, so they belong to that atomic change.
- `scripts/canonical-secret-catalog/manifest.yaml` — the **source** for two generated files carrying
  *"Not populated on Model1 shared trial"*. 🔴 Fix the manifest and regenerate; don't edit the generated output.
- `scripts/provisioning/Seed-PlatformKeyVault.ps1:402-403` — seeds `SharedPlatformOpenAiEndpoint`, a retired
  artifact, for the H12c Model 1 branch.
- `infrastructure/bicep/modules/controlplane-worker-app-service.bicep:120,123,278,283` — `Model1Shared`-conditional
  worker config + `SharedPlatformOpenAiEndpoint`.
- `infrastructure/bicep/modules/bff-runtime-rbac.bicep:23-24,67,70,73` — `sprk-{env}-shared-bff-uami` for
  Model 1; per D-12/ADR-052 the **stamp UAMI** is used in both models.
- `infrastructure/bicep/modules/controlplane-subscription-rbac.bicep:38-39` — assumes a "fleet subscription"
  holding many customers' stamps; ADR-027 (amended) gives each customer their own.

---

## 7. Definition of done

- [ ] Worktree merged with master; **full arch suite 326/326**
- [ ] H3 creates one app registration per customer, unconditionally; shared-path options, request type and tests gone
- [ ] One `TenancyModel` enum; **zero** string comparisons against tenancy literals in production code; parse-or-reject at the edge
- [ ] `AiSearchTenantFilterInvariantProbeTests` **rewritten**, not mechanically updated
- [ ] `sprk_tenancymodel` **migrated** (fanned out by `Profile`), not relabelled; H12c key impact handled
- [ ] `model1-*.bicep` retired across **all seven** surfaces in §4; `bicep-e2e-dry-run.ps1` green; the ARM-artifact workflow and manifest schema updated
- [ ] `dotnet build src/server/api/Sprk.Bff.Api/` clean
- [ ] Docs in §6 corrected or explicitly deferred with a reason

---

## 8. NOT yours — so you don't duplicate it

`unified-access-control-r2` is doing these; they are in `Sprk.Bff.Api`, not the control plane:

- **`Secure Project` → `Secure Record`** business-unit rename (`Api/ExternalAccess/*`)
- **Three Redis cache-key sites** failing subject-discrimination (`agent-thread`, `agent-config`,
  `approle-module-map`) — required regardless of the Redis dedication decision

---

## 9. Open questions the owner has NOT yet answered

Do not assume an answer; ask.

1. **Power BI shared F-SKU capacity pool** — `reporting-admin.md` assumed one. Not on D-12's closed
   two-item sharing exception list. A shared pool would be a **new decision**.
2. **M365 Copilot agent** — shared vs per-customer, still TBD (`COMPONENT-INVENTORY.md` §11).
3. **Redis Standard tier performance** — dedication and the Standard SKU are decided; whether Standard meets
   the throughput bar is unverified.

---

## 10. If you think a decision is wrong

Use CLAUDE.md §6.5 and escalate to the owner. **Do not silently implement something different**, and do not
re-open **D-13** — it has been raised three times and §6 of that note records why the counter-argument fails.
