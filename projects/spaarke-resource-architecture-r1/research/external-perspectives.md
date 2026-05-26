# External Perspectives — Resource Architecture Design Review

> **Date**: 2026-05-23
> **Context**: The owner took the research prompt from the project planning document to four different AI advisors. Their verbatim responses are preserved here as input to the design.md.
> **Why preserved**: Diversity of reasoning matters more than any individual recommendation. When this project resumes after BFF remediation, future contributors should be able to trace why specific decisions were made — and challenge them if the world has changed.

## Synthesis (for quick orientation)

| Question | Convergence | Divergence |
|---|---|---|
| Manifest format | All four endorse "Git is canonical, App Config is runtime projection" | DeepSeek/Gemini/ChatGPT: standalone YAML/JSON. Claude: `.bicepparam` files. **Project resolution: Claude's position.** |
| AVM adoption | All four: selectively, not religiously. Wrap AVM resource modules with custom Bicep for Spaarke-specific orchestration | Timeline differs but direction consistent |
| Catalog interface | All four: NOT a single god-catalog. Per-family typed catalogs | Claude pushes hardest on enum-driven accessors (no strings) — **highest leverage change** |
| Customer-specific by default | All four: schema-enforced at provisioning time | Implementation mechanism varies |
| Naming convention | Globally-unique resources encode customer/env; scoped resources don't | Specific prefix patterns differ — `sprk-`, `spaarke-`, both proposed |
| Drift detection | All four: layered (what-if + ARG + custom) | **Claude alone raised Azure Deployment Stacks with denyWriteAndDelete** — the most consequential addition |
| Dataverse scope | Infrastructure refs in catalog YES, entity schema NO | All four agree |
| Anti-patterns | Hardcoded fallbacks, portal drift, big-bang refactor | All four converge |
| Reference architectures | AVM landing zone, Azure Architecture Center multi-tenant, Well-Architected SaaS | All four point at similar sources |

**Most important single insight** (Claude): *"That's not primarily a source-of-truth problem — it's an indirection problem. You leaked the physical name of a resource through every layer of your code, tests, scripts, and templates instead of leaking a logical identifier that gets bound to a physical name once, at one boundary."* This reframing led the project to drop the standalone manifest YAML and rely instead on `.bicepparam` + typed catalogs + Deployment Stacks.

---

## Response 1 — DeepSeek

### Foundational Architecture & Tooling for a Multi-Tenant ISV SaaS

For Spaarke's multi-tenant, multi-deployment model, the goal is to establish a "source of truth" that is fully automated, treating your Bicep templates and manifest as the contract and ensuring the application layer reads from a typed abstraction. Below, I've addressed each of your nine specific questions and provided concrete, forward-looking recommendations based on the current (2025-2026) Azure ecosystem.

#### 1. Azure Verified Modules (AVM) vs. Custom Bicep Modules

AVM provides pre-built, Microsoft-maintained modules that enforce the Well-Architected Framework and are now the recommended starting point for new deployments. For Spaarke, the key benefits are consistency and a shared module strategy across your 23 Bicep templates, which directly helps prevent the drift you're experiencing.

- **When AVM Helps**: AVM is ideal for the core "platform" resources that every customer needs—such as App Services, Key Vaults, and Service Bus namespaces.
- **When AVM Adds Overhead**: When customer-specific naming conventions or shared-vs-dedicated logic (like your AI Search deployment models) become complex. In these cases, it's best to wrap an AVM module with a thin custom Bicep module that handles your specific logic (e.g., `customer-override-parameters.json`), rather than trying to bend the AVM to do everything.

This hybrid approach balances the speed of AVM with the control of custom modules, ensuring your unique business logic remains manageable.

#### 2. Single Source of Truth: The Hybrid Manifest Model

A static manifest alone becomes stale; a dynamic service alone lacks the declarative control needed for infrastructure. The recommended approach is a hybrid model:

1. **Canonical Manifest in Source Control**: A YAML/JSON file (e.g., `spaarke-resource-manifest.json`) that declaratively defines *all* resources. It serves as your documentation, drift-detection source, and onboarding template.
2. **Azure App Configuration for Runtime**: Deployment scripts (in CI/CD) use this manifest to populate an App Configuration store with correct, labeled key-values for each customer and environment. The application (BFF) then loads its configuration from this service using a typed provider at startup.

The Microsoft-published multi-tenant SaaS architectures strongly recommend leveraging Azure App Configuration's label-based multi-tenancy for exactly this pattern. App Configuration's built-in stacking capability (loading base values, then overriding with a `Customer-{Id}` label) enables a clean separation between the manifest and runtime behavior.

#### 3. Customer-Override Mechanism

Your goal of making every resource "customer-specific by default" aligns perfectly with a layered approach to configuration:

- **Standardized Default**: `spaarke-resource-manifest.json` in source control.
- **Customer Override**: A separate `customer-overrides.yaml` file per customer in source control, which is loaded and merged during their dedicated deployment pipeline.

This model supports your three AI Search models beautifully:
- A "Shared" customer has no search-service override.
- A "Dedicated" customer overrides the search service name.
- A "BYOK" customer overrides the entire connection string.

This merge must happen during the Bicep deployment (using `parameters` and `overrides`), which then populates the customer-specific label in Azure App Configuration. The separation is clear: resource provisioning and overrides are declared at provisioning time, while feature flags and non-infrastructure settings can be runtime-mutable via the App Configuration store.

#### 4. Code Consumption: Beyond `IOptions<T>` with `IResourceCatalog`

