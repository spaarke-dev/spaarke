# D-12 doc remediation — the canonical definition, and the file plan

> **Status**: inventory in progress (2026-09-28). Lane A (`docs/guides/**`, `docs/enhancements/**`) reported;
> two lanes still running. **No doc edits have been made yet.**
> **Gate**: D-12 §6 item 1 says the definition must be *picked first*, or remediation re-encodes the
> ambiguity. §1 below is that pick. Every rewrite below states it or points at it — never restates it
> differently.

---

## 1. The canonical definition — paste or link, never paraphrase

> Spaarke has **two deployment models**. They differ in **one axis only: which Azure tenant owns the
> customer's subscription.** Everything else is the same.
>
> | | Dataverse environment | Azure tenant | Azure subscription + resource group |
> |---|---|---|---|
> | **Model 1** | dedicated, one per customer | **Spaarke's** | dedicated, one per customer |
> | **Model 2** | dedicated, one per customer | **the customer's own** | dedicated, one per customer |
>
> **Every Azure resource is dedicated per customer in both models** — App Service Plan, AI Search, Redis,
> Azure OpenAI, Cosmos, Key Vault, Storage, Service Bus, SPE container, Dataverse environment.
>
> Only two things actually differ, and both follow from tenant ownership: **Model 2 requires H0.5 admin
> consent** and **Model 2 requires Azure Lighthouse delegation**. Model 1 requires neither.
>
> **Terminology, binding**: *tenant* = the Entra/Azure/Dataverse **tenant GUID**. *customer* = the business
> entity. Under Model 1 **many customers share one Azure tenant**, so `tenantId` **cannot** distinguish
> them. Never write "tenant" when you mean "customer".

**There is no Model 3.** There is no "shared", "trial" or "SMB" tier. There is no 2a/2b split.

⚠️ **The rewrite's biggest trap** (D-12 §4): the two models are now *nearly identical infrastructurally*.
There will be a pull to justify two models by inventing differences between them. Resist it — the list of
real differences is the two rows above.

---

## 2. Lane A — `docs/guides/**` + `docs/enhancements/**` ✅ swept

**6 BLOCKING · 6 WORDING (4 of them stubs) · 10 CLEAN**, of 22 files with hits.

### BLOCKING

| # | File | Shape of the work |
|---|---|---|
| A1 | `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` | 🔴 **Worst file in the repo for this.** Self-declared *authoritative*. ~25 model passages; needs a **section-level rewrite** of §1.1, §2.3, **all of §3**, §5 handler table, §6 env-var table, §7.2/§7.8, §12 — not a term swap. §3.2 is literally titled *"Model 1 — Shared Trial/SMB Tier"*. |
| A2 | `docs/guides/BYOK-CONFIGURATION-GUIDE.md` | Three-model matrix incl. an invented **Model 3**; asserts a **shared AI Search index** across customers. Rewrite §"Deployment Model Matrix" (L39-58) to two columns; retire the `Shared` value of `DefaultRagModel`. |
| A3 | `docs/guides/RAG-ARCHITECTURE.md` | Names the triad **Shared/Dedicated/CustomerOwned** and marks **Shared as the DEFAULT**, isolated by `tenantId` filter — the exact control D-12 shows delivers nothing under Model 1. Delete or retire the Shared section; make Dedicated the default. |
| A4 | `docs/enhancements/SPAARKE-AI-STRATEGY-AND-ROADMAP.md` | *"Model 1: Spaarke-Hosted — multi-tenant with logical isolation (tenantId filtering)"*. Rewrite the Deployment Models section (L179-190) + the cost-recovery section. |
| A5 | `docs/guides/SPAARKE-AI-STRATEGY-AND-ROADMAP.md` | **DELETE, do not fix** — see §3 below. |
| A6 | `docs/guides/PROVISIONING-PREREQUISITES.md` | `PRQ-T-07` = a shared multitenant BFF app-reg *"(Model 1 tier only)"* flagged **`never_delete: true`**. See §4 — the machine-readable source is worse. |

⚠️ **A6 has a correct row that must NOT be "fixed"**: `PROVISIONING-PREREQUISITES.md:30` —
*"`once_per_tenant` | Spaarke Model 1 tenant (or customer tenant Model 2)"* — is **already right** under
D-12. Flagged because a careless sweep would break it.

### WORDING

