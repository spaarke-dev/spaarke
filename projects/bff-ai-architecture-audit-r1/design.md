# BFF AI Architecture Audit — Design (Skeleton)

> **Status**: 🆕 SKELETON — methodology + scope pending owner discussion
> **Primary input**: [`notes/initial-findings.md`](notes/initial-findings.md)
> **Created**: 2026-06-04
> **Authored by**: Claude (main session) after Option C decision

---

## 1. Purpose

Produce a comprehensive inventory + canonical-architecture decisions for AI infrastructure in `Sprk.Bff.Api`. Output guides scope of downstream projects (r3, R5, future r4).

## 2. Audit scope (TBD)

Scope to be locked by owner. Suggested anchor: categories surfaced in `notes/initial-findings.md` §2-§6 plus expansions discovered during audit.

### 2.1 In-scope candidates (per initial findings)

- **Category 1**: Intent classification systems (4 found)
- **Category 2**: Lookup services (4 near-identical)
- **Category 3**: Search services (4 distinct)
- **Category 4**: Cache patterns (11+ direct usages)
- **Category 5**: Prompt builders (3+ patterns)
- **Category 6+**: Anything else discovered during audit

### 2.2 Possible scope expansions (owner decides)

- DI registration patterns — are services registered consistently?
- NuGet package audit — duplicate / conflicting versions across projects?
- Configuration / options pattern — consistent `IOptions<T>` usage?
- Telemetry / observability — consistent OTEL spans, metric names?
- ADR alignment — which existing ADRs are actually followed?

### 2.3 Out of scope (likely)

- Code changes / refactoring (audit produces recommendations; downstream projects do the work)
- ADR creation (any new ADRs needed surface as audit output recommendations, not authored by audit)
- Production deployment / migration (planning only)

## 3. Methodology (TBD)

### 3.1 Phase structure (likely; refined per owner discussion)

| Phase | Effort | Output |
|---|---|---|
| Inventory | ~3d | Systematic file-by-file catalog of every AI service; consumer mapping via grep; state classification (active/deprecated/unused) |
| Per-category analysis | ~3d | For each category: canonical candidate, tradeoffs, migration cost |
| Owner review | iterative | Per-category decisions; signed off in `decisions/` |
| Migration planning | ~2d | Work-item sizing per service; cross-team coordination needs |
| Documentation | ~2d | Final report + decision records |

### 3.2 Discovery techniques

- `Grep` for service name across `src/` to find consumers
- `Read` each service to understand contracts + behavior
- `Glob` to enumerate service categories
- Cross-reference against project READMEs to attribute origin
- Cross-reference against R5 coord doc §3.1 to identify documented reuse intent

### 3.3 Owner-review cadence (TBD)

Options:
- A) Per-category as decisions form (~5-6 owner touchpoints)
- B) Single final review with all decisions packaged
- C) Hybrid: review after inventory phase (course-correct), then per-category, then final

## 4. Decisions framework

For each category surfaced in `notes/initial-findings.md`, audit answers:

1. **Canonical service** — which existing service is the "real one"
2. **Deprecation candidates** — which services should be deprecated/deleted
3. **Migration plan** — how downstream projects move to canonical
4. **Effort estimate** — total work to migrate
5. **Cross-team coordination** — which teams need to be involved
6. **Rollback strategy** — what happens if canonical decision turns out wrong

Each decision recorded as a decision record (`DR-###`) in [`decisions/`](decisions/).

## 5. Cross-team coordination protocol (TBD)

Likely teams to engage:

- **SprkChat platform team** — owns `CapabilityRouter`, `PlaybookDispatcher`, `playbook-embeddings` infrastructure. Their sign-off needed if canonical decision changes their consumer code.
- **R5 (Spaarke Assistant)** — primary downstream consumer; already heads-upped re: chat-agent audit. Their input needed re: which capabilities they actually consume.
- **Playbook-builder project owner** — if `IntentClassificationService` + `AiPlaybookBuilderService` are still alive, owner sign-off on canonical decisions.
- **Insights Engine r3 team** (us) — primary downstream consumer; audit findings unblock r3 wave 2 scope.

## 6. Output documents

| Document | Purpose | Status |
|---|---|---|
| `notes/inventory.md` | Every AI service + consumers + state + origin | 🔲 |
| `notes/canonical-architecture-decisions.md` | Final canonical-architecture report (the audit's primary deliverable) | 🔲 |
| `notes/migration-plan.md` | Per-service work-item sizing for downstream projects | 🔲 |
| `decisions/DR-###-*.md` | Per-category decision records | 🔲 |
| `notes/r3-scope-recommendations.md` | Specific guidance for r3 wave 2 | 🔲 |
| `notes/r5-audit-recommendations.md` | Specific guidance for R5 chat-agent audit | 🔲 |
| `notes/process-recommendation.md` | Should periodic AI architecture review be institutionalized? | 🔲 |

## 7. Effort estimate

Per `notes/initial-findings.md` §9:

- Inventory: ~3d
- Canonical-architecture decisions: ~3d
- Migration plan: ~2d
- Documentation + decision records: ~2d
- **Total: ~2 weeks**

After audit completes, r3 wave 1 has been proceeding independently; r3 wave 2 scope locked per audit findings.

## 8. Open questions for owner

- **Q-001**: Audit scope — categories 1-6 from initial findings only, or broader (DI patterns, NuGet, config, telemetry, ADRs)?
- **Q-002**: Owner-review cadence — per-category, single final review, or hybrid?
- **Q-003**: Cross-team coordination — sequential (Insights → SprkChat → R5 → playbook-builder) or parallel?
- **Q-004**: Naming — should the canonical infrastructure get explicit names (e.g., "Spaarke Canonical AI Stack") to reduce future drift?
- **Q-005**: Should audit recommend ADRs for canonical-architecture lock-ins, or just produce decision records?
- **Q-006**: Process — institutionalize periodic AI architecture review? Per-quarter? Per-major-project-cycle?

---

*Skeleton authored 2026-06-04 by main session. Audit methodology + scope solidifies as owner-mediated discussion progresses.*
