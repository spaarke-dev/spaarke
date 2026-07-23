# Task 041 — Rule-Store Decision: Grep Evidence + §11 Escalation

> **Date**: 2026-07-22 · **Status**: ✅ RESOLVED — **owner chose Path B (dedicated Dataverse table)**.
> The owner created **`sprk_communicationrule`** live in `spaarkedev1` with the FR-12 columns
> (`sprk_matter` lookup, `sprk_tenant`, `sprk_confidencethreshold` decimal, `sprk_flagprivilege`,
> `sprk_enabled`, `sprk_priority`). Rationale: a dedicated, evolvable table is the right home for the
> anticipated **family of future comms rules** (not a one-off) — this is the §11 "first-of-a-family"
> justification, and the mild ADR-039 second-config-surface tension is accepted with that documented reason.
> Escalation was still correct: modifying the r2-owned routing surface (Path A) or adding schema (Path B)
> is an owner decision, not an agent default.
>
> **ADR-039 exception (accepted)**: comms-RI rules live in `sprk_communicationrule`, a comms-specific
> config surface alongside the platform Binding table. Accepted because (1) the rules are a growing comms
> family the owner wants to own end-to-end, (2) they never route AI dispatch (Binding's job) — they gate
> comms-RI actions, a distinct concern, and (3) the gate reads its own table + owns its own match logic,
> introducing no second *routing* resolver.

## The question (POML step 1/2, §11 gate)

Can the comms policy layer (FR-12) express its **tenant + matter** rule match — and a **per-rule confidence threshold** — using the existing Binding (`sprk_playbookconsumer`) rows + `sprk_matchconditions`, or does it need a new thin comms-rule table?

## Grep evidence (file:line)

| Finding | Evidence | Verdict |
|---|---|---|
| Match predicate is a flat AND-conjunction (string=equality, array=OR-in-list) | `ConsumerRoutingService.TryMatchConditions` — [ConsumerRoutingService.cs:1051-1120](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerRoutingService.cs#L1051) | `{"tenant":"X","matter":"Y"}` **semantically expresses** tenant AND matter ✅ |
| Routing context resolves only TWO keys | `ResolveContextValue` — [:1122-1135](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerRoutingService.cs#L1122): `"mimeType"`→ctx.MimeType, `"documentType"`→ctx.DocumentType, `_ => null` | A `tenant`/`matter` key resolves to **null → no-match (fail-closed)** — Binding match through `ConsumerRoutingService` **cannot see tenant/matter today** ❌ |
| `IRoutingContext` carries only mime/docType | `BuildCacheKey` [:1176-1192](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerRoutingService.cs#L1176) fingerprints `MimeType`+`DocumentType` only | Extending the context = modifying the r2-owned routing surface |
| No confidence field on Binding; predicate is match-only | `Binding.cs` — no confidence/threshold column; `MatchConditionsJson` is equality/in-list, not `>=` | Per-rule confidence **not** expressible in `MatchConditionsJson`; belongs in a dedicated `CommsPolicyOptions` dial (mirrors `EventRulesOptions.ClassifyConfidenceThreshold`) — does NOT force a table |

**Bottom line:** the flat-predicate MECHANISM can express tenant+matter, but the shared `ConsumerRoutingService` (owned by `spaarke-ai-architecture-redesign-r2`) does not expose those dimensions. The confidence dimension is cleanly a config option, not a table driver.

## The fork (owner decision required — POML escalation)

| Path | What it does | Pros | Cons |
|---|---|---|---|
| **A — extend the shared routing surface** | Add `tenant`/`matter` to `IRoutingContext` + `ResolveContextValue` + `BuildCacheKey`; author `comms-ri` Binding rows; resolve via existing `IConsumerRoutingService` | Purest ADR-039 (ONE routing surface); no new table; reuses the resolution/cache/priority algorithm | **Modifies r2-owned `ConsumerRoutingService`** (cross-project hot path, coordination + regression surface for chat/event routing) |
| **C — Binding rows, comms-local evaluator (RECOMMENDED)** | Author `comms-ri` Binding rows (`MatchConditionsJson` = `{"tenant","matter"}`); the comms policy gate QUERIES those rows and evaluates the flat predicate against tenant/matter **itself** (small contained predicate eval); confidence = new `CommsPolicyOptions` dial | No new table (§11 satisfied); **no change to the r2-owned routing surface**; rule store stays Binding (ADR-039 — one config table); comms owns its own match context | A small, contained re-implementation of the flat-map predicate eval (a mini of `TryMatchConditions`) — minor §11 duplication, well-bounded |
| **B — new thin comms-rule table** | Create a comms-specific `sprk_commsrirule` table (tenant, matter, confidenceThreshold, privilegeFlag…) | Fully decoupled from Binding/r2; comms owns its schema end-to-end | **New Dataverse table + second config surface** (ADR-039 single-routing-surface tension); heaviest option; the POML reserves this ONLY if Binding provably can't express the match — and it CAN (mechanism-wise) |

## Recommendation

**Path C.** The evidence shows Binding's predicate mechanism *can* express tenant+matter, so a new table (Path B) is not warranted (§11 / ADR-039). Path A is architecturally "purest" but modifies the r2-owned shared `ConsumerRoutingService` — a cross-project hot-path change with real regression surface for chat/event routing, for a comms-only need. Path C keeps the rule store as Binding rows (honoring ADR-039's single config table) while letting the comms policy gate own its own tenant/matter match evaluation — no r2 change, no new table, confidence as a dedicated `CommsPolicyOptions` dial per the POML's own instruction. The only cost is a small, self-contained flat-predicate evaluation inside the comms gate.

**Awaiting owner decision (A / B / C) before implementing task 041.**