`AI-DEPLOYMENT-GUIDE.md` (3 real passages incl. a `model1-shared.bicep` troubleshooting entry),
`AI-MONITORING-DASHBOARD.md` (**defer** — it references `model2-full.bicep`, which is *not* retired; it goes
stale only *after* the Bicep rename, so it is downstream of the code work, not an independent fix), and four
**stubs** needing a single parenthetical deleted: `CUSTOMER-DEPLOYMENT-GUIDE.md`,
`ENVIRONMENT-DEPLOYMENT-GUIDE.md`, `PRODUCTION-DEPLOYMENT-GUIDE.md`, `SPAARKE-DEPLOYMENT-GUIDE.md`.

---

## 3. ✅ Verified: the two AI-STRATEGY files are the SAME DOCUMENT

Measured, not inferred: `diff` between `docs/guides/SPAARKE-AI-STRATEGY-AND-ROADMAP.md` (379 lines) and
`docs/enhancements/SPAARKE-AI-STRATEGY-AND-ROADMAP.md` (389 lines) is **20 lines, entirely in the header**.
The bodies are **identical**.

The `enhancements` copy's own header explains why:

> *"**Note (2026-04-05)**: Moved from `docs/guides/` to `docs/enhancements/` during the R2 documentation
> refactoring — this is a strategy/positioning document, not a how-to guide."*

**The move copied the file and never deleted the original.** Five months later both are still live, both
carry the identical wrong Model 1 definition, and nothing marks one as superseded.

🔴 **Therefore A5 is a DELETE, not a fix.** `docs/enhancements/` is the declared destination; the `guides`
copy is a leftover. Fixing both would double the work *and* leave the duplication in place to re-diverge —
which is how four conflicting Model 1 definitions arose in the first place. Check inbound links before
deleting; leave a stub if any exist.

---

## 4. 🔴 The machine-readable source of truth is worse than the doc

`scripts/provisioning-prereqs/prereqs.yaml:121-129` — the **declared source of truth** behind
`PROVISIONING-PREREQUISITES.md`:

```yaml
  - id: PRQ-T-07
    name: Multitenant BFF app-reg (Model 1 tier only — shared across all Model 1 customers)
    scope: once_per_tenant
    frequency: once for entire Model 1 tier
    owner: Spaarke platform admin
    never_delete: true
    consequence_of_absence: Model 1 customers cannot authenticate; per spec.md v3.5 MUST rule.
    check_recipe:
      cli: az ad app list --filter "signInAudience eq 'AzureADMultipleOrgs'" ...
```

It states the retired tier **three times** (`name`, `frequency`, `consequence_of_absence`), carries a
**`never_delete: true`** protecting a retired artifact, and — unlike the markdown — has an **executable
`check_recipe`**. Fixing only the markdown leaves the authoritative, runnable copy wrong.

D-12 §6 already lists `SharedBffAppRegistrationId` as a retired artifact; this is where it is enforced.

---

## 5. Lane B — `docs/architecture` + `docs/adr` + `docs/assessments` + `docs/procedures` + `docs/data-model` ✅ swept

**15 BLOCKING · 15 WORDING · 1 AMBIGUOUS · 14 CLEAN**, of 45 files with hits.

### BLOCKING

`INFRASTRUCTURE-PACKAGING-STRATEGY.md` · `SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md` ·
`INSIGHTS-ENGINE-ARCHITECTURE.md` · `caching-architecture.md` · `rag-architecture.md` ·
`auth-AI-azure-resources.md` · `ai-semantic-relationship-graph.md` · `ci-cd-architecture.md` ·
`ADR-009` · `ADR-013` · `ADR-015` · `ADR-052` · `procedures/ci-cd-workflow.md` ·
`procedures/MULTI-ENVIRONMENT-PROVISIONING-GUIDE.md` ·
`assessments/quality-followups-execution-checkpoint-2026-08-19.md`

### 🔔 Four ADRs need **Decision-text** amendments — owner approval, not a doc fix

This is the boundary between remediation and new decisions. **Do not write these without sign-off**
(CLAUDE.md §6.5). Listed in descending order of consequence:

| ADR | What changes | Why it is Decision text, not wording |
|---|---|---|
| **ADR-015** (AI data governance) | Tier 2 + Tier 3 **MUST** *"partition by `tenantId` with no cross-tenant query capability"*, and the Cosmos container mapping (`audit`, `sessions`, `memory` → `/tenantId`) | These MUSTs carry the **7-year immutable audit** and **GDPR Art.17 erasure** guarantees. Under Model 1 all customers share one partition value. |
| **ADR-009** (caching, Redis-first) | Decision §5 *"Cache key tenant prefix mandatory"* — the **enforced key format** `tenant:{tenantId}:{resource}:{id}:v{version}` | D-12 §3 names this exact key as the 🔴 collision. Either the format gains `customerId`, or the ADR must state the prefix is **not** a customer boundary. |
| **ADR-052** (workload placement) | §6 Guardrails (*"a shared multi-tenant Function app is acceptable"* for Model 1, *"the shared BFF UAMI"*), §7 (*"a shared hub for Model 1"*), §10 (per-tenancy-model approval gate) | Binding guardrails sanctioning shared compute for Model 1. ⚠️ `WorkloadPlacementDocDriftTests` **fails the build** on contradicting phrasings — editing prose here can redden CI. |
| **ADR-013** (AI architecture) | Context two-model framing + the normative Azure Resource Requirements tables (shared OpenAI per region, shared Redis, differential TPM) | Decision text (in-process BFF placement) is **unaffected** — lower bar, but it still changes the ADR's stated rationale. |

