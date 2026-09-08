# 🔔 ADR Conflict — Resolution Record: ADR-016

> **Task** 012 (spec FR-08) · **Date** 2026-09-04 · **Format**: CLAUDE.md §6.5

- **ADR in question**: ADR-016 — AI Cost, Rate Limits, and Backpressure
- **Specific rule challenged**: the ADR's **`Status: Proposed`**. Content: *"**Layered throttling** to AI operations: per-endpoint rate limiting, bounded concurrency, and explicit budgets. Use async jobs for heavy work."*
- **Conflict**: orphaned `Proposed` — never ratified, no gate, ~2025-12. Routed to arch test + nightly review; held by FR-08. 112 keyword hits, but the terms (`RateLimit`, `Backpressure`, `TokenBudget`) are broad enough that the count is **not** evidence the ADR's specific rules hold.
- **Proposed path**: **C — confirm and comply.** Ratify as written.
- **Rationale**: layered throttling on expensive AI operations is standard practice with no credible alternative on offer, and the ADR names no artifact that has drifted. Nothing identified as wrong ⇒ nothing to amend.
- **Impact of accepting path C**: ADR-016 leaves the FR-08 hold.
- **Alternative considered and rejected**: **Path B (amend).** Rejected — no defective clause was identified.
- ⚠️ **Two honesty notes.**
  1. **Evidence is weak**, as with ADR-020: 112 broad hits, no demonstrated compliance. Weaker than ADR-019's basis.
  2. **This ADR sits close to the security boundary and was deliberately *not* escalated.** It scored **zero** hits on every auth/security/compliance term, and its framing throughout is cost and capacity. But per-endpoint rate limiting is also an **abuse-control** mechanism, and a reviewer who reads it that way would be entitled to require sign-off. The classification here is *cost*, and it is stated openly so it can be overridden rather than discovered later.
- **Enforcement status**: ⛔ held until sign-off.
