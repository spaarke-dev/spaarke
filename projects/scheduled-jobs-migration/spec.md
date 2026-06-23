# Scheduled Jobs Migration — Spec

> **Status**: PLACEHOLDER — populate via `/design-to-spec projects/scheduled-jobs-migration/design.md`
> **Created**: 2026-06-22
>
> This file will hold the AI-optimized specification (FRs, NFRs, ACs, MUST/MUST NOT rules) generated from `design.md` after owner answers the Open Questions in that doc.

## What goes here (after `/design-to-spec`)

The `/design-to-spec` skill will produce:

- **Functional Requirements (FRs)** — per-job migration FRs + framework integration FRs
- **Non-Functional Requirements (NFRs)** — publish-size, CVE, behavior preservation
- **Acceptance Criteria (ACs)** — per FR
- **MUST / MUST NOT rules** — e.g., MUST preserve cadence; MUST NOT introduce new IOptions blocks
- **Success Criteria** — graduation tests (lifted from `README.md`)
- **Assumptions** — list of owner-confirmed answers to design's Open Questions

## Prerequisite

Before running `/design-to-spec`:

1. Owner answers the **Open Questions for Owner** section in [`design.md`](design.md) (OQ-1 through OQ-8)
2. Phase 0 audit task is filed (if not already in design — currently described conceptually in design § Phasing)

## How to populate this file

```bash
/design-to-spec projects/scheduled-jobs-migration/design.md
```

The skill will OVERWRITE this file with the generated spec. After that, run `/task-create` to populate `tasks/`.

---

*Placeholder generated 2026-06-22 during project scaffolding.*
