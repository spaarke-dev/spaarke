# 042 — Regarding-vs-related intent: owner design decision (2026-07-30)

> **Task**: 042 (P4, FULL, opus/xhigh) — FR-12 regarding-vs-related intent.
> **Status**: design LOCKED by owner in-session 2026-07-30. Supersedes the POML's "documented
> related-to relationship model / second-mechanism" framing and spec §6.5's ADR-024 path-A entry.

---

## The escalation question that was raised (and resolved)

The POML `<escalation>` trigger asked: can "related-to" be represented distinctly WITHOUT a second
regarding mechanism, and is the "new filing based on X" vs "file onto X" boundary separable without
risking silent misfile? Investigation confirmed:

- **ADR-024 is `regarding`-only.** `sprk_communication` has exactly ONE denormalized resolver family
  (`sprk_regardingrecordid/name/number/type/url`) + the 12 typed `sprk_regarding*` lookups. There is
  **no `sprk_relatedrecord*` family** (grep = 0 hits; confirmed against live `spaarkedev1` `describe`).
- So neither ADR-024 nor the schema has a "related-to" concept today.

## Owner decision (the resolution)

The owner collapsed the tension by rejecting the premise that a distinct "related-to" representation is
needed at all:

1. **One direct relationship only: `regarding`.** A "new" matter is not special — once created it is just
   a record. Parent/child/grandchild lineage is derived from the *target records'* own relationships,
   never from a second field on the communication.
2. **Cross-references are NOT structural.** "This email references / is subject-matter-related to a prior
   record" is interesting but is captured in the **LLM triage summary** (`sprk_triagesummary`, existing),
   not as a `related` link/field.
3. **No `related` field, no second mechanism.** The ADR-024 tension is therefore *dissolved*, not resolved
   via a path-A exception — there is nothing new to represent.
4. **Keep the "propose create new record" leg.** When the communication clearly *presents a new record
   while referencing an existing one* — e.g. *"this is a new litigation matter related to matter
   LIT-123456"* — the LLM flags **create new matter** (that is the articulated intent). Human-confirmed,
   gated, nothing auto-finalizes.

## 042 as-built scope (simplified)

| Behavior | Mechanism |
|---|---|
| Detect "new-record" intent (+ referenced record) | Reuse the triage/classification signal (no 2nd full LLM pass) |
| Suppress the misfile onto the referenced record X | Demote X's explicit-ID match from **Resolved/auto-file → Suggested** (additive in `AssociationStatusMapper`, modeled like the existing `FallbackFields` demotion). Safe-by-construction: worst case an email lands in the reviewer pile; never a *new* auto-misfile. |
| Note the cross-reference | Written into `sprk_triagesummary` (existing LLM output field) |
| Offer "create new record" | Gated `create_record` proposal (reuse 040's create-proposal pattern; `sprk_emailreviewlog` Proposed row; human-confirmed; ADR-015 nothing auto-finalizes) |
| "related" field / second regarding mechanism | **NONE** — one relationship (`regarding`) |

## Doc updates this decision requires (owner-requested)

- **ADR-024 amended** (Path B, owner-requested): add a "Single direct relationship — regarding" clarifying
  section stating regarding is the one child→parent link; cross-references are derived from target-record
  relationships or summarized by the LLM, not a second child field; FR-12 introduces no `related` field.
- **spec §6.5 ADR Tensions**: the ADR-024 row is re-cast from "path-A: represent related-to distinctly"
  → "dissolved: no related field; regarding is the one relationship; FR-12 = suppress-misfile + summary
  note + propose create-new".
- **spec FR-12**: reworded to drop "linking the referenced record as related" persistence; keep
  suppress-misfile + note-in-summary + propose-create-new.

## Why this is safe (misfile-critical property)

Suppression is **demote-only**. A false "new-record" classification downgrades an auto-file to a human
review (safe). A missed one is just the pre-existing auto-file behavior (042 never makes misfiling worse).
The three reviewer options (file-onto-X / create-new / dismiss) are surfaced; the create branch
auto-finalizes nothing (ADR-015). The LLM signal need not be perfect because the failure mode is a review,
not a misfile.