The `IResourceCatalog` abstraction is an improvement, but as the number of resources grows, it can become a "god interface." A better pattern for .NET 8 is an **extended, typed `IOptions<T>`**:

- **Structured Options Classes**: Continue to define well-structured `IOptions<T>` classes, but let all property values be populated by the Azure App Configuration provider at startup. This keeps business logic clean and injectable.
- **Avoid String Literals via Constants**: Inject strongly typed "catalog" interfaces (like `IIndexCatalog` and `IQueueCatalog`) that internally derive their values from structured `IOptions<T>` objects. This provides type safety and clear separation of concerns while preventing the `IResourceCatalog` from becoming a bottleneck.

This approach ensures your code remains testable and modular, without needing to know where the values originated.

#### 5. CAF-Based Naming Conventions

The Cloud Adoption Framework (CAF) provides the backbone for resource naming by recommending a consistent, standardized format that ensures global uniqueness. For Spaarke, the `{nouns-plural}-index` pattern is logical but needs CAF-compliant structure to avoid collisions:

**Recommended Pattern**:
`spaarke-<customerId>-<nouns-plural>-<resource-type>-<environment>`
- Example: `spaarke-cust123-files-index-prod`
- For shared resources: `spaarke-shared-monitor-workspace-prod`

This pattern communicates ownership (customer), purpose (index type), resource category, and environment at a glance. For resources that must be globally unique (like storage accounts), the `customerId` and a region token provide the necessary uniqueness.

#### 6. Drift Detection in the Azure Ecosystem

Drift detection for Bicep remains an area of active improvement. The `what-if` operation is a good first line of defense but has known limitations with nested deployments and noisy APIs.

- **Primary Mechanism**: Run a **dedicated pipeline** that uses the `AzureDriftDetector` tool (which leverages the `what-if` API) and custom scripts to compare your desired-state manifest against the current Azure state.
- **Secondary Guard**: Use **Azure Resource Graph queries** to periodically audit live resources against your naming convention and required tags.
- **Long-Term Investment**: **Azure Deployment Stacks** are the future. They provide explicit lifecycle management, including the detection and blocking of un-managed resources, which is far more robust than a point-in-time comparison.

Your `customer-overrides.yaml` files serve as the "desired state" for each customer, making drift detection a straightforward comparison of `(manifest + overrides)` vs. `live Azure`.

#### 7. M365 & Dataverse: A Separate but Consistent Approach

You should treat Dataverse entities as "resources," but through a separate catalog mechanism. The approach is fundamentally consistent, but the tooling is different.

- **For Dataverse**: You can use the Dataverse SDK to build a typed catalog of logical names. These can be stored in the same `spaarke-resource-manifest.json` but consumed by a dedicated `IDataverseEntityCatalog` in the BFF, which loads them from a special key in Azure App Configuration or a `dataverse-entities.json` file embedded in the app.
- **For M365/Graph**: Apply the same principle: a dedicated `IGraphResourceCatalog` that abstracts user/group object IDs for your internal playbooks.

This ensures consistent, single-source-of-truth governance without forcing disparate technologies into an inappropriate abstraction.

#### 8. Anti-Patterns & Pitfalls to Avoid

Based on patterns observed in the field, the following are critical to avoid when retroactively unifying a scattered resource architecture:

- **Centralized "One Size Fits All" Over-Engineering**: The most common anti-pattern is placing everything in a single subscription or resource group, which creates a blast radius and makes customer-specific isolation impossible.
- **The "Quick Fix" Portal Drift**: The primary source of drift is manual changes in the Azure Portal (e.g., someone re-generates an access key). This must be culturally eliminated, with tooling (like `AzureDriftDetector`) acting as the safety net.
- **Over-Relying on `what-if`**: Treat `what-if` as a helpful preview, not a drift guarantee. Its known limitations mean you must also use `Resource Graph` queries and dedicated drift detection tooling.
- **Hardcoding "Default" Overrides**: Avoid baking a single shared default into your Bicep modules. Every resource should be parameterized, with the "customer must supply" mantra enforced by your `customer-overrides.yaml` merge logic.

#### 9. Reference Architectures & Open-Source Examples

**1. The Gold Standard: Azure SaaS Development Kit (ASDK)**
This is the official Microsoft reference architecture for exactly your scenario. It includes a full multi-tenant implementation with tenant lifecycle management, CI/CD, and a configuration management pattern you can study. It demonstrates best practices for onboarding, permissions, and a central control plane.

**2. Design Methodology for SaaS Workloads**
This Well-Architected Framework guide helps you align your business requirements (like flexible deployment models) with your technical architecture, ensuring you make the right trade-off decisions from the start.

**3. Architectural Approaches for Multitenant Solutions**
A comprehensive guide from the Architecture Center that details deployment stamp patterns, tenant isolation models, and resource organization strategies. This is your blueprint for scaling beyond a few customers.

**4. Automated Deployment with Dataverse & Power Platform Pipelines**
A crucial reference for bringing Dataverse under the same IaC umbrella. It demonstrates a CI/CD pipeline with Git integration, showing you how to manage Dataverse solution components (like your PCF controls) declaratively.

#### Recommended Phased Implementation

To get started, I recommend this phased approach:

