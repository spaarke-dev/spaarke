# Spaarke Daily Update Service R5

> **Portfolio**: [Project #653](https://github.com/spaarke-dev/spaarke/issues/653) · Epic [#421 SPAARKE AI](https://github.com/spaarke-dev/spaarke/issues/421) · Board [Project #2](https://github.com/users/spaarke-dev/projects/2)
>
> **Status**: ✅ **Complete** — 100% · shipped to master (PR #611, `bdf23f11e`) · operator-UAT confirmed 2026-07-10
>
> **Created**: 2026-07-01 · **Spec'd**: 2026-07-08 · **Completed**: 2026-07-10
>
> **Predecessor**: [`spaarke-daily-update-service-r4`](../spaarke-daily-update-service-r4/) (complete 2026-06-26)
>
> **Scope**: (1) accuracy by construction — deterministic item rows + deterministic-fact TL;DR, no groundedness threshold; (2) visual redesign via `/prototype`; (3) hardening sweep (Choice-coercion, collaborator-scope, de-dup, tests, OData doc). **Deferred** (filed as issues at close): Monitored-For schema ([#650](https://github.com/spaarke-dev/spaarke/issues/650)), EventDetailSidePane `@odata.bind` fix ([#651](https://github.com/spaarke-dev/spaarke/issues/651)), Email-briefing typed-note passthrough ([#652](https://github.com/spaarke-dev/spaarke/issues/652)). See [`design.md`](design.md) v0.2 · [`spec.md`](spec.md) · [`plan.md`](plan.md).

## Outcome (2026-07-10)

Shipped and operator-confirmed on spaarkedev1. Delivered across all three scope pillars plus an operator-UAT round:

- **Accuracy by construction** — deterministic item rows (Matters/Documents/Projects/Tasks/To Dos) + deterministic-fact TL;DR. **Membership completeness fix** (removed `distinct='true'` FetchXml bug: 0 → 49 matters live) + contact-link resolution via `systemuser.sprk_primarycontact` (assigned-attorney/paralegal matters now surface).
- **Appearance** — `/prototype`-harnessed redesign ported to `@spaarke/daily-briefing-components`; richer rows (description + "Updated {date}"), `{number}  {name}` titles, Documents + "Matters & Projects" stat tiles, file-preview modal (reused `RichFilePreviewDialog`), email-share (briefing + per-item), Settings windows wired end-to-end.
- **Hardening** — `UpdateRecordNodeExecutor` Choice-coercion (500 fix), collaborator-scope fix, collector de-dup, `QueryHighPriority*` collapse, primary-contact cache, OData convention doc, client-helper + eval-corpus tests.

**Close-out gates**: `/code-review` ship-clean (0 critical); `/test-diet` clean (11 files all MAINTAIN, 0 scaffolding) → [`notes/test-diet-report.md`](notes/test-diet-report.md); lessons in [`notes/lessons-learned.md`](notes/lessons-learned.md); deferrals in [`notes/defer-issues.md`](notes/defer-issues.md).

### Changelog
- **2026-07-10** — Project Complete. Merged PR #611; operator UAT confirmed; wrap-up gates run; 3 items deferred as issues #650/#651/#652.
- **2026-07-09** — Operator-UAT round (email-share, membership completeness, richer rows, tiles, file-preview, titles, header) merged via PR #607 + #611.
- **2026-07-08** — Spec + plan + tasks generated (pipeline); execution began.

## Purpose

R5 collects enhancements and structural fixes for the Daily Briefing feature discovered during R7 Wave 12 operator UAT (2026-06-30 → 2026-07-01). R7 shipped the widget cutover, structured TL;DR + High Priority section, inline references, and prompt tightening. R5 addresses what remained — chiefly LLM hallucination in narrative bullets, the case for deterministic activity notes, and a schema-level replacement for the binary Monitor flag.

## Inbound feedback (source: R7 Wave 12 UAT)

See [`notes/inbound-from-r7/`](notes/inbound-from-r7/) for the full capture:

| # | Document | Summary |
|---|---|---|
| 1 | [LLM hallucinations + determinism](notes/inbound-from-r7/01-llm-hallucinations-and-determinism.md) | Cross-item pairing hallucination evidence + Fully-Deterministic Activity Notes proposal |
| 2 | [Monitored-For schema](notes/inbound-from-r7/02-monitored-for-schema.md) | Choice option set replacing binary `sprk_monitor` with a semantic reason |
| 3 | [R7 code-review follow-ups](notes/inbound-from-r7/03-code-review-followups.md) | Five items from the 2026-06-30 scoped review — inherited by R5 |
| 4 | [Latent bugs](notes/inbound-from-r7/04-latent-bugs.md) | `EventDetailSidePane/TodoSection` mirrors the R7 W12 Add-to-ToDo bug |
| 5 | [Deploy-safety governance](notes/inbound-from-r7/05-deploy-safety-governance.md) | Concurrent-deploy race with `spaarkeai-compose-r1`; sync-master-first rule |

## Deferred follow-ups

Three items were deferred at close and filed as tracked issues (see [`notes/defer-issues.md`](notes/defer-issues.md)):
- **DEF-001** — Monitored-For schema ([#650](https://github.com/spaarke-dev/spaarke/issues/650), next-round)
- **DEF-002** — EventDetailSidePane `@odata.bind` casing fix ([#651](https://github.com/spaarke-dev/spaarke/issues/651), someday)
- **DEF-003** — Email Briefing typed-note passthrough ([#652](https://github.com/spaarke-dev/spaarke/issues/652), next-round)

## Related projects

- [`spaarke-daily-update-service`](../spaarke-daily-update-service/) — R1/R2 base
- [`spaarke-daily-update-service-r2`](../spaarke-daily-update-service-r2/) — widget framework migration
- [`spaarke-daily-update-service-r3`](../spaarke-daily-update-service-r3/) — read-state + TTL + 3 actions
- [`spaarke-daily-update-service-r4`](../spaarke-daily-update-service-r4/) — hallucination Round 1 (temperature 0 + grounding + `EntityNameValidator`), consumer redesign, 3-action overflow menu, 4 dead-preferences removed
- [`spaarke-ai-platform-unification-r7`](../spaarke-ai-platform-unification-r7/) — R7 Wave 12 widget cutover (source of R5 feedback)
