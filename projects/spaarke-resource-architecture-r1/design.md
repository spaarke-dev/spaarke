# Spaarke Resource Architecture — Design Document

> **Project**: `spaarke-resource-architecture-r1`
> **Status**: DRAFT — design only, execution deferred until `sdap-bff-api-remediation-fix` completes
> **Created**: 2026-05-23
> **Recalibrated**: 2026-05-23 after external review by DeepSeek, ChatGPT, Gemini, and Claude (see [`research/external-perspectives.md`](research/external-perspectives.md))
> **Audience**: Project owner, BFF developers, infra/IaC engineers, security reviewer

---

## 1. Context

### 1.1 What prompted this project

Today's AI Search indexing regression investigation (2026-05-22) traced through a chain of issues: orphan chunks from the wizard upload pipeline, GUID case mismatch between Xrm and Dataverse Web API clients, and a `privilege_group_ids` field created without `filterable: true` that cannot be amended post-creation. While fixing those, a thorough grep revealed that a single resource identifier — `spaarke-knowledge-index-v2` — appears in **184 places across 67 files**: BFF code, tests, schema files, deploy scripts, documentation, project artifacts.

That's one identifier. The same pattern of scattered references exists for ~13 categories of resources Spaarke depends on:

1. Azure AI Search service + indexes (8 in the dev environment, several legacy/orphan)
2. Azure OpenAI account + ~6 model deployments
3. Key Vault + ~20 secret names
4. Cosmos DB databases + containers
5. Service Bus namespace + queues + topics
6. Redis caches + key prefixes
7. App Services (BFF + future Connect)
8. Application Insights
9. Storage accounts + blob containers
10. SharePoint Embedded container types + per-customer containers
11. Dataverse environment URL + ~50 entities + solution components
12. Managed Identities + AAD app registrations
13. Spaarke-internal abstractions (playbook IDs, capability names, scope IDs, knowledge source IDs)

The order-of-magnitude technical debt is in the thousands of references.

### 1.2 The real diagnosis

External review (Claude, in particular) reframed the problem precisely:

> *"That's not primarily a source-of-truth problem — it's an indirection problem. You leaked the physical name of a resource through every layer of your code, tests, scripts, and templates instead of leaking a logical identifier that gets bound to a physical name once, at one boundary."*

The fix is not a meta-registry. The fix is:

- **One naming function** in IaC that all resource names flow through
- **Typed catalog interfaces** in code with enum-driven accessors that make string-literal resource references a compile error
- **Per-customer Bicep parameter files** with validation that refuses to provision without an explicit customer profile
- **Deployment Stacks with deny-write-and-delete** to prevent portal-drift at the source
- **Layered audit tooling** for the residual drift surface

### 1.3 What success looks like

- A new resource family can be introduced without touching 67+ files. Touch the catalog enum, the naming function, the customer profile schema, and the Bicep module. Done.
- A rename of an existing resource is mechanical and safe. Bicep params change, App Service settings re-deploy, code is unaffected because it consumes by enum.
- A customer deployment cannot accidentally inherit a shared default. The validator refuses without an explicit declaration.
- A portal change to a provisioned resource is denied at the platform level.
- A discrepancy between code expectations, manifest declarations, and live Azure state shows up in CI or nightly audit, not as a user-facing regression.

### 1.4 What Spaarke already has (the baseline is stronger than initially assumed)

External review confirmed that Spaarke is roughly 40-50% there:

- **[ADR-027](../../.claude/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md)** already established `platform.bicep` / `customer.bicep` separation in production. Resource groups follow `rg-spaarke-platform-{env}` and `rg-spaarke-{customerId}-{env}`.
- **23 modular Bicep templates** in `infrastructure/bicep/modules/` cover the core resource families (ai-foundry-hub, ai-search, openai, content-safety, cosmos-db, key-vault, service-bus, storage-account, app-service, monitoring, redis, etc.).
- **Three deployment models** are already coded in [`IKnowledgeDeploymentService`](../../src/server/api/Sprk.Bff.Api/Services/Ai/IKnowledgeDeploymentService.cs): `Shared`, `Dedicated`, `CustomerOwned` (BYOK).
- **Partial resource inventory** already exists in [`docs/architecture/auth-AI-azure-resources.md`](../../docs/architecture/auth-AI-azure-resources.md) (619 lines, AI resources) and [`docs/guides/CONFIGURATION-MATRIX.md`](../../docs/guides/CONFIGURATION-MATRIX.md) (400+ lines, 50+ settings).
- **Token substitution pattern** at CI/CD time with `#{TOKEN_NAME}#` markers in [`appsettings.template.json`](../../src/server/api/Sprk.Bff.Api/appsettings.template.json), documented in [`appsettings.tokens.md`](../../src/server/api/Sprk.Bff.Api/appsettings.tokens.md). 28 tokens.
- **18 `*Options.cs` classes** in `Configuration/` using `IOptions<T>` pattern with validators.

