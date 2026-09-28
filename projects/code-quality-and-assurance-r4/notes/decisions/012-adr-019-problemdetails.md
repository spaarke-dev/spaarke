# 🔔 ADR Conflict — Resolution Record: ADR-019

> **Task** 012 (spec FR-08) · **Date** 2026-09-04 · **Format**: CLAUDE.md §6.5

- **ADR in question**: ADR-019 — ProblemDetails & Error Handling
- **Specific rule challenged**: not a rule — the ADR's **`Status: Proposed`**. Its content is *"Use **RFC 7807 ProblemDetails** for all API errors. Include stable error codes and correlation IDs. For SSE, emit terminal error events."*
- **Conflict**: **187 files implement this ADR**, and it is cited 555 times across project tasks — yet it was never ratified. It is routed to a blocking arch test (FR-06), and FR-08 forbids enforcing an unratified rule. So the most widely-implemented error contract in the codebase cannot be enforced, on a technicality nobody ever closed. It has sat this way since ~2025-12.
- **Proposed path**: **C — confirm and comply.** Ratify as written; change `Status: Proposed` → `Accepted`.
- **Rationale**: the decision is not in doubt on any axis that matters. The code already complies at scale (187 files), the ADR accurately describes that code, RFC 7807 is a settled industry standard rather than a contested design choice, and no competing error contract exists in the repo. There is nothing to amend — the text is correct; only its status is wrong. **Path B would be amendment-for-its-own-sake.**
- **Impact of accepting path C**: ADR-019 leaves the FR-08 hold and becomes schedulable for its FR-06 arch test. No code changes — the estate already complies.
- **Alternative considered and rejected**: **Path B (amend).** Genuinely considered because the ADR **names no checkable artifact** (see the accuracy re-verification), so an arch test has nothing to anchor to. Rejected as a *ratification* answer — the naming gap is real but orthogonal, and holding ratification hostage to it keeps 187 files governed by an unratified rule for longer. **Filed separately**: ADR-019 should gain one canonical artifact reference, which is a minor amendment *after* ratification, not a blocker to it.
- **Enforcement status**: ⛔ held until sign-off. On ratification → schedulable.
