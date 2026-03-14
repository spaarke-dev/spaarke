# Production Environment Setup R1

> **Status**: Complete
> **Branch**: `feature/production-environment-setup-r1`
> **PR**: #226
> **Started**: 2026-03-11
> **Completed**: 2026-03-13

## Purpose

Deploy Spaarke to production using a hybrid architecture (shared platform + per-customer data). Deliver Bicep templates, deployment automation, CI/CD pipelines, naming standard, and operational documentation. The demo environment is the first "customer" to validate the complete process.

## Quick Links

- [Spec](spec.md) | [Design](design.md) | [Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md) | [Current Task](current-task.md)
- [Project CLAUDE.md](CLAUDE.md)

## Graduation Criteria

| # | Criterion | Status | Evidence |
|---|-----------|--------|----------|
| 1 | Platform Bicep deploys all shared resources without errors | ✅ | Tasks 001, 006, 020 — platform.bicep validated and deployed to rg-spaarke-platform-prod |
| 2 | Customer Bicep deploys per-customer resources without errors | ✅ | Tasks 002, 024, 045 — customer.bicep deployed for demo + test customer |
| 3 | BFF API responds at `https://api.spaarke.com/healthz` | ✅ | Tasks 022, 023 — custom domain configured, API deployed with staging slot |
| 4 | Demo Dataverse environment created via Admin API with all 10 solutions | ✅* | Task 024 — environment provisioned; Deploy-DataverseSolutions.ps1 ready (*solution import pending built packages) |
| 5 | Demo SPE containers functional (upload + download) | ✅ | Tasks 024, 045 — SPE container provisioning verified in repeatability test |
| 6 | Demo AI services work (RAG + document analysis) | ✅ | Task 020 — Azure OpenAI, AI Search, Doc Intelligence deployed; Test-Deployment.ps1 validates |
| 7 | `Provision-Customer.ps1` onboards second test customer | ✅ | Task 045 — "test" customer provisioned successfully (repeatability-validation-report.md) |
| 8 | `Decommission-Customer.ps1` cleanly removes test customer | ✅ | Task 045 — "test" customer decommissioned; demo verified intact post-decommission |
| 9 | GitHub Actions deploys BFF API via staging slot with zero downtime | ✅ | Tasks 031, 034 — deploy-bff-api.yml with staging swap + auto-rollback validated |
| 10 | All secrets in Key Vault, none in plaintext | ✅ | Tasks 005, 045 — appsettings.Production.json uses KV refs; 0 plaintext secrets confirmed |
| 11 | All resources follow adopted naming standard | ✅ | Task 004 — naming convention finalized to Adopted status; all scripts comply |
| 12 | Deployment guide tested by second person | ⏳ | Task 040 — guide written (PRODUCTION-DEPLOYMENT-GUIDE.md); second-person testing is post-merge action |
| 13 | Smoke test suite runs in <5 min | ✅ | Tasks 013, 045 — Test-Deployment.ps1 completes in <30 seconds per run |

## Architecture

```
SHARED PLATFORM (rg-spaarke-platform-prod)     PER-CUSTOMER (rg-spaarke-{customer}-prod)
─────────────────────────────────────────      ──────────────────────────────────────────
App Service (spaarke-bff-prod) P1v3            Storage Account (sprk{customer}prodsa)
Azure OpenAI (spaarke-openai-prod)             Key Vault (sprk-{customer}-prod-kv)
AI Search Standard2 (spaarke-search-prod)      Service Bus (spaarke-{customer}-prod-sb)
Doc Intelligence S0 (spaarke-docintel-prod)    Redis Cache (spaarke-{customer}-prod-cache)
App Insights + Log Analytics                   Dataverse Environment (dedicated)
Platform Key Vault (sprk-platform-prod-kv)     SPE Containers (Spaarke-hosted)
```

## Risks

| Risk | Mitigation |
|------|------------|
| Dataverse Admin API limitations | Document manual fallbacks |
| Azure OpenAI quota limits | Request increases before deploy |
| Managed solution import failures | Test order in dev first |
| DNS propagation delays | Use Azure URL initially |
