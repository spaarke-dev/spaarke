# Playbook Architecture — REDIRECT

> **Last reviewed**: 2026-06-26
> **Status**: SUPERSEDED. This document has been replaced by three canonical docs as part of the spaarke-daily-update-service-r4 canonical-truth loop.

---

## See instead

| Topic | Canonical doc |
|---|---|
| **Runtime semantics** — dispatch shapes (Path A / A.5 / B / C), mode detection (emergent from `sprk_playbooknode` row presence; NO `sprk_playbookmode` column), action lookup precedence (FK → ConfigJson `__actionType` → NodeType-default), scope-array advisory semantics, empty-payload contract, Legacy-mode log site catalog, the two parallel orchestrators, NodeType (5 values) vs ActionType (31 enum values), G1-G12 pitfalls | [`ai-architecture-playbook-runtime.md`](ai-architecture-playbook-runtime.md) |
| **Config-bag boundary** — where does a new config field live: Action row vs Playbook header columns vs Node row (`sprk_configjson` per-node) vs Playbook-level N:N scopes (4-home decision tree); anti-patterns for `sprk_configjson` overuse; routing-fields-in-configjson tech debt | [`ai-architecture-actions-nodes-scopes.md`](ai-architecture-actions-nodes-scopes.md) |
| **Consumer dispatch** — `sprk_playbookconsumer` entity contract, `IConsumerRoutingService.ResolveAsync` semantics (5-min IMemoryCache TTL), `IInvokePlaybookAi.InvokePlaybookAsync` non-streaming facade, Path A / A.5 / B decision matrix, R4 `/narrate` case study | [`ai-architecture-playbook-consumer-routing.md`](ai-architecture-playbook-consumer-routing.md) |
| **Deploy procedure** — `Deploy-Playbook.ps1` input file format, 12-step deploy sequence, `sprk_isactive` load-bearing rule, actionCode lint, skip-vs-`-Force` behaviour, failure recovery, verification queries, "Playbook has no nodes — using Legacy mode" troubleshooting | [`../guides/ai-guide-playbook-deploy-recipe.md`](../guides/ai-guide-playbook-deploy-recipe.md) |
| **JPS schema** — instruction/input/output/scopes sections, `$ref` / `$choices` resolution, override merge, structured output | [`../guides/JPS-AUTHORING-GUIDE.md`](../guides/JPS-AUTHORING-GUIDE.md) (scoped to schema reference + decision trees after R4 trim) |
| **Maker recipe** — author a `sprk_event` notification playbook in PlaybookBuilder | [`../guides/PLAYBOOK-AUTHOR-GUIDE.md`](../guides/PLAYBOOK-AUTHOR-GUIDE.md) |

---

## Historical content

The historical content of this document (Three-Level Node Type System table, builder subsystem detail, scheduler reference, pitfalls G1-G11 as originally written) is preserved in git history at commit `1e8c95b8e` ("Spaarke Platform Foundations (R3) — Membership Service + Spaarke.Scheduling + Playbook Engine Hardening (#415)"). View via `git show 1e8c95b8e -- docs/architecture/playbook-architecture.md`.

The R4 canonical-truth loop (2026-06-26) found this document had grown to ~685 lines covering three distinct concerns (runtime + config-bag boundary + deploy procedure) that are now better served as separate canonical docs. The split eliminates the duplication with `AI-ARCHITECTURE.md` (former 20% overlap on Tool Handler Framework, Scope Resolution, Known Pitfalls) and the gap on consumer routing (which had no canonical home and lived only in R4 decision notes).
