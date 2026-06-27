# Epic Descriptions — 12 Initial Portfolio Epics (FR-05)

> Source: spec FR-05 + design.md §4.6 taxonomy strawman. Used by task 005 to create the 12 Epic Issues on Project #2.
> Format matches `epic.yml` template fields: Objectives/Focus, Scope, Success Criteria, Projected Timeframe.

---

## 1. AI Platform & Chat

**Objectives**: Unify Spaarke's AI surfaces into a single platform — chat routing, capability discovery, playbook orchestration, observability. Eliminate parallel chat stacks across SpaarkeAi, SmartTodo, LegalWorkspace.

**Scope**: In: chat routing redesign, capability router, shared agent framework, conversation history persistence, Foundry-grounded agents. Out: customer-facing chatbot.

**Success criteria**: Single ChatEndpoints surface used by all surfaces; capability router handles soft-slash + free-text; conversation context persists across compaction; ≥3 production agents grounded against SPE.

**Projected timeframe**: H2 2026 ongoing.

---

## 2. Insights Engine

**Objectives**: Generate operational insights from Dataverse + SPE data. Daily briefings, due-date awareness, risk flags, summarization at the entity level.

**Scope**: In: daily briefing, due-date worker, summarization pipeline, insights widgets. Out: third-party analytics.

**Success criteria**: Daily briefing reliably runs across customers; due-date alerts surface in app notifications; widget surfaces summarized insights without per-record click.

**Projected timeframe**: H2 2026.

---

## 3. Smart Todo

**Objectives**: First-class `sprk_todo` entity with 11-entity regarding (ADR-024), Code Page, parent-form subgrids, Outlook ribbon integration, BFF Office endpoints, and feature-gated MS To Do sync scaffolding.

**Scope**: In: SmartTodo Code Page, parent-form subgrids, Outlook + LinkedTodosBanner, BFF Office endpoints, MS To Do sync (gated). Out: Teams-native task surface in r1.

**Success criteria**: SmartTodo Code Page handles 11-entity regarding; subgrids work on parent forms; Outlook ribbon creates todos; MS To Do sync gated and tested.

**Projected timeframe**: r3 shipped; subsequent enhancements as needed.

---

## 4. Document Intelligence

**Objectives**: AI-assisted document classification, summarization, similarity, OCR-aware ingestion. Powers entity-aware document operations across legal/matter surfaces.

**Scope**: In: document classification, summarization, FindSimilar, content extraction pipeline. Out: e-signature/redaction.

**Success criteria**: Documents classified within minutes of upload; summarization runs on demand; FindSimilar returns useful results across SPE containers.

**Projected timeframe**: H2 2026 → H1 2027.

---

## 5. BFF & Test Hygiene

**Objectives**: Keep `Sprk.Bff.Api` lean, well-tested, and within publish-size + dependency-vulnerability budgets per CLAUDE.md §10 BFF Hygiene. Strengthen unit test fixtures and integration coverage.

**Scope**: In: publish-size monitoring, CVE remediation, fixture-contract enforcement, test-suite repair projects, ADR-032 Null-Object pattern adoption. Out: BFF rewrite or splitting.

**Success criteria**: BFF publish-size ≤60 MB compressed; zero HIGH CVEs; test pass rate ≥99%; binding rules in CLAUDE.md §10.B/C/D enforced in PR review.

**Projected timeframe**: Ongoing (continuous hygiene).

---

## 6. Auth & SSO

**Objectives**: Maintain Spaarke Auth v2 architecture per ADR-028 — OBO + app-only Graph auth, SSO binding, secure token caches. Streamline new-customer auth setup.

**Scope**: In: OBO flow refinements, SSO binding, Key Vault secret rotation, app registration tooling. Out: B2C/B2B identity federation.

**Success criteria**: All new customers provisioned with auth deployment scripts; zero credentials committed; Auth ADR-028 followed in every BFF-touching project.

**Projected timeframe**: Maintenance + targeted improvements.

---

## 7. Code Quality

**Objectives**: Cross-cutting code quality — ADR enforcement, code-review judgment layer, doc-drift audits, conventions sweep, AI procedure quality.

**Scope**: In: ADR auditing, code-review judgment layer, doc-drift auditing, AI procedure maintenance (ai-procedure-quality projects), conventions sweep. Out: third-party static analysis.

**Success criteria**: ADRs ≤200 LOC each + indexed; doc-drift audits at project boundaries; ai-procedure-quality projects maintained at cadence; ANTI-PATTERNS.md current.

**Projected timeframe**: Continuous.

---

## 8. Procedures & Knowledge

**Objectives**: Maintain Spaarke's AI-coding procedures, knowledge base, and onboarding documentation. Ensure the AI agent has accurate, current operational rules.

**Scope**: In: HOW-TO-INITIATE-NEW-PROJECT.md, AI-CODING-PROCEDURES-GUIDE.md, knowledge/ subdirectory, researcher subagent memory, root CLAUDE.md. Out: customer-facing docs.

**Success criteria**: Procedures docs accurate; knowledge/ accurately reflects current best practices; researcher subagent memory pruned regularly; root CLAUDE.md ≤250 LOC.

**Projected timeframe**: Continuous.

---

## 9. CI/CD & Tooling

**Objectives**: Maintain GitHub Actions workflows + scripts + deployment tooling. Tiered CI model (blocking/advisory/info) with escape hatches. Reliable, debuggable, non-flaky.

**Scope**: In: workflow rationalization, nightly health, build/deploy/test scripts, PowerShell utilities, repo cleanup tooling, this portfolio tracker. Out: external CI providers.

**Success criteria**: CI runtime under target; flaky tests classified + retried; workflows in `.github/workflows/` ≤12 files; tooling discoverable + maintained.

**Projected timeframe**: Continuous.

---

## 10. Insights / Widgets / Search

**Objectives**: Inline + side-by-side MCP widgets that show insights inside the Spaarke chat surface — search results, document lookups, due-date views, related-record explorers.

**Scope**: In: MCP widget framework, AI search widget, document viewer widget, calendar widget, due-date widget, semantic search widget. Out: third-party widget marketplaces.

**Success criteria**: ≥5 production widgets shipping; widget framework reusable across surfaces; semantic search returns relevant results; performance budgets met.

**Projected timeframe**: H2 2026 → H1 2027.

---

## 11. Communications

**Objectives**: Outlook + Teams integration surfaces for legal-ops workflows. Email-to-record, ribbon-driven create, side pane experiences, app-notification routing.

**Scope**: In: Outlook ribbon, Office Add-ins, Teams app patterns, app-notification routing per entity. Out: end-user-customizable workflow builders.

**Success criteria**: Outlook ribbon ships matter/document/todo creates; app notifications work across 11-entity regarding; Office Add-in deployment scripted and reproducible.

**Projected timeframe**: H2 2026 → H1 2027.

---

## 12. Multi-tenant

**Objectives**: Provision, manage, and operate multi-customer Spaarke environments. Tenant onboarding, environment provisioning, deploy-promote pipeline, customer-specific configs.

**Scope**: In: provision-customer workflow, deploy-promote pipeline, environment registry, customer-specific config files, tenant isolation. Out: cross-tenant data sharing.

**Success criteria**: New customer provisioning automated end-to-end; environment registry single source of truth; deploy-promote workflow reliable; no tenant data leakage.

**Projected timeframe**: H2 2026 → H1 2027.