1. **Define the Manifest**: Create a comprehensive, single `spaarke-resource-manifest.json` for your *platform* as a first step.
2. **Build the CI/CD Pipeline**: Create a pipeline that reads the manifest, generates `customer-overrides.yaml` stubs, and executes Bicep `what-if` and `deploy` operations.
3. **Populate App Configuration**: As a post-deployment step, push the final, merged configuration for each customer into an App Configuration store with the appropriate labels.
4. **Refactor the BFF**: In the BFF, replace hardcoded strings with the typed `IOptions<T>` pattern, where values are bound at startup from the App Configuration provider.

This progressive rollout allows you to immediately see benefits without requiring a full-scale rewrite of your existing 28 environment-specific tokens.

---

## Response 2 — ChatGPT

Your attached brief is strong and correctly frames the real issue: this is not just "configuration cleanup." It is a foundational resource contract problem across Azure, Dataverse, Graph, M365, CI/CD, tests, and Spaarke-internal abstractions. The AI Search index appearing in 184 places across 67 files is the symptom; the root problem is absence of a typed resource catalog and provisioning manifest.

My guidance: proceed, but narrow the implementation into a disciplined architecture rather than a giant "catalog everything" refactor.

### Recommended direction

Use a hybrid model:

- Git manifest is canonical for declared resources.
- Bicep/AVM deploys Azure infrastructure from that manifest.
- Deployment outputs project into Azure App Configuration for runtime consumption.
- Key Vault remains the secret authority.
- Dataverse remains the product/application metadata authority.
- Code consumes resource names through typed resource clients/catalogs, not raw strings.

This aligns with Microsoft's current Azure multitenancy guidance, which treats SaaS multitenancy as an architectural choice across resource isolation, deployment, configuration, governance, identity, data, and operations, not merely a hosting pattern.

### Key decisions I would make

1. **Use AVM selectively, not religiously.**
   AVM is appropriate for standard Azure resources where Microsoft's module abstraction adds consistency: App Service, Key Vault, Storage, Service Bus, AI Search, App Configuration, managed identities, diagnostics, monitoring. Keep custom Bicep modules where Spaarke has domain-specific composition logic. AVM's purpose is to standardize high-quality IaC modules; it is not a replacement for your product deployment model.

2. **Do not replace `IOptions<T>` everywhere.**
   Keep `IOptions<T>` for behavioral settings: limits, flags, timeouts, thresholds, feature behavior. Add a resource catalog for identifiers: index names, queue names, endpoint URIs, deployment names, container types, secret names, app registrations, Dataverse environment IDs, and playbook IDs.

3. **Avoid one giant `IResourceCatalog` as the primary developer surface.**
   Use a central catalog internally, but expose typed family accessors:
   - `SearchResources`
   - `QueueResources`
   - `AiModelResources`
   - `DataverseResources`
   - `GraphResources`
   - `SpeResources`
   - `PlaybookResources`

   A generic `catalog.GetIndexName("documents")` will eventually become stringly typed again. Prefer `catalog.Search.DocumentsIndexName` or `SearchResourceCatalog.Documents.IndexName`.

4. **Treat Dataverse logical names as product schema constants, not deployed infrastructure resources.**
   They should be in scope, but in a different catalog partition. Azure resources are provisioned resources. Dataverse tables, columns, choices, Custom APIs, security roles, and solution components are application schema resources. Playbooks, scope IDs, and capability IDs are product semantic resources. Keep them unified under the same governance model, but do not force them into the same shape.

5. **Use App Configuration as runtime projection, not the source of truth.**
   Azure App Configuration is well suited for centralized runtime configuration, labels, refresh, feature flags, and Key Vault references. Microsoft's guidance explicitly supports App Configuration with Key Vault references so secrets can remain in Key Vault while applications consume references through configuration.
   But App Configuration should not become the authoritative declaration of what Spaarke is supposed to deploy.

### Proposed architecture

I would define three layers:

1. **Resource Manifest** — This is committed to source control and versioned. It declares Spaarke's expected resource families, default naming rules, isolation model, required/optional status, allowed sharing model, and environment/customer override behavior.
2. **Customer Provisioning Manifest** — Generated per customer/environment from the platform manifest plus approved overrides. This becomes the deployment input and the audit artifact.
3. **Runtime Resource Catalog** — Generated from deployment outputs and published to App Configuration. The BFF loads this at startup into typed validated options/catalog classes.

This gives you traceability:
- What Spaarke expects
- What a customer/environment declares
- What Bicep deployed
- What the app consumed
- What Azure actually contains

### Naming convention

Your proposed `spaarke-{nouns-plural}-index` is readable but insufficient for multi-customer and multi-environment deployments.

Use a structured pattern:

`sprk-{workload}-{capability}-{resourcePurpose}-{customerCode}-{env}-{regionCode}`

Examples:
- `sprk-sdap-search-files-acme-prod-eus`
- `sprk-sdap-search-records-acme-prod-eus`
- `sprk-ai-openai-main-acme-prod-eus`
- `sprk-core-sb-jobs-acme-prod-eus`

For Azure AI Search indexes specifically, the service name may need customer/env uniqueness, while index names can remain purpose-based inside the service:
- Search service: `sprk-sdap-search-acme-prod-eus`
- Index: `files`
- Index: `records`
- Index: `references`

That is cleaner than encoding everything into every index name.

### Drift detection

Use multiple controls, not one tool:

- Bicep `what-if` for pre-deployment change preview.
- Azure Resource Graph for inventory and reconciliation across many customer deployments.
- Azure Policy for enforceable guardrails and audit/deny controls.
- Custom manifest reconciler for Spaarke-specific checks.
- App Configuration/runtime catalog validation at startup.

