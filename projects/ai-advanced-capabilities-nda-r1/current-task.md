# Current Task — `ai-advanced-capabilities-nda-r1`

**Active task**: 001 — ADR-039 advisory-tier amendment (MERGE GATE)
**Task file**: tasks/001-adr-039-advisory-tier-amendment.poml
**Phase**: 0 Governance Gate
**Status**: in-progress
**Started**: 2026-07-25

## Rigor
- **Level**: FULL · **Model tier**: opus @ high (session on Opus 4.8 ✅) · **Step mode**: prescriptive
- **Reason**: ADR amendment (governance), merge gate, high blast radius.
- **Governance posture**: Path-B amendment — draft + adr-check, then PRESENT for human review; do NOT treat as merged autonomously (CLAUDE.md §6/§6.5).

## Knowledge loaded
- `.claude/adr/ADR-039-grounded-execution-closed-catalogs.md` (concise — real filename; POML guessed a short name)
- `docs/adr/ADR-039-grounded-execution-closed-catalogs.md` (full)
- `.claude/adr/INDEX.md` (039 row), `.claude/CHANGELOG.md` (format + convention)

## Amendment approach (decided)
Refine grounded-execution invariant (a) with a declared **output determinism mode** in catalog data:
- `fact` (default, deterministic — extractive/verbatim-cited, unchanged prior behavior)
- `advisory` (probabilistic — reasoning/synthesis depth + Reasoning tier permitted, STILL prompt-controlled + schema-validated + citation-required for factual claims + decline-if-unverifiable + not-authoritative disclaimer + all other ADR-039 invariants hold).
Principle-level (a property of output, in DATA) — names no mechanism. Consistent with "behavior is data" + "risk is catalog-declared data".

## Completed steps
- [x] Step 0.5 rigor declaration
- [x] Step 1 load task POML
- [x] Step 4/5 loaded ADR-039 (both), INDEX, CHANGELOG
- [ ] Step 8.2 amend concise ADR-039
- [ ] Step 8.3 amend full ADR-039
- [ ] Step 8.4 update both ADR INDEX rows + CHANGELOG
- [ ] Step 8.5 adr-check
- [ ] Step 9/9.5 acceptance + gates
- [ ] PRESENT for human review (do not merge autonomously)

## Files modified this session
- (pending)

## Next action
Author the amendment section in `.claude/adr/ADR-039-grounded-execution-closed-catalogs.md`.

## Notes
- Real ADR filename is `ADR-039-grounded-execution-closed-catalogs.md` (POML/PLAN used a shortened guess) — deviation noted; using the real files.
- Advisory NDA output already lives under invariant (a); the amendment sanctions reasoning *depth* within (a), not a new ungrounded lane.
