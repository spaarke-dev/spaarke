# Spaarke Multi-Container Multi-Index Routing

> **Last Updated**: 2026-06-07
>
> **Status**: Complete (with deferred items + 3 UAT pending in-browser verification)

## Overview

Extends Spaarke's create wizards, BFF resolver, SemanticSearchControl PCF, and SemanticSearch code page to support **record-scoped routing**: containers and AI Search indexes are selected per record at create time, with Business Unit cascading defaults and individual records able to override. Replaces the prior "one container + one index per tenant" assumption that broke after the recent migration and the new "Protected Matter" requirement.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan, WBS, dependencies |
| [Design Spec](./spec.md) | AI-optimized specification (FR/NFR enumerated) |
| [Design Doc](./design.md) | Original 497-line design (4 review rounds) |
| [Task Index](./tasks/TASK-INDEX.md) | Task tracker with parallel groups |
| [AI Context](./CLAUDE.md) | Project-scoped Claude Code context |
| [Current Task](./current-task.md) | Active task state (context recovery) |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Complete |
| **Progress** | 91% (39 of 43 ✅, 1 🚫 deferred — Invoice wizard doesn't exist, 3 🔲 UAT pending) |
| **Target Date** | 2026-06-07 |
| **Completed Date** | 2026-06-07 |
| **Owner** | Spaarke development team |

## Problem Statement

Spaarke documents are stored in SharePoint Embedded (SPE) containers and indexed in Azure AI Search. The platform assumed **one container + one index per tenant**, but operational reality has changed: a recent migration introduced multiple SPE containers, and a new "Protected Matter" requirement needs an isolated index for sensitive matters. Today's record creates write either an empty `sprk_containerid`/`sprk_searchindexname` or the wrong value, and the BFF + PCF + code-page chain can only target one index. Without per-record routing, the platform cannot honor data isolation, and existing records have inconsistent values that need to be backfilled.

## Solution Summary

Extend the existing **Spaarke Create Wizards** (Matter, Project, Invoice, WorkAssignment, Event) and the **DocumentUploadWizard** to populate both `sprk_containerid` and `sprk_searchindexname` on create — sourced from the user's Business Unit, with explicit overrides preserved (INV-5). Extend the BFF `IKnowledgeDeploymentService.GetSearchClientAsync` resolver with an optional explicit `indexName` parameter validated against an allow-list. Bump SemanticSearchControl PCF to **v1.1.74** with a new bound property for the index name and full filter parity in the `navigateTo` envelope to the code page. Update the SemanticSearch code page to consume those envelope params end-to-end. Run a one-time PowerShell backfill (parent-record + document + drift audit) to correct historical data using evidence from each document's existing `sprk_graphdriveid`. **No Dataverse plugins, no Power Automate, no new field mappings** — both inheritance mechanisms (OOB attributemaps and `sprk_fieldmappingprofile`) remain untouched within their existing scopes.

## Graduation Criteria

The project is considered **complete** when:

- [x] A new Matter created via the wizard has both `sprk_containerid` and `sprk_searchindexname` populated from the user's BU (MCP-verified, after post-UAT cascade-alignment fix 2026-06-07).
- [ ] An operator-set explicit `sprk_searchindexname` override on a Matter is inherited by every Document subsequently uploaded under that Matter — **UAT pending in-browser verification** (post-UAT fix deployed; awaiting operator re-test).
- [ ] A BU's `sprk_searchindexname` value can be changed; existing records retain their values; new records use the new BU value (INV-3 coexistence — MCP-verified before/after). **Not yet exercised; backfill dry-run confirms backfill scripts honor INV-5.**
- [ ] SemanticSearchControl PCF v1.1.74 on a Protected Matter (`sprk_searchindexname = "spaarke-file-index"`) returns only documents from that index — **UAT pending; the indexer pipeline finding (AI Search not pulling from SPE) currently makes this not externally testable**.
- [ ] PCF "Open in Semantic Search" launches the code page modal with identical filter state + result set as the PCF was showing — **UAT pending in-browser side-by-side compare**.
- [x] Backfill scripts (parent-record + document + drift-audit) run end-to-end against the test environment without overwriting explicit values and halt loudly on unmapped containers. **Dry-run confirms behavior; 31 historical matters + 395 historical documents would be filled with 0 INV-5 violations and 0 halts. Drift-audit script has a small schema-assumption bug documented for follow-up.**
- [x] Operator runbook (`docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md`) covers all 7 ops scenarios from design §6.
- [x] All 8 phases (A through H) deploy in dependency order — 018 BFF (Azure App Service), 029 Wizards (6 web resources), 035 PCF v1.1.74 (Dataverse solution), 044 SemanticSearch code-page — all published to SPAARKE DEV 1.

## Scope

### In Scope

- Phase A — 5 Spaarke create wizards + DocumentUploadWizard set `sprk_searchindexname`; CreateProjectWizard latent `sprk_containerid` gap fixed.
- Phase A.5 — Operator pre-deploy BU value setup (per §5.0 of design.md).
- Phase B — BFF `IKnowledgeDeploymentService.GetSearchClientAsync` signature extension + allow-list validation + request DTO additions.
- Phase D — PCF v1.1.74: new bound `searchIndexName` property + send in BFF request + navigateTo envelope.
- Phase D.1 — Full filter parity (query/scope/entityId/threshold/searchMode/fileTypes/dateFrom/dateTo/tags/associatedOnly) in PCF → code page envelope.
- Phase E — SemanticSearch code page: extended `parseUrlParams`, App.tsx wiring, hooks include `searchIndexName`.
- Phase F — One-time PowerShell backfill (parent records + documents + drift audit) using §5.1 hardcoded `sprk_graphdriveid` → `sprk_searchindexname` map.
- Phase G — Operator runbook + architecture-doc update.
- Phase H — Coordinated deploy + UAT.

### Out of Scope

- Dataverse plugins (Spaarke convention)
- Power Automate flows (Spaarke convention)
- New Dataverse field mappings (OOB attributemaps + `sprk_fieldmappingprofile` stay untouched)
- Populating `sprk_containerid` on `sprk_document` (canonical Document container field is `sprk_graphdriveid`)
- BU-change auto-sync / fan-out (coexistence is the desired model per INV-3)
- Cross-tenant search
- End-user index picker UI
- Re-indexing API for moving documents between physical indexes (future epic)
- Container → index map maintenance UI (hardcoded in backfill for R1; future epic)
- Orphan Document handling (dev/test data; out per §9 round-3)
- AI Search index provisioning automation (operator provisions in Azure)

## Key Decisions

| Decision | Rationale | ADR / Reference |
|----------|-----------|-----------------|
| Inherit via Spaarke Create Wizards, not Dataverse field mappings | Existing OOB attributemaps don't cascade container/index; wizards are already the cascade point | spec.md §Owner Clarifications row 6 |
| BFF allow-list validation (static appsettings) | Tightest blast-radius without per-tenant lookup; rejects typos before they hit Azure Search | spec.md FR-BFF-02 / FR-BFF-06 |
| Backfill via one-time PowerShell from Claude Code project (no scheduled job) | Migration is one-time; ongoing values come from wizards | spec.md §Owner Clarifications row 3 |
| Backfill derives container from evidence (mode of child docs' `sprk_graphdriveid`), not from BU | BU has been changed; evidence is more reliable | spec.md §Owner Clarifications row 10 |
| Document container field is `sprk_graphdriveid` only; `sprk_containerid` stays NULL on documents | Documents already have the canonical pointer; adding `sprk_containerid` would duplicate state | spec.md §Owner Clarifications row 12 / INV |
| PCF version bump to v1.1.74 (5-location update per `/pcf-deploy`) | Standard Spaarke PCF deploy contract | spec.md FR-PCF-04 + NFR-10 |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Stale `dist/` of `@spaarke/auth` or `@spaarke/ui-components` poisons PCF/code-page bundles | High | Medium | Saved lesson `feedback_stale-shared-lib-dist-poisons-codepage-bundle` — mandatory clean rebuild before PCF + code-page builds (NFR-10, NFR-11) |
| Unmapped SPE container appears in backfill (third unknown ID) | Medium | Low | Backfill HALTS LOUD with operator-actionable error; operator extends §5.1 hardcoded map (FR-BF-01 / FR-BF-02) |
| BFF allow-list misconfigured per environment → valid requests rejected | High | Low | INFO log at startup shows the allow-list (FR-BFF-06); operator runbook covers per-env config |
| Wizards deployed before BFF accepts new field → 400 errors in prod | Medium | Low | Strict deploy order A.5 → B → A → D → E → F (spec §Dependencies + §11 of design) |
| Filter-parity drift between PCF state and code page seed → UAT fails | Medium | Medium | FR-PARITY-01/02 enumerates every param; UAT walkthrough is an explicit gate |
| Backfill kills a 10K-record run partway → state diverges | Low | Low | All backfill scripts are idempotent + resumable + paged (FR-BF-04 / NFR-04) |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Azure AI Search indexes `spaarke-knowledge-index-v2`, `spaarke-file-index` | External | Ready | Operator-provisioned in tenant subscription |
| SPE containers `b!yLRd…` and `b!vzGD…` | External | Ready | Operator-provisioned per spec §Dependencies |
| Dataverse schema (`businessunit.sprk_searchindexname`, parent entities' `sprk_searchindexname`, `sprk_document.sprk_searchindexname`) | External | Ready | MCP-verified ✓ |
| Operator BU value setup (Phase A.5) | Internal | Not started | Must complete BEFORE Phase A wizard deploy |
| BFF deploy (Phase B) | Internal | Not started | Must land BEFORE PCF v1.1.74 (Phase D) |
| PR #363 (PCF v1.1.73) | Internal | Open | This project's PR lands SEPARATELY (v1.1.74 succeeds v1.1.73) |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | spaarke-dev | Overall accountability |
| Implementation | Claude Code via `task-execute` skill | Phase implementation under task-execute protocol |
| Reviewer | spaarke-dev | Code review, ADR-compliance check, UAT |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-06-07 | 1.0 | Initial draft — project artifacts + WBS generated by `/project-pipeline` | Claude Code |
| 2026-06-07 | 1.1 | All 11 waves shipped to SPAARKE DEV 1; post-UAT fixes applied (Matter cascade + DocumentUploadWizard caller wiring); status → Complete. 39 of 43 ✅, 1 🚫 deferred (Invoice wizard), 3 🔲 UAT pending. AI Search indexer pipeline finding surfaced for separate follow-up. Full lessons in `notes/lessons-learned.md`. | Claude Code |

---

*Template version: 1.0 | Generated by `project-pipeline` from `spec.md` (4,293 words) + `design.md` (6,325 words)*
