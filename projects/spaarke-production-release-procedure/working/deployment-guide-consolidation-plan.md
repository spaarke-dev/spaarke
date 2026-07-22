# Deployment Guide Consolidation Plan

> **Status**: In progress
> **Started**: 2026-04-24
> **Owner**: Ralph / Claude Code

## Motivation

Two overlapping guides exist in `docs/guides/`:

| Guide | Lines | Focus |
|-------|-------|-------|
| `ENVIRONMENT-DEPLOYMENT-GUIDE.md` | 820 | Generic "any environment" + known issues catalog |
| `PRODUCTION-DEPLOYMENT-GUIDE.md` | ~1670 | Production-specific + phases + AI/HUMAN dual execution |

They overlap ~70%, are edited independently, and readers don't know which to use. Consolidate into one authoritative guide covering all environments, with production-specific overlays where needed.

## Content Mapping

| Topic | Source | Target Section |
|-------|--------|----------------|
| Overview + "build once, deploy anywhere" | Both | §1 Overview |
| Dual AI/HUMAN execution | PROD | §1 Overview (callout) |
| Prerequisites (tools, access, quotas) | Both | §2 Prerequisites |
| Subscription/RG strategy | PROD §15 | §3 Deployment Models |
| Azure infrastructure (Bicep + CLI) | Both | §4 Phase 1: Infrastructure |
| Entra ID app registrations | Both | §5 Phase 2: App Registrations |
| Managed identity (incl. staging slot) | PROD §4.5 | §5 Phase 2 |
| Key Vault + App Settings | Both | §6 Phase 3: Secrets |
| Solution export/fix/import pipeline | ENV §6-7 | §7 Phase 4: Dataverse Solutions |
| Managed vs unmanaged | PROD §17 | §7 Phase 4 (sub) |
| Environment variables (7 canonical) | Both | §8 Phase 5: Env Vars |
| SharePoint Embedded setup | Both | §9 Phase 6: SPE |
| BFF API deployment (slot swap) | PROD §6 | §10 Phase 7: BFF API |
| Custom domain + SSL | PROD §7 | §11 Phase 8: Domain (prod only) |
| Customer provisioning | PROD §8 | §12 Phase 9: Customer |
| Validation (17 smoke tests + env var checks) | PROD §8.5, §9 | §13 Phase 10: Validation |
| Day-2 operations | PROD §10 | §14 Day-2 Operations |
| Rollback procedures | PROD §11 | §15 Rollback |
| Troubleshooting decision tree | PROD §12 | §16 Troubleshooting |
| CI/CD pipelines (Azure) | PROD §20 | §17 CI/CD |
| Dataverse CI/CD | PROD §18 | §18 Dataverse CI/CD |
| **Known issues catalog (13 items)** | ENV §13 | **Appendix A** (preserved intact) |
| **Complete app settings reference (40+)** | ENV §14 | **Appendix B** (preserved intact) |
| Resource inventory | PROD §14 | Appendix C |
| Script reference | PROD | Appendix D |

## Target Structure

```
docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md

1. Overview
   1.1 Audiences
   1.2 Build once, deploy anywhere
   1.3 Dual execution model (AI/HUMAN)
   1.4 Related guides

2. Prerequisites
   2.1 Required tools
   2.2 Required access
   2.3 Required Azure quotas
   2.4 Authentication setup

3. Deployment Models
   3.1 Single subscription (dev/testing)
   3.2 Env-separated subscriptions (recommended for prod)
   3.3 Shared platform vs per-customer resources

4. Phase 1: Azure Infrastructure
   4.1 Subscription setup
   4.2 Resource providers
   4.3 Bicep deployment (preferred)
   4.4 Azure CLI reference (fallback)
   4.5 Verification

5. Phase 2: Entra ID App Registrations
   5.1 BFF API app (confidential)
   5.2 UI SPA app (public)
   5.3 Admin consent
   5.4 Dataverse application user
   5.5 Managed identity RBAC (⚠️ both slots!)

6. Phase 3: Key Vault + App Settings
   6.1 Seed secrets
   6.2 Configure app settings with KV references
   6.3 Verify

7. Phase 4: Dataverse Solutions
   7.1 Export from dev (with fix pipeline)
   7.2 Max upload size adjustment
   7.3 Import order (SpaarkeFeatures → SpaarkeCore → others)
   7.4 Managed vs unmanaged guidance
   7.5 Version management

8. Phase 5: Dataverse Environment Variables
   8.1 The 7 canonical variables
   8.2 Automated setup (Provision-Customer.ps1)
   8.3 Manual setup (PAC CLI or portal)
   8.4 Data type corrections (common issue)

9. Phase 6: SharePoint Embedded
   9.1 Container type creation (SPO Mgmt Shell only)
   9.2 Add billing (Syntex regions — not westus2!)
   9.3 Register container type (Graph API)
   9.4 Create root container + activate
   9.5 Store IDs (Key Vault + env var)
   9.6 Per-BU containers

10. Phase 7: BFF API Deployment
    10.1 Build + zip
    10.2 Slot deploy (production)
    10.3 Direct deploy (non-prod)
    10.4 Startup troubleshooting flow

11. Phase 8: Custom Domain + SSL (Production Only)
    11.1 DNS records (CNAME + TXT — BOTH required)
    11.2 Bind domain
    11.3 Enable SSL
    11.4 CORS configuration

12. Phase 9: Customer Provisioning
    12.1 Provision-Customer.ps1 end-to-end
    12.2 13-step pipeline walkthrough
    12.3 Resume from step (idempotency)
    12.4 Demo vs real customer differences

13. Phase 10: Validation
    13.1 Test-Deployment.ps1 (17 smoke tests)
    13.2 Validate-DeployedEnvironment.ps1 (env vars, CORS, dev leakage)
    13.3 Manual verification checklist

14. Day-2 Operations
    14.1 Subsequent BFF API deployments
    14.2 Provisioning additional customers
    14.3 Customer decommissioning
    14.4 Secret rotation (90-day recommended)
    14.5 Log viewing + App Insights

15. Rollback Procedures
    15.1 BFF API (slot swap back)
    15.2 Infrastructure (Bicep redeploy)
    15.3 Dataverse solutions (version rollback)

16. Troubleshooting
    16.1 Decision tree (what to try first)
    16.2 Common issues reference

17. CI/CD Integration (Azure)
    17.1 deploy-platform.yml
    17.2 deploy-bff-api.yml
    17.3 provision-customer.yml
    17.4 GitHub environment protection
    17.5 Required secrets

18. CI/CD for Dataverse
    18.1 Current state
    18.2 Recommended GitHub Actions architecture
    18.3 PAC CLI service principal auth
    18.4 Microsoft Power Platform Actions
    18.5 Solution Packager (advanced)

Appendix A: Known Issues and Workarounds (from ENV §13)
  - 13 documented issues with symptoms and fixes

Appendix B: Complete App Settings Reference (from ENV §14)
  - 40+ settings with Key Vault refs

Appendix C: Resource Inventory
  - Platform and per-customer resource names

Appendix D: Script Reference
  - All scripts in scripts/ with purpose and key parameters

Appendix E: Environment-Specific Overlays
  - Dev vs UAT vs Demo vs Production differences
```

