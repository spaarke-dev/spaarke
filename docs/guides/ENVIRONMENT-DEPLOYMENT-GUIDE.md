# Spaarke Environment Deployment Guide (STUB — content merged)

> **Status**: **RETIRED as of 2026-08-17.** Content merged into the single authoritative customer-provisioning guide.
> **Authoritative source**: [`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md)

This guide previously carried the end-to-end environment build (Azure infra, Entra app registrations, KV secret population, Dataverse solution import, SharePoint Embedded, BFF API deployment, Dataverse App User, environment variables, validation) validated during the 2026-03 demo environment build. Per task 001 of `customer-provisioning-orchestration-r1` (spec.md Gap 4 + R6 doc-drift carry-over), that content is now consolidated into [`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) — covering both Model 1 (shared trial) and Model 2 (dedicated stamp), the full handler catalog H0–H14 (§5), naming + KV secret bootstrap (§6), the per-phase pipeline walkthrough (§7), and troubleshooting (§13). **Follow the authoritative guide** for any new environment provisioning; this stub is retained for git history + inbound-link continuity.
