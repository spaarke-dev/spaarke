---
name: bff-dataverse-http-unification-2026-06
description: Assessment finding for spaarke-bff-dataverse-http-unification-r1 project — orphan-named-client bug class repeats in 7 BFF services with 4 different HTTP shapes; recommend new IDataverseHttpClient abstraction + Tier A/B/C/D refactoring plan.
metadata:
  type: project
---

# BFF Dataverse HTTP Unification — Assessment 2026-06-23

**Why**: PR #417 fixed 3 playbook executors with `"DataverseApi"` orphan-named-client bug. User asked to scout the rest of the BFF for the same bug class and adjacent issues to scope a follow-up project.

**How to apply**: When designing or reviewing the spec for `spaarke-bff-dataverse-http-unification-r1`, reference `c:\code_files\spaarke-wt-spaarke-daily-update-service-r2\projects\spaarke-daily-update-service-r2\notes\bff-dataverse-http-unification-assessment.md`. The five "known" services have 4 different HTTP semantic shapes — one (EmailTemplateService) is dead code, one fits IGenericEntityService cleanly (EmailAssociationService), three need a new `IDataverseHttpClient` (SessionRestoreService for ETag headers, BulkRagIndexingJobHandler for future $batch, RecordSyncJob for OData paging).

**Key new findings beyond brief**:
- `"BingWebSearch"` named-client is ALSO an orphan (`WebSearchTools.cs:63`, `WebSearchHandler.cs:108` reference it via `CreateClient`; never registered with `AddHttpClient` anywhere). Same bug class as the original PR #417, different external service.
- `EmailTemplateService` has ZERO injection sites despite being DI-registered — confirmed dead.
- Five total `new ClientSecretCredential(...)` direct-construction sites across the codebase — ADR-028 anti-pattern; should consolidate into one auth handler.
- Test fixtures stub `CreateClient("DataverseAssociation")` etc., masking the prod bug — exactly the "fixture-lies-about-DI" anti-pattern from `.claude/constraints/bff-extensions.md § F.2`.
- Recommended design: typed-class `services.AddHttpClient<IDataverseHttpClient, DataverseHttpClient>()` makes the string-orphan bug impossible by construction (compile-time injection). Should amend ADR-010 to prefer typed > named for non-trivial clients.

**Sources**: see assessment file's "Sources" section — 14 BFF code paths, 2 test fixtures, 3 ADRs.
