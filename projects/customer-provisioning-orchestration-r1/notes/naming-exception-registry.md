# Naming Exception Registry — `customer-provisioning-orchestration-r1`

> **Owner**: customer-provisioning-orchestration-r1
> **Authority**: spec.md FR-35 + §7.9 R1–R4 · owner directive #3 (2026-08-15) · r3 task 063 handoff §4a
> **Canonical standard**: [`docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md`](../../../docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md) § "KV-Secret & Resource Naming Standard (Conformance-Gated)"
> **Enforcement**: `scripts/naming-conformance-check.ps1` (advisory-until-remediated per surface — r1 owns application, r3 owns the gate)
> **Codified**: 2026-08-17 by task 020

---

## Purpose

The canonical KV/resource naming standard (`sprk-{env}-kv`, env-agnostic secret names, one canonical casing per logical secret, no orphan/duplicate secrets) is enforced repo-wide by `scripts/naming-conformance-check.ps1`. A small set of **live dev-artifact names** cannot be safely renamed without breaking active flows; a smaller set of **live PascalCase secrets** are grandfathered as the single canonical casing (per FR-35 R2). Both classes are documented here as the **single source of exceptions** so:

- Task 021 (H13 naming-conformance-check invocation) does NOT flag them as canonical drift.
- The Phase H canonical secret-catalog manifest generator (FR-36) treats them as authoritative exceptions.
- Future editors see the carve-out rationale inline at the Bicep param without diving into r3-handoff / owner-directive history.

Missing an exception here is a defect — future well-meaning conformance sweeps will propose renames that break live flows.

---

## Registry

