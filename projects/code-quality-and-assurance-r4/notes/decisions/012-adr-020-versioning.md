# 🔔 ADR Conflict — Resolution Record: ADR-020

> **Task** 012 (spec FR-08) · **Date** 2026-09-04 · **Format**: CLAUDE.md §6.5

- **ADR in question**: ADR-020 — Versioning Strategy
- **Specific rule challenged**: the ADR's **`Status: Proposed`**. Content: *"**SemVer for packages**, **tolerant readers** for payloads, **explicit schema versioning** for evolving contracts. No silent breaking changes."*
- **Conflict**: orphaned `Proposed` — never ratified, no promotion gate named, dated ~2025-12. It is routed to a blocking arch test and FR-08 therefore holds it. Evidence that it shipped is **weak**: 14 files match versioning terms, but the match is broad and does not demonstrate the ADR's specific rules are followed.
- **Proposed path**: **C — confirm and comply.** Ratify as written.
- **Rationale**: the three rules are settled engineering practice rather than contested design choices, and the repo shows no competing versioning convention. Nothing in the text is wrong, so there is nothing to amend. Ratifying makes explicit what the codebase already treats as default.
- **Impact of accepting path C**: ADR-020 leaves the FR-08 hold. **No code changes expected** — but see the honesty note below.
- **Alternative considered and rejected**: **Path B (amend).** Rejected because no specific clause was found to be wrong; amending without an identified defect is motion, not improvement.
- ⚠️ **Honesty note — this record is weaker than the ADR-019 one, and should not be read as equivalent.** ADR-019's ratification rests on 187 files demonstrably complying. Here compliance is **assumed from the absence of a competing convention**, not demonstrated. A reviewer may reasonably prefer to verify the SemVer/tolerant-reader rules against real payload handling before ratifying. Recorded so the difference in evidential strength is visible rather than smoothed over.
- **Enforcement status**: ⛔ held until sign-off.
