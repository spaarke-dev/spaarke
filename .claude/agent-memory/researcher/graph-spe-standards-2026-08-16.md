---
name: graph-spe-standards-2026-08-16
description: Aug 2026 sanity-check of Graph SDK v6 / SPE / MI / multi-tenant consent / Exchange app-only / Power Platform standards against Spaarke r1 customer-provisioning design. Verdict — r1 design v3.2 has NO stale patterns blocking execution; three medium-term upgrade paths worth capturing (Exchange RBAC-for-Apps replaces ApplicationAccessPolicy; MI-as-FIC GA'd; SPE bootstrap privilege loosened June 2026).
metadata:
  type: reference
---

Full report: `projects/customer-provisioning-orchestration-r1/notes/graph-spe-2026-08-standards-spike.md`.

## Headline findings

- **Graph SDK v6.5.0 / Kiota 2.0 = latest** (no v7). ODataError contract unchanged since the .NET 10 cutover. r1 design matches.
- **SPE confidential-client + up-to-24h replication = STILL CURRENT** per [SPE auth doc updated 2026-07-13](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/configure-authentication-authorization). T6 fix correct.
- **Exchange RBAC for Applications** explicitly *"replaces Application Access Policies"* per [MS Learn (updated 2026-03-16)](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac). No hard cutover date yet, but the legacy doc is titled `(legacy)` and Microsoft says deprecation "announced in future." Coexistence is safe (additive). r1 keeps ApplicationAccessPolicy for execution; add Phase-D migration item to design.
- **MI-as-FIC GA in 2026** — UAMI can be a federated credential on an Entra app-reg (max 20 FICs). Enables secretless cross-tenant. Phase-C-plus optimization for Model 2, not r1 scope.
- **Terraform Power Platform provider v4.1.0** (Jan 2026) validates D14. Our M-10 deferral is orthogonal to provider readiness.

## SPE 2026 rollup (from [whats-new](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/whats-new))

- **June 2026**: `FileStorageContainerType.Manage.All` no longer requires SPE-Admin / Global-Admin — any non-guest user in owning tenant can create a container type. H8 runbook simplification.
- **May 2026**: bulk container permissions upsert via delta PATCH; container-type audit logs.
- **March 2026**: container-type owners via `permissions` navigation (beta, max 3 owners); SPE agent SDK deprecated → SPE-as-Foundry-knowledge-source.
- **Feb 2026**: SPE PP connector GA; SPE in Microsoft 365 21Vianet (China).
- **Jan 2026**: `fileStorageContainer` column APIs GA in v1.0 (confirms `spe-dedup-content-identity-2026-07` dedup lever is GA).
- **Dec 2025**: `fileStorageContainerType` + `fileStorageContainerTypeRegistration` APIs GA in v1.0.

## Answer to "does r1 design v3.2 need material changes based on this spike?"

**NO.** r1 design v3.2 has no stale patterns blocking execution. Three future-oriented backlog items surfaced (Exchange RBAC-for-Apps migration; MI-as-FIC exploration; H8 runbook privilege footnote) — none affect the current phasing plan.

## Related memories

- [[kiota-cve-2026-44503-tfm-2026-08-11]] — Graph 6.5.0 / Kiota 2.0.0 baseline for .NET 10
- [[spe-ciam-crosstenant-apponly-brokering-2026-07-18]] — app-only vs delegated matrix
- [[spe-dedup-content-identity-2026-07]] — custom columns dedup lever
- [[spe-wopi-coauthoring-lock-423-2026-07-30]] — WOPI co-authoring lock behavior
- [[ciam-user-provisioning-graph-2026-07-19]] — cross-tenant Graph identity mechanics
- [[spaarke-customer-stamp-pricing-2026-08-12]] — Aug 2026 cost baseline

## Open questions

- RBAC-for-Apps hard-cutover date is unpublished. Recheck each quarter.
- MI-as-FIC has GA-'d for cross-tenant Azure, but no fully worked SPE example ships from MS yet. If Spaarke adopts, budget spike time.