**What's missing**:
- Code abstraction (typed family catalogs) that's the single binding point for resource identifiers in code
- Cross-layer naming-convention enforcement (Bicep params + tokens + code literals are independent today)
- Drift prevention (Deployment Stacks)
- Drift detection tooling (live state vs. manifest vs. code expectations)
- An ADR articulating the unified resource architecture
- Customer-override mechanism with explicit-opt-in semantics enforced by schema

---

## 2. External Review Summary

Four AI advisors (DeepSeek, ChatGPT, Gemini, Claude) were asked the same nine-question prompt about Spaarke's situation. Verbatim responses are preserved in [`research/external-perspectives.md`](research/external-perspectives.md). Key findings:

### 2.1 Strong consensus (3+ of 4 agreed) — TAKEN AS DECIDED

| Decision | Rationale |
|---|---|
| **Hybrid model**: Git is contract → Bicep/IaC binds → Azure App Configuration is runtime projection → Key Vault holds secrets | Git for review/diff/versioning; App Config for runtime label-based tenant filtering; KV unchanged role |
| **AVM selectively, not religiously** | Use AVM resource modules as building blocks; keep custom Bicep for Spaarke-specific orchestration. Critical context from Claude: classic ALZ-Bicep archived Feb 2026; AVM is the direction |
| **No god-catalog** | One `IResourceCatalog` interface becomes a 30-method monster. Use per-family catalogs |
| **Typed accessors with enums, NOT strings** | `catalog.GetIndex(SearchIndex.Documents)` instead of `catalog.GetIndex("documents")`. **Highest leverage single change in the entire proposal** — gives the compiler a chance to catch every drift |
| **Customer-specific by default via schema enforcement** | Validation refuses to provision without an explicit customer profile. No silent shared default |
| **Globally-unique vs scoped names follow different rules** | Encode customer in globally-unique resource names; do NOT repeat the parent's scope in nested names (indexes inside a search service, secrets inside KV) |
| **Dataverse: infrastructure references YES, entity schema NO** | Dataverse env URL, app IDs, MI client IDs → catalog. Entity logical names → solution-versioned generated constants |
| **Don't store the catalog in Dataverse** | Bootstrap loop — you have to know how to reach Dataverse before you can read the config that tells you which Dataverse |
| **Drift detection is layered** | Bicep `what-if` + Azure Resource Graph audit + custom reconciler. None alone is sufficient |
| **One resource family per slice, end-to-end** | AI Search is the natural first slice |

### 2.2 Divergence — the real decision points

