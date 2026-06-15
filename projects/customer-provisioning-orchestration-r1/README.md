# customer-provisioning-orchestration-r1

> **Status**: Design engagement (Phase 0 — Discovery)
> **Worktree**: `C:\code_files\spaarke-wt-customer-provisioning-orchestration-r1`
> **Branch**: `work/customer-provisioning-orchestration-r1` (off master)
> **Created**: 2026-06-13
> **Supersedes (design)**: `projects/spaarke-environment-factory-r1/design.md`
> **Engagement rule**: discovery-and-design only — NO implementation code this session.

## What this project is

The discovery-and-design engagement for **Customer Provisioning & Deployment Orchestration** — productizing Spaarke-hosted customer onboarding into a verified, resumable, control-plane-driven pipeline.

When a paying customer is approved, an orchestrated process provisions a dedicated Dataverse environment in the Spaarke tenant, deploys the full (managed) solution, provisions per-customer isolated Azure resources, seeds configuration and starter data, and wires post-deploy integrations. The same package later deploys into a customer's own tenant (Model 2) with target tenant as the only meaningful variable.

## Three-layer architecture (locked)

1. **Layer 1 — Deterministic handlers**: provisioning steps as idempotent job handlers under the ADR-004 async job contract. The invariant engine.
2. **Layer 2 — Control-plane API + MCP server** (`spaarke-provisioning`): run-lifecycle tools (`create_provisioning_run`, `run_preflight`, `get_run_status`, `advance_gate`, `get_phase_logs`). Also invariant.
3. **Layer 3 — Front ends** (swappable): Claude Code operator skill now; thin MDA fleet dashboard + Spaarke Assistant later.

Claude Code is an authorized internal MCP client of the control plane — never a runtime product component, never customer-facing.

## Engagement deliverables (this engagement — NO implementation code)

| Phase | Deliverable | Path | Status |
|-------|-------------|------|--------|
| 0 | Discovery report | `discovery/phase-0-discovery-report.md` | ✅ written |
| 1 | Draft ADR (three-layer orchestration + managed-solutions) | `design/ADR-DRAFT-customer-provisioning-orchestration.md` | ⛔ gated on Phase 0 review |
| 1 | Design specs (handler catalog, control-plane API + MCP surface, data model, parameter model) | `design/` | ⛔ gated on Phase 0 review |

**Hard gate**: Phase 1 does not begin until the Phase 0 discovery report is written to disk **and reviewed by the owner**.

## Locked decisions (do not relitigate)

See `discovery/phase-0-discovery-report.md` §1. Summary: managed solutions for customer envs (unmanaged stays dev-only); one package / two targets (Spaarke tenant | customer tenant); no shared resources between customers (one BFF per customer env; dedicated OpenAI/Search/DocIntel/ServiceBus/Redis/KeyVault/AppInsights); Azure subscription per customer (SpaarkeOwned default | CustomerOwned via Lighthouse); Spaarke buys licenses; two identity presets (B2BGuest | NativeAccount); consumption SKUs; model versions pinned per ADR-020; gates verified not inferred; ProvisioningRun is system of record; every step idempotent and resumable.
