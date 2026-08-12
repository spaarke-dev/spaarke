# Project Update — Customer Provisioning & Deployment Orchestration (r1)

> **Date**: 2026-08-12
> **Author**: Owner working session (assessment + discussion)
> **Project**: `customer-provisioning-orchestration-r1`
> **Worktree**: `C:\code_files\spaarke-wt-customer-provisioning-orchestration-r1` · branch `work/customer-provisioning-orchestration-r1`
> **Design status before this update**: design.md written 2026-06-27, Q1–Q6 resolved (D12–D17), **awaiting owner review → `/design-to-spec`**. Cold ~7 weeks.
> **Purpose of this update**: Capture the 2026-08-12 assessment (fresh investigation of existing code, docs, prior projects, and Aug-2026 best practices), reconcile it with the locked design, and define a **design-refresh + to-do list** to run before `/design-to-spec` → `/project-pipeline`.
> **Companion doc**: [`COMPONENT-INVENTORY.md`](COMPONENT-INVENTORY.md) — authoritative bill-of-materials.

---

## 1. Why this update exists

The owner asked for a structured, repeatable, maximally-automated process to deploy Spaarke for new customers, with a north star of **"provision + deploy a new customer, off-the-shelf, in a day."** A six-workstream investigation (deployment tooling, prior projects, solution footprint, Azure/auth architecture, data side, and Aug-2026 best practices) plus a legal-AI cost-economics research pass produced the findings below. This project (`customer-provisioning-orchestration-r1`) is confirmed as the **correct convergence home** for that effort — but its design predates several of these findings and one locked decision (D3) is now worth re-validating.

---

## 2. Headline: this is a "unify + close 4 gaps" effort, not greenfield

Spaarke is ~70% of the way to repeatable customer deployment. The spine already exists and is production-proven (it built the demo env):

- **`scripts/Provision-Customer.ps1`** — 13-step, idempotent, resumable orchestrator (RG → Bicep → KV → Dataverse env → solution import → env-vars → SPE container → registry → smoke test).
- **`infrastructure/bicep/`** — 26 modules + `platform.bicep`/`customer.bicep` + **both** model stacks (`model1-shared`, `model1-customer`, `model2-full`), parameterized by `customerId`.
- **`Deploy-Release.ps1` / `Deploy-Platform.ps1` / `Decommission-Customer.ps1` / `Validate-DeployedEnvironment.ps1`** — app-layer release, platform, teardown, acceptance-scan.
- **`Build-SpaarkeMaster.ps1`** — machine composition of the full 386-component Dataverse solution.
- **This project's `design.md`** — 3-layer control-plane architecture (L1 handlers → L2 control-plane API/MCP → L3 swappable front ends), D1–D17 locked.

The real work: **converge ~10 overlapping deployment projects + ~11 drifting guides into one package + one control plane + one guide**, close four gaps (§6), and productize per the locked 3-layer design.

---

## 3. The two models map onto Microsoft's 2026 tenancy spectrum

| Spaarke model | Microsoft pattern | Target |
|---|---|---|
| **Model 1** (Spaarke-hosted, shared platform + per-customer isolation) | fully-multitenant / vertically-partitioned | SMB / trial — accept **logical** isolation |
| **Model 2** (fully dedicated stamp) | automated single-tenant / deployment stamp | Regulated legal — **physical** isolation |

Microsoft's own guidance uses **cloud software for legal firms** as the canonical example that justifies dedicated stamps, and states dedicated-per-customer is "unsustainable unless you provision a **dedicated subscription per tenant**." → When dedicating, the isolation boundary is an **Azure subscription per customer** (r1 **D4** already locked this), not just a resource group.

**Azure-tenant question resolved**: do **not** mint a separate Entra tenant per customer. Use **one Spaarke tenant + one multitenant Entra app**, isolate at the **subscription** layer. A separate tenant only enters when a customer **brings their own** (Model 2 in their tenant). This resolves the `spe-multi-tenant-architecture-r1` blocker as a **code change** (remove hardcoded `TENANT_ID`, make BFF multi-issuer aware), not a tenant-proliferation strategy.