### ⚠️ Two conflicts that exist TODAY, independent of D-12

1. **ADR-042 contradicts ADR-015 and neither cites the other.** ADR-042 *rejects* `/tenantId` partitioning
   (*"dedicated-per-customer environments make tenant a single hot partition"*) and already assumes
   customer-dedicated deployments — it is the **one doc in the lane that pre-agrees with D-12**. But it
   rejects on **capacity** grounds, never states the **isolation** consequence, and leaves the legacy
   `/tenantId` container in place (*"MUST NOT be retired or re-keyed"*) while ADR-015 still mandates that
   key as the compliance standard.
2. `assessments/quality-followups-execution-checkpoint-2026-08-19.md:130` says *"scope the multitenant +
   consent mechanism explicitly to **Model 1**"* — **inverted** under D-12 §5. Model 1 needs neither
   consent nor Lighthouse; that mechanism belongs to Model 2.

---

## 6. 🔴 Two findings verified by me, both worse than reported

### 6.1 The "D-12" identifier collides — 4 sites, 2 of them MUST rules

✅ **Verified.** `docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md` uses **`D-12` as a local decision ID**
meaning *"`tenantId` is first-class on every new index"*:

- `:391` — *"Per D-12 it is first-class on every new index"*
- `:1670` — table row: *"`tenantId` first-class on every new index | D-12"*
- `:1766` — *"Do not create any new index without `tenantId` as a first-class field (D-12)."*
- `:1793` — *"**MUST** carry `tenantId` as a first-class top-level field on every Insight artifact and on
  every new index schema (D-12)."*

The agent found two sites; there are **four**, and `:1766`/`:1793` are **MUST** rules. A reader who knows
the owner's D-12 will read these as *D-12 mandating `tenantId`-keyed isolation* — the precise opposite of
what D-12 says. **This must be disambiguated BEFORE any remediation cites D-12 in that file.**

### 6.2 `agent-thread:{tenantId}` — a latent cross-**user** defect, not just cross-customer

✅ **Verified in source**, and the scope is wider than the lane report claimed.
`AgentServiceClient.cs:52-56` composes the on-wire key
`spaarke:tenant:{tenantId}:agent-thread:thread:v1` from `ITenantCache`, where `CacheResource` and `CacheId`
are **compile-time constants**. The key therefore varies by **`tenantId` and nothing else** — no user, no
session, no customer.

Consequences, in order of severity:

1. **Today, inside a single tenant: every user shares one Foundry agent thread.** `CreateOrResumeThreadAsync(tenantId)`
   resumes whatever thread the last user created. That is a cross-**user** conversation-history leak, and it
   does **not** depend on D-12, Model 1, or tenancy at all.
2. **Under Model 1 it additionally becomes cross-customer**, since every customer presents Spaarke's
   `tenantId`.

✅ **Mitigating — verified**: `AgentServiceOptions.Enabled` defaults to **`false`** (`:26`, `[Required]`,
opt-in) and no `appsettings*.json` sets it. So this is **latent, not live** — one config flag away.

Contrast with `chat:session:{tenantId}:{sessionId}`, which is safe because `sessionId` discriminates. This
one has no second key part.

**This is not doc remediation.** It needs its own fix + a regression test, and it should not ride in a
documentation commit. Filing separately.

---

## 7. Sequencing

1. **§1 is fixed first** and every rewrite conforms to it. (D-12 §6 item 1.)
2. **A5 delete** before A4 — otherwise A4's fix gets copied into a file that is about to be removed.
3. **A6 + §4 together** — the YAML is the source of truth; the markdown alone is not a fix.
4. **A1 last among the BLOCKING set.** It is the authoritative guide, so it should be written once the
   smaller files have settled the vocabulary rather than being rewritten twice.
5. **`AI-MONITORING-DASHBOARD.md` is deferred** to the Bicep rename — it is not wrong today.
6. Lanes B and C pending; this plan will extend, not change, when they land.
