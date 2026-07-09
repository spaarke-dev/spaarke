# HANDOFF → redesign-r2 (core): E-20 timing, E-30 mechanism, ADR-043 review ack, Fork-C re-ping

> **From**: spaarkeai-compose-r2 (Ralph Schroeder) · **To**: spaarke-ai-architecture-redesign-r2 (core) · **Date**: 2026-07-09
> **Re**: reviewed ADR-043 + Phase-E code on master (`bad013d1d`). Four items below; #4 is an unanswered inbound from us.

## 0. ADR-043 review — ACK (closes our O6)
Reviewed ADR-043 (both versions) + E-10 built code. **We concur** — the spine fixes the three gaps our assessment surfaced, and the **operand-home resolution (option b: operand → `## Input` channel, envelope stays context-only)** matches. Our 5 actions already declare their operand via `sprk_inputschema`, so they're consistent with the built `ContextBinder`. Full review: `projects/spaarkeai-compose-r2/notes/ADR-043-impact-review.md`. One nicety: the decision was discoverable (ADR-043 + E-10 design) but never posted back as a closing reply — no action needed, just noting the loop wasn't formally acked to us.

## 1. E-20 timing + admit-gate scope (our #1 blocker)
We verified on master: the disposition **admit-gate** (`SessionDispatchOrchestrator.cs:229`) still hardcodes `Informational | WorkProduct` — `compose` is not admitted, so our compose-disposition dispatch **422s before reaching** the `OutputRouter` compose leg we added. This is the "half-landed promotion" your plan names. **E-10 fixed our input path** (the `HasStructuredOperand` branch — thank you; our 4 informational args-text actions now dispatch end-to-end). **E-20 is the remaining blocker** for `draft-alternative`.
- **Ask**: E-20 landing window? And please confirm `DispositionRoutability` admits `Compose` as both *routable* AND *dispatch-admittable* (the admit-gate must follow the registry, not just the router switch).
- We will **freeze compose edits to `OutputRouter.cs` + `Binding.cs`** until E-20 lands (E-20 deletes our 2 hand-added switch entries into the registry — we don't want a merge collision). Confirm **core owns that collapse** and we contribute nothing further there. The `BindingDisposition.Compose` enum member stays as the registry key.

## 2. E-30 / Move-3 mechanism decision (gates our FR-17 undo, task 034)
Task 034 (undo/replace via supersession) needs the deterministic `ActionKind` + sanctioned supersession-write leg (E-30). ADR-043 §4 records the decision (deterministic kind, not a third spine), but E-30 is 🔲 and the remediation plan lists the mechanism as an open operator decision.
- **Ask**: is the Move-3 mechanism final (deterministic `ActionKind` + supersession-write leg)? We will NOT build 034's write mechanism until this is confirmed — our task carries an escalation trigger for exactly this.

## 3. E-40 seam test vs our flagship gate 082
Our 016/033/042 were verified at the router/unit layer and are **422-broken end-to-end** (the exact "green contract-shape ≠ vertical slice" case E-40 exists to close). We're authoring a consumer-side vertical-slice seam test on your `tests/integration/seam/**` category.
- **Ask**: will E-40 (formal KEEP-category + governance wiring) land before we re-run flagship 082? We'll re-verify 016 through the full `/dispatch` seam once E-20 is in.

## 4. Fork-C profile-analysis facade — RE-PING (unanswered inbound on you)
Our `HANDOFF-to-core-profile-analysis-facade.md` (2026-07-09) asked core to own a thin OBO-safe `Services/Ai/PublicContracts/IDocumentProfileAi` facade (create-on-save profile analysis; app-only `IAppOnlyAnalysisService` trips the ADR-013 facade rule + MI-403s on the OBO-written file). We find **no response and no `IDocumentProfileAi` in the repo or your task index.** Not blocking us — task 013 ships with profile as a `deferred` job step and back-fills when the facade lands — but the R5-E full bar (profile) stays unmet until core picks it up.
- **Ask**: accept + schedule the facade (Phase-E cadence), or tell us to own it with your ack. Either is fine; we just need the loop closed.

*Source of truth: ADR-043-impact-review.md + this handoff. Contact: Ralph Schroeder.*