There is no perfect native Bicep equivalent to Terraform state-based drift detection; Bicep/ARM is declarative and does not maintain state the same way Terraform does. Resource Graph plus `what-if` plus custom reconciliation is the practical Azure-native answer.

### Main pitfall to avoid

Do not start by trying to eliminate every string literal everywhere. That becomes too large and risks creating an over-abstracted platform.

Start with the highest-risk resource families:

1. AI Search services and indexes
2. Azure OpenAI deployments/models
3. Service Bus queues/topics
4. Key Vault secret names
5. Managed identities and app registrations
6. Dataverse environment URLs and solution identifiers
7. Playbook IDs, scope IDs, capability IDs

Then add analyzers/tests to prevent regression.

### Bottom line

The right answer is not "more appsettings" and not "just App Configuration." The right architecture is a Spaarke Resource Catalog system:

- Git-declared
- Bicep/AVM-deployed
- App Configuration-projected
- Key Vault-secured
- Dataverse-aware
- Typed in .NET
- Reconciled against Azure/M365/Dataverse live state

I would turn this into a formal ADR and a phased implementation spec.

---

## Response 3 — Gemini

This problem statement describes a classic inflection point for enterprise multi-tenant ISVs on Azure. The configuration sprawl and multi-layered dependency drift you are experiencing across Azure, M365, and Dataverse is a common side effect of successfully scaling past the initial MVP phase.

Below is an architecture blueprint designed around Microsoft's modern standard patterns (2025–2026) to unify your resource topology into a single declarative catalog.

### 1. The Infrastructure Foundation: Azure Verified Modules (AVM)

**Use AVM for Leaves; Custom Modules for Orchestration**

Azure Verified Modules (AVM) reached full production maturity and is the absolute recommended standard for Azure Infrastructure as Code (IaC). However, you should not replace your entire framework with raw AVM.

- **The Right Approach:** Use AVM **Resource Modules** as the low-level blocks for individual resources (e.g., Azure AI Search, Cosmos DB, Key Vault). Keep your custom Bicep modules (`platform.bicep` and `customer.bicep`) as the high-level orchestrators that piece these blocks together.
- **Where AVM Helps:** AVM inherently handles complex, security-critical configurations that enterprise customers demand during procurement: Private Endpoints, Diagnostic Settings to Log Analytics, Customer-Managed Keys (CMK) orchestration, and built-in Azure Policy validation.
- **Where It Adds Overhead:** AVM modules are highly generic and parameter-dense. If you expose raw AVM parameters directly to your customer onboarding pipeline, you will create parameter bloating.

> **Architectural Verdict:** Encapsulate AVM modules inside your own `customer.bicep` wrapper. Hardcode Spaarke's strict security invariants inside your wrapper, and only expose highly relevant functional inputs (e.g., SKU size, bounciness, tenant ID) to your provisioning pipeline.

### 2. The Single Source of Truth: Hybrid Manifest Architecture

For an ISV managing a split of shared platform infrastructure and highly customized customer tenants, **Option C (The Hybrid Model)** is the cleanest operational choice.

```
+------------------------------------+
|       Git Source Control           |
|  (Canonical State / GitOps Schema) |
+------------------------------------+
                  |
                  |  [ Azure DevOps / GitHub Actions ]
                  v
+------------------------------------+
|      Bicep / M365 Provisioning     |
+------------------------------------+
                  |
                  +--------------------------------+
                  | (Infra State)                  | (Runtime State)
                  v                                v
+------------------------------------+   +------------------------------------+
|      Deployed Azure Resources      |   |      Azure App Configuration       |
|   (rg-spaarke-{customer}-{env})    |   |  (Hierarchical Labels per Tenant)  |
+------------------------------------+   +------------------------------------+
                                                           |
                                                           | (Startup / Key Vault Ref)
                                                           v
                                         +------------------------------------+
                                         |         Spaarke .NET BFF          |
                                         +------------------------------------+
```

**Why Hybrid Beats the Alternatives**

- **Static Manifest in Git alone (Option A):** Fails because code cannot easily query Git at runtime to resolve dynamic connections, especially across tenant scopes.
- **App Configuration alone (Option B):** Fails because it lacks schema validation, pull-request guardrails, and version-controlled rollbacks.

**The Mechanics of the Hybrid Model**

1. **The Canonical State:** A strongly typed JSON schema file (`topology.json` or `manifest.json`) lives in your Git repository. It lists every resource Spaarke relies on across infrastructure, identity, and internal logic.
2. **The Deployment Pipeline:** When CI/CD fires, Bicep parses this manifest to provision or update infrastructure.
3. **The Runtime Projection:** The final task in your deployment pipeline writes the output values (fully qualified domain names, index names, resource IDs) into **Azure App Configuration** using tenant-specific labels (e.g., `TenantId:customerA`).

### 3. Customer-Override Mechanism & Mutation Lifecycles

To clearly segregate configurations, you must map your settings against their natural mutation lifecycles. For an ISV, configurations live across two distinct phases:

**Phase 1: Provisioning-Time Configuration (Immutable at Runtime)**

This defines structural topology: structural names, SKUs, regions, and identity bounds.

- **Implementation:** Store this in a Git-tracked `tenants/{customerId}.json` file.
- **The Blueprint Fallback Rule:** To enforce your goal where shared resources must be explicitly declared, use a strict JSON schema. If `aiSearchModel` is omitted or set to null, the validation engine flags it as an error rather than silently defaulting to a shared cluster.

**Phase 2: Runtime-Mutable Configuration (Dynamic Settings)**

