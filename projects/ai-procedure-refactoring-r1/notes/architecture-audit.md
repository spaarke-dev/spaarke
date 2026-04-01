# Architecture Docs Audit — docs/architecture/

> **Task**: 010 — Audit all docs/architecture/ files and classify as keep/trim/delete
> **Date**: 2026-03-31
> **Auditor**: AI (task-execute)
> **Output feeds into**: Tasks 011-015 (trimming by domain)

---

## Summary Counts

| Category | Count |
|----------|-------|
| **Keep** (Stable Decision / Reference Data) | 9 |
| **Trim** (Implementation Description — decisions + impl mixed) | 22 |
| **Delete** (Pure Implementation / Deprecated) | 8 |
| **Total** | 39 |

---

## Classification Table

| # | Filename | Lines | Category | Rationale | Trim Task |
|---|----------|-------|----------|-----------|-----------|
| 1 | `AI-ARCHITECTURE.md` | 1176 | **Trim** | Four-tier architecture decisions are valid; large body of component inventory, code paths, and executor internals duplicates the codebase. | Task 011 |
| 2 | `AI-CHAT-STRATEGY-M365-COPILOT-VS-SPRKCHAT.md` | 350 | **Trim** | Two-plane strategy (M365 Copilot vs SprkChat) is a stable architectural decision worth preserving; implementation sections (context payload shapes, session sharing) describe code. | Task 011 |
| 3 | `AZURE-RESOURCE-NAMING-CONVENTION.md` | 367 | **Keep** | Pure stable decision: authoritative naming rules for all Azure/Dataverse resources; MUST follow for all new resources. No implementation details to trim. | Task 015 |
| 4 | `BFF-RENAME-AND-CONFIG-STRATEGY.md` | 313 | **Delete** | Pure implementation record of a completed rename (Spe → Sprk). The rename is done; file lists and namespace updates are historical artifacts with no ongoing architectural value. | — |
| 5 | `INDEX.md` | 75 | **Trim** | Table of contents with broken links (references auth files not in the directory); update to reflect actual files and remove dead links. | Task 015 |
| 6 | `INFRASTRUCTURE-PACKAGING-STRATEGY.md` | 928 | **Trim** | Multi-tenant deployment model decision (Model 1 vs Model 2, Bicep + PP solutions) is a valid stable decision; large "current state" gap analysis and implementation roadmap sections are stale or in-code. | Task 014 |
| 7 | `SIDE-PANE-PLATFORM-ARCHITECTURE.md` | 685 | **Trim** | Configuration-driven SidePaneManager pattern and ribbon trigger approach are architectural decisions; implementation sections with full code samples, BroadcastChannel protocol, and component file listings duplicate code. | Task 013 |
| 8 | `SPAARKE-AI-STRATEGY.md` | 1360 | **Delete** | Explicitly deprecated — superseded by `AI-ARCHITECTURE.md` and `docs/guides/SPAARKE-AI-STRATEGY-AND-ROADMAP.md`. Retained only for historical reference; no AI agent should load this. | — |
| 9 | `Spaarke-Microsoft-IQ-ADOPTION-ANALYSIS.md` | 372 | **Delete** | Explicitly deprecated — superseded by `docs/guides/SPAARKE-AI-STRATEGY-AND-ROADMAP.md`. Historical analysis of Microsoft IQ services; no architectural decisions here not captured in current docs. | — |
| 10 | `SPAARKE-REPOSITORY-ARCHITECTURE.md` | 411 | **Trim** | Repo structure diagram is a useful stable reference; lower sections (component status table, deployment status) become stale and duplicate project tracking. | Task 015 |
| 11 | `VISUALHOST-ARCHITECTURE.md` | 1273 | **Trim** | Configuration-driven visualization framework decision and data source modes are architectural; version-by-version changelog, full component file tree, and extension guide duplicate code/docs. | Task 013 |
| 12 | `ai-document-summary-architecture.md` | 1353 | **Trim** | Header notes it is partially superseded for AI sections; document creation process flows (upload, email, Outlook, Word) are still accurate and worth keeping as decision-level flow descriptions. Superseded AI analysis content should be removed. | Task 011 |
| 13 | `ai-implementation-reference.md` | 3639 | **Delete** | Explicitly "implementation companion" — working code examples, configuration patterns, and service implementations. This is the codebase described in prose; pure implementation content that should live in code comments and the codebase, not a 3600-line doc. | — |
| 14 | `ai-semantic-relationship-graph.md` | 708 | **Trim** | Multi-modal discovery design (structural + semantic graph) and dual-frontend decision are architectural; API endpoint specs, index field schemas, and full React component file listing duplicate code. | Task 011 |
| 15 | `auth-AI-azure-resources.md` | 438 | **Keep** | Pure reference data: resource names, endpoints, model deployments, subscription IDs for AI Azure resources. Essential lookup doc; not duplicated in code. | Task 015 |
| 16 | `auth-azure-resources.md` | 852 | **Keep** | Pure reference data: all Azure AD app registration GUIDs, redirect URIs, API permissions, and resource IDs for SDAP auth. Essential lookup doc; not duplicated in code. | Task 015 |
| 17 | `auth-performance-monitoring.md` | 289 | **Keep** | Stable operational reference: latency baselines (cold vs. warm per leg), token cache TTL decision (55-min), and monitoring thresholds. Decisions and reference data in a concise, usable format. | Task 015 |
| 18 | `auth-security-boundaries.md` | 226 | **Keep** | Stable architectural decision: seven trust boundary definitions (Browser↔Dataverse, PCF↔BFF, BFF↔AD, etc.) and per-boundary validation requirements. Pure decision content, no implementation bloat. | Task 015 |
| 19 | `code-page-runtime-config-proposal.md` | 126 | **Delete** | Marked "Proposal" — describes a problem (build-time config baked in) and proposes a solution. If adopted, the decision belongs in an ADR; if not, the doc is moot. Too short to trim; the proposal state makes it inappropriate as a reference doc. | — |
| 20 | `communication-service-architecture.md` | 984 | **Trim** | Key design decisions (Graph API over Dataverse email, subscriptions over SSS, dual send modes, per-endpoint auth filter) are architectural; API endpoint specs, Dataverse entity schema, DI registration code, and ADR compliance table duplicate code. | Task 012 |
| 21 | `email-to-document-architecture.md` | 485 | **Trim** | Hybrid trigger design (webhook + polling backup), idempotency via Redis locks, and AI handoff pattern are architectural decisions worth keeping; process flow detail (service names, method calls) duplicates code. | Task 012 |
| 22 | `email-to-document-automation.md` | 1054 | **Trim** | Overlaps substantially with `email-to-document-architecture.md` — both cover the same feature. More detailed on component reference, configuration, and troubleshooting. After trimming both, consider merging into one canonical doc. | Task 012 |
| 23 | `event-to-do-architecture.md` | 640 | **Trim** | R1→R2 architecture evolution decision (unified code page vs. two iframes) is a valid architectural decision; component file listings, Dataverse schema, and TodoContext implementation details duplicate code. | Task 013 |
| 24 | `external-access-spa-architecture.md` | 646 | **Trim** | External B2B guest auth model (Entra B2B, MSAL in SPA, no direct Dataverse from browser) is a stable architectural decision; endpoint path inventory and service file listing duplicate code. | Task 012 |
| 25 | `finance-intelligence-architecture.md` | 1430 | **Trim** | Architectural highlights (hybrid VisualHost, structured output pattern, idempotency via alternate keys, VisibilityState determinism) are sound decisions; data model field listings, method signature tables, and job pipeline flowcharts duplicate code. | Task 012 |
| 26 | `multi-environment-portability-strategy.md` | 416 | **Trim** | Layered portability strategy (alternate keys, option sets, env vars, Dataverse env vars) is a stable architectural decision; current-state table of hardcoded GUIDs is a point-in-time gap list that belongs in task tracking, not architecture docs. | Task 014 |
| 27 | `office-outlook-teams-integration-architecture.md` | 1253 | **Trim** | Office add-in capability design (save email artifacts, AI metadata extraction, unified UI) is architectural; component file tree, service class method listings, and full sequence diagrams duplicate code and tutorials. | Task 013 |
| 28 | `playbook-architecture.md` | 651 | **Trim** | Three-level node type system, execution engine design, and canvas data model are genuine architectural decisions; node executor method signatures and internal state machine details duplicate code. | Task 011 |
| 29 | `sdap-auth-patterns.md` | 1324 | **Trim** | Nine authentication pattern taxonomy is a stable architectural reference; each pattern's "when to use" guidance is valuable. Full token flow diagrams with exact GUIDs and Azure AD endpoint URLs are operational detail duplicated in `auth-azure-resources.md`. | Task 012 |
| 30 | `sdap-bff-api-patterns.md` | 1494 | **Trim** | SPE single-container-per-environment decision (ADR-005 enforcement) and Redis caching TTL decisions are architectural; upload session chunking algorithms, Graph API call sequences, and method-level service descriptions duplicate code. | Task 012 |
| 31 | `sdap-component-interactions.md` | 1038 | **Trim** | Cross-component impact table is a useful architectural reference for change management; the full component interaction diagrams with service method names and file paths duplicate code structure. | Task 012 |
| 32 | `sdap-document-processing-architecture.md` | 765 | **Keep** | Supersedes three older docs per its own header; clear route comparison matrix (file upload vs email vs add-in), dual pipeline principle, and auth mode per route are stable architectural decisions. Well-structured, not bloated. | Task 015 |
| 33 | `sdap-overview.md` | 583 | **Keep** | Structural monolith decision, seven functional domain boundary definition, and component model are stable architectural decisions at the right level of abstraction. Good TL;DR for AI agents loading context. | Task 015 |
| 34 | `sdap-pcf-patterns.md` | 311 | **Trim** | Migration decision (Custom Page + PCF wrapper → DocumentUploadWizard Code Page, per ADR-006) and EntityDocumentConfig pattern are architectural; component file tree listing and PCF configuration tables duplicate code. | Task 013 |
| 35 | `sdap-troubleshooting.md` | 922 | **Delete** | Operational runbook (14 known issues, symptoms, root causes, Azure Portal steps). This is ops/SRE content, not architecture. Value belongs in a runbook or wiki, not the architecture docs directory. | — |
| 36 | `sdap-workspace-integration-patterns.md` | 396 | **Trim** | Entity-agnostic creation service pattern, document operations endpoint design, and app-only Service Bus analysis pattern are architectural decisions extending the job contract; EntityCreationService method table and nav-prop discovery logic duplicate code. | Task 012 |
| 37 | `uac-access-control.md` | 616 | **Trim** | Three-plane access control model (Dataverse records, SPE files, AI Search) and dual caller type design (internal vs. external) are stable architectural decisions; OperationAccessPolicy enum member listing and Redis cache implementation notes duplicate code. | Task 012 |
| 38 | `ui-dialog-shell-architecture.md` | 817 | **Trim** | Three-layer UI model (shared library, code page wrappers, consumers), IDataService abstraction pattern, and shell selection decision tree are architectural; full component file tree, wizard command handler documentation, and ribbon XML snippets duplicate code. | Task 013 |
| 39 | `universal-dataset-grid-architecture.md` | 789 | **Trim** | Shared codebase + OOB appearance parity + configuration-driven views are architectural goals worth preserving; component hierarchy file tree, FetchXML query construction, and column configuration API signatures duplicate code. | Task 013 |