## Transition Plan

1. **Draft consolidated guide** at `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md` (new file)
2. **Mark old guides as superseded**:
   - Add banner at top: "This guide has been consolidated into [SPAARKE-DEPLOYMENT-GUIDE.md]. Use the new guide for all deployments."
   - Keep files in place for ~2 weeks to catch external references, then archive to `docs/guides/archive/`
3. **Update cross-references**:
   - `CLAUDE.md` (root) — any mentions
   - `docs/guides/CUSTOMER-DEPLOYMENT-GUIDE.md`
   - `docs/guides/CUSTOMER-ONBOARDING-RUNBOOK.md`
   - Any skills in `.claude/skills/` that reference old paths
4. **Validate links**: run link-check over docs/
5. **Commit** with clear message indicating consolidation

## Open Questions

| Question | Tentative Answer |
|----------|-----------------|
| Rename to drop "GUIDE" suffix? | Keep — it's the convention |
| Should CUSTOMER-DEPLOYMENT-GUIDE.md and CUSTOMER-ONBOARDING-RUNBOOK.md also fold in? | Not now — they're customer-facing / operational and have distinct audience |
| Put working copy of draft in `projects/` or directly in `docs/`? | Directly in `docs/guides/` — it's the source of truth; working notes go in this plan file |
| Delete old files or archive? | Archive under `docs/guides/archive/` after 2-week transition |

## Progress

- [x] Plan written
- [x] Fix ShareLinkBaseUrl inaccuracy in ENV guide
- [x] Fix ShareLinkBaseUrl inaccuracy in PROD guide
- [x] Draft consolidated SPAARKE-DEPLOYMENT-GUIDE.md
- [x] Delete old ENV and PROD guides (user preferred delete over archive)
- [x] Update cross-references in azure-deploy skill, M365-COPILOT guides, HOW-TO-SETUP-CONTAINERTYPES, ADR-027
- [ ] Commit

## Outcome

**Consolidated guide**: [docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md](../../../docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md)
- 18 sections + 4 appendices
- All 14+ known issues preserved
- Complete app settings reference preserved
- Script reference consolidated
- Dual AI/HUMAN execution model throughout

**Deleted files**:
- `docs/guides/ENVIRONMENT-DEPLOYMENT-GUIDE.md`
- `docs/guides/PRODUCTION-DEPLOYMENT-GUIDE.md`

**Cross-references updated** (5 files):
- `.claude/skills/azure-deploy/SKILL.md` (3 instances)
- `docs/guides/M365-COPILOT-DEPLOYMENT-GUIDE.md`
- `docs/guides/M365-COPILOT-ADMIN-GUIDE.md`
- `docs/guides/HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md`
- `docs/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md`

**Historical project files** (intentionally left alone — archival):
- `projects/spaarke-self-service-registration-app/tasks/041-deploy-and-configure.poml`
- `projects/demo-environment-deployment/DEPLOYMENT-ASSESSMENT.md`
- `projects/ai-procedure-refactoring-r1/notes/*`
- `projects/production-environment-setup-r2/tasks/053-update-documentation.poml`
- `projects/x-production-environment-setup-r1/*`