This defines operational behavior: AI prompts, system flags, playbook configurations, and features.

- **Implementation:** Store this in **Azure App Configuration** using hierarchical paths combined with App Config labels.

```
Key:   spaarke:ai:index-name
Value: spaarke-records-index
Label: customerA-prod
```

Your .NET Core application configures the Azure App Configuration provider to query keys using the incoming tenant context at startup or via dynamic refresh:

```csharp
config.AddAzureAppConfiguration(options =>
{
    options.Connect(endpoint)
           .Select("spaarke:*", labelFilter: $"{currentTenant}-{currentEnv}");
});
```

### 4. Code Consumption: Clean Architecture for Resource Discovery

Injecting a blanket `IOptions<T>` creates tightly coupled dependencies and strips away compile-time safety across dynamic tenant contexts. However, a single monolithic `IResourceCatalog` will eventually turn into an unmaintainable interface.

**The Hybrid Solution: Typed Catalogs with Functional Sub-Contexts**

Create a clean, unified `IResourceCatalog` facade that exposes highly focused, dedicated sub-catalogs. This approach provides a singular entry point for discovery while honoring interface segregation principles.

```csharp
public interface IResourceCatalog
{
    IIndexCatalog Indexes { get; }
    IQueueCatalog Queues { get; }
    IIdentityCatalog Identity { get; }
    IInternalCatalog Internals { get; }
}

public interface IIndexCatalog
{
    // String literals are completely eradicated from business logic
    string GetIndexName(DocType docType);
}

public enum DocType { Files, Records, References }
```

**The Multi-Tenant Resolution Engine**

Behind this interface, the implementation relies on a custom `TenantContext` scoped provider to dynamically resolve resource tokens based on the active execution context:

```csharp
public class AppConfigResourceCatalog : IResourceCatalog
{
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IConfiguration _configuration;

    public AppConfigResourceCatalog(ITenantContextAccessor tenantContext, IConfiguration configuration)
    {
        _tenantContext = tenantContext;
        _configuration = configuration;
    }

    public IIndexCatalog Indexes => new AppConfigIndexCatalog(_tenantContext.CurrentTenantId, _configuration);
}
```

### 5. Global Naming Strategy for Multi-Tenant Isolation

To prevent naming collisions across identical shared platforms, while ensuring strict compliance with the Cloud Adoption Framework (CAF), adopt a **Tokenized Deterministic Naming** pattern.

**The Structural Layout**

`spaarke-{env}-{tenantIdentifier}-{resourceAbbreviation}-{qualifier}`

**Applying the Pattern safely within Azure and Core Constraints**

- **Shared AI Search Cluster:** `spaarke-prod-global-srch`
- **Customer Dedicated Cluster:** `spaarke-prod-cust42-srch`
- **AI Search Indexes:** `spaarke-files-idx` and `spaarke-records-idx`

> **Critical Design Constraint:** Azure AI Search index names **cannot** contain dots or uppercase characters, and must start with a letter. Keep tenant identifiers out of the index names themselves if they are already isolated inside a tenant-dedicated resource group or cluster. This keeps your runtime code clean, uniform, and agnostic across index instances.

### 6. Drift Detection Framework for Multi-Tenant Scale

When managing infrastructure footprints across dozens or hundreds of tenant environments, running traditional Bicep `what-if` calculations directly in individual deployment loops introduces severe performance bottlenecks.

**Recommended Tooling Stack for Scaled ISVs**

| Tooling Option | Best Suited For | Implementation Strategy |
| --- | --- | --- |
| **Azure Resource Graph (ARG)** | Scaled Infrastructure Audits | **High Recommendation.** Execute cross-tenant, cross-resource group KQL queries inside a central automation runbook to check if actual resource tags match your Git manifest declarations. |
| **Bicep `what-if`** | CI/CD Pre-Deployment Verification | Run this specifically inside your pull-request validation pipelines for target tenants before merging changes down to main branches. |
| **Azure Policy (Deny/Audit)** | Compliance & Perimeter Enforcement | Deploy enterprise-level policies across customer subscriptions to block manual configurations outside your automated Service Principal execution boundaries. |

**Designing a Custom Manifest Reconciler**

For complex architectures, write a lightweight .NET integration tool executed via a GitHub Action or Azure DevOps Pipeline. This script fetches live resource details via the Azure SDK and verifies them directly against your `manifest.json` schema layout.

```csharp
// Conceptual verification tool logic
var activeCluster = azure.SearchServices.GetById(manifest.SearchResourceId);
foreach(var expectedIndex in manifest.ExpectedIndexes)
{
    if (!activeCluster.Indexes.Exists(expectedIndex))
        throw new DriftDetectedException($"Missing index: {expectedIndex}");
}
```

### 7. Unifying the M365, Dataverse, and Graph Ecosystems

Dataverse and M365 references **must** be managed as first-class citizens alongside your core Azure resource blueprints. Treat logical tables, column metadata, and service principal bindings as infrastructure dependencies.

**Leveraging Dataverse Environment Variables**

Do not attempt to map or inject raw Dataverse schema tables or entity properties directly inside your Azure App Configuration layers. Instead, rely on Dataverse **Environment Variables** as your primary abstraction bridge.

```
+---------------------------+       System Sync       +----------------------------+
|  Azure App Configuration  | =====================>  | Dataverse Env Variables    |
| (Manages Azure Footprint) |                         | (Manages Power Apps State) |
+---------------------------+                         +----------------------------+
```

**The Unified Blueprint Architecture**