---

## Files by Trim Task

### Task 011 — AI Architecture Docs
- `AI-ARCHITECTURE.md` (1176 lines) — Trim
- `AI-CHAT-STRATEGY-M365-COPILOT-VS-SPRKCHAT.md` (350 lines) — Trim
- `ai-document-summary-architecture.md` (1353 lines) — Trim (partially superseded)
- `ai-semantic-relationship-graph.md` (708 lines) — Trim
- `playbook-architecture.md` (651 lines) — Trim

### Task 012 — BFF/API Architecture Docs
- `communication-service-architecture.md` (984 lines) — Trim
- `email-to-document-architecture.md` (485 lines) — Trim
- `email-to-document-automation.md` (1054 lines) — Trim (overlap with above)
- `external-access-spa-architecture.md` (646 lines) — Trim
- `finance-intelligence-architecture.md` (1430 lines) — Trim
- `sdap-auth-patterns.md` (1324 lines) — Trim
- `sdap-bff-api-patterns.md` (1494 lines) — Trim
- `sdap-component-interactions.md` (1038 lines) — Trim
- `sdap-workspace-integration-patterns.md` (396 lines) — Trim
- `uac-access-control.md` (616 lines) — Trim

### Task 013 — UI/Frontend Architecture Docs
- `SIDE-PANE-PLATFORM-ARCHITECTURE.md` (685 lines) — Trim
- `VISUALHOST-ARCHITECTURE.md` (1273 lines) — Trim
- `event-to-do-architecture.md` (640 lines) — Trim
- `office-outlook-teams-integration-architecture.md` (1253 lines) — Trim
- `sdap-pcf-patterns.md` (311 lines) — Trim
- `ui-dialog-shell-architecture.md` (817 lines) — Trim
- `universal-dataset-grid-architecture.md` (789 lines) — Trim

