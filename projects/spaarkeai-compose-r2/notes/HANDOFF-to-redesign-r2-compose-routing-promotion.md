# HANDOFF → spaarke-ai-architecture-redesign-r2: compose-disposition routing promotion applied

**From**: spaarkeai-compose-r2 · **Date**: 2026-07-09 · **Needs**: review of a change in your core routing files

## TL;DR

Your **task 010** published the `ComposeDisposition` v1 **contract** (walking skeleton) and — per its own POML — **deliberately deferred the production routing promotion** to preserve parallel-agent safety:

> *"Production routing promotion (`BindingDisposition.Compose` in Binding.cs + `ToLedgerValue` case + `OutputRouter` routing case) DESCRIBED in the contract doc — not [applied]."*

We then checked every redesign-r2 task POML: **no task owns that promotion.** It fell through the cracks — it's the piece that actually lets a Binding declare `sprk_disposition = compose` and route to a ledger write, and Compose's 042/033/034 are blocked without it.

Rather than sit blocked on unscheduled work, **spaarkeai-compose-r2 applied the promotion** (it's ~3 lines + a test). Flagging it because it touches **your core-owned routing files**.

## What we changed (please review)

| File | Change |
|---|---|
| `Services/Ai/PublicContracts/Binding.cs` | Added `BindingDisposition.Compose = 100000006` (next `sprk_disposition` option-set value after `Notification`). |
| `Services/Ai/OutputRouter.cs` (`ToLedgerValue`) | `BindingDisposition.Compose => ComposeDisposition.DispositionValue` (`"compose"`). |
| `Services/Ai/OutputRouter.cs` (routing switch) | Added a `case BindingDisposition.Compose` — **pass-through like `Informational`**: the compose `SessionOutput` is stored (store-before-render, ADR-040) and the client re-materializes from the ledger. The router never parses the opaque Compose-owned payload. |
| `tests/unit/.../OutputRouterTests.cs` | `RouteAsync_ComposeDisposition_StoresComposeEntryThenReturnsIt` — asserts store + `"compose"` ledger value + opaque payload passthrough. |

Verified: BFF builds clean; 31 router/compose unit tests green. It rides the existing SSE/ledger surface (ADR-039 — no second dispatch protocol); no new rendering path (ADR-040).

## What we need from you

1. **Confirm the pass-through modeling is what you intended** (compose = informational-style store-then-client-render, per your contract doc). If you envisioned the router emitting the `ComposeDispositionFrame` itself (vs. the SSE/streaming layer), say so — we modeled it as store+return, matching `Informational`.
2. **Confirm the option-set value** `100000006` for the `sprk_disposition` global choice (the Dataverse OptionSet needs this member for a deployed Binding to carry it — a deploy-time concern; flagging so it's on your radar for the choice-column seed).
3. **Do NOT re-implement this in a redesign-r2 task** — it's done. If you'd planned to own it, treat this as satisfied (avoid a duplicate/conflicting edit to `Binding.cs` / `OutputRouter.cs`).

Commit ref: filled in at merge (see the compose-r2 branch commit "compose-disposition routing promotion").