**Recommended overall posture**: **vertically-partitioned** — Model 2 dedicated as the default for real/regulated customers; a Model 1 shared, metered tier for trials/demos where a per-prospect fixed floor is uneconomic.

---

## 4. ⚠️ Decision to reconcile: D3 (no shared resources) vs. cost of the fixed floors

**This is the one place where the assessment pushes back on the locked design and should be re-validated before spec.** (Per root CLAUDE.md §6.5 ADR/decision-conflict protocol.)

- **Locked**: r1 **D3 = "no shared resources between customers"** — dedicated per-customer OpenAI, AI Search, Doc Intelligence, Service Bus, Redis, Key Vault, App Insights; **D4 = subscription per customer**.
- **The tension**: three resources carry a **fixed monthly floor** regardless of usage — **App Service Plan, Azure OpenAI (provisioned TPM), Azure AI Search (fixed tier)**. Dedicating them per customer is honest and noisy-neighbor-free (Azure Cost Management + tags = native per-customer bill, **zero metering infra**), but the floor is brutal for small/trial customers who barely use the system.
- **The counter-option (Model 1 sharing)**: far lower floor, but requires building a **per-tenant token-metering layer** (APIM gateway or app-level custom metric keyed on tenant) to bill fairly, and accepts logical-only isolation (hard sell to regulated legal).

**Recommendation (to confirm)**: **keep D3 dedicated as the default** (right for legal; makes billing honest) **and** add two things as a **D3 amendment**:
1. A **shared, metered trial/SMB tier** (Model 1) for prospects — needs the metering layer.
2. Build the **per-tenant token-metering layer anyway** — it is a **no-regret** investment: it powers the pricing model (§5) under *any* tenancy choice.

**Resolution path**: Path A (project-scoped D3 amendment documented in design.md ADR-tensions) — not a full ADR change.

---

## 5. Cost economics — the Harvey/Legora per-seat problem, and how our model answers it

Legal-AI vendors are openly moving off flat per-seat pricing because agentic usage decouples cost from headcount (10–100× per-user variance). **Harvey** (Nov 2025) → usage/outcome + revenue-share; **Legora** (Jun 2026) → credit-based metered "Agent Pro." Category consensus: **hybrid — seat/platform floor + metered/credit lane for heavy usage.**

**Where token cost derives** (matters for architecture, not just pricing):
1. **Context re-transmission in the agent loop** (dominant — stateless model resends accumulated context every step; a 20-step task ≈ 200× a chat turn).
2. RAG context stuffing (2–10K tokens/query untuned).
3. **Long legal documents** (128K-context turn costs 4–6× a 16K one) — *Spaarke is agentic **and** long-document = worst-case token profile.*
4. Tool-schema/planning overhead, reasoning tokens, retries; then output/embeddings/OCR.

**Two-layer answer for Spaarke:**

**(a) Architectural cost controls** (attack the drivers): **prompt caching** on the stable agent-loop prefix (~50–90% off cached input), **model tiering** (cheap for routing/extraction, frontier for drafting; ~90% reduction reported), **retrieval + context compression** (RAG saves 60–80% vs stuffing), **per-tenant token budgets/quotas** (runaway-loop guardrail), **batch API** (~50% off) for non-interactive work, evaluate **PTU only after 30–60 days PAYG telemetry**.

**(b) Pricing model**: **platform/seat fee (predictable floor = margin hedge) + included AI allowance (credits/tasks) + metered overage.** Matches where Harvey/Legora landed. Reserve outcome/revenue-share as an experimental enterprise motion.

