# Spaarke Resource Architecture (R1)

> **Project**: `spaarke-resource-architecture-r1`
> **Status**: 🟡 **DEFERRED** — design only; execution sequenced after [`sdap-bff-api-remediation-fix`](../sdap-bff-api-remediation-fix/) completes
> **Created**: 2026-05-23
> **Origin**: AI Search index regression investigation (2026-05-22) surfaced 184 references to a single resource identifier across 67 files. The investigation produced two fixes (wizard `documentId` linkage, ribbon GUID lowercasing) but the broader pattern — scattered resource references with no single source of truth — was flagged as foundational debt requiring a structured project.

## Why this project exists

A single Azure AI Search index name (`spaarke-knowledge-index-v2`) appears in 184 places across 67 files: BFF code, tests, docs, schema files, deploy scripts, CI/CD templates, and historical project artifacts. The same pattern likely exists for ~13 categories of resources (indexes, OpenAI deployments, Key Vault secrets, Cosmos containers, Service Bus queues, Redis prefixes, App Services, SPE container types, Dataverse env URLs, MIs, AAD app registrations, plus internal Spaarke abstractions like playbook IDs and capability names).

There is no single source of truth that says "here are the resources Spaarke depends on, and here's what they're called per environment per customer." Resource names are leaked through every layer of the stack, which makes:

- Renames cause cross-layer drift instantly (the 2026-05-19 / 2026-05-22 indexing regression was a downstream consequence)
- Customer-deployment customization fragile and untestable
- Drift between code expectations, manifest declarations, and live Azure state invisible until someone reproduces a user-facing failure

This project addresses the disease, not the symptom.

## What this project delivers (target shape)

1. **Per-resource-family typed catalogs** (`ISearchCatalog`, `IQueueCatalog`, `IIdentityCatalog`, `IAiCatalog`, `ISecretCatalog`, etc.) with enum-driven accessors — never string keys. No code outside the catalog implementation references resource names as literals.
2. **Single Bicep naming function** — `(customer, env, kind, purpose) → name` lives in one place, with deterministic slug rules and length-truncation for resources with naming limits.
3. **Per-customer `customer.parameters.bicepparam` files** with `@validate()` decorators enforcing "every resource must be explicitly declared — no implicit shared defaults."
4. **Azure Deployment Stacks** with `--deny-settings-mode denyWriteAndDelete` — prevents portal drift by denying out-of-band changes to provisioned resources.
5. **Azure App Configuration as runtime-mutable layer** (Phase 5) — for things that genuinely change at runtime: feature flags, AI prompt overrides, throttle thresholds. NOT for resource names (those are deploy-time immutable).
6. **Layered drift detection** — Bicep `what-if` in CI, nightly Azure Resource Graph audit per customer subscription, small custom reconciler script.
7. **New ADR** ("Spaarke Resource Catalog Architecture") extending — not superseding — ADR-027.

## What this project explicitly does NOT do

- **Standalone YAML/JSON manifest as a separate artifact** — rejected after external review. The Bicep parameters + generated solution constants + typed catalogs are the manifest. A separate file would create the synchronization problem it was meant to solve.
- **AVM rip-and-replace** — opportunistic migration only as resources are touched for other reasons.
- **Dataverse entity schema in catalog** — entity logical names live in solution-versioned generated constants, not the catalog. The catalog covers **infrastructure** references only (env URL, app registrations, MI client IDs).
- **Big-bang refactor** — one resource family per slice, end-to-end, then the next.

## Why deferred

The [`sdap-bff-api-remediation-fix`](../sdap-bff-api-remediation-fix/) project is starting now and touches the same code surface (`Services/Ai/PublicContracts/` facade creation, AI job handler relocation, BFF DI organization). Running two foundational refactors in parallel is the failure mode to avoid. This project resumes after BFF remediation lands.

## Sequencing

1. **Now**: BFF remediation runs.
2. **On completion**: Re-review this project's design against the post-remediation BFF state. The shape of `Services/Ai/PublicContracts/` will tell us where catalogs should live and how they integrate.
3. **Then**: Re-confirm scope with owner. Some of the work proposed here may have been absorbed by remediation; some may need adjustment.
4. **Then**: Begin Phase 1 (AI Search family) as the first execution slice.

## Files in this project

| File | Purpose |
|---|---|
| [`README.md`](README.md) | This file — orientation |
| [`design.md`](design.md) | Full design: context, recalibrated approach, naming convention, phased plan, open questions |
| [`research/external-perspectives.md`](research/external-perspectives.md) | Verbatim responses from DeepSeek, ChatGPT, Gemini, and Claude to the external-review research prompt |
| [`current-task.md`](current-task.md) | Project state — currently DEFERRED |
| [`CLAUDE.md`](CLAUDE.md) | AI agent instructions for this project |

## Related projects

- [`projects/sdap-bff-api-remediation-fix/`](../sdap-bff-api-remediation-fix/) — **Prerequisite.** Owns service composition, AI hygiene, publish-size, CI guardrails.
- [`projects/spaarke-connect-integration-module-r1/`](../spaarke-connect-integration-module-r1/) — **Dependent.** Spaarke Connect needs the customer-overridable resource pattern this project establishes. Connect's design should be updated with this as a prerequisite once this project executes.
- [`projects/ai-search-indexing-fix/`](../ai-search-indexing-fix/) — **Origin.** The investigation that surfaced the 184-references finding.

## Related ADRs and constraints

- [`ADR-027`](../../.claude/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md) — Platform/customer Bicep split. This project extends ADR-027.
- [`ADR-013`](../../.claude/adr/ADR-013-ai-architecture.md) — BFF Hygiene. The catalog work helps enforce extension constraints.
- [`ADR-028`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — Auth + Managed Identity. Auth resource refs flow through catalog.
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — BFF placement criteria. Clean catalogs make extraction decisions easier.
- [`.claude/FAILURE-MODES.md#g-4`](../../.claude/FAILURE-MODES.md) — AI Search field-config immutability, the most recent example of what this project prevents.
