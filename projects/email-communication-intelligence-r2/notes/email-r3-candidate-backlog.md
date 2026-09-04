# email-r3 candidate backlog — reconciliation cards + matching signal tuning

> **Created**: 2026-09-04 · owner-directed during r2 UAT of the Association Engine "Related to" cards.
> **Source scenario**: capture `Fw: PAT-942665 Patent Application 19183531 - Elisa Liardo` — a patent-intake
> email ("new patent application related to both PAT-942665 and PAT-942404… associate to either record").
> The engine produced 4 conflicting `sprk_regardingmatter` candidates; the card strip shows only 3.

## The concrete evidence (why these items matter)

The 4 candidates the engine produced for that capture, resolved to names:

| Rank | Matter # | Matter name | Score | Signal | Shown in cards? |
|---|---|---|---|---|---|
| 1 | PAT-897705 | Methods and Systems for Customized Network Patent | 100% | ThreadContinuity (forwarded thread's parent) | ✅ |
| 2 | REAL-2026-123456.02 | **Real Estate Transaction Matter** (noise) | 99.65% | attached invoice PDF + body/thread `REAL-*` ref | ✅ |
| 3 | PAT-942665 | Patent Application 19183531 - Elisa Liardo | 98.95% | ExplicitRef **capped 0.65** + subject name-match 0.97 | ✅ |
| 4 | PAT-942404 | Targeted Protein Degradation Patent Application | 96.5% | ExplicitRef **capped 0.65** + body name-match 0.90 | ❌ **hidden (top-3 only)** |

Takeaways: the cards show **GUIDs**, so a reviewer can't tell "patent matter from the thread" (a good
signal) from "Real Estate matter dragged in by an invoice" (noise); and the **top-3 cap hides a genuinely
relevant matter** (PAT-942404).

## Backlog items (all → email-r3 unless noted)

### R3-CARD-1 — card preview (record type + name + key fields)
Each "Related to" card must show the **record type + matter number + name** (and ideally a 1-line preview),
not a GUID. Owner: "can't determine from the limited information provided what record type or if it's
relevant." The thread-parent match (PAT-897705) is a *good* signal — but only legible if the card names it.

### R3-CARD-2 — "See all" link → all-candidates modal (THE fix for this scenario)
Add a **"See all"** link next to the "Related to" label → a modal listing **all** candidates (not just top-3),
each with name + score + why-matched. Owner (point 4): the real issue is **not ranking** — it's that we cap
the strip to 3 when there are **>3 strong candidates**, so #4 (PAT-942404) vanishes. Surfacing all candidates
fixes it **without touching the engine**. Low risk, high value.

### R3-SIGNAL-1 — "related to X" should rank HIGH (separate suggest-rank from auto-file-safety) · ADR-045/FR-12
Owner (point 5): *"a signal 'related to' … should be a high signal — it's telling what is related to."*
Today `IdentifierReverseLookupRung.ReferencedNotFiledCap` (FR-12) caps a well-formed identifier from **0.90 →
0.65** when `NewRecordIntentDetector` sees "new … related to X" — a **misfile guard** so "new matter related to
LIT-123456" doesn't auto-file onto LIT-123456. That single confidence number does **two jobs at once** —
ranking AND auto-file eligibility — so demoting auto-file also demotes the rank.
**Proposed direction**: split the two — "related to X" stays **auto-file-ineligible** (safe) but ranks **high
as a suggestion** (surfaces prominently). This is an **ADR-045 / FR-12 change** (root CLAUDE.md §6.5) — do NOT
change engine constants silently. **Gate on G4** so the before/after is measured on the golden set, not tuned
by feel.

## Sequencing note
R3-CARD-1 and R3-CARD-2 are UI/config only (no engine change) → shippable independently in r3.
R3-SIGNAL-1 is engine confidence-banding → **build G4 (eval harness) first**, then tune + measure.