1. Define all required system fields, schema dependencies, and remote target boundaries inside your canonical `manifest.json`.
2. During deployment, utilize Power Platform DevOps Tools or custom CLI actions to pack your structural data definitions into a standard **Dataverse Solution**.
3. Inject tenant-specific endpoints directly into Dataverse Environment Variables (with type definitions set to `Secret` or `JSON`) during solution installation loops. This completely isolates your React PCF components and embedded application contexts from hardcoded string variables.

### 8. Structural Anti-Patterns & Common Pitfalls

When retrofitting unified configurations into production SaaS platforms, beware of these structural architectural traps:

- **The Cache Invalidation Trap:** Fetching configuration strings inside long-lived Singleton objects creates silent runtime drift when changing tenant scopes. Ensure your custom `IResourceCatalog` resolves mappings per request context via `Scoped` dependency lifecycles.
- **The Secret Leakage Anti-Pattern:** Never inject sensitive access tokens, certificates, or primary keys directly into your App Configuration tables. App Configuration must exclusively use **Key Vault References** (`@Microsoft.KeyVault(...)`), pulling values securely from tenant-specific vaults at runtime.
- **The Big Bang Migration Mistake:** Rewriting all 13 structural layers simultaneously can destabilize production environments. Migrate your infrastructure sequentially: decouple complex systems like Azure AI Search first, thoroughly test your typed discovery facades, and then scale the framework across secondary queues, caches, and storage.

### 9. Industry Reference Architectures

To review end-to-end multi-tenant implementation strategies on Azure, explore these authoritative production playbooks:

- **Azure Well-Architected Framework for SaaS Workloads:** Review the Microsoft cloud guidance specifically tailored for ISV multi-tenant platform design patterns.
- **SmartHotel360 / RockPaperAzures Reference Apps:** Browse the official Azure Architecture Center patterns detailing Multi-Tenant SaaS systems built around unified configuration and isolated data tiers.
- **Azure-Samples / multitenant-saas-guidance:** Examine GitHub repositories showing template automation for multi-region tenant deployment strategies on Azure.

---

## Response 4 — Claude

I'll dig into this. Before answering, let me check current state on a few specifics — AVM status, recent Microsoft ISV guidance, and any new patterns in this space that may have shifted since the cutoff.

Thanks for the detailed write-up. A few things upfront, then I'll work through your questions in roughly the order you asked but regrouped where it helps.

### The diagnosis I'd offer first

The "184 occurrences in 67 files" finding is real and worth fixing, but I want to push gently on the framing. That's not primarily a *source-of-truth* problem — it's an *indirection* problem. You leaked the **physical name** of a resource through every layer of your code, tests, scripts, and templates instead of leaking a **logical identifier** that gets bound to a physical name once, at one boundary. The fix is not a meta-registry; the fix is a small, boring naming function plus a typed catalog at the seams.

The reason I want to flag this is that I've seen teams respond to a "180 places" finding by building elaborate runtime registries with manifests, sync jobs, App Config replication, Cosmos-backed lookups, and a year later they have a different kind of debt that's harder to refactor than 184 grep hits would have been. So before you build the cathedral, decide whether you actually need it or whether you need a typed accessor and a `customer.parameters.bicepparam` file.

That said — your goal #3 (customer-specific by default, opt-in to shared) and goal #5 (drift detection) are good reasons to invest in some structure beyond "just refactor." Let me work through the pieces.

### On the Bicep foundation (your Q1)

In early 2026 the answer here genuinely shifted: Azure Verified Modules for Platform Landing Zone went GA, and the classic ALZ-Bicep repo will be archived on February 16, 2026 with limited support for 12 months after. Non-AVM modules are being retired from the public Bicep module registry. So even if you don't use the Landing Zone accelerator, the *direction* is clear: AVM resource modules are the default building block going forward.

For an ISV with 23 custom modules, I would not rip-and-replace. The pragmatic path is:

- New resources: use AVM resource modules (`br/public:avm/res/...`) directly. They encode the CAF-aligned defaults you'd otherwise have to write.
- Existing modules: keep them, but as you touch each, evaluate the AVM equivalent. The AVM resource modules are deliberately narrower than ALZ pattern modules, so the swap is usually mechanical.
- Your `platform.bicep` / `customer.bicep` split becomes the equivalent of an AVM **pattern module** — that's the right shape; AVM patterns are exactly "compose resource modules into a deployable unit." You can publish your own private pattern modules to an ACR-backed registry.

The bigger upgrade I'd push you on is **Deployment Stacks**. Deployment Stacks bring native state management to Bicep: when you remove a resource from your Bicep file, the stack automatically deletes or detaches it; `--deny-settings-mode denyWriteAndDelete` prevents anyone — even admins — from making manual changes outside Bicep. For an ISV deploying into customer subscriptions, the deny-settings story alone is worth the migration: it eliminates an entire class of "customer ops touched the resource and now things are weird" tickets, and it gives you drift protection at the platform layer rather than something you have to build.

### Source of truth: hybrid, but be precise about what each layer holds (your Q2 and Q3)

The right shape, in my view, is your option (c) — but I'd be more specific about the split:

**Git is the contract.** A `customer.manifest.yaml` per customer (or `customer.parameters.bicepparam`, which I lean toward for type safety) declares *intent*: "Customer Foo, environment prod, gets a dedicated AI Search instance, uses the shared OpenAI deployment, owns its own Key Vault." This is reviewable, diffable, and the single thing humans edit. It's not the runtime; it's the input to deployment.

