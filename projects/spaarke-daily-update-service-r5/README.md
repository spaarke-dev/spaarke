# Spaarke Daily Update Service R5

> **Status**: Not started — feedback capture phase
>
> **Created**: 2026-07-01
>
> **Predecessor**: [`spaarke-daily-update-service-r4`](../spaarke-daily-update-service-r4/) (complete 2026-06-26)

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

## Next step

Author `design.md` from these notes when formalizing R5. The five documents above are intentionally scoped so each can become a spec Functional Requirement (FR) with minimal restructuring.

## Related projects

- [`spaarke-daily-update-service`](../spaarke-daily-update-service/) — R1/R2 base
- [`spaarke-daily-update-service-r2`](../spaarke-daily-update-service-r2/) — widget framework migration
- [`spaarke-daily-update-service-r3`](../spaarke-daily-update-service-r3/) — read-state + TTL + 3 actions
- [`spaarke-daily-update-service-r4`](../spaarke-daily-update-service-r4/) — hallucination Round 1 (temperature 0 + grounding + `EntityNameValidator`), consumer redesign, 3-action overflow menu, 4 dead-preferences removed
- [`spaarke-ai-platform-unification-r7`](../spaarke-ai-platform-unification-r7/) — R7 Wave 12 widget cutover (source of R5 feedback)