**The synthesis that closes the loop**: with r1's **dedicated-per-customer** model, each customer's Azure spend **is** their AI cost → **usage-passthrough pricing becomes natural and honest** (arguably more defensible than Harvey's, because COGS is transparently the customer's own dedicated resource bill). The **per-tenant metering layer is the single no-regret engineering investment** under any pricing model.

---

## 6. The four gaps between "70%" and push-button

| # | Gap | Fix | r1 handler |
|---|---|---|---|
| **1** | **Config seed decoupled from provisioning** (highest leverage). Solutions ship definitions, not rows → fresh env is non-functional (grids blank, wizards unmapped, AI dark, workspace won't render). | Fold existing seeders (`Deploy-All-AI-SeedData.ps1`, `Seed-PlaybookConsumers.ps1`, grid/field-mapping/workspace-layout seeds) into the orchestrator as a **declarative config-seed manifest step**; resolve the two-source AI seed drift. | H12 (make first-class, not "thin") |
| **2** | **Web-resource/code-page layer hardcoded to `spaarkedev1`.** | Harden `Deploy-Release.ps1` Phase 4 to be `customerId`-driven; chain into the orchestrator. | H6 + release |
| **3** | **Entra app registrations + Dataverse App User manual/single-tenant.** ~11 grants by hand, admin consent human-only, App User is PPAC-UI-only. Customer-tenant (Model 2) needs multi-issuer BFF + per-customer app-reg automation + admin-consent landing flow. Two steps are **irreducibly manual** (admin consent; SPE billing/consent). | H3 scripts grants idempotently; H10 attempts SDK then gate; build consent landing. | H3, H10, H11 |
| **4** | **No single verified acceptance gate; docs fragmented.** 4 overlapping master guides (stale ones not deprecated in-body), contradictory env-var counts, **silent-failure class** (`keyVaultReferenceIdentity`→UAMI; two Exchange policies; staging-slot different MI) not in customer runbook. | One authoritative guide + one validated env-var/app-setting manifest (reconciled to code `[Required]`) + extend `Validate-DeployedEnvironment.ps1` into an end-to-end "customer production-ready" gate. | validation |

**Live drift traps to fix regardless**: (a) SPE container creation now **403s on delegated token** → needs confidential-client app-only; (b) **Cosmos DB provisioning not in the 13 steps** though BFF won't start without it.

---

## 7. Self-service feasibility & data side (objectives #6, #7)

- **Customer self-deploy**: automatable for ~95%, gated by **two irreducible customer-admin actions** — (1) admin consent to the multitenant Entra app (per-tenant, cannot be bulk-applied), (2) SPE billing/consent activation. Package = solution primitives (managed solutions + Package Deployer) + control-plane pipeline + Claude Code primitives + **one** onboarding guide with `[CUSTOMER ADMIN]` gates marked. Build the **BFF to double as the consent-capture onboarding client** (capture `tid` on consent callback → trigger pipeline).
- **Data migration** (**genuine gap**): `Migrate-DataverseData.ps1` is Dataverse→Dataverse only and **excludes `sprk_document`/SPE content**; no CSV/legacy import, no bulk "load files into SPE + create `sprk_document` + index." Lives in the `spaarke-data` CLI (`SPAARKE-DATA-CLI`, Phase 0 only). r1 currently **scopes migration out** — confirm that's still right for "day-one" (acceptable: new customer starts empty-but-functional; migration is a follow-on).

---

## 8. Tooling recommendation: adopt the Terraform Power Platform provider (hybrid)

The "single package for Power Apps + Azure" is the **first-party Terraform Power Platform provider** — the only IaC toolchain covering **both** Dataverse environment lifecycle and Azure in one plan/state (Bicep has no Power Platform provider). **Recommendation (hybrid, not rip-replace):** keep the 26 tuned Bicep modules for the Azure stamp; adopt the **Terraform Power Platform provider for Dataverse env provisioning** (exactly where D14's manual PPAC steps live); use **Package Deployer** for the solution artifact. Gotcha: SPs **can't** create `Developer`-type envs (use Sandbox/Production); SP must be admin-bootstrapped via BAP API once.

---

## 9. North star reality check

**"Up and running in a day" is real for a clean off-the-shelf standup.** The automatable path runs in <1h of pipeline time. The three things that blow past a day are **lead-time, not compute**: Azure quota / OpenAI region capacity (1–3 days), SPE container-type replication (up to 24h), customer admin consent + security review (customer-dependent). **Restated north star**: *"Automated provisioning completes in <1h of pipeline runtime; customer is production-ready within one business day of admin consent + quota being in place."* Front-load the three lead-time items.

---

## 10. To-do — design-refresh before `/design-to-spec`

Ordered. Items 1–4 are the design-refresh delta; 5–6 are the deliverables already produced this session.

1. **[DECISION] Re-validate D3/D4** against §4–5. Either reaffirm D3 + add the metering-layer & shared-trial-tier amendment, or amend. Document in design.md **ADR-Tensions** (Path A). — *owner decision; blocks spec.*
2. **[DESIGN] Make config-seed (Gap 1) a first-class handler** — promote H12 from "thin" to a declarative config-seed manifest; resolve `scripts/seed-data` vs `infra/dataverse` source-of-truth. — *highest functional payoff.*
3. **[DESIGN] Refresh footprint** — the solution has grown since June (Compose, notifications, messaging, email-intelligence, etc.). [`COMPONENT-INVENTORY.md`](COMPONENT-INVENTORY.md) is the new baseline; work its §12 verification backlog.
4. **[DECISION] Confirm data-migration scope** (in or deferred to `spaarke-data` CLI) for the day-one north star.
5. **[DELIVERABLE ✅] Authoritative component inventory** — [`COMPONENT-INVENTORY.md`](COMPONENT-INVENTORY.md).
6. **[DELIVERABLE ✅] This project update** — captures assessment, decisions, gaps, to-do.

**Then**: `/design-to-spec` → `/project-pipeline`. Build sequence stays L1 handlers → L2 control plane → L3 operator skill (D8).

### Fast-follow engineering items (independent of tenancy decision)
- Build the **per-tenant token-metering layer** (APIM gateway or app-level custom metric) — no-regret.
- Fix the **SPE delegated-token 403** (confidential-client app-only).
- Add **Cosmos DB provisioning** to the orchestrator.
- Consolidate deployment docs → one authoritative guide + one validated env-var/app-setting manifest.
- Extend `Validate-DeployedEnvironment.ps1` → single end-to-end acceptance gate.

---

## 11. References

- Design: [`design.md`](design.md) (D1–D17, handler catalog H0–H14, risk register) · [`discovery/phase-0-discovery-report.md`](discovery/phase-0-discovery-report.md)
- Inventory: [`COMPONENT-INVENTORY.md`](COMPONENT-INVENTORY.md)
- Spine: `scripts/Provision-Customer.ps1`, `Deploy-Release.ps1`, `Deploy-Platform.ps1`, `Build-SpaarkeMaster.ps1`, `Validate-DeployedEnvironment.ps1`
- IaC: `infrastructure/bicep/**` (stacks `model1-shared`, `model1-customer`, `model2-full`)
- Guides to consolidate: `SPAARKE-DEPLOYMENT-GUIDE.md`, `CUSTOMER-ONBOARDING-RUNBOOK.md`, `auth-deployment-setup.md`, `MULTI-ENVIRONMENT-PROVISIONING-GUIDE.md`
- Related projects: `production-environment-setup-r2` (env-agnostic config), `spe-multi-tenant-architecture-r1` (customer-hosted auth — unbuilt), `spaarke-demo-data-setup-r1` (`spaarke-data` CLI)
- Architecture: `INFRASTRUCTURE-PACKAGING-STRATEGY.md` (Model 1/2), ADR-014, ADR-027 (subscription isolation; unmanaged-solution amendment 2026-06-02), ADR-028 (auth)