**Bicep + AVM is the binder.** Deployment translates the manifest into physical Azure resources with names generated by a single naming function. That function takes `(customer, env, kind, purpose)` and emits a name — it lives in *one* Bicep file (or one small library), and every resource name in your templates flows through it. The "184 occurrences" problem can never recur because no other code is allowed to compute a resource name.

**Azure App Configuration is the runtime projection.** After deployment, a step in your pipeline writes the *resolved* names into App Config under a label like `customer:foo/env:prod`. The BFF reads its configuration with that label filter via the App Configuration provider. The provider caches and supports sentinel-key refresh, so the runtime cost is negligible.

Microsoft's multitenant guidance recommends key prefixes for managing tenant-specific settings and reserving labels for other purposes — because if you use labels for tenant IDs, you can't use labels for anything else. I'd take that advice if you also want to vary by environment within the same store. In your case you already have per-customer resource groups; you might be fine using labels as `customer/env` and leaving prefixes for resource families (e.g., `search:`, `openai:`, `keyvault:`).

What I would **not** do: store this config in Dataverse. That creates a bootstrap loop — you have to know how to reach Dataverse before you can read the config that tells you which Dataverse. Keep the catalog above Dataverse in the dependency graph.

**Default-deny is enforced in the manifest schema.** Make customer manifests fail validation if a required resource binding is missing and no `mode: shared` opt-in is present. This is your goal #3, and it's a schema problem, not an architecture problem — a JSON Schema or `@validate()` decorator (the new Bicep authoring-time validation) covers it.

### Code consumption: not a god-catalog, families instead (your Q4)

I'd push back on a single `IResourceCatalog`. That interface tends to grow into a 30-method monster that everything depends on, and it conflates two different concerns: *resource identity* ("what is the documents index named in this deployment?") and *behavior configuration* ("what's the rerank threshold?"). Keep those separate.

What I'd actually do:

- Keep `IOptions<T>` for behavior — embedding model name, batch sizes, thresholds, timeouts. This is what it's good at.
- Add narrow, per-family catalog interfaces for resource identity: `ISearchCatalog.GetIndex(SearchIndex.Documents)`, `IQueueCatalog.GetQueue(WorkQueue.ConnectJobs)`, `ISecretCatalog.GetSecret(WellKnownSecret.AISearchKey)`.
- The argument to each method is an **enum or sealed record**, not a string. `GetIndex("documents")` accepts typos; `GetIndex(SearchIndex.Documents)` does not. This is the single highest-leverage change you can make, because it gives the compiler a chance to catch every drift between code and manifest.
- The implementations all read from the same underlying source (App Config–backed `IConfiguration` with the customer/env label applied). You're not multiplying infrastructure; you're partitioning *interfaces* so each consumer depends on what it actually uses.

You'll write maybe 5-7 of these family catalogs to cover your 13 resource categories. That's not a lot of code, and it survives refactors much better than a single big-bag catalog.

### Naming convention (your Q5)

For globally-unique resources (storage accounts, search services, hostnames) you must encode the customer in the name. For nested names (indexes inside a search service, secrets inside a key vault, queues inside a service bus) you should **not** — the parent already scopes them, and repeating "customer-env" in nested names just makes them ugly without adding uniqueness.

A pattern that works:

- Globally unique: `sprk-{customer}-{env}-{purpose}-{kind}` (e.g., `sprk-acme-prod-search`, `sprk-acme-prod-kv`). Use the short prefix `sprk` consistently with your Dataverse `sprk_` publisher prefix; it ties everything together visually.
- Scoped within a parent: just `{purpose}` (e.g., index named `files`, `records`, `references` inside the search service). Drop the plural-vs-singular pendulum and pick one rule — I'd suggest singular for index/queue/container names because they describe *the resource*, not its contents.
- Resource group: `rg-sprk-{customer}-{env}` matches your current convention; keep it.

Two practical notes: Azure has hard length limits on storage accounts (24 chars, no dashes) and key vault names (24 chars, dashes ok); your naming function needs `customer` slug rules that handle truncation deterministically. And: bake the slug rules into one place. Don't let the slug get computed three different ways for three different resources.

### Drift detection (your Q6)

The ecosystem reality in 2026: Drift detection in Azure currently relies on the what-if operator; deployment stack-based drift detection is being transitioned to once it becomes generally available. So the pragmatic stack today is:

- **Bicep what-if** in CI on every PR — catches "your template would change live state."
- **Deployment Stacks with denyWriteAndDelete** — prevents drift from happening in the first place. This is the strongest tool you have right now.
- **Azure Resource Graph query** as a nightly job — answer the question "do the resources we expect for customer X actually exist, in the right region, with the right tags?" Resource Graph is fast and cheap and ideal for cross-subscription, cross-customer fleet views.
- **A small custom validator** that reads your manifest and your code's catalog declarations, and checks they agree. This is the analog to "what does code expect vs. what does manifest declare" that you mentioned. It's worth ~200 lines of C#; it pays for itself the first time a PR adds a new index in code but forgets the manifest.

Azure Policy is for *governance* (you must have tag X, you cannot deploy outside region Y). It's not great for "the resource exists with the right name." Don't try to make it do that.

### On including M365 / Dataverse / Graph (your Q7)

I'd include them in the catalog, but at the **infrastructure** level, not the **schema** level. Two different things:

