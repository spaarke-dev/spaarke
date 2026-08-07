# ADR-049 R6 Path-B Amendment — summary

> **Task**: 001 (`spaarkeai-compose-r6`) · **Date**: 2026-08-05 · **Path**: B (ADR amendment, per root CLAUDE.md §6.5)
> **Artifact**: the "R6 Path-B Amendment" block in [`.claude/adr/ADR-049-compose-shadow-document.md`](../../../.claude/adr/ADR-049-compose-shadow-document.md)

## What changed

ADR-049 now carries a dated R6 Path-B amendment that **supersedes, for the save path only**:
- **I-4** — "untouched XML subtrees are byte-identical after save"
- **line-40 MUST NOT** — "MUST NOT re-derive the `.docx` from the editor model on save"

with **render-on-save**: every save renders a fresh `.docx` from the canonical document model into a new
immutable SPE version. This eliminates the anchor-reconciliation **422 bug class by construction** — there is
nothing to anchor against on save.

## The four codified points (verbatim from spec ADR-Tensions)

1. Save renders a new immutable version from the canonical model — **no surgical anchoring on the save path**
   (retires the `ComposeBaselineParaIdStamper` count-gate + per-op anchoring from the save path).
2. **Version history is the fidelity safety net** (append-only SPE versions; prior versions retrievable).
3. **Representative-corpus round-trip is a release gate** (CI harness, seeded with the NDA).
4. The surgical `ComposeShadowPatchEngine` is retained **only** for a transitional clean-apply path.

## Scope guard (what the amendment does NOT touch)

- **I-7** (no write-path text-search) — remains in force, satisfied **trivially** (rendering needs no search).
- **R4.5 read/reference invariants F-1…F-5** — **unchanged** (one reader, deterministic numbering,
  `paraId → legal-number` + `CitationResolver`, honest layout numbering). The supersession is save-path only;
  the browse projector's read-side use of byte-identity is not superseded.
- **I-1/I-2** hold in spirit — the server still authors all `.docx` bytes (client never authors); only the
  save-path *authoritative model* shifts from "retained original + ops" to "canonical model rendered to a new version".
- **No auth/security/compliance ADR** and **no unrelated ADR-049 section** modified.

## Why Path B (not A or C)

- **Path A** (per-document surgical tolerance — the abandoned `compose-anchor-robustness-r1` framing) —
  rejected: a per-divergence tolerance patch *is* the treadmill; it cannot close the anchor bug *class*.
- **Path C** (comply — keep surgical byte-patch on save) — rejected: complying re-ships the exact 422
  anchor-reconciliation failure R6 exists to eliminate.

## Traceability

- Implements spec **FR-01** (render-on-save core), **FR-02** (retire count-gate/surgical save path), **FR-08**
  (round-trip fidelity CI harness); enables **FR-07** (version-history open UX = the safety net).
- **Merge obligation (Path B)**: this amendment **MUST merge with or before** the dependent R6 Phase-1 code —
  tasks **010 / 011 / 012** carry `deps` on 001 and a `<gate>` on the merged amendment. Do not merge the
  render-on-save code ahead of the amendment (that would be a silent deviation, forbidden by §6.5).

## Escalation check

The `<escalation>` trigger (superseding any invariant *beyond* the save path, or any auth/security ADR) did
**not** fire — the amendment is confined to the Compose save-path fidelity decision, and the R4.5 read-side
invariants are explicitly preserved.
