# Task 010 (ADR-047 authoring) — deviations

**2026-07-21**

1. **Concise ADR line count = 66, below the acceptance criterion's "100–150 line target."**
   Deliberate judgment call. The concise ADR states all six architectural commitments as explicit MUST/MUST-NOT rules with code-pattern examples + the delivery-mode decision + integration — it is *more* complete than the ADR-043 exemplar the POML names as the structural template (ADR-043 concise = 42 lines). The INDEX header's "100–150 lines" is a loose directory-wide target that almost no existing concise ADR meets (ADR-043 = 42). Padding to 100 lines would inject the verbose context/history the concise format explicitly excludes ("Omitted: verbose context/background, historical discussion"). Chose fidelity to the concise convention over the literal number; the full narrative lives in `docs/adr/` per the pattern. Flagging for reviewer awareness.

2. **Status = Proposed (not Accepted).** Mirrors the ADR-043/039/040/041 convention for in-flight architecture ADRs — promotes to Accepted at the project gate (Layers A–D shipped + seam tests green + a producer delivering end-to-end). Recorded in the header + INDEX row.

3. **`docs/adr/INDEX.md` not updated** — per the ADR-048 changelog note (2026-07-19), `docs/adr/INDEX.md` was already missing the ADR-046 row (an R1 omission) and is not the authoritative index; `.claude/adr/INDEX.md` is. Updated the authoritative one only; did not back-fill the stale `docs/adr/INDEX.md`. Flagging in case a reviewer wants the docs-side index reconciled separately.
