# BFF AI Architecture Audit — r1

> **Status**: 🆕 INITIATED (2026-06-04)
> **Triggered by**: r3 design discussion of `ai-spaarke-insights-engine-r3` surfaced multiple parallel intent-classification systems
> **Decision**: Owner chose "Option C — pause r3, do dedicated audit FIRST" rather than try to scope r3 reconciliation work without canonical architecture decisions
> **Primary input**: [`notes/initial-findings.md`](notes/initial-findings.md) — captures discovery before context is lost
> **Estimated effort**: ~2 weeks

---

## 1. Purpose

The Spaarke BFF (`Sprk.Bff.Api`) has accumulated AI infrastructure across 5+ project cycles. Each project added what it needed; no holistic cleanup has happened. The result, documented in `notes/initial-findings.md`:

- **4 parallel intent classification systems** (`CapabilityRouter`, `PlaybookDispatcher`, `IntentClassificationService`, `InsightsIntentClassifier`)
- **4 near-identical lookup services** (`PlaybookLookupService`, `ActionLookupService`, `ToolLookupService`, `SkillLookupService`) — DRY violation
- **4 distinct-but-uncoordinated search services** (`IRagService`, `SemanticSearchService`, `RecordSearchService`, `PlaybookEmbeddingService`)
- **3+ prompt-builder patterns**
- **11+ services rolling their own cache** with different TTL/eviction semantics
- Possibly more (full inventory pending)

r3 cannot responsibly scope reconciliation work without knowing the canonical answer. This audit project produces those answers.

## 2. Deliverables

Per [`notes/initial-findings.md`](notes/initial-findings.md) §8:

1. **Comprehensive inventory** — every AI service + consumers + state (active/deprecated/unused) + originating project
2. **Canonical-architecture decisions** per category (intent classification, lookup services, search services, cache patterns, prompt builders, …)
3. **Migration plan** — per service/category: code paths, tests, deploy implications, coordination needs, effort estimate
4. **r3 + r4 scope guidance** — what's safe to proceed now vs. what waits for audit findings
5. **Process recommendation** — should periodic AI architecture review be institutionalized?

## 3. Status

| Phase | Status |
|---|---|
| Project initialization | ✅ 2026-06-04 |
| Initial findings capture | ✅ [`notes/initial-findings.md`](notes/initial-findings.md) |
| design.md (audit scope + methodology) | 🔄 owner-mediated discussion next |
| spec.md | 🔲 derives from design |
| plan.md (waves + task POMLs) | 🔲 derives from spec |
| Implementation (the audit itself) | 🔲 starts after planning |
| Final decisions + r3 unblock | 🔲 ~2 weeks from initiation |

## 4. Downstream projects waiting on this

| Project | Wave / item | Reason waiting |
|---|---|---|
| `ai-spaarke-insights-engine-r3` | Wave 2 (Tier 2.5 — Insights ↔ CapabilityRouter + PlaybookDispatcher reconciliation) | Cannot scope without canonical architecture decision |
| `spaarke-ai-platform-unification-r5` | None explicitly blocked, but heads-up sent re: chat-agent intent layer parallel-build risk | Audit findings may surface R5-side duplications |

r3 wave 1 (Tier 1 cleanup: `NullInsightsAi`, v1.2 `spe://` href, test-fixture hygiene, telemetry, index rename) is **safe to proceed** independent of audit — those items are infrastructure cleanup that doesn't depend on architecture decisions.

## 5. Key references

| Doc | Purpose |
|---|---|
| [`notes/initial-findings.md`](notes/initial-findings.md) | Captured discovery — primary audit input |
| [`design.md`](design.md) | Audit methodology + scope (skeleton; pending discussion) |
| [`CLAUDE.md`](CLAUDE.md) | Project-scoped Claude context |
| [`current-task.md`](current-task.md) | Active task tracker |
| `projects/ai-spaarke-insights-engine-r3/design.md` | The r3 project waiting on this audit |
| `projects/spaarke-ai-platform-unification-r5/notes/insights-r2-coordination.md` §8.12-§8.16 | R5 heads-up about Insights duplication finding (the trigger) |

## 6. Coordination

- **r3 (Insights Engine Phase 2)** — paused on Wave 2 scope; will resume when audit produces canonical decisions
- **R5 (Spaarke Assistant)** — heads-up sent; should also audit own chat-agent layer
- **SprkChat platform team** — owns `CapabilityRouter` + `PlaybookDispatcher` + related infrastructure; reconciliation decisions need their sign-off
- **Earlier playbook-builder project** — owner-identified as source of some accumulated services; may have ownership / deprecation decisions to make

---

*Created 2026-06-04 by main session at owner direction. Authored after r3 design discussion surfaced the underlying architectural debt pattern.*