| Exception | Scope | Rationale | Owner | Expiry | Cross-ref |
|---|---|---|---|---|---|
| `spaarke-spekvcert` | Azure Key Vault (dev subscription) | Live dev-env vault. Rename would break dev-env cert-based flows + dev App Service KV references + Dataverse-persisted config still pointing at this vault. Canonical form would be `sprk-dev-kv`; owner directive #3 (2026-08-15) explicitly rejects live-dev remediation for this project. Codified as a **DO-NOT-RENAME** carve-out per FR-35 + §7.9 R3. Bicep vault-name is a param (task 018) so new-environment provisioning uses the canonical `sprk-{env}-kv` form while the dev exception is honored by parameter override at deployment time. | customer-provisioning-orchestration-r1 (delegated from platform owner) | **Permanent — dev artifact** (revisit only if dev subscription is decommissioned or explicitly rebuilt on canonical names) | spec.md FR-35 · §7.9 R3 · r3 handoff §4a · `AZURE-RESOURCE-NAMING-CONVENTION.md` § "Dev Environment (DO NOT RENAME)" |
| `Dataverse-ClientSecret` | Key Vault secret (all envs) | Grandfathered PascalCase per FR-35 R2 (one canonical casing per logical secret). **DO-NOT-DELETE**: BFF OBO + shared-lib Dataverse code paths depend on this exact spelling. Never rotate to a second casing. Applying the canonical env-agnostic name pattern requires keeping this spelling as the single canonical form. Handled in task 019 (canonical KV-secret name application at H4). | customer-provisioning-orchestration-r1 (secret-name owner) | **Permanent — grandfathered PascalCase** (until #3b credential migration on NG1 / RED-4 track completes, at which point this secret is retired, not renamed) | r3 handoff §4a · task 019 · spec.md MUST rules line 242 |
| `BFF-API-ClientSecret` | Key Vault secret (all envs) | Grandfathered PascalCase per FR-35 R2. **DO-NOT-DELETE**: shared-lib Dataverse client bootstrap depends on this exact spelling. Same rationale as `Dataverse-ClientSecret`. Handled in task 019. | customer-provisioning-orchestration-r1 (secret-name owner) | **Permanent — grandfathered PascalCase** (until credential migration retires it) | r3 handoff §4a · task 019 · spec.md MUST rules line 242 |
| `spe-infrastructure-westus2` | Azure Resource Group (dev subscription) | Legacy dev-env resource group. Uses prohibited `spe-*` prefix. Rename would require re-parenting all dev-env resources (App Service, KV, Dataverse-referenced storage). Owner directive #3 excludes live-dev remediation. Already documented in `AZURE-RESOURCE-NAMING-CONVENTION.md` § "Dev Environment (DO NOT RENAME)" — cross-referenced here as an r1-scoped exception the conformance gate must recognize. | platform owner (via directive #3) | **Permanent — dev artifact** | `AZURE-RESOURCE-NAMING-CONVENTION.md` § "Dev Environment (DO NOT RENAME)" |
| `spe-api-dev-67e2xz` | Azure App Service (dev subscription) | Legacy dev-env BFF App Service. Uses prohibited `spe-*` prefix + random suffix. Rename requires re-creation (App Service names are immutable) which would rotate all KV references + App Registration reply URLs. Owner directive #3 excludes. | platform owner | **Permanent — dev artifact** | `AZURE-RESOURCE-NAMING-CONVENTION.md` § "Dev Environment (DO NOT RENAME)" |
| `spe-bff-api` | Entra ID App Registration (dev) | Legacy dev-env BFF app registration. Rename would break existing OBO consent + change the API scope URI (`api://spe-bff-api/user_impersonation`) breaking all client apps trusting the dev issuer. Owner directive #3 excludes. | platform owner | **Permanent — dev artifact** | `AZURE-RESOURCE-NAMING-CONVENTION.md` § "Dev Environment (DO NOT RENAME)" |
| `api://spe-bff-api/user_impersonation` | Entra ID API Scope URI (dev) | Downstream of `spe-bff-api` app registration. Rename would break dev client apps. Retained for consistency with the app-reg exception above. | platform owner | **Permanent — dev artifact** | `AZURE-RESOURCE-NAMING-CONVENTION.md` § "Dev Environment (DO NOT RENAME)" |
| `sdap-jobs` | Service Bus Queue (dev) | Legacy dev-env job queue using prohibited `sdap-*` prefix. Rename requires draining + queue re-creation. New environments use `document-processing` (canonical). | platform owner | **Permanent — dev artifact** (new environments provision `document-processing`, not `sdap-jobs`) | `AZURE-RESOURCE-NAMING-CONVENTION.md` § "Dev Environment (DO NOT RENAME)" |

---

## Consumer contract

The following consumers MUST treat this registry as the single source of exceptions when evaluating naming-conformance:

- **`scripts/naming-conformance-check.ps1`** — task 021 wires exception-awareness into the H13 acceptance invocation. Exceptions listed here MUST NOT be reported as canonical drift.
- **Phase H canonical secret-catalog manifest generator** (FR-36) — treats `Dataverse-ClientSecret` + `BFF-API-ClientSecret` as authoritative canonical spellings; treats `spaarke-spekvcert` as the vault target only when the deployment target is the dev subscription.
- **Task 018 Bicep vault-name parameterization** — the `keyVaultName` param default is `sprk-${env}-kv` (canonical); dev-env deployment overrides with `spaarke-spekvcert`.
- **Any future conformance sweep** — MUST consult this registry before proposing a rename PR.

---

## Adding a new exception

An addition to this registry is a **carve-out from the canonical standard** and requires:

1. **Rationale that names a concrete failure mode** if canonical is applied (per CLAUDE.md §11 cost-of-doing-nothing rule). "It would be inconvenient to rename" is not sufficient; "renaming breaks live X flow because Y" is.
2. **Owner directive citation** or explicit owner sign-off in PR description.
3. **Expiry policy** — either a concrete condition that would end the exception (e.g. "when dev subscription is decommissioned") or "permanent — dev artifact" with the reason it is not remediable.
4. **Cross-reference** to the canonical standard doc + any spec/design/handoff citation.
5. **PR must be reviewed** against CLAUDE.md §6.5 ADR Conflict Resolution Protocol paths — this is a Path A (project-scoped exception) codification.

---

## History

- **2026-08-17** — Initial registry published by task 020 (per spec.md FR-35 + §7.9 R3 + owner directive #3 + r3 handoff §4a). Codifies `spaarke-spekvcert` DO-NOT-RENAME + cross-references PascalCase secret grandfathering (task 019) + legacy dev-env names (`AZURE-RESOURCE-NAMING-CONVENTION.md`).