1. **Standalone YAML/JSON manifest** (DeepSeek/Gemini/ChatGPT) **vs `.bicepparam` files** (Claude). **Resolved**: Claude's position wins. Bicep parameters with `@validate()` decorators + generated solution constants + typed catalogs collectively form the manifest. A separate YAML file would create the synchronization problem it was meant to solve.
2. **App Configuration as first-class vs runtime projection**. **Resolved**: Runtime projection (Claude's sequencing). App Service settings populated by deployment outputs handle 95% of the case. App Config comes online when there's a clear runtime-mutability need (feature flags, AI prompt overrides).
3. **Azure Deployment Stacks priority**. **Resolved**: Claude is the only one who raised this; it's the most consequential addition to the original plan. Deployment Stacks with `--deny-settings-mode denyWriteAndDelete` is the prevention layer that makes drift detection mostly unnecessary.

### 2.3 Claude's framing challenge — "do you really need to do this?"

This was the most important question. Re-evaluation:

| Original component | Real value? | Verdict |
|---|---|---|
| Typed family catalogs with enums | Eliminates the 184-references problem at compile time | **Keep — load-bearing** |
| Single Bicep naming function | Eliminates cross-layer drift in resource names | **Keep — load-bearing** |
| Per-customer `.bicepparam` files with `@validate()` | "Customer-specific by default" enforced by schema | **Keep — load-bearing** |
| Standalone YAML/JSON manifest | Source of truth across Azure + Dataverse + Spaarke abstractions | **Drop — redundant** |
| Azure App Configuration runtime layer | Feature flags, per-tenant overrides | **Defer to Phase 5 — when needed** |
| Custom drift-detection tooling | Catch live-state drift | **Reduce to a small reconciler** — Deployment Stacks prevents most of it |
| Dataverse entity logical names in catalog | Centralized refs | **Drop — generated solution constants are the right answer** |

**The project is meaningfully smaller than originally proposed.**

---

## 3. Recalibrated Approach

### 3.1 Architecture in one diagram

```
                         GIT (source of truth)
   ┌────────────────────────────────────────────────────────────┐
   │                                                            │
   │   infrastructure/bicep/                                    │
   │   ├── platform.bicep        (existing — shared resources)  │
   │   ├── customer.bicep        (existing — per-customer)      │
   │   ├── naming.bicep          (NEW — single naming function) │
   │   ├── modules/              (existing — 23 modules, AVM as │
   │   │                          they're touched)              │
   │   └── customers/                                           │
   │       ├── acme.prod.bicepparam                             │
   │       └── globex.prod.bicepparam                           │
   │   (NEW — per-customer profiles with @validate())           │
   │                                                            │
   │   src/server/api/Sprk.Bff.Api/Services/Catalog/            │
   │   ├── ISearchCatalog.cs                                    │
   │   ├── IQueueCatalog.cs                                     │
   │   ├── IIdentityCatalog.cs                                  │
   │   ├── IAiCatalog.cs                                        │
   │   ├── ISecretCatalog.cs                                    │
   │   └── … (NEW — per-family typed catalogs)                  │
   │                                                            │
   │   src/server/api/Sprk.Bff.Api/Catalog/Enums/                │
   │   ├── SearchIndex.cs                                       │
   │   ├── WorkQueue.cs                                         │
   │   ├── WellKnownSecret.cs                                   │
   │   └── … (NEW — enums every accessor uses)                  │
   │                                                            │
   └─────────────────────┬──────────────────────────────────────┘
                         │
                         ▼  (deployment via Azure DevOps / GitHub Actions)
   ┌────────────────────────────────────────────────────────────┐
   │  AZURE DEPLOYMENT STACKS                                   │
   │  --deny-settings-mode denyWriteAndDelete                   │
   │  Provisions resources, manages lifecycle, blocks portal    │
   │  changes outside the stack.                                │
   └─────────────────────┬──────────────────────────────────────┘
                         │
                         ▼
   ┌────────────────────────────────────────────────────────────┐
   │  AZURE APP SERVICE SETTINGS  (per BFF instance per customer) │
   │  Populated by Bicep outputs:                                 │
   │   - Catalog__Search__Documents = "spaarke-acme-prod-search"  │
   │   - Catalog__Queue__ConnectJobs = "sprk-acme-jobs"           │
   │   - Catalog__Secret__AISearchKey = "@KV(.../AISearch-Key/)"  │
   └─────────────────────┬──────────────────────────────────────┘
                         │
                         ▼
   ┌────────────────────────────────────────────────────────────┐
   │  BFF STARTUP — typed catalog implementations bind from      │
   │  IConfiguration. Validation errors are fail-fast.           │
   │                                                            │
   │  No business-logic code references resource names as       │
   │  string literals. Test-double catalogs are injected for    │
   │  unit/integration tests.                                   │
   └────────────────────────────────────────────────────────────┘

  PHASE 5+:
   Azure App Configuration is added for runtime-mutable config
   (feature flags, AI prompt overrides), with label filtering
   by customer/env. NOT for resource names.

  ONGOING:
   - Bicep what-if in CI on every PR
   - Nightly Azure Resource Graph audit per customer subscription
   - Small custom reconciler asserts code expectations match
     manifest declarations match live deployed state
```

### 3.2 Catalog interface design

**Pattern**: per-family interface, enum-driven accessor, scoped lifetime, IConfiguration-backed implementation.

Example for AI Search (the Phase 1 family):

```csharp
// Enums — additive only. Removing an enum value is a breaking change.
public enum SearchIndex
{
    Files,         // file/document chunks for RAG (replaces "knowledge-index-v2")
    Records,       // Dataverse record metadata for matching
    References,    // curated reference knowledge (golden refs)
    Playbooks,     // playbook semantic vectors
    Invoices       // invoice processing
}

// Interface — narrow, family-scoped, no string keys.
public interface ISearchCatalog
{
    string GetIndexName(SearchIndex index);
    Uri Endpoint { get; }
    string ResolveChunkId(SearchIndex index, string speFileId, int chunkIndex);
    // Add methods only when they're actually needed by a consumer.
}

// Implementation — reads from IConfiguration populated by App Service settings
// which were populated by Bicep deployment outputs.
internal sealed class ConfigSearchCatalog : ISearchCatalog
{
    private readonly IDictionary<SearchIndex, string> _indexNames;
    public Uri Endpoint { get; }

    public ConfigSearchCatalog(IConfiguration configuration)
    {
        // Bind under "Catalog:Search" section. Throw at startup if any required
        // index is missing — there is no silent fallback to a literal.
        _indexNames = new Dictionary<SearchIndex, string>
        {
            [SearchIndex.Files]      = Require(configuration, "Catalog:Search:Files"),
            [SearchIndex.Records]    = Require(configuration, "Catalog:Search:Records"),
            [SearchIndex.References] = Require(configuration, "Catalog:Search:References"),
            [SearchIndex.Playbooks]  = Require(configuration, "Catalog:Search:Playbooks"),
            [SearchIndex.Invoices]   = Require(configuration, "Catalog:Search:Invoices"),
        };
        Endpoint = new Uri(Require(configuration, "Catalog:Search:Endpoint"));
    }

    public string GetIndexName(SearchIndex index) => _indexNames[index];

    public string ResolveChunkId(SearchIndex index, string speFileId, int chunkIndex)
        => $"{speFileId}_{chunkIndex}"; // (existing pattern; canonicalised here)

    private static string Require(IConfiguration cfg, string key) =>
        cfg[key] ?? throw new InvalidOperationException(
            $"Resource catalog missing required configuration: {key}. " +
            "Every catalog entry MUST be populated by Bicep deployment outputs. " +
            "If you see this error, check the App Service application settings " +
            "and the per-customer .bicepparam file.");
}
```

**Why this shape**:
- One enum + one interface per family. Adding a sixth index = add an enum value + add a `Require()` line. Mechanical.
- `GetIndexName(SearchIndex.Files)` is the only way to retrieve an index name. The compiler refuses `GetIndexName("files")`.
- Failures are fail-fast at startup, not silent at runtime. A missing configuration entry is a deployment bug, surfaced at boot.
- Implementation is internal. Tests inject `TestSearchCatalog`; production code never sees `ConfigSearchCatalog` outside DI registration.

The same shape applies for `IQueueCatalog`, `IIdentityCatalog`, `IAiCatalog`, `ISecretCatalog`, etc. ~5-7 family catalogs cover the 13 resource categories.

### 3.3 Bicep naming function (single source)

One Bicep file owns the naming logic. Every resource name in templates flows through it. Pseudocode:

```bicep
// infrastructure/bicep/naming.bicep
@export()
func ResourceName(
  customer string,
  environment string,
  kind string,        // 'search', 'kv', 'sb', 'cosmos', 'storage', 'app', etc.
  purpose string,     // 'files', 'records', 'jobs', 'sessions', etc. — optional
  region string       // 'eus', 'wus2', etc.
) string => /* deterministic slug rules + length truncation per Azure resource limits */

@export()
func ScopedResourceName(
  kind string,        // 'index', 'secret', 'queue'
  purpose string      // 'files', 'AISearchKey', 'jobs'
) string => /* simple, no customer/env — the parent already scopes it */
```

Usage:
- Globally-unique: `sprk-{customer}-{env}-{purpose}-{kind}` → `sprk-acme-prod-search`, `sprk-acme-prod-kv`
- Scoped within a parent: just `{purpose}` → index `files`, queue `jobs`, secret `AISearchKey`

**Length truncation rules** are encoded in `naming.bicep`. Storage accounts (24 chars, no dashes) and Key Vault names (24 chars, dashes ok) get deterministic truncation that's stable across re-runs.

### 3.4 Per-customer Bicep profiles

```bicep
// infrastructure/bicep/customers/acme.prod.bicepparam
using '../customer.bicep'

param customer = 'acme'
param environment = 'prod'

// Every required resource must be declared. @validate() in customer.bicep
// fails the deployment if any required parameter is missing.
param search = { mode: 'dedicated' }
param openAi  = { mode: 'shared' }          // explicit opt-in to shared
param keyVault = { mode: 'dedicated' }
param serviceBus = { mode: 'dedicated' }
param cosmos = { mode: 'dedicated' }
// ...
```

**Customer-specific by default** is enforced by `customer.bicep`'s `@validate()` decorators:

```bicep
@validate('search.mode must be specified — dedicated, shared, or customerOwned')
param search { mode: 'dedicated' | 'shared' | 'customerOwned', ... }
```

No customer accidentally inherits a shared default. The default mode for every resource is `null`, which `@validate()` rejects. Opting into shared is an explicit line.

### 3.5 Naming convention

`sprk-{customer}-{env}-{purpose}-{kind}` for globally-unique resources. Bare `{purpose}` for scoped resources. `sprk-` prefix matches the Dataverse `sprk_` publisher prefix for visual consistency.

| Category | Pattern | Examples |
|---|---|---|
| AI Search service | `sprk-{customer}-{env}-search` | `sprk-acme-prod-search` |
| AI Search index (scoped inside service) | `{purpose}` | `files`, `records`, `references`, `playbooks`, `invoices` |
| Service Bus namespace | `sprk-{customer}-{env}-sb` | `sprk-acme-prod-sb` |
| Service Bus queue (scoped inside ns) | `{purpose}` | `jobs`, `connect-events` |
| Cosmos DB account | `sprk-{customer}-{env}-cosmos` | `sprk-acme-prod-cosmos` |
| Cosmos database (scoped) | `{purpose}` | `ai`, `chat-sessions` |
| Key Vault | `sprk-{customer}-{env}-kv` | `sprk-acme-prod-kv` (24-char cap honoured) |
| Key Vault secret (scoped) | `{ResourceFamily}-{Purpose}` (PascalCase) | `AISearch-AdminKey`, `Graph-WebhookSigningKey` |
| App Service | `sprk-{customer}-{component}-{env}` | `sprk-acme-bff-prod` |
| Resource Group | `rg-spaarke-{customer}-{env}` | `rg-spaarke-acme-prod` (existing ADR-027 convention, kept) |

**Migration note**: existing dev resources (e.g., `spaarke-search-dev`, `spaarke-knowledge-index-v2`) do NOT auto-rename. Phase 1 stands up new resources with the new convention, migrates code to consume them, and decommissions the old. Customers don't see a churn event; dev gets a one-time rename pass.

### 3.6 Phased execution plan

| Phase | Scope | Duration | Risk |
|---|---|---|---|
| **0** | Manifest schema review, ADR draft, naming convention ratified, full resource-reference inventory across all 13 categories (extends today's AI Search inventory) | ~1-2 weeks | Low |
| **1** | AI Search family end-to-end: typed `ISearchCatalog` + enums + Bicep naming function + per-customer .bicepparam + new AI Search service stand-up in dev + new SPE containers + code refactor + tests + decommission old. **This is the proof of pattern.** | ~3-4 weeks | Medium — first family, most architecture work, fixes the privilege_group_ids deferred issue |
| **2** | Customer profiles + Deployment Stacks. Migrate dev + demo to Deployment Stacks with denyWriteAndDelete. Establish customer onboarding tooling that generates `.bicepparam` from a template | ~2-3 weeks | Medium — Deployment Stack migration has live-resource implications |
| **3** | Convert remaining Azure resource families: OpenAI, Service Bus, Cosmos, Redis, Key Vault, App Services, App Insights, Storage. One per slice | ~3-4 weeks | Low — pattern is proven |
| **4** | M365 / Dataverse / Graph infrastructure refs (env URL, MIs, app registrations). Entity logical names handled separately via solution-generated constants | ~1-2 weeks | Low |
| **5** | Azure App Configuration for runtime-mutable config (feature flags, AI prompt overrides, throttle thresholds). NOT for resource names | ~1 week | Low |
| **6** | Drift detection: Bicep what-if in CI, nightly ARG audit, custom reconciler | ~1 week | Low |
| **7** | AVM migration: opportunistic, not time-boxed. New resources use AVM; existing swap as touched | Ongoing | Very low |

Total: ~3-4 months of focused work, broken into useful 1-2 week slices.

---

## 4. Open Questions (to revisit after BFF remediation completes)

These need either user decision, BFF-remediation-output input, or external clarification:

1. **Catalog DI lifetime** — Singleton or Scoped? Singleton is simpler if catalog state is read-only; Scoped is needed if catalog values can vary per-request (e.g., tenant-aware in a multi-tenant BFF instance). Defer until the BFF remediation tells us whether BFF instances will be single-tenant or multi-tenant.
2. **Where catalogs live in the namespace structure** — `Services/Catalog/`? `Infrastructure/Catalog/`? Depends on where BFF remediation lands `Services/Ai/PublicContracts/`. Coordinate with remediation team.
3. **Test catalog injection pattern** — `WebApplicationFactory` overrides? Direct constructor injection in unit tests? Both? Probably both, with a shared `FakeSearchCatalog` builder in test helpers.
4. **Generated solution constants for Dataverse entities** — manual or tool-generated (`pac modelbuilder` early-bound types)? Today entity logical names are scattered as constants; consider unifying via a generator.
5. **Customer onboarding flow** — is there an existing CLI tool or pipeline we extend, or does this project build a `Generate-CustomerProfile.ps1` from scratch?
6. **App Configuration store sharing** — one store per environment with customer/env labels, or one store per customer? The Microsoft multi-tenant guidance recommends prefixes for tenant settings and reserving labels for other purposes — needs investigation when Phase 5 starts.
7. **ADR scope** — does the new ADR ("Spaarke Resource Catalog Architecture") supersede parts of ADR-027 or strictly extend? Probably extend, with ADR-027 cross-referenced.
8. **Migration of existing dev resources** — accept the rename churn in dev (no customer impact), but coordinate timing with any ongoing demo / customer work.

---

## 5. Why this is deferred — the BFF remediation interaction

The [`sdap-bff-api-remediation-fix`](../sdap-bff-api-remediation-fix/) project is starting now. Its five outcomes (size reduction, security hygiene, CI guardrails, codified prevention, and **internal AI hygiene**) touch the same code surface this project would touch — specifically:

- **Outcome E** creates `Services/Ai/PublicContracts/` facade and migrates 20 inbound CRUD→AI direct dependencies through it. Our typed catalogs would naturally consume from there.
- **Outcome E** moves AI-coupled job handlers from `Services/Jobs/Handlers/` to `Services/Ai/Jobs/`. Our catalogs would inject into those.
- The 99 DI registrations (vs ADR-010's ≤15 target) are flagged as **out-of-scope** for BFF remediation — that's exactly the territory our catalog work cleans up.
- The 18 Options classes are **not in BFF remediation scope** — they're our territory.

**Sequencing decision**: BFF remediation completes first. Then this project re-reviews against the post-remediation BFF state. The shape of `Services/Ai/PublicContracts/` and the post-remediation DI organization will tell us where catalogs should live and how they integrate. Some of what we proposed may be absorbed by remediation; some may need adjustment.

When this project resumes:
1. Read post-remediation [`design.md`](../sdap-bff-api-remediation-fix/design.md) and [`current-task.md`](../sdap-bff-api-remediation-fix/current-task.md) to understand the new BFF shape.
2. Re-validate this design against the new shape (1-2 hours).
3. Update Phase 1 specifics based on what changed.
4. Confirm scope and execution plan with owner.
5. Begin Phase 1 (AI Search family).

---

## 6. Critical files (existing — context for design decisions)

### ADRs and constraints

- [`.claude/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md`](../../.claude/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md) — Current platform/customer Bicep split (this project extends, doesn't supersede)
- [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — Auth + Managed Identity patterns; auth resources flow through catalog
- [`.claude/adr/ADR-013-ai-architecture.md`](../../.claude/adr/ADR-013-ai-architecture.md) — Multi-tenancy + BFF Hygiene
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — BFF placement criteria (clean catalogs make extraction decisions easier)
- [`.claude/FAILURE-MODES.md#g-4`](../../.claude/FAILURE-MODES.md) — AI Search field-config immutability gotcha (origin example)

### Existing resource inventories (Phase 0 input)

- [`docs/architecture/auth-AI-azure-resources.md`](../../docs/architecture/auth-AI-azure-resources.md) — 619 lines, AI resources
- [`docs/guides/CONFIGURATION-MATRIX.md`](../../docs/guides/CONFIGURATION-MATRIX.md) — 400+ lines, 50+ settings
- [`docs/architecture/INFRASTRUCTURE-PACKAGING-STRATEGY.md`](../../docs/architecture/INFRASTRUCTURE-PACKAGING-STRATEGY.md) — Customer ownership model

### Current IaC and configuration

- [`infrastructure/bicep/platform.bicep`](../../infrastructure/bicep/platform.bicep) and [`customer.bicep`](../../infrastructure/bicep/customer.bicep)
- [`infrastructure/bicep/modules/`](../../infrastructure/bicep/modules/) — 23 custom modules (AVM candidates)
- [`src/server/api/Sprk.Bff.Api/appsettings.template.json`](../../src/server/api/Sprk.Bff.Api/appsettings.template.json) — Token contract (28 tokens)
- [`src/server/api/Sprk.Bff.Api/appsettings.tokens.md`](../../src/server/api/Sprk.Bff.Api/appsettings.tokens.md) — Token documentation
- [`src/server/api/Sprk.Bff.Api/Configuration/`](../../src/server/api/Sprk.Bff.Api/Configuration/) — 18 Options classes (refactor target)
- [`src/server/api/Sprk.Bff.Api/Services/Ai/IKnowledgeDeploymentService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/IKnowledgeDeploymentService.cs) — Shared/Dedicated/CustomerOwned pattern (expand to all resource families)

### Related projects

- [`projects/sdap-bff-api-remediation-fix/`](../sdap-bff-api-remediation-fix/) — Prerequisite
- [`projects/spaarke-connect-integration-module-r1/`](../spaarke-connect-integration-module-r1/) — Dependent
- [`projects/ai-search-indexing-fix/`](../ai-search-indexing-fix/) — Origin

---

## 7. Verification (when execution resumes)

End-to-end proof points that gate Phase 1 completion:

1. **No string-literal resource names in BFF code** outside the catalog implementation files. Verified by static analyzer / lint rule that fails CI if a known resource name appears outside designated files.
2. **All AI Search consumers use `ISearchCatalog.GetIndexName(SearchIndex.X)`**. Verified by grep + the lint rule.
3. **Deployment of dev environment via Deployment Stack** succeeds; manual portal change to a provisioned resource is denied.
4. **`.bicepparam` validation refuses** to deploy a customer profile that omits any required resource declaration.
5. **Test catalogs** are injected in unit + integration tests. A test that references `SearchIndex.X` for an enum value that doesn't exist fails compilation.
6. **Existing index rename + new SPE container in dev** complete without losing the AI Search write path. Privilege_group_ids field correctly configured as filterable=true on the new index (resolves [FAILURE-MODES G-4](../../.claude/FAILURE-MODES.md)).
7. **App Insights diagnostic logs** continue to surface (preserved from today's regression fix work — Resolved deployment, Batch indexed, Indexed FileName).
8. **Spaarke Connect prerequisites updated** — Connect's design.md points to this project as a foundation.

Acceptance: any agent or engineer can add a new resource family by (a) adding an enum, (b) adding a catalog method, (c) adding a Bicep module + naming function entry, (d) adding a customer-profile parameter, (e) populating App Service settings via deployment. No code change in business logic. No documentation cross-reference updates beyond the catalog files.