### Task 014 — Infrastructure Architecture Docs
- `INFRASTRUCTURE-PACKAGING-STRATEGY.md` (928 lines) — Trim
- `multi-environment-portability-strategy.md` (416 lines) — Trim

### Task 015 — Reference/Stable Architecture Docs
- `AZURE-RESOURCE-NAMING-CONVENTION.md` (367 lines) — Keep
- `INDEX.md` (75 lines) — Trim (dead links, update only)
- `SPAARKE-REPOSITORY-ARCHITECTURE.md` (411 lines) — Trim
- `auth-AI-azure-resources.md` (438 lines) — Keep
- `auth-azure-resources.md` (852 lines) — Keep
- `auth-performance-monitoring.md` (289 lines) — Keep
- `auth-security-boundaries.md` (226 lines) — Keep
- `sdap-document-processing-architecture.md` (765 lines) — Keep
- `sdap-overview.md` (583 lines) — Keep

---

## Delete List (Action Required)

These files should be deleted — no trimming needed, no stable decisions to preserve:

| File | Lines | Reason |
|------|-------|--------|
| `BFF-RENAME-AND-CONFIG-STRATEGY.md` | 313 | Completed rename — pure historical implementation record |
| `SPAARKE-AI-STRATEGY.md` | 1360 | Explicitly deprecated, superseded by AI-ARCHITECTURE.md |
| `Spaarke-Microsoft-IQ-ADOPTION-ANALYSIS.md` | 372 | Explicitly deprecated, superseded by strategy guide |
| `ai-implementation-reference.md` | 3639 | Pure implementation companion — code examples, not decisions |
| `code-page-runtime-config-proposal.md` | 126 | Proposal status; if adopted → ADR; if not → irrelevant |
| `sdap-troubleshooting.md` | 922 | Operational runbook — not architecture; belongs in wiki |

**Total lines to delete**: ~6,732 lines

---

## Notes for Trim Tasks

1. **email-to-document duplication**: `email-to-document-architecture.md` and `email-to-document-automation.md` cover the same feature. Task 012 should trim both and consider merging into one canonical doc.

2. **ai-document-summary-architecture.md**: Header explicitly says AI analysis sections are outdated; only document creation process flows remain valid. Task 011 should remove all AI pipeline content and keep only the upload/email/Outlook/Word flow comparison.

3. **sdap-auth-patterns.md**: Contains operational GUIDs that duplicate `auth-azure-resources.md`. Task 012 should replace exact GUIDs with references to the dedicated resource file rather than inline values.

4. **Trim guiding principle**: Preserve "why" (decisions, constraints, rationale) and remove "what exactly" (file names, method signatures, field lists, code samples). Code is the source of truth for implementation.