- *Infrastructure*: Dataverse environment URL, SPE container type ID, AAD app registration IDs, managed identity client IDs, Graph endpoint. These belong in your catalog — they're resources you provision, and they have per-customer identity. Treat them like Azure resources.
- *Schema*: Dataverse entity logical names (`sprk_matter`, `sprk_document`), choice option values, relationship names. These do **not** belong in your catalog — they live in Dataverse solutions, are versioned with the solution, and trying to mirror them in a manifest creates a synchronization problem where the manifest will lie. Code that touches Dataverse entities should use generated proxies from the solution (e.g., early-bound types via `pac modelbuilder` or its successor) or constants generated from solution metadata, not catalog lookups.

The line is: if the value differs per customer deployment, it's catalog. If it's the same across all deployments because it's part of the product's schema, it's a constant generated from solution metadata.

### Pitfalls (your Q8)

A few I've seen kill projects like this:

1. **The catalog becomes runtime-mutable when it shouldn't be.** Resource names are deploy-time facts. If you let admins edit them in a UI without a redeploy, you've signed up to handle the case where a resource is referenced by old name and new name simultaneously. Make the catalog read-only at runtime; if you need to rename, redeploy.

2. **Stringly-typed accessors creep back in.** Someone adds a `catalog.GetByKey(string)` "escape hatch" for that one case, and within a year you're back to string literals. Don't add the escape hatch. If a new resource appears, add it to the enum.

3. **The manifest becomes a meta-meta-manifest.** Resist the urge to make the manifest format itself customizable. One schema, evolved with normal versioning.

4. **You bind too early to a "Customer" abstraction in Dataverse.** Dataverse-as-customer-registry is appealing until you realize that the bootstrap path (cold start, disaster recovery, onboarding a brand-new customer) has to work *before* Dataverse is reachable. Keep the customer registry at a layer that doesn't depend on per-customer resources — App Configuration at the platform level, or a Cosmos DB in `rg-spaarke-platform-prod`.

5. **Tests don't get the same catalog.** This is subtle: if tests use `AnalysisOptions` with hardcoded values, your "code doesn't have string literals" claim is half-true. Make tests use a `TestSearchCatalog` that the test framework injects; if a test references a resource that isn't in the enum, it shouldn't compile.

6. **Treating the refactor as a big-bang.** This work has natural seams. Do one resource family at a time, end-to-end (manifest → AVM module → App Config → catalog interface → consumers → tests). You'll learn things in the first family that change how you do the second.

### References (your Q9)

Honestly, there's no single ISV reference that does all of this end-to-end and is publicly available. The pieces I'd point you at:

- The **AVM repo** (`github.com/Azure/Azure-Verified-Modules`) and the AVM portal at `aka.ms/AVM` — the Bicep Landing Zone starter module is the closest thing to a Microsoft-blessed pattern for composing AVM resource modules into a customer deployment.
- The **Azure Architecture Center multitenant guidance**, specifically the "Architectural considerations for App Configuration in a multitenant solution" page — it's where the prefix-vs-label advice lives.
- For the App Service side, the **App Service Landing Zone Accelerator**.
- For Dataverse + ISV patterns specifically, there's not a great public reference; the Power Platform Solution Architect's Handbook covers some ground but doesn't get into IaC-meets-Dataverse the way you'd want.

### If I were prioritizing your next quarter

I'd sequence it: typed per-family catalogs first (highest leverage, lowest risk, immediate cleanup of the 184-occurrences problem); naming function consolidation second; App Configuration as the runtime projection third; Deployment Stacks migration fourth; AVM module migration opportunistically; drift detection last because the first four reduce drift to the point where detection is mostly a safety net rather than a daily firefight.

Happy to go deeper on any of these — the catalog interface design and the App Config / manifest split are the two where the devil is most in the details.

---

## How these responses shaped the design

Tracing each major design decision in `../design.md` back to the source(s):

| Design decision | Driven by |
|---|---|
| Drop the standalone YAML manifest in favor of `.bicepparam` + typed catalogs + generated solution constants | Claude's "indirection not source-of-truth" reframing |
| Per-family typed catalogs (NOT god-catalog), enum-driven accessors | All four — strongest in Claude |
| AVM selectively, opportunistic migration, custom modules as pattern composition | All four — Gemini and Claude most specific on the GA timeline |
| Azure Deployment Stacks with denyWriteAndDelete as the prevention layer | Claude (only respondent to raise this — most consequential addition) |
| App Configuration as runtime projection, deferred to Phase 5 | Claude's sequencing; DeepSeek/Gemini/ChatGPT supported the concept but had it earlier in their proposals |
| Customer-specific by default via `@validate()` Bicep decorators | All four; mechanism specifics from Claude |
| Globally-unique vs. scoped naming distinction | Claude and Gemini explicit; ChatGPT touched on it |
| Naming pattern `sprk-{customer}-{env}-{purpose}-{kind}` | Claude (uses `sprk-` to match Dataverse publisher prefix) |
| Dataverse infrastructure refs YES, entity schema NO via generated constants | Claude and ChatGPT explicit; matches the existing solution-versioning model |
| Don't store catalog in Dataverse (bootstrap loop) | Claude explicit; Gemini concurred via diagram |
| Phased: AI Search family first, prove the pattern, then expand | All four; ChatGPT explicit list of priority families |
| Anti-pattern: stringly-typed escape hatch creeps back in | Claude (pitfalls section) |
| Anti-pattern: tests don't get the same catalog | Claude (pitfalls section) |
| Drift detection layered: what-if + Resource Graph + custom reconciler | All four |
| Reference architectures: AVM portal, AAC multi-tenant, Well-Architected SaaS | All four pointed at similar sources |
